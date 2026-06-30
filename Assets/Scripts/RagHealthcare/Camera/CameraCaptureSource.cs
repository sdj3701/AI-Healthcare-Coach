using System;
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
        [SerializeField] private bool playOnStart = true;

        private WebCamTexture webCamTexture;
        private Texture2D frameTexture;
        private string activeDeviceName;

        public event Action<Texture> PreviewTextureChanged;

        public bool IsRunning => webCamTexture != null && webCamTexture.isPlaying;
        public string ActiveDeviceName => activeDeviceName;
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

            if (WebCamTexture.devices == null || WebCamTexture.devices.Length == 0)
            {
                Debug.LogWarning("[CameraCaptureSource] No camera device was found.");
                return false;
            }

            activeDeviceName = ResolveCameraDeviceName();
            webCamTexture = string.IsNullOrWhiteSpace(activeDeviceName)
                ? new WebCamTexture(Mathf.Max(16, requestedWidth), Mathf.Max(16, requestedHeight), Mathf.Max(1, requestedFps))
                : new WebCamTexture(activeDeviceName, Mathf.Max(16, requestedWidth), Mathf.Max(16, requestedHeight), Mathf.Max(1, requestedFps));

            webCamTexture.Play();
            PreviewTextureChanged?.Invoke(webCamTexture);
            return true;
        }

        public void StopCamera()
        {
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

        private string ResolveCameraDeviceName()
        {
            if (!string.IsNullOrWhiteSpace(cameraDeviceName))
            {
                foreach (var device in WebCamTexture.devices)
                {
                    if (string.Equals(device.name, cameraDeviceName, StringComparison.OrdinalIgnoreCase))
                    {
                        return device.name;
                    }
                }
            }

            return WebCamTexture.devices[0].name;
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
