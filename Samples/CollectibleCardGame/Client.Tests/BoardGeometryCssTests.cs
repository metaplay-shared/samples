using System.Text;
using Game.Client.Components.Board;
using Microsoft.Playwright.NUnit;

namespace Game.Client.Tests;

/// <summary> Checks the rendered hand identifiers and portrait Den at full and compact board sizes. </summary>
[TestFixture]
public sealed class BoardGeometryCssTests : PageTest
{
    [TestCase(1280, 720, 2)]
    [TestCase(1280, 720, 10)]
    [TestCase(640, 360, 2)]
    [TestCase(640, 360, 10)]
    public async Task FanBadgesRemainSeparateAndVisible(int width, int height, int count)
    {
        await Page.SetViewportSizeAsync(width, height);
        StringBuilder markup = new StringBuilder("<div class='board-stage'><div class='board-frame'><div class='hand'>");
        List<HandCardPlacement> placements = HandFanGeometry.Layout(count);
        for (int ndx = 0; ndx < placements.Count; ndx++)
        {
            HandCardPlacement place = placements[ndx];
            markup.Append(FormattableString.Invariant(
                $"<div class='handcard' style='--fan-x:{place.OffsetX};--fan-y:{place.OffsetY};--fan-rot:{place.RotationDeg}deg;z-index:{ndx + 1}'>"));
            markup.Append("<div class='card-face'><span class='card-face-cost'>3</span><img class='card-face-crest' /></div></div>");
        }
        markup.Append("</div></div></div>");
        await Page.SetContentAsync(markup.ToString());
        await AddStylesAsync();
        await Page.Mouse.MoveAsync(0, 0);
        await Page.EvaluateAsync(
            "() => Promise.all(document.getAnimations().filter(animation => "
            + "animation.effect.getTiming().iterations !== Infinity).map(animation => animation.finished.catch(() => {})))");

        string[] errors = await Page.Locator(".hand").EvaluateAsync<string[]>(
            """
            hand => {
                const badges = [...hand.querySelectorAll('.card-face-cost, .card-face-crest')];
                const errors = [];
                for (let i = 0; i < badges.length; i++) {
                    const box = badges[i].getBoundingClientRect();
                    const top = document.elementFromPoint(box.x + box.width / 2, box.y + box.height / 2);
                    if (top !== badges[i]) errors.push(`Badge ${i} is covered`);
                    for (let j = i + 1; j < badges.length; j++) {
                        const other = badges[j].getBoundingClientRect();
                        if (box.left < other.right && box.right > other.left &&
                            box.top < other.bottom && box.bottom > other.top)
                            errors.push(`Badges ${i} and ${j} overlap`);
                    }
                }
                return errors;
            }
            """);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
    }

    [TestCase(1280, 720)]
    [TestCase(640, 360)]
    public async Task DenArtworkFillsItsSlotAtCardHeight(int width, int height)
    {
        await Page.SetViewportSizeAsync(width, height);
        await Page.SetContentAsync(
            "<div class='board-stage'><div class='board-frame'><div class='battlefield-row battlefield-row-mine'>"
            + $"<div class='den'><div class='den-shell'><img class='den-art' src='{DenArtDataUri()}' /></div></div>"
            + "<div class='card-face'></div>"
            + "</div></div></div>");
        await AddStylesAsync();

        // The raster has to be decoded before naturalWidth means anything.
        await Page.Locator(".den-art").EvaluateAsync("image => image.decode()");

        double[] geometry = await Page.Locator(".den").EvaluateAsync<double[]>(
            """
            den => {
                const box = den.getBoundingClientRect();
                const card = document.querySelector('.card-face').getBoundingClientRect();
                const art = den.querySelector('.den-art');
                const artBox = art.getBoundingClientRect();
                return [box.width / box.height, box.height - card.height,
                    artBox.width - den.clientWidth, artBox.height - den.clientHeight,
                    artBox.left - box.left - den.clientLeft, artBox.top - box.top - den.clientTop,
                    art.naturalWidth / art.naturalHeight,
                    // The content box, which is what the image is laid into: `.den` is border-box with a
                    // border, so its border-box ratio is a hair off the one `object-fit` actually resolves
                    // against.
                    den.clientWidth / den.clientHeight];
            }
            """);
        Assert.Multiple(() =>
        {
            Assert.That(geometry[0], Is.EqualTo(1.8 * 24 / 35).Within(.015));
            Assert.That(geometry[1], Is.EqualTo(0).Within(2.1));
            foreach (double difference in geometry.Skip(2).Take(4))
                Assert.That(difference, Is.EqualTo(0).Within(1));

            // The one assertion that can see a stretched arch. `getBoundingClientRect` on an <img> returns
            // the *element* box, which `object-fit` does not change — so the four deltas above are blind to
            // the only property that decides whether the art is distorted. The raster is painted at the
            // box's own aspect, so the two ratios must agree; if they drift,
            // `contain` letterboxes rather than stretching, and this is what says so.
            Assert.That(geometry[6], Is.EqualTo(geometry[7]).Within(.01),
                $"the Den raster's aspect {geometry[6]:0.###} does not match the content box it is drawn into {geometry[7]:0.###}");
        });
        await Expect(Page.Locator(".den-art")).ToHaveCSSAsync("object-fit", "contain");
    }

