using Game.Logic;
using Game.Server.CourseGhosts;
using Metaplay.Core;
using Metaplay.Core.Model;
using System;
using System.Collections.Generic;

namespace Game.Server.MapRegistry
{
    /// <summary>
    /// One map the registry has published, and the little the registry knows about it: when it first entered,
    /// how often it has been in the active set, and when it was last put there. Everything about how the map
    /// has been *run* (its par, its opponents, its spread of times) belongs to the map's own
    /// <see cref="CourseGhostPoolState"/>, which is found by hashing the map id rather than by looking here.
    /// <para>
    /// A map is never removed, so a map that leaves the active set keeps its entry and comes back with its
    /// history intact.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public class MapRegistryEntry
    {
        /// <summary>The composition this entry is for: the three part ids joined into a map id.</summary>
        [MetaMember(1)] public CourseId MapId { get; private set; }

        /// <summary>When the map entered the registry.</summary>
        [MetaMember(2)] public MetaTime PublishedAt { get; private set; }

        /// <summary>How many times the map has entered the active set, its first activation included.</summary>
        [MetaMember(3)] public int TimesActivated { get; private set; }

        /// <summary>When the map last entered the active set. This is what decides which map leaves next.</summary>
        [MetaMember(4)] public MetaTime LastActivatedAt { get; private set; }

        public MapRegistryEntry() { }

        public MapRegistryEntry(CourseId mapId, MetaTime publishedAt)
        {
            MapId       = mapId;
            PublishedAt = publishedAt;
        }

        /// <summary>Count one entry into the active set against this map.</summary>
        public void MarkActivated(MetaTime now)
        {
            TimesActivated++;
            LastActivatedAt = now;
        }
    }

    /// <summary>
    /// Every map the server has ever published, and the handful of them a run may be started on right now.
    /// This is the state <see cref="MapRegistryActor"/> owns and persists.
    /// <para>
    /// A map is an ordered triplet of course parts and its id is that triplet, so the registry stores ids and
    /// nothing else: what a map looks like comes from composing it against the published config, and what has
    /// been run on it comes from its own ghost pool. That is what lets a map leave the active set and return
    /// months later to the times and recordings it already had.
    /// </para>
    /// <para>
    /// Rotation is one map out and one map in, on the clock, and it is persisted. A server restart does not
    /// reshuffle the set, and however long the server was down it catches up with a single rotation.
    /// </para>
    /// </summary>
    [MetaSerializable]
    [SupportedSchemaVersions(1, 1)]
    public class MapRegistryState : ISchemaMigratable
    {
        /// <summary>
        /// How many maps a run may be started on at once. Enough for variety between runs, and few enough that
        /// each one is played often enough to gather a field.
        /// </summary>
        public const int ActiveSetSize = 4;

        /// <summary>How many maps the registry publishes when it first starts, one more than it activates.</summary>
        public const int InitialMapCount = 5;

        /// <summary>Every map ever published, in publication order. This list never shrinks.</summary>
        [MetaMember(1)] public List<MapRegistryEntry> Maps { get; private set; } = new List<MapRegistryEntry>();

        /// <summary>
        /// The maps a run may be started on right now: <see cref="ActiveSetSize"/> of them whenever the parts
        /// in the config allow that many.
        /// </summary>
        [MetaMember(2)] public List<CourseId> ActiveMapIds { get; private set; } = new List<CourseId>();

        /// <summary>When the active set last changed on the rotation clock.</summary>
        [MetaMember(3)] public MetaTime LastRotatedAt { get; private set; }

        /// <summary>
        /// The <see cref="TimeTrialConstants.RulesetVersion"/> the maps in the active set were gated under. A
        /// bump means every one of them is driven again before it is served, because a map the runners could
        /// finish under the old movement values is not necessarily one they can finish under the new ones.
        /// </summary>
        [MetaMember(4)] public int RulesetVersion { get; private set; }

        public MapRegistryState() { }

        /// <summary>Whether the registry has published anything yet.</summary>
        public bool IsInitialized => Maps.Count > 0;

        /// <summary>Whether a run may be started on this map right now.</summary>
        public bool IsActive(CourseId mapId) => mapId != null && ActiveMapIds.Contains(mapId);

        /// <summary>What the registry knows about a map, or null for one it has never published.</summary>
        public MapRegistryEntry TryGetEntry(CourseId mapId)
        {
            if (mapId == null)
                return null;

            foreach (MapRegistryEntry entry in Maps)
            {
                if (entry.MapId == mapId)
                    return entry;
            }

            return null;
        }

