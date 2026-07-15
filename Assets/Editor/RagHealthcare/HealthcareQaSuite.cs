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
using Rag.Healthcare.Privacy;
using Rag.Healthcare.Qa;
using Rag.Healthcare.Rag.Runtime;
using Rag.Healthcare.Rag.Rules;
using Rag.Healthcare.Replay;
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
            VerifyHotPathObjectReuse(failures);
            VerifyPhaseReversalRecognition(failures);
            VerifyDepthUsesMinimumAngle(failures);
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

                var stepField = typeof(MobileWorkoutPrototypeView).GetField(
                    "currentStep",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                var renderMethod = typeof(MobileWorkoutPrototypeView).GetMethod(
                    "RenderCurrentStep",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (stepField != null && renderMethod != null)
                {
                    stepField.SetValue(view, Enum.ToObject(stepField.FieldType, 3));
                    renderMethod.Invoke(view, null);
                }

                var preview = documentRoot?.Q<Image>("camera-or-replay-preview");
                var overlay = documentRoot?.Q<VisualElement>("pose-overlay");
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
            var mapMethod = typeof(MobileWorkoutPrototypeView).GetMethod(
                "ToPreviewPoint",
                BindingFlags.Static | BindingFlags.NonPublic);
            var scaleMethod = typeof(MobileWorkoutPrototypeView).GetMethod(
                "ResolvePreviewScale",
                BindingFlags.Static | BindingFlags.NonPublic);
            Check(mapMethod != null, "The mobile pose coordinate mapper must exist.", failures);
            Check(scaleMethod != null, "The raw camera preview transform mapper must exist.", failures);
            if (mapMethod == null || scaleMethod == null)
            {
                return;
            }

            var rect = new Rect(0f, 0f, 100f, 200f);
            var asymmetricJoint = new TrackedJoint { x = 0.2f, y = 0.3f };
            var centerJoint = new TrackedJoint { x = 0.5f, y = 0.5f };
            var rearPoint = (Vector2)mapMethod.Invoke(null, new object[] { asymmetricJoint, rect, false });
            var frontPoint = (Vector2)mapMethod.Invoke(null, new object[] { asymmetricJoint, rect, true });
            var frontCenter = (Vector2)mapMethod.Invoke(null, new object[] { centerJoint, rect, true });

            Check(Mathf.Abs(rearPoint.x - 20f) < 0.01f && Mathf.Abs(rearPoint.y - 60f) < 0.01f,
                "Rear-camera landmarks must preserve upright normalized coordinates.", failures);
            Check(Mathf.Abs(frontPoint.x - 80f) < 0.01f && Mathf.Abs(frontPoint.y - 60f) < 0.01f,
                "Front-camera landmarks must mirror only the display X coordinate.", failures);
            Check(Mathf.Abs(frontCenter.x - 50f) < 0.01f && Mathf.Abs(frontCenter.y - 100f) < 0.01f,
                "Front-camera mirroring must keep the center landmark fixed.", failures);

            foreach (var rotation in new[] { 0, 90, 180, 270 })
            {
                foreach (var verticallyMirrored in new[] { false, true })
                {
                    foreach (var selfieMirrored in new[] { false, true })
                    {
                        var actual = (Vector3)scaleMethod.Invoke(
                            null,
                            new object[] { rotation, verticallyMirrored, selfieMirrored });
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
            detector.Update(PhaseFeature(170f, 0f, 1000L), settings);
            detector.Update(PhaseFeature(145f, -100f, 1100L), settings);
            detector.Update(PhaseFeature(130f, -100f, 1200L), settings);
            var bottomPhase = detector.Update(PhaseFeature(128f, 10f, 1300L), settings).CurrentPhase;
            detector.Update(PhaseFeature(140f, 80f, 1400L), settings);
            var completed = detector.Update(PhaseFeature(165f, 80f, 1500L), settings);

            Check(bottomPhase == ExercisePhase.Bottom,
                "Descent-to-ascent reversal must recognize the squat bottom without a full stop.", failures);
            Check(completed.RepCount == 1,
                "A stabilized standing-descent-bottom-ascent-standing sequence must count one rep.", failures);
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
