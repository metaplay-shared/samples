using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace WebClient.Tests;

/// <summary>
/// Reports whether the client under test is still running, and collects what it logged.
/// <para>
/// Blazor reveals <c>#blazor-error-ui</c> when an unhandled exception reaches the renderer, and the app stops
/// rendering after that. Playwright assertions do not notice, so a test can pass with a dead client and a later
/// test then fails with an unrelated locator timeout. A test during which the error bar was revealed is
/// treated as failed.
/// </para>
/// </summary>
public static class ClientHealth
{
    /// <summary>
    /// Returns whether Blazor has revealed its unhandled-error bar on the page.
    /// <para>
    /// Blazor reveals the bar by writing an inline <c>display</c> style onto the element, so this reads the
    /// inline style, not the computed style. The bar is hidden by a stylesheet
    /// (<c>WebClientBase/wwwroot/css/app-base.css</c>). If that stylesheet failed to load, the computed style
    /// would show the bar on every page and every test would report a dead client.
    /// </para>
    /// </summary>
    public static async Task<bool> ErrorBarRevealedAsync(IPage? page)
    {
        if (page == null)
            return false;

        try
        {
            return await page.EvaluateAsync<bool>(
                "() => { const el = document.getElementById('blazor-error-ui');" +
                " return !!el && el.style.display !== '' && el.style.display !== 'none'; }");
        }
        catch (Exception)
        {
            // This runs in teardown. An exception here would replace the test's own failure, and a closed page or
            // a browser that never started says nothing about the client, so report no error bar.
            return false;
        }
    }

    /// <summary>
    /// Collects a page's console errors and warnings, page exceptions, and failed requests for the life of the
    /// page. Each test gets its own fixture instance and so its own log.
    /// </summary>
    public sealed class Log
    {
        private readonly List<string> _entries = new List<string>();

        /// <summary>Starts collecting from <paramref name="page"/>. The handlers only append, so they cannot fail a test.</summary>
        public static Log AttachTo(IPage page)
        {
            Log log = new Log();

            page.Console += (_, message) =>
            {
                if (message.Type == "error" || message.Type == "warning")
                    log.Add($"console.{message.Type}: {message.Text}");
            };
            page.PageError += (_, error) => log.Add($"pageerror: {error}");
            page.RequestFailed += (_, request) => log.Add($"requestfailed: {request.Url} ({request.Failure})");

            return log;
        }

        private void Add(string entry)
        {
            lock (_entries)
            {
                // A client in an error loop can log thousands of identical lines, so the log keeps only the first
                // MaxEntries. The first error usually explains the rest.
                if (_entries.Count < MaxEntries)
                    _entries.Add(entry);
            }
        }

        /// <summary>The collected entries, oldest first. Empty if the page logged nothing.</summary>
        public IReadOnlyList<string> Entries
        {
            get { lock (_entries) { return _entries.ToArray(); } }
        }

        private const int MaxEntries = 40;
    }
}
