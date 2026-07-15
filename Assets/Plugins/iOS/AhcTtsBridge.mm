#import <AVFoundation/AVFoundation.h>

#include <algorithm>
#include <atomic>
#include <cstdint>
#include <cstring>
#include <deque>
#include <iterator>
#include <limits>
#include <mutex>
#include <string>
#include <utility>

// Existing Unity entry points remain available at the bottom of this file.
// The versioned event ABI below is additive and can be adopted by managed code
// without changing older builds that only call AhcTtsSpeak/AhcTtsIsSpeaking.
typedef struct AhcTtsEventV1 {
    int32_t version;
    int32_t type;
    int32_t code;
    int32_t droppedBefore;
    int64_t sequence;
    int64_t requestId;
    int64_t generation;
} AhcTtsEventV1;

static_assert(sizeof(AhcTtsEventV1) == 40, "AhcTtsEventV1 ABI must remain 40 bytes.");

enum AhcTtsEventType : int32_t {
    AhcTtsEventInitialized = 1,
    AhcTtsEventQueued = 2,
    AhcTtsEventStarted = 3,
    AhcTtsEventFinished = 4,
    AhcTtsEventCancelled = 5,
    AhcTtsEventDropped = 6,
    AhcTtsEventFailed = 7,
    AhcTtsEventShutdown = 8,
    AhcTtsEventInterruptionBegan = 9,
    AhcTtsEventInterruptionEnded = 10,
    AhcTtsEventMediaServicesLost = 11,
    AhcTtsEventMediaServicesReset = 12,
    AhcTtsEventRouteChanged = 13,
    AhcTtsEventPaused = 14,
    AhcTtsEventContinued = 15
};

enum AhcTtsEventCode : int32_t {
    AhcTtsCodeNone = 0,
    AhcTtsCodePendingReplaced = 1,
    AhcTtsCodeLowerPriority = 2,
    AhcTtsCodeExplicitStop = 3,
    AhcTtsCodeShutdown = 4,
    AhcTtsCodeMediaServicesReset = 5,
    AhcTtsCodeInterruptedForNewRequest = 6
};

enum AhcTtsEnqueueFlags : int32_t {
    // The default never stops the current utterance. At most one pending request
    // is retained, and a newer request replaces it according to priority.
    AhcTtsEnqueueDefault = 0,
    // A request with at least the active request's priority may cancel it. Only
    // one stop is issued until the matching delegate callback arrives.
    AhcTtsEnqueueInterruptCurrent = 1 << 0
};

struct AhcQueuedEvent {
    AhcTtsEventV1 value;
    std::string message;
};

static constexpr size_t AhcMaximumEventCount = 64;
// Non-terminal traffic is capped below the hard limit so completion/failure
// events still have room while the managed consumer is briefly delayed.
static constexpr size_t AhcTerminalEventReserve = 16;
static std::mutex AhcEventMutex;
static std::deque<AhcQueuedEvent> AhcEvents;
static std::atomic<int64_t> AhcNextEventSequence{0};
static std::atomic<int64_t> AhcDroppedEventCount{0};
static std::atomic<int64_t> AhcDroppedTerminalEventCount{0};
static std::atomic<int64_t> AhcDroppedNonTerminalEventCount{0};
static std::atomic<int64_t> AhcNextRequestId{0};
static std::atomic<int64_t> AhcGenerationCounter{0};
static std::atomic<int64_t> AhcCurrentGeneration{0};
static std::atomic<int32_t> AhcSpeechBusy{0};
static int64_t AhcDroppedSinceLastAcceptedEvent;

@interface AhcSpeechRequest : NSObject
@property(nonatomic, assign) int64_t requestId;
@property(nonatomic, assign) int64_t generation;
@property(nonatomic, assign) int32_t priority;
@property(nonatomic, copy) NSString *text;
@end

@implementation AhcSpeechRequest
@end

@interface AhcSpeechDelegate : NSObject <AVSpeechSynthesizerDelegate> {
@private
    int64_t _generation;
}
- (instancetype)initWithGeneration:(int64_t)generation;
- (void)invalidate;
@end

