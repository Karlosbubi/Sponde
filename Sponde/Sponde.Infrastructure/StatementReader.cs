using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using Sponde.Application;
using Sponde.Domain;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Sponde.Infrastructure;

public sealed record PdfPageText(int Number, string Text);
public interface IPdfFormatParser
{
    string Id { get; }
    bool Recognizes(string text);
    ImportPreview Parse(string path, string hash, ImmutableArray<PdfPageText> pages);
}

public sealed class N26FormatParser : IPdfFormatParser
{
    public string Id => "n26";
    public bool Recognizes(string text) => text.Contains("Dein alter Kontostand", StringComparison.OrdinalIgnoreCase) && text.Contains("Dein neuer Kontostand", StringComparison.OrdinalIgnoreCase);
    public ImportPreview Parse(string path, string hash, ImmutableArray<PdfPageText> pages) => N26Parser.Parse(path, hash, pages);
}
public sealed class VrFormatParser : IPdfFormatParser
{
    public string Id => "vr";
    public bool Recognizes(string text) => text.Contains("alter Kontostand vom", StringComparison.OrdinalIgnoreCase) && text.Contains("neuer Kontostand v.", StringComparison.OrdinalIgnoreCase);
    public ImportPreview Parse(string path, string hash, ImmutableArray<PdfPageText> pages) => VrParser.Parse(path, hash, pages);
}
public sealed class PayrollFormatParser : IPdfFormatParser
{
    public string Id => "payroll";
    public bool Recognizes(string text) => text.Contains("Gesamtbrutto", StringComparison.OrdinalIgnoreCase) && text.Contains("Gesetzliches Netto", StringComparison.OrdinalIgnoreCase);
    public ImportPreview Parse(string path, string hash, ImmutableArray<PdfPageText> pages) => PayrollParser.Parse(path, hash, pages);
}

