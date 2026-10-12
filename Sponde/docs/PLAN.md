# Sponde: discovery and implementation plan

Status: implementation authorized; initial application implemented. See STATUS.md
for verification evidence and remaining platform acceptance.
Updated: 2026-10-12.

## Confirmed direction

- Desktop GUI for Windows 11, Fedora and openSUSE Slowroll.
- C# + Avalonia for the entire application, including bookkeeping and analysis.
- Ledger records gross salary and payroll deductions; the main budget reports
  net bank receipts. Link payroll and bank evidence without duplicate income.
- Personal budgeting interface with quality bookkeeping in the background.
- App-owned, durable transaction database for future analysis.
- Requirement change D023: no Excel import. The existing workbook is only a
  reference for the target UI and analysis. Plans, categories and notes are
  managed in Sponde; financial observations are imported from PDFs.
- Support at least N26, VR and payroll PDFs through content-based, extensible
  format adapters and shared extraction/validation infrastructure.
- Import bank statement PDFs and payslip PDFs from the existing finance folder.
- Local database; manual backup/restore between machines.
- Offline-only application: no online component and no application-level
  encryption for local data.
- Generic Linux desktop deployment; distro-specific tailoring is not a planning
  prerequisite. Validate GUI/native integration on Fedora and Slowroll.
- Clean architecture, functional influences, high engineering standards.
- Planning sign-off received on 2026-10-12: "Great. I hereby sign off. Implement
  our current plan." The following implementation milestones are authorized.

## Evidence and limitations

The supplied finance folder was inspected read-only. No financial source files
were changed or copied into the repository. Do not commit personal documents,
extracted text, identifiers, financial amounts or real-data test fixtures.
The source location remains user configuration, never a hardcoded runtime path.

Only general format and product requirements are retained here. Source inventories,
periods, record counts, classifications and financial results stay private.

- The example workbook illustrates plans, actuals, reconciliation and trends.
- Supported PDF layout families are N26 (including named Spaces), VR and payroll.
- Text extraction is the initial approach; scans require a separate OCR capability.
- Multi-page statements and retroactive payroll correction pages must be handled.
- Preserve the distinction between review and unknown classifications.

Transaction columns include booking date, account, counterparty, amount,
type, category, classification status, note, source document, PDF page,
purpose, value date, running balance and bank category. Derived expense,
income and savings columns should become projections, not imported facts.

Cached workbook totals are not independent validation. Reconcile parsed statements
and payroll arithmetically; never publish a user's reconciliation results.

Bank extraction observations: N26 uses explicit signs and full dates; VR uses
S/H debit/credit indicators, abbreviated dates and wrapped descriptions.
Statement headers, carry-forward balances and notices must not become entries.
Section boundaries matter when one PDF contains multiple accounts.

Existing repository assets: .NET 10 Hello World console project; parent Docs
and Utils contain a PostgreSQL shopping/product/cart model. The user has
confirmed detailed shopping tracking and receipt ingestion as future feature
ideas. Preserve these assets as a design reference for that extension; they
do not replace the finance ledger. The Git
root is the parent folder; this plan is within the writable solution directory.
No applicable AGENTS.md was found in the inspected project tree. Git status
could not be obtained with the host's default Git invocation, which reported a
failure to change to the parent repository directory; no Git mutation attempted.

## Workbook behavior to preserve

- Report actual cash flow by booking date; retain value date separately.
- Refunds reduce the corresponding spending category.
- Own-account transfers do not inflate income or spending, including transfers
  whose two sides fall in different months.
- Savings contributions and withdrawals are distinct from consumption.
- Current plans include monthly equivalents of annual subscriptions. Actuals
  show real payment dates. Missing plan amounts remain missing, not zero.
- Cancelled subscriptions retain historical actuals.
- Payslips explain bank income; importing one must not add another salary receipt.
- Payroll corrections affect payout in their settlement month; retain the
  original earnings period for explanation and avoid importing year-to-date
  totals as monthly amounts.
- Payroll post-net adjustments are already applied before bank payout.
- Included insurance components must not be counted twice.
- Cash withdrawals and aggregated credit-card settlements can have unknown
  final purposes. Missing card/cash detail must remain explicit.
