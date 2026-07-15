using System;
using System.Collections.Generic;

namespace Rag.Healthcare.Tts
{
    public enum TtsRequestPriority
    {
        Info = 0,
        Warning = 1,
        Critical = 2
    }

    public enum TtsEnqueueDisposition
    {
        AcceptedAsActive,
        AcceptedAsPending,
        CoalescedPending,
        AcceptedWithPreemption,
        SuppressedActiveDuplicate,
        SuppressedCooldownDuplicate,
        DroppedLowerPriority,
        RejectedInvalid,
        RejectedGeneration,
        RejectedBackendUnhealthy
    }

    public enum TtsSchedulerActionType
    {
        None,
        Start,
        StopForPreemption,
        QuarantineBackend
    }

    /// <summary>
    /// Immutable request owned by <see cref="TtsRequestScheduler"/>.
    /// The scheduler never reads Unity time, so its policy can be tested as plain C#.
    /// </summary>
    public sealed class TtsScheduledRequest
    {
        internal TtsScheduledRequest(
            long requestId,
            string text,
            string semanticId,
            TtsRequestPriority priority,
            double enqueuedAt,
            double expiresAt,
            int generation)
        {
            RequestId = requestId;
            Text = text;
            SemanticId = semanticId;
            Priority = priority;
            EnqueuedAt = enqueuedAt;
            ExpiresAt = expiresAt;
            Generation = generation;
        }

        public long RequestId { get; }
        public string Text { get; }
        public string SemanticId { get; }
        public TtsRequestPriority Priority { get; }
        public double EnqueuedAt { get; }
        public double ExpiresAt { get; }
        public int Generation { get; }

        public bool IsExpired(double now)
        {
            return now >= ExpiresAt;
        }
    }

    public readonly struct TtsEnqueueResult
    {
        public TtsEnqueueResult(
            TtsEnqueueDisposition disposition,
            TtsScheduledRequest request,
            string reason)
        {
            Disposition = disposition;
            Request = request;
            Reason = reason ?? string.Empty;
        }

        public TtsEnqueueDisposition Disposition { get; }
        public TtsScheduledRequest Request { get; }
        public string Reason { get; }

        public bool IsScheduled =>
            Disposition == TtsEnqueueDisposition.AcceptedAsActive ||
            Disposition == TtsEnqueueDisposition.AcceptedAsPending ||
            Disposition == TtsEnqueueDisposition.CoalescedPending ||
            Disposition == TtsEnqueueDisposition.AcceptedWithPreemption;

        public bool IsBenignSuppression =>
            Disposition == TtsEnqueueDisposition.SuppressedActiveDuplicate ||
            Disposition == TtsEnqueueDisposition.SuppressedCooldownDuplicate ||
            Disposition == TtsEnqueueDisposition.DroppedLowerPriority;
    }

    public readonly struct TtsSchedulerAction
    {
        public static readonly TtsSchedulerAction None =
            new TtsSchedulerAction(TtsSchedulerActionType.None, null, false);

        public TtsSchedulerAction(
            TtsSchedulerActionType type,
            TtsScheduledRequest request,
            bool stopTimedOut)
        {
            Type = type;
            Request = request;
            StopTimedOut = stopTimedOut;
        }

        public TtsSchedulerActionType Type { get; }
        public TtsScheduledRequest Request { get; }
        public bool StopTimedOut { get; }
    }

    public sealed class TtsRequestSchedulerOptions
    {
        public double DuplicateCooldownSeconds { get; set; } = 2d;
        public double StartObservationGraceSeconds { get; set; } = 0.05d;
        public double StopTimeoutSeconds { get; set; } = 0.75d;
        public TtsRequestPriority MinimumPreemptPriority { get; set; } = TtsRequestPriority.Critical;
        public int MaximumRecentSemanticIds { get; set; } = 128;
    }

    /// <summary>
    /// A bounded, latest-relevant TTS policy with exactly one active and one pending slot.
    /// It owns policy only; the caller owns the platform TTS service and acknowledges actions.
    /// </summary>
    public sealed class TtsRequestScheduler
    {
        private enum ActiveRequestState
        {
            None,
            Queued,
            StartRequested,
            Speaking,
            StopRequested,
            WaitingForStop
        }

        private readonly TtsRequestSchedulerOptions options;
        private readonly Dictionary<string, double> recentSemanticTimes =
            new Dictionary<string, double>(StringComparer.Ordinal);

