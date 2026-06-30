using System.Collections.Generic;
using UnityEngine;

namespace AIHealthcareCoach.MediaPipe
{
    public sealed class PoseExerciseFeedbackAnalyzer
    {
        private const int LeftShoulder = 11;
        private const int RightShoulder = 12;
        private const int LeftHip = 23;
        private const int RightHip = 24;
        private const int LeftKnee = 25;
        private const int RightKnee = 26;
        private const int LeftAnkle = 27;
        private const int RightAnkle = 28;
        private const int LeftHeel = 29;
        private const int RightHeel = 30;
        private const int LeftFootIndex = 31;
        private const int RightFootIndex = 32;

        private readonly Dictionary<string, float> lastFeedbackTimes = new Dictionary<string, float>();

        public float minimumConfidence = 0.5f;
        public float maximumKneeValgusOffset = 0.08f;
        public float minimumSquatKneeAngle = 70f;
        public float maximumSquatKneeAngle = 165f;
        public float maximumLeftRightKneeAngleDelta = 18f;
        public float maximumTorsoTiltDegrees = 35f;
        public float maximumHipLevelDelta = 0.08f;
        public float maximumShoulderLevelDelta = 0.08f;
        public float maximumCenterBalanceOffset = 0.12f;
        public float feedbackCooldownSeconds = 2f;

        public int Analyze(LandmarkFrame frame, IList<PoseExerciseFeedbackMessage> results)
        {
            if (frame == null || frame.landmarks == null || results == null)
            {
                return 0;
            }

            var initialCount = results.Count;
            AnalyzeKnee(frame, "left", LeftHip, LeftKnee, LeftAnkle, results);
            AnalyzeKnee(frame, "right", RightHip, RightKnee, RightAnkle, results);
            AnalyzeKneeSymmetry(frame, results);
            AnalyzeTorsoTilt(frame, results);
            AnalyzeHipLevel(frame, results);
            AnalyzeShoulderLevel(frame, results);
            AnalyzeCenterBalance(frame, results);
            AnalyzeFootVisibility(frame, "left", LeftAnkle, LeftHeel, LeftFootIndex, results);
            AnalyzeFootVisibility(frame, "right", RightAnkle, RightHeel, RightFootIndex, results);
            return results.Count - initialCount;
        }

        private void AnalyzeKnee(
            LandmarkFrame frame,
            string side,
            int hipId,
            int kneeId,
            int ankleId,
            IList<PoseExerciseFeedbackMessage> results)
        {
            if (!TryGetTriplet(frame, hipId, kneeId, ankleId, out var hip, out var knee, out var ankle, out var confidence))
            {
                return;
            }

            var kneeAngle = Angle(hip, knee, ankle);
            var offset = DistancePointToLine(knee, hip, ankle);

            if (offset > maximumKneeValgusOffset)
            {
                AddFeedback(
                    results,
                    side + "_knee_alignment",
                    side + " knee is drifting away from the hip-ankle line.",
                    kneeId,
                    PoseExerciseFeedbackSeverity.Warning,
                    confidence);
            }

            if (kneeAngle > maximumSquatKneeAngle)
            {
                AddFeedback(
                    results,
                    side + "_knee_bend_low",
                    side + " knee bend is shallow. Bend the knee a little more.",
                    kneeId,
                    PoseExerciseFeedbackSeverity.Info,
                    confidence);
            }
            else if (kneeAngle < minimumSquatKneeAngle)
            {
                AddFeedback(
                    results,
                    side + "_knee_bend_deep",
                    side + " knee bend is too deep. Reduce the depth slightly.",
                    kneeId,
                    PoseExerciseFeedbackSeverity.Warning,
                    confidence);
            }
        }

        private void AnalyzeKneeSymmetry(LandmarkFrame frame, IList<PoseExerciseFeedbackMessage> results)
        {
            if (!TryGetTriplet(frame, LeftHip, LeftKnee, LeftAnkle, out var leftHip, out var leftKnee, out var leftAnkle, out var leftConfidence) ||
                !TryGetTriplet(frame, RightHip, RightKnee, RightAnkle, out var rightHip, out var rightKnee, out var rightAnkle, out var rightConfidence))
            {
                return;
            }

            var leftAngle = Angle(leftHip, leftKnee, leftAnkle);
            var rightAngle = Angle(rightHip, rightKnee, rightAnkle);
            if (Mathf.Abs(leftAngle - rightAngle) <= maximumLeftRightKneeAngleDelta)
            {
                return;
            }

            AddFeedback(
                results,
                "knee_symmetry",
                "Left and right knee bend are uneven.",
                LeftKnee,
                PoseExerciseFeedbackSeverity.Info,
                Mathf.Min(leftConfidence, rightConfidence));
        }

