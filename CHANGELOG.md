# Changelog

## 1.0.0 — 2026-09-20
- Applied Windows display topology and optional refresh rate when a game starts, with RestoreHost lease restore on stop, cancel, crash, or Playnite exit.
- Owned HDR for game sessions: global policies (leave alone / always on / Features+Tags metadata), per-game Inherit / Force on / Force off / Do not touch, and write-off restore that does not trust ACM readback.
- Migrated Playnite native `EnableSystemHdr` via setup wizard and Maintenance so native and NX restores do not stack.
- Added EDID-based display identity, aliases, Overview live status (HDR Unknown when ACM may lie), NX settings chrome, Desktop top panel, and Theme API (`SourceName` DisplayManager).
- Documented Night Light as out of scope for v1; shipped EN/ES wiki (Overview, HDR, displays, ACM FAQ, native checkbox).
