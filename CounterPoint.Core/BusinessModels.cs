namespace CounterPoint.Core;

public sealed class BusinessData
{
    public List<StockLocation> Locations { get; set; } = [new(Store.CounterLocationId, "Main counter")];
    public List<LocationStock> LocationStocks { get; set; } = [];
    public List<StockTransfer> Transfers { get; set; } = [];
    public List<PurchaseOrder> PurchaseOrders { get; set; } = [];
    public List<SupplierReturn> SupplierReturns { get; set; } = [];
    public List<SupplierPayment> SupplierPayments { get; set; } = [];
    public List<Stocktake> Stocktakes { get; set; } = [];
    public List<BundleRecipe> Bundles { get; set; } = [];
    public List<PriceRule> PriceRules { get; set; } = [];
    public List<CreditAccount> CreditAccounts { get; set; } = [];
    public List<AccountPayment> AccountPayments { get; set; } = [];
    public List<GiftCard> GiftCards { get; set; } = [];
    public List<GiftEntry> GiftEntries { get; set; } = [];
    public List<CustomerOrder> Orders { get; set; } = [];
    public List<OrderPayment> OrderPayments { get; set; } = [];
    public List<ServiceJob> Jobs { get; set; } = [];
    public List<Employee> Employees { get; set; } = [];
    public List<Attendance> Attendance { get; set; } = [];
    public List<CommissionPayment> CommissionPayments { get; set; } = [];
    public List<AuditEntry> Audit { get; set; } = [];
}
public sealed record StockLocation(Guid Id, string Name, bool Active = true);
public sealed record LocationStock(Guid LocationId, Guid ProductId, int Quantity);
public sealed record StockTransfer(string Number, DateTimeOffset Time, Guid ProductId, string Product, Guid FromId, string From, Guid ToId, string To, int Quantity, string Reason);
public sealed record OrderItem(Guid ProductId, string Name, int Quantity, decimal UnitPrice)
{
    public decimal Total => Quantity * UnitPrice;
}
public sealed record PurchaseOrder(string Number, Guid SupplierId, string Supplier, Guid LocationId, DateTime Created, DateTime Expected, string Status, List<OrderItem> Lines)
{
    public decimal Total => Lines.Sum(l => l.Total);
}
public sealed record SupplierReturn(string Number, DateTimeOffset Time, string PurchaseNumber, Guid SupplierId, Guid ProductId, int Quantity, decimal Credit, Guid LocationId, string Reason);
public sealed record SupplierPayment(DateTimeOffset Time, Guid SupplierId, string PurchaseNumber, decimal Amount, string Method, string Reference);
public sealed record StocktakeLine(Guid ProductId, string Product, int Expected, int? Counted);
public sealed record Stocktake(Guid Id, string Name, Guid LocationId, DateTimeOffset Created, string Status, List<StocktakeLine> Lines);
public sealed record BundlePart(Guid ProductId, int Quantity);
public sealed record BundleRecipe(Guid ProductId, List<BundlePart> Parts);
public sealed record PriceRule(Guid Id, string Name, Guid ProductId, Guid CustomerId, int MinimumQuantity, decimal UnitPrice, DateTime Starts, DateTime Ends, bool Enabled = true);
public sealed record ReplenishmentRow(Guid ProductId, string Product, Guid SupplierId, string Supplier, int Available, int Inbound, decimal DailyDemand, int Suggested, decimal UnitCost);
public sealed record ValuationRow(Guid ProductId, string Product, int Quantity, decimal UnitCost, decimal Value);
public sealed record MarginRow(string Receipt, DateTimeOffset Time, decimal NetRevenue, decimal Cost, decimal GrossMargin);
public sealed record CreditAccount(Guid CustomerId, decimal Limit, int TermsDays, bool Enabled = true);
public sealed record AccountPayment(Guid Id, DateTimeOffset Time, Guid CustomerId, decimal Amount, string Method, string Reference, Guid ShiftId);
public sealed record AccountStatementRow(DateTimeOffset Time, string Reference, string Type, decimal Debit, decimal Credit, decimal Balance);
public sealed record PaymentPart(string Method, decimal Amount, string Reference = "");
public sealed record StockPart(Guid ProductId, string Name, int Quantity, decimal UnitCost);
public sealed record ReturnItem(Guid ProductId, int Quantity, bool Restock);
public sealed record GiftCard(string Code, string Recipient, DateTime Expires, bool Active = true);
public sealed record GiftEntry(Guid Id, DateTimeOffset Time, string Code, string Type, decimal Amount, string Reference, string Method, Guid ShiftId);
public sealed record CustomerOrder(Guid Id, string Number, string Kind, Guid CustomerId, Guid LocationId, DateTimeOffset Created, DateTime Due, string Address, string Notes, string Status, List<SaleLine> Lines, decimal Discount, decimal TaxRate, string SaleNumber = "", string Tracking = "")
{
    public decimal Total => new Sale("", Created, Lines, Discount, TaxRate, "Card", 0).Total;
}
public sealed record OrderPayment(Guid Id, DateTimeOffset Time, Guid OrderId, decimal Amount, string Method, string Reference, Guid ShiftId);
public sealed record Employee(Guid Id, string Name, string Username, string Role, string Salt, string PasswordHash, bool Active, decimal CommissionRate);
public sealed record Attendance(Guid Id, Guid EmployeeId, string Employee, DateTimeOffset ClockIn, DateTimeOffset? ClockOut, string CorrectionReason = "")
{
    public decimal Hours => ClockOut == null ? 0 : decimal.Round((decimal)(ClockOut.Value - ClockIn).TotalHours, 2);
}
public sealed record AuditEntry(long Sequence, DateTimeOffset Time, string Actor, string Action, string Changes, string PreviousHash, string Hash);
public sealed record CommissionPayment(Guid Id, Guid EmployeeId, DateTimeOffset Time, decimal Amount, string Reference, string Method, Guid ShiftId);
public sealed record CommissionBalance(Guid EmployeeId, string Employee, decimal Earned, decimal Paid, decimal Outstanding);
public sealed record ServiceJob(Guid Id, string Number, Guid CustomerId, Guid EmployeeId, string Device, string Serial, string Problem, string Diagnosis, DateTime Due, string Status, Guid LocationId, List<OrderItem> Parts, Guid LaborProductId, decimal LaborCharge, string SaleNumber = "");
