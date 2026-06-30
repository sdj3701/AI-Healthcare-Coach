using UnityEngine;

namespace Rag.Healthcare.Pose.Analysis
{
    public static class PoseGeometry
    {
        public static bool TryGetJoint2D(
            JointTrackingFrame frame,
            string jointName,
            float minimumVisibility,
            out Vector2 position,
            out TrackedJoint joint)
        {
            position = default;
            joint = null;

            if (frame == null || !frame.TryGetJoint(jointName, out joint))
            {
                return false;
            }

            if (GetJointScore(joint) < minimumVisibility)
            {
                return false;
            }

            position = new Vector2(joint.x, joint.y);
            return true;
        }

        public static float GetJointScore(TrackedJoint joint)
        {
            if (joint == null)
            {
                return 0f;
            }

            if (joint.visibility > 0f && joint.confidence > 0f)
            {
                return Mathf.Min(joint.visibility, joint.confidence);
            }

            return Mathf.Max(joint.visibility, joint.confidence);
        }

        public static float Angle(Vector2 a, Vector2 center, Vector2 b)
        {
            var first = a - center;
            var second = b - center;

            if (first.sqrMagnitude <= Mathf.Epsilon || second.sqrMagnitude <= Mathf.Epsilon)
            {
                return 0f;
            }

            return Vector2.Angle(first, second);
        }

        public static float DistancePointToLine(Vector2 point, Vector2 lineStart, Vector2 lineEnd)
        {
            var line = lineEnd - lineStart;
            if (line.sqrMagnitude <= Mathf.Epsilon)
            {
                return Vector2.Distance(point, lineStart);
            }

            return Mathf.Abs((line.x * (lineStart.y - point.y)) - ((lineStart.x - point.x) * line.y)) /
                   Mathf.Sqrt(line.sqrMagnitude);
        }

        public static Vector2 Midpoint(Vector2 a, Vector2 b)
        {
            return (a + b) * 0.5f;
        }
    }
}
