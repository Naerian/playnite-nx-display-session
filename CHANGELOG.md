# Changelog



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
