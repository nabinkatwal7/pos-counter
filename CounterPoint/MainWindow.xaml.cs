using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using CounterPoint.Core;

namespace CounterPoint;
public partial class MainWindow : Window
{
    private Store store = null!;
    private readonly List<SaleLine> cart = [];
    public MainWindow() : this(null) { }
    public MainWindow(string? dataFile)
    {
        InitializeComponent();
        try
        {
            store = new Store(dataFile ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CounterPoint", "store.json"));
            BuildOperations();
            BuildRetail();
            TaxRate.Text = store.Data.Settings.DefaultTax.ToString(CultureInfo.CurrentCulture);
            Refresh();
        }
        catch (Exception ex) { if (dataFile != null) throw; MessageBox.Show("Could not load shop data. Your file has been preserved.\n" + ex.Message, "CounterPoint"); Close(); }
    }
    private void Refresh()
    {
        var query = Search.Text.Trim();
        ProductsGrid.ItemsSource = store.Data.Products.Where(p => !p.Archived && $"{p.Name} {p.Category} {p.Barcode}".Contains(query, StringComparison.OrdinalIgnoreCase)).OrderBy(p => p.Name).ToList();
        InventoryGrid.ItemsSource = store.Data.Products.OrderBy(p => p.Name).ToList();
        ApplySalesFilter();
        var today = store.Data.Sales.Where(s => s.Time.LocalDateTime.Date == DateTime.Today).ToList();
        TodaySummary.Text = $"Today · {today.Count} sales · {today.Sum(s => s.Total):N2}";
        var selectedCustomer = SaleCustomer.SelectedValue;
        SaleCustomer.ItemsSource = store.Data.Customers.OrderBy(c => c.Name).ToList();
        SaleCustomer.SelectedValue = selectedCustomer;
        RefreshOperations();
        RefreshCart();
    }
    private void RefreshCart()
    {
        CartGrid.ItemsSource = cart.ToList();
        TotalsChanged(this, new TextChangedEventArgs(TextBox.TextChangedEvent, UndoAction.None));
    }
    private void SearchChanged(object sender, TextChangedEventArgs e) { if (store != null) Refresh(); }
    private void SearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        var product = store.Data.Products.FirstOrDefault(p => !p.Archived && p.Barcode.Equals(Search.Text.Trim(), StringComparison.OrdinalIgnoreCase));
        if (product != null) { Add(product); Search.Clear(); }
        else Status.Text = "No matching barcode. Select a product from the catalog.";
    }
    private void AddSelected(object sender, RoutedEventArgs e) { if (ProductsGrid.SelectedItem is Product product) Add(product); }
    private void Add(Product product)
    {
        if (product.Archived) { Status.Text = "This product is archived."; return; }
        var line = cart.FirstOrDefault(l => l.ProductId == product.Id);
        if ((line?.Quantity ?? 0) >= product.Stock) { Status.Text = "No more stock available for this product."; return; }
        if (line != null) cart.Remove(line);
        cart.Add(new SaleLine(product.Id, product.Name, product.Price, (line?.Quantity ?? 0) + 1));
        RefreshCart(); Status.Text = $"Added {product.Name}.";
    }
    private void Increase(object sender, RoutedEventArgs e) { if (CartGrid.SelectedItem is SaleLine line) Add(store.Data.Products.Single(p => p.Id == line.ProductId)); }
    private void Decrease(object sender, RoutedEventArgs e)
    {
        if (CartGrid.SelectedItem is not SaleLine line) return;
        cart.Remove(line); if (line.Quantity > 1) cart.Add(line with { Quantity = line.Quantity - 1 }); RefreshCart();
    }
    private void Remove(object sender, RoutedEventArgs e) { if (CartGrid.SelectedItem is SaleLine line) { cart.Remove(line); RefreshCart(); } }
    private void Clear(object sender, RoutedEventArgs e) { ResetSale(); RefreshCart(); }
    private static decimal Money(TextBox box) => decimal.TryParse(box.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var value) ? value : throw new InvalidOperationException("Enter valid numbers for amounts and tax.");
    private void TotalsChanged(object sender, TextChangedEventArgs e)
    {
        if (TotalLabel == null || TaxRate == null || Discount == null) return;
        try
        {
            var subtotal = cart.Sum(l => l.Total); var discount = store == null ? Money(Discount) : store.CalculateDiscount(subtotal, Money(Discount), couponBox?.Text ?? "", pointsBox == null ? 0 : Integer(pointsBox.Text), SaleCustomer.SelectedValue as Guid?); var rate = Money(TaxRate);
            if (discount < 0 || discount > subtotal || rate < 0 || rate > 100) throw new InvalidOperationException();
            var tax = decimal.Round((subtotal - discount) * rate / 100, 2, MidpointRounding.AwayFromZero);
            Breakdown.Text = $"Subtotal {subtotal:N2}   ·   Tax {tax:N2}";
            TotalLabel.Text = $"Total   {subtotal - discount + tax:N2}";
        }
        catch (Exception ex) { TotalLabel.Text = "Check amounts"; Breakdown.Text = ex is InvalidOperationException && ex.Message.Length < 150 ? ex.Message : "Check discount, coupon, points and tax (0–100%)."; }
    }
    private void Checkout(object sender, RoutedEventArgs e)
    {
        try
        {
            var method = ((ComboBoxItem)Payment.SelectedItem).Content.ToString()!;
            var sale = store.Checkout(cart, Money(Discount), Money(TaxRate), method, method == "Cash" ? Money(Tendered) : 0, SaleCustomer.SelectedValue as Guid?, couponBox.Text, Integer(pointsBox.Text), resumedCartId);
            ResetSale(); Refresh();
            Status.Text = $"{sale.Number} saved · Change {sale.Change:N2}"; ShowReceipt(sale);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Sale could not be completed", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private void NewProduct(object sender, RoutedEventArgs e) => ProductEditor(null);
    private void EditProduct(object sender, RoutedEventArgs e) { if (InventoryGrid.SelectedItem is Product product) ProductEditor(product); }
    private void ProductEditor(Product? original)
    {
        var dialog = new Window { Title = original == null ? "New product" : "Edit product", Width = 420, Height = 570, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, Background = Background };
        var panel = new StackPanel { Margin = new Thickness(24) }; dialog.Content = panel;
        TextBox Field(string label, string value) { panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(4, 8, 0, 0) }); var box = new TextBox { Text = value }; panel.Children.Add(box); return box; }
        var name = Field("Product name", original?.Name ?? ""); var barcode = Field("Barcode / SKU", original?.Barcode ?? "");
        var category = Field("Category", original?.Category ?? "General"); var price = Field("Unit price", (original?.Price ?? 0).ToString(CultureInfo.CurrentCulture));
        var stock = Field("Stock on hand", (original?.Stock ?? 0).ToString());
        stock.IsReadOnly = original != null;
        stock.ToolTip = "Use Shop operations → Stock audit to adjust existing stock with a reason.";
        var save = new Button { Content = "Save product", Margin = new Thickness(4, 20, 4, 0) }; panel.Children.Add(save);
        save.Click += (_, _) =>
        {
            try
            {
                if (!int.TryParse(stock.Text, out var quantity)) throw new InvalidOperationException("Stock must be a whole number.");
                if (cart.Any(l => l.ProductId == original?.Id)) throw new InvalidOperationException("Remove this product from the current cart before editing it.");
                store.SaveProduct(new Product { Id = original?.Id ?? Guid.NewGuid(), Name = name.Text.Trim(), Barcode = barcode.Text.Trim(), Category = category.Text.Trim(), Price = Money(price), Stock = quantity, ReorderLevel = original?.ReorderLevel ?? 5, Archived = original?.Archived ?? false });
                Refresh(); dialog.Close(); Status.Text = "Product saved.";
            }
            catch (Exception ex) { MessageBox.Show(dialog, ex.Message, "Check product"); }
        };
        dialog.ShowDialog();
    }
    private void ViewReceipt(object sender, RoutedEventArgs e) { if (SalesGrid.SelectedItem is Sale sale) ShowReceipt(sale); }
    private void ShowReceipt(Sale sale) => ShowDocument(sale.Number, Store.Receipt(sale));
    private void ShowDocument(string title, string receipt)
    {
        var window = new Window { Title = title, Width = 460, Height = 680, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new DockPanel { Margin = new Thickness(16) }; window.Content = panel;
        var print = new Button { Content = "Print receipt" }; DockPanel.SetDock(print, Dock.Bottom); panel.Children.Add(print);
        panel.Children.Add(new TextBox { Text = receipt, IsReadOnly = true, FontFamily = new System.Windows.Media.FontFamily("Consolas"), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, TextWrapping = TextWrapping.Wrap });
        print.Click += (_, _) =>
        {
            try
            {
                var pd = new PrintDialog(); if (pd.ShowDialog() != true) return;
                var document = new FlowDocument(new Paragraph(new Run(receipt))) { FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 12, PageWidth = pd.PrintableAreaWidth, PageHeight = pd.PrintableAreaHeight, ColumnWidth = pd.PrintableAreaWidth, PagePadding = new Thickness(20) };
                pd.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, title);
            }
            catch (Exception ex) { MessageBox.Show(window, ex.Message, "Print failed"); }
        };
        window.ShowDialog();
    }
}
