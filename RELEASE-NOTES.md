# PickAds 1.2.0 Beta 1 — Safer Preview

An experimental Windows preview by miniu, with a resizable interface and a
redesigned administrator-permission boundary.

## Downloads

- **PickAds-Windows-Safer-Preview.zip**: extract it to find the app, checksum,
  license, usage instructions and security review.
- **PickAds.exe**: the same app as a separate download.
- **PickAds.exe.sha256**: checksum for this exact build.

Keep Windows protection enabled. Investigate any antivirus detection; do not
add an exclusion or bypass protection to run the app.

## Changes

- The interface runs as an ordinary user. Only explicit system changes request UAC.
- A short-lived helper accepts one bounded hosts/DNS operation and exits.
- Elevated automatic startup is removed. Optional startup opens only the normal UI.
- Detection proposes domains; review them and click **Apply new blocks**.
- Hosts edits validate inputs, preserve unrelated bytes, respect read-only
  protection, refuse ambiguous/redirected files, and retain a recovery backup.
- Recent lookups, expanded detection rules and window resizing are included.

## Before upgrading

Do not restore or run a flagged old EXE just to change its settings. Use Task
Scheduler to inspect and disable old PickAds/MC Ad Blocker/PickAds-Reviewed tasks,
and Windows Startup Apps to disable older entries. Replacing an EXE does not
remove those tasks. Existing hosts blocks remain until explicitly removed.

## Testing and limitations

This release workflow requires its Windows build, UI/layout checks and managed
hosts/protocol checks to pass before publication. Those checks do not exercise
live UAC approval, alternate administrator credentials or all launcher behavior.
The app is unsigned and is not certified malware-free. A fresh antivirus review
of this exact release remains outstanding. The older uploaded 1.1.0 binary had
11/71 detections; its cause remains unresolved. That result is not a scan of
this new build. Read the included security review before testing.

PickAds cannot block every ad and may affect other applications using the same
domains. It is not affiliated with Dawn Client, Lunar Client, Modrinth App,
Mojang, Microsoft or any other Minecraft launcher/client.

MIT License. Copyright (c) 2026 miniuid. See the included LICENSE.
