using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VoxIA.Services
{
    public static class AudioVolumeService
    {
        [ComImport]
        [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumeratorComObject
        {
        }

        [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr ppDevices);
            int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppEndpoint);
            int GetDevice(string pwstrId, out IMMDevice ppDevice);
            int RegisterEndpointNotificationCallback(IntPtr pClient);
            int UnregisterEndpointNotificationCallback(IntPtr pClient);
        }

        [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
            int OpenPropertyStore(int stgmAccess, out IntPtr ppProperties);
            int GetId(out IntPtr ppstrId);
            int GetState(out int pdwState);
        }

        [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionManager2
        {
            int GetAudioSessionControl(ref Guid AudioSessionGuid, int StreamFlags, out IntPtr SessionControl);
            int GetSimpleAudioVolume(ref Guid AudioSessionGuid, int StreamFlags, out ISimpleAudioVolume AudioVolume);
            int GetSessionEnumerator(out IAudioSessionEnumerator SessionEnum);
            int RegisterSessionNotification(IntPtr NewSessionId);
            int UnregisterSessionNotification(IntPtr NewSessionId);
            int RegisterDuckNotification(string sessionID, IntPtr pduckNotification);
            int UnregisterDuckNotification(IntPtr pduckNotification);
        }

        [Guid("E2F5EE55-0470-452C-A470-8E1102A16972"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionEnumerator
        {
            int GetCount(out int SessionCount);
            int GetSession(int SessionIndex, out IAudioSessionControl Session);
        }

        [Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionControl
        {
            int GetState(out int pRetVal);
            int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
            int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string Value, ref Guid EventContext);
            int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
            int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string Value, ref Guid EventContext);
            int GetGroupingParam(out Guid pRetVal);
            int SetGroupingParam(ref Guid Override, ref Guid EventContext);
            int RegisterAudioSessionNotification(IntPtr NewNotifications);
            int UnregisterAudioSessionNotification(IntPtr NewNotifications);
        }

        [Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionControl2 : IAudioSessionControl
        {
            new int GetState(out int pRetVal);
            new int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
            new int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string Value, ref Guid EventContext);
            new int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
            new int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string Value, ref Guid EventContext);
            new int GetGroupingParam(out Guid pRetVal);
            new int SetGroupingParam(ref Guid Override, ref Guid EventContext);
            new int RegisterAudioSessionNotification(IntPtr NewNotifications);
            new int UnregisterAudioSessionNotification(IntPtr NewNotifications);

            int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
            int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
            int GetProcessId(out int pRetVal);
            int IsSystemSoundsSession();
            int SetDuckingPreference(bool optOut);
        }

        [Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ISimpleAudioVolume
        {
            int SetMasterVolume(float fLevel, ref Guid EventContext);
            int GetMasterVolume(out float pfLevel);
            int SetMute(bool bMute, ref Guid EventContext);
            int GetMute(out bool pbMute);
        }

        private const int eRender = 0;
        private const int eCapture = 1;
        private const int eConsole = 0;
        private const int CLSCTX_ALL = 23;

        /// <summary>
        /// Resets and forces VoxIA audio volume in Windows Volume Mixer / Audio Engine to 100% unmuted.
        /// </summary>
        public static void EnsureFullVolume()
        {
            try
            {
                int currentPid = Process.GetCurrentProcess().Id;
                IMMDeviceEnumerator enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();

                int[] dataFlows = new int[] { eRender };

                foreach (int flow in dataFlows)
                {
                    try
                    {
                        IMMDevice device;
                        if (enumerator.GetDefaultAudioEndpoint(flow, eConsole, out device) == 0 && device != null)
                        {
                            Guid IID_IAudioSessionManager2 = typeof(IAudioSessionManager2).GUID;
                            object mgrObj;
                            if (device.Activate(ref IID_IAudioSessionManager2, CLSCTX_ALL, IntPtr.Zero, out mgrObj) == 0 && mgrObj is IAudioSessionManager2)
                            {
                                IAudioSessionManager2 mgr = (IAudioSessionManager2)mgrObj;
                                IAudioSessionEnumerator sessionEnum;
                                if (mgr.GetSessionEnumerator(out sessionEnum) == 0 && sessionEnum != null)
                                {
                                    int count;
                                    if (sessionEnum.GetCount(out count) == 0)
                                    {
                                        for (int i = 0; i < count; i++)
                                        {
                                            IAudioSessionControl sessionCtrl;
                                            if (sessionEnum.GetSession(i, out sessionCtrl) == 0 && sessionCtrl != null)
                                            {
                                                if (sessionCtrl is IAudioSessionControl2)
                                                {
                                                    IAudioSessionControl2 sessionCtrl2 = (IAudioSessionControl2)sessionCtrl;
                                                    int pid;
                                                    if (sessionCtrl2.GetProcessId(out pid) == 0 && pid == currentPid)
                                                    {
                                                        if (sessionCtrl is ISimpleAudioVolume)
                                                        {
                                                            ISimpleAudioVolume volume = (ISimpleAudioVolume)sessionCtrl;
                                                            Guid emptyGuid = Guid.Empty;
                                                            volume.SetMute(false, ref emptyGuid);
                                                            volume.SetMasterVolume(1.0f, ref emptyGuid);
                                                            Logger.LogInfo(string.Format("Force-reset Windows Volume Mixer level to 100% unmuted for VoxIA on flow {0}.", flow));
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Endpoint doesn't support session enumeration (normal for capture on some hardware)
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("Error in AudioVolumeService.EnsureFullVolume", ex);
            }
        }
    }
}
