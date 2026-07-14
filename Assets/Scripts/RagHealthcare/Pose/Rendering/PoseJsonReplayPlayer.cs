using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Rag.Healthcare.Rag.Logging;
using UnityEngine;

#pragma warning disable 0649

namespace Rag.Healthcare.Pose.Rendering
{
    public sealed class PoseJsonReplayPlayer : MonoBehaviour
    {
        [SerializeField] private SessionJsonlLogger sessionLogger;
        [SerializeField] private PoseAvatar3DPreview avatarPreview;
        [SerializeField] private string sessionDirectoryName = "RagSessions";
        [SerializeField, Range(0.1f, 4f)] private float playbackSpeed = 1f;
        [SerializeField, Range(0.01f, 1f)] private float maximumFrameDelaySeconds = 0.2f;
        [SerializeField] private bool loopReplay;

        private Coroutine replayCoroutine;

        public bool IsPlaying { get; private set; }
        public string LastReplayPath { get; private set; } = string.Empty;
        public string LastReplayStatus { get; private set; } = "Replay idle";
        public int LoadedFrameCount { get; private set; }

        public Texture PreviewTexture
        {
            get
            {
                EnsureAvatarPreview();
                return avatarPreview == null ? null : avatarPreview.PreviewTexture;
            }
        }

        private void Awake()
        {
            sessionLogger ??= FindFirstObjectByType<SessionJsonlLogger>();
            avatarPreview ??= FindFirstObjectByType<PoseAvatar3DPreview>();
        }

        public void PlayLatestSession()
        {
            StopReplay();

            sessionLogger ??= FindFirstObjectByType<SessionJsonlLogger>();
            sessionLogger?.Flush();

            if (!TryResolveReplayPath(out var path))
            {
                LastReplayStatus = "Replay file not found";
                Debug.LogWarning("[PoseJsonReplayPlayer] No session JSONL file was found.");
                return;
            }

            if (!TryLoadFrames(path, out var frames))
            {
                LastReplayStatus = "Replay has no frames";
                Debug.LogWarning("[PoseJsonReplayPlayer] No frame records were found in " + path);
                return;
            }

            LastReplayPath = path;
            LoadedFrameCount = frames.Count;
            EnsureAvatarPreview();
            avatarPreview?.RenderFrame(frames[0]);
            replayCoroutine = StartCoroutine(ReplayRoutine(frames));
        }

        public void StopReplay()
        {
            if (replayCoroutine != null)
            {
                StopCoroutine(replayCoroutine);
                replayCoroutine = null;
            }

            IsPlaying = false;
        }

        private IEnumerator ReplayRoutine(IReadOnlyList<JointTrackingFrame> frames)
        {
            IsPlaying = true;
            LastReplayStatus = "Replay playing";
            EnsureAvatarPreview();

            do
            {
                for (var i = 0; i < frames.Count; i++)
                {
                    avatarPreview?.RenderFrame(frames[i]);

                    if (i >= frames.Count - 1)
                    {
                        continue;
                    }

                    var delay = CalculateFrameDelay(frames[i], frames[i + 1]);
                    if (delay > 0f)
                    {
                        yield return new WaitForSecondsRealtime(delay);
                    }
                    else
                    {
                        yield return null;
                    }
                }
            }
            while (loopReplay);

            IsPlaying = false;
            replayCoroutine = null;
            LastReplayStatus = "Replay complete";
        }

        private bool TryResolveReplayPath(out string path)
        {
            path = string.Empty;

            if (sessionLogger != null &&
                !string.IsNullOrWhiteSpace(sessionLogger.CurrentLogPath) &&
                File.Exists(sessionLogger.CurrentLogPath))
            {
                path = sessionLogger.CurrentLogPath;
                return true;
            }

            var directory = Path.Combine(Application.persistentDataPath, sessionDirectoryName);
            if (!Directory.Exists(directory))
            {
                return false;
            }

            var latestFile = Directory
                .GetFiles(directory, "*.jsonl", SearchOption.TopDirectoryOnly)
                .Select(file => new FileInfo(file))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .FirstOrDefault();

            if (latestFile == null)
            {
                return false;
            }

            path = latestFile.FullName;
            return true;
        }

