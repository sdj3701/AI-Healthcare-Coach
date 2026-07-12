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
        [SerializeField, Range(5, 60)] private int expectedPoseFps = 15;
        [SerializeField, Min(0f)] private float duplicateCooldownSeconds = 3f;
        [SerializeField, Min(0f)] private float minimumGlobalFeedbackIntervalSeconds = 1.5f;
        [SerializeField, Range(20, 140)] private int maxSpokenTextLength = 70;
        [SerializeField] private bool speakCorrectRepCount = true;
        [SerializeField] private string correctRepFeedbackFormat = "정확합니다. {0}개.";
        [SerializeField, Min(0)] private int targetCorrectRepCount;
        [SerializeField] private RealtimePoseRuleSettings ruleSettings = new RealtimePoseRuleSettings();

        private readonly PoseFrameNormalizer normalizer = new PoseFrameNormalizer();
        private readonly PoseFeatureExtractor featureExtractor = new PoseFeatureExtractor();
        private readonly ExercisePhaseDetector phaseDetector = new ExercisePhaseDetector();
        private readonly RealtimePoseRuleEngine ruleEngine = new RealtimePoseRuleEngine();
        private readonly FeedbackPrioritizer prioritizer = new FeedbackPrioritizer();
        private readonly FeedbackComposer composer = new FeedbackComposer();

        private PoseWindowBuffer windowBuffer;
        private bool currentRepInProgress;
        private bool currentRepHasViolation;

        public ExercisePhaseState PhaseState => phaseDetector.State;
        public PoseWindowStats LatestStats { get; private set; }
        public int CorrectRepCount { get; private set; }
        public int TargetCorrectRepCount => targetCorrectRepCount;
        public bool HasCorrectRepTarget => targetCorrectRepCount > 0;
        public bool IsCorrectRepTargetComplete => HasCorrectRepTarget && CorrectRepCount >= targetCorrectRepCount;
        public bool CurrentRepHasViolation => currentRepHasViolation;
        public bool LastCompletedRepWasCorrect { get; private set; }

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
            featureExtractor.Reset();
            phaseDetector.Reset();
            prioritizer.Reset();
            LatestStats = null;
            CorrectRepCount = 0;
            currentRepInProgress = false;
            currentRepHasViolation = false;
            LastCompletedRepWasCorrect = false;
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

            sessionLogger?.LogFrame(frame);

            var view = normalizer.Normalize(frame, ruleSettings.minimumVisibility);
            var feature = featureExtractor.Extract(view, exercise, ruleSettings.minimumVisibility);
            windowBuffer.Add(feature);

            var previousPhase = phaseDetector.State.CurrentPhase;
            var previousRepCount = phaseDetector.State.RepCount;
            var phaseState = phaseDetector.Update(feature, ruleSettings);
            if (previousPhase != phaseState.CurrentPhase)
            {
                sessionLogger?.LogPhase(phaseState);
            }

            LatestStats = PoseWindowStats.Calculate(windowBuffer, ruleSettings);
            var candidates = ruleEngine.Evaluate(feature, LatestStats, phaseState, ruleSettings);
            UpdateCorrectRepCount(previousPhase, previousRepCount, phaseState, candidates);
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

        private void UpdateCorrectRepCount(
            ExercisePhase previousPhase,
            int previousRepCount,
            ExercisePhaseState phaseState,
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
            }

            if (currentRepInProgress && HasPoseViolation(candidates))
            {
                currentRepHasViolation = true;
            }

            if (phaseState.RepCount > previousRepCount)
            {
            LastCompletedRepWasCorrect = !currentRepHasViolation;
            if (LastCompletedRepWasCorrect && CanIncrementCorrectRepCount())
            {
                CorrectRepCount++;
                SpeakCorrectRepCount();
            }

            currentRepInProgress = false;
            currentRepHasViolation = false;
            return;
            }

            if (currentRepInProgress &&
                previousPhase != ExercisePhase.Standing &&
                phaseState.CurrentPhase == ExercisePhase.Standing)
            {
                currentRepInProgress = false;
                currentRepHasViolation = false;
                LastCompletedRepWasCorrect = false;
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

        private static bool IsRepActive(ExercisePhase phase)
        {
            return phase == ExercisePhase.Descent ||
                   phase == ExercisePhase.Bottom ||
                   phase == ExercisePhase.Ascent;
        }

        private static bool HasPoseViolation(IReadOnlyList<FeedbackEvent> candidates)
        {
            return candidates != null && candidates.Count > 0;
        }

        private void CreateWindowBuffer()
        {
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
