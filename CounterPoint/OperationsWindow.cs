using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using CounterPoint.Core;

namespace CounterPoint;
public partial class MainWindow
{
    private readonly Dictionary<string, DataGrid> operationGrids = [];
    private DatePicker reportFrom = null!, reportTo = null!;
    private TextBlock reportSummary = null!;
    private void WalkIn(object sender, RoutedEventArgs e) => SaleCustomer.SelectedIndex = -1;
    private void Safe(Action action)
    {
        try { action(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Check details", MessageBoxButton.OK, MessageBoxImage.Warning); Refresh(); }
    }
    private void Form(string title, (string Label, string Value)[] fields, Action<string[]> save)
    {
        var window = new Window { Owner = this, Title = title, Width = 490, Height = Math.Min(700, 180 + fields.Length * 78), WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Background };
        var panel = new StackPanel { Margin = new Thickness(24) };
        window.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var boxes = fields.Select(field => { panel.Children.Add(new TextBlock { Text = field.Label, Margin = new Thickness(4, 8, 0, 0) }); var box = new TextBox { Text = field.Value }; panel.Children.Add(box); return box; }).ToArray();
        var button = new Button { Content = "Save", Margin = new Thickness(4, 20, 4, 4) }; panel.Children.Add(button);
        button.Click += (_, _) =>
        {
            try { save(boxes.Select(b => b.Text.Trim()).ToArray()); Refresh(); window.Close(); Status.Text = title + " saved."; }
            catch (Exception ex) { MessageBox.Show(window, ex.Message, "Check details"); Refresh(); }
        };
        window.ShowDialog();
    }
    private static decimal Number(string value) => decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out var result) ? result : throw new InvalidOperationException("Enter a valid amount.");
    private static int Integer(string value) => int.TryParse(value, out var result) ? result : throw new InvalidOperationException("Enter a whole number quantity.");
    private T Selected<T>(string key) => operationGrids[key].SelectedItem is T value ? value : throw new InvalidOperationException("Select a row first.");
    private void BuildOperations()
    {
        var tabs = new TabControl { TabStripPlacement = Dock.Left };
        MainTabs.Items.Add(new TabItem { Header = "Shop operations", Content = tabs });
        DockPanel Page(string key, string description)
        {
            var panel = new DockPanel { Margin = new Thickness(8, 16, 8, 8) };
            var text = new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 0, 4, 12) };
            DockPanel.SetDock(text, Dock.Top); panel.Children.Add(text);
            tabs.Items.Add(new TabItem { Header = key, Content = panel }); return panel;
        }
        StackPanel Toolbar(DockPanel page)
        {
            var tools = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(tools, Dock.Top); page.Children.Add(tools); return tools;
        }
        void Button(Panel panel, string label, Action action)
        {
            var button = new Button { Content = label }; button.Click += (_, _) => Safe(action); panel.Children.Add(button);
        }
        void Grid(DockPanel page, string key)
        {
            var grid = new DataGrid { AutoGenerateColumns = true, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
            grid.AutoGeneratingColumn += (_, e) =>
            {
                if (e.PropertyName.EndsWith("Id") || (key == "ReturnSales" && e.PropertyName is not ("Number" or "Time" or "CustomerName" or "Total" or "Payment"))) e.Cancel = true;
                if (e.PropertyType == typeof(decimal) && e.Column is DataGridTextColumn column) column.Binding.StringFormat = "N2";
            };
            operationGrids[key] = grid; page.Children.Add(grid);
        }
        var customers = Page("Customers", "Customer directory · select a customer for their purchase history. Assign a customer to a sale on the Sell tab.");
        var ct = Toolbar(customers);
        void EditCustomer(Customer? c) => Form("Customer", [("Name", c?.Name ?? ""), ("Phone", c?.Phone ?? ""), ("Email", c?.Email ?? "")], v => store.SaveCustomer(new(c?.Id ?? Guid.NewGuid(), v[0], v[1], v[2])));
        Button(ct, "+ Customer", () => EditCustomer(null)); Button(ct, "Edit", () => EditCustomer(Selected<Customer>("Customers")));
        Button(ct, "Purchase history", () =>
        {
            var c = Selected<Customer>("Customers");
            var sales = store.Data.Sales.Where(s => s.CustomerId == c.Id).OrderByDescending(s => s.Time).ToList();
            var total = sales.Sum(s => s.Total) - store.Data.Returns.Where(r => sales.Any(s => s.Number == r.SaleNumber)).Sum(r => r.Amount);
            ShowTable($"{c.Name} · {sales.Count} sales · Net {total:N2}", sales.Select(s => new { s.Number, s.Time, s.Total, Returned = store.Data.Returns.Any(r => r.SaleNumber == s.Number) }));
        }); Grid(customers, "Customers");

        var returns = Page("Returns", "Full-sale returns restore every item to stock. Refunds are recorded against the original payment method; issue cash or terminal refunds separately.");
        var rt = Toolbar(returns);
        Button(rt, "Return selected sale", () =>
        {
            var sale = Selected<Sale>("ReturnSales");
            Form($"Refund {sale.Number} · {sale.Total:N2}", [("Reason (all items will be restocked)", "")], v =>
            {
                if (cart.Any(l => sale.Lines.Any(s => s.ProductId == l.ProductId))) throw new InvalidOperationException("Clear affected products from the cart first.");
                store.ReturnSale(sale.Number, v[0]);
            });
        }); Button(rt, "Return ledger", () => ShowTable("Returns", store.Data.Returns)); Grid(returns, "ReturnSales");

        var stock = Page("Stock audit", "Select a product, enter a positive or negative adjustment and a reason. Each adjustment is recorded in the stock ledger.");
        var st = Toolbar(stock);
        Button(st, "Adjust stock", () => { var p = Selected<Product>("Stock"); Form("Adjust " + p.Name, [("Quantity change (+ received / − damaged)", "0"), ("Reason", "")], v => store.AdjustStock(p.Id, Integer(v[0]), v[1])); });
        Button(st, "Movement ledger", () => ShowTable("Stock movements", store.Data.Movements.OrderByDescending(m => m.Time))); Grid(stock, "Stock");

        var low = Page("Low stock", "Products at or below their reorder level. Set the threshold for any product from Stock audit using the button below.");
        var lt = Toolbar(low);
        Button(lt, "Set selected alert level", () => { var p = Selected<Product>("Low"); EditLevel(p); });
        Button(st, "Set reorder level", () => EditLevel(Selected<Product>("Stock"))); Grid(low, "Low");
        void EditLevel(Product p) => Form("Reorder level · " + p.Name, [("Alert at or below this stock", p.ReorderLevel.ToString())], v => store.SetReorderLevel(p.Id, Integer(v[0])));

        var suppliers = Page("Suppliers", "Supplier contacts used when receiving purchases."); var su = Toolbar(suppliers);
        void EditSupplier(Supplier? s) => Form("Supplier", [("Name", s?.Name ?? ""), ("Phone", s?.Phone ?? ""), ("Address", s?.Address ?? "")], v => store.SaveSupplier(new(s?.Id ?? Guid.NewGuid(), v[0], v[1], v[2])));
        Button(su, "+ Supplier", () => EditSupplier(null)); Button(su, "Edit", () => EditSupplier(Selected<Supplier>("Suppliers"))); Grid(suppliers, "Suppliers");

        var purchase = Page("Purchases", "Receive supplier deliveries one product per entry. Use the same supplier reference for different products; duplicate product/reference entries are blocked.");
        var pt = Toolbar(purchase); Button(pt, "Receive delivery", ReceiveDelivery); Grid(purchase, "Purchases");
        var expenses = Page("Expenses", "Record shop operating expenses separately from stock purchases."); var et = Toolbar(expenses);
        Button(et, "+ Expense", () => Form("Record expense", [("Category", "Utilities"), ("Description", ""), ("Amount", "0")], v => store.AddExpense(v[0], v[1], Number(v[2])))); Grid(expenses, "Expenses");

        var reports = Page("Reports", "Daily totals by transaction date. Net = sales less refunds, including tax. Expenses and purchases are separate outflows; these figures are not a profit calculation.");
        var tools = Toolbar(reports); reportFrom = new DatePicker { SelectedDate = DateTime.Today.AddDays(-6), Width = 130, Margin = new Thickness(4) }; reportTo = new DatePicker { SelectedDate = DateTime.Today, Width = 130, Margin = new Thickness(4) };
        tools.Children.Add(reportFrom); tools.Children.Add(reportTo); Button(tools, "Run report", UpdateReport); Button(tools, "Export CSV", ExportReport);
        reportSummary = new TextBlock { Margin = new Thickness(4, 12, 4, 12), TextWrapping = TextWrapping.Wrap }; DockPanel.SetDock(reportSummary, Dock.Top); reports.Children.Add(reportSummary); Grid(reports, "Reports");
        var settings = Page("Settings", "Shop identity and receipt text are captured on each new sale. Existing receipts retain their original details. Default tax applies to the next sale after saving.");
        var settingsTools = Toolbar(settings); Button(settingsTools, "Edit shop settings", () =>
        {
            var s = store.Data.Settings;
            Form("Shop settings", [("Shop name", s.Name), ("Address / contact", s.Address), ("Currency label", s.Currency), ("Default tax %", s.DefaultTax.ToString(CultureInfo.CurrentCulture)), ("Receipt footer", s.Footer)], v =>
            { store.SaveSettings(new() { Name = v[0], Address = v[1], Currency = v[2], DefaultTax = Number(v[3]), Footer = v[4] }); if (cart.Count == 0) TaxRate.Text = v[3]; });
        }); Grid(settings, "Settings");
        UpdateReport();
    }
    private void RefreshOperations()
    {
        if (operationGrids.Count == 0) return;
        operationGrids["Customers"].ItemsSource = store.Data.Customers.OrderBy(c => c.Name).ToList();
        operationGrids["Suppliers"].ItemsSource = store.Data.Suppliers.OrderBy(c => c.Name).ToList();
        operationGrids["ReturnSales"].ItemsSource = store.Data.Sales.Where(s => !store.Data.Returns.Any(r => r.SaleNumber == s.Number)).OrderByDescending(s => s.Time).ToList();
        operationGrids["Stock"].ItemsSource = store.Data.Products.OrderBy(p => p.Name).ToList();
        operationGrids["Low"].ItemsSource = store.LowStock();
        operationGrids["Purchases"].ItemsSource = store.Data.Purchases.OrderByDescending(p => p.Time).ToList();
        operationGrids["Expenses"].ItemsSource = store.Data.Expenses.OrderByDescending(p => p.Time).ToList();
        operationGrids["Settings"].ItemsSource = new[] { store.Data.Settings };
        Status.Text = $"{store.LowStock().Count} products need restocking.";
        RefreshRetail();
    }
    private void ShowTable<T>(string title, IEnumerable<T> rows)
    {
        var window = new Window { Owner = this, Title = title, Width = 950, Height = 560, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        window.Content = new DataGrid { AutoGenerateColumns = true, ItemsSource = rows.ToList(), Margin = new Thickness(16) }; window.ShowDialog();
    }
    private void ReceiveDelivery()
    {
        if (store.Data.Suppliers.Count == 0 || store.Data.Products.Count == 0) throw new InvalidOperationException("Create a supplier and product first.");
        var window = new Window { Owner = this, Title = "Receive delivery", Width = 460, Height = 580, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(24) }; window.Content = panel;
        ComboBox Select(string label, System.Collections.IEnumerable source)
        {
            panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(4, 8, 0, 0) });
            var combo = new ComboBox { ItemsSource = source, DisplayMemberPath = "Name", Padding = new Thickness(8), Margin = new Thickness(4), SelectedIndex = 0 }; panel.Children.Add(combo); return combo;
        }
        var supplier = Select("Supplier", store.Data.Suppliers); var product = Select("Product", store.Data.Products);
        TextBox Field(string label, string value) { panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(4, 8, 0, 0) }); var box = new TextBox { Text = value }; panel.Children.Add(box); return box; }
        var reference = Field("Supplier invoice / delivery reference", ""); var qty = Field("Quantity received", "1"); var cost = Field("Unit purchase cost", "0");
        var save = new Button { Content = "Receive and update stock" }; panel.Children.Add(save);
        save.Click += (_, _) =>
        {
            try { store.ReceivePurchase(((Supplier)supplier.SelectedItem).Id, ((Product)product.SelectedItem).Id, Integer(qty.Text), Money(cost), reference.Text.Trim()); Refresh(); window.Close(); }
            catch (Exception ex) { MessageBox.Show(window, ex.Message, "Check delivery"); Refresh(); }
        }; window.ShowDialog();
    }
    private IReadOnlyList<DailyReport> ReportRows() => store.Report(reportFrom.SelectedDate ?? throw new InvalidOperationException("Select a start date."), reportTo.SelectedDate ?? throw new InvalidOperationException("Select an end date."));
    private void UpdateReport()
    {
        var rows = ReportRows(); operationGrids["Reports"].ItemsSource = rows;
        reportSummary.Text = $"Sales {rows.Sum(r => r.Gross):N2} · Refunds {rows.Sum(r => r.Refunds):N2} · Net {rows.Sum(r => r.Net):N2} · Expenses {rows.Sum(r => r.Expenses):N2} · Purchases {rows.Sum(r => r.Purchases):N2}";
    }
    private void ExportReport()
    {
        var rows = ReportRows(); var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "CSV files|*.csv", FileName = "daily-report.csv" };
        if (dialog.ShowDialog(this) != true) return;
        var lines = new[] { "Date,Sales,Gross,Refunds,Net,Expenses,Purchases,CashSales,CardSales" }.Concat(rows.Select(r => FormattableString.Invariant($"{r.Date:yyyy-MM-dd},{r.Sales},{r.Gross},{r.Refunds},{r.Net},{r.Expenses},{r.Purchases},{r.CashSales},{r.CardSales}")));
        System.IO.File.WriteAllLines(dialog.FileName, lines); Status.Text = "Report exported.";
    }
}
