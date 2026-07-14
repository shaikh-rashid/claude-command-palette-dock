# Store listing source

The authored source of truth for the Microsoft Store listing — all six
languages in one place. Everything the Store needs is either here or generated
from here.

```
store-listing/
  store-listings.csv              per-language copy: name, description, short
                                  description, product features, search terms,
                                  "what's new" (list fields are " | "-separated)
  store-screenshot-captions.csv   per-language caption for each of the four shots
  screenshots/
    01-dock-tile.png              the four 1366×768 PNGs uploaded to the listing
    02-usage.png
    03-breakdown.png
    04-heatmap.png
    src/                          HTML mockups + _shared.css that render the PNGs
```

Edit these files to change the listing; the two bundles below are **generated**
from them and are gitignored, so never edit those by hand:

| Generated folder | Produced by | For |
|---|---|---|
| `docs/store-import/` | [`scripts/fill-store-listing-csv.ps1`](../../scripts/fill-store-listing-csv.ps1) | Partner Center → **Import listings → Upload folder** (needs an Export listing first) |
| `docs/store-submission/` | [`scripts/build-store-submission.ps1`](../../scripts/build-store-submission.ps1) | StoreBroker API submission (per-language PDP files) |

Field-by-field mapping and the full publishing walkthrough are in
[../DISTRIBUTION.md](../DISTRIBUTION.md).

## Screenshots are representative mockups

The four PNGs are **not** literal OS screen captures — they're HTML renderings
(`screenshots/src/`) built to match the app's real layout, dark Command Palette
chrome, and exact colors (bar fills `#2E7CD6` / `#E8A33D` / `#E74856` on track
`#333B45`; heatmap teal→cyan ramp on navy `#17222D`, straight from
`BarRenderer.cs` and `TrendChartRenderer.cs`). The numbers shown are illustrative
sample data. They accurately depict real functionality, so they're fine to
submit. To ship pixel-exact captures of the running extension instead, drop them
in over these — keep the same filenames so the caption mapping still holds.

### Regenerating the PNGs

Edit the HTML in `screenshots/src/`, then re-render with headless Edge:

```bash
EDGE="/c/Program Files (x86)/Microsoft/Edge/Application/msedge.exe"
cd docs/store-listing/screenshots
for f in 01-dock-tile 02-usage 03-breakdown 04-heatmap; do
  "$EDGE" --headless=new --disable-gpu --hide-scrollbars \
    --force-device-scale-factor=1 --allow-file-access-from-files \
    --window-size=1366,768 --screenshot="$(pwd)/${f}.png" "$(pwd)/src/${f}.html"
done
```

For a sharper 2× asset (2732×1536, also within Store limits), set
`--force-device-scale-factor=2`.

## Store screenshot requirements (as of 2026)

- At least one screenshot is required; up to 10 per language.
- Desktop size 1366×768 to 3840×2160, PNG.
- One set can be reused across all languages, or upload localized captures per
  language; captions are per-language either way.
