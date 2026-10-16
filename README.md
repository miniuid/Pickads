# Pickads

PickAds is a free, open-source Windows app by miniuid that helps block ads in Minecraft launchers such as Dawn Client, Lunar Client, and Modrinth App.

It blocks ad-serving domains through the Windows hosts file without modifying Minecraft or launcher files. Blocking affects your whole computer, and some ads may remain.

## How to use

1. Download PickAds-Windows from the latest successful build on the project’s GitHub Actions page.
2. Right-click the downloaded ZIP and choose Extract All.
3. Scan PickAds.exe with Microsoft Defender before opening it.
4. Open PickAds.exe and approve administrator permission if you choose to proceed.
5. Click Turn on blocking, then fully close and reopen your launcher.

Keep Windows protection enabled. Do not bypass antivirus detections.

## Controls

• Auto-block new ads: Checks your computer’s DNS cache about every 15 seconds and adds domains matching PickAds’ ad rules.
• Scan now: Runs that check immediately and automatically adds matching domains.
• Clear DNS: Clears cached domain lookups before a fresh scan.
• Add site: Adds a domain to your blocklist.
• Unblock: Removes selected domains and prevents automatic detection from adding them again.
• Notifications: Shows alerts when new domains are blocked.

If login, downloads, or another feature stops working, unblock the suspected domain or turn blocking off, then restart the launcher.

## Start with Windows

Turn on Start with Windows and confirm setup. PickAds saves a protected copy in a folder like:

C:\Program Files\PickAds-Startup-<random letters and numbers>\

A Windows scheduled task starts that copy in the tray shortly after sign-in. On an administrator account, it runs without asking for permission each time.

Turn the switch off to remove the startup task. Installed files remain. This feature does not grant administrator rights to standard accounts.

## Closing and updating

Closing the window keeps PickAds running beside the Windows clock. Double-click its tray icon to reopen it, or right-click it and choose Exit.

Exiting does not remove existing blocks. Click Turn off blocking to remove them.

To update: turn startup off, exit PickAds, open the new version, then enable startup again.

To uninstall: turn automatic detection, blocking, and startup off; exit PickAds; then remove its files. Settings are stored in C:\ProgramData\PickAds. Older versions’ startup tasks may need separate removal.

## Important information

PickAds cannot guarantee that every ad will be blocked or that every detected domain is safe to block. Its DNS scan sees computer-wide activity, not just Minecraft traffic.

The code compiled successfully, but the latest startup behavior has not been tested on Windows in the development environment. A successful build or clean antivirus scan does not guarantee safety.

## Not affiliated

PickAds is an independent community project. It is not affiliated with, endorsed by, sponsored by, or officially connected to Dawn Client, Lunar Client, Modrinth App, Mojang, Microsoft, or any other Minecraft launcher or client.

All product names, logos, and trademarks belong to their respective owners.

Blocking ads may reduce revenue for launcher developers and mod creators. Consider supporting projects you use through their official support options.

## License

Copyright (c) 2026 miniuid.
Released under the MIT License. See LICENSE for the full terms.
