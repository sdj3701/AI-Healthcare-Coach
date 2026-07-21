import Foundation
import Dispatch
import UIKit
import MediaPipeTasksVision

private final class AHCLegacyPoseWaiter {
    private let lock = NSLock()
    private let semaphore = DispatchSemaphore(value: 0)
    private var completionStatus: Int32?

    func complete(with status: Int32) {
        lock.lock()
        guard completionStatus == nil else {
            lock.unlock()
            return
        }

        completionStatus = status
        lock.unlock()
        semaphore.signal()
    }

    func wait(timeoutSeconds: TimeInterval) -> Int32? {
        if let status = status {
            return status
        }

        guard semaphore.wait(timeout: .now() + timeoutSeconds) == .success else {
            return status
        }

        return status
    }

    var status: Int32? {
        lock.lock()
        defer { lock.unlock() }
        return completionStatus
    }
}

private struct AHCPoseSubmission {
    let token: Int64
    let generation: Int64
    let timestampMs: Int
    let width: Int
    let height: Int
    let rotationAngle: Int
    let mirrored: Bool
    let landmarkerIdentifier: ObjectIdentifier
    let legacyWaiter: AHCLegacyPoseWaiter?
}

private final class AHCMediaPipePoseBridge: NSObject, PoseLandmarkerLiveStreamDelegate {
    static let shared = AHCMediaPipePoseBridge()

    private let stateLock = NSLock()
    private let resultQueue = DispatchQueue(
        label: "com.aihealthcarecoach.mediapipe.pose-result",
        qos: .userInitiated
    )
    private let submissionQueue = DispatchQueue(
        label: "com.aihealthcarecoach.mediapipe.pose-submission",
        qos: .userInitiated
    )
    private let submissionQueueCapacity = DispatchSemaphore(value: 1)
    private let teardownQueue = DispatchQueue(
        label: "com.aihealthcarecoach.mediapipe.pose-teardown",
        qos: .utility
    )

    private var poseLandmarker: PoseLandmarker?
    private var latestJson = "{}"
    private var lastError = ""
    private var resultStatus: Int32 = 0
    private var generation: Int64 = 0
    private var nextSubmissionToken: Int64 = 0
    private var preparingGeneration: Int64?
    private var preparingLegacyWaiter: AHCLegacyPoseWaiter?
    private var inFlightSubmission: AHCPoseSubmission?
    private var cancelRequested = false
    private var lastSubmittedTimestamp = -1

    private let maximumImageDimension = 8_192
    private let maximumFrameByteCount = 256 * 1_024 * 1_024
    private let legacyWaitTimeoutSeconds: TimeInterval = 1.0

    private let landmarkNames = [
        "nose",
        "left_eye_inner",
        "left_eye",
        "left_eye_outer",
        "right_eye_inner",
        "right_eye",
        "right_eye_outer",
        "left_ear",
        "right_ear",
        "mouth_left",
        "mouth_right",
        "left_shoulder",
        "right_shoulder",
        "left_elbow",
        "right_elbow",
        "left_wrist",
        "right_wrist",
        "left_pinky",
        "right_pinky",
        "left_index",
        "right_index",
        "left_thumb",
        "right_thumb",
        "left_hip",
        "right_hip",
        "left_knee",
        "right_knee",
        "left_ankle",
        "right_ankle",
        "left_heel",
        "right_heel",
        "left_foot_index",
        "right_foot_index"
    ]

    private override init() {
        super.init()
    }

