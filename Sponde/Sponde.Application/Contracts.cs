using System.Collections.Immutable;
using System.Globalization;
using Sponde.Domain;

namespace Sponde.Application;

public sealed record DocumentFile(string Path, string Hash);
public sealed record ImportPreview(string Origin, Dataset Data, ImmutableArray<DocumentFile> Documents,
    ImmutableArray<string> Issues, string ParserVersion)
{
    public bool CanCommit => Issues.IsEmpty && (!Data.Transactions.IsEmpty || !Data.Payslips.IsEmpty);
    public string DisplayName => !Data.Payslips.IsEmpty
        ? "Entgeltnachweis · " + string.Join(", ", Data.Payslips.Select(p => p.Period.ToString("yyyy-MM", CultureInfo.InvariantCulture)).Distinct())
        : !Data.Statements.IsEmpty
            ? string.Join(" / ", Data.Statements.Select(s => s.Account).Distinct()) + " · " + string.Join(", ", Data.Statements.Select(s => s.Month.ToString("yyyy-MM", CultureInfo.InvariantCulture)).Distinct())
            : "Nicht erkannt";
}
public sealed record BatchImportItem(ImportPreview Preview, bool Duplicate = false);
public sealed record PdfBatchPreview(ImmutableArray<BatchImportItem> Items)
{
    public int ReadyCount => Items.Count(i => !i.Duplicate && i.Preview.CanCommit);
    public bool CanCommit => ReadyCount > 0;
}
public enum FileImportOutcome { Imported, AlreadyPresent, Skipped, Failed, Cancelled }
public sealed record FileImportResult(string DisplayName, string OriginalName, FileImportOutcome Outcome, int TransactionsAdded, string Message);
public sealed record PdfBatchResult(ImmutableArray<FileImportResult> Files);
public interface IFinanceStore
{
    Dataset Read();
    int Commit(ImportPreview preview, CancellationToken cancellationToken = default);
    void Classify(Guid id, MovementKind kind, string category, ReviewStatus status, string note);
    void CreateBackup(string destination);
    void RestoreBackup(string source);
    void SaveBudget(BudgetLine line);
    string? FindDocument(string name);
    void Split(Guid id, ImmutableArray<Allocation> allocations);
    void LinkTransfer(Guid outgoing, Guid incoming);
    ImmutableArray<ClassificationRule> ReadRules();
    void SaveRule(ClassificationRule rule);
}
public interface IStatementReader { ImportPreview Read(string path, CancellationToken cancellationToken = default); }

