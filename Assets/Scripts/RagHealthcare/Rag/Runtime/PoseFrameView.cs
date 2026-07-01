using System;
using System.Collections.Generic;
using Rag.Healthcare.Pose;

namespace Rag.Healthcare.Rag.Runtime
{
    public sealed class PoseFrameView
    {
        private readonly Dictionary<string, TrackedJoint> jointsByName =
            new Dictionary<string, TrackedJoint>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, TrackedJoint> validJointsByName =
            new Dictionary<string, TrackedJoint>(StringComparer.OrdinalIgnoreCase);

        public JointTrackingFrame RawFrame { get; private set; }
        public long TimestampUnixMilliseconds { get; private set; }
        public int JointCount => jointsByName.Count;
        public int ValidJointCount => validJointsByName.Count;

        public void Reset(JointTrackingFrame frame, float minimumVisibility)
        {
            RawFrame = frame;
            TimestampUnixMilliseconds = frame == null ? 0L : frame.timestampUnixMilliseconds;
            jointsByName.Clear();
            validJointsByName.Clear();

            if (frame == null || frame.joints == null)
            {
                return;
            }

            foreach (var joint in frame.joints)
            {
                if (joint == null || string.IsNullOrWhiteSpace(joint.name))
                {
                    continue;
                }

                jointsByName[joint.name] = joint;
                if (GetJointScore(joint) >= minimumVisibility)
                {
                    validJointsByName[joint.name] = joint;
                }
            }
        }

        public bool TryGetJoint(string jointName, out TrackedJoint joint, bool requireValid = true)
        {
            joint = null;
            if (string.IsNullOrWhiteSpace(jointName))
            {
                return false;
            }

            return requireValid
                ? validJointsByName.TryGetValue(jointName, out joint)
                : jointsByName.TryGetValue(jointName, out joint);
        }

        public static float GetJointScore(TrackedJoint joint)
        {
            if (joint == null)
            {
                return 0f;
            }

            if (joint.visibility > 0f && joint.confidence > 0f)
            {
                return Math.Min(joint.visibility, joint.confidence);
            }

            return Math.Max(joint.visibility, joint.confidence);
        }
    }
}
