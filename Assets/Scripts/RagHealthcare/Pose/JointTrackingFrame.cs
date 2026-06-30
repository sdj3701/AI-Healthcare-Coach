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
