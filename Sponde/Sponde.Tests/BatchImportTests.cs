using System.Collections.Immutable;
using System.Globalization;
using Sponde.Application;
using Sponde.Domain;
using Sponde.Infrastructure;
using Xunit;

namespace Sponde.Tests;

public sealed class BatchImportTests
{
    [Fact]
    public void MixedSelectionImportsBankBeforePayrollRegardlessOfSelectionOrder()
    {
        using var f = new Fixture();
        var bank = f.Bank("random-a.pdf", new(2000, 9, 3), 70000, 0, 70000);
        var payroll = f.Payroll("random-b.pdf");
        var batch = f.Service.PreviewStatements([payroll, bank]);
        Assert.Equal(2, batch.ReadyCount);
        Assert.Equal("Entgeltnachweis · 2000-09", batch.Items[0].Preview.DisplayName);
        Assert.Equal("N26 · 2000-09", batch.Items[1].Preview.DisplayName);
        var result = f.Service.CommitStatements(batch);
        Assert.All(result.Files, r => Assert.Equal(FileImportOutcome.Imported, r.Outcome));
        Assert.Equal("random-a.pdf", result.Files[0].OriginalName);
        Assert.Single(f.Service.Read().Transactions); Assert.Single(f.Service.Read().Payslips);
        Assert.Empty(f.Service.Read().Budget);
        Assert.Equal(70000, Reports.Month(f.Service.Read().Transactions, new(2000, 9, 1)).Income);
    }

    [Fact]
    public void UnsupportedAndUnreadableFilesDoNotPreventValidImports()
    {
        using var f = new Fixture();
        var bank = f.Bank("good.pdf", new(2000, 9, 3), 70000, 0, 70000);
        var unknown = f.Write("unknown.pdf", "An unsupported document");
        var missing = Path.Combine(f.Root, "missing.pdf");
        var batch = f.Service.PreviewStatements([unknown, missing, bank]);
        Assert.Equal(1, batch.ReadyCount);
        var result = f.Service.CommitStatements(batch);
        Assert.Single(result.Files.Where(r => r.Outcome == FileImportOutcome.Imported));
        Assert.Equal(2, result.Files.Count(r => r.Outcome == FileImportOutcome.Skipped));
        Assert.Single(f.Service.Read().Transactions);
    }

    [Fact]
    public void IdenticalContentWithDifferentNamesIsImportedOnlyOnce()
    {
        using var f = new Fixture();
        var bank = f.Bank("original.pdf", new(2000, 9, 3), 70000, 0, 70000);
        var copy = Path.Combine(f.Root, "totally-different-name.pdf"); File.Copy(bank, copy);
        var batch = f.Service.PreviewStatements([bank, copy]);
        Assert.Equal(1, batch.ReadyCount); Assert.True(batch.Items[1].Duplicate);
        Assert.Equal(batch.Items[0].Preview.DisplayName, batch.Items[1].Preview.DisplayName);
        var result = f.Service.CommitStatements(batch);
        Assert.Single(result.Files.Where(r => r.Outcome == FileImportOutcome.Imported));
        Assert.Single(result.Files.Where(r => r.Outcome == FileImportOutcome.AlreadyPresent));
        Assert.Single(f.Service.Read().Transactions);
    }

    [Fact]
    public void ReimportingMixedBatchDoesNotDuplicatePaymentsOrPayroll()
    {
        using var f = new Fixture();
        var bank = f.Bank("a.pdf", new(2000, 9, 3), 70000, 0, 70000); var payroll = f.Payroll("b.pdf");
        f.Service.CommitStatements(f.Service.PreviewStatements([payroll, bank]));
        var repeated = f.Service.CommitStatements(f.Service.PreviewStatements([bank, payroll]));
        Assert.All(repeated.Files, r => Assert.Equal(FileImportOutcome.AlreadyPresent, r.Outcome));
        Assert.Single(f.Service.Read().Transactions); Assert.Single(f.Service.Read().Payslips);
    }

    [Fact]
    public void FailedBankDoesNotPermitPayrollBasedOnlyOnItsUncommittedPreview()
    {
        using var f = new Fixture();
        var bank = f.Bank("salary.pdf", new(2000, 9, 3), 70000, 0, 70000);
        var other = f.Bank("other.pdf", new(2000, 10, 3), -500, 70000, 69500);
        var payroll = f.Payroll("pay.pdf");
        var batch = f.Service.PreviewStatements([payroll, bank, other]); Assert.Equal(3, batch.ReadyCount);
        File.AppendAllText(bank, "changed after review");
        var result = f.Service.CommitStatements(batch);
        Assert.Equal(2, result.Files.Count(r => r.Outcome == FileImportOutcome.Failed));
        Assert.Single(result.Files.Where(r => r.Outcome == FileImportOutcome.Imported));
        Assert.Empty(f.Service.Read().Payslips); Assert.Single(f.Service.Read().Transactions);
        Assert.Equal(-500, f.Service.Read().Transactions.Single().Amount.Cents);
    }

