using System;
using System.Collections;
using UnityEngine;

namespace Rag.Healthcare.Camera
{
    public sealed class CameraCaptureSource : MonoBehaviour
    {
        [Header("Camera")]
        [SerializeField] private string cameraDeviceName = string.Empty;
        [SerializeField] private int requestedWidth = 1280;
        [SerializeField] private int requestedHeight = 720;
        [SerializeField] private int requestedFps = 30;
        [SerializeField] private bool preferFrontCamera = true;
        [SerializeField] private bool playOnStart = true;

        [Header("Lifecycle")]
        [SerializeField, Min(0f)] private float restartSettleDelaySeconds = 0.25f;
        [SerializeField, Min(1f)] private float firstFrameTimeoutSeconds = 8f;

        private WebCamTexture webCamTexture;
        private Texture2D frameTexture;
        private string activeDeviceName;
        private Coroutine startCameraCoroutine;
        private bool isStarting;
        private bool activeCameraIsFrontFacing;
        private bool hasReceivedFirstFrame;
        private bool resumeCameraAfterPause;
        private bool isQuitting;
        private int lifecycleVersion;
        private float nextAllowedStartTime;
        private Coroutine switchCameraCoroutine;
        private Action<bool, string> switchCameraCompletion;
        private bool isSwitchingCamera;
        private int switchOperationVersion;
        private bool hasExplicitCameraSelection;
        private bool explicitCameraIsFrontFacing;
        private string explicitCameraDeviceName = string.Empty;

        public event Action<Texture> PreviewTextureChanged;

        public bool IsRunning => webCamTexture != null && webCamTexture.isPlaying;
        public bool IsStarting => isStarting;
        public bool IsSwitchingCamera => isSwitchingCamera;
        public string ActiveDeviceName => activeDeviceName;
        public bool ActiveCameraIsFrontFacing => activeCameraIsFrontFacing;
        public bool PreferFrontCamera => preferFrontCamera;
        public string LastError { get; private set; } = string.Empty;
        public Texture PreviewTexture => webCamTexture;
        public WebCamTexture WebCamTexture => webCamTexture;
        public int FrameWidth => webCamTexture != null ? webCamTexture.width : 0;
        public int FrameHeight => webCamTexture != null ? webCamTexture.height : 0;
        public int VideoRotationAngle => webCamTexture != null
            ? NormalizeRotationAngle(webCamTexture.videoRotationAngle)
            : 0;
        public bool VideoVerticallyMirrored => webCamTexture != null && webCamTexture.videoVerticallyMirrored;
        public bool HasValidFrame => IsRunning && hasReceivedFirstFrame && FrameWidth > 16 && FrameHeight > 16;

        private void Start()
        {
            if (playOnStart)
            {
                StartCamera();
            }
        }

        private void OnDestroy()
        {
            CancelActiveCameraSwitch("Camera source was destroyed.");
            resumeCameraAfterPause = false;
            StopCameraInternal(false);

            if (frameTexture != null)
            {
                Destroy(frameTexture);
                frameTexture = null;
            }
        }

        private void OnDisable()
        {
            CancelActiveCameraSwitch("Camera source was disabled.");
            resumeCameraAfterPause = false;
            StopCameraInternal(false);
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                resumeCameraAfterPause = IsRunning || isStarting;
                CancelActiveCameraSwitch("Camera switch was cancelled while the app paused.");
                if (resumeCameraAfterPause)
                {
                    StopCameraInternal(true);
                }

                return;
            }

            if (!resumeCameraAfterPause || isQuitting)
            {
                return;
            }

            resumeCameraAfterPause = false;
            StartCamera();
        }

        private void OnApplicationQuit()
        {
            CancelActiveCameraSwitch("Camera switch was cancelled while the app was quitting.");
            isQuitting = true;
            resumeCameraAfterPause = false;
        }

