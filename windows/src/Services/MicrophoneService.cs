using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace VoxIA.Services
{
    /// <summary>
    /// Lista os microfones do Windows e descobre qual usar na gravação.
    /// Nome vazio = "Padrão do sistema" (o Windows escolhe).
    /// </summary>
    public static class MicrophoneService
    {
        public const int WaveMapper = -1;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct WAVEINCAPS
        {
            public ushort wMid;
            public ushort wPid;
            public uint vDriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szPname;
            public uint dwFormats;
            public ushort wChannels;
            public ushort wReserved1;
        }

        [DllImport("winmm.dll", CharSet = CharSet.Auto)]
        private static extern int waveInGetNumDevs();

        [DllImport("winmm.dll", CharSet = CharSet.Auto)]
        private static extern int waveInGetDevCaps(IntPtr uDeviceID, ref WAVEINCAPS pwic, int cbwic);

        /// <summary>Nomes das entradas de áudio, na ordem em que o Windows as numera.</summary>
        public static List<string> GetInputDeviceNames()
        {
            var names = new List<string>();
            try
            {
                int count = waveInGetNumDevs();
                for (int i = 0; i < count; i++)
                {
                    WAVEINCAPS caps = new WAVEINCAPS();
                    if (waveInGetDevCaps(new IntPtr(i), ref caps, Marshal.SizeOf(typeof(WAVEINCAPS))) == 0)
                        names.Add(caps.szPname ?? "");
                    else
                        names.Add("");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("Could not list microphones", ex);
            }
            return names;
        }

        /// <summary>Número do dispositivo para o nome escolhido; WaveMapper (padrão) se vazio ou não encontrado.</summary>
        public static int ResolveDeviceId(string preferredName)
        {
            if (string.IsNullOrWhiteSpace(preferredName)) return WaveMapper;

            List<string> names = GetInputDeviceNames();
            for (int i = 0; i < names.Count; i++)
            {
                if (string.Equals(names[i], preferredName, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            Logger.LogWarning(string.Format("Preferred microphone '{0}' not found; using the system default.", preferredName));
            return WaveMapper;
        }
    }
}
