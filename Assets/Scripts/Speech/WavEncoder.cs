using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace AIHealthcareCoach.Speech
{
    public static class WavEncoder
    {
        private const int BitsPerSample = 16;

        public static byte[] Encode(AudioClip clip)
        {
            if (clip == null)
            {
                throw new ArgumentNullException(nameof(clip));
            }

            var samples = new float[clip.samples * clip.channels];
            clip.GetData(samples, 0);

            return Encode(samples, clip.channels, clip.frequency);
        }

        public static byte[] Encode(float[] samples, int channels, int sampleRate)
        {
            if (samples == null)
            {
                throw new ArgumentNullException(nameof(samples));
            }

            if (channels <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(channels));
            }

            if (sampleRate <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleRate));
            }

            var dataLength = samples.Length * sizeof(short);
            var byteRate = sampleRate * channels * BitsPerSample / 8;
            var blockAlign = channels * BitsPerSample / 8;

            using var stream = new MemoryStream(44 + dataLength);
            using var writer = new BinaryWriter(stream);

            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataLength);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)channels);
            writer.Write(sampleRate);
            writer.Write(byteRate);
            writer.Write((short)blockAlign);
            writer.Write((short)BitsPerSample);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(dataLength);

            foreach (var sample in samples)
            {
                var clamped = Mathf.Clamp(sample, -1f, 1f);
                writer.Write((short)Mathf.RoundToInt(clamped * short.MaxValue));
            }

            return stream.ToArray();
        }
    }
}
