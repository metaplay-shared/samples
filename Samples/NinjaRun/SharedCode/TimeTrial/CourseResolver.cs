namespace Game.Logic
{
    /// <summary>
    /// The one place a course id becomes a course. Everything that holds an id rather than geometry (a ticket,
    /// a stored personal best, a ghost pool's own name) comes through here, so client and server turn the same
    /// id into the same course.
    /// </summary>
    public static class CourseResolver
    {
        /// <summary>
        /// The course a ticket, a best result or a pool names: the map composed from the parts in the published
        /// config. Null when the id names no map those parts compose, and without a config.
        /// <para>
        /// A composition is built afresh on every call, from the archive the caller's session is pinned to.
        /// That is what keeps a client and the server composing the same geometry: a cached course would outlive the config it was composed from.
        /// </para>
        /// </summary>
        public static CourseDefinition TryResolve(CourseId courseId, SharedGameConfig config)
        {
            if (config == null)
                return null;

            if (CourseComposer.TryCompose(courseId, config, out CourseDefinition composed))
                return composed;

            return null;
        }

        /// <summary>
        /// The course an attempt is played on: the one its id names, or, for a ticket opened on a part editor
        /// draft, the draft it carries composed alone: a draft's id names nothing on its own, so the geometry
        /// travels on the ticket and this is the one place it is read back. Both the run screen and the server's
        /// recomputation of a recording come through here, so both compose the same course from the same ticket.
        /// Null when the id names nothing and there is no draft, and for a draft that composes into nothing.
        /// </summary>
        public static CourseDefinition TryResolve(TimeTrialTicket ticket, SharedGameConfig config)
        {
            if (ticket == null)
                return null;

            if (ticket.DraftPart != null)
                return ticket.DraftPart.TryComposeAlone(config?.Global);

            return TryResolve(ticket.CourseId, config);
        }
    }
}