public sealed class FinanceService(IFinanceStore store, IStatementReader statements)
{
    public Dataset Read() => store.Read();
    public ImportPreview PreviewStatement(string path, CancellationToken cancellationToken = default)
    {
        var preview = statements.Read(path, cancellationToken);
        var current = store.Read();
        var accounts = (current.Accounts.IsDefault ? [] : current.Accounts).Concat(preview.Data.Accounts.IsDefault ? [] : preview.Data.Accounts);
        return Prepare(preview, current.Transactions, accounts, store.ReadRules());
    }
    private static ImportPreview Prepare(ImportPreview preview, IEnumerable<Transaction> availableTransactions, IEnumerable<BankAccount> accounts, IEnumerable<ClassificationRule> rules)
    {
        var accountSnapshot = accounts.ToArray();
        var ruleSnapshot = rules.ToArray();
        preview = preview with { Data = preview.Data with { Transactions = preview.Data.Transactions.Select(t => Classification.Apply(OwnTransfers.Apply(t, accountSnapshot), ruleSnapshot)).ToImmutableArray() } };
        foreach (var slip in preview.Data.Payslips)
        {
            var matches = availableTransactions.Where(t => t.Kind == MovementKind.Income && t.Date.Year == slip.Period.Year && t.Date.Month == slip.Period.Month && t.Amount == slip.Payout).ToArray();
            if (matches.Length != 1) preview = preview with { Issues = preview.Issues.Add("Payslip needs exactly one matching bank income payment, either already stored or in this selection.") };
        }
        return preview;
    }
    public PdfBatchPreview PreviewStatements(IEnumerable<string> paths, CancellationToken cancellationToken = default)
    {
        var raw = ImmutableArray.CreateBuilder<BatchImportItem>();
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ImportPreview preview;
            try { preview = statements.Read(path, cancellationToken); }
            catch (Exception ex) when (ex is IOException or ArgumentException or FormatException or NotSupportedException)
            {
                preview = new(Path.GetFileName(path), Dataset.Empty, [new(path, "")], [$"PDF could not be read ({ex.GetType().Name})."], "pdf-unreadable-v1");
            }
            var hash = preview.Documents.FirstOrDefault()?.Hash ?? "";
            raw.Add(new(preview, hash.Length > 0 && !hashes.Add(hash)));
        }
        var current = store.Read();
        var rules = store.ReadRules();
        var available = raw.Where(i => !i.Duplicate && i.Preview.Issues.IsEmpty).ToArray();
        var accounts = (current.Accounts.IsDefault ? [] : current.Accounts).Concat(available.SelectMany(i => i.Preview.Data.Accounts.IsDefault ? [] : i.Preview.Data.Accounts)).ToArray();
        var projected = current.Transactions.ToList();
        var coverage = current.Statements.Select(s => (s.Account, s.Month.Year, s.Month.Month)).ToHashSet();
        foreach (var item in available)
        {
            var bank = item.Preview;
            foreach (var statement in bank.Data.Statements)
                if (coverage.Add((statement.Account, statement.Month.Year, statement.Month.Month)))
                    projected.AddRange(bank.Data.Transactions.Where(t => t.Account == statement.Account && t.Date.Year == statement.Month.Year && t.Date.Month == statement.Month.Month));
            projected.AddRange(bank.Data.Transactions.Where(t => !bank.Data.Statements.Any(s => s.Account == t.Account && s.Month.Year == t.Date.Year && s.Month.Month == t.Date.Month)));
        }
        var transactions = projected.GroupBy(t => t.Id).Select(g => g.First()).Select(t => Classification.Apply(OwnTransfers.Apply(t, accounts), rules)).ToArray();
        return new(raw.Select(i => i with { Preview = Prepare(i.Preview, transactions, accounts, rules) }).ToImmutableArray());
    }
    public PdfBatchResult CommitStatements(PdfBatchPreview batch, CancellationToken cancellationToken = default)
    {
        var results = ImmutableArray.CreateBuilder<FileImportResult>();
        // Each document is atomic; unrelated valid documents can succeed even
        // when one fails. Payroll is always processed after bank documents.
        foreach (var item in batch.Items.OrderBy(i => !i.Preview.Data.Payslips.IsEmpty))
        {
            var preview = item.Preview;
            FileImportResult Result(FileImportOutcome outcome, string message, int added = 0) => new(preview.DisplayName, preview.Origin, outcome, added, message);
            if (cancellationToken.IsCancellationRequested) { results.Add(Result(FileImportOutcome.Cancelled, "Not imported; cancelled.")); continue; }
            if (item.Duplicate) { results.Add(Result(FileImportOutcome.AlreadyPresent, "Identical PDF is already in this selection.")); continue; }
            if (!preview.CanCommit) { results.Add(Result(FileImportOutcome.Skipped, string.Join("; ", preview.Issues))); continue; }
            try
            {
                var before = store.Read();
                var checkedPreview = Prepare(preview, before.Transactions, before.Accounts.IsDefault ? [] : before.Accounts, store.ReadRules());
                if (!checkedPreview.CanCommit) { results.Add(Result(FileImportOutcome.Failed, string.Join("; ", checkedPreview.Issues))); continue; }
                var count = Commit(checkedPreview, cancellationToken);
                var newPayroll = store.Read().Payslips.Length > before.Payslips.Length;
                results.Add(Result(count > 0 || newPayroll ? FileImportOutcome.Imported : FileImportOutcome.AlreadyPresent, count > 0 || newPayroll ? "Document imported." : "No duplicate entries created.", count));
            }
            catch (OperationCanceledException) { results.Add(Result(FileImportOutcome.Cancelled, "Current document rolled back; previously completed documents remain imported.")); }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException or FormatException or System.Data.Common.DbException)
            { results.Add(Result(FileImportOutcome.Failed, ex.Message)); }
        }
        return new(results.ToImmutable());
    }
    public int Commit(ImportPreview preview, CancellationToken cancellationToken = default)
    {
        if (!preview.CanCommit) throw new InvalidOperationException("Resolve import errors before committing.");
        foreach (var t in preview.Data.Transactions) JournalEntry.For(t).Validate();
        var count = store.Commit(preview, cancellationToken);
        // Newly discovered account identities can explain earlier unclassified
        // movements. Only unresolved records change; manual decisions survive.
        var current = store.Read();
        foreach (var row in current.Transactions)
        {
            var revised = OwnTransfers.Apply(row, current.Accounts.IsDefault ? [] : current.Accounts);
            if (revised != row) store.Classify(row.Id, revised.Kind, revised.Category, revised.Review, revised.Note);
        }
        return count;
    }
    public void Classify(Guid id, MovementKind kind, string category, ReviewStatus status, string note)
    {
        if (string.IsNullOrWhiteSpace(category)) throw new ArgumentException("A category is required.", nameof(category));
        store.Classify(id, kind, category.Trim(), status, note);
    }
    public void Backup(string path) => store.CreateBackup(path);
    public void Restore(string path) => store.RestoreBackup(path);
    public string? SourcePath(string name) => store.FindDocument(name);
    public void SaveBudget(BudgetLine line)
    {
        if (string.IsNullOrWhiteSpace(line.Category) || line.EffectiveFrom is null || line.EffectiveFrom.Value.Day != 1 || line.RecurrenceMonths < 1)
            throw new ArgumentException("A category and first-of-month effective date are required.");
        store.SaveBudget(line);
    }
    public void Split(Guid id, ImmutableArray<Allocation> allocations)
    {
        var row = store.Read().Transactions.Single(t => t.Id == id);
        if (row.Kind is not (MovementKind.Expense or MovementKind.Refund) || allocations.Length < 2 ||
            allocations.Any(a => a.TransactionId != id || string.IsNullOrWhiteSpace(a.Category) || Math.Sign(a.Amount.Cents) != Math.Sign(row.Amount.Cents)) || allocations.Sum(a => a.Amount.Cents) != row.Amount.Cents)
            throw new ArgumentException("Expense allocations must have named categories, matching signs and sum exactly to the transaction amount.");
        store.Split(id, allocations);
    }
    public void LinkTransfer(Guid outgoing, Guid incoming) => store.LinkTransfer(outgoing, incoming);
    public ImmutableArray<ClassificationRule> Rules() => store.ReadRules();
    public void SaveRule(ClassificationRule rule)
    {
        if (rule.Contains.Trim().Length < 3 || string.IsNullOrWhiteSpace(rule.Category)) throw new ArgumentException("A rule needs a pattern of at least three characters and a category.");
        store.SaveRule(rule with { Contains = rule.Contains.Trim(), Category = rule.Category.Trim() });
    }
    public int ApplyRules()
    {
        var rules = store.ReadRules(); var count = 0;
        foreach (var row in store.Read().Transactions)
        {
            var revised = Classification.Apply(row, rules);
            if (revised == row) continue;
            store.Classify(row.Id, revised.Kind, revised.Category, revised.Review, revised.Note); count++;
        }
        return count;
    }
}