static AVSpeechSynthesizer *AhcSynthesizer;
static AhcSpeechDelegate *AhcDelegate;
static AVSpeechSynthesisVoice *AhcKoreanVoice;
static AhcSpeechRequest *AhcActiveRequest;
static AhcSpeechRequest *AhcPendingRequest;
static AVSpeechUtterance *AhcActiveUtterance;
static BOOL AhcStopInFlight;
// Ownership changes at ABI call time, while synthesizer mutations stay on main.
// This protects the process-wide synthesizer when the demo and workout wrappers
// overlap or when a background caller initializes and immediately disposes.
static std::atomic<int32_t> AhcOwnerCount{0};

static void AhcRunOnMain(dispatch_block_t block);
static void AhcDispatchOnMain(dispatch_block_t block);
static void AhcEnsureSynthesizer(void);
static void AhcStartRequest(AhcSpeechRequest *request);
static void AhcStartPendingRequestIfPossible(void);
static void AhcCompleteActiveRequest(
    AVSpeechUtterance *utterance,
    int64_t delegateGeneration,
    int32_t eventType,
    int32_t eventCode,
    NSString *message);
static void AhcHandleMediaServicesReset(int64_t delegateGeneration);

static bool AhcIsTerminalOrCriticalEvent(int32_t type)
{
    switch (type) {
        case AhcTtsEventFinished:
        case AhcTtsEventCancelled:
        case AhcTtsEventDropped:
        case AhcTtsEventFailed:
        case AhcTtsEventShutdown:
        case AhcTtsEventMediaServicesLost:
        case AhcTtsEventMediaServicesReset:
            return true;
        default:
            return false;
    }
}

static void AhcRecordDroppedEvent(bool terminal)
{
    AhcDroppedEventCount.fetch_add(1, std::memory_order_relaxed);
    if (terminal) {
        AhcDroppedTerminalEventCount.fetch_add(1, std::memory_order_relaxed);
    } else {
        AhcDroppedNonTerminalEventCount.fetch_add(1, std::memory_order_relaxed);
    }
    ++AhcDroppedSinceLastAcceptedEvent;
}

static void AhcPushEvent(
    int32_t type,
    int32_t code,
    int64_t requestId,
    int64_t generation,
    NSString *message)
{
    AhcQueuedEvent event{};
    event.value.version = 1;
    event.value.type = type;
    event.value.code = code;
    event.value.droppedBefore = 0;
    event.value.requestId = requestId;
    event.value.generation = generation;
    const char *utf8 = message == nil ? nullptr : message.UTF8String;
    event.message = utf8 == nullptr ? std::string() : std::string(utf8);

    std::lock_guard<std::mutex> guard(AhcEventMutex);
    const bool incomingIsTerminal = AhcIsTerminalOrCriticalEvent(type);

    // Reserve capacity for request terminal states. Once the soft limit is
    // reached, route/started/queued chatter is discarded and the monotonic
    // out-of-band counters make that unhealthy condition observable.
    if (!incomingIsTerminal &&
        AhcEvents.size() >= AhcMaximumEventCount - AhcTerminalEventReserve) {
        AhcRecordDroppedEvent(false);
        return;
    }

    if (AhcEvents.size() >= AhcMaximumEventCount) {
        // Never invalidate the event currently presented by Poll. Prefer the
        // oldest non-terminal event after the front, and never evict a request's
        // terminal state merely to make room for status chatter.
        auto candidate = AhcEvents.end();
        if (AhcEvents.size() > 1) {
            candidate = std::find_if(
                std::next(AhcEvents.begin()),
                AhcEvents.end(),
                [](const AhcQueuedEvent &queued) {
                    return !AhcIsTerminalOrCriticalEvent(queued.value.type);
                });
        }

        if (candidate == AhcEvents.end()) {
            AhcRecordDroppedEvent(incomingIsTerminal);
            return;
        }

        AhcRecordDroppedEvent(false);
        AhcEvents.erase(candidate);
    }

    const int64_t droppedBefore = AhcDroppedSinceLastAcceptedEvent;
    event.value.droppedBefore = droppedBefore > std::numeric_limits<int32_t>::max()
        ? std::numeric_limits<int32_t>::max()
        : static_cast<int32_t>(droppedBefore);
    AhcDroppedSinceLastAcceptedEvent = 0;
    // Allocate the sequence while holding the queue lock so sequence order and
    // poll order remain identical even if a future producer runs off-main.
    event.value.sequence = AhcNextEventSequence.fetch_add(1, std::memory_order_relaxed) + 1;
    AhcEvents.push_back(std::move(event));
}

