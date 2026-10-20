using System;
using System.Collections.Generic;

namespace VoxIA.Services
{
    /// <summary>
    /// Prepares a recording (16-bit mono PCM) before it is encoded and sent to Whisper:
    /// removes low-frequency rumble, trims silence at both ends, shortens very long pauses
    /// and lifts quiet recordings. Speech is never attenuated or cut: whenever something looks
    /// unsafe the original audio is returned untouched.
    /// </summary>
    public static class AudioPreprocessor
    {
        public class Result
        {
            public byte[] Pcm;
            public bool Changed;
            public double OriginalSeconds;
            public double NewSeconds;
            public double Gain;
            public string Note;
            public bool Silent;   // true quando não há fala nenhuma (não deve ser enviado ao Whisper)
        }

        private const int FrameMs = 20;
        private const int HangFrames = 12;          // 240 ms of padding around any detected speech
        private const int EdgePadFrames = 8;        // extra 160 ms kept before the first / after the last speech
        private const int LongPauseFrames = 125;    // pauses over 2.5 s are shortened
        private const int KeptPauseFrames = 30;     // ... keeping 0.6 s on each side of the cut
        private const double HighPassHz = 80.0;
        private const double TargetRms = 3000.0;
        private const double QuietRms = 2200.0;
        private const double MaxGain = 4.0;
        private const double MaxPeak = 30000.0;
        private const double MinSpeechRms = 150.0;  // abaixo disso é ruído ambiente, não fala
        private const int MinSpeechFrames = 12;     // 240 ms de energia de fala no mínimo

