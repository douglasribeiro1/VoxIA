using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace VoxIA.Services
{
    public static class AudioEncoderService
    {
        private const uint MF_VERSION = 0x00020070;
        private const uint MFSTARTUP_NOSOCKET = 0x1;

        [DllImport("mfplat.dll", ExactSpelling = true)]
        private static extern int MFStartup(uint Version, uint dwFlags);

        [DllImport("mfplat.dll", ExactSpelling = true)]
        private static extern int MFShutdown();

        [DllImport("mfplat.dll", ExactSpelling = true)]
        private static extern int MFCreateMediaType(out IntPtr ppMFType);

        [DllImport("mfplat.dll", ExactSpelling = true)]
        private static extern int MFCreateMemoryBuffer(uint cbMaxLength, out IntPtr ppBuffer);

        [DllImport("mfplat.dll", ExactSpelling = true)]
        private static extern int MFCreateSample(out IntPtr ppSample);

        [DllImport("mfreadwrite.dll", ExactSpelling = true)]
        private static extern int MFCreateSinkWriterFromURL([MarshalAs(UnmanagedType.LPWStr)] string pwszOutputURL, IntPtr pByteStream, IntPtr pAttributes, out IntPtr ppSinkWriter);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetGUIDDelegate(IntPtr thisPtr, ref Guid guidKey, ref Guid guidValue);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetUINT32Delegate(IntPtr thisPtr, ref Guid guidKey, uint unValue);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int AddStreamDelegate(IntPtr thisPtr, IntPtr pTargetMediaType, out uint pdwStreamIndex);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetInputMediaTypeDelegate(IntPtr thisPtr, uint dwStreamIndex, IntPtr pInputMediaType, IntPtr pAttributes);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int BeginWritingDelegate(IntPtr thisPtr);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int WriteSampleDelegate(IntPtr thisPtr, uint dwStreamIndex, IntPtr pSample);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int FinalizeDelegate(IntPtr thisPtr);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int LockDelegate(IntPtr thisPtr, out IntPtr ppbBuffer, out uint pcbMaxLength, out uint pcbCurrentLength);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int UnlockDelegate(IntPtr thisPtr);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetCurrentLengthDelegate(IntPtr thisPtr, uint cbCurrentLength);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetSampleTimeDelegate(IntPtr thisPtr, long hnsSampleTime);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetSampleDurationDelegate(IntPtr thisPtr, long hnsSampleDuration);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int AddBufferDelegate(IntPtr thisPtr, IntPtr pBuffer);

        private static Guid MF_MT_MAJOR_TYPE = new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
        private static Guid MF_MT_SUBTYPE = new Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
        private static Guid MF_MT_AUDIO_AVG_BYTES_PER_SECOND = new Guid("1aab75c8-cfef-451c-ab95-ac034b8e1731");
        private static Guid MF_MT_AUDIO_NUM_CHANNELS = new Guid("37e48bf5-645e-4c5b-89de-ada9e29b696a");
        private static Guid MF_MT_AUDIO_SAMPLES_PER_SECOND = new Guid("5faeeae7-0290-4c31-9e8a-c534f68d9dba");
        private static Guid MF_MT_AUDIO_BLOCK_ALIGNMENT = new Guid("322de230-9eeb-43bd-ab7a-ff412251541d");
        private static Guid MF_MT_AUDIO_BITS_PER_SAMPLE = new Guid("f2deb57f-40fa-4764-aa33-ed4f2d1ff669");

        private static Guid MFMediaType_Audio = new Guid("73647561-0000-0010-8000-00AA00389B71");
        private static Guid MFAudioFormat_PCM = new Guid("00000001-0000-0010-8000-00AA00389B71");
        private static Guid MFAudioFormat_AAC = new Guid("00001610-0000-0010-8000-00AA00389B71");

        private static T GetVTableDelegate<T>(IntPtr comPtr, int slotIndex) where T : class
        {
            IntPtr vtable = Marshal.ReadIntPtr(comPtr);
            IntPtr funcPtr = Marshal.ReadIntPtr(vtable, slotIndex * IntPtr.Size);
            return (T)(object)Marshal.GetDelegateForFunctionPointer(funcPtr, typeof(T));
        }

        public static string GetMimeType(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return "audio/m4a";
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            switch (ext)
            {
                case ".m4a": return "audio/m4a";
                case ".mp4": return "audio/mp4";
                case ".flac": return "audio/flac";
                case ".mp3": return "audio/mp3";
                case ".ogg": return "audio/ogg";
                case ".webm": return "audio/webm";
                default: return "audio/wav";
            }
        }

        public static void SaveAudioFile(byte[] pcmData, string filePath, uint sampleRate = 44100)
        {
            if (pcmData == null) pcmData = new byte[0];

            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext == ".m4a" || ext == ".mp4")
            {
                try
                {
                    bool ok = EncodePcmToM4a(pcmData, filePath, sampleRate, 1);
                    if (ok && File.Exists(filePath))
                    {
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError("Error encoding M4A via Media Foundation, falling back to FLAC/WAV", ex);
                }

                // Fallback to FLAC if M4A direct creation failed
                string flacFallback = Path.ChangeExtension(filePath, ".flac");
                try
                {
                    byte[] flacBytes = EncodePcmToFlac(pcmData, (int)sampleRate, 1, 16);
                    File.WriteAllBytes(flacFallback, flacBytes);
                    return;
                }
                catch { }
            }
            else if (ext == ".flac")
            {
                try
                {
                    byte[] flacBytes = EncodePcmToFlac(pcmData, (int)sampleRate, 1, 16);
                    File.WriteAllBytes(filePath, flacBytes);
                    return;
                }
                catch (Exception ex)
                {
                    Logger.LogError("Error encoding FLAC, falling back to WAV", ex);
                }
            }

            SavePcmWavFile(pcmData, filePath, sampleRate);
        }

        public static bool EncodePcmToM4a(byte[] pcmData, string filePath, uint sampleRate = 44100, uint channels = 1)
        {
            int hr = MFStartup(MF_VERSION, MFSTARTUP_NOSOCKET);
            if (hr != 0)
            {
                Logger.LogError(string.Format("MFStartup failed with error 0x{0:X}", hr));
                return false;
            }

            try
            {
                IntPtr pSinkWriter;
                hr = MFCreateSinkWriterFromURL(filePath, IntPtr.Zero, IntPtr.Zero, out pSinkWriter);
                if (hr != 0 || pSinkWriter == IntPtr.Zero)
                {
                    Logger.LogError(string.Format("MFCreateSinkWriterFromURL failed with error 0x{0:X}", hr));
                    return false;
                }

                var addStream = GetVTableDelegate<AddStreamDelegate>(pSinkWriter, 3);
                var setInputMediaType = GetVTableDelegate<SetInputMediaTypeDelegate>(pSinkWriter, 4);
                var beginWriting = GetVTableDelegate<BeginWritingDelegate>(pSinkWriter, 5);
                var writeSample = GetVTableDelegate<WriteSampleDelegate>(pSinkWriter, 6);
                var doFinalize = GetVTableDelegate<FinalizeDelegate>(pSinkWriter, 11);

                uint aacBytesPerSec = 12000; // 96 kbps high quality AAC
                uint outSampleRate = sampleRate;

                // 1. Output AAC Media Type
                IntPtr pOutType;
                MFCreateMediaType(out pOutType);
                var setGuidOut = GetVTableDelegate<SetGUIDDelegate>(pOutType, 24);
                var setUint32Out = GetVTableDelegate<SetUINT32Delegate>(pOutType, 21);

                setGuidOut(pOutType, ref MF_MT_MAJOR_TYPE, ref MFMediaType_Audio);
                setGuidOut(pOutType, ref MF_MT_SUBTYPE, ref MFAudioFormat_AAC);
                setUint32Out(pOutType, ref MF_MT_AUDIO_NUM_CHANNELS, channels);
                setUint32Out(pOutType, ref MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
                setUint32Out(pOutType, ref MF_MT_AUDIO_SAMPLES_PER_SECOND, outSampleRate);
                setUint32Out(pOutType, ref MF_MT_AUDIO_AVG_BYTES_PER_SECOND, aacBytesPerSec);

                uint streamIndex;
                hr = addStream(pSinkWriter, pOutType, out streamIndex);
                if (hr != 0)
                {
                    Marshal.Release(pOutType);
                    Marshal.Release(pSinkWriter);
                    Logger.LogError(string.Format("SinkWriter AddStream failed with error 0x{0:X}", hr));
                    return false;
                }

                // 2. Input PCM Media Type (Captured from waveIn microphone)
                IntPtr pInType;
                MFCreateMediaType(out pInType);
                var setGuidIn = GetVTableDelegate<SetGUIDDelegate>(pInType, 24);
                var setUint32In = GetVTableDelegate<SetUINT32Delegate>(pInType, 21);

                uint blockAlign = channels * 2;
                uint avgBytesPCM = outSampleRate * blockAlign;

                setGuidIn(pInType, ref MF_MT_MAJOR_TYPE, ref MFMediaType_Audio);
                setGuidIn(pInType, ref MF_MT_SUBTYPE, ref MFAudioFormat_PCM);
                setUint32In(pInType, ref MF_MT_AUDIO_NUM_CHANNELS, channels);
                setUint32In(pInType, ref MF_MT_AUDIO_SAMPLES_PER_SECOND, outSampleRate);
                setUint32In(pInType, ref MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
                setUint32In(pInType, ref MF_MT_AUDIO_BLOCK_ALIGNMENT, blockAlign);
                setUint32In(pInType, ref MF_MT_AUDIO_AVG_BYTES_PER_SECOND, avgBytesPCM);

                hr = setInputMediaType(pSinkWriter, streamIndex, pInType, IntPtr.Zero);
                if (hr != 0)
                {
                    Marshal.Release(pOutType);
                    Marshal.Release(pInType);
                    Marshal.Release(pSinkWriter);
                    Logger.LogError(string.Format("SinkWriter SetInputMediaType failed with error 0x{0:X}", hr));
                    return false;
                }

                hr = beginWriting(pSinkWriter);
                if (hr != 0)
                {
                    Marshal.Release(pOutType);
                    Marshal.Release(pInType);
                    Marshal.Release(pSinkWriter);
                    Logger.LogError(string.Format("SinkWriter BeginWriting failed with error 0x{0:X}", hr));
                    return false;
                }

                // 3. Write PCM audio data into M4A AAC Writer
                if (pcmData != null && pcmData.Length > 0)
                {
                    uint bufSize = (uint)pcmData.Length;
                    IntPtr pBuf;
                    MFCreateMemoryBuffer(bufSize, out pBuf);
                    var lockBuf = GetVTableDelegate<LockDelegate>(pBuf, 3);
                    var unlockBuf = GetVTableDelegate<UnlockDelegate>(pBuf, 4);
                    var setCurLen = GetVTableDelegate<SetCurrentLengthDelegate>(pBuf, 6);

                    IntPtr pData;
                    uint maxLen, curLen;
                    lockBuf(pBuf, out pData, out maxLen, out curLen);
                    Marshal.Copy(pcmData, 0, pData, pcmData.Length);
                    unlockBuf(pBuf);
                    setCurLen(pBuf, bufSize);

                    IntPtr pSample;
                    MFCreateSample(out pSample);
                    var addBuffer = GetVTableDelegate<AddBufferDelegate>(pSample, 42);
                    var setSampleTime = GetVTableDelegate<SetSampleTimeDelegate>(pSample, 36);
                    var setSampleDuration = GetVTableDelegate<SetSampleDurationDelegate>(pSample, 38);

                    addBuffer(pSample, pBuf);
                    setSampleTime(pSample, 0);

                    long durationHns = (long)((double)pcmData.Length / avgBytesPCM * 10000000.0);
                    setSampleDuration(pSample, durationHns);

                    writeSample(pSinkWriter, streamIndex, pSample);

                    Marshal.Release(pBuf);
                    Marshal.Release(pSample);
                }

                doFinalize(pSinkWriter);

                Marshal.Release(pOutType);
                Marshal.Release(pInType);
                Marshal.Release(pSinkWriter);

                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError("Exception in EncodePcmToM4a", ex);
                return false;
            }
            finally
            {
                MFShutdown();
            }
        }

        public static byte[] EncodePcmToFlac(byte[] pcmData, int sampleRate = 44100, int channels = 1, int bitsPerSample = 16)
        {
            if (pcmData == null || pcmData.Length == 0)
                return new byte[0];

            int totalSamples = pcmData.Length / (channels * (bitsPerSample / 8));
            int blockSize = 4096;

            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write(Encoding.ASCII.GetBytes("fLaC"));

                bw.Write((byte)0x80);
                bw.Write((byte)0x00);
                bw.Write((byte)0x00);
                bw.Write((byte)0x22);

                WriteBigEndian16(bw, (ushort)blockSize);
                WriteBigEndian16(bw, (ushort)blockSize);
                bw.Write(new byte[6]);

                ulong streamInfoFields = 0;
                streamInfoFields |= ((ulong)sampleRate & 0xFFFFF) << 44;
                streamInfoFields |= ((ulong)(channels - 1) & 0x07) << 41;
                streamInfoFields |= ((ulong)(bitsPerSample - 1) & 0x1F) << 36;
                streamInfoFields |= ((ulong)totalSamples & 0xFFFFFFFFF);
                WriteBigEndian64(bw, streamInfoFields);

                bw.Write(new byte[16]);

                short[] samples = new short[totalSamples];
                for (int i = 0; i < totalSamples; i++)
                {
                    int offset = i * 2;
                    samples[i] = (short)(pcmData[offset] | (pcmData[offset + 1] << 8));
                }

                int sampleIndex = 0;
                int frameNumber = 0;

                while (sampleIndex < totalSamples)
                {
                    int currentBlockSize = Math.Min(blockSize, totalSamples - sampleIndex);
                    WriteFrame(bw, samples, sampleIndex, currentBlockSize, sampleRate, frameNumber++);
                    sampleIndex += currentBlockSize;
                }

                return ms.ToArray();
            }
        }

        private static void WriteFrame(BinaryWriter bw, short[] samples, int offset, int blockSize, int sampleRate, int frameNumber)
        {
            using (var frameMs = new MemoryStream())
            using (var frameBw = new BinaryWriter(frameMs))
            {
                WriteBigEndian16(frameBw, 0xFFF8);

                byte bsCode = (blockSize == 4096) ? (byte)0x0C : (byte)0x06;
                byte srCode = (sampleRate == 44100) ? (byte)0x09 : (sampleRate == 16000 ? (byte)0x08 : (byte)0x04);
                frameBw.Write((byte)((bsCode << 4) | (srCode & 0x0F)));
                frameBw.Write((byte)0x08);
                WriteUtf8UInt32(frameBw, (uint)frameNumber);

                if (bsCode == 0x06)
                {
                    frameBw.Write((byte)(blockSize - 1));
                }

                byte[] headerBytes = frameMs.ToArray();
                byte headerCrc = ComputeCrc8(headerBytes);
                frameBw.Write(headerCrc);

                frameBw.Write((byte)0x02);

                for (int i = 0; i < blockSize; i++)
                {
                    short sample = samples[offset + i];
                    WriteBigEndian16(frameBw, (ushort)sample);
                }

                byte[] frameData = frameMs.ToArray();
                ushort frameCrc16 = ComputeCrc16(frameData);
                WriteBigEndian16(frameBw, frameCrc16);

                bw.Write(frameMs.ToArray());
            }
        }

        public static void SavePcmWavFile(byte[] pcmData, string filePath, uint sampleRate)
        {
            if (pcmData == null) pcmData = new byte[0];
            uint subChunk2Size = (uint)pcmData.Length;
            uint chunkSize = (uint)(36 + subChunk2Size);
            ushort numChannels = 1;
            ushort bitsPerSample = 16;
            uint byteRate = (uint)(sampleRate * numChannels * (bitsPerSample / 8));
            ushort blockAlign = (ushort)(numChannels * (bitsPerSample / 8));

            using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            using (var bw = new BinaryWriter(fs))
            {
                bw.Write(Encoding.ASCII.GetBytes("RIFF"));
                bw.Write(chunkSize);
                bw.Write(Encoding.ASCII.GetBytes("WAVE"));

                bw.Write(Encoding.ASCII.GetBytes("fmt "));
                bw.Write((uint)16);
                bw.Write((ushort)1);
                bw.Write(numChannels);
                bw.Write(sampleRate);
                bw.Write(byteRate);
                bw.Write(blockAlign);
                bw.Write(bitsPerSample);

                bw.Write(Encoding.ASCII.GetBytes("data"));
                bw.Write(subChunk2Size);
                bw.Write(pcmData);

                bw.Flush();
            }
        }

        private static void WriteBigEndian16(BinaryWriter bw, ushort val)
        {
            bw.Write((byte)((val >> 8) & 0xFF));
            bw.Write((byte)(val & 0xFF));
        }

        private static void WriteBigEndian64(BinaryWriter bw, ulong val)
        {
            for (int i = 7; i >= 0; i--)
            {
                bw.Write((byte)((val >> (i * 8)) & 0xFF));
            }
        }

        private static void WriteUtf8UInt32(BinaryWriter bw, uint val)
        {
            if (val < 0x80)
            {
                bw.Write((byte)val);
            }
            else if (val < 0x800)
            {
                bw.Write((byte)(0xC0 | (val >> 6)));
                bw.Write((byte)(0x80 | (val & 0x3F)));
            }
            else
            {
                bw.Write((byte)(0xE0 | (val >> 12)));
                bw.Write((byte)(0x80 | ((val >> 6) & 0x3F)));
                bw.Write((byte)(0x80 | (val & 0x3F)));
            }
        }

        private static byte ComputeCrc8(byte[] data)
        {
            byte crc = 0;
            for (int i = 0; i < data.Length; i++)
            {
                crc ^= data[i];
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 0x80) != 0)
                        crc = (byte)((crc << 1) ^ 0x07);
                    else
                        crc <<= 1;
                }
            }
            return crc;
        }

        private static ushort ComputeCrc16(byte[] data)
        {
            ushort crc = 0;
            for (int i = 0; i < data.Length; i++)
            {
                crc ^= (ushort)(data[i] << 8);
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 0x8000) != 0)
                        crc = (ushort)((crc << 1) ^ 0x8005);
                    else
                        crc <<= 1;
                }
            }
            return crc;
        }
    }
}