        private ActiveRequestState activeState;
        private bool observedBackendSpeaking;
        private double activeStartedAt;
        private double stopIssuedAt;
        private long nextRequestId = 1;

        public TtsRequestScheduler(TtsRequestSchedulerOptions options = null)
        {
            this.options = options ?? new TtsRequestSchedulerOptions();
        }

        public int Generation { get; private set; }
        public TtsScheduledRequest Active { get; private set; }
        public TtsScheduledRequest Pending { get; private set; }
        public bool IsBusy => Active != null || Pending != null;
        public bool HasPending => Pending != null;
        public bool IsQuarantined { get; private set; }

        public void BeginGeneration(int generation)
        {
            Generation = generation;
            Active = null;
            Pending = null;
            activeState = ActiveRequestState.None;
            observedBackendSpeaking = false;
            activeStartedAt = 0d;
            stopIssuedAt = 0d;
            IsQuarantined = false;
            recentSemanticTimes.Clear();
        }

        public void QuarantineBackend()
        {
            Quarantine();
        }

        public TtsEnqueueResult Enqueue(
            string text,
            string semanticId,
            TtsRequestPriority priority,
            double now,
            double ttlSeconds,
            int generation)
        {
            if (generation != Generation)
            {
                return new TtsEnqueueResult(
                    TtsEnqueueDisposition.RejectedGeneration,
                    null,
                    "Request belongs to a stale TTS generation.");
            }

            if (IsQuarantined)
            {
                return new TtsEnqueueResult(
                    TtsEnqueueDisposition.RejectedBackendUnhealthy,
                    null,
                    "TTS backend is quarantined until a new session begins.");
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                return new TtsEnqueueResult(
                    TtsEnqueueDisposition.RejectedInvalid,
                    null,
                    "TTS text is empty.");
            }

            var trimmedText = text.Trim();
            var normalizedSemanticId = string.IsNullOrWhiteSpace(semanticId)
                ? trimmedText
                : semanticId.Trim();

            DropExpiredPending(now);
            DropExpiredUnstartedActive(now);

            var upgradesActiveSemantic = IsSameSemantic(Active, normalizedSemanticId) &&
                                         priority > Active.Priority;
            if (IsSameSemantic(Active, normalizedSemanticId) && !upgradesActiveSemantic)
            {
                return new TtsEnqueueResult(
                    TtsEnqueueDisposition.SuppressedActiveDuplicate,
                    Active,
                    "The same semantic request is already active.");
            }

            if (IsSameSemantic(Pending, normalizedSemanticId))
            {
                if (priority < Pending.Priority)
                {
                    return new TtsEnqueueResult(
                        TtsEnqueueDisposition.DroppedLowerPriority,
                        Pending,
                        "A higher-priority request with the same semantic ID is pending.");
                }

                var request = CreateRequest(
                    trimmedText,
                    normalizedSemanticId,
                    priority,
                    now,
                    ttlSeconds,
                    generation);
                Pending = request;
                MarkRecentlyAccepted(request.SemanticId, now);
                if (ShouldPreemptActive(request))
                {
                    RequestPreemption();
                    return new TtsEnqueueResult(
                        TtsEnqueueDisposition.AcceptedWithPreemption,
                        request,
                        "Pending request was coalesced and will preempt the active request.");
                }

                return new TtsEnqueueResult(
                    TtsEnqueueDisposition.CoalescedPending,
                    request,
                    "Pending request was replaced by its latest value.");
            }

            if (!upgradesActiveSemantic && IsCoolingDown(normalizedSemanticId, now))
            {
                return new TtsEnqueueResult(
                    TtsEnqueueDisposition.SuppressedCooldownDuplicate,
                    null,
                    "The semantic request is inside its duplicate cooldown.");
            }

            if (Active == null)
            {
                var request = CreateRequest(
                    trimmedText,
                    normalizedSemanticId,
                    priority,
                    now,
                    ttlSeconds,
                    generation);
                SetActive(request);
                MarkRecentlyAccepted(request.SemanticId, now);
                return new TtsEnqueueResult(
                    TtsEnqueueDisposition.AcceptedAsActive,
                    request,
                    "Request became active.");
            }

            if (Pending != null && priority < Pending.Priority)
            {
                return new TtsEnqueueResult(
                    TtsEnqueueDisposition.DroppedLowerPriority,
                    Pending,
                    "The pending slot contains a higher-priority request.");
            }

            // The queue is intentionally bounded. Equal-priority requests use latest-wins,
            // while higher-priority requests replace lower-priority pending work.
            var queuedRequest = CreateRequest(
                trimmedText,
                normalizedSemanticId,
                priority,
                now,
                ttlSeconds,
                generation);
            Pending = queuedRequest;
            MarkRecentlyAccepted(queuedRequest.SemanticId, now);

            if (ShouldPreemptActive(queuedRequest))
            {
                RequestPreemption();
                return new TtsEnqueueResult(
                    TtsEnqueueDisposition.AcceptedWithPreemption,
                    queuedRequest,
                    "Critical request will preempt lower-priority speech.");
            }

            return new TtsEnqueueResult(
                TtsEnqueueDisposition.AcceptedAsPending,
                queuedRequest,
                "Request entered the pending slot.");
        }

