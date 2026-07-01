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
        [SerializeField] private RealtimePoseRuleSettings ruleSettings = new RealtimePoseRuleSettings();

        private readonly PoseFrameNormalizer normalizer = new PoseFrameNormalizer();
        private readonly PoseFeatureExtractor featureExtractor = new PoseFeatureExtractor();
        private readonly ExercisePhaseDetector phaseDetector = new ExercisePhaseDetector();
        private readonly RealtimePoseRuleEngine ruleEngine = new RealtimePoseRuleEngine();
        private readonly FeedbackPrioritizer prioritizer = new FeedbackPrioritizer();
        private readonly FeedbackComposer composer = new FeedbackComposer();

        private PoseWindowBuffer windowBuffer;

        public ExercisePhaseState PhaseState => phaseDetector.State;
        public PoseWindowStats LatestStats { get; private set; }

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
            var phaseState = phaseDetector.Update(feature, ruleSettings);
            if (previousPhase != phaseState.CurrentPhase)
            {
                sessionLogger?.LogPhase(phaseState);
            }

            LatestStats = PoseWindowStats.Calculate(windowBuffer, ruleSettings);
            var candidates = ruleEngine.Evaluate(feature, LatestStats, phaseState, ruleSettings);
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