        private void AnalyzeTorsoTilt(LandmarkFrame frame, IList<PoseExerciseFeedbackMessage> results)
        {
            if (!TryGetPoint(frame, LeftShoulder, out var leftShoulder, out var leftShoulderConfidence) ||
                !TryGetPoint(frame, RightShoulder, out var rightShoulder, out var rightShoulderConfidence) ||
                !TryGetPoint(frame, LeftHip, out var leftHip, out var leftHipConfidence) ||
                !TryGetPoint(frame, RightHip, out var rightHip, out var rightHipConfidence))
            {
                return;
            }

            var shoulderCenter = Midpoint(leftShoulder, rightShoulder);
            var hipCenter = Midpoint(leftHip, rightHip);
            var torso = shoulderCenter - hipCenter;
            if (torso.sqrMagnitude <= Mathf.Epsilon)
            {
                return;
            }

            var tilt = Vector2.Angle(torso, Vector2.down);
            if (tilt <= maximumTorsoTiltDegrees)
            {
                return;
            }

            var confidence = Mathf.Min(
                Mathf.Min(leftShoulderConfidence, rightShoulderConfidence),
                Mathf.Min(leftHipConfidence, rightHipConfidence));

            AddFeedback(
                results,
                "torso_tilt",
                "Torso is leaning too far. Keep shoulders stacked over the hips.",
                LeftShoulder,
                PoseExerciseFeedbackSeverity.Warning,
                confidence);
        }

        private void AnalyzeHipLevel(LandmarkFrame frame, IList<PoseExerciseFeedbackMessage> results)
        {
            if (!TryGetPoint(frame, LeftHip, out var leftHip, out var leftConfidence) ||
                !TryGetPoint(frame, RightHip, out var rightHip, out var rightConfidence))
            {
                return;
            }

            if (Mathf.Abs(leftHip.y - rightHip.y) <= maximumHipLevelDelta)
            {
                return;
            }

            AddFeedback(
                results,
                "hip_level",
                "Hip level is tilted. Keep both hips closer to level.",
                LeftHip,
                PoseExerciseFeedbackSeverity.Warning,
                Mathf.Min(leftConfidence, rightConfidence));
        }

        private void AnalyzeShoulderLevel(LandmarkFrame frame, IList<PoseExerciseFeedbackMessage> results)
        {
            if (!TryGetPoint(frame, LeftShoulder, out var leftShoulder, out var leftConfidence) ||
                !TryGetPoint(frame, RightShoulder, out var rightShoulder, out var rightConfidence))
            {
                return;
            }

            if (Mathf.Abs(leftShoulder.y - rightShoulder.y) <= maximumShoulderLevelDelta)
            {
                return;
            }

            AddFeedback(
                results,
                "shoulder_level",
                "Shoulders are tilted. Face forward and keep both shoulders level.",
                LeftShoulder,
                PoseExerciseFeedbackSeverity.Info,
                Mathf.Min(leftConfidence, rightConfidence));
        }

        private void AnalyzeCenterBalance(LandmarkFrame frame, IList<PoseExerciseFeedbackMessage> results)
        {
            if (!TryGetPoint(frame, LeftHip, out var leftHip, out var leftHipConfidence) ||
                !TryGetPoint(frame, RightHip, out var rightHip, out var rightHipConfidence) ||
                !TryGetPoint(frame, LeftAnkle, out var leftAnkle, out var leftAnkleConfidence) ||
                !TryGetPoint(frame, RightAnkle, out var rightAnkle, out var rightAnkleConfidence))
            {
                return;
            }

            var hipCenter = Midpoint(leftHip, rightHip);
            var ankleCenter = Midpoint(leftAnkle, rightAnkle);
            if (Mathf.Abs(hipCenter.x - ankleCenter.x) <= maximumCenterBalanceOffset)
            {
                return;
            }

            var confidence = Mathf.Min(
                Mathf.Min(leftHipConfidence, rightHipConfidence),
                Mathf.Min(leftAnkleConfidence, rightAnkleConfidence));

            AddFeedback(
                results,
                "center_balance",
                "Body center is drifting away from the feet. Bring weight back to the middle.",
                LeftHip,
                PoseExerciseFeedbackSeverity.Warning,
                confidence);
        }

