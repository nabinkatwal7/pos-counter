using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using CounterPoint.Core;

namespace CounterPoint;
internal sealed class ReturnEntry
{
    public Guid ProductId { get; init; }
    public string Product { get; init; } = "";
    public int Sold { get; init; }
    public int Returned { get; init; }
    public int Remaining => Sold - Returned;
    public int ReturnNow { get; set; }
    public bool Restock { get; set; } = true;
}
public partial class MainWindow
{
    private Guid fulfillingOrderId, fulfillingJobId;
    private ComboBox saleLocation = null!;
    private Guid SelectedLocation => saleLocation?.SelectedValue is Guid location ? location : Store.CounterLocationId;
    private bool IsFulfillment => fulfillingOrderId != Guid.Empty || fulfillingJobId != Guid.Empty;
    private void BuildCustomerBusiness()
    {
        var tabs = new TabControl { TabStripPlacement = Dock.Left }; MainTabs.Items.Add(new TabItem { Header = "Accounts & orders", Content = tabs });
        var accounts = BusinessPage(tabs, "Customer credit", "Create credit limits and payment terms, invoice at checkout using Credit or a split portion, collect partial payments, and view running statements and aging. Returns reduce debt; overpaid returned invoices leave customer credit for future credit purchases.");
        BusinessAction(accounts.Tools, "Configure account", CreditAccountEditor);
        BusinessAction(accounts.Tools, "Receive payment", AccountPaymentEditor);
        BusinessAction(accounts.Tools, "View statement", () => { var account = BusinessSelected<AccountRow>("Customer credit"); ShowTable(account.Customer + " · Statement", store.CustomerStatement(account.Id)); });
        BusinessAction(accounts.Tools, "Outstanding aging", () => { var account = BusinessSelected<AccountRow>("Customer credit"); ShowTable(account.Customer + " · Aging", store.CustomerAging(account.Id)); });
        BusinessAction(accounts.Tools, "Payment ledger", () => ShowTable("Customer account payments", store.Data.Business.AccountPayments));

        var gifts = BusinessPage(tabs, "Gift cards", "Issue or top up stored-value cards, redeem at checkout, suspend lost cards and inspect balances. Gift-card portions of returns restore card value. Funding and refunds are ledger records; external card-terminal processing remains separate.");
        BusinessAction(gifts.Tools, "Issue gift card", IssueGiftCardEditor);
        BusinessAction(gifts.Tools, "Top up", () =>
        {
            var card = BusinessSelected<GiftRow>("Gift cards"); var e = Editor("Top up " + card.Code); var amount = e.Text("Amount", "0"); var method = e.Options("Funding method", "Cash", "Cash", "Card"); var reference = e.Text("Unique funding reference"); Edit(e, () => store.TopUpGiftCard(card.Code, Number(amount.Text), Option(method), reference.Text));
        });
        BusinessAction(gifts.Tools, "Suspend / activate", () => { store.ToggleGiftCard(BusinessSelected<GiftRow>("Gift cards").Code); Refresh(); });
        BusinessAction(gifts.Tools, "Value ledger", () => { var card = BusinessSelected<GiftRow>("Gift cards"); ShowTable(card.Code + " · Ledger", store.Data.Business.GiftEntries.Where(g => g.Code == card.Code).OrderBy(g => g.Time)); });

        var layaway = BusinessPage(tabs, "Layaway", "Create a layaway from the current Sell cart and named customer. Inventory is reserved immediately. Record multiple deposits, collect the balance at checkout, then mark collected. Cancellation releases stock and records deposit refunds against their original methods.");
        BusinessAction(layaway.Tools, "Create from Sell cart", () => CreateCustomerOrderEditor("Layaway"));
        BusinessAction(layaway.Tools, "Record deposit", LayawayDepositEditor);
        BusinessAction(layaway.Tools, "Fulfill at checkout", () => LoadCustomerOrder(BusinessSelected<CustomerOrder>("Layaway")));
        BusinessAction(layaway.Tools, "Mark ready", () => ChangeCustomerOrder("Layaway", "Ready"));
        BusinessAction(layaway.Tools, "Mark collected", () => ChangeCustomerOrder("Layaway", "Collected"));
        BusinessAction(layaway.Tools, "Cancel & refund deposits", () => CancelCustomerOrder("Layaway"));
        BusinessAction(layaway.Tools, "Order / payments", () => ShowCustomerOrder(BusinessSelected<CustomerOrder>("Layaway")));

        var delivery = BusinessPage(tabs, "Delivery orders", "Capture multi-line customer orders and delivery details, reserve stock, pick, invoice, dispatch with tracking and confirm delivery. Drafts do not reserve stock. Invoiced orders use the normal return workflow.");
        BusinessAction(delivery.Tools, "Create from Sell cart", () => CreateCustomerOrderEditor("Delivery"));
        BusinessAction(delivery.Tools, "Reserve", () => ChangeCustomerOrder("Delivery orders", "Reserved"));
        BusinessAction(delivery.Tools, "Mark picked", () => ChangeCustomerOrder("Delivery orders", "Picked"));
        BusinessAction(delivery.Tools, "Invoice at checkout", () => LoadCustomerOrder(BusinessSelected<CustomerOrder>("Delivery orders")));
        BusinessAction(delivery.Tools, "Dispatch", () => { var order = BusinessSelected<CustomerOrder>("Delivery orders"); var e = Editor("Dispatch " + order.Number); var tracking = e.Text("Courier / tracking reference"); Edit(e, () => store.UpdateOrderState(order.Id, "Dispatched", tracking.Text)); });
        BusinessAction(delivery.Tools, "Confirm delivered", () => ChangeCustomerOrder("Delivery orders", "Delivered"));
        BusinessAction(delivery.Tools, "Cancel order", () => CancelCustomerOrder("Delivery orders"));
        BusinessAction(delivery.Tools, "Order / delivery note", () => ShowCustomerOrder(BusinessSelected<CustomerOrder>("Delivery orders")));

        var returns = BusinessPage(tabs, "Partial returns", "Return selected quantities from an original receipt, mark each line for restock or write-off, and preserve unreturned quantities. Refunds include allocated discounts and tax, follow original payment portions and cannot exceed the receipt. Physical refunds are performed separately.");
        BusinessAction(returns.Tools, "Return selected items", PartialReturnEditor);
        BusinessAction(returns.Tools, "Refund ledger", () => ShowTable("Refunds", store.Data.Returns.OrderByDescending(r => r.Time)));
        BusinessAction(returns.Tools, "View original receipt", () => ShowReceipt(BusinessSelected<Sale>("Partial returns")));
        BuildCheckoutExtensions();
    }
    private void CreditAccountEditor()
    {
        var e = Editor("Customer credit account"); var customer = e.Choice("Customer", CustomerChoices()); var limit = e.Text("Credit limit", "0"); var terms = e.Text("Payment terms (days)", "30"); var enabled = e.Check("Credit enabled", true);
        void Load() { if (customer.SelectedValue is Guid id) { var a = store.Data.Business.CreditAccounts.SingleOrDefault(a => a.CustomerId == id); limit.Text = (a?.Limit ?? 0).ToString(); terms.Text = (a?.TermsDays ?? 30).ToString(); enabled.IsChecked = a?.Enabled ?? true; } }
        customer.SelectionChanged += (_, _) => Load(); Load(); Edit(e, () => store.SaveCreditAccount(Pick(customer), Number(limit.Text), Integer(terms.Text), enabled.IsChecked == true));
    }
    private void AccountPaymentEditor()
    {
        var account = BusinessSelected<AccountRow>("Customer credit"); var e = Editor("Receive payment · " + account.Customer); e.Note($"Current balance: {account.Balance:N2}"); var amount = e.Text("Amount", Math.Max(0, account.Balance).ToString()); var method = e.Options("Method", "Cash", "Cash", "Card"); var reference = e.Text("Unique payment reference");
        Edit(e, () => store.ReceiveAccountPayment(account.Id, Number(amount.Text), Option(method), reference.Text));
    }
    private void IssueGiftCardEditor()
    {
        var e = Editor("Issue gift card"); var code = e.Text("Unique card code", "GC-" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant()); var recipient = e.Text("Recipient / notes"); var expiry = e.Date("Valid through", DateTime.Today.AddYears(1)); var amount = e.Text("Initial value", "0"); var method = e.Options("Funding method", "Cash", "Cash", "Card"); var reference = e.Text("Funding reference");
        Edit(e, () => store.IssueGiftCard(code.Text, recipient.Text, PickDate(expiry), Number(amount.Text), Option(method), reference.Text));
    }
    private void CreateCustomerOrderEditor(string kind)
    {
        if (cart.Count == 0 || SaleCustomer.SelectedValue is not Guid customer) throw new InvalidOperationException("Build the order in Sell and select a named customer first.");
        if (IsFulfillment || resumedCartId != null || couponBox.Text.Length > 0 || Integer(pointsBox.Text) != 0) throw new InvalidOperationException("Use a new cart without coupons or points to create an order.");
        var e = Editor("Create " + kind); var due = e.Date("Promised / collection date", DateTime.Today.AddDays(7)); var address = e.Text("Delivery address", "", true); var notes = e.Text("Order notes", "", true); e.Note($"Customer: {store.Data.Customers.Single(c => c.Id == customer).Name}. Items and agreed prices are fixed once the order is saved.");
        Edit(e, () => { store.CreateOrder(kind, customer, SelectedLocation, PickDate(due), address.Text, notes.Text, cart, Money(Discount), Money(TaxRate)); ResetSale(); });
    }
    private void LayawayDepositEditor()
    {
        var order = BusinessSelected<CustomerOrder>("Layaway"); var e = Editor("Deposit · " + order.Number); e.Note($"Order total: {order.Total:N2} · Paid: {store.OrderDeposits(order.Id):N2} · Due: {order.Total - store.OrderDeposits(order.Id):N2}"); var amount = e.Text("Deposit amount", "0"); var method = e.Options("Method", "Cash", "Cash", "Card"); var reference = e.Text("Unique deposit reference");
        Edit(e, () => store.PayOrder(order.Id, Number(amount.Text), Option(method), reference.Text));
    }
    private void ChangeCustomerOrder(string grid, string state) { store.UpdateOrderState(BusinessSelected<CustomerOrder>(grid).Id, state); Refresh(); }
    private void CancelCustomerOrder(string grid)
    {
        var order = BusinessSelected<CustomerOrder>(grid); var e = Editor("Cancel " + order.Number); var reason = e.Text("Cancellation reason"); e.Note($"Deposits to refund: {store.OrderDeposits(order.Id):N2}. Issue the actual refund separately through the original payment method.");
        Edit(e, () => { store.CancelOrder(order.Id, reason.Text); if (fulfillingOrderId == order.Id) ResetSale(); });
    }
    private void ShowCustomerOrder(CustomerOrder order)
    {
        ShowDocument(order.Number, $"{order.Kind.ToUpperInvariant()} ORDER {order.Number}\nCustomer: {store.Data.Customers.Single(c => c.Id == order.CustomerId).Name}\nDue: {order.Due:yyyy-MM-dd} · {order.Status}\n{order.Address}\n{order.Notes}\n" + string.Join("\n", order.Lines.Select(l => $"{l.Name} · {l.Quantity} × {l.Price:N2} = {l.Total:N2}")) + $"\nTotal: {order.Total:N2}\nDeposits: {store.OrderDeposits(order.Id):N2}\nInvoice: {order.SaleNumber}\nTracking: {order.Tracking}\n\n" + string.Join("\n", store.Data.Business.OrderPayments.Where(p => p.OrderId == order.Id).Select(p => $"{p.Time:yyyy-MM-dd HH:mm} · {p.Method} · {p.Amount:N2} · {p.Reference}")));
    }
    private void LoadCustomerOrder(CustomerOrder order)
    {
        RequireEmptyCart(); if (order.Status is not ("Reserved" or "Picked" or "Ready")) throw new InvalidOperationException("Reserve this order before invoicing; it must not already be invoiced or cancelled.");
        ResetSale(); fulfillingOrderId = order.Id; cart.AddRange(order.Lines); SaleCustomer.SelectedValue = order.CustomerId; saleLocation.SelectedValue = order.LocationId; Discount.Text = order.Discount.ToString(); TaxRate.Text = order.TaxRate.ToString(); LockFulfillment(); RefreshCart(); MainTabs.SelectedIndex = 0;
        Status.Text = $"Fulfilling {order.Number}. Deposits applied: {store.OrderDeposits(order.Id):N2}";
    }
    private void PartialReturnEditor()
    {
        var sale = BusinessSelected<Sale>("Partial returns"); var e = Editor("Return items · " + sale.Number, 900); var reason = e.Text("Return reason");
        var entries = sale.Lines.Select(l => new ReturnEntry { ProductId = l.ProductId, Product = l.Name, Sold = l.Quantity, Returned = store.ReturnedQuantity(sale, l.ProductId) }).ToList();
        var grid = EditableGrid(entries, ("Product", "Product", true), ("Sold", "Sold", true), ("Returned", "Returned", true), ("Remaining", "Remaining", true), ("Return now", "ReturnNow", false));
        grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Restock", Binding = new Binding("Restock") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged } }); e.Fields.Children.Add(grid);
        Edit(e, () => { grid.CommitEdit(DataGridEditingUnit.Cell, true); grid.CommitEdit(DataGridEditingUnit.Row, true); var returned = store.ReturnLines(sale.Number, entries.Where(r => r.ReturnNow != 0).Select(r => new ReturnItem(r.ProductId, r.ReturnNow, r.Restock)), reason.Text); ShowDocument("Return · " + sale.Number, $"REFUND RECORD\n{sale.Number}\n{returned.Time:yyyy-MM-dd HH:mm}\nReason: {reason.Text}\nRefund: {returned.Amount:N2}\n" + string.Join("\n", store.RefundPayments(returned).Select(p => $"{p.Method} · {p.Amount:N2} · {p.Reference}"))); });
    }
    private void BuildCheckoutExtensions()
    {
        foreach (var method in new[] { "Credit", "GiftCard", "Split" }) Payment.Items.Add(new ComboBoxItem { Content = method });
        salesPayment.ItemsSource = new[] { "All", "Cash", "Card", "Credit", "GiftCard", "Split" }; salesPayment.SelectedIndex = 0;
        salesStatus.ItemsSource = new[] { "All", "Active", "Partial", "Returned" }; salesStatus.SelectedIndex = 0;
        var customerPanel = (StackPanel)SaleCustomer.Parent;
        var row = new DockPanel(); customerPanel.Children.Add(row);
        row.Children.Add(new TextBlock { Text = "Sell from", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4) });
        saleLocation = new ComboBox { ItemsSource = LocationChoices().ToList(), DisplayMemberPath = "Label", SelectedValuePath = "Id", SelectedValue = Store.CounterLocationId, Margin = new Thickness(4), Padding = new Thickness(6) }; row.Children.Add(saleLocation);
        saleLocation.SelectionChanged += (_, _) => { if (store != null) RefreshCatalog(); };
        Breakdown.TextWrapping = TextWrapping.Wrap;
    }
    private void LockFulfillment()
    {
        SaleCustomer.IsEnabled = !IsFulfillment; saleLocation.IsEnabled = !IsFulfillment;
        var fixedOrder = fulfillingOrderId != Guid.Empty; Discount.IsEnabled = !fixedOrder; TaxRate.IsEnabled = !fixedOrder; couponBox.IsEnabled = !fixedOrder; pointsBox.IsEnabled = !fixedOrder;
    }
    private void RefreshCustomerBusiness()
    {
        businessGrids["Customer credit"].ItemsSource = store.Data.Customers.Select(c => { var a = store.Data.Business.CreditAccounts.SingleOrDefault(a => a.CustomerId == c.Id); return new AccountRow(c.Id, c.Name, a?.Limit ?? 0, a?.TermsDays ?? 0, a?.Enabled ?? false, store.CustomerBalance(c.Id)); }).ToList();
        businessGrids["Gift cards"].ItemsSource = store.Data.Business.GiftCards.Select(g => new GiftRow(g.Code, g.Recipient, g.Expires, g.Active, store.GiftBalance(g.Code))).ToList();
        businessGrids["Layaway"].ItemsSource = store.Data.Business.Orders.Where(o => o.Kind == "Layaway").OrderByDescending(o => o.Created).ToList();
        businessGrids["Delivery orders"].ItemsSource = store.Data.Business.Orders.Where(o => o.Kind == "Delivery").OrderByDescending(o => o.Created).ToList();
        businessGrids["Partial returns"].ItemsSource = store.Data.Sales.Where(s => !store.FullyReturned(s)).OrderByDescending(s => s.Time).ToList();
        var location = saleLocation.SelectedValue; saleLocation.ItemsSource = LocationChoices().ToList(); saleLocation.SelectedValue = location ?? Store.CounterLocationId;
    }
}
