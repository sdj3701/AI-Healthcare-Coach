#import <AVFoundation/AVFoundation.h>

static AVSpeechSynthesizer *AhcSynthesizer;

@interface AhcSpeechDelegate : NSObject <AVSpeechSynthesizerDelegate>
@end

@implementation AhcSpeechDelegate
- (void)speechSynthesizer:(AVSpeechSynthesizer *)synthesizer
 didFinishSpeechUtterance:(AVSpeechUtterance *)utterance
{
    [[AVAudioSession sharedInstance] setActive:NO
                                   withOptions:AVAudioSessionSetActiveOptionNotifyOthersOnDeactivation
                                         error:nil];
}

- (void)speechSynthesizer:(AVSpeechSynthesizer *)synthesizer
didCancelSpeechUtterance:(AVSpeechUtterance *)utterance
{
    [[AVAudioSession sharedInstance] setActive:NO
                                   withOptions:AVAudioSessionSetActiveOptionNotifyOthersOnDeactivation
                                         error:nil];
}
@end

static AhcSpeechDelegate *AhcDelegate;

static void AhcEnsureSynthesizer(void)
{
    if (AhcSynthesizer == nil) {
        AhcSynthesizer = [[AVSpeechSynthesizer alloc] init];
        AhcDelegate = [[AhcSpeechDelegate alloc] init];
        AhcSynthesizer.delegate = AhcDelegate;
    }
}

static void AhcRunOnMain(dispatch_block_t block)
{
    if ([NSThread isMainThread]) {
        block();
    } else {
        dispatch_async(dispatch_get_main_queue(), block);
    }
}

extern "C" {
    void AhcTtsInitialize(void)
    {
        AhcRunOnMain(^{ AhcEnsureSynthesizer(); });
    }

    int AhcTtsSpeak(const char *utf8Text)
    {
        if (utf8Text == nullptr || utf8Text[0] == '\0') {
            return 0;
        }

        NSString *text = [NSString stringWithUTF8String:utf8Text];
        if (text.length == 0) {
            return 0;
        }

        AhcRunOnMain(^{
            AhcEnsureSynthesizer();
            AVAudioSession *session = [AVAudioSession sharedInstance];
            [session setCategory:AVAudioSessionCategoryPlayback
                            mode:AVAudioSessionModeSpokenAudio
                         options:AVAudioSessionCategoryOptionMixWithOthers |
                                 AVAudioSessionCategoryOptionDuckOthers
                           error:nil];
            [session setActive:YES error:nil];

            [AhcSynthesizer stopSpeakingAtBoundary:AVSpeechBoundaryImmediate];
            AVSpeechUtterance *utterance = [AVSpeechUtterance speechUtteranceWithString:text];
            AVSpeechSynthesisVoice *koreanVoice = [AVSpeechSynthesisVoice voiceWithLanguage:@"ko-KR"];
            if (koreanVoice != nil) {
                utterance.voice = koreanVoice;
            }
            utterance.rate = AVSpeechUtteranceDefaultSpeechRate;
            [AhcSynthesizer speakUtterance:utterance];
        });
        return 1;
    }

    int AhcTtsIsSpeaking(void)
    {
        return AhcSynthesizer != nil && AhcSynthesizer.isSpeaking ? 1 : 0;
    }

    void AhcTtsStop(void)
    {
        AhcRunOnMain(^{
            [AhcSynthesizer stopSpeakingAtBoundary:AVSpeechBoundaryImmediate];
        });
    }

    void AhcTtsShutdown(void)
    {
        AhcRunOnMain(^{
            [AhcSynthesizer stopSpeakingAtBoundary:AVSpeechBoundaryImmediate];
            AhcSynthesizer.delegate = nil;
            AhcSynthesizer = nil;
            AhcDelegate = nil;
            [[AVAudioSession sharedInstance] setActive:NO
                                           withOptions:AVAudioSessionSetActiveOptionNotifyOthersOnDeactivation
                                                 error:nil];
        });
    }
}
