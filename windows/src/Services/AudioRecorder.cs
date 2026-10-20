using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace VoxIA.Services
{
    public class AudioRecorder
    {
        private const int WAVE_MAPPER = -1;
        private const int CALLBACK_EVENT = 0x00050000;
        private const ushort WAVE_FORMAT_PCM = 1;
        private const uint WHDR_DONE = 0x00000001;
        private const uint WHDR_PREPARED = 0x00000002;
        private const uint WAIT_OBJECT_0 = 0x00000000;

        [StructLayout(LayoutKind.Sequential)]
        public struct WAVEFORMATEX
        {
            public ushort wFormatTag;
            public ushort nChannels;
            public uint nSamplesPerSec;
            public uint nAvgBytesPerSec;
            public ushort nBlockAlign;
            public ushort wBitsPerSample;
            public ushort cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct WAVEHDR
        {
            public IntPtr lpData;
            public uint dwBufferLength;
            public uint dwBytesRecorded;
            public IntPtr dwUser;
            public uint dwFlags;
            public uint dwLoops;
            public IntPtr lpNext;
            public IntPtr reserved;
        }

        [DllImport("winmm.dll", SetLastError = true)]
        private static extern int waveInOpen(out IntPtr phwi, int uDeviceID, ref WAVEFORMATEX pwfx, IntPtr dwCallback, IntPtr dwInstance, int fdwOpen);

        [DllImport("winmm.dll", SetLastError = true)]
        private static extern int waveInPrepareHeader(IntPtr hwi, IntPtr pwh, int cbwh);

        [DllImport("winmm.dll", SetLastError = true)]
        private static extern int waveInUnprepareHeader(IntPtr hwi, IntPtr pwh, int cbwh);

        [DllImport("winmm.dll", SetLastError = true)]
        private static extern int waveInAddBuffer(IntPtr hwi, IntPtr pwh, int cbwh);

        [DllImport("winmm.dll", SetLastError = true)]
        private static extern int waveInStart(IntPtr hwi);

        [DllImport("winmm.dll", SetLastError = true)]
        private static extern int waveInStop(IntPtr hwi);

        [DllImport("winmm.dll", SetLastError = true)]
        private static extern int waveInReset(IntPtr hwi);

        [DllImport("winmm.dll", SetLastError = true)]
        private static extern int waveInClose(IntPtr hwi);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateEvent(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private IntPtr _hWaveIn = IntPtr.Zero;
        private IntPtr _hEvent = IntPtr.Zero;
        private Thread _recordThread;
        private volatile bool _isRecording = false;
        private string _currentFilePath = null;

        /// <summary>Verdadeiro quando a última gravação não tinha fala (só silêncio ou ruído).</summary>
        public bool LastRecordingWasSilent { get; private set; }
        private DateTime _recordingStartTime;
        private MemoryStream _recordedAudioStream;
        private List<IntPtr> _headerPointers = new List<IntPtr>();
        private List<IntPtr> _bufferPointers = new List<IntPtr>();
        private WAVEFORMATEX _format;
        private volatile float _currentAudioLevel = 0f;

        public bool IsRecording
        {
            get { return _isRecording; }
        }

        public float CurrentAudioLevel
        {
            get { return _isRecording ? _currentAudioLevel : 0f; }
        }

        public string CurrentFilePath
        {
            get { return _currentFilePath; }
        }

        public TimeSpan RecordingDuration
        {
            get { return _isRecording ? DateTime.Now - _recordingStartTime : TimeSpan.Zero; }
        }

        public string StartRecording()
        {
            if (_isRecording)
            {
                StopRecording();
            }

            // Ensure Windows Volume Mixer hasn't muted or locked VoxIA to volume 1
            AudioVolumeService.EnsureFullVolume();

            try
            {
                string audioId = Guid.NewGuid().ToString("N");
                _currentFilePath = Path.Combine(AppPathService.AudioQueueDirectory, string.Format("rec_{0}.m4a", audioId));
                _recordedAudioStream = new MemoryStream();

                // Standard 44.1kHz 16-bit Mono PCM format (Studio quality for native M4A/AAC & Whisper)
                uint sampleRate = 44100;
                ushort channels = 1;
                ushort bitsPerSample = 16;
                ushort blockAlign = (ushort)(channels * (bitsPerSample / 8));
                uint avgBytesPerSec = sampleRate * blockAlign;

                _format = new WAVEFORMATEX
                {
                    wFormatTag = WAVE_FORMAT_PCM,
                    nChannels = channels,
                    nSamplesPerSec = sampleRate,
                    wBitsPerSample = bitsPerSample,
                    nBlockAlign = blockAlign,
                    nAvgBytesPerSec = avgBytesPerSec,
                    cbSize = 0
                };

                _hEvent = CreateEvent(IntPtr.Zero, false, false, null);
                if (_hEvent == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Failed to create Win32 audio event handle.");
                }

                int result = waveInOpen(out _hWaveIn, WAVE_MAPPER, ref _format, _hEvent, IntPtr.Zero, CALLBACK_EVENT);
                if (result != 0)
                {
                    throw new InvalidOperationException(string.Format("Failed to open Windows audio input device (waveInOpen error {0}). Verify microphone connection.", result));
                }

                _headerPointers.Clear();
                _bufferPointers.Clear();

                // Allocate 8 ring buffers (0.1 seconds each = 3200 bytes at 16kHz)
                int bufferSize = (int)(avgBytesPerSec / 10);
                int hdrSize = Marshal.SizeOf(typeof(WAVEHDR));

                for (int i = 0; i < 8; i++)
                {
                    IntPtr pBuffer = Marshal.AllocHGlobal(bufferSize);
                    IntPtr pHeader = Marshal.AllocHGlobal(hdrSize);

                    _bufferPointers.Add(pBuffer);
                    _headerPointers.Add(pHeader);

                    WAVEHDR hdr = new WAVEHDR
                    {
                        lpData = pBuffer,
                        dwBufferLength = (uint)bufferSize,
                        dwBytesRecorded = 0,
                        dwUser = IntPtr.Zero,
                        dwFlags = 0,
                        dwLoops = 0,
                        lpNext = IntPtr.Zero,
                        reserved = IntPtr.Zero
                    };

                    Marshal.StructureToPtr(hdr, pHeader, false);
                    waveInPrepareHeader(_hWaveIn, pHeader, hdrSize);
                    waveInAddBuffer(_hWaveIn, pHeader, hdrSize);
                }

                _isRecording = true;
                _recordingStartTime = DateTime.Now;

                // Start background reader thread
                _recordThread = new Thread(RecordThreadLoop);
                _recordThread.IsBackground = true;
                _recordThread.Start();

                result = waveInStart(_hWaveIn);
                if (result != 0)
                {
                    throw new InvalidOperationException(string.Format("Failed to start audio recording stream (waveInStart error {0}).", result));
                }

                Logger.LogInfo(string.Format("Native M4A/AAC audio recording started ({0}Hz Mono): {1}", _format.nSamplesPerSec, _currentFilePath));
                return _currentFilePath;
            }
            catch (Exception ex)
            {
                CleanupWaveIn();
                Logger.LogError("Error starting audio recording", ex);
                _isRecording = false;
                _currentFilePath = null;
                throw;
            }
        }

        private void RecordThreadLoop()
        {
            int hdrSize = Marshal.SizeOf(typeof(WAVEHDR));

            while (_isRecording)
            {
                if (WaitForSingleObject(_hEvent, 20) == WAIT_OBJECT_0)
                {
                    for (int i = 0; i < _headerPointers.Count; i++)
                    {
                        IntPtr pHeader = _headerPointers[i];
                        WAVEHDR hdr = (WAVEHDR)Marshal.PtrToStructure(pHeader, typeof(WAVEHDR));

                        if ((hdr.dwFlags & WHDR_DONE) == WHDR_DONE)
                        {
                            if (hdr.dwBytesRecorded > 0)
                            {
                                byte[] data = new byte[hdr.dwBytesRecorded];
                                Marshal.Copy(hdr.lpData, data, 0, (int)hdr.dwBytesRecorded);

                                lock (_recordedAudioStream)
                                {
                                    _recordedAudioStream.Write(data, 0, data.Length);
                                }

                                // Real-time audio amplitude calculation for visualizer
                                int sampleCount = (int)hdr.dwBytesRecorded / 2;
                                if (sampleCount > 0)
                                {
                                    long sumSquares = 0;
                                    for (int s = 0; s < hdr.dwBytesRecorded - 1; s += 2)
                                    {
                                        short sample = (short)(data[s] | (data[s + 1] << 8));
                                        sumSquares += (long)sample * sample;
                                    }
                                    double rms = Math.Sqrt(sumSquares / (double)sampleCount);
                                    // Scale RMS to 0.0 - 1.0 (typical speaking voice is ~1200 to 7000)
                                    float rawLevel = (float)Math.Min(1.0, Math.Max(0.0, (rms - 150.0) / 4500.0));
                                    _currentAudioLevel = (_currentAudioLevel * 0.35f) + (rawLevel * 0.65f);
                                }
                            }

                            if (_isRecording && _hWaveIn != IntPtr.Zero)
                            {
                                hdr.dwFlags = WHDR_PREPARED;
                                hdr.dwBytesRecorded = 0;
                                Marshal.StructureToPtr(hdr, pHeader, false);
                                waveInAddBuffer(_hWaveIn, pHeader, hdrSize);
                            }
                            else
                            {
                                hdr.dwFlags = 0;
                                hdr.dwBytesRecorded = 0;
                                Marshal.StructureToPtr(hdr, pHeader, false);
                            }
                        }
                    }
                }
            }
        }

        public string StopRecording()
        {
            if (!_isRecording)
                return _currentFilePath;

            _isRecording = false;
            _currentAudioLevel = 0f;

            try
            {
                if (_hWaveIn != IntPtr.Zero)
                {
                    waveInStop(_hWaveIn);
                    waveInReset(_hWaveIn);
                }

                if (_recordThread != null && _recordThread.IsAlive)
                {
                    _recordThread.Join(500);
                }

                LastRecordingWasSilent = false;
                lock (_recordedAudioStream)
                {
                    byte[] pcmData = _recordedAudioStream.ToArray();
                    if (SettingsService.Current.EnableAudioPreprocessing)
                    {
                        try
                        {
                            var prep = AudioPreprocessor.Process(pcmData, (int)_format.nSamplesPerSec);
                            LastRecordingWasSilent = prep.Silent;
                            if (prep.Changed)
                            {
                                Logger.LogInfo(string.Format("Audio preprocessing: {0:F1}s -> {1:F1}s, gain x{2:F2} ({3}).", prep.OriginalSeconds, prep.NewSeconds, prep.Gain, prep.Note));
                                pcmData = prep.Pcm;
                            }
                            else
                            {
                                Logger.LogInfo("Audio preprocessing skipped: " + prep.Note);
                            }
                        }
                        catch (Exception prepEx)
                        {
                            Logger.LogError("Audio preprocessing failed; using the original recording", prepEx);
                        }
                    }
                    AudioEncoderService.SaveAudioFile(pcmData, _currentFilePath, _format.nSamplesPerSec);
                    try
                    {
                        AudioChunker.SaveParts(pcmData, (int)_format.nSamplesPerSec, _currentFilePath);
                    }
                    catch (Exception chunkEx)
                    {
                        AudioChunker.DeleteParts(_currentFilePath);
                        Logger.LogError("Could not split the recording into blocks; it will be sent whole", chunkEx);
                    }
                }

                CleanupWaveIn();

                if (File.Exists(_currentFilePath))
                {
                    FileInfo fi = new FileInfo(_currentFilePath);
                    Logger.LogInfo(string.Format("Audio recording stopped and saved ({0} bytes): {1}", fi.Length, _currentFilePath));
                    return _currentFilePath;
                }
                else
                {
                    Logger.LogWarning(string.Format("Recorded file not found after save: {0}", _currentFilePath));
                    return null;
                }
            }
            catch (Exception ex)
            {
                CleanupWaveIn();
                Logger.LogError("Error stopping audio recording", ex);
                return null;
            }
        }

        public void CancelRecording()
        {
            if (!_isRecording) return;
            _isRecording = false;
            _currentAudioLevel = 0f;

            try
            {
                if (_hWaveIn != IntPtr.Zero)
                {
                    waveInStop(_hWaveIn);
                    waveInReset(_hWaveIn);
                }

                if (_recordThread != null && _recordThread.IsAlive)
                {
                    _recordThread.Join(500);
                }

                CleanupWaveIn();

                if (!string.IsNullOrEmpty(_currentFilePath) && File.Exists(_currentFilePath))
                {
                    try { File.Delete(_currentFilePath); } catch { }
                }
                _currentFilePath = null;
                Logger.LogInfo("Audio recording cancelled and file deleted.");
            }
            catch (Exception ex)
            {
                CleanupWaveIn();
                Logger.LogError("Error cancelling audio recording", ex);
            }
        }

        private void CleanupWaveIn()
        {
            try
            {
                int hdrSize = Marshal.SizeOf(typeof(WAVEHDR));
                if (_hWaveIn != IntPtr.Zero)
                {
                    for (int i = 0; i < _headerPointers.Count; i++)
                    {
                        IntPtr pHeader = _headerPointers[i];
                        waveInUnprepareHeader(_hWaveIn, pHeader, hdrSize);
                    }
                    waveInClose(_hWaveIn);
                    _hWaveIn = IntPtr.Zero;
                }

                if (_hEvent != IntPtr.Zero)
                {
                    CloseHandle(_hEvent);
                    _hEvent = IntPtr.Zero;
                }

                foreach (var pHeader in _headerPointers)
                {
                    if (pHeader != IntPtr.Zero)
                        Marshal.FreeHGlobal(pHeader);
                }
                _headerPointers.Clear();

                foreach (var pBuffer in _bufferPointers)
                {
                    if (pBuffer != IntPtr.Zero)
                        Marshal.FreeHGlobal(pBuffer);
                }
                _bufferPointers.Clear();

                if (_recordedAudioStream != null)
                {
                    _recordedAudioStream.Dispose();
                    _recordedAudioStream = null;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("Error during waveIn cleanup", ex);
            }
        }

        private static void SavePcmWavFile(byte[] pcmData, string filePath, uint sampleRate)
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
                // RIFF header
                bw.Write(Encoding.ASCII.GetBytes("RIFF"));
                bw.Write(chunkSize);
                bw.Write(Encoding.ASCII.GetBytes("WAVE"));

                // fmt chunk
                bw.Write(Encoding.ASCII.GetBytes("fmt "));
                bw.Write((uint)16); // SubChunk1Size for PCM = 16
                bw.Write((ushort)1); // AudioFormat = 1 (PCM)
                bw.Write(numChannels);
                bw.Write(sampleRate);
                bw.Write(byteRate);
                bw.Write(blockAlign);
                bw.Write(bitsPerSample);

                // data chunk
                bw.Write(Encoding.ASCII.GetBytes("data"));
                bw.Write(subChunk2Size);
                bw.Write(pcmData);

                bw.Flush();
            }
        }
    }
}
