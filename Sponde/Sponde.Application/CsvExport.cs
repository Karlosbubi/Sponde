using System.Globalization;
using System.Text;
using Sponde.Domain;

namespace Sponde.Application;

public static class CsvExport
{
    public static string Transactions(IEnumerable<Transaction> rows)
    {
        var output = new StringBuilder("Id;BookingDate;Account;Counterparty;AmountEUR;Kind;Category;Review;Source;Page;Purpose\r\n");
        foreach (var row in rows)
        {
            var values = new[] { row.Id.ToString(), row.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), row.Account, row.Counterparty,
                row.Amount.Euros.ToString("0.00", CultureInfo.InvariantCulture), row.Kind.ToString(), row.Category, row.Review.ToString(), row.Source,
                row.Page.ToString(CultureInfo.InvariantCulture), row.Purpose };
            output.AppendLine(string.Join(';', values.Select(SafeCell)));
        }
        return output.ToString();
    }
    private static string SafeCell(string text)
    {
        // Quoting alone does not prevent spreadsheet formula injection.
        var trimmed = text.TrimStart();
        if (trimmed.Length > 0 && "=+@-".Contains(trimmed[0]) && !decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out _)) text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