public sealed class StatementReader(IEnumerable<IPdfFormatParser>? formats = null) : IStatementReader
{
    private readonly ImmutableArray<IPdfFormatParser> formats = (formats ?? DefaultFormats()).ToImmutableArray();
    private static IEnumerable<IPdfFormatParser> DefaultFormats() => [new N26FormatParser(), new VrFormatParser(), new PayrollFormatParser()];
    public static ImmutableArray<PdfPageText> Extract(string path, CancellationToken cancellationToken = default)
    {
        using var pdf = PdfDocument.Open(path);
        return pdf.GetPages().Select(p => { cancellationToken.ThrowIfCancellationRequested(); return new PdfPageText(p.Number, ContentOrderTextExtractor.GetText(p)); }).ToImmutableArray();
    }
    public ImportPreview Read(string path, CancellationToken cancellationToken = default)
    {
        if (!Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            return Unsupported(path, "", "Only PDF documents can be imported.");
        ImmutableArray<PdfPageText> pages;
        try { pages = Extract(path, cancellationToken); }
        catch (Exception ex) when (ex is UglyToad.PdfPig.Core.PdfDocumentFormatException or UglyToad.PdfPig.Exceptions.PdfDocumentEncryptedException)
        { throw new IOException("PDF is damaged, encrypted or cannot be read.", ex); }
        cancellationToken.ThrowIfCancellationRequested();
        return RecognizeAndParse(path, Evidence.Hash(path), pages, formats);
    }
    public static ImportPreview Parse(string path, string hash, ImmutableArray<PdfPageText> pages) => RecognizeAndParse(path, hash, pages, DefaultFormats());
    public static ImportPreview RecognizeAndParse(string path, string hash, ImmutableArray<PdfPageText> pages, IEnumerable<IPdfFormatParser> formats)
    {
        var normalized = pages.Select(p => p with { Text = p.Text.Replace('\u00a0', ' ').Replace('\u2212', '-') }).ToImmutableArray();
        var all = string.Join("\n", normalized.Select(p => p.Text));
        var recognized = formats.Where(f => f.Recognizes(all)).ToArray();
        if (recognized.Length != 1) return Unsupported(path, hash, recognized.Length == 0 ? "Unsupported PDF layout or missing text. No entries were created." : "Ambiguous PDF format. No entries were created.");
        try
        {
            var preview = recognized[0].Parse(path, hash, normalized);
            var issues = preview.Issues.ToBuilder();
            if (preview.Data.Transactions.Select(t => t.Id).Distinct().Count() != preview.Data.Transactions.Length) issues.Add("Duplicate source observation identities.");
            foreach (var statement in preview.Data.Statements)
                if (!Reports.Reconcile(preview.Data.Transactions, statement).Balanced && !issues.Any(i => i.Contains("balance", StringComparison.OrdinalIgnoreCase))) issues.Add("Statement movements do not reconcile.");
            return preview with { Issues = issues.ToImmutable() };
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException)
        { return Unsupported(path, hash, $"The recognized {recognized[0].Id} format contains invalid required data. No entries were created."); }
    }
    private static ImportPreview Unsupported(string path, string hash, string issue) => new(Path.GetFileName(path), Dataset.Empty, [new(path, hash)], [issue], "pdf-unsupported-v1");
    internal static string AccountIdentifier(string text)
    {
        var match = Regex.Match(text, @"(?:Space\s+)?IBAN:\s*([A-Z]{2}\d{2}[ A-Z0-9]+?)(?=\s*[•|]\s*BIC|\s+BIC|\s*$)", RegexOptions.Multiline);
        return match.Success ? string.Concat(match.Groups[1].Value.Where(c => !char.IsWhiteSpace(c))) : "";
    }
    internal static long Cents(string value)
    {
        var clean = value.Replace("€", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal);
        return Money.FromEuros(decimal.Parse(clean, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.GetCultureInfo("de-DE"))).Cents;
    }
    internal static DateOnly Date(string value) => DateOnly.ParseExact(value, "dd.MM.yyyy", CultureInfo.InvariantCulture);
    internal static string[] Lines(string text) => text.Replace("\r", "", StringComparison.Ordinal).Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    internal static Transaction Create(string hash, int ordinal, DateOnly date, string account, string party, long cents, string source, int page, string purpose, DateOnly? valueDate)
    {
        var savings = party.Contains("Instant Savings", StringComparison.OrdinalIgnoreCase);
        var transfer = party.Equals("Von Main Account", StringComparison.OrdinalIgnoreCase) || party.Equals("An Main Account", StringComparison.OrdinalIgnoreCase);
        return new(Evidence.Id($"pdf:{hash}:{account}:{ordinal}"), date, account, party, new(cents),
            savings ? MovementKind.Savings : transfer ? MovementKind.Transfer : cents >= 0 ? MovementKind.Income : MovementKind.Expense,
            savings ? "Tagesgeld" : transfer ? "Eigene Überträge" : "Nicht zugeordnet",
            savings || transfer ? ReviewStatus.Assigned : ReviewStatus.Review, "", source, page, purpose, valueDate, "");
    }
}

public static class N26Parser
{
    public static ImportPreview Parse(string path, string hash, ImmutableArray<PdfPageText> pages)
    {
        var rows = ImmutableArray.CreateBuilder<Transaction>();
        var balances = ImmutableArray.CreateBuilder<StatementBalance>();
        var accounts = new Dictionary<string, BankAccount>(StringComparer.Ordinal);
        var issues = ImmutableArray.CreateBuilder<string>();
        var source = Path.GetFileName(path);
        foreach (var page in pages)
        {
            var accountMatch = Regex.Match(page.Text, @"Space:\s*([^\r\n]+)");
            var account = accountMatch.Success ? "N26 " + accountMatch.Groups[1].Value.Trim() : "N26";
            var identifier = StatementReader.AccountIdentifier(page.Text);
            if (identifier.Length > 0) accounts[account] = new(account, identifier);
            var period = Regex.Match(page.Text, @"(\d{2}\.\d{2}\.\d{4})\s+bis\s+(\d{2}\.\d{2}\.\d{4})");
            if (!period.Success) { issues.Add($"Page {page.Number}: missing statement period."); continue; }
            var month = StatementReader.Date(period.Groups[1].Value);
            var old = Regex.Match(page.Text, @"Dein alter Kontostand\s*([+-]?[\d.]+,\d{2})\s*€");
            var closing = Regex.Match(page.Text, @"Dein neuer Kontostand\s*([+-]?[\d.]+,\d{2})\s*€");
            if (old.Success && closing.Success)
                balances.Add(new(source, account, month, new(StatementReader.Cents(old.Groups[1].Value)), new(StatementReader.Cents(closing.Groups[1].Value)), null));
            var lines = StatementReader.Lines(page.Text);
            var header = Array.FindIndex(lines, l => l.Contains("Verbuchungsdatum", StringComparison.Ordinal));
            if (header < 0) continue;
            var block = new List<string>();
            for (var i = header + 1; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.Contains("Kontoauszug Nr.", StringComparison.Ordinal)) break;
                var match = Regex.Match(line, @"^(\d{2}\.\d{2}\.\d{4})\s+([+-]?[\d.]+,\d{2})\s*€$");
                if (match.Success)
                {
                    if (block.Count == 0) { issues.Add($"Page {page.Number}: transaction description missing."); continue; }
                    var value = Regex.Match(string.Join(" ", block), @"Wertstellung\s+(\d{2}\.\d{2}\.\d{4})");
                    rows.Add(StatementReader.Create(hash, rows.Count, StatementReader.Date(match.Groups[1].Value), account,
                        block[0], StatementReader.Cents(match.Groups[2].Value), source, page.Number, string.Join(" ", block.Skip(1)), value.Success ? StatementReader.Date(value.Groups[1].Value) : null));
                    block.Clear();
                }
                else block.Add(line);
            }
            if (block.Count > 0) issues.Add($"Page {page.Number}: unparsed transaction text.");
        }
        if (rows.Count == 0 || balances.Count == 0) issues.Add("No complete N26 statement found.");
        foreach (var account in rows.Select(t => t.Account).Distinct())
            if (!balances.Any(b => b.Account == account)) issues.Add($"Missing balance summary for {account}.");
        foreach (var balance in balances)
            if (!Reports.Reconcile(rows, balance).Balanced) issues.Add($"Balance mismatch for {balance.Account}, {balance.Month:yyyy-MM}.");
        return new(source, new(rows.ToImmutable(), balances.ToImmutable(), [], [], Accounts: accounts.Values.ToImmutableArray()), [new(path, hash)], issues.ToImmutable(), "pdf-n26-v2");
    }
}

