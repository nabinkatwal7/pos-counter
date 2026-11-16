using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using CounterPoint.Core;

namespace CounterPoint;
public partial class MainWindow
{
    private TextBox couponBox = null!, pointsBox = null!, salesSearch = null!;
    private DatePicker salesFrom = null!, salesTo = null!, performanceFrom = null!, performanceTo = null!;
    private ComboBox salesPayment = null!, salesStatus = null!;
    private TextBlock loyaltyHint = null!, shiftHint = null!;
    private Guid? resumedCartId;
    private bool filteringSales;
    private void ResetSale()
    {
        fulfillingOrderId = Guid.Empty; fulfillingJobId = Guid.Empty;
        resumedCartId = null; cart.Clear(); couponBox.Clear(); pointsBox.Text = "0";
        Discount.Text = "0"; Tendered.Text = "0"; SaleCustomer.SelectedIndex = -1;
        TaxRate.Text = store.Data.Settings.DefaultTax.ToString(CultureInfo.CurrentCulture);
        if (saleLocation != null) LockFulfillment();
    }
    private void BuildRetail()
    {
        var catalog = (DockPanel)Search.Parent;
        var offers = new StackPanel { Margin = new Thickness(0, 4, 0, 4) }; DockPanel.SetDock(offers, Dock.Top);
        catalog.Children.Insert(1, offers);
        var input = new Grid(); input.ColumnDefinitions.Add(new()); input.ColumnDefinitions.Add(new()); offers.Children.Add(input);
        StackPanel Field(int column, string label)
        {
            var stack = new StackPanel(); System.Windows.Controls.Grid.SetColumn(stack, column); stack.Children.Add(new TextBlock { Text = label, Margin = new Thickness(4, 0, 4, 0) }); input.Children.Add(stack); return stack;
        }
        couponBox = new TextBox { ToolTip = "Optional coupon code" }; Field(0, "Coupon code").Children.Add(couponBox);
        pointsBox = new TextBox { Text = "0" }; Field(1, "Loyalty points to redeem").Children.Add(pointsBox);
        loyaltyHint = new TextBlock { Margin = new Thickness(4), Foreground = System.Windows.Media.Brushes.SlateGray, TextWrapping = TextWrapping.Wrap }; offers.Children.Add(loyaltyHint);
        couponBox.TextChanged += TotalsChanged; pointsBox.TextChanged += TotalsChanged;
        SaleCustomer.SelectionChanged += (_, _) => { RefreshLoyalty(); RefreshCart(); };
        var saleTools = new WrapPanel(); offers.Children.Add(saleTools);
        AddAction(saleTools, "Hold cart", () => SaveCurrentCart("Held"));
        AddAction(saleTools, "Save quotation", () => SaveCurrentCart("Quote"));
        AddAction(saleTools, "New sale", () => { if (cart.Count > 0 && MessageBox.Show(this, "Discard the current unsaved cart?", "New sale", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return; ResetSale(); Refresh(); });
        // Keep the cart usable even when the window is short.
        var cartPanel = (DockPanel)CartGrid.Parent;
        var checkoutPanel = cartPanel.Children.OfType<StackPanel>().Last();
        cartPanel.Children.Remove(checkoutPanel);
        var checkoutScroll = new ScrollViewer { Content = checkoutPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 400 };
        DockPanel.SetDock(checkoutScroll, Dock.Bottom); cartPanel.Children.Insert(1, checkoutScroll);
        CartGrid.MinHeight = 90;
        var complete = checkoutPanel.Children.OfType<Button>().Last(); complete.Style = (Style)FindResource("PrimaryButton");

        var salesPage = (DockPanel)SalesGrid.Parent;
        var filters = new WrapPanel { Margin = new Thickness(0, 4, 0, 8) }; DockPanel.SetDock(filters, Dock.Top); salesPage.Children.Insert(0, filters);
        salesSearch = new TextBox { Width = 220, ToolTip = "Receipt, customer or product", Margin = new Thickness(4) }; filters.Children.Add(salesSearch);
        salesFrom = new DatePicker { Width = 130, ToolTip = "From date (optional)", Margin = new Thickness(4) }; salesTo = new DatePicker { Width = 130, ToolTip = "Through date (optional)", Margin = new Thickness(4) };
        filters.Children.Add(salesFrom); filters.Children.Add(salesTo);
        salesPayment = new ComboBox { ItemsSource = new[] { "All", "Cash", "Card" }, SelectedIndex = 0, Width = 100, Margin = new Thickness(4) }; filters.Children.Add(salesPayment);
        salesStatus = new ComboBox { ItemsSource = new[] { "All", "Active", "Returned" }, SelectedIndex = 0, Width = 110, Margin = new Thickness(4) }; filters.Children.Add(salesStatus);
        AddAction(filters, "Search sales", ApplySalesFilter);
        AddAction(filters, "Reset filters", () => { salesSearch.Clear(); salesFrom.SelectedDate = null; salesTo.SelectedDate = null; salesPayment.SelectedIndex = 0; salesStatus.SelectedIndex = 0; ApplySalesFilter(); });

        var tabs = new TabControl { TabStripPlacement = Dock.Left };
        MainTabs.Items.Add(new TabItem { Header = "Retail tools", Content = tabs });
        (DockPanel Page, WrapPanel Tools) Page(string title, string description)
        {
            var panel = new DockPanel { Margin = new Thickness(16) };
            var descriptionBlock = new TextBlock { Text = description, Margin = new Thickness(4, 0, 4, 16), TextWrapping = TextWrapping.Wrap };
            DockPanel.SetDock(descriptionBlock, Dock.Top); panel.Children.Add(descriptionBlock);
            var tools = new WrapPanel(); DockPanel.SetDock(tools, Dock.Top); panel.Children.Add(tools);
            tabs.Items.Add(new TabItem { Header = title, Content = panel }); return (panel, tools);
        }
        void Grid(DockPanel panel, string key)
        {
            var grid = new DataGrid { AutoGenerateColumns = true, Margin = new Thickness(0, 12, 0, 0) };
            grid.AutoGeneratingColumn += (_, e) =>
            {
                if (e.PropertyName.EndsWith("Id") || e.PropertyName == "Lines") e.Cancel = true;
                if (e.Column is DataGridTextColumn column)
                {
                    if (e.PropertyType == typeof(decimal) || e.PropertyType == typeof(decimal?)) column.Binding.StringFormat = "N2";
                    if (e.PropertyType == typeof(DateTime) || e.PropertyType == typeof(DateTimeOffset) || e.PropertyType == typeof(DateTimeOffset?)) column.Binding.StringFormat = "yyyy-MM-dd HH:mm";
                }
            }; operationGrids[key] = grid; panel.Children.Add(grid);
        }
        foreach (var (key, kind) in new[] { ("Held carts", "Held"), ("Quotations", "Quote") })
        {
            var page = Page(key, kind == "Held" ? "Park a customer's cart while serving another customer. Saved carts do not reserve inventory." : "Create a dated quotation from Sell. Loading uses current prices and availability; the saved quotation remains until checkout succeeds.");
            AddAction(page.Tools, "Load into Sell", () => LoadSavedCart(Selected<SavedCart>(key)));
            if (kind == "Quote") AddAction(page.Tools, "View / print quotation", () =>
            {
                var quote = Selected<SavedCart>(key);
                ShowDocument("Quotation · " + quote.Name, $"{store.Data.Settings.Name}\nQUOTATION — NOT A RECEIPT\n{quote.Name}\nValid through {quote.ExpiresOn:yyyy-MM-dd}\n" + string.Join("\n", quote.Lines.Select(l => $"{l.Name} · {l.Quantity} × {l.Price:N2} = {l.Total:N2}")) + $"\nDiscount: {quote.Discount:N2}\nTax: {quote.TaxRate}%\nEstimated total: {quote.Total:N2}\nPrices rechecked at checkout. Stock not reserved.");
            });
            AddAction(page.Tools, "Remove saved entry", () => { var saved = Selected<SavedCart>(key); if (MessageBox.Show(this, $"Remove {saved.Name}?", "Saved cart", MessageBoxButton.YesNo) == MessageBoxResult.Yes) { store.DeleteSavedCart(saved.Id); if (resumedCartId == saved.Id) resumedCartId = null; Refresh(); } });
            Grid(page.Page, key);
        }
        var coupons = Page("Coupons", "Create fixed or percentage discounts. Expiry is inclusive; usage limits include returned sales. A coupon applies after the manual discount and before loyalty points.");
        void EditCoupon(Coupon? c) => Form("Coupon", [("Code", c?.Code ?? ""), ("Type: Percent or Fixed", c?.Percentage == true ? "Percent" : "Fixed"), ("Value", (c?.Value ?? 10).ToString()), ("Minimum subtotal", (c?.Minimum ?? 0).ToString()), ("Expiry (yyyy-MM-dd)", (c?.ExpiresOn ?? DateTime.Today.AddMonths(1)).ToString("yyyy-MM-dd")), ("Maximum uses", (c?.MaxUses ?? 100).ToString())], v =>
        {
            if (v[1] is not ("Percent" or "Fixed")) throw new InvalidOperationException("Type must be Percent or Fixed.");
            store.SaveCoupon(new(v[0], v[1] == "Percent", Number(v[2]), Number(v[3]), Date(v[4]), Integer(v[5]), c?.Enabled ?? true));
        });
        AddAction(coupons.Tools, "+ Coupon", () => EditCoupon(null)); AddAction(coupons.Tools, "Edit", () => EditCoupon(Selected<Coupon>("Coupons")));
        AddAction(coupons.Tools, "Enable / disable", () => { var c = Selected<Coupon>("Coupons"); store.SaveCoupon(c with { Enabled = !c.Enabled }); Refresh(); }); Grid(coupons.Page, "Coupons");
        var loyalty = Page("Loyalty", "Customers earn one point per full 100 of the paid total. One point discounts one currency unit before tax. Full returns reverse earned/redeemed points; balances can become negative if earned points were already spent.");
        AddAction(loyalty.Tools, "Refresh balances", RefreshRetail); Grid(loyalty.Page, "Loyalty");
        var shifts = Page("Cash shifts", "Open a shift with a float, record drawer cash in/out, then reconcile the counted cash. Cash sales and refunds are linked to the open shift. Expenses and purchases require a separate cash-out entry if paid from this drawer.");
        shiftHint = new TextBlock { Margin = new Thickness(4, 12, 4, 8), TextWrapping = TextWrapping.Wrap }; DockPanel.SetDock(shiftHint, Dock.Top); shifts.Page.Children.Add(shiftHint);
        AddAction(shifts.Tools, "Open shift", () => Form("Open shift", [("Operator", ""), ("Opening cash", "0")], v => store.OpenShift(v[0], Number(v[1]))));
        AddAction(shifts.Tools, "Cash in / out", () => Form("Drawer movement", [("Amount (+ in / − out)", "0"), ("Reason", "")], v => store.MoveCash(Number(v[0]), v[1])));
        AddAction(shifts.Tools, "Close & reconcile", () => Form("Close shift", [("Counted cash", "0")], v => store.CloseShift(Number(v[0]))));
        AddAction(shifts.Tools, "Cash movement ledger", () => ShowTable("Drawer movements", store.Data.CashEntries)); Grid(shifts.Page, "Shifts");
        var files = Page("Data & backup", "Export a CSV template, edit it, then import products in one transaction. Matching barcodes update products and stock; omissions are preserved. Backups contain all shop data and a checksum. Restore creates a recovery backup first.");
        AddAction(files.Tools, "Export products CSV", () => SaveText("Products CSV|*.csv", "products.csv", store.ExportProducts()));
        AddAction(files.Tools, "Import products CSV", () =>
        {
            RequireEmptyCart(); var file = OpenFile("Products CSV|*.csv"); if (file == null) return;
            if (MessageBox.Show(this, "Import will update matching products and their stock counts. Continue?", "Import inventory", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            var count = store.ImportProducts(System.IO.File.ReadAllText(file)); Refresh(); Status.Text = $"Imported {count} products.";
        });
        AddAction(files.Tools, "Create backup", () => SaveText("CounterPoint backup|*.cpbackup", "CounterPoint-" + DateTime.Today.ToString("yyyyMMdd") + ".cpbackup", store.ExportBackup()));
        AddAction(files.Tools, "Restore backup", () =>
        {
            RequireEmptyCart(); var file = OpenFile("CounterPoint backup|*.cpbackup;*.json"); if (file == null) return;
            if (MessageBox.Show(this, "Replace current shop data with this backup? A recovery copy will be saved beside the store file. Close any open shift first.", "Restore shop", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            store.RestoreBackup(System.IO.File.ReadAllText(file)); ResetSale(); Refresh(); UpdateReport(); Status.Text = "Backup restored. A recovery copy of the previous data was saved.";
        });
        var performance = Page("Product analytics", "Rank products by net sales before tax. Discounts are allocated proportionally to items. Returns are deducted on their return date; figures are revenue, not profit.");
        performanceFrom = new DatePicker { SelectedDate = DateTime.Today.AddDays(-29), Width = 140, Margin = new Thickness(4) }; performanceTo = new DatePicker { SelectedDate = DateTime.Today, Width = 140, Margin = new Thickness(4) };
        performance.Tools.Children.Add(performanceFrom); performance.Tools.Children.Add(performanceTo);
        AddAction(performance.Tools, "Run analysis", UpdatePerformance); Grid(performance.Page, "Performance");
        var archive = Page("Product archive", "Archive products to remove them from the sell catalog and low-stock alerts. Receipts, stock and reports remain available. Restore a product to sell it again.");
        AddAction(archive.Tools, "Archive / restore selected", () =>
        {
            var p = Selected<Product>("Archive");
            if (cart.Any(l => l.ProductId == p.Id)) throw new InvalidOperationException("Remove this product from the current cart first.");
            store.ArchiveProduct(p.Id, !p.Archived); Refresh();
        }); Grid(archive.Page, "Archive");
        UpdatePerformance();
    }
    private void AddAction(Panel panel, string title, Action action)
    {
        var button = new Button { Content = title }; button.Click += (_, _) => Safe(action); panel.Children.Add(button);
    }
    private static DateTime Date(string text) => DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : throw new InvalidOperationException("Use yyyy-MM-dd for dates.");
    private void RequireEmptyCart() { if (cart.Count != 0) throw new InvalidOperationException("Hold or clear the current cart first."); }
    private void SaveCurrentCart(string kind)
    {
        if (resumedCartId != null) throw new InvalidOperationException("This cart is already saved. Complete it or start a new sale.");
        if (couponBox.Text.Length > 0 || Integer(pointsBox.Text) != 0) throw new InvalidOperationException("Remove the coupon and loyalty redemption before saving; apply them when completing the sale.");
        Form(kind == "Held" ? "Hold cart" : "Save quotation", [("Customer / order name", (SaleCustomer.SelectedItem as Customer)?.Name ?? ""), ("Valid through (yyyy-MM-dd; quotations only)", DateTime.Today.AddDays(7).ToString("yyyy-MM-dd"))], v =>
        { store.SaveCart(v[0], kind, cart, Money(Discount), Money(TaxRate), SaleCustomer.SelectedValue as Guid?, Date(v[1])); ResetSale(); });
    }
    private void LoadSavedCart(SavedCart saved)
    {
        RequireEmptyCart(); var lines = store.ResumeCart(saved.Id);
        ResetSale(); cart.AddRange(lines); resumedCartId = saved.Id; Discount.Text = saved.Discount.ToString(); TaxRate.Text = saved.TaxRate.ToString(); SaleCustomer.SelectedValue = saved.CustomerId;
        RefreshCart(); MainTabs.SelectedIndex = 0; Status.Text = "Loaded saved order; prices and availability were rechecked.";
    }
    private void RefreshLoyalty()
    {
        if (loyaltyHint == null) return;
        loyaltyHint.Text = SaleCustomer.SelectedValue is Guid id ? $"Available loyalty balance: {store.LoyaltyBalance(id)} points" : "Select a customer to earn or redeem loyalty points.";
    }
    private void RefreshRetail()
    {
        if (!operationGrids.ContainsKey("Archive")) return;
        operationGrids["Held carts"].ItemsSource = store.Data.SavedCarts.Where(c => c.Kind == "Held").ToList();
        operationGrids["Quotations"].ItemsSource = store.Data.SavedCarts.Where(c => c.Kind == "Quote").ToList();
        operationGrids["Coupons"].ItemsSource = store.Data.Coupons.ToList();
        operationGrids["Loyalty"].ItemsSource = store.Data.Customers.Select(c => new { c.Name, c.Phone, Points = store.LoyaltyBalance(c.Id) }).ToList();
        operationGrids["Shifts"].ItemsSource = store.Data.Shifts.OrderByDescending(s => s.Opened).ToList();
        operationGrids["Archive"].ItemsSource = store.Data.Products.OrderBy(p => p.Name).ToList();
        var active = store.Data.Shifts.SingleOrDefault(s => s.Closed == null);
        shiftHint.Text = active == null ? "No shift is open. Sales made without an open shift remain outside drawer reconciliation." : $"Open: {active.Operator} · Expected cash: {store.ExpectedCash(active.Id):N2}";
        RefreshLoyalty();
        if (businessReady) { RefreshSupplyChain(); RefreshCustomerBusiness(); RefreshTeamServices(); }
    }
    private void ApplySalesFilter()
    {
        if (store == null || filteringSales) return;
        filteringSales = true;
        try { SalesGrid.ItemsSource = store.FindSales(salesSearch?.Text ?? "", salesFrom?.SelectedDate, salesTo?.SelectedDate, salesPayment?.SelectedItem as string ?? "All", salesStatus?.SelectedItem as string ?? "All"); }
        catch (Exception ex) { Status.Text = ex.Message; }
        finally { filteringSales = false; }
    }
    private void UpdatePerformance() => operationGrids["Performance"].ItemsSource = store.Performance(performanceFrom.SelectedDate ?? throw new InvalidOperationException("Choose a start date."), performanceTo.SelectedDate ?? throw new InvalidOperationException("Choose an end date."));
    private string? OpenFile(string filter)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = filter }; return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }
    private void SaveText(string filter, string name, string text)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = filter, FileName = name };
        if (dialog.ShowDialog(this) == true) { System.IO.File.WriteAllText(dialog.FileName, text); Status.Text = "File saved."; }
    }
}
