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

        public event Action<Texture> PreviewTextureChanged;

        public bool IsRunning => webCamTexture != null && webCamTexture.isPlaying;
        public bool IsStarting => isStarting;
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
            resumeCameraAfterPause = false;
            StopCameraInternal(false);

            if (frameTexture != null)
            {
                Destroy(frameTexture);
                frameTexture = null;
            }
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                resumeCameraAfterPause = IsRunning || isStarting;
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

        public void ConfigureCapture(int width, int height, int fps)
        {
            requestedWidth = Mathf.Max(16, width);
            requestedHeight = Mathf.Max(16, height);
            requestedFps = Mathf.Max(1, fps);
        }

        private IEnumerator StartCameraRoutine(int startVersion)
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

            var devices = WebCamTexture.devices;
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
                candidateTexture = string.IsNullOrWhiteSpace(activeDeviceName)
                    ? new WebCamTexture(Mathf.Max(16, requestedWidth), Mathf.Max(16, requestedHeight), Mathf.Max(1, requestedFps))
                    : new WebCamTexture(activeDeviceName, Mathf.Max(16, requestedWidth), Mathf.Max(16, requestedHeight), Mathf.Max(1, requestedFps));

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
                if (candidateTexture != null &&
                    candidateTexture.isPlaying &&
                    candidateTexture.didUpdateThisFrame &&
                    candidateTexture.width > 16 &&
                    candidateTexture.height > 16)
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

        public void StopCamera()
        {
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

            foreach (var device in devices)
            {
                if (device.isFrontFacing == preferFrontCamera)
                {
                    return device;
                }
            }

            return devices[0];
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
