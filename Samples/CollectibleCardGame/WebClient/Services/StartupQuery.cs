using Microsoft.AspNetCore.Components;

namespace WebClient.Services;

/// <summary>
/// The query the page was loaded with, read once, when the first page is drawn. The game's development and test
/// switches (<c>?dev=</c>, <c>?beat=</c>, <c>?motion=</c>, <c>?stale=</c>, <c>?poll=</c>) are read from it, so they hold
/// for the whole visit: in-app navigation does not keep them in the address.
/// </summary>
public sealed class StartupQuery
{
    readonly List<(string Key, string Value)> _pairs = new List<(string Key, string Value)>();

    public StartupQuery(NavigationManager navigation)
    {
        foreach (string pair in new Uri(navigation.Uri).Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = pair.IndexOf('=');
            string key = equals > 0 ? pair[..equals] : pair;
            string value = equals > 0 ? Uri.UnescapeDataString(pair[(equals + 1)..]) : "";
            _pairs.Add((key, value));
        }
    }

    /// <summary> Every parameter, in order, with an empty value for a parameter given without one. </summary>
    public IReadOnlyList<(string Key, string Value)> Pairs => _pairs;

    /// <summary> Whether the query carried <paramref name="key"/>=<paramref name="value"/>, such as <c>dev=ranks</c>. </summary>
    public bool Has(string key, string value) => _pairs.Contains((key, value));

    /// <summary> The value of the first <paramref name="key"/>, or null when the query has none. </summary>
    public string? Value(string key)
    {
        foreach ((string pairKey, string pairValue) in _pairs)
        {
            if (pairKey == key)
                return pairValue;
        }

        return null;
    }
}
