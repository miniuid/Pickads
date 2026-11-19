# PickAds Safer Preview — Windows

An open-source Windows ad-domain blocker by **miniuid**, licensed
under MIT. This experimental security-focused build preserves the resizable
window, expanded detection rules and Recent lookups view, while reducing
administrator privileges. It is not an antivirus certification.

## Important: the uploaded EXE was a different build

The reviewed upload identified itself as version 1.1.0 and had SHA-256:

`ea2cb34dd9fbb515ee55dc847faa3a148f8119937752acfb6ad90453a9d0c423`

Its hash matches the screenshot showing 11/71 detections. It was decompiled as
**data**, never executed. It differs from our earlier reviewed source and lacks
several protections. Do not substitute it for this source build or restore it
from quarantine based on this review. See [SECURITY-REVIEW.md](SECURITY-REVIEW.md).

## What changed

- The main window, tray and detection run as your **normal user**. Do not use
  Run as administrator. The manifest is `asInvoker`.
- Windows approval is requested only for an explicit enable, disable, apply or
  Clear DNS operation. A short-lived helper handles that one request and exits.
- No elevated startup task, service, passwordless rule or installed startup copy
  is created. Optional ordinary-user startup is off by default.
- Automatic detection is off by default. It **proposes** domains; it does not
  silently change the Windows hosts file. On Windows, use **Apply new blocks**.
- Requests are bounded and validated, file paths are fixed in the helper, and the
  helper's pipe connection disallows impersonating the elevated client.
- Hosts edits keep unrelated bytes, refuse malformed/duplicate markers and
  read-only/redirected/hard-linked files, keep a first-edit backup, and attempt
  rollback if a write fails. No file protection is cleared.
- Settings are per user in `%LOCALAPPDATA%\PickAds`, not shared ProgramData.

## Before updating: disable legacy startup

**Do not run a flagged EXE just to uninstall its startup task.**

1. Open Windows **Task Scheduler**, then **Task Scheduler Library**.
2. Inspect tasks named `PickAds`, `MC Ad Blocker`, or `PickAds-Reviewed-S-1-5-...`.
   Check their Actions point to your old PickAds copy before disabling them.
3. In Windows **Settings > Apps > Startup**, disable older PickAds entries too.
4. Exit old running PickAds instances before opening this preview.

This new EXE cannot remove tasks already installed by an older version. Existing
hosts blocks remain until explicitly removed. Installing this file alone does
not clean or certify your PC.

## Download, open and use

Use the successful **Build PickAds Safer Preview** GitHub Actions run on the
`pickads-windows-safer` branch. Download **PickAds-Windows-Safer-Preview** under
Artifacts, extract it and scan the new EXE using current Windows protection.
Do not bypass a detection or add an exclusion.

1. Open `PickAds.exe` normally. The title says **PickAds Safer Preview**.
2. Review **Domain list**. The initial list contains 61 domains from the uploaded
   build plus any valid domains found in the existing marked hosts section.
3. Click **Turn on blocking** and approve Windows' permission prompt if wanted.
4. Fully close and reopen the Minecraft launcher.
5. Use **Scan now** or optional **Find new ads** to propose additional domains.
6. Review the list, then click **Apply new blocks** to approve system changes.
   Add/Unblock changes also need Apply while blocking is on.
7. **Recent lookups** shows computer-wide DNS names without preselecting anything.
   Its Copy all action warns about potentially private DNS names before copying.
8. Click **Turn off blocking** and approve the removal to undo the marked section.

Closing the window hides it in the tray. Right-click the tray icon to reopen or
exit. Exiting leaves existing system blocks in place. Domain blocking affects all
apps using Windows' resolver and cannot guarantee that every ad is blocked.

**Start with Windows** opens only the unprivileged interface. It needs a stable
EXE location; moving the file breaks that entry. Applying system changes still
needs approval. No passwords are saved and Windows protection stays enabled.

## Validation and remaining limits

The source and tests compile with Microsoft .NET SDK 8.0.419 targeting .NET
Framework 4.8. Managed security and layout checks run locally under Mono/Xvfb.
The Windows workflow additionally exercises hard-link refusal. Live UAC, pipe
permissions, alternate administrator credentials, antivirus results and Windows
startup still require native desktop validation. The preview remains unsigned.
See the review for exact completed checks. No zero-detection or malware-free claim.

## Build

On Windows with the .NET SDK:

```text
dotnet build PickAds.csproj -c Release -o dist
```

`build.bat` also includes all three source files for the Windows Framework compiler.
The GitHub workflow runs both test projects and preserves failure logs.

## Independence and license

PickAds is not affiliated with, endorsed by, sponsored by, or officially connected
to Dawn Client, Lunar Client, Modrinth App, Mojang, Microsoft or other Minecraft
launchers/clients. Names and trademarks belong to their owners. Blocking ads may
reduce revenue for developers and creators; consider their official support options.

Copyright (c) 2026 miniuid. [MIT License](LICENSE).
