namespace CounterPoint.Core;

public sealed partial class Store
{
    public PurchaseOrder CreatePurchaseOrder(Guid supplierId, Guid location, DateTime expected)
    {
        var supplier = Data.Suppliers.Single(s => s.Id == supplierId); location = NormalizeLocation(location); ValidateLocation(location);
        Require(expected.Date >= DateTime.Today, "Expected delivery cannot be in the past.");
        var order = new PurchaseOrder($"BUY-{Data.Business.PurchaseOrders.Count + 1:000000}", supplierId, supplier.Name, location, DateTime.Today, expected.Date, "Draft", []);
        Commit(() => Data.Business.PurchaseOrders.Add(order)); return order;
    }
    public void SetPurchaseOrderLine(string number, Guid productId, int quantity, decimal cost)
    {
        var order = Data.Business.PurchaseOrders.Single(o => o.Number == number); var product = Data.Products.Single(p => p.Id == productId);
        Require(order.Status == "Draft" && product.Kind == "Stock" && !product.Archived && quantity >= 0 && cost >= 0, "Edit draft orders only; use physical products, non-negative quantities and costs. Zero quantity removes a line.");
        Commit(() => { order.Lines.RemoveAll(l => l.ProductId == productId); if (quantity > 0) order.Lines.Add(new(productId, product.Name, quantity, cost)); });
    }
    public void ReplacePurchaseOrderLines(string number, IEnumerable<OrderItem> lines)
    {
        var order = Data.Business.PurchaseOrders.Single(o => o.Number == number); var rows = lines.ToList();
        Require(order.Status == "Draft" && rows.Select(l => l.ProductId).Distinct().Count() == rows.Count && rows.All(l => l.Quantity > 0 && l.UnitPrice >= 0 && Data.Products.Any(p => p.Id == l.ProductId && p.Kind == "Stock" && !p.Archived)), "Only draft orders can be edited; select distinct physical products with positive quantities and non-negative costs.");
        var snapshot = rows.Select(l => l with { Name = Data.Products.Single(p => p.Id == l.ProductId).Name }).ToList();
        Commit(() => Data.Business.PurchaseOrders[Data.Business.PurchaseOrders.IndexOf(order)] = order with { Lines = snapshot });
    }
    public void SetPurchaseOrderStatus(string number, string status)
    {
        var order = Data.Business.PurchaseOrders.Single(o => o.Number == number);
        Require((status == "Submitted" && order.Status == "Draft" && order.Lines.Count > 0) || (status == "Cancelled" && order.Status is "Draft" or "Submitted" or "Part received"), "Submit a nonempty draft, or cancel the unreceived remainder of an open order.");
        Commit(() => Data.Business.PurchaseOrders[Data.Business.PurchaseOrders.IndexOf(order)] = order with { Status = status });
    }
    public int ReceivedQuantity(string orderNumber, Guid productId) => Data.Purchases.Where(p => p.OrderNumber == orderNumber && p.ProductId == productId).Sum(p => p.Quantity);
    public void ReceivePurchaseOrder(string number, string deliveryReference, IEnumerable<(Guid ProductId, int Quantity)> quantities)
    {
        var order = Data.Business.PurchaseOrders.Single(o => o.Number == number); var rows = quantities.ToList();
        Require(order.Status is "Submitted" or "Part received" && !string.IsNullOrWhiteSpace(deliveryReference) && rows.Count > 0 && rows.Select(r => r.ProductId).Distinct().Count() == rows.Count, "Select an open submitted order, enter a delivery reference and distinct receipt lines.");
        ValidateLocation(order.LocationId);
        foreach (var row in rows)
        {
            var line = order.Lines.SingleOrDefault(l => l.ProductId == row.ProductId);
            Require(line != null && row.Quantity > 0 && row.Quantity <= line.Quantity - ReceivedQuantity(number, row.ProductId), "Receipt quantity exceeds the outstanding order quantity.");
            Require(!Data.Purchases.Any(p => p.SupplierId == order.SupplierId && p.Reference.Equals(deliveryReference.Trim(), StringComparison.OrdinalIgnoreCase) && p.ProductId == row.ProductId), "This supplier delivery reference and product were already received.");
        }
        Commit(() =>
        {
            foreach (var row in rows)
            {
                var line = order.Lines.Single(l => l.ProductId == row.ProductId);
                var purchase = new Purchase($"PO-{Data.Purchases.Count + 1:000000}", DateTimeOffset.Now, order.SupplierId, order.Supplier, deliveryReference.Trim(), row.ProductId, line.Name, row.Quantity, line.UnitPrice) { LocationId = order.LocationId, OrderNumber = number, DueDate = DateTime.Today.AddDays(30) };
                Data.Purchases.Add(purchase); ReceiveCostedStock(row.ProductId, order.LocationId, row.Quantity, line.UnitPrice, "Receive " + number + " / " + deliveryReference);
            }
            var status = order.Lines.All(l => ReceivedQuantity(number, l.ProductId) >= l.Quantity) ? "Received" : "Part received";
            Data.Business.PurchaseOrders[Data.Business.PurchaseOrders.IndexOf(order)] = order with { Status = status };
        });
    }
    public void ReturnToSupplier(string purchaseNumber, int quantity, string reason)
    {
        var purchase = Data.Purchases.Single(p => p.Number == purchaseNumber);
        var already = Data.Business.SupplierReturns.Where(r => r.PurchaseNumber == purchaseNumber).Sum(r => r.Quantity);
        Require(quantity > 0 && quantity <= purchase.Quantity - already && quantity <= StockAt(purchase.ProductId, purchase.LocationId) - ReservedStock(purchase.ProductId, purchase.LocationId) && !string.IsNullOrWhiteSpace(reason), "Return a positive quantity within the original receipt and available stock; enter a reason.");
        Commit(() =>
        {
            ChangeStock(purchase.ProductId, purchase.LocationId, -quantity, "Supplier return: " + reason);
            Data.Business.SupplierReturns.Add(new($"SR-{Data.Business.SupplierReturns.Count + 1:000000}", DateTimeOffset.Now, purchaseNumber, purchase.SupplierId, purchase.ProductId, quantity, Round(quantity * purchase.UnitCost), purchase.LocationId, reason));
        });
    }
    public decimal SupplierInvoiceBalance(string purchaseNumber)
    {
        var p = Data.Purchases.Single(p => p.Number == purchaseNumber);
        return p.Total - Data.Business.SupplierReturns.Where(r => r.PurchaseNumber == purchaseNumber).Sum(r => r.Credit) - Data.Business.SupplierPayments.Where(x => x.PurchaseNumber == purchaseNumber).Sum(x => x.Amount);
    }
    public void PaySupplier(string purchaseNumber, decimal amount, string method, string reference)
    {
        var p = Data.Purchases.Single(p => p.Number == purchaseNumber);
        Require(amount > 0 && amount <= SupplierInvoiceBalance(purchaseNumber) && method is "Cash" or "Card" && !string.IsNullOrWhiteSpace(reference), "Enter a positive payment within the invoice balance and a reference.");
        Require(!Data.Business.SupplierPayments.Any(x => x.SupplierId == p.SupplierId && x.Reference.Equals(reference.Trim(), StringComparison.OrdinalIgnoreCase)), "Supplier payment reference has already been recorded.");
        Commit(() => { Data.Business.SupplierPayments.Add(new(DateTimeOffset.Now, p.SupplierId, purchaseNumber, amount, method, reference.Trim())); DrawerOut(amount, method, "Supplier payment: " + reference); });
    }
    public void ReceiveSupplierRefund(string purchaseNumber, decimal amount, string method, string reference)
    {
        var p = Data.Purchases.Single(p => p.Number == purchaseNumber);
        Require(amount > 0 && amount <= -SupplierInvoiceBalance(purchaseNumber) && method is "Cash" or "Card" && !string.IsNullOrWhiteSpace(reference), "Enter a positive refund within the supplier credit balance and a reference.");
        Require(!Data.Business.SupplierPayments.Any(x => x.SupplierId == p.SupplierId && x.Reference.Equals(reference.Trim(), StringComparison.OrdinalIgnoreCase)), "This supplier payment/refund reference already exists.");
        Commit(() => { Data.Business.SupplierPayments.Add(new(DateTimeOffset.Now, p.SupplierId, purchaseNumber, -amount, method, reference.Trim())); DrawerOut(-amount, method, "Supplier refund: " + reference); });
    }
    public IReadOnlyList<ReplenishmentRow> ReplenishmentPlan(int lookbackDays, int coverageDays, Guid location)
    {
        Require(lookbackDays >= 1 && lookbackDays <= 365 && coverageDays >= 1 && coverageDays <= 365, "Demand lookback and coverage must be between 1 and 365 days.");
        location = NormalizeLocation(location); ValidateLocation(location); var since = DateTime.Today.AddDays(1 - lookbackDays);
        var demand = Data.Sales.Where(s => s.LocationId == location && s.Time.LocalDateTime.Date >= since).SelectMany(s => Demand(s.Lines)).GroupBy(p => p.ProductId).ToDictionary(g => g.Key, g => g.Sum(p => p.Quantity));
        return Data.Products.Where(p => p.Kind == "Stock" && !p.Archived).Select(p =>
        {
            var daily = (decimal)demand.GetValueOrDefault(p.Id) / lookbackDays;
            var inbound = Data.Business.PurchaseOrders.Where(o => o.LocationId == location && o.Status is "Draft" or "Submitted" or "Part received").SelectMany(o => o.Lines.Where(l => l.ProductId == p.Id).Select(l => l.Quantity - ReceivedQuantity(o.Number, p.Id))).Sum();
            var available = StockAt(p.Id, location) - ReservedStock(p.Id, location);
            var target = Math.Max(p.ReorderLevel, checked((int)decimal.Ceiling(daily * (p.LeadDays + coverageDays))) + p.SafetyStock);
            return new ReplenishmentRow(p.Id, p.Name, p.PreferredSupplierId, Data.Suppliers.SingleOrDefault(s => s.Id == p.PreferredSupplierId)?.Name ?? "Assign supplier", available, inbound, Round(daily), Math.Max(0, target - available - inbound), p.UnitCost);
        }).Where(r => r.Suggested > 0).OrderBy(r => r.Supplier).ThenBy(r => r.Product).ToList();
    }
    public IReadOnlyList<PurchaseOrder> CreateReplenishmentOrders(int lookbackDays, int coverageDays, Guid location)
    {
        location = NormalizeLocation(location); var plan = ReplenishmentPlan(lookbackDays, coverageDays, location);
        Require(plan.Count > 0 && plan.All(r => Data.Suppliers.Any(s => s.Id == r.SupplierId)), "There are no shortages, or some suggested products need a preferred supplier.");
        var orders = new List<PurchaseOrder>();
        Commit(() =>
        {
            foreach (var group in plan.GroupBy(r => r.SupplierId))
            {
                var order = new PurchaseOrder($"BUY-{Data.Business.PurchaseOrders.Count + 1:000000}", group.Key, group.First().Supplier, location, DateTime.Today, DateTime.Today.AddDays(group.Max(r => Data.Products.Single(p => p.Id == r.ProductId).LeadDays)), "Draft", group.Select(r => new OrderItem(r.ProductId, r.Product, r.Suggested, r.UnitCost)).ToList());
                Data.Business.PurchaseOrders.Add(order); orders.Add(order);
            }
        }); return orders;
    }
}
