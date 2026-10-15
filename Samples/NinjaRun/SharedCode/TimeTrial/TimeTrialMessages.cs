using Metaplay.Core;

namespace Game.Logic
{
    /// <summary>
    /// Ask the server to open a run. It carries nothing: the map is the server's to pick, from the registry's
    /// active set, and the ticket it issues comes back with the opponents it selected on
    /// <see cref="PlayerOpenTimeTrial"/>.
    /// <para>
    /// A message rather than a player action because the client cannot execute the outcome: it does not know
    /// the active set, and an action that picked a map on the client would have to be re-picked on the server
    /// anyway.
    /// </para>
    /// </summary>
    [MetaMessage(MessageCodes.TimeTrialStartRequest, MessageDirection.ClientToServer), MessageRoutingRuleOwnedPlayer]
    public class TimeTrialStartRequest : MetaMessage
    {
        public TimeTrialStartRequest() { }
    }

    /// <summary>
    /// A developer's ask to race a course they named (any legal map, whether or not it
    /// is published or active) against the field that course would give a real run. The server answers the
    /// way it answers <see cref="TimeTrialStartRequest"/>, with <see cref="PlayerOpenTimeTrial"/>, except that
    /// the course is the one named here and the ticket is a practice ticket, so the race counts for nothing.
    /// <para>
    /// A message cannot be marked development-only the way an action is, so the handler checks for itself that
    /// the player is a developer or the server runs with its development features on, and turns anyone else
    /// down with nothing sent back.
    /// </para>
    /// </summary>
    [MetaMessage(MessageCodes.TimeTrialStartOnMapRequest, MessageDirection.ClientToServer), MessageRoutingRuleOwnedPlayer]
    public class TimeTrialStartOnMapRequest : MetaMessage
    {
        /// <summary>The course to race, by any id <see cref="CourseResolver"/> answers for.</summary>
        public CourseId CourseId { get; private set; }

        public TimeTrialStartOnMapRequest() { }
        public TimeTrialStartOnMapRequest(CourseId courseId) { CourseId = courseId; }
    }

    /// <summary>
    /// A finished run handed to the server: the ticket that identifies the attempt, and everything the player
    /// pressed during it. The server runs the recording through its own simulation and decides from that what
    /// the time was, so nothing here carries a result the client computed.
    /// <para>
    /// This is a message rather than a player action because the outcome is not something the client can
    /// execute: the server answers it by enqueueing <see cref="PlayerAcceptTimeTrialResult"/> or
    /// <see cref="PlayerRejectTimeTrialResult"/>. The client sends it with
    /// <c>MetaplaySDK.MessageDispatcher.SendMessage</c>.
    /// </para>
    /// </summary>
    [MetaMessage(MessageCodes.TimeTrialSubmitRequest, MessageDirection.ClientToServer), MessageRoutingRuleOwnedPlayer]
    public class TimeTrialSubmitRequest : MetaMessage
    {
        public int                TicketId  { get; private set; }
        public TimeTrialRecording Recording { get; private set; }

        public TimeTrialSubmitRequest() { }

        public TimeTrialSubmitRequest(int ticketId, TimeTrialRecording recording)
        {
            TicketId  = ticketId;
            Recording = recording;
        }
    }
}