    func initialize(
        modelPath rawModelPath: String,
        numPoses: Int,
        minPoseDetectionConfidence: Float,
        minPosePresenceConfidence: Float,
        minTrackingConfidence: Float
    ) -> Int32 {
        stateLock.lock()
        defer { stateLock.unlock() }

        // Warm reuse: keep the live PoseLandmarker, but clear any stuck session
        // slot left after Stop so a later Start can submit again. Preserve
        // lastSubmittedTimestamp so live-stream timestamps stay monotonic.
        if poseLandmarker != nil {
            preparingLegacyWaiter?.complete(with: -16)
            inFlightSubmission?.legacyWaiter?.complete(with: -16)
            advanceGenerationLocked()
            preparingGeneration = nil
            preparingLegacyWaiter = nil
            inFlightSubmission = nil
            cancelRequested = false
            resultStatus = 0
            lastError = ""
            latestJson = "{}"
            return 0
        }

        let modelPath = normalizedPath(rawModelPath)
        guard FileManager.default.fileExists(atPath: modelPath) else {
            publishErrorLocked(
                status: -2,
                code: "MODEL_NOT_FOUND",
                message: "MediaPipe model file was not found: \(modelPath)"
            )
            return -2
        }

        let options = PoseLandmarkerOptions()
        options.runningMode = .liveStream
        options.poseLandmarkerLiveStreamDelegate = self
        options.numPoses = numPoses
        options.minPoseDetectionConfidence = minPoseDetectionConfidence
        options.minPosePresenceConfidence = minPosePresenceConfidence
        options.minTrackingConfidence = minTrackingConfidence
        options.baseOptions.modelAssetPath = modelPath
        options.baseOptions.delegate = .GPU

        do {
            poseLandmarker = try PoseLandmarker(options: options)
            advanceGenerationLocked()
            lastSubmittedTimestamp = -1
            preparingGeneration = nil
            preparingLegacyWaiter = nil
            inFlightSubmission = nil
            cancelRequested = false
            resultStatus = 0
            lastError = ""
            latestJson = "{}"
            return 0
        } catch {
            publishErrorLocked(
                status: -3,
                code: "INITIALIZE_FAILED",
                message: "Failed to initialize PoseLandmarker: \(error.localizedDescription)"
            )
            return -3
        }
    }

    func submitRgba(
        rgbaPointer: UnsafeRawPointer?,
        width: Int,
        height: Int,
        timestampMs: Int,
        rotationAngle: Int,
        mirrored: Bool
    ) -> Int32 {
        return submitRgba(
            rgbaPointer: rgbaPointer,
            width: width,
            height: height,
            timestampMs: timestampMs,
            rotationAngle: rotationAngle,
            mirrored: mirrored,
            legacyWaiter: nil
        )
    }

    func processRgba(
        rgbaPointer: UnsafeRawPointer?,
        width: Int,
        height: Int,
        timestampMs: Int,
        rotationAngle: Int,
        mirrored: Bool
    ) -> Int32 {
        let waiter = AHCLegacyPoseWaiter()
        let submitStatus = submitRgba(
            rgbaPointer: rgbaPointer,
            width: width,
            height: height,
            timestampMs: timestampMs,
            rotationAngle: rotationAngle,
            mirrored: mirrored,
            legacyWaiter: waiter
        )

        guard submitStatus == 0 else {
            return submitStatus
        }

        if let completionStatus = waiter.wait(timeoutSeconds: legacyWaitTimeoutSeconds) {
            return completionStatus
        }

        return timeOutLegacySubmission(waiter: waiter)
    }

    func tryConsumeLatest() -> Int32 {
        stateLock.lock()
        defer { stateLock.unlock() }

        let status = resultStatus
        if status != 0 {
            resultStatus = 0
        }

        return status
    }

    func copyLatestJson(to buffer: UnsafeMutablePointer<CChar>?, capacity: Int) -> Int32 {
        stateLock.lock()
        defer { stateLock.unlock() }
        return copyString(latestJson, to: buffer, capacity: capacity)
    }

    func copyLastError(to buffer: UnsafeMutablePointer<CChar>?, capacity: Int) -> Int32 {
        stateLock.lock()
        defer { stateLock.unlock() }
        return copyString(lastError, to: buffer, capacity: capacity)
    }

    func cancelPending() {
        stateLock.lock()
        let hadPendingWork = preparingGeneration != nil || inFlightSubmission != nil

        // MediaPipe has no live-stream cancel API, so invalidate the generation and
        // clear the logical one-frame slot. Late callbacks are discarded by the
        // finishDetection generation/slot guards. Keep lastSubmittedTimestamp so a
        // warm reused graph still submits monotonic timestamps.
        preparingLegacyWaiter?.complete(with: -16)
        inFlightSubmission?.legacyWaiter?.complete(with: -16)
        if hadPendingWork {
            advanceGenerationLocked()
            preparingGeneration = nil
            preparingLegacyWaiter = nil
            inFlightSubmission = nil
        }
        cancelRequested = false
        publishCancellationLocked()
        stateLock.unlock()
    }

