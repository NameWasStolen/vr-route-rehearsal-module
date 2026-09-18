namespace VRTutorial
{
    /// <summary>
    /// A lesson the participant has to actually perform - snap turning, holding the movement
    /// input - as opposed to a page they only read.
    ///
    /// This exists so TutorialFlow can put a lesson back to its starting state without knowing
    /// which lesson it is. That matters for reviewing: reopening the movement lesson from the
    /// pause menu has to be a fresh attempt, and the flow cannot hard-code a list of task types
    /// it knows how to reset without needing an edit every time a lesson is added.
    ///
    /// Tasks already reset themselves on enable, and steps deactivate while hidden, so in the
    /// normal case re-entering a step is enough. This interface is what makes that reliable for
    /// a step with Deactivate When Hidden unticked, where no enable ever happens.
    /// </summary>
    public interface ITutorialTask
    {
        /// <summary>Returns the lesson to its unstarted state, prompt included.</summary>
        void ResetTask();

        /// <summary>True once the participant has satisfied the lesson.</summary>
        bool IsComplete { get; }
    }
}
