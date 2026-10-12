using Sponde.Domain;
using Sponde.Infrastructure;
using Xunit;

namespace Sponde.Tests;

public sealed class ParserTests
{
    [Fact]
    public void N26SpaceAndMainRemainSeparateWithRepeatedSameDayAmounts()
    {
        var preview = N26Parser.Parse("synthetic.pdf", "hash", [new(1, """
            Beschreibung Verbuchungsdatum Betrag
            Merchant A
            Mastercard
            Wertstellung 02.09.2026
            03.09.2026 -5,00€
            Merchant A
            Mastercard
            Wertstellung 02.09.2026
            03.09.2026 -5,00€
            Kontoauszug Nr. 09/2026
            01.09.2026 bis 30.09.2026
            """), new(2, """
            Dein alter Kontostand +20,00€
            Dein neuer Kontostand +10,00€
            01.09.2026 bis 30.09.2026
            """), new(3, """
            Beschreibung Verbuchungsdatum Betrag
            Merchant B
            Mastercard
            Wertstellung 04.09.2026
            04.09.2026 -3,00€
            Space Kontoauszug Nr. 09/2026
            Space: Test Space
            01.09.2026 bis 30.09.2026
            """), new(4, """
            Dein alter Kontostand +5,00€
            Dein neuer Kontostand +2,00€
            Space: Test Space
            01.09.2026 bis 30.09.2026
            """)]);
        Assert.True(preview.CanCommit, string.Join(";", preview.Issues)); Assert.Equal(3, preview.Data.Transactions.Length);
        Assert.Equal(3, preview.Data.Transactions.Select(t => t.Id).Distinct().Count());
        Assert.Equal(2, preview.Data.Statements.Length); Assert.Equal(new DateOnly(2026, 9, 2), preview.Data.Transactions[0].ValueDate);
    }
    [Fact]
    public void N26MismatchCannotCommit()
    {
        var preview = N26Parser.Parse("x.pdf", "hash", [new(1, "Dein alter Kontostand +10,00€\nDein neuer Kontostand +9,00€\n01.09.2026 bis 30.09.2026")]);
        Assert.False(preview.CanCommit); Assert.NotEmpty(preview.Issues);
    }
    [Fact]
    public void VrSignsAbbreviatedDatesAndWrappedPurposeArePreserved()
    {
        var preview = VrParser.Parse("vr.pdf", "hash", [new(1, """
            neuer Kontostand v. 30.09.2026 8,00 H
            alter Kontostand vom 31.08.2026
            VorgangWert
            10,00 H
            01.09. 31.08. Kartenzahlung PN:1 5,00 S
            Synthetic Shop
            Wrapped purpose
            continued
            02.09. 02.09. Gutschrift PN:2 3,00 H
            Synthetic refund
            Übertrag auf Blatt 2 8,00 H
            """)]);
        Assert.True(preview.CanCommit, string.Join(";", preview.Issues)); Assert.Equal(-500, preview.Data.Transactions[0].Amount.Cents);
        Assert.Contains("continued", preview.Data.Transactions[0].Purpose); Assert.Equal(new DateOnly(2026, 8, 31), preview.Data.Transactions[0].ValueDate);
    }
    [Fact]
    public void PayrollExcludesIncludedComponentsAndYearToDateAmounts()
    {
        var preview = PayrollParser.Parse("pay.pdf", "hash", [new(1, """
            Entgeltnachweis
            für September 2026
            Gesamtbrutto (EBeschV) 1.000,00 9.000,00
            Lohnsteuer 100,00 100,00 900,00
            Kirchensteuer 10,00 10,00 90,00
            Krankenversicherung 9,25 % 50,00 50,00 450,00
            KV Zusatzbeitrag (in KV-Abz. enth.) 10,00
            Rentenversicherung 9,30 % 90,00 90,00 810,00
            Arbeitslosenversicherung 1,30 % 20,00 20,00 180,00
            Pflegeversicherung 2,40 % 30,00 30,00 270,00
            Beitragszuschl. PV (in PV-Abz.enth) 5,00
            Gesetzliches Netto (EBeschV) 700,00
            Summe Rückrechnung -10,00
            Synthetic deduction -20,00
            Überweisung 670,00 EUR
            """)]);
        Assert.True(preview.CanCommit, string.Join(";", preview.Issues)); Assert.Equal(30000, preview.Data.Payslips.Single().Deductions.Cents);
        Assert.Empty(preview.Data.Transactions);
    }
    [Fact]
    public void UnknownPdfCannotInventTransactions()
    {
        var preview = StatementReader.Parse("scan.pdf", "hash", [new(1, "")]); Assert.False(preview.CanCommit); Assert.Empty(preview.Data.Transactions);
    }
}
