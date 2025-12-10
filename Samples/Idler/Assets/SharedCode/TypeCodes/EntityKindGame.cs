// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;

namespace Game.Logic
{
    /// <summary>
    /// Registry for game-specific <see cref="EntityKind"/>s.
    /// </summary>
    [EntityKindRegistry(30, 50)] // Legacy range from when only 64 values were supported
    [EntityKindRegistry(100, 300)] // New range now that 1024 values are supported
    public static class EntityKindGame
    {
        // EntityKinds introduced before Release 29 still use the old range:
        public static readonly EntityKind AsyncMatchmaker = EntityKind.FromValue(30);

        // An example from the new range:
        public static readonly EntityKind Party = EntityKind.FromValue(100);

        // An example of an illegal value (outside the ranges specified):
        //public static readonly EntityKind Illegal = EntityKind.FromValue(80);
    }
}
