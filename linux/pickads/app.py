"""PickAds Qt6 desktop client. The interface never runs as root."""
import json
import os
from pathlib import Path
import sys
from datetime import datetime

from PySide6.QtCore import Qt, QProcess, QTimer, Signal, QRectF
from PySide6.QtGui import QColor, QPainter, QIcon, QPen, QFont, QPixmap
from PySide6.QtNetwork import QLocalServer, QLocalSocket
from PySide6.QtWidgets import (QApplication, QWidget, QVBoxLayout, QHBoxLayout, QLabel,
                               QPushButton, QFrame, QListWidget, QAbstractItemView,
                               QInputDialog, QMessageBox, QSystemTrayIcon, QMenu)

sys.path.insert(0, str(Path(__file__).resolve().parent))
from core import Store, clean_host, is_ad, active_domains


class Switch(QPushButton):
    def __init__(self, name, parent=None):
        super().__init__(parent)
        self.setObjectName(name)
        self.setAccessibleName(name)
        self.setCheckable(True)
        self.setFixedSize(46, 27)
        self.setCursor(Qt.PointingHandCursor)

    def paintEvent(self, event):
        p = QPainter(self)
        p.setRenderHint(QPainter.Antialiasing)
        p.setPen(Qt.NoPen)
        color = "#22c55e" if self.isChecked() else ("#4b5563" if dark_mode() else "#cbd0d8")
        p.setBrush(QColor(color))
        p.drawRoundedRect(QRectF(0, 0, 46, 27), 13.5, 13.5)
        p.setBrush(QColor("white"))
        p.drawEllipse(QRectF(23 if self.isChecked() else 3, 3, 21, 21))


def dark_mode():
    return QApplication.palette().window().color().lightness() < 128


def logo(on=True):
    if on:
        for path in (Path(__file__).resolve().parent.parent / "assets/pickads.png",
                     Path("/usr/share/icons/hicolor/256x256/apps/pickads.png")):
            if path.is_file():
                return QPixmap(str(path)).scaled(64, 64, Qt.KeepAspectRatio, Qt.SmoothTransformation)
    image = QPixmap(64, 64)
    image.fill(Qt.transparent)
    p = QPainter(image)
    p.setRenderHint(QPainter.Antialiasing)
    p.setPen(Qt.NoPen)
    p.setBrush(QColor("#0e0f11"))
    p.drawRoundedRect(0, 0, 64, 64, 16, 16)
    p.setPen(QPen(QColor("#22c55e" if on else "#80848a"), 6, Qt.SolidLine, Qt.RoundCap))
    p.drawLine(20, 45, 43, 20)
    p.drawArc(19, 16, 31, 32, 20 * 16, 95 * 16)
    p.end()
    return image


class Broker(QWidget):
    finished = Signal(object)

    def __init__(self, parent=None):
        super().__init__(parent)
        self.process = QProcess(self)
        self.process.setProgram("/usr/bin/pkexec")
        self.process.setArguments(["--disable-internal-agent", "/usr/libexec/pickads-helper"])
        self.process.readyReadStandardOutput.connect(self.read_reply)
        self.process.finished.connect(self.exited)
        self.process.errorOccurred.connect(self.failed)
        self.process.started.connect(self.send_pending)
        self.pending = None
        self.buffer = b""
        self.deadline = QTimer(self)
        self.deadline.setSingleShot(True)
        self.deadline.timeout.connect(self.timed_out)

    def request(self, request):
        if self.pending is not None:
            raise RuntimeError("A system operation is already running.")
        self.pending = request
        self.deadline.start(120000)
        if self.process.state() == QProcess.NotRunning:
            if not Path("/usr/libexec/pickads-helper").is_file():
                self.finish({"ok": False, "error": "Install the PickAds RPM before changing system settings."})
                return
            self.process.start()
        else:
            self.send_pending()

    def send_pending(self):
        if self.pending is not None:
            self.process.write((json.dumps(self.pending) + "\n").encode())

    def read_reply(self):
        self.buffer += bytes(self.process.readAllStandardOutput())
        if len(self.buffer) > 2 * 1024 * 1024:
            self.timed_out()
            return
        while b"\n" in self.buffer:
            line, self.buffer = self.buffer.split(b"\n", 1)
            try:
                reply = json.loads(line)
                if not isinstance(reply, dict) or type(reply.get("ok")) is not bool:
                    raise ValueError("Invalid helper response")
                self.finish(reply)
            except (ValueError, UnicodeError):
                self.finish({"ok": False, "error": "Invalid reply from system helper."})

    def finish(self, reply):
        if self.pending is None:
            return
        self.pending = None
        self.deadline.stop()
        self.finished.emit(reply)

    def exited(self, code, status):
        self.buffer = b""
        self.finish({"ok": False, "error": "Authorization was cancelled or the helper closed. No success was confirmed."})

    def failed(self, error):
        self.finish({"ok": False, "error": "Could not open the authorization helper. Check that polkit is installed and running."})

    def timed_out(self):
        self.process.kill()
        self.finish({"ok": False, "error": "System operation timed out. Check the blocking status before trying again."})

    def stop(self):
        self.process.closeWriteChannel()
        if not self.process.waitForFinished(2000):
            self.process.kill()
            self.process.waitForFinished(1000)


