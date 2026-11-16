using System.Text.Json;

namespace CounterPoint.Core;

public sealed class Product
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Barcode { get; set; } = "";
    public string Category { get; set; } = "General";
    public decimal Price { get; set; }
    public int Stock { get; set; }
    public int ReorderLevel { get; set; } = 5;
    public bool Archived { get; set; }
    public string Kind { get; set; } = "Stock";
    public decimal UnitCost { get; set; }
    public Guid PreferredSupplierId { get; set; }
    public int LeadDays { get; set; } = 7;
    public int SafetyStock { get; set; } = 3;
}
public sealed record SaleLine(Guid ProductId, string Name, decimal Price, int Quantity)
{
    public decimal UnitCost { get; init; }
    public List<StockPart> Components { get; init; } = [];
    public decimal Total => Price * Quantity;
}
public sealed record Sale(string Number, DateTimeOffset Time, List<SaleLine> Lines, decimal Discount, decimal TaxRate, string Payment, decimal Tendered)
{
    public Guid? CustomerId { get; init; }
    public string CustomerName { get; init; } = "Walk-in";
    public string ShopName { get; init; } = "COUNTERPOINT";
    public string ShopAddress { get; init; } = "";
    public string Currency { get; init; } = "";
    public string Footer { get; init; } = "Thank you for shopping with us!";
    public string CouponCode { get; init; } = "";
    public int PointsEarned { get; init; }
    public int PointsRedeemed { get; init; }
    public Guid? ShiftId { get; init; }
    public Guid LocationId { get; init; } = Store.CounterLocationId;
    public Guid EmployeeId { get; init; }
    public string EmployeeName { get; init; } = "Owner";
    public decimal CommissionRate { get; init; }
    public DateTime DueDate { get; init; }
    public Guid OrderId { get; init; }
    public Guid JobId { get; init; }
    public List<PaymentPart> Payments { get; init; } = [];
    public List<PaymentPart> DepositPayments { get; init; } = [];
    public decimal Subtotal => Lines.Sum(x => x.Total);
    public decimal Tax => decimal.Round((Subtotal - Discount) * TaxRate / 100, 2, MidpointRounding.AwayFromZero);
    public decimal Total => Subtotal - Discount + Tax;
    public decimal Change => Payments.Count > 0 ? (Payments.Any(p => p.Method == "Cash") ? Tendered - Payments.Where(p => p.Method == "Cash").Sum(p => p.Amount) : 0) : Payment == "Cash" ? Tendered - Total : 0;
}
public sealed class StoreData
{
    public List<Product> Products { get; set; } = [];
    public List<Sale> Sales { get; set; } = [];
    public List<Customer> Customers { get; set; } = [];
    public List<Supplier> Suppliers { get; set; } = [];
    public List<StockMovement> Movements { get; set; } = [];
    public List<Purchase> Purchases { get; set; } = [];
    public List<Expense> Expenses { get; set; } = [];
    public List<SaleReturn> Returns { get; set; } = [];
    public ShopSettings Settings { get; set; } = new();
    public List<SavedCart> SavedCarts { get; set; } = [];
    public List<Coupon> Coupons { get; set; } = [];
    public List<CashShift> Shifts { get; set; } = [];
    public List<CashEntry> CashEntries { get; set; } = [];
    public BusinessData Business { get; set; } = new();
}
public sealed partial class Store
{
    private readonly string path;
    public StoreData Data { get; private set; }
    public Store(string path)
    {
        this.path = path;
        Data = File.Exists(path) ? JsonSerializer.Deserialize<StoreData>(File.ReadAllText(path)) ?? throw new InvalidDataException("Invalid store file.") : new StoreData();
    }
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(Data, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, true);
    }
    public void SaveProduct(Product product)
    {
        if (string.IsNullOrWhiteSpace(product.Name) || string.IsNullOrWhiteSpace(product.Barcode) || product.Price < 0 || product.Stock < 0 || product.ReorderLevel < 0)
            throw new InvalidOperationException("Enter a name, barcode, non-negative price and stock.");
        if (Data.Products.Any(x => x.Id != product.Id && x.Barcode.Equals(product.Barcode, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("That barcode already belongs to another product.");
        Require(product.Kind is "Stock" or "Service" or "Bundle" && product.UnitCost >= 0, "Check product type and cost.");
        Require(product.Stock >= ReservedStock(product.Id, CounterLocationId), "Stock cannot fall below reserved quantities.");
        var old = Data.Products.FirstOrDefault(x => x.Id == product.Id);
        Require(old == null || old.Kind == product.Kind, "Change product type through product configuration.");
        Commit(() => { if (old != null) Data.Products.Remove(old); Data.Products.Add(product); });
    }
    public Sale Checkout(IEnumerable<SaleLine> lines, decimal discount, decimal taxRate, string payment, decimal tendered, Guid? customerId = null, string couponCode = "", int points = 0, Guid? savedCartId = null, List<PaymentPart>? payments = null, Guid locationId = default, Guid orderId = default, Guid jobId = default)
    {
        locationId = NormalizeLocation(locationId);
        var source = lines.ToList();
        var order = orderId == Guid.Empty ? null : Data.Business.Orders.SingleOrDefault(o => o.Id == orderId);
        var job = jobId == Guid.Empty ? null : Data.Business.Jobs.SingleOrDefault(j => j.Id == jobId);
        Require(orderId == Guid.Empty || (order != null && IsReserved(order)), "Select an open reserved order.");
        Require(jobId == Guid.Empty || (job != null && job.Status == "Ready"), "Service job must be ready for invoicing.");
        Require(order == null || job == null, "Invoice an order or a service job, not both.");
        bool Matches(IEnumerable<SaleLine> expected) => source.OrderBy(l => l.ProductId).Select(l => (l.ProductId, l.Quantity, l.Price)).SequenceEqual(expected.OrderBy(l => l.ProductId).Select(l => (l.ProductId, l.Quantity, l.Price)));
        if (order != null) Require(customerId == order.CustomerId && locationId == order.LocationId && Matches(order.Lines) && discount == order.Discount && taxRate == order.TaxRate && points == 0 && string.IsNullOrWhiteSpace(couponCode), "Keep the order's customer, location, items, prices, discount and tax unchanged at fulfillment.");
        if (job != null) Require(customerId == job.CustomerId && locationId == job.LocationId && Matches(ServiceJobLines(job.Id)), "Keep the service job's customer, location, parts and agreed labor charge unchanged.");
        var items = SnapshotLines(source, customerId, order == null && job == null);
        CheckDemand(items, locationId, orderId, jobId);
        if (discount < 0 || discount > items.Sum(x => x.Total) || taxRate < 0 || taxRate > 100 || payment is not ("Cash" or "Card" or "Split" or "Credit" or "GiftCard"))
            throw new InvalidOperationException("Check discount, tax and payment method.");
        var customer = Data.Customers.FirstOrDefault(c => c.Id == customerId);
        if (customerId != null && customer == null) throw new InvalidOperationException("Customer no longer exists.");
        var reduction = CalculateDiscount(items.Sum(x => x.Total), discount, couponCode, points, customerId);
        var saved = savedCartId == null ? null : Data.SavedCarts.SingleOrDefault(c => c.Id == savedCartId);
        Require(savedCartId == null || (saved != null && (saved.Kind != "Quote" || saved.ExpiresOn >= DateTime.Today)), "This saved cart is missing or the quotation has expired.");
        var activeShift = Data.Shifts.SingleOrDefault(s => s.Closed == null);
        var deposits = order == null ? [] : Data.Business.OrderPayments.Where(p => p.OrderId == order.Id).GroupBy(p => p.Method).Select(g => new PaymentPart(g.Key, g.Sum(p => p.Amount), order.Number)).Where(p => p.Amount > 0).ToList();
        var sale = new Sale($"CP-{Data.Sales.Count + 1:000000}", DateTimeOffset.Now, items, reduction, taxRate, payment, tendered)
        { CustomerId = customerId, CustomerName = customer?.Name ?? "Walk-in", ShopName = Data.Settings.Name, ShopAddress = Data.Settings.Address, Currency = Data.Settings.Currency, Footer = Data.Settings.Footer, CouponCode = couponCode.Trim().ToUpperInvariant(), PointsRedeemed = points, ShiftId = activeShift?.Id, LocationId = locationId, EmployeeId = CurrentEmployee?.Id ?? Guid.Empty, EmployeeName = CurrentEmployee?.Name ?? "Owner", CommissionRate = CurrentEmployee?.CommissionRate ?? 0, OrderId = orderId, JobId = jobId, DepositPayments = deposits, DueDate = DateTime.Today.AddDays(Data.Business.CreditAccounts.SingleOrDefault(a => a.CustomerId == customerId)?.TermsDays ?? 0) };
        var due = sale.Total - deposits.Sum(p => p.Amount); Require(due >= 0, "Deposits exceed the order total.");
        payments ??= due == 0 ? [] : [new(payment, due)];
        ValidatePayments(payments, due, tendered, customerId);
        payments = payments.Select(p => p.Method == "GiftCard" ? p with { Reference = p.Reference.Trim().ToUpperInvariant() } : p).ToList();
        var methods = deposits.Concat(payments).Select(p => p.Method).Distinct().ToList();
        sale = sale with { Payments = payments, Payment = methods.Count == 1 ? methods[0] : methods.Count > 1 ? "Split" : payment };
        sale = sale with { PointsEarned = customerId == null ? 0 : checked((int)decimal.Floor(sale.Total / 100)) };
        Commit(() =>
        {
            foreach (var part in Demand(items)) ChangeStock(part.ProductId, locationId, -part.Quantity, "Sale " + sale.Number);
            Data.Sales.Add(sale);
            if (saved != null) Data.SavedCarts.Remove(saved);
            foreach (var gift in payments.Where(p => p.Method == "GiftCard")) Data.Business.GiftEntries.Add(new(Guid.NewGuid(), sale.Time, gift.Reference, "Redeem", -gift.Amount, sale.Number, "GiftCard", ActiveShiftId));
            if (order != null) Data.Business.Orders[Data.Business.Orders.IndexOf(order)] = order with { Status = "Invoiced", SaleNumber = sale.Number };
            if (job != null) Data.Business.Jobs[Data.Business.Jobs.IndexOf(job)] = job with { Status = "Invoiced", SaleNumber = sale.Number };
        });
        return sale;
    }
    public static string Receipt(Sale sale) => $"{sale.ShopName}\n{sale.ShopAddress}\nSHOP RECEIPT {sale.Currency}\n{sale.Number}\n{sale.Time:yyyy-MM-dd HH:mm}\nCustomer: {sale.CustomerName}\n--------------------------------\n" +
        string.Join("\n", sale.Lines.Select(x => $"{x.Name}\n  {x.Quantity} x {x.Price:0.00}    {x.Total:0.00}")) +
        $"\n--------------------------------\nSubtotal: {sale.Subtotal:0.00}\nDiscount: {sale.Discount:0.00}\nCoupon: {sale.CouponCode}\nPoints used / earned: {sale.PointsRedeemed} / {sale.PointsEarned}\nTax ({sale.TaxRate:0.##}%): {sale.Tax:0.00}\nTOTAL: {sale.Total:0.00}\nPayment: {sale.Payment}\nReceived: {(sale.Payment == "Card" ? sale.Total : sale.Tendered):0.00}\nChange: {sale.Change:0.00}\n\n{sale.Footer}";
}
