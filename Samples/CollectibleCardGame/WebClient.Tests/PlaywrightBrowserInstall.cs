namespace WebClient.Tests;

/// <summary>
/// Installs the browser that this version of Playwright needs before the first fixture starts, so a fresh checkout
/// needs no install step. When the browser is already installed, this only checks for it.
/// </summary>
[SetUpFixture]
public class PlaywrightBrowserInstall
{
    [OneTimeSetUp]
    public void InstallBrowser()
    {
        int exitCode = Microsoft.Playwright.Program.Main(new[] { "install", "chromium" });
        if (exitCode != 0)
            throw new InvalidOperationException(
                $"Installing the Playwright browser failed with exit code {exitCode}; its output above says why.");
    }
}
