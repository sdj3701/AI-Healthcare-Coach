using System;
using System.Collections;
using UnityEngine;

namespace Rag.Healthcare.Pose.Providers
{
    public interface IPoseTrackingProvider : IDisposable
    {
        bool IsReady { get; }

        IEnumerator Initialize();

        IEnumerator EstimatePose(
            Texture source,
            long timestampUnixMilliseconds,
            Action<JointTrackingFrame> onFrame,
            Action<string> onError);
    }
}
