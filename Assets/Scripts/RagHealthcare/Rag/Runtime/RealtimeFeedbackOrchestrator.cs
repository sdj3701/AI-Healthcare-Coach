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
        [SerializeField] private string correctRepFeedbackFormat = "정확합니다. {0}개.";
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
        private readonly PoseWindowStats reusableStats = new PoseWindowStats();
        private readonly WorkoutSessionStateMachine sessionState = new WorkoutSessionStateMachine();
        private readonly PersonalizedRomEvaluator romEvaluator = new PersonalizedRomEvaluator();

        private PoseWindowBuffer windowBuffer;
        private bool currentRepInProgress;
        private bool currentRepHasViolation;
        private bool requiresStandingRearm = true;
        private RealtimePoseRuleSettings baseRuleSettings;
        private RealtimePoseRuleSettings workingRuleSettings;

        public ExercisePhaseState PhaseState => phaseDetector.State;
        public PoseWindowStats LatestStats { get; private set; }
        public PoseTrackingQualityReport LatestTrackingQuality { get; private set; }
        public int CorrectRepCount { get; private set; }
        public int TargetCorrectRepCount => targetCorrectRepCount;
        public bool HasCorrectRepTarget => targetCorrectRepCount > 0;
        public bool IsCorrectRepTargetComplete => HasCorrectRepTarget && CorrectRepCount >= targetCorrectRepCount;
        public bool CurrentRepHasViolation => currentRepHasViolation;
        public bool LastCompletedRepWasCorrect { get; private set; }
        public bool IsWaitingForStandingRearm => requiresStandingRearm;

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

        public void BeginWorkoutSession(bool skipCalibration = false)
        {
            ApplyPersonalizedRomFromProfile();
            sessionState.Configure(calibrationSettings);
            if (skipCalibration)
            {
                sessionState.BeginCalibratedSession();
            }
            else
            {
                sessionState.BeginSession();
            }
        }

        public void EndWorkoutSession()
        {
            sessionState.EndSession();
            RestoreBaseRuleSettings();
        }

        public void ResetRuntimeState()
        {
            if (sessionState.IsSessionActive)
            {
                sessionState.EndSession();
            }

            RestoreBaseRuleSettings();
            windowBuffer?.Clear();
            trackingQualityEvaluator.Reset();
            landmarkStabilizer.Reset();
            featureExtractor.Reset();
            phaseDetector.Reset();
            prioritizer.Reset();
            repQuality.Reset();
            reusableStats.Reset();
            LatestStats = null;
            LatestTrackingQuality = null;
            CorrectRepCount = 0;
            currentRepInProgress = false;
            currentRepHasViolation = false;
            LastCompletedRepWasCorrect = false;
            requiresStandingRearm = true;
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
            sessionState.Tick(frame, LatestTrackingQuality, Time.deltaTime);

            if (sessionState.IsSessionActive && !sessionState.AllowsPoseAnalysis)
            {
                SuspendPoseAnalysis(frame.timestampUnixMilliseconds);
                return;
            }

            var stabilizedFrame = landmarkStabilizer.Stabilize(frame, settings);
            sessionLogger?.LogFrame(stabilizedFrame);

            if (LatestTrackingQuality == null || !LatestTrackingQuality.AllowsPoseAnalysis)
            {
                SuspendPoseAnalysis(frame.timestampUnixMilliseconds);
                return;
            }

            var view = normalizer.Normalize(stabilizedFrame, settings.minimumVisibility);
            var feature = featureExtractor.Extract(view, exercise, settings.minimumVisibility);
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
                feature = featureExtractor.Extract(view, exercise, settings.minimumVisibility);
            }

            windowBuffer.Add(feature);

            var previousPhase = phaseDetector.State.CurrentPhase;
            var previousRepCount = phaseDetector.State.RepCount;
            var phaseState = phaseDetector.Update(feature, settings);
            if (previousPhase != phaseState.CurrentPhase)
            {
                sessionLogger?.LogPhase(phaseState);
            }

            LatestStats = PoseWindowStats.Calculate(
                windowBuffer,
                settings,
                reusableStats,
                analysisWindowSeconds);
            var candidates = ruleEngine.Evaluate(feature, LatestStats, phaseState, settings);
            UpdateCorrectRepCount(previousPhase, previousRepCount, phaseState, feature, candidates);
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

            sessionLogger?.LogFeedback(selected, message);
            feedbackReceiver ??= FindFirstObjectByType<PoseFeedbackJsonReceiver>();
            feedbackReceiver?.ReceiveFeedback(message);
        }

        private void SuspendPoseAnalysis(long timestampUnixMilliseconds)
        {
            var previousPhase = phaseDetector.State.CurrentPhase;
            phaseDetector.Suspend(timestampUnixMilliseconds);
            if (previousPhase != phaseDetector.State.CurrentPhase)
            {
                sessionLogger?.LogPhase(phaseDetector.State);
            }

            windowBuffer?.Clear();
            featureExtractor.Reset();
            reusableStats.Reset();
            LatestStats = null;
            repQuality.Reset();
            currentRepInProgress = false;
            currentRepHasViolation = false;
            LastCompletedRepWasCorrect = false;
            requiresStandingRearm = true;
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
                LastCompletedRepWasCorrect = false;
                repQuality.Reset();
            }

            if (currentRepInProgress && IsRepActive(phaseState.CurrentPhase))
            {
                repQuality.Observe(feature != null && feature.HasReliableSquatCore, candidates, settings);
                currentRepHasViolation = repQuality.HasConfirmedViolation(settings);
            }

            if (phaseState.RepCount > previousRepCount)
            {
                var hasEnoughEvidence = repQuality.HasEnoughEvidence(settings);
                LastCompletedRepWasCorrect = repQuality.IsCorrect(settings);
                if (LastCompletedRepWasCorrect && CanIncrementCorrectRepCount())
                {
                    CorrectRepCount++;
                    SpeakCorrectRepCount();
                }
                else if (!hasEnoughEvidence)
                {
                    SpeakUncertainRep();
                }

                currentRepInProgress = false;
                currentRepHasViolation = false;
                repQuality.Reset();
                return;
            }

            if (currentRepInProgress &&
                previousPhase != ExercisePhase.Standing &&
                phaseState.CurrentPhase == ExercisePhase.Standing)
            {
                currentRepInProgress = false;
                currentRepHasViolation = false;
                LastCompletedRepWasCorrect = false;
                repQuality.Reset();
            }
        }

        private void ApplyPersonalizedRomFromProfile()
        {
            profileStatus ??= FindFirstObjectByType<OnboardingStatusManager>();
            var baseCopy = CloneRuleSettings(baseRuleSettings ?? ruleSettings);
            if (profileStatus != null &&
                profileStatus.HasCompletedProfile &&
                profileStatus.Profile != null)
            {
                var safety = profileStatus.Profile.romSafety;
                if (safety == null || IsRomSafetyEmpty(safety))
                {
                    safety = romEvaluator.Evaluate(profileStatus.Profile);
                }

                workingRuleSettings = romEvaluator.ApplyDerate(baseCopy, safety);
                return;
            }

            workingRuleSettings = baseCopy;
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

        private void SpeakCorrectRepCount()
        {
            if (!speakCorrectRepCount || CorrectRepCount <= 0)
            {
                return;
            }

            var countText = CorrectRepCount.ToString();
            var text = string.IsNullOrWhiteSpace(correctRepFeedbackFormat)
                ? $"정확합니다. {countText}개."
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

        private static bool IsRepActive(ExercisePhase phase)
        {
            return phase == ExercisePhase.Descent ||
                   phase == ExercisePhase.Bottom ||
                   phase == ExercisePhase.Ascent;
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
