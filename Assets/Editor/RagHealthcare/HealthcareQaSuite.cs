using System;
using System.Collections.Generic;
using System.Reflection;
using AIHealthcareCoach.MediaPipe;
using AIHealthcareCoach.Editor;
using Rag.Healthcare.Camera;
using Rag.Healthcare.Monetization;
using Rag.Healthcare.Performance;
using Rag.Healthcare.Pose;
using Rag.Healthcare.Pose.Calibration;
using Rag.Healthcare.Pose.Providers;
using Rag.Healthcare.Pose.Session;
using Rag.Healthcare.Privacy;
using Rag.Healthcare.Product;
using Rag.Healthcare.Qa;
using Rag.Healthcare.Rag.Composition;
using Rag.Healthcare.Rag.Knowledge;
using Rag.Healthcare.Rag.Runtime;
using Rag.Healthcare.Rag.Rules;
using Rag.Healthcare.Replay;
using Rag.Healthcare.Reports;
using Rag.Healthcare.Tts;
using Rag.Healthcare.UI;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rag.Healthcare.Editor
{
    public static class HealthcareQaSuite
    {
        [MenuItem("AI Healthcare/Run Deterministic QA Suite")]
        public static void RunMenu()
        {
            var failures = Run();
            var passed = failures.Count == 0;
            if (passed)
            {
                Debug.Log("AI_HEALTHCARE_QA_PASSED");
            }
            else
            {
                Debug.LogError(
                    "AI_HEALTHCARE_QA_FAILED: " +
                    string.Join("; ", failures));
            }

            EditorUtility.DisplayDialog(
                "AI Healthcare QA",
                passed
                    ? "All deterministic QA checks passed."
                    : string.Join("\n", failures),
                "OK");
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

            VerifyLandmarkStability(failures);
            VerifyFrontCameraTrackingQuality(failures);
            VerifyWorkoutSessionLifecycle(failures);
            VerifyIosDevelopmentLaunchScheme(failures);
            VerifyPersonalizedRomSafety(failures);
            VerifyProfileCompletionGate(failures);
            VerifyHotPathObjectReuse(failures);
            VerifyPhaseReversalRecognition(failures);
            VerifyAdaptiveSquatDepthFloor(failures);
            VerifyJointCoordinateSquatPipeline(failures);
            VerifyDepthUsesMinimumAngle(failures);
            VerifyHipToKneeDepthFloorFeedback(failures);
            VerifySequentialBottomDecision(failures);
            VerifySessionDepthPersonalization(failures);
            VerifyBottomReachedSuppressesShallowWarning(failures);
            VerifyDeepSquatCountsAsCorrect(failures);
            VerifyKneeAlignmentPhaseGateAndSeverity(failures);
            VerifyTorsoAndPelvicGeometry(failures);
            VerifyAnalysisWindowUsesTimestamps(failures);
            VerifyTemporalRepQuality(failures);

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
            Check(!entitlements.Has(ProductFeature.ExpertContent),
                "Expert stretching content must remain locked before payment.", failures);
            entitlements.Grant(ProductFeature.ExpertContent);
            Check(entitlements.Has(ProductFeature.ExpertContent),
                "Payment entitlement must unlock expert stretching content.", failures);

            Check(TtsBackendResolver.ResolveAuto(RuntimePlatform.WindowsEditor) == TtsBackend.WindowsPowerShell,
                "Windows Editor must resolve to the audible PowerShell TTS backend.", failures);
            Check(TtsBackendResolver.ResolveAuto(RuntimePlatform.Android) == TtsBackend.AndroidNative,
                "Android must resolve to the native Android TTS backend.", failures);
            Check(TtsBackendResolver.ResolveAuto(RuntimePlatform.IPhonePlayer) == TtsBackend.IosNative,
                "iOS must resolve to the native AVSpeechSynthesizer backend.", failures);
            VerifyTtsScheduling(failures);

            VerifyBundledKoreanFont(failures);
            VerifyMobileUiStructure(failures);
            VerifyPosePreviewCoordinateMapping(failures);
            VerifyVisibilityGating(failures);
            VerifySafetyConstrainedPrompt(failures);

            var acceptance = PerformanceAcceptanceEvaluator.Evaluate(new PerformanceBenchmarkResult
            {
                durationSeconds = 600f, averagePoseFps = 15f, averageInferenceMs = 45f, droppedFrames = 5
            });
            Check(acceptance.passed, "Nominal performance fixture should pass.", failures);

            var shortSmoke = PerformanceAcceptanceEvaluator.Evaluate(new PerformanceBenchmarkResult
            {
                durationSeconds = 60f, averagePoseFps = 15f, averageInferenceMs = 45f, droppedFrames = 0
            });
            Check(!shortSmoke.passed, "60s-style short result must not pass 10m acceptance.", failures);
            Check(shortSmoke.failures != null &&
                  Array.Exists(shortSmoke.failures, reason => reason.IndexOf("10-minute", StringComparison.OrdinalIgnoreCase) >= 0),
                "60s-style short result must fail on duration.", failures);

            var mediaPipe = MediaPipeInstallationVerifier.Verify();
            Check(mediaPipe.success, mediaPipe.message, failures);
            foreach (var failure in failures) Debug.LogError("[QA] " + failure);
            return failures;
        }

        private static void VerifySafetyConstrainedPrompt(ICollection<string> failures)
        {
            const string leakedFeedbackId = "FREE_TEXT_LEAK_MARKER_do_not_inject";
            var summary = new PoseSessionSummary
            {
                sessionId = "session-leak-check",
                exercise = "squat-free-text-must-not-appear",
                durationSeconds = 120.5f,
                feedbackCount = 4,
                warningFeedbackCount = 2,
                criticalFeedbackCount = 1,
                averagePoseFps = 15.25f,
                averageVisibility = 0.854f,
                topFeedbackIds = new[] { leakedFeedbackId }
            };

            string first;
            try
            {
                first = SafetyConstrainedPromptTemplate.Build(summary);
            }
            catch (Exception ex)
            {
                Check(false, "SafetyConstrainedPromptTemplate.Build must not throw for a valid summary: " + ex.Message, failures);
                return;
            }

            var second = SafetyConstrainedPromptTemplate.Build(summary);
            Check(first == second, "SafetyConstrainedPromptTemplate.Build must be deterministic for the same summary.", failures);

            var nullRejected = false;
            try
            {
                SafetyConstrainedPromptTemplate.Build(null);
            }
            catch (ArgumentNullException)
            {
                nullRejected = true;
            }

            Check(nullRejected, "SafetyConstrainedPromptTemplate.Build must reject a null summary.", failures);
            Check(!string.IsNullOrEmpty(SafetyConstrainedPromptTemplate.SafetyDirectives),
                "SafetyDirectives must be a non-empty public constant.", failures);

            Check(first.IndexOf("diagnose", StringComparison.OrdinalIgnoreCase) >= 0 &&
                  first.IndexOf("prescribe", StringComparison.OrdinalIgnoreCase) >= 0 &&
                  first.IndexOf("diseases", StringComparison.OrdinalIgnoreCase) >= 0 &&
                  first.IndexOf("invent facts", StringComparison.OrdinalIgnoreCase) >= 0 &&
                  first.IndexOf("Korean", StringComparison.OrdinalIgnoreCase) >= 0,
                "Prompt must include safety constraint keywords (diagnose/prescribe/diseases/invent/Korean).", failures);

            Check(first.Contains("duration=120.5s") &&
                  first.Contains("feedback=4") &&
                  first.Contains("warnings=2") &&
                  first.Contains("critical=1") &&
                  first.Contains("pose_fps=15.3") &&
                  first.Contains("visibility=0.85"),
                "Prompt must include InvariantCulture whitelist numeric tokens.", failures);

            Check(first.IndexOf(leakedFeedbackId, StringComparison.Ordinal) < 0 &&
                  first.IndexOf(summary.sessionId, StringComparison.Ordinal) < 0 &&
                  first.IndexOf(summary.exercise, StringComparison.Ordinal) < 0,
                "Prompt must not leak free-text fields such as topFeedbackIds, sessionId, or exercise.", failures);
        }

        private static void VerifyIosDevelopmentLaunchScheme(
            ICollection<string> failures)
        {
            const string original =
                "<Scheme><LaunchAction buildConfiguration = \"ReleaseForRunning\">" +
                "</LaunchAction><ArchiveAction buildConfiguration = \"Release\">" +
                "</ArchiveAction></Scheme>";
            var updated =
                IOSDevelopmentBuild.UseDebugLaunchConfiguration(original);

            Check(
                updated.Contains(
                    "<LaunchAction buildConfiguration = \"Debug\">") &&
                updated.Contains(
                    "<ArchiveAction buildConfiguration = \"Release\">"),
                "The iOS development scheme must use Debug for LaunchAction without changing ArchiveAction.",
                failures);
            Check(
                IOSDevelopmentBuild.UseDebugLaunchConfiguration(updated) ==
                updated,
                "Applying the iOS development LaunchAction fix twice must be idempotent.",
                failures);

            const string sharedCache =
                "export BEE_CACHE_DIRECTORY=\\\"$HOME/Library/Unity/cache/bee\\\"";
            var localCache =
                IOSDevelopmentBuild.UseProjectLocalBeeCache(sharedCache);
            Check(
                localCache.Contains(
                    "$PROJECT_DIR/Il2CppBuildCache/$CONFIGURATION") &&
                !localCache.Contains("$HOME/Library/Unity/cache/bee"),
                "The iOS development IL2CPP build must not reuse Unity's shared Bee cache.",
                failures);
            Check(
                IOSDevelopmentBuild.UseProjectLocalBeeCache(localCache) ==
                localCache,
                "Applying the export-local Bee cache fix twice must be idempotent.",
                failures);

            var stableBuildOptions =
                IOSDevelopmentBuild.UseStableIOSBuildOptions(
                    BuildOptions.AutoRunPlayer |
                    BuildOptions.Development |
                    BuildOptions.ConnectWithProfiler |
                    BuildOptions.AllowDebugging |
                    BuildOptions.EnableDeepProfilingSupport |
                    BuildOptions.WaitForPlayerConnection);
            Check(
                (stableBuildOptions & BuildOptions.AutoRunPlayer) != 0 &&
                (stableBuildOptions & BuildOptions.Development) == 0 &&
                (stableBuildOptions & BuildOptions.ConnectWithProfiler) == 0 &&
                (stableBuildOptions & BuildOptions.AllowDebugging) == 0 &&
                (stableBuildOptions &
                    BuildOptions.EnableDeepProfilingSupport) == 0 &&
                (stableBuildOptions &
                    BuildOptions.WaitForPlayerConnection) == 0 &&
                (stableBuildOptions & BuildOptions.CleanBuildCache) != 0,
                "Unity 6000.3.18f1 iOS builds must preserve safe requested options, remove Development/debug/profiler waits, and add Clean Build Cache.",
                failures);

            const string duplicatePhaseId =
                "C62A2A42F32E085EF849CF0B";
            const string sourcePhaseId =
                "7F4E059C2717216D00A2CBE4";
            var duplicatePhases =
                "firstTarget = {\n" +
                "\tbuildPhases = (\n" +
                "\t\t" + sourcePhaseId + " /* Sources */,\n" +
                "\t\t" + duplicatePhaseId + " /* ShellScript */,\n" +
                "\t\t" + duplicatePhaseId + " /* ShellScript */,\n" +
                "\t);\n" +
                "};\n" +
                "secondTarget = {\n" +
                "\tbuildPhases = (\n" +
                "\t\t" + duplicatePhaseId + " /* ShellScript */,\n" +
                "\t);\n" +
                "};";
            var uniquePhases =
                IOSDevelopmentBuild.RemoveDuplicateBuildPhaseReferences(
                    duplicatePhases);
            Check(
                uniquePhases.Split(
                    new[] { duplicatePhaseId },
                    StringSplitOptions.None).Length == 3 &&
                uniquePhases.Contains(sourcePhaseId),
                "Each Xcode target must keep one copy of every build phase while preserving other phases.",
                failures);
            Check(
                IOSDevelopmentBuild.RemoveDuplicateBuildPhaseReferences(
                    uniquePhases) == uniquePhases,
                "Removing duplicate Xcode build phases twice must be idempotent.",
                failures);

            var originalCodeGeneration =
                PlayerSettings.GetIl2CppCodeGeneration(
                    NamedBuildTarget.iOS);
            try
            {
                PlayerSettings.SetIl2CppCodeGeneration(
                    NamedBuildTarget.iOS,
                    Il2CppCodeGeneration.OptimizeSpeed);
                IOSDevelopmentBuild
                    .ConfigureStableIOSIl2CppCodeGeneration();
                Check(
                    PlayerSettings.GetIl2CppCodeGeneration(
                        NamedBuildTarget.iOS) ==
                    Il2CppCodeGeneration.OptimizeSize,
                    "Every iOS export must use OptimizeSize so URP RenderGraph generic metadata is generated.",
                    failures);
                Check(
                    new IOSIl2CppBuildPreprocessor().callbackOrder < 0,
                    "The iOS IL2CPP code-generation guard must run before normal build preprocessors.",
                    failures);
            }
            finally
            {
                PlayerSettings.SetIl2CppCodeGeneration(
                    NamedBuildTarget.iOS,
                    originalCodeGeneration);
            }
        }

        private static void Check(bool condition, string failure, ICollection<string> failures)
        {
            if (!condition) failures.Add(failure);
        }

        private static void VerifyBundledKoreanFont(ICollection<string> failures)
        {
            var font = Resources.Load<Font>("Fonts/NotoSansKR-Regular");
            Check(font != null, "The mobile UI Korean font must be bundled in Resources.", failures);
            if (font == null)
            {
                return;
            }

            const string requiredCharacters = "AI 헬스케어 코치 운동 선택 목표 설정 자세 추적 리플레이 0123456789";
            foreach (var character in requiredCharacters)
            {
                if (!char.IsWhiteSpace(character))
                {
                    Check(font.HasCharacter(character), "The mobile UI font is missing character: " + character, failures);
                }
            }
        }

        private static void VerifyTtsScheduling(ICollection<string> failures)
        {
            var scheduler = new TtsRequestScheduler(new TtsRequestSchedulerOptions
            {
                DuplicateCooldownSeconds = 2d,
                StartObservationGraceSeconds = 0.1d,
                StopTimeoutSeconds = 0.5d,
                MinimumPreemptPriority = TtsRequestPriority.Critical
            });
            scheduler.BeginGeneration(7);

            var info = scheduler.Enqueue(
                "기본 안내",
                "info",
                TtsRequestPriority.Info,
                0d,
                2d,
                7);
            var startInfo = scheduler.Poll(0d, false);
            Check(info.Disposition == TtsEnqueueDisposition.AcceptedAsActive &&
                  startInfo.Type == TtsSchedulerActionType.Start,
                "The first TTS request must become the only active request.", failures);
            scheduler.AcknowledgeStarted(info.Request.RequestId, 0d, true);

            scheduler.Enqueue(
                "일반 경고",
                "warning",
                TtsRequestPriority.Warning,
                0.1d,
                3d,
                7);
            var critical = scheduler.Enqueue(
                "즉시 멈추세요",
                "critical",
                TtsRequestPriority.Critical,
                0.2d,
                6d,
                7);
            var stop = scheduler.Poll(0.2d, true);
            Check(critical.Disposition == TtsEnqueueDisposition.AcceptedWithPreemption &&
                  scheduler.HasPending &&
                  scheduler.Pending.Priority == TtsRequestPriority.Critical &&
                  stop.Type == TtsSchedulerActionType.StopForPreemption,
                "Critical TTS must replace lower-priority pending speech and request one stop.", failures);

            scheduler.AcknowledgeStopIssued(info.Request.RequestId, 0.2d);
            Check(scheduler.Poll(0.4d, true).Type == TtsSchedulerActionType.None,
                "TTS must wait for a cancel terminal state before promoting Critical speech.", failures);
            var quarantine = scheduler.Poll(0.8d, true);
            Check(quarantine.Type == TtsSchedulerActionType.QuarantineBackend &&
                  scheduler.IsQuarantined &&
                  !scheduler.IsBusy,
                "A backend stop timeout must quarantine speech instead of starting concurrently.", failures);

            scheduler.BeginGeneration(8);
            var active = scheduler.Enqueue(
                "기존 안내",
                "active",
                TtsRequestPriority.Info,
                1d,
                2d,
                8);
            scheduler.Poll(1d, false);
            scheduler.AcknowledgeStarted(active.Request.RequestId, 1d, true);
            var pendingCritical = scheduler.Enqueue(
                "안전 안내",
                "safety",
                TtsRequestPriority.Critical,
                1.1d,
                6d,
                8);
            scheduler.Poll(1.1d, true);
            scheduler.AcknowledgeStopIssued(active.Request.RequestId, 1.1d);
            var promoteAfterCancel = scheduler.Poll(1.2d, false);
            Check(pendingCritical.IsScheduled &&
                  promoteAfterCancel.Type == TtsSchedulerActionType.Start &&
                  promoteAfterCancel.Request.Priority == TtsRequestPriority.Critical,
                "Critical TTS may start only after the backend reports idle/cancelled.", failures);

            var stale = scheduler.Enqueue(
                "이전 세션 안내",
                "stale",
                TtsRequestPriority.Info,
                1.3d,
                2d,
                7);
            Check(stale.Disposition == TtsEnqueueDisposition.RejectedGeneration,
                "A stale TTS generation must never enter the speech queue.", failures);

            scheduler.BeginGeneration(9);
            var deferredInfo = scheduler.Enqueue(
                "곧 만료될 일반 안내",
                "deferred_info",
                TtsRequestPriority.Info,
                2d,
                0.05d,
                9);
            var immediateCritical = scheduler.Enqueue(
                "즉시 안전 자세를 확인하세요",
                "immediate_critical",
                TtsRequestPriority.Critical,
                2d,
                6d,
                9);
            var startHighestPriority = scheduler.Poll(2d, false);
            Check(deferredInfo.IsScheduled &&
                  immediateCritical.IsScheduled &&
                  startHighestPriority.Type == TtsSchedulerActionType.Start &&
                  startHighestPriority.Request.RequestId == immediateCritical.Request.RequestId &&
                  scheduler.Active.RequestId == immediateCritical.Request.RequestId &&
                  scheduler.Pending.RequestId == deferredInfo.Request.RequestId,
                "A higher-priority request admitted before Update must start before a queued lower-priority request.",
                failures);

            scheduler.AcknowledgeStarted(immediateCritical.Request.RequestId, 2d, true);
            scheduler.Poll(2.01d, true);
            var afterCriticalFinished = scheduler.Poll(2.2d, false);
            Check(afterCriticalFinished.Type == TtsSchedulerActionType.None && !scheduler.IsBusy,
                "A displaced lower-priority request must retain its TTL and expire while higher-priority speech runs.",
                failures);

            scheduler.BeginGeneration(10);
            var workoutStart = scheduler.Enqueue(
                "운동을 시작합니다",
                "workout_start",
                TtsRequestPriority.Info,
                3d,
                3d,
                10);
            var startWorkoutSpeech = scheduler.Poll(3d, false);
            scheduler.AcknowledgeStarted(
                workoutStart.Request.RequestId,
                3d,
                true);
            var staleDepth = scheduler.Enqueue(
                "엉덩이를 조금 더 내려 주세요",
                "squat_depth_shallow",
                TtsRequestPriority.Warning,
                3.1d,
                3d,
                10);
            var cancelledPending = scheduler.CancelNotStartedSemantic(
                "squat_depth_shallow",
                3.2d);
            var afterWorkoutSpeech = scheduler.Poll(3.4d, false);
            Check(startWorkoutSpeech.Type == TtsSchedulerActionType.Start &&
                  staleDepth.Disposition ==
                  TtsEnqueueDisposition.AcceptedAsPending &&
                  cancelledPending &&
                  afterWorkoutSpeech.Type == TtsSchedulerActionType.None &&
                  !scheduler.IsBusy,
                "A corrected pose must remove its pending depth cue before it can play after Standing.",
                failures);

            scheduler.BeginGeneration(11);
            scheduler.Enqueue(
                "엉덩이를 조금 더 내려 주세요",
                "squat_depth_shallow",
                TtsRequestPriority.Warning,
                4d,
                3d,
                11);
            var cancelledQueued = scheduler.CancelNotStartedSemantic(
                "squat_depth_shallow",
                4.01d);
            Check(cancelledQueued &&
                  scheduler.Poll(4.02d, false).Type ==
                  TtsSchedulerActionType.None &&
                  !scheduler.IsBusy,
                "A depth cue corrected before the next TTS pump must never enter the backend.",
                failures);

            scheduler.BeginGeneration(12);
            var standingAnnouncement = scheduler.Enqueue(
                "운동을 시작합니다",
                "workout_start",
                TtsRequestPriority.Info,
                5d,
                3d,
                12);
            scheduler.Poll(5d, false);
            scheduler.AcknowledgeStarted(
                standingAnnouncement.Request.RequestId,
                5d,
                true);
            scheduler.Enqueue(
                "상체를 세워 주세요",
                "squat_torso_tilt",
                TtsRequestPriority.Warning,
                5.1d,
                3d,
                12);
            var cancelledStandingPose =
                scheduler.CancelNotStartedSemanticPrefix(
                    "squat_",
                    5.2d);
            var correctAfterStanding = scheduler.Enqueue(
                "올바른 자세입니다. 1개.",
                "correct_rep_1",
                TtsRequestPriority.Info,
                5.21d,
                2d,
                12);
            var startCorrectCount = scheduler.Poll(5.3d, false);
            Check(cancelledStandingPose &&
                  correctAfterStanding.IsScheduled &&
                  startCorrectCount.Type ==
                  TtsSchedulerActionType.Start &&
                  startCorrectCount.Request.SemanticId ==
                  "correct_rep_1",
                "Standing must cancel pending squat coaching while preserving the completed-rep count announcement.",
                failures);
            Check(!RealtimeFeedbackOrchestrator.AllowsPostureCoaching(
                      ExercisePhase.Standing) &&
                  !RealtimeFeedbackOrchestrator.AllowsPostureCoaching(
                      ExercisePhase.Unknown) &&
                  RealtimeFeedbackOrchestrator.AllowsPostureCoaching(
                      ExercisePhase.Descent) &&
                  RealtimeFeedbackOrchestrator.AllowsPostureCoaching(
                      ExercisePhase.Bottom) &&
                  RealtimeFeedbackOrchestrator.AllowsPostureCoaching(
                      ExercisePhase.Ascent),
                "Posture TTS must be limited to active squat phases.",
                failures);

            var admissionPrioritizer = new FeedbackPrioritizer();
            var retryCandidate = new FeedbackEvent
            {
                Id = "squat_depth_shallow",
                RuleId = "squat_depth_shallow",
                Severity = FeedbackSeverity.Warning,
                Confidence = 1f,
                PersistenceRatio = 1f
            };
            var retryCandidates = new[] { retryCandidate };
            var firstSelection = admissionPrioritizer.TrySelect(
                retryCandidates,
                3f,
                1.5f,
                out var firstSelected);
            var retryBeforeCommit = admissionPrioritizer.TrySelect(
                retryCandidates,
                3f,
                1.5f,
                out var retriedSelected);
            admissionPrioritizer.CommitSelection(firstSelected);
            var retryAfterCommit = admissionPrioritizer.TrySelect(
                retryCandidates,
                3f,
                1.5f,
                out _);
            Check(firstSelection &&
                  retryBeforeCommit &&
                  firstSelected == retryCandidate &&
                  retriedSelected == retryCandidate &&
                  !retryAfterCommit,
                "Feedback cooldown must begin only after downstream TTS admission is committed.",
                failures);

            var admissionHost =
                new GameObject("Pose Feedback Admission QA Host");
            try
            {
                var coachTts =
                    admissionHost.AddComponent<CoachTtsController>();
                coachTts.BeginSession();
                var receiver =
                    admissionHost.AddComponent<PoseFeedbackJsonReceiver>();
                var feedback = new PoseFeedbackMessage
                {
                    id = "squat_depth_shallow",
                    text = "엉덩이를 조금 더 내려 주세요.",
                    confidence = 1f,
                    severity = FeedbackSeverity.Warning
                };
                var firstAdmission = receiver.ReceiveFeedback(feedback);
                var duplicateAdmission = receiver.ReceiveFeedback(feedback);
                Check(firstAdmission && !duplicateAdmission,
                    "Pose feedback must report true only when a new TTS request is actually scheduled.",
                    failures);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(admissionHost);
            }
        }

        private static void VerifyMobileUiStructure(ICollection<string> failures)
        {
            var host = new GameObject("Mobile UI QA Host");
            try
            {
                var view = host.AddComponent<MobileWorkoutPrototypeView>();
                var document = host.GetComponent<UIDocument>();
                var documentRoot = document == null ? null : document.rootVisualElement;
                var fullScreen = documentRoot?.Q<VisualElement>("full-screen-content");

                Check(documentRoot != null, "The mobile UI must create a UI document.", failures);
                Check(fullScreen != null, "The mobile UI must create a full-screen content root.", failures);
                Check(fullScreen != null && fullScreen.style.position.value == Position.Absolute,
                    "The mobile UI content root must stretch absolutely across the panel.", failures);
                Check(documentRoot?.Q<ScrollView>("step-scroll") != null,
                    "The mobile UI body must remain scrollable on short phone screens.", failures);
                Check(documentRoot?.Q<VisualElement>("phone-notch") == null &&
                      documentRoot?.Q<VisualElement>("phone-home-indicator") == null,
                    "The app UI must not draw a second phone frame inside the physical screen.", failures);
                var restDurationField = typeof(MobileWorkoutPrototypeView).GetField(
                    "restDurationSeconds",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Check(
                    restDurationField != null &&
                    (int)restDurationField.GetValue(view) == 3,
                    "The configurable between-set rest must default to three seconds.",
                    failures);
                Check(
                    MobileWorkoutPrototypeView.ShouldStartSetRest(2, 2, 2, 0) &&
                    !MobileWorkoutPrototypeView.ShouldStartSetRest(2, 2, 2, 2) &&
                    !MobileWorkoutPrototypeView.ShouldStartSetRest(4, 2, 2, 2) &&
                    !MobileWorkoutPrototypeView.ShouldStartSetRest(2, 2, 1, 0),
                    "Two reps by two sets must rest once after rep two, never again at the final target.",
                    failures);
                Check(
                    MobileWorkoutPrototypeView.CalculateWorkoutScore(5, 4) == 80 &&
                    MobileWorkoutPrototypeView.CalculateWorkoutScore(0, 0) == 0,
                    "Workout result score must be the bounded correct/total percentage.",
                    failures);
                Check(
                    typeof(MobileWorkoutPrototypeView).GetMethod(
                        "SetPaidStretchingAccess",
                        BindingFlags.Instance | BindingFlags.Public) != null,
                    "The payment layer must have an explicit stretching entitlement hook.",
                    failures);

                var buildPreviewMethod = typeof(MobileWorkoutPrototypeView).GetMethod(
                    "BuildPreviewPanel",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                var previewPanel = buildPreviewMethod?.Invoke(view, null) as VisualElement;
                var preview = previewPanel?.Q<Image>("camera-or-replay-preview");
                var overlay = previewPanel?.Q<VisualElement>("pose-overlay");
                Check(preview != null && overlay != null,
                    "The session UI must create separate camera preview and pose overlay elements.", failures);
                Check(preview != null && overlay != null && ReferenceEquals(preview.parent, overlay.parent),
                    "The pose overlay must be a sibling of the raw camera preview.", failures);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        private static void VerifyPosePreviewCoordinateMapping(ICollection<string> failures)
        {
            var rect = new Rect(0f, 0f, 100f, 200f);
            var asymmetricJoint = new TrackedJoint { x = 0.2f, y = 0.3f };
            var centerJoint = new TrackedJoint { x = 0.5f, y = 0.5f };
            var rearPoint = PoseDisplayCoordinateMapper.ToDisplayPoint(asymmetricJoint, rect, false);
            // Util API: mirrorX:true flips display X (tests/special paths). Live overlay uses false.
            var frontMirrorUtilPoint = PoseDisplayCoordinateMapper.ToDisplayPoint(asymmetricJoint, rect, true);
            var frontMirrorUtilCenter = PoseDisplayCoordinateMapper.ToDisplayPoint(centerJoint, rect, true);
            // Live overlay contract: front camera uses ToDisplayPoint(..., mirrorX: false),
            // so rear and front overlay mapping are identical for the same landmark.
            var liveOverlayFrontPoint = PoseDisplayCoordinateMapper.ToDisplayPoint(asymmetricJoint, rect, false);

            Check(Mathf.Abs(rearPoint.x - 20f) < 0.01f && Mathf.Abs(rearPoint.y - 60f) < 0.01f,
                "Rear-camera landmarks must preserve upright normalized coordinates.", failures);
            Check(Mathf.Abs(frontMirrorUtilPoint.x - 80f) < 0.01f && Mathf.Abs(frontMirrorUtilPoint.y - 60f) < 0.01f,
                "ToDisplayPoint(mirrorX:true) util API must mirror only the display X coordinate.", failures);
            Check(Mathf.Abs(frontMirrorUtilCenter.x - 50f) < 0.01f && Mathf.Abs(frontMirrorUtilCenter.y - 100f) < 0.01f,
                "ToDisplayPoint(mirrorX:true) util API must keep the center landmark fixed.", failures);
            Check(Mathf.Abs(liveOverlayFrontPoint.x - rearPoint.x) < 0.01f &&
                  Mathf.Abs(liveOverlayFrontPoint.y - rearPoint.y) < 0.01f,
                "Live overlay contract: front uses mirrorX:false, same path as rear for the same landmark.", failures);

            foreach (var rotation in new[] { 0, 90, 180, 270 })
            {
                foreach (var verticallyMirrored in new[] { false, true })
                {
                    foreach (var selfieMirrored in new[] { false, true })
                    {
                        var actual = PoseDisplayCoordinateMapper.ResolvePreviewScale(
                            rotation,
                            verticallyMirrored,
                            selfieMirrored);
                        var quarterTurn = rotation == 90 || rotation == 270;
                        var expectedX = selfieMirrored && !quarterTurn ? -1f : 1f;
                        var expectedY = verticallyMirrored ? -1f : 1f;
                        if (selfieMirrored && quarterTurn)
                        {
                            expectedY *= -1f;
                        }

                        Check(Mathf.Approximately(actual.x, expectedX) &&
                              Mathf.Approximately(actual.y, expectedY),
                            "Camera preview transform mismatch for rotation " + rotation +
                            ", verticalMirror=" + verticallyMirrored +
                            ", selfieMirror=" + selfieMirrored + ".",
                            failures);
                    }
                }
            }
        }

        private static void VerifyVisibilityGating(ICollection<string> failures)
        {
            var visibleJoint = new TrackedJoint
            {
                name = PoseJointNames.LeftKnee,
                x = 0.5f,
                y = 0.5f,
                visibility = 0.8f,
                confidence = 0.8f
            };
            // Explicit 0/0 after ResolveLandmarkScores would be remapped to 1/1 at ingest;
            // overlay CanRender still rejects Max(conf,vis) < 0.45 for already-resolved joints.
            var hiddenJoint = new TrackedJoint
            {
                name = PoseJointNames.RightKnee,
                x = 0.5f,
                y = 0.5f,
                visibility = 0f,
                confidence = 0f
            };

            Check(PoseDisplayCoordinateMapper.CanRender(visibleJoint),
                "Visible joints must render in the mobile pose overlay.", failures);
            Check(!PoseDisplayCoordinateMapper.CanRender(hiddenJoint),
                "Zero-confidence landmarks must not render in the mobile pose overlay.", failures);
            Check(Mathf.Approximately(PoseDisplayCoordinateMapper.MinimumRenderableScore, 0.45f),
                "Renderable score threshold must stay aligned with realtime rule settings.", failures);

            var resolveMethod = typeof(MediaPipePoseTrackingProvider).GetMethod(
                "ResolveLandmarkScores",
                BindingFlags.Static | BindingFlags.NonPublic);
            Check(resolveMethod != null,
                "MediaPipePoseTrackingProvider.ResolveLandmarkScores must remain accessible for QA.", failures);
            if (resolveMethod != null)
            {
                VerifyResolvedLandmarkScores(resolveMethod, 0f, 0f, 1f, 1f,
                    "ResolveLandmarkScores(0,0) must default to (1,1).", failures);
                VerifyResolvedLandmarkScores(resolveMethod, 0f, 0.8f, 0.8f, 0.8f,
                    "ResolveLandmarkScores(0,0.8) must mutual-fallback to (0.8,0.8).", failures);
                VerifyResolvedLandmarkScores(resolveMethod, 0.7f, 0f, 0.7f, 0.7f,
                    "ResolveLandmarkScores(0.7,0) must mutual-fallback to (0.7,0.7).", failures);
            }
        }

        private static void VerifyResolvedLandmarkScores(
            MethodInfo resolveMethod,
            float visibilityInput,
            float presenceInput,
            float expectedVisibility,
            float expectedConfidence,
            string message,
            ICollection<string> failures)
        {
            var args = new object[] { visibilityInput, presenceInput, 0f, 0f };
            resolveMethod.Invoke(null, args);
            var visibility = (float)args[2];
            var confidence = (float)args[3];
            Check(Mathf.Approximately(visibility, expectedVisibility) &&
                  Mathf.Approximately(confidence, expectedConfidence),
                message, failures);
        }

        private static void VerifyLandmarkStability(ICollection<string> failures)
        {
            var settings = new RealtimePoseRuleSettings();
            var stabilizer = new PoseLandmarkStabilizer();
            var baseline = CloneWithJointOffset(SyntheticPoseFixtures.Standing(), PoseJointNames.LeftKnee, 0f, 1000L);
            var first = stabilizer.Stabilize(baseline, settings);
            first.TryGetJoint(PoseJointNames.LeftKnee, out var firstKnee);
            var firstKneeX = firstKnee == null ? 0f : firstKnee.x;
            var jittered = CloneWithJointOffset(baseline, PoseJointNames.LeftKnee, 0.02f, 1100L);
            var second = stabilizer.Stabilize(jittered, settings);
            second.TryGetJoint(PoseJointNames.LeftKnee, out var secondKnee);
            var secondKneeX = secondKnee == null ? 0f : secondKnee.x;
            var outlier = CloneWithJointOffset(baseline, PoseJointNames.LeftKnee, 0.3f, 1200L);
            var third = stabilizer.Stabilize(outlier, settings);
            third.TryGetJoint(PoseJointNames.LeftKnee, out var thirdKnee);
            var thirdKneeX = thirdKnee == null ? 0f : thirdKnee.x;

            Check(firstKnee != null && secondKnee != null && thirdKnee != null,
                "Landmark stabilizer must preserve tracked knees.", failures);
            Check(firstKnee != null && secondKnee != null &&
                  Mathf.Abs(secondKneeX - firstKneeX) < 0.02f,
                "Median plus EMA smoothing must reduce small landmark jitter.", failures);
            Check(secondKnee != null && thirdKnee != null &&
                  Mathf.Abs(thirdKneeX - secondKneeX) < 0.01f,
                "A single large landmark jump must be rejected as an outlier.", failures);

            var lowConfidence = SyntheticPoseFixtures.LowConfidence();
            lowConfidence.timestampUnixMilliseconds = 1300L;
            var held = stabilizer.Stabilize(lowConfidence, settings);
            held.TryGetJoint(PoseJointNames.LeftKnee, out var heldKnee);
            Check(stabilizer.HeldLowConfidenceJointCount > 0 &&
                  heldKnee != null &&
                  PoseFrameView.GetJointScore(heldKnee) < settings.minimumVisibility,
                "Held coordinates must retain the current low confidence so analysis cannot treat them as observed.", failures);
        }

        private static void VerifyFrontCameraTrackingQuality(ICollection<string> failures)
        {
            var settings = new RealtimePoseRuleSettings();
            var evaluator = new PoseTrackingQualityEvaluator();

            PoseTrackingQualityReport report = null;
            for (var i = 0; i < settings.trackingQualityGoodFrames; i++)
            {
                var standing = SyntheticPoseFixtures.Standing();
                standing.timestampUnixMilliseconds = 1000L + i * 100L;
                report = evaluator.Evaluate(standing, settings);
            }

            Check(report != null && report.State == PoseTrackingQualityState.Good,
                "Stable front-camera core landmarks must enter Good after the configured warm-up frames.", failures);
            Check(report != null && report.HasReliableCore && report.IsFrontal && report.IsFullyInFrame,
                "Good tracking quality must require confidence, frontal span, and full-frame visibility.", failures);

            var clipped = SyntheticPoseFixtures.Standing();
            clipped.TryGetJoint(PoseJointNames.LeftAnkle, out var leftAnkle);
            clipped.TryGetJoint(PoseJointNames.RightAnkle, out var rightAnkle);
            if (leftAnkle != null) leftAnkle.y = 1f;
            if (rightAnkle != null) rightAnkle.y = 1f;
            report = evaluator.Evaluate(clipped, settings);
            Check(report.State == PoseTrackingQualityState.Degraded && !report.IsFullyInFrame,
                "Clipped ankles must degrade tracking and pause pose decisions.", failures);

            for (var i = 0; i < settings.trackingQualityUnavailableFrames; i++)
            {
                var lowConfidence = SyntheticPoseFixtures.LowConfidence();
                lowConfidence.timestampUnixMilliseconds = 2000L + i * 100L;
                report = evaluator.Evaluate(lowConfidence, settings);
            }

            Check(report.State == PoseTrackingQualityState.Unavailable && !report.AllowsPoseAnalysis,
                "Sustained low-confidence core landmarks must become Unavailable.", failures);
        }

        private static void VerifyWorkoutSessionLifecycle(ICollection<string> failures)
        {
            var settings = new CalibrationSettings
            {
                calibrationVisibilityThreshold = 0.85f,
                calibrationHoldSeconds = 0.2f,
                countdownSeconds = 0.3f,
                pauseVisibilityThreshold = 0.6f,
                outOfFrameGraceSeconds = 0.2f,
                reReadyDebounceSeconds = 0.2f
            };
            var machine = new WorkoutSessionStateMachine(settings);
            var goodQuality = new PoseTrackingQualityReport
            {
                State = PoseTrackingQualityState.Good
            };
            var degradedQuality = new PoseTrackingQualityReport
            {
                State = PoseTrackingQualityState.Degraded
            };
            var calibrationConfirmedCount = 0;
            machine.CalibrationConfirmed += () => calibrationConfirmedCount++;

            machine.BeginCalibrationSession();
            machine.Tick(SyntheticPoseFixtures.Standing(), goodQuality, 0.1f);
            Check(machine.State == WorkoutTrackingState.ReadyForCalibration,
                "Calibration must remain ready until the configured hold duration is met.", failures);

            machine.Tick(SyntheticPoseFixtures.Standing(), goodQuality, 0.11f);
            Check(machine.State == WorkoutTrackingState.CountingDown,
                "Stable full-body visibility must start the countdown.", failures);

            machine.Tick(SyntheticPoseFixtures.Standing(), goodQuality, 0.15f);
            machine.Tick(SyntheticPoseFixtures.Standing(), goodQuality, 0.16f);
            Check(machine.State == WorkoutTrackingState.InWorkout &&
                  calibrationConfirmedCount == 1 &&
                  !machine.AllowsPoseAnalysis,
                "Countdown completion must confirm calibration exactly once without starting workout pose analysis.", failures);

            var rollbackMachine = new WorkoutSessionStateMachine(settings);
            rollbackMachine.BeginCalibrationSession();
            rollbackMachine.Tick(SyntheticPoseFixtures.Standing(), goodQuality, 0.21f);
            rollbackMachine.Tick(SyntheticPoseFixtures.LowConfidence(), degradedQuality, 0.01f);
            Check(rollbackMachine.State == WorkoutTrackingState.ReadyForCalibration,
                "Losing full-body visibility during countdown must roll back to calibration.", failures);

            var workoutMachine = new WorkoutSessionStateMachine(settings);
            workoutMachine.BeginWorkoutSession();
            Check(workoutMachine.State == WorkoutTrackingState.InWorkout &&
                  workoutMachine.AllowsPoseAnalysis &&
                  Mathf.Approximately(workoutMachine.CountdownRemainingSeconds, 0f),
                "An already calibrated workout must begin analysis immediately without a countdown.", failures);

            workoutMachine.Tick(SyntheticPoseFixtures.LowConfidence(), degradedQuality, 0.1f);
            Check(workoutMachine.State == WorkoutTrackingState.InWorkout,
                "A brief workout tracking degradation must stay inside the out-of-frame grace period.", failures);

            workoutMachine.Tick(SyntheticPoseFixtures.LowConfidence(), degradedQuality, 0.11f);
            Check(workoutMachine.State == WorkoutTrackingState.PausedOutOfFrame &&
                  !workoutMachine.AllowsPoseAnalysis,
                "Sustained workout tracking degradation must pause pose analysis.", failures);

            workoutMachine.Tick(SyntheticPoseFixtures.LowConfidence(), degradedQuality, 1f);
            workoutMachine.Tick(SyntheticPoseFixtures.LowConfidence(), degradedQuality, 1f);
            Check(workoutMachine.State == WorkoutTrackingState.PausedOutOfFrame &&
                  Mathf.Approximately(workoutMachine.CountdownRemainingSeconds, 0f),
                "A prolonged workout pause must never re-enter calibration or countdown.", failures);

            workoutMachine.Tick(SyntheticPoseFixtures.LowConfidence(), goodQuality, 0.01f);
            Check(workoutMachine.State == WorkoutTrackingState.InWorkout &&
                  workoutMachine.AllowsPoseAnalysis,
                "One recovered workout-quality frame must resume analysis immediately without full-body calibration.", failures);

            workoutMachine.EndSession();
            Check(!workoutMachine.IsSessionActive && !workoutMachine.AllowsPoseAnalysis,
                "Ending a workout session must disable pose analysis.", failures);
        }

        private static void VerifyPersonalizedRomSafety(ICollection<string> failures)
        {
            var evaluator = new PersonalizedRomEvaluator();
            var profile = new UserProfileData
            {
                injuries = InjuryRegions.Knee | InjuryRegions.LowerBack,
                skill = SkillLevel.Beginner
            };
            var safety = evaluator.Evaluate(profile);
            Check(Mathf.Approximately(safety.minimumBottomKneeAngleDelta, 25f) &&
                  Mathf.Approximately(safety.maximumBottomKneeAngleDelta, 5f) &&
                  Mathf.Approximately(safety.maximumTorsoTiltDegreesDelta, -12f) &&
                  safety.suppressDeeperEncouragement,
                "Knee/lower-back beginner profile must choose the most conservative ROM deltas.", failures);

            var baseSettings = new RealtimePoseRuleSettings();
            var originalMinimum = baseSettings.minimumBottomKneeAngle;
            var originalMaximum = baseSettings.maximumBottomKneeAngle;
            var originalTorsoTilt = baseSettings.maximumTorsoTiltDegrees;
            var personalized = evaluator.ApplyDerate(baseSettings, safety);
            Check(personalized != null &&
                  Mathf.Approximately(personalized.minimumBottomKneeAngle, originalMinimum + 25f) &&
                  Mathf.Approximately(personalized.maximumBottomKneeAngle, originalMaximum + 5f) &&
                  Mathf.Approximately(personalized.maximumTorsoTiltDegrees, originalTorsoTilt - 12f),
                "ROM safety deltas must be applied to the active workout settings.", failures);
            Check(Mathf.Approximately(baseSettings.minimumBottomKneeAngle, originalMinimum) &&
                  Mathf.Approximately(baseSettings.maximumBottomKneeAngle, originalMaximum) &&
                  Mathf.Approximately(baseSettings.maximumTorsoTiltDegrees, originalTorsoTilt),
                "Personalized ROM application must not mutate the serialized base settings.", failures);
        }

        private static void VerifyProfileCompletionGate(ICollection<string> failures)
        {
            var profile = new UserProfileData
            {
                ageYears = 35,
                gender = Gender.Other,
                heightCm = 170f,
                weightKg = 70f,
                sessionsPerWeek = 3
            };
            Check(!profile.IsComplete,
                "Body metrics alone must not skip the unfinished workout-preference onboarding step.", failures);

            profile.onboardingCompleted = true;
            Check(profile.IsComplete,
                "A fully committed profile with valid body metrics must pass the onboarding gate.", failures);
        }

        private static void VerifyHotPathObjectReuse(ICollection<string> failures)
        {
            var settings = new RealtimePoseRuleSettings();
            var stabilizer = new PoseLandmarkStabilizer();
            var firstFrame = stabilizer.Stabilize(
                CloneWithJointOffset(SyntheticPoseFixtures.Standing(), PoseJointNames.LeftKnee, 0f, 1000L),
                settings);
            var firstJointArray = firstFrame.joints;
            var secondFrame = stabilizer.Stabilize(
                CloneWithJointOffset(SyntheticPoseFixtures.Standing(), PoseJointNames.LeftKnee, 0.01f, 1100L),
                settings);
            Check(ReferenceEquals(firstFrame, secondFrame) && ReferenceEquals(firstJointArray, secondFrame.joints),
                "Landmark stabilization must reuse its frame and joint array after warm-up.", failures);

            var buffer = new PoseWindowBuffer(2);
            var source = ReliableFeature(150f, 1000L);
            buffer.Add(source);
            source.AverageKneeAngle = 90f;
            Check(!ReferenceEquals(source, buffer.GetChronological(0)) &&
                  Mathf.Approximately(buffer.GetChronological(0).AverageKneeAngle, 150f),
                "Pose window slots must own reusable copies instead of retaining mutable feature views.", failures);

            var reusableStats = new PoseWindowStats();
            var calculatedStats = PoseWindowStats.Calculate(buffer, settings, reusableStats);
            Check(ReferenceEquals(reusableStats, calculatedStats),
                "Window statistics must support caller-owned result reuse.", failures);

            var ruleFeature = ReliableFeature(130f, 1200L);
            ruleFeature.TorsoTiltDegrees = settings.MaximumTorsoTiltDegrees + 10f;
            var ruleStats = new PoseWindowStats
            {
                FrameCount = settings.minimumRuleEvaluationFrames,
                ValidCoreFrameCount = settings.minimumRuleEvaluationFrames,
                ValidCoreFrameRatio = 1f,
                AverageTorsoTiltDegrees = ruleFeature.TorsoTiltDegrees,
                TorsoTiltViolationRatio = 1f
            };
            var phase = new ExercisePhaseState { CurrentPhase = ExercisePhase.Descent, Exercise = "squat" };
            var ruleEngine = new RealtimePoseRuleEngine();
            var firstEvents = ruleEngine.Evaluate(ruleFeature, ruleStats, phase, settings);
            var firstEvent = firstEvents.Count == 0 ? null : firstEvents[0];
            var secondEvents = ruleEngine.Evaluate(ruleFeature, ruleStats, phase, settings);
            Check(firstEvent != null && secondEvents.Count > 0 && ReferenceEquals(firstEvent, secondEvents[0]),
                "Rule evaluation must reuse feedback events after pool warm-up.", failures);
        }

        private static void VerifyPhaseReversalRecognition(ICollection<string> failures)
        {
            var settings = new RealtimePoseRuleSettings();
            var detector = new ExercisePhaseDetector();
            // Standing entry at >= standingKneeAngle (150); exit hysteresis keeps Standing until < standingExit (140).
            detector.Update(PhaseFeature(170f, 0f, 1000L), settings);
            Check(detector.State.CurrentPhase == ExercisePhase.Standing,
                "Knee angle 170 must enter Standing with StandingKneeAngle=150.", failures);
            detector.Update(PhaseFeature(165f, 0f, 1050L), settings);
            Check(detector.State.CurrentPhase == ExercisePhase.Standing,
                "Knee angle 165 must remain Standing.", failures);
            detector.Update(PhaseFeature(139f, -100f, 1100L), settings);
            detector.Update(PhaseFeature(130f, -100f, 1200L), settings);
            var bottomPhase = detector.Update(PhaseFeature(128f, 10f, 1300L), settings).CurrentPhase;
            detector.Update(PhaseFeature(140f, 80f, 1400L), settings);
            var completed = detector.Update(PhaseFeature(165f, 80f, 1500L), settings);

            Check(bottomPhase == ExercisePhase.Bottom,
                "Descent-to-ascent reversal must recognize the squat bottom without a full stop.", failures);
            Check(completed.RepCount == 1,
                "A stabilized standing-descent-bottom-ascent-standing sequence must count one rep.", failures);

            var naturalStandingSettings = new RealtimePoseRuleSettings
            {
                standingKneeAngle = 165f,
                standingExitKneeAngle = 160f,
                minimumPhaseKneeAngleExcursion = 8f
            };
            var naturalStandingDetector = new ExercisePhaseDetector();
            naturalStandingDetector.Update(
                PhaseFeature(165f, 0f, 2000L),
                naturalStandingSettings);
            naturalStandingDetector.Update(
                PhaseFeature(158f, -20f, 2100L),
                naturalStandingSettings);
            naturalStandingDetector.Update(
                PhaseFeature(158f, 0f, 2200L),
                naturalStandingSettings);
            var naturalStandingRecovered = naturalStandingDetector.Update(
                PhaseFeature(165f, 20f, 2300L),
                naturalStandingSettings);
            Check(naturalStandingRecovered.RepCount == 0 &&
                  naturalStandingRecovered.CurrentPhase == ExercisePhase.Standing,
                "A user standing naturally at 165° with a 7° wobble must not inherit a synthetic 180° baseline or count a rep.", failures);

            var slowDetector = new ExercisePhaseDetector();
            slowDetector.Update(
                PhaseFeature(170f, 0f, 3000L, -0.25f),
                settings);
            var slowDescentPhase = slowDetector.Update(
                PhaseFeature(139f, -5f, 3100L, -0.08f),
                settings).CurrentPhase;
            slowDetector.Update(
                PhaseFeature(130f, -5f, 3200L, -0.03f),
                settings);
            slowDetector.Update(
                PhaseFeature(122f, -5f, 3300L, -0.01f),
                settings);
            var continuedSlowDescentPhase = slowDetector.Update(
                PhaseFeature(118f, -5f, 3500L, 0.01f),
                settings).CurrentPhase;
            slowDetector.Update(
                PhaseFeature(116f, -2f, 3600L, 0.02f),
                settings);
            var slowBottomPhase = slowDetector.Update(
                PhaseFeature(116f, 0f, 3700L, 0.02f),
                settings).CurrentPhase;
            slowDetector.Update(
                PhaseFeature(130f, 5f, 3800L, -0.02f),
                settings);
            slowDetector.Update(
                PhaseFeature(145f, 5f, 3900L, -0.10f),
                settings);
            var slowCompleted = slowDetector.Update(
                PhaseFeature(165f, 5f, 4000L, -0.25f),
                settings);
            Check(slowDescentPhase == ExercisePhase.Descent &&
                  continuedSlowDescentPhase == ExercisePhase.Descent,
                "A slow monotonic squat must remain in Descent instead of becoming Unknown or Bottom too early.",
                failures);
            Check(slowBottomPhase == ExercisePhase.Bottom &&
                  slowCompleted.RepCount == 1,
                "A slow descent-bottom-ascent sequence must count exactly one complete rep.",
                failures);
        }

        private static void VerifyAdaptiveSquatDepthFloor(
            ICollection<string> failures)
        {
            var settings = new RealtimePoseRuleSettings
            {
                minimumHipToKneeDepth = 0f,
                hipToKneeLevelTolerance = 0.03f,
                minimumHipToKneeDepthFrames = 2,
                maximumCountableBottomKneeAngle = 135f,
                adaptiveBottomSampleCount = 3,
                adaptiveBottomKneeAngleMargin = 8f
            };

            var oneFrameDetector = new ExercisePhaseDetector();
            var timestamp = 3000L;
            RunDepthRep(
                oneFrameDetector,
                settings,
                ref timestamp,
                132f,
                0f,
                1);
            Check(oneFrameDetector.State.RepCount == 0 &&
                  oneFrameDetector.State.AdaptiveBottomSampleCount == 0,
                "One frame at the hip-to-knee floor must not count or train the adaptive profile.", failures);

            var adaptiveDetector = new ExercisePhaseDetector();
            timestamp = 5000L;
            RunDepthRep(
                adaptiveDetector,
                settings,
                ref timestamp,
                132f,
                0f,
                2);
            Check(adaptiveDetector.State.RepCount == 1 &&
                  adaptiveDetector.State.AdaptiveBottomSampleCount == 1,
                "The first complete rep with two stable frames exactly at knee height must count immediately. " +
                $"Observed reps={adaptiveDetector.State.RepCount}, samples={adaptiveDetector.State.AdaptiveBottomSampleCount}, " +
                $"phase={adaptiveDetector.State.CurrentPhase}, reached={adaptiveDetector.State.HasReachedHipToKneeDepthInCurrentRep}.", failures);

            RunDepthRep(
                adaptiveDetector,
                settings,
                ref timestamp,
                135f,
                0.02f,
                2);
            RunDepthRep(
                adaptiveDetector,
                settings,
                ref timestamp,
                135f,
                0.01f,
                2);
            Check(adaptiveDetector.State.RepCount == 3 &&
                  adaptiveDetector.State.AdaptiveBottomSampleCount == 3 &&
                  Mathf.Abs(
                      adaptiveDetector.State.AdaptiveBottomKneeAngle -
                      134f) < 0.01f,
                "Three accepted reps must learn their session-average bottom knee angle.", failures);
            Check(Mathf.Abs(
                      adaptiveDetector.State.EffectiveBottomKneeAngle -
                      142f) < 0.01f,
                "The learned bottom angle must apply only its bounded recognition margin.", failures);

            var learnedAngle =
                adaptiveDetector.State.AdaptiveBottomKneeAngle;
            RunDepthRep(
                adaptiveDetector,
                settings,
                ref timestamp,
                100f,
                0.03f,
                2);
            Check(adaptiveDetector.State.RepCount == 4 &&
                  adaptiveDetector.State.AdaptiveBottomSampleCount == 3 &&
                  Mathf.Approximately(
                      adaptiveDetector.State.AdaptiveBottomKneeAngle,
                      learnedAngle),
                "The adaptive profile must freeze after the configured three accepted samples.", failures);

            var nearLevelDetector = new ExercisePhaseDetector();
            timestamp = 9000L;
            RunDepthRep(
                nearLevelDetector,
                settings,
                ref timestamp,
                132f,
                -0.015f,
                2);
            Check(nearLevelDetector.State.RepCount == 1 &&
                  nearLevelDetector.State.AdaptiveBottomSampleCount == 1,
                "A hip center visually level with the knees must pass the tolerant 2D gate when secondary depth evidence is valid.",
                failures);

            var secondaryGuardDetector = new ExercisePhaseDetector();
            RunDepthRep(
                secondaryGuardDetector,
                settings,
                ref timestamp,
                139f,
                -0.015f,
                3);
            Check(secondaryGuardDetector.State.RepCount == 0,
                "Passing the tolerant 2D hip/knee gate alone must not count without secondary knee-flexion or hip-drop depth.",
                failures);

            var aboveKneeDetector = new ExercisePhaseDetector();
            RunDepthRep(
                aboveKneeDetector,
                settings,
                ref timestamp,
                90f,
                -0.04f,
                3);
            Check(aboveKneeDetector.State.RepCount == 0 &&
                  aboveKneeDetector.State.AdaptiveBottomSampleCount == 0,
                "Even a deeply bent knee angle must not count while the hip center remains clearly above the 2D tolerance band.", failures);

            var interruptedEvidenceDetector =
                new ExercisePhaseDetector();
            timestamp = 11000L;
            interruptedEvidenceDetector.Update(
                PhaseFeature(170f, 0f, timestamp, -0.25f),
                settings);
            timestamp += 100L;
            interruptedEvidenceDetector.Update(
                PhaseFeature(139f, -100f, timestamp, -0.08f),
                settings);
            timestamp += 100L;
            interruptedEvidenceDetector.Update(
                PhaseFeature(132f, -60f, timestamp, 0.01f),
                settings);
            timestamp += 100L;
            var unreliableBottom =
                PhaseFeature(132f, 0f, timestamp, 0.01f);
            unreliableBottom.HasHipToKneeDepth = false;
            interruptedEvidenceDetector.Update(
                unreliableBottom,
                settings);
            timestamp += 100L;
            interruptedEvidenceDetector.Update(
                PhaseFeature(132f, 0f, timestamp, 0.01f),
                settings);
            timestamp += 100L;
            interruptedEvidenceDetector.Update(
                PhaseFeature(150f, 100f, timestamp, -0.08f),
                settings);
            timestamp += 100L;
            interruptedEvidenceDetector.Update(
                PhaseFeature(170f, 100f, timestamp, -0.25f),
                settings);
            Check(interruptedEvidenceDetector.State.RepCount == 0 &&
                  interruptedEvidenceDetector.State.AdaptiveBottomSampleCount == 0,
                "A missing/unreliable depth frame must break consecutive floor evidence and must not train the profile.", failures);

            adaptiveDetector.Suspend(timestamp + 100L);
            Check(adaptiveDetector.State.AdaptiveBottomSampleCount == 3 &&
                  Mathf.Approximately(
                      adaptiveDetector.State.AdaptiveBottomKneeAngle,
                      learnedAngle),
                "A temporary tracking suspension must preserve the session adaptive depth profile.", failures);
            adaptiveDetector.Reset();
            Check(adaptiveDetector.State.AdaptiveBottomSampleCount == 0 &&
                  Mathf.Approximately(
                      adaptiveDetector.State.AdaptiveBottomKneeAngle,
                      0f),
                "A new-session reset must clear the adaptive depth profile.", failures);
        }

        private static void RunDepthRep(
            ExercisePhaseDetector detector,
            RealtimePoseRuleSettings settings,
            ref long timestamp,
            float bottomKneeAngle,
            float hipToKneeDepth,
            int stableDepthFrames)
        {
            detector.Update(
                PhaseFeature(170f, 0f, timestamp, -0.25f),
                settings);
            timestamp += 100L;
            detector.Update(
                PhaseFeature(
                    Mathf.Min(139f, bottomKneeAngle + 10f),
                    -100f,
                    timestamp,
                    -0.08f),
                settings);
            timestamp += 100L;

            for (var i = 0; i < stableDepthFrames; i++)
            {
                detector.Update(
                    PhaseFeature(
                        bottomKneeAngle,
                        i == 0 ? -60f : -5f,
                        timestamp,
                        hipToKneeDepth),
                    settings);
                timestamp += 100L;
            }

            detector.Update(
                PhaseFeature(
                    Mathf.Min(155f, bottomKneeAngle + 18f),
                    100f,
                    timestamp,
                    -0.08f),
                settings);
            timestamp += 100L;
            detector.Update(
                PhaseFeature(170f, 100f, timestamp, -0.25f),
                settings);
            timestamp += 100L;
        }

        private static void VerifyJointCoordinateSquatPipeline(ICollection<string> failures)
        {
            var settings = new RealtimePoseRuleSettings();
            var normal = RunSquatPipeline(
                SyntheticPoseFixtures.SquatRepSequence(),
                settings);
            Check(normal.SawStanding &&
                  normal.SawDescent &&
                  normal.SawBottom &&
                  normal.SawAscent &&
                  !normal.SawBottomAfterAscent,
                "Joint-coordinate squat fixtures must traverse Standing, Descent, Bottom, and Ascent after stabilization.", failures);
            Check(normal.RepCount == 1 && normal.SawSufficientBottom,
                "A full joint-coordinate squat sequence must count exactly one sufficiently deep rep.", failures);
            Check(normal.MaximumHipToKneeDepth >= settings.MinimumHipToKneeDepth,
                "A counted joint-coordinate squat must place the hip center at knee height or lower.", failures);

            var shallow = RunSquatPipeline(
                SyntheticPoseFixtures.ShallowSquatSequence(),
                settings);
            Check(shallow.SawBottom && shallow.SawBottomWithoutSufficientDepth,
                "A shallow full movement must still expose Bottom for depth guidance without marking sufficient depth.", failures);
            Check(shallow.RepCount == 0,
                "A shallow joint-coordinate movement must not increment the rep count.", failures);

            var noise = RunSquatPipeline(
                BuildStandingKneeNoiseSequence(),
                settings);
            Check(noise.MinimumKneeAngle >= 170f &&
                  !noise.SawBottom &&
                  noise.RepCount == 0,
                "Small standing noise near 175 degrees must never create a Bottom phase or rep.", failures);

            var transformedFrames = TransformPoseSequence(
                SyntheticPoseFixtures.SquatRepSequence(),
                0.82f,
                new Vector2(0.04f, 0.015f),
                true);
            var transformed = RunSquatPipeline(transformedFrames, settings);
            Check(transformed.SawStanding &&
                  transformed.SawDescent &&
                  transformed.SawBottom &&
                  transformed.SawAscent &&
                  transformed.RepCount == 1,
                "Scale, translation, and horizontal mirroring must preserve the squat phases and rep count.", failures);

            var originalBottomFeature = ExtractSinglePoseFeature(
                SyntheticPoseFixtures.SquatBottom(),
                settings);
            var atKneeFeature = ExtractSinglePoseFeature(
                SyntheticPoseFixtures.HipAtKneeSquatBottom(),
                settings);
            var aboveKneeFeature = ExtractSinglePoseFeature(
                SyntheticPoseFixtures.DeepKneeHipAboveSquatBottom(),
                settings);
            Check(atKneeFeature.HasHipToKneeDepth &&
                  Mathf.Abs(atKneeFeature.HipToKneeDepth) < 0.001f,
                "Hip and knee centers at the same y coordinate must extract a zero depth-floor value.", failures);
            Check(aboveKneeFeature.HasHipToKneeDepth &&
                  aboveKneeFeature.HipToKneeDepth < 0f &&
                  aboveKneeFeature.AverageKneeAngle <
                  settings.BottomKneeAngle,
                "The extractor must keep a deeply bent but above-knee hip pose below the hard depth floor.", failures);
            var originalLeftValgus = originalBottomFeature.LeftKneeValgusOffset;
            var originalHipCoordinate = originalBottomFeature.HipCenterY;
            var originalHipToKneeDepth =
                originalBottomFeature.HipToKneeDepth;
            var transformedBottom = TransformPoseSequence(
                new[] { SyntheticPoseFixtures.SquatBottom() },
                0.82f,
                new Vector2(0.04f, 0.015f),
                true)[0];
            var transformedBottomFeature = ExtractSinglePoseFeature(
                transformedBottom,
                settings);
            Check(originalBottomFeature.HasKneeWidthRatio &&
                  transformedBottomFeature.HasKneeWidthRatio &&
                  Mathf.Abs(
                      originalBottomFeature.KneeWidthRatio -
                      transformedBottomFeature.KneeWidthRatio) < 0.01f &&
                  Mathf.Abs(originalLeftValgus - transformedBottomFeature.LeftKneeValgusOffset) < 0.01f &&
                  Mathf.Abs(originalHipCoordinate - transformedBottomFeature.HipCenterY) < 0.01f &&
                  Mathf.Abs(
                      originalHipToKneeDepth -
                      transformedBottomFeature.HipToKneeDepth) < 0.01f,
                "Knee-width ratio, body-scale normalized knee offset, hip coordinate, and hip-to-knee depth must remain stable after scale/translate/mirror transforms.", failures);

            VerifyPoseQualityHysteresisAndSquatScale(settings, failures);
        }

        private static SquatPipelineResult RunSquatPipeline(
            JointTrackingFrame[] frames,
            RealtimePoseRuleSettings settings)
        {
            var result = new SquatPipelineResult();
            var stabilizer = new PoseLandmarkStabilizer();
            var normalizer = new PoseFrameNormalizer();
            var extractor = new PoseFeatureExtractor();
            var detector = new ExercisePhaseDetector();
            var hasSeenAscent = false;

            if (frames == null)
            {
                return result;
            }

            foreach (var frame in frames)
            {
                var stabilized = stabilizer.Stabilize(frame, settings);
                var view = normalizer.Normalize(stabilized, settings.MinimumVisibility);
                var feature = extractor.Extract(view, "squat", settings);
                var phase = detector.Update(feature, settings);
                result.MinimumKneeAngle = Mathf.Min(
                    result.MinimumKneeAngle,
                    feature.AverageKneeAngle);
                if (feature.HasHipToKneeDepth)
                {
                    result.MaximumHipToKneeDepth = Mathf.Max(
                        result.MaximumHipToKneeDepth,
                        feature.HipToKneeDepth);
                }
                result.SawStanding |= phase.CurrentPhase == ExercisePhase.Standing;
                result.SawDescent |= phase.CurrentPhase == ExercisePhase.Descent;
                result.SawBottom |= phase.CurrentPhase == ExercisePhase.Bottom;
                result.SawBottomAfterAscent |=
                    hasSeenAscent &&
                    phase.CurrentPhase == ExercisePhase.Bottom;
                result.SawAscent |= phase.CurrentPhase == ExercisePhase.Ascent;
                hasSeenAscent |= phase.CurrentPhase == ExercisePhase.Ascent;
                result.SawSufficientBottom |=
                    phase.CurrentPhase == ExercisePhase.Bottom &&
                    phase.HasReachedBottomInCurrentRep;
                result.SawBottomWithoutSufficientDepth |=
                    phase.CurrentPhase == ExercisePhase.Bottom &&
                    !phase.HasReachedBottomInCurrentRep;
            }

            result.RepCount = detector.State.RepCount;
            return result;
        }

        private static PoseFeatureFrame ExtractSinglePoseFeature(
            JointTrackingFrame frame,
            RealtimePoseRuleSettings settings)
        {
            var view = new PoseFrameNormalizer().Normalize(
                frame,
                settings.MinimumVisibility);
            return new PoseFeatureExtractor().Extract(view, "squat", settings);
        }

        private static JointTrackingFrame[] BuildStandingKneeNoiseSequence()
        {
            const int frameCount = 10;
            var frames = new JointTrackingFrame[frameCount];
            for (var i = 0; i < frameCount; i++)
            {
                var timestamp = 1000L + i * 100L;
                var offset = i % 2 == 0 ? -0.003f : 0.002f;
                var frame = CloneWithJointOffset(
                    SyntheticPoseFixtures.Standing(),
                    PoseJointNames.LeftKnee,
                    offset,
                    timestamp);
                frames[i] = CloneWithJointOffset(
                    frame,
                    PoseJointNames.RightKnee,
                    -offset,
                    timestamp);
            }

            return frames;
        }

        private static JointTrackingFrame[] TransformPoseSequence(
            JointTrackingFrame[] source,
            float scale,
            Vector2 translation,
            bool mirrorHorizontally)
        {
            if (source == null)
            {
                return Array.Empty<JointTrackingFrame>();
            }

            var transformed = new JointTrackingFrame[source.Length];
            for (var frameIndex = 0; frameIndex < source.Length; frameIndex++)
            {
                var sourceFrame = source[frameIndex];
                if (sourceFrame == null || sourceFrame.joints == null)
                {
                    transformed[frameIndex] = sourceFrame;
                    continue;
                }

                var joints = new TrackedJoint[sourceFrame.joints.Length];
                for (var jointIndex = 0; jointIndex < sourceFrame.joints.Length; jointIndex++)
                {
                    var sourceJoint = sourceFrame.joints[jointIndex];
                    if (sourceJoint == null)
                    {
                        continue;
                    }

                    var x =
                        (sourceJoint.x - 0.5f) * scale +
                        0.5f +
                        translation.x;
                    if (mirrorHorizontally)
                    {
                        x = 1f - x;
                    }

                    joints[jointIndex] = new TrackedJoint
                    {
                        name = sourceJoint.name,
                        x = Mathf.Clamp01(x),
                        y = Mathf.Clamp01(
                            (sourceJoint.y - 0.5f) * scale +
                            0.5f +
                            translation.y),
                        z = sourceJoint.z * scale,
                        visibility = sourceJoint.visibility,
                        confidence = sourceJoint.confidence
                    };
                }

                transformed[frameIndex] = new JointTrackingFrame
                {
                    id = sourceFrame.id,
                    sessionId = sourceFrame.sessionId,
                    timestampUnixMilliseconds = sourceFrame.timestampUnixMilliseconds,
                    joints = joints,
                    feedback = sourceFrame.feedback
                };
            }

            return transformed;
        }

        private static void VerifyPoseQualityHysteresisAndSquatScale(
            RealtimePoseRuleSettings settings,
            ICollection<string> failures)
        {
            var evaluator = new PoseTrackingQualityEvaluator();
            PoseTrackingQualityReport report = null;
            for (var i = 0; i < settings.TrackingQualityGoodFrames; i++)
            {
                var standing = SyntheticPoseFixtures.Standing();
                standing.timestampUnixMilliseconds = 1000L + i * 100L;
                report = evaluator.Evaluate(standing, settings);
            }

            var transientLowConfidence = SyntheticPoseFixtures.LowConfidence();
            transientLowConfidence.timestampUnixMilliseconds = 2000L;
            report = evaluator.Evaluate(transientLowConfidence, settings);
            Check(report.State == PoseTrackingQualityState.Degraded &&
                  report.ShouldHoldPoseAnalysis &&
                  report.CanPreservePoseAnalysis &&
                  !report.RequiresPoseAnalysisReset,
                "One low-confidence frame must hold new analysis while preserving phase/window state.", failures);

            for (var i = 1; i < settings.TrackingQualityUnavailableFrames; i++)
            {
                var lowConfidence = SyntheticPoseFixtures.LowConfidence();
                lowConfidence.timestampUnixMilliseconds = 2000L + i * 100L;
                report = evaluator.Evaluate(lowConfidence, settings);
            }

            Check(report.State == PoseTrackingQualityState.Unavailable &&
                  report.RequiresPoseAnalysisReset &&
                  !report.CanPreservePoseAnalysis,
                "Sustained low-confidence frames must reach the hard-reset tracking state.", failures);

            var bottomEvaluator = new PoseTrackingQualityEvaluator();
            for (var i = 0; i < settings.TrackingQualityGoodFrames; i++)
            {
                var bottom = SyntheticPoseFixtures.SquatBottom();
                bottom.timestampUnixMilliseconds = 3000L + i * 100L;
                report = bottomEvaluator.Evaluate(bottom, settings);
            }

            Check(report.State == PoseTrackingQualityState.Good &&
                  report.BodyHeight >= settings.MinimumTrackedBodyHeight,
                "A deep squat must retain sufficient segment-chain body scale instead of being misclassified as TooSmall.", failures);
        }

        private sealed class SquatPipelineResult
        {
            public bool SawStanding;
            public bool SawDescent;
            public bool SawBottom;
            public bool SawAscent;
            public bool SawSufficientBottom;
            public bool SawBottomWithoutSufficientDepth;
            public bool SawBottomAfterAscent;
            public int RepCount;
            public float MinimumKneeAngle = 180f;
            public float MaximumHipToKneeDepth =
                float.NegativeInfinity;
        }

        private static void VerifyTemporalRepQuality(ICollection<string> failures)
        {
            var settings = new RealtimePoseRuleSettings();
            var accumulator = new RepQualityAccumulator();
            var info = new[]
            {
                new FeedbackEvent { Severity = FeedbackSeverity.Info, PersistenceRatio = 1f }
            };
            for (var i = 0; i < 8; i++) accumulator.Observe(true, info, settings);
            Check(accumulator.IsCorrect(settings),
                "Info and camera guidance must not invalidate a stable rep.", failures);

            accumulator.Reset();
            var transientWarning = new[]
            {
                new FeedbackEvent { Severity = FeedbackSeverity.Warning, PersistenceRatio = 0.2f }
            };
            for (var i = 0; i < 10; i++)
            {
                accumulator.Observe(true, i == 3 ? transientWarning : Array.Empty<FeedbackEvent>(), settings);
            }
            Check(accumulator.IsCorrect(settings),
                "One transient warning frame must not invalidate an otherwise stable rep.", failures);

            // Depth shallow style: low PersistenceRatio (window violation ratio) on a minority of frames.
            accumulator.Reset();
            var lowPersistenceDepthWarning = new[]
            {
                new FeedbackEvent
                {
                    Severity = FeedbackSeverity.Warning,
                    PersistenceRatio = 0.2f,
                    RuleId = "squat_depth_shallow"
                }
            };
            for (var i = 0; i < 10; i++)
            {
                accumulator.Observe(
                    true,
                    i == 2 || i == 7 ? lowPersistenceDepthWarning : Array.Empty<FeedbackEvent>(),
                    settings);
            }
            Check(accumulator.IsCorrect(settings),
                "Low-persistence depth warnings on a minority of frames must keep CorrectRep.", failures);

            accumulator.Reset();
            var correctedDepthWarning = new[]
            {
                new FeedbackEvent
                {
                    Severity = FeedbackSeverity.Warning,
                    PersistenceRatio = 1f,
                    RuleId = "squat_depth_shallow"
                }
            };
            for (var i = 0; i < 8; i++)
            {
                accumulator.Observe(
                    true,
                    i < 5
                        ? correctedDepthWarning
                        : Array.Empty<FeedbackEvent>(),
                    settings);
            }
            Check(accumulator.IsCorrect(settings),
                "A shallow cue corrected before rep completion must not block the main correct score.",
                failures);

            accumulator.Reset();
            var persistentWarning = new[]
            {
                new FeedbackEvent
                {
                    RuleId = "squat_torso_tilt",
                    Severity = FeedbackSeverity.Warning,
                    PersistenceRatio = 0.8f
                }
            };
            accumulator.Observe(true, persistentWarning, settings);
            for (var i = 1; i < 8; i++) accumulator.Observe(true, Array.Empty<FeedbackEvent>(), settings);
            Check(accumulator.IsCorrect(settings),
                "One stale high-persistence warning must remain correctable during a slow rep.", failures);

            accumulator.Reset();
            for (var i = 0; i < 8; i++)
            {
                accumulator.Observe(
                    true,
                    i < 4
                        ? persistentWarning
                        : Array.Empty<FeedbackEvent>(),
                    settings);
            }
            Check(!accumulator.IsCorrect(settings),
                "A repeated same-rule posture warning must still invalidate the rep.", failures);

            var confirmedRules = new List<string>();
            accumulator.CollectConfirmedViolationRuleIds(settings, confirmedRules);
            Check(
                confirmedRules.Count == 1 &&
                confirmedRules[0] == "squat_torso_tilt",
                "Completed-rep reporting must retain the exact confirmed posture rule.",
                failures);
        }

        private static void VerifyDepthUsesMinimumAngle(ICollection<string> failures)
        {
            var settings = new RealtimePoseRuleSettings();
            var buffer = new PoseWindowBuffer(6);
            for (var i = 0; i < 5; i++) buffer.Add(ReliableFeature(170f, 1000L + i * 50L));
            var bottomFeature = ReliableFeature(130f, 1300L);
            buffer.Add(bottomFeature);
            var stats = PoseWindowStats.Calculate(buffer, settings);
            var phase = new ExercisePhaseState
            {
                CurrentPhase = ExercisePhase.Bottom,
                Exercise = "squat",
                HasReachedBottomInCurrentRep = true,
                HasHipToKneeDepth = true,
                HasReachedHipToKneeDepthInCurrentRep = true,
                MaximumHipToKneeDepthInCurrentRep = 0.02f,
                MinimumKneeAngleInCurrentRep = 130f,
                MaximumCountableBottomKneeAngle =
                    settings.MaximumCountableBottomKneeAngle,
                MinimumBottomHipDrop = settings.MinimumBottomHipDrop
            };
            var candidates = new RealtimePoseRuleEngine().Evaluate(bottomFeature, stats, phase, settings);
            var hasShallowError = false;
            foreach (var candidate in candidates)
            {
                if (candidate.RuleId == "squat_depth_hip_height" ||
                    candidate.RuleId == "squat_depth_personal_target")
                {
                    hasShallowError = true;
                }
            }

            Check(Mathf.Approximately(stats.MinimumKneeAngle, 130f),
                "Depth evaluation must retain the minimum knee angle in the analysis window.", failures);
            Check(!hasShallowError,
                "Standing frames in the analysis window must not make a sufficiently deep squat look shallow.", failures);
        }

        private static void VerifyHipToKneeDepthFloorFeedback(
            ICollection<string> failures)
        {
            var settings = new RealtimePoseRuleSettings();
            var buffer = new PoseWindowBuffer(8);
            for (var i = 0; i < 6; i++) buffer.Add(ReliableFeature(172f, 1000L + i * 50L));
            var bottomFeature = ReliableFeature(172f, 1400L);
            bottomFeature.HipToKneeDepth = -0.04f;
            buffer.Add(bottomFeature);
            var stats = PoseWindowStats.Calculate(buffer, settings);
            var phase = new ExercisePhaseState
            {
                CurrentPhase = ExercisePhase.Bottom,
                Exercise = "squat",
                HasReachedBottomInCurrentRep = false,
                HasHipToKneeDepth = true,
                CurrentHipToKneeDepth = -0.04f,
                MaximumHipToKneeDepthInCurrentRep = -0.04f,
                RequiredHipToKneeDepth =
                    settings.MinimumAcceptedHipToKneeDepth
            };
            var candidates = new RealtimePoseRuleEngine().Evaluate(bottomFeature, stats, phase, settings);

            FeedbackEvent shallow = null;
            foreach (var candidate in candidates)
            {
                if (candidate.RuleId == "squat_depth_hip_height")
                {
                    shallow = candidate;
                    break;
                }
            }

            Check(shallow != null,
                "A Bottom pose with hips above knee height must emit squat_depth_hip_height guidance.", failures);
            Check(shallow != null &&
                  shallow.Severity == FeedbackSeverity.Warning,
                "The absolute hip-to-knee floor must be a Warning so it cannot count as a correct rep.", failures);
            Check(shallow != null &&
                  shallow.Evidence.TryGetValue(
                      "hipToKneeDepth",
                      out var evidence) &&
                  Mathf.Approximately(evidence, -0.04f),
                "Depth-floor feedback must expose the signed normalized hip-to-knee evidence.", failures);
            Check(shallow != null &&
                  shallow.PreferTemplateText &&
                  shallow.TemplateText ==
                  "엉덩이와 무릎 높이가 충분히 가까워지지 않았습니다. 엉덩이를 조금 더 내려 주세요.",
                "Stage-1 depth failure must explain that consecutive hip/knee level confirmation failed.",
                failures);
            var genericDepthRetrieval = new List<RetrievalResult>
            {
                new RetrievalResult
                {
                    Chunk = new KnowledgeChunk
                    {
                        RealtimeText =
                            "원인 구분이 없는 공통 깊이 안내입니다."
                    },
                    Score = 10f
                }
            };
            var primaryMessage = new FeedbackComposer().Compose(
                shallow,
                genericDepthRetrieval,
                200);
            Check(primaryMessage != null &&
                  primaryMessage.text == shallow.TemplateText,
                "Stage-specific depth guidance must reach TTS without being replaced by generic RAG text.",
                failures);

            var repeatedCandidates = new RealtimePoseRuleEngine().Evaluate(
                bottomFeature,
                stats,
                phase,
                settings);
            var repeatedShallow = false;
            foreach (var candidate in repeatedCandidates)
            {
                if (candidate.RuleId == "squat_depth_hip_height")
                {
                    repeatedShallow = true;
                    break;
                }
            }

            Check(repeatedShallow &&
                  !phase.HasIssuedShallowDepthFeedbackInCurrentRep,
                "A depth cue not yet admitted to TTS must remain eligible on the next Bottom frame.",
                failures);
            phase.HasIssuedBottomDecisionFeedbackInCurrentRep = true;
            var deliveredCandidates = new RealtimePoseRuleEngine().Evaluate(
                bottomFeature,
                stats,
                phase,
                settings);
            var repeatedAfterDelivery = false;
            foreach (var candidate in deliveredCandidates)
            {
                if (candidate.RuleId == "squat_depth_hip_height")
                {
                    repeatedAfterDelivery = true;
                    break;
                }
            }

            Check(repeatedAfterDelivery &&
                  phase.HasIssuedBottomDecisionFeedbackInCurrentRep,
                "The rule event must remain observable for rep scoring while the orchestrator limits TTS to once per rep.",
                failures);

            var secondaryPhase = new ExercisePhaseState
            {
                CurrentPhase = ExercisePhase.Bottom,
                Exercise = "squat",
                HasHipToKneeDepth = true,
                CurrentHipToKneeDepth = -0.01f,
                MaximumHipToKneeDepthInCurrentRep = -0.01f,
                HasReachedHipToKneeDepthInCurrentRep = true,
                HasReachedSecondaryDepthInCurrentRep = false,
                MinimumKneeAngleInCurrentRep = 150f,
                RequiredHipToKneeDepth =
                    settings.MinimumAcceptedHipToKneeDepth,
                MaximumCountableBottomKneeAngle =
                    settings.MaximumCountableBottomKneeAngle
            };
            var secondaryCandidates =
                new RealtimePoseRuleEngine().Evaluate(
                    ReliableFeature(150f, 1500L),
                    stats,
                    secondaryPhase,
                    settings);
            FeedbackEvent secondaryDepth = null;
            foreach (var candidate in secondaryCandidates)
            {
                if (candidate.RuleId == "squat_depth_personal_target")
                {
                    secondaryDepth = candidate;
                    break;
                }
            }

            Check(secondaryDepth != null &&
                  secondaryDepth.Evidence.TryGetValue(
                      "minimumKneeAngle",
                      out var secondaryEvidence) &&
                  Mathf.Approximately(secondaryEvidence, 150f),
                "A pose that passes 2D hip/knee level must still expose secondary-depth evidence when flexion and hip drop are insufficient.",
                failures);
            Check(secondaryDepth != null &&
                  secondaryDepth.PreferTemplateText &&
                  secondaryDepth.TemplateText ==
                  "정렬은 좋습니다. 현재 가능한 범위에서 조금 더 앉아 주세요.",
                "The personal-depth failure must keep its dedicated TTS text.",
                failures);
            var secondaryMessage = new FeedbackComposer().Compose(
                secondaryDepth,
                genericDepthRetrieval,
                200);
            Check(secondaryMessage != null &&
                  secondaryMessage.text == secondaryDepth.TemplateText,
                "Stage-2 depth guidance must reach TTS without being replaced by generic RAG text.",
                failures);
        }

        private static void VerifyBottomReachedSuppressesShallowWarning(ICollection<string> failures)
        {
            var settings = new RealtimePoseRuleSettings();
            // Window has no usable knee samples (MinimumKneeAngle=0); depth must come from rep min.
            var stats = new PoseWindowStats
            {
                FrameCount = settings.minimumRuleEvaluationFrames,
                ValidCoreFrameCount = settings.minimumRuleEvaluationFrames,
                ValidCoreFrameRatio = 1f,
                MinimumKneeAngle = 0f,
                ShallowDepthViolationRatio = 0.5f
            };
            var phase = new ExercisePhaseState
            {
                CurrentPhase = ExercisePhase.Bottom,
                Exercise = "squat",
                HasReachedBottomInCurrentRep = true,
                HasReachedHipToKneeDepthInCurrentRep = true,
                MinimumKneeAngleInCurrentRep = 130f,
                MaximumCountableBottomKneeAngle =
                    settings.MaximumCountableBottomKneeAngle,
                MinimumBottomHipDrop = settings.MinimumBottomHipDrop
            };
            var feature = ReliableFeature(172f, 2000L);
            var candidates = new RealtimePoseRuleEngine().Evaluate(feature, stats, phase, settings);

            FeedbackEvent shallow = null;
            foreach (var candidate in candidates)
            {
                if (candidate.RuleId == "squat_depth_hip_height" ||
                    candidate.RuleId == "squat_depth_personal_target")
                {
                    shallow = candidate;
                    break;
                }
            }

            Check(shallow == null,
                "A sequentially passed Bottom must not emit either shallow-depth decision.", failures);
        }

        private static void VerifySequentialBottomDecision(
            ICollection<string> failures)
        {
            var settings = new RealtimePoseRuleSettings();
            var feature = ReliableFeature(145f, 1900L);
            var stableStats = new PoseWindowStats
            {
                FrameCount = settings.minimumRuleEvaluationFrames,
                ValidCoreFrameCount =
                    settings.minimumRuleEvaluationFrames,
                ValidCoreFrameRatio = 1f,
                MinimumKneeAngle = 145f
            };

            var hipFailure = new ExercisePhaseState
            {
                CurrentPhase = ExercisePhase.Bottom,
                Exercise = "squat",
                HasHipToKneeDepth = true,
                MaximumHipToKneeDepthInCurrentRep = -0.04f,
                MinimumKneeAngleInCurrentRep = 145f,
                MaximumCountableBottomKneeAngle =
                    settings.MaximumCountableBottomKneeAngle,
                MinimumBottomHipDrop = settings.MinimumBottomHipDrop
            };
            var hipCandidates = new RealtimePoseRuleEngine().Evaluate(
                feature,
                stableStats,
                hipFailure,
                settings);
            Check(
                hipCandidates.Count == 1 &&
                hipCandidates[0].RuleId ==
                    "squat_depth_hip_height" &&
                hipFailure.CurrentBottomDecision ==
                    SquatBottomDecision.HipHeightFailed,
                "The first sequential gate must emit only HipHeightFailed.",
                failures);

            feature.HasKneeWidthRatio = true;
            feature.KneeWidthRatio = 0.65f;
            feature.HasLeftKneeValgus = true;
            feature.LeftKneeValgusOffset = 0.2f;
            var collapseStats = new PoseWindowStats
            {
                FrameCount = settings.minimumRuleEvaluationFrames,
                ValidCoreFrameCount =
                    settings.minimumRuleEvaluationFrames,
                ValidCoreFrameRatio = 1f,
                KneeWidthObservationRatio = 1f,
                MinimumKneeWidthRatio = 0.65f,
                KneeCollapseViolationRatio = 1f,
                MaximumConsecutiveKneeCollapseFrames = 2,
                KneeAlignmentViolationRatio = 1f,
                MinimumKneeAngle = 145f
            };
            var collapsePhase = new ExercisePhaseState
            {
                CurrentPhase = ExercisePhase.Bottom,
                Exercise = "squat",
                HasReachedHipToKneeDepthInCurrentRep = true,
                MinimumKneeAngleInCurrentRep = 145f,
                MaximumHipDropInCurrentRep = 0.06f,
                MaximumCountableBottomKneeAngle =
                    settings.MaximumCountableBottomKneeAngle,
                MinimumBottomHipDrop = settings.MinimumBottomHipDrop
            };
            var collapseCandidates =
                new RealtimePoseRuleEngine().Evaluate(
                    feature,
                    collapseStats,
                    collapsePhase,
                    settings);
            Check(
                collapseCandidates.Count == 1 &&
                collapseCandidates[0].RuleId ==
                    "squat_knee_collapse" &&
                collapsePhase.CurrentBottomDecision ==
                    SquatBottomDecision.KneeCollapseFailed,
                "A confirmed inward knee collapse must be the only second-gate decision.",
                failures);

            feature.HasKneeWidthRatio = false;
            feature.HasLeftKneeValgus = false;
            var personalPhase = new ExercisePhaseState
            {
                CurrentPhase = ExercisePhase.Bottom,
                Exercise = "squat",
                HasReachedHipToKneeDepthInCurrentRep = true,
                MinimumKneeAngleInCurrentRep = 145f,
                MaximumHipDropInCurrentRep = 0.06f,
                MaximumCountableBottomKneeAngle =
                    settings.MaximumCountableBottomKneeAngle,
                MinimumBottomHipDrop = settings.MinimumBottomHipDrop
            };
            var personalCandidates =
                new RealtimePoseRuleEngine().Evaluate(
                    feature,
                    stableStats,
                    personalPhase,
                    settings);
            Check(
                personalCandidates.Count == 1 &&
                personalCandidates[0].RuleId ==
                    "squat_depth_personal_target" &&
                personalPhase.CurrentBottomDecision ==
                    SquatBottomDecision.PersonalDepthFailed,
                "After height and alignment pass, only PersonalDepthFailed may be emitted.",
                failures);

            var passedPhase = new ExercisePhaseState
            {
                CurrentPhase = ExercisePhase.Bottom,
                Exercise = "squat",
                HasReachedHipToKneeDepthInCurrentRep = true,
                MinimumKneeAngleInCurrentRep = 130f,
                MaximumHipDropInCurrentRep = 0.06f,
                MaximumCountableBottomKneeAngle =
                    settings.MaximumCountableBottomKneeAngle,
                MinimumBottomHipDrop = settings.MinimumBottomHipDrop
            };
            var passedCandidates =
                new RealtimePoseRuleEngine().Evaluate(
                    ReliableFeature(130f, 1950L),
                    stableStats,
                    passedPhase,
                    settings);
            Check(
                passedCandidates.Count == 0 &&
                passedPhase.CurrentBottomDecision ==
                    SquatBottomDecision.Passed &&
                passedPhase.HasPassedBottomDecisionInCurrentRep,
                "A passed Bottom must remain silent until Standing completes the rep.",
                failures);

            var deepStats = new PoseWindowStats
            {
                FrameCount = settings.minimumRuleEvaluationFrames,
                ValidCoreFrameCount =
                    settings.minimumRuleEvaluationFrames,
                ValidCoreFrameRatio = 1f,
                MinimumKneeAngle = 40f,
                DeepDepthViolationRatio = 1f,
                MaximumConsecutiveExcessiveDepthFrames = 3
            };
            var deepPhase = new ExercisePhaseState
            {
                CurrentPhase = ExercisePhase.Bottom,
                Exercise = "squat",
                HasReachedHipToKneeDepthInCurrentRep = true,
                MinimumKneeAngleInCurrentRep = 40f,
                MaximumCountableBottomKneeAngle =
                    settings.MaximumCountableBottomKneeAngle,
                MinimumBottomHipDrop = settings.MinimumBottomHipDrop
            };
            var deepCandidates =
                new RealtimePoseRuleEngine().Evaluate(
                    ReliableFeature(40f, 2000L),
                    deepStats,
                    deepPhase,
                    settings);
            Check(
                deepCandidates.Count == 0 &&
                deepPhase.CurrentBottomDecision ==
                    SquatBottomDecision.Passed &&
                deepPhase.HasPassedBottomDecisionInCurrentRep,
                "A deep squat must pass after the height, alignment, and personal-depth gates.",
                failures);
        }

        private static void VerifySessionDepthPersonalization(
            ICollection<string> failures)
        {
            var settings = new RealtimePoseRuleSettings();
            var detector = new ExercisePhaseDetector();
            var first = detector.RegisterPersonalDepthFailureCandidate(
                145f,
                0.06f,
                settings);
            var second = detector.RegisterPersonalDepthFailureCandidate(
                146f,
                0.061f,
                settings);
            var third = detector.RegisterPersonalDepthFailureCandidate(
                144f,
                0.059f,
                settings);
            Check(
                !first &&
                !second &&
                third &&
                detector.State.HasPersonalizedDepthProfile &&
                Mathf.Abs(
                    detector.State.MaximumCountableBottomKneeAngle -
                    148f) < 0.001f &&
                Mathf.Abs(
                    detector.State.MinimumBottomHipDrop -
                    0.05f) < 0.001f,
                "Three consistent personal-depth failures must adapt only the next-rep targets inside the safety clamps.",
                failures);
            Check(
                detector.ConsumePersonalizedDepthAnnouncement() &&
                !detector.ConsumePersonalizedDepthAnnouncement(),
                "The personalized-depth announcement must be consumable only once.",
                failures);

            var inconsistent = new ExercisePhaseDetector();
            inconsistent.RegisterPersonalDepthFailureCandidate(
                140f,
                0.05f,
                settings);
            inconsistent.RegisterPersonalDepthFailureCandidate(
                152f,
                0.075f,
                settings);
            var inconsistentApplied =
                inconsistent.RegisterPersonalDepthFailureCandidate(
                    145f,
                    0.06f,
                    settings);
            Check(
                !inconsistentApplied &&
                !inconsistent.State.HasPersonalizedDepthProfile &&
                inconsistent.State.PersonalDepthFailureSampleCount == 0,
                "Inconsistent failed depths must reset the candidate sequence without relaxing the target.",
                failures);

            detector.Reset();
            Check(
                !detector.State.HasPersonalizedDepthProfile &&
                detector.State.MaximumCountableBottomKneeAngle == 0f,
                "A new-session reset must clear the runtime personalized depth profile.",
                failures);
        }

        private static void VerifyDeepSquatCountsAsCorrect(
            ICollection<string> failures)
        {
            var settings = new RealtimePoseRuleSettings();
            var stats = new PoseWindowStats
            {
                FrameCount = settings.minimumRuleEvaluationFrames,
                ValidCoreFrameCount = settings.minimumRuleEvaluationFrames,
                ValidCoreFrameRatio = 1f,
                MinimumKneeAngle = 40f,
                DeepDepthViolationRatio = 1f,
                MaximumConsecutiveExcessiveDepthFrames = 2
            };
            var phase = new ExercisePhaseState
            {
                CurrentPhase = ExercisePhase.Bottom,
                Exercise = "squat",
                HasReachedBottomInCurrentRep = true,
                HasReachedHipToKneeDepthInCurrentRep = true,
                MinimumKneeAngleInCurrentRep = 40f,
                MaximumCountableBottomKneeAngle =
                    settings.MaximumCountableBottomKneeAngle,
                MinimumBottomHipDrop = settings.MinimumBottomHipDrop
            };
            var feature = ReliableFeature(40f, 2100L);
            var candidates = new RealtimePoseRuleEngine().Evaluate(
                feature,
                stats,
                phase,
                settings);

            Check(
                candidates.Count == 0 &&
                phase.CurrentBottomDecision ==
                    SquatBottomDecision.Passed &&
                phase.HasPassedBottomDecisionInCurrentRep,
                "Consecutive deep frames must be accepted as a passed squat without a warning event.",
                failures);

            var accumulator = new RepQualityAccumulator();
            var retiredProviderEvent = new[]
            {
                new FeedbackEvent
                {
                    RuleId = "squat_depth_excessive",
                    Severity = FeedbackSeverity.Warning,
                    PersistenceRatio = 1f
                }
            };
            for (var i = 0; i < settings.minimumValidRepFrames + 2; i++)
            {
                accumulator.Observe(
                    true,
                    retiredProviderEvent,
                    settings);
            }

            Check(
                accumulator.IsCorrect(settings),
                "A retired excessive-depth event from an older provider must not invalidate CorrectRep.",
                failures);

            var receiverObject =
                new GameObject("Deep Feedback Suppression QA");
            try
            {
                var receiver =
                    receiverObject.AddComponent<PoseFeedbackJsonReceiver>();
                var accepted = receiver.ReceiveFeedback(
                    new PoseFeedbackMessage
                    {
                        id = "squat_depth_excessive",
                        text =
                            "너무 깊게 내려갔습니다. 깊이를 조금 줄여 주세요.",
                        confidence = 1f,
                        severity = FeedbackSeverity.Warning
                    });
                Check(
                    !accepted &&
                    receiver.LatestFeedback == null,
                    "Legacy excessive-depth messages must be rejected before UI and TTS delivery.",
                    failures);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(receiverObject);
            }
        }

        private static void VerifyKneeAlignmentPhaseGateAndSeverity(ICollection<string> failures)
        {
            var settings = new RealtimePoseRuleSettings();
            var feature = ReliableFeature(100f, 1500L);
            feature.HasRightKneeValgus = true;
            feature.RightKneeValgusOffset = settings.MaximumKneeValgusOffset * 2f;
            var stats = new PoseWindowStats
            {
                FrameCount = settings.minimumRuleEvaluationFrames,
                ValidCoreFrameCount = settings.minimumRuleEvaluationFrames,
                ValidCoreFrameRatio = 1f,
                RightKneeObservationRatio = 1f,
                RightKneeAlignmentViolationRatio = 1f
            };
            var standingPhase = new ExercisePhaseState { CurrentPhase = ExercisePhase.Standing, Exercise = "squat" };
            var standingEvents = new RealtimePoseRuleEngine().Evaluate(feature, stats, standingPhase, settings);
            var standingKneeAlignment = false;
            foreach (var candidate in standingEvents)
            {
                if (candidate.RuleId == "squat_knee_alignment") standingKneeAlignment = true;
            }

            Check(!standingKneeAlignment,
                "Standing phase must not emit knee alignment events even with high valgus.", failures);

            feature.RightKneeValgusOffset = settings.MaximumKneeValgusOffset * 1.2f;
            var descentPhase = new ExercisePhaseState { CurrentPhase = ExercisePhase.Descent, Exercise = "squat" };
            var bottomEvents = new RealtimePoseRuleEngine().Evaluate(feature, stats, descentPhase, settings);
            FeedbackEvent mild = null;
            foreach (var candidate in bottomEvents)
            {
                if (candidate.RuleId == "squat_knee_alignment")
                {
                    mild = candidate;
                    break;
                }
            }

            Check(mild != null,
                "Active descent with mild valgus must emit a knee alignment event.", failures);
            Check(mild != null && mild.Severity == FeedbackSeverity.Info,
                "Mild valgus (offset <= MaximumKneeValgusOffset * 1.4) must be Info, not Warning.", failures);
            Check(mild != null && mild.Side == "right",
                "Mild valgus fixture must attribute the event to the right knee.", failures);
        }

        private static void VerifyTorsoAndPelvicGeometry(ICollection<string> failures)
        {
            var settings = new RealtimePoseRuleSettings();
            var joints = new[]
            {
                Joint(PoseJointNames.LeftShoulder, 0.4f, 0.3f),
                Joint(PoseJointNames.RightShoulder, 0.6f, 0.3f),
                Joint(PoseJointNames.LeftHip, 0.4f, 0.55f),
                Joint(PoseJointNames.RightHip, 0.6f, 0.55f),
                Joint(PoseJointNames.LeftKnee, 0.4f, 0.75f),
                Joint(PoseJointNames.RightKnee, 0.6f, 0.75f),
                Joint(PoseJointNames.LeftAnkle, 0.4f, 0.95f),
                Joint(PoseJointNames.RightAnkle, 0.6f, 0.95f)
            };
            var view = new PoseFrameView();
            view.Reset(new JointTrackingFrame { timestampUnixMilliseconds = 1000L, joints = joints }, settings.MinimumVisibility);
            var extractor = new PoseFeatureExtractor();
            var upright = extractor.Extract(view, "squat", settings.MinimumVisibility);

            Check(upright.HasTorsoTilt && upright.TorsoTiltDegrees < 0.1f,
                "Upright shoulders above hips must produce approximately 0° torso tilt in image coordinates.", failures);
            Check(upright.HasPelvicTilt && upright.PelvicTiltRatio < 0.01f,
                "Level hips must produce approximately zero normalized pelvic tilt.", failures);

            joints[3].y = 0.63f; // relative hip/shoulder line angle => tan(angle) = 0.40
            view.Reset(new JointTrackingFrame { timestampUnixMilliseconds = 1100L, joints = joints }, settings.MinimumVisibility);
            var tilted = extractor.Extract(view, "squat", settings.MinimumVisibility);
            Check(tilted.HasPelvicTilt && Mathf.Abs(tilted.PelvicTiltRatio - 0.4f) < 0.01f,
                "Pelvic tilt must be measured relative to the shoulder line.", failures);

            var stats = new PoseWindowStats
            {
                FrameCount = settings.minimumRuleEvaluationFrames,
                ValidCoreFrameCount = settings.minimumRuleEvaluationFrames,
                ValidCoreFrameRatio = 1f,
                AveragePelvicTiltRatio = tilted.PelvicTiltRatio,
                PelvicTiltViolationRatio = 1f
            };
            var events = new RealtimePoseRuleEngine().Evaluate(
                tilted,
                stats,
                new ExercisePhaseState { CurrentPhase = ExercisePhase.Descent, Exercise = "squat" },
                settings);
            var hasPelvicFeedback = false;
            foreach (var candidate in events)
            {
                if (candidate.RuleId == "squat_pelvic_tilt")
                {
                    hasPelvicFeedback = true;
                    break;
                }
            }

            Check(hasPelvicFeedback,
                "Persistent normalized pelvic tilt must emit squat_pelvic_tilt feedback.", failures);

            var standingEvents = new RealtimePoseRuleEngine().Evaluate(
                tilted,
                stats,
                new ExercisePhaseState { CurrentPhase = ExercisePhase.Standing, Exercise = "squat" },
                settings);
            var hasStandingPelvicFeedback = false;
            foreach (var candidate in standingEvents)
            {
                if (candidate.RuleId == "squat_pelvic_tilt")
                {
                    hasStandingPelvicFeedback = true;
                    break;
                }
            }
            Check(!hasStandingPelvicFeedback,
                "Standing phase must not emit pelvis-alignment feedback.", failures);

            // A rolled camera affects shoulder and hip lines equally. It must not be
            // reported as a pelvis-only asymmetry.
            joints[0].y = 0.34f;
            joints[1].y = 0.38f;
            joints[2].y = 0.59f;
            joints[3].y = 0.63f;
            view.Reset(new JointTrackingFrame { timestampUnixMilliseconds = 1200L, joints = joints }, settings.MinimumVisibility);
            var cameraRolled = extractor.Extract(view, "squat", settings.MinimumVisibility);
            Check(cameraRolled.HasPelvicTilt && cameraRolled.PelvicTiltRatio < 0.01f,
                "Equal shoulder and hip slopes must be treated as camera/body roll, not pelvic tilt.", failures);
        }

        private static TrackedJoint Joint(string name, float x, float y)
        {
            return new TrackedJoint
            {
                name = name,
                x = x,
                y = y,
                visibility = 1f,
                confidence = 1f
            };
        }

        private static void VerifyAnalysisWindowUsesTimestamps(ICollection<string> failures)
        {
            var settings = new RealtimePoseRuleSettings();
            var buffer = new PoseWindowBuffer(8);
            // Old deep frame outside a 1.2s window should not affect minimum knee angle.
            buffer.Add(ReliableFeature(90f, 1000L));
            for (var i = 0; i < 4; i++)
            {
                buffer.Add(ReliableFeature(170f, 3000L + i * 100L));
            }

            buffer.Add(ReliableFeature(150f, 3400L));
            var reusable = new PoseWindowStats();
            var stats = PoseWindowStats.Calculate(buffer, settings, reusable, 1.2f);
            Check(ReferenceEquals(reusable, stats),
                "Timed window statistics must support caller-owned result reuse.", failures);
            Check(stats.FrameCount == 5,
                "Pose window stats must exclude frames older than analysisWindowSeconds.", failures);
            Check(Mathf.Approximately(stats.MinimumKneeAngle, 150f),
                "Minimum knee angle must ignore samples outside the analysis time window.", failures);

            var shortBuffer = new PoseWindowBuffer(4);
            shortBuffer.Add(ReliableFeature(100f, 1000L));
            shortBuffer.Add(ReliableFeature(160f, 4000L));
            var shortStats = PoseWindowStats.Calculate(shortBuffer, settings, new PoseWindowStats(), 0.5f);
            Check(shortStats.FrameCount == 2,
                "Timed window must keep available newest samples when fewer than the minimum sample floor exist.", failures);
            Check(Mathf.Approximately(shortStats.MinimumKneeAngle, 100f),
                "Sparse timed windows must expand to the newest minimum samples even if older than the cutoff.", failures);
        }

        private static PoseFeatureFrame PhaseFeature(
            float kneeAngle,
            float velocity,
            long timestamp,
            float hipToKneeDepth = 0.05f)
        {
            return new PoseFeatureFrame
            {
                Exercise = "squat",
                TimestampUnixMilliseconds = timestamp,
                HasLeftKneeAngle = true,
                HasRightKneeAngle = true,
                HasHipToKneeDepth = true,
                HipToKneeDepth = hipToKneeDepth,
                AverageKneeAngle = kneeAngle,
                KneeAngleVelocityDegreesPerSecond = velocity
            };
        }

        private static PoseFeatureFrame ReliableFeature(float kneeAngle, long timestamp)
        {
            return new PoseFeatureFrame
            {
                Exercise = "squat",
                TimestampUnixMilliseconds = timestamp,
                HasLeftKneeAngle = true,
                HasRightKneeAngle = true,
                HasTorsoTilt = true,
                HasCenterBalance = true,
                HasHipToKneeDepth = true,
                HipToKneeDepth = 0.05f,
                AverageKneeAngle = kneeAngle
            };
        }

        private static JointTrackingFrame CloneWithJointOffset(
            JointTrackingFrame source,
            string jointName,
            float offsetX,
            long timestamp)
        {
            var joints = new TrackedJoint[source.joints.Length];
            for (var i = 0; i < source.joints.Length; i++)
            {
                var joint = source.joints[i];
                joints[i] = new TrackedJoint
                {
                    name = joint.name,
                    x = joint.x + (joint.name == jointName ? offsetX : 0f),
                    y = joint.y,
                    z = joint.z,
                    visibility = joint.visibility,
                    confidence = joint.confidence
                };
            }

            return new JointTrackingFrame
            {
                id = source.id,
                sessionId = source.sessionId,
                timestampUnixMilliseconds = timestamp,
                joints = joints,
                feedback = source.feedback
            };
        }
    }
}