static void AhcUpdateBusyState(void)
{
    AhcSpeechBusy.store(
        AhcActiveRequest != nil || AhcPendingRequest != nil ? 1 : 0,
        std::memory_order_release);
}

static void AhcRunOnMain(dispatch_block_t block)
{
    if (block == nil) {
        return;
    }

    if ([NSThread isMainThread]) {
        block();
    } else {
        // Never synchronously wait for Unity's main thread. The request id and
        // event queue provide completion/status information to background callers.
        dispatch_async(dispatch_get_main_queue(), block);
    }
}

static void AhcDispatchOnMain(dispatch_block_t block)
{
    if (block == nil) {
        return;
    }

    // Unity invokes the public ABI from its main/update thread. Always enqueue
    // public mutations so AVSpeechSynthesizer work never extends that P/Invoke
    // frame. FIFO ordering on the main queue preserves initialize/enqueue/stop.
    dispatch_async(dispatch_get_main_queue(), block);
}

@implementation AhcSpeechDelegate

- (instancetype)initWithGeneration:(int64_t)generation
{
    self = [super init];
    if (self == nil) {
        return nil;
    }

    _generation = generation;
    NSNotificationCenter *center = [NSNotificationCenter defaultCenter];
    [center addObserver:self
               selector:@selector(handleAudioSessionInterruption:)
                   name:AVAudioSessionInterruptionNotification
                 object:nil];
    [center addObserver:self
               selector:@selector(handleAudioRouteChange:)
                   name:AVAudioSessionRouteChangeNotification
                 object:nil];
    [center addObserver:self
               selector:@selector(handleMediaServicesLost:)
                   name:AVAudioSessionMediaServicesWereLostNotification
                 object:nil];
    [center addObserver:self
               selector:@selector(handleMediaServicesReset:)
                   name:AVAudioSessionMediaServicesWereResetNotification
                 object:nil];
    return self;
}

- (void)invalidate
{
    [[NSNotificationCenter defaultCenter] removeObserver:self];
}

- (void)dealloc
{
    [self invalidate];
}

- (void)speechSynthesizer:(AVSpeechSynthesizer *)synthesizer
 didStartSpeechUtterance:(AVSpeechUtterance *)utterance
{
    const int64_t generation = _generation;
    AhcRunOnMain(^{
        if (generation != AhcCurrentGeneration.load(std::memory_order_acquire) ||
            utterance != AhcActiveUtterance ||
            AhcActiveRequest == nil) {
            return;
        }

        AhcSpeechBusy.store(1, std::memory_order_release);
        AhcPushEvent(
            AhcTtsEventStarted,
            AhcTtsCodeNone,
            AhcActiveRequest.requestId,
            generation,
            @"Speech started.");
    });
}

- (void)speechSynthesizer:(AVSpeechSynthesizer *)synthesizer
 didFinishSpeechUtterance:(AVSpeechUtterance *)utterance
{
    const int64_t generation = _generation;
    AhcRunOnMain(^{
        AhcCompleteActiveRequest(
            utterance,
            generation,
            AhcTtsEventFinished,
            AhcTtsCodeNone,
            @"Speech finished.");
    });
}

- (void)speechSynthesizer:(AVSpeechSynthesizer *)synthesizer
 didCancelSpeechUtterance:(AVSpeechUtterance *)utterance
{
    const int64_t generation = _generation;
    AhcRunOnMain(^{
        const int32_t code = AhcStopInFlight && AhcPendingRequest != nil
            ? AhcTtsCodeInterruptedForNewRequest
            : AhcTtsCodeExplicitStop;
        AhcCompleteActiveRequest(
            utterance,
            generation,
            AhcTtsEventCancelled,
            code,
            @"Speech cancelled.");
    });
}

- (void)speechSynthesizer:(AVSpeechSynthesizer *)synthesizer
 didPauseSpeechUtterance:(AVSpeechUtterance *)utterance
{
    const int64_t generation = _generation;
    AhcRunOnMain(^{
        if (generation != AhcCurrentGeneration.load(std::memory_order_acquire) ||
            utterance != AhcActiveUtterance ||
            AhcActiveRequest == nil) {
            return;
        }
        AhcPushEvent(
            AhcTtsEventPaused,
            AhcTtsCodeNone,
            AhcActiveRequest.requestId,
            generation,
            @"Speech paused.");
    });
}

