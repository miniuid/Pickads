# PickAds source security review

## Scope and conclusion
Reviewed every supplied text source/build/configuration file without executing the application or build scripts. The ZIP contains no EXE, DLL, dependency packages, or other executable payload. Images/icons were inventoried, not independently security-scanned.

No apparent RAT, remote command channel, credential theft, keylogging, payload downloader, obfuscated executable code, or antivirus-disabling behavior was found in the supplied C# source. This does not prove that the quarantined EXE matches this source or that either is malware-free. No antivirus scanner or Windows sandbox validation was performed.

## Behaviors explaining the concern
Original source requests administrator rights, modifies the system hosts file, reads the system DNS cache, runs ipconfig, and can register a highest-available-privilege logon task named PickAds. It also refreshes an existing task automatically and migrates an older task. Those task names match the screenshot, but that does not establish why Defender classified the binary. The source contains no facility for an attacker to send commands remotely.

## Changes in this copy
- Removed the entire scheduled-task implementation, automatic task refresh/migration, and startup setting. Existing tasks on a user's computer are not removed by this source change.
- Removed automatic first-launch hosts modification. New installations require the user to turn blocking on.
- Defaulted automatic DNS-cache monitoring to off. Existing saved auto=1 settings remain enabled.
- Resolved ipconfig using the Windows system directory rather than executable search paths. Removed the now-unused task-query process helper.
- Limited elevated relaunch arguments to the supported --tray flag.
- Stopped clearing a read-only hosts attribute, added a first-edit backup next to hosts, and refused writes when a PickAds section is unclosed.
- Replaced README guidance to bypass protection and its unsupported false-positive assurance.

## Remaining risks and validation
The application still runs its UI and tray process with administrator rights; separating privileged hosts edits into a narrow helper would require further design and Windows testing. Hosts changes affect every application, can block legitimate domains, and persist after exit. Shared ProgramData settings and executable placement should be reviewed for Windows ACLs before any elevated deployment. The backup is a first-edit recovery copy, not a transactional or concurrent-write guarantee.

Static checks confirm scheduled-task code is absent, all supplied process-launch sites were reviewed, and the patch touches only the changes described above. Installed Microsoft .NET SDK 8.0.419 and built the project targeting .NET Framework 4.8 with Microsoft reference assemblies. Build and clean rebuild both succeeded with zero warnings/errors and identical EXE SHA-256: 83fa05b480baf155d137afde1917b42fbd4c40124ff03988ea60560d6c1b2c0c. The output was identified as a Windows GUI .NET PE executable. This modified source has NOT been functionally tested on Windows, antivirus-scanned, or validated against the quarantined EXE. Do not treat this archive as a certified clean release.

## Windows follow-up
Keep the old EXE quarantined. Inspect and remove the old PickAds scheduled task in Task Scheduler, and run Defender Offline/full scans. Build this source with build.bat on a Windows development machine, scan the newly generated EXE with current Defender definitions, then test with a disposable Windows VM snapshot. Verify no new scheduled task appears, hosts remains unchanged before opting in, monitoring defaults off with fresh settings, blocking on/off preserves unrelated hosts lines, the backup exists, and read-only/malformed hosts are refused. Investigate any new detection rather than excluding the app.

## Optional startup update

The Start with Windows switch now explicitly opts into a current-user Run entry named PickAdsReviewed. It defaults absent/off, can be removed with the switch, and is never migrated or refreshed automatically. The executable manifest is asInvoker; Main still asks for UAC approval before starting the privileged UI, including at sign-in. No highest-privilege task or UAC bypass is used. Earlier statements about removing scheduled tasks remain accurate; startup now uses this separate mechanism. This update compiled successfully, but login, UAC, registry permissions and startup behavior still require Windows testing. Existing saved binaries/checksums above describe the earlier build, not this update.
