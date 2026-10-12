# Sponde decision register

Updated: 2026-10-12. Accepted means explicitly requested/answered by the user;
proposed means awaiting discussion. Implementation sign-off received 2026-10-12.

| ID | Status | Decision | Rationale / consequences |
| --- | --- | --- | --- |
| D001 | Accepted | Plan together before implementation; persist plan and decisions | Discovery and documentation only for now |
| D002 | Accepted | Windows 11, Fedora and openSUSE Slowroll desktop GUI; generic Linux deployment | Exact distro versions do not block implementation; smoke-test GUI/native integration on target desktops before release |
| D003 | Accepted, superseding comparison-only status | C# + Avalonia for the entire application, including bookkeeping and analysis | Immutable records and pure domain functions; one language simplifies debugging and packaging; no Rust interop planned |
| D004 | Accepted | Personal budget interface with quality bookkeeping in background | Design a balanced ledger and simple budget projections; detailed ledger policy remains proposed |
| D005 | Accepted | Local database and manual backup/restore between machines | No automatic synchronization in initial scope |
| D006 | Accepted | Own transaction database; import bank and payslip PDFs | Preserve evidence and enable future analysis |
| D007 | Accepted | Clean architecture with functional influences and high engineering standards | Pure domain transformations and effects at boundaries |
| D008 | Implemented under sign-off | SQLite and one desktop process | Small deployment with transactional durability; no server administration |
| D009 | Implemented under sign-off | Immutable observations plus balanced postings and traceable corrections | Protect provenance, user edits and reconciliation |
| D010 | Implemented under sign-off | Text-first, bank-specific versioned PDF parsers; OCR deferred | All 18 supplied PDFs parsed and validated; new layouts still require validation |
| D011 | Superseded by D023 | Former workbook migration | Removed from code and UI; existing prototype data retained separately |
| D012 | Offline accepted; managed originals proposed | No online component; managed original PDFs outside Git | Private evidence remains available after source-folder changes if managed copies are adopted |
| D013 | Implemented under sign-off | Versioned backup bundle with restore verification | Database and evidence travel together safely |
| D014 | Local-data policy accepted; backups proposed unencrypted | No application-level encryption for local data | Password-encrypted backups omitted from proposed first milestone; not separately decided by the user |
| D015 | Accepted direction; detailed posting design open | Gross salary and payroll deductions in the ledger; net bank receipts in the main budget | Link payslip and bank evidence without duplicate income; incomplete card/cash coverage policy remains open |
| D016 | Accepted roadmap idea | Future detailed shopping tracking and receipt ingestion; retain existing shopping schema as reference | Deferred from first release; link receipt/item detail to ledger transactions without double-counting expenses |
| D017 | Accepted roadmap idea | Separate workbook/BI-style analysis workspace for ad hoc questions beyond budgeting | Example: most-purchased cheese brand; needs receipt/product data and explicit measures; query interface and BI technology remain open |
| D018 | Accepted, clarified by D023 | Overview is inspired by the example workbook | Monthly metrics and trends remain; no workbook data, categories or plans are imported |
| D019 | Accepted | Implement the agreed plan | User sign-off: "Great. I hereby sign off. Implement our current plan." |
| D020 | Superseded by D023 | Former read-only OOXML adapter | Workbook reader and its tests removed; no Excel import remains |
| D021 | Implementation choice | Standard Avalonia Linux X11/XWayland backend for initial packages | Native Wayland opt-in remains deferred; Fedora/Slowroll native desktop acceptance outstanding |
| D022 | Confirmed local environment | User explicitly turned Smart App Control off on 2026-10-12 | Earlier development-build execution blocker resolved; application design unchanged |
| D023 | Accepted and implemented | No Excel import; workbook is only an example of the desired UI and analysis | Replaces workbook bootstrap/migration; transactions come from PDFs, plans are managed in Sponde; launcher uses a separate PDF-only dataset |
| D024 | Implementation choice under D023 | Extensible content-based PDF format registry with shared validation | N26, VR and payroll adapters; no filename conventions or hardcoded user-name classification; new adapters can be registered without changing import orchestration |
| D025 | Accepted and implemented | Select and import multiple PDFs together | Per-file previews/results, bank-before-payroll ordering, duplicate detection and cancellation; labels derive from document contents, original filenames remain as provenance |
| D026 | Accepted | Publish Windows MSI and latest-stable Fedora RPM through GitHub Actions after merges into main | Self-contained x64 packages, native install/start/uninstall checks, release checksums; see RELEASING.md |
| D027 | Accepted | Protect main and move development to feature branches | PRs and passing Windows/Linux/package checks required; solo-maintainer approval count remains zero |
| D028 | Implementation choice | MSI/RPM versions share a major/minor prefix and workflow run number | Stable upgrade identity, exact-commit release tags, unsigned packages until signing identities are configured |

## Explicit user responses

- Technology: initially "Decide after comparing the tradeoffs"; subsequently
  "then It's settled" in response to the C# + Avalonia recommendation for the
  whole application.
- Payroll: "Yes" to gross salary and payroll deductions in the ledger with
  net bank receipts in the main budget.
- Scope: "A bit of both, Personal budget with quality bookkeeping in the background".
- Data portability: "Local database, manual backup/restore between machines".
- Future feature: detailed shopping tracking, including receipt ingestion;
  keep the existing shopping schema in mind for this extension.
- Future feature: workbook/BI-style analysis for specific questions outside
  normal budget considerations, such as which cheese brand is purchased most.
- Local-data encryption is unnecessary for the user's scope; no online component.
- Standard Linux desktop distributions should share a generic deployment;
  avoid making exact distro versions a prerequisite for starting implementation.
- First usable UI must cover the main page of the Excel workbook.
- Implementation sign-off: "Great. I hereby sign off. Implement our current plan."
- Requirement change: no import of an existing budget spreadsheet. The current
  spreadsheet is an example of the target experience only. PDF import must
  support at least the three currently used formats and be as generic as practical.

See [PLAN.md](PLAN.md) for evidence, proposed architecture, milestones and
acceptance criteria. Update this register when a choice is made; retain reasons
and superseded decisions rather than rewriting history silently.