        public bool StartCamera()
        {
            if (isQuitting || !isActiveAndEnabled)
            {
                LastError = "Camera source is not active.";
                return false;
            }

            if (isStarting || IsRunning)
            {
                return true;
            }

            if (webCamTexture != null)
            {
                var staleTexture = DetachCurrentCamera();
                ScheduleRestartDelay();
                NotifyPreviewTextureChanged(null);
                ReleaseCameraTexture(staleTexture);
            }

            LastError = string.Empty;
            hasReceivedFirstFrame = false;
            isStarting = true;
            var startVersion = ++lifecycleVersion;

            try
            {
                var coroutine = StartCoroutine(StartCameraRoutine(startVersion));
                startCameraCoroutine = isStarting && startVersion == lifecycleVersion ? coroutine : null;
            }
            catch (Exception exception)
            {
                isStarting = false;
                startCameraCoroutine = null;
                LastError = "Camera failed to start: " + exception.Message;
                Debug.LogWarning("[CameraCaptureSource] " + LastError);
                return false;
            }

            return true;
        }

        public void TogglePreferredCameraFacing()
        {
            preferFrontCamera = !preferFrontCamera;
        }

        public bool IsCameraFacingAvailable(bool frontFacing, out string error)
        {
            error = string.Empty;

            WebCamDevice[] devices;
            try
            {
                devices = WebCamTexture.devices;
            }
            catch (Exception exception)
            {
                error = "Camera devices could not be queried: " + exception.Message;
                return false;
            }

            if (devices == null || devices.Length == 0)
            {
                error = "No camera device was found.";
                return false;
            }

            if (TryResolveCameraForFacing(devices, frontFacing, out _))
            {
                return true;
            }

            error = frontFacing
                ? "A front-facing camera is not available."
                : "A rear-facing camera is not available.";
            return false;
        }

        /// <summary>
        /// Starts the camera when necessary and waits until a valid frame is available,
        /// startup fails, the request is superseded, or the startup timeout expires.
        /// </summary>
        public IEnumerator EnsureCameraReady(Action<bool, string> onCompleted)
        {
            if (HasValidFrame)
            {
                InvokeCameraOperationCompleted(onCompleted, true, string.Empty);
                yield break;
            }

            if (isQuitting || !isActiveAndEnabled)
            {
                const string inactiveError = "Camera source is not active.";
                LastError = inactiveError;
                InvokeCameraOperationCompleted(onCompleted, false, inactiveError);
                yield break;
            }

            if (!isStarting && !IsRunning && !StartCamera())
            {
                var startError = string.IsNullOrWhiteSpace(LastError)
                    ? "Camera could not be started."
                    : LastError;
                InvokeCameraOperationCompleted(onCompleted, false, startError);
                yield break;
            }

            var readyLifecycleVersion = lifecycleVersion;
            while (true)
            {
                if (HasValidFrame)
                {
                    InvokeCameraOperationCompleted(onCompleted, true, string.Empty);
                    yield break;
                }

                if (lifecycleVersion != readyLifecycleVersion)
                {
                    InvokeCameraOperationCompleted(onCompleted, false, "Camera startup was cancelled.");
                    yield break;
                }

                if (!isStarting)
                {
                    var failedError = string.IsNullOrWhiteSpace(LastError)
                        ? "Camera stopped before a valid frame was received."
                        : LastError;
                    InvokeCameraOperationCompleted(onCompleted, false, failedError);
                    yield break;
                }

                yield return null;
            }
        }

        /// <summary>
        /// Owns the complete camera-facing transition. The returned coroutine can be
        /// yielded by callers; completion is reported after the replacement camera has
        /// delivered a valid frame or after recovery has finished.
        /// </summary>
        public Coroutine SwitchCameraFacing(bool targetFront, Action<bool, string> onCompleted)
        {
            if (isQuitting || !isActiveAndEnabled)
            {
                InvokeCameraOperationCompleted(onCompleted, false, "Camera source is not active.");
                return null;
            }

            if (isSwitchingCamera)
            {
                InvokeCameraOperationCompleted(onCompleted, false, "A camera switch is already in progress.");
                return null;
            }

            isSwitchingCamera = true;
            switchCameraCompletion = onCompleted;
            var operationVersion = ++switchOperationVersion;

            try
            {
                var coroutine = StartCoroutine(SwitchCameraFacingRoutine(targetFront, operationVersion, onCompleted));
                switchCameraCoroutine = isSwitchingCamera && operationVersion == switchOperationVersion
                    ? coroutine
                    : null;
                return coroutine;
            }
            catch (Exception exception)
            {
                isSwitchingCamera = false;
                switchCameraCoroutine = null;
                switchCameraCompletion = null;
                ClearExplicitCameraSelection();
                var error = "Camera switch could not be started: " + exception.Message;
                LastError = error;
                Debug.LogWarning("[CameraCaptureSource] " + error);
                InvokeCameraOperationCompleted(onCompleted, false, error);
                return null;
            }
        }

