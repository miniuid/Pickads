Name:           pickads
Version:        0.1.0
Release:        1%{?dist}
Summary:        PickAds desktop ad-domain blocker for Minecraft launchers
License:        MIT
URL:            https://github.com/miniuid/Pickads
Source0:        pickads-linux-%{version}.tar.gz
BuildArch:      noarch
BuildRequires:  python3
BuildRequires:  python3-pyside6
BuildRequires:  desktop-file-utils
Requires:       python3
Requires:       python3-pyside6
Requires:       polkit
Requires:       systemd-resolved
Requires:       qt6-qtwayland

%description
A Qt6 interface with manual and automatic hosts-based ad-domain blocking,
system-tray controls and optional desktop autostart. The interface runs as a
normal user. A polkit-authorized session helper performs narrowly scoped
system operations. Does not modify Minecraft launcher files.

%prep
%autosetup -n pickads-linux-%{version}

%build
# Python source; no compilation or downloaded build dependencies.

%install
install -d %{buildroot}%{_datadir}/pickads
install -m 0644 pickads/app.py pickads/core.py pickads/rules.json %{buildroot}%{_datadir}/pickads/
install -D -m 0755 pickads/helper.py %{buildroot}%{_libexecdir}/pickads-helper
install -D -m 0755 packaging/pickads %{buildroot}%{_bindir}/pickads
install -D -m 0644 packaging/io.github.miniuid.PickAds.desktop %{buildroot}%{_datadir}/applications/io.github.miniuid.PickAds.desktop
install -D -m 0644 packaging/io.github.miniuid.pickads.policy %{buildroot}%{_datadir}/polkit-1/actions/io.github.miniuid.pickads.policy
install -D -m 0644 assets/pickads.png %{buildroot}%{_datadir}/icons/hicolor/256x256/apps/pickads.png

%check
desktop-file-validate packaging/io.github.miniuid.PickAds.desktop
QT_QPA_PLATFORM=offscreen python3 -m unittest discover -s tests -v

%files
%license LICENSE
%doc README.md VALIDATION.md
%{_bindir}/pickads
%{_libexecdir}/pickads-helper
%{_datadir}/pickads/
%{_datadir}/applications/io.github.miniuid.PickAds.desktop
%{_datadir}/polkit-1/actions/io.github.miniuid.pickads.policy
%{_datadir}/icons/hicolor/256x256/apps/pickads.png