    /// <summary>
    /// The real Den raster, inlined. This fixture serves no files — it builds its own markup — so the only
    /// way to measure the shipped image's own aspect is to carry its bytes in.
    /// </summary>
    static string DenArtDataUri()
    {
        string path = Path.GetFullPath(Path.Combine(
            TestContext.CurrentContext.TestDirectory, "../../../../Client/wwwroot/art/den.webp"));
        return "data:image/webp;base64," + Convert.ToBase64String(File.ReadAllBytes(path));
    }

    async Task AddStylesAsync()
    {
        foreach (string name in new[] { "board.css", "cards.css" })
        {
            string path = Path.GetFullPath(Path.Combine(
                TestContext.CurrentContext.TestDirectory, $"../../../../Client/wwwroot/{name}"));
            await Page.AddStyleTagAsync(new() { Content = await File.ReadAllTextAsync(path) });
        }
    }

    [TestCase(1280, 720)]
    [TestCase(640, 360)]
    public async Task EveryBattlefieldGapMatchesIncludingBothSidesOfTheDen(int width, int height)
    {
        await Page.SetViewportSizeAsync(width, height);
        StringBuilder markup = new StringBuilder("<div class='board-stage'><div class='board-frame'><div class='battlefield-row'>");
        markup.Append("<div class='den'><div class='den-panel'><div class='den-hp'><b>123</b><small>/ 125</small></div></div></div>");
        foreach (string slot in new[] { "left-outer", "left-far", "left-near", "right-near", "right-far", "right-outer" })
            markup.Append($"<div class='critter-slot critter-slot-{slot}'><div class='card-face'></div></div>");
        markup.Append("</div></div></div>");
        await Page.SetContentAsync(markup.ToString());
        await AddStylesAsync();
        await Page.EvaluateAsync("() => Promise.all(document.getAnimations().map(animation => animation.finished.catch(() => {})))");
        double[] gaps = await Page.EvaluateAsync<double[]>("""
            () => {
                const boxes = [...document.querySelectorAll('.critter-slot, .den')]
                    .map(node => node.getBoundingClientRect()).sort((a,b) => a.left - b.left);
                return boxes.slice(1).map((box, index) => box.left - boxes[index].right);
            }
            """);
        Assert.That(gaps.Max() - gaps.Min(), Is.LessThan(1));
        Assert.That(gaps.Min(), Is.GreaterThan(0));
        await Expect(Page.Locator(".den-hp")).ToHaveCSSAsync("white-space", "nowrap");
    }

    /// <summary>
    /// The Den's hit points are three digits over three digits since the stat domain was widened, and
    /// the line that draws them never wraps — so what does not fit the Den's own panel spills over the
    /// artwork rather than shrinking, which is what the 2026-09-09 playtest saw.
    /// <para>
    /// This renders the whole line the board renders — the health icon's box, the big value, the demoted
    /// maximum, and above it the name row with a display name too long for the panel and both seat marks —
    /// because a fixture that measured the digits alone under-measures the line by the icon's width and its
    /// gap, and said the spilling line fitted. It also asks for **8 % of the panel** in hand rather than
    /// zero: the app draws its text in Inter (declared on <c>*</c> in <c>ClientBase</c>'s <c>App.razor</c>), a
    /// web font this fixture serves no files for and therefore does not measure, so a line that merely *just*
    /// fitted here would prove nothing about the one on screen. <c>BoardReadabilityTests</c> is where the real
    /// Inter is measured.
    /// </para>
    /// <para>
    /// 1280 x 1024 is not a device but a shape: a window narrower than the board's own aspect, where the box
    /// is sized by the frame's width and the text by the viewport's height. That is what the line spilled at,
    /// and it is also a real monitor. 640 x 360 is the other side of
    /// <c>@media (max-width: 720px), (max-height: 460px)</c>, where the compact branch takes the card width
    /// from 7.5 cqw to 8.6.
    /// </para>
    /// </summary>
    [TestCase(1280, 720)]
    [TestCase(1440, 900)]
    [TestCase(1280, 1024)]
    [TestCase(640, 360)]
    public async Task ThreeDigitDenHitPointsFitTheDenPanel(int width, int height)
    {
        await Page.SetViewportSizeAsync(width, height);
        await Page.SetContentAsync(DenPanelMarkup("125"));
        await AddStylesAsync();

        double[] fit = await Page.Locator(".den-hp").EvaluateAsync<double[]>(
            """
            hp => {
                const panel = hp.closest('.den-panel').getBoundingClientRect();
                const box = hp.getBoundingClientRect();
                const who = hp.closest('.den-panel').querySelector('.den-who');
                const name = who.querySelector('.den-name');
                const marks = [...who.querySelectorAll('.den-bot, .den-away')];
                return [box.width - panel.width, hp.scrollWidth - hp.clientWidth,
                    panel.width, hp.querySelector('img').getBoundingClientRect().width,
                    who.getBoundingClientRect().width - panel.width,
                    name.scrollWidth - name.clientWidth,
                    Math.min(...marks.map(mark => mark.getBoundingClientRect().width))];
            }
            """);

        double margin = fit[2] * 0.08;
        Assert.Multiple(() =>
        {
            Assert.That(fit[0], Is.LessThanOrEqualTo(-margin),
                $"the hit-point line leaves {-fit[0]:0.#}px of a {fit[2]:0.#}px Den panel in hand, and needs at "
                + $"least {margin:0.#}px, because the font it is really drawn in is not the one measured here");
            Assert.That(fit[1], Is.LessThanOrEqualTo(0), "the hit-point line is overflowing its own box");
            Assert.That(fit[3], Is.GreaterThan(1), "the health icon has been squeezed out of the line");

            // The name row carries whatever a player called themselves, so it is the name that gives way:
            // it ellipsizes inside the panel and the two marks keep their glyphs.
            Assert.That(fit[4], Is.LessThanOrEqualTo(1), $"the name row is {fit[4]:0.#}px wider than the panel");
            Assert.That(fit[5], Is.GreaterThan(0), "a display name too long for the panel was not clipped");
            Assert.That(fit[6], Is.GreaterThan(1), "a seat mark has been squeezed out of the name row");
        });
    }