        /// <summary>
        /// Publish the registry's first <see cref="InitialMapCount"/> maps, each past
        /// <see cref="PassesPublicationGate"/>, and activate the first <see cref="ActiveSetSize"/> of them.
        /// </summary>
        public void Initialize(MetaTime now, SharedGameConfig config)
        {
            Maps         = new List<MapRegistryEntry>();
            ActiveMapIds = new List<CourseId>();

            IReadOnlyList<CourseId> legal = CourseComposer.LegalMapIds(config);

            for (int ndx = 0; ndx < InitialMapCount && legal.Count > 0; ndx++)
            {
                // Spread across the list rather than taken from its front: the list is enumerated part by part,
                // so its first entries all open on the same part. A candidate the gate refuses is passed over
                // for the next legal map along.
                int      start     = (int)((long)ndx * legal.Count / InitialMapCount);
                CourseId published = null;

                for (int step = 0; step < legal.Count; step++)
                {
                    CourseId candidate = legal[(start + step) % legal.Count];
                    if (TryGetEntry(candidate) != null)
                        continue;
                    if (!PassesPublicationGate(candidate, config))
                        continue;

                    published = candidate;
                    break;
                }

                if (published == null)
                    break;

                Maps.Add(new MapRegistryEntry(published, now));
            }

            for (int ndx = 0; ndx < Maps.Count && ActiveMapIds.Count < ActiveSetSize; ndx++)
                Activate(Maps[ndx].MapId, now);

            LastRotatedAt  = now;
            RulesetVersion = TimeTrialConstants.RulesetVersion;
        }

        /// <summary>Whether the active set is due to change: a full <paramref name="period"/> since it last did.</summary>
        public bool IsRotationDue(MetaTime now, MetaDuration period) => now >= LastRotatedAt + period;

        /// <summary>
        /// One rotation: the active map that has been in the set longest leaves, and one drawn entrant enters.
        /// Returns the pair, with both ids null when nothing could be drawn.
        /// </summary>
        public (CourseId Outgoing, CourseId Incoming) Rotate(MetaTime now, SharedGameConfig config)
        {
            if (!IsInitialized || config == null)
                return (null, null);

            // Drawn before anything leaves, so a map cannot leave the set and walk straight back into it.
            CourseId incoming = DrawEntrant(now, config);
            if (incoming == null)
                return (null, null);

            CourseId outgoing = null;
            if (ActiveMapIds.Count >= ActiveSetSize)
            {
                outgoing = LeastRecentlyActivated();
                if (outgoing != null)
                    ActiveMapIds.Remove(outgoing);
            }

            Activate(incoming, now);
            // The period restarts from now, so a server that was down for several periods rotates once rather
            // than replacing the whole set in a burst.
            LastRotatedAt = now;

            return (outgoing, incoming);
        }

        /// <summary>
        /// Hold the active set to what this config can serve, and fill it back up to <see cref="ActiveSetSize"/>.
        /// Returns how many maps changed.
        /// </summary>
        public int Reconcile(MetaTime now, SharedGameConfig config)
        {
            if (!IsInitialized || config == null)
                return 0;

            bool reGate = RulesetVersion != TimeTrialConstants.RulesetVersion;
            int  changed = 0;

            List<CourseId> kept = new List<CourseId>();
            foreach (CourseId mapId in ActiveMapIds)
            {
                // A config publish can retire a part an active map names, and a ruleset bump can leave the
                // scripted runners unable to finish a map they once could.
                bool holds = !kept.Contains(mapId)
                          && IsComposable(mapId, config)
                          && (!reGate || PassesPublicationGate(mapId, config));

                if (holds)
                    kept.Add(mapId);
                else
                    changed++;
            }

            ActiveMapIds   = kept;
            RulesetVersion = TimeTrialConstants.RulesetVersion;

            while (ActiveMapIds.Count < ActiveSetSize)
            {
                CourseId incoming = DrawEntrant(now, config);
                if (incoming == null)
                    break;

                Activate(incoming, now);
                changed++;
            }

            return changed;
        }

