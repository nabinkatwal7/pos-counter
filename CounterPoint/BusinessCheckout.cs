using System.Windows.Controls;
using CounterPoint.Core;

namespace CounterPoint;
public partial class MainWindow
{
    private bool businessReady;
    private void RefreshCatalog()
    {
        if (store == null) return;
        var query = Search.Text.Trim();
        var products = store.Data.Products.Where(p => !p.Archived && $"{p.Name} {p.Category} {p.Barcode}".Contains(query, StringComparison.OrdinalIgnoreCase)).OrderBy(p => p.Name).ToList();
        foreach (var p in products)
        {
            try { p.Available = store.SellableQuantity(p.Id, SelectedLocation, fulfillingOrderId, fulfillingJobId); }
            catch (InvalidOperationException) { p.Available = 0; }
        }
        ProductsGrid.ItemsSource = products;
    }
    private void RepriceCart()
    {
        if (store == null || IsFulfillment) return;
        var customer = SaleCustomer.SelectedValue as Guid?;
        for (var i = 0; i < cart.Count; i++) cart[i] = cart[i] with { Price = store.SellingPrice(cart[i].ProductId, cart[i].Quantity, customer) };
    }
    private decimal CurrentDue()
    {
        var subtotal = cart.Sum(l => l.Total); var discount = store.CalculateDiscount(subtotal, Money(Discount), couponBox.Text, Integer(pointsBox.Text), SaleCustomer.SelectedValue as Guid?);
        var total = new Sale("", DateTimeOffset.Now, cart, discount, Money(TaxRate), "Card", 0).Total;
        return total - (fulfillingOrderId == Guid.Empty ? 0 : store.OrderDeposits(fulfillingOrderId));
    }
    private void ExecuteCheckout(string method, List<PaymentPart>? portions, decimal cashReceived)
    {
        var sale = store.Checkout(cart, Money(Discount), Money(TaxRate), method, cashReceived, SaleCustomer.SelectedValue as Guid?, couponBox.Text, Integer(pointsBox.Text), resumedCartId, portions, SelectedLocation, fulfillingOrderId, fulfillingJobId);
        ResetSale(); Refresh(); Status.Text = $"{sale.Number} saved · Change {sale.Change:N2}"; ShowReceipt(sale);
    }
    private void TenderEditor(string method)
    {
        var due = CurrentDue(); var e = Editor(method == "GiftCard" ? "Gift card checkout" : "Split payment");
        e.Note($"Amount due now: {due:N2}. Portions must total this amount exactly. External card-terminal charges are recorded here after processing.");
        var cash = e.Text("Cash portion", "0"); var card = e.Text("Card portion", "0"); var credit = e.Text("Customer credit portion", "0"); var gift = e.Text("Gift card portion", method == "GiftCard" ? due.ToString() : "0"); var code = e.Text("Gift card code"); var received = e.Text("Actual cash received", "0");
        if (method == "GiftCard") { cash.IsEnabled = false; card.IsEnabled = false; credit.IsEnabled = false; }
        e.SaveButton.Content = "Complete sale";
        Edit(e, () =>
        {
            var values = new[] { new PaymentPart("Cash", Number(cash.Text)), new PaymentPart("Card", Number(card.Text)), new PaymentPart("Credit", Number(credit.Text)), new PaymentPart("GiftCard", Number(gift.Text), code.Text) };
            if (values.Any(p => p.Amount < 0)) throw new InvalidOperationException("Payment portions cannot be negative.");
            ExecuteCheckout(method, values.Where(p => p.Amount > 0).ToList(), Number(received.Text));
        });
    }
}
