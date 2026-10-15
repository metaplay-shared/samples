using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// Hooks the server-side PlayerActor can implement to react to player actions (e.g. to talk to other
    /// entities or trigger side effects). Add methods as your game needs them.
    /// </summary>
    public interface IPlayerModelServerListener
    {
    }

    /// <summary>
    /// Hooks the client can implement to react to model changes (typically to re-render the UI).
    /// </summary>
    public interface IPlayerModelClientListener
    {
        /// <summary>
        /// Triggered when a new attempt is opened, with the field it is raced against. The lineup is null or
        /// empty for a practice run, which is raced against nobody and placed nowhere.
        /// </summary>
        void OnTimeTrialStarted(TimeTrialTicket ticket, IReadOnlyList<TimeTrialGhost> lineup);

        /// <summary> Triggered by <see cref="PlayerAcceptTimeTrialResult"/> when the server's result is recorded. </summary>
        void OnTimeTrialAccepted(CourseId courseId, TimeTrialResult result, bool isNewBest, TimeTrialStanding standing);

        /// <summary> Triggered by <see cref="PlayerRejectTimeTrialResult"/> when a submission was not counted. </summary>
        void OnTimeTrialRejected(int ticketId, TimeTrialRejectReason reason);
    }

    public class EmptyPlayerModelServerListener : IPlayerModelServerListener
    {
        public static readonly EmptyPlayerModelServerListener Instance = new EmptyPlayerModelServerListener();
    }

    public class EmptyPlayerModelClientListener : IPlayerModelClientListener
    {
        public static readonly EmptyPlayerModelClientListener Instance = new EmptyPlayerModelClientListener();

        public void OnTimeTrialStarted(TimeTrialTicket ticket, IReadOnlyList<TimeTrialGhost> lineup) { }
        public void OnTimeTrialAccepted(CourseId courseId, TimeTrialResult result, bool isNewBest, TimeTrialStanding standing) { }
        public void OnTimeTrialRejected(int ticketId, TimeTrialRejectReason reason) { }
    }
}