class Window(QWidget):
    def __init__(self, store=None, broker=None, hosts_path=Path("/etc/hosts"), autostart=None):
        super().__init__()
        self.store = store or Store()
        self.hosts_path = hosts_path
        self.autostart = autostart or Path(os.environ.get("XDG_CONFIG_HOME", str(Path.home() / ".config"))) / "autostart/pickads.desktop"
        self.broker = broker or Broker(self)
        self.broker.finished.connect(self.operation_done)
        self.operation = None
        self.busy = False
        self.allow_close = False
        self.automatic_scan = False
        self.enabled = False
        self.setObjectName("window")
        self.setWindowTitle("PickAds")
        self.setWindowIcon(QIcon(logo()))
        self.setFixedWidth(420)
        self.setMinimumHeight(620)
        self.setFont(QFont("sans-serif", 10))
        outer = QVBoxLayout(self)
        outer.setContentsMargins(16, 16, 16, 12)
        outer.setSpacing(12)

        hero, layout = self.card(outer)
        header = QHBoxLayout()
        icon = QLabel()
        icon.setPixmap(logo())
        icon.setFixedSize(64, 64)
        header.addWidget(icon)
        texts = QVBoxLayout()
        self.status = QLabel()
        self.status.setObjectName("status")
        self.detail = QLabel()
        self.detail.setProperty("sub", True)
        texts.addWidget(self.status)
        texts.addWidget(self.detail)
        header.addSpacing(16)
        header.addLayout(texts, 1)
        layout.addLayout(header)
        self.block_button = QPushButton()
        self.block_button.setObjectName("blocking")
        self.block_button.setMinimumHeight(42)
        self.block_button.clicked.connect(self.toggle_blocking)
        layout.addSpacing(4)
        layout.addWidget(self.block_button)

        settings, rows = self.card(outer)
        self.auto = self.setting(rows, "Auto-block new ads", "Checks for new ads in the background")
        self.startup = self.setting(rows, "Start with Linux", "Opens quietly in the system tray")
        self.notify = self.setting(rows, "Notifications", "Tell me when new ads get blocked")
        self.auto.setChecked(self.store.auto)
        self.notify.setChecked(self.store.notify)
        self.startup.setChecked(self.autostart.exists())
        self.auto.clicked.connect(self.set_auto)
        self.startup.clicked.connect(self.set_startup)
        self.notify.clicked.connect(self.set_notify)

        sites, content = self.card(outer)
        labels = QHBoxLayout()
        self.count = QLabel()
        self.count.setProperty("bold", True)
        labels.addWidget(self.count)
        labels.addStretch()
        hint = QLabel("Select a site to unblock it")
        hint.setProperty("sub", True)
        hint.setStyleSheet("font-size: 11px;")
        labels.addWidget(hint)
        content.addLayout(labels)
        self.sites = QListWidget()
        self.sites.setObjectName("sites")
        self.sites.setSelectionMode(QAbstractItemView.ExtendedSelection)
        self.sites.setHorizontalScrollBarPolicy(Qt.ScrollBarAlwaysOff)
        self.sites.setTextElideMode(Qt.ElideRight)
        self.sites.setMinimumHeight(116)
        content.addWidget(self.sites)
        buttons = QHBoxLayout()
        buttons.setSpacing(6)
        self.buttons = {}
        for title, action in (("Scan now", self.scan_now), ("Clear DNS", self.clear_dns), ("Add site", self.add_site), ("Unblock", self.unblock)):
            button = QPushButton(title)
            button.setObjectName(title)
            button.setMinimumHeight(34)
            button.clicked.connect(action)
            self.buttons[title] = button
            buttons.addWidget(button)
        content.addLayout(buttons)
        self.footer = QLabel("Closing this window keeps it running in the tray")
        self.footer.setWordWrap(True)
        self.footer.setAlignment(Qt.AlignCenter)
        self.footer.setProperty("sub", True)
        outer.addWidget(self.footer)
        self.style_app()
        QApplication.styleHints().colorSchemeChanged.connect(self.style_app)

        self.tray = QSystemTrayIcon(QIcon(logo()), self)
        self.tray.setToolTip("PickAds")
        menu = QMenu()
        menu.addAction("Open PickAds", self.show_window)
        menu.addAction("Exit", self.exit_app)
        self.tray.setContextMenu(menu)
        self.tray.activated.connect(lambda reason: self.show_window() if reason in (QSystemTrayIcon.Trigger, QSystemTrayIcon.DoubleClick) else None)
        self.tray.show()
        if not QSystemTrayIcon.isSystemTrayAvailable():
            self.footer.setText("No system tray available; closing this window exits PickAds")
        self.timer = QTimer(self)
        self.timer.setInterval(15000)
        self.timer.timeout.connect(self.auto_tick)
        self.timer.start()
        self.refresh()

    def card(self, parent_layout):
        card = QFrame()
        card.setProperty("card", True)
        layout = QVBoxLayout(card)
        layout.setContentsMargins(20, 18, 20, 16)
        layout.setSpacing(8)
        parent_layout.addWidget(card)
        return card, layout

    def setting(self, parent, title, subtitle):
        row = QHBoxLayout()
        labels = QVBoxLayout()
        labels.setSpacing(2)
        heading = QLabel(title)
        heading.setProperty("bold", True)
        sub = QLabel(subtitle)
        sub.setProperty("sub", True)
        sub.setStyleSheet("font-size: 11px;")
        labels.addWidget(heading)
        labels.addWidget(sub)
        row.addLayout(labels, 1)
        switch = Switch(title)
        row.addWidget(switch)
        parent.addLayout(row)
        return switch

    def style_app(self, *args):
        dark = dark_mode()
        bg, card, text, sub, neutral = ("#111315", "#1c1f23", "#f3f4f6", "#9ca3af", "#2a2e34") if dark else ("#f3f4f5", "#ffffff", "#111827", "#667085", "#f1f3f5")
        self.setStyleSheet(f'''
            QWidget#window {{ background: {bg}; color: {text}; }}
            QFrame[card="true"] {{ background: {card}; border-radius: 20px; }}
            QLabel {{ color: {text}; background: transparent; }}
            QLabel[sub="true"] {{ color: {sub}; }}
            QLabel[bold="true"] {{ font-weight: 600; }}
            QLabel#status {{ font-size: 20px; font-weight: 700; }}
            QPushButton {{ background: {neutral}; color: {text}; border: none; border-radius: 17px; padding: 7px 9px; font-size: 11px; font-weight: 600; }}
            QPushButton:hover {{ border: 1px solid #22c55e; }}
            QPushButton:disabled {{ color: {sub}; }}
            QPushButton#blocking {{ font-size: 14px; border-radius: 21px; }}
            QPushButton#blocking[primary="true"] {{ background: #22c55e; color: #052e16; }}
            QListWidget {{ background: {card}; color: {text}; border: none; outline: none; }}
            QListWidget::item {{ padding: 6px; border-radius: 12px; }}
            QListWidget::item:selected {{ background: #153a22; color: #bbf7d0; }}
            QScrollBar:vertical {{ background: {card}; width: 8px; border: none; }}
            QScrollBar::handle:vertical {{ background: {sub}; border-radius: 4px; min-height: 20px; }}
            QScrollBar::add-line:vertical, QScrollBar::sub-line:vertical {{ height: 0; }}
        ''')
        for switch in (self.auto, self.startup, self.notify):
            switch.update()

    def refresh(self):
        try:
            current = active_domains(self.hosts_path.read_bytes())
            self.enabled = current is not None
            self.status.setText("Ads are blocked" if self.enabled else "Blocking is off")
            if current is not None and set(current) != self.store.domains:
                self.detail.setText("Blocklist changes not yet applied")
            else:
                self.detail.setText(f"{len(current)} ad sites blocked" if current is not None else "Turn on blocking to apply your list")
        except (OSError, ValueError) as exc:
            self.status.setText("Hosts needs attention")
            self.detail.setText(str(exc)[:60])
        self.count.setText(f"Blocked sites  ({len(self.store.domains)})")
        self.sites.clear()
        self.sites.addItems(sorted(self.store.domains))
        self.block_button.setText("Turn off blocking" if self.enabled else "Turn on blocking")
        self.block_button.setProperty("primary", not self.enabled)
        self.block_button.style().unpolish(self.block_button)
        self.block_button.style().polish(self.block_button)
        self.tray.setIcon(QIcon(logo(self.enabled)))
        self.tray.setToolTip("PickAds — blocking " + ("ON" if self.enabled else "OFF"))

    def error(self, message):
        self.footer.setText(message)
        QMessageBox.warning(self, "PickAds", message)

    def run(self, action, **kwargs):
        if self.busy:
            return
        self.busy = True
        self.operation = action
        for widget in [self.block_button, self.auto, self.startup, self.notify, *self.buttons.values()]:
            widget.setEnabled(False)
        self.footer.setText("Waiting for system operation / authorization…")
        self.broker.request({"action": action, **kwargs})

    def apply(self):
        self.run("apply", enabled=self.enabled, domains=sorted(self.store.domains))

    def toggle_blocking(self):
        self.run("apply", enabled=not self.enabled, domains=sorted(self.store.domains))

    def operation_done(self, result):
        action = self.operation
        self.busy = False
        for widget in [self.block_button, self.auto, self.startup, self.notify, *self.buttons.values()]:
            widget.setEnabled(True)
        if not result.get("ok"):
            if self.automatic_scan:
                self.store.auto = False
                self.auto.setChecked(False)
                try:
                    self.store.save()
                except OSError:
                    pass  # Remains off for this session even if disk settings cannot be saved.
            self.automatic_scan = False
            self.refresh()
            self.error(result.get("error", "System operation failed."))
            return
        self.footer.setText(result.get("warning", "Last check " + datetime.now().strftime("%I:%M %p") + " · done"))
        if action == "scan":
            try:
                found = [name for name in result.get("domains", []) if is_ad(name)]
                added = self.store.add(found)
                self.refresh()
                self.footer.setText(f"Last check · {len(added)} new ad site(s)")
                if added and self.store.notify:
                    self.tray.showMessage("PickAds", f"Found {len(added)} new ad site(s).", QSystemTrayIcon.Information, 4000)
                self.automatic_scan = False
                if added and self.enabled:
                    self.apply()
            except (OSError, ValueError) as exc:
                self.error(str(exc))
        else:
            self.refresh()

    def scan_now(self):
        self.automatic_scan = False
        self.run("scan")

    def clear_dns(self):
        self.run("flush")

    def auto_tick(self):
        if self.store.auto and not self.busy:
            self.automatic_scan = True
            self.run("scan")

    def set_auto(self, checked):
        self.store.auto = checked
        try:
            self.store.save()
        except OSError as exc:
            self.store.auto = not checked
            self.auto.setChecked(not checked)
            self.error("Could not save settings: " + str(exc))
            return
        if checked:
            self.auto_tick()

    def set_notify(self, checked):
        self.store.notify = checked
        try:
            self.store.save()
        except OSError as exc:
            self.store.notify = not checked
            self.notify.setChecked(not checked)
            self.error("Could not save settings: " + str(exc))

    def set_startup(self, checked):
        try:
            if checked:
                self.autostart.parent.mkdir(parents=True, exist_ok=True)
                self.autostart.write_text("[Desktop Entry]\nType=Application\nName=PickAds\nExec=/usr/bin/pickads --tray\nIcon=pickads\nTerminal=false\n")
            else:
                self.autostart.unlink(missing_ok=True)
        except OSError as exc:
            self.startup.setChecked(self.autostart.exists())
            self.error(str(exc))

    def add_site(self):
        text, ok = QInputDialog.getText(self, "Add site", "Type the ad site to block, like ads.example.com")
        if not ok:
            return
        try:
            self.store.add([clean_host(text)], manual=True)
            self.refresh()
            if self.enabled:
                self.apply()
        except (OSError, ValueError) as exc:
            self.error(str(exc))

    def unblock(self):
        selected = [item.text() for item in self.sites.selectedItems()]
        if not selected:
            return
        try:
            self.store.remove(selected)
            self.refresh()
            if self.enabled:
                self.apply()
        except (OSError, ValueError) as exc:
            self.error(str(exc))

    def show_window(self):
        self.refresh()
        self.showNormal()
        self.raise_()
        self.activateWindow()

    def closeEvent(self, event):
        if self.busy:
            event.ignore()
            self.footer.setText("Wait for the system operation to finish before closing.")
            return
        if not self.allow_close and QSystemTrayIcon.isSystemTrayAvailable():
            event.ignore()
            self.hide()
            return
        self.broker.stop()
        event.accept()

    def exit_app(self):
        if self.busy:
            self.error("Wait for the system operation to finish before exiting.")
            return
        self.allow_close = True
        self.tray.hide()
        self.broker.stop()
        QApplication.quit()