public static class VrParser
{
    public static ImportPreview Parse(string path, string hash, ImmutableArray<PdfPageText> pages)
    {
        var source = Path.GetFileName(path);
        var all = string.Join("\n", pages.Select(p => p.Text));
        var issues = ImmutableArray.CreateBuilder<string>();
        var rows = ImmutableArray.CreateBuilder<Transaction>();
        var closing = Regex.Match(all, @"neuer Kontostand v\.\s*(\d{2}\.\d{2}\.\d{4})\s*([\d.]+,\d{2})\s*([SH])");
        var opening = Regex.Match(all, @"alter Kontostand vom\s*(\d{2}\.\d{2}\.\d{4})\s*(?:VorgangWert\s*)?([\d.]+,\d{2})\s*([SH])");
        if (!closing.Success || !opening.Success)
            return new(source, Dataset.Empty, [new(path, hash)], ["VR balance layout not recognized."], "pdf-vr-v1");
        var month = StatementReader.Date(closing.Groups[1].Value);
        foreach (var page in pages)
        {
            Transaction? pending = null;
            var details = new List<string>();
            void Flush()
            {
                if (pending is null) return;
                var party = details.Count > 0 ? details[0] : pending.Counterparty;
                var item = StatementReader.Create(hash, rows.Count, pending.Date, "VR", party, pending.Amount.Cents, source, page.Number,
                    pending.Purpose + " " + string.Join(" ", details.Skip(1)), pending.ValueDate);
                rows.Add(item); pending = null; details.Clear();
            }
            foreach (var line in StatementReader.Lines(page.Text))
            {
                var match = Regex.Match(line, @"^(\d{2}\.\d{2}\.)\s+(\d{2}\.\d{2}\.)\s+(.+?)\s+([\d.]+,\d{2})\s*([SH])$");
                if (match.Success)
                {
                    Flush();
                    DateOnly ShortDate(string date)
                    {
                        var value = StatementReader.Date(date + month.Year);
                        return value.Month > month.Month + 6 ? value.AddYears(-1) : value;
                    }
                    pending = StatementReader.Create(hash, rows.Count, ShortDate(match.Groups[1].Value), "VR", "",
                        StatementReader.Cents(match.Groups[4].Value) * (match.Groups[5].Value == "S" ? -1 : 1), source, page.Number,
                        match.Groups[3].Value, ShortDate(match.Groups[2].Value));
                }
                else if (line.StartsWith("Übertrag", StringComparison.Ordinal) || line.StartsWith("neuer Kontostand", StringComparison.Ordinal) || line.Contains("Rechnungsabschluss", StringComparison.Ordinal)) Flush();
                else if (pending is not null) details.Add(line);
            }
            Flush();
        }
        var balance = new StatementBalance(source, "VR", new(month.Year, month.Month, 1),
            new(StatementReader.Cents(opening.Groups[2].Value) * (opening.Groups[3].Value == "S" ? -1 : 1)),
            new(StatementReader.Cents(closing.Groups[2].Value) * (closing.Groups[3].Value == "S" ? -1 : 1)), null);
        if (rows.Count == 0 || !Reports.Reconcile(rows, balance).Balanced) issues.Add("VR transactions do not reconcile to the statement balance.");
        var identifier = StatementReader.AccountIdentifier(pages[0].Text);
        return new(source, new(rows.ToImmutable(), [balance], [], [], Accounts: identifier.Length > 0 ? [new("VR", identifier)] : []), [new(path, hash)], issues.ToImmutable(), "pdf-vr-v2");
    }
}
