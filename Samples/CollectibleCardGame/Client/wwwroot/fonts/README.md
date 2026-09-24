# The app's two web fonts

Self-hosted, because a client that fetches its own text metrics from the public internet draws in a substitute
whenever that fetch fails — silently, and in a font nothing measured. The board's tightest line is nine glyphs in a
panel 84 % of a door wide, and the fixture that guards it
waits for a face that has actually loaded, which is only an honest guard if the file is ours to serve.

| Family | Used for | Files | Bytes |
|---|---|---|---|
| **Inter** v20, variable | all body text — declared on `*` in `ClientBase/Components/App.razor` | 7 subsets | 213.8 KiB |
| **Lilita One** v17 | the display face, `.font-display` in `ClientBase/wwwroot/css/app-base.css` | 2 subsets | 11.8 KiB |

The `@font-face` rules are in [`../fonts.css`](../fonts.css), loaded from `index.html` before every other stylesheet.
Both families are variable fonts, so one file per subset serves every weight the app asks for (Inter at 400, 500,
600, 700 and 800; `font-weight: 900` matches Inter's 800 face and is synthesized).
Every subset Google Fonts offered is here, so a display name outside Latin still draws in Inter, and `unicode-range`
means a session downloads only the subsets its text needs — **57.7 KiB for an all-Latin session**, not 225.

Licences beside the files: `OFL-Inter.txt` (Copyright 2016 The Inter Project Authors) and `OFL-LilitaOne.txt`
(Copyright 2011 Juan Montoreano, Reserved Font Name Lilita). Both are the SIL Open Font License 1.1, which is what
lets these files be redistributed inside this repository.

## Refreshing them

The files and `../fonts.css` were derived from the stylesheet the Google Fonts CSS2 API serves a modern browser —
fetched with:

```bash
curl -A "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 Chrome/131.0.0.0 Safari/537.36" \
    "https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700;800&family=Lilita+One&display=swap"
```

That user agent is what makes the API answer in `woff2`. Each `@font-face` block in the reply was copied verbatim
into `../fonts.css` with its `src` pointed at `fonts/<family>-<subset>.woff2`, and each distinct `fonts.gstatic.com`
URL downloaded to that name. Nothing was re-encoded or subset here: the bytes are Google's.

Refresh only deliberately. A newer Inter changes text metrics, which moves every measured line on the board — do it
with `BoardGeometryCssTests` and `BoardReadabilityTests` in front of you.
