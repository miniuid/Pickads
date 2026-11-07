# Hardened source review copy

This modified source compiled successfully with .NET SDK 8.0.419 targeting .NET Framework 4.8. It has not been run or tested on Windows. It is not a malware-free certification. See SECURITY-REVIEW.md for findings and validation limits.

<p align="center">
  <img src="assets/PickAds-icon.png" width="112" alt="PickAds logo">
</p>

<h1 align="center">PickAds</h1>

<p align="center">
  A small, free Windows app that blocks ads in Minecraft launchers like Lunar Client, Dawn Client and the Modrinth App.
</p>

<p align="center">
  <img src="assets/screenshot.png" width="640" alt="PickAds in light and dark mode">
</p>

## Get the Windows build

After these files are pushed to GitHub, open **Actions**, select the latest successful **Build PickAds** run, and download **PickAds-Windows** under **Artifacts**. GitHub sign-in is required to download the ZIP. Extract the ZIP and scan the EXE with Defender before running it. Do not bypass a detection. GitHub building a file does not certify it as malware-free.

## Features

- **One click** to turn ad blocking on or off
- **Finds new ads automatically** in the background and blocks them
- **Start with Windows** is optional and off by default. After approval it installs a protected startup copy and an elevated logon task, so an administrator account is not prompted at each sign-in.
- **Unblock anything** with one click if a launcher stops working
- Light and dark mode, matching your Windows setting
- One small `.exe` (about 80 KB). Nothing to install. Uses the .NET Framework already built into Windows 10 and 11.

## How to use

1. Download and extract the build ZIP using the instructions above.
2. Open it and click **Yes** when Windows asks for permission. PickAds needs admin rights to edit the Windows hosts file.
3. Click **Turn on blocking** to edit the hosts file. Enable **Auto-block new ads** only if you want background DNS-cache inspection. Both are off by default for new settings.

If ads still show, fully close the launcher (check the tray near the clock) and open it again.

Keep antivirus protection enabled. Do not bypass a malware detection or restore the previously quarantined executable on the strength of this source review.

## How it works

PickAds adds ad-server domains to the Windows **hosts file** (`C:\Windows\System32\drivers\etc\hosts`), pointing them to `0.0.0.0` so your PC can't connect to them. It's the same idea as Pi-hole or a browser ad blocker.

- When automatic detection is enabled, every 15 seconds it checks the Windows DNS cache for recently visited domains and blocks any that match known ad networks.
- It **never modifies any launcher's files or the game**. It only blocks connections to ad servers.
- Its entries in the hosts file sit between `# >>> PickAds START` and `# <<< PickAds END`. Turning blocking off removes them and leaves the rest of your hosts file untouched.
- It doesn't collect or send any data. It never connects to the internet itself.

Settings and lists are stored in `C:\ProgramData\PickAds\`.

### Antivirus warnings

Some antivirus programs flag new, unsigned apps that edit the hosts file and start with Windows, because malware sometimes does those things too. A detection must be investigated; this source review cannot establish that an existing executable is safe or matches this source.

## Uninstall

1. Open PickAds and click **Turn off blocking**, remove any legacy PickAds scheduled task through Windows Task Scheduler after inspecting it. This version does not create or modify startup tasks.
2. Right-click the tray icon and choose **Exit**.
3. Delete `PickAds.exe` and the folder `C:\ProgramData\PickAds`.

## Build it yourself

**Easiest (no installs):** on Windows, double-click `build.bat`. The app appears in `dist\PickAds.exe`.

**With the .NET SDK:**

```
dotnet build PickAds.csproj -c Release -o dist
```

## Forks

Forks are welcome under the MIT License. Please **use a different name and icon** for your fork, so people don't confuse it with the official PickAds.

## Disclaimer

PickAds is an independent project. It is **not affiliated with, endorsed by, or connected to** Lunar Client (Moonsworth), Dawn Client, Modrinth, Mojang or Microsoft. All product names belong to their owners.

Blocking ads means launchers and creators earn less from you. On Modrinth, part of the ad revenue goes to mod creators. If you'd like to support them, consider [Modrinth+](https://modrinth.com/plus).

## License

[MIT](LICENSE)

## Optional startup

Open PickAds, approve its administrator prompt, and turn on **Start with Windows**. Confirm the explanation. The app copies itself into a new protected `Program Files\PickAds-Startup-<random>` folder and registers a scheduled task for the current Windows identity, starting 15 seconds after sign-in with highest available privileges. This is intended for Windows administrator accounts; it does not grant administrator rights to standard accounts. Approving UAC as another account sets up startup for that account instead.

Turn the switch off to remove the task and the older PickAdsReviewed Run entry. Protected installed files remain. To update the startup copy: turn startup off, exit PickAds, open the new EXE, and turn startup on again. Moving the downloaded EXE does not move the installed startup copy. Existing legacy tasks named PickAds or MC Ad Blocker are not changed. Antivirus protection remains enabled; do not bypass detections. Login behavior has not been tested on Windows in this environment.

## Resize the window

Drag a window edge or corner to resize PickAds, or use the title-bar maximize button.
The domain list expands with the window; settings stay aligned and the bottom buttons
share the available width. The original small size is the minimum to avoid clipping.

When updating from an older startup copy: turn **Start with Windows** off, exit
PickAds through the tray, open the new EXE, then enable startup again if wanted.
