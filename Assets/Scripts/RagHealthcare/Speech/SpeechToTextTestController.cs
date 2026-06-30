using System;
using System.Collections;
using Rag.Healthcare.Api;
using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif
using UnityEngine.UI;

namespace Rag.Healthcare.Speech
{
    public sealed class SpeechToTextTestController : MonoBehaviour
    {
        [Header("OpenAI")]
        [SerializeField] private string model = "gpt-4o-transcribe";
        [SerializeField] private string language = "ko";
        [SerializeField, TextArea(2, 5)] private string prompt =
            "This is Korean speech from a coaching session. Transcribe exercise, posture, breathing, pain, routine, and feedback terms naturally.";
        [SerializeField] private int timeoutSeconds = 60;

        [Header("Recording")]
        [SerializeField] private string microphoneDeviceName = string.Empty;
        [SerializeField] private int sampleRate = 16000;
        [SerializeField] private int maxRecordingSeconds = 10;
        [SerializeField] private int minimumRecordingMilliseconds = 300;

        [Header("Test UI")]
        [SerializeField] private bool autoBuildUi = true;
        [SerializeField] private InputField apiKeyInput;
        [SerializeField] private Button startButton;
        [SerializeField] private Button stopAndTranscribeButton;
        [SerializeField] private Text statusText;
        [SerializeField] private Text transcriptText;

        private AudioClip recordingClip;
        private string activeMicrophoneDevice;
        private bool isRecording;
        private bool isTranscribing;
        private Coroutine timerCoroutine;

        private void Awake()
        {
            if (autoBuildUi)
            {
                BuildUiIfNeeded();
            }

            HookUi();
            SetStatus("Ready. Enter an API key or set OPENAI_API_KEY, then start recording.");
            SetTranscript(string.Empty);
            RefreshButtons();
        }

        private void OnDestroy()
        {
            if (isRecording)
            {
                Microphone.End(activeMicrophoneDevice);
            }
        }

        public void StartRecording()
        {
            if (isRecording || isTranscribing)
            {
                return;
            }

            if (Microphone.devices == null || Microphone.devices.Length == 0)
            {
                SetStatus("No microphone device was found.");
                return;
            }

            activeMicrophoneDevice = ResolveMicrophoneDevice();
            recordingClip = Microphone.Start(activeMicrophoneDevice, false, maxRecordingSeconds, sampleRate);
            isRecording = true;
            SetTranscript(string.Empty);
            SetStatus($"Recording from '{activeMicrophoneDevice ?? "Default"}'...");
            RefreshButtons();

            if (timerCoroutine != null)
            {
                StopCoroutine(timerCoroutine);
            }

            timerCoroutine = StartCoroutine(AutoStopAtLimit());
        }

        public void StopRecordingAndTranscribe()
        {
            if (!isRecording)
            {
                return;
            }

            var samplePosition = Microphone.GetPosition(activeMicrophoneDevice);
            Microphone.End(activeMicrophoneDevice);
            isRecording = false;

            if (timerCoroutine != null)
            {
                StopCoroutine(timerCoroutine);
                timerCoroutine = null;
            }

            if (samplePosition <= 0 || recordingClip == null)
            {
                SetStatus("Recording was empty.");
                RefreshButtons();
                return;
            }

            var durationMilliseconds = samplePosition * 1000 / recordingClip.frequency;
            if (durationMilliseconds < minimumRecordingMilliseconds)
            {
                SetStatus("Recording was too short. Try again.");
                RefreshButtons();
                return;
            }

            var wavBytes = BuildTrimmedWav(recordingClip, samplePosition);
            StartCoroutine(Transcribe(wavBytes, durationMilliseconds));
        }

        private IEnumerator Transcribe(byte[] wavBytes, int durationMilliseconds)
        {
            var apiKey = ResolveApiKey();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                SetStatus("Missing API key. Enter a key in the field or set OPENAI_API_KEY before launching Unity.");
                RefreshButtons();
                yield break;
            }

            isTranscribing = true;
            RefreshButtons();
            SetStatus($"Sending {durationMilliseconds / 1000f:0.0}s WAV to OpenAI...");

