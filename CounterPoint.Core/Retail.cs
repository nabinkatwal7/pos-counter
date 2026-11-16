using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.VisualBasic.FileIO;

namespace CounterPoint.Core;
public sealed record SavedCart(Guid Id, string Name, string Kind, DateTimeOffset Created, DateTime ExpiresOn, List<SaleLine> Lines, decimal Discount, decimal TaxRate, Guid? CustomerId)
{
    public Guid LocationId { get; init; } = Store.CounterLocationId;
    public decimal Total => new Sale("", Created, Lines, Discount, TaxRate, "Card", 0).Total;
}
public sealed record Coupon(string Code, bool Percentage, decimal Value, decimal Minimum, DateTime ExpiresOn, int MaxUses, bool Enabled = true);
public sealed record CashShift(Guid Id, string Operator, DateTimeOffset Opened, decimal Opening, DateTimeOffset? Closed = null, decimal? Counted = null, decimal? Expected = null)
{
    public decimal? Variance => Counted - Expected;
}
public sealed record CashEntry(Guid ShiftId, DateTimeOffset Time, decimal Amount, string Reason);
public sealed record ProductPerformance(string Barcode, string Product, int Sold, int Returned, int NetUnits, decimal NetSalesBeforeTax);
public sealed record BackupFile(string Format, string Payload, string Sha256);