        public void ConfigureCapture(int width, int height, int fps)
        {
            requestedWidth = Mathf.Max(16, width);
            requestedHeight = Mathf.Max(16, height);
            requestedFps = Mathf.Max(1, fps);
        }

        private IEnumerator StartCameraRoutine(int startVersion)
        {
            try
            {
                while (IsCurrentStart(startVersion) && Time.realtimeSinceStartup < nextAllowedStartTime)
                {
                    yield return null;
                }

                if (!IsCurrentStart(startVersion))
                {
                    yield break;
                }

                if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
                {
                    yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);
                }

                if (!IsCurrentStart(startVersion))
                {
                    yield break;
                }

                if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
                {
                    FailStart(startVersion, "Camera permission was denied.", null);
                    yield break;
                }

                WebCamDevice[] devices;
                try
                {
                    devices = WebCamTexture.devices;
                }
                catch (Exception exception)
                {
                    FailStart(startVersion, "Camera devices could not be queried: " + exception.Message, null);
                    yield break;
                }

                if (devices == null || devices.Length == 0)
                {
                    FailStart(startVersion, "No camera device was found.", null);
                    yield break;
                }

                WebCamTexture candidateTexture = null;
                try
                {
                    var selectedDevice = ResolveCameraDevice(devices);
                    activeCameraIsFrontFacing = selectedDevice.isFrontFacing;
                    activeDeviceName = selectedDevice.name;

                    int width = requestedWidth;
                    int height = requestedHeight;
#if UNITY_IOS && !UNITY_EDITOR
                    // iOS 기기 발열 방지 및 관절 인식 반응속도(CPU 복사 & GPU 추론) 극대화를 위해 해상도 제한
                    if (width == 1280 && height == 720)
                    {
                        width = 640;
                        height = 360; // 16:9 비율 유지
                    }
                    else if (width > 640 || height > 480)
                    {
                        width = 640;
                        height = 480; // 4:3 또는 기타 비율 640 상한
                    }
#endif

                    candidateTexture = string.IsNullOrWhiteSpace(activeDeviceName)
                        ? new WebCamTexture(Mathf.Max(16, width), Mathf.Max(16, height), Mathf.Max(1, requestedFps))
                        : new WebCamTexture(activeDeviceName, Mathf.Max(16, width), Mathf.Max(16, height), Mathf.Max(1, requestedFps));

                    if (!IsCurrentStart(startVersion))
                    {
                        ReleaseCameraTexture(candidateTexture);
                        yield break;
                    }

                    webCamTexture = candidateTexture;
                    candidateTexture.Play();
                }
                catch (Exception exception)
                {
                    FailStart(startVersion, "Camera failed to start: " + exception.Message, candidateTexture);
                    yield break;
                }

                var firstFrameDeadline = Time.realtimeSinceStartup + Mathf.Max(1f, firstFrameTimeoutSeconds);
                while (IsCurrentStart(startVersion) && Time.realtimeSinceStartup < firstFrameDeadline)
                {
                    bool hasValidCandidateFrame;
                    try
                    {
                        hasValidCandidateFrame = candidateTexture != null &&
                                                 candidateTexture.isPlaying &&
                                                 candidateTexture.didUpdateThisFrame &&
                                                 candidateTexture.width > 16 &&
                                                 candidateTexture.height > 16;
                    }
                    catch (Exception exception)
                    {
                        FailStart(startVersion, "Camera frame access failed: " + exception.Message, candidateTexture);
                        yield break;
                    }

                    if (hasValidCandidateFrame)
                    {
                        hasReceivedFirstFrame = true;
                        CompleteStart(startVersion);
                        NotifyPreviewTextureChanged(candidateTexture);
                        yield break;
                    }

                    yield return null;
                }

                if (IsCurrentStart(startVersion))
                {
                    FailStart(
                        startVersion,
                        "Camera did not provide a valid frame within " +
                        Mathf.Max(1f, firstFrameTimeoutSeconds).ToString("0.#") + " seconds.",
                        candidateTexture);
                }
            }
            finally
            {
                // Unity exceptions raised after a yield do not return through the
                // StartCamera call-site. Always release the starting flag so a later
                // START or screen re-entry can retry instead of remaining wedged.
                if (IsCurrentStart(startVersion))
                {
                    FailStart(startVersion, "Camera startup ended unexpectedly.", webCamTexture);
                }
            }
        }

        public void StopCamera()
        {
            CancelActiveCameraSwitch("Camera switch was cancelled because the camera stopped.");
            resumeCameraAfterPause = false;
            StopCameraInternal(false);
        }

        private void StopCameraInternal(bool preserveResumeRequest)
        {
            var hadActiveRequest = isStarting || webCamTexture != null;
            lifecycleVersion++;

            if (startCameraCoroutine != null)
            {
                StopCoroutine(startCameraCoroutine);
                startCameraCoroutine = null;
            }

            isStarting = false;

            if (!preserveResumeRequest)
            {
                resumeCameraAfterPause = false;
            }

            var textureToRelease = DetachCurrentCamera();
            if (hadActiveRequest)
            {
                ScheduleRestartDelay();
            }

            if (textureToRelease != null)
            {
                NotifyPreviewTextureChanged(null);
                ReleaseCameraTexture(textureToRelease);
            }
        }

        public bool TryCaptureJpeg(out byte[] jpegBytes, int quality)
        {
            jpegBytes = null;

            if (!HasValidFrame)
            {
                return false;
            }

            EnsureFrameTexture(webCamTexture.width, webCamTexture.height);
            frameTexture.SetPixels32(webCamTexture.GetPixels32());
            frameTexture.Apply(false);

            jpegBytes = frameTexture.EncodeToJPG(Mathf.Clamp(quality, 1, 100));
            return jpegBytes != null && jpegBytes.Length > 0;
        }

        public bool TryGetPixels32(Color32[] buffer, out int width, out int height)
        {
            width = FrameWidth;
            height = FrameHeight;

            if (!HasValidFrame || buffer == null || buffer.Length < width * height)
            {
                return false;
            }

            webCamTexture.GetPixels32(buffer);
            return true;
        }

        private WebCamDevice ResolveCameraDevice(WebCamDevice[] devices)
        {
            // An explicit user-facing switch must win over an Inspector-pinned device.
            // The exact device chosen during preflight is preferred so multi-lens rear
            // cameras do not silently resolve to a different device during restart.
            if (hasExplicitCameraSelection)
            {
                if (!string.IsNullOrWhiteSpace(explicitCameraDeviceName))
                {
                    foreach (var device in devices)
                    {
                        if (device.isFrontFacing == explicitCameraIsFrontFacing &&
                            string.Equals(device.name, explicitCameraDeviceName, StringComparison.OrdinalIgnoreCase))
                        {
                            return device;
                        }
                    }
                }

                if (TryResolveCameraForFacing(devices, explicitCameraIsFrontFacing, out var explicitDevice))
                {
                    return explicitDevice;
                }
            }

            if (!string.IsNullOrWhiteSpace(cameraDeviceName))
            {
                foreach (var device in devices)
                {
                    if (string.Equals(device.name, cameraDeviceName, StringComparison.OrdinalIgnoreCase))
                    {
                        return device;
                    }
                }
            }

            if (TryResolveCameraForFacing(devices, preferFrontCamera, out var preferredDevice))
            {
                return preferredDevice;
            }

            return devices[0];
        }

        private IEnumerator SwitchCameraFacingRoutine(
            bool targetFront,
            int operationVersion,
            Action<bool, string> onCompleted)
        {
            // Make sure SwitchCameraFacing can publish its Coroutine handle before any
            // preflight failure completes this iterator.
            yield return null;

            if (operationVersion != switchOperationVersion || !isActiveAndEnabled)
            {
                yield break;
            }

            var originalPreference = preferFrontCamera;
            var originalFacing = HasValidFrame ? activeCameraIsFrontFacing : preferFrontCamera;
            var originalDeviceName = activeDeviceName;
            var restorePreviousCamera = IsRunning || isStarting;
            var completionSent = false;

            try
            {
                if (!TryGetCameraForFacing(targetFront, out var targetDevice, out var preflightError))
                {
                    completionSent = true;
                    CompleteCameraSwitchState(operationVersion);
                    InvokeCameraOperationCompleted(onCompleted, false, preflightError);
                    yield break;
                }

                if (HasValidFrame && activeCameraIsFrontFacing == targetFront)
                {
                    preferFrontCamera = targetFront;
                    completionSent = true;
                    CompleteCameraSwitchState(operationVersion);
                    InvokeCameraOperationCompleted(onCompleted, true, string.Empty);
                    yield break;
                }

                SetExplicitCameraSelection(targetDevice.name, targetFront);
                StopCameraInternal(false);

                bool targetReady = false;
                string targetError = string.Empty;
                yield return EnsureCameraReady((success, error) =>
                {
                    targetReady = success;
                    targetError = error;
                });

                if (operationVersion != switchOperationVersion)
                {
                    yield break;
                }

                if (targetReady && HasValidFrame && activeCameraIsFrontFacing == targetFront)
                {
                    preferFrontCamera = targetFront;
                    completionSent = true;
                    CompleteCameraSwitchState(operationVersion);
                    InvokeCameraOperationCompleted(onCompleted, true, string.Empty);
                    yield break;
                }

                if (targetReady)
                {
                    targetError = targetFront
                        ? "The camera started, but it did not activate the requested front-facing device."
                        : "The camera started, but it did not activate the requested rear-facing device.";
                }
                else if (string.IsNullOrWhiteSpace(targetError))
                {
                    targetError = "The requested camera failed to start.";
                }

                preferFrontCamera = originalPreference;
                ClearExplicitCameraSelection();

                if (!restorePreviousCamera)
                {
                    StopCameraInternal(false);
                    completionSent = true;
                    CompleteCameraSwitchState(operationVersion);
                    InvokeCameraOperationCompleted(onCompleted, false, targetError);
                    yield break;
                }

                StopCameraInternal(false);
                SetExplicitCameraSelection(originalDeviceName, originalFacing);

                bool recoveryReady = false;
                string recoveryError = string.Empty;
                yield return EnsureCameraReady((success, error) =>
                {
                    recoveryReady = success;
                    recoveryError = error;
                });

                var recoveredOriginalFacing = recoveryReady &&
                                             HasValidFrame &&
                                             activeCameraIsFrontFacing == originalFacing;
                var finalError = recoveredOriginalFacing
                    ? targetError + " The previous camera was restored."
                    : targetError + " Previous camera recovery also failed: " +
                      (string.IsNullOrWhiteSpace(recoveryError) ? "unknown error" : recoveryError);

                completionSent = true;
                CompleteCameraSwitchState(operationVersion);
                InvokeCameraOperationCompleted(onCompleted, false, finalError);
            }
            finally
            {
                if (operationVersion == switchOperationVersion)
                {
                    if (!completionSent)
                    {
                        CompleteCameraSwitchState(operationVersion);
                        InvokeCameraOperationCompleted(onCompleted, false, "Camera switch did not complete.");
                    }
                    else
                    {
                        CompleteCameraSwitchState(operationVersion);
                    }
                }
            }
        }

        private void CompleteCameraSwitchState(int operationVersion)
        {
            if (operationVersion != switchOperationVersion)
            {
                return;
            }

            ClearExplicitCameraSelection();
            isSwitchingCamera = false;
            switchCameraCoroutine = null;
            switchCameraCompletion = null;
        }

        private void CancelActiveCameraSwitch(string error)
        {
            switchOperationVersion++;
            if (!isSwitchingCamera && switchCameraCoroutine == null)
            {
                ClearExplicitCameraSelection();
                return;
            }

            var coroutine = switchCameraCoroutine;
            var completion = switchCameraCompletion;
            switchCameraCoroutine = null;
            switchCameraCompletion = null;
            isSwitchingCamera = false;
            ClearExplicitCameraSelection();

            if (coroutine != null)
            {
                StopCoroutine(coroutine);
            }

            InvokeCameraOperationCompleted(completion, false, error);
        }

        private bool TryGetCameraForFacing(bool frontFacing, out WebCamDevice device, out string error)
        {
            device = default;
            error = string.Empty;

            WebCamDevice[] devices;
            try
            {
                devices = WebCamTexture.devices;
            }
            catch (Exception exception)
            {
                error = "Camera devices could not be queried: " + exception.Message;
                return false;
            }

            if (devices == null || devices.Length == 0)
            {
                error = "No camera device was found.";
                return false;
            }

            if (TryResolveCameraForFacing(devices, frontFacing, out device))
            {
                return true;
            }

            error = frontFacing
                ? "A front-facing camera is not available."
                : "A rear-facing camera is not available.";
            return false;
        }

        private static bool TryResolveCameraForFacing(
            WebCamDevice[] devices,
            bool frontFacing,
            out WebCamDevice selectedDevice)
        {
            if (devices != null)
            {
                foreach (var device in devices)
                {
                    if (device.isFrontFacing == frontFacing)
                    {
                        selectedDevice = device;
                        return true;
                    }
                }
            }

            selectedDevice = default;
            return false;
        }

        private void SetExplicitCameraSelection(string deviceName, bool frontFacing)
        {
            hasExplicitCameraSelection = true;
            explicitCameraIsFrontFacing = frontFacing;
            explicitCameraDeviceName = deviceName ?? string.Empty;
        }

        private void ClearExplicitCameraSelection()
        {
            hasExplicitCameraSelection = false;
            explicitCameraIsFrontFacing = false;
            explicitCameraDeviceName = string.Empty;
        }

        private void InvokeCameraOperationCompleted(
            Action<bool, string> onCompleted,
            bool success,
            string error)
        {
            try
            {
                onCompleted?.Invoke(success, error ?? string.Empty);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }

        private bool IsCurrentStart(int startVersion)
        {
            return isStarting && lifecycleVersion == startVersion;
        }

        private void CompleteStart(int startVersion)
        {
            if (lifecycleVersion != startVersion)
            {
                return;
            }

            isStarting = false;
            startCameraCoroutine = null;
        }

        private void FailStart(int startVersion, string error, WebCamTexture candidateTexture)
        {
            if (lifecycleVersion != startVersion)
            {
                if (candidateTexture != null && candidateTexture != webCamTexture)
                {
                    ReleaseCameraTexture(candidateTexture);
                }

                return;
            }

            LastError = error;
            Debug.LogWarning("[CameraCaptureSource] " + LastError);

            var textureToRelease = webCamTexture == candidateTexture
                ? DetachCurrentCamera()
                : candidateTexture;

            isStarting = false;
            startCameraCoroutine = null;
            ScheduleRestartDelay();

            if (textureToRelease != null)
            {
                NotifyPreviewTextureChanged(null);
                ReleaseCameraTexture(textureToRelease);
            }
        }

        private WebCamTexture DetachCurrentCamera()
        {
            var detachedTexture = webCamTexture;
            webCamTexture = null;
            hasReceivedFirstFrame = false;
            activeDeviceName = string.Empty;
            activeCameraIsFrontFacing = false;
            return detachedTexture;
        }

        private void ReleaseCameraTexture(WebCamTexture texture)
        {
            if (texture == null)
            {
                return;
            }

            try
            {
                if (texture.isPlaying)
                {
                    texture.Stop();
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[CameraCaptureSource] Camera stop failed: " + exception.Message);
            }

            Destroy(texture);
        }

        private void ScheduleRestartDelay()
        {
            nextAllowedStartTime = Mathf.Max(
                nextAllowedStartTime,
                Time.realtimeSinceStartup + Mathf.Max(0f, restartSettleDelaySeconds));
        }

        private void NotifyPreviewTextureChanged(Texture texture)
        {
            try
            {
                PreviewTextureChanged?.Invoke(texture);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }

        private static int NormalizeRotationAngle(int angle)
        {
            return ((angle % 360) + 360) % 360;
        }

        private void EnsureFrameTexture(int width, int height)
        {
            if (frameTexture != null && frameTexture.width == width && frameTexture.height == height)
            {
                return;
            }

            if (frameTexture != null)
            {
                Destroy(frameTexture);
            }

            frameTexture = new Texture2D(width, height, TextureFormat.RGB24, false);
        }
    }
}
