using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace VoxIA.Services
{
    /// <summary>
    /// Durante o ditado, sobe o volume de captação do microfone no Windows para 100% e devolve o
    /// nível original ao parar. Com o fone longe da boca a voz chega baixa; subir o ganho antes da
    /// conversão digital deixa a fala mais nítida do que amplificar o áudio depois de gravado.
    ///
    /// Se o som começar a saturar (microfone perto demais), o nível desce sozinho, aos poucos, até
    /// parar de saturar, nunca abaixo do nível que a pessoa usava. Se o VoxIA fechar no meio do
    /// ditado, o nível original fica salvo em disco e é devolvido na próxima abertura.
    /// Todas as chamadas devem vir da thread da interface (os objetos COM do áudio são criados nela).
    /// </summary>
    public static class MicrophoneLevelService
    {
        private const float StepDown = 0.08f;          // quanto desce a cada saturação detectada
        private const int ClipCheckMs = 300;
        public const int ClipPeak = 32000;              // amostra acima disso = saturando

        private static IAudioEndpointVolume _endpoint;
        private static float _originalLevel = -1f;
        private static float _currentLevel = -1f;
        private static int _clipEvents;
        private static System.Windows.Forms.Timer _clipTimer;

        private static string RestoreFilePath
        {
            get { return Path.Combine(AppPathService.DataDirectory, "mic_level_restore.txt"); }
        }

        /// <summary>Chamado pela gravação (qualquer thread) quando um bloco de 100 ms satura.</summary>
        public static void ReportClipping()
        {
            Interlocked.Increment(ref _clipEvents);
        }

        /// <summary>Sobe o microfone que será usado para 100%. Sem efeito se a opção estiver desligada.</summary>
        public static void Boost(string preferredMicrophone)
        {
            Restore();
            if (!SettingsService.Current.BoostMicrophoneDuringDictation) return;
            try
            {
                IAudioEndpointVolume endpoint = FindEndpoint(preferredMicrophone);
                if (endpoint == null) return;
                float level;
                Guid ctx = Guid.Empty;
                if (endpoint.GetMasterVolumeLevelScalar(out level) != 0) return;
                if (level >= 0.99f)
                {
                    Logger.LogInfo("Microphone level already at maximum.");
                    return;
                }
                SaveRestoreFile(level);
                if (endpoint.SetMasterVolumeLevelScalar(1.0f, ref ctx) != 0)
                {
                    DeleteRestoreFile();
                    return;
                }
                _endpoint = endpoint;
                _originalLevel = level;
                _currentLevel = 1.0f;
                Interlocked.Exchange(ref _clipEvents, 0);
                StartClipTimer();
                Logger.LogInfo(string.Format("Microphone level raised from {0:P0} to 100% for dictation.", level));
            }
            catch (Exception ex)
            {
                Logger.LogError("Could not raise the microphone level", ex);
            }
        }

        /// <summary>Devolve o nível original do microfone (se ele foi alterado).</summary>
        public static void Restore()
        {
            StopClipTimer();
            if (_endpoint == null) return;
            try
            {
                Guid ctx = Guid.Empty;
                _endpoint.SetMasterVolumeLevelScalar(_originalLevel, ref ctx);
                Logger.LogInfo(string.Format("Microphone level restored to {0:P0}.", _originalLevel));
            }
            catch (Exception ex)
            {
                Logger.LogError("Could not restore the microphone level", ex);
            }
            finally
            {
                _endpoint = null;
                _originalLevel = -1f;
                _currentLevel = -1f;
                DeleteRestoreFile();
            }
        }

        /// <summary>Na abertura do app: devolve o nível se o VoxIA fechou no meio de um ditado.</summary>
        public static void RestoreAfterCrash()
        {
            try
            {
                if (!File.Exists(RestoreFilePath)) return;
                string[] parts = File.ReadAllText(RestoreFilePath).Split('|');
                float level;
                if (parts.Length >= 1 && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out level))
                {
                    IAudioEndpointVolume endpoint = FindEndpoint(parts.Length >= 2 ? parts[1] : "");
                    if (endpoint != null)
                    {
                        Guid ctx = Guid.Empty;
                        endpoint.SetMasterVolumeLevelScalar(Math.Max(0f, Math.Min(1f, level)), ref ctx);
                        Logger.LogInfo(string.Format("Microphone level restored to {0:P0} after an unexpected exit.", level));
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("Could not restore the microphone level after an unexpected exit", ex);
            }
            DeleteRestoreFile();
        }

        // ---------- Saturação ----------

        private static void StartClipTimer()
        {
            StopClipTimer();
            _clipTimer = new System.Windows.Forms.Timer { Interval = ClipCheckMs };
            _clipTimer.Tick += (s, e) => OnClipTick();
            _clipTimer.Start();
        }

        private static void StopClipTimer()
        {
            if (_clipTimer == null) return;
            _clipTimer.Stop();
            _clipTimer.Dispose();
            _clipTimer = null;
        }

        private static void OnClipTick()
        {
            int clips = Interlocked.Exchange(ref _clipEvents, 0);
            if (clips == 0 || _endpoint == null || _currentLevel <= _originalLevel) return;
            try
            {
                float next = Math.Max(_originalLevel, _currentLevel - StepDown);
                Guid ctx = Guid.Empty;
                if (_endpoint.SetMasterVolumeLevelScalar(next, ref ctx) == 0)
                {
                    _currentLevel = next;
                    Logger.LogInfo(string.Format("Microphone was clipping; level lowered to {0:P0}.", next));
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("Could not lower the microphone level", ex);
            }
        }

        // ---------- Arquivo de segurança ----------

        private static void SaveRestoreFile(float level)
        {
            try
            {
                string mic = SettingsService.Current.PreferredMicrophone ?? "";
                File.WriteAllText(RestoreFilePath, level.ToString("R", CultureInfo.InvariantCulture) + "|" + mic);
            }
            catch { }
        }

        private static void DeleteRestoreFile()
        {
            try { if (File.Exists(RestoreFilePath)) File.Delete(RestoreFilePath); } catch { }
        }

        // ---------- Dispositivo ----------

        /// <summary>
        /// Microfone padrão do Windows ou, se a pessoa escolheu um, o de mesmo nome. Os nomes da
        /// lista de gravação (waveIn) são cortados em 31 letras, por isso compara pelo começo.
        /// </summary>
        private static IAudioEndpointVolume FindEndpoint(string preferredMicrophone)
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            IMMDevice device = null;

            if (!string.IsNullOrWhiteSpace(preferredMicrophone))
            {
                IMMDeviceCollection collection;
                if (enumerator.EnumAudioEndpoints(ECapture, DeviceStateActive, out collection) == 0 && collection != null)
                {
                    uint count;
                    collection.GetCount(out count);
                    string wanted = preferredMicrophone.Trim();
                    for (uint i = 0; i < count && device == null; i++)
                    {
                        IMMDevice candidate;
                        if (collection.Item(i, out candidate) != 0 || candidate == null) continue;
                        string name = FriendlyName(candidate);
                        if (name.Length > 0 && (name.Equals(wanted, StringComparison.OrdinalIgnoreCase) ||
                            name.StartsWith(wanted, StringComparison.OrdinalIgnoreCase) ||
                            wanted.StartsWith(name, StringComparison.OrdinalIgnoreCase)))
                        {
                            device = candidate;
                        }
                    }
                }
            }

            if (device == null && enumerator.GetDefaultAudioEndpoint(ECapture, EConsole, out device) != 0)
                return null;
            if (device == null) return null;

            Guid iid = typeof(IAudioEndpointVolume).GUID;
            object obj;
            if (device.Activate(ref iid, ClsctxAll, IntPtr.Zero, out obj) != 0) return null;
            return obj as IAudioEndpointVolume;
        }

        private static string FriendlyName(IMMDevice device)
        {
            IPropertyStore store;
            if (device.OpenPropertyStore(StgmRead, out store) != 0 || store == null) return "";
            PropertyKey key = new PropertyKey { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };
            PropVariant value = new PropVariant();
            try
            {
                if (store.GetValue(ref key, out value) != 0) return "";
                return value.vt == VtLpwstr && value.pointer != IntPtr.Zero ? (Marshal.PtrToStringUni(value.pointer) ?? "") : "";
            }
            finally
            {
                PropVariantClear(ref value);
            }
        }

        // ---------- COM (Core Audio) ----------

        private const int ECapture = 1;
        private const int EConsole = 0;
        private const int DeviceStateActive = 1;
        private const int ClsctxAll = 23;
        private const int StgmRead = 0;
        private const ushort VtLpwstr = 31;

        [DllImport("ole32.dll")]
        private static extern int PropVariantClear(ref PropVariant pvar);

        [StructLayout(LayoutKind.Sequential)]
        private struct PropertyKey
        {
            public Guid fmtid;
            public int pid;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PropVariant
        {
            public ushort vt;
            public ushort reserved1;
            public ushort reserved2;
            public ushort reserved3;
            public IntPtr pointer;
            public IntPtr pointer2;
        }

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumeratorComObject
        {
        }

        [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection ppDevices);
            int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppEndpoint);
            int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);
            int RegisterEndpointNotificationCallback(IntPtr pClient);
            int UnregisterEndpointNotificationCallback(IntPtr pClient);
        }

        [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceCollection
        {
            int GetCount(out uint pcDevices);
            int Item(uint nDevice, out IMMDevice ppDevice);
        }

        [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
            int OpenPropertyStore(int stgmAccess, out IPropertyStore ppProperties);
            int GetId(out IntPtr ppstrId);
            int GetState(out int pdwState);
        }

        [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            int GetCount(out uint cProps);
            int GetAt(uint iProp, out PropertyKey pkey);
            int GetValue(ref PropertyKey key, out PropVariant pv);
            int SetValue(ref PropertyKey key, ref PropVariant propvar);
            int Commit();
        }

        [Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioEndpointVolume
        {
            int RegisterControlChangeNotify(IntPtr pNotify);
            int UnregisterControlChangeNotify(IntPtr pNotify);
            int GetChannelCount(out uint pnChannelCount);
            int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
            int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
            int GetMasterVolumeLevel(out float pfLevelDB);
            int GetMasterVolumeLevelScalar(out float pfLevel);
            int SetChannelVolumeLevel(uint nChannel, float fLevelDB, ref Guid pguidEventContext);
            int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, ref Guid pguidEventContext);
            int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
            int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
            int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid pguidEventContext);
            int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
            int GetVolumeStepInfo(out uint pnStep, out uint pnStepCount);
            int VolumeStepUp(ref Guid pguidEventContext);
            int VolumeStepDown(ref Guid pguidEventContext);
            int QueryHardwareSupport(out uint pdwHardwareSupportMask);
            int GetVolumeRange(out float pflVolumeMindB, out float pflVolumeMaxdB, out float pflVolumeIncrementdB);
        }
    }
}