            var options = new OpenAITranscriptionRequest
            {
                Model = model,
                Language = language,
                Prompt = prompt,
                TimeoutSeconds = timeoutSeconds
            };

            OpenAITranscriptionResult result = null;
            yield return OpenAISpeechToTextClient.TranscribeWav(wavBytes, apiKey, options, value => result = value);

            isTranscribing = false;
            RefreshButtons();

            if (result == null)
            {
                SetStatus("No response was returned.");
                yield break;
            }

            if (!result.Success)
            {
                SetStatus(result.Error);
                yield break;
            }

            SetStatus("Transcription complete.");
            SetTranscript(result.Text);
        }

        private byte[] BuildTrimmedWav(AudioClip sourceClip, int samplePosition)
        {
            var channels = sourceClip.channels;
            var samples = new float[samplePosition * channels];
            sourceClip.GetData(samples, 0);
            return WavEncoder.Encode(samples, channels, sourceClip.frequency);
        }

        private IEnumerator AutoStopAtLimit()
        {
            var seconds = Mathf.Max(1, maxRecordingSeconds);
            yield return new WaitForSeconds(seconds);

            if (isRecording)
            {
                StopRecordingAndTranscribe();
            }
        }

        private string ResolveMicrophoneDevice()
        {
            if (!string.IsNullOrWhiteSpace(microphoneDeviceName))
            {
                foreach (var device in Microphone.devices)
                {
                    if (string.Equals(device, microphoneDeviceName, StringComparison.OrdinalIgnoreCase))
                    {
                        return device;
                    }
                }
            }

            return Microphone.devices[0];
        }

        private string ResolveApiKey()
        {
            if (apiKeyInput != null && !string.IsNullOrWhiteSpace(apiKeyInput.text))
            {
                return apiKeyInput.text;
            }

            return Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        }

        private void HookUi()
        {
            if (startButton != null)
            {
                startButton.onClick.RemoveListener(StartRecording);
                startButton.onClick.AddListener(StartRecording);
            }

            if (stopAndTranscribeButton != null)
            {
                stopAndTranscribeButton.onClick.RemoveListener(StopRecordingAndTranscribe);
                stopAndTranscribeButton.onClick.AddListener(StopRecordingAndTranscribe);
            }
        }

        private void RefreshButtons()
        {
            if (startButton != null)
            {
                startButton.interactable = !isRecording && !isTranscribing;
            }

            if (stopAndTranscribeButton != null)
            {
                stopAndTranscribeButton.interactable = isRecording && !isTranscribing;
            }
        }

        private void SetStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = message;
            }

            Debug.Log("[SpeechToTextTest] " + message);
        }

        private void SetTranscript(string text)
        {
            if (transcriptText != null)
            {
                transcriptText.text = text;
            }
        }

        private void BuildUiIfNeeded()
        {
            if (startButton != null && stopAndTranscribeButton != null && statusText != null && transcriptText != null)
            {
                return;
            }

            EnsureEventSystem();

            var canvasObject = new GameObject("Speech To Text Test Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);

            var panel = CreatePanel(canvasObject.transform);
            CreateHeader(panel.transform);
            apiKeyInput = CreateInput(panel.transform, "OpenAI API key for local testing");
            statusText = CreateText(panel.transform, "Ready.", 18, TextAnchor.MiddleLeft, 48f);

            var buttonRow = CreateHorizontalGroup(panel.transform, 12f);
            startButton = CreateButton(buttonRow.transform, "Start Recording");
            stopAndTranscribeButton = CreateButton(buttonRow.transform, "Stop & Transcribe");

            CreateText(panel.transform, "Transcript", 18, TextAnchor.MiddleLeft, 32f);
            transcriptText = CreateText(panel.transform, string.Empty, 20, TextAnchor.UpperLeft, 220f);
            transcriptText.color = new Color(0.1f, 0.12f, 0.14f);
        }

        private static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null)
            {
                return;
            }

            var eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            eventSystem.AddComponent<InputSystemUIInputModule>();
#else
            eventSystem.AddComponent<StandaloneInputModule>();
