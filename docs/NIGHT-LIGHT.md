# Night Light (experimental — cut in v1)

Display Manager **does not manage** Windows Night Light in this version.

## Why

Windows has **no public, supported Night Light API** that is deterministic across Windows 10 and Windows 11. Community tools reverse-engineer undocumented `CloudStore` binary registry values. Those formats change between builds; bad writes can leave Night Light broken until sign-out or reboot.

That fails the product rule used for HDR: restore must be honest and reliable for HTPC / couch use. Guessing CloudStore blobs would risk lying in Overview and breaking the desktop.

## What you see in Settings

- **General → Night Light**: “Do not touch” only, labelled experimental / cut.
- **Overview**: “Not managed — no supported Win10+Win11 Night Light API”.

## What to do instead

Change Night Light in **Windows Settings → System → Display → Night light**.

## Spanish

Ver [NIGHT-LIGHT.es.md](NIGHT-LIGHT.es.md).
