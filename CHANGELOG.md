# Changelog

## 1.0.8 — 2026-10-08
- Settings open is much faster: coalesce refresh work, stop re-running Win32 mode/HDR probes from Overview updates, and cache display mode/HDR queries for the settings session.
- Fixed a regression where Desktop/Fullscreen mode tabs bubbled `SelectionChanged` and rebuilt every per-game/platform profile editor (very slow with many game overrides). Those editors now load only when their tab is open.
- Cache `EnumDisplaySettingsEx` (RAWMODE) results used for refresh/resolution lists — CRT/CRU setups can expose thousands of timings and were freezing Settings even after downgrading (same saved config).
- Defer building the Refresh rate / Resolution launch pages until those tabs are opened.

## 1.0.7 — 2026-10-08
- Separate **primary display** pickers for Playnite Desktop and Fullscreen (from your connected displays).
- **When a game launches** can switch between Desktop and Fullscreen to edit each mode’s HDR, refresh rate, resolution, turn-off-others, and missing-display settings.
- Entering Playnite Fullscreen always applies the Fullscreen primary (and that mode’s topology) and keeps it for the session; the old opt-in checkbox was removed.
- Connected-display cards and Overview **Displays** show **Desktop** / **Fullscreen** play primaries.
- Setup wizard applies the chosen play display to both modes (split later in Settings) and copies HDR/refresh to both.

## 1.0.6 — 2026-10-07
- Fixed the primary display for games resetting to Keep Windows default after you pick a monitor in Settings.

## 1.0.5 — 2026-10-07
- Game context menu Display list now shows custom display names (aliases), matching Settings.
- Per-game **Other displays** context menu: inherit global, turn off other displays, or keep them on (issue #1).
- Optional **Apply primary display layout when entering fullscreen**: switches to the preferred play display (and turn-off-others / missing-display fallback) when fullscreen starts, keeps that layout across games, and restores the desktop layout when returning to desktop mode.
- Relocate-Playnite copy uses fullscreen/desktop wording instead of the English product names.
- Russian (`ru_RU`) localization is included with the other community languages.

## 1.0.4 — 2026-10-02
- Added a dedicated support log file under plugin user data so users can share diagnostics when something fails.
- Structured the support log like a readable session diary: `SESSION begin/end`, fixed topics (`session.plan`, `topology.apply`, `hdr.apply`, …), levels, and detail dumps on warn/error (or verbose).
- Debug log controls live under Advanced → Maintenance (open / clear / verbose), matching Metadata AI’s maintenance pattern.

## 1.0.3 — 2026-10-02
- Added global HDR policy to defer to Playnite’s native per-game Enable HDR setting (NX does not write HDR or clear the flag).
- Show HDR metadata match names only when the metadata policy is selected.
- Refresh rate Exact options use a ComboBox (like resolution) instead of a long radio list, in Settings and the setup wizard.
- Promoted **When a game launches** to a top-level settings tab (next to Displays) so HDR, refresh rate, resolution, and missing-display are easier to reach.
- Use the same left-side navigation for **When a game launches** sub-pages (HDR, refresh rate, resolution, missing display).

## 1.0.2 — 2026-09-30
- When you change the preferred display for games, options that do not work on that screen (exact resolution, refresh rate, or Force HDR) are reset automatically.
- A clear dialog shows what was changed, with before and after values, on the same screen where Playnite is open.
- The game context menu now uses the same preferred display as Settings, so custom resolutions appear when they should.

## 1.0.1 — 2026-09-30
- Enumerate driver custom timings (AMD/NVIDIA/CRU) via EnumDisplaySettingsEx EDS_RAWMODE.
- Group Exact resolutions into From monitor vs Additional (non-EDID), styled like Controller Manager Looks.
- Add a refresh control for the resolution list and keep unavailable Exact picks visible without breaking game launch.

## 1.0.0 — 2026-09-20
- Applied Windows display topology and optional refresh rate when a game starts, with RestoreHost lease restore on stop, cancel, crash, or Playnite exit.
- Owned HDR for game sessions: global policies (leave alone / always on / Features+Tags metadata), per-game Inherit / Force on / Force off / Do not touch, and write-off restore that does not trust ACM readback.
- Migrated Playnite native `EnableSystemHdr` via setup wizard and Maintenance so native and NX restores do not stack.
- Added EDID-based display identity, aliases, Overview live status (HDR Unknown when ACM may lie), NX settings chrome, Desktop top panel, and Theme API (`SourceName` DisplayManager).
- Documented Night Light as out of scope for v1; shipped EN/ES wiki (Overview, HDR, displays, ACM FAQ, native checkbox).
