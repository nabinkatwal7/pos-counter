using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using CounterPoint.Core;

namespace CounterPoint;
public sealed record Choice(Guid Id, string Label);
public sealed record EmployeeRow(Guid Id, string Name, string Username, string Role, bool Active, decimal CommissionRate);
public sealed record AccountRow(Guid Id, string Customer, decimal Limit, int TermsDays, bool Enabled, decimal Balance);
public sealed record GiftRow(string Code, string Recipient, DateTime Expires, bool Active, decimal Balance);
public sealed record SupplierInvoiceRow(string Number, string Supplier, string Reference, DateTime DueDate, decimal Total, decimal Returned, decimal Paid, decimal Balance);
public sealed record StockRow(Guid Id, string Product, string Category, string Kind, int OnHand, int Reserved, int Available, decimal UnitCost);

internal sealed class BusinessEditor
{
    public Window Window { get; }
    public StackPanel Fields { get; } = new() { Margin = new Thickness(24) };
    public BusinessEditor(Window owner, string title, double width = 620)
    {
        Window = new Window { Owner = owner, Title = title, Width = width, Height = 700, MinHeight = 420, MinWidth = 440, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = owner.Background };
        var dock = new DockPanel(); Window.Content = dock;
        var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(20, 8, 20, 16) };
        DockPanel.SetDock(bottom, Dock.Bottom); dock.Children.Add(bottom);
        SaveButton = new Button { Content = "Save", Style = (Style)owner.FindResource("PrimaryButton"), MinWidth = 110 };
        var cancel = new Button { Content = "Cancel", IsCancel = true }; bottom.Children.Add(cancel); bottom.Children.Add(SaveButton);
        dock.Children.Add(new ScrollViewer { Content = Fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
    }
    public Button SaveButton { get; }
    public void Label(string label) => Fields.Children.Add(new TextBlock { Text = label, Margin = new Thickness(4, 10, 4, 2), TextWrapping = TextWrapping.Wrap });
    public void Note(string text) => Fields.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.SlateGray, Margin = new Thickness(4, 4, 4, 12) });
    public TextBox Text(string label, string value = "", bool multiline = false)
    {
        Label(label); var input = new TextBox { Text = value, AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, MinHeight = multiline ? 70 : 34 }; Fields.Children.Add(input); return input;
    }
    public PasswordBox Password(string label)
    {
        Label(label); var box = new PasswordBox { Margin = new Thickness(4), Padding = new Thickness(10, 7, 10, 7), MinHeight = 34 }; Fields.Children.Add(box); return box;
    }
    public ComboBox Choice(string label, IEnumerable<Choice> values, Guid selected = default)
    {
        Label(label); var input = new ComboBox { ItemsSource = values.ToList(), DisplayMemberPath = "Label", SelectedValuePath = "Id", Margin = new Thickness(4), Padding = new Thickness(8), MinHeight = 34 };
        input.SelectedValue = selected; if (input.SelectedIndex < 0 && input.Items.Count > 0) input.SelectedIndex = 0; Fields.Children.Add(input); return input;
    }
    public ComboBox Options(string label, string selected, params string[] values)
    {
        Label(label); var input = new ComboBox { ItemsSource = values, SelectedItem = selected, Margin = new Thickness(4), Padding = new Thickness(8), MinHeight = 34 }; Fields.Children.Add(input); return input;
    }
    public DatePicker Date(string label, DateTime value)
    {
        Label(label); var input = new DatePicker { SelectedDate = value, Margin = new Thickness(4), Padding = new Thickness(6) }; Fields.Children.Add(input); return input;
    }
    public CheckBox Check(string label, bool value = false)
    {
        var box = new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(4, 12, 4, 8) }; Fields.Children.Add(box); return box;
    }
}

