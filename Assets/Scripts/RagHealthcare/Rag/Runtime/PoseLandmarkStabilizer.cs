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
            public Vector3 Sample0;
            public Vector3 Sample1;
            public Vector3 Sample2;
            public int SampleCount;
            public int NextSampleIndex;
            public Vector3 Smoothed;
            public bool HasSmoothed;
            public float Visibility;
            public float Confidence;
            public long LastAcceptedTimestamp;
            public int ConsecutiveOutliers;

            public void Reset()
            {
                Sample0 = default;
                Sample1 = default;
                Sample2 = default;
                SampleCount = 0;
                NextSampleIndex = 0;
                Smoothed = default;
                HasSmoothed = false;
                Visibility = 0f;
                Confidence = 0f;
                LastAcceptedTimestamp = 0L;
                ConsecutiveOutliers = 0;
            }
        }

        private readonly Dictionary<string, JointState> states =
            new Dictionary<string, JointState>(PoseJointNames.MediaPipe33.Length, StringComparer.OrdinalIgnoreCase);
        private readonly JointTrackingFrame outputFrame = new JointTrackingFrame();
        private TrackedJoint[] outputJoints = Array.Empty<TrackedJoint>();
        private TrackedJoint[] jointPool = Array.Empty<TrackedJoint>();

        // The returned frame is a reusable view and is valid until the next Stabilize call.
        public JointTrackingFrame Stabilize(JointTrackingFrame frame, RealtimePoseRuleSettings settings)
        {
            if (frame == null || frame.joints == null || settings == null)
            {
                return frame;
            }

            EnsureOutputCapacity(frame.joints.Length);
            for (var i = 0; i < frame.joints.Length; i++)
            {
                var source = frame.joints[i];
                if (source == null)
                {
                    outputJoints[i] = null;
                    continue;
                }

                var target = jointPool[i] ??= new TrackedJoint();
                outputJoints[i] = target;
                StabilizeJoint(source, target, frame.timestampUnixMilliseconds, settings);
            }

            outputFrame.id = frame.id;
            outputFrame.sessionId = frame.sessionId;
            outputFrame.timestampUnixMilliseconds = frame.timestampUnixMilliseconds;
            outputFrame.joints = outputJoints;
            outputFrame.feedback = frame.feedback ?? Array.Empty<PoseFeedbackMessage>();
            return outputFrame;
        }

        public void Reset()
        {
            foreach (var state in states.Values)
            {
                state.Reset();
            }
        }

        private void EnsureOutputCapacity(int jointCount)
        {
            if (outputJoints.Length == jointCount)
            {
                return;
            }

            outputJoints = new TrackedJoint[jointCount];
            jointPool = new TrackedJoint[jointCount];
        }

        private void StabilizeJoint(
            TrackedJoint joint,
            TrackedJoint target,
            long timestampUnixMilliseconds,
            RealtimePoseRuleSettings settings)
        {
            if (string.IsNullOrWhiteSpace(joint.name))
            {
                CopyJoint(joint, target);
                return;
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
                WriteJoint(target, joint.name, state.Smoothed, state.Visibility, state.Confidence);
                return;
            }

            if (score < settings.minimumVisibility)
            {
                CopyJoint(joint, target);
                return;
            }

            var raw = joint.NormalizedPosition;
            var deltaX = raw.x - state.Smoothed.x;
            var deltaY = raw.y - state.Smoothed.y;
            var maximumJump = settings.maximumNormalizedJointJump;
            if (state.HasSmoothed && deltaX * deltaX + deltaY * deltaY > maximumJump * maximumJump)
            {
                state.ConsecutiveOutliers++;
                if (state.ConsecutiveOutliers <= settings.maximumConsecutiveOutlierFrames)
                {
                    WriteJoint(target, joint.name, state.Smoothed, state.Visibility, state.Confidence);
                    return;
                }

                state.SampleCount = 0;
                state.NextSampleIndex = 0;
                state.Smoothed = raw;
            }
            else
            {
                state.ConsecutiveOutliers = 0;
                AddSample(state, raw);
                var median = Median(state);
                state.Smoothed = state.HasSmoothed
                    ? Vector3.Lerp(state.Smoothed, median, Mathf.Clamp01(settings.landmarkSmoothingAlpha))
                    : median;
            }

            state.HasSmoothed = true;
            state.Visibility = joint.visibility;
            state.Confidence = joint.confidence;
            state.LastAcceptedTimestamp = timestampUnixMilliseconds;
            WriteJoint(target, joint.name, state.Smoothed, joint.visibility, joint.confidence);
        }

        private static void AddSample(JointState state, Vector3 value)
        {
            switch (state.NextSampleIndex)
            {
                case 0:
                    state.Sample0 = value;
                    break;
                case 1:
                    state.Sample1 = value;
                    break;
                default:
                    state.Sample2 = value;
                    break;
            }

            state.NextSampleIndex = (state.NextSampleIndex + 1) % 3;
            if (state.SampleCount < 3)
            {
                state.SampleCount++;
            }
        }

        private static Vector3 Median(JointState state)
        {
            if (state.SampleCount <= 1)
            {
                return state.Sample0;
            }

            if (state.SampleCount == 2)
            {
                return (state.Sample0 + state.Sample1) * 0.5f;
            }

            return new Vector3(
                MiddleOfThree(state.Sample0.x, state.Sample1.x, state.Sample2.x),
                MiddleOfThree(state.Sample0.y, state.Sample1.y, state.Sample2.y),
                MiddleOfThree(state.Sample0.z, state.Sample1.z, state.Sample2.z));
        }

        private static float MiddleOfThree(float a, float b, float c)
        {
            return a + b + c - Mathf.Min(a, Mathf.Min(b, c)) - Mathf.Max(a, Mathf.Max(b, c));
        }

        private static void CopyJoint(TrackedJoint source, TrackedJoint target)
        {
            target.name = source.name;
            target.x = source.x;
            target.y = source.y;
            target.z = source.z;
            target.visibility = source.visibility;
            target.confidence = source.confidence;
        }

        private static void WriteJoint(
            TrackedJoint target,
            string name,
            Vector3 position,
            float visibility,
            float confidence)
        {
            target.name = name;
            target.x = position.x;
            target.y = position.y;
            target.z = position.z;
            target.visibility = visibility;
            target.confidence = confidence;
        }
    }
}
