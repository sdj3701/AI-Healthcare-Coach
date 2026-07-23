using System.Collections.Generic;
using Rag.Healthcare.Pose;
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

        private PoseWindowBuffer windowBuffer;
        private bool currentRepInProgress;
        private bool currentRepHasViolation;
        private bool requiresStandingRearm = true;

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

        private void Awake()
        {
            trackingController ??= FindFirstObjectByType<JointTrackingController>();
            feedbackReceiver ??= FindFirstObjectByType<PoseFeedbackJsonReceiver>();
            ragRetriever ??= FindFirstObjectByType<RagRetriever>();
            sessionLogger ??= FindFirstObjectByType<SessionJsonlLogger>();

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

        public void ResetRuntimeState()
        {
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

            LatestTrackingQuality = trackingQualityEvaluator.Evaluate(frame, ruleSettings);
            var stabilizedFrame = landmarkStabilizer.Stabilize(frame, ruleSettings);
            sessionLogger?.LogFrame(stabilizedFrame);

            if (LatestTrackingQuality == null || !LatestTrackingQuality.AllowsPoseAnalysis)
            {
                SuspendPoseAnalysis(frame.timestampUnixMilliseconds);
                return;
            }

            var view = normalizer.Normalize(stabilizedFrame, ruleSettings.minimumVisibility);
            var feature = featureExtractor.Extract(view, exercise, ruleSettings.minimumVisibility);
            if (requiresStandingRearm)
            {
                // Do not resume halfway through a squat after an occlusion. A partial
                // bottom/ascent sequence does not contain enough evidence for a rep.
                if (!feature.HasReliableSquatCore || feature.AverageKneeAngle < ruleSettings.StandingKneeAngle)
                {
                    phaseDetector.Suspend(frame.timestampUnixMilliseconds);
                    LatestStats = null;
                    return;
                }

                requiresStandingRearm = false;
                featureExtractor.Reset();
                feature = featureExtractor.Extract(view, exercise, ruleSettings.minimumVisibility);
            }

            windowBuffer.Add(feature);

            var previousPhase = phaseDetector.State.CurrentPhase;
            var previousRepCount = phaseDetector.State.RepCount;
            var phaseState = phaseDetector.Update(feature, ruleSettings);
            if (previousPhase != phaseState.CurrentPhase)
            {
                sessionLogger?.LogPhase(phaseState);
            }

            LatestStats = PoseWindowStats.Calculate(
                windowBuffer,
                ruleSettings,
                reusableStats,
                analysisWindowSeconds);
            var candidates = ruleEngine.Evaluate(feature, LatestStats, phaseState, ruleSettings);
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
                repQuality.Observe(feature != null && feature.HasReliableSquatCore, candidates, ruleSettings);
                currentRepHasViolation = repQuality.HasConfirmedViolation(ruleSettings);
            }

            if (phaseState.RepCount > previousRepCount)
            {
                var hasEnoughEvidence = repQuality.HasEnoughEvidence(ruleSettings);
                LastCompletedRepWasCorrect = repQuality.IsCorrect(ruleSettings);
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
