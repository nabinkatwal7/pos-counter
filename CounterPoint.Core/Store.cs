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
}
public sealed record SaleLine(Guid ProductId, string Name, decimal Price, int Quantity)
{
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
    public decimal Subtotal => Lines.Sum(x => x.Total);
    public decimal Tax => decimal.Round((Subtotal - Discount) * TaxRate / 100, 2, MidpointRounding.AwayFromZero);
    public decimal Total => Subtotal - Discount + Tax;
    public decimal Change => Payment == "Cash" ? Tendered - Total : 0;
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
        var old = Data.Products.FirstOrDefault(x => x.Id == product.Id);
        if (old != null) Data.Products.Remove(old);
        Data.Products.Add(product);
        try { Save(); } catch { Data.Products.Remove(product); if (old != null) Data.Products.Add(old); throw; }
    }
    public Sale Checkout(IEnumerable<SaleLine> lines, decimal discount, decimal taxRate, string payment, decimal tendered, Guid? customerId = null, string couponCode = "", int points = 0, Guid? savedCartId = null)
    {
        var items = lines.ToList();
        if (items.Count == 0 || items.Any(x => x.Quantity <= 0) || items.Select(x => x.ProductId).Distinct().Count() != items.Count)
            throw new InvalidOperationException("Add valid items to the cart.");
        foreach (var line in items)
        {
            var product = Data.Products.SingleOrDefault(x => x.Id == line.ProductId);
            if (product == null || product.Archived || product.Stock < line.Quantity || product.Price != line.Price)
                throw new InvalidOperationException($"Check current price and stock for {line.Name}.");
        }
        if (discount < 0 || discount > items.Sum(x => x.Total) || taxRate < 0 || taxRate > 100 || payment is not ("Cash" or "Card"))
            throw new InvalidOperationException("Check discount, tax and payment method.");
        var customer = Data.Customers.FirstOrDefault(c => c.Id == customerId);
        if (customerId != null && customer == null) throw new InvalidOperationException("Customer no longer exists.");
        var reduction = CalculateDiscount(items.Sum(x => x.Total), discount, couponCode, points, customerId);
        var saved = savedCartId == null ? null : Data.SavedCarts.SingleOrDefault(c => c.Id == savedCartId);
        Require(savedCartId == null || (saved != null && (saved.Kind != "Quote" || saved.ExpiresOn >= DateTime.Today)), "This saved cart is missing or the quotation has expired.");
        var activeShift = Data.Shifts.SingleOrDefault(s => s.Closed == null);
        var sale = new Sale($"CP-{Data.Sales.Count + 1:000000}", DateTimeOffset.Now, items, reduction, taxRate, payment, tendered)
        { CustomerId = customerId, CustomerName = customer?.Name ?? "Walk-in", ShopName = Data.Settings.Name, ShopAddress = Data.Settings.Address, Currency = Data.Settings.Currency, Footer = Data.Settings.Footer, CouponCode = couponCode.Trim().ToUpperInvariant(), PointsRedeemed = points, ShiftId = activeShift?.Id };
        sale = sale with { PointsEarned = customerId == null ? 0 : checked((int)decimal.Floor(sale.Total / 100)) };
        if (payment == "Cash" && tendered < sale.Total) throw new InvalidOperationException("Cash received is less than the total.");
        Commit(() =>
        {
            foreach (var line in items) Data.Products.Single(x => x.Id == line.ProductId).Stock -= line.Quantity;
            Data.Sales.Add(sale);
            if (saved != null) Data.SavedCarts.Remove(saved);
        });
        return sale;
    }
    public static string Receipt(Sale sale) => $"{sale.ShopName}\n{sale.ShopAddress}\nSHOP RECEIPT {sale.Currency}\n{sale.Number}\n{sale.Time:yyyy-MM-dd HH:mm}\nCustomer: {sale.CustomerName}\n--------------------------------\n" +
        string.Join("\n", sale.Lines.Select(x => $"{x.Name}\n  {x.Quantity} x {x.Price:0.00}    {x.Total:0.00}")) +
        $"\n--------------------------------\nSubtotal: {sale.Subtotal:0.00}\nDiscount: {sale.Discount:0.00}\nCoupon: {sale.CouponCode}\nPoints used / earned: {sale.PointsRedeemed} / {sale.PointsEarned}\nTax ({sale.TaxRate:0.##}%): {sale.Tax:0.00}\nTOTAL: {sale.Total:0.00}\nPayment: {sale.Payment}\nReceived: {(sale.Payment == "Card" ? sale.Total : sale.Tendered):0.00}\nChange: {sale.Change:0.00}\n\n{sale.Footer}";
}
