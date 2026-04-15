# Elysium Wallpaper

A Windows desktop wallpaper app that finds, curates, and rotates wallpapers for you — search the web, build collections, match the time of day, or assign different images per monitor. Runs quietly in the system tray.

![Elysium Wallpaper](docs/banner.png)

## What it does

- **Time-of-day rotation.** Splits the day into four user-editable slots (morning / noon / evening / night) and applies a wallpaper whose lighting actually fits the current slot. Per-slot luminance + histogram gates mean a midday beach never sneaks into the 2am slot.
- **Rotate however you want.** Or switch modes: change every 5 / 15 / 30 minutes / 1h / 2h / 4h / 8h — rotating from your favorites, downloaded library, and an optional local folder (recursive). Recently-applied wallpapers are tracked so you don't see the same one twice in a row.
- **Search the web.** Tag search across [Pexels](https://www.pexels.com/) and [Openverse](https://openverse.org/) (commercial-use, modification-allowed filter), with orientation / color / aspect filters, paginated grid / list / compact views, and full-screen preview before you commit.
- **Favorites, collections, history.** Bookmark any image, group favorites into named collections, play through a collection in order, and see every wallpaper the engine has applied with one-click reapply.
- **Per-monitor assignment.** On multi-monitor setups, fetch one aspect-matched image per display and apply each to its target with a single click.
- **Spec-aware defaults.** Reads your CPU, GPU, RAM, and display topology on launch and pre-populates min-resolution, layout mode (Fill / Fit / Stretch / Center / Span), and thread budget without you touching anything.
- **Not this one? Reroll.** Don't like the current pick? The tray menu and the "Not This One" button pull a fresh slot-appropriate image and force an apply.
- **Lives in your tray.** Close-to-tray with a right-click menu: Show, Next wallpaper, Previous wallpaper, Start / Stop engine, Exit. Optional run-on-startup.
- **Your library is yours.** Favorites, history, and downloaded images live in `%LocalAppData%` and survive clean reinstalls. Export / import your profile to move everything to another machine.
- **Stays current.** On launch, checks GitHub for a newer version and offers one-click download if there is one.
- **Crash-safe.** If the app ever dies unexpectedly, the next launch offers a banner to open or copy the crash log so you can tell me what went wrong.

## Install

1. Download `ElysiumWallpaper-win-x64.zip` from [Releases](https://github.com/rps321321/elysium-wallpaper/releases).
2. Unzip anywhere — it's self-contained, no .NET install required.
3. Double-click `ElysiumWallpaper.exe`.

## Quick start

1. Type a tag (`mountains`, `nebula`, `forest`, anything) into Search.
2. Press **Enter** or click **Search**. Browse the grid, click any thumbnail to preview, click **Apply** to set as wallpaper or **Save** to bookmark.
3. Click **Start Engine** in the right sidebar to enable automatic time-of-day rotation.
4. Optionally close the window — the app minimizes to the tray and keeps rotating.

The slot times (morning/noon/evening/night) are editable in **Settings**. Default boundaries: 06:00 / 12:00 / 17:00 / 20:00.

### Keyboard shortcuts
- **Ctrl + K** — focus the search box
- **Ctrl + R** — reroll the current wallpaper
- **F5** — fetch fresh wallpapers now
- **Enter / Esc** — submit / clear search

## Tag search (optional Pexels API key)

The auto-rotation engine works out of the box using [Openverse](https://openverse.org/) for image fetching — no setup needed. **Tag search** and **per-monitor auto-assign** additionally need a free [Pexels](https://www.pexels.com/api/) API key.

To enable them:

1. Sign up at [pexels.com/api](https://www.pexels.com/api/) — the free tier (200 req/hr, 20k req/month) is plenty.
2. Provide your key one of two ways:
   - **Environment variable**: `setx ELYSIUM_PEXELS_API_KEY "your-key-here"` then restart the app.
   - **Key file**: save the key as a single line in `%LocalAppData%\ElysiumWallpaper\pexels.key`.
3. Restart the app. Tag search will start working.

Without a key, the engine still rotates wallpapers via Openverse — only tag search and per-monitor assign are gated.

## Credits

- **Photos provided by [Pexels](https://www.pexels.com/).** When you search or per-monitor-assign, the downloaded images come from Pexels' free-for-commercial-use catalogue. Photographer credit is shown on each search result card and preview.
- **Images via [Openverse](https://openverse.org/)** under their original Creative Commons licenses (CC0, CC BY, CC BY-SA). When the engine fetches from Openverse, each downloaded file is paired with a `{filename}.attribution.json` sidecar in `%LocalAppData%\ElysiumWallpaper\images\` containing the creator, license, and source URL so the original work can be properly credited.

Please respect the licenses attached to each image when sharing screenshots or redistributing.

## License

MIT — see [LICENSE](LICENSE).