- (void)speechSynthesizer:(AVSpeechSynthesizer *)synthesizer
 didContinueSpeechUtterance:(AVSpeechUtterance *)utterance
{
    const int64_t generation = _generation;
    AhcRunOnMain(^{
        if (generation != AhcCurrentGeneration.load(std::memory_order_acquire) ||
            utterance != AhcActiveUtterance ||
            AhcActiveRequest == nil) {
            return;
        }
        AhcPushEvent(
            AhcTtsEventContinued,
            AhcTtsCodeNone,
            AhcActiveRequest.requestId,
            generation,
            @"Speech continued.");
    });
}

- (void)handleAudioSessionInterruption:(NSNotification *)notification
{
    NSNumber *typeValue = notification.userInfo[AVAudioSessionInterruptionTypeKey];
    NSNumber *optionsValue = notification.userInfo[AVAudioSessionInterruptionOptionKey];
    const AVAudioSessionInterruptionType type =
        (AVAudioSessionInterruptionType)typeValue.unsignedIntegerValue;
    const int32_t options = (int32_t)optionsValue.unsignedIntegerValue;
    const int64_t generation = _generation;

    AhcRunOnMain(^{
        if (generation != AhcCurrentGeneration.load(std::memory_order_acquire)) {
            return;
        }

        const int64_t requestId = AhcActiveRequest == nil ? 0 : AhcActiveRequest.requestId;
        if (type == AVAudioSessionInterruptionTypeBegan) {
            AhcPushEvent(
                AhcTtsEventInterruptionBegan,
                options,
                requestId,
                generation,
                @"Audio interruption began.");
        } else {
            // usesApplicationAudioSession=false delegates reactivation/resume to
            // AVSpeechSynthesizer. Do not fight the system with setActive calls.
            AhcPushEvent(
                AhcTtsEventInterruptionEnded,
                options,
                requestId,
                generation,
                @"Audio interruption ended.");
        }
    });
}

- (void)handleAudioRouteChange:(NSNotification *)notification
{
    NSNumber *reasonValue = notification.userInfo[AVAudioSessionRouteChangeReasonKey];
    const int32_t reason = (int32_t)reasonValue.unsignedIntegerValue;
    const int64_t generation = _generation;

    AhcRunOnMain(^{
        if (generation != AhcCurrentGeneration.load(std::memory_order_acquire)) {
            return;
        }
        const int64_t requestId = AhcActiveRequest == nil ? 0 : AhcActiveRequest.requestId;
        AhcPushEvent(
            AhcTtsEventRouteChanged,
            reason,
            requestId,
            generation,
            @"Audio route changed.");
    });
}

- (void)handleMediaServicesLost:(NSNotification *)notification
{
    const int64_t generation = _generation;
    AhcRunOnMain(^{
        if (generation != AhcCurrentGeneration.load(std::memory_order_acquire)) {
            return;
        }
        const int64_t requestId = AhcActiveRequest == nil ? 0 : AhcActiveRequest.requestId;
        AhcPushEvent(
            AhcTtsEventMediaServicesLost,
            AhcTtsCodeNone,
            requestId,
            generation,
            @"Audio media services were lost.");
    });
}

- (void)handleMediaServicesReset:(NSNotification *)notification
{
    const int64_t generation = _generation;
    AhcRunOnMain(^{ AhcHandleMediaServicesReset(generation); });
}

@end

static void AhcEnsureSynthesizer(void)
{
    NSCAssert([NSThread isMainThread], @"TTS synthesizer state must be mutated on the main thread.");
    if (AhcSynthesizer != nil) {
        return;
    }

    const int64_t generation =
        AhcGenerationCounter.fetch_add(1, std::memory_order_relaxed) + 1;
    AhcCurrentGeneration.store(generation, std::memory_order_release);

    AhcSynthesizer = [[AVSpeechSynthesizer alloc] init];
    if (AhcSynthesizer == nil) {
        AhcCurrentGeneration.store(0, std::memory_order_release);
        AhcPushEvent(
            AhcTtsEventFailed,
            AhcTtsCodeNone,
            0,
            generation,
            @"Failed to create AVSpeechSynthesizer.");
        return;
    }

    // The previous bridge reconfigured and deactivated the app-wide shared
    // AVAudioSession for every utterance. Unity also owns audio objects on that
    // session, so those transitions could stall or stop the whole player. With
    // this set to NO, iOS creates and manages a separate speech audio session,
    // including activation, interruption handling, mixing and ducking.
    AhcSynthesizer.usesApplicationAudioSession = NO;
    AhcDelegate = [[AhcSpeechDelegate alloc] initWithGeneration:generation];
    AhcSynthesizer.delegate = AhcDelegate;

    // Voice discovery can be non-trivial on the first request. Cache it during
    // initialization instead of repeating it in the pose-result hot path.
    AhcKoreanVoice = [AVSpeechSynthesisVoice voiceWithLanguage:@"ko-KR"];
    AhcPushEvent(
        AhcTtsEventInitialized,
        AhcTtsCodeNone,
        0,
        generation,
        @"AVSpeechSynthesizer initialized with a system-managed speech session.");
}

