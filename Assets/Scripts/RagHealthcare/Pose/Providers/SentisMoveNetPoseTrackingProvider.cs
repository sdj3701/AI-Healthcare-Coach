using System;
using System.Collections;
using UnityEngine;

namespace Rag.Healthcare.Pose.Providers
{
    public sealed class SentisMoveNetPoseTrackingProvider : PoseTrackingProvider
    {
        public override PoseTrackingBackend Backend => PoseTrackingBackend.LocalSentisMoveNet;
        public override bool IsReady => false;

        public override IEnumerator Initialize()
        {
            SetFailure("Sentis MoveNet provider is not implemented yet.");
            yield break;
        }

        public override IEnumerator EstimatePose(
            Texture source,
            long timestampUnixMilliseconds,
            Action<JointTrackingFrame> onFrame,
            Action<string> onError)
        {
            onError?.Invoke("Sentis MoveNet provider is not implemented yet.");
            yield break;
        }
    }
}
