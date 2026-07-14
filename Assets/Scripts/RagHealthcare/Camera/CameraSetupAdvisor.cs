using System;
using System.Collections.Generic;
using Rag.Healthcare.Pose;
using UnityEngine;

namespace Rag.Healthcare.Camera
{
    public enum CameraSetupIssue
    {
        None,
        TooDark,
        TooBright,
        TooFar,
        TooClose,
        BodyCropped,
        CameraTilted,
        LowConfidence
    }

    [Serializable]
    public sealed class CameraSetupReport
    {
        public bool ready;
        public float brightness;
        public float bodyHeightRatio;
        public float averageConfidence;
        public CameraSetupIssue[] issues;
        public string message;
    }

    public sealed class CameraSetupAdvisor
    {
        public CameraSetupReport Evaluate(JointTrackingFrame frame, Color32[] pixels, int width, int height)
        {
            var issues = new List<CameraSetupIssue>();
            var brightness = EstimateBrightness(pixels, width, height);
            if (brightness >= 0f && brightness < 0.18f) issues.Add(CameraSetupIssue.TooDark);
            if (brightness > 0.92f) issues.Add(CameraSetupIssue.TooBright);

            var bodyHeight = 0f;
            var confidence = 0f;
            var observed = 0;
            if (frame?.joints != null && frame.joints.Length > 0)
            {
                var minX = 1f;
                var maxX = 0f;
                var minY = 1f;
                var maxY = 0f;
                foreach (var joint in frame.joints)
                {
                    if (joint == null) continue;
                    minX = Mathf.Min(minX, joint.x);
                    maxX = Mathf.Max(maxX, joint.x);
                    minY = Mathf.Min(minY, joint.y);
                    maxY = Mathf.Max(maxY, joint.y);
                    confidence += Mathf.Clamp01(Mathf.Max(joint.visibility, joint.confidence));
                    observed++;
                }

                bodyHeight = maxY - minY;
                if (bodyHeight < 0.45f) issues.Add(CameraSetupIssue.TooFar);
                if (bodyHeight > 0.94f) issues.Add(CameraSetupIssue.TooClose);
                if (minX < 0.02f || maxX > 0.98f || minY < 0.02f || maxY > 0.98f) issues.Add(CameraSetupIssue.BodyCropped);
            }
            else
            {
                issues.Add(CameraSetupIssue.LowConfidence);
            }

            confidence = observed == 0 ? 0f : confidence / observed;
            if (confidence < 0.5f && !issues.Contains(CameraSetupIssue.LowConfidence)) issues.Add(CameraSetupIssue.LowConfidence);

            if (TryShoulderTilt(frame, out var shoulderTilt) && shoulderTilt > 0.10f)
            {
                issues.Add(CameraSetupIssue.CameraTilted);
            }

            return new CameraSetupReport
            {
                ready = issues.Count == 0,
                brightness = brightness,
                bodyHeightRatio = bodyHeight,
                averageConfidence = confidence,
                issues = issues.ToArray(),
                message = BuildMessage(issues)
            };
        }

        private static float EstimateBrightness(Color32[] pixels, int width, int height)
        {
            if (pixels == null || width <= 0 || height <= 0 || pixels.Length < width * height) return -1f;
            var stride = Mathf.Max(1, pixels.Length / 256);
            var total = 0f;
            var count = 0;
            for (var i = 0; i < pixels.Length; i += stride)
            {
                var pixel = pixels[i];
                total += (0.2126f * pixel.r + 0.7152f * pixel.g + 0.0722f * pixel.b) / 255f;
                count++;
            }
            return count == 0 ? -1f : total / count;
        }

        private static bool TryShoulderTilt(JointTrackingFrame frame, out float tilt)
        {
            tilt = 0f;
            if (frame == null ||
                !frame.TryGetJoint(PoseJointNames.LeftShoulder, out var left) ||
                !frame.TryGetJoint(PoseJointNames.RightShoulder, out var right)) return false;
            tilt = Mathf.Abs(left.y - right.y) / Mathf.Max(0.05f, Mathf.Abs(left.x - right.x));
            return true;
        }

        private static string BuildMessage(List<CameraSetupIssue> issues)
        {
            if (issues.Count == 0) return "카메라 설정이 준비되었습니다.";
            return issues[0] switch
            {
                CameraSetupIssue.TooDark => "조명을 밝게 하거나 빛이 얼굴 정면에서 오도록 조정하세요.",
                CameraSetupIssue.TooBright => "강한 역광을 피하고 카메라 방향을 조정하세요.",
                CameraSetupIssue.TooFar => "전신이 보이는 범위에서 카메라에 조금 더 가까이 서세요.",
                CameraSetupIssue.TooClose => "발끝까지 보이도록 카메라에서 조금 더 멀리 서세요.",
                CameraSetupIssue.BodyCropped => "머리와 발끝이 화면 안에 들어오도록 위치를 조정하세요.",
                CameraSetupIssue.CameraTilted => "카메라를 바닥과 수평으로 맞추세요.",
                _ => "전신이 선명하게 인식될 때까지 위치와 조명을 조정하세요."
            };
        }
    }
}