    func dispose() {
        let retiredLandmarker: PoseLandmarker?
        stateLock.lock()
        advanceGenerationLocked()
        preparingLegacyWaiter?.complete(with: -16)
        inFlightSubmission?.legacyWaiter?.complete(with: -16)
        retiredLandmarker = poseLandmarker
        poseLandmarker = nil
        inFlightSubmission = nil
        preparingGeneration = nil
        preparingLegacyWaiter = nil
        cancelRequested = false
        lastSubmittedTimestamp = -1
        resultStatus = 0
        latestJson = "{}"
        lastError = ""
        stateLock.unlock()

        // PoseLandmarker teardown may wait for its delegate/graph worker. Releasing
        // the final bridge-owned strong reference while stateLock is held can then
        // deadlock with a callback trying to acquire the same lock. Keep teardown
        // serialized, but entirely outside the bridge state lock.
        guard let retiredLandmarker = retiredLandmarker else {
            return
        }

        teardownQueue.async { [retiredLandmarker] in
            withExtendedLifetime(retiredLandmarker) {}
        }
    }

    func poseLandmarker(
        _ poseLandmarker: PoseLandmarker,
        didFinishDetection result: PoseLandmarkerResult?,
        timestampInMilliseconds timestampMs: Int,
        error: Error?
    ) {
        // MediaPipe invokes this delegate off the caller thread. A dedicated serial
        // queue keeps JSON creation ordered and prevents it from touching Unity's
        // render/main thread.
        resultQueue.async { [weak self] in
            self?.finishDetection(
                poseLandmarker: poseLandmarker,
                result: result,
                timestampMs: timestampMs,
                error: error
            )
        }
    }

    private func submitRgba(
        rgbaPointer: UnsafeRawPointer?,
        width: Int,
        height: Int,
        timestampMs: Int,
        rotationAngle: Int,
        mirrored: Bool,
        legacyWaiter: AHCLegacyPoseWaiter?
    ) -> Int32 {
        stateLock.lock()

        guard let landmarker = poseLandmarker else {
            publishErrorLocked(
                status: -10,
                code: "NOT_INITIALIZED",
                message: "PoseLandmarker is not initialized."
            )
            stateLock.unlock()
            return -10
        }

        guard let rgbaPointer = rgbaPointer,
              let byteLayout = validatedByteLayout(width: width, height: height) else {
            publishErrorLocked(
                status: -11,
                code: "INVALID_FRAME",
                message: "Input frame is invalid."
            )
            stateLock.unlock()
            return -11
        }

        // One active frame is intentional. MediaPipe live-stream mode may silently
        // drop a second frame while busy, so explicit backpressure is safer.
        guard preparingGeneration == nil, inFlightSubmission == nil else {
            stateLock.unlock()
            return -14
        }

        // dispose()/initialize() can clear lifecycle state while an older queued
        // conversion is still draining. A non-blocking capacity gate keeps the
        // serial queue bounded to one physical preparation task in that case.
        guard submissionQueueCapacity.wait(timeout: DispatchTime.now()) == .success else {
            stateLock.unlock()
            return -14
        }

        let submissionGeneration = generation
        let landmarkerIdentifier = ObjectIdentifier(landmarker)
        preparingGeneration = submissionGeneration
        preparingLegacyWaiter = legacyWaiter
        cancelRequested = false
        resultStatus = 0
        lastError = ""
        stateLock.unlock()

        // Unity only guarantees that its pinned Color32[] is valid until this C
        // call returns. Keep this one native copy synchronous; image allocation,
        // orientation correction, and MediaPipe submission run on a serial queue.
        let rgbaData = Data(bytes: rgbaPointer, count: byteLayout.byteCount)

        submissionQueue.async { [self] in
            defer { submissionQueueCapacity.signal() }
            autoreleasepool {
                prepareAndSubmit(
                    rgbaData: rgbaData,
                    landmarker: landmarker,
                    landmarkerIdentifier: landmarkerIdentifier,
                    submissionGeneration: submissionGeneration,
                    timestampMs: timestampMs,
                    width: width,
                    height: height,
                    rotationAngle: rotationAngle,
                    mirrored: mirrored,
                    legacyWaiter: legacyWaiter
                )
            }
        }

        return 0
    }

