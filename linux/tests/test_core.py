import importlib.util
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "pickads"))
from core import (START, END, Store, RULES, active_domains, cache_domains,
                  clean_host, is_ad, transform_hosts, validate_domains)
import helper


class HostsTests(unittest.TestCase):
    def test_enable_update_disable_preserves_unrelated_bytes(self):
        base = b"# local entries\r\n127.0.0.1 localhost\r\n192.0.2.4 my.local\n"
        first = transform_hosts(base, ["ads.example.com"], True)
        self.assertTrue(first.startswith(base))
        self.assertIn(b"0.0.0.0 ads.example.com\n:: ads.example.com\n", first)
        self.assertEqual(active_domains(first), ["ads.example.com"])
        second = transform_hosts(first, ["other.example.com"], True)
        self.assertNotIn(b"ads.example.com", second)
        self.assertEqual(second.count(START), 1)
        self.assertEqual(transform_hosts(second, [], False), base)

    def test_noop_and_missing_final_newline(self):
        self.assertEqual(transform_hosts(b"127.0.0.1 localhost", [], False), b"127.0.0.1 localhost")
        text = transform_hosts(b"127.0.0.1 localhost", [], True)
        self.assertIn(b"localhost\n" + START, text)
        self.assertEqual(transform_hosts(text, [], True), text)

    def test_bad_markers_fail_closed(self):
        for data in (START + b"\n", END + b"\n", START + b"\n" + START + b"\n" + END,
                     START + b"\n" + END + b"\n" + START + b"\n" + END):
            with self.subTest(data=data), self.assertRaises(ValueError):
                transform_hosts(data, [], False)

    def test_preserves_other_app_sections(self):
        base = b"# Other tool START\n0.0.0.0 another.example.com\n# Other tool END\n"
        self.assertEqual(transform_hosts(transform_hosts(base, [], True), [], False), base)

    def test_rejects_injection_and_oversized_lists(self):
        for name in ("ads.com\n127.0.0.1 bank.com", "ads.com # comment", "-bad.com", "localhost", "127.0.0.1", "$(id).com"):
            with self.subTest(name=name), self.assertRaises(ValueError):
                transform_hosts(b"", [name], True)
        with self.assertRaises(ValueError):
            validate_domains(["example.com"] * 5001)

    def test_file_backup_inode_permissions_and_restore(self):
        with tempfile.TemporaryDirectory() as d:
            path = Path(d) / "hosts"
            original = b"127.0.0.1 localhost\n"
            path.write_bytes(original)
            path.chmod(0o640)
            inode = path.stat().st_ino
            helper.write_hosts(["ads.example.com"], True, path)
            helper.write_hosts(["other.example.com"], True, path)
            self.assertEqual(Path(str(path) + ".pickads-backup").read_bytes(), original)
            self.assertEqual(path.stat().st_ino, inode)
            self.assertEqual(path.stat().st_mode & 0o777, 0o640)
            helper.write_hosts([], False, path)
            self.assertEqual(path.read_bytes(), original)

    def test_symlink_and_readonly_hosts_rejected(self):
        with tempfile.TemporaryDirectory() as d:
            real = Path(d) / "real"
            real.write_bytes(b"original\n")
            link = Path(d) / "hosts"
            link.symlink_to(real)
            with self.assertRaises(OSError):
                helper.write_hosts([], True, link)
            real.chmod(0o444)
            with self.assertRaises(PermissionError):
                helper.write_hosts([], True, real)
            self.assertEqual(real.read_bytes(), b"original\n")

    def test_backup_symlink_and_malformed_hosts_unchanged(self):
        with tempfile.TemporaryDirectory() as d:
            path = Path(d) / "hosts"
            path.write_bytes(b"original\n")
            Path(str(path) + ".pickads-backup").symlink_to(path)
            with self.assertRaises(ValueError):
                helper.write_hosts([], True, path)
            self.assertEqual(path.read_bytes(), b"original\n")
            path.write_bytes(START + b"\n")
            with self.assertRaises(ValueError):
                helper.write_hosts([], True, path)
            self.assertEqual(path.read_bytes(), START + b"\n")

    def test_helper_rejects_arbitrary_operations_and_paths(self):
        for request in ({"action": "shell", "command": "id"}, {"action": "scan", "path": "/tmp/a"},
                        {"action": "apply", "domains": [], "enabled": "true"}, []):
            with self.subTest(request=request), self.assertRaises(ValueError):
                helper.dispatch(request)


class RulesTests(unittest.TestCase):
    def test_windows_starter_list_valid(self):
        self.assertEqual(len(validate_domains(RULES["Starter"])), 57)

    def test_rules_safe_exceptions_and_suffix_boundaries(self):
        for domain in ("ad.doubleclick.net", "ads.example.com", "rtb2.example.com", "imasdk.googleapis.com"):
            self.assertTrue(is_ad(domain), domain)
        for domain in ("login.minecraft.net", "ads.microsoft.com", "notadnxs.com", "example.com"):
            self.assertFalse(is_ad(domain), domain)

    def test_url_cleaning(self):
        self.assertEqual(clean_host(" HTTPS://ADS.Example.com/path?q=1 "), "ads.example.com")
        with self.assertRaises(ValueError):
            clean_host("not a domain")

    def test_cache_schema_only_reads_query_keys(self):
        fixture = [{"protocol": "dns", "cache": [{"key": {"name": "Ads.Example.com."}, "rrs": [{"name": "ignored.example.com"}]}]}]
        self.assertEqual(cache_domains(fixture), ["ads.example.com"])
        self.assertEqual(cache_domains([]), [])
        with self.assertRaises(ValueError):
            cache_domains({"bad": "schema"})

    def test_cache_failure_not_reported_as_empty_success(self):
        with patch.object(helper, "command", side_effect=RuntimeError("resolved unavailable")):
            with self.assertRaisesRegex(RuntimeError, "unavailable"):
                helper.dispatch({"action": "scan"})

    def test_unblock_persists_and_manual_add_overrides(self):
        with tempfile.TemporaryDirectory() as d:
            store = Store(Path(d) / "settings.json")
            self.assertFalse(store.auto)
            store.add(["ads.example.com"])
            store.remove(["ads.example.com"])
            self.assertEqual(store.add(["ads.example.com"]), [])
            loaded = Store(store.path)
            self.assertIn("ads.example.com", loaded.allow)
            self.assertNotIn("ads.example.com", loaded.domains)
            loaded.add(["ads.example.com"], manual=True)
            self.assertNotIn("ads.example.com", Store(store.path).allow)

    def test_existing_settings_preserved(self):
        with tempfile.TemporaryDirectory() as d:
            store = Store(Path(d) / "settings.json")
            store.auto = True
            store.notify = False
            store.save()
            loaded = Store(store.path)
            self.assertTrue(loaded.auto)
            self.assertFalse(loaded.notify)


if __name__ == "__main__":
    unittest.main()
