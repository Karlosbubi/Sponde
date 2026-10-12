using System.Collections.Immutable;
using Sponde.Application;
using Sponde.Domain;
using Sponde.Infrastructure;
using Xunit;

namespace Sponde.Tests;

public sealed class PdfPipelineTests
{
    [Fact]
    public void NonPdfInputIsRejectedWithoutOpeningIt()
    {
        var preview = new StatementReader().Read("does-not-exist.xlsx");
        Assert.False(preview.CanCommit);
        Assert.Contains("Only PDF", preview.Issues.Single());
    }

    [Fact]
    public void FormatIsDetectedFromContentRatherThanFilename()
    {
        var preview = StatementReader.Parse("arbitrary-name.pdf", "hash", [new(1, """
            Beschreibung Verbuchungsdatum Betrag
            Example merchant
            Wertstellung 02.09.2026
            03.09.2026 -5,00€
            Kontoauszug Nr. 09/2026
            01.09.2026 bis 30.09.2026
            """), new(2, """
            Dein alter Kontostand +10,00€
            Dein neuer Kontostand +5,00€
            01.09.2026 bis 30.09.2026
            """)]);
        Assert.True(preview.CanCommit, string.Join(";", preview.Issues));
        Assert.Equal("pdf-n26-v2", preview.ParserVersion);
        Assert.Empty(preview.Data.Budget);
        Assert.Equal(ReviewStatus.Review, preview.Data.Transactions.Single().Review);
    }

    [Fact]
    public void AdditionalFormatCanBeRegisteredWithoutChangingTheReader()
    {
        var preview = StatementReader.RecognizeAndParse("new-format.pdf", "hash", [new(1, "Custom statement")], [new CustomFormat()]);
        Assert.True(preview.CanCommit);
        Assert.Equal("pdf-custom-v1", preview.ParserVersion);
    }

    [Fact]
    public void SharedValidationRejectsUnbalancedExtensionParserOutput()
    {
        var preview = StatementReader.RecognizeAndParse("new-format.pdf", "hash", [new(1, "Custom statement")], [new CustomFormat(true)]);
        Assert.False(preview.CanCommit);
        Assert.Contains("Statement movements do not reconcile.", preview.Issues);
    }

    [Fact]
    public void AmbiguousContentIsNotAssignedToAnArbitraryFormat()
    {
        var preview = StatementReader.RecognizeAndParse("new-format.pdf", "hash", [new(1, "Custom statement")], [new CustomFormat(), new CustomFormat()]);
        Assert.False(preview.CanCommit);
        Assert.Contains("Ambiguous", preview.Issues.Single());
    }

    [Fact]
    public void OwnTransfersUseAccountIdentifiersAndPreserveManualDecisions()
    {
        var row = new Transaction(Guid.NewGuid(), new(2026, 9, 1), "Primary", "Example recipient", new(-500), MovementKind.Expense,
            "Nicht zugeordnet", ReviewStatus.Review, "", "source.pdf", 1, "IBAN: XX00 1234 5678", null, "");
        BankAccount[] accounts = [new("Secondary", "XX0012345678")];
        Assert.Equal(MovementKind.Transfer, OwnTransfers.Apply(row, accounts).Kind);
        Assert.Equal(row, OwnTransfers.Apply(row with { Purpose = "Ordinary payment" }, accounts) with { Purpose = row.Purpose });
        var manual = row with { Category = "Reviewed", Review = ReviewStatus.Assigned };
        Assert.Equal(manual, OwnTransfers.Apply(manual, accounts));
    }

    private sealed class CustomFormat(bool unbalanced = false) : IPdfFormatParser
    {
        public string Id => "custom";
        public bool Recognizes(string text) => text.Contains("Custom statement", StringComparison.Ordinal);
        public ImportPreview Parse(string path, string hash, ImmutableArray<PdfPageText> pages)
        {
            var row = new Transaction(Evidence.Id(hash), new(2026, 9, 1), "Custom account", "Example", new(-500), MovementKind.Expense,
                "Nicht zugeordnet", ReviewStatus.Review, "", Path.GetFileName(path), 1, "", null, "");
            var balance = new StatementBalance(row.Source, row.Account, new(2026, 9, 1), new(1000), new(unbalanced ? 499 : 500), 1);
            return new(row.Source, new([row], [balance], [], []), [new(path, hash)], [], "pdf-custom-v1");
        }
    }
}
