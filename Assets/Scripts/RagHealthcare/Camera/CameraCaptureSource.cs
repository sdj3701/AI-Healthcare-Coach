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

        private WebCamTexture webCamTexture;
        private Texture2D frameTexture;
        private string activeDeviceName;
        private Coroutine startCameraCoroutine;
        private bool isStarting;
        private bool activeCameraIsFrontFacing;

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
        public bool HasValidFrame => IsRunning && FrameWidth > 16 && FrameHeight > 16;

        private void Start()
        {
            if (playOnStart)
            {
                StartCamera();
            }
        }

        private void OnDestroy()
        {
            StopCamera();

            if (frameTexture != null)
            {
                Destroy(frameTexture);
                frameTexture = null;
            }
        }

        public bool StartCamera()
        {
            if (IsRunning)
            {
                return true;
            }

            if (isStarting)
            {
                return true;
            }

            LastError = string.Empty;
            isStarting = true;
            startCameraCoroutine = StartCoroutine(StartCameraRoutine());
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

        private IEnumerator StartCameraRoutine()
        {
            if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
            {
                yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);
            }

            if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
            {
                LastError = "Camera permission was denied.";
                Debug.LogWarning("[CameraCaptureSource] " + LastError);
                isStarting = false;
                startCameraCoroutine = null;
                yield break;
            }

            if (WebCamTexture.devices == null || WebCamTexture.devices.Length == 0)
            {
                LastError = "No camera device was found.";
                Debug.LogWarning("[CameraCaptureSource] " + LastError);
                isStarting = false;
                startCameraCoroutine = null;
                yield break;
            }

            try
            {
                var selectedDevice = ResolveCameraDevice();
                activeCameraIsFrontFacing = selectedDevice.isFrontFacing;
                activeDeviceName = selectedDevice.name;
                webCamTexture = string.IsNullOrWhiteSpace(activeDeviceName)
                    ? new WebCamTexture(Mathf.Max(16, requestedWidth), Mathf.Max(16, requestedHeight), Mathf.Max(1, requestedFps))
                    : new WebCamTexture(activeDeviceName, Mathf.Max(16, requestedWidth), Mathf.Max(16, requestedHeight), Mathf.Max(1, requestedFps));

                webCamTexture.Play();
                PreviewTextureChanged?.Invoke(webCamTexture);
            }
            catch (Exception exception)
            {
                LastError = "Camera failed to start: " + exception.Message;
                Debug.LogWarning("[CameraCaptureSource] " + LastError);
                if (webCamTexture != null)
                {
                    Destroy(webCamTexture);
                    webCamTexture = null;
                }

                activeDeviceName = string.Empty;
                activeCameraIsFrontFacing = false;
            }

            isStarting = false;
            startCameraCoroutine = null;
        }

        public void StopCamera()
        {
            if (startCameraCoroutine != null)
            {
                StopCoroutine(startCameraCoroutine);
                startCameraCoroutine = null;
            }

            isStarting = false;

            if (webCamTexture == null)
            {
                return;
            }

            if (webCamTexture.isPlaying)
            {
                webCamTexture.Stop();
            }

            Destroy(webCamTexture);
            webCamTexture = null;
            activeDeviceName = string.Empty;
            activeCameraIsFrontFacing = false;
            PreviewTextureChanged?.Invoke(null);
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

        private WebCamDevice ResolveCameraDevice()
        {
            if (!string.IsNullOrWhiteSpace(cameraDeviceName))
            {
                foreach (var device in WebCamTexture.devices)
                {
                    if (string.Equals(device.name, cameraDeviceName, StringComparison.OrdinalIgnoreCase))
                    {
                        return device;
                    }
                }
            }

            foreach (var device in WebCamTexture.devices)
            {
                if (device.isFrontFacing == preferFrontCamera)
                {
                    return device;
                }
            }

            return WebCamTexture.devices[0];
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
