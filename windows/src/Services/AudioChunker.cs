using System;
using System.Collections.Generic;
using System.IO;

namespace VoxIA.Services
{
    /// <summary>
    /// Long dictations are split at natural pauses into blocks shorter than Whisper's 30 s window.
    /// Whisper sometimes skips a whole sentence when it decodes a long file window by window;
    /// a block that fits in a single window does not suffer from that. The full recording is
    /// still saved and is used whenever the blocks are missing.
    /// </summary>
    public static class AudioChunker
    {
        private const double SplitAboveSeconds = 30.0;   // shorter recordings are sent whole
        private const double MinChunkSeconds = 15.0;
        private const double MaxChunkSeconds = 27.0;
        private const double MinTailSeconds = 4.0;       // never leave a tiny last block
        private const int FrameMs = 20;
        private const int QuietWindowFrames = 10;        // cut in the middle of the quietest 200 ms

        /// <summary>
        /// Na transcrição durante a fala, um bloco só é cortado quando já há áudio suficiente para
        /// escolher a pausa com a mesma janela usada no ditado inteiro (bloco máximo + cauda mínima).
        /// </summary>
        public const double LiveWindowSeconds = MaxChunkSeconds + MinTailSeconds;

        /// <summary>Sample offsets where the PCM (16-bit mono) should be cut. Empty = do not split.</summary>
        public static List<int> FindCuts(byte[] pcm, int sampleRate)
        {
            var cuts = new List<int>();
            if (pcm == null || sampleRate < 8000) return cuts;
            int n = pcm.Length / 2;
            if (n < sampleRate * SplitAboveSeconds) return cuts;

            int frameLen = sampleRate * FrameMs / 1000;
            int frames = n / frameLen;
            double[] rms = new double[frames];
            for (int f = 0; f < frames; f++)
            {
                double sum = 0;
                int s0 = f * frameLen;
                for (int i = s0; i < s0 + frameLen; i++)
                {
                    double s = (short)(pcm[2 * i] | (pcm[2 * i + 1] << 8));
                    sum += s * s;
                }
                rms[f] = Math.Sqrt(sum / frameLen);
            }

            int framesPerSec = 1000 / FrameMs;
            int minF = (int)(MinChunkSeconds * framesPerSec);
            int maxF = (int)(MaxChunkSeconds * framesPerSec);
            int tailF = (int)(MinTailSeconds * framesPerSec);
            int start = 0;

            while (frames - start > maxF)
            {
                int lo = start + minF;
                int hi = Math.Min(start + maxF, frames - tailF) - QuietWindowFrames;
                if (hi <= lo) break;

                // quietest 200 ms window; on ties prefer the later one (longer blocks, fewer cuts)
                double windowSum = 0;
                for (int k = lo; k < lo + QuietWindowFrames; k++) windowSum += rms[k];
                double best = windowSum;
                int bestAt = lo;
                for (int w = lo + 1; w <= hi; w++)
                {
                    windowSum += rms[w + QuietWindowFrames - 1] - rms[w - 1];
                    if (windowSum <= best) { best = windowSum; bestAt = w; }
                }

                int cutFrame = bestAt + QuietWindowFrames / 2;
                cuts.Add(cutFrame * frameLen);
                start = cutFrame;
            }
            return cuts;
        }

        /// <summary>Writes rec_x.part01.flac, rec_x.part02.flac ... next to the recording. Returns how many were written.</summary>
        public static int SaveParts(byte[] pcm, int sampleRate, string recordingPath)
        {
            DeleteParts(recordingPath);
            List<int> cuts = FindCuts(pcm, sampleRate);
            if (cuts.Count == 0) return 0;

            int n = pcm.Length / 2;
            var bounds = new List<int> { 0 };
            bounds.AddRange(cuts);
            bounds.Add(n);

            var lengths = new List<string>();
            for (int i = 0; i < bounds.Count - 1; i++)
            {
                int s0 = bounds[i], s1 = bounds[i + 1];
                byte[] part = new byte[(s1 - s0) * 2];
                Buffer.BlockCopy(pcm, s0 * 2, part, 0, part.Length);
                byte[] flac = AudioEncoderService.EncodePcmToFlac(part, sampleRate, 1, 16);
                File.WriteAllBytes(PartPath(recordingPath, i + 1), flac);
                lengths.Add(((s1 - s0) / (double)sampleRate).ToString("F1"));
            }
            Logger.LogInfo(string.Format("Recording split at pauses into {0} blocks for transcription ({1} s).", bounds.Count - 1, string.Join(" + ", lengths)));
            return bounds.Count - 1;
        }

        /// <summary>The block files of a recording, in order. Empty when it was not split.</summary>
        public static List<string> GetParts(string recordingPath)
        {
            var parts = new List<string>();
            if (string.IsNullOrEmpty(recordingPath)) return parts;
            for (int i = 1; i < 1000; i++)
            {
                string p = PartPath(recordingPath, i);
                if (!File.Exists(p)) break;
                parts.Add(p);
            }
            return parts;
        }

        public static void DeleteParts(string recordingPath)
        {
            foreach (string p in GetParts(recordingPath))
            {
                try { File.Delete(p); } catch { }
            }
        }

        private static string PartPath(string recordingPath, int index)
        {
            string dir = Path.GetDirectoryName(recordingPath) ?? "";
            string name = Path.GetFileNameWithoutExtension(recordingPath);
            return Path.Combine(dir, string.Format("{0}.part{1:D2}.flac", name, index));
        }
    }
}
