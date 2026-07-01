using System;
using System.Globalization;
using System.IO;
using System.Text;
using Rag.Healthcare.Pose;
using Rag.Healthcare.Rag.Runtime;
using UnityEngine;

namespace Rag.Healthcare.Rag.Logging
{
    public sealed class SessionJsonlLogger : MonoBehaviour
    {
        [SerializeField] private bool logFrames = true;
        [SerializeField] private bool logFeedback = true;
        [SerializeField] private string directoryName = "RagSessions";

        private readonly StringBuilder builder = new StringBuilder(4096);
        private StreamWriter writer;
        private string sessionId;

        public string SessionId => sessionId;
        public string CurrentLogPath { get; private set; }

        private void Awake()
        {
            BeginSession();
        }

        private void OnDestroy()
        {
            EndSession();
        }

        public void BeginSession()
        {
            if (writer != null)
            {
                return;
            }

            sessionId = "session_" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
            var directory = Path.Combine(Application.persistentDataPath, directoryName);
            Directory.CreateDirectory(directory);
            CurrentLogPath = Path.Combine(directory, sessionId + ".jsonl");
            writer = new StreamWriter(CurrentLogPath, false, new UTF8Encoding(false));
            WriteRaw("{\"type\":\"session_start\",\"sessionId\":\"" + Escape(sessionId) + "\",\"timestampUnixMilliseconds\":" + Now() + "}");
            Debug.Log("[SessionJsonlLogger] Logging to " + CurrentLogPath);
        }

        public void EndSession()
        {
            if (writer == null)
            {
                return;
            }

            WriteRaw("{\"type\":\"session_end\",\"sessionId\":\"" + Escape(sessionId) + "\",\"timestampUnixMilliseconds\":" + Now() + "}");
            writer.Flush();
            writer.Dispose();
            writer = null;
        }

        public void LogFrame(JointTrackingFrame frame)
        {
            if (!logFrames || writer == null || frame == null)
            {
                return;
            }

            builder.Length = 0;
            builder.Append("{\"type\":\"frame\",\"sessionId\":\"")
                .Append(Escape(GetSessionId(frame.sessionId)))
                .Append("\",\"frameId\":\"")
                .Append(Escape(frame.id))
                .Append("\",\"timestampUnixMilliseconds\":")
                .Append(frame.timestampUnixMilliseconds)
                .Append(",\"joints\":[");

            if (frame.joints != null)
            {
                var writtenJointCount = 0;
                for (var i = 0; i < frame.joints.Length; i++)
                {
                    var joint = frame.joints[i];
                    if (joint == null)
                    {
                        continue;
                    }

                    if (writtenJointCount > 0)
                    {
                        builder.Append(',');
                    }

                    writtenJointCount++;
                    builder.Append("{\"name\":\"")
                        .Append(Escape(joint.name))
                        .Append("\",\"x\":")
                        .Append(Float(joint.x))
                        .Append(",\"y\":")
                        .Append(Float(joint.y))
                        .Append(",\"z\":")
                        .Append(Float(joint.z))
                        .Append(",\"visibility\":")
                        .Append(Float(joint.visibility))
                        .Append(",\"confidence\":")
                        .Append(Float(joint.confidence))
                        .Append('}');
                }
            }

            builder.Append("]}");
            WriteRaw(builder.ToString());
        }

        public void LogFeedback(FeedbackEvent feedbackEvent, PoseFeedbackMessage message)
        {
            if (!logFeedback || writer == null || feedbackEvent == null || message == null)
            {
                return;
            }

            builder.Length = 0;
            builder.Append("{\"type\":\"feedback\",\"sessionId\":\"")
                .Append(Escape(sessionId))
                .Append("\",\"timestampUnixMilliseconds\":")
                .Append(feedbackEvent.TimestampUnixMilliseconds)
                .Append(",\"id\":\"")
                .Append(Escape(message.id))
                .Append("\",\"ruleId\":\"")
                .Append(Escape(feedbackEvent.RuleId))
                .Append("\",\"exercise\":\"")
                .Append(Escape(feedbackEvent.Exercise))
                .Append("\",\"joint\":\"")
                .Append(Escape(message.joint))
                .Append("\",\"severity\":\"")
                .Append(feedbackEvent.Severity)
                .Append("\",\"confidence\":")
                .Append(Float(message.confidence))
                .Append(",\"text\":\"")
                .Append(Escape(message.text))
                .Append("\"}");

            WriteRaw(builder.ToString());
        }

        public void LogPhase(ExercisePhaseState phaseState)
        {
            if (writer == null || phaseState == null)
            {
                return;
            }

            WriteRaw(
                "{\"type\":\"phase\",\"sessionId\":\"" + Escape(sessionId) +
                "\",\"timestampUnixMilliseconds\":" + phaseState.PhaseStartedAtUnixMilliseconds +
                ",\"exercise\":\"" + Escape(phaseState.Exercise) +
                "\",\"phase\":\"" + phaseState.CurrentPhase +
                "\",\"repCount\":" + phaseState.RepCount +
                "}");
        }

        private string GetSessionId(string frameSessionId)
        {
            if (!string.IsNullOrWhiteSpace(frameSessionId))
            {
                return frameSessionId;
            }

            return sessionId;
        }

        private void WriteRaw(string line)
        {
            if (writer == null)
            {
                return;
            }

            writer.WriteLine(line);
        }

        private static long Now()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        private static string Float(float value)
        {
            return value.ToString("0.######", CultureInfo.InvariantCulture);
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");
        }
    }
}
