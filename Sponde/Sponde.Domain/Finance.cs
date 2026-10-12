using System.Collections.Immutable;

namespace Sponde.Domain;

public readonly record struct Money(long Cents)
{
    public decimal Euros => Cents / 100m;
    public static Money FromEuros(decimal value)
    {
        var cents = value * 100m;
        if (cents != decimal.Truncate(cents)) throw new ArgumentException("Settled EUR amounts must have at most two decimal places.", nameof(value));
        return new(checked((long)cents));
    }
}

public enum MovementKind { Income, Expense, Refund, Transfer, Savings }
public enum ReviewStatus { Assigned, Review, Unknown }
public sealed record Transaction(Guid Id, DateOnly Date, string Account, string Counterparty,
    Money Amount, MovementKind Kind, string Category, ReviewStatus Review, string Note,
    string Source, int Page, string Purpose, DateOnly? ValueDate, string BankCategory);
public sealed record Posting(string Account, long DebitCents);
public sealed record JournalEntry(Guid Id, DateOnly Date, ImmutableArray<Posting> Postings)
{
    public void Validate()
    {
        if (Postings.Length < 2 || Postings.Any(p => string.IsNullOrWhiteSpace(p.Account)) || Postings.Sum(p => p.DebitCents) != 0)
            throw new InvalidOperationException("A journal entry must contain at least two named postings and balance exactly.");
    }
    public static JournalEntry For(Transaction t)
    {
        var offset = t.Kind switch
        {
            MovementKind.Transfer => "Clearing:Transfers",
            MovementKind.Savings => "Asset:Unverified savings",
            _ when t.Review != ReviewStatus.Assigned => "Clearing:Unclassified",
            MovementKind.Income => "Income:" + t.Category,
            _ => "Expense:" + t.Category
        };
        var entry = new JournalEntry(t.Id, t.Date, [new("Asset:" + t.Account, t.Amount.Cents), new(offset, checked(-t.Amount.Cents))]);
        entry.Validate();
        return entry;
    }
}

public sealed record StatementBalance(string Source, string Account, DateOnly Month,
    Money Opening, Money Closing, int? ExpectedCount);
public sealed record BudgetLine(string Category, Money? Monthly, string Account, bool Income, DateOnly? EffectiveFrom = null, bool Cancelled = false, string? ReportingCategory = null,
    Money? PaymentAmount = null, int RecurrenceMonths = 1, DateOnly? NextPayment = null);
public sealed record PayrollAdjustment(DateOnly EarningsPeriod, int Page, Money NetEffect);
public sealed record PayrollLine(string Label, Money Amount);
public sealed record Payslip(string Source, DateOnly Period, Money Gross, Money Deductions,
    Money Net, Money Correction, Money Canteen, Money Payout, ImmutableArray<PayrollLine> Lines,
    ImmutableArray<PayrollLine> Earnings = default, ImmutableArray<PayrollAdjustment> Adjustments = default);