    private func prepareAndSubmit(
        rgbaData: Data,
        landmarker: PoseLandmarker,
        landmarkerIdentifier: ObjectIdentifier,
        submissionGeneration: Int64,
        timestampMs: Int,
        width: Int,
        height: Int,
        rotationAngle: Int,
        mirrored: Bool,
        legacyWaiter: AHCLegacyPoseWaiter?
    ) {
        let image = makeImage(
            rgbaData: rgbaData,
            width: width,
            height: height,
            rotationAngle: rotationAngle,
            mirrored: mirrored
        )

        guard let image = image else {
            stateLock.lock()
            let isCurrentPreparation = preparingGeneration == submissionGeneration
            if isCurrentPreparation {
                preparingGeneration = nil
                preparingLegacyWaiter = nil
            }

            let status: Int32
            let isCurrentGraph = generation == submissionGeneration &&
                poseLandmarker.map { ObjectIdentifier($0) == landmarkerIdentifier } == true
            if isCurrentPreparation && cancelRequested && isCurrentGraph {
                status = -16
                cancelRequested = false
                publishCancellationLocked()
            } else if isCurrentGraph {
                status = -12
                publishErrorLocked(
                    status: status,
                    code: "IMAGE_CONVERSION_FAILED",
                    message: "Failed to convert Unity RGBA frame to MPImage."
                )
            } else {
                status = -16
            }

            legacyWaiter?.complete(with: status)
            stateLock.unlock()
            return
        }

        stateLock.lock()
        let isCurrentPreparation = preparingGeneration == submissionGeneration
        let isCurrentGraph = generation == submissionGeneration &&
            poseLandmarker.map { ObjectIdentifier($0) == landmarkerIdentifier } == true
        guard isCurrentPreparation, isCurrentGraph, !cancelRequested else {
            if isCurrentPreparation {
                preparingGeneration = nil
                preparingLegacyWaiter = nil
            }

            let status: Int32
            if isCurrentPreparation && cancelRequested && isCurrentGraph {
                status = -16
                cancelRequested = false
                publishCancellationLocked()
            } else {
                status = -16
            }

            legacyWaiter?.complete(with: status)
            stateLock.unlock()
            return
        }

        guard let monotonicTimestamp = nextTimestampLocked(requested: timestampMs) else {
            preparingGeneration = nil
            preparingLegacyWaiter = nil
            publishErrorLocked(
                status: -13,
                code: "TIMESTAMP_EXHAUSTED",
                message: "Pose frame timestamp exceeded the supported range."
            )
            legacyWaiter?.complete(with: -13)
            stateLock.unlock()
            return
        }

        nextSubmissionToken &+= 1
        let submission = AHCPoseSubmission(
            token: nextSubmissionToken,
            generation: submissionGeneration,
            timestampMs: monotonicTimestamp,
            width: width,
            height: height,
            rotationAngle: rotationAngle,
            mirrored: mirrored,
            landmarkerIdentifier: landmarkerIdentifier,
            legacyWaiter: legacyWaiter
        )

        inFlightSubmission = submission
        preparingGeneration = nil
        preparingLegacyWaiter = nil
        resultStatus = 0
        lastError = ""
        stateLock.unlock()

        do {
            try landmarker.detectAsync(
                image: image,
                timestampInMilliseconds: monotonicTimestamp
            )
        } catch {
            stateLock.lock()
            let isCurrentSubmission =
                inFlightSubmission?.token == submission.token &&
                inFlightSubmission?.generation == submission.generation

            let status: Int32
            if isCurrentSubmission {
                inFlightSubmission = nil
                let isCurrentGraph = generation == submissionGeneration &&
                    poseLandmarker.map { ObjectIdentifier($0) == landmarkerIdentifier } == true
                if cancelRequested && isCurrentGraph {
                    status = -16
                    cancelRequested = false
                    publishCancellationLocked()
                } else if isCurrentGraph {
                    status = -13
                    publishErrorLocked(
                        status: status,
                        code: "PROCESS_FAILED",
                        message: "PoseLandmarker async submission failed: \(error.localizedDescription)"
                    )
                } else {
                    status = -16
                }
            } else {
                status = generation == submissionGeneration ? -13 : -16
            }

            legacyWaiter?.complete(with: status)
            stateLock.unlock()
        }
    }

