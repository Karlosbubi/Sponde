using System.Collections.Immutable;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Sponde.Application;
using Sponde.Domain;

namespace Sponde.Infrastructure;

public sealed class SqliteFinanceStore : IFinanceStore, IDisposable
{
    private readonly string root;
    private readonly FileStream lease;
    private readonly object gate = new();
    private const int SchemaVersion = 5;
    public SqliteFinanceStore(string directory)
    {
        root = Path.GetFullPath(directory);
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "documents"));
        lease = new FileStream(Path.Combine(root, "session.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            using var db = Open();
            var version = Convert.ToInt32(Scalar(db, "PRAGMA user_version"));
            if (version > SchemaVersion) throw new InvalidOperationException("This database needs a newer Sponde version.");
            if (version == 0)
            {
                using var tx = db.BeginTransaction();
                Execute(db, """
                    CREATE TABLE transactions(id TEXT PRIMARY KEY, source TEXT NOT NULL, json TEXT NOT NULL);
                    CREATE TABLE observations(id TEXT PRIMARY KEY REFERENCES transactions(id), json TEXT NOT NULL);
                    CREATE TABLE postings(entry_id TEXT NOT NULL REFERENCES transactions(id), account TEXT NOT NULL, debit_cents INTEGER NOT NULL);
                    CREATE TABLE statements(key TEXT PRIMARY KEY, json TEXT NOT NULL);
                    CREATE TABLE budgets(category TEXT NOT NULL, income INTEGER NOT NULL, json TEXT NOT NULL, PRIMARY KEY(category,income));
                    CREATE TABLE payslips(source TEXT PRIMARY KEY, json TEXT NOT NULL);
                    CREATE TABLE payroll_matches(source TEXT PRIMARY KEY REFERENCES payslips(source), transaction_id TEXT NOT NULL UNIQUE REFERENCES transactions(id));
                    CREATE TABLE documents(hash TEXT PRIMARY KEY, name TEXT NOT NULL, extension TEXT NOT NULL);
                    CREATE TABLE imports(hash TEXT NOT NULL, parser TEXT NOT NULL, committed_utc TEXT NOT NULL, PRIMARY KEY(hash,parser));
                    CREATE TABLE changes(id INTEGER PRIMARY KEY, transaction_id TEXT NOT NULL, at_utc TEXT NOT NULL, before_json TEXT NOT NULL, after_json TEXT NOT NULL);
                    CREATE TABLE verification(transaction_id TEXT NOT NULL REFERENCES transactions(id), document_hash TEXT NOT NULL REFERENCES documents(hash), parser TEXT NOT NULL, page INTEGER NOT NULL, PRIMARY KEY(transaction_id,document_hash));
                    CREATE INDEX transaction_source ON transactions(source);
                    PRAGMA user_version=1;
                    """, tx);
                tx.Commit();
            }
            Migrate(db);
        }
        catch { lease.Dispose(); throw; }
    }
    private SqliteConnection Open(string? path = null)
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path ?? Path.Combine(root, "finance.sqlite"), Pooling = false, ForeignKeys = true }.ToString());
        db.Open();
        return db;
    }
    public Dataset Read()
    {
        lock (gate)
        {
            using var db = Open();
            using var verifiedCommand = Command(db, "SELECT DISTINCT transaction_id FROM verification");
            using var verifiedReader = verifiedCommand.ExecuteReader();
            var verified = ImmutableArray.CreateBuilder<Guid>();
            while (verifiedReader.Read()) verified.Add(Guid.Parse(verifiedReader.GetString(0)));
            return new(ReadJson<Transaction>(db, "transactions"), ReadJson<StatementBalance>(db, "statements"),
                ReadJson<BudgetLine>(db, "budget_versions"), ReadJson<Payslip>(db, "payslips"),
                ReadJson<Allocation>(db, "allocations"), ReadJson<TransferLink>(db, "transfer_links"), verified.ToImmutable(), ReadJson<BankAccount>(db, "bank_accounts"));
        }
    }
    public int Commit(ImportPreview preview, CancellationToken cancellationToken = default)
    {
        if (!preview.CanCommit) throw new InvalidOperationException("Invalid import preview.");
        if (preview.Documents.IsDefaultOrEmpty || preview.Documents.Any(d => !Path.GetExtension(d.Path).Equals(".pdf", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Only PDF evidence can be imported.");
        lock (gate)
        {
            foreach (var doc in preview.Documents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Evidence.Hash(doc.Path) != doc.Hash) throw new IOException("Source changed after preview; preview it again.");
                var target = DocumentPath(doc.Hash, Path.GetExtension(doc.Path));
                if (!File.Exists(target)) File.Copy(doc.Path, target);
                if (Evidence.Hash(target) != doc.Hash) throw new IOException("Managed evidence failed checksum verification.");
            }
            using var db = Open();
            using var tx = db.BeginTransaction();
            foreach (var doc in preview.Documents)
                Execute(db, "INSERT OR IGNORE INTO documents VALUES($hash,$name,$ext)", tx,
                    ("$hash", doc.Hash), ("$name", Path.GetFileName(doc.Path)), ("$ext", Path.GetExtension(doc.Path)));
            var primaryHash = preview.Documents[0].Hash;
            if (Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM imports WHERE hash=$hash AND parser=$parser", tx, ("$hash", primaryHash), ("$parser", preview.ParserVersion))) != 0)
                return 0;
            var existing = ReadJson<Transaction>(db, "transactions", tx);
            var existingStatements = ReadJson<StatementBalance>(db, "statements", tx);
            foreach (var statement in preview.Data.Statements)
            {
                var prior = existingStatements.FirstOrDefault(s => s.Account == statement.Account && s.Month == statement.Month);
                if (prior is not null && (prior.Opening != statement.Opening || prior.Closing != statement.Closing))
                    throw new InvalidOperationException("Statement balances disagree with existing history. Import left unchanged.");
            }
            var used = new HashSet<Guid>();
            var count = 0;
            foreach (var t in preview.Data.Transactions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                JournalEntry.For(t).Validate();
                // Reissued PDF sections must agree including multiplicity.
                // Never guess overlap matches or overwrite manual classification.
                var candidates = existing.Where(e => e.Account == t.Account && e.Date == t.Date && e.Amount == t.Amount && !used.Contains(e.Id)).ToArray();
                var coveredPeriod = preview.Data.Statements.Any(s => s.Account == t.Account && s.Month.Year == t.Date.Year && s.Month.Month == t.Date.Month)
                    && existingStatements.Any(s => s.Account == t.Account && s.Month.Year == t.Date.Year && s.Month.Month == t.Date.Month);
                var sameSourcePeriod = existing.Any(e => e.Source == t.Source && e.Account == t.Account && e.Date.Year == t.Date.Year && e.Date.Month == t.Date.Month);
                if (preview.ParserVersion.StartsWith("pdf-", StringComparison.Ordinal) && (coveredPeriod || sameSourcePeriod))
                {
                    var priorSection = existing.Where(e => e.Account == t.Account && e.Date.Year == t.Date.Year && e.Date.Month == t.Date.Month).ToArray();
                    var newSection = preview.Data.Transactions.Where(e => e.Account == t.Account && e.Date.Year == t.Date.Year && e.Date.Month == t.Date.Month).ToArray();
                    if (priorSection.Length != newSection.Length || candidates.Length == 0)
                        throw new InvalidOperationException("PDF and existing history disagree. Import left unchanged; investigate the overlap.");
                    var match = candidates.FirstOrDefault(e => e.Page == t.Page && e.Counterparty == t.Counterparty);
                    if (match is null && candidates.Length != 1)
                        throw new InvalidOperationException("Ambiguous overlap with existing history; manual investigation required.");
                    match ??= candidates[0];
                    used.Add(match.Id);
                    Execute(db, "INSERT OR IGNORE INTO verification VALUES($id,$hash,$parser,$page)", tx,
                        ("$id", match.Id.ToString()), ("$hash", primaryHash), ("$parser", preview.ParserVersion), ("$page", t.Page));
                    continue;
                }
                if (existing.Any(e => e.Id == t.Id)) continue;
                var json = JsonSerializer.Serialize(t);
                Execute(db, "INSERT INTO transactions VALUES($id,$source,$json)", tx, ("$id", t.Id.ToString()), ("$source", t.Source), ("$json", json));
                Execute(db, "INSERT INTO observations VALUES($id,$json)", tx, ("$id", t.Id.ToString()), ("$json", json));
                WritePostings(db, tx, t);
                if (preview.ParserVersion.StartsWith("pdf-", StringComparison.Ordinal))
                    Execute(db, "INSERT INTO verification VALUES($id,$hash,$parser,$page)", tx,
                        ("$id", t.Id.ToString()), ("$hash", primaryHash), ("$parser", preview.ParserVersion), ("$page", t.Page));
                count++;
            }
            foreach (var s in preview.Data.Statements)
                Execute(db, "INSERT OR IGNORE INTO statements VALUES($key,$json)", tx, ("$key", $"{s.Account}:{s.Month:yyyy-MM}"), ("$json", JsonSerializer.Serialize(s)));
            if (!preview.Data.Accounts.IsDefault)
                foreach (var account in preview.Data.Accounts)
                {
                    var known = ReadJson<BankAccount>(db, "bank_accounts", tx).FirstOrDefault(a => a.Name == account.Name);
                    if (known is not null && known.Identifier != account.Identifier) throw new InvalidOperationException("Account identity changed; explicit account setup is required.");
                    Execute(db, "INSERT OR IGNORE INTO bank_accounts VALUES($name,$json)", tx, ("$name", account.Name), ("$json", JsonSerializer.Serialize(account)));
                }
            foreach (var b in preview.Data.Budget)
                Execute(db, "INSERT OR IGNORE INTO budget_versions VALUES($category,$income,$month,$json)", tx, ("$category", b.Category), ("$income", b.Income), ("$month", b.EffectiveFrom?.ToString("yyyy-MM-dd") ?? "0001-01-01"), ("$json", JsonSerializer.Serialize(b)));
            foreach (var p in preview.Data.Payslips)
            {
                var matches = existing.Where(t => t.Kind == MovementKind.Income && t.Date.Year == p.Period.Year && t.Date.Month == p.Period.Month && t.Amount == p.Payout).ToArray();
                if (matches.Length != 1) throw new InvalidOperationException("Payslip needs exactly one matching bank payment.");
                var payment = matches[0];
                var reviewed = payment.Review == ReviewStatus.Assigned ? payment : payment with { Category = "Gehalt Netto", Review = ReviewStatus.Assigned };
                Execute(db, "UPDATE transactions SET json=$json WHERE id=$id", tx, ("$json", JsonSerializer.Serialize(reviewed)), ("$id", payment.Id.ToString()));
                Execute(db, "INSERT INTO changes(transaction_id,at_utc,before_json,after_json) VALUES($id,$utc,$before,$after)", tx,
                    ("$id", payment.Id.ToString()), ("$utc", DateTimeOffset.UtcNow.ToString("O")), ("$before", JsonSerializer.Serialize(payment)), ("$after", JsonSerializer.Serialize(reviewed)));
                var normalized = p with { Earnings = p.Earnings.IsDefault ? [] : p.Earnings, Adjustments = p.Adjustments.IsDefault ? [] : p.Adjustments };
                Execute(db, "INSERT OR IGNORE INTO payslips VALUES($source,$json)", tx, ("$source", p.Source), ("$json", JsonSerializer.Serialize(normalized)));
                Execute(db, "INSERT INTO payroll_matches VALUES($source,$id)", tx, ("$source", p.Source), ("$id", payment.Id.ToString()));
                var postings = ImmutableArray.CreateBuilder<Posting>();
                postings.Add(new("Asset:" + payment.Account, p.Payout.Cents));
                postings.Add(new("Income:Gross salary", -p.Gross.Cents));
                foreach (var line in p.Lines) postings.Add(new("Expense:Payroll:" + line.Label, line.Amount.Cents));
                if (p.PostNetDeductions.Cents != 0) postings.Add(new("Expense:Payroll:Post-net deductions", p.PostNetDeductions.Cents));
                if (p.Correction.Cents != 0) postings.Add(new("Clearing:Payroll corrections", -p.Correction.Cents));
                new JournalEntry(payment.Id, payment.Date, postings.ToImmutable()).Validate();
                Execute(db, "DELETE FROM postings WHERE entry_id=$id", tx, ("$id", payment.Id.ToString()));
                foreach (var posting in postings)
                    Execute(db, "INSERT INTO postings VALUES($id,$account,$cents)", tx, ("$id", payment.Id.ToString()), ("$account", posting.Account), ("$cents", posting.DebitCents));
            }
            Execute(db, "INSERT INTO imports VALUES($hash,$parser,$utc)", tx, ("$hash", primaryHash), ("$parser", preview.ParserVersion), ("$utc", DateTimeOffset.UtcNow.ToString("O")));
            WriteOpenings(db, tx);
            cancellationToken.ThrowIfCancellationRequested();
            tx.Commit();
            return count;
        }
    }
    public void Classify(Guid id, MovementKind kind, string category, ReviewStatus status, string note)
    {
        lock (gate)
        {
            using var db = Open();
            using var tx = db.BeginTransaction();
            var before = ReadJson<Transaction>(db, "transactions", tx).Single(t => t.Id == id);
            if (Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM transfer_links WHERE outgoing=$id OR incoming=$id", tx, ("$id", id.ToString()))) > 0 && kind != MovementKind.Transfer)
                throw new InvalidOperationException("A linked transfer must remain a transfer.");
            var hasAllocations = Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM allocations WHERE transaction_id=$id", tx, ("$id", id.ToString()))) > 0;
            if (hasAllocations && (kind != before.Kind || category != before.Category))
                throw new InvalidOperationException("Use the split editor to change an allocated transaction's categories.");
            var payrollLinked = Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM payroll_matches WHERE transaction_id=$id", tx, ("$id", id.ToString()))) > 0;
            if (payrollLinked && kind != MovementKind.Income) throw new InvalidOperationException("A payroll-linked payment must remain income.");
            var after = before with { Kind = kind, Category = category, Review = status, Note = note };
            JournalEntry.For(after).Validate();
            Execute(db, "UPDATE transactions SET json=$json WHERE id=$id", tx, ("$json", JsonSerializer.Serialize(after)), ("$id", id.ToString()));
            if (!payrollLinked && !hasAllocations)
            {
                Execute(db, "DELETE FROM postings WHERE entry_id=$id", tx, ("$id", id.ToString()));
                WritePostings(db, tx, after);
            }
            Execute(db, "INSERT INTO changes(transaction_id,at_utc,before_json,after_json) VALUES($id,$utc,$before,$after)", tx,
                ("$id", id.ToString()), ("$utc", DateTimeOffset.UtcNow.ToString("O")), ("$before", JsonSerializer.Serialize(before)), ("$after", JsonSerializer.Serialize(after)));
            tx.Commit();
        }
    }
    private static void WritePostings(SqliteConnection db, SqliteTransaction tx, Transaction t)
    {
        foreach (var p in JournalEntry.For(t).Postings)
            Execute(db, "INSERT INTO postings VALUES($id,$account,$cents)", tx, ("$id", t.Id.ToString()), ("$account", p.Account), ("$cents", p.DebitCents));
    }
    private static void Migrate(SqliteConnection db)
    {
        var version = Convert.ToInt32(Scalar(db, "PRAGMA user_version"));
        if (version == 1)
        {
            using var tx = db.BeginTransaction();
            Execute(db, """
                CREATE TABLE budget_versions(category TEXT NOT NULL, income INTEGER NOT NULL, effective_month TEXT NOT NULL, json TEXT NOT NULL, PRIMARY KEY(category,income,effective_month));
                INSERT INTO budget_versions SELECT category,income,'0001-01-01',json FROM budgets;
                CREATE TABLE opening_entries(account TEXT PRIMARY KEY, month TEXT NOT NULL, source TEXT NOT NULL, json TEXT NOT NULL);
                PRAGMA user_version=2;
                """, tx);
            WriteOpenings(db, tx);
            tx.Commit();
        }
        version = Convert.ToInt32(Scalar(db, "PRAGMA user_version"));
        if (version == 2)
        {
            using var tx = db.BeginTransaction();
            Execute(db, """
                CREATE TABLE allocations(id INTEGER PRIMARY KEY, transaction_id TEXT NOT NULL REFERENCES transactions(id), json TEXT NOT NULL);
                CREATE TABLE transfer_links(outgoing TEXT PRIMARY KEY REFERENCES transactions(id), incoming TEXT NOT NULL UNIQUE REFERENCES transactions(id), json TEXT NOT NULL);
                PRAGMA user_version=3;
                """, tx);
            tx.Commit();
        }
        version = Convert.ToInt32(Scalar(db, "PRAGMA user_version"));
        if (version == 3)
        {
            using var tx = db.BeginTransaction();
            Execute(db, "CREATE TABLE classification_rules(id TEXT PRIMARY KEY, json TEXT NOT NULL); PRAGMA user_version=4;", tx); tx.Commit();
        }
        version = Convert.ToInt32(Scalar(db, "PRAGMA user_version"));
        if (version == 4)
        {
            using var tx = db.BeginTransaction();
            Execute(db, "CREATE TABLE bank_accounts(name TEXT PRIMARY KEY, json TEXT NOT NULL); PRAGMA user_version=5;", tx); tx.Commit();
        }
    }
    public ImmutableArray<ClassificationRule> ReadRules()
    {
        lock (gate) { using var db = Open(); return ReadJson<ClassificationRule>(db, "classification_rules"); }
    }
    public void SaveRule(ClassificationRule rule)
    {
        lock (gate)
        {
            using var db = Open(); Execute(db, "INSERT INTO classification_rules VALUES($id,$json)", null, ("$id", rule.Id.ToString()), ("$json", JsonSerializer.Serialize(rule)));
        }
    }
    public void Split(Guid id, ImmutableArray<Allocation> allocations)
    {
        lock (gate)
        {
            using var db = Open(); using var tx = db.BeginTransaction();
            var row = ReadJson<Transaction>(db, "transactions", tx).Single(t => t.Id == id);
            if (row.Kind is not (MovementKind.Expense or MovementKind.Refund) || allocations.Length < 2 || allocations.Sum(a => a.Amount.Cents) != row.Amount.Cents || allocations.Any(a => a.TransactionId != id || string.IsNullOrWhiteSpace(a.Category) || Math.Sign(a.Amount.Cents) != Math.Sign(row.Amount.Cents))) throw new ArgumentException("Invalid allocation.");
            Execute(db, "DELETE FROM allocations WHERE transaction_id=$id", tx, ("$id", id.ToString()));
            var reviewed = row with { Category = "Aufgeteilt", Review = ReviewStatus.Assigned };
            Execute(db, "UPDATE transactions SET json=$json WHERE id=$id", tx, ("$json", JsonSerializer.Serialize(reviewed)), ("$id", id.ToString()));
            foreach (var a in allocations) Execute(db, "INSERT INTO allocations(transaction_id,json) VALUES($id,$json)", tx, ("$id", id.ToString()), ("$json", JsonSerializer.Serialize(a)));
            Execute(db, "DELETE FROM postings WHERE entry_id=$id", tx, ("$id", id.ToString()));
            var postings = allocations.Select(a => new Posting("Expense:" + a.Category, -a.Amount.Cents)).Prepend(new("Asset:" + row.Account, row.Amount.Cents)).ToImmutableArray();
            new JournalEntry(row.Id, row.Date, postings).Validate();
            foreach (var p in postings) Execute(db, "INSERT INTO postings VALUES($id,$account,$cents)", tx, ("$id", id.ToString()), ("$account", p.Account), ("$cents", p.DebitCents));
            Execute(db, "INSERT INTO changes(transaction_id,at_utc,before_json,after_json) VALUES($id,$utc,$before,$after)", tx,
                ("$id", id.ToString()), ("$utc", DateTimeOffset.UtcNow.ToString("O")), ("$before", JsonSerializer.Serialize(row)), ("$after", JsonSerializer.Serialize(allocations)));
            tx.Commit();
        }
    }
    public void LinkTransfer(Guid outgoing, Guid incoming)
    {
        lock (gate)
        {
            using var db = Open(); using var tx = db.BeginTransaction();
            var rows = ReadJson<Transaction>(db, "transactions", tx); var a = rows.Single(t => t.Id == outgoing); var b = rows.Single(t => t.Id == incoming);
            if (a.Kind != MovementKind.Transfer || b.Kind != MovementKind.Transfer || a.Account == b.Account || a.Amount.Cents >= 0 || b.Amount.Cents != -a.Amount.Cents)
                throw new ArgumentException("Transfer legs must be on different accounts and have equal opposite amounts.");
            var link = new TransferLink(outgoing, incoming);
            Execute(db, "INSERT INTO transfer_links VALUES($out,$in,$json)", tx, ("$out", outgoing.ToString()), ("$in", incoming.ToString()), ("$json", JsonSerializer.Serialize(link)));
            tx.Commit();
        }
    }
    private static void WriteOpenings(SqliteConnection db, SqliteTransaction tx)
    {
        foreach (var group in ReadJson<StatementBalance>(db, "statements", tx).GroupBy(s => s.Account))
        {
            var s = group.OrderBy(s => s.Month).First();
            var entry = new JournalEntry(Evidence.Id("opening:" + s.Account), s.Month, [new("Asset:" + s.Account, s.Opening.Cents), new("Equity:Opening balance", -s.Opening.Cents)]);
            entry.Validate();
            Execute(db, "INSERT INTO opening_entries VALUES($account,$month,$source,$json) ON CONFLICT(account) DO UPDATE SET month=excluded.month,source=excluded.source,json=excluded.json", tx,
                ("$account", s.Account), ("$month", s.Month.ToString("yyyy-MM-dd")), ("$source", s.Source), ("$json", JsonSerializer.Serialize(entry)));
        }
    }
    public void SaveBudget(BudgetLine line)
    {
        lock (gate)
        {
            using var db = Open();
            Execute(db, "INSERT INTO budget_versions VALUES($category,$income,$month,$json) ON CONFLICT(category,income,effective_month) DO UPDATE SET json=excluded.json",
                null, ("$category", line.Category), ("$income", line.Income), ("$month", line.EffectiveFrom?.ToString("yyyy-MM-dd") ?? throw new ArgumentException("Effective month required.")), ("$json", JsonSerializer.Serialize(line)));
        }
    }
    private string DocumentPath(string hash, string extension) => Path.Combine(root, "documents", hash + extension.ToLowerInvariant());
    public string? FindDocument(string name)
    {
        lock (gate)
        {
            using var db = Open();
            using var cmd = Command(db, "SELECT hash,extension FROM documents WHERE name=$name", null, ("$name", name));
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? DocumentPath(reader.GetString(0), reader.GetString(1)) : null;
        }
    }
    public void CreateBackup(string destination)
    {
        lock (gate)
        {
            if (File.Exists(destination)) throw new IOException("Choose a new backup filename.");
            var staging = Path.Combine(root, "backup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            try
            {
                using (var db = Open()) using (var copy = Open(Path.Combine(staging, "finance.sqlite"))) db.BackupDatabase(copy);
                var manifest = new Dictionary<string, string> { ["finance.sqlite"] = Evidence.Hash(Path.Combine(staging, "finance.sqlite")) };
                using (var db = Open()) using (var cmd = Command(db, "SELECT hash,extension FROM documents")) using (var r = cmd.ExecuteReader())
                    while (r.Read())
                    {
                        var relative = "documents/" + r.GetString(0) + r.GetString(1).ToLowerInvariant();
                        var output = Path.Combine(staging, relative);
                        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                        File.Copy(DocumentPath(r.GetString(0), r.GetString(1)), output);
                        if (Evidence.Hash(output) != r.GetString(0)) throw new IOException("Evidence checksum mismatch.");
                        manifest[relative] = Evidence.Hash(output);
                    }
                File.WriteAllText(Path.Combine(staging, "manifest.json"), JsonSerializer.Serialize(manifest));
                ZipFile.CreateFromDirectory(staging, destination);
                using var validated = new ValidatedBackup(destination, root);
            }
            finally { Directory.Delete(staging, true); }
        }
    }
    public void RestoreBackup(string source)
    {
        lock (gate)
        {
            using var valid = new ValidatedBackup(source, root);
            var before = Path.Combine(root, $"before-restore-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.spondebackup");
            CreateBackup(before);
            foreach (var file in Directory.EnumerateFiles(Path.Combine(valid.Directory, "documents")))
            {
                var destination = Path.Combine(root, "documents", Path.GetFileName(file));
                if (!File.Exists(destination)) File.Copy(file, destination);
            }
            using var incoming = Open(Path.Combine(valid.Directory, "finance.sqlite"));
            using var db = Open();
            incoming.BackupDatabase(db);
            Migrate(db);
        }
    }
    private sealed class ValidatedBackup : IDisposable
    {
        public string Directory { get; }
        public ValidatedBackup(string archive, string parent)
        {
            Directory = Path.Combine(parent, "restore-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Directory);
            System.IO.Directory.CreateDirectory(Path.Combine(Directory, "documents"));
            try
            {
                using var zip = ZipFile.OpenRead(archive);
                var manifestEntry = zip.GetEntry("manifest.json") ?? throw new IOException("Missing backup manifest.");
                using var stream = manifestEntry.Open();
                var manifest = JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? throw new IOException("Invalid manifest.");
                if (!manifest.ContainsKey("finance.sqlite")) throw new IOException("Missing database.");
                if (zip.Entries.Select(e => e.FullName).Distinct().Count() != zip.Entries.Count) throw new IOException("Duplicate backup entries.");
                foreach (var (relative, hash) in manifest)
                {
                    if (relative != "finance.sqlite" && !System.Text.RegularExpressions.Regex.IsMatch(relative, @"^documents/[A-F0-9]{64}\.(pdf|xlsx)$")) throw new IOException("Invalid backup path.");
                    var entry = zip.GetEntry(relative) ?? throw new IOException("Missing backup file.");
                    entry.ExtractToFile(Path.Combine(Directory, relative));
                    if (Evidence.Hash(Path.Combine(Directory, relative)) != hash) throw new IOException("Backup checksum mismatch.");
                }
                using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(Directory, "finance.sqlite"), Pooling = false, Mode = SqliteOpenMode.ReadOnly }.ToString());
                db.Open();
                var version = Convert.ToInt32(Scalar(db, "PRAGMA user_version"));
                if (version < 1 || version > SchemaVersion || (string)Scalar(db, "PRAGMA integrity_check")! != "ok") throw new IOException("Incompatible or damaged backup database.");
                if (Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM (SELECT t.id FROM transactions t LEFT JOIN postings p ON p.entry_id=t.id GROUP BY t.id HAVING SUM(p.debit_cents) != 0 OR COUNT(p.entry_id) < 2)")) != 0) throw new IOException("Unbalanced backup ledger.");
                using (var foreignKeys = Command(db, "PRAGMA foreign_key_check")) using (var check = foreignKeys.ExecuteReader())
                    if (check.Read()) throw new IOException("Backup contains broken references.");
                if (version >= 2) foreach (var entry in ReadJson<JournalEntry>(db, "opening_entries")) entry.Validate();
                using var documents = Command(db, "SELECT hash,extension FROM documents");
                using var r = documents.ExecuteReader();
                while (r.Read())
                {
                    var relative = "documents/" + r.GetString(0) + r.GetString(1).ToLowerInvariant();
                    if (!manifest.TryGetValue(relative, out var hash) || hash != r.GetString(0)) throw new IOException("Backup is missing evidence.");
                }
            }
            catch { Dispose(); throw; }
        }
        public void Dispose() => System.IO.Directory.Delete(Directory, true);
    }
    private static ImmutableArray<T> ReadJson<T>(SqliteConnection db, string table, SqliteTransaction? tx = null)
    {
        using var cmd = Command(db, "SELECT json FROM " + table, tx);
        using var r = cmd.ExecuteReader();
        var result = ImmutableArray.CreateBuilder<T>();
        while (r.Read()) result.Add(JsonSerializer.Deserialize<T>(r.GetString(0))!);
        return result.ToImmutable();
    }
    private static SqliteCommand Command(SqliteConnection db, string sql, SqliteTransaction? tx = null, params (string, object)[] parameters)
    {
        var cmd = db.CreateCommand(); cmd.CommandText = sql; cmd.Transaction = tx;
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value);
        return cmd;
    }
    private static void Execute(SqliteConnection db, string sql, SqliteTransaction? tx = null, params (string, object)[] parameters)
    { using var cmd = Command(db, sql, tx, parameters); cmd.ExecuteNonQuery(); }
    private static object? Scalar(SqliteConnection db, string sql, SqliteTransaction? tx = null, params (string, object)[] parameters)
    { using var cmd = Command(db, sql, tx, parameters); return cmd.ExecuteScalar(); }
    public void Dispose() => lease.Dispose();
}
