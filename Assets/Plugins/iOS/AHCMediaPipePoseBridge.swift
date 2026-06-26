import Foundation
import UIKit
import MediaPipeTasksVision

private final class AHCMediaPipePoseBridge {
    static let shared = AHCMediaPipePoseBridge()

    private var poseLandmarker: PoseLandmarker?
    private var latestJson = "{}"
    private var lastError = ""

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

    func initialize(
        modelPath rawModelPath: String,
        numPoses: Int,
        minPoseDetectionConfidence: Float,
        minPosePresenceConfidence: Float,
        minTrackingConfidence: Float
    ) -> Int32 {
        let modelPath = normalizedPath(rawModelPath)
        guard FileManager.default.fileExists(atPath: modelPath) else {
            lastError = "MediaPipe model file was not found: \(modelPath)"
            latestJson = errorJson(code: "MODEL_NOT_FOUND", message: lastError)
            return -2
        }

        let options = PoseLandmarkerOptions()
        options.runningMode = .video
        options.numPoses = numPoses
        options.minPoseDetectionConfidence = minPoseDetectionConfidence
        options.minPosePresenceConfidence = minPosePresenceConfidence
        options.minTrackingConfidence = minTrackingConfidence
        options.baseOptions.modelAssetPath = modelPath

        do {
            poseLandmarker = try PoseLandmarker(options: options)
            lastError = ""
            latestJson = "{}"
            return 0
        } catch {
            lastError = "Failed to initialize PoseLandmarker: \(error.localizedDescription)"
            latestJson = errorJson(code: "INITIALIZE_FAILED", message: lastError)
            return -3
        }
    }

    func processRgba(
        rgbaPointer: UnsafeRawPointer?,
        width: Int,
        height: Int,
        timestampMs: Int,
        rotationAngle: Int,
        mirrored: Bool
    ) -> Int32 {
        guard let poseLandmarker = poseLandmarker else {
            lastError = "PoseLandmarker is not initialized."
            latestJson = errorJson(code: "NOT_INITIALIZED", message: lastError)
            return -10
        }

        guard let rgbaPointer = rgbaPointer, width > 0, height > 0 else {
            lastError = "Input frame is invalid."
            latestJson = errorJson(code: "INVALID_FRAME", message: lastError)
            return -11
        }

        guard let image = makeImage(
            rgbaPointer: rgbaPointer,
            width: width,
            height: height,
            rotationAngle: rotationAngle
        ) else {
            lastError = "Failed to convert Unity RGBA frame to MPImage."
            latestJson = errorJson(code: "IMAGE_CONVERSION_FAILED", message: lastError)
            return -12
        }

        do {
            let result = try poseLandmarker.detect(
                videoFrame: image,
                timestampInMilliseconds: timestampMs
            )

            latestJson = resultJson(
                result: result,
                timestampMs: timestampMs,
                width: width,
                height: height,
                rotationAngle: rotationAngle,
                mirrored: mirrored
            )
            lastError = ""
            return 0
        } catch {
            lastError = "PoseLandmarker frame processing failed: \(error.localizedDescription)"
            latestJson = errorJson(code: "PROCESS_FAILED", message: lastError)
            return -13
        }
    }

    func copyLatestJson(to buffer: UnsafeMutablePointer<CChar>?, capacity: Int) -> Int32 {
        return copyString(latestJson, to: buffer, capacity: capacity)
    }

    func copyLastError(to buffer: UnsafeMutablePointer<CChar>?, capacity: Int) -> Int32 {
        return copyString(lastError, to: buffer, capacity: capacity)
    }

    func dispose() {
        poseLandmarker = nil
        latestJson = "{}"
        lastError = ""
    }

    private func makeImage(
        rgbaPointer: UnsafeRawPointer,
        width: Int,
        height: Int,
        rotationAngle: Int
    ) -> MPImage? {
        let bytesPerRow = width * 4
        let byteCount = bytesPerRow * height
        let data = Data(bytes: rgbaPointer, count: byteCount)

        guard let provider = CGDataProvider(data: data as CFData) else {
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
            bytesPerRow: bytesPerRow,
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
        result: PoseLandmarkerResult,
        timestampMs: Int,
        width: Int,
        height: Int,
        rotationAngle: Int,
        mirrored: Bool
    ) -> String {
        let landmarks = result.landmarks.first ?? []
        let worldLandmarks = result.worldLandmarks.first ?? []

        let payload: [String: Any] = [
            "timestampMs": timestampMs,
            "cameraMode": "ios_mediapipe_video",
            "sourceWidth": width,
            "sourceHeight": height,
            "mirrored": mirrored,
            "rotationAngle": rotationAngle,
            "landmarks": normalizedLandmarkPayload(landmarks),
            "worldLandmarks": worldLandmarkPayload(worldLandmarks),
            "errorCode": "",
            "errorMessage": ""
        ]

        return jsonString(payload)
    }

    private func normalizedLandmarkPayload(_ landmarks: [NormalizedLandmark]) -> [[String: Any]] {
        var payload: [[String: Any]] = []
        payload.reserveCapacity(landmarks.count)

        for (index, landmark) in landmarks.enumerated() {
            payload.append([
                "id": index,
                "name": name(for: index),
                "x": landmark.x,
                "y": landmark.y,
                "z": landmark.z,
                "visibility": landmark.visibility?.floatValue ?? 0.0,
                "presence": landmark.presence?.floatValue ?? 0.0
            ])
        }

        return payload
    }

    private func worldLandmarkPayload(_ landmarks: [Landmark]) -> [[String: Any]] {
        var payload: [[String: Any]] = []
        payload.reserveCapacity(landmarks.count)

        for (index, landmark) in landmarks.enumerated() {
            payload.append([
                "id": index,
                "name": name(for: index),
                "x": landmark.x,
                "y": landmark.y,
                "z": landmark.z,
                "visibility": landmark.visibility?.floatValue ?? 0.0,
                "presence": landmark.presence?.floatValue ?? 0.0
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

    private func errorJson(code: String, message: String) -> String {
        return jsonString([
            "timestampMs": 0,
            "cameraMode": "ios_mediapipe_video",
            "sourceWidth": 0,
            "sourceHeight": 0,
            "mirrored": false,
            "rotationAngle": 0,
            "landmarks": [],
            "worldLandmarks": [],
            "errorCode": code,
            "errorMessage": message
        ])
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
        timestampMs: Int(timestampMs),
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