    private func finishDetection(
        poseLandmarker callbackLandmarker: PoseLandmarker,
        result: PoseLandmarkerResult?,
        timestampMs: Int,
        error: Error?
    ) {
        let callbackIdentifier = ObjectIdentifier(callbackLandmarker)

        stateLock.lock()
        guard let submission = inFlightSubmission,
              submission.landmarkerIdentifier == callbackIdentifier,
              submission.timestampMs == timestampMs else {
            stateLock.unlock()
            return
        }

        guard submission.generation == generation else {
            inFlightSubmission = nil
            submission.legacyWaiter?.complete(with: -16)
            stateLock.unlock()
            return
        }

        if cancelRequested {
            inFlightSubmission = nil
            cancelRequested = false
            publishCancellationLocked()
            submission.legacyWaiter?.complete(with: -16)
            stateLock.unlock()
            return
        }
        stateLock.unlock()

        let completionStatus: Int32
        let generatedJson: String
        let generatedError: String

        if let error = error {
            completionStatus = -13
            generatedError = "PoseLandmarker frame processing failed: \(error.localizedDescription)"
            generatedJson = errorJson(
                code: "PROCESS_FAILED",
                message: generatedError,
                timestampMs: submission.timestampMs,
                width: submission.width,
                height: submission.height,
                rotationAngle: submission.rotationAngle,
                mirrored: submission.mirrored
            )
        } else {
            completionStatus = 0
            generatedError = ""
            generatedJson = resultJson(result: result, submission: submission)
        }

        stateLock.lock()
        guard let currentSubmission = inFlightSubmission,
              currentSubmission.token == submission.token,
              currentSubmission.generation == submission.generation else {
            stateLock.unlock()
            return
        }

        inFlightSubmission = nil
        let isCurrentGraph = generation == submission.generation &&
            poseLandmarker.map { ObjectIdentifier($0) == submission.landmarkerIdentifier } == true
        if cancelRequested && isCurrentGraph {
            cancelRequested = false
            publishCancellationLocked()
            submission.legacyWaiter?.complete(with: -16)
        } else if isCurrentGraph {
            latestJson = generatedJson
            lastError = generatedError
            resultStatus = completionStatus == 0 ? 1 : completionStatus
            submission.legacyWaiter?.complete(with: completionStatus)
        } else {
            submission.legacyWaiter?.complete(with: -16)
        }
        stateLock.unlock()
    }

    private func timeOutLegacySubmission(waiter: AHCLegacyPoseWaiter) -> Int32 {
        if let status = waiter.status {
            return status
        }

        stateLock.lock()
        if let status = waiter.status {
            stateLock.unlock()
            return status
        }

        let isPreparing = preparingLegacyWaiter === waiter
        let isInFlight = inFlightSubmission?.legacyWaiter === waiter
        guard isPreparing || isInFlight else {
            stateLock.unlock()
            return waiter.status ?? -15
        }

        // Keep the lifecycle generation intact so the physical work can drain its
        // slot. Disposal/recovery remain the only operations that replace a graph.
        cancelRequested = true
        publishErrorLocked(
            status: -15,
            code: "PROCESS_TIMEOUT",
            message: "PoseLandmarker did not finish within the legacy wait limit."
        )
        waiter.complete(with: -15)
        stateLock.unlock()
        return -15
    }

