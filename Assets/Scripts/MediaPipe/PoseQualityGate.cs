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
        public int visibleRequiredLandmarks;
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
        private readonly float averageVisibilityThreshold;

        public PoseQualityGate(float requiredVisibility, float averageVisibilityThreshold)
        {
            this.requiredVisibility = requiredVisibility;
            this.averageVisibilityThreshold = averageVisibilityThreshold;
        }

        public PoseQualityReport Evaluate(bool cameraRunning, LandmarkFrame frame)
        {
            if (!cameraRunning)
            {
                return Report(PoseQualityState.NoCamera, 0f, 0, "Camera is stopped.");
            }

            if (frame == null || !frame.HasPose)
            {
                return Report(PoseQualityState.NoPose, 0f, 0, "No pose landmarks detected.");
            }

            var averageVisibility = CalculateAverageVisibility(frame);
            var visibleRequired = CountVisibleRequired(frame);

            if (HasMissingGroup(frame, UpperBodyIds))
            {
                return Report(PoseQualityState.UpperBodyMissing, averageVisibility, visibleRequired, "Upper body landmarks are not stable.");
            }

            if (HasMissingGroup(frame, LowerBodyIds))
            {
                return Report(PoseQualityState.LowerBodyMissing, averageVisibility, visibleRequired, "Lower body landmarks are not stable.");
            }

            if (averageVisibility < averageVisibilityThreshold)
            {
                return Report(PoseQualityState.LowConfidence, averageVisibility, visibleRequired, "Average landmark visibility is low.");
            }

            return Report(PoseQualityState.Ready, averageVisibility, visibleRequired, "Ready.");
        }

        private PoseQualityReport Report(PoseQualityState state, float averageVisibility, int visibleRequired, string message)
        {
            return new PoseQualityReport
            {
                state = state,
                averageVisibility = averageVisibility,
                visibleRequiredLandmarks = visibleRequired,
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

        private bool HasMissingGroup(LandmarkFrame frame, int[] ids)
        {
            for (var i = 0; i < ids.Length; i++)
            {
                if (!IsVisible(frame, ids[i]))
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
    }
}
