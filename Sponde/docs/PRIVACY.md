# Repository privacy policy

Only source code, general format requirements, synthetic fixtures and public
project identity belong in this repository. Do not commit names of people,
employers, addresses, account/tax/employee identifiers, email addresses, private
paths, document filenames, extracted text, real amounts, corpus statistics or
reconciliation results. This includes implementation constants, comments,
planning documents, test assertions, commit messages and build artifacts.

The GitHub repository namespace and contributor pseudonym are public project
identity. Package attribution uses `Sponde contributors`. Git authors should use
a pseudonym and a GitHub noreply address. Dependency license notices retain their
upstream authors' public attribution; those are unrelated to private finance data.

PDF adapters recognize public layout markers, column structure and arithmetic.
They read all owner, account, counterparty, period and amount values from the
selected document at runtime. No employee or employer names, meal-provider names,
owned IBANs, personal account nicknames or merchant-specific categorization rules
are embedded. User classification rules live in the local database.

Payroll post-net adjustments are parsed structurally between net and payout rows.
`PostNetDeductions` retains the historical JSON key `Canteen` for existing local
datasets and backups; it represents general post-net adjustments. Public tests
use invented round amounts, generic names and dates from a synthetic calendar.
Documents used privately for validation are not test fixtures and their details
must not enter documentation or logs.

`packaging/Check-RepositoryPrivacy.ps1` checks tracked files for private document
types, databases, personal filesystem paths, credentials, non-example emails and
checksum-valid bank identifiers. CI runs it on Windows and Linux. This catches
recognizable patterns; it cannot prove absence of all personal information.
Manual source/document review remains required, especially for parser literals
and copied source details. Error messages report file and issue type, never the
sensitive matched value.

Before sharing changes, inspect the complete staged diff and commit metadata.
Do not upload private debug logs or `.local` content. Package builds consume only
published application binaries and dependency notices. Real data stays in the
OS local data directory and in user-managed backups.

If information was committed, editing the current file is insufficient. Audit
reachable historical objects, branches, tags, pull-request views, Actions logs
and artifacts. Remove affected artifacts and prepare a sanitized history in an
isolated mirror. Replacing published Git history changes commit IDs and requires
explicit approval (received for this cleanup on 2026-10-12). Existing clones must be replaced or carefully cleaned; they
must not push the old history back. GitHub can retain cached commit/PR references
after a force-push; removing those may require GitHub Support. Do not claim that
earlier public copies can be recalled or that exposure has been undone.