    [Fact]
    public void CancelledBatchProducesExplicitResultsWithoutCommittingFiles()
    {
        using var f = new Fixture();
        var bank = f.Bank("salary.pdf", new(2000, 9, 3), 70000, 0, 70000); var payroll = f.Payroll("pay.pdf");
        var batch = f.Service.PreviewStatements([bank, payroll]);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var result = f.Service.CommitStatements(batch, cancellation.Token);
        Assert.All(result.Files, r => Assert.Equal(FileImportOutcome.Cancelled, r.Outcome));
        Assert.Empty(f.Service.Read().Transactions); Assert.Empty(f.Service.Read().Payslips);
    }

    [Fact]
    public void SameFilenameInDifferentFoldersDoesNotMergeDifferentPeriods()
    {
        using var f = new Fixture();
        var september = f.Bank("first/statement.pdf", new(2000, 9, 3), 70000, 0, 70000);
        var october = f.Bank("second/statement.pdf", new(2000, 10, 3), -500, 70000, 69500);
        var result = f.Service.CommitStatements(f.Service.PreviewStatements([september, october]));
        Assert.All(result.Files, r => Assert.Equal(FileImportOutcome.Imported, r.Outcome));
        Assert.Equal(2, f.Service.Read().Transactions.Length); Assert.Equal(2, f.Service.Read().Statements.Length);
    }

    [Fact]
    public void RenamedReissuedStatementDoesNotDoubleCountProjectedPayrollIncome()
    {
        using var f = new Fixture();
        var original = f.Bank("first-name.pdf", new(2000, 9, 3), 70000, 0, 70000);
        var reissued = f.Write("different-name.pdf", File.ReadAllText(original) + "\nReissued footer");
        var payroll = f.Payroll("payout.pdf");
        var batch = f.Service.PreviewStatements([payroll, original, reissued]);
        Assert.Equal(3, batch.ReadyCount);
        var result = f.Service.CommitStatements(batch);
        Assert.DoesNotContain(result.Files, r => r.Outcome == FileImportOutcome.Failed);
        Assert.Single(f.Service.Read().Transactions); Assert.Single(f.Service.Read().Payslips);
    }

    [Fact]
    public void DamagedPdfBecomesAFileIssueRatherThanAbortingBatchPreview()
    {
        using var f = new Fixture(); var damaged = f.Write("damaged.pdf", "not a PDF file");
        var service = new FinanceService(f.Store, new StatementReader());
        var batch = service.PreviewStatements([damaged]);
        Assert.Single(batch.Items); Assert.False(batch.CanCommit); Assert.NotEmpty(batch.Items[0].Preview.Issues);
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "sponde-batch-" + Guid.NewGuid().ToString("N"));
        public SqliteFinanceStore Store { get; }
        public FinanceService Service { get; }
        public Fixture() { Directory.CreateDirectory(Root); Store = new(Root); Service = new(Store, new TextFixtureReader()); }
        public string Write(string name, string contents) { var path = Path.Combine(Root, name); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, contents); return path; }
        public string Bank(string name, DateOnly date, long amount, long opening, long closing)
        {
            string Euro(long cents) => (cents / 100m).ToString("+0.00;-0.00;0.00", CultureInfo.GetCultureInfo("de-DE"));
            var month = new DateOnly(date.Year, date.Month, 1);
            return Write(name, $"""
                Beschreibung Verbuchungsdatum Betrag
                Synthetic payment
                Wertstellung {date:dd.MM.yyyy}
                {date:dd.MM.yyyy} {Euro(amount)}€
                Kontoauszug Nr. sample
                {month:dd.MM.yyyy} bis {month.AddMonths(1).AddDays(-1):dd.MM.yyyy}
                Dein alter Kontostand {Euro(opening)}€
                Dein neuer Kontostand {Euro(closing)}€
                """);
        }
        public string Payroll(string name) => Write(name, """
            Entgeltnachweis
            für September 2000
            Gesamtbrutto (EBeschV) 1.000,00 9.000,00
            Lohnsteuer 100,00 100,00 900,00
            Kirchensteuer 10,00 10,00 90,00
            Krankenversicherung 50,00 50,00 450,00
            Rentenversicherung 90,00 90,00 810,00
            Arbeitslosenversicherung 20,00 20,00 180,00
            Pflegeversicherung 30,00 30,00 270,00
            Gesetzliches Netto (EBeschV) 700,00
            Überweisung 700,00 EUR
            """);
        public void Dispose() { Store.Dispose(); Directory.Delete(Root, true); }
    }
    private sealed class TextFixtureReader : IStatementReader
    {
        public ImportPreview Read(string path, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return StatementReader.Parse(path, Evidence.Hash(path), [new(1, File.ReadAllText(path))]);
        }
    }
}