    /// <summary>
    /// Losing hit points must not shift the line under the eye: 125 and 111 are the same width, because the
    /// figures are tabular. Asserted as a width rather than as a declared property, since a font without
    /// tabular figures would satisfy the declaration and still jiggle.
    /// </summary>
    [Test]
    public async Task DenHitPointsDoNotShiftAsTheyChange()
    {
        await Page.SetViewportSizeAsync(1280, 720);
        double[] widths = new double[2];
        foreach ((int ndx, string value) in new[] { (0, "125"), (1, "111") })
        {
            await Page.SetContentAsync(DenPanelMarkup(value));
            await AddStylesAsync();
            widths[ndx] = await Page.Locator(".den-hp b").EvaluateAsync<double>(
                "value => value.getBoundingClientRect().width");
        }

        Assert.That(widths[1], Is.EqualTo(widths[0]).Within(0.5),
            $"'111' is {widths[1]:0.##}px where '125' is {widths[0]:0.##}px, so the number moves as it changes");
    }

    /// <summary>
    /// One Den, drawn the way <c>DenDoor</c> draws it: the same classes, the same order, a display name
    /// longer than the panel and both seat marks. The icon carries a transparent pixel rather than the
    /// shipped raster because only its CSS box is under test, and <c>body</c> carries the stack the app
    /// declares on <c>*</c> — the *stack*, not the font: a page built with <c>SetContentAsync</c> serves no
    /// files, so Inter's own file cannot be fetched here and the first fallback is what gets measured.
    /// </summary>
    static string DenPanelMarkup(string hitPoints)
        => "<body style=\"font-family: Inter, system-ui, sans-serif\">"
        + "<div class='board-stage'><div class='board-frame'><div class='battlefield-row battlefield-row-mine'>"
        + "<div class='den'><div class='den-shell'><img class='den-art' />"
        + "<div class='den-panel'>"
        + "<div class='den-who'><span class='den-name'>Whiskerbottom the Third</span>"
        + "<span class='den-bot'>\U0001F916</span><span class='den-away'>…</span></div>"
        + $"<div class='den-hp'><img src='{TransparentPixel}' /><b>{hitPoints}</b><small>/ 125</small></div>"
        + "</div></div></div>"
        + "</div></div></div></body>";

    const string TransparentPixel =
        "data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7";

    /// <summary>
    /// Critter stats are two digits in the widened domain — a rank-5 Old Mossback is a 37/42 — and they are
    /// drawn into fixed-size badges whose text is merely centred, so a number too wide for its badge spills
    /// over the card art rather than being clipped or shrunk. Checked at the compact size too, where the
    /// badge is smallest.
    /// </summary>
    [TestCase(1280, 720)]
    [TestCase(640, 360)]
    public async Task TwoDigitCritterStatsFitTheirBadges(int width, int height)
    {
        await Page.SetViewportSizeAsync(width, height);
        await Page.SetContentAsync(
            "<div class='board-stage'><div class='board-frame'><div class='battlefield-row battlefield-row-mine'>"
            + "<div class='critter-slot critter-slot-left-near'><div class='card-face'>"
            + "<span class='card-face-cost'>7</span>"
            + "<span class='card-face-atk'>37</span><span class='card-face-hp'>42</span>"
            + "</div></div></div></div></div>");
        await AddStylesAsync();

        double[] overflow = await Page.EvaluateAsync<double[]>(
            """
            () => [...document.querySelectorAll('.card-face-atk, .card-face-hp, .card-face-cost')]
                .map(badge => badge.scrollWidth - badge.clientWidth)
            """);

        Assert.That(overflow, Is.Not.Empty);
        foreach (double spill in overflow)
            Assert.That(spill, Is.LessThanOrEqualTo(0), "a stat badge is narrower than the number in it");
    }
}
