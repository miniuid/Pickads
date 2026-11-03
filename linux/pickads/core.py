"""Unprivileged rules, settings and strict hosts-section transformations."""
import ipaddress
import json
import os
from pathlib import Path
import re
import tempfile
from urllib.parse import urlsplit

START = b"# >>> PickAds START"
END = b"# <<< PickAds END"
MAX_DOMAINS = 5000
HOST = re.compile(r"(?=.{4,253}\Z)(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z][a-z0-9-]{0,62}\Z", re.ASCII)
AD_LABEL = re.compile(r"(?:ad|ads|adserver|adservice|adsrv|adserve|rtb\d*|prebid.*|.*adsystem.*|.*doubleclick.*|pagead\d*|securepubads|googleads\d*)\Z")
RULES = json.loads(Path(__file__).with_name("rules.json").read_text())


def valid_host(value):
    if not isinstance(value, str) or not HOST.fullmatch(value):
        return False
    try:
        ipaddress.ip_address(value)
        return False
    except ValueError:
        return True


def clean_host(value):
    value = value.strip().lower()
    parsed = urlsplit(value if "://" in value else "//" + value)
    name = (parsed.hostname or "").rstrip(".")
    if not valid_host(name):
        raise ValueError("Enter a domain such as ads.example.com, not an IP address.")
    return name


def validate_domains(values):
    if not isinstance(values, list) or len(values) > MAX_DOMAINS:
        raise ValueError("Expected a list of at most 5,000 domains.")
    if any(not valid_host(v) for v in values):
        raise ValueError("The blocklist contains an invalid domain.")
    return sorted(set(values))


def suffix(name, values):
    return any(name == s or name.endswith("." + s) for s in values)


def is_ad(name):
    if not valid_host(name):
        return False
    if suffix(name, RULES["AdOverrides"]):
        return True
    if suffix(name, RULES["SafeSuffixes"]):
        return False
    return suffix(name, RULES["AdSuffixes"]) or any(AD_LABEL.fullmatch(p) for p in name.split("."))


def section_bounds(data):
    """Refuse ambiguous sections. Byte offsets preserve all unrelated content."""
    start = end = None
    pos = 0
    for line in data.splitlines(keepends=True):
        token = line.strip()
        if token == START:
            if start is not None or end is not None:
                raise ValueError("Duplicate or nested PickAds hosts markers; no changes made.")
            start = pos
        elif token == END:
            if start is None or end is not None:
                raise ValueError("Unmatched PickAds hosts marker; no changes made.")
            end = pos + len(line)
        pos += len(line)
    if (start is None) != (end is None):
        raise ValueError("Incomplete PickAds hosts section; no changes made.")
    return None if start is None else (start, end)


def active_domains(data):
    bounds = section_bounds(data)
    if bounds is None:
        return None
    names = set()
    for line in data[bounds[0]:bounds[1]].splitlines():
        parts = line.decode("ascii", "replace").split()
        if len(parts) == 2 and parts[0] in ("0.0.0.0", "::") and valid_host(parts[1]):
            names.add(parts[1])
    return sorted(names)


def transform_hosts(data, domains, enabled):
    domains = validate_domains(domains)
    bounds = section_bounds(data)
    block = b""
    if enabled:
        block = START + b"\n# Managed by PickAds. Turn blocking off in the app to remove.\n"
        for name in domains:
            block += ("0.0.0.0 " + name + "\n:: " + name + "\n").encode("ascii")
        block += END + b"\n"
    if bounds:
        return data[:bounds[0]] + block + data[bounds[1]:]
    if not enabled:
        return data
    return data + (b"\n" if data and not data.endswith(b"\n") else b"") + block


def cache_domains(data):
    """systemd-resolved DumpCache JSON: scopes[].cache[].key.name."""
    if not isinstance(data, list):
        raise ValueError("Unexpected DNS-cache response from systemd-resolved.")
    names = set()
    for scope in data:
        if not isinstance(scope, dict) or not isinstance(scope.get("cache"), list):
            raise ValueError("Unexpected DNS-cache scope.")
        for item in scope["cache"]:
            name = item.get("key", {}).get("name", "").lower().rstrip(".")
            if valid_host(name):
                names.add(name)
    return sorted(names)


class Store:
    def __init__(self, path=None):
        self.path = Path(path) if path else Path(os.environ.get("XDG_CONFIG_HOME", str(Path.home() / ".config"))) / "pickads/settings.json"
        self.domains = set(RULES["Starter"])
        self.allow = set()
        self.auto = False
        self.notify = True
        if self.path.exists():
            data = json.loads(self.path.read_text())
            self.domains = set(validate_domains(data["domains"]))
            self.allow = set(validate_domains(data.get("allow", [])))
            self.auto = data.get("auto") is True
            self.notify = data.get("notify", True) is True

    def save(self):
        self.path.parent.mkdir(parents=True, exist_ok=True, mode=0o700)
        fd, temp = tempfile.mkstemp(dir=self.path.parent)
        try:
            with os.fdopen(fd, "w") as stream:
                json.dump({"domains": sorted(self.domains), "allow": sorted(self.allow), "auto": self.auto, "notify": self.notify}, stream, indent=2)
            os.replace(temp, self.path)
        finally:
            if os.path.exists(temp):
                os.unlink(temp)

    def add(self, names, manual=False):
        names = set(validate_domains(list(names)))
        if manual:
            self.allow -= names
        else:
            names -= self.allow
        new = names - self.domains
        validate_domains(list(self.domains | new))
        self.domains |= new
        self.save()
        return sorted(new)

    def remove(self, names):
        names = set(names) & self.domains
        self.domains -= names
        self.allow |= names
        self.save()
