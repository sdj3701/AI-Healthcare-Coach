namespace AIHealthcareCoach.MediaPipe
{
    public sealed class PoseFrameRingBuffer
    {
        private PoseFrameSample[] samples = new PoseFrameSample[0];
        private int nextIndex;
        private int count;

        public int Count
        {
            get { return count; }
        }

        public int Capacity
        {
            get { return samples == null ? 0 : samples.Length; }
        }

        public void Configure(float bufferSeconds, float sampleFps)
        {
            var capacity = UnityEngine.Mathf.Max(
                1,
                UnityEngine.Mathf.CeilToInt(UnityEngine.Mathf.Max(0.1f, bufferSeconds) * UnityEngine.Mathf.Max(1f, sampleFps)));

            if (samples != null && samples.Length == capacity)
            {
                Clear();
                return;
            }

            samples = new PoseFrameSample[capacity];
            Clear();
        }

        public void Add(LandmarkFrame frame, PoseQualityReport qualityReport, float inferenceMs)
        {
            if (frame == null)
            {
                return;
            }

            if (samples == null || samples.Length == 0)
            {
                Configure(5f, 15f);
            }

            samples[nextIndex] = new PoseFrameSample
            {
                frame = frame,
                timestampMs = frame.timestampMs,
                averageVisibility = qualityReport == null ? 0f : qualityReport.averageVisibility,
                averagePresence = qualityReport == null ? 0f : qualityReport.averagePresence,
                cameraFps = frame.cameraFps,
                poseFps = frame.poseFps,
                inferenceMs = inferenceMs
            };

            nextIndex = (nextIndex + 1) % samples.Length;
            if (count < samples.Length)
            {
                count++;
            }
        }

        public void Clear()
        {
            if (samples != null)
            {
                for (var i = 0; i < samples.Length; i++)
                {
                    samples[i] = null;
                }
            }

            nextIndex = 0;
            count = 0;
        }
    }

    public sealed class PoseFrameSample
    {
        public LandmarkFrame frame;
        public long timestampMs;
        public float averageVisibility;
        public float averagePresence;
        public float cameraFps;
        public float poseFps;
        public float inferenceMs;
    }
}
