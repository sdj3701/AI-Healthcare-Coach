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
using Rag.Healthcare.Rag.Runtime;
using Rag.Healthcare.Rag.Rules;
using Rag.Healthcare.Replay;
using Rag.Healthcare.Reports;
using Rag.Healthcare.Tts;
using Rag.Healthcare.UI;
using UnityEditor;
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

            VerifyLandmarkStability(failures);
            VerifyFrontCameraTrackingQuality(failures);
            VerifyWorkoutSessionLifecycle(failures);
            VerifyPersonalizedRomSafety(failures);
            VerifyProfileCompletionGate(failures);
            VerifyHotPathObjectReuse(failures);
            VerifyPhaseReversalRecognition(failures);
            VerifyJointCoordinateSquatPipeline(failures);
            VerifyDepthUsesMinimumAngle(failures);
            VerifyShallowDepthInfoBand(failures);
            VerifyBottomReachedSuppressesShallowWarning(failures);
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
            var originalLeftValgus = originalBottomFeature.LeftKneeValgusOffset;
            var originalHipCoordinate = originalBottomFeature.HipCenterY;
            var transformedBottom = TransformPoseSequence(
                new[] { SyntheticPoseFixtures.SquatBottom() },
                0.82f,
                new Vector2(0.04f, 0.015f),
                true)[0];
            var transformedBottomFeature = ExtractSinglePoseFeature(
                transformedBottom,
                settings);
            Check(Mathf.Abs(originalLeftValgus - transformedBottomFeature.LeftKneeValgusOffset) < 0.01f &&
                  Mathf.Abs(originalHipCoordinate - transformedBottomFeature.HipCenterY) < 0.01f,
                "Body-scale normalized knee offset and hip coordinate must remain stable after scale/translate/mirror transforms.", failures);

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
            var persistentWarning = new[]
            {
                new FeedbackEvent { Severity = FeedbackSeverity.Warning, PersistenceRatio = 0.8f }
            };
            accumulator.Observe(true, persistentWarning, settings);
            for (var i = 1; i < 8; i++) accumulator.Observe(true, Array.Empty<FeedbackEvent>(), settings);
            Check(!accumulator.IsCorrect(settings),
                "A high-persistence warning must still invalidate the rep.", failures);
        }

        private static void VerifyDepthUsesMinimumAngle(ICollection<string> failures)
        {
            var settings = new RealtimePoseRuleSettings();
            var buffer = new PoseWindowBuffer(6);
            for (var i = 0; i < 5; i++) buffer.Add(ReliableFeature(170f, 1000L + i * 50L));
            var bottomFeature = ReliableFeature(130f, 1300L);
            buffer.Add(bottomFeature);
            var stats = PoseWindowStats.Calculate(buffer, settings);
            var phase = new ExercisePhaseState { CurrentPhase = ExercisePhase.Bottom, Exercise = "squat" };
            var candidates = new RealtimePoseRuleEngine().Evaluate(bottomFeature, stats, phase, settings);
            var hasShallowError = false;
            foreach (var candidate in candidates)
            {
                if (candidate.RuleId == "squat_depth_shallow") hasShallowError = true;
            }

            Check(Mathf.Approximately(stats.MinimumKneeAngle, 130f),
                "Depth evaluation must retain the minimum knee angle in the analysis window.", failures);
            Check(!hasShallowError,
                "Standing frames in the analysis window must not make a sufficiently deep squat look shallow.", failures);
        }

        private static void VerifyShallowDepthInfoBand(ICollection<string> failures)
        {
            var settings = new RealtimePoseRuleSettings();
            var buffer = new PoseWindowBuffer(8);
            // Info band: maximumBottomKneeAngle(170) < depth ≤ maximumRecognizableBottomKneeAngle(175)
            // HasReachedBottom=false so Info shallow can still fire in the rare Bottom-without-flag path.
            for (var i = 0; i < 6; i++) buffer.Add(ReliableFeature(172f, 1000L + i * 50L));
            var bottomFeature = ReliableFeature(172f, 1400L);
            buffer.Add(bottomFeature);
            var stats = PoseWindowStats.Calculate(buffer, settings);
            var phase = new ExercisePhaseState
            {
                CurrentPhase = ExercisePhase.Bottom,
                Exercise = "squat",
                HasReachedBottomInCurrentRep = false
            };
            var candidates = new RealtimePoseRuleEngine().Evaluate(bottomFeature, stats, phase, settings);

            FeedbackEvent shallow = null;
            foreach (var candidate in candidates)
            {
                if (candidate.RuleId == "squat_depth_shallow")
                {
                    shallow = candidate;
                    break;
                }
            }

            Check(shallow != null,
                "170–175° bottom depth (reachedBottom=false) must emit squat_depth_shallow guidance.", failures);
            Check(shallow != null && shallow.Severity == FeedbackSeverity.Info,
                "170–175° bottom depth must be Info, not Warning, so CorrectRep is not forced false.", failures);
            Check(Mathf.Approximately(stats.MinimumKneeAngle, 172f),
                "Info-band fixture must keep MinimumKneeAngle at 172°.", failures);
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
                MinimumKneeAngleInCurrentRep = 172f
            };
            var feature = ReliableFeature(172f, 2000L);
            var candidates = new RealtimePoseRuleEngine().Evaluate(feature, stats, phase, settings);

            FeedbackEvent shallow = null;
            foreach (var candidate in candidates)
            {
                if (candidate.RuleId == "squat_depth_shallow")
                {
                    shallow = candidate;
                    break;
                }
            }

            Check(shallow == null,
                "Bottom + HasReachedBottomInCurrentRep must not emit squat_depth_shallow (no shallow nagging).", failures);
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
            var bottomPhase = new ExercisePhaseState { CurrentPhase = ExercisePhase.Bottom, Exercise = "squat" };
            var bottomEvents = new RealtimePoseRuleEngine().Evaluate(feature, stats, bottomPhase, settings);
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
                "Bottom phase with mild valgus must emit a knee alignment event.", failures);
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

        private static PoseFeatureFrame PhaseFeature(float kneeAngle, float velocity, long timestamp)
        {
            return new PoseFeatureFrame
            {
                Exercise = "squat",
                TimestampUnixMilliseconds = timestamp,
                HasLeftKneeAngle = true,
                HasRightKneeAngle = true,
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
