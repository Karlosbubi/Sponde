using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using Sponde.Application;
using Sponde.Domain;

namespace Sponde.Infrastructure;

public static class PayrollParser
{
    public static ImportPreview Parse(string path, string hash, ImmutableArray<PdfPageText> pages)
    {
        var source = Path.GetFileName(path);
        var issues = ImmutableArray.CreateBuilder<string>();
        var current = pages.Where(p => p.Text.Contains("Gesamtbrutto", StringComparison.Ordinal) &&
            !Regex.IsMatch(p.Text, @"für\s+\p{L}+\s+\d{4}\s+R\b")).ToArray();
        if (current.Length != 1) return new(source, Dataset.Empty, [new(path, hash)], ["Payslip must contain exactly one current payroll page."], "pdf-payroll-v1");
        var lines = StatementReader.Lines(current[0].Text);
        var period = Regex.Match(current[0].Text, @"für\s+(\p{L}+)\s+(\d{4})(?!\s*R)");
        if (!period.Success) return new(source, Dataset.Empty, [new(path, hash)], ["Missing current payroll period."], "pdf-payroll-v1");
        var month = DateOnly.FromDateTime(DateTime.ParseExact($"01 {period.Groups[1].Value} {period.Groups[2].Value}", "dd MMMM yyyy", CultureInfo.GetCultureInfo("de-DE")));
        long Value(string prefix, bool required = true)
        {
            var line = lines.FirstOrDefault(l => l.StartsWith(prefix, StringComparison.Ordinal) &&
                Regex.IsMatch(l[prefix.Length..], @"-?[\d.]+,\d{2}"));
            if (line is null) { if (required) issues.Add($"Missing payroll field: {prefix}."); return 0; }
            // Percent rates precede amount columns; strip those before reading values.
            var text = Regex.Replace(line[prefix.Length..], @"[\d.,]+\s*%", "");
            var amounts = Regex.Matches(text, @"-?[\d.]+,\d{2}");
            if (amounts.Count == 0) { issues.Add($"Invalid payroll field: {prefix}."); return 0; }
            // Last amount is YTD when several columns exist. Month is the
            // penultimate value; gross/net/payout have their own layouts.
            var index = prefix is "Gesamtbrutto" or "Gesetzliches Netto" or "Überweisung" or "Summe Rückrechnung" ? 0 : amounts.Count >= 3 ? amounts.Count - 2 : 0;
            return StatementReader.Cents(amounts[index].Value);
        }
        var gross = Value("Gesamtbrutto");
        var deductions = ImmutableArray.CreateBuilder<PayrollLine>();
        foreach (var label in new[] { "Lohnsteuer", "Kirchensteuer", "Krankenversicherung", "Rentenversicherung", "Arbeitslosenversicherung", "Pflegeversicherung" })
            deductions.Add(new(label, new(Value(label))));
        var net = Value("Gesetzliches Netto");
        var correction = Value("Summe Rückrechnung", false);
        // Read post-net adjustment rows by their location and numeric columns,
        // never by employee, employer or meal-provider names.
        var netIndex = Array.FindIndex(lines, l => l.StartsWith("Gesetzliches Netto", StringComparison.Ordinal));
        var payoutIndex = Array.FindIndex(lines, l => l.StartsWith("Überweisung", StringComparison.Ordinal));
        var postNet = lines.Skip(netIndex + 1).Take(Math.Max(0, payoutIndex - netIndex - 1))
            .Where(l => !l.StartsWith("Summe Rückrechnung", StringComparison.Ordinal))
            .Select(l => Regex.Match(l, @"^.+?\s+(-?[\d.]+,\d{2})(?:\s+EUR|\s*€)?$"))
            .Where(m => m.Success).Select(m => StatementReader.Cents(m.Groups[1].Value)).ToArray();
        var postNetDeductions = -postNet.Sum();
        var payout = Value("Überweisung");
        var earnings = ImmutableArray.CreateBuilder<PayrollLine>();
        var grossIndex = Array.FindIndex(lines, l => l.StartsWith("Gesamtbrutto", StringComparison.Ordinal));
        foreach (var line in lines.Take(Math.Max(0, grossIndex)).Where(l => Regex.IsMatch(l, @"\S.+\s+-?[\d.]+,\d{2}\s*$")))
        {
            var values = Regex.Matches(line, @"-?[\d.]+,\d{2}");
            if (values.Count > 0)
            {
                var label = Regex.Split(line, @"\s+LSG\b|\s+-?[\d.]+,\d{2}")[0].Trim();
                earnings.Add(new(label, new(StatementReader.Cents(values[^1].Value))));
            }
        }
        var adjustments = ImmutableArray.CreateBuilder<PayrollAdjustment>();
        foreach (var page in pages.Where(p => Regex.IsMatch(p.Text, @"für\s+\p{L}+\s+\d{4}\s+R\b")))
        {
            var prior = Regex.Match(page.Text, @"für\s+(\p{L}+)\s+(\d{4})\s+R\b");
            var amount = Regex.Match(page.Text, @"Rückrechnungsbetrag\s+(-?[\d.]+,\d{2})");
            if (!amount.Success) { issues.Add($"Missing retroactive adjustment on page {page.Number}."); continue; }
            var date = DateOnly.FromDateTime(DateTime.ParseExact($"01 {prior.Groups[1].Value} {prior.Groups[2].Value}", "dd MMMM yyyy", CultureInfo.GetCultureInfo("de-DE")));
            adjustments.Add(new(date, page.Number, new(-StatementReader.Cents(amount.Groups[1].Value))));
        }
        if (adjustments.Count > 0 && adjustments.Sum(a => a.NetEffect.Cents) != correction) issues.Add("Retroactive pages do not reconcile to the current correction amount.");
        if (gross - deductions.Sum(l => l.Amount.Cents) != net) issues.Add("Gross salary and statutory deductions do not reconcile to net pay.");
        if (net + correction - postNetDeductions != payout) issues.Add("Net pay, corrections and post-net deductions do not reconcile to payout.");
        var slip = new Payslip(source, month, new(gross), new(deductions.Sum(l => l.Amount.Cents)), new(net), new(correction), new(postNetDeductions), new(payout), deductions.ToImmutable(), earnings.ToImmutable(), adjustments.ToImmutable());
        return new(source, new([], [], [], [slip]), [new(path, hash)], issues.ToImmutable(), "pdf-payroll-v2");
    }
}
