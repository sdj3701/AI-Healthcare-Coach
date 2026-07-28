using System.Collections.Generic;
using Rag.Healthcare.Pose;
using Rag.Healthcare.Pose.Session;
using Rag.Healthcare.Product;
using Rag.Healthcare.Rag.Composition;
using Rag.Healthcare.Rag.Knowledge;
using Rag.Healthcare.Rag.Logging;
using UnityEngine;

namespace Rag.Healthcare.Rag.Runtime
{
    public sealed class RealtimeFeedbackOrchestrator : MonoBehaviour
    {
        private const float MaximumPoseDeltaSeconds = 0.25f;

        [Header("Scene References")]
        [SerializeField] private JointTrackingController trackingController;
        [SerializeField] private PoseFeedbackJsonReceiver feedbackReceiver;
        [SerializeField] private RagRetriever ragRetriever;
        [SerializeField] private SessionJsonlLogger sessionLogger;
        [SerializeField] private OnboardingStatusManager profileStatus;

        [Header("Runtime")]
        [SerializeField] private bool startTrackingOnStart = true;
        [SerializeField] private string exercise = "squat";
        [SerializeField, Range(0.5f, 3f)] private float analysisWindowSeconds = 1.2f;
        [SerializeField, Range(5, 60)] private int expectedPoseFps = 12;
        [SerializeField, Min(0f)] private float duplicateCooldownSeconds = 3f;
        [SerializeField, Min(0f)] private float minimumGlobalFeedbackIntervalSeconds = 1.5f;
        [SerializeField, Range(20, 140)] private int maxSpokenTextLength = 70;
        [SerializeField] private bool speakCorrectRepCount = true;
        [SerializeField] private string correctRepFeedbackFormat = "올바른 자세입니다. {0}개.";
        [SerializeField, Min(0)] private int targetCorrectRepCount;
        [SerializeField] private RealtimePoseRuleSettings ruleSettings = new RealtimePoseRuleSettings();
        [SerializeField] private CalibrationSettings calibrationSettings = new CalibrationSettings();

        private readonly PoseFrameNormalizer normalizer = new PoseFrameNormalizer();
        private readonly PoseTrackingQualityEvaluator trackingQualityEvaluator = new PoseTrackingQualityEvaluator();
        private readonly PoseLandmarkStabilizer landmarkStabilizer = new PoseLandmarkStabilizer();
        private readonly PoseFeatureExtractor featureExtractor = new PoseFeatureExtractor();
        private readonly ExercisePhaseDetector phaseDetector = new ExercisePhaseDetector();
        private readonly RealtimePoseRuleEngine ruleEngine = new RealtimePoseRuleEngine();
        private readonly FeedbackPrioritizer prioritizer = new FeedbackPrioritizer();
        private readonly FeedbackComposer composer = new FeedbackComposer();
        private readonly RepQualityAccumulator repQuality = new RepQualityAccumulator();
        private readonly Dictionary<string, int> sessionIssueCounts =
            new Dictionary<string, int>();
        private readonly List<string> completedRepIssueRules =
            new List<string>(8);
        private readonly PoseWindowStats reusableStats = new PoseWindowStats();
        private readonly WorkoutSessionStateMachine sessionState = new WorkoutSessionStateMachine();
        private readonly PersonalizedRomEvaluator romEvaluator = new PersonalizedRomEvaluator();

        private PoseWindowBuffer windowBuffer;
        private bool currentRepInProgress;
        private bool currentRepHasViolation;
        private bool currentRepHasBottomSafetyFailure;
        private bool requiresStandingRearm = true;
        private bool poseAnalysisSuspended;
        private long lastSessionFrameTimestampMilliseconds;
        private RealtimePoseRuleSettings baseRuleSettings;
        private RealtimePoseRuleSettings workingRuleSettings;

