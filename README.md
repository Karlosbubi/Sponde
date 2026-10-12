# Sponde

Offline personal budgeting and accounting for Windows and Linux, built with
C#/.NET and Avalonia. Import bank statements and payslips from PDF, review
transactions, plan budgets, keep a balanced ledger, and move local data with
manual backup/restore. The example Excel workbook is a UI reference only.

Download Windows MSI and Fedora RPM installers from
[GitHub Releases](https://github.com/Karlosbubi/Sponde/releases). Both include
.NET. Packages are currently unsigned; release assets include SHA-256 checksums.

- [Build, run and application documentation](Sponde/README.md)
- [Plan](Sponde/docs/PLAN.md) and [decisions](Sponde/docs/DECISIONS.md)
- [Architecture](Sponde/docs/ARCHITECTURE.md) and [status](Sponde/docs/STATUS.md)
- [Packaging, releases and feature-branch policy](Sponde/docs/RELEASING.md)

Development uses feature branches and pull requests to protected `main`.
CI tests Windows and Linux, builds and installs both packages, and publishes
a release after successful merges. Real financial documents and datasets
are kept outside version control and release assets.
