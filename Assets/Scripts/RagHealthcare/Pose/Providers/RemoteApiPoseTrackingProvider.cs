using System;
using System.Collections;
using Rag.Healthcare.Api;
using UnityEngine;

namespace Rag.Healthcare.Pose.Providers
{
    public sealed class RemoteApiPoseTrackingProvider : PoseTrackingProvider
    {
        [SerializeField] private string apiEndpointUrl = "http://localhost:8000/api/pose/track";
        [SerializeField] private string apiBearerToken = string.Empty;
        [SerializeField] private string sessionId = string.Empty;
        [SerializeField] private int timeoutSeconds = 10;
        [SerializeField, Range(1, 100)] private int jpegQuality = 75;

        private Texture2D readableTexture;
        private bool isReady;

        public override PoseTrackingBackend Backend => PoseTrackingBackend.RemoteApi;
        public override bool IsReady => isReady;

        public override IEnumerator Initialize()
        {
            isReady = !string.IsNullOrWhiteSpace(apiEndpointUrl);
            if (!isReady)
            {
                SetFailure("Pose tracking API endpoint is missing.");
            }

            yield break;
        }

        public override IEnumerator EstimatePose(
            Texture source,
            long timestampUnixMilliseconds,
            Action<JointTrackingFrame> onFrame,
            Action<string> onError)
        {
            if (!IsReady)
            {
                onError?.Invoke("Pose tracking API endpoint is missing.");
                yield break;
            }

            if (!TryEncodeTexture(source, out var jpegBytes))
            {
                onError?.Invoke("No camera frame was provided.");
                yield break;
            }

            var request = new PoseTrackingApiRequest
            {
                EndpointUrl = apiEndpointUrl,
                BearerToken = apiBearerToken,
                SessionId = sessionId,
                TimeoutSeconds = timeoutSeconds
            };

            PoseTrackingApiResult result = null;
            yield return PoseTrackingApiClient.SendFrame(jpegBytes, request, value => result = value);

            if (result == null)
            {
                onError?.Invoke("Pose tracking API did not return a result.");
                yield break;
            }

            if (!result.Success)
            {
                onError?.Invoke(result.Error);
                yield break;
            }

            var frame = TryParseFrame(result.RawJson, onError);
            if (frame != null)
            {
                onFrame?.Invoke(frame);
            }
        }

        public override void Dispose()
        {
            if (readableTexture != null)
            {
                Destroy(readableTexture);
                readableTexture = null;
            }
        }

        private bool TryEncodeTexture(Texture source, out byte[] jpegBytes)
        {
            jpegBytes = null;

            if (source == null || source.width <= 16 || source.height <= 16)
            {
                return false;
            }

            EnsureReadableTexture(source.width, source.height);

            if (source is WebCamTexture webCamTexture)
            {
                readableTexture.SetPixels32(webCamTexture.GetPixels32());
                readableTexture.Apply(false);
                jpegBytes = readableTexture.EncodeToJPG(Mathf.Clamp(jpegQuality, 1, 100));
                return jpegBytes != null && jpegBytes.Length > 0;
            }

            var previous = RenderTexture.active;
            var temporary = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);

            try
            {
                Graphics.Blit(source, temporary);
                RenderTexture.active = temporary;
                readableTexture.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0, false);
                readableTexture.Apply(false);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temporary);
            }

            jpegBytes = readableTexture.EncodeToJPG(Mathf.Clamp(jpegQuality, 1, 100));
            return jpegBytes != null && jpegBytes.Length > 0;
        }

        private void EnsureReadableTexture(int width, int height)
        {
            if (readableTexture != null && readableTexture.width == width && readableTexture.height == height)
            {
                return;
            }

            if (readableTexture != null)
            {
                Destroy(readableTexture);
            }

            readableTexture = new Texture2D(width, height, TextureFormat.RGB24, false);
        }

        private static JointTrackingFrame TryParseFrame(string rawJson, Action<string> onError)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                onError?.Invoke("Pose tracking API returned an empty response.");
                return null;
            }

            try
            {
                var frame = JsonUtility.FromJson<JointTrackingFrame>(rawJson);
                if (frame == null)
                {
                    onError?.Invoke("Pose tracking response could not be parsed.");
                }

                return frame;
            }
            catch (ArgumentException exception)
            {
                onError?.Invoke("Pose tracking response JSON was invalid: " + exception.Message);
                return null;
            }
        }
    }
}
