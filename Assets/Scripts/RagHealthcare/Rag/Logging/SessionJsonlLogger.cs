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
        [SerializeField, Range(1, 30)] private int maxLoggedFrameRate = 5;
        [SerializeField] private bool beginSessionOnAwake;
        [SerializeField] private string directoryName = "RagSessions";

        private readonly StringBuilder builder = new StringBuilder(4096);
        private StreamWriter writer;
        private string sessionId;
        private long nextFrameLogTimestamp;
        private bool loggingUnavailable;

        public string SessionId => sessionId;
        public string CurrentLogPath { get; private set; }

        private void Awake()
        {
            if (beginSessionOnAwake)
            {
                BeginSession();
            }
        }

        private void OnDestroy()
        {
            EndSession();
        }

        public void BeginSession()
        {
            if (writer != null || loggingUnavailable)
            {
                return;
            }

            try
            {
                sessionId = "session_" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
                var directory = Path.Combine(Application.persistentDataPath, directoryName);
                Directory.CreateDirectory(directory);
                CurrentLogPath = Path.Combine(directory, sessionId + ".jsonl");
                writer = new StreamWriter(CurrentLogPath, false, new UTF8Encoding(false));
                nextFrameLogTimestamp = 0;
                WriteRaw("{\"type\":\"session_start\",\"sessionId\":\"" + Escape(sessionId) + "\",\"timestampUnixMilliseconds\":" + Now() + "}");
                Debug.Log("[SessionJsonlLogger] Logging to " + CurrentLogPath);
            }
            catch (Exception exception)
            {
                writer?.Dispose();
                writer = null;
                CurrentLogPath = string.Empty;
                loggingUnavailable = true;
                Debug.LogWarning("[SessionJsonlLogger] Session logging is unavailable: " + exception.Message);
            }
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
            if (!logFrames || frame == null)
            {
                return;
            }

            BeginSession();
            if (writer == null)
            {
                return;
            }

            var timestamp = frame.timestampUnixMilliseconds > 0 ? frame.timestampUnixMilliseconds : Now();
            if (timestamp < nextFrameLogTimestamp)
            {
                return;
            }

            nextFrameLogTimestamp = timestamp + Math.Max(1L, 1000L / Math.Max(1, maxLoggedFrameRate));

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
                        .Append("\",\"x\":");
                    AppendFloat(builder, joint.x);
                    builder.Append(",\"y\":");
                    AppendFloat(builder, joint.y);
                    builder.Append(",\"z\":");
                    AppendFloat(builder, joint.z);
                    builder.Append(",\"visibility\":");
                    AppendFloat(builder, joint.visibility);
                    builder.Append(",\"confidence\":");
                    AppendFloat(builder, joint.confidence);
                    builder.Append('}');
                }
            }

            builder.Append("]}");
            WriteRaw(builder.ToString());
        }

        public void LogFeedback(FeedbackEvent feedbackEvent, PoseFeedbackMessage message)
        {
            if (!logFeedback || feedbackEvent == null || message == null)
            {
                return;
            }

            BeginSession();
            if (writer == null)
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
                .Append("\",\"confidence\":");
            AppendFloat(builder, message.confidence);
            builder.Append(",\"text\":\"")
                .Append(Escape(message.text))
                .Append("\"}");

            WriteRaw(builder.ToString());
        }

        public void LogPhase(ExercisePhaseState phaseState)
        {
            if (phaseState == null)
            {
                return;
            }

            BeginSession();
            if (writer == null)
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

        public void Flush()
        {
            writer?.Flush();
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

        private static void AppendFloat(StringBuilder target, float value)
        {
            Span<char> characters = stackalloc char[48];
            if (value.TryFormat(characters, out var written, "0.######", CultureInfo.InvariantCulture))
            {
                target.Append(characters.Slice(0, written));
                return;
            }

            target.Append(value.ToString("0.######", CultureInfo.InvariantCulture));
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