        private static bool TryLoadFrames(string path, out List<JointTrackingFrame> frames)
        {
            frames = new List<JointTrackingFrame>();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return false;
            }

            var feedbackEvents = new List<LoggedFeedbackRecord>();

            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (line.Contains("\"type\":\"frame\""))
                {
                    try
                    {
                        var record = JsonUtility.FromJson<LoggedFrameRecord>(line);
                        if (record == null || record.joints == null || record.joints.Length == 0)
                        {
                            continue;
                        }

                        frames.Add(new JointTrackingFrame
                        {
                            id = string.IsNullOrWhiteSpace(record.frameId) ? Guid.NewGuid().ToString("N") : record.frameId,
                            sessionId = record.sessionId,
                            timestampUnixMilliseconds = record.timestampUnixMilliseconds,
                            joints = record.joints,
                            feedback = null
                        });
                    }
                    catch (ArgumentException exception)
                    {
                        Debug.LogWarning("[PoseJsonReplayPlayer] Skipped invalid frame JSON: " + exception.Message);
                    }
                }
                else if (line.Contains("\"type\":\"feedback\""))
                {
                    try
                    {
                        var record = JsonUtility.FromJson<LoggedFeedbackRecord>(line);
                        if (record != null && !string.IsNullOrWhiteSpace(record.joint))
                        {
                            feedbackEvents.Add(record);
                        }
                    }
                    catch (ArgumentException exception)
                    {
                        Debug.LogWarning("[PoseJsonReplayPlayer] Skipped invalid feedback JSON: " + exception.Message);
                    }
                }
            }

            if (frames.Count == 0)
            {
                return false;
            }

            frames.Sort((left, right) => left.timestampUnixMilliseconds.CompareTo(right.timestampUnixMilliseconds));

            // Map feedback to active frames for 2.5 seconds (2500ms) after feedback timestamp
            foreach (var frame in frames)
            {
                var frameFeedback = new List<PoseFeedbackMessage>();
                foreach (var fb in feedbackEvents)
                {
                    var diff = frame.timestampUnixMilliseconds - fb.timestampUnixMilliseconds;
                    if (diff >= 0 && diff <= 2500)
                    {
                        if (Enum.TryParse<FeedbackSeverity>(fb.severity, true, out var severityEnum))
                        {
                            frameFeedback.Add(new PoseFeedbackMessage
                            {
                                id = fb.id,
                                text = fb.text,
                                joint = fb.joint,
                                confidence = fb.confidence,
                                severity = severityEnum
                            });
                        }
                    }
                }
                frame.feedback = frameFeedback.ToArray();
            }

            return frames.Count > 0;
        }

        private float CalculateFrameDelay(JointTrackingFrame current, JointTrackingFrame next)
        {
            var deltaSeconds = (next.timestampUnixMilliseconds - current.timestampUnixMilliseconds) / 1000f;
            if (deltaSeconds <= 0f)
            {
                return 0f;
            }

            var speed = Mathf.Max(0.1f, playbackSpeed);
            return Mathf.Min(deltaSeconds / speed, maximumFrameDelaySeconds);
        }

        private void EnsureAvatarPreview()
        {
            avatarPreview ??= FindFirstObjectByType<PoseAvatar3DPreview>();
            if (avatarPreview == null)
            {
                avatarPreview = gameObject.AddComponent<PoseAvatar3DPreview>();
            }
        }

        [Serializable]
        private sealed class LoggedFrameRecord
        {
            public string type;
            public string sessionId;
            public string frameId;
            public long timestampUnixMilliseconds;
            public TrackedJoint[] joints;
        }

        [Serializable]
        private sealed class LoggedFeedbackRecord
        {
            public string type;
            public string sessionId;
            public long timestampUnixMilliseconds;
            public string id;
            public string ruleId;
            public string exercise;
            public string joint;
            public string severity;
            public float confidence;
            public string text;
        }
    }
}

#pragma warning restore 0649
