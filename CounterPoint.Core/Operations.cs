using System.Text.Json;

namespace CounterPoint.Core;
public sealed record Customer(Guid Id, string Name, string Phone, string Email);
public sealed record Supplier(Guid Id, string Name, string Phone, string Address);
public sealed record StockMovement(DateTimeOffset Time, Guid ProductId, string Product, int Quantity, string Reason);
public sealed record Purchase(string Number, DateTimeOffset Time, Guid SupplierId, string Supplier, string Reference, Guid ProductId, string Product, int Quantity, decimal UnitCost)
{
    public decimal Total => Quantity * UnitCost;
}
public sealed record Expense(DateTimeOffset Time, string Category, string Description, decimal Amount);
public sealed record SaleReturn(string SaleNumber, DateTimeOffset Time, string Reason, decimal Amount, string Payment);
public sealed record ShopSettings
{
    public string Name { get; init; } = "COUNTERPOINT";
    public string Address { get; init; } = "";
    public string Currency { get; init; } = "";
    public decimal DefaultTax { get; init; }
    public string Footer { get; init; } = "Thank you for shopping with us!";
}
public sealed record DailyReport(DateTime Date, int Sales, decimal Gross, decimal Refunds, decimal Net, decimal Expenses, decimal Purchases, decimal CashSales, decimal CardSales);
public sealed partial class Store
{
    private void Commit(Action change)
    {
        var before = JsonSerializer.Serialize(Data);
        try { change(); Save(); }
        catch { Data = JsonSerializer.Deserialize<StoreData>(before)!; throw; }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    public void SaveCustomer(Customer customer)
    {
        Require(!string.IsNullOrWhiteSpace(customer.Name), "Enter the customer name.");
        Commit(() => { Data.Customers.RemoveAll(c => c.Id == customer.Id); Data.Customers.Add(customer); });
    }
    public void SaveSupplier(Supplier supplier)
    {
        Require(!string.IsNullOrWhiteSpace(supplier.Name), "Enter the supplier name.");
        Commit(() => { Data.Suppliers.RemoveAll(c => c.Id == supplier.Id); Data.Suppliers.Add(supplier); });
    }
    public void AdjustStock(Guid productId, int change, string reason)
    {
        var product = Data.Products.Single(p => p.Id == productId);
        Require(change != 0 && (long)product.Stock + change >= 0 && (long)product.Stock + change <= int.MaxValue && !string.IsNullOrWhiteSpace(reason), "Enter a nonzero quantity and reason. Stock cannot become negative.");
        Commit(() => { product.Stock += change; Data.Movements.Add(new(DateTimeOffset.Now, productId, product.Name, change, reason)); });
    }
    public void SetReorderLevel(Guid productId, int level)
    {
        Require(level >= 0, "Reorder level cannot be negative.");
        Commit(() => Data.Products.Single(p => p.Id == productId).ReorderLevel = level);
    }
    public IReadOnlyList<Product> LowStock() => Data.Products.Where(p => p.Stock <= p.ReorderLevel).OrderBy(p => p.Stock).ToList();
    public void ReceivePurchase(Guid supplierId, Guid productId, int quantity, decimal cost, string reference)
    {
        var product = Data.Products.Single(p => p.Id == productId);
        var supplier = Data.Suppliers.Single(s => s.Id == supplierId);
        Require(quantity > 0 && cost >= 0 && (long)product.Stock + quantity <= int.MaxValue && !string.IsNullOrWhiteSpace(reference), "Enter a positive quantity, non-negative unit cost and supplier reference.");
        Require(!Data.Purchases.Any(p => p.SupplierId == supplierId && p.Reference.Equals(reference, StringComparison.OrdinalIgnoreCase) && p.ProductId == productId), "This reference and product have already been received from this supplier.");
        Commit(() =>
        {
            var purchase = new Purchase($"PO-{Data.Purchases.Count + 1:000000}", DateTimeOffset.Now, supplierId, supplier.Name, reference, productId, product.Name, quantity, cost);
            _ = purchase.Total;
            Data.Purchases.Add(purchase); product.Stock += quantity;
            Data.Movements.Add(new(DateTimeOffset.Now, productId, product.Name, quantity, $"Received {purchase.Number} / {reference}"));
        });
    }
    public void AddExpense(string category, string description, decimal amount)
    {
        Require(!string.IsNullOrWhiteSpace(category) && !string.IsNullOrWhiteSpace(description) && amount > 0, "Enter a category, description and positive expense amount.");
        Commit(() => Data.Expenses.Add(new(DateTimeOffset.Now, category, description, amount)));
    }
    public SaleReturn ReturnSale(string saleNumber, string reason)
    {
        var sale = Data.Sales.Single(s => s.Number == saleNumber);
        Require(!Data.Returns.Any(r => r.SaleNumber == saleNumber), "This sale has already been returned.");
        Require(!string.IsNullOrWhiteSpace(reason), "Enter a reason for the return.");
        foreach (var line in sale.Lines) Require((long)Data.Products.Single(p => p.Id == line.ProductId).Stock + line.Quantity <= int.MaxValue, "Restocking would exceed the stock limit.");
        var result = new SaleReturn(saleNumber, DateTimeOffset.Now, reason, sale.Total, sale.Payment);
        Commit(() =>
        {
            foreach (var line in sale.Lines)
            {
                Data.Products.Single(p => p.Id == line.ProductId).Stock += line.Quantity;
                Data.Movements.Add(new(result.Time, line.ProductId, line.Name, line.Quantity, $"Return {saleNumber}: {reason}"));
            }
            Data.Returns.Add(result);
        });
        return result;
    }
    public void SaveSettings(ShopSettings settings)
    {
        Require(!string.IsNullOrWhiteSpace(settings.Name) && settings.DefaultTax >= 0 && settings.DefaultTax <= 100, "Enter a shop name and tax rate between 0 and 100.");
        Commit(() => Data.Settings = settings);
    }
    public IReadOnlyList<DailyReport> Report(DateTime from, DateTime to)
    {
        Require(to.Date >= from.Date && (to.Date - from.Date).TotalDays <= 366, "Choose a date range of up to 367 days.");
        return Enumerable.Range(0, (to.Date - from.Date).Days + 1).Select(offset =>
        {
            var day = from.Date.AddDays(offset);
            var sales = Data.Sales.Where(s => s.Time.LocalDateTime.Date == day).ToList();
            var returns = Data.Returns.Where(r => r.Time.LocalDateTime.Date == day).Sum(r => r.Amount);
            var gross = sales.Sum(s => s.Total);
            return new DailyReport(day, sales.Count, gross, returns, gross - returns,
                Data.Expenses.Where(e => e.Time.LocalDateTime.Date == day).Sum(e => e.Amount),
                Data.Purchases.Where(p => p.Time.LocalDateTime.Date == day).Sum(p => p.Total),
                sales.Where(s => s.Payment == "Cash").Sum(s => s.Total), sales.Where(s => s.Payment == "Card").Sum(s => s.Total));
        }).ToList();
    }
}