- Absent brokerage or savings statements limit coverage. Do not claim complete
  wealth, investment returns or full reconciliation of unobserved accounts.

## Proposed first release

1. Configure source and local data folders; import with a preview.
   Allow multi-file PDF selections with per-file previews and outcomes. Detect
   format, account/type and period from content; original filenames are provenance.
   Process bank documents before payroll regardless of selection order. Each file
   commits atomically; unrelated valid files can succeed while another fails.
   Cancellation leaves completed files intact and reports unfinished files clearly.
2. Review unsupported files, extraction problems, duplicate candidates and
   unclassified transactions; view source page evidence.
3. Search/filter transactions; edit categories and split a transaction with an
   explanation, preserving the imported observation.
4. Reconcile each covered account and statement period.
5. Manage effective-dated income/expense plans and recurring payments.
6. Show monthly budget versus actual, net consumption, surplus before savings,
   savings movements, cash balance movement and category trends.
7. Explain payroll and match payouts to bank transactions.
8. Export analysis to CSV and make/restore verified portable backups.

Proposed screens: Overview, Transactions, Import & Review, Budget,
Accounts & Reconciliation, Payroll, Settings & Backups. A ledger inspection
view can expose postings without making everyday users enter debits/credits.
Keyboard navigation, readable German number/date formatting, scalable text,
and explicit empty/error/loading states are requirements.

Accepted first-UI requirement: cover the workbook's Overview (main page),
rather than stopping at transaction/import screens. Provide month selection,
income, net expenses, surplus before savings, net savings, account movement,
notable/one-off spending, unresolved classification count, monthly trends,
cumulative surplus and leading spending categories. Preserve the meaning of
the workbook's measures; adapt its presentation to a desktop dashboard rather
than copying spreadsheet cell layout. Support drill-down to contributing
transactions and show account/period coverage beside totals.

Build the Overview from the app-owned database populated directly from PDFs.
Do not import spreadsheet rows, cached formulas, categories, notes or plans.
Show unresolved classifications and missing plans explicitly. The example's
special-case spending labels are not hardcoded product requirements; use general
metrics and category drill-down instead. Bank imports must work from an empty
database; payroll attaches its explanation to a matching bank payment.

Defer automatic synchronization, online banking, tax filing, investment
valuation, generic plugin loading, receipt/product tracking and cloud AI.
OCR is a later capability unless actual imports require it. Unsupported scans
must produce a clear review item rather than fabricated transactions.

### Future extension: shopping detail and receipts

User-requested roadmap idea: ingest receipts and track purchases at item level.
Keep the existing product, store, cart, purchase and price concepts as design
inputs. Reassess the schema when implementing this feature rather than assuming
its current PostgreSQL form is the final storage model.

Model receipt documents and purchase items as supporting detail linked to
existing financial transactions. Receipt ingestion must not create a second
expense when the corresponding bank payment is already recorded. Allow
multiple receipts/payments per purchase and item-level category allocations,
with explicit reconciliation of totals, discounts, taxes and rounding. Preserve
original receipt evidence and uncertain extraction for review. Cash purchases
can later create a reviewed cash-account entry when no bank payment exists.

Keep stable transaction IDs and source-document links in the initial design so
this extension can attach later. Do not build product tables, receipt OCR or
matching workflows before this feature is scheduled and its requirements are
agreed. Price history and product comparisons are possible follow-on features,
not yet agreed scope.

### Future extension: analysis workspace

User-requested roadmap idea: a workbook/BI-style workspace for ad hoc questions
about the data, separate from normal budget considerations. Example: "Which
cheese brand do I buy most?" This is a capability request, not a decision to
embed or depend on Microsoft Power BI. The analysis experience must remain
compatible with the agreed Windows and Linux targets.

Proposed features for later discussion: saved analyses, filters, grouping,
aggregations, tables/charts and export. Decide visual query building, SQL and
natural-language querying when this extension is scheduled; none is assumed
to be required yet. Analyses read data without changing ledger postings or
budget rules. Use versioned read models so exploratory questions do not couple
the UI to internal storage tables.