        public ExercisePhaseState PhaseState => phaseDetector.State;
        public PoseWindowStats LatestStats { get; private set; }
        public PoseTrackingQualityReport LatestTrackingQuality { get; private set; }
        public int TotalRepCount { get; private set; }
        public int CorrectRepCount { get; private set; }
        public int TargetCorrectRepCount => targetCorrectRepCount;
        public bool HasCorrectRepTarget => targetCorrectRepCount > 0;
        public bool IsCorrectRepTargetComplete => HasCorrectRepTarget && CorrectRepCount >= targetCorrectRepCount;
        public bool CurrentRepHasViolation => currentRepHasViolation;
        public bool LastCompletedRepWasCorrect { get; private set; }
        public bool IsWaitingForStandingRearm => requiresStandingRearm;
        public IReadOnlyDictionary<string, int> SessionIssueCounts =>
            sessionIssueCounts;

        public WorkoutTrackingState SessionState => sessionState.State;
        public float CountdownRemaining => sessionState.CountdownRemainingSeconds;
        public FullBodyCalibrationReport LatestCalibration => sessionState.LatestCalibration;
        public WorkoutSessionStateMachine SessionStateMachine => sessionState;

        private RealtimePoseRuleSettings ActiveRuleSettings => workingRuleSettings ?? ruleSettings;

        private void Awake()
        {
            trackingController ??= FindFirstObjectByType<JointTrackingController>();
            feedbackReceiver ??= FindFirstObjectByType<PoseFeedbackJsonReceiver>();
            ragRetriever ??= FindFirstObjectByType<RagRetriever>();
            sessionLogger ??= FindFirstObjectByType<SessionJsonlLogger>();
            profileStatus ??= FindFirstObjectByType<OnboardingStatusManager>();

            if (ragRetriever == null)
            {
                ragRetriever = gameObject.AddComponent<RagRetriever>();
            }

            if (sessionLogger == null)
            {
                sessionLogger = gameObject.AddComponent<SessionJsonlLogger>();
            }

#if UNITY_IOS && !UNITY_EDITOR
            if (ruleSettings != null)
            {
                ruleSettings.landmarkSmoothingAlpha = 0.55f;
            }
#endif

            baseRuleSettings = CloneRuleSettings(ruleSettings);
            workingRuleSettings = null;
            CreateWindowBuffer();
        }

        private void Start()
        {
            if (startTrackingOnStart)
            {
                trackingController?.StartTracking();
            }
        }

        private void OnEnable()
        {
            if (trackingController != null)
            {
                trackingController.TrackingFrameReceived += HandleTrackingFrame;
            }
        }

        private void OnDisable()
        {
            if (trackingController != null)
            {
                trackingController.TrackingFrameReceived -= HandleTrackingFrame;
            }
        }

        public void BeginCalibrationSession()
        {
            PrepareSession(preserveAdaptiveDepthProfile: false);
            sessionState.BeginCalibrationSession();
        }

        public void BeginExerciseSession()
        {
            // START after a temporary stop continues the same workout totals.
            // SetCorrectRepTarget/ResetRuntimeState define a genuinely new session.
            PrepareSession(preserveAdaptiveDepthProfile: true);
            sessionState.BeginWorkoutSession();
        }

        public void ResumeExerciseSession()
        {
            PrepareSession(preserveAdaptiveDepthProfile: true);
            sessionState.BeginWorkoutSession();
        }

        [System.Obsolete("Use BeginCalibrationSession() or BeginExerciseSession() so the session intent is explicit.")]
        public void BeginWorkoutSession(bool skipCalibration = false)
        {
            if (skipCalibration)
            {
                BeginExerciseSession();
                return;
            }

            BeginCalibrationSession();
        }

        public void EndWorkoutSession()
        {
            sessionState.EndSession();
            RestoreBaseRuleSettings();
            ResetTrackingContinuity(
                preserveCorrectRepCount: true,
                preserveAdaptiveDepthProfile: true);
        }

        public void ResetRuntimeState()
        {
            if (sessionState.IsSessionActive)
            {
                sessionState.EndSession();
            }

            RestoreBaseRuleSettings();
            ResetTrackingContinuity(
                preserveCorrectRepCount: false,
                preserveAdaptiveDepthProfile: false);
        }

