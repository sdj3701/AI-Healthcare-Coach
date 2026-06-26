using UnityEngine;
using UnityEngine.Audio;

namespace AIHealthcareCoach.Tts
{
    public sealed class TtsAudioDuckingController : MonoBehaviour
    {
        private const float MinLinearVolume = 0.0001f;

        [Header("Mixer")]
        [SerializeField] private AudioMixer audioMixer;
        [SerializeField] private string musicVolumeParameter = "MusicVolumeDb";
        [SerializeField] private string coachVoiceVolumeParameter = "CoachVoiceVolumeDb";

        [Header("Volumes")]
        [SerializeField] private float normalMusicVolumeDb = 0f;
        [SerializeField] private float duckedMusicVolumeDb = -14f;
        [SerializeField] private float coachVoiceVolumeDb = 0f;

        [Header("Timing")]
        [SerializeField] private float duckFadeInSeconds = 0.15f;
        [SerializeField] private float releaseSeconds = 0.5f;
        [SerializeField] private float holdAfterTtsSeconds = 0.3f;

        [Header("Test Music")]
        [SerializeField] private bool createTestMusicSource = true;
        [SerializeField] private AudioSource[] musicSources;
        [SerializeField] private float testMusicVolume = 0.45f;

        private AudioSource testMusicSource;
        private float currentMusicVolumeDb;
        private float sourceMusicVolume;
        private float targetMusicVolumeDb;
        private float targetSourceVolume;
        private float fadeStartedAt;
        private float fadeDuration;
        private float fadeStartVolumeDb;
        private float fadeStartSourceVolume;
        private float restoreAllowedAt;
        private bool duckRequested;

        public TtsPlaybackState PlaybackState { get; private set; } = TtsPlaybackState.Idle;

        public bool IsDucking
        {
            get { return PlaybackState == TtsPlaybackState.Ducking || duckRequested; }
        }

        public bool IsTestMusicPlaying
        {
            get { return testMusicSource != null && testMusicSource.isPlaying; }
        }

        public float CurrentMusicVolumeDb
        {
            get { return currentMusicVolumeDb; }
        }

        public float DuckedMusicVolumeDb
        {
            get { return duckedMusicVolumeDb; }
        }

        public string StatusText
        {
            get
            {
                return $"{PlaybackState} / Music {currentMusicVolumeDb:0.0} dB";
            }
        }

        private void Awake()
        {
            currentMusicVolumeDb = normalMusicVolumeDb;
            sourceMusicVolume = testMusicVolume;
            ApplyMusicVolume(currentMusicVolumeDb, sourceMusicVolume);
            ApplyCoachVoiceVolume();

            if (createTestMusicSource)
            {
                EnsureTestMusicSource();
            }
        }

        private void Update()
        {
            if (!duckRequested
                && PlaybackState == TtsPlaybackState.Ducking
                && Time.unscaledTime >= restoreAllowedAt)
            {
                StartFade(normalMusicVolumeDb, testMusicVolume, releaseSeconds, TtsPlaybackState.Restoring);
            }

            if (PlaybackState != TtsPlaybackState.Ducking && PlaybackState != TtsPlaybackState.Restoring)
            {
                return;
            }

            var duration = Mathf.Max(0.001f, fadeDuration);
            var t = Mathf.Clamp01((Time.unscaledTime - fadeStartedAt) / duration);
            var eased = Mathf.SmoothStep(0f, 1f, t);

            currentMusicVolumeDb = Mathf.Lerp(fadeStartVolumeDb, targetMusicVolumeDb, eased);
            sourceMusicVolume = Mathf.Lerp(fadeStartSourceVolume, targetSourceVolume, eased);
            ApplyMusicVolume(currentMusicVolumeDb, sourceMusicVolume);

            if (t < 1f)
            {
                return;
            }

            currentMusicVolumeDb = targetMusicVolumeDb;
            sourceMusicVolume = targetSourceVolume;
            ApplyMusicVolume(currentMusicVolumeDb, sourceMusicVolume);

            if (PlaybackState == TtsPlaybackState.Restoring)
            {
                PlaybackState = TtsPlaybackState.Idle;
            }
        }