Product-level questions depend on receipt ingestion and reviewed product
normalization: product identity, brand, product type, quantity, unit and package
size. Bank statements alone cannot answer the cheese example. Retain receipt
descriptions and provenance alongside normalized attributes, and expose missing
brands or incomplete receipt coverage. "Most" must specify the measure (purchase
frequency, units, weight or spending) and time range. Saved analyses retain
those definitions so their results are reproducible and comparable.

Keep this on the future TODO list alongside receipt/shopping detail. The first
release supplies durable IDs, provenance and export; implementation of the
analysis workspace remains deferred.

## Technology decision: C# + Avalonia

Accepted by the user after discussing the alternatives. Use C# throughout the
domain, application, infrastructure and GUI. Functional design uses immutable
records, explicit outcomes and pure domain functions. No Rust interop is planned;
reconsider a focused component only if measured future needs justify it.
The comparison below is retained as decision context.

| Criterion | C# + Avalonia | Rust + iced |
| --- | --- | --- |
| Starting point | Fits current .NET 10 skeleton | Requires replacement/addition of Rust workspace |
| Functional style | Immutable records, pure functions, explicit result types; discipline needed around mutable UI models | Enums, ownership, Result and message/update architecture fit naturally |
| UI approach | XAML and bindings; thin view models or explicit state transitions | Elm-inspired state/message/update/view model |
| Finance/import ecosystem | Candidate managed PDF, spreadsheet and SQLite libraries to evaluate | Candidate Rust PDF, spreadsheet and SQLite crates to evaluate |
| Maintenance burden | Familiar .NET tooling; avoid excessive MVVM/framework dependencies | Strong compiler constraints; assess GUI and PDF integration effort |
| Distribution | Self-contained builds; validate native GUI dependencies | Native binaries; validate rendering and any PDF/OCR dependencies |
| Platform proof | Windows/Linux documented; Slowroll requires our own validation | Cross-platform library; exact target desktop behavior requires validation |

C# + Avalonia fits the existing project and document/table-heavy workflow.
One language simplifies debugging, packaging and maintenance. Correctness relies
on domain rules, exact money arithmetic and verification; splitting the UI and
ledger across C# and Rust adds interop without a demonstrated benefit.

After approval to implement experiments, evaluate: a transaction table with
sorting/filtering and keyboard use, background import with cancellation, PDF
text extraction, SQLite migration and packaging. Use synthetic data first.
Use standard Windows/Linux desktop builds. Exact Linux distro versions are not
a prerequisite for starting implementation. Validate rendering, native library
dependencies, dialogs and packaging on Fedora and Slowroll before release;
record tested environments then. Confirm CPU architectures when packaging.

Official references checked 2026-10-12:

- https://docs.avaloniaui.net/docs/supported-platforms
- https://docs.avaloniaui.net/docs/platform-specific-guides/linux
- https://book.iced.rs/architecture.html
- https://www.sqlite.org/onefile.html

Avalonia documents X11 and an opt-in native Wayland package from 12.1.0.
It lists Fedora versions in its support matrix, but does not list Slowroll as
a tested distribution. Pin versions after validation; do not assume a generic
Linux CI build proves desktop support on all requested systems.

## Proposed architecture

One desktop process and one local data store. Four coarse boundaries are
enough; avoid microservices, generic repository frameworks or event sourcing.

- Domain: immutable finance types, ledger balancing, classification, date and
  money rules, reconciliation and report calculations. No GUI/SQL/PDF access.
- Application: import, review, commit, reconcile, budget, backup use cases;
  orchestrates effects through narrow ports and returns explicit outcomes.
- Infrastructure: PDF extraction, an extensible format registry, bank/payroll parsers, SQLite,
  filesystem, migrations, backup and platform integration.
- Desktop: views and presentation state; invokes application use cases.

Dependency rule: Desktop and Infrastructure depend inward. Domain never
references them. Inject ports at the composition root. Use pure transformations
for parsing normalized text, validation, categorization and calculations;
perform I/O at the edges. Model failure explicitly and never silently coerce
bad input into a zero amount. Interfaces exist at actual effect boundaries.