internal sealed class ProductLineEditor
{
    public ObservableCollection<OrderItem> Lines { get; } = [];
    public StackPanel View { get; } = new();
    public ProductLineEditor(IEnumerable<Product> products, IEnumerable<OrderItem>? initial = null, Func<Product, decimal>? price = null)
    {
        if (initial != null) foreach (var line in initial) Lines.Add(line);
        var tools = new WrapPanel(); View.Children.Add(tools);
        var product = new ComboBox { ItemsSource = products.OrderBy(p => p.Name).ToList(), DisplayMemberPath = "Name", Width = 240, Margin = new Thickness(4), Padding = new Thickness(6), ToolTip = "Product" }; tools.Children.Add(product);
        var qty = new TextBox { Text = "1", Width = 70, ToolTip = "Quantity" }; tools.Children.Add(qty);
        var cost = new TextBox { Text = "0", Width = 100, ToolTip = "Unit price / cost" }; tools.Children.Add(cost);
        product.SelectionChanged += (_, _) => { if (product.SelectedItem is Product p) cost.Text = (price?.Invoke(p) ?? p.Price).ToString(CultureInfo.CurrentCulture); };
        var add = new Button { Content = "Add / replace line" }; tools.Children.Add(add);
        var grid = new DataGrid { ItemsSource = Lines, AutoGenerateColumns = false, Height = 210, Margin = new Thickness(4) };
        grid.Columns.Add(new DataGridTextColumn { Header = "Product", Binding = new Binding("Name"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Qty", Binding = new Binding("Quantity"), Width = 65 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Unit amount", Binding = new Binding("UnitPrice") { StringFormat = "N2" }, Width = 110 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Total", Binding = new Binding("Total") { StringFormat = "N2" }, Width = 100 }); View.Children.Add(grid);
        var remove = new Button { Content = "Remove selected line" }; View.Children.Add(remove); remove.Click += (_, _) => { if (grid.SelectedItem is OrderItem line) Lines.Remove(line); };
        add.Click += (_, _) =>
        {
            if (product.SelectedItem is not Product p || !int.TryParse(qty.Text, out var quantity) || quantity <= 0 || !decimal.TryParse(cost.Text, out var amount) || amount < 0) { MessageBox.Show("Select a product and enter positive quantity and non-negative amount."); return; }
            var existing = Lines.FirstOrDefault(l => l.ProductId == p.Id); if (existing != null) Lines.Remove(existing); Lines.Add(new(p.Id, p.Name, quantity, amount));
        };
        if (product.Items.Count > 0) product.SelectedIndex = 0;
    }
}

public partial class MainWindow
{
    private readonly Dictionary<string, DataGrid> businessGrids = [];
    private BusinessEditor Editor(string title, double width = 620) => new(this, title, width);
    private void Edit(BusinessEditor editor, Action save)
    {
        editor.SaveButton.Click += (_, _) =>
        {
            try { if (HasValidationErrors(editor.Window)) throw new InvalidOperationException("Correct the highlighted table values before saving."); save(); Refresh(); editor.Window.Close(); }
            catch (Exception ex) { MessageBox.Show(editor.Window, ex.Message, "Check details", MessageBoxButton.OK, MessageBoxImage.Warning); Refresh(); }
        }; editor.Window.ShowDialog();
    }
    private static bool HasValidationErrors(DependencyObject element)
    {
        if (Validation.GetHasError(element)) return true;
        if (element is not System.Windows.Media.Visual && element is not System.Windows.Media.Media3D.Visual3D) return false;
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(element); i++) if (HasValidationErrors(System.Windows.Media.VisualTreeHelper.GetChild(element, i))) return true;
        return false;
    }
    private static Guid Pick(ComboBox box) => box.SelectedValue is Guid id ? id : throw new InvalidOperationException("Select a value first.");
    private static string Option(ComboBox box) => box.SelectedItem as string ?? throw new InvalidOperationException("Select an option.");
    private static DateTime PickDate(DatePicker box) => box.SelectedDate?.Date ?? throw new InvalidOperationException("Select a date.");
    private IEnumerable<Choice> ProductChoices(Func<Product, bool>? predicate = null) => store.Data.Products.Where(p => !p.Archived && (predicate?.Invoke(p) ?? true)).OrderBy(p => p.Name).Select(p => new Choice(p.Id, p.Name + " · " + p.Barcode));
    private IEnumerable<Choice> CustomerChoices() => store.Data.Customers.OrderBy(c => c.Name).Select(c => new Choice(c.Id, c.Name + " · " + c.Phone));
    private IEnumerable<Choice> SupplierChoices() => store.Data.Suppliers.OrderBy(c => c.Name).Select(c => new Choice(c.Id, c.Name));
    private IEnumerable<Choice> LocationChoices() => store.Data.Business.Locations.Where(l => l.Active).Select(l => new Choice(l.Id, l.Name));
    private IEnumerable<Choice> EmployeeChoices(bool optional = false) => (optional ? new[] { new Choice(Guid.Empty, "Unassigned") } : []).Concat(store.Data.Business.Employees.Where(e => e.Active).Select(e => new Choice(e.Id, e.Name)));
    private (DockPanel Panel, WrapPanel Tools) BusinessPage(TabControl tabs, string key, string description)
    {
        var page = new DockPanel { Margin = new Thickness(16) };
        var note = new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 0, 4, 12) }; DockPanel.SetDock(note, Dock.Top); page.Children.Add(note);
        var tools = new WrapPanel(); DockPanel.SetDock(tools, Dock.Top); page.Children.Add(tools);
        var grid = new DataGrid { AutoGenerateColumns = true, Margin = new Thickness(4, 12, 4, 4), CanUserSortColumns = true };
        grid.AutoGeneratingColumn += (_, e) =>
        {
            if (e.PropertyName.EndsWith("Id") || e.PropertyName is "Lines" or "Parts" or "PasswordHash" or "Salt" or "Components" or "Payments" or "DepositPayments") e.Cancel = true;
            if (e.Column is DataGridTextColumn column)
            {
                column.Width = new DataGridLength(1, DataGridLengthUnitType.Auto);
                if (e.PropertyType == typeof(decimal)) column.Binding.StringFormat = "N2";
                if (e.PropertyType == typeof(DateTime)) column.Binding.StringFormat = "yyyy-MM-dd";
                if (e.PropertyType == typeof(DateTimeOffset) || e.PropertyType == typeof(DateTimeOffset?)) column.Binding.StringFormat = "yyyy-MM-dd HH:mm";
            }
        };
        businessGrids[key] = grid; page.Children.Add(grid); tabs.Items.Add(new TabItem { Header = key, Content = page }); return (page, tools);
    }
    private T BusinessSelected<T>(string key) => businessGrids[key].SelectedItem is T value ? value : throw new InvalidOperationException("Select a row first.");
    private void BusinessAction(Panel panel, string label, Action action) => AddAction(panel, label, action);
}
