using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rag.Healthcare.Pose.Calibration
{
    [Serializable]
    public sealed class PoseCalibrationProfile
    {
        public string createdAtUtc;
        public int sampleCount;
        public float centerX;
        public float floorY;
        public float bodyScale;
        public float shoulderWidth;
        public float hipWidth;
        public bool valid;
    }

    public sealed class PoseCalibrationService
    {
        private readonly List<JointTrackingFrame> samples = new List<JointTrackingFrame>();

        public int SampleCount => samples.Count;

        public void Reset()
        {
            samples.Clear();
        }

        public bool AddFrame(JointTrackingFrame frame, float minimumConfidence = 0.55f)
        {
            if (!TryMetrics(frame, minimumConfidence, out _, out _, out _, out _, out _)) return false;
            samples.Add(frame);
            return true;
        }

        public PoseCalibrationProfile Build(int minimumSamples = 20)
        {
            var centers = new List<float>();
            var floors = new List<float>();
            var scales = new List<float>();
            var shoulders = new List<float>();
            var hips = new List<float>();

            foreach (var frame in samples)
            {
                if (!TryMetrics(frame, 0.55f, out var center, out var floor, out var scale, out var shoulder, out var hip)) continue;
                centers.Add(center);
                floors.Add(floor);
                scales.Add(scale);
                shoulders.Add(shoulder);
                hips.Add(hip);
            }

            return new PoseCalibrationProfile
            {
                createdAtUtc = DateTime.UtcNow.ToString("o"),
                sampleCount = centers.Count,
                centerX = Median(centers),
                floorY = Median(floors),
                bodyScale = Median(scales),
                shoulderWidth = Median(shoulders),
                hipWidth = Median(hips),
                valid = centers.Count >= Mathf.Max(1, minimumSamples) && Median(scales) > 0.2f
            };
        }

        private static bool TryMetrics(JointTrackingFrame frame, float minimumConfidence, out float center, out float floor, out float scale, out float shoulderWidth, out float hipWidth)
        {
            center = floor = scale = shoulderWidth = hipWidth = 0f;
            if (!TryJoint(frame, PoseJointNames.LeftShoulder, minimumConfidence, out var leftShoulder) ||
                !TryJoint(frame, PoseJointNames.RightShoulder, minimumConfidence, out var rightShoulder) ||
                !TryJoint(frame, PoseJointNames.LeftHip, minimumConfidence, out var leftHip) ||
                !TryJoint(frame, PoseJointNames.RightHip, minimumConfidence, out var rightHip) ||
                !TryJoint(frame, PoseJointNames.LeftAnkle, minimumConfidence, out var leftAnkle) ||
                !TryJoint(frame, PoseJointNames.RightAnkle, minimumConfidence, out var rightAnkle)) return false;

            center = (leftHip.x + rightHip.x) * 0.5f;
            floor = Mathf.Max(leftAnkle.y, rightAnkle.y);
            var shoulderY = (leftShoulder.y + rightShoulder.y) * 0.5f;
            scale = Mathf.Abs(floor - shoulderY);
            shoulderWidth = Vector2.Distance(new Vector2(leftShoulder.x, leftShoulder.y), new Vector2(rightShoulder.x, rightShoulder.y));
            hipWidth = Vector2.Distance(new Vector2(leftHip.x, leftHip.y), new Vector2(rightHip.x, rightHip.y));
            return scale > 0.05f;
        }

        private static bool TryJoint(JointTrackingFrame frame, string name, float minimumConfidence, out TrackedJoint joint)
        {
            joint = null;
            return frame != null && frame.TryGetJoint(name, out joint) && Mathf.Max(joint.visibility, joint.confidence) >= minimumConfidence;
        }

        private static float Median(List<float> values)
        {
            if (values.Count == 0) return 0f;
            values.Sort();
            var middle = values.Count / 2;
            return values.Count % 2 == 0 ? (values[middle - 1] + values[middle]) * 0.5f : values[middle];
        }
    }

    [Serializable]
    public sealed class NormalizedJointSample
    {
        public string name;
        public float x;
        public float y;
        public float z;
        public float confidence;
    }

    public static class PoseCoordinateNormalizer
    {
        public static NormalizedJointSample[] Normalize(JointTrackingFrame frame, PoseCalibrationProfile profile)
        {
            if (frame?.joints == null || profile == null || !profile.valid) return Array.Empty<NormalizedJointSample>();
            var scale = Mathf.Max(0.05f, profile.bodyScale);
            var result = new NormalizedJointSample[frame.joints.Length];
            for (var i = 0; i < frame.joints.Length; i++)
            {
                var joint = frame.joints[i];
                result[i] = new NormalizedJointSample
                {
                    name = joint.name,
                    x = (joint.x - profile.centerX) / scale,
                    y = (profile.floorY - joint.y) / scale,
                    z = joint.z / scale,
                    confidence = Mathf.Clamp01(Mathf.Max(joint.visibility, joint.confidence))
                };
            }
            return result;
        }
    }
}
