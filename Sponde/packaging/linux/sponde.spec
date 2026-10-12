Name:           sponde
Version:        %{app_version}
Release:        1%{?dist}
Summary:        Offline personal budgeting and accounting
License:        MIT
URL:            https://github.com/Karlosbubi/Sponde
Source0:        sponde-%{version}.tar.gz
BuildArch:      x86_64
BuildRequires:  desktop-file-utils
# Private self-contained .NET and GUI libraries must not advertise system ABI providers.
AutoReqProv:    no
Requires:       glibc, libstdc++, libgcc, zlib, openssl-libs, libicu, krb5-libs
Requires:       libX11, libICE, libSM, libXrandr, libXi, libXcursor
Requires:       fontconfig, freetype, libuuid, mesa-libGL, xorg-x11-server-Xwayland
%global debug_package %{nil}
%global __strip /bin/true
%global __brp_mangle_shebangs %{nil}
%global _build_id_links none

%description
Sponde imports bank and payroll PDFs into a local balanced ledger, with budgeting,
transaction review, analysis and portable backups. No network service is used.
This upstream package includes a self-contained .NET runtime.

%prep
%setup -q

%build
# Payload is published by the tested .NET release build before RPM assembly.

%install
install -d %{buildroot}%{_libdir}/sponde
cp -a app/. %{buildroot}%{_libdir}/sponde/
chmod 0755 %{buildroot}%{_libdir}/sponde/Sponde
install -D -m 0755 sponde-launcher %{buildroot}%{_bindir}/sponde
install -D -m 0644 sponde.desktop %{buildroot}%{_datadir}/applications/sponde.desktop
install -D -m 0644 sponde.svg %{buildroot}%{_datadir}/icons/hicolor/scalable/apps/sponde.svg
install -D -m 0644 sponde.png %{buildroot}%{_datadir}/icons/hicolor/256x256/apps/sponde.png

%check
desktop-file-validate %{buildroot}%{_datadir}/applications/sponde.desktop

%files
%license LICENSE
%{_libdir}/sponde/
%{_bindir}/sponde
%{_datadir}/applications/sponde.desktop
%{_datadir}/icons/hicolor/scalable/apps/sponde.svg
%{_datadir}/icons/hicolor/256x256/apps/sponde.png
