#!/usr/bin/python3 -I
"""Root broker: fixed operations, fixed paths, no shell, no user-supplied filenames.

Run only the root-owned installed copy via pkexec. Authority ends on stdin EOF.
"""
import fcntl
import json
import os
from pathlib import Path
import stat
import subprocess
import sys

sys.path.insert(0, "/usr/share/pickads")
from core import cache_domains, transform_hosts  # noqa: E402

HOSTS = Path("/etc/hosts")
MAX_INPUT = 2 * 1024 * 1024


def command(args):
    result = subprocess.run(args, capture_output=True, timeout=15, check=False,
                            env={"PATH": "/usr/sbin:/usr/bin", "LC_ALL": "C"})
    if result.returncode:
        raise RuntimeError(result.stderr.decode(errors="replace").strip()[:800] or "Resolver operation failed.")
    return result.stdout


def flush():
    command(["/usr/bin/resolvectl", "flush-caches"])


def write_hosts(domains, enabled, path=HOSTS):
    # Lock a fixed root-only file to serialize all PickAds instances. Do not rename
    # /etc/hosts: this preserves its inode, ownership, ACLs and SELinux label.
    lockfd = os.open(str(path) + ".pickads-lock", os.O_CREAT | os.O_RDWR | os.O_NOFOLLOW, 0o600)
    try:
        fcntl.flock(lockfd, fcntl.LOCK_EX | fcntl.LOCK_NB)
        fd = os.open(path, os.O_RDWR | os.O_NOFOLLOW)
        try:
            info = os.fstat(fd)
            if not stat.S_ISREG(info.st_mode) or info.st_size > MAX_INPUT:
                raise ValueError("Hosts must be a regular file smaller than 2 MiB.")
            if not info.st_mode & stat.S_IWUSR:
                raise PermissionError("Hosts is read-only; PickAds will not override it.")
            original = os.read(fd, MAX_INPUT + 1)
            changed = transform_hosts(original, domains, enabled)
            if changed == original:
                return
            # One untouched first-edit backup. Refuse symlink backup targets.
            try:
                backup = os.open(str(path) + ".pickads-backup", os.O_CREAT | os.O_EXCL | os.O_WRONLY | os.O_NOFOLLOW, 0o600)
            except FileExistsError:
                if Path(str(path) + ".pickads-backup").is_symlink():
                    raise ValueError("Unsafe hosts backup path.")
            else:
                with os.fdopen(backup, "wb") as stream:
                    stream.write(original)
                    stream.flush()
                    os.fsync(stream.fileno())
            with os.fdopen(os.dup(fd), "r+b") as stream:
                # Another editor may not honor our lock. Abort a detected change.
                stream.seek(0)
                if stream.read(MAX_INPUT + 1) != original:
                    raise RuntimeError("Hosts changed while editing. Please try again.")
                try:
                    stream.seek(0)
                    stream.write(changed)
                    stream.truncate()
                    stream.flush()
                    os.fsync(stream.fileno())
                except OSError:
                    stream.seek(0)
                    stream.write(original)
                    stream.truncate()
                    stream.flush()
                    os.fsync(stream.fileno())
                    raise
        finally:
            os.close(fd)
    finally:
        os.close(lockfd)


def dispatch(request):
    if not isinstance(request, dict):
        raise ValueError("Invalid request.")
    action = request.get("action")
    if action == "apply" and set(request) == {"action", "domains", "enabled"}:
        if type(request["enabled"]) is not bool:
            raise ValueError("Enabled must be boolean.")
        write_hosts(request["domains"], request["enabled"])
        try:
            flush()
            return {"ok": True}
        except (OSError, RuntimeError, subprocess.TimeoutExpired):
            return {"ok": True, "warning": "Hosts updated; DNS cache could not be cleared. Restart your launcher."}
    if action == "scan" and set(request) == {"action"}:
        raw = command(["/usr/bin/resolvectl", "--json=short", "--no-ask-password", "show-cache"])
        return {"ok": True, "domains": cache_domains(json.loads(raw))}
    if action == "flush" and set(request) == {"action"}:
        flush()
        return {"ok": True}
    raise ValueError("Unsupported helper operation.")


def main():
    if os.geteuid() != 0:
        print("This helper requires administrator authorization.", file=sys.stderr)
        return 1
    while line := sys.stdin.buffer.readline(MAX_INPUT + 1):
        if len(line) > MAX_INPUT:
            return 1
        try:
            reply = dispatch(json.loads(line))
        except (OSError, ValueError, RuntimeError, subprocess.TimeoutExpired) as exc:
            reply = {"ok": False, "error": str(exc)[:1000]}
        print(json.dumps(reply), flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