        public static Result Process(byte[] pcm, int sampleRate)
        {
            var res = new Result { Pcm = pcm, Changed = false, Gain = 1.0, Note = "" };
            if (pcm == null || pcm.Length < 4 || sampleRate < 8000)
            {
                res.Note = "too short";
                return res;
            }

            int n = pcm.Length / 2;
            res.OriginalSeconds = n / (double)sampleRate;
            if (res.OriginalSeconds < 0.6)
            {
                res.Note = "shorter than 0.6s";
                return res;
            }

            // 1. Samples + first-order high-pass filter (removes rumble / DC offset)
            float[] x = new float[n];
            double rc = 1.0 / (2.0 * Math.PI * HighPassHz);
            double dt = 1.0 / sampleRate;
            double a = rc / (rc + dt);
            double prevIn = 0, prevOut = 0;
            for (int i = 0; i < n; i++)
            {
                double s = (short)(pcm[2 * i] | (pcm[2 * i + 1] << 8));
                double y = a * (prevOut + s - prevIn);
                prevIn = s;
                prevOut = y;
                x[i] = (float)y;
            }

            // 2. Frame energies
            int frameLen = sampleRate * FrameMs / 1000;
            int frames = (n + frameLen - 1) / frameLen;
            double[] rms = new double[frames];
            for (int f = 0; f < frames; f++)
            {
                int s0 = f * frameLen;
                int s1 = Math.Min(n, s0 + frameLen);
                double sum = 0;
                for (int i = s0; i < s1; i++) sum += (double)x[i] * x[i];
                rms[f] = Math.Sqrt(sum / Math.Max(1, s1 - s0));
            }

            double[] sorted = (double[])rms.Clone();
            Array.Sort(sorted);
            double noise = sorted[(int)(frames * 0.10)];
            double loud = sorted[Math.Min(frames - 1, (int)(frames * 0.90))];
            if (loud < 60.0)
            {
                res.Note = "recording is essentially silent; left untouched";
                res.Silent = true;
                return res;
            }

            // Sem fala de verdade (só ruído do ambiente), o Whisper inventa palavras: não enviar
            double speechLevel = Math.Max(MinSpeechRms, noise * 3.0);
            int speechFrames = 0;
            for (int f = 0; f < frames; f++)
            {
                if (rms[f] > speechLevel) speechFrames++;
            }
            if (speechFrames < MinSpeechFrames)
            {
                res.Note = "no speech detected; left untouched";
                res.Silent = true;
                return res;
            }

            double thr = Math.Max(noise * 3.0, 100.0);
            thr = Math.Min(thr, loud * 0.25);
            thr = Math.Max(thr, 60.0);

            // 3. Speech frames, widened so soft word onsets / endings are never clipped
            bool[] active = new bool[frames];
            for (int f = 0; f < frames; f++)
            {
                if (rms[f] > thr)
                {
                    int lo = Math.Max(0, f - HangFrames);
                    int hi = Math.Min(frames - 1, f + HangFrames);
                    for (int k = lo; k <= hi; k++) active[k] = true;
                }
            }

            int first = -1, last = -1;
            for (int f = 0; f < frames; f++)
            {
                if (active[f]) { if (first < 0) first = f; last = f; }
            }
            if (first < 0)
            {
                res.Note = "no speech detected; left untouched";
                return res;
            }

            first = Math.Max(0, first - EdgePadFrames);
            last = Math.Min(frames - 1, last + EdgePadFrames);

            // 4. Build the list of kept frame ranges (long inner pauses are shortened)
            var ranges = new List<int[]>();
            int segStart = first;
            int f2 = first;
            while (f2 <= last)
            {
                if (active[f2]) { f2++; continue; }
                int runStart = f2;
                while (f2 <= last && !active[f2]) f2++;
                int runLen = f2 - runStart;
                if (runLen > LongPauseFrames)
                {
                    ranges.Add(new int[] { segStart, runStart + KeptPauseFrames - 1 });
                    segStart = f2 - KeptPauseFrames;
                }
            }
            ranges.Add(new int[] { segStart, last });

            // 5. Gain for quiet recordings (never attenuates)
            double sumSq = 0; long cnt = 0; double peak = 0;
            foreach (var r in ranges)
            {
                for (int f = r[0]; f <= r[1]; f++)
                {
                    if (!active[f]) continue;
                    int s0 = f * frameLen;
                    int s1 = Math.Min(n, s0 + frameLen);
                    for (int i = s0; i < s1; i++)
                    {
                        double v = x[i];
                        sumSq += v * v; cnt++;
                        double av = Math.Abs(v);
                        if (av > peak) peak = av;
                    }
                }
            }
            double speechRms = cnt > 0 ? Math.Sqrt(sumSq / cnt) : 0;
            double gain = 1.0;
            if (speechRms > 0 && speechRms < QuietRms)
            {
                gain = Math.Min(MaxGain, TargetRms / speechRms);
                if (peak * gain > MaxPeak) gain = MaxPeak / peak;
                if (gain < 1.05) gain = 1.0;
            }

            // 6. Output
            int outSamples = 0;
            foreach (var r in ranges)
            {
                int s0 = r[0] * frameLen;
                int s1 = Math.Min(n, (r[1] + 1) * frameLen);
                outSamples += Math.Max(0, s1 - s0);
            }
            if (outSamples < sampleRate * 0.3)
            {
                res.Note = "result too short; left untouched";
                return res;
            }

            byte[] output = new byte[outSamples * 2];
            int pos = 0;
            foreach (var r in ranges)
            {
                int s0 = r[0] * frameLen;
                int s1 = Math.Min(n, (r[1] + 1) * frameLen);
                for (int i = s0; i < s1; i++)
                {
                    double v = x[i] * gain;
                    if (v > 32767.0) v = 32767.0;
                    if (v < -32768.0) v = -32768.0;
                    short sv = (short)Math.Round(v);
                    output[pos++] = (byte)(sv & 0xFF);
                    output[pos++] = (byte)((sv >> 8) & 0xFF);
                }
            }

            res.Pcm = output;
            res.Changed = true;
            res.Gain = gain;
            res.NewSeconds = outSamples / (double)sampleRate;
            res.Note = string.Format("threshold {0:F0}, noise {1:F0}, speech rms {2:F0}", thr, noise, speechRms);
            return res;
        }
    }
}
