using System.Windows;
using System.IO;
using System.Windows.Controls;
using CounterPoint;
using CounterPoint.Core;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CounterPointUi-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var file = System.IO.Path.Combine(directory, "store.json"); var store = new Store(file);
            var p = new Product { Name = "Sample coffee", Barcode = "TEST001", Price = 150, Stock = 10 }; store.SaveProduct(p);
            var c = new Customer(Guid.NewGuid(), "UI customer", "", ""); store.SaveCustomer(c);
            store.SaveSupplier(new(Guid.NewGuid(), "Supplier", "", ""));
            store.OpenShift("UI cashier", 100);
            store.Checkout([new(p.Id, p.Name, 150, 1)], 0, 13, "Cash", 200, c.Id);
            store.SaveCoupon(new("TEST", true, 10, 0, DateTime.Today.AddDays(7), 10));
            store.SaveCart("Held example", "Held", [new(p.Id, p.Name, 150, 1)], 0, 0, c.Id, DateTime.Today);
            store.SaveCart("Quote example", "Quote", [new(p.Id, p.Name, 150, 1)], 0, 13, c.Id, DateTime.Today.AddDays(7));
            var app = new App(); app.InitializeComponent();
            var window = new MainWindow(file);
            var root = (FrameworkElement)window.Content;
            void Layout() { root.Measure(new Size(1230, 720)); root.Arrange(new Rect(0, 0, 1230, 720)); root.UpdateLayout(); }
            Layout();
            var mainTabs = (TabControl)window.FindName("MainTabs"); var pageCount = 0;
            foreach (TabItem tab in mainTabs.Items)
            {
                mainTabs.SelectedItem = tab; Layout(); pageCount++;
                if (tab.Content is TabControl nested)
                    foreach (TabItem page in nested.Items) { nested.SelectedItem = page; Layout(); pageCount++; }
            }
            if (pageCount < 20) throw new Exception("Missing management pages.");
            var button = new Button { Content = "Fluent test" };
            var buttonStyle = (Style)app.FindResource(typeof(Button));
            if (buttonStyle.BasedOn != app.FindResource("DefaultButtonStyle")) throw new Exception("Buttons do not inherit Microsoft's Fluent template.");
            button.Style = buttonStyle; button.ApplyTemplate();
            if (button.Template == null) throw new Exception("Missing Fluent button template.");
            var grids = FindLogical<DataGrid>(window).ToList();
            if (grids.Any(g => g.Items.Count > 0 && g.Columns.Count == 0)) throw new Exception("A populated table has no columns.");
            window.Close(); app.Shutdown();
            Console.WriteLine($"PASS: {pageCount} WPF pages initialized and laid out with populated data; Microsoft Fluent resources and button template verified.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { Directory.Delete(directory, true); }
    }
    private static IEnumerable<T> FindLogical<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T item) yield return item;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var found in FindLogical<T>(child)) yield return found;
    }
}
