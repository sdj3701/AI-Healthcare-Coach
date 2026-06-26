using System.Collections;
using UnityEngine;

namespace AIHealthcareCoach.MediaPipe
{
    public sealed class CameraPreviewController : MonoBehaviour
    {
        [SerializeField] private int requestedWidth = 640;
        [SerializeField] private int requestedHeight = 480;
        [SerializeField] private int requestedFps = 30;
        [SerializeField] private bool preferFrontCamera = true;
        [SerializeField] private bool mirrorFrontCameraPreview = true;

        private WebCamTexture webCamTexture;
        private bool activeCameraIsFrontFacing;
        private int frameCount;
        private float fpsWindowStartedAt;

        public WebCamTexture Texture
        {
            get { return webCamTexture; }
        }

        public bool IsRunning
        {
            get { return webCamTexture != null && webCamTexture.isPlaying; }
        }

        public bool DidUpdateThisFrame
        {
            get { return webCamTexture != null && webCamTexture.didUpdateThisFrame; }
        }

        public int Width
        {
            get { return webCamTexture == null ? 0 : webCamTexture.width; }
        }

        public int Height
        {
            get { return webCamTexture == null ? 0 : webCamTexture.height; }
        }

        public int RotationAngle
        {
            get { return webCamTexture == null ? 0 : webCamTexture.videoRotationAngle; }
        }

        public bool IsDisplayMirrored
        {
            get { return mirrorFrontCameraPreview && activeCameraIsFrontFacing; }
        }

        public float CameraFps { get; private set; }
        public string ActiveDeviceName { get; private set; }
        public string LastError { get; private set; }

        public IEnumerator StartCamera()
        {
            LastError = string.Empty;

            if (IsRunning)
            {
                yield break;
            }

            if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
            {
                yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);
            }

            if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
            {
                LastError = "Camera permission was denied.";
                yield break;
            }

            var devices = WebCamTexture.devices;
            if (devices == null || devices.Length == 0)
            {
                LastError = "No camera device was found.";
                yield break;
            }

            var selectedDevice = SelectDevice(devices);
            activeCameraIsFrontFacing = selectedDevice.isFrontFacing;
            ActiveDeviceName = selectedDevice.name;

            webCamTexture = new WebCamTexture(selectedDevice.name, requestedWidth, requestedHeight, requestedFps);
            webCamTexture.Play();

            fpsWindowStartedAt = Time.unscaledTime;
            frameCount = 0;
            CameraFps = 0f;
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
            ActiveDeviceName = string.Empty;
            activeCameraIsFrontFacing = false;
            CameraFps = 0f;
        }

        public void TickFps()
        {
            if (!DidUpdateThisFrame)
            {
                return;
            }

            frameCount++;
            var elapsed = Time.unscaledTime - fpsWindowStartedAt;
            if (elapsed < 1f)
            {
                return;
            }

            CameraFps = frameCount / elapsed;
            frameCount = 0;
            fpsWindowStartedAt = Time.unscaledTime;
        }

        public Color32[] GetPixels(Color32[] reusableBuffer)
        {
            if (webCamTexture == null || webCamTexture.width <= 16 || webCamTexture.height <= 16)
            {
                return reusableBuffer;
            }

            var requiredLength = webCamTexture.width * webCamTexture.height;
            if (reusableBuffer == null || reusableBuffer.Length != requiredLength)
            {
                reusableBuffer = new Color32[requiredLength];
            }

            return webCamTexture.GetPixels32(reusableBuffer);
        }

        public void DrawPreview(Rect rect)
        {
            if (webCamTexture == null)
            {
                GUI.Box(rect, "Camera preview");
                return;
            }

            var previousMatrix = GUI.matrix;
            if (IsDisplayMirrored)
            {
                GUIUtility.ScaleAroundPivot(new Vector2(-1f, 1f), rect.center);
            }

            if (RotationAngle != 0)
            {
                GUIUtility.RotateAroundPivot(-RotationAngle, rect.center);
            }

            GUI.DrawTexture(rect, webCamTexture, ScaleMode.ScaleToFit, false);
            GUI.matrix = previousMatrix;
        }

        private WebCamDevice SelectDevice(WebCamDevice[] devices)
        {
            for (var i = 0; i < devices.Length; i++)
            {
                if (devices[i].isFrontFacing == preferFrontCamera)
                {
                    return devices[i];
                }
            }

            return devices[0];
        }

        private void OnDestroy()
        {
            StopCamera();
        }
    }
}