        private void AnalyzeFootVisibility(
            LandmarkFrame frame,
            string side,
            int ankleId,
            int heelId,
            int footIndexId,
            IList<PoseExerciseFeedbackMessage> results)
        {
            if (!TryGetPoint(frame, ankleId, out _, out var ankleConfidence))
            {
                return;
            }

            var hasHeel = TryGetPoint(frame, heelId, out _, out _);
            var hasFootIndex = TryGetPoint(frame, footIndexId, out _, out _);
            if (hasHeel && hasFootIndex)
            {
                return;
            }

            AddFeedback(
                results,
                side + "_foot_visibility",
                side + " foot is not visible enough for stable posture analysis.",
                ankleId,
                PoseExerciseFeedbackSeverity.Info,
                ankleConfidence);
        }

        private bool TryGetTriplet(
            LandmarkFrame frame,
            int firstId,
            int centerId,
            int thirdId,
            out Vector2 first,
            out Vector2 center,
            out Vector2 third,
            out float confidence)
        {
            var hasFirst = TryGetPoint(frame, firstId, out first, out var firstConfidence);
            var hasCenter = TryGetPoint(frame, centerId, out center, out var centerConfidence);
            var hasThird = TryGetPoint(frame, thirdId, out third, out var thirdConfidence);
            confidence = Mathf.Min(firstConfidence, Mathf.Min(centerConfidence, thirdConfidence));
            return hasFirst && hasCenter && hasThird;
        }

        private bool TryGetPoint(LandmarkFrame frame, int id, out Vector2 point, out float confidence)
        {
            point = default;
            confidence = 0f;

            if (frame == null || frame.landmarks == null || id < 0 || id >= frame.landmarks.Length)
            {
                return false;
            }

            var landmark = frame.landmarks[id];
            confidence = Mathf.Min(Mathf.Clamp01(landmark.visibility), Mathf.Clamp01(landmark.presence));
            if (confidence < minimumConfidence)
            {
                return false;
            }

            point = new Vector2(landmark.x, landmark.y);
            return true;
        }

        private void AddFeedback(
            IList<PoseExerciseFeedbackMessage> results,
            string id,
            string text,
            int jointId,
            PoseExerciseFeedbackSeverity severity,
            float confidence)
        {
            if (IsCoolingDown(id))
            {
                return;
            }

            lastFeedbackTimes[id] = Time.unscaledTime;
            results.Add(new PoseExerciseFeedbackMessage
            {
                id = id,
                text = text,
                jointId = jointId,
                jointName = PoseLandmarkNames.GetName(jointId),
                confidence = Mathf.Clamp01(confidence),
                severity = severity
            });
        }

        private bool IsCoolingDown(string id)
        {
            return lastFeedbackTimes.TryGetValue(id, out var lastTime) &&
                   Time.unscaledTime - lastTime < feedbackCooldownSeconds;
        }

        private static Vector2 Midpoint(Vector2 a, Vector2 b)
        {
            return (a + b) * 0.5f;
        }

        private static float Angle(Vector2 a, Vector2 center, Vector2 b)
        {
            var first = a - center;
            var second = b - center;
            if (first.sqrMagnitude <= Mathf.Epsilon || second.sqrMagnitude <= Mathf.Epsilon)
            {
                return 0f;
            }

            return Vector2.Angle(first, second);
        }

        private static float DistancePointToLine(Vector2 point, Vector2 lineStart, Vector2 lineEnd)
        {
            var line = lineEnd - lineStart;
            if (line.sqrMagnitude <= Mathf.Epsilon)
            {
                return Vector2.Distance(point, lineStart);
            }

            return Mathf.Abs((line.x * (lineStart.y - point.y)) - ((lineStart.x - point.x) * line.y)) /
                   Mathf.Sqrt(line.sqrMagnitude);
        }
    }
}
