using System;
using System.Collections.Generic;
using AIHealthcareCoach.MediaPipe;
using Rag.Healthcare.Camera;
using Rag.Healthcare.Monetization;
using Rag.Healthcare.Performance;
using Rag.Healthcare.Pose.Calibration;
using Rag.Healthcare.Pose.Providers;
using Rag.Healthcare.Privacy;
using Rag.Healthcare.Qa;
using Rag.Healthcare.Rag.Rules;
using Rag.Healthcare.Replay;
using Rag.Healthcare.Tts;
using UnityEditor;
using UnityEngine;

namespace Rag.Healthcare.Editor
{
    public static class HealthcareQaSuite
    {
        [MenuItem("AI Healthcare/Run Deterministic QA Suite")]
        public static void RunMenu()
        {
            var failures = Run();
            EditorUtility.DisplayDialog("AI Healthcare QA", failures.Count == 0 ? "All deterministic QA checks passed." : string.Join("\n", failures), "OK");
        }

        public static void RunBatch()
        {
            var failures = Run();
            if (failures.Count > 0) throw new InvalidOperationException(string.Join("; ", failures));
            Debug.Log("AI_HEALTHCARE_QA_PASSED");
        }

        public static List<string> Run()
        {
            var failures = new List<string>();
            Check(SyntheticPoseFixtures.Standing().joints.Length == 33, "Synthetic fixture must contain 33 landmarks.", failures);

            var calibration = new PoseCalibrationService();
            for (var i = 0; i < 20; i++) calibration.AddFrame(SyntheticPoseFixtures.Standing());
            var profile = calibration.Build();
            Check(profile.valid, "Calibration profile should be valid after 20 frames.", failures);
            Check(PoseCoordinateNormalizer.Normalize(SyntheticPoseFixtures.Standing(), profile).Length == 33, "Normalized pose must contain 33 landmarks.", failures);
            Check(FloorReferenceEstimator.Estimate(SyntheticPoseFixtures.Standing()).valid, "Floor reference should be valid.", failures);

            var darkPixels = new Color32[100];
            var cameraReport = new CameraSetupAdvisor().Evaluate(SyntheticPoseFixtures.Standing(), darkPixels, 10, 10);
            Check(Array.IndexOf(cameraReport.issues, CameraSetupIssue.TooDark) >= 0, "Dark camera fixture should be detected.", failures);

            WorkoutNetworkGuard.BeginOfflineWorkout();
            Check(!WorkoutNetworkGuard.ValidateBackend(PoseTrackingBackend.RemoteApi, out _), "Remote backend must be blocked offline.", failures);
            WorkoutNetworkGuard.EndOfflineWorkout();

            Check(ExerciseRuleProfiles.ValidateReusableProfile(ExerciseRuleProfiles.Lunge, out _), "Lunge reuse profile must validate.", failures);
            Check(ReusableLowerBodyRuleEvaluator.Evaluate(SyntheticPoseFixtures.Standing(), ExerciseRuleProfiles.Lunge).valid, "Reusable lunge rule evaluator must execute.", failures);
            Check(ReplayConfidenceEvaluator.Evaluate(SyntheticPoseFixtures.LowConfidence()).blurAvatar, "Low-confidence replay must be blurred.", failures);
            Check(InstructorSquatClip.Sample(1f).joints.Length >= 12, "Instructor clip must produce a renderable pose.", failures);

            var entitlements = new EntitlementService();
            Check(entitlements.Has(ProductFeature.LiveSafetyFeedback), "Safety feedback must never require payment.", failures);

            Check(TtsBackendResolver.ResolveAuto(RuntimePlatform.WindowsEditor) == TtsBackend.WindowsPowerShell,
                "Windows Editor must resolve to the audible PowerShell TTS backend.", failures);
            Check(TtsBackendResolver.ResolveAuto(RuntimePlatform.Android) == TtsBackend.AndroidNative,
                "Android must resolve to the native Android TTS backend.", failures);
            Check(TtsBackendResolver.ResolveAuto(RuntimePlatform.IPhonePlayer) == TtsBackend.IosNative,
                "iOS must resolve to the native AVSpeechSynthesizer backend.", failures);

            var acceptance = PerformanceAcceptanceEvaluator.Evaluate(new PerformanceBenchmarkResult
            {
                durationSeconds = 600f, averagePoseFps = 15f, averageInferenceMs = 45f, droppedFrames = 5
            });
            Check(acceptance.passed, "Nominal performance fixture should pass.", failures);

            var mediaPipe = MediaPipeInstallationVerifier.Verify();
            Check(mediaPipe.success, mediaPipe.message, failures);
            foreach (var failure in failures) Debug.LogError("[QA] " + failure);
            return failures;
        }

        private static void Check(bool condition, string failure, ICollection<string> failures)
        {
            if (!condition) failures.Add(failure);
        }
    }
}