        private void ResetTrackingContinuity(
            bool preserveCorrectRepCount,
            bool preserveAdaptiveDepthProfile)
        {
            windowBuffer?.Clear();
            trackingQualityEvaluator.Reset();
            landmarkStabilizer.Reset();
            featureExtractor.Reset();
            phaseDetector.Reset(preserveAdaptiveDepthProfile);
            prioritizer.Reset();
            repQuality.Reset();
            reusableStats.Reset();
            LatestStats = null;
            LatestTrackingQuality = null;
            if (!preserveCorrectRepCount)
            {
                TotalRepCount = 0;
                CorrectRepCount = 0;
                sessionIssueCounts.Clear();
            }

            currentRepInProgress = false;
            currentRepHasViolation = false;
            currentRepHasBottomSafetyFailure = false;
            LastCompletedRepWasCorrect = false;
            requiresStandingRearm = true;
            poseAnalysisSuspended = false;
            lastSessionFrameTimestampMilliseconds = 0L;
            CancelPendingPoseFeedback();
        }

        public void SetCorrectRepTarget(int targetCount)
        {
            targetCorrectRepCount = Mathf.Max(0, targetCount);
            ResetRuntimeState();
        }

        private void HandleTrackingFrame(JointTrackingFrame frame)
        {
            if (frame == null)
            {
                return;
            }

            var settings = ActiveRuleSettings;
            LatestTrackingQuality = trackingQualityEvaluator.Evaluate(frame, settings);
            sessionState.Tick(
                frame,
                LatestTrackingQuality,
                ResolvePoseDeltaSeconds(frame.timestampUnixMilliseconds));

            if (!sessionState.IsSessionActive || !sessionState.AllowsPoseAnalysis)
            {
                SuspendPoseAnalysis(frame.timestampUnixMilliseconds);
                return;
            }

            var stabilizedFrame = landmarkStabilizer.Stabilize(frame, settings);
            sessionLogger?.LogFrame(stabilizedFrame);

            if (LatestTrackingQuality == null || !LatestTrackingQuality.AllowsPoseAnalysis)
            {
                if (LatestTrackingQuality != null &&
                    LatestTrackingQuality.CanPreservePoseAnalysis &&
                    !LatestTrackingQuality.RequiresPoseAnalysisReset)
                {
                    // Hold new decisions during short confidence dips without
                    // discarding the active squat phase or temporal evidence.
                    return;
                }

                SuspendPoseAnalysis(frame.timestampUnixMilliseconds);
                return;
            }

            var view = normalizer.Normalize(stabilizedFrame, settings.minimumVisibility);
            var feature = featureExtractor.Extract(view, exercise, settings);
            if (requiresStandingRearm)
            {
                // Do not resume halfway through a squat after an occlusion. A partial
                // bottom/ascent sequence does not contain enough evidence for a rep.
                if (!feature.HasReliableSquatCore || feature.AverageKneeAngle < settings.StandingKneeAngle)
                {
                    phaseDetector.Suspend(frame.timestampUnixMilliseconds);
                    LatestStats = null;
                    return;
                }

                requiresStandingRearm = false;
                featureExtractor.Reset();
                feature = featureExtractor.Extract(view, exercise, settings);
            }

            poseAnalysisSuspended = false;
            windowBuffer.Add(feature);

            var previousPhase = phaseDetector.State.CurrentPhase;
            var previousRepCount = phaseDetector.State.RepCount;
            var phaseState = phaseDetector.Update(feature, settings);
            if (previousPhase != phaseState.CurrentPhase)
            {
                sessionLogger?.LogPhase(phaseState);
            }

            CancelObsoletePoseFeedback(phaseState);
            LatestStats = PoseWindowStats.Calculate(
                windowBuffer,
                settings,
                reusableStats,
                analysisWindowSeconds);
            var candidates = ruleEngine.Evaluate(feature, LatestStats, phaseState, settings);
            UpdateCorrectRepCount(previousPhase, previousRepCount, phaseState, feature, candidates);
            TrySpeakPersonalizedDepthAnnouncement();
            // Correct-rep count speech is emitted above on the transition back to
            // Standing. Ordinary posture coaching is limited to an active movement.
            if (!AllowsPostureCoaching(phaseState.CurrentPhase))
            {
                return;
            }

            if (phaseState.CurrentPhase == ExercisePhase.Bottom &&
                phaseState.HasIssuedBottomDecisionFeedbackInCurrentRep)
            {
                return;
            }

            if (!prioritizer.TrySelect(candidates, duplicateCooldownSeconds, minimumGlobalFeedbackIntervalSeconds, out var selected))
            {
                return;
            }

            var retrieved = ragRetriever == null ? null : ragRetriever.Retrieve(selected);
            var message = composer.Compose(selected, retrieved, maxSpokenTextLength);
            if (message == null)
            {
                return;
            }

            feedbackReceiver ??= FindFirstObjectByType<PoseFeedbackJsonReceiver>();
            if (feedbackReceiver == null ||
                !feedbackReceiver.ReceiveFeedback(message))
            {
                return;
            }

            prioritizer.CommitSelection(selected);
            if (IsBottomDecisionRule(selected.RuleId))
            {
                // The rule engine may retry the candidate until the receiver has
                // actually admitted it to TTS. Lock only after that handoff succeeds.
                phaseState.HasIssuedShallowDepthFeedbackInCurrentRep = true;
                phaseState.HasIssuedBottomDecisionFeedbackInCurrentRep = true;
            }

            sessionLogger?.LogFeedback(selected, message);
        }

