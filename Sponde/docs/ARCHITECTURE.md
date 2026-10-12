# Architecture

Sponde is one desktop process with a local SQLite database and managed source
documents. C# is used throughout. There is no network service or telemetry.

| Project | Responsibility | Dependencies |
| --- | --- | --- |
| Sponde.Domain | Immutable finance records, exact EUR arithmetic, balancing, projections, budget selection and rule resolution | Standard library |
| Sponde.Application | Import/review/commit, classification, splitting, budget and recovery use cases; storage and reader ports; CSV projection | Domain |
| Sponde.Infrastructure | PdfPig extraction, extensible format adapters, bank/payroll parsers, SQLite migrations, provenance and backup implementation | Application/Domain |
| Sponde | Avalonia composition root and desktop presentation | Application; Infrastructure only at composition |
| Sponde.Tests | Synthetic domain, PDF pipeline, parser, persistence and headless GUI tests | Application, Infrastructure, Desktop |
| Sponde.Tools | Explicit private-corpus validation and diagnostics | Application/Infrastructure |

The initial UI is implemented with standard Avalonia controls in C#, rather
than XAML. Financial rules stay outside views. Work runs in the background;
imports check cancellation before committing, and a cancelled transaction rolls
back. Views are rebuilt from immutable dataset snapshots after writes.

## Financial semantics

Money is EUR in signed integer cents. Bank assets use positive debit postings.
Each journal entry balances exactly. Opening statement balances have separate
balanced equity entries, selected from the earliest covered statement per
account. They do not appear as budget income.

Own-account transfers use clearing postings and can be explicitly linked.
Cross-month booking dates remain unchanged. A savings destination not covered
by statements remains an unverified asset; its posting is not evidence of a
fully reconciled holding. Unknown categories use suspense.

Expense allocations sum to the bank event and produce category-specific
postings, without creating extra cash events. Classifications keep an immutable
source observation and an edit history. Splits cannot be overwritten through
ordinary category editing.

Reviewed payroll replaces the matched bank payment's net-income postings with
gross income, statutory deductions, meal deductions and correction clearing.
The budget still reads the original cash payment. Retroactive pages retain the
earnings period and source page. Correction clearing describes the settlement
effect; it does not claim a fully reconstructed historical gross-payroll ledger.

## Persistence

Explicit schema migrations use SQLite transactions. Transactions and source
observations retain typed immutable records as JSON; postings, identities,
foreign keys, matching, evidence and version keys are relational. Future
analysis can add versioned query views or normalized projections without making
the UI depend on raw storage layout. All settled money is stored as integer
cents, including ledger postings.

Content hashes detect exact reimports. A reissued PDF overlapping prior history
must agree in account-section cardinality and transaction date/amount multiplicity.
Ambiguous overlap is rejected instead of guessed; current manual edits remain.
General overlap resolution for reissued statements from changed layouts remains
a limitation. The old spreadsheet bootstrap path has been removed.

There is no Excel reader or import port. A format registry selects exactly one
PDF adapter using content signatures. Unsupported or ambiguous documents remain
uncommittable. Shared normalization, date/money helpers and reconciliation checks
serve all formats; format-specific layout interpretation stays in each adapter.
New adapters implement IPdfFormatParser and can be injected into StatementReader.
Own transfers use account identifiers discovered in PDFs, not personal names.

Multi-file import is an application use case. Preview derives display labels from
parsed accounts/periods, detects identical content and projects statement coverage
once per account/period so reissued documents do not duplicate payroll candidates.
Commit orders banks before payroll and rechecks payroll against actually committed
payments. Files commit independently with explicit outcomes; cancellation preserves
completed files. Original filenames remain provenance rather than format identity.

Backups use SQLite's backup API, managed document copies and a checksum
manifest. Restore validates paths, hashes, schema compatibility, foreign keys,
database integrity and ledger balancing before replacement. The current state
is backed up first. Source evidence is retained until explicitly managed by a
future retention feature; no automatic deletion policy is implemented.

## Dependencies

- Avalonia.Desktop and Fluent theme 12.1.2: MIT.
- Microsoft.Data.Sqlite 10.0.11: MIT.
- SQLitePCLRaw.bundle_e_sqlite3 3.0.5: Apache-2.0; native SQLite is public domain.
- PdfPig 0.1.12: Apache-2.0.
- xUnit, test SDK/adapter and Avalonia.Headless are development dependencies.

Versions and transitive dependencies are recorded in package lock files. NuGet
audit remains enabled and warnings are errors. The initially vulnerable SQLite
native dependency was replaced rather than suppressing the advisory.
