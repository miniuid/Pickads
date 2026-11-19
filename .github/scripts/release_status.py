"""Verify published assets and record evidence in a Git note, not app history."""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile
import zipfile


def run(*args, **kwargs):
    return subprocess.check_output(args, text=True, **kwargs).strip()


repo = os.environ["GITHUB_REPOSITORY"]
tag = os.environ["GITHUB_REF_NAME"]
commit = run("git", "rev-parse", "HEAD")
status = {
    "tag": tag,
    "source_commit": commit,
    "build": os.environ["PICKADS_BUILD_RESULT"],
    "publish": os.environ["PICKADS_PUBLISH_RESULT"],
    "run_url": f'{os.environ["GITHUB_SERVER_URL"]}/{repo}/actions/runs/{os.environ["GITHUB_RUN_ID"]}',
    "complete": False,
}
try:
    if status["build"] == "success" and status["publish"] == "success":
        release = json.loads(run("gh", "release", "view", tag, "--repo", repo,
                                 "--json", "url,isDraft,isPrerelease,tagName,assets"))
        required = {"PickAds.exe", "PickAds.exe.sha256", "PickAds-Windows-Safer-Preview.zip"}
        names = {asset["name"] for asset in release["assets"]}
        if release["isDraft"] or not release["isPrerelease"] or release["tagName"] != tag or not required <= names:
            raise RuntimeError("Release state or required assets did not match the requested prerelease.")
        with tempfile.TemporaryDirectory() as temp:
            for name in sorted(required):
                run("gh", "release", "download", tag, "--repo", repo,
                    "--pattern", name, "--output", str(Path(temp) / name))
            actual = hashlib.sha256((Path(temp) / "PickAds.exe").read_bytes()).hexdigest()
            expected = (Path(temp) / "PickAds.exe.sha256").read_text().split()[0]
            if actual != expected:
                raise RuntimeError("Downloaded EXE did not match its published checksum.")
            with zipfile.ZipFile(Path(temp) / "PickAds-Windows-Safer-Preview.zip") as archive:
                if hashlib.sha256(archive.read("PickAds.exe")).hexdigest() != actual:
                    raise RuntimeError("ZIP did not contain the same EXE.")
            status.update(complete=True, release_url=release["url"], assets=sorted(names), sha256=actual)
    else:
        logs = {}
        for path in sorted(Path("release-diagnostics").glob("*.log")):
            logs[path.name] = path.read_text(errors="replace")[-12000:]
        status["diagnostics"] = logs
except Exception as exc:
    status["verification_error"] = str(exc)

Path("release-status.json").write_text(json.dumps(status, indent=2) + "\n")
# Git access remains available to the onboarding workspace even when REST does
# not. Notes preserve source commits and avoid a visible status-only branch.
ref = "refs/notes/pickads-release"
exists = subprocess.run(["git", "ls-remote", "--exit-code", "origin", ref],
                        stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL).returncode == 0
if exists:
    subprocess.run(["git", "fetch", "origin", f"{ref}:{ref}"], check=True)
subprocess.run(["git", "config", "user.name", "github-actions[bot]"], check=True)
subprocess.run(["git", "config", "user.email", "41898282+github-actions[bot]@users.noreply.github.com"], check=True)
subprocess.run(["git", "notes", "--ref=pickads-release", "add", "-f", "-F", "release-status.json", commit], check=True)
subprocess.run(["git", "push", "origin", ref], check=True)
print(json.dumps(status, indent=2))
if status["build"] == "success" and status["publish"] == "success" and not status["complete"]:
    raise SystemExit("Published release asset verification failed.")