        private void SuspendPoseAnalysis(long timestampUnixMilliseconds)
        {
            if (poseAnalysisSuspended)
            {
                return;
            }

            poseAnalysisSuspended = true;
            var previousPhase = phaseDetector.State.CurrentPhase;
            phaseDetector.Suspend(timestampUnixMilliseconds);
            if (previousPhase != phaseDetector.State.CurrentPhase)
            {
                sessionLogger?.LogPhase(phaseDetector.State);
            }

            windowBuffer?.Clear();
            landmarkStabilizer.Reset();
            featureExtractor.Reset();
            reusableStats.Reset();
            LatestStats = null;
            repQuality.Reset();
            currentRepInProgress = false;
            currentRepHasViolation = false;
            currentRepHasBottomSafetyFailure = false;
            LastCompletedRepWasCorrect = false;
            requiresStandingRearm = true;
            CancelPendingPoseFeedback();
        }

        private void UpdateCorrectRepCount(
            ExercisePhase previousPhase,
            int previousRepCount,
            ExercisePhaseState phaseState,
            PoseFeatureFrame feature,
            IReadOnlyList<FeedbackEvent> candidates)
        {
            var settings = ActiveRuleSettings;
            if (phaseState == null)
            {
                return;
            }

            if (!currentRepInProgress && IsRepActive(phaseState.CurrentPhase))
            {
                currentRepInProgress = true;
                currentRepHasViolation = false;
                currentRepHasBottomSafetyFailure = false;
                LastCompletedRepWasCorrect = false;
                repQuality.Reset();
            }

            if (currentRepInProgress && IsRepActive(phaseState.CurrentPhase))
            {
                repQuality.Observe(feature != null && feature.HasReliableSquatCore, candidates, settings);
                if (phaseState.CurrentBottomDecision ==
                    SquatBottomDecision.KneeCollapseFailed)
                {
                    currentRepHasBottomSafetyFailure = true;
                }

                currentRepHasViolation =
                    currentRepHasBottomSafetyFailure ||
                    repQuality.HasConfirmedViolation(settings);
            }

            if (phaseState.HasCompletedSquatAttemptThisFrame)
            {
                UpdatePersonalizedDepthProfile(phaseState, settings);
            }

            if (phaseState.RepCount > previousRepCount)
            {
                TotalRepCount += phaseState.RepCount - previousRepCount;
                var hasEnoughEvidence = repQuality.HasEnoughEvidence(settings);
                LastCompletedRepWasCorrect =
                    phaseState.LastCompletedBottomDecision ==
                        SquatBottomDecision.Passed &&
                    !currentRepHasBottomSafetyFailure &&
                    repQuality.IsCorrect(settings);
                if (LastCompletedRepWasCorrect && CanIncrementCorrectRepCount())
                {
                    CorrectRepCount++;
                    SpeakCorrectRepCount();
                }
                else if (!hasEnoughEvidence &&
                         !currentRepHasBottomSafetyFailure)
                {
                    IncrementSessionIssue("rep_tracking_uncertain");
                    SpeakUncertainRep();
                }
                else
                {
                    completedRepIssueRules.Clear();
                    if (phaseState.LastCompletedBottomDecision ==
                        SquatBottomDecision.KneeCollapseFailed)
                    {
                        completedRepIssueRules.Add(
                            "squat_knee_collapse");
                    }
                    repQuality.CollectConfirmedViolationRuleIds(
                        settings,
                        completedRepIssueRules);
                    foreach (var ruleId in completedRepIssueRules)
                    {
                        IncrementSessionIssue(ruleId);
                    }

                    if (completedRepIssueRules.Count == 0)
                    {
                        IncrementSessionIssue("unknown_posture");
                    }
                }

                currentRepInProgress = false;
                currentRepHasViolation = false;
                currentRepHasBottomSafetyFailure = false;
                repQuality.Reset();
                return;
            }

            if (currentRepInProgress &&
                previousPhase != ExercisePhase.Standing &&
                phaseState.CurrentPhase == ExercisePhase.Standing)
            {
                currentRepInProgress = false;
                currentRepHasViolation = false;
                currentRepHasBottomSafetyFailure = false;
                LastCompletedRepWasCorrect = false;
                repQuality.Reset();
            }
        }

