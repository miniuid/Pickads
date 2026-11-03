# Linux port validation

## Completed

- Retrieved the Windows source at commit `50e62d4` and compared the rendered Qt6
  light/dark interface with its screenshot. Reused its logo, 57 starter domains,
  ad suffixes, safe suffixes and exceptions.
- 26 automated tests passed under Python 3.13 / PySide6 6.8 in an isolated Debian
  13 container: core hosts editing, refusal of malformed markers/symlinks/read-only
  files, injection rejection, inode/permission preservation, original backup,
  settings persistence, remembered unblock exceptions, Qt widget interactions,
  cancelled authorization state, auto-scan failure handling and autostart removal.
- Installed helper integration passed against real systemd-resolved 257 with a
  localhost-only DNS fixture in that disposable container: scan saw the query,
  IPv4 and IPv6 libc lookups returned blocking addresses, cache clearing worked,
  disabling restored every original hosts byte, and helper EOF ended the session.
  This tested root helper operations directly; it did not simulate a KDE password dialog.
- Extracted the final RPM payload and started its actual launcher as an ordinary
  user in offscreen Qt; it remained in the GUI event loop without authorization
  at launch. Running the launcher as root was correctly refused.
- Desktop entry validation and a noarch RPM/source RPM build completed. RPM
  dependency metadata was checked for Fedora package names and fixed helper paths.
  The local build used Debian's equivalent Qt/RPM tools with RPM **build dependency**
  name checking omitted (`--nodeps`); package tests still ran. No TLS or package
  signature checks were disabled. No Fedora runtime dependency bypass is advised.

## Not yet established

The Fedora 44 container image could be pulled and its release verified, but this
workspace's network proxy denied Fedora repository access (HTTP 403 for
`mirrors.fedoraproject.org`, `dl.fedoraproject.org`, `download.fedoraproject.org`).
Consequently no successful Fedora dependency install, Fedora-native RPM build,
or Fedora desktop test is claimed. The committed GitHub workflow performs a
Fedora-native build when run in GitHub Actions; adding the workflow is not proof
that it passed.

A real Fedora KDE test is still needed for interactive polkit authorization and
cancellation, tray support under Plasma/Wayland, login/autostart, and SELinux
enforcement. Offscreen tests and Debian containers do not verify those features.
No specific Minecraft launcher compatibility or antivirus certification is claimed.

## Desktop acceptance check

1. Install the RPM through normal `dnf install` dependency resolution; do not
   disable signature checks or force missing dependencies.
2. Open as a normal user. Confirm it starts with blocking/auto/startup off on a
   fresh profile and that the interface follows KDE's light/dark preference.
3. Cancel the first system authorization: status must not report success.
4. Authorize, enable blocking and inspect the single marked hosts section.
5. Scan a known cached ad domain through systemd-resolved; verify Unblock prevents
   automatic re-addition. Verify Clear DNS removes cached records.
6. Close to tray, reopen, exit, and enable/disable autostart across a sign-in.
7. Turn blocking off and verify unrelated hosts entries and SELinux label remain.
8. Test launcher login, downloads and skins as well as ads before promoting it.
