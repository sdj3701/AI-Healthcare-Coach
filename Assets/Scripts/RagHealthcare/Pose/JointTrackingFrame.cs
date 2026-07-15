using System;
using UnityEngine;

namespace Rag.Healthcare.Pose
{
    [Serializable]
    public sealed class JointTrackingFrame
    {
        public string id;
        public string sessionId;
        public long timestampUnixMilliseconds;
        public TrackedJoint[] joints;
        public PoseFeedbackMessage[] feedback;

        public bool TryGetJoint(string jointName, out TrackedJoint joint)
        {
            joint = null;

            if (string.IsNullOrWhiteSpace(jointName) || joints == null)
            {
                return false;
            }

            // MediaPipe frames use a stable 33-landmark order. Take the constant-time
            // path when that contract is present, while preserving the legacy scan for
            // replay, remote, or test frames whose joints may be reordered.
            if (PoseJointNames.TryGetMediaPipeIndex(jointName, out var mediaPipeIndex) &&
                mediaPipeIndex >= 0 &&
                mediaPipeIndex < joints.Length)
            {
                var indexedCandidate = joints[mediaPipeIndex];
                if (indexedCandidate != null &&
                    string.Equals(indexedCandidate.name, jointName, StringComparison.OrdinalIgnoreCase))
                {
                    joint = indexedCandidate;
                    return true;
                }
            }

            foreach (var candidate in joints)
            {
                if (candidate != null && string.Equals(candidate.name, jointName, StringComparison.OrdinalIgnoreCase))
                {
                    joint = candidate;
                    return true;
                }
            }

            return false;
        }
    }

    [Serializable]
    public sealed class TrackedJoint
    {
        public string name;
        public float x;
        public float y;
        public float z;
        public float visibility;
        public float confidence;

        public Vector3 NormalizedPosition => new Vector3(x, y, z);
    }
}