        public TtsSchedulerAction Poll(double now, bool backendIsSpeaking)
        {
            DropExpiredPending(now);
            if (Pending == null && activeState == ActiveRequestState.StopRequested)
            {
                // The preempting intent became stale before Stop was issued.
                // Keep the still-relevant active utterance instead of cancelling it for nothing.
                activeState = ActiveRequestState.Speaking;
            }

            DropExpiredUnstartedActive(now);
            PromotePendingIfIdle(now);
            PromoteHigherPriorityPendingBeforeStart(now);

            if (Active == null)
            {
                return TtsSchedulerAction.None;
            }

            switch (activeState)
            {
                case ActiveRequestState.Queued:
                    activeState = ActiveRequestState.StartRequested;
                    return new TtsSchedulerAction(TtsSchedulerActionType.Start, Active, false);

                case ActiveRequestState.StartRequested:
                    return TtsSchedulerAction.None;

                case ActiveRequestState.StopRequested:
                    if (backendIsSpeaking)
                    {
                        return new TtsSchedulerAction(TtsSchedulerActionType.StopForPreemption, Active, false);
                    }

                    CompleteActiveAndPromote(now);
                    if (Active == null)
                    {
                        return TtsSchedulerAction.None;
                    }

                    activeState = ActiveRequestState.StartRequested;
                    return new TtsSchedulerAction(TtsSchedulerActionType.Start, Active, false);

                case ActiveRequestState.WaitingForStop:
                {
                    var timedOut = backendIsSpeaking &&
                                   now - stopIssuedAt >= Math.Max(0d, options.StopTimeoutSeconds);
                    if (backendIsSpeaking && !timedOut)
                    {
                        return TtsSchedulerAction.None;
                    }

                    if (timedOut)
                    {
                        var timedOutRequest = Active;
                        Quarantine();
                        return new TtsSchedulerAction(
                            TtsSchedulerActionType.QuarantineBackend,
                            timedOutRequest,
                            true);
                    }

                    CompleteActiveAndPromote(now);
                    if (Active == null)
                    {
                        return TtsSchedulerAction.None;
                    }

                    activeState = ActiveRequestState.StartRequested;
                    return new TtsSchedulerAction(TtsSchedulerActionType.Start, Active, timedOut);
                }

                case ActiveRequestState.Speaking:
                    if (backendIsSpeaking)
                    {
                        observedBackendSpeaking = true;
                        return TtsSchedulerAction.None;
                    }

                    if (!observedBackendSpeaking &&
                        now - activeStartedAt < Math.Max(0d, options.StartObservationGraceSeconds))
                    {
                        return TtsSchedulerAction.None;
                    }

                    CompleteActiveAndPromote(now);
                    if (Active == null)
                    {
                        return TtsSchedulerAction.None;
                    }

                    activeState = ActiveRequestState.StartRequested;
                    return new TtsSchedulerAction(TtsSchedulerActionType.Start, Active, false);

                default:
                    return TtsSchedulerAction.None;
            }
        }

        public bool AcknowledgeStarted(long requestId, double now, bool backendIsSpeaking)
        {
            if (Active == null || Active.RequestId != requestId ||
                activeState != ActiveRequestState.StartRequested)
            {
                return false;
            }

            activeState = ActiveRequestState.Speaking;
            activeStartedAt = now;
            observedBackendSpeaking = backendIsSpeaking;
            return true;
        }

        public bool AcknowledgeStartFailed(long requestId, double now)
        {
            if (Active == null || Active.RequestId != requestId)
            {
                return false;
            }

            CompleteActiveAndPromote(now);
            return true;
        }

        public bool AcknowledgeStopIssued(long requestId, double now)
        {
            if (Active == null || Active.RequestId != requestId ||
                activeState != ActiveRequestState.StopRequested)
            {
                return false;
            }

            activeState = ActiveRequestState.WaitingForStop;
            stopIssuedAt = now;
            return true;
        }

