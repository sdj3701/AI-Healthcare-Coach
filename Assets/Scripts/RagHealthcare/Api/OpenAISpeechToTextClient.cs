using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace Rag.Healthcare.Api
{
    public static class OpenAISpeechToTextClient
    {
        private const string TranscriptionsUrl = "https://api.openai.com/v1/audio/transcriptions";

        public static IEnumerator TranscribeWav(
            byte[] wavBytes,
            string apiKey,
            OpenAITranscriptionRequest options,
            Action<OpenAITranscriptionResult> onComplete)
        {
            if (wavBytes == null || wavBytes.Length == 0)
            {
                onComplete?.Invoke(OpenAITranscriptionResult.Fail("No audio data was provided."));
                yield break;
            }

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                onComplete?.Invoke(OpenAITranscriptionResult.Fail("OpenAI API key is missing."));
                yield break;
            }

            options ??= new OpenAITranscriptionRequest();

            var form = new List<IMultipartFormSection>
            {
                new MultipartFormDataSection("model", options.Model),
                new MultipartFormFileSection("file", wavBytes, "speech.wav", "audio/wav")
            };

            if (!string.IsNullOrWhiteSpace(options.Language))
            {
                form.Add(new MultipartFormDataSection("language", options.Language));
            }

            if (!string.IsNullOrWhiteSpace(options.Prompt))
            {
                form.Add(new MultipartFormDataSection("prompt", options.Prompt));
            }

            using var request = UnityWebRequest.Post(TranscriptionsUrl, form);
            request.SetRequestHeader("Authorization", "Bearer " + apiKey.Trim());
            request.timeout = Mathf.Max(1, options.TimeoutSeconds);

            yield return request.SendWebRequest();

            var responseBody = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
            if (request.result != UnityWebRequest.Result.Success)
            {
                onComplete?.Invoke(OpenAITranscriptionResult.Fail(
                    BuildErrorMessage(request.responseCode, responseBody, request.error),
                    request.responseCode,
                    responseBody));
                yield break;
            }

            var response = JsonUtility.FromJson<OpenAITranscriptionResponse>(responseBody);
            if (response == null || string.IsNullOrWhiteSpace(response.text))
            {
                onComplete?.Invoke(OpenAITranscriptionResult.Fail(
                    "The transcription response did not contain text.",
                    request.responseCode,
                    responseBody));
                yield break;
            }

            onComplete?.Invoke(OpenAITranscriptionResult.Ok(response.text, request.responseCode, responseBody));
        }

        private static string BuildErrorMessage(long responseCode, string responseBody, string transportError)
        {
            var error = TryParseErrorResponse(responseBody);
            if (error != null && error.error != null && !string.IsNullOrWhiteSpace(error.error.message))
            {
                return $"OpenAI error {responseCode}: {error.error.message}";
            }

            if (!string.IsNullOrWhiteSpace(transportError))
            {
                return $"Request failed {responseCode}: {transportError}";
            }

            return $"Request failed {responseCode}.";
        }

        private static OpenAIErrorResponse TryParseErrorResponse(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
            {
                return null;
            }

            try
            {
                return JsonUtility.FromJson<OpenAIErrorResponse>(responseBody);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }

    [Serializable]
    public sealed class OpenAITranscriptionRequest
    {
        public string Model = "gpt-4o-transcribe";
        public string Language = "ko";
        public string Prompt = string.Empty;
        public int TimeoutSeconds = 60;
    }

    public sealed class OpenAITranscriptionResult
    {
        private OpenAITranscriptionResult(bool success, string text, string error, long responseCode, string rawJson)
        {
            Success = success;
            Text = text;
            Error = error;
            ResponseCode = responseCode;
            RawJson = rawJson;
        }

        public bool Success { get; }
        public string Text { get; }
        public string Error { get; }
        public long ResponseCode { get; }
        public string RawJson { get; }

        public static OpenAITranscriptionResult Ok(string text, long responseCode, string rawJson)
        {
            return new OpenAITranscriptionResult(true, text, string.Empty, responseCode, rawJson);
        }

        public static OpenAITranscriptionResult Fail(string error, long responseCode = 0, string rawJson = "")
        {
            return new OpenAITranscriptionResult(false, string.Empty, error, responseCode, rawJson);
        }
    }

    [Serializable]
    internal sealed class OpenAITranscriptionResponse
    {
        public string text;
    }

    [Serializable]
    internal sealed class OpenAIErrorResponse
    {
        public OpenAIError error;
    }

    [Serializable]
    internal sealed class OpenAIError
    {
        public string message;
        public string type;
        public string code;
    }
}