static void AhcStartRequest(AhcSpeechRequest *request)
{
    NSCAssert([NSThread isMainThread], @"TTS requests must start on the main thread.");
    if (request == nil || AhcActiveRequest != nil) {
        return;
    }

    AhcEnsureSynthesizer();
    if (AhcSynthesizer == nil) {
        AhcPushEvent(
            AhcTtsEventFailed,
            AhcTtsCodeNone,
            request.requestId,
            request.generation,
            @"Speech synthesizer is unavailable.");
        AhcUpdateBusyState();
        return;
    }

    request.generation = AhcCurrentGeneration.load(std::memory_order_acquire);
    AhcActiveRequest = request;
    AhcActiveUtterance = [AVSpeechUtterance speechUtteranceWithString:request.text];
    if (AhcKoreanVoice != nil) {
        AhcActiveUtterance.voice = AhcKoreanVoice;
    }
    AhcActiveUtterance.rate = AVSpeechUtteranceDefaultSpeechRate;
    AhcStopInFlight = NO;
    AhcSpeechBusy.store(1, std::memory_order_release);
    [AhcSynthesizer speakUtterance:AhcActiveUtterance];
}

static void AhcStartPendingRequestIfPossible(void)
{
    NSCAssert([NSThread isMainThread], @"TTS queue must advance on the main thread.");
    if (AhcActiveRequest != nil || AhcPendingRequest == nil) {
        AhcUpdateBusyState();
        return;
    }

    AhcSpeechRequest *next = AhcPendingRequest;
    AhcPendingRequest = nil;
    next.generation = AhcCurrentGeneration.load(std::memory_order_acquire);
    AhcStartRequest(next);
}

static void AhcCompleteActiveRequest(
    AVSpeechUtterance *utterance,
    int64_t delegateGeneration,
    int32_t eventType,
    int32_t eventCode,
    NSString *message)
{
    NSCAssert([NSThread isMainThread], @"TTS completion must be handled on the main thread.");
    if (delegateGeneration != AhcCurrentGeneration.load(std::memory_order_acquire) ||
        utterance != AhcActiveUtterance ||
        AhcActiveRequest == nil) {
        return;
    }

    const int64_t requestId = AhcActiveRequest.requestId;
    const int64_t generation = AhcActiveRequest.generation;
    AhcActiveUtterance = nil;
    AhcActiveRequest = nil;
    AhcStopInFlight = NO;
    AhcPushEvent(eventType, eventCode, requestId, generation, message);
    AhcStartPendingRequestIfPossible();
}