        private TtsScheduledRequest CreateRequest(
            string text,
            string semanticId,
            TtsRequestPriority priority,
            double now,
            double ttlSeconds,
            int generation)
        {
            var requestId = nextRequestId++;
            if (nextRequestId <= 0)
            {
                nextRequestId = 1;
            }

            var expiresAt = ttlSeconds > 0d
                ? now + ttlSeconds
                : double.PositiveInfinity;
            return new TtsScheduledRequest(
                requestId,
                text,
                semanticId,
                priority,
                now,
                expiresAt,
                generation);
        }

        private bool ShouldPreemptActive(TtsScheduledRequest request)
        {
            return Active != null &&
                   activeState == ActiveRequestState.Speaking &&
                   request.Priority >= options.MinimumPreemptPriority &&
                   request.Priority > Active.Priority;
        }

        private void RequestPreemption()
        {
            if (activeState == ActiveRequestState.Speaking)
            {
                activeState = ActiveRequestState.StopRequested;
            }
        }

        private void SetActive(TtsScheduledRequest request)
        {
            Active = request;
            activeState = request == null ? ActiveRequestState.None : ActiveRequestState.Queued;
            observedBackendSpeaking = false;
            activeStartedAt = 0d;
            stopIssuedAt = 0d;
        }

        private void Quarantine()
        {
            Active = null;
            Pending = null;
            activeState = ActiveRequestState.None;
            observedBackendSpeaking = false;
            activeStartedAt = 0d;
            stopIssuedAt = 0d;
            IsQuarantined = true;
        }

        private void CompleteActiveAndPromote(double now)
        {
            SetActive(null);
            PromotePendingIfIdle(now);
        }

        private void PromotePendingIfIdle(double now)
        {
            if (Active != null)
            {
                return;
            }

            DropExpiredPending(now);
            if (Pending == null)
            {
                return;
            }

            var promoted = Pending;
            Pending = null;
            SetActive(promoted);
        }

        private void PromoteHigherPriorityPendingBeforeStart(double now)
        {
            if (activeState != ActiveRequestState.Queued ||
                Active == null ||
                Pending == null ||
                Pending.Priority <= Active.Priority)
            {
                return;
            }

            // Neither request has entered the platform backend yet, so the pending request
            // can safely take the active slot. Keep the displaced lower-priority request in
            // the single pending slot; its original TTL still decides whether it remains
            // relevant after the higher-priority speech finishes.
            var deferredRequest = Active;
            var promotedRequest = Pending;
            Pending = deferredRequest.IsExpired(now) ? null : deferredRequest;
            SetActive(promotedRequest);
        }

        private void DropExpiredPending(double now)
        {
            if (Pending != null && Pending.IsExpired(now))
            {
                Pending = null;
            }
        }

        private void DropExpiredUnstartedActive(double now)
        {
            if (Active == null || !Active.IsExpired(now))
            {
                return;
            }

            if (activeState == ActiveRequestState.Queued ||
                activeState == ActiveRequestState.StartRequested)
            {
                SetActive(null);
                PromotePendingIfIdle(now);
            }
        }

        private bool IsCoolingDown(string semanticId, double now)
        {
            var cooldown = Math.Max(0d, options.DuplicateCooldownSeconds);
            return cooldown > 0d &&
                   recentSemanticTimes.TryGetValue(semanticId, out var lastAcceptedAt) &&
                   now - lastAcceptedAt < cooldown;
        }

        private void MarkRecentlyAccepted(string semanticId, double now)
        {
            recentSemanticTimes[semanticId] = now;
            var maximumEntries = Math.Max(1, options.MaximumRecentSemanticIds);
            if (recentSemanticTimes.Count <= maximumEntries)
            {
                return;
            }

            string oldestKey = null;
            var oldestTime = double.PositiveInfinity;
            foreach (var pair in recentSemanticTimes)
            {
                if (pair.Value < oldestTime)
                {
                    oldestTime = pair.Value;
                    oldestKey = pair.Key;
                }
            }

            if (oldestKey != null)
            {
                recentSemanticTimes.Remove(oldestKey);
            }
        }

        private static bool IsSameSemantic(TtsScheduledRequest request, string semanticId)
        {
            return request != null &&
                   string.Equals(request.SemanticId, semanticId, StringComparison.Ordinal);
        }
    }
}
