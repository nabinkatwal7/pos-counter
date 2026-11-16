namespace CounterPoint.Core;

public sealed partial class Store
{
    public static readonly Guid CounterLocationId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    public Guid NormalizeLocation(Guid id) => id == Guid.Empty ? CounterLocationId : id;
    public void SaveLocation(Guid id, string name, bool active)
    {
        Require(!string.IsNullOrWhiteSpace(name) && (id != CounterLocationId || active), "Enter a location name; the main counter must remain active.");
        Require(!Data.Business.Locations.Any(l => l.Id != id && l.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)), "Location name already exists.");
        var old = Data.Business.Locations.SingleOrDefault(l => l.Id == id);
        Require(active || (!Data.Business.LocationStocks.Any(s => s.LocationId == id && s.Quantity > 0) && !Data.Business.Orders.Any(o => o.LocationId == id && IsReserved(o))), "Empty the location and finish its reserved orders before disabling it.");
        Commit(() => { if (old != null) Data.Business.Locations.Remove(old); Data.Business.Locations.Add(new(id == Guid.Empty ? Guid.NewGuid() : id, name.Trim(), active)); });
    }
    public int StockAt(Guid productId, Guid locationId)
    {
        locationId = NormalizeLocation(locationId);
        return locationId == CounterLocationId ? Data.Products.Single(p => p.Id == productId).Stock : Data.Business.LocationStocks.SingleOrDefault(s => s.ProductId == productId && s.LocationId == locationId)?.Quantity ?? 0;
    }
    public int TotalStock(Guid productId) => checked(Data.Products.Single(p => p.Id == productId).Stock + Data.Business.LocationStocks.Where(s => s.ProductId == productId).Sum(s => s.Quantity));
    private void ValidateLocation(Guid id) => Require(Data.Business.Locations.Any(l => l.Id == NormalizeLocation(id) && l.Active), "Select an active stock location.");
    private void SetStock(Guid productId, Guid locationId, int quantity)
    {
        Require(quantity >= 0, "Stock cannot be negative."); locationId = NormalizeLocation(locationId);
        if (locationId == CounterLocationId) Data.Products.Single(p => p.Id == productId).Stock = quantity;
        else { Data.Business.LocationStocks.RemoveAll(s => s.ProductId == productId && s.LocationId == locationId); Data.Business.LocationStocks.Add(new(locationId, productId, quantity)); }
    }
    private void ChangeStock(Guid productId, Guid locationId, int change, string reason, bool record = true)
    {
        var product = Data.Products.Single(p => p.Id == productId);
        Require(product.Kind == "Stock", "Only physical stock products have inventory quantities.");
        SetStock(productId, locationId, checked(StockAt(productId, locationId) + change));
        if (record) Data.Movements.Add(new(DateTimeOffset.Now, productId, product.Name, change, reason) { LocationId = NormalizeLocation(locationId) });
    }
    private List<StockPart> UnitComponents(Product product)
    {
        if (product.Kind == "Service") return [];
        if (product.Kind == "Stock") return [new(product.Id, product.Name, 1, product.UnitCost)];
        var recipe = Data.Business.Bundles.SingleOrDefault(b => b.ProductId == product.Id);
        Require(recipe != null && recipe.Parts.Count > 0, "Configure bundle components first.");
        return recipe!.Parts.Select(part =>
        {
            var p = Data.Products.Single(p => p.Id == part.ProductId);
            Require(!p.Archived && p.Kind == "Stock", "Bundle contains an unavailable component."); return new StockPart(p.Id, p.Name, part.Quantity, p.UnitCost);
        }).ToList();
    }
    private List<StockPart> Demand(IEnumerable<SaleLine> lines)
    {
        return lines.SelectMany(l => (l.Components.Count > 0 ? l.Components : UnitComponents(Data.Products.Single(p => p.Id == l.ProductId)))
            .Select(p => p with { Quantity = checked(p.Quantity * l.Quantity) })).GroupBy(p => p.ProductId)
            .Select(g => g.First() with { Quantity = checked(g.Sum(p => p.Quantity)) }).ToList();
    }
    private static bool IsReserved(CustomerOrder order) => order.Status is "Reserved" or "Picked" or "Ready";
    private static bool IsJobReserved(ServiceJob job) => job.Status is "Approved" or "In progress" or "Ready";
    public int ReservedStock(Guid productId, Guid locationId, Guid exceptOrder = default, Guid exceptJob = default)
    {
        locationId = NormalizeLocation(locationId);
        var orders = Data.Business.Orders.Where(o => o.Id != exceptOrder && o.LocationId == locationId && IsReserved(o)).SelectMany(o => Demand(o.Lines)).Where(p => p.ProductId == productId).Sum(p => p.Quantity);
        var jobs = Data.Business.Jobs.Where(j => j.Id != exceptJob && j.LocationId == locationId && IsJobReserved(j)).SelectMany(j => j.Parts).Where(p => p.ProductId == productId).Sum(p => p.Quantity);
        return checked(orders + jobs);
    }
    public int SellableQuantity(Guid productId, Guid locationId, Guid exceptOrder = default, Guid exceptJob = default)
    {
        var product = Data.Products.Single(p => p.Id == productId);
        if (product.Archived) return 0;
        if (product.Kind == "Service") return int.MaxValue;
        var components = UnitComponents(product);
        return components.Min(p => Math.Max(0, StockAt(p.ProductId, locationId) - ReservedStock(p.ProductId, locationId, exceptOrder, exceptJob)) / p.Quantity);
    }
    private void CheckDemand(IEnumerable<SaleLine> lines, Guid locationId, Guid exceptOrder = default, Guid exceptJob = default)
    {
        ValidateLocation(locationId);
        foreach (var part in Demand(lines)) Require(StockAt(part.ProductId, locationId) - ReservedStock(part.ProductId, locationId, exceptOrder, exceptJob) >= part.Quantity, $"Insufficient unreserved stock for {part.Name}.");
    }
    public void TransferStock(Guid productId, Guid from, Guid to, int quantity, string reason)
    {
        from = NormalizeLocation(from); to = NormalizeLocation(to); ValidateLocation(from); ValidateLocation(to);
        Require(from != to && quantity > 0 && !string.IsNullOrWhiteSpace(reason) && StockAt(productId, from) - ReservedStock(productId, from) >= quantity, "Choose different locations, a positive quantity within unreserved stock, and a reason.");
        Commit(() =>
        {
            ChangeStock(productId, from, -quantity, "Transfer out: " + reason); ChangeStock(productId, to, quantity, "Transfer in: " + reason);
            Data.Business.Transfers.Add(new($"TR-{Data.Business.Transfers.Count + 1:000000}", DateTimeOffset.Now, productId, Data.Products.Single(p => p.Id == productId).Name, from, Data.Business.Locations.Single(l => l.Id == from).Name, to, Data.Business.Locations.Single(l => l.Id == to).Name, quantity, reason));
        });
    }
    public void AdjustLocationStock(Guid productId, Guid location, int change, string reason)
    {
        location = NormalizeLocation(location); ValidateLocation(location);
        Require(change != 0 && !string.IsNullOrWhiteSpace(reason) && (long)StockAt(productId, location) + change >= ReservedStock(productId, location), "Enter a nonzero adjustment and reason. Reserved quantities must remain available.");
        Commit(() => ChangeStock(productId, location, change, reason));
    }
    public void ConfigureProduct(Guid productId, string kind, decimal cost, Guid supplier, int leadDays, int safetyStock)
    {
        var product = Data.Products.Single(p => p.Id == productId);
        Require(kind is "Stock" or "Service" or "Bundle" && cost >= 0 && leadDays >= 0 && leadDays <= 365 && safetyStock >= 0, "Check product type, cost, lead time (0–365) and safety stock.");
        Require(supplier == Guid.Empty || Data.Suppliers.Any(s => s.Id == supplier), "Select an existing supplier.");
        Require(product.Kind == kind || (TotalStock(productId) == 0 && !Data.Business.Bundles.Any(b => b.Parts.Any(p => p.ProductId == productId)) && !HasOpenProductUse(productId) && !Data.Sales.Any(s => s.Lines.Any(l => l.ProductId == productId))), "Clear stock and reservations before changing product type. Products already used on receipts keep their type.");
        Commit(() => { product.Kind = kind; product.UnitCost = cost; product.PreferredSupplierId = supplier; product.LeadDays = leadDays; product.SafetyStock = safetyStock; });
    }
    private bool HasOpenProductUse(Guid productId) => Data.Business.Orders.Any(o => IsReserved(o) && (o.Lines.Any(l => l.ProductId == productId) || Demand(o.Lines).Any(p => p.ProductId == productId))) || Data.Business.Jobs.Any(j => IsJobReserved(j) && (j.LaborProductId == productId || j.Parts.Any(l => l.ProductId == productId)));
    public void SaveBundle(Guid productId, IEnumerable<BundlePart> components)
    {
        var parts = components.ToList(); var product = Data.Products.Single(p => p.Id == productId);
        Require(product.Kind == "Bundle" && parts.Count > 0 && parts.Select(p => p.ProductId).Distinct().Count() == parts.Count && parts.All(p => p.ProductId != productId && p.Quantity > 0 && Data.Products.Any(x => x.Id == p.ProductId && x.Kind == "Stock" && !x.Archived)), "Select a bundle product and distinct physical components with positive quantities.");
        Require(!HasOpenProductUse(productId), "Finish reserved orders using this bundle before changing the recipe.");
        Commit(() => { Data.Business.Bundles.RemoveAll(b => b.ProductId == productId); Data.Business.Bundles.Add(new(productId, parts)); });
    }
    public Stocktake StartStocktake(string name, Guid location)
    {
        location = NormalizeLocation(location); ValidateLocation(location);
        Require(!string.IsNullOrWhiteSpace(name) && !Data.Business.Stocktakes.Any(s => s.LocationId == location && s.Status == "Counting"), "Enter a name and finish the current stocktake at this location first.");
        var take = new Stocktake(Guid.NewGuid(), name, location, DateTimeOffset.Now, "Counting", Data.Products.Where(p => p.Kind == "Stock").Select(p => new StocktakeLine(p.Id, p.Name, StockAt(p.Id, location), null)).ToList());
        Require(take.Lines.Count > 0, "Create physical products before counting."); Commit(() => Data.Business.Stocktakes.Add(take)); return take;
    }
    public void CountStock(Guid stocktakeId, Guid productId, int count)
    {
        var take = Data.Business.Stocktakes.Single(t => t.Id == stocktakeId); var line = take.Lines.Single(l => l.ProductId == productId);
        Require(take.Status == "Counting" && count >= 0, "Stocktake must be open and count cannot be negative.");
        Commit(() => take.Lines[take.Lines.IndexOf(line)] = line with { Counted = count });
    }
    public void RecordStocktakeCounts(Guid stocktakeId, IEnumerable<(Guid ProductId, int Count)> counts)
    {
        var take = Data.Business.Stocktakes.Single(t => t.Id == stocktakeId); var rows = counts.ToList();
        Require(take.Status == "Counting" && rows.Count > 0 && rows.Select(r => r.ProductId).Distinct().Count() == rows.Count && rows.All(r => r.Count >= 0 && take.Lines.Any(l => l.ProductId == r.ProductId)), "Use an open stocktake and distinct non-negative physical counts.");
        Commit(() => { foreach (var row in rows) { var line = take.Lines.Single(l => l.ProductId == row.ProductId); take.Lines[take.Lines.IndexOf(line)] = line with { Counted = row.Count }; } });
    }
    public void FinishStocktake(Guid id, bool apply)
    {
        var take = Data.Business.Stocktakes.Single(t => t.Id == id); Require(take.Status == "Counting", "Stocktake is already closed.");
        if (apply)
        {
            Require(take.Lines.All(l => l.Counted != null && StockAt(l.ProductId, take.LocationId) == l.Expected && l.Counted >= ReservedStock(l.ProductId, take.LocationId)), "Count every item. Stock changed since the snapshot or a count conflicts with reservations; cancel and start a fresh count.");
        }
        Commit(() =>
        {
            if (apply) foreach (var line in take.Lines.Where(l => l.Counted != l.Expected)) ChangeStock(line.ProductId, take.LocationId, line.Counted!.Value - line.Expected, "Stocktake: " + take.Name);
            Data.Business.Stocktakes[Data.Business.Stocktakes.IndexOf(take)] = take with { Status = apply ? "Applied" : "Cancelled" };
        });
    }
    private void ReceiveCostedStock(Guid productId, Guid location, int quantity, decimal unitCost, string reason)
    {
        var product = Data.Products.Single(p => p.Id == productId); var previous = TotalStock(productId);
        product.UnitCost = decimal.Round((previous * product.UnitCost + quantity * unitCost) / checked(previous + quantity), 4, MidpointRounding.AwayFromZero);
        ChangeStock(productId, location, quantity, reason);
    }
    public IReadOnlyList<ValuationRow> InventoryValuation() => Data.Products.Where(p => p.Kind == "Stock").Select(p => new ValuationRow(p.Id, p.Name, TotalStock(p.Id), p.UnitCost, Round(TotalStock(p.Id) * p.UnitCost))).ToList();
    public IReadOnlyList<MarginRow> MarginReport(DateTime from, DateTime to)
    {
        Require(to.Date >= from.Date, "Choose a valid date range.");
        var rows = Data.Sales.Where(s => s.Time.LocalDateTime.Date >= from.Date && s.Time.LocalDateTime.Date <= to.Date).Select(s =>
        {
            var revenue = s.Subtotal - s.Discount; var cost = s.Lines.Sum(l => l.UnitCost * l.Quantity); return new MarginRow(s.Number, s.Time, revenue, Round(cost), Round(revenue - cost));
        }).ToList();
        foreach (var r in Data.Returns.Where(r => r.Time.LocalDateTime.Date >= from.Date && r.Time.LocalDateTime.Date <= to.Date))
        {
            var sale = Data.Sales.Single(s => s.Number == r.SaleNumber);
            var revenue = sale.Total == 0 ? 0 : Round(r.Amount * (sale.Subtotal - sale.Discount) / sale.Total);
            var cost = r.Lines.Count == 0 ? sale.Lines.Sum(l => l.UnitCost * l.Quantity) : r.Lines.Where(l => l.Restock).Sum(l => sale.Lines.Single(s => s.ProductId == l.ProductId).UnitCost * l.Quantity);
            rows.Add(new(r.SaleNumber + " return", r.Time, -revenue, -Round(cost), Round(cost - revenue)));
        }
        return rows.OrderBy(r => r.Time).ToList();
    }
}
