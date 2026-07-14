using System;
using Rag.Healthcare.Pose.Providers;

namespace Rag.Healthcare.Privacy
{
    public static class WorkoutNetworkGuard
    {
        public static bool OfflineWorkoutActive { get; private set; }
        public static bool IsNetworkAllowed => !OfflineWorkoutActive;

        public static void BeginOfflineWorkout() => OfflineWorkoutActive = true;
        public static void EndOfflineWorkout() => OfflineWorkoutActive = false;

        public static bool ValidateBackend(PoseTrackingBackend backend, out string reason)
        {
            if (OfflineWorkoutActive && backend == PoseTrackingBackend.RemoteApi)
            {
                reason = "RemoteApi backend is prohibited while offline workout privacy mode is active.";
                return false;
            }
            reason = string.Empty;
            return true;
        }
    }
}
