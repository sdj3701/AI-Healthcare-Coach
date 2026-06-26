using UnityEngine;

namespace AIHealthcareCoach.Tts
{
    public sealed class TtsDemoView : MonoBehaviour
    {
        [SerializeField] private TtsController controller;
        [SerializeField, TextArea(3, 6)]
        private string inputText = "무릎이 안쪽으로 모이고 있어요. 무릎을 발끝 방향으로 맞춰주세요.";

        private const string ReadyStatus = "문장을 입력하고 Speak 버튼을 누르세요.";
        private string statusMessage = ReadyStatus;
        private Vector2 scrollPosition;
        private GUIStyle titleStyle;
        private GUIStyle labelStyle;
        private GUIStyle textAreaStyle;
        private GUIStyle buttonStyle;
        private GUIStyle statusStyle;

        private void Awake()
        {
            if (controller == null)
            {
                controller = FindFirstObjectByType<TtsController>();
            }
        }

        private void OnGUI()
        {
            EnsureStyles();

            var panelWidth = Mathf.Min(Screen.width - 48f, 760f);
            var panelHeight = Mathf.Min(Screen.height - 48f, 430f);
            var panelRect = new Rect(
                (Screen.width - panelWidth) * 0.5f,
                (Screen.height - panelHeight) * 0.5f,
                panelWidth,
                panelHeight);

            GUILayout.BeginArea(panelRect, GUI.skin.box);
            scrollPosition = GUILayout.BeginScrollView(scrollPosition);

            GUILayout.Label("AI Healthcare Coach TTS Demo", titleStyle);
            GUILayout.Space(8f);
            GUILayout.Label("읽어줄 코칭 문장을 직접 입력하세요.", labelStyle);
            GUILayout.Space(10f);

            GUI.SetNextControlName("TtsInput");
            inputText = GUILayout.TextArea(inputText, textAreaStyle, GUILayout.MinHeight(130f));

            GUILayout.Space(16f);
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Speak", buttonStyle, GUILayout.Width(150f), GUILayout.Height(48f)))
            {
                HandleSpeakClicked();
            }

            GUILayout.Space(12f);

            if (GUILayout.Button("Stop", buttonStyle, GUILayout.Width(150f), GUILayout.Height(48f)))
            {
                HandleStopClicked();
            }

            GUILayout.EndHorizontal();

            GUILayout.Space(16f);
            GUILayout.Label(statusMessage, statusStyle);

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        public void HandleSpeakClicked()
        {
            if (controller == null)
            {
                controller = FindFirstObjectByType<TtsController>();
            }

            if (controller == null)
            {
                statusMessage = "TTS 컨트롤러를 찾을 수 없습니다.";
                return;
            }

            controller.TrySpeak(inputText, out statusMessage);
        }

        public void HandleStopClicked()
        {
            if (controller == null)
            {
                controller = FindFirstObjectByType<TtsController>();
            }

            if (controller == null)
            {
                statusMessage = "TTS 컨트롤러를 찾을 수 없습니다.";
                return;
            }

            controller.StopSpeaking(out statusMessage);
        }

        private void EnsureStyles()
        {
            if (titleStyle != null)
            {
                return;
            }

            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 28,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white },
                wordWrap = true
            };

            labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 17,
                normal = { textColor = new Color(0.82f, 0.86f, 0.9f) },
                wordWrap = true
            };

            textAreaStyle = new GUIStyle(GUI.skin.textArea)
            {
                fontSize = 18,
                wordWrap = true
            };
            textAreaStyle.padding = new RectOffset(12, 12, 10, 10);

            buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold
            };

            statusStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                normal = { textColor = new Color(0.72f, 0.95f, 0.82f) },
                wordWrap = true
            };
        }
    }
}
