using System;

namespace VRTutorial
{
    /// <summary>
    /// The one place anything can ask "is the participant in the pause menu right now".
    ///
    /// A static flag rather than a reference to PauseController on purpose. The controller lives
    /// in the tutorial scene, which loads additively; the things that care about pausing are
    /// scattered across steps and may enable before or after it. Unity also cannot serialise a
    /// cross-scene reference, so if the pause menu is ever promoted to Bootstrap a direct
    /// reference would have to be torn out again.
    ///
    /// Note what pausing deliberately does NOT do: touch Time.timeScale. Every coroutine in
    /// TutorialFlow and HeadLockedUI runs on unscaled time, so a zero timescale would not stop
    /// them anyway, and freezing the world while head tracking keeps running is unpleasant in a
    /// headset. Pausing suspends locomotion and asks tasks to hold; nothing else.
    /// </summary>
    public static class TutorialPause
    {
        private static bool _isPaused;

        /// <summary>Raised whenever the pause state changes, with the new state.</summary>
        public static event Action<bool> Changed;

        public static bool IsPaused
        {
            get => _isPaused;
            set
            {
                if (_isPaused == value) return;
                _isPaused = value;
                Changed?.Invoke(_isPaused);
            }
        }

        /// <summary>
        /// Clears the flag without raising the event. Called when the tutorial scene unloads:
        /// the flag is static and would otherwise survive into the next scene, leaving a fresh
        /// session convinced it started paused.
        /// </summary>
        public static void ResetState() => _isPaused = false;
    }
}
