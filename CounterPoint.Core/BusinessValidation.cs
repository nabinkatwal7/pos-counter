namespace CounterPoint.Core;

public sealed partial class Store
{
    private static void ValidateBusinessBackup(StoreData data)
    {
        var b = data.Business;
        static bool Unique<T, TKey>(IEnumerable<T> rows, Func<T, TKey> key) => rows.Select(key).Distinct().Count() == rows.Count();
        bool Product(Guid id) => data.Products.Any(p => p.Id == id);
        bool Customer(Guid id) => data.Customers.Any(c => c.Id == id);
        bool Location(Guid id) => b.Locations.Any(l => l.Id == id);
        Require(Unique(b.Locations, l => l.Id) && b.Locations.Any(l => l.Id == CounterLocationId && l.Active) && b.Locations.All(l => !string.IsNullOrWhiteSpace(l.Name)), "Backup has invalid stock locations.");
        Require(Unique(b.LocationStocks, s => (s.LocationId, s.ProductId)) && b.LocationStocks.All(s => Location(s.LocationId) && s.LocationId != CounterLocationId && Product(s.ProductId) && s.Quantity >= 0), "Backup has inconsistent location balances.");
        Require(data.Products.All(p => p.UnitCost >= 0 && p.Kind is "Stock" or "Service" or "Bundle" && (p.Kind == "Stock" || p.Stock == 0)), "Backup has invalid product types or costs.");
        Require(Unique(b.Bundles, x => x.ProductId) && b.Bundles.All(x => data.Products.Any(p => p.Id == x.ProductId && p.Kind == "Bundle") && x.Parts.Count > 0 && Unique(x.Parts, p => p.ProductId) && x.Parts.All(p => p.Quantity > 0 && data.Products.Any(s => s.Id == p.ProductId && s.Kind == "Stock"))), "Backup has invalid bundle recipes.");
        Require(Unique(b.CreditAccounts, a => a.CustomerId) && b.CreditAccounts.All(a => Customer(a.CustomerId) && a.Limit >= 0 && a.TermsDays >= 0 && a.TermsDays <= 365), "Backup has invalid customer credit accounts.");
        Require(b.AccountPayments.All(p => Customer(p.CustomerId) && p.Amount > 0 && p.Method is "Cash" or "Card"), "Backup has invalid account payments.");
        Require(Unique(b.GiftCards, c => c.Code) && b.GiftCards.All(c => !string.IsNullOrWhiteSpace(c.Code)) && b.GiftEntries.All(e => b.GiftCards.Any(c => c.Code == e.Code)), "Backup has inconsistent gift card entries.");
        Require(Unique(b.Orders, o => o.Id) && Unique(b.Orders, o => o.Number) && b.Orders.All(o => Customer(o.CustomerId) && Location(o.LocationId) && o.Kind is "Layaway" or "Delivery" && o.Lines.Count > 0 && Unique(o.Lines, l => l.ProductId) && o.Lines.All(l => Product(l.ProductId) && l.Quantity > 0 && l.Price >= 0) && o.Discount >= 0 && o.Discount <= o.Lines.Sum(l => l.Total) && o.TaxRate >= 0 && o.TaxRate <= 100), "Backup has invalid customer orders.");
        Require(b.OrderPayments.All(p => b.Orders.Any(o => o.Id == p.OrderId) && p.Method is "Cash" or "Card") && b.Orders.All(o => { var paid = b.OrderPayments.Where(p => p.OrderId == o.Id).Sum(p => p.Amount); return paid >= 0 && paid <= o.Total; }), "Backup has inconsistent order deposits.");
        Require(Unique(b.PurchaseOrders, p => p.Number) && b.PurchaseOrders.All(p => data.Suppliers.Any(s => s.Id == p.SupplierId) && Location(p.LocationId) && Unique(p.Lines, l => l.ProductId) && p.Lines.All(l => Product(l.ProductId) && l.Quantity > 0 && l.UnitPrice >= 0)), "Backup has invalid purchase orders.");
        Require(b.SupplierReturns.All(r => r.Quantity > 0 && r.Credit >= 0 && data.Purchases.Any(p => p.Number == r.PurchaseNumber && p.ProductId == r.ProductId && p.SupplierId == r.SupplierId)) && data.Purchases.All(p => b.SupplierReturns.Where(r => r.PurchaseNumber == p.Number).Sum(r => r.Quantity) <= p.Quantity), "Backup has invalid supplier returns.");
        Require(b.SupplierPayments.All(p => data.Purchases.Any(r => r.Number == p.PurchaseNumber && r.SupplierId == p.SupplierId) && p.Method is "Cash" or "Card"), "Backup has invalid supplier payments.");
        Require(Unique(b.Employees, e => e.Id) && Unique(b.Employees, e => e.Username.ToUpperInvariant()) && (b.Employees.Count == 0 || b.Employees.Any(e => e.Active && e.Role == "Administrator")) && b.Employees.All(e => e.Role is "Cashier" or "Manager" or "Administrator" && e.CommissionRate >= 0 && e.CommissionRate <= 100 && e.Salt.Length == 32 && e.PasswordHash.Length == 64 && e.Salt.All(Uri.IsHexDigit) && e.PasswordHash.All(Uri.IsHexDigit)), "Backup has invalid employees or no active administrator.");
        Require(b.Attendance.All(a => b.Employees.Any(e => e.Id == a.EmployeeId) && (a.ClockOut == null || a.ClockOut >= a.ClockIn)) && b.Attendance.Where(a => a.ClockOut == null).GroupBy(a => a.EmployeeId).All(g => g.Count() == 1), "Backup has invalid attendance entries.");
        Require(b.Jobs.All(j => Customer(j.CustomerId) && Location(j.LocationId) && (j.EmployeeId == Guid.Empty || b.Employees.Any(e => e.Id == j.EmployeeId)) && j.Parts.All(p => Product(p.ProductId) && p.Quantity > 0 && p.UnitPrice >= 0) && (j.Status == "Intake" || Product(j.LaborProductId))), "Backup has invalid service jobs.");
        Require(b.PriceRules.All(r => Product(r.ProductId) && (r.CustomerId == Guid.Empty || Customer(r.CustomerId)) && r.MinimumQuantity > 0 && r.UnitPrice >= 0 && r.Ends >= r.Starts), "Backup has invalid price books.");
        foreach (var sale in data.Sales)
        {
            Require(Location(sale.LocationId) && sale.Lines.All(l => l.UnitCost >= 0 && l.Components.All(c => Product(c.ProductId) && c.Quantity > 0 && c.UnitCost >= 0)), "Backup contains invalid sale stock snapshots.");
            var payments = sale.Payments.Concat(sale.DepositPayments).ToList();
            Require(payments.Count == 0 || (payments.All(p => p.Amount > 0 && p.Method is "Cash" or "Card" or "Credit" or "GiftCard") && payments.Sum(p => p.Amount) == sale.Total), "Backup has an unbalanced sale payment.");
            var returns = data.Returns.Where(r => r.SaleNumber == sale.Number).ToList();
            Require(returns.Sum(r => r.Amount) <= sale.Total && returns.All(r => r.Amount >= 0 && r.Lines.All(l => l.Quantity > 0 && sale.Lines.Any(s => s.ProductId == l.ProductId))), "Backup has invalid refunds.");
            foreach (var line in sale.Lines) Require(returns.Sum(r => r.Lines.Count == 0 ? line.Quantity : r.Lines.Where(l => l.ProductId == line.ProductId).Sum(l => l.Quantity)) <= line.Quantity, "Backup returns exceed sold quantities.");
        }
    }
}
