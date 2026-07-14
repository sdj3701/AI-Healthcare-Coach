using System;
using System.Collections.Generic;
using Rag.Healthcare.Pose;
using UnityEngine;

namespace Rag.Healthcare.Rag.Runtime
{
    public sealed class PoseLandmarkStabilizer
    {
        private sealed class JointState
        {
            public readonly List<Vector3> Samples = new List<Vector3>(3);
            public Vector3 Smoothed;
            public bool HasSmoothed;
            public float Visibility;
            public float Confidence;
            public long LastAcceptedTimestamp;
            public int ConsecutiveOutliers;
        }

        private readonly Dictionary<string, JointState> states =
            new Dictionary<string, JointState>(StringComparer.OrdinalIgnoreCase);

        public JointTrackingFrame Stabilize(JointTrackingFrame frame, RealtimePoseRuleSettings settings)
        {
            if (frame == null || frame.joints == null || settings == null)
            {
                return frame;
            }

            var joints = new TrackedJoint[frame.joints.Length];
            for (var i = 0; i < frame.joints.Length; i++)
            {
                joints[i] = StabilizeJoint(frame.joints[i], frame.timestampUnixMilliseconds, settings);
            }

            return new JointTrackingFrame
            {
                id = frame.id,
                sessionId = frame.sessionId,
                timestampUnixMilliseconds = frame.timestampUnixMilliseconds,
                joints = joints,
                feedback = frame.feedback ?? Array.Empty<PoseFeedbackMessage>()
            };
        }

        public void Reset() => states.Clear();

        private TrackedJoint StabilizeJoint(
            TrackedJoint joint,
            long timestampUnixMilliseconds,
            RealtimePoseRuleSettings settings)
        {
            if (joint == null || string.IsNullOrWhiteSpace(joint.name))
            {
                return joint;
            }

            if (!states.TryGetValue(joint.name, out var state))
            {
                state = new JointState();
                states[joint.name] = state;
            }

            var score = PoseFrameView.GetJointScore(joint);
            var elapsedSeconds = state.LastAcceptedTimestamp <= 0L || timestampUnixMilliseconds <= state.LastAcceptedTimestamp
                ? float.MaxValue
                : (timestampUnixMilliseconds - state.LastAcceptedTimestamp) / 1000f;

            if (state.HasSmoothed &&
                score < settings.minimumVisibility &&
                elapsedSeconds <= settings.lowConfidenceGraceSeconds)
            {
                return CreateJoint(joint.name, state.Smoothed, state.Visibility, state.Confidence);
            }

            if (score < settings.minimumVisibility)
            {
                return CloneJoint(joint);
            }

            var raw = joint.NormalizedPosition;
            if (state.HasSmoothed &&
                Vector2.Distance(new Vector2(raw.x, raw.y), new Vector2(state.Smoothed.x, state.Smoothed.y)) >
                settings.maximumNormalizedJointJump)
            {
                state.ConsecutiveOutliers++;
                if (state.ConsecutiveOutliers <= settings.maximumConsecutiveOutlierFrames)
                {
                    return CreateJoint(joint.name, state.Smoothed, state.Visibility, state.Confidence);
                }

                state.Samples.Clear();
                state.Smoothed = raw;
            }
            else
            {
                state.ConsecutiveOutliers = 0;
                AddSample(state.Samples, raw);
                var median = Median(state.Samples);
                state.Smoothed = state.HasSmoothed
                    ? Vector3.Lerp(state.Smoothed, median, Mathf.Clamp01(settings.landmarkSmoothingAlpha))
                    : median;
            }

            state.HasSmoothed = true;
            state.Visibility = joint.visibility;
            state.Confidence = joint.confidence;
            state.LastAcceptedTimestamp = timestampUnixMilliseconds;
            return CreateJoint(joint.name, state.Smoothed, joint.visibility, joint.confidence);
        }

        private static void AddSample(IList<Vector3> samples, Vector3 value)
        {
            if (samples.Count >= 3)
            {
                samples.RemoveAt(0);
            }

            samples.Add(value);
        }

        private static Vector3 Median(IList<Vector3> values)
        {
            if (values.Count == 0)
            {
                return default;
            }

            var xs = new float[values.Count];
            var ys = new float[values.Count];
            var zs = new float[values.Count];
            for (var i = 0; i < values.Count; i++)
            {
                xs[i] = values[i].x;
                ys[i] = values[i].y;
                zs[i] = values[i].z;
            }

            Array.Sort(xs);
            Array.Sort(ys);
            Array.Sort(zs);
            return new Vector3(Middle(xs), Middle(ys), Middle(zs));
        }

        private static float Middle(IReadOnlyList<float> values)
        {
            var middle = values.Count / 2;
            return values.Count % 2 == 0
                ? (values[middle - 1] + values[middle]) * 0.5f
                : values[middle];
        }

        private static TrackedJoint CloneJoint(TrackedJoint joint)
        {
            return new TrackedJoint
            {
                name = joint.name,
                x = joint.x,
                y = joint.y,
                z = joint.z,
                visibility = joint.visibility,
                confidence = joint.confidence
            };
        }

        private static TrackedJoint CreateJoint(
            string name,
            Vector3 position,
            float visibility,
            float confidence)
        {
            return new TrackedJoint
            {
                name = name,
                x = position.x,
                y = position.y,
                z = position.z,
                visibility = visibility,
                confidence = confidence
            };
        }
    }
}
