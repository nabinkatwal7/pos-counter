namespace CounterPoint.Core;

public sealed partial class Store
{
    public decimal SellingPrice(Guid productId, int quantity, Guid? customerId = null)
    {
        var product = Data.Products.Single(p => p.Id == productId);
        var rule = Data.Business.PriceRules.Where(r => r.ProductId == productId && r.Enabled && r.Starts <= DateTime.Today && r.Ends >= DateTime.Today && r.MinimumQuantity <= quantity && (r.CustomerId == Guid.Empty || r.CustomerId == customerId))
            .OrderByDescending(r => r.CustomerId != Guid.Empty).ThenByDescending(r => r.MinimumQuantity).ThenByDescending(r => r.Starts).ThenBy(r => r.Id).FirstOrDefault();
        return rule?.UnitPrice ?? product.Price;
    }
    public void SavePriceRule(PriceRule rule)
    {
        Require(Data.Products.Any(p => p.Id == rule.ProductId && !p.Archived) && (rule.CustomerId == Guid.Empty || Data.Customers.Any(c => c.Id == rule.CustomerId)) && !string.IsNullOrWhiteSpace(rule.Name) && rule.MinimumQuantity > 0 && rule.UnitPrice >= 0 && rule.Ends.Date >= rule.Starts.Date, "Select a product/customer and enter a name, positive minimum quantity, non-negative unit price and valid dates.");
        Require(!Data.Business.PriceRules.Any(r => r.Id != rule.Id && r.Enabled && rule.Enabled && r.ProductId == rule.ProductId && r.CustomerId == rule.CustomerId && r.MinimumQuantity == rule.MinimumQuantity && r.Starts <= rule.Ends && r.Ends >= rule.Starts), "A rule for this product, customer and quantity already overlaps these dates.");
        rule = rule with { Id = rule.Id == Guid.Empty ? Guid.NewGuid() : rule.Id, Starts = rule.Starts.Date, Ends = rule.Ends.Date };
        Commit(() => { Data.Business.PriceRules.RemoveAll(r => r.Id == rule.Id); Data.Business.PriceRules.Add(rule); });
    }
    private List<SaleLine> SnapshotLines(IEnumerable<SaleLine> lines, Guid? customer, bool validatePrice = true)
    {
        var items = lines.ToList();
        Require(items.Count > 0 && items.Select(l => l.ProductId).Distinct().Count() == items.Count && items.All(l => l.Quantity > 0 && l.Price >= 0), "Add distinct items with positive quantities and non-negative prices.");
        return items.Select(line =>
        {
            var product = Data.Products.SingleOrDefault(p => p.Id == line.ProductId);
            Require(product != null && !product.Archived && (!validatePrice || line.Price == SellingPrice(product.Id, line.Quantity, customer)), $"Refresh the price or availability for {line.Name}.");
            var parts = UnitComponents(product!);
            return new SaleLine(product!.Id, product.Name, line.Price, line.Quantity) { UnitCost = parts.Sum(p => p.UnitCost * p.Quantity), Components = parts };
        }).ToList();
    }
    public CustomerOrder CreateOrder(string kind, Guid customerId, Guid location, DateTime due, string address, string notes, IEnumerable<SaleLine> lines, decimal discount, decimal tax)
    {
        Require(kind is "Layaway" or "Delivery" && Data.Customers.Any(c => c.Id == customerId) && due.Date >= DateTime.Today && (kind != "Delivery" || !string.IsNullOrWhiteSpace(address)), "Select a customer, future due date and delivery address when required.");
        location = NormalizeLocation(location); ValidateLocation(location);
        var snapshot = SnapshotLines(lines, customerId);
        Require(discount >= 0 && discount <= snapshot.Sum(l => l.Total) && tax >= 0 && tax <= 100, "Check order discount and tax.");
        if (kind == "Layaway") CheckDemand(snapshot, location);
        var order = new CustomerOrder(Guid.NewGuid(), $"ORD-{Data.Business.Orders.Count + 1:000000}", kind, customerId, location, DateTimeOffset.Now, due.Date, address, notes, kind == "Layaway" ? "Reserved" : "Draft", snapshot, discount, tax);
        Commit(() => Data.Business.Orders.Add(order)); return order;
    }
    public decimal OrderDeposits(Guid orderId) => Data.Business.OrderPayments.Where(p => p.OrderId == orderId).Sum(p => p.Amount);
    public void PayOrder(Guid orderId, decimal amount, string method, string reference)
    {
        var order = Data.Business.Orders.Single(o => o.Id == orderId);
        Require(order.Kind == "Layaway" && IsReserved(order) && amount > 0 && OrderDeposits(orderId) + amount <= order.Total && method is "Cash" or "Card" && !string.IsNullOrWhiteSpace(reference), "Choose an open layaway and enter a positive deposit within its remaining balance, with a reference.");
        Require(!Data.Business.OrderPayments.Any(p => p.OrderId == orderId && p.Reference.Equals(reference.Trim(), StringComparison.OrdinalIgnoreCase)), "This deposit reference already exists.");
        Commit(() => Data.Business.OrderPayments.Add(new(Guid.NewGuid(), DateTimeOffset.Now, orderId, amount, method, reference.Trim(), ActiveShiftId)));
    }
    public void UpdateOrderState(Guid orderId, string state, string tracking = "")
    {
        var order = Data.Business.Orders.Single(o => o.Id == orderId);
        var valid = (order.Status == "Draft" && state == "Reserved") || (order.Status == "Reserved" && state is "Picked" or "Ready") || (order.Status == "Picked" && state == "Ready") || (order.Status == "Invoiced" && state == "Dispatched" && order.Kind == "Delivery") || (order.Status == "Dispatched" && state == "Delivered") || (order.Status == "Invoiced" && state == "Collected" && order.Kind == "Layaway");
        Require(valid && (state != "Dispatched" || !string.IsNullOrWhiteSpace(tracking)), "Use the next valid order state and enter a tracking/courier reference when dispatching.");
        if (state == "Reserved") CheckDemand(order.Lines, order.LocationId, order.Id);
        Commit(() => Data.Business.Orders[Data.Business.Orders.IndexOf(order)] = order with { Status = state, Tracking = tracking.Length > 0 ? tracking : order.Tracking });
    }
    public void CancelOrder(Guid orderId, string reason)
    {
        var order = Data.Business.Orders.Single(o => o.Id == orderId);
        Require(order.Status is "Draft" or "Reserved" or "Picked" or "Ready" && !string.IsNullOrWhiteSpace(reason), "Cancel only an uninvoiced order and enter a reason.");
        var deposits = Data.Business.OrderPayments.Where(p => p.OrderId == orderId).GroupBy(p => p.Method).Select(g => (Method: g.Key, Amount: g.Sum(p => p.Amount))).Where(p => p.Amount > 0).ToList();
        Commit(() =>
        {
            foreach (var deposit in deposits) Data.Business.OrderPayments.Add(new(Guid.NewGuid(), DateTimeOffset.Now, orderId, -deposit.Amount, deposit.Method, "Cancel refund: " + reason, ActiveShiftId));
            Data.Business.Orders[Data.Business.Orders.IndexOf(order)] = order with { Status = "Cancelled", Notes = order.Notes + "\nCancelled: " + reason };
        });
    }
    public ServiceJob CreateServiceJob(Guid customerId, Guid employeeId, Guid location, string device, string serial, string problem, DateTime due)
    {
        Require(Data.Customers.Any(c => c.Id == customerId) && (employeeId == Guid.Empty || Data.Business.Employees.Any(e => e.Id == employeeId && e.Active)) && !string.IsNullOrWhiteSpace(device) && !string.IsNullOrWhiteSpace(problem) && due.Date >= DateTime.Today, "Select a customer, optional active technician, device, reported problem and future due date.");
        location = NormalizeLocation(location); ValidateLocation(location);
        var job = new ServiceJob(Guid.NewGuid(), $"JOB-{Data.Business.Jobs.Count + 1:000000}", customerId, employeeId, device, serial, problem, "", due.Date, "Intake", location, [], Guid.Empty, 0);
        Commit(() => Data.Business.Jobs.Add(job)); return job;
    }
    public void EstimateServiceJob(Guid id, string diagnosis, Guid laborProductId, decimal laborCharge, IEnumerable<OrderItem> parts)
    {
        var job = Data.Business.Jobs.Single(j => j.Id == id); var rows = parts.ToList();
        Require(job.Status is "Intake" or "Estimated" && !string.IsNullOrWhiteSpace(diagnosis) && laborCharge >= 0 && Data.Products.Any(p => p.Id == laborProductId && p.Kind == "Service" && !p.Archived), "Estimate an intake job with a diagnosis, active service product and non-negative labor charge.");
        Require(rows.Select(l => l.ProductId).Distinct().Count() == rows.Count && rows.All(l => l.Quantity > 0 && l.UnitPrice >= 0 && Data.Products.Any(p => p.Id == l.ProductId && p.Kind == "Stock" && !p.Archived)), "Parts must be distinct physical products with positive quantities and non-negative prices.");
        var snapshot = rows.Select(l => l with { Name = Data.Products.Single(p => p.Id == l.ProductId).Name }).ToList();
        Commit(() => Data.Business.Jobs[Data.Business.Jobs.IndexOf(job)] = job with { Status = "Estimated", Diagnosis = diagnosis, LaborProductId = laborProductId, LaborCharge = laborCharge, Parts = snapshot });
    }
    public List<SaleLine> ServiceJobLines(Guid id)
    {
        var job = Data.Business.Jobs.Single(j => j.Id == id);
        Require(job.LaborProductId != Guid.Empty, "Estimate the service job first.");
        var lines = job.Parts.Select(p => new SaleLine(p.ProductId, p.Name, p.UnitPrice, p.Quantity)).ToList();
        var labor = Data.Products.Single(p => p.Id == job.LaborProductId); lines.Add(new(labor.Id, labor.Name, job.LaborCharge, 1)); return lines;
    }
    public void UpdateServiceJob(Guid id, string state)
    {
        var job = Data.Business.Jobs.Single(j => j.Id == id);
        Require((job.Status == "Estimated" && state == "Approved") || (job.Status == "Approved" && state == "In progress") || (job.Status == "In progress" && state == "Ready") || (job.Status == "Invoiced" && state == "Collected") || (job.Status is "Intake" or "Estimated" or "Approved" or "In progress" or "Ready" && state == "Cancelled"), "Use the next valid service job state.");
        if (state == "Approved") CheckDemand(ServiceJobLines(id), job.LocationId, exceptJob: id);
        Commit(() => Data.Business.Jobs[Data.Business.Jobs.IndexOf(job)] = job with { Status = state });
    }
}