static void AhcQueueRequestOnMain(
    NSString *text,
    int64_t requestId,
    int32_t priority,
    int32_t flags)
{
    NSCAssert([NSThread isMainThread], @"TTS queue must be mutated on the main thread.");
    AhcEnsureSynthesizer();
    const int64_t generation = AhcCurrentGeneration.load(std::memory_order_acquire);
    if (AhcSynthesizer == nil || generation == 0) {
        AhcPushEvent(
            AhcTtsEventFailed,
            AhcTtsCodeNone,
            requestId,
            generation,
            @"Speech request could not be queued because initialization failed.");
        return;
    }

    AhcSpeechRequest *request = [[AhcSpeechRequest alloc] init];
    request.requestId = requestId;
    request.generation = generation;
    request.priority = priority;
    request.text = text;

    if (AhcActiveRequest == nil) {
        AhcPushEvent(
            AhcTtsEventQueued,
            AhcTtsCodeNone,
            requestId,
            generation,
            @"Speech request queued.");
        AhcStartRequest(request);
        return;
    }

    BOOL acceptedAsPending = NO;
    if (AhcPendingRequest == nil) {
        AhcPendingRequest = request;
        acceptedAsPending = YES;
    } else if (priority >= AhcPendingRequest.priority) {
        AhcPushEvent(
            AhcTtsEventDropped,
            AhcTtsCodePendingReplaced,
            AhcPendingRequest.requestId,
            AhcPendingRequest.generation,
            @"Pending speech request was replaced by a newer request.");
        AhcPendingRequest = request;
        acceptedAsPending = YES;
    } else {
        AhcPushEvent(
            AhcTtsEventDropped,
            AhcTtsCodeLowerPriority,
            requestId,
            generation,
            @"Speech request was dropped because a higher-priority request is pending.");
    }

    if (!acceptedAsPending) {
        AhcUpdateBusyState();
        return;
    }

    AhcPushEvent(
        AhcTtsEventQueued,
        AhcTtsCodeNone,
        requestId,
        generation,
        @"Speech request queued as the latest pending request.");
    AhcUpdateBusyState();

    const BOOL mayInterrupt =
        (flags & AhcTtsEnqueueInterruptCurrent) != 0 &&
        priority >= AhcActiveRequest.priority;
    if (mayInterrupt && !AhcStopInFlight) {
        AhcStopInFlight = YES;
        AVSpeechUtterance *activeUtterance = AhcActiveUtterance;
        const int64_t activeGeneration = AhcActiveRequest.generation;
        const BOOL stopAccepted =
            [AhcSynthesizer stopSpeakingAtBoundary:AVSpeechBoundaryImmediate];
        if (!stopAccepted) {
            AhcCompleteActiveRequest(
                activeUtterance,
                activeGeneration,
                AhcTtsEventCancelled,
                AhcTtsCodeInterruptedForNewRequest,
                @"Speech was cancelled for a newer request.");
        }
    }
}

static void AhcHandleMediaServicesReset(int64_t delegateGeneration)
{
    NSCAssert([NSThread isMainThread], @"Media-services reset must be handled on the main thread.");
    if (delegateGeneration != AhcCurrentGeneration.load(std::memory_order_acquire)) {
        return;
    }

    const int64_t activeRequestId = AhcActiveRequest == nil ? 0 : AhcActiveRequest.requestId;
    AhcPushEvent(
        AhcTtsEventMediaServicesReset,
        AhcTtsCodeMediaServicesReset,
        activeRequestId,
        delegateGeneration,
        @"Audio media services were reset.");

    if (AhcActiveRequest != nil) {
        AhcPushEvent(
            AhcTtsEventFailed,
            AhcTtsCodeMediaServicesReset,
            AhcActiveRequest.requestId,
            AhcActiveRequest.generation,
            @"Active speech ended because audio media services reset.");
    }

    if (AhcPendingRequest != nil) {
        // Native code has no authoritative age/priority context after a reset.
        // Report a terminal drop and let the managed scheduler decide whether a
        // still-fresh intent should be submitted again.
        AhcPushEvent(
            AhcTtsEventDropped,
            AhcTtsCodeMediaServicesReset,
            AhcPendingRequest.requestId,
            AhcPendingRequest.generation,
            @"Pending speech requires managed resubmission after media-services reset.");
    }

    // The active utterance may have been partially heard, so do not replay it.
    // Pending TTL is owned by managed code, so native code also does not replay it.
    AhcSynthesizer.delegate = nil;
    [AhcDelegate invalidate];
    AhcSynthesizer = nil;
    AhcDelegate = nil;
    AhcKoreanVoice = nil;
    AhcActiveRequest = nil;
    AhcPendingRequest = nil;
    AhcActiveUtterance = nil;
    AhcStopInFlight = NO;
    AhcCurrentGeneration.store(0, std::memory_order_release);

    AhcEnsureSynthesizer();
    AhcUpdateBusyState();
}