        private void UpdatePersonalizedDepthProfile(
            ExercisePhaseState phaseState,
            RealtimePoseRuleSettings settings)
        {
            var isEligibleFailure =
                phaseState.LastCompletedBottomDecision ==
                    SquatBottomDecision.PersonalDepthFailed &&
                LatestTrackingQuality != null &&
                LatestTrackingQuality.State ==
                    PoseTrackingQualityState.Good &&
                repQuality.HasEnoughEvidence(settings) &&
                !repQuality.HasConfirmedViolation(settings) &&
                !currentRepHasBottomSafetyFailure;
            if (!isEligibleFailure)
            {
                phaseDetector.RejectPersonalDepthFailureCandidate();
                return;
            }

            phaseDetector.RegisterPersonalDepthFailureCandidate(
                phaseState.LastCompletedAttemptMinimumKneeAngle,
                phaseState.LastCompletedAttemptMaximumHipDrop,
                settings);
        }

        private void TrySpeakPersonalizedDepthAnnouncement()
        {
            var phaseState = phaseDetector.State;
            if (phaseState == null ||
                !phaseState.HasPendingPersonalizedDepthAnnouncement ||
                phaseState.CurrentPhase != ExercisePhase.Standing)
            {
                return;
            }

            feedbackReceiver ??=
                FindFirstObjectByType<PoseFeedbackJsonReceiver>();
            if (feedbackReceiver != null &&
                feedbackReceiver.ReceiveFeedback(
                    new PoseFeedbackMessage
                    {
                        id = "depth_profile_adjusted",
                        text = "현재 가능한 깊이에 맞춰 기준을 조정했습니다.",
                        joint = string.Empty,
                        confidence = 1f,
                        severity = FeedbackSeverity.Info
                    }))
            {
                phaseDetector.ConsumePersonalizedDepthAnnouncement();
            }
        }

