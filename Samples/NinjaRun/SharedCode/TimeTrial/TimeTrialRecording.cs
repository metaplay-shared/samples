using Metaplay.Core.Model;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// One change of the jump button's state, pinned to the simulation step it happened on. Render frames and
    /// the device clock are not part of a recording: a step index is the only timing the simulation reads.
    /// </summary>
    [MetaSerializable]
    public struct TimeTrialInput
    {
        /// <summary>The step this transition applies to. It takes effect before that step is simulated.</summary>
        [MetaMember(1)] public int Step { get; private set; }

        /// <summary>True for a press, false for a release.</summary>
        [MetaMember(2)] public bool Pressed { get; private set; }

        public TimeTrialInput(int step, bool pressed)
        {
            Step    = step;
            Pressed = pressed;
        }

        public override string ToString() => $"{(Pressed ? "press" : "release")}@{Step}";
    }

    /// <summary>
    /// The ordered input transitions of one attempt: everything the player did, and the whole of what the
    /// server needs to run the attempt again for itself.
    /// </summary>
    [MetaSerializable]
    public class TimeTrialRecording
    {
        [MetaMember(1)] public List<TimeTrialInput> Inputs { get; private set; } = new List<TimeTrialInput>();

        public TimeTrialRecording() { }
        public TimeTrialRecording(List<TimeTrialInput> inputs) { Inputs = inputs; }

        /// <summary>
        /// Whether the recording is one the simulation can run at all. The payload is untrusted, so this also
        /// bounds the cost of simulating it.
        /// </summary>
        public static bool IsWellFormed(TimeTrialRecording recording)
        {
            if (recording?.Inputs == null)
                return false;
            if (recording.Inputs.Count > TimeTrialConstants.MaxInputEvents)
                return false;

            int  previousStep = -1;
            bool expectPress  = true;

            foreach (TimeTrialInput input in recording.Inputs)
            {
                if (input.Pressed != expectPress)
                    return false;
                if (input.Step < previousStep)
                    return false;
                if (input.Step < 0 || input.Step >= TimeTrialConstants.MaxRunSteps)
                    return false;

                previousStep = input.Step;
                expectPress  = !expectPress;
            }

            return true;
        }
    }
}
