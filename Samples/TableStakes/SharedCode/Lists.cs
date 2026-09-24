using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>Helpers for the lazily created lists that model state keeps as <c>[MetaMember]</c> fields.</summary>
    static class Lists
    {
        /// <summary>Adds <paramref name="item"/> unless <paramref name="list"/> already holds it, creating the list if it is null.</summary>
        internal static void AddOnce<T>(ref List<T> list, T item)
        {
            list ??= new List<T>();
            if (!list.Contains(item))
                list.Add(item);
        }
    }
}
