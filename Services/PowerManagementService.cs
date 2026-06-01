using System;
using System.Runtime.InteropServices;

namespace LocalCursor.Services
{
    /// <summary>
    /// Prevents Windows from going to sleep or locking the screen while the application is running.
    /// </summary>
    public class PowerManagementService : IDisposable
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern uint SetThreadExecutionState(uint esFlags);

        // Execution state flags
        private const uint ES_CONTINUOUS = 0x80000000;
        private const uint ES_SYSTEM_REQUIRED = 0x00000001;
        private const uint ES_DISPLAY_REQUIRED = 0x00000002;
        private const uint ES_AWAYMODE_REQUIRED = 0x00000040;

        private bool _isActive;

        /// <summary>
        /// Prevents the system from sleeping and the display from turning off.
        /// Call this when starting long-running operations.
        /// </summary>
        public void PreventSleep()
        {
            if (_isActive) return;

            // Prevent sleep and keep display on
            SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED);
            _isActive = true;
        }

        /// <summary>
        /// Allows the system to sleep again.
        /// Call this when operations are complete or the app is closing.
        /// </summary>
        public void AllowSleep()
        {
            if (!_isActive) return;

            // Clear all flags, allow normal sleep behavior
            SetThreadExecutionState(ES_CONTINUOUS);
            _isActive = false;
        }

        /// <summary>
        /// Resets the idle timer (pings the system to stay awake).
        /// Useful during ongoing operations.
        /// </summary>
        public void KeepAwake()
        {
            SetThreadExecutionState(ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED);
        }

        public void Dispose()
        {
            AllowSleep();
        }
    }
}
