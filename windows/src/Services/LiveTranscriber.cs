using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace VoxIA.Services
{
    /// <summary>
    /// Transcrição durante a fala: enquanto a pessoa ainda dita, cada bloco de ~15 a 27 s é cortado
    /// numa pausa (mesma regra do ditado inteiro) e já vai para o Whisper. Ao parar, só falta o
    /// último trecho, então ditados longos ficam prontos quase na hora.
    ///
    /// A precisão é a mesma do envio inteiro: os blocos saem nos mesmos pontos de corte e cada um
    /// recebe como prompt o texto dos anteriores. Se um bloco falhar (rede), a transcrição ao vivo
    /// é abandonada e o ditado inteiro segue pelo caminho normal, com a fila de reenvio.
    /// Mesma lógica do Android (LiveTranscriber.kt).
    /// </summary>
    public class LiveTranscriber
    {
        public class Partial
        {
            public int CommittedSamples;
            public string Text;
        }

        private const int PollMs = 1000;
        private const int FinishTimeoutMs = 20000;

        private readonly AudioRecorder _recorder;
        private readonly string _fieldContext;
        private readonly StringBuilder _text = new StringBuilder();
        private volatile bool _stopRequested;
        private volatile bool _failed;
        private int _committedSamples;
        private Task _task;

        public LiveTranscriber(AudioRecorder recorder, string fieldContext)
        {
            _recorder = recorder;
            _fieldContext = fieldContext;
        }

        public void Start()
        {
            _task = Task.Run(new Func<Task>(LoopAsync));
        }

        private async Task LoopAsync()
        {
            try
            {
                while (!_stopRequested && !_failed)
                {
                    await Task.Delay(PollMs);
                    if (_stopRequested) break;

                    int sampleRate = _recorder.SampleRate;
                    int window = (int)(AudioChunker.LiveWindowSeconds * sampleRate);
                    int available = _recorder.RecordedSamples;
                    if (available - _committedSamples < window) continue;

                    byte[] pcm = _recorder.ReadRecorded(_committedSamples, available);
                    if (pcm == null) continue;
                    List<int> cuts = AudioChunker.FindCuts(pcm, sampleRate);
                    if (cuts.Count == 0) continue;
                    int cut = cuts[0];

                    byte[] chunk = new byte[cut * 2];
                    Buffer.BlockCopy(pcm, 0, chunk, 0, chunk.Length);
                    var prep = AudioPreprocessor.Process(chunk, sampleRate);
                    // Bloco do meio do ditado só é pulado se estiver praticamente mudo: fala baixa
                    // (microfone longe) também vai para o Whisper
                    if (!prep.Mute)
                    {
                        byte[] toSend = (prep.Changed && SettingsService.Current.EnableAudioPreprocessing) ? prep.Pcm : chunk;
                        string promptContext = _text.Length == 0 ? _fieldContext : _text.ToString();
                        string piece;
                        try
                        {
                            piece = await TranscriptionService.TranscribeLiveChunkAsync(toSend, sampleRate, promptContext, prep.SpeechFrames);
                        }
                        catch (Exception ex)
                        {
                            Logger.LogWarning("Live block failed; the whole dictation will be sent when it stops: " + ex.Message);
                            _failed = true;
                            break;
                        }
                        piece = (piece ?? "").Trim();
                        if (piece.Length > 0)
                        {
                            lock (_text)
                            {
                                if (_text.Length > 0) _text.Append(' ');
                                _text.Append(piece);
                            }
                        }
                    }
                    _committedSamples += cut;
                    Logger.LogInfo(string.Format("Live block ready ({0:F1} s); {1} chars so far.", cut / (double)sampleRate, _text.Length));
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("Live transcription error; the whole dictation will be sent when it stops", ex);
                _failed = true;
            }
        }

        /// <summary>
        /// Para de cortar blocos e espera o que estiver em andamento (no máximo 20 s). Devolve o que
        /// já foi transcrito, ou null quando nada foi adiantado (ou houve falha) e o ditado deve ir inteiro.
        /// </summary>
        public async Task<Partial> FinishAsync()
        {
            _stopRequested = true;
            Task running = _task;
            _task = null;
            if (running != null)
            {
                Task done = await Task.WhenAny(running, Task.Delay(FinishTimeoutMs));
                if (done != running)
                {
                    Logger.LogWarning("Live block took too long; the whole dictation will be sent.");
                    _failed = true;
                }
            }
            if (_failed || _committedSamples == 0) return null;
            lock (_text)
            {
                return new Partial { CommittedSamples = _committedSamples, Text = _text.ToString() };
            }
        }

        public void Cancel()
        {
            _stopRequested = true;
            _failed = true;
        }
    }
}