        /// <summary>
        /// The publication gate: whether every scripted runner in <see cref="TimeTrialSeedRunners.SeedLevels"/>
        /// can finish the map. A map enters the registry only if it passes.
        /// </summary>
        public static bool PassesPublicationGate(CourseId mapId, SharedGameConfig config)
        {
            // Not a map id, a part this config does not hold, or an illegal triplet: nothing to publish.
            if (!CourseComposer.TryCompose(mapId, config, out CourseDefinition course))
                return false;

            // The same drive the map's ghost pool makes when it seeds itself. The sharpest runner's finish is the
            // map's par, so a map any runner cannot finish would have no par or no full seed field.
            foreach (TimeTrialBotSkill skill in TimeTrialSeedRunners.SeedLevels)
            {
                if (TimeTrialGhost.CreateSeed(course, config.TimeTrialRules, skill) == null)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Whether this config still composes this map: the id is a map id, it names parts the config holds,
        /// and the triplet is a legal composition.
        /// </summary>
        public static bool IsComposable(CourseId mapId, SharedGameConfig config)
            => CourseComposer.TryLookUpParts(mapId, config, out List<CoursePartInfo> _, out string _);

        /// <summary>Put a map into the active set and count the activation against its entry.</summary>
        void Activate(CourseId mapId, MetaTime now)
        {
            if (mapId == null || ActiveMapIds.Contains(mapId))
                return;

            ActiveMapIds.Add(mapId);
            TryGetEntry(mapId)?.MarkActivated(now);
        }

        /// <summary>
        /// The active map that has been in the set longest. An active id the registry holds no entry for is
        /// taken as the oldest, so an inconsistency leaves the set at the first rotation rather than sticking.
        /// </summary>
        CourseId LeastRecentlyActivated()
        {
            CourseId oldest   = null;
            MetaTime oldestAt = MetaTime.Epoch;

            foreach (CourseId mapId in ActiveMapIds)
            {
                MapRegistryEntry entry = TryGetEntry(mapId);
                if (entry == null)
                    return mapId;

                if (oldest == null || entry.LastActivatedAt < oldestAt)
                {
                    oldest   = mapId;
                    oldestAt = entry.LastActivatedAt;
                }
            }

            return oldest;
        }

        /// <summary>
        /// The map to put into the active set next. The draw is seeded from the moment of the rotation and the
        /// size of the registry, so the same state rotated at the same moment always picks the same map: a
        /// rotation is replayable, and a restart mid-rotation cannot produce two different active sets.
        /// </summary>
        CourseId DrawEntrant(MetaTime now, SharedGameConfig config)
        {
            RandomPCG rng = RandomPCG.CreateFromSeed(((ulong)now.MillisecondsSinceEpoch << 8) ^ (ulong)(uint)Maps.Count);

            // Even odds between a map that has been here before and one nobody has seen.
            bool returningFirst = rng.NextInt(2) == 0;

            CourseId drawn = returningFirst ? DrawReturning(rng, config) : DrawNewlyPublished(rng, config, now);
            if (drawn != null)
                return drawn;

            // One of the two pools is empty (every legal map is already published, or nothing has left the
            // active set yet) so the other one supplies the entrant.
            return returningFirst ? DrawNewlyPublished(rng, config, now) : DrawReturning(rng, config);
        }

        /// <summary>A map the registry already holds that is not in the active set, with its history intact.</summary>
        CourseId DrawReturning(RandomPCG rng, SharedGameConfig config)
        {
            List<CourseId> candidates = new List<CourseId>();
            foreach (MapRegistryEntry entry in Maps)
            {
                if (!IsActive(entry.MapId))
                    candidates.Add(entry.MapId);
            }

            return DrawPastTheGate(candidates, rng, config);
        }

        /// <summary>
        /// A legal composition the registry has never published, appended to <see cref="Maps"/> as it is drawn.
        /// This is the only place a map is published after the registry's first start.
        /// </summary>
        CourseId DrawNewlyPublished(RandomPCG rng, SharedGameConfig config, MetaTime now)
        {
            List<CourseId> candidates = new List<CourseId>();
            foreach (CourseId mapId in CourseComposer.LegalMapIds(config))
            {
                if (TryGetEntry(mapId) == null)
                    candidates.Add(mapId);
            }

            CourseId drawn = DrawPastTheGate(candidates, rng, config);
            if (drawn == null)
                return null;

            Maps.Add(new MapRegistryEntry(drawn, now));
            return drawn;
        }

        /// <summary>
        /// One of these candidates, drawn at random and proved playable. A candidate the gate turns away is
        /// taken out of the draw and another is tried, so a single unplayable composition does not stall a
        /// rotation.
        /// </summary>
        static CourseId DrawPastTheGate(List<CourseId> candidates, RandomPCG rng, SharedGameConfig config)
        {
            while (candidates.Count > 0)
            {
                int      index     = rng.NextInt(candidates.Count);
                CourseId candidate = candidates[index];

                if (PassesPublicationGate(candidate, config))
                    return candidate;

                candidates.RemoveAt(index);
            }

            return null;
        }
    }
}
