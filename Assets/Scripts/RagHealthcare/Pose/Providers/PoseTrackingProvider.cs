using System.Collections;
using UnityEngine;

namespace Rag.Healthcare.Pose.Providers
{
    public abstract class PoseTrackingProvider : MonoBehaviour, IPoseTrackingProvider
    {
        public abstract PoseTrackingBackend Backend { get; }
        public abstract bool IsReady { get; }
        public string LastError { get; protected set; } = string.Empty;

        public abstract IEnumerator Initialize();

        public abstract IEnumerator EstimatePose(
            Texture source,
            long timestampUnixMilliseconds,
            System.Action<JointTrackingFrame> onFrame,
            System.Action<string> onError);

        public virtual void Dispose()
        {
        }

        protected void SetFailure(string message)
        {
            LastError = message;
            Debug.LogWarning($"[{GetType().Name}] {message}");
        }
    }
}
