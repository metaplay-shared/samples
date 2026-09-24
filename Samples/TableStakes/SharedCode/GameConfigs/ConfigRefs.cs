using Metaplay.Core;
using Metaplay.Core.Config;

namespace Game.Logic
{
    /// <summary>
    /// Resolves config references whether or not the config's <see cref="MetaRef{TItem}"/>s have been resolved.
    /// </summary>
    public static class ConfigRefs
    {
        /// <summary>
        /// The item <paramref name="reference"/> names, or null when the reference is null or names no item in
        /// <paramref name="library"/>. Reports no error.
        /// <para>
        /// An imported config has its references resolved. A config built in a test is not imported, so its
        /// references are not resolved and the item is looked up by key instead.
        /// </para>
        /// </summary>
        public static TInfo Resolve<TKey, TInfo>(MetaRef<TInfo> reference, IGameConfigLibrary<TKey, TInfo> library)
            where TInfo : class, IGameConfigData<TKey> =>
            reference == null      ? null
          : reference.IsResolved   ? reference.Ref
          :                          library?.GetValueOrDefault((TKey)reference.KeyObject);
    }
}
