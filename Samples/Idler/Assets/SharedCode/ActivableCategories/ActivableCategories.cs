// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Activables;

namespace Game.Logic
{
    /// <summary>
    /// This metadata declaration lets the Metaplay SDK know about
    /// Idler's "In-Game Events" category of activables.
    ///
    /// Idler's "happy hour" and "special producer events" kinds
    /// of activables belong to this category.
    /// See <see cref="ActivableKindMetadataHappyHour"/>
    /// and <see cref="ActivableKindMetadataSpecialProducerEvent"/>.
    /// As a consequence, those types of events are both shown on
    /// the same "In-Game Events" page on the dashboard.
    /// </summary>
    [MetaActivableCategoryMetadata(
        id:             "Event",
        displayName:    "In-Game Events",
        description:    "In-game events with flexible scheduling and targeting rules.")]
    public static class ActivableCategoryMetadataEvent
    {
    }
}
