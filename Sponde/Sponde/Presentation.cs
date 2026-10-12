using System.Collections.Immutable;
using System.Globalization;
using Sponde.Domain;

namespace Sponde;

public static class Presentation
{
    public static CultureInfo German { get; } = CultureInfo.GetCultureInfo("de-DE");
    public static string Currency(long cents) => (cents / 100m).ToString("C", German);
    public static string Month(DateOnly date) => date.ToString("MMMM yyyy", German);
    public static string Status(ReviewStatus status) => status switch { ReviewStatus.Assigned => "Zugeordnet", ReviewStatus.Review => "Prüfen", _ => "Zweck unbekannt" };
    public static ImmutableArray<DateOnly> Months(Dataset data) => data.Transactions.Select(t => new DateOnly(t.Date.Year, t.Date.Month, 1)).Distinct().Order().ToImmutableArray();
}