Import state: discovered -> identified -> extracted -> parsed -> validated ->
reviewed -> committed, with failed/unsupported/cancelled outcomes. Each stage
retains diagnostics. Background work must not block the GUI thread.

## Proposed persistence and ledger model

SQLite with explicit, versioned migrations and foreign keys. No database
server is required. EUR integer minor units for settled money; higher-precision
decimal arithmetic for rates/intermediate payroll calculations with explicit
rounding. Never binary floating-point money. Currency is always stored.

Proposed records:

- SourceDocument: content hash, original filename, document kind, managed
  relative location, import metadata and parser version.
- ImportRun and SourceObservation: document/page/section/row provenance,
  extracted facts, diagnostics and reproducible parser output.
- Account: ledger role (asset/liability/income/expense/equity), currency,
  owned-account relationships and statement coverage.
- Statement: account, date range, opening/closing balances and available
  control counts/totals. Coverage is explicit, not inferred from folder names.
- JournalEntry and Posting: balanced financial facts, source links, review
  state and correction relationships.
- Category and Allocation: budget/report classification independent of bank
  descriptions; splits must sum exactly to the transaction amount.
- TransferMatch: two source observations, matched postings or clearing state.
- BudgetVersion and BudgetLine: effective periods, recurrence, annual amounts,
  account preference, cancellation and explicit unknown values.
- Payslip, PayrollLine and PayrollMatch: pay period, correction period,
  components, payout and supporting bank entries.
- ClassificationRule and ChangeRecord: priority, effective version,
  user overrides and who/when/why history (single local user initially).

Proposed bookkeeping conventions (need user agreement):

- Every posted journal entry balances exactly per currency. Opening account
  balances post against opening equity; absent opening balances stay flagged.
- Imported debits/credits map deterministically to postings. Unclear purposes
  use suspense, with visible unresolved status, rather than guessed expenses.
- A transfer leg uses transfer clearing until its other leg is linked. Linking
  replaces/reverses the temporary treatment with traceable corrections and
  preserves each account's original dates. Do not require same-month matching.
- Saving into an owned asset is an asset transfer. Where the destination is not
  observed, mark it unreconciled; external holdings are not verified balances.
- Payroll bank receipt initially has one income entry. A reviewed payroll
  breakdown replaces/reclassifies that treatment with gross income, deductions
  and payout without doubling income. Cash-flow reporting continues to use
  bank cash events. The user has accepted gross salary and payroll deductions
  in the ledger with net bank receipts in the main budget. Detailed posting and
  settlement rules still require design and validation.
- Unknown credit-card settlements and cash withdrawals use clearing accounts
  if underlying details are missing. Show observed cash outflow and its unknown
  purpose; do not misrepresent complete consumption-level bookkeeping.
- Corrections preserve history; never silently overwrite imported facts or
  let reimports erase a user's categorization.

Balanced postings ensure arithmetic consistency, not proof of full coverage
or correct classification. Reports must expose those separate properties.

## Import correctness and migration strategy

Use bank-specific versioned parsers, not one universal regex. Identify layouts
from document content. Preserve German signs, decimal separators, booking vs
value dates, document section/account identity, and cross-page descriptions.

Deduplicate identical files by content hash. For overlapping/reissued
statements, match source observations using account, bank references and
statement context. Date + amount + merchant alone is not a unique key;
legitimate repeated payments must survive. Ambiguous matches require review.
Reimport must be idempotent, including Space transactions, corrections and
previous manual edits. Commit a validated statement atomically; a mismatch
can stay staged for investigation without pretending it reconciled.

No workbook migration or Excel import. Recognize PDFs from content rather than
filenames, directories, employer names or user names. Register additional format
adapters without changing orchestration. Shared checks cover amount/date parsing,
observation identities, statement reconciliation and unsupported/ambiguous input.
Learn owned account identifiers from bank statement headers; use those to explain
own transfers without hardcoded personal names. Preserve uncertain classifications
for review, and do not manufacture spending categories from statement signs alone.

## Privacy, durability and recovery

