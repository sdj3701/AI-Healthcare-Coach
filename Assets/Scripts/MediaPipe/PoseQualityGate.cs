using System;
using UnityEngine;

namespace AIHealthcareCoach.MediaPipe
{
    public enum PoseQualityState
    {
        NotStarted,
        NoCamera,
        NoPose,
        LowConfidence,
        UpperBodyMissing,
        LowerBodyMissing,
        Ready
    }

    [Serializable]
    public sealed class PoseQualityReport
    {
        public PoseQualityState state;
        public float averageVisibility;
        public float averagePresence;
        public int visibleRequiredLandmarks;
        public int trackableRequiredLandmarks;
        public int requiredLandmarkCount;
        public string message;

        public bool IsReady
        {
            get { return state == PoseQualityState.Ready; }
        }
    }

    public sealed class PoseQualityGate
    {
        private static readonly int[] UpperBodyIds = { 11, 12, 13, 14, 15, 16 };
        private static readonly int[] LowerBodyIds = { 23, 24, 25, 26, 27, 28 };
        private static readonly int[] RequiredIds = { 11, 12, 23, 24, 25, 26, 27, 28 };

        private readonly float requiredVisibility;
        private readonly float requiredPresence;
        private readonly float averageVisibilityThreshold;
        private readonly float edgeMargin;

        public PoseQualityGate(float requiredVisibility, float averageVisibilityThreshold)
            : this(requiredVisibility, requiredVisibility, averageVisibilityThreshold, 0.02f)
        {
        }

        public PoseQualityGate(
            float requiredVisibility,
            float requiredPresence,
            float averageVisibilityThreshold,
            float edgeMargin)
        {
            this.requiredVisibility = requiredVisibility;
            this.requiredPresence = requiredPresence;
            this.averageVisibilityThreshold = averageVisibilityThreshold;
            this.edgeMargin = Mathf.Clamp(edgeMargin, 0f, 0.2f);
        }

        public PoseQualityReport Evaluate(bool cameraRunning, LandmarkFrame frame)
        {
            if (!cameraRunning)
            {
                return Report(PoseQualityState.NoCamera, 0f, 0f, 0, 0, "Camera is stopped.");
            }

            if (frame == null || !frame.HasPose)
            {
                return Report(PoseQualityState.NoPose, 0f, 0f, 0, 0, "No pose landmarks detected.");
            }

            var averageVisibility = CalculateAverageVisibility(frame);
            var averagePresence = CalculateAveragePresence(frame);
            var visibleRequired = CountVisibleRequired(frame);
            var trackableRequired = CountTrackableRequired(frame);

            if (HasMissingGroup(frame, UpperBodyIds))
            {
                return Report(
                    PoseQualityState.UpperBodyMissing,
                    averageVisibility,
                    averagePresence,
                    visibleRequired,
                    trackableRequired,
                    "Upper body landmarks are not stable or are outside the camera frame.");
            }

            if (HasMissingGroup(frame, LowerBodyIds))
            {
                return Report(
                    PoseQualityState.LowerBodyMissing,
                    averageVisibility,
                    averagePresence,
                    visibleRequired,
                    trackableRequired,
                    "Lower body landmarks are not stable or are outside the camera frame.");
            }

            if (averageVisibility < averageVisibilityThreshold || averagePresence < averageVisibilityThreshold)
            {
                return Report(
                    PoseQualityState.LowConfidence,
                    averageVisibility,
                    averagePresence,
                    visibleRequired,
                    trackableRequired,
                    "Average landmark confidence is low.");
            }

            return Report(
                PoseQualityState.Ready,
                averageVisibility,
                averagePresence,
                visibleRequired,
                trackableRequired,
                "Ready.");
        }

        private PoseQualityReport Report(
            PoseQualityState state,
            float averageVisibility,
            float averagePresence,
            int visibleRequired,
            int trackableRequired,
            string message)
        {
            return new PoseQualityReport
            {
                state = state,
                averageVisibility = averageVisibility,
                averagePresence = averagePresence,
                visibleRequiredLandmarks = visibleRequired,
                trackableRequiredLandmarks = trackableRequired,
                requiredLandmarkCount = RequiredIds.Length,
                message = message
            };
        }

        private float CalculateAverageVisibility(LandmarkFrame frame)
        {
            if (frame.landmarks == null || frame.landmarks.Length == 0)
            {
                return 0f;
            }

            var total = 0f;
            for (var i = 0; i < frame.landmarks.Length; i++)
            {
                total += Mathf.Clamp01(frame.landmarks[i].visibility);
            }

            return total / frame.landmarks.Length;
        }

        private float CalculateAveragePresence(LandmarkFrame frame)
        {
            if (frame.landmarks == null || frame.landmarks.Length == 0)
            {
                return 0f;
            }

            var total = 0f;
            for (var i = 0; i < frame.landmarks.Length; i++)
            {
                total += Mathf.Clamp01(frame.landmarks[i].presence);
            }

            return total / frame.landmarks.Length;
        }

        private int CountVisibleRequired(LandmarkFrame frame)
        {
            var count = 0;
            for (var i = 0; i < RequiredIds.Length; i++)
            {
                if (IsVisible(frame, RequiredIds[i]))
                {
                    count++;
                }
            }

            return count;
        }

        private int CountTrackableRequired(LandmarkFrame frame)
        {
            var count = 0;
            for (var i = 0; i < RequiredIds.Length; i++)
            {
                if (IsTrackable(frame, RequiredIds[i]))
                {
                    count++;
                }
            }

            return count;
        }

        private bool HasMissingGroup(LandmarkFrame frame, int[] ids)
        {
            for (var i = 0; i < ids.Length; i++)
            {
                if (!IsTrackable(frame, ids[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsVisible(LandmarkFrame frame, int id)
        {
            return frame.landmarks != null
                   && id >= 0
                   && id < frame.landmarks.Length
                   && frame.landmarks[id].visibility >= requiredVisibility;
        }

        private bool IsTrackable(LandmarkFrame frame, int id)
        {
            return frame.landmarks != null
                   && id >= 0
                   && id < frame.landmarks.Length
                   && frame.landmarks[id].visibility >= requiredVisibility
                   && frame.landmarks[id].presence >= requiredPresence
                   && IsInsideFrame(frame.landmarks[id]);
        }

        private bool IsInsideFrame(PoseLandmark landmark)
        {
            return landmark.x >= edgeMargin
                   && landmark.x <= 1f - edgeMargin
                   && landmark.y >= edgeMargin
                   && landmark.y <= 1f - edgeMargin;
        }
    }
}
