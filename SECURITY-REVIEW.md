# PickAds security review and safer preview

## Uploaded file reviewed without execution

- Size: 88,576 bytes; managed Windows PE executable.
- SHA-256: `ea2cb34dd9fbb515ee55dc847faa3a148f8119937752acfb6ad90453a9d0c423`.
- This matches the supplied VirusTotal screenshot showing 11/71 detections.
- Assembly version: 1.1.0.0. No Authenticode certificate table is present.
- Decompiled as data with pinned ILSpy 9.1.0.7988, not executed, loaded as a runtime
  assembly or uploaded to an external scanner by this task. Compared the managed
  code with the known resizable-source build, including the additional Lookups view.

No apparent remote-control channel, credential theft, keylogger, payload download,
process injection or antivirus-disabling routine was found in the managed code
reviewed. This is a limited static finding, not proof of absence or a false-positive
verdict. The detection's cause remains unresolved. Several vendors can share
engines; their count is not a probability of infection or safety.

## Concrete risks in that upload

The upload does not match the previously reviewed/resizable source build. It has:

- A `requireAdministrator` manifest and an administrator-level interface/tray.
- First-launch automatic hosts changes and DNS detection enabled by default.
- Elevated logon tasks, automatic task refresh/migration and an executable task
  target based on the running EXE's location.
- Temporary task XML under a predictable user-temp filename.
- Commands resolved by names such as `schtasks` and `ipconfig`.
- Clearing hosts read-only protection, no first-edit recovery backup, and no
  rejection of malformed section markers before rewriting hosts.
- Extra ad rules and a DNS lookup view, which do not themselves establish malware.

Those observations help explain the attack surface; they do not establish why any
particular antivirus vendor classified the file. Its publisher/build provenance
has not been established by this review.

## New preview: security-relevant changes

The replacement is rebuilt from reviewed repository source, not a patched/packed
version of the uploaded binary. It keeps the resizable window and reconstructs
its useful lookup view, 61 starter domains and expanded detection rules.

1. **Privilege separation:** interface, tray, DNS reading and settings run as an
   ordinary user. Manually elevated GUI launches are refused. Only explicit
   system-change actions request UAC. The helper handles one request and exits.
2. **Narrow IPC:** a random named pipe grants access to the invoking SID and
   elevated administrators. The elevated client explicitly uses anonymous
   impersonation level so the ordinary server cannot impersonate its token.
   Only enable, disable and DNS-flush operations are accepted; no paths, programs,
   arbitrary commands or settings files are accepted across that boundary.
   Domains/counts/line lengths are bounded. Timeouts and cancellations are errors.
3. **No permanent elevation:** no service, elevated task, passwordless rule or
   installed EXE copy is created. Optional startup uses only the ordinary user's
   Run key and starts without administrator privileges.
4. **Explicit review:** detection and list changes are staged. Apply new blocks
   requires user action and administrator approval. Detection/startup default off
   in the fresh per-user profile. Imported existing hosts entries are not silently
   changed on first launch.
5. **Hosts protections:** production helper selects the system hosts path itself.
   Reparse-point paths, hard links on Windows, read-only files, oversized files,
   invalid domains and ambiguous markers are refused. An exclusive write-sharing
   policy prevents concurrent normal writes during editing. Unrelated bytes are
   preserved, a first-edit backup is kept without overwrite, and write errors
   trigger an attempt to restore the in-memory original. The original file handle
   is used instead of replacing the file/ACL or clearing protection flags.
6. **Other changes:** per-user LocalAppData settings, fixed Windows DNS command
   paths, checked subprocess exit status/timeouts, no lookup preselection, and a
   privacy confirmation before copying computer-wide DNS names. New-domain logs
   record counts rather than domain names.

## Validation completed

- App and both test projects built using Microsoft .NET SDK 8.0.419 / net48,
  zero warnings and zero errors after refreshing the initially network-denied
  NuGet audit cache. TLS, package signatures and vulnerability checks stayed on.
- 38 managed security assertions passed under Mono against temporary fixtures:
  strict protocol/count/line validation, injection rejection, malformed/duplicate
  marker refusal, byte preservation, idempotency, disk update/removal, backups,
  read-only protection and invalid helper arguments.
- 78 UI/layout assertions passed under Mono/Xvfb: resize behavior, explicit Apply
  control, lookup contents and no preselection. No system hosts file, startup
  task or registry entry was changed by these checks.

## Not verified / remaining limits

Live Windows UAC/pipe permissions (including alternate admin credentials), Windows
hard-link metadata enforcement, startup, power-loss recovery and native desktop
behavior have NOT been exercised here. The committed Windows CI includes the
hard-link test; its outcome is not yet confirmed. The build is unsigned and has
NOT been antivirus-scanned or certified. It may still be flagged. Do not repack,
obfuscate, add exclusions or bypass detections to make a scan pass; request vendor
review of any disputed detection.

The helper is launched from the selected EXE with an ordinary UAC consent prompt;
this is not a signed, protected installer/deployment scheme. It does not defend
against malware already running as the user or administrator, or guarantee
integrity of files downloaded from untrusted sources. A backup/rollback attempt
cannot guarantee recovery after power loss or disk failure.

**Existing elevated startup tasks are not removed by replacing an EXE.** Do not
run the flagged old file merely to disable them. Inspect and disable the known
PickAds tasks through Task Scheduler and older ordinary entries through Startup
Apps. See README for upgrade instructions. Existing hosts blocks remain until
explicitly removed. No claim is made that the user's PC is clean.

## Replacement identity

Local preview version: 1.2.0-safer-preview.
Local EXE SHA-256: `9fd81775619bfde414865f973fcfae79a8f518d4af38bc490fc84672886786d1`.
GitHub's own rebuild can have a different hash; compare it with the checksum from
that exact build, and keep its associated source and review report.