    private func makeImage(
        rgbaData: Data,
        width: Int,
        height: Int,
        rotationAngle: Int,
        mirrored: Bool
    ) -> MPImage? {
        guard let layout = validatedByteLayout(width: width, height: height),
              rgbaData.count == layout.byteCount else {
            return nil
        }

        let imageData: Data
        if mirrored {
            // WebCamTexture.videoVerticallyMirrored describes the memory layout,
            // not a display-only UIImage transform. Reverse the RGBA scanlines so
            // MediaPipe receives pixels in the same upright coordinate system.
            guard let flippedData = verticallyFlippedRgbaData(
                rgbaData,
                bytesPerRow: layout.bytesPerRow,
                height: height
            ) else {
                return nil
            }
            imageData = flippedData
        } else {
            imageData = rgbaData
        }

        guard let provider = CGDataProvider(data: imageData as CFData) else {
            return nil
        }

        let colorSpace = CGColorSpaceCreateDeviceRGB()
        let bitmapInfo = CGBitmapInfo(rawValue:
            CGImageAlphaInfo.premultipliedLast.rawValue |
            CGBitmapInfo.byteOrder32Big.rawValue
        )

        guard let cgImage = CGImage(
            width: width,
            height: height,
            bitsPerComponent: 8,
            bitsPerPixel: 32,
            bytesPerRow: layout.bytesPerRow,
            space: colorSpace,
            bitmapInfo: bitmapInfo,
            provider: provider,
            decode: nil,
            shouldInterpolate: false,
            intent: .defaultIntent
        ) else {
            return nil
        }

        let uiImage = UIImage(
            cgImage: cgImage,
            scale: 1.0,
            orientation: imageOrientation(for: rotationAngle)
        )

        return try? MPImage(uiImage: uiImage)
    }

    private func verticallyFlippedRgbaData(
        _ data: Data,
        bytesPerRow: Int,
        height: Int
    ) -> Data? {
        guard bytesPerRow > 0,
              height > 0,
              data.count == bytesPerRow * height else {
            return nil
        }

        var flippedData = Data(count: data.count)
        let copiedAllRows = data.withUnsafeBytes {
            (sourceBuffer: UnsafeRawBufferPointer) -> Bool in
            guard let sourceBaseAddress = sourceBuffer.baseAddress else {
                return false
            }

            return flippedData.withUnsafeMutableBytes {
                (destinationBuffer: UnsafeMutableRawBufferPointer) -> Bool in
                guard let destinationBaseAddress = destinationBuffer.baseAddress else {
                    return false
                }

                for destinationRow in 0..<height {
                    let sourceRow = height - destinationRow - 1
                    destinationBaseAddress
                        .advanced(by: destinationRow * bytesPerRow)
                        .copyMemory(
                            from: sourceBaseAddress.advanced(by: sourceRow * bytesPerRow),
                            byteCount: bytesPerRow
                        )
                }

                return true
            }
        }

        return copiedAllRows ? flippedData : nil
    }

    private func validatedByteLayout(width: Int, height: Int) -> (bytesPerRow: Int, byteCount: Int)? {
        guard width > 0,
              height > 0,
              width <= maximumImageDimension,
              height <= maximumImageDimension else {
            return nil
        }

        let rowResult = width.multipliedReportingOverflow(by: 4)
        guard !rowResult.overflow else {
            return nil
        }

        let countResult = rowResult.partialValue.multipliedReportingOverflow(by: height)
        guard !countResult.overflow,
              countResult.partialValue <= maximumFrameByteCount else {
            return nil
        }

        return (rowResult.partialValue, countResult.partialValue)
    }

    private func nextTimestampLocked(requested: Int) -> Int? {
        let clampedRequest = max(0, requested)

        if lastSubmittedTimestamp < 0 {
            lastSubmittedTimestamp = clampedRequest
            return clampedRequest
        }

        guard lastSubmittedTimestamp < Int.max else {
            return nil
        }

        let timestamp = max(clampedRequest, lastSubmittedTimestamp + 1)
        lastSubmittedTimestamp = timestamp
        return timestamp
    }

    private func imageOrientation(for rotationAngle: Int) -> UIImage.Orientation {
        let normalized = ((rotationAngle % 360) + 360) % 360
        switch normalized {
        case 90:
            return .right
        case 180:
            return .down
        case 270:
            return .left
        default:
            return .up
        }
    }

