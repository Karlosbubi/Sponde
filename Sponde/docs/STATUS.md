# Implementation status

Updated 2026-10-12. The first application is implemented. Platform acceptance
is not complete; this document distinguishes delivered code from verified use.

## Delivered

- C#/.NET 10 solution with Domain, Application, Infrastructure, Desktop, Tests
  and private-validation tool boundaries.
- Avalonia Overview, transactions/review, imports, reconciliation, budgets,
  classification rules, payroll and backup screens. German UI and EUR formatting.
- Immutable financial records, exact settled cents, balanced ledger postings,
  opening-equity entries, splits and explicit cross-month transfer linking.
- SQLite schema migrations through version 5; source observations, edit history,
  managed hashed evidence, PDF verification and one-process data ownership.
- PDF-only imports. The example spreadsheet is a UI/analysis reference; its
  reader, import port, migration path and import controls have been removed.
  Plans and classifications are managed directly in Sponde.
- N26 main/Space and VR PDFs; payroll gross/deductions, meal/prepaid deductions,
  original correction periods and bank payment matching without double income.
- Extensible content-based format registry, shared normalization/validation,
  and owned-account identification from PDF headers without hardcoded user names.
- Multi-file PDF selection, per-file previews/results, bank-before-payroll ordering,
  content-derived document labels and independent file commits with cancellation.
- Effective-dated budget changes/cancellation, conflict-aware literal rules,
  monthly CSV export and cancellable imports.
- Snapshot/checksum backups, validated restore and pre-restore recovery backup.
- Pinned dependencies/lock files, formatter/analyzers and Windows/Linux CI setup.
- Self-contained Windows x64 and Linux x64 build folders under ignored
  `.local/publish`; PDF-only working dataset under ignored `.local/pdf-data`.
  The old prototype dataset remains separately in `.local/data` and is not opened
  by the launcher.

## Verification

- 38 synthetic tests pass: money/ledger invariants, cash-flow semantics,
  reconciliation counts, idempotency, manual edits, malformed backups, source
  changes, payroll matching, source-layout parsing, Excel rejection, content-based
  recognition, adapter extension, ambiguous formats and shared validation,
  rules, budget history/recurrence, splits, transfers, CSV safety, cancellation and GUI.
  Batch tests cover mixed selection order, duplicates, unreadable files, partial
  failure, payroll dependency rechecks, same-name files and renamed reissues.
- Headless Avalonia renders the Overview and navigates all screens. Screenshot
  uses synthetic data; no real financial information is in test fixtures.
- Private corpus rebuilt exclusively from PDFs: 478 bank observations, all 12 bank PDFs reconcile,
  all six payroll PDFs match, all 18 PDF reimports are idempotent, and a complete
  backup/restore preserves the resulting dataset. No spreadsheet was read;
  no spreadsheet categories, notes or budget plans were imported. Unclear
  classifications remain open. Four payroll correction periods are retained.
- The complete 18-PDF corpus also passes as one mixed, reverse-order batch:
  478 transactions, six payslips, idempotent batch reimport and verified restore.
- Debug and Release compilation have warnings treated as errors. Windows x64
  and Linux x64 self-contained publishing succeeds.
- Native Windows GUI startup with the prepared real dataset succeeds.
- Locked dependency restore and formatting checks are part of validation.
- CI has been written, but has not been run on GitHub in this session.

## Remaining acceptance and limits

- Actual Fedora and openSUSE Slowroll GUI/packaging tests require those desktops.
  Cross-compilation and headless Windows tests do not establish native Linux
  runtime behavior. The initial Linux backend uses X11/XWayland.
- This is an unsigned development build. Windows Smart App Control initially
  blocked a compiled DLL; later standard test and tool execution succeeded.
  The user confirmed on 2026-10-12 that Smart App Control is explicitly off.
  Native Windows startup has passed; the earlier local blocker is resolved.
- Import parser support is limited to validated source layouts. OCR, encrypted
  PDF handling and broad reissued/overlapping-statement conflict resolution are
  deferred; unsupported/conflicting imports stop for investigation.
- Managed source access opens the OS PDF viewer and displays a page reference;
  there is no embedded PDF renderer.
- Plans start empty and are created directly in the app with effective dates.
  Monthly equivalents, recurrence intervals,
  payment amounts and known next-payment dates are supported; automated
  future-payment alerts are not implemented.
- Brokerage/savings holdings, card purchase details and cash expenditure detail
  remain incomplete when source statements are absent. Savings are unverified
  asset movements, not a complete investment valuation.
- Payroll correction clearing captures settlement effects and source periods;
  full retroactive gross-component restatement is not implemented.
- Rules can be added and applied to unresolved records. Conflicting priorities
  remain unresolved. Advanced rule editing and provenance comparison UI are
  future refinements.
- No automatic managed-document deletion/retention workflow yet.
- Future receipt/product tracking and the ad hoc analysis workspace remain
  roadmap items, as agreed.

## Try the application

On this workspace's Windows machine, run `./Run-Sponde.ps1` to open the prepared
private dataset. To start empty, run the published executable without
`--data-dir`, then import from the UI. See README.md for build/run commands.
The latest PDF-only portable dataset is `.local/pdf-data/verified.spondebackup`.
Manual backup/restore is the supported way to move data between machines.