        private void ApplyPersonalizedRomFromProfile()
        {
            profileStatus ??= FindFirstObjectByType<OnboardingStatusManager>();
            var sourceSettings = baseRuleSettings ?? ruleSettings;
            if (profileStatus != null &&
                profileStatus.HasCompletedProfile &&
                profileStatus.Profile != null)
            {
                var safety = profileStatus.Profile.romSafety;
                if (safety == null || IsRomSafetyEmpty(safety))
                {
                    safety = romEvaluator.Evaluate(profileStatus.Profile);
                }

                workingRuleSettings = romEvaluator.ApplyDerate(sourceSettings, safety);
                return;
            }

            workingRuleSettings = CloneRuleSettings(sourceSettings);
        }

        private void PrepareSession(bool preserveAdaptiveDepthProfile)
        {
            ApplyPersonalizedRomFromProfile();
            ResetTrackingContinuity(
                preserveCorrectRepCount: true,
                preserveAdaptiveDepthProfile: preserveAdaptiveDepthProfile);
            sessionState.Configure(calibrationSettings);
        }

        private float ResolvePoseDeltaSeconds(long timestampUnixMilliseconds)
        {
            var fallback = Mathf.Clamp(Time.unscaledDeltaTime, 0f, MaximumPoseDeltaSeconds);
            if (timestampUnixMilliseconds <= 0L)
            {
                lastSessionFrameTimestampMilliseconds = 0L;
                return fallback;
            }

            if (lastSessionFrameTimestampMilliseconds <= 0L ||
                timestampUnixMilliseconds <= lastSessionFrameTimestampMilliseconds)
            {
                lastSessionFrameTimestampMilliseconds = timestampUnixMilliseconds;
                return fallback;
            }

            var elapsedMilliseconds =
                timestampUnixMilliseconds - lastSessionFrameTimestampMilliseconds;
            lastSessionFrameTimestampMilliseconds = timestampUnixMilliseconds;
            return Mathf.Clamp(
                elapsedMilliseconds / 1000f,
                0f,
                MaximumPoseDeltaSeconds);
        }

        private void RestoreBaseRuleSettings()
        {
            workingRuleSettings = null;
        }

        private static bool IsRomSafetyEmpty(RomSafetyProfile safety)
        {
            if (safety == null)
            {
                return true;
            }

            return Mathf.Approximately(safety.bottomKneeAngleDelta, 0f) &&
                   Mathf.Approximately(safety.minimumBottomKneeAngleDelta, 0f) &&
                   Mathf.Approximately(safety.maximumBottomKneeAngleDelta, 0f) &&
                   Mathf.Approximately(safety.maximumTorsoTiltDegreesDelta, 0f) &&
                   !safety.suppressDeeperEncouragement &&
                   string.IsNullOrWhiteSpace(safety.derateReason);
        }

        private static RealtimePoseRuleSettings CloneRuleSettings(RealtimePoseRuleSettings source)
        {
            if (source == null)
            {
                return new RealtimePoseRuleSettings();
            }

            var copy = new RealtimePoseRuleSettings();
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(source), copy);
            return copy;
        }

        private bool CanIncrementCorrectRepCount()
        {
            return !HasCorrectRepTarget || CorrectRepCount < targetCorrectRepCount;
        }

        private void IncrementSessionIssue(string ruleId)
        {
            if (string.IsNullOrWhiteSpace(ruleId))
            {
                ruleId = "unknown_posture";
            }

            sessionIssueCounts.TryGetValue(ruleId, out var count);
            sessionIssueCounts[ruleId] = count + 1;
        }

        private void SpeakCorrectRepCount()
        {
            if (!speakCorrectRepCount || CorrectRepCount <= 0)
            {
                return;
            }

            var countText = CorrectRepCount.ToString();
            var text = string.IsNullOrWhiteSpace(correctRepFeedbackFormat)
                ? $"올바른 자세입니다. {countText}개."
                : correctRepFeedbackFormat.Replace("{0}", countText);

            feedbackReceiver ??= FindFirstObjectByType<PoseFeedbackJsonReceiver>();
            feedbackReceiver?.ReceiveFeedback(new PoseFeedbackMessage
            {
                id = "correct_rep_" + CorrectRepCount,
                text = text,
                joint = string.Empty,
                confidence = 1f,
                severity = FeedbackSeverity.Info
            });
        }

