using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace Rag.Healthcare.Api
{
    public static class PoseTrackingApiClient
    {
        public static IEnumerator SendFrame(
            byte[] jpegBytes,
            PoseTrackingApiRequest options,
            Action<PoseTrackingApiResult> onComplete)
        {
            if (jpegBytes == null || jpegBytes.Length == 0)
            {
                onComplete?.Invoke(PoseTrackingApiResult.Fail("No camera frame was provided."));
                yield break;
            }

            if (options == null || string.IsNullOrWhiteSpace(options.EndpointUrl))
            {
                onComplete?.Invoke(PoseTrackingApiResult.Fail("Pose tracking API endpoint is missing."));
                yield break;
            }

            var form = new List<IMultipartFormSection>
            {
                new MultipartFormFileSection("frame", jpegBytes, "frame.jpg", "image/jpeg")
            };

            if (!string.IsNullOrWhiteSpace(options.SessionId))
            {
                form.Add(new MultipartFormDataSection("sessionId", options.SessionId));
            }

            using var request = UnityWebRequest.Post(options.EndpointUrl, form);

            if (!string.IsNullOrWhiteSpace(options.BearerToken))
            {
                request.SetRequestHeader("Authorization", "Bearer " + options.BearerToken.Trim());
            }

            request.timeout = Mathf.Max(1, options.TimeoutSeconds);

            yield return request.SendWebRequest();

            var responseBody = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
            if (request.result != UnityWebRequest.Result.Success)
            {
                onComplete?.Invoke(PoseTrackingApiResult.Fail(
                    BuildErrorMessage(request.responseCode, responseBody, request.error),
                    request.responseCode,
                    responseBody));
                yield break;
            }

            onComplete?.Invoke(PoseTrackingApiResult.Ok(responseBody, request.responseCode));
        }

        private static string BuildErrorMessage(long responseCode, string responseBody, string transportError)
        {
            var error = TryParseErrorResponse(responseBody);
            if (error != null && !string.IsNullOrWhiteSpace(error.message))
            {
                return $"Pose tracking API error {responseCode}: {error.message}";
            }

            if (error != null && error.error != null && !string.IsNullOrWhiteSpace(error.error.message))
            {
                return $"Pose tracking API error {responseCode}: {error.error.message}";
            }

            if (!string.IsNullOrWhiteSpace(transportError))
            {
                return $"Pose tracking API request failed {responseCode}: {transportError}";
            }

            return $"Pose tracking API request failed {responseCode}.";
        }

        private static ApiErrorResponse TryParseErrorResponse(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
            {
                return null;
            }

            try
            {
                return JsonUtility.FromJson<ApiErrorResponse>(responseBody);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }

    [Serializable]
    public sealed class PoseTrackingApiRequest
    {
        public string EndpointUrl;
        public string BearerToken;
        public string SessionId;
        public int TimeoutSeconds = 10;
    }

    public sealed class PoseTrackingApiResult
    {
        private PoseTrackingApiResult(bool success, string rawJson, string error, long responseCode)
        {
            Success = success;
            RawJson = rawJson;
            Error = error;
            ResponseCode = responseCode;
        }

        public bool Success { get; }
        public string RawJson { get; }
        public string Error { get; }
        public long ResponseCode { get; }

        public static PoseTrackingApiResult Ok(string rawJson, long responseCode)
        {
            return new PoseTrackingApiResult(true, rawJson, string.Empty, responseCode);
        }

        public static PoseTrackingApiResult Fail(string error, long responseCode = 0, string rawJson = "")
        {
            return new PoseTrackingApiResult(false, rawJson, error, responseCode);
        }
    }

    [Serializable]
    internal sealed class ApiErrorResponse
    {
        public string message;
        public ApiError error;
    }

    [Serializable]
    internal sealed class ApiError
    {
        public string message;
        public string code;
    }
}