#endif
        }

        private static GameObject CreatePanel(Transform parent)
        {
            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            panel.transform.SetParent(parent, false);

            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(760f, 560f);

            var image = panel.GetComponent<Image>();
            image.color = new Color(0.95f, 0.97f, 0.98f, 0.96f);

            var layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(28, 28, 24, 24);
            layout.spacing = 12f;
            layout.childControlHeight = false;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            return panel;
        }

        private static void CreateHeader(Transform parent)
        {
            var title = CreateText(parent, "OpenAI Speech-to-Text Test", 28, TextAnchor.MiddleLeft, 40f);
            title.color = new Color(0.05f, 0.07f, 0.09f);

            var subtitle = CreateText(parent, "Local test scene. Do not ship client-side API keys in production builds.", 16, TextAnchor.MiddleLeft, 34f);
            subtitle.color = new Color(0.35f, 0.38f, 0.42f);
        }

        private static InputField CreateInput(Transform parent, string placeholder)
        {
            var inputObject = new GameObject("Api Key Input", typeof(RectTransform), typeof(Image), typeof(InputField), typeof(LayoutElement));
            inputObject.transform.SetParent(parent, false);

            inputObject.GetComponent<Image>().color = Color.white;
            var layout = inputObject.GetComponent<LayoutElement>();
            layout.preferredHeight = 44f;

            var input = inputObject.GetComponent<InputField>();
            input.contentType = InputField.ContentType.Password;
            input.lineType = InputField.LineType.SingleLine;

            var text = CreateInputText(inputObject.transform, "Text", string.Empty, TextAnchor.MiddleLeft);
            var placeholderText = CreateInputText(inputObject.transform, "Placeholder", placeholder, TextAnchor.MiddleLeft);
            placeholderText.color = new Color(0.5f, 0.52f, 0.56f, 0.75f);

            input.textComponent = text;
            input.placeholder = placeholderText;
            return input;
        }

        private static Text CreateInputText(Transform parent, string name, string value, TextAnchor alignment)
        {
            var textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);

            var rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(12f, 4f);
            rect.offsetMax = new Vector2(-12f, -4f);

            var text = textObject.GetComponent<Text>();
            text.font = ResolveFont();
            text.fontSize = 16;
            text.alignment = alignment;
            text.color = Color.black;
            text.text = value;
            return text;
        }

        private static GameObject CreateHorizontalGroup(Transform parent, float spacing)
        {
            var row = new GameObject("Button Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(parent, false);

            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            row.GetComponent<LayoutElement>().preferredHeight = 52f;
            return row;
        }

        private static Button CreateButton(Transform parent, string label)
        {
            var buttonObject = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonObject.transform.SetParent(parent, false);

            var image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.12f, 0.34f, 0.72f);

            var colors = buttonObject.GetComponent<Button>().colors;
            colors.normalColor = new Color(0.12f, 0.34f, 0.72f);
            colors.highlightedColor = new Color(0.16f, 0.42f, 0.84f);
            colors.pressedColor = new Color(0.08f, 0.25f, 0.55f);
            colors.disabledColor = new Color(0.56f, 0.61f, 0.68f);
            buttonObject.GetComponent<Button>().colors = colors;

            var layout = buttonObject.GetComponent<LayoutElement>();
            layout.preferredHeight = 48f;

            var text = CreateText(buttonObject.transform, label, 18, TextAnchor.MiddleCenter, 48f);
            var rect = text.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            text.color = Color.white;

            return buttonObject.GetComponent<Button>();
        }

        private static Text CreateText(Transform parent, string value, int fontSize, TextAnchor alignment, float preferredHeight)
        {
            var textObject = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            textObject.transform.SetParent(parent, false);

            var layout = textObject.GetComponent<LayoutElement>();
            layout.preferredHeight = preferredHeight;

            var text = textObject.GetComponent<Text>();
            text.font = ResolveFont();
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = new Color(0.18f, 0.2f, 0.23f);
            text.text = value;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            return text;
        }

        private static Font ResolveFont()
        {
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ??
                   Resources.GetBuiltinResource<Font>("Arial.ttf");
        }
    }
}