extern "C" {
    void AhcTtsInitialize(void)
    {
        int32_t owners = AhcOwnerCount.load(std::memory_order_acquire);
        while (owners < std::numeric_limits<int32_t>::max() &&
               !AhcOwnerCount.compare_exchange_weak(
                   owners,
                   owners + 1,
                   std::memory_order_acq_rel,
                   std::memory_order_acquire)) {
        }

        AhcDispatchOnMain(^{
            // The matching shutdown may have arrived before this asynchronous
            // block. Do not resurrect a synthesizer with no remaining owner.
            if (AhcOwnerCount.load(std::memory_order_acquire) > 0) {
                AhcEnsureSynthesizer();
            }
        });
    }

    // Additive request API. Returns a positive, process-monotonic request id on
    // acceptance, or zero when the UTF-8 input is invalid. Flags use
    // AhcTtsEnqueueFlags; priority is an application-defined signed integer.
    int64_t AhcTtsEnqueue(const char *utf8Text, int32_t priority, int32_t flags)
    {
        if (utf8Text == nullptr || utf8Text[0] == '\0') {
            return 0;
        }

        NSString *text = [NSString stringWithUTF8String:utf8Text];
        if (text.length == 0) {
            return 0;
        }

        text = [text copy];
        priority = std::max<int32_t>(-1000, std::min<int32_t>(1000, priority));
        flags &= AhcTtsEnqueueInterruptCurrent;
        const int64_t requestId =
            AhcNextRequestId.fetch_add(1, std::memory_order_relaxed) + 1;

        AhcDispatchOnMain(^{ AhcQueueRequestOnMain(text, requestId, priority, flags); });
        return requestId;
    }

    int AhcTtsSpeak(const char *utf8Text)
    {
        // Backward-compatible calls no longer flush an active utterance. They
        // keep only the latest equal-priority pending request.
        return AhcTtsEnqueue(utf8Text, 0, AhcTtsEnqueueDefault) != 0 ? 1 : 0;
    }

    int AhcTtsIsSpeaking(void)
    {
        // Include the short Starting and pending hand-off states so managed
        // ducking does not oscillate between consecutive utterances.
        return AhcSpeechBusy.load(std::memory_order_acquire) != 0 ? 1 : 0;
    }

    void AhcTtsStop(void)
    {
        AhcDispatchOnMain(^{
            if (AhcPendingRequest != nil) {
                AhcPushEvent(
                    AhcTtsEventDropped,
                    AhcTtsCodeExplicitStop,
                    AhcPendingRequest.requestId,
                    AhcPendingRequest.generation,
                    @"Pending speech request was cleared by Stop.");
                AhcPendingRequest = nil;
            }

            if (AhcSynthesizer == nil || AhcActiveRequest == nil) {
                AhcStopInFlight = NO;
                AhcUpdateBusyState();
                return;
            }

            if (AhcStopInFlight) {
                AhcUpdateBusyState();
                return;
            }

            AhcStopInFlight = YES;
            AVSpeechUtterance *activeUtterance = AhcActiveUtterance;
            const int64_t activeGeneration = AhcActiveRequest.generation;
            const BOOL stopAccepted =
                [AhcSynthesizer stopSpeakingAtBoundary:AVSpeechBoundaryImmediate];
            if (!stopAccepted) {
                AhcCompleteActiveRequest(
                    activeUtterance,
                    activeGeneration,
                    AhcTtsEventCancelled,
                    AhcTtsCodeExplicitStop,
                    @"Speech stopped.");
            }
        });
    }

    void AhcTtsShutdown(void)
    {
        int32_t owners = AhcOwnerCount.load(std::memory_order_acquire);
        while (owners > 0 &&
               !AhcOwnerCount.compare_exchange_weak(
                   owners,
                   owners - 1,
                   std::memory_order_acq_rel,
                   std::memory_order_acquire)) {
        }

        AhcDispatchOnMain(^{
            // AhcSynthesizer is process-global. A scene/controller may release
            // its wrapper while another initialized wrapper still owns it.
            if (AhcOwnerCount.load(std::memory_order_acquire) > 0) {
                return;
            }

            const int64_t generation =
                AhcCurrentGeneration.load(std::memory_order_acquire);

            if (AhcSynthesizer == nil &&
                AhcActiveRequest == nil &&
                AhcPendingRequest == nil) {
                AhcSpeechBusy.store(0, std::memory_order_release);
                return;
            }

            if (AhcPendingRequest != nil) {
                AhcPushEvent(
                    AhcTtsEventDropped,
                    AhcTtsCodeShutdown,
                    AhcPendingRequest.requestId,
                    AhcPendingRequest.generation,
                    @"Pending speech request was dropped during shutdown.");
            }
            if (AhcActiveRequest != nil) {
                AhcPushEvent(
                    AhcTtsEventCancelled,
                    AhcTtsCodeShutdown,
                    AhcActiveRequest.requestId,
                    AhcActiveRequest.generation,
                    @"Active speech was cancelled during shutdown.");
            }

            // Detach first so a synchronous cancellation callback cannot mutate
            // a new generation or start a pending request during teardown.
            AhcSynthesizer.delegate = nil;
            [AhcDelegate invalidate];
            if (AhcSynthesizer != nil && AhcSynthesizer.isSpeaking) {
                [AhcSynthesizer stopSpeakingAtBoundary:AVSpeechBoundaryImmediate];
            }

            AhcSynthesizer = nil;
            AhcDelegate = nil;
            AhcKoreanVoice = nil;
            AhcActiveRequest = nil;
            AhcPendingRequest = nil;
            AhcActiveUtterance = nil;
            AhcStopInFlight = NO;
            AhcCurrentGeneration.store(0, std::memory_order_release);
            AhcSpeechBusy.store(0, std::memory_order_release);
            AhcPushEvent(
                AhcTtsEventShutdown,
                AhcTtsCodeShutdown,
                0,
                generation,
                @"AVSpeechSynthesizer shut down.");
        });
    }

    // Non-destructive ordered poll. The caller must acknowledge the front event
    // with AhcTtsAcknowledgeEvent before the next event becomes visible.
    int AhcTtsPollEvent(AhcTtsEventV1 *outEvent)
    {
        if (outEvent == nullptr) {
            return 0;
        }

        std::lock_guard<std::mutex> guard(AhcEventMutex);
        if (AhcEvents.empty()) {
            return 0;
        }
        *outEvent = AhcEvents.front().value;
        return 1;
    }

    // Returns 1 when the front event was removed, 0 when the queue is empty,
    // and -1 when sequence does not match the current front event.
    int AhcTtsAcknowledgeEvent(int64_t sequence)
    {
        std::lock_guard<std::mutex> guard(AhcEventMutex);
        if (AhcEvents.empty()) {
            return 0;
        }
        if (AhcEvents.front().value.sequence != sequence) {
            return -1;
        }
        AhcEvents.pop_front();
        return 1;
    }

    // Returns required UTF-8 bytes including the trailing NUL, or 0 when the
    // event was evicted/acknowledged. A null buffer can be used as a size query.
    int32_t AhcTtsGetEventMessage(
        int64_t sequence,
        char *buffer,
        int32_t capacity)
    {
        std::lock_guard<std::mutex> guard(AhcEventMutex);
        const auto found = std::find_if(
            AhcEvents.begin(),
            AhcEvents.end(),
            [sequence](const AhcQueuedEvent &event) {
                return event.value.sequence == sequence;
            });
        if (found == AhcEvents.end()) {
            return 0;
        }

        const size_t requiredSize = found->message.size() + 1;
        const int32_t maximumInt32 = std::numeric_limits<int32_t>::max();
        const int32_t required = requiredSize > static_cast<size_t>(maximumInt32)
            ? maximumInt32
            : static_cast<int32_t>(requiredSize);
        if (buffer == nullptr || capacity <= 0) {
            return required;
        }

        const size_t copyCount = std::min(
            found->message.size(),
            static_cast<size_t>(capacity - 1));
        if (copyCount > 0) {
            std::memcpy(buffer, found->message.data(), copyCount);
        }
        buffer[copyCount] = '\0';
        return required;
    }

    int64_t AhcTtsGetGeneration(void)
    {
        return AhcCurrentGeneration.load(std::memory_order_acquire);
    }

    int64_t AhcTtsGetDroppedEventCount(void)
    {
        return AhcDroppedEventCount.load(std::memory_order_acquire);
    }

    int64_t AhcTtsGetDroppedTerminalEventCount(void)
    {
        return AhcDroppedTerminalEventCount.load(std::memory_order_acquire);
    }

    int64_t AhcTtsGetDroppedNonTerminalEventCount(void)
    {
        return AhcDroppedNonTerminalEventCount.load(std::memory_order_acquire);
    }

    int32_t AhcTtsGetOwnerCount(void)
    {
        return AhcOwnerCount.load(std::memory_order_acquire);
    }

    int32_t AhcTtsGetEventQueueCapacity(void)
    {
        return static_cast<int32_t>(AhcMaximumEventCount);
    }

    int32_t AhcTtsGetEventQueueCount(void)
    {
        std::lock_guard<std::mutex> guard(AhcEventMutex);
        return static_cast<int32_t>(AhcEvents.size());
    }
}
