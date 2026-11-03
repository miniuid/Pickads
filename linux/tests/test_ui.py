import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "pickads"))
from PySide6.QtCore import QObject, Signal
from PySide6.QtWidgets import QApplication, QMessageBox, QInputDialog
from PySide6.QtTest import QTest
from PySide6.QtCore import Qt
from core import Store, transform_hosts
from app import Window

APP = QApplication.instance() or QApplication([])


class FakeBroker(QObject):
    finished = Signal(object)

    def __init__(self, hosts):
        super().__init__()
        self.hosts = hosts
        self.requests = []
        self.fail = False

    def request(self, request):
        self.requests.append(request)
        if self.fail:
            self.finished.emit({"ok": False, "error": "Authorization cancelled"})
        elif request["action"] == "apply":
            self.hosts.write_bytes(transform_hosts(self.hosts.read_bytes(), request["domains"], request["enabled"]))
            self.finished.emit({"ok": True})
        elif request["action"] == "scan":
            self.finished.emit({"ok": True, "domains": ["ads.example.com", "login.minecraft.net"]})
        else:
            self.finished.emit({"ok": True})

    def stop(self):
        pass


class UITests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.root = Path(self.directory.name)
        self.hosts = self.root / "hosts"
        self.hosts.write_text("127.0.0.1 localhost\n")
        self.store = Store(self.root / "settings.json")
        self.broker = FakeBroker(self.hosts)
        self.window = Window(self.store, self.broker, self.hosts, self.root / "autostart/pickads.desktop")
        self.window.show()
        APP.processEvents()

    def tearDown(self):
        self.window.timer.stop()
        self.window.allow_close = True
        self.window.tray.hide()
        self.window.close()
        self.window.deleteLater()
        APP.processEvents()
        self.directory.cleanup()

    def test_windows_controls_and_safe_defaults(self):
        self.assertEqual(self.window.sites.count(), 57)
        self.assertEqual(self.window.status.text(), "Blocking is off")
        self.assertFalse(self.window.auto.isChecked())
        self.assertFalse(self.window.startup.isChecked())
        self.assertEqual(set(self.window.buttons), {"Scan now", "Clear DNS", "Add site", "Unblock"})
        self.assertEqual(self.broker.requests, [])

    def test_toggle_updates_actual_hosts_status(self):
        QTest.mouseClick(self.window.block_button, Qt.LeftButton)
        self.assertEqual(self.window.status.text(), "Ads are blocked")
        QTest.mouseClick(self.window.block_button, Qt.LeftButton)
        self.assertEqual(self.hosts.read_text(), "127.0.0.1 localhost\n")

    def test_scan_adds_only_ads_and_unblock_is_respected(self):
        self.window.scan_now()
        self.assertIn("ads.example.com", self.store.domains)
        self.assertNotIn("login.minecraft.net", self.store.domains)
        self.window.sites.findItems("ads.example.com", Qt.MatchExactly)[0].setSelected(True)
        self.window.unblock()
        self.window.scan_now()
        self.assertNotIn("ads.example.com", self.store.domains)

    def test_add_applies_while_blocking_and_clear_dns(self):
        self.window.toggle_blocking()
        with patch.object(QInputDialog, "getText", return_value=("ads.example.com", True)):
            self.window.add_site()
        self.assertIn(b"0.0.0.0 ads.example.com", self.hosts.read_bytes())
        self.window.clear_dns()
        self.assertEqual(self.broker.requests[-1], {"action": "flush"})

    def test_startup_is_user_owned_and_removable(self):
        QTest.mouseClick(self.window.startup, Qt.LeftButton)
        self.assertIn("Exec=/usr/bin/pickads --tray", self.window.autostart.read_text())
        QTest.mouseClick(self.window.startup, Qt.LeftButton)
        self.assertFalse(self.window.autostart.exists())

    def test_authorization_failure_does_not_claim_blocking(self):
        self.broker.fail = True
        with patch.object(QMessageBox, "warning"):
            self.window.toggle_blocking()
        self.assertEqual(self.window.status.text(), "Blocking is off")
        self.assertTrue(self.window.block_button.isEnabled())

    def test_auto_failure_disables_repeated_requests(self):
        self.broker.fail = True
        with patch.object(QMessageBox, "warning"):
            self.window.set_auto(True)
        self.assertFalse(self.store.auto)
        self.assertFalse(self.window.auto.isChecked())
        count = len(self.broker.requests)
        self.window.auto_tick()
        self.assertEqual(len(self.broker.requests), count)

    def test_closing_hides_when_tray_available(self):
        with patch("app.QSystemTrayIcon.isSystemTrayAvailable", return_value=True):
            self.window.close()
        self.assertFalse(self.window.isVisible())
        self.assertFalse(self.window.allow_close)

    def test_cannot_close_during_hosts_operation(self):
        self.window.busy = True
        with patch("app.QSystemTrayIcon.isSystemTrayAvailable", return_value=False):
            self.window.close()
        self.assertTrue(self.window.isVisible())
        self.window.busy = False

    def test_settings_write_error_rolls_back_switch(self):
        with patch.object(self.store, "save", side_effect=OSError("disk full")), patch.object(QMessageBox, "warning"):
            self.window.auto.setChecked(True)
            self.window.set_auto(True)
        self.assertFalse(self.window.auto.isChecked())
        self.assertFalse(self.store.auto)


if __name__ == "__main__":
    unittest.main()
