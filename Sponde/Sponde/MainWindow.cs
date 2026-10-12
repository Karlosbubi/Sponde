using System.Collections.Immutable;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Sponde.Application;
using Sponde.Domain;

namespace Sponde;

public sealed class MainWindow : Window
{
    private readonly FinanceService service;
    private readonly ContentControl content = new();
    private readonly ComboBox monthPicker = new() { Width = 205 };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = Brushes.SlateGray };
    private readonly StackPanel navigation = new() { Spacing = 10 };
    private Dataset data = Dataset.Empty;
    private DateOnly month = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private string screen = "Übersicht";
    private bool busy;
    private CancellationTokenSource? operation;
    private readonly Button cancel = new() { Content = "Abbrechen", IsEnabled = false, Margin = new(12, 0, 0, 0) };
    private PdfBatchPreview? batch;
    private PdfBatchResult? batchResult;
    private int previewIndex;

    public MainWindow(FinanceService service)
    {
        this.service = service;
        Title = "Sponde · Budget & Finanzen"; Width = 1400; Height = 940; MinWidth = 1000; MinHeight = 700;
        Background = Brush("#F3F5F8"); RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
        var layout = new Grid { ColumnDefinitions = new("220,*") };
        var sidebar = new StackPanel { Margin = new(24, 34), Spacing = 30 };
        sidebar.Children.Add(new TextBlock { Text = "sponde", FontSize = 32, FontWeight = FontWeight.Bold, Foreground = Brushes.White });
        sidebar.Children.Add(new TextBlock { Text = "DEINE FINANZEN\nKlar. Nachvollziehbar. Lokal.", FontSize = 12, Foreground = Brush("#B8C5D9"), LineHeight = 21 });
        foreach (var name in new[] { "Übersicht", "Buchungen", "Import & Prüfung", "Kontoabgleich", "Budget", "Regeln", "Entgelt", "Sicherung" })
        {
            var button = new Button { Content = name, HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new(14, 11), Background = Brush("#22334F"), Foreground = Brushes.White };
            button.Click += (_, _) => { if (!busy) { screen = name; Render(); } }; navigation.Children.Add(button);
        }
        sidebar.Children.Add(navigation);
        sidebar.Children.Add(new TextBlock { Text = "Offline · EUR\nDeine Daten bleiben lokal.", Foreground = Brush("#B8C5D9"), FontSize = 12, LineHeight = 21 });
        layout.Children.Add(new Border { Background = Brush("#15243B"), Child = sidebar });
        var main = new Grid { RowDefinitions = new("Auto,*,Auto"), Margin = new(32, 26) };
        Grid.SetColumn(main, 1); layout.Children.Add(main);
        var top = new Grid { ColumnDefinitions = new("*,Auto,Auto"), Margin = new(0, 0, 0, 20) };
        top.Children.Add(new TextBlock { Text = "Budget & Finanzen", FontSize = 17, FontWeight = FontWeight.SemiBold, Foreground = Brush("#526077"), VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(monthPicker, 1); top.Children.Add(monthPicker); main.Children.Add(top);
        Grid.SetColumn(cancel, 2); top.Children.Add(cancel); cancel.Click += (_, _) => operation?.Cancel();
        monthPicker.SelectionChanged += (_, _) => { if (monthPicker.SelectedItem is MonthChoice choice) { month = choice.Date; Render(); } };
        Grid.SetRow(content, 1); main.Children.Add(content);
        Grid.SetRow(status, 2); status.Margin = new(0, 12, 0, 0); main.Children.Add(status); Content = layout;
        Opened += async (_, _) => await Run(() => data = service.Read(), "Daten geladen.", Refresh);
        Closing += (_, e) => { if (busy) { e.Cancel = true; if (cancel.IsEnabled) operation?.Cancel(); status.Text = "Bitte warten, bis der laufende Vorgang beendet ist."; } };
    }
    private sealed record MonthChoice(DateOnly Date) { public override string ToString() => Presentation.Month(Date); }
    private void Refresh()
    {
        var months = Presentation.Months(data);
        if (!months.Contains(month) && months.Length > 0) month = months[^1];
        monthPicker.ItemsSource = months.Select(m => new MonthChoice(m)).ToArray();
        monthPicker.SelectedItem = monthPicker.Items.Cast<MonthChoice>().FirstOrDefault(c => c.Date == month); Render();
    }
    private async Task Run(Action action, string success, Action? then = null, bool cancellable = false)
    {
        if (busy) return;
        busy = true; operation = new(); cancel.IsEnabled = cancellable; navigation.IsEnabled = false; monthPicker.IsEnabled = false; content.IsEnabled = false; status.Text = "Wird verarbeitet …";
        try { await Task.Run(action); status.Text = success; then?.Invoke(); }
        catch (OperationCanceledException) { status.Text = "Import abgebrochen. Keine Buchungen übernommen."; }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException or FormatException or NotSupportedException or System.Data.Common.DbException)
        { status.Text = "Vorgang nicht abgeschlossen: " + ex.Message; }
        finally { operation.Dispose(); operation = null; cancel.IsEnabled = false; busy = false; navigation.IsEnabled = true; monthPicker.IsEnabled = true; content.IsEnabled = true; }
    }
    private void Render() => content.Content = screen switch
    {
        "Buchungen" => Transactions(),
        "Import & Prüfung" => Import(),
        "Kontoabgleich" => Reconcile(),
        "Budget" => Budget(),
        "Regeln" => Rules(),
        "Entgelt" => Payroll(),
        "Sicherung" => Backup(),
        _ => Overview()
    };
    private static SolidColorBrush Brush(string color) => new(Color.Parse(color));
    private static TextBlock Text(string text, double size = 14, bool bold = false) => new() { Text = text, FontSize = size, FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal, Foreground = Brush("#20314C"), TextWrapping = TextWrapping.Wrap };
    private static StackPanel Stack(double spacing = 14) => new() { Spacing = spacing };
    private static Border Card(Control child) => new() { Background = Brushes.White, CornerRadius = new(12), Padding = new(22), Child = child, BorderBrush = Brush("#E3E8EF"), BorderThickness = new(1) };
    private static Control Scroll(Control child) => new ScrollViewer { Content = child, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private static Button ActionButton(string label, Func<Task> action)
    { var b = new Button { Content = label, Padding = new(16, 10) }; b.Click += async (_, _) => await action(); return b; }
    private static StackPanel Heading(string title, string subtitle)
    { var s = Stack(8); s.Children.Add(Text(title, 29, true)); s.Children.Add(Text(subtitle, 13)); return s; }
    private Transaction[] MonthRows() => data.Transactions.Where(t => t.Date.Year == month.Year && t.Date.Month == month.Month).ToArray();
    private Control Overview()
    {
        var root = Stack(22); root.Children.Add(Heading("Dein Budget", Presentation.Month(month) + " · Abrechnung nach Buchungstag"));
        if (data.Transactions.IsEmpty)
        {
            var empty = Stack(); empty.Children.Add(Text("Willkommen bei Sponde", 22, true));
            empty.Children.Add(Text("Importiere deine Kontoauszüge als PDF. N26, VR und Entgeltnachweise werden anhand ihres Inhalts erkannt. Kategorien und Planwerte verwaltest du in Sponde."));
            empty.Children.Add(ActionButton("PDFs auswählen", ImportPdf)); root.Children.Add(Card(empty)); return Scroll(root);
        }
        var rows = MonthRows(); var summary = Reports.Month(rows, month);
        var metrics = new UniformGrid { Columns = 3, Rows = 2 };
        foreach (var (label, value, explanation) in new[] {
            ("Einnahmen", summary.Income, "Netto-Bankeingänge"), ("Ausgaben netto", summary.Expenses, "Nach Erstattungen · ohne Sparen"),
            ("Überschuss vor Sparen", summary.Surplus, "Einnahmen minus Ausgaben"), ("Sparen netto", summary.Savings, "Einzahlungen minus Entnahmen"),
            ("Kontoveränderung", summary.Movement, "Alle erfassten Kontobewegungen"), ("Offene Zuordnungen", (long)summary.Unresolved, "Kategorien und Bewegungsarten prüfen") })
        {
            var body = Stack(9); body.Children.Add(Text(label, 13)); body.Children.Add(Text(label == "Offene Zuordnungen" ? value.ToString(Presentation.German) : Presentation.Currency(value), 27, true)); body.Children.Add(Text(explanation, 11));
            var card = Card(body); card.Margin = new(0, 0, 12, 12); metrics.Children.Add(card);
        }
        root.Children.Add(metrics);
        var coverage = data.Statements.Where(s => s.Month.Year == month.Year && s.Month.Month == month.Month).ToArray();
        var verifiedIds = data.VerifiedTransactions.IsDefault ? [] : data.VerifiedTransactions.ToHashSet();
        var coverageText = rows.GroupBy(t => t.Account).Select(g => $"{g.Key}: {g.Count(t => verifiedIds.Contains(t.Id))}/{g.Count()} PDF-verifiziert");
        root.Children.Add(Text($"Belegabdeckung: {string.Join(" · ", coverageText)}. {summary.Unresolved} Zuordnungen offen. Depot-/Tagesgeldbestände sind nicht vollständig belegt.", 12));
        var detail = new Grid { ColumnDefinitions = new("*,*"), ColumnSpacing = 16 };
        var attention = Stack(10); attention.Children.Add(Text("Auffällige Posten", 18, true));
        foreach (var c in Reports.Categories(rows, data.Allocations).Take(4))
            attention.Children.Add(ActionButton(c.Category + "  " + Presentation.Currency(c.Cents), () => ShowCategory(c.Category)));
        attention.Children.Add(ActionButton($"{summary.Unresolved} offene Zuordnungen prüfen", () => { screen = "Buchungen"; Render(); return Task.CompletedTask; })); detail.Children.Add(Card(attention));
        var plans = Stack(10); plans.Children.Add(Text("Ist und aktueller Plan", 18, true));
        var effectiveBudget = Reports.BudgetAt(data.Budget, month);
        plans.Children.Add(Text("Einnahmenplan  " + Presentation.Currency(effectiveBudget.Where(b => b.Income).Sum(b => b.Monthly?.Cents ?? 0))));
        plans.Children.Add(Text("Ausgabenplan  " + Presentation.Currency(effectiveBudget.Where(b => !b.Income).Sum(b => b.Monthly?.Cents ?? 0))));
        plans.Children.Add(Text(effectiveBudget.IsEmpty ? "Noch kein Budget angelegt. Planwerte im Bereich Budget hinzufügen." : $"{effectiveBudget.Count(b => b.Monthly is null)} Planbeträge offen. Planversionen gelten ab ihrem gewählten Monat.", 12));
        var planCard = Card(plans); Grid.SetColumn(planCard, 1); detail.Children.Add(planCard); root.Children.Add(detail);
        var months = Presentation.Months(data).Select(m => Reports.Month(data.Transactions, m)).ToArray();
        var charts = new Grid { ColumnDefinitions = new("*,*"), ColumnSpacing = 16 };
        var chart = Stack(); chart.Children.Add(Text("Monatlicher Überschuss", 18, true)); chart.Children.Add(new TrendChart { Months = months, Height = 210 }); charts.Children.Add(Card(chart));
        var cumulative = Stack(); cumulative.Children.Add(Text("Überschuss kumuliert", 18, true)); cumulative.Children.Add(new TrendChart { Months = months, Cumulative = true, Height = 210 });
        var cumulativeCard = Card(cumulative); Grid.SetColumn(cumulativeCard, 1); charts.Children.Add(cumulativeCard); root.Children.Add(charts);
        var categories = Stack(12); categories.Children.Add(Text("Größte Ausgabenkategorien · Gesamtzeitraum", 18, true));
        var totals = Reports.Categories(data.Transactions, data.Allocations).Take(6).ToArray(); var max = Math.Max(1, totals.Select(c => c.Cents).DefaultIfEmpty(1).Max());
        foreach (var c in totals)
        {
            var line = new Grid { ColumnDefinitions = new("220,*,130"), ColumnSpacing = 15 }; line.Children.Add(Text(c.Category));
            var bar = new ProgressBar { Minimum = 0, Maximum = max, Value = Math.Max(0, c.Cents), Height = 9, VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("#12856A") };
            Grid.SetColumn(bar, 1); line.Children.Add(bar); var amount = Text(Presentation.Currency(c.Cents)); amount.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(amount, 2); line.Children.Add(amount); categories.Children.Add(line);
        }
        root.Children.Add(Card(categories)); return Scroll(root);
    }
    private static ListBox TransactionList(IEnumerable<Transaction> rows) => new()
    {
        ItemsSource = rows,
        Background = Brushes.White,
        ItemTemplate = new FuncDataTemplate<Transaction>((t, _) =>
        {
            if (t is null) return new TextBlock();
            var grid = new Grid { ColumnDefinitions = new("90,85,*,160,120,120"), ColumnSpacing = 10, Margin = new(8) };
            var values = new[] { t.Date.ToString("dd.MM.yy"), t.Account, t.Counterparty, t.Category, Presentation.Currency(t.Amount.Cents), Presentation.Status(t.Review) };
            for (var i = 0; i < values.Length; i++) { var cell = Text(values[i], 12); Grid.SetColumn(cell, i); grid.Children.Add(cell); }
            return grid;
        })
    };
    private Control Transactions()
    {
        var root = new Grid { RowDefinitions = new("Auto,Auto,*,Auto"), RowSpacing = 16 }; root.Children.Add(Heading("Buchungen", "Originale Bankbewegungen · Kategorien und Prüfstatus ändern"));
        var filters = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var search = new TextBox { PlaceholderText = "Suchen: Name, Zweck, Kategorie …", Width = 380 }; var unresolved = new CheckBox { Content = "Nur offene Zuordnungen" };
        filters.Children.Add(search); filters.Children.Add(unresolved); filters.Children.Add(ActionButton("CSV exportieren", ExportCsv)); Grid.SetRow(filters, 1); root.Children.Add(filters);
        var list = TransactionList([]); Grid.SetRow(list, 2); root.Children.Add(list); var editor = Stack(10); Grid.SetRow(editor, 3); root.Children.Add(Card(editor));
        void Filter() => list.ItemsSource = MonthRows().Where(t => (!unresolved.IsChecked.GetValueOrDefault() || t.Review != ReviewStatus.Assigned) && (string.IsNullOrWhiteSpace(search.Text) || $"{t.Counterparty} {t.Purpose} {t.Category}".Contains(search.Text, StringComparison.OrdinalIgnoreCase))).OrderBy(t => t.Date).ToArray();
        search.TextChanged += (_, _) => Filter(); unresolved.IsCheckedChanged += (_, _) => Filter(); editor.Children.Add(Text("Wähle eine Buchung, um ihre Zuordnung und Quelle zu prüfen.", 12));
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is not Transaction t) return;
            editor.Children.Clear(); editor.Children.Add(Text($"{t.Counterparty} · {Presentation.Currency(t.Amount.Cents)} · {t.Source}, Seite {t.Page}", 15, true)); editor.Children.Add(Text(t.Purpose, 12));
            var line = new WrapPanel { Orientation = Orientation.Horizontal };
            var category = new TextBox { Text = t.Category, Width = 220, PlaceholderText = "Kategorie", Margin = new(0, 0, 8, 8) };
            var kind = new ComboBox { ItemsSource = Enum.GetValues<MovementKind>(), SelectedItem = t.Kind, Width = 140, Margin = new(0, 0, 8, 8) };
            var review = new ComboBox { ItemsSource = Enum.GetValues<ReviewStatus>(), SelectedItem = t.Review, Width = 130, Margin = new(0, 0, 8, 8) };
            var note = new TextBox { Text = t.Note, Width = 200, PlaceholderText = "Hinweis", Margin = new(0, 0, 8, 8) };
            line.Children.Add(category); line.Children.Add(kind); line.Children.Add(review); line.Children.Add(note);
            line.Children.Add(ActionButton("Speichern", () => Run(() => { service.Classify(t.Id, (MovementKind)kind.SelectedItem!, category.Text ?? "", (ReviewStatus)review.SelectedItem!, note.Text ?? ""); data = service.Read(); }, "Zuordnung gespeichert.", Refresh)));
            editor.Children.Add(line);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            actions.Children.Add(ActionButton("Quellbeleg öffnen", () => OpenSource(t.Source)));
            if (t.Kind is MovementKind.Expense or MovementKind.Refund) actions.Children.Add(ActionButton("Auf Kategorien aufteilen", () => SplitTransaction(t)));
            editor.Children.Add(actions);
        }; Filter(); return root;
    }
    private Control Import()
    {
        var root = Stack(18); root.Children.Add(Heading("Import & Prüfung", "Vorschau prüfen, dann ausdrücklich übernehmen. Quelldateien bleiben unverändert."));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 }; buttons.Children.Add(ActionButton("PDFs auswählen", ImportPdf)); root.Children.Add(buttons);
        if (batch is null || batch.Items.IsEmpty) root.Children.Add(Card(Text("Mehrere Kontoauszüge und Entgeltnachweise gemeinsam auswählen. Belegart, Konto und Zeitraum werden aus dem Inhalt erkannt; Kontoauszüge werden automatisch vor Entgeltnachweisen verarbeitet. Jede Datei erhält eine eigene Vorschau und ein eigenes Ergebnis.")));
        else
        {
            root.Children.Add(Text($"{batch.Items.Length} PDFs ausgewählt · {batch.ReadyCount} bereit · {batch.Items.Count(i => i.Duplicate)} doppelt · {batch.Items.Count(i => !i.Duplicate && !i.Preview.CanCommit)} zu prüfen", 19, true));
            var commit = ActionButton("Geprüfte PDFs übernehmen", () => Run(() => { batchResult = service.CommitStatements(batch!, operation!.Token); data = service.Read(); }, "Sammelimport beendet. Ergebnisse je Datei prüfen.", Refresh, cancellable: true));
            commit.IsEnabled = batch.CanCommit && batchResult is null; root.Children.Add(commit);
            if (batchResult is not null)
                foreach (var result in batchResult.Files)
                {
                    var outcome = result.Outcome switch { FileImportOutcome.Imported => "Übernommen", FileImportOutcome.AlreadyPresent => "Bereits enthalten", FileImportOutcome.Skipped => "Nicht übernommen", FileImportOutcome.Cancelled => "Abgebrochen", _ => "Fehlgeschlagen" };
                    root.Children.Add(Card(Text($"{result.DisplayName} · {outcome} · {result.TransactionsAdded} neue Buchungen\n{result.OriginalName}: {result.Message}", 13)));
                }
            previewIndex = Math.Clamp(previewIndex, 0, batch.Items.Length - 1);
            var selector = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = batch.Items.Select((i, index) => new PreviewChoice(index, i.Preview.DisplayName, i.Preview.Origin)).ToArray(), SelectedIndex = previewIndex };
            selector.SelectionChanged += (_, _) => { if (selector.SelectedItem is PreviewChoice choice) { previewIndex = choice.Index; Render(); } }; root.Children.Add(selector);
            var item = batch.Items[previewIndex];
            var preview = item.Preview;
            root.Children.Add(Text($"{preview.DisplayName} · {preview.Data.Transactions.Length} Buchungen · {preview.Data.Payslips.Length} Entgeltnachweise", 19, true));
            root.Children.Add(Text("Originaldatei: " + preview.Origin, 12));
            if (item.Duplicate) root.Children.Add(Text("Diese PDF ist bereits identisch in der Auswahl enthalten.", 12));
            foreach (var issue in preview.Issues) root.Children.Add(Text("Prüfen: " + issue));
            foreach (var s in preview.Data.Statements) { var r = Reports.Reconcile(preview.Data.Transactions, s); root.Children.Add(Text($"{s.Account}: {r.Count} Buchungen · Saldenabweichung {Presentation.Currency(r.Difference)}")); }
            foreach (var slip in preview.Data.Payslips) root.Children.Add(Text($"{Presentation.Month(slip.Period)} · Brutto {Presentation.Currency(slip.Gross.Cents)} · Auszahlung {Presentation.Currency(slip.Payout.Cents)}"));
            var list = TransactionList(preview.Data.Transactions); list.Height = 440; root.Children.Add(list);
        }
        return Scroll(root);
    }
    private sealed record PreviewChoice(int Index, string DisplayName, string OriginalName)
    {
        public override string ToString() => DisplayName + " · " + OriginalName;
    }
    private void AddTransferLinks(StackPanel root)
    {
        root.Children.Add(Text("Eigene Überträge · getrennte Buchungstage bleiben erhalten", 19, true));
        var linked = data.Transfers.IsDefault ? [] : data.Transfers.SelectMany(t => new[] { t.Outgoing, t.Incoming }).ToHashSet();
        var outgoing = data.Transactions.Where(t => t.Kind == MovementKind.Transfer && t.Amount.Cents < 0 && !linked.Contains(t.Id)).ToArray();
        foreach (var a in outgoing.Where(t => t.Date.Year == month.Year && t.Date.Month == month.Month))
        {
            var candidates = data.Transactions.Where(b => b.Kind == MovementKind.Transfer && b.Account != a.Account && b.Amount.Cents == -a.Amount.Cents && !linked.Contains(b.Id) && Math.Abs(b.Date.DayNumber - a.Date.DayNumber) <= 7).ToArray();
            var box = Stack(8); box.Children.Add(Text($"{a.Date:dd.MM.yyyy} · {a.Account} · {Presentation.Currency(a.Amount.Cents)} · {candidates.Length} Gegenbuchungen möglich"));
            foreach (var b in candidates) box.Children.Add(ActionButton($"Mit {b.Account} am {b.Date:dd.MM.yyyy} verknüpfen", () => Run(() => { service.LinkTransfer(a.Id, b.Id); data = service.Read(); }, "Übertrag verknüpft.", Refresh)));
            root.Children.Add(Card(box));
        }
    }
    private async Task<string?> SelectFile(string title, string pattern)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = title, AllowMultiple = false, FileTypeFilter = [new FilePickerFileType(title) { Patterns = [pattern] }] }); return files.FirstOrDefault()?.TryGetLocalPath();
    }
    private async Task ImportPdf()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Kontoauszüge und Entgeltnachweise", AllowMultiple = true, FileTypeFilter = [new FilePickerFileType("PDF-Belege") { Patterns = ["*.pdf"] }] });
        if (files.Count == 0) return;
        var paths = files.Select(file => file.TryGetLocalPath()).ToArray();
        if (paths.Any(path => path is null)) { status.Text = "Bitte lokale PDF-Dateien auswählen."; return; }
        await Run(() => { batch = service.PreviewStatements(paths.Select(path => path!), operation!.Token); batchResult = null; previewIndex = 0; }, "Vorschau bereit. Bitte die Dateien prüfen.", () => { screen = "Import & Prüfung"; Render(); }, cancellable: true);
    }
    private Control Reconcile()
    {
        var root = Stack(); root.Children.Add(Heading("Kontoabgleich", "Anfangssaldo + Buchungen = Endsaldo · exakt auf den Cent"));
        foreach (var s in data.Statements.Where(s => s.Month.Year == month.Year && s.Month.Month == month.Month))
        {
            var r = Reports.Reconcile(data.Transactions, s); var body = Stack(8); body.Children.Add(Text(s.Account + (r.Balanced ? " · Abgeglichen" : " · Abweichung"), 19, true));
            body.Children.Add(Text($"Anfang {Presentation.Currency(s.Opening.Cents)}   +   Bewegung {Presentation.Currency(r.ActualMovement)}   =   Ende {Presentation.Currency(s.Closing.Cents)}"));
            body.Children.Add(Text($"Differenz {Presentation.Currency(r.Difference)} · {r.Count} Buchungen · erwartet: {s.ExpectedCount?.ToString() ?? "keine unabhängige Anzahl"}", 12));
            body.Children.Add(ActionButton("Beleg öffnen", () => OpenSource(s.Source.Split(',')[0]))); root.Children.Add(Card(body));
        }
        AddTransferLinks(root); return Scroll(root);
    }
    private Control Budget()
    {
        var root = Stack(); root.Children.Add(Heading("Budget", "Planversionen ab dem gewählten Monat · leere Beträge bleiben offen"));
        var current = Reports.BudgetAt(data.Budget, month);
        foreach (var line in current.OrderBy(b => b.Income ? 0 : 1).ThenBy(b => b.Category))
        {
            var row = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 14 };
            row.Children.Add(Text($"{(line.Income ? "Einnahme" : "Ausgabe")} · {line.Category} · {(line.Monthly is null ? "Betrag offen" : Presentation.Currency(line.Monthly.Value.Cents))} · {line.Account}"));
            var edit = ActionButton("Ändern", () => EditBudget(line)); Grid.SetColumn(edit, 1); row.Children.Add(edit); root.Children.Add(Card(row));
        }
        root.Children.Add(ActionButton("Neue Planposition", () => EditBudget(new("", null, "", false))));
        root.Children.Add(Text("Planvergleich nach Ausgabenkategorie", 19, true));
        var actuals = Reports.Categories(MonthRows(), data.Allocations);
        foreach (var group in current.Where(b => !b.Income).GroupBy(b => b.ReportingCategory ?? b.Category))
        {
            var actual = actuals.Where(a => a.Category == group.Key).Sum(a => a.Cents);
            var plan = group.Any(b => b.Monthly is null) ? (long?)null : group.Sum(b => b.Monthly!.Value.Cents);
            root.Children.Add(Text($"{group.Key} · Ist {Presentation.Currency(actual)} · Plan {(plan is null ? "offen" : Presentation.Currency(plan.Value))} · Δ {(plan is null ? "offen" : Presentation.Currency(actual - plan.Value))}", 13));
        }
        root.Children.Add(Text("Nächste wiederkehrende Zahlungen", 19, true));
        foreach (var line in current.Where(b => b.RecurrenceMonths > 1 || b.NextPayment is not null).OrderBy(b => Reports.NextPayment(b, month)))
            root.Children.Add(Text($"{line.Category} · {(line.PaymentAmount is null ? "Zahlungsbetrag offen" : Presentation.Currency(line.PaymentAmount.Value.Cents))} · alle {line.RecurrenceMonths} Monate · {(Reports.NextPayment(line, month) is { } next ? next.ToString("dd.MM.yyyy") : "Termin offen")}", 13));
        return Scroll(root);
    }
    private async Task EditBudget(BudgetLine original)
    {
        var dialog = new Window { Title = "Planversion · " + Presentation.Month(month), Width = 540, Height = 660, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var body = Stack(12); body.Margin = new(24);
        var name = new TextBox { Text = original.Category, PlaceholderText = "Kategorie", IsReadOnly = original.Category.Length > 0 };
        var amount = new TextBox { Text = original.Monthly?.Euros.ToString("0.00", Presentation.German), PlaceholderText = "Monatsbetrag in EUR (leer = offen)" };
        var account = new TextBox { Text = original.Account, PlaceholderText = "Konto" };
        var reportingCategory = new TextBox { Text = original.ReportingCategory ?? original.Category, PlaceholderText = "Ausgabenkategorie für den Ist-Vergleich" };
        var payment = new TextBox { Text = original.PaymentAmount?.Euros.ToString("0.00", Presentation.German), PlaceholderText = "Zahlungsbetrag (optional, unabhängig vom Monatsplan)" };
        var recurrence = new NumericUpDown { Minimum = 1, Maximum = 120, Value = original.RecurrenceMonths };
        var nextPayment = new TextBox { Text = original.NextPayment?.ToString("dd.MM.yyyy"), PlaceholderText = "Nächste Zahlung (TT.MM.JJJJ, leer = offen)" };
        var income = new CheckBox { Content = "Einnahmenposition", IsChecked = original.Income, IsEnabled = original.Category.Length == 0 };
        var cancelled = new CheckBox { Content = "Ab diesem Monat beendet", IsChecked = original.Cancelled };
        var error = Text("Die neue Version gilt ab " + Presentation.Month(month) + ". Frühere Versionen bleiben erhalten.", 12);
        body.Children.Add(name); body.Children.Add(amount); body.Children.Add(account); body.Children.Add(reportingCategory);
        body.Children.Add(payment); body.Children.Add(Text("Zahlungsturnus in Monaten", 12)); body.Children.Add(recurrence); body.Children.Add(nextPayment);
        body.Children.Add(income); body.Children.Add(cancelled); body.Children.Add(error);
        body.Children.Add(ActionButton("Planversion speichern", () =>
        {
            try
            {
                var money = string.IsNullOrWhiteSpace(amount.Text) ? (Money?)null : Money.FromEuros(decimal.Parse(amount.Text, Presentation.German));
                var paid = string.IsNullOrWhiteSpace(payment.Text) ? (Money?)null : Money.FromEuros(decimal.Parse(payment.Text, Presentation.German));
                var next = string.IsNullOrWhiteSpace(nextPayment.Text) ? (DateOnly?)null : DateOnly.ParseExact(nextPayment.Text, "dd.MM.yyyy", Presentation.German);
                dialog.Close(new BudgetLine(name.Text ?? "", money, account.Text ?? "", income.IsChecked == true, month, cancelled.IsChecked == true, reportingCategory.Text, paid, (int)(recurrence.Value ?? 1), next));
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException) { error.Text = "Bitte einen gültigen Betrag mit höchstens zwei Nachkommastellen eingeben."; }
            return Task.CompletedTask;
        }));
        dialog.Content = body;
        var result = await dialog.ShowDialog<BudgetLine?>(this);
        if (result is not null) await Run(() => { service.SaveBudget(result); data = service.Read(); }, "Planversion gespeichert.", Refresh);
    }
    private async Task ExportCsv()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Buchungen exportieren", SuggestedFileName = $"buchungen-{month:yyyy-MM}.csv", DefaultExtension = "csv" });
        var path = file?.TryGetLocalPath(); if (path is null) return;
        var rows = MonthRows(); await Run(() => File.WriteAllText(path, CsvExport.Transactions(rows), new System.Text.UTF8Encoding(true)), "Buchungen des gewählten Monats exportiert.");
    }
    private async Task ShowCategory(string category)
    {
        var dialog = new Window { Title = category + " · " + Presentation.Month(month), Width = 1050, Height = 580, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        dialog.Content = TransactionList(MonthRows().Where(t => t.Category == category || (!data.Allocations.IsDefault && data.Allocations.Any(a => a.TransactionId == t.Id && a.Category == category))));
        await dialog.ShowDialog(this);
    }
    private async Task SplitTransaction(Transaction row)
    {
        var dialog = new Window { Title = "Buchung aufteilen", Width = 560, Height = 450, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var body = Stack(12); body.Margin = new(24);
        body.Children.Add(Text($"{row.Counterparty} · {Presentation.Currency(row.Amount.Cents)}", 18, true));
        body.Children.Add(Text("Pro Zeile: Kategorie; Betrag in EUR. Beträge positiv eingeben; Summe muss dem vollständigen Buchungsbetrag entsprechen.", 12));
        var allocations = data.Allocations.IsDefault ? [] : data.Allocations.Where(a => a.TransactionId == row.Id).ToArray();
        var input = new TextBox { AcceptsReturn = true, Height = 180, Text = string.Join('\n', allocations.Select(a => a.Category + "; " + Math.Abs(a.Amount.Euros).ToString("0.00", Presentation.German))) };
        var error = Text("Mindestens zwei Kategorien erforderlich.", 12); body.Children.Add(input); body.Children.Add(error);
        body.Children.Add(ActionButton("Aufteilung speichern", () =>
        {
            try
            {
                var values = (input.Text ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line =>
                {
                    var parts = line.Split(';'); if (parts.Length != 2) throw new FormatException();
                    var amount = decimal.Parse(parts[1].Trim(), Presentation.German); if (amount <= 0) throw new FormatException();
                    return new Allocation(row.Id, parts[0].Trim(), Money.FromEuros(amount * Math.Sign(row.Amount.Cents)));
                }).ToImmutableArray();
                if (values.Length < 2 || values.Sum(a => a.Amount.Cents) != row.Amount.Cents || values.Any(a => a.Category.Length == 0)) throw new FormatException();
                dialog.Close(values);
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException) { error.Text = "Kategorien und Beträge prüfen: Die Summe muss genau stimmen."; }
            return Task.CompletedTask;
        })); dialog.Content = body;
        var result = await dialog.ShowDialog<ImmutableArray<Allocation>>(this);
        if (!result.IsDefaultOrEmpty) await Run(() => { service.Split(row.Id, result); data = service.Read(); }, "Buchung aufgeteilt.", Refresh);
    }
    private Control Payroll()
    {
        var root = Stack(); root.Children.Add(Heading("Entgelt", "Brutto und Abzüge im Ledger · Netto-Bankeingänge im Budget"));
        foreach (var slip in data.Payslips.OrderBy(p => p.Period))
        {
            var body = Stack(9); body.Children.Add(Text(Presentation.Month(slip.Period), 20, true)); body.Children.Add(Text($"Brutto {Presentation.Currency(slip.Gross.Cents)} · gesetzliche Abzüge {Presentation.Currency(slip.Deductions.Cents)} · Netto {Presentation.Currency(slip.Net.Cents)}"));
            body.Children.Add(Text($"Korrektur {Presentation.Currency(slip.Correction.Cents)} · Kantine {Presentation.Currency(slip.Canteen.Cents)} · Auszahlung {Presentation.Currency(slip.Payout.Cents)}"));
            foreach (var l in slip.Lines) body.Children.Add(Text(l.Label + "  " + Presentation.Currency(l.Amount.Cents), 12)); body.Children.Add(ActionButton("Entgeltnachweis öffnen", () => OpenSource(slip.Source))); root.Children.Add(Card(body));
            if (!slip.Earnings.IsDefault) foreach (var l in slip.Earnings) body.Children.Add(Text("Brutto: " + l.Label + "  " + Presentation.Currency(l.Amount.Cents), 12));
            if (!slip.Adjustments.IsDefault) foreach (var a in slip.Adjustments) body.Children.Add(Text($"Rückrechnung für {Presentation.Month(a.EarningsPeriod)} · {Presentation.Currency(a.NetEffect.Cents)} · Seite {a.Page}", 12));
        }
        if (data.Payslips.IsEmpty) root.Children.Add(Text("Entgeltnachweise über Import & Prüfung hinzufügen. Der Bankeingang muss bereits erfasst sein.")); return Scroll(root);
    }
    private Control Rules()
    {
        var root = Stack(); root.Children.Add(Heading("Zuordnungsregeln", "Textmuster für künftige Importe · bestätigte manuelle Zuordnungen bleiben erhalten"));
        foreach (var rule in service.Rules().OrderByDescending(r => r.Priority)) root.Children.Add(Card(Text($"{rule.Contains} → {rule.Category} · {rule.Kind} · Konto {(rule.Account.Length == 0 ? "alle" : rule.Account)} · Priorität {rule.Priority} · ab {rule.EffectiveFrom:dd.MM.yyyy}")));
        var form = Stack(); var pattern = new TextBox { PlaceholderText = "Text in Zahlungsbeteiligtem oder Verwendungszweck" };
        var category = new TextBox { PlaceholderText = "Kategorie" }; var account = new TextBox { PlaceholderText = "Konto (leer = alle)" };
        var kind = new ComboBox { ItemsSource = Enum.GetValues<MovementKind>(), SelectedItem = MovementKind.Expense };
        var priority = new NumericUpDown { Minimum = 0, Maximum = 1000, Value = 10 };
        form.Children.Add(pattern); form.Children.Add(category); form.Children.Add(account); form.Children.Add(kind); form.Children.Add(Text("Priorität · höhere Zahl gewinnt", 12)); form.Children.Add(priority);
        form.Children.Add(Text("Gilt ab dem gewählten Monat. Gleichrangige widersprüchliche Regeln lassen die Zuordnung offen.", 12));
        form.Children.Add(ActionButton("Regel hinzufügen", () => Run(() => service.SaveRule(new(Guid.NewGuid(), pattern.Text ?? "", account.Text ?? "", (MovementKind)kind.SelectedItem!, category.Text ?? "", (int)(priority.Value ?? 0), month)), "Regel gespeichert.", Render)));
        root.Children.Add(Card(form));
        root.Children.Add(ActionButton("Regeln auf offene Buchungen anwenden", () => Run(() => { service.ApplyRules(); data = service.Read(); }, "Offene Buchungen geprüft; bestehende bestätigte Zuordnungen bleiben erhalten.", Refresh)));
        return Scroll(root);
    }
    private Control Backup()
    {
        var root = Stack(); root.Children.Add(Heading("Sicherung", "Datenbank und Belege zusammen sichern · manuell zwischen Geräten übertragen"));
        root.Children.Add(Text("Sicherungen sind unverschlüsselt. Eine Wiederherstellung ersetzt die aktuelle Historie; vorher wird automatisch eine Sicherung des bisherigen Standes erstellt."));
        root.Children.Add(ActionButton("Neue Sicherung erstellen", async () =>
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Sicherung speichern", SuggestedFileName = $"sponde-{DateTime.Now:yyyyMMdd-HHmmss}.spondebackup", DefaultExtension = "spondebackup", FileTypeChoices = [new FilePickerFileType("Sponde-Sicherung") { Patterns = ["*.spondebackup"] }] });
            var path = file?.TryGetLocalPath(); if (path is not null) await Run(() => service.Backup(path), "Sicherung erstellt und geprüft.");
        }));
        root.Children.Add(ActionButton("Sicherung zur Wiederherstellung auswählen", async () =>
        {
            var path = await SelectFile("Sponde-Sicherung", "*.spondebackup"); if (path is null) return;
            var confirm = new Window { Title = "Historie ersetzen?", Width = 500, Height = 240, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var body = Stack(); body.Margin = new(24); body.Children.Add(Text("Die Sicherung ersetzt den aktuellen Stand, auch wenn sie älter ist. Den bisherigen Stand sichert Sponde zuvor automatisch."));
            body.Children.Add(ActionButton("Wiederherstellung bestätigen", () => { confirm.Close(true); return Task.CompletedTask; })); body.Children.Add(ActionButton("Abbrechen", () => { confirm.Close(false); return Task.CompletedTask; })); confirm.Content = body;
            if (await confirm.ShowDialog<bool>(this)) await Run(() => { service.Restore(path); data = service.Read(); }, "Sicherung wiederhergestellt.", Refresh);
        })); return Scroll(root);
    }
    private async Task OpenSource(string name)
    {
        var path = service.SourcePath(name);
        if (path is null) { status.Text = "Beleg nicht im verwalteten Datenordner vorhanden."; return; }
        var file = await StorageProvider.TryGetFileFromPathAsync(path);
        if (file is null) { status.Text = "Beleg nicht gefunden."; return; }
        await Launcher.LaunchFileAsync(file);
    }
}