    private func resultJson(
        result: PoseLandmarkerResult?,
        submission: AHCPoseSubmission
    ) -> String {
        let landmarks = result?.landmarks.first ?? []

        let payload: [String: Any] = [
            "timestampMs": submission.timestampMs,
            "cameraMode": "ios_mediapipe_live_stream",
            "sourceWidth": submission.width,
            "sourceHeight": submission.height,
            "mirrored": submission.mirrored,
            "rotationAngle": submission.rotationAngle,
            "landmarks": normalizedLandmarkPayload(landmarks, rotationAngle: submission.rotationAngle),
            // No runtime consumer uses world landmarks. Omitting their conversion
            // substantially reduces allocations and JSON work on every frame.
            "worldLandmarks": [],
            "errorCode": "",
            "errorMessage": ""
        ]

        return jsonString(payload)
    }

    private func normalizedLandmarkPayload(_ landmarks: [NormalizedLandmark], rotationAngle: Int) -> [[String: Any]] {
        let normalizedLandmarks = landmarks.prefix(landmarkNames.count)
        var payload: [[String: Any]] = []
        payload.reserveCapacity(normalizedLandmarks.count)

        let normalizedAngle = ((rotationAngle % 360) + 360) % 360

        for (index, landmark) in normalizedLandmarks.enumerated() {
            let visibility = landmark.visibility?.floatValue ?? landmark.presence?.floatValue ?? 1.0
            let presence = landmark.presence?.floatValue ?? landmark.visibility?.floatValue ?? 1.0
            
            var rx = landmark.x
            var ry = landmark.y
            
            switch normalizedAngle {
            case 90:
                rx = 1.0 - landmark.y
                ry = landmark.x
            case 180:
                rx = 1.0 - landmark.x
                ry = 1.0 - landmark.y
            case 270:
                rx = landmark.y
                ry = 1.0 - landmark.x
            default:
                break
            }

            payload.append([
                "id": index,
                "name": name(for: index),
                "x": rx,
                "y": ry,
                "z": landmark.z,
                "visibility": visibility,
                "presence": presence
            ])
        }

        return payload
    }

    private func name(for index: Int) -> String {
        guard index >= 0 && index < landmarkNames.count else {
            return "unknown"
        }

        return landmarkNames[index]
    }

    private func errorJson(
        code: String,
        message: String,
        timestampMs: Int = 0,
        width: Int = 0,
        height: Int = 0,
        rotationAngle: Int = 0,
        mirrored: Bool = false
    ) -> String {
        return jsonString([
            "timestampMs": timestampMs,
            "cameraMode": "ios_mediapipe_live_stream",
            "sourceWidth": width,
            "sourceHeight": height,
            "mirrored": mirrored,
            "rotationAngle": rotationAngle,
            "landmarks": [],
            "worldLandmarks": [],
            "errorCode": code,
            "errorMessage": message
        ])
    }

    private func publishErrorLocked(status: Int32, code: String, message: String) {
        lastError = message
        latestJson = errorJson(code: code, message: message)
        resultStatus = status
    }

    private func publishCancellationLocked() {
        publishErrorLocked(
            status: -16,
            code: "PROCESS_CANCELLED",
            message: "Pose inference was cancelled."
        )
    }

    private func advanceGenerationLocked() {
        generation &+= 1
        if generation == 0 {
            generation = 1
        }
    }

    private func jsonString(_ payload: [String: Any]) -> String {
        guard JSONSerialization.isValidJSONObject(payload),
              let data = try? JSONSerialization.data(withJSONObject: payload, options: []) else {
            return "{\"errorCode\":\"JSON_SERIALIZATION_FAILED\",\"errorMessage\":\"Failed to serialize pose result.\"}"
        }

        return String(data: data, encoding: .utf8) ?? "{}"
    }

