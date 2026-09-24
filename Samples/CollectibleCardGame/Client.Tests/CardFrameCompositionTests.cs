using Microsoft.Playwright;

namespace Game.Client.Tests;

/// <summary> Both live card types share one frame; only critters add the decorative stat sockets. </summary>
[TestFixture]
public sealed class CardFrameCompositionTests : OfflineTestBase
{
    [TestCase(1280, 720)]
    [TestCase(640, 360)]
    public async Task SharedFrameAndCritterSocketsLoadAndAlign(int width, int height)
    {
        await Page.SetViewportSizeAsync(width, height);
        await Page.GotoAsync($"{BaseUrl}/dev/board?scene=heal&env=offline&motion=reduced");
        await Expect(Page.GetByTestId("match-board")).ToBeVisibleAsync(new() { Timeout = BootTimeout });
        ILocator critter = Page.Locator("[data-testid='board-critter'] .card-face-critter").First;
        ILocator trick = Page.Locator("[data-testid='hand-card'] .card-face-trick").First;
        await Expect(critter).ToBeVisibleAsync();
        await Expect(trick).ToBeVisibleAsync();
        await Expect(critter.Locator(".card-face-frame")).ToHaveAttributeAsync("src", "art/card-frame-shared.webp");
        await Expect(trick.Locator(".card-face-frame")).ToHaveAttributeAsync("src", "art/card-frame-shared.webp");
        await Expect(critter.Locator(".card-face-stat-sockets")).ToHaveCountAsync(1);
        await Expect(trick.Locator(".card-face-stat-sockets")).ToHaveCountAsync(0);
        bool illustrationIsAboveFrame = await trick.EvaluateAsync<bool>(
            """
            async face => {
                const window = face.querySelector('.card-face-art-window');
                const art = window.querySelector('img');
                await art.decode();
                return art.naturalWidth > 0 && Number(getComputedStyle(window).zIndex)
                    > Number(getComputedStyle(face.querySelector('.card-face-frame')).zIndex);
            }
            """);
        Assert.That(illustrationIsAboveFrame, Is.True, "the opaque parchment must not hide the Trick illustration");

        double[] geometry = await critter.EvaluateAsync<double[]>(
            """
            async face => {
                const frame = face.querySelector('.card-face-frame');
                const sockets = face.querySelector('.card-face-stat-sockets');
                await Promise.all([frame.decode(), sockets.decode()]);
                return [frame.naturalWidth, frame.naturalHeight, sockets.naturalWidth, sockets.naturalHeight,
                    sockets.offsetLeft, sockets.offsetWidth - face.offsetWidth,
                    (sockets.offsetTop + sockets.offsetHeight - face.offsetHeight) / face.offsetHeight,
                    sockets.offsetHeight / face.offsetHeight,
                    getComputedStyle(sockets).pointerEvents === 'none' ? 1 : 0];
            }
            """);
        Assert.Multiple(() =>
        {
            Assert.That(geometry[0], Is.EqualTo(384));
            Assert.That(geometry[1], Is.EqualTo(560));
            Assert.That(geometry[2], Is.EqualTo(384));
            Assert.That(geometry[3], Is.EqualTo(140));
            Assert.That(geometry[4], Is.Zero);
            Assert.That(geometry[5], Is.EqualTo(0).Within(1));
            Assert.That(geometry[6], Is.EqualTo(.03).Within(.015));
            Assert.That(geometry[7], Is.EqualTo(.25).Within(.015));
            Assert.That(geometry[8], Is.EqualTo(1));
        });
    }
}