public sealed record Allocation(Guid TransactionId, string Category, Money Amount);
public sealed record TransferLink(Guid Outgoing, Guid Incoming);
public sealed record BankAccount(string Name, string Identifier);
public static class OwnTransfers
{
    public static Transaction Apply(Transaction row, IEnumerable<BankAccount> accounts)
    {
        if (row.Review == ReviewStatus.Assigned) return row;
        var purpose = string.Concat(row.Purpose.Where(c => !char.IsWhiteSpace(c)));
        foreach (var account in accounts.Where(a => a.Name != row.Account))
        {
            var namedSpace = account.Name.StartsWith("N26 ", StringComparison.Ordinal) && row.Account.StartsWith("N26", StringComparison.Ordinal)
                && (row.Counterparty.Equals("An " + account.Name[4..], StringComparison.OrdinalIgnoreCase) || row.Counterparty.Equals("Von " + account.Name[4..], StringComparison.OrdinalIgnoreCase));
            if (namedSpace || account.Identifier.Length > 0 && purpose.Contains(account.Identifier, StringComparison.OrdinalIgnoreCase))
                return row with { Kind = MovementKind.Transfer, Category = "Eigene Überträge", Review = ReviewStatus.Assigned };
        }
        return row;
    }
}
public sealed record ClassificationRule(Guid Id, string Contains, string Account, MovementKind Kind, string Category, int Priority, DateOnly EffectiveFrom);
public static class Classification
{
    public static Transaction Apply(Transaction row, IEnumerable<ClassificationRule> rules)
    {
        if (row.Review == ReviewStatus.Assigned) return row;
        var matches = rules.Where(r => r.EffectiveFrom <= row.Date && (r.Account.Length == 0 || r.Account == row.Account) &&
            $"{row.Counterparty} {row.Purpose}".Contains(r.Contains, StringComparison.OrdinalIgnoreCase)).OrderByDescending(r => r.Priority).ToArray();
        if (matches.Length == 0) return row;
        var best = matches.Where(r => r.Priority == matches[0].Priority).ToArray();
        if (best.Select(r => (r.Kind, r.Category)).Distinct().Count() != 1) return row;
        return row with { Kind = best[0].Kind, Category = best[0].Category, Review = ReviewStatus.Assigned };
    }
}
public sealed record Dataset(ImmutableArray<Transaction> Transactions, ImmutableArray<StatementBalance> Statements,
    ImmutableArray<BudgetLine> Budget, ImmutableArray<Payslip> Payslips,
    ImmutableArray<Allocation> Allocations = default, ImmutableArray<TransferLink> Transfers = default,
    ImmutableArray<Guid> VerifiedTransactions = default, ImmutableArray<BankAccount> Accounts = default)
{
    public static Dataset Empty { get; } = new([], [], [], []);
}
public sealed record MonthlySummary(DateOnly Month, long Income, long Expenses, long Savings,
    long Movement, int Unresolved)
{
    public long Surplus => checked(Income - Expenses);
}
public sealed record CategoryTotal(string Category, long Cents);
public sealed record Reconciliation(StatementBalance Statement, long ActualMovement, int Count)
{
    public long Difference => checked(ActualMovement - (Statement.Closing.Cents - Statement.Opening.Cents));
    public bool Balanced => Difference == 0 && (Statement.ExpectedCount is null || Count == Statement.ExpectedCount);
}
public static class Reports
{
    public static DateOnly? NextPayment(BudgetLine line, DateOnly from)
    {
        if (line.Cancelled || line.NextPayment is null || line.RecurrenceMonths < 1) return null;
        var anchor = line.NextPayment.Value;
        if (anchor >= from) return anchor;
        var months = (from.Year - anchor.Year) * 12 + from.Month - anchor.Month;
        var periods = months / line.RecurrenceMonths;
        var candidate = anchor.AddMonths(periods * line.RecurrenceMonths);
        return candidate >= from ? candidate : anchor.AddMonths((periods + 1) * line.RecurrenceMonths);
    }
    public static ImmutableArray<BudgetLine> BudgetAt(IEnumerable<BudgetLine> lines, DateOnly month) => lines
        .Where(b => b.EffectiveFrom is null || b.EffectiveFrom <= month)
        .GroupBy(b => (b.Category, b.Income)).Select(g => g.OrderByDescending(b => b.EffectiveFrom ?? DateOnly.MinValue).First())
        .Where(b => !b.Cancelled).ToImmutableArray();
    public static MonthlySummary Month(IEnumerable<Transaction> source, DateOnly month)
    {
        var rows = source.Where(t => t.Date.Year == month.Year && t.Date.Month == month.Month).ToArray();
        return new(month, rows.Where(t => t.Kind == MovementKind.Income).Sum(t => t.Amount.Cents),
            -rows.Where(t => t.Kind is MovementKind.Expense or MovementKind.Refund).Sum(t => t.Amount.Cents),
            -rows.Where(t => t.Kind == MovementKind.Savings).Sum(t => t.Amount.Cents), rows.Sum(t => t.Amount.Cents),
            rows.Count(t => t.Review != ReviewStatus.Assigned));
    }
    public static ImmutableArray<CategoryTotal> Categories(IEnumerable<Transaction> source, ImmutableArray<Allocation> allocations = default) => source
        .Where(t => t.Kind is MovementKind.Expense or MovementKind.Refund)
        .SelectMany(t => allocations.IsDefaultOrEmpty || !allocations.Any(a => a.TransactionId == t.Id)
            ? [new CategoryTotal(t.Category, -t.Amount.Cents)] : allocations.Where(a => a.TransactionId == t.Id).Select(a => new CategoryTotal(a.Category, -a.Amount.Cents)))
        .GroupBy(t => t.Category).Select(g => new CategoryTotal(g.Key, g.Sum(t => t.Cents)))
        .OrderByDescending(c => c.Cents).ToImmutableArray();
    public static Reconciliation Reconcile(IEnumerable<Transaction> source, StatementBalance statement)
    {
        var rows = source.Where(t => t.Account == statement.Account && t.Date.Year == statement.Month.Year && t.Date.Month == statement.Month.Month).ToArray();
        return new(statement, rows.Sum(t => t.Amount.Cents), rows.Length);
    }
}
