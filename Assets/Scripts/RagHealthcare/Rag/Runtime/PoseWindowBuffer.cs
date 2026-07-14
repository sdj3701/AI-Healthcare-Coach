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
            for (var i = 0; i < frames.Length; i++)
            {
                frames[i] = new PoseFeatureFrame();
            }
        }

        public int Count => count;
        public int Capacity => frames.Length;

        public void Add(PoseFeatureFrame frame)
        {
            if (frame == null)
            {
                return;
            }

            frames[nextIndex].CopyFrom(frame);
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
                frames[i].Reset();
            }

            nextIndex = 0;
            count = 0;
        }

        public PoseFeatureFrame GetChronological(int index)
        {
            if (index < 0 || index >= count)
            {
                return null;
            }

            var frameIndex = nextIndex - count + index;
            if (frameIndex < 0)
            {
                frameIndex += frames.Length;
            }

            return frames[frameIndex];
        }

        public IEnumerable<PoseFeatureFrame> RecentFrames()
        {
            for (var i = 0; i < count; i++)
            {
                var frame = GetChronological(i);
                if (frame != null)
                {
                    yield return frame;
                }
            }
        }
    }
}
