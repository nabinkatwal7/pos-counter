using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using CounterPoint.Core;

namespace CounterPoint;
internal sealed class ReceiptEntry
{
    public Guid ProductId { get; init; }
    public string Product { get; init; } = "";
    public int Ordered { get; init; }
    public int Received { get; init; }
    public int Remaining => Ordered - Received;
    public int Receive { get; set; }
}
internal sealed class CountEntry
{
    public Guid ProductId { get; init; }
    public string Product { get; init; } = "";
    public int Expected { get; init; }
    public int? Counted { get; set; }
}
public partial class MainWindow
{
    private ComboBox supplyLocation = null!;
    private TextBox demandLookback = null!, demandCoverage = null!;
    private DatePicker marginFrom = null!, marginTo = null!;
    private void BuildSupplyChain()
    {
        var tabs = new TabControl { TabStripPlacement = Dock.Left }; MainTabs.Items.Add(new TabItem { Header = "Supply chain", Content = tabs });
        var locations = BusinessPage(tabs, "Stock locations", "Manage physical storage locations. Main counter quantities remain compatible with existing data. Select a location for balances, transfers and adjustments.");
        BusinessAction(locations.Tools, "+ Location", () => EditLocation(null));
        BusinessAction(locations.Tools, "Edit location", () => EditLocation(BusinessSelected<StockLocation>("Stock locations")));
        BusinessAction(locations.Tools, "Location stock", () => ShowLocationStock(BusinessSelected<StockLocation>("Stock locations").Id));
        BusinessAction(locations.Tools, "Transfer stock", TransferEditor);
        BusinessAction(locations.Tools, "Adjust location stock", LocationAdjustmentEditor);
        BusinessAction(locations.Tools, "Transfer ledger", () => ShowTable("Stock transfers", store.Data.Business.Transfers.OrderByDescending(t => t.Time)));

        var orders = BusinessPage(tabs, "Purchase orders", "Build multi-line draft orders, submit them, receive partial deliveries, and cancel outstanding quantities. Receipts update stock, weighted costs and supplier invoice balances together.");
        BusinessAction(orders.Tools, "+ Purchase order", NewPurchaseOrder);
        BusinessAction(orders.Tools, "Edit draft lines", EditPurchaseOrder);
        BusinessAction(orders.Tools, "Submit", () => { store.SetPurchaseOrderStatus(BusinessSelected<PurchaseOrder>("Purchase orders").Number, "Submitted"); Refresh(); });
        BusinessAction(orders.Tools, "Receive delivery", ReceiveOrderEditor);
        BusinessAction(orders.Tools, "Cancel remainder", () => { var order = BusinessSelected<PurchaseOrder>("Purchase orders"); if (MessageBox.Show(this, "Cancel all unreceived quantities on " + order.Number + "?", "Purchase order", MessageBoxButton.YesNo) == MessageBoxResult.Yes) { store.SetPurchaseOrderStatus(order.Number, "Cancelled"); Refresh(); } });
        BusinessAction(orders.Tools, "View / print order", () => { var o = BusinessSelected<PurchaseOrder>("Purchase orders"); ShowDocument(o.Number, $"PURCHASE ORDER {o.Number}\nSupplier: {o.Supplier}\nExpected: {o.Expected:yyyy-MM-dd}\nStatus: {o.Status}\n" + string.Join("\n", o.Lines.Select(l => $"{l.Name} · {l.Quantity} × {l.UnitPrice:N2} = {l.Total:N2}")) + $"\nTotal: {o.Total:N2}"); });

        var supplierReturns = BusinessPage(tabs, "Supplier returns", "Return physical goods against an original receipt. Quantity is capped by the receipt and unreserved stock. A credit reduces the related supplier invoice; paid invoices can become supplier credits.");
        BusinessAction(supplierReturns.Tools, "Return received goods", SupplierReturnEditor);
        BusinessAction(supplierReturns.Tools, "Original receipts", () => ShowTable("Purchase receipts", store.Data.Purchases.OrderByDescending(p => p.Time)));

        var payables = BusinessPage(tabs, "Supplier accounts", "Receipt-based supplier invoices, credit notes, partial payments and supplier refunds. Cash payments affect the currently open drawer automatically. Card records represent externally processed transactions.");
        BusinessAction(payables.Tools, "Pay selected invoice", SupplierPaymentEditor);
        BusinessAction(payables.Tools, "Record supplier refund", SupplierRefundEditor);
        BusinessAction(payables.Tools, "Payment ledger", () => ShowTable("Supplier payments / refunds", store.Data.Business.SupplierPayments.OrderByDescending(p => p.Time)));
        BusinessAction(payables.Tools, "Supplier statement", SupplierStatementEditor);

        var counts = BusinessPage(tabs, "Stocktakes", "Start a location snapshot, record physical counts and reconcile all variances in one transaction. Every product must be counted; changed stock or conflicting reservations block reconciliation.");
        BusinessAction(counts.Tools, "+ Stocktake", () => { var e = Editor("Start stocktake"); var name = e.Text("Count name"); var location = e.Choice("Location", LocationChoices()); Edit(e, () => store.StartStocktake(name.Text, Pick(location))); });
        BusinessAction(counts.Tools, "Record counts", StocktakeCountEditor);
        BusinessAction(counts.Tools, "View variances", () => { var t = BusinessSelected<Stocktake>("Stocktakes"); ShowTable(t.Name, t.Lines.Select(l => new { l.Product, l.Expected, l.Counted, Variance = l.Counted - l.Expected })); });
        BusinessAction(counts.Tools, "Apply reconciliation", () => { var t = BusinessSelected<Stocktake>("Stocktakes"); if (MessageBox.Show(this, "Apply counted quantities to inventory for " + t.Name + "?", "Stocktake", MessageBoxButton.YesNo) == MessageBoxResult.Yes) { store.FinishStocktake(t.Id, true); Refresh(); } });
        BusinessAction(counts.Tools, "Cancel count", () => { store.FinishStocktake(BusinessSelected<Stocktake>("Stocktakes").Id, false); Refresh(); });

        var costing = BusinessPage(tabs, "Costing & margins", "Configure product types, average cost and purchasing defaults. Receipts maintain a weighted average across locations; sales snapshot their component costs. Historical sales with no recorded costs cannot provide a reliable margin.");
        BusinessAction(costing.Tools, "Configure selected product", ProductConfigurationEditor);
        BusinessAction(costing.Tools, "Inventory valuation", () => ShowTable("Inventory valuation · " + store.InventoryValuation().Sum(v => v.Value).ToString("N2"), store.InventoryValuation()));
        marginFrom = new DatePicker { SelectedDate = DateTime.Today.AddDays(-29), Width = 135, Margin = new Thickness(4) }; marginTo = new DatePicker { SelectedDate = DateTime.Today, Width = 135, Margin = new Thickness(4) }; costing.Tools.Children.Add(marginFrom); costing.Tools.Children.Add(marginTo);
        BusinessAction(costing.Tools, "Gross margin report", () => { var report = store.MarginReport(PickDate(marginFrom), PickDate(marginTo)); ShowTable($"Gross margin {report.Sum(r => r.GrossMargin):N2} · before operating expenses", report); });

        var planner = BusinessPage(tabs, "Replenishment", "Forecast physical component demand from completed sales, lead time, safety stock and planned inbound quantities. Convert shortages into supplier-grouped draft purchase orders for review.");
        supplyLocation = new ComboBox { ItemsSource = LocationChoices().ToList(), DisplayMemberPath = "Label", SelectedValuePath = "Id", SelectedValue = Store.CounterLocationId, Width = 160, Margin = new Thickness(4) }; planner.Tools.Children.Add(supplyLocation);
        demandLookback = new TextBox { Text = "30", Width = 70, ToolTip = "Demand lookback days" }; demandCoverage = new TextBox { Text = "14", Width = 70, ToolTip = "Future coverage days" }; planner.Tools.Children.Add(demandLookback); planner.Tools.Children.Add(demandCoverage);
        BusinessAction(planner.Tools, "Calculate plan", UpdateReplenishment);
        BusinessAction(planner.Tools, "Create draft purchase orders", () => { var created = store.CreateReplenishmentOrders(Integer(demandLookback.Text), Integer(demandCoverage.Text), Pick(supplyLocation)); Refresh(); UpdateReplenishment(); Status.Text = $"Created {created.Count} draft purchase orders."; });

        var bundles = BusinessPage(tabs, "Product bundles", "Configure a zero-stock product as Bundle under Costing & margins, then define its physical components. Sales and returns consume or restore component stock. Open reservations protect recipes from modification.");
        BusinessAction(bundles.Tools, "Configure bundle recipe", BundleEditor);
        BusinessAction(bundles.Tools, "View selected recipe", () => { var p = BusinessSelected<Product>("Product bundles"); var recipe = store.Data.Business.Bundles.SingleOrDefault(b => b.ProductId == p.Id); ShowTable(p.Name, (recipe?.Parts ?? []).Select(l => new { Product = store.Data.Products.Single(p => p.Id == l.ProductId).Name, l.Quantity })); });

        var prices = BusinessPage(tabs, "Price books", "Schedule customer-specific and general quantity tiers. Customer-specific rules take priority, then the largest qualifying quantity. Checkout reprices the cart when customer or quantity changes.");
        BusinessAction(prices.Tools, "+ Price rule", () => PriceRuleEditor(null));
        BusinessAction(prices.Tools, "Edit price rule", () => PriceRuleEditor(BusinessSelected<PriceRule>("Price books")));
        BusinessAction(prices.Tools, "Enable / disable", () => { var rule = BusinessSelected<PriceRule>("Price books"); store.SavePriceRule(rule with { Enabled = !rule.Enabled }); Refresh(); });
    }
    private void EditLocation(StockLocation? location)
    {
        var e = Editor("Stock location"); var name = e.Text("Name", location?.Name ?? ""); var active = e.Check("Active", location?.Active ?? true);
        Edit(e, () => store.SaveLocation(location?.Id ?? Guid.Empty, name.Text, active.IsChecked == true));
    }
    private void ShowLocationStock(Guid location) => ShowTable("Stock · " + store.Data.Business.Locations.Single(l => l.Id == location).Name, store.Data.Products.Where(p => p.Kind == "Stock").Select(p => new { p.Name, p.Barcode, OnHand = store.StockAt(p.Id, location), Reserved = store.ReservedStock(p.Id, location), Available = store.StockAt(p.Id, location) - store.ReservedStock(p.Id, location) }));
    private void TransferEditor()
    {
        var e = Editor("Transfer stock"); var product = e.Choice("Product", ProductChoices(p => p.Kind == "Stock")); var from = e.Choice("From", LocationChoices()); var to = e.Choice("To", LocationChoices()); var qty = e.Text("Quantity", "1"); var reason = e.Text("Reason");
        Edit(e, () => store.TransferStock(Pick(product), Pick(from), Pick(to), Integer(qty.Text), reason.Text));
    }
    private void LocationAdjustmentEditor()
    {
        var e = Editor("Adjust location stock"); var product = e.Choice("Product", ProductChoices(p => p.Kind == "Stock")); var location = e.Choice("Location", LocationChoices()); var qty = e.Text("Quantity change (+ / −)", "0"); var reason = e.Text("Reason");
        Edit(e, () => store.AdjustLocationStock(Pick(product), Pick(location), Integer(qty.Text), reason.Text));
    }
    private void NewPurchaseOrder()
    {
        var e = Editor("Create purchase order"); var supplier = e.Choice("Supplier", SupplierChoices()); var location = e.Choice("Receive into", LocationChoices()); var due = e.Date("Expected delivery", DateTime.Today.AddDays(7));
        Edit(e, () => store.CreatePurchaseOrder(Pick(supplier), Pick(location), PickDate(due)));
    }
    private void EditPurchaseOrder()
    {
        var order = BusinessSelected<PurchaseOrder>("Purchase orders"); var e = Editor("Edit lines · " + order.Number, 850);
        e.Note("Add or replace products below. Unit amount is purchase cost; zero-stock services and bundles cannot be purchased.");
        var lines = new ProductLineEditor(store.Data.Products.Where(p => p.Kind == "Stock" && !p.Archived), order.Lines, p => p.UnitCost); e.Fields.Children.Add(lines.View);
        Edit(e, () => store.ReplacePurchaseOrderLines(order.Number, lines.Lines));
    }
    private void ReceiveOrderEditor()
    {
        var order = BusinessSelected<PurchaseOrder>("Purchase orders"); var e = Editor("Receive · " + order.Number, 850);
        var reference = e.Text("Supplier delivery / invoice reference");
        var entries = order.Lines.Select(l => new ReceiptEntry { ProductId = l.ProductId, Product = l.Name, Ordered = l.Quantity, Received = store.ReceivedQuantity(order.Number, l.ProductId) }).ToList();
        var grid = EditableGrid(entries, ("Product", "Product", true), ("Ordered", "Ordered", true), ("Received", "Received", true), ("Remaining", "Remaining", true), ("Receive now", "Receive", false)); e.Fields.Children.Add(grid);
        Edit(e, () => { grid.CommitEdit(DataGridEditingUnit.Cell, true); grid.CommitEdit(DataGridEditingUnit.Row, true); store.ReceivePurchaseOrder(order.Number, reference.Text, entries.Where(r => r.Receive != 0).Select(r => (r.ProductId, r.Receive))); });
    }
    private DataGrid EditableGrid(System.Collections.IEnumerable source, params (string Header, string Property, bool ReadOnly)[] columns)
    {
        var grid = new DataGrid { ItemsSource = source, AutoGenerateColumns = false, IsReadOnly = false, Height = 320, Margin = new Thickness(4), CanUserAddRows = false };
        foreach (var column in columns) grid.Columns.Add(new DataGridTextColumn { Header = column.Header, Binding = new Binding(column.Property) { Mode = column.ReadOnly ? BindingMode.OneWay : BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, IsReadOnly = column.ReadOnly, Width = new DataGridLength(1, DataGridLengthUnitType.Star) }); return grid;
    }
    private void SupplierReturnEditor()
    {
        var e = Editor("Return goods to supplier"); var purchase = e.Choice("Original receipt", store.Data.Purchases.Select(p => new Choice(ReceiptKey(p.Number), p.Number + " · " + p.Supplier + " · " + p.Product)));
        var qty = e.Text("Quantity", "1"); var reason = e.Text("Reason");
        Edit(e, () => store.ReturnToSupplier(store.Data.Purchases.Single(p => ReceiptKey(p.Number) == Pick(purchase)).Number, Integer(qty.Text), reason.Text));
    }
    private static Guid ReceiptKey(string number) => new(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(number)));
    private void SupplierPaymentEditor()
    {
        var invoice = BusinessSelected<SupplierInvoiceRow>("Supplier accounts"); var e = Editor("Pay " + invoice.Number); e.Note($"Outstanding: {invoice.Balance:N2}"); var amount = e.Text("Payment amount", Math.Max(0, invoice.Balance).ToString()); var method = e.Options("Method", "Cash", "Cash", "Card"); var reference = e.Text("Unique payment reference");
        Edit(e, () => store.PaySupplier(invoice.Number, Number(amount.Text), Option(method), reference.Text));
    }
    private void SupplierRefundEditor()
    {
        var invoice = BusinessSelected<SupplierInvoiceRow>("Supplier accounts"); var e = Editor("Supplier credit refund · " + invoice.Number); var amount = e.Text("Refund received", Math.Max(0, -invoice.Balance).ToString()); var method = e.Options("Method", "Cash", "Cash", "Card"); var reference = e.Text("Refund reference");
        Edit(e, () => store.ReceiveSupplierRefund(invoice.Number, Number(amount.Text), Option(method), reference.Text));
    }
    private void SupplierStatementEditor()
    {
        var e = Editor("Supplier statement"); var supplier = e.Choice("Supplier", SupplierChoices()); e.SaveButton.Content = "View statement";
        Edit(e, () => ShowTable("Supplier statement", store.Data.Purchases.Where(p => p.SupplierId == Pick(supplier)).Select(p => new { p.Number, p.Time, p.Reference, p.DueDate, Total = p.Total, Balance = store.SupplierInvoiceBalance(p.Number), OverdueDays = Math.Max(0, (DateTime.Today - (p.DueDate == default ? p.Time.Date.AddDays(30) : p.DueDate)).Days) })));
    }
    private void StocktakeCountEditor()
    {
        var take = BusinessSelected<Stocktake>("Stocktakes"); var e = Editor("Record counts · " + take.Name, 800); e.Note("Leave uncounted rows blank. Counts are saved to the session without changing inventory until reconciliation.");
        var rows = take.Lines.Select(l => new CountEntry { ProductId = l.ProductId, Product = l.Product, Expected = l.Expected, Counted = l.Counted }).ToList();
        var grid = EditableGrid(rows, ("Product", "Product", true), ("Snapshot", "Expected", true), ("Physical count", "Counted", false)); e.Fields.Children.Add(grid);
        Edit(e, () => { grid.CommitEdit(DataGridEditingUnit.Cell, true); grid.CommitEdit(DataGridEditingUnit.Row, true); store.RecordStocktakeCounts(take.Id, rows.Where(r => r.Counted != null).Select(r => (r.ProductId, r.Counted!.Value))); });
    }
    private void ProductConfigurationEditor()
    {
        RequireEmptyCart(); var p = BusinessSelected<Product>("Costing & margins"); var e = Editor("Configure " + p.Name);
        var kind = e.Options("Product type", p.Kind, "Stock", "Service", "Bundle"); var cost = e.Text("Current average unit cost", p.UnitCost.ToString()); var supplier = e.Choice("Preferred supplier", new[] { new Choice(Guid.Empty, "Unassigned") }.Concat(SupplierChoices()), p.PreferredSupplierId); var lead = e.Text("Lead time in days", p.LeadDays.ToString()); var safety = e.Text("Safety stock", p.SafetyStock.ToString());
        e.Note("Changing type requires zero physical stock and no open reservations. Cost changes affect future sales, not historical cost snapshots.");
        Edit(e, () => store.ConfigureProduct(p.Id, Option(kind), Number(cost.Text), Pick(supplier), Integer(lead.Text), Integer(safety.Text)));
    }
    private void BundleEditor()
    {
        var e = Editor("Bundle recipe", 850); var product = e.Choice("Bundle product", ProductChoices(p => p.Kind == "Bundle"));
        var lines = new ProductLineEditor(store.Data.Products.Where(p => p.Kind == "Stock" && !p.Archived), price: _ => 0); e.Note("Unit amount is ignored here; component quantities define one bundle unit."); e.Fields.Children.Add(lines.View);
        void Load() { lines.Lines.Clear(); if (product.SelectedValue is Guid id) foreach (var part in store.Data.Business.Bundles.SingleOrDefault(b => b.ProductId == id)?.Parts ?? []) lines.Lines.Add(new(part.ProductId, store.Data.Products.Single(p => p.Id == part.ProductId).Name, part.Quantity, 0)); }
        product.SelectionChanged += (_, _) => Load(); Load(); Edit(e, () => store.SaveBundle(Pick(product), lines.Lines.Select(l => new BundlePart(l.ProductId, l.Quantity))));
    }
    private void PriceRuleEditor(PriceRule? rule)
    {
        var e = Editor("Quantity / customer price rule"); var name = e.Text("Rule name", rule?.Name ?? ""); var product = e.Choice("Product", ProductChoices(), rule?.ProductId ?? Guid.Empty); var customer = e.Choice("Customer", new[] { new Choice(Guid.Empty, "All customers") }.Concat(CustomerChoices()), rule?.CustomerId ?? Guid.Empty);
        var qty = e.Text("Minimum quantity", (rule?.MinimumQuantity ?? 1).ToString()); var price = e.Text("Unit selling price", (rule?.UnitPrice ?? 0).ToString()); var from = e.Date("Starts", rule?.Starts ?? DateTime.Today); var to = e.Date("Ends", rule?.Ends ?? DateTime.Today.AddMonths(1)); var enabled = e.Check("Enabled", rule?.Enabled ?? true);
        Edit(e, () => store.SavePriceRule(new(rule?.Id ?? Guid.Empty, name.Text, Pick(product), Pick(customer), Integer(qty.Text), Number(price.Text), PickDate(from), PickDate(to), enabled.IsChecked == true)));
    }
    private void UpdateReplenishment() => businessGrids["Replenishment"].ItemsSource = store.ReplenishmentPlan(Integer(demandLookback.Text), Integer(demandCoverage.Text), Pick(supplyLocation));
    private void RefreshSupplyChain()
    {
        businessGrids["Stock locations"].ItemsSource = store.Data.Business.Locations.ToList();
        businessGrids["Purchase orders"].ItemsSource = store.Data.Business.PurchaseOrders.OrderByDescending(o => o.Number).ToList();
        businessGrids["Supplier returns"].ItemsSource = store.Data.Business.SupplierReturns.OrderByDescending(r => r.Time).ToList();
        businessGrids["Supplier accounts"].ItemsSource = store.Data.Purchases.Select(p => new SupplierInvoiceRow(p.Number, p.Supplier, p.Reference, p.DueDate == default ? p.Time.Date.AddDays(30) : p.DueDate, p.Total, store.Data.Business.SupplierReturns.Where(r => r.PurchaseNumber == p.Number).Sum(r => r.Credit), store.Data.Business.SupplierPayments.Where(x => x.PurchaseNumber == p.Number).Sum(x => x.Amount), store.SupplierInvoiceBalance(p.Number))).ToList();
        businessGrids["Stocktakes"].ItemsSource = store.Data.Business.Stocktakes.OrderByDescending(t => t.Created).ToList();
        businessGrids["Costing & margins"].ItemsSource = store.Data.Products.OrderBy(p => p.Name).ToList();
        businessGrids["Product bundles"].ItemsSource = store.Data.Products.Where(p => p.Kind == "Bundle").ToList();
        businessGrids["Price books"].ItemsSource = store.Data.Business.PriceRules.ToList();
        var selected = supplyLocation.SelectedValue; supplyLocation.ItemsSource = LocationChoices().ToList(); supplyLocation.SelectedValue = selected ?? Store.CounterLocationId;
    }
}
