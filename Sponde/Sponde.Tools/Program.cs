using System.Text.Json;
using Sponde.Application;
using Sponde.Domain;
using Sponde.Infrastructure;

if (args.Length < 2) { Console.WriteLine("Usage: Sponde.Tools inspect|validate <finance-folder> [local-data-folder]; restore <backup> <data-folder>"); return; }
if (args[0] == "restore" && args.Length == 3)
{
    using var target = new SqliteFinanceStore(args[2]); target.RestoreBackup(args[1]); Console.WriteLine("Backup restored; previous dataset retained in recovery backup."); return;
}
var folder = Path.GetFullPath(args[1]);
if (args[0] == "inspect")
{
    var ordinal = 0;
    foreach (var path in Directory.EnumerateFiles(folder, "*.pdf", SearchOption.AllDirectories))
    {
        var preview = new StatementReader().Read(path);
        Console.WriteLine($"PDF #{++ordinal}: issues={preview.Issues.Length}");
        if (preview.Issues.Length > 0 && args.Length > 2)
        {
            Directory.CreateDirectory(args[2]);
            File.WriteAllText(Path.Combine(args[2], Path.GetFileNameWithoutExtension(path) + ".txt"), string.Join("\n---PAGE---\n", StatementReader.Extract(path).Select(p => p.Text)));
        }
    }
    return;
}
if (args[0] != "validate" || args.Length != 3) throw new ArgumentException("Validation requires an explicit scratch data folder.");
using var store = new SqliteFinanceStore(args[2]);
var reader = new StatementReader();
var service = new FinanceService(store, reader);
var paths = Directory.EnumerateFiles(folder, "*.pdf", SearchOption.AllDirectories).ToArray();
// Deliberately unordered selection exercises the same batch workflow as the UI.
var batch = service.PreviewStatements(paths.Reverse());
foreach (var item in batch.Items)
{
    var preview = item.Preview;
    Console.WriteLine($"PDF validation: issues={preview.Issues.Length}");
    if (!preview.CanCommit) throw new InvalidOperationException("Private corpus validation failed.");
}
var result = service.CommitStatements(batch);
if (result.Files.Any(f => f.Outcome is FileImportOutcome.Failed or FileImportOutcome.Cancelled or FileImportOutcome.Skipped)) throw new InvalidOperationException("Batch commit failed.");
var repeated = service.CommitStatements(service.PreviewStatements(paths));
if (repeated.Files.Any(f => f.Outcome != FileImportOutcome.AlreadyPresent || f.TransactionsAdded != 0)) throw new InvalidOperationException("Batch reimport was not idempotent.");
var final = service.Read();
if (!final.Budget.IsEmpty) throw new InvalidOperationException("PDF validation must not import budget plans.");
if (final.Statements.Any(s => !Reports.Reconcile(final.Transactions, s).Balanced)) throw new InvalidOperationException("Account reconciliation failed.");
var backup = Path.Combine(args[2], "validated.spondebackup");
service.Backup(backup);
service.Restore(backup);
if (JsonSerializer.Serialize(final) != JsonSerializer.Serialize(service.Read())) throw new InvalidOperationException("Restore changed data.");
Console.WriteLine("PASS: PDF-only import, exact reconciliation, idempotent imports and backup restore. Private counts and identifiers omitted.");