public sealed partial class Store
{
    public void ArchiveProduct(Guid id, bool archived)
    {
        Require(!archived || !HasOpenProductUse(id), "Finish reserved orders using this product before archiving it.");
        Commit(() => Data.Products.Single(p => p.Id == id).Archived = archived);
    }
    public int LoyaltyBalance(Guid customerId) => Data.Sales.Where(s => s.CustomerId == customerId).Sum(s => s.PointsEarned - s.PointsRedeemed - (s.Total == 0 ? (FullyReturned(s) ? s.PointsEarned - s.PointsRedeemed : 0) : (int)decimal.Floor(s.PointsEarned * Refunded(s.Number) / s.Total) - (int)decimal.Floor(s.PointsRedeemed * Refunded(s.Number) / s.Total)));
    public void SaveCoupon(Coupon coupon)
    {
        coupon = coupon with { Code = coupon.Code.Trim().ToUpperInvariant(), ExpiresOn = coupon.ExpiresOn.Date };
        Require(coupon.Code.Length > 0 && coupon.Value > 0 && (!coupon.Percentage || coupon.Value <= 100) && coupon.Minimum >= 0 && coupon.MaxUses > 0, "Enter a code, positive value, non-negative minimum and positive usage limit; percent must be at most 100.");
        Commit(() => { Data.Coupons.RemoveAll(c => c.Code == coupon.Code); Data.Coupons.Add(coupon); });
    }
    public decimal CalculateDiscount(decimal subtotal, decimal manual, string code, int points, Guid? customerId)
    {
        Require(subtotal >= 0 && manual >= 0 && manual <= subtotal && points >= 0, "Check discount and points amounts.");
        var discount = manual;
        if (!string.IsNullOrWhiteSpace(code))
        {
            var coupon = Data.Coupons.SingleOrDefault(c => c.Code.Equals(code.Trim(), StringComparison.OrdinalIgnoreCase));
            Require(coupon != null && coupon.Enabled && coupon.ExpiresOn.Date >= DateTime.Today && subtotal >= coupon.Minimum && Data.Sales.Count(s => s.CouponCode == coupon.Code) < coupon.MaxUses, "Coupon is invalid, expired, below its minimum or fully used.");
            discount += coupon!.Percentage ? decimal.Round((subtotal - manual) * coupon.Value / 100, 2, MidpointRounding.AwayFromZero) : Math.Min(coupon.Value, subtotal - manual);
        }
        Require(points == 0 || (customerId != null && Data.Customers.Any(c => c.Id == customerId) && points <= LoyaltyBalance(customerId.Value)), "Select a customer with enough loyalty points.");
        Require(discount + points <= subtotal, "Discounts and points exceed the subtotal.");
        return discount + points;
    }
    public SavedCart SaveCart(string name, string kind, IEnumerable<SaleLine> lines, decimal discount, decimal tax, Guid? customerId, DateTime expires, Guid locationId = default)
    {
        var items = lines.ToList();
        Require(!string.IsNullOrWhiteSpace(name) && kind is "Held" or "Quote" && items.Count > 0 && items.All(l => l.Quantity > 0) && items.Select(l => l.ProductId).Distinct().Count() == items.Count, "Enter a name and a valid nonempty cart.");
        Require(items.All(l => Data.Products.Any(p => p.Id == l.ProductId && !p.Archived && SellingPrice(p.Id, l.Quantity, customerId) == l.Price)), "Cart contains unavailable products or outdated prices.");
        Require(discount >= 0 && discount <= items.Sum(l => l.Total) && tax >= 0 && tax <= 100 && (kind != "Quote" || expires.Date >= DateTime.Today), "Check discount, tax and quote expiry.");
        Require(customerId == null || Data.Customers.Any(c => c.Id == customerId), "Customer does not exist.");
        locationId = NormalizeLocation(locationId); ValidateLocation(locationId);
        var saved = new SavedCart(Guid.NewGuid(), name, kind, DateTimeOffset.Now, expires.Date, items, discount, tax, customerId) { LocationId = locationId };
        Commit(() => Data.SavedCarts.Add(saved)); return saved;
    }
    public List<SaleLine> ResumeCart(Guid id)
    {
        var saved = Data.SavedCarts.Single(c => c.Id == id);
        Require(saved.Kind != "Quote" || saved.ExpiresOn >= DateTime.Today, "Quotation has expired.");
        var lines = saved.Lines.Select(l =>
        {
            var p = Data.Products.SingleOrDefault(p => p.Id == l.ProductId);
            Require(p != null && !p.Archived && SellableQuantity(p.Id, saved.LocationId) >= l.Quantity, $"Check availability of {l.Name}.");
            return new SaleLine(p!.Id, p.Name, SellingPrice(p.Id, l.Quantity, saved.CustomerId), l.Quantity);
        }).ToList();
        CheckDemand(lines, saved.LocationId);
        Require(saved.Discount <= lines.Sum(l => l.Total), "Current prices are lower than the saved discount. Edit the saved order first.");
        return lines;
    }
    public void DeleteSavedCart(Guid id) => Commit(() => Data.SavedCarts.RemoveAll(c => c.Id == id));
    public void OpenShift(string name, decimal opening)
    {
        Require(!string.IsNullOrWhiteSpace(name) && opening >= 0 && !Data.Shifts.Any(s => s.Closed == null), "Enter an operator and non-negative float. Close the current shift first.");
        Commit(() => Data.Shifts.Add(new(Guid.NewGuid(), name, DateTimeOffset.Now, opening)));
    }
    public decimal ExpectedCash(Guid id)
    {
        var shift = Data.Shifts.Single(s => s.Id == id);
        return shift.Expected ?? shift.Opening + Data.Sales.Where(s => s.ShiftId == id).Sum(s => s.Payments.Count == 0 && s.DepositPayments.Count == 0 ? (s.Payment == "Cash" ? s.Total : 0) : s.Payments.Where(p => p.Method == "Cash").Sum(p => p.Amount))
            - Data.Returns.Where(r => r.ShiftId == id).Sum(r => RefundPayments(r).Where(p => p.Method == "Cash").Sum(p => p.Amount)) + Data.CashEntries.Where(e => e.ShiftId == id).Sum(e => e.Amount)
            + Data.Business.AccountPayments.Where(p => p.ShiftId == id && p.Method == "Cash").Sum(p => p.Amount)
            + Data.Business.OrderPayments.Where(p => p.ShiftId == id && p.Method == "Cash").Sum(p => p.Amount)
            + Data.Business.GiftEntries.Where(p => p.ShiftId == id && p.Method == "Cash" && p.Type is "Issue" or "Top-up").Sum(p => p.Amount);
    }
    public void MoveCash(decimal amount, string reason)
    {
        var shift = Data.Shifts.SingleOrDefault(s => s.Closed == null);
        Require(shift != null && amount != 0 && !string.IsNullOrWhiteSpace(reason), "Open a shift and enter a nonzero cash amount and reason.");
        Commit(() => Data.CashEntries.Add(new(shift!.Id, DateTimeOffset.Now, amount, reason)));
    }
    public void CloseShift(decimal counted)
    {
        var shift = Data.Shifts.SingleOrDefault(s => s.Closed == null);
        Require(shift != null && counted >= 0, "Open a shift and enter a non-negative cash count.");
        var closed = shift! with { Closed = DateTimeOffset.Now, Counted = counted, Expected = ExpectedCash(shift!.Id) };
        Commit(() => Data.Shifts[Data.Shifts.IndexOf(shift)] = closed);
    }
    public IReadOnlyList<Sale> FindSales(string query, DateTime? from, DateTime? to, string payment = "All", string status = "All")
    {
        Require(from == null || to == null || from.Value.Date <= to.Value.Date, "End date must follow start date.");
        return Data.Sales.Where(s => $"{s.Number} {s.CustomerName} {string.Join(' ', s.Lines.Select(l => l.Name))}".Contains(query.Trim(), StringComparison.OrdinalIgnoreCase)
            && (from == null || s.Time.LocalDateTime.Date >= from.Value.Date) && (to == null || s.Time.LocalDateTime.Date <= to.Value.Date)
            && (payment == "All" || s.Payment == payment || PaymentsFor(s).Any(p => p.Method == payment)) && (status == "All" || (status == "Returned" ? FullyReturned(s) : status == "Partial" ? Refunded(s.Number) > 0 && !FullyReturned(s) : !FullyReturned(s))))
            .OrderByDescending(s => s.Time).ToList();
    }
    public IReadOnlyList<ProductPerformance> Performance(DateTime from, DateTime to)
    {
        Require(to.Date >= from.Date, "End date must follow start date.");
        var sales = Data.Sales.Where(s => s.Time.LocalDateTime.Date >= from.Date && s.Time.LocalDateTime.Date <= to.Date).ToList();
        var returned = Data.Returns.Where(r => r.Time.LocalDateTime.Date >= from.Date && r.Time.LocalDateTime.Date <= to.Date).Select(r => Data.Sales.Single(s => s.Number == r.SaleNumber)).ToList();
        decimal Amount(Sale s, SaleLine l) => s.Subtotal == 0 ? 0 : l.Total * (s.Subtotal - s.Discount) / s.Subtotal;
        return Data.Products.Select(p =>
        {
            var sold = sales.SelectMany(s => s.Lines.Where(l => l.ProductId == p.Id).Select(l => (s, l))).ToList();
            var refunds = returned.SelectMany(s => s.Lines.Where(l => l.ProductId == p.Id).Select(l => (s, l))).ToList();
            return new ProductPerformance(p.Barcode, p.Name, sold.Sum(x => x.l.Quantity), refunds.Sum(x => x.l.Quantity), sold.Sum(x => x.l.Quantity) - refunds.Sum(x => x.l.Quantity), decimal.Round(sold.Sum(x => Amount(x.s, x.l)) - refunds.Sum(x => Amount(x.s, x.l)), 2));
        }).OrderByDescending(p => p.NetSalesBeforeTax).ToList();
    }
    public string ExportProducts()
    {
        string Cell(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
        return "Barcode,Name,Category,Price,Stock,ReorderLevel,Archived\r\n" + string.Join("\r\n", Data.Products.Select(p => string.Join(',', Cell(p.Barcode), Cell(p.Name), Cell(p.Category), p.Price.ToString(CultureInfo.InvariantCulture), p.Stock, p.ReorderLevel, p.Archived)));
    }
    public int ImportProducts(string csv)
    {
        using var parser = new TextFieldParser(new StringReader(csv)) { TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = true };
        parser.SetDelimiters(",");
        var header = parser.ReadFields();
        Require(header != null && header.SequenceEqual(new[] { "Barcode", "Name", "Category", "Price", "Stock", "ReorderLevel", "Archived" }), "CSV headers must match the exported product template.");
        var rows = new List<Product>();
        while (!parser.EndOfData)
        {
            var v = parser.ReadFields()!;
            Require(v.Length == 7, "Every CSV row must have seven columns.");
            Require(decimal.TryParse(v[3], NumberStyles.Number, CultureInfo.InvariantCulture, out var price) && int.TryParse(v[4], out var stock) && int.TryParse(v[5], out var reorder) && bool.TryParse(v[6], out var archived), "Invalid CSV price, stock, reorder level or archived flag.");
            int.TryParse(v[4], out stock); int.TryParse(v[5], out reorder); bool.TryParse(v[6], out archived);
            Require(v[0].Length > 0 && v[1].Length > 0 && price >= 0 && stock >= 0 && reorder >= 0 && !rows.Any(p => p.Barcode.Equals(v[0], StringComparison.OrdinalIgnoreCase)), "Invalid or duplicate barcode, name, price or stock in CSV.");
            var existing = Data.Products.FirstOrDefault(p => p.Barcode.Equals(v[0], StringComparison.OrdinalIgnoreCase));
            Require(existing == null || (stock >= ReservedStock(existing.Id, CounterLocationId) && (!archived || !HasOpenProductUse(existing.Id)) && (existing.Kind == "Stock" || stock == 0)), "CSV changes conflict with reserved inventory or a non-stock product.");
            rows.Add(new() { Id = existing?.Id ?? Guid.NewGuid(), Barcode = v[0], Name = v[1], Category = v[2], Price = price, Stock = stock, ReorderLevel = reorder, Archived = archived, Kind = existing?.Kind ?? "Stock", UnitCost = existing?.UnitCost ?? 0, PreferredSupplierId = existing?.PreferredSupplierId ?? Guid.Empty, LeadDays = existing?.LeadDays ?? 7, SafetyStock = existing?.SafetyStock ?? 3 });
        }
        Require(rows.Count > 0, "CSV contains no products.");
        Commit(() =>
        {
            foreach (var p in rows)
            {
                var old = Data.Products.FirstOrDefault(x => x.Id == p.Id); var change = p.Stock - (old?.Stock ?? 0);
                if (old != null) Data.Products.Remove(old); Data.Products.Add(p);
                if (change != 0) Data.Movements.Add(new(DateTimeOffset.Now, p.Id, p.Name, change, "CSV inventory import"));
            }
        }); return rows.Count;
    }
    public string ExportBackup()
    {
        var payload = JsonSerializer.Serialize(Data);
        return JsonSerializer.Serialize(new BackupFile("CounterPoint/1", payload, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))));
    }
    public void RestoreBackup(string backup)
    {
        EnsureAccess(nameof(RestoreBackup));
        var envelope = JsonSerializer.Deserialize<BackupFile>(backup) ?? throw new InvalidDataException("Invalid backup file.");
        Require(envelope.Format == "CounterPoint/1" && envelope.Payload != null && Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(envelope.Payload))) == envelope.Sha256, "Backup format or checksum is invalid.");
        using var document = JsonDocument.Parse(envelope.Payload!);
        Require(document.RootElement.ValueKind == JsonValueKind.Object && typeof(StoreData).GetProperties().Where(p => p.Name != "Business").All(p => document.RootElement.TryGetProperty(p.Name, out _)), "Backup is missing shop data.");
        static bool HasNull(JsonElement e) => e.ValueKind == JsonValueKind.Null || (e.ValueKind == JsonValueKind.Array && e.EnumerateArray().Any(HasNull)) || (e.ValueKind == JsonValueKind.Object && e.EnumerateObject().Any(p => p.Value.ValueKind == JsonValueKind.Null && p.Name is not ("CustomerId" or "ShiftId" or "Closed" or "Counted" or "Expected" or "ClockOut") || p.Value.ValueKind != JsonValueKind.Null && HasNull(p.Value)));
        Require(!HasNull(document.RootElement), "Backup contains invalid empty fields.");
        var data = JsonSerializer.Deserialize<StoreData>(envelope.Payload!)!;
        Require(data.Products.Select(p => p.Id).Distinct().Count() == data.Products.Count && data.Products.Select(p => p.Barcode.ToUpperInvariant()).Distinct().Count() == data.Products.Count && data.Products.All(p => p.Stock >= 0 && p.Price >= 0 && !string.IsNullOrWhiteSpace(p.Name) && !string.IsNullOrWhiteSpace(p.Barcode)), "Backup contains invalid products.");
        Require(data.Sales.Select(s => s.Number).Distinct().Count() == data.Sales.Count && data.Sales.All(s => s.Lines.Count > 0 && s.Lines.All(l => l.Quantity > 0 && data.Products.Any(p => p.Id == l.ProductId))) && data.Returns.All(r => data.Sales.Any(s => s.Number == r.SaleNumber)) && data.Shifts.Count(s => s.Closed == null) <= 1, "Backup contains inconsistent sales or shifts.");
        ValidateBusinessBackup(data);
        Require(!Data.Shifts.Any(s => s.Closed == null), "Close the active shift before restoring a backup.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path + ".before-restore-" + DateTime.Now.ToString("yyyyMMddHHmmssfff") + ".json", ExportBackup());
        Commit(() => Data = data);
        CurrentEmployee = null;
    }
}