    private func copyString(_ value: String, to buffer: UnsafeMutablePointer<CChar>?, capacity: Int) -> Int32 {
        let bytes = Array(value.utf8CString)
        let required = bytes.count

        guard let buffer = buffer, capacity > 0 else {
            return Int32(required)
        }

        let copyCount = min(required - 1, capacity - 1)
        bytes.withUnsafeBufferPointer { pointer in
            if let baseAddress = pointer.baseAddress, copyCount > 0 {
                buffer.update(from: baseAddress, count: copyCount)
            }
        }
        buffer[copyCount] = 0

        return Int32(required)
    }

    private func normalizedPath(_ path: String) -> String {
        if path.hasPrefix("file://"), let url = URL(string: path) {
            return url.path
        }

        return path
    }
}

// Version 2 guarantees the non-blocking submit/consume ABI used by the Unity
// runtime. Managed code checks this before creating the graph so an incremental
// build cannot silently fall back to the legacy semaphore-based entry point.
@_cdecl("AHC_PoseGetBridgeVersion")
public func AHC_PoseGetBridgeVersion() -> Int32 {
    return 2
}

@_cdecl("AHC_PoseInitialize")
public func AHC_PoseInitialize(
    _ modelPathPointer: UnsafePointer<CChar>?,
    _ numPoses: Int32,
    _ minPoseDetectionConfidence: Float,
    _ minPosePresenceConfidence: Float,
    _ minTrackingConfidence: Float
) -> Int32 {
    guard let modelPathPointer = modelPathPointer else {
        return -1
    }

    return AHCMediaPipePoseBridge.shared.initialize(
        modelPath: String(cString: modelPathPointer),
        numPoses: Int(numPoses),
        minPoseDetectionConfidence: minPoseDetectionConfidence,
        minPosePresenceConfidence: minPosePresenceConfidence,
        minTrackingConfidence: minTrackingConfidence
    )
}

@_cdecl("AHC_PoseSubmitRgba")
public func AHC_PoseSubmitRgba(
    _ rgbaPointer: UnsafeRawPointer?,
    _ width: Int32,
    _ height: Int32,
    _ timestampMs: Int64,
    _ rotationAngle: Int32,
    _ mirrored: Int32
) -> Int32 {
    return AHCMediaPipePoseBridge.shared.submitRgba(
        rgbaPointer: rgbaPointer,
        width: Int(width),
        height: Int(height),
        timestampMs: Int(clamping: timestampMs),
        rotationAngle: Int(rotationAngle),
        mirrored: mirrored != 0
    )
}

@_cdecl("AHC_PoseTryConsumeLatest")
public func AHC_PoseTryConsumeLatest() -> Int32 {
    return AHCMediaPipePoseBridge.shared.tryConsumeLatest()
}

@_cdecl("AHC_PoseCancelPending")
public func AHC_PoseCancelPending() {
    AHCMediaPipePoseBridge.shared.cancelPending()
}

@_cdecl("AHC_PoseProcessRgba")
public func AHC_PoseProcessRgba(
    _ rgbaPointer: UnsafeRawPointer?,
    _ width: Int32,
    _ height: Int32,
    _ timestampMs: Int64,
    _ rotationAngle: Int32,
    _ mirrored: Int32
) -> Int32 {
    return AHCMediaPipePoseBridge.shared.processRgba(
        rgbaPointer: rgbaPointer,
        width: Int(width),
        height: Int(height),
        timestampMs: Int(clamping: timestampMs),
        rotationAngle: Int(rotationAngle),
        mirrored: mirrored != 0
    )
}

@_cdecl("AHC_PoseGetLatestJson")
public func AHC_PoseGetLatestJson(
    _ buffer: UnsafeMutablePointer<CChar>?,
    _ capacity: Int32
) -> Int32 {
    return AHCMediaPipePoseBridge.shared.copyLatestJson(
        to: buffer,
        capacity: Int(capacity)
    )
}

@_cdecl("AHC_PoseGetLastError")
public func AHC_PoseGetLastError(
    _ buffer: UnsafeMutablePointer<CChar>?,
    _ capacity: Int32
) -> Int32 {
    return AHCMediaPipePoseBridge.shared.copyLastError(
        to: buffer,
        capacity: Int(capacity)
    )
}

@_cdecl("AHC_PoseDispose")
public func AHC_PoseDispose() {
    AHCMediaPipePoseBridge.shared.dispose()
}
