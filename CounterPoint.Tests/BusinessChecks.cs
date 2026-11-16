using CounterPoint.Core;

internal static class BusinessChecks
{
    private static int assertions;
    private static void Check(bool condition, string message) { assertions++; if (!condition) throw new Exception(message); }
    private static void Reject(Action action)
    {
        assertions++;
        try { action(); } catch (InvalidOperationException) { return; }
        throw new Exception("Invalid business operation was accepted.");
    }
    public static void Run(string directory)
    {
        var file = Path.Combine(directory, "business.json"); var store = new Store(file);
        var p = new Product { Name = "Coffee beans", Barcode = "B001", Price = 10, Stock = 20, UnitCost = 4 };
        var q = new Product { Name = "Filter pack", Barcode = "B002", Price = 20, Stock = 20, UnitCost = 6 };
        var customer = new Customer(Guid.NewGuid(), "Business customer", "555", ""); var supplier = new Supplier(Guid.NewGuid(), "Business supplier", "", "");
        store.SaveProduct(p); store.SaveProduct(q); store.SaveCustomer(customer); store.SaveSupplier(supplier); store.OpenShift("Business tests", 100);
        var shift = store.Data.Shifts.Single().Id;

        // 1. Locations and transfers: conserved stock, destination checkout, protected quantities.
        store.SaveLocation(Guid.Empty, "Warehouse", true); var warehouse = store.Data.Business.Locations.Single(l => l.Name == "Warehouse").Id;
        store.TransferStock(p.Id, Store.CounterLocationId, warehouse, 7, "Move overflow");
        Check(store.StockAt(p.Id, warehouse) == 7 && p.Stock == 13 && store.TotalStock(p.Id) == 20, "Transfer did not conserve stock.");
        Reject(() => store.TransferStock(p.Id, warehouse, warehouse, 1, "Same location"));
        var warehouseSale = store.Checkout([new(p.Id, p.Name, 10, 3)], 0, 0, "Card", 0, locationId: warehouse);
        Check(store.StockAt(p.Id, warehouse) == 4 && p.Stock == 13, "Checkout used the wrong location.");

        // 2. Multi-line purchasing, partial deliveries and receipt idempotence.
        var po = store.CreatePurchaseOrder(supplier.Id, warehouse, DateTime.Today.AddDays(7));
        store.ReplacePurchaseOrderLines(po.Number, [new(p.Id, p.Name, 5, 8), new(q.Id, q.Name, 4, 12)]);
        store.SetPurchaseOrderStatus(po.Number, "Submitted");
        Reject(() => store.SetPurchaseOrderLine(po.Number, p.Id, 10, 8));
        store.ReceivePurchaseOrder(po.Number, "DEL-001", [(p.Id, 2), (q.Id, 1)]);
        Check(store.Data.Business.PurchaseOrders.Single(o => o.Number == po.Number).Status == "Part received" && store.ReceivedQuantity(po.Number, p.Id) == 2, "Partial receiving failed.");
        var unchanged = store.ExportBackup(); Reject(() => store.ReceivePurchaseOrder(po.Number, "DEL-002", [(p.Id, 4), (q.Id, 1)]));
        Check(store.ExportBackup() == unchanged, "Invalid multi-line receipt changed data.");
        Reject(() => store.ReceivePurchaseOrder(po.Number, "DEL-001", [(p.Id, 1)]));
        store.ReceivePurchaseOrder(po.Number, "DEL-002", [(p.Id, 3), (q.Id, 3)]);
        Check(store.Data.Business.PurchaseOrders.Single(o => o.Number == po.Number).Status == "Received" && store.StockAt(q.Id, warehouse) == 4, "PO completion failed.");

        // 3–4. Supplier returns, partial payments, credit notes and credit refunds.
        var receipt = store.Data.Purchases.First(r => r.OrderNumber == po.Number && r.ProductId == p.Id);
        store.ReturnToSupplier(receipt.Number, 1, "Damaged delivery");
        Check(store.SupplierInvoiceBalance(receipt.Number) == 8 && store.StockAt(p.Id, warehouse) == 8, "Supplier credit did not reduce stock and invoice.");
        Reject(() => store.ReturnToSupplier(receipt.Number, 2, "Over return"));
        var drawer = store.ExpectedCash(shift); store.PaySupplier(receipt.Number, 8, "Cash", "PAY-001");
        Check(store.SupplierInvoiceBalance(receipt.Number) == 0 && store.ExpectedCash(shift) == drawer - 8, "Supplier cash payment incorrect.");
        Reject(() => store.PaySupplier(receipt.Number, 1, "Cash", "PAY-002"));
        store.ReturnToSupplier(receipt.Number, 1, "Remaining unit defective");
        Check(store.SupplierInvoiceBalance(receipt.Number) == -8, "Paid supplier invoice did not create credit.");
        store.ReceiveSupplierRefund(receipt.Number, 8, "Cash", "REF-001");
        Check(store.SupplierInvoiceBalance(receipt.Number) == 0 && store.ExpectedCash(shift) == drawer, "Supplier credit refund incorrect.");

        // 5. Stocktake reconciliation and stale-snapshot conflict detection.
        var take = store.StartStocktake("Warehouse count", warehouse);
        store.RecordStocktakeCounts(take.Id, take.Lines.Select(l => (l.ProductId, l.Expected + (l.ProductId == p.Id ? 1 : 0))));
        store.FinishStocktake(take.Id, true); Check(store.StockAt(p.Id, warehouse) == 8, "Stocktake variance not applied.");
        var stale = store.StartStocktake("Stale count", warehouse); store.RecordStocktakeCounts(stale.Id, stale.Lines.Select(l => (l.ProductId, l.Expected)));
        store.AdjustLocationStock(q.Id, warehouse, 1, "Concurrent delivery"); Reject(() => store.FinishStocktake(stale.Id, true));
        store.FinishStocktake(stale.Id, false);

        // 6–7. Cost snapshots, valuation and supplier-grouped demand planning.
        Check(warehouseSale.Lines.Single().UnitCost == 4 && store.MarginReport(DateTime.Today, DateTime.Today).Single().GrossMargin == 18, "Historical cost snapshot changed after receiving.");
        Check(p.UnitCost > 4 && store.InventoryValuation().Sum(r => r.Value) > 0, "Weighted costing or valuation missing.");
        store.ConfigureProduct(p.Id, "Stock", p.UnitCost, supplier.Id, 2, 5); store.ConfigureProduct(q.Id, "Stock", q.UnitCost, supplier.Id, 2, 5);
        store.SetReorderLevel(p.Id, 50); store.SetReorderLevel(q.Id, 50);
        var plan = store.ReplenishmentPlan(30, 14, Store.CounterLocationId); Check(plan.Count == 2 && plan.All(r => r.Suggested > 0), "Demand planner failed.");
        var planned = store.CreateReplenishmentOrders(30, 14, Store.CounterLocationId); Check(planned.Count == 1 && planned.Single().Lines.Count == 2 && store.ReplenishmentPlan(30, 14, Store.CounterLocationId).Count == 0, "Replenishment grouping or planned inbound accounting failed.");
        store.AdjustStock(p.Id, 100, "Test replenishment"); store.AdjustStock(q.Id, 100, "Test replenishment");

        // 8. Credit limits, partial collections, return credits, statements and aging.
        store.SaveCreditAccount(customer.Id, 100, 7, true);
        var creditSale = store.Checkout([new(p.Id, p.Name, 10, 5)], 0, 0, "Credit", 0, customer.Id);
        Check(store.CustomerBalance(customer.Id) == 50 && creditSale.DueDate == DateTime.Today.AddDays(7), "Credit sale not posted.");
        store.ReceiveAccountPayment(customer.Id, 20, "Cash", "ACCOUNT-1"); Check(store.CustomerBalance(customer.Id) == 30, "Credit payment failed.");
        Reject(() => store.Checkout([new(p.Id, p.Name, 10, 8)], 0, 0, "Credit", 0, customer.Id));
        Reject(() => store.ReceiveAccountPayment(customer.Id, 1, "Card", "ACCOUNT-1"));
        store.ReturnLines(creditSale.Number, [new(p.Id, 2, false)], "Partial credit return");
        Check(store.CustomerBalance(customer.Id) == 10 && store.CustomerStatement(customer.Id).Last().Balance == 10 && store.CustomerAging(customer.Id).Count == 1, "Account return/statement/aging incorrect.");
        store.ReturnSale(creditSale.Number, "Remaining return"); Check(store.CustomerBalance(customer.Id) == -20, "Paid returned invoice did not leave customer credit.");

        // 9,17. Split tenders and repeated partial returns, with exact refunds and restocking.
        drawer = store.ExpectedCash(shift);
        var split = store.Checkout([new(p.Id, p.Name, 10, 2)], 0, 0, "Split", 10, payments: [new("Cash", 7), new("Card", 13)]);
        Check(split.Change == 3 && store.ExpectedCash(shift) == drawer + 7, "Split cash/change incorrect.");
        var beforeStock = p.Stock; var partial = store.ReturnLines(split.Number, [new(p.Id, 1, false)], "Write off opened pack");
        Check(partial.Amount == 10 && p.Stock == beforeStock && store.RefundPayments(partial).Sum(x => x.Amount) == 10 && !store.FullyReturned(split), "Partial write-off refund incorrect.");
        Reject(() => store.ReturnLines(split.Number, [new(p.Id, 2, true)], "Over return"));
        var final = store.ReturnSale(split.Number, "Remaining sealed pack");
        Check(final.Amount == 10 && p.Stock == beforeStock + 1 && store.FullyReturned(split) && store.ExpectedCash(shift) == drawer, "Remaining return or split refund cash incorrect.");
        var cents = store.Checkout([new(p.Id, p.Name, 10, 3), new(q.Id, q.Name, 20, 2)], 3.17m, 13, "Card", 0);
        var a = store.ReturnLines(cents.Number, [new(p.Id, 1, true)], "One unit"); var b = store.ReturnLines(cents.Number, [new(q.Id, 1, true)], "One unit"); var c = store.ReturnSale(cents.Number, "All remaining");
        Check(a.Amount + b.Amount + c.Amount == cents.Total, "Partial tax/discount rounding lost money.");

        // 10. Stored-value cards: funding, split redemption, refund restoration and suspension.
        drawer = store.ExpectedCash(shift); store.IssueGiftCard("BUS-GIFT", "Recipient", DateTime.Today.AddYears(1), 50, "Cash", "GIFT-FUND");
        store.TopUpGiftCard("BUS-GIFT", 20, "Card", "GIFT-TOP"); Check(store.GiftBalance("BUS-GIFT") == 70 && store.ExpectedCash(shift) == drawer + 50, "Gift funding or drawer posting failed.");
        var giftSale = store.Checkout([new(q.Id, q.Name, 20, 1)], 0, 0, "Split", 5, payments: [new("GiftCard", 15, "BUS-GIFT"), new("Cash", 5)]);
        Check(store.GiftBalance("BUS-GIFT") == 55, "Gift redemption failed."); store.ReturnSale(giftSale.Number, "Gift return"); Check(store.GiftBalance("BUS-GIFT") == 70, "Gift refund did not restore balance.");
        store.ToggleGiftCard("BUS-GIFT"); Reject(() => store.Checkout([new(p.Id, p.Name, 10, 1)], 0, 0, "GiftCard", 0, payments: [new("GiftCard", 10, "BUS-GIFT")])); store.ToggleGiftCard("BUS-GIFT");

        // 11. Layaway reservations, deposit cash timing, final balance and cancellation refunds.
        beforeStock = p.Stock; drawer = store.ExpectedCash(shift);
        var layaway = store.CreateOrder("Layaway", customer.Id, Store.CounterLocationId, DateTime.Today.AddDays(7), "", "", [new(p.Id, p.Name, 10, 2)], 0, 0);
        Check(store.ReservedStock(p.Id, Store.CounterLocationId) == 2 && p.Stock == beforeStock, "Layaway did not reserve without consuming stock.");
        Reject(() => store.TransferStock(p.Id, Store.CounterLocationId, warehouse, p.Stock, "Must retain reservations"));
        store.PayOrder(layaway.Id, 5, "Cash", "DEPOSIT-1"); store.PayOrder(layaway.Id, 7, "Card", "DEPOSIT-2");
        var fulfilled = store.Checkout(layaway.Lines, 0, 0, "Cash", 10, customer.Id, locationId: layaway.LocationId, orderId: layaway.Id);
        Check(fulfilled.Change == 2 && fulfilled.DepositPayments.Sum(t => t.Amount) == 12 && p.Stock == beforeStock - 2 && store.ExpectedCash(shift) == drawer + 13, "Layaway fulfillment double-counted deposits or stock.");
        store.UpdateOrderState(layaway.Id, "Collected"); store.ReturnSale(fulfilled.Number, "Return collected order"); Check(store.ExpectedCash(shift) == drawer, "Layaway return did not refund all original cash portions.");
        var cancelled = store.CreateOrder("Layaway", customer.Id, Store.CounterLocationId, DateTime.Today, "", "", [new(p.Id, p.Name, 10, 1)], 0, 0); store.PayOrder(cancelled.Id, 5, "Cash", "CANCEL-DEP"); store.CancelOrder(cancelled.Id, "Customer cancelled"); Check(store.OrderDeposits(cancelled.Id) == 0 && store.ReservedStock(p.Id, Store.CounterLocationId) == 0, "Layaway cancellation failed to release reservation/refund deposit.");

        // 12. Bundle component consumption and historical recipe-safe returns.
        var bundle = new Product { Name = "Coffee starter kit", Barcode = "KIT001", Price = 35, Kind = "Bundle" }; store.SaveProduct(bundle); store.SaveBundle(bundle.Id, [new(p.Id, 2), new(q.Id, 1)]);
        beforeStock = p.Stock; var bundleSale = store.Checkout([new(bundle.Id, bundle.Name, 35, 1)], 0, 0, "Card", 0);
        Check(p.Stock == beforeStock - 2 && bundleSale.Lines.Single().Components.Count == 2, "Bundle failed to consume components.");
        store.SaveBundle(bundle.Id, [new(p.Id, 1), new(q.Id, 1)]); store.ReturnSale(bundleSale.Number, "Old recipe return"); Check(p.Stock == beforeStock, "Bundle return used edited recipe instead of sale snapshot.");
        var bundleHold = store.CreateOrder("Layaway", customer.Id, Store.CounterLocationId, DateTime.Today, "", "", [new(bundle.Id, bundle.Name, 35, 1)], 0, 0); Reject(() => store.SaveBundle(bundle.Id, [new(p.Id, 3)])); store.CancelOrder(bundleHold.Id, "Release");

        // 13. Dated quantity tiers and customer-specific price priority.
        var generalRule = new PriceRule(Guid.NewGuid(), "Wholesale", p.Id, Guid.Empty, 3, 8, DateTime.Today, DateTime.Today.AddDays(30)); store.SavePriceRule(generalRule);
        var specialRule = new PriceRule(Guid.NewGuid(), "Customer price", p.Id, customer.Id, 1, 9, DateTime.Today, DateTime.Today.AddDays(30)); store.SavePriceRule(specialRule);
        Check(store.SellingPrice(p.Id, 3, customer.Id) == 9 && store.SellingPrice(p.Id, 3) == 8, "Price book priorities incorrect.");
        Reject(() => store.Checkout([new(p.Id, p.Name, 10, 3)], 0, 0, "Card", 0));
        store.Checkout([new(p.Id, p.Name, 9, 3)], 0, 0, "Card", 0, customer.Id);
        store.SavePriceRule(specialRule with { Enabled = false }); store.SavePriceRule(generalRule with { Enabled = false });

        // 18. Delivery workflow: draft, reservation, picking, invoice, dispatch, proof of delivery.
        var delivery = store.CreateOrder("Delivery", customer.Id, warehouse, DateTime.Today.AddDays(1), "Market road", "Call before arrival", [new(q.Id, q.Name, 20, 2)], 0, 0);
        Check(store.ReservedStock(q.Id, warehouse) == 0, "Draft delivery reserved stock."); store.UpdateOrderState(delivery.Id, "Reserved"); store.UpdateOrderState(delivery.Id, "Picked");
        Reject(() => store.UpdateOrderState(delivery.Id, "Delivered"));
        var deliveredSale = store.Checkout(delivery.Lines, 0, 0, "Card", 0, customer.Id, locationId: warehouse, orderId: delivery.Id);
        store.UpdateOrderState(delivery.Id, "Dispatched", "COURIER-123"); store.UpdateOrderState(delivery.Id, "Delivered");
        Check(store.Data.Business.Orders.Single(o => o.Id == delivery.Id).SaleNumber == deliveredSale.Number && store.Data.Business.Orders.Single(o => o.Id == delivery.Id).Status == "Delivered", "Delivery invoice/tracking lifecycle failed.");

        // 19. Service intake, estimate approval, reserved parts, labor invoicing and collection.
        var labor = new Product { Name = "Repair labor", Barcode = "LABOR", Kind = "Service", Price = 50 }; store.SaveProduct(labor);
        var job = store.CreateServiceJob(customer.Id, Guid.Empty, Store.CounterLocationId, "Coffee machine", "SERIAL-123", "No power", DateTime.Today.AddDays(2));
        store.EstimateServiceJob(job.Id, "Replace filter and clean", labor.Id, 30, [new(q.Id, q.Name, 1, 20)]);
        store.UpdateServiceJob(job.Id, "Approved"); Check(store.ReservedStock(q.Id, Store.CounterLocationId) == 1, "Approved service job did not reserve parts.");
        Reject(() => store.ConfigureProduct(q.Id, "Service", 0, Guid.Empty, 0, 0)); store.UpdateServiceJob(job.Id, "In progress"); store.UpdateServiceJob(job.Id, "Ready");
        var jobSale = store.Checkout(store.ServiceJobLines(job.Id), 0, 0, "Card", 0, customer.Id, jobId: job.Id);
        Check(jobSale.Total == 50 && labor.Stock == 0 && store.ReservedStock(q.Id, Store.CounterLocationId) == 0, "Service invoice failed to consume parts and preserve non-stock labor."); store.UpdateServiceJob(job.Id, "Collected");

        // 14–16,20. Role enforcement, password hashing, attendance, audit integrity and commissions.
        store.SaveEmployee(Guid.Empty, "Owner", "owner", "Administrator", "OwnerPassword123", true, 0);
        Check(store.Data.Business.Employees.Single().PasswordHash != "OwnerPassword123", "Password stored as plain text.");
        Reject(() => store.SignIn("owner", "wrong")); store.SignIn("owner", "OwnerPassword123");
        store.SaveEmployee(Guid.Empty, "Cashier", "cashier", "Cashier", "CashierPassword123", true, 5); var cashier = store.Data.Business.Employees.Single(e => e.Username == "cashier");
        store.SignOut(); store.SignIn("cashier", "CashierPassword123");
        Reject(() => store.TransferStock(p.Id, Store.CounterLocationId, warehouse, 1, "Unauthorized")); Reject(() => store.SavePriceRule(generalRule)); Reject(() => store.RestoreBackup(store.ExportBackup()));
        store.ClockIn(); Reject(store.ClockIn); store.ClockOut(); var time = store.Data.Business.Attendance.Single();
        Reject(() => store.CorrectAttendance(time.Id, DateTimeOffset.Now.AddHours(-2), DateTimeOffset.Now.AddHours(-1), "Unauthorized correction"));
        var commissionSale = store.Checkout([new(p.Id, p.Name, 10, 2)], 0, 0, "Card", 0); Check(commissionSale.EmployeeId == cashier.Id && commissionSale.CommissionRate == 5, "Cashier/rate not captured at checkout.");
        store.SignOut(); store.SignIn("owner", "OwnerPassword123");
        store.CorrectAttendance(time.Id, DateTimeOffset.Now.AddHours(-2), DateTimeOffset.Now.AddHours(-1), "Correct forgotten punch"); Check(store.Data.Business.Attendance.Single().Hours == 1, "Attendance correction failed.");
        var balance = store.CommissionBalances().Single(b => b.EmployeeId == cashier.Id); Check(balance.Outstanding == 1, "Commission accrual incorrect.");
        store.PayCommission(cashier.Id, 1, "Cash", "COMMISSION-1"); store.ReturnLines(commissionSale.Number, [new(p.Id, 1, true)], "Return after settlement"); Check(store.CommissionBalances().Single(b => b.EmployeeId == cashier.Id).Outstanding == -0.5m, "Commission reversal/carry-forward incorrect.");
        Check(store.VerifyAudit() && store.Data.Business.Audit.Any(a => a.Actor == "cashier" && a.Action == "Checkout"), "Actor audit missing or chain invalid.");
        var audit = store.Data.Business.Audit[0]; store.Data.Business.Audit[0] = audit with { Changes = "Tampered" }; Check(!store.VerifyAudit(), "Audit tampering not detected."); store.Data.Business.Audit[0] = audit;

        // A failed write must roll back stock, tender ledgers, sales and audit as one unit.
        unchanged = store.ExportBackup();
        using (var locked = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            try { store.Checkout([new(p.Id, p.Name, 10, 1)], 0, 0, "GiftCard", 0, payments: [new("GiftCard", 10, "BUS-GIFT")]); throw new Exception("Expected locked-file failure."); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        Check(store.ExportBackup() == unchanged, "Failed checkout did not roll back all business state.");
        var reload = new Store(file); Check(reload.VerifyAudit() && reload.Data.Business.PurchaseOrders.Count > 0 && reload.Data.Business.Orders.Count == 4 && reload.Data.Business.Jobs.Single().Status == "Collected", "Business state failed to persist.");
        store.CloseShift(store.ExpectedCash(shift)); var backup = store.ExportBackup(); var restored = new Store(Path.Combine(directory, "business-restored.json")); restored.RestoreBackup(backup);
        Check(restored.VerifyAudit() && restored.Data.Business.Employees.Count == 2 && restored.Data.Business.Attendance.Single().Hours == 1, "Business backup round trip failed.");
        Console.WriteLine($"PASS: {assertions} integrated business assertions covering all twenty major workflows and transaction rollback.");
    }
}
