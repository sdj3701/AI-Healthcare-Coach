using System;
using System.Collections.Generic;
using AIHealthcareCoach.MediaPipe;
using Rag.Healthcare.Pose;
using Rag.Healthcare.Pose.Rendering;
using UnityEngine;

namespace Rag.Healthcare.Replay
{
    public static class InstructorSquatClip
    {
        public const float DurationSeconds = 3.2f;

        public static JointTrackingFrame Sample(float timeSeconds)
        {
            var phase = Mathf.Repeat(timeSeconds, DurationSeconds) / DurationSeconds;
            var depth = Mathf.Sin(phase * Mathf.PI);
            depth *= depth;
            var hipY = Mathf.Lerp(0.52f, 0.68f, depth);
            var kneeY = Mathf.Lerp(0.70f, 0.73f, depth);
            var kneeSpread = Mathf.Lerp(0.13f, 0.17f, depth);

            var joints = new List<TrackedJoint>
            {
                Joint(PoseJointNames.Nose, 0.5f, Mathf.Lerp(0.18f, 0.29f, depth)),
                Joint(PoseJointNames.LeftShoulder, 0.42f, Mathf.Lerp(0.31f, 0.40f, depth)),
                Joint(PoseJointNames.RightShoulder, 0.58f, Mathf.Lerp(0.31f, 0.40f, depth)),
                Joint(PoseJointNames.LeftElbow, 0.38f, 0.45f),
                Joint(PoseJointNames.RightElbow, 0.62f, 0.45f),
                Joint(PoseJointNames.LeftWrist, 0.45f, 0.49f),
                Joint(PoseJointNames.RightWrist, 0.55f, 0.49f),
                Joint(PoseJointNames.LeftHip, 0.45f, hipY),
                Joint(PoseJointNames.RightHip, 0.55f, hipY),
                Joint(PoseJointNames.LeftKnee, 0.5f - kneeSpread, kneeY),
                Joint(PoseJointNames.RightKnee, 0.5f + kneeSpread, kneeY),
                Joint(PoseJointNames.LeftAnkle, 0.38f, 0.90f),
                Joint(PoseJointNames.RightAnkle, 0.62f, 0.90f),
                Joint(PoseJointNames.LeftHeel, 0.37f, 0.92f),
                Joint(PoseJointNames.RightHeel, 0.63f, 0.92f),
                Joint(PoseJointNames.LeftFootIndex, 0.42f, 0.94f),
                Joint(PoseJointNames.RightFootIndex, 0.68f, 0.94f)
            };
            return new JointTrackingFrame
            {
                id = "instructor_" + phase.ToString("0.000"),
                timestampUnixMilliseconds = (long)(timeSeconds * 1000f),
                joints = joints.ToArray(),
                feedback = Array.Empty<PoseFeedbackMessage>()
            };
        }

        private static TrackedJoint Joint(string name, float x, float y) => new TrackedJoint
        {
            name = name,
            x = x,
            y = y,
            z = 0f,
            visibility = 1f,
            confidence = 1f
        };
    }

    [Serializable]
    public sealed class ReplayTimelineMarker
    {
        public long timeMilliseconds;
        public string ruleId;
        public string joint;
        public string label;
        public string severity;
    }

    public static class ReplayTimelineBuilder
    {
        public static ReplayTimelineMarker[] Build(IReadOnlyList<PoseFeedbackEvent> events)
        {
            if (events == null) return Array.Empty<ReplayTimelineMarker>();
            var result = new List<ReplayTimelineMarker>();
            for (var i = 0; i < events.Count; i++)
            {
                var item = events[i];
                if (item == null) continue;
                result.Add(new ReplayTimelineMarker
                {
                    timeMilliseconds = item.tMs,
                    ruleId = item.ruleId,
                    joint = item.jointName,
                    label = item.message,
                    severity = item.severity
                });
            }
            result.Sort((left, right) => left.timeMilliseconds.CompareTo(right.timeMilliseconds));
            return result.ToArray();
        }
    }

    public enum ReplayConfidenceLabel { Reliable, Limited, Unavailable }

    [Serializable]
    public sealed class ReplayConfidencePresentation
    {
        public ReplayConfidenceLabel label;
        public float confidence;
        public bool blurAvatar;
        public string message;
    }

    public static class ReplayConfidenceEvaluator
    {
        public static ReplayConfidencePresentation Evaluate(JointTrackingFrame frame)
        {
            if (frame?.joints == null || frame.joints.Length == 0)
                return new ReplayConfidencePresentation { label = ReplayConfidenceLabel.Unavailable, blurAvatar = true, message = "이 구간은 관절 좌표가 없습니다." };
            var total = 0f;
            foreach (var joint in frame.joints) total += Mathf.Clamp01(Mathf.Max(joint.visibility, joint.confidence));
            var confidence = total / frame.joints.Length;
            if (confidence < 0.4f)
                return new ReplayConfidencePresentation { label = ReplayConfidenceLabel.Unavailable, confidence = confidence, blurAvatar = true, message = "인식 신뢰도가 낮아 자세를 판정하지 않았습니다." };
            if (confidence < 0.65f)
                return new ReplayConfidencePresentation { label = ReplayConfidenceLabel.Limited, confidence = confidence, blurAvatar = true, message = "일부 관절의 신뢰도가 낮은 참고 구간입니다." };
            return new ReplayConfidencePresentation { label = ReplayConfidenceLabel.Reliable, confidence = confidence, blurAvatar = false, message = "신뢰 가능한 좌표 구간입니다." };
        }
    }

    public sealed class AvatarComparisonCoordinator : MonoBehaviour
    {
        public const string ReplayDisclaimer = "이 리플레이는 원본 영상이 아니라 저장된 관절 좌표로 재구성한 시각화입니다.";

        [SerializeField] private PoseAvatar3DPreview userAvatar;
        [SerializeField] private PoseAvatar3DPreview instructorAvatar;
        [SerializeField] private ReplayViewpoint viewpoint = ReplayViewpoint.Front;

        public event Action<ReplayConfidencePresentation> ConfidenceChanged;

        private void Awake()
        {
            userAvatar ??= gameObject.AddComponent<PoseAvatar3DPreview>();
            if (instructorAvatar == null)
            {
                var child = new GameObject("Instructor Squat Preview");
                child.transform.SetParent(transform, false);
                instructorAvatar = child.AddComponent<PoseAvatar3DPreview>();
            }
            userAvatar.SetViewport(new Vector2(250f, 250f), new Vector2(-400f, 24f));
            instructorAvatar.SetViewport(new Vector2(250f, 250f), new Vector2(-130f, 24f));
            SetViewpoint(viewpoint);
        }

        public void Render(float replaySeconds, JointTrackingFrame userFrame)
        {
            userAvatar.RenderFrame(userFrame);
            instructorAvatar.RenderFrame(InstructorSquatClip.Sample(replaySeconds));
            ConfidenceChanged?.Invoke(ReplayConfidenceEvaluator.Evaluate(userFrame));
        }

        public void SetViewpoint(ReplayViewpoint value)
        {
            viewpoint = value;
            userAvatar?.SetViewpoint(value);
            instructorAvatar?.SetViewpoint(value);
        }
    }
}