def main():
    if os.geteuid() == 0:
        print("Run PickAds as your normal desktop user, not with sudo.", file=sys.stderr)
        return 1
    app = QApplication(sys.argv)
    app.setApplicationName("PickAds")
    app.setDesktopFileName("io.github.miniuid.PickAds")
    # Keep one UI per user, like the Windows app. Socket lives in the private
    # XDG runtime directory; stale sockets are removed only after failed connect.
    socket_path = str(Path(os.environ.get("XDG_RUNTIME_DIR", f"/run/user/{os.getuid()}")) / "pickads.sock")
    client = QLocalSocket()
    client.connectToServer(socket_path)
    if client.waitForConnected(300):
        client.write(b"show")
        client.waitForBytesWritten(500)
        return 0
    QLocalServer.removeServer(socket_path)
    server = QLocalServer()
    if not server.listen(socket_path):
        QMessageBox.critical(None, "PickAds", "Could not create the desktop session socket.")
        return 1
    try:
        window = Window()
    except (OSError, ValueError) as exc:
        QMessageBox.critical(None, "PickAds", "Could not load settings: " + str(exc))
        return 1
    def connected():
        peer = server.nextPendingConnection()
        window.show_window()
        peer.disconnectFromServer()
        peer.deleteLater()
    server.newConnection.connect(connected)
    if "--tray" not in sys.argv or not QSystemTrayIcon.isSystemTrayAvailable():
        window.show()
    app.setQuitOnLastWindowClosed(not QSystemTrayIcon.isSystemTrayAvailable())
    app.aboutToQuit.connect(window.broker.stop)
    return app.exec()


if __name__ == "__main__":
    raise SystemExit(main())
