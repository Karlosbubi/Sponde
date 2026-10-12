# Sponde

Offline personal budgeting with a balanced ledger, built in C# and Avalonia.
Windows 11 and standard Linux desktops are the targets. Product/receipt tracking
and an exploratory analysis workspace remain future features.

## Run

From this solution directory, with .NET SDK 10.0.400 installed:

```powershell
dotnet restore Sponde.slnx --locked-mode
dotnet run --project Sponde/Sponde.csproj
```

The first launch starts empty. Select one or several PDFs through **Import & Prüfung**,
review each preview and commit the valid files. Bank and payroll PDFs can be mixed
in one selection; bank documents are processed first. Payroll needs exactly one
matching bank income payment, already stored or provided by that selection.
Sources remain untouched. Unknown/ambiguous
layouts and balance mismatches cannot be committed. Create plans and categories
in Sponde. **There is no Excel import**; the example spreadsheet only informed
the target UI and analysis.

Document labels (account/type and period) are derived from PDF contents. Original
filenames remain visible for traceability and are not renamed. Each file receives
its own result; invalid files do not block unrelated valid files. Content-identical
duplicates are skipped. Cancelling stops unfinished files; completed file imports
remain committed and are listed in the results.

To open a chosen local dataset:

```powershell
dotnet run --project Sponde/Sponde.csproj -- --data-dir .local/pdf-data
```

In the prepared Windows workspace, `./Run-Sponde.ps1` starts the published app
with the verified private dataset. These ignored local outputs are not in Git.
The portable PDF-only backup is `.local/pdf-data/verified.spondebackup`.
The older prototype dataset in `.local/data` is retained separately and is no
longer opened by the launcher.

Default storage is `LocalApplicationData/Sponde` (typically
`%LOCALAPPDATA%/Sponde` on Windows and `~/.local/share/Sponde` on Linux).
The database, managed evidence and backups are unencrypted. Keep the active
database outside live cloud synchronization. Only one process can own a dataset.

## Features

- Overview corresponding to the workbook's main page: month selection, cash
  income, net spending, surplus, savings, account movement, notable spending,
  trends, cumulative surplus and top categories.
- Search, classification review, source access and category drill-down.
- Transaction splits with exact totals; own-account transfer linking across
  month boundaries; unresolved transfers retain clearing treatment.
- Effective-dated budget versions, cancellation, explicit unknown amounts and
  manually configured plan-versus-actual category mapping; recurring payment
  amounts, intervals and next-payment dates.
- Prioritized literal classification rules, with conflict handling and manual
  override preservation.
- N26 main/Space, VR and payroll parsing through a content-based extensible
  format registry; balance reconciliation and imports from an empty database.
- Gross payroll and deductions in ledger postings, net bank receipts in the
  budget, meal deductions and original periods for payroll corrections.
- Monthly transaction CSV export; consistent database/evidence backups with
  checksums, restore validation and an automatic pre-restore recovery backup.

PDFs open in the OS's default viewer; the UI also displays the source page number.
The current parser layouts are derived from the supplied 2026 corpus. Changed,
scanned or encrypted layouts need further parser work and are not silently
treated as supported. Statement reconciliation proves movement totals, not
complete brokerage, cash or credit-card purchase detail.

## Build and test

```powershell
dotnet build Sponde.slnx --no-restore
dotnet test Sponde.Tests/Sponde.Tests.csproj --no-restore
dotnet format Sponde.slnx --no-restore --verify-no-changes
dotnet publish Sponde/Sponde.csproj --no-restore -c Release -r win-x64 --self-contained true -o .local/publish/win-x64
dotnet publish Sponde/Sponde.csproj --no-restore -c Release -r linux-x64 --self-contained true -o .local/publish/linux-x64
```

Run `Sponde.exe` on Windows or `./Sponde` on Linux from the published folder.
After transferring a Linux build from Windows, run `chmod +x Sponde` first.
Ship the entire folder, not just the executable. Linux requires its normal
desktop libraries (X11/XWayland, fontconfig and graphics libraries). This build
uses Avalonia's default Linux X11 backend; native Wayland is not enabled.
Actual Fedora/Slowroll desktop acceptance is still required.

Windows Smart App Control can block unsigned development binaries. Do not
mistake successful compilation for successful launch. See Microsoft's
[Smart App Control guidance](https://learn.microsoft.com/windows/apps/develop/smart-app-control/overview).

## Private validation

Synthetic fixtures belong in Git; real finance documents and extracted data do
not. The optional tool accepts explicit source and scratch directories:

```powershell
dotnet run --project Sponde.Tools/Sponde.Tools.csproj -- validate "<finance-folder>" ".local/validation"
```

Use a new scratch directory. It discovers PDFs by content, imports bank statements
before payroll, checks reimport idempotency and account reconciliation, and performs
backup/restore. It never opens a spreadsheet and verifies that no budget plans
were imported.
It prints counts and diagnostics, not transaction details. `inspect` parses
PDFs without committing. Its optional third argument writes private diagnostic
text; use an ignored local directory only.

See [the plan](docs/PLAN.md), [decisions](docs/DECISIONS.md),
[architecture](docs/ARCHITECTURE.md) and [implementation status](docs/STATUS.md).
