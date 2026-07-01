using System.Collections.Generic;

namespace Rag.Healthcare.Rag.Runtime
{
    public sealed class PoseWindowBuffer
    {
        private readonly PoseFeatureFrame[] frames;
        private int nextIndex;
        private int count;

        public PoseWindowBuffer(int capacity)
        {
            frames = new PoseFeatureFrame[capacity < 1 ? 1 : capacity];
        }

        public int Count => count;
        public int Capacity => frames.Length;

        public void Add(PoseFeatureFrame frame)
        {
            if (frame == null)
            {
                return;
            }

            frames[nextIndex] = frame;
            nextIndex = (nextIndex + 1) % frames.Length;
            if (count < frames.Length)
            {
                count++;
            }
        }

        public void Clear()
        {
            for (var i = 0; i < frames.Length; i++)
            {
                frames[i] = null;
            }

            nextIndex = 0;
            count = 0;
        }

        public IEnumerable<PoseFeatureFrame> RecentFrames()
        {
            for (var i = 0; i < count; i++)
            {
                var index = nextIndex - count + i;
                if (index < 0)
                {
                    index += frames.Length;
                }

                var frame = frames[index];
                if (frame != null)
                {
                    yield return frame;
                }
            }
        }
    }
}
