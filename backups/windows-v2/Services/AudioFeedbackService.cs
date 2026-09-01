using System;
using System.Media;
using System.Runtime.InteropServices;
using System.Threading;

namespace VoxIA.Services
{
    public static class AudioFeedbackService
    {
        [DllImport("user32.dll")]
        private static extern bool MessageBeep(uint uType);

        public static void PlayStartBeep()
        {
            if (!SettingsService.Current.AudioBeepFeedback) return;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    SystemSounds.Asterisk.Play();
                }
                catch
                {
                    MessageBeep(0x00000040); // MB_ICONASTERISK
                }
            });
        }

        public static void PlayStopBeep()
        {
            if (!SettingsService.Current.AudioBeepFeedback) return;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    SystemSounds.Exclamation.Play();
                }
                catch
                {
                    MessageBeep(0x00000030); // MB_ICONEXCLAMATION
                }
            });
        }

        public static void PlaySuccessBeep()
        {
            if (!SettingsService.Current.AudioBeepFeedback) return;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    SystemSounds.Hand.Play();
                }
                catch
                {
                    MessageBeep(0);
                }
            });
        }
    }
}
