# Packaging and releases

Accepted 2026-10-12: ship a Windows 11 x64 MSI and an x86_64 RPM built and
installed on the latest stable Fedora. Both include the .NET runtime. No
financial documents, database or development dataset belong in either package.
Packages preserve supplied dependency licenses and .NET runtime notices, and
include an inventory of restored dependencies under the `licenses` directory.

The repository-root `.github/workflows/build.yml` runs on pull requests targeting
`main`, pushes to `main`, and manual dispatch. Tests and formatting must pass on
Windows and Linux before packaging. Package checks install, start the native GUI,
uninstall, and check that a separate user dataset survives. Fedora uses the
`registry.fedoraproject.org/fedora:latest` container and Xvfb for the GUI check.
This follows Fedora stable releases; it does not follow Rawhide. A real desktop
acceptance check remains useful, particularly on openSUSE Slowroll.

Only a successful run on `main` publishes a GitHub release. Pull requests build
and test packages, uploading workflow artifacts for review without publishing.
The release contains both installers and `SHA256SUMS`, and its tag references
the exact tested commit. Publication starts as a draft and finishes after both
assets upload. Re-running a run retains its version and replaces its assets.
Only the publication job receives write access; action references are pinned.

## Versions

`packaging/version-prefix.txt` supplies major/minor; the workflow run number
supplies patch, for example `0.1.7` and tag `v0.1.7`. Failed and PR runs consume
numbers, so gaps are expected. Windows Installer requires major/minor <= 255 and
patch <= 65535; the workflow fails before exceeding these bounds. Before reaching
that limit, change the version policy and introduce a fresh workflow run counter.
RPM release is `1.fcNN`, reflecting the Fedora container used for that run.

## Windows

WiX 6.0.2 is pinned in `.config/dotnet-tools.json`. The MSI installs per machine
into Program Files, offers a destination dialog, includes the MIT license and a
Start Menu shortcut, registers with Windows Apps, and supports major upgrades.
The stable WiX package ID preserves the upgrade identity across releases.
WiX validates the generated MSI; CI installs a lower-version package then checks
that an upgrade leaves exactly one current-version registration. App data is outside the install directory and
is never authored as an installer component.

For a local package build, publish a self-contained `win-x64` payload and run:

```powershell
./packaging/windows/Build-Msi.ps1 -PayloadDirectory <published-folder> -Version 0.1.1 -OutputDirectory <output-folder>
```

`Test-Msi.ps1` installs software and is intended for ephemeral CI runners only.

## Fedora

The RPM installs its private runtime into `/usr/lib64/sponde`, a launcher into
`/usr/bin/sponde`, and a desktop entry and icons under `/usr/share`. It declares
system GUI/runtime dependencies explicitly. Private .NET libraries do not
advertise system ABI providers and are not stripped or repackaged as debuginfo.
DNF owns installation, upgrade and removal; the package has no data-removal
scripts. Install with `sudo dnf install ./sponde-<version>-1.fcNN.x86_64.rpm`.

Inside Fedora, build and test with:

```bash
bash packaging/linux/build-rpm.sh <published-linux-folder> 0.1.1 <output-folder>
bash packaging/linux/test-rpm.sh <rpm-file>
```

The test requires root, DNF, desktop-file-utils, Xvfb and fonts, and is intended
for an ephemeral container. The application itself runs as a normal desktop user.

## Branch policy and signing

Development happens on feature branches with pull requests to `main`. Required
checks are `desktop (windows-latest)`, `desktop (ubuntu-latest)`, `Windows MSI`
and `Fedora RPM`. Protection requires current checks, resolved conversations and
linear history, and blocks direct pushes, force pushes and deletion, including
for administrators. An approving review is optional for the sole-maintainer
workflow; a pull request and checks are mandatory. Use squash merges.

These initial packages are unsigned. Signing would require a Windows code-signing
identity and an RPM signing key; neither is currently configured. SHA-256
checksums verify downloaded bytes, not publisher identity. The workflow does not
create or store signing credentials.
