using System;

namespace VRTutorial
{
    /// <summary>
    /// Where the participant currently is in asking for help.
    ///
    /// Three states rather than a bool, because "holding the button down" and "help has been
    /// asked for" need different answers from everything that cares. Locomotion should keep
    /// working while somebody is still deciding; it should not once the request is placed.
    /// </summary>
    public enum AssistanceState
    {
        /// <summary>Nothing happening.</summary>
        Idle,

        /// <summary>The button is being held and the confirmation bar is filling.</summary>
        Confirming,

        /// <summary>The request has been placed and the participant is waiting.</summary>
        Requested,
    }

    /// <summary>
    /// The one place anything can ask whether the participant is asking for help.
    ///
    /// Static, for the same reasons TutorialPause is, and they are worth repeating because they
    /// are what keeps this working when the scenes are loaded additively: the controller lives in
    /// the tutorial scene, the things that care may enable before or after it, and Unity cannot
    /// serialise a cross-scene reference if this is ever promoted to Bootstrap.
    /// </summary>
    public static class AssistanceRequest
    {
        private static AssistanceState _state = AssistanceState.Idle;

        /// <summary>Raised whenever the state changes, with the new state.</summary>
        public static event Action<AssistanceState> Changed;

        public static AssistanceState State
        {
            get => _state;
            set
            {
                if (_state == value) return;
                _state = value;
                Changed?.Invoke(_state);
            }
        }

        /// <summary>True whenever one of the assistance panels is on screen.</summary>
        public static bool IsActive => _state != AssistanceState.Idle;

        /// <summary>
        /// Clears the state without raising the event. Called when the tutorial scene unloads:
        /// the state is static and would otherwise survive into the next scene, leaving a fresh
        /// session convinced it began with a request outstanding.
        /// </summary>
        public static void ResetState() => _state = AssistanceState.Idle;
    }
}