        private void SpeakUncertainRep()
        {
            feedbackReceiver ??= FindFirstObjectByType<PoseFeedbackJsonReceiver>();
            feedbackReceiver?.ReceiveFeedback(new PoseFeedbackMessage
            {
                id = "rep_tracking_uncertain",
                text = "관절 인식이 불안정해 이번 동작은 횟수에 포함하지 않았습니다.",
                joint = string.Empty,
                confidence = 1f,
                severity = FeedbackSeverity.Info
            });
        }

        private void CancelObsoletePoseFeedback(
            ExercisePhaseState phaseState)
        {
            if (phaseState == null ||
                !AllowsPostureCoaching(phaseState.CurrentPhase))
            {
                CancelPendingPoseFeedback();
                return;
            }

            if (phaseState.CurrentPhase == ExercisePhase.Bottom)
            {
                CancelPendingBottomDecisionFeedbackExcept(
                    phaseState.CurrentBottomDecision);
                return;
            }

            CancelPendingDepthFeedback();
        }

        private void CancelPendingDepthFeedback()
        {
            feedbackReceiver ??=
                FindFirstObjectByType<PoseFeedbackJsonReceiver>();
            feedbackReceiver?.CancelPendingFeedback(
                "squat_depth_hip_height");
            feedbackReceiver?.CancelPendingFeedback(
                "squat_knee_collapse");
            feedbackReceiver?.CancelPendingFeedback(
                "squat_depth_personal_target");
            feedbackReceiver?.CancelPendingFeedback(
                "squat_depth_excessive");
        }

        private void CancelPendingBottomDecisionFeedbackExcept(
            SquatBottomDecision decision)
        {
            feedbackReceiver ??=
                FindFirstObjectByType<PoseFeedbackJsonReceiver>();
            if (feedbackReceiver == null)
            {
                return;
            }

            if (decision != SquatBottomDecision.HipHeightFailed)
            {
                feedbackReceiver.CancelPendingFeedback(
                    "squat_depth_hip_height");
            }

            if (decision != SquatBottomDecision.KneeCollapseFailed)
            {
                feedbackReceiver.CancelPendingFeedback(
                    "squat_knee_collapse");
            }

            if (decision != SquatBottomDecision.PersonalDepthFailed)
            {
                feedbackReceiver.CancelPendingFeedback(
                    "squat_depth_personal_target");
            }

            // Retired rule: always cancel any request queued by an older build.
            feedbackReceiver.CancelPendingFeedback(
                "squat_depth_excessive");
        }

        private void CancelPendingPoseFeedback()
        {
            feedbackReceiver ??=
                FindFirstObjectByType<PoseFeedbackJsonReceiver>();
            feedbackReceiver?.CancelPendingFeedbackPrefix("squat_");
        }

        private static bool IsRepActive(ExercisePhase phase)
        {
            return phase == ExercisePhase.Descent ||
                   phase == ExercisePhase.Bottom ||
                   phase == ExercisePhase.Ascent;
        }

        public static bool AllowsPostureCoaching(ExercisePhase phase)
        {
            return IsRepActive(phase);
        }

        private static bool IsBottomDecisionRule(string ruleId)
        {
            return ruleId == "squat_depth_hip_height" ||
                   ruleId == "squat_knee_collapse" ||
                   ruleId == "squat_depth_personal_target";
        }

        private void CreateWindowBuffer()
        {
            // capacity / expectedPoseFps only size the ring buffer.
            // PoseWindowStats.Calculate filters by analysisWindowSeconds (timestamp) as the real window.
            var capacity = Mathf.CeilToInt(Mathf.Max(0.5f, analysisWindowSeconds) * Mathf.Max(5, expectedPoseFps));
            windowBuffer = new PoseWindowBuffer(capacity);
        }

        private void OnValidate()
        {
            if (Application.isPlaying)
            {
                CreateWindowBuffer();
            }
        }
    }
}