Offline-only. No online component, telemetry or document upload. Logs contain technical
diagnostics without full PDF text, salary data or financial identifiers.
Managed original documents retain provenance even if the input folder moves.
The user has declined application-level encryption for local data. Managed
document retention remains a separate design decision.

Keep the active database in the OS local application-data directory, outside
live OneDrive synchronization. A portable backup includes a consistent SQLite
snapshot, managed documents, configuration, schema version and checksums.
Use the database backup API, not a live file copy that can omit journal/WAL
state. Validate restore in a temporary folder before replacing the active data;
retain the previous database for recovery. Only one machine is active for a
given dataset; restoring an older backup must warn about rollback of history.

No at-rest encryption layer is planned. Password-encrypted backups are also
excluded from the proposed first milestone; the user explicitly declined local
data encryption, but backup encryption has not been separately answered.
A backup is accepted only after a successful restore test.

## Engineering and acceptance criteria

- Pinned toolchain/dependencies, lock files, reproducible documented build.
- Formatter, analyzer/linter and warnings policy in CI; license/dependency
  review before selecting PDF and GUI dependencies.
- Synthetic and structurally anonymized fixtures in Git. Real source corpus
  stays local and is used only for authorized private validation.
- Unit/property tests for balancing, money precision, allocations, transfers,
  refunds and payroll corrections. Test useful invariants and edge cases.
- Parser fixture tests for all known layouts, correction pages, Space sections,
  repeated same-day payments, broken input and missing text.
- Integration tests for atomic imports, rollback, migrations, reimport/manual
  edits, backup/restore and unsupported newer schema versions.
- GUI smoke testing on Windows 11, Fedora and Slowroll, including actual
  desktop sessions, HiDPI, keyboard navigation, dialogs and source viewing.
- Every imported covered statement satisfies opening + signed movements =
  closing exactly to the cent; compare counts where independently available.
- Import all observations from supported PDFs without reading Excel;
  preserve source pages, exact amounts and duplicate-payment multiplicity.
- Salary payout reconciliation and monthly report parity have independently
  explained differences. Repeated imports create no extra cash events.
- A user can import, resolve a review item, reconcile a month and restore a
  backup without accessing developer tools.

## Milestones and decision gates

0. Planning sign-off received; scope and implementation decisions are recorded
   in DECISIONS.md. See STATUS.md for delivered functionality and limitations.
1. After authorization, validate framework/PDF/storage feasibility and choose
   exact libraries and versions; record an accepted architecture decision.
2. Deliver one vertical slice: a bank statement -> reviewed balanced ledger ->
   reconciliation -> transaction UI -> verified backup. Include the first usable
   Overview dashboard populated exclusively through PDF imports.
3. Add the second bank and N26 Space; prove transfer and duplicate handling.
4. Complete PDF-only corpus verification, budget editing, classification
   rules and expanded monthly reports beyond the initial Overview.
5. Add payroll breakdowns, corrections and bank matching.
6. Validate packages and restore on all target systems; complete user acceptance.

Do not set delivery dates until framework and parser feasibility are measured.
Each milestone ends with a runnable result and observable acceptance evidence.

## Questions for the next discussion

These are refinements, not prerequisites for implementation. Proposed defaults:
German UI and EUR/de-DE formatting; create categories and plans in the app;
retain managed original PDFs until explicit deletion; carry forward the existing
payroll detail including bonuses/corrections; standard accessible desktop
controls; manual cash and detailed card workflows follow the first milestone.
Implementation sign-off was given on 2026-10-12. These refinements do not block
the authorized implementation; current choices and limitations are in STATUS.md.

1. Which payroll details should be visible beyond the agreed gross salary,
   deductions and net bank receipts (e.g. bonuses and correction periods)?
2. What presentation and accessibility preferences should guide the Avalonia UI?
3. Which category taxonomy and plan positions should be configured in the app?
4. German UI first? Which machines are available for acceptance? Record their
   desktop/session and CPU architecture during packaging and verification.
5. Should the app retain managed PDF originals? How long? Backup encryption is
   omitted from the proposed first milestone unless separately requested.
6. Are manual cash entries, credit-card detail and split transactions needed
   immediately? Additional statements can improve later coverage.

