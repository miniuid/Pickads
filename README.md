# Pickads


PickAds is a free, open-source Windows app by **miniuid** that helps block ads in Minecraft launchers such as Dawn Client, Lunar Client, and Modrinth App.

It blocks ad-serving domains through the Windows hosts file without modifying Minecraft or launcher files. Blocking affects your whole computer, and some ads may remain.

## Download and use

1. Open the [Releases page](https://github.com/miniuid/Pickads/releases).
2. Under **Assets**, download **PickAds-Windows-Experimental-Release.zip**.
3. Right-click the downloaded ZIP and select **Extract All**.
4. Scan **PickAds.exe** with Microsoft Defender before opening it.
5. Open **PickAds.exe** and approve administrator permission if you choose to proceed.
6. Click **Turn on blocking**, then fully close and reopen your launcher.

The **Source code** downloads contain development files, not the ready-to-use app.

Keep Windows protection enabled. Do not bypass antivirus detections.

## Controls

- **Auto-block new ads:** Checks your computer’s DNS cache about every 15 seconds and adds domains matching PickAds’ ad rules. Off by default with fresh settings.
- **Scan now:** Runs that check immediately and automatically adds matching domains.
- **Clear DNS:** Clears cached domain lookups before a fresh scan.
- **Add site:** Adds a domain to your blocklist.
- **Unblock:** Removes selected domains and prevents automatic detection from adding them again. Manually adding a domain overrides that exception.
- **Notifications:** Shows alerts when new domains are blocked.

If login, downloads, or another feature stops working, unblock the suspected domain or turn blocking off, then restart the affected application.

## Start with Windows

This option is off by default. Turn on **Start with Windows** and confirm setup.

PickAds saves a protected startup copy in a folder like:

```text
C:\Program Files\PickAds-Startup-<random letters and numbers>\
```

A Windows scheduled task starts that copy in the tray approximately 15 seconds after sign-in. On an administrator account, it runs with administrator rights without another permission prompt each time.

Turn the switch off to remove the startup task. Installed files remain.

This feature does not grant administrator rights to standard accounts. If setup is approved using a different administrator account, startup belongs to that account instead.

## Closing and updating

Closing the window keeps PickAds running in the system tray beside the Windows clock. Double-click its icon to reopen it, or right-click it and choose **Exit**.

**Exiting does not remove existing blocks.** Click **Turn off blocking** to remove them.

To update:

1. Turn **Start with Windows** off.
2. Exit PickAds through its tray icon.
3. Download, extract, and scan the new version.
4. Open the new EXE.
5. Enable startup again if wanted.

Downloading a new EXE does not automatically update the protected startup copy.

## Uninstall

1. Turn **Auto-block new ads** off.
2. Click **Turn off blocking**.
3. Turn **Start with Windows** off.
4. Exit PickAds through its tray icon.
5. Remove the downloaded files and any unused PickAds startup folders you have identified.

Settings and lists are stored in `C:\ProgramData\PickAds`. You can remove that folder after exiting if you no longer want them.

Older versions’ startup tasks may need separate removal through Windows Task Scheduler.

## Important information

This is an **experimental release**. PickAds may miss ads or block domains needed by other applications. Its DNS scan sees computer-wide activity, not just Minecraft traffic.

The code compiled successfully, but the latest startup behavior has not been tested on Windows in the development environment.

One scanner flagged a tested build on VirusTotal, and that detection remains unresolved. A successful build or clean antivirus scan does not guarantee safety.

## Not affiliated

PickAds is an independent project. It is **not affiliated with, endorsed by, sponsored by, or officially connected to Dawn Client, Lunar Client, Modrinth App, Mojang, Microsoft, or any other Minecraft launcher or client**.

All product names, logos, and trademarks belong to their respective owners.

Blocking ads may reduce revenue for launcher developers and mod creators. Consider supporting projects you use through their official support options.

## License

Copyright (c) 2026 miniuid.

Released under the [MIT License](LICENSE). See LICENSE for the full terms.
