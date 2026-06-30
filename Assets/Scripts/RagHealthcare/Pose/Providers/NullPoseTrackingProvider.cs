using System;
using System.Collections;
using UnityEngine;

namespace Rag.Healthcare.Pose.Providers
{
    public sealed class NullPoseTrackingProvider : PoseTrackingProvider
    {
        public override PoseTrackingBackend Backend => PoseTrackingBackend.Disabled;
        public override bool IsReady => false;

        public override IEnumerator Initialize()
        {
            SetFailure("Pose tracking is disabled.");
            yield break;
        }

        public override IEnumerator EstimatePose(
            Texture source,
            long timestampUnixMilliseconds,
            Action<JointTrackingFrame> onFrame,
            Action<string> onError)
        {
            onError?.Invoke("Pose tracking is disabled.");
            yield break;
        }
    }
}