        public void BeginDucking()
        {
            duckRequested = true;
            restoreAllowedAt = float.PositiveInfinity;
            StartFade(duckedMusicVolumeDb, DbToLinear(duckedMusicVolumeDb) * testMusicVolume, duckFadeInSeconds, TtsPlaybackState.Ducking);
        }

        public void EndDucking()
        {
            duckRequested = false;
            restoreAllowedAt = Time.unscaledTime + Mathf.Max(0f, holdAfterTtsSeconds);
        }

        public void ForceRestore()
        {
            duckRequested = false;
            restoreAllowedAt = 0f;
            currentMusicVolumeDb = normalMusicVolumeDb;
            sourceMusicVolume = testMusicVolume;
            ApplyMusicVolume(currentMusicVolumeDb, sourceMusicVolume);
            PlaybackState = TtsPlaybackState.Idle;
        }

        public void ToggleTestMusic()
        {
            EnsureTestMusicSource();

            if (testMusicSource.isPlaying)
            {
                testMusicSource.Stop();
                return;
            }

            testMusicSource.Play();
        }

        public void SetTestMusicPlaying(bool shouldPlay)
        {
            EnsureTestMusicSource();

            if (shouldPlay)
            {
                if (!testMusicSource.isPlaying)
                {
                    testMusicSource.Play();
                }
            }
            else if (testMusicSource.isPlaying)
            {
                testMusicSource.Stop();
            }
        }

        private void StartFade(float targetDb, float targetLinearVolume, float duration, TtsPlaybackState state)
        {
            fadeStartedAt = Time.unscaledTime;
            fadeDuration = Mathf.Max(0.001f, duration);
            fadeStartVolumeDb = currentMusicVolumeDb;
            fadeStartSourceVolume = sourceMusicVolume;
            targetMusicVolumeDb = targetDb;
            targetSourceVolume = Mathf.Clamp01(targetLinearVolume);
            PlaybackState = state;
        }

        private void ApplyMusicVolume(float db, float linearVolume)
        {
            if (audioMixer != null && !string.IsNullOrEmpty(musicVolumeParameter))
            {
                audioMixer.SetFloat(musicVolumeParameter, db);
            }

            if (musicSources != null)
            {
                for (var i = 0; i < musicSources.Length; i++)
                {
                    if (musicSources[i] != null)
                    {
                        musicSources[i].volume = linearVolume;
                    }
                }
            }

            if (testMusicSource != null)
            {
                testMusicSource.volume = linearVolume;
            }
        }

        private void ApplyCoachVoiceVolume()
        {
            if (audioMixer != null && !string.IsNullOrEmpty(coachVoiceVolumeParameter))
            {
                audioMixer.SetFloat(coachVoiceVolumeParameter, coachVoiceVolumeDb);
            }
        }

        private void EnsureTestMusicSource()
        {
            if (testMusicSource != null)
            {
                return;
            }

            testMusicSource = gameObject.AddComponent<AudioSource>();
            testMusicSource.playOnAwake = false;
            testMusicSource.loop = true;
            testMusicSource.spatialBlend = 0f;
            testMusicSource.volume = sourceMusicVolume;
            testMusicSource.clip = CreateTestMusicClip();
        }

        private static AudioClip CreateTestMusicClip()
        {
            const int sampleRate = 44100;
            const int seconds = 4;
            var samples = sampleRate * seconds;
            var data = new float[samples];

            for (var i = 0; i < samples; i++)
            {
                var time = i / (float)sampleRate;
                var beat = Mathf.Sin(2f * Mathf.PI * 2f * time) > 0f ? 1f : 0.65f;
                var toneA = Mathf.Sin(2f * Mathf.PI * 220f * time);
                var toneB = Mathf.Sin(2f * Mathf.PI * 277.18f * time);
                var toneC = Mathf.Sin(2f * Mathf.PI * 329.63f * time);
                data[i] = (toneA * 0.18f + toneB * 0.11f + toneC * 0.08f) * beat;
            }

            var clip = AudioClip.Create("TTS Ducking Test Music", samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static float DbToLinear(float db)
        {
            return Mathf.Max(MinLinearVolume, Mathf.Pow(10f, db / 20f));
        }

        private void OnDestroy()
        {
            if (testMusicSource != null)
            {
                testMusicSource.Stop();
            }
        }
    }
}
