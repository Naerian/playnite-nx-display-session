# Night Light (cut in v1)

Display Manager **does not manage** Windows Night Light in this version. The control was removed from Settings so it does not look like a broken toggle.

## Why

Windows has **no public, supported Night Light API** that is deterministic across Windows 10 and Windows 11. Community tools (including [nightlight-cli](https://github.com/nathanbabcock/nightlight-cli)) reverse-engineer undocumented `CloudStore` binary registry values. Those formats change between builds; the upstream project itself reports breakage on recent Windows updates (2026). Bad writes can leave Night Light broken until sign-out or reboot.

That fails the product rule used for HDR: restore must be honest and reliable for HTPC / couch use.

## What to do instead

Change Night Light in **Windows Settings → System → Display → Night light**.

## Spanish

Ver [NIGHT-LIGHT.es.md](NIGHT-LIGHT.es.md).
