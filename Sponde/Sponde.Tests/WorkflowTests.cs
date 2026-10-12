using System.Collections.Immutable;
using Sponde.Application;
using Sponde.Domain;
using Sponde.Infrastructure;
using Xunit;

namespace Sponde.Tests;

public sealed class WorkflowTests
{
    [Fact]
    public void RecurringDatesKeepOriginalMonthEndAnchor()
    {
        var line = new BudgetLine("Annual", new(100), "N26", false, PaymentAmount: new(1200), RecurrenceMonths: 12, NextPayment: new(2024, 2, 29));
        Assert.Equal(new DateOnly(2025, 2, 28), Reports.NextPayment(line, new(2025, 2, 1)));
        Assert.Equal(new DateOnly(2028, 2, 29), Reports.NextPayment(line, new(2027, 3, 1)));
        Assert.Null(Reports.NextPayment(line with { Cancelled = true }, new(2025, 2, 1)));
    }
    [Fact]
    public void BudgetVersionsDoNotRewriteHistoryAndCancellationIsEffectiveDated()
    {
        BudgetLine[] lines = [new("Subscription", new(1000), "N26", false), new("Subscription", new(1500), "N26", false, new(2026, 9, 1)), new("Subscription", new(1500), "N26", false, new(2026, 10, 1), true)];
        Assert.Equal(1000, Reports.BudgetAt(lines, new(2026, 8, 1)).Single().Monthly!.Value.Cents);
        Assert.Equal(1500, Reports.BudgetAt(lines, new(2026, 9, 1)).Single().Monthly!.Value.Cents);
        Assert.Empty(Reports.BudgetAt(lines, new(2026, 10, 1)));
    }
    [Fact]
    public void RulesRespectManualOverridesAndAmbiguousPrioritiesRemainOpen()
    {
        var row = new Transaction(Guid.NewGuid(), new(2026, 9, 1), "N26", "Synthetic shop", new(-500), MovementKind.Expense, "Unknown", ReviewStatus.Review, "", "x.pdf", 1, "", null, "");
        ClassificationRule[] rules = [new(Guid.NewGuid(), "shop", "", MovementKind.Expense, "Food", 10, new(2026, 1, 1)), new(Guid.NewGuid(), "shop", "", MovementKind.Expense, "Household", 10, new(2026, 1, 1))];
        Assert.Equal(row, Classification.Apply(row, rules));
        Assert.Equal("Food", Classification.Apply(row, rules.Take(1)).Category);
        Assert.Equal("Reviewed manually", Classification.Apply(row with { Category = "Reviewed manually", Review = ReviewStatus.Assigned }, rules).Category);
    }
    [Fact]
    public void AllocationChangesCategoryReportsWithoutChangingCashMovement()
    {
        var row = new Transaction(Guid.NewGuid(), new(2026, 9, 1), "N26", "Synthetic shop", new(-500), MovementKind.Expense, "Unknown", ReviewStatus.Review, "", "x.pdf", 1, "", null, "");
        ImmutableArray<Allocation> split = [new(row.Id, "Food", new(-300)), new(row.Id, "Household", new(-200))];
        Assert.Equal(500, Reports.Categories([row], split).Sum(c => c.Cents)); Assert.Equal(-500, Reports.Month([row], new(2026, 9, 1)).Movement);
    }
    [Fact]
    public void CsvEscapesFormulaLikeTextAndKeepsMoneyNumeric()
    {
        var row = new Transaction(Guid.NewGuid(), new(2026, 9, 1), "N26", "=CMD()", new(-500), MovementKind.Expense, "Food", ReviewStatus.Assigned, "", "x.pdf", 1, "quote\";newline\n", null, "");
        var csv = CsvExport.Transactions([row]); Assert.Contains("' =", csv.Replace("'=", "' =", StringComparison.Ordinal)); Assert.Contains("\"-5.00\"", csv); Assert.Contains("quote\"\"", csv);
    }
    [Fact]
    public void CrossMonthTransferAndSplitSurviveDatabaseRecovery()
    {
        var root = Path.Combine(Path.GetTempPath(), "sponde-workflow-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            using var store = new SqliteFinanceStore(root); var service = new FinanceService(store, new StatementReader());
            var path = Path.Combine(root, "synthetic.pdf"); File.WriteAllText(path, "synthetic");
            var outgoing = new Transaction(Guid.NewGuid(), new(2026, 8, 31), "VR", "Own account", new(-500), MovementKind.Transfer, "Own", ReviewStatus.Assigned, "", "synthetic.pdf", 1, "", null, "");
            var incoming = outgoing with { Id = Guid.NewGuid(), Date = new(2026, 9, 1), Account = "N26", Amount = new(500) };
            var expense = incoming with { Id = Guid.NewGuid(), Kind = MovementKind.Expense, Amount = new(-500) };
            store.Commit(new("synthetic", new([outgoing, incoming, expense], [], [], []), [new(path, Evidence.Hash(path))], [], "pdf-test"));
            service.LinkTransfer(outgoing.Id, incoming.Id); service.Split(expense.Id, [new(expense.Id, "Food", new(-300)), new(expense.Id, "Household", new(-200))]);
            Assert.Single(store.Read().Transfers); Assert.Equal(2, store.Read().Allocations.Length);
            Assert.Throws<ArgumentException>(() => service.Split(expense.Id, [new(expense.Id, "Food", new(-300)), new(expense.Id, "Household", new(-100))]));
            store.Classify(expense.Id, MovementKind.Expense, "Aufgeteilt", ReviewStatus.Assigned, "Note update");
            var backup = Path.Combine(root, "snapshot.spondebackup"); store.CreateBackup(backup); store.RestoreBackup(backup);
            Assert.Single(store.Read().Transfers); Assert.Equal(2, store.Read().Allocations.Length);
        }
        finally { Directory.Delete(root, true); }
    }
}
