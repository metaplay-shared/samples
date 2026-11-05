using Microsoft.Playwright;

namespace WebClient.Tests;

/// <summary>What the client in one page logged during a test, and whether it stopped with Blazor's error bar.</summary>
internal sealed class ClientHealth
{
    // A client in an error loop logs the same lines over and over. The first ones usually explain the rest.
    private const int MaxEntries = 40;

    private readonly List<string> _entries = new List<string>();

    private ClientHealth(IPage page) => Page = page;

    public IPage Page { get; }

    /// <summary>Whether the test expects this client to stop, so the error bar on it is no failure.</summary>
    public bool ErrorBarExpected { get; set; }

    /// <summary>Start collecting the page's console errors and warnings, page errors and failed requests.</summary>
    public static ClientHealth Watch(IPage page)
    {
        ClientHealth health = new ClientHealth(page);
        page.Console += (_, message) =>
        {
            if (message.Type == "error" || message.Type == "warning")
                health.Add($"console.{message.Type}: {message.Text}");
        };
        page.PageError += (_, error) => health.Add($"pageerror: {error}");
        page.RequestFailed += (_, request) => health.Add($"requestfailed: {request.Url} ({request.Failure})");
        return health;
    }

    public IReadOnlyList<string> Entries
    {
        get { lock (_entries) return _entries.ToArray(); }
    }

    /// <summary>
    /// Whether Blazor revealed its error bar, which it does when an unhandled exception stops the app from rendering.
    /// </summary>
    public async Task<bool> ErrorBarShownAsync()
    {
        try
        {
            // Blazor reveals the bar with an inline style. The computed style would show the bar on any page whose
            // stylesheet failed to load, which says nothing about the client.
            return await Page.EvaluateAsync<bool>(
                "() => { const bar = document.getElementById('blazor-error-ui');" +
                " return !!bar && bar.style.display !== '' && bar.style.display !== 'none'; }");
        }
        catch (Exception)
        {
            // This runs after the test, where an exception would replace the test's own failure. A closed page or a
            // browser that never started says nothing about the client either.
            return false;
        }
    }

    private void Add(string entry)
    {
        lock (_entries)
        {
            if (_entries.Count < MaxEntries)
                _entries.Add(entry);
        }
    }
}
