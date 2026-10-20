using System;
using System.Collections.Generic;

namespace VRTutorial
{
    /// <summary>
    /// The one place anything can ask "is the participant in a paused state right now" - the
    /// pause menu, or the help-requested panel.
    ///
    /// A static flag rather than a reference to PauseController on purpose. The controller lives
    /// in the tutorial scene, which loads additively; the things that care about pausing are
    /// scattered across steps and may enable before or after it. Unity also cannot serialise a
    /// cross-scene reference, so if the pause menu is ever promoted to Bootstrap a direct
    /// reference would have to be torn out again.
    ///
    /// Pausing is held by owners rather than set as a bool. Two things pause: the pause menu and
    /// a confirmed help request, and "Get help" from the pause menu closes the menu while the
    /// request panel is opening. With a single bool, that close un-paused the request - the
    /// participant could walk and turn with the help panel in front of them. With owners, the
    /// menu releases only its own hold and the request's hold keeps everything paused.
    ///
    /// Note what pausing deliberately does NOT do: touch Time.timeScale. Every coroutine in
    /// TutorialFlow and HeadLockedUI runs on unscaled time, so a zero timescale would not stop
    /// them anyway, and freezing the world while head tracking keeps running is unpleasant in a
    /// headset. Pausing suspends locomotion, locks the head-locked panels in place and asks tasks
    /// to hold; nothing else.
    /// </summary>
    public static class TutorialPause
    {
        private static readonly HashSet<object> Holders = new HashSet<object>();

        /// <summary>Raised whenever the overall pause state changes, with the new state.</summary>
        public static event Action<bool> Changed;

        /// <summary>True while anything holds the pause.</summary>
        public static bool IsPaused => Holders.Count > 0;

        /// <summary>Pauses on behalf of an owner. Holding twice with the same owner is harmless.</summary>
        public static void Hold(object owner)
        {
            if (owner == null) return;
            bool was = IsPaused;
            Holders.Add(owner);
            if (!was && IsPaused) Changed?.Invoke(true);
        }

        /// <summary>Releases an owner's hold. Unpauses only when no other owner still holds it.</summary>
        public static void Release(object owner)
        {
            if (owner == null) return;
            bool was = IsPaused;
            Holders.Remove(owner);
            if (was && !IsPaused) Changed?.Invoke(false);
        }

        public static bool IsHeldBy(object owner) => owner != null && Holders.Contains(owner);

        /// <summary>
        /// Clears every hold without raising the event. Called when the tutorial scene unloads:
        /// the state is static and would otherwise survive into the next scene, leaving a fresh
        /// session convinced it started paused.
        /// </summary>
        public static void ResetState() => Holders.Clear();
    }
}
