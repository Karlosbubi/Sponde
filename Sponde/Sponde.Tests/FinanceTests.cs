using System.Collections.Immutable;
using System.IO.Compression;
using System.Text.Json;
using Sponde.Application;
using Sponde.Domain;
using Sponde.Infrastructure;
using Xunit;

namespace Sponde.Tests;

public sealed class FinanceTests
{
    private static Transaction Row(long cents, MovementKind kind = MovementKind.Expense, ReviewStatus review = ReviewStatus.Assigned) => new(Guid.NewGuid(), new(2000, 9, 3), "N26", "Synthetic merchant", new(cents), kind, "Food", review, "", "synthetic.pdf", 1, "Synthetic purpose", null, "");
    [Fact]
    public void MoneyRejectsFractionalCentsAndPreservesNegativeValues()
    {
        Assert.Throws<ArgumentException>(() => Money.FromEuros(0.001m));
        Assert.Equal(-12345, Money.FromEuros(-123.45m).Cents);
    }
    [Fact]
    public void EveryGeneratedMovementBalancesAndUnknownsUseClearing()
    {
        var random = new Random(7821);
        foreach (var kind in Enum.GetValues<MovementKind>())
            for (var i = 0; i < 100; i++)
            {
                var row = Row(random.Next(-1000000, 1000000), kind, ReviewStatus.Review);
                var entry = JournalEntry.For(row); entry.Validate();
                Assert.Equal(0, entry.Postings.Sum(p => p.DebitCents));
                Assert.Equal(row.Amount.Cents, entry.Postings[0].DebitCents);
            }
        Assert.Equal("Clearing:Unclassified", JournalEntry.For(Row(-500, review: ReviewStatus.Unknown)).Postings[1].Account);
        Assert.Throws<InvalidOperationException>(() => new JournalEntry(Guid.NewGuid(), new(2000, 1, 1), [new("a", 100), new("b", -99)]).Validate());
    }
    [Fact]
    public void TransfersSavingsAndRefundsHaveDifferentBudgetMeaning()
    {
        Transaction[] rows = [Row(10000, MovementKind.Income), Row(-3000), Row(500, MovementKind.Refund), Row(-2000, MovementKind.Savings), Row(-1000, MovementKind.Transfer)];
        var month = Reports.Month(rows, new(2000, 9, 1));
        Assert.Equal(10000, month.Income); Assert.Equal(2500, month.Expenses); Assert.Equal(2000, month.Savings);
        Assert.Equal(7500, month.Surplus); Assert.Equal(4500, month.Movement);
        Assert.Equal(2500, Reports.Categories(rows).Single().Cents);
    }
    [Fact]
    public void ReconciliationChecksBothMoneyAndIndependentCount()
    {
        Transaction[] rows = [Row(-500)];
        var s = new StatementBalance("synthetic.pdf", "N26", new(2000, 9, 1), new(1000), new(500), 2);
        Assert.False(Reports.Reconcile(rows, s).Balanced);
        Assert.True(Reports.Reconcile(rows, s with { ExpectedCount = 1 }).Balanced);
    }
    [Fact]
    public void ImportReimportManualEditAndBackupRestorePreserveFacts()
    {
        using var fixture = new StoreFixture(); var row = Row(-500); var preview = fixture.Preview([row]);
        Assert.Equal(1, fixture.Store.Commit(preview)); Assert.Equal(0, fixture.Store.Commit(preview));
        fixture.Store.Classify(row.Id, MovementKind.Expense, "Groceries", ReviewStatus.Assigned, "Reviewed");
        Assert.Equal(0, fixture.Store.Commit(preview)); Assert.Equal("Groceries", fixture.Store.Read().Transactions.Single().Category);
        var backup = Path.Combine(fixture.Root, "snapshot.spondebackup"); fixture.Store.CreateBackup(backup);
        fixture.Store.Classify(row.Id, MovementKind.Expense, "Changed", ReviewStatus.Review, "Later"); fixture.Store.RestoreBackup(backup);
        Assert.Equal("Groceries", fixture.Store.Read().Transactions.Single().Category);
        Assert.NotEmpty(Directory.EnumerateFiles(fixture.Root, "before-restore-*.spondebackup"));
    }
    [Fact]
    public void ChangedSourceCannotCommitStalePreview()
    {
        using var fixture = new StoreFixture(); var preview = fixture.Preview([Row(-500)]);
        File.AppendAllText(fixture.Source, "changed"); Assert.Throws<IOException>(() => fixture.Store.Commit(preview)); Assert.Empty(fixture.Store.Read().Transactions);
    }
    [Fact]
    public void CancelledCommitLeavesNoTransactions()
    {
        using var f = new StoreFixture(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => f.Store.Commit(f.Preview([Row(-500)]), cancellation.Token)); Assert.Empty(f.Store.Read().Transactions);
    }
    [Fact]
    public void DuplicatePaymentsSurviveAndAmbiguousOverlapRollsBack()
    {
        using var f = new StoreFixture(); var first = Row(-500); var second = Row(-500);
        Assert.Equal(2, f.Store.Commit(f.Preview([first, second])));
        var candidate = Row(-500) with { Counterparty = "Different merchant", Page = 2 };
        var pdf = f.Preview([candidate, candidate with { Id = Guid.NewGuid() }]) with { ParserVersion = "pdf-overlap-v2" };
        Assert.Throws<InvalidOperationException>(() => f.Store.Commit(pdf)); Assert.Equal(2, f.Store.Read().Transactions.Length);
    }
    [Fact]
    public void PayrollAddsGrossPostingsWithoutAddingBankIncomeAgain()
    {
        using var f = new StoreFixture(); var salary = Row(70000, MovementKind.Income);
        f.Store.Commit(f.Preview([salary]));
        var slip = new Payslip("payroll.pdf", new(2000, 9, 1), new(100000), new(30000), new(70000), new(0), new(0), new(70000), [new("Tax", new(30000))]);
        var preview = f.Preview([]) with { ParserVersion = "pdf-payroll-v1", Data = new([], [], [], [slip]) };
        f.Store.Commit(preview);
        Assert.Single(f.Store.Read().Transactions); Assert.Single(f.Store.Read().Payslips);
        Assert.Equal(70000, Reports.Month(f.Store.Read().Transactions, new(2000, 9, 1)).Income);
        Assert.Throws<InvalidOperationException>(() => f.Store.Classify(salary.Id, MovementKind.Expense, "Wrong", ReviewStatus.Assigned, ""));
    }
    [Fact]
    public void CorruptOrTraversalBackupDoesNotChangeActiveDatabase()
    {
        using var f = new StoreFixture(); f.Store.Commit(f.Preview([Row(-500)]));
        var malicious = Path.Combine(f.Root, "bad.spondebackup");
        using (var zip = ZipFile.Open(malicious, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open());
            writer.Write(JsonSerializer.Serialize(new Dictionary<string, string> { ["finance.sqlite"] = "bad", ["../outside"] = "bad" }));
        }
        Assert.Throws<IOException>(() => f.Store.RestoreBackup(malicious)); Assert.Single(f.Store.Read().Transactions);
    }
    [Fact]
    public void OnlyOneProcessCanOwnDataset()
    {
        using var f = new StoreFixture(); Assert.Throws<IOException>(() => new SqliteFinanceStore(f.Root));
    }
    private sealed class StoreFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "sponde-tests-" + Guid.NewGuid().ToString("N"));
        public string Source { get; }
        public SqliteFinanceStore Store { get; }
        public StoreFixture() { Directory.CreateDirectory(Root); Source = Path.Combine(Root, "synthetic.pdf"); File.WriteAllText(Source, "synthetic evidence"); Store = new(Root); }
        public ImportPreview Preview(ImmutableArray<Transaction> rows) => new("Synthetic", new(rows, [], [], []), [new(Source, Evidence.Hash(Source))], [], "pdf-test-v1");
        public void Dispose() { Store.Dispose(); Directory.Delete(Root, true); }
    }
}
