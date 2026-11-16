using CounterPoint.Core;

internal static class RetailChecks
{
    public static void Run(string directory)
    {
        void Check(bool condition, string reason) { if (!condition) throw new Exception(reason); }
        void Reject(Action action) { try { action(); } catch (Exception ex) when (ex is InvalidOperationException or System.Text.Json.JsonException or InvalidDataException) { return; } throw new Exception("Invalid retail operation accepted."); }
        var path = Path.Combine(directory, "retail.json"); var store = new Store(path);
        var p = new Product { Name = "Coffee, \"House\"", Barcode = "0001", Category = "Drinks", Price = 100, Stock = 20 };
        store.SaveProduct(p); var customer = new Customer(Guid.NewGuid(), "Alex", "", ""); store.SaveCustomer(customer);
        var held = store.SaveCart("Counter one", "Held", [new(p.Id, p.Name, 100, 2)], 0, 0, customer.Id, DateTime.Today);
        var quote = store.SaveCart("Estimate", "Quote", [new(p.Id, p.Name, 100, 2)], 10, 10, customer.Id, DateTime.Today.AddDays(1));
        Check(p.Stock == 20 && store.ResumeCart(held.Id).Single().Quantity == 2 && quote.Total == 209, "Held cart or quotation changed stock/totals.");
        Reject(() => store.SaveCart("Expired", "Quote", [new(p.Id, p.Name, 100, 1)], 0, 0, null, DateTime.Today.AddDays(-1)));
        store.SaveCoupon(new("save10", true, 10, 100, DateTime.Today, 1));
        store.SaveCoupon(new("EXPIRED", false, 5, 0, DateTime.Today.AddDays(-1), 1));
        Reject(() => store.CalculateDiscount(100, 0, "EXPIRED", 0, null));
        Reject(() => store.CalculateDiscount(50, 0, "SAVE10", 0, null));
        store.OpenShift("Cashier", 100); var shiftId = store.Data.Shifts.Single().Id;
        Reject(() => store.OpenShift("Second cashier", 0));
        var sale = store.Checkout([new(p.Id, p.Name, 100, 5)], 0, 0, "Cash", 500, customer.Id, "save10");
        Check(sale.Total == 450 && sale.Change == 50 && sale.PointsEarned == 4 && store.LoyaltyBalance(customer.Id) == 4 && store.ExpectedCash(shiftId) == 550, "Coupon, loyalty or drawer cash incorrect.");
        Reject(() => store.Checkout([new(p.Id, p.Name, 100, 1)], 0, 0, "Card", 0, customer.Id, "SAVE10"));
        Reject(() => store.Checkout([new(p.Id, p.Name, 100, 1)], 0, 0, "Card", 0, customer.Id, "", 5));
        var redeemed = store.Checkout([new(p.Id, p.Name, 100, 1)], 0, 0, "Card", 0, customer.Id, "", 3);
        Check(redeemed.Total == 97 && store.LoyaltyBalance(customer.Id) == 1, "Point redemption incorrect.");
        store.ReturnSale(sale.Number, "Return"); Check(store.LoyaltyBalance(customer.Id) == -3 && store.ExpectedCash(shiftId) == 100, "Return did not reverse points/cash.");
        store.ReturnSale(redeemed.Number, "Return"); Check(store.LoyaltyBalance(customer.Id) == 0, "Redeemed points not restored.");
        store.MoveCash(-20, "Petty cash"); store.MoveCash(10, "Cash added");
        Check(store.ExpectedCash(shiftId) == 90, "Drawer movements incorrect.");
        store.CloseShift(85); Check(store.Data.Shifts.Single().Variance == -5, "Shift variance incorrect.");
        Reject(() => store.MoveCash(1, "No shift"));
        var performance = store.Performance(DateTime.Today, DateTime.Today).Single();
        Check(performance.Sold == 6 && performance.Returned == 6 && performance.NetUnits == 0 && performance.NetSalesBeforeTax == 0, "Product analytics incorrect.");
        Check(store.FindSales("alex", null, null, "Cash", "Returned").Count == 1 && store.FindSales("Coffee", null, null, "Card", "Active").Count == 0, "Sales search failed.");
        var before = store.Data.Products.Single().Stock;
        store.ArchiveProduct(p.Id, true);
        Reject(() => store.Checkout([new(p.Id, p.Name, 100, 1)], 0, 0, "Card", 0));
        Reject(() => store.ResumeCart(held.Id)); Check(store.LowStock().Count == 0, "Archived product alert included.");
        store.ArchiveProduct(p.Id, false);
        var completed = store.Checkout(store.ResumeCart(held.Id), 0, 0, "Card", 0, customer.Id, savedCartId: held.Id);
        Check(!store.Data.SavedCarts.Any(c => c.Id == held.Id) && store.Data.Products.Single().Stock == before - 2, "Saved cart not consumed atomically.");
        Reject(() => store.Checkout([new(p.Id, p.Name, 100, 1)], 0, 0, "Card", 0, savedCartId: held.Id));
        var csv = store.ExportProducts();
        var imported = new Store(Path.Combine(directory, "import.json"));
        Check(imported.ImportProducts(csv) == 1 && imported.Data.Products.Single().Name == p.Name && imported.Data.Products.Single().Barcode == "0001", "Quoted CSV round trip failed.");
        var original = imported.ExportProducts();
        Reject(() => imported.ImportProducts(csv + "\r\n\"0002\",\"Bad\",\"Test\",-1,1,1,False"));
        Check(imported.ExportProducts() == original, "Failed CSV import changed inventory.");
        Check(imported.ImportProducts(csv) == 1 && imported.Data.Products.Count == 1, "CSV barcode upsert duplicated product.");
        var backup = store.ExportBackup(); var restored = new Store(Path.Combine(directory, "restore.json")); restored.RestoreBackup(backup);
        Check(restored.Data.Sales.Count == 3 && restored.Data.SavedCarts.Single().Kind == "Quote" && restored.Data.Coupons.Count == 2 && restored.Data.Shifts.Single().Variance == -5 && restored.LoyaltyBalance(customer.Id) == 2, "Backup lost retail data.");
        var restoreSnapshot = restored.ExportBackup(); Reject(() => restored.RestoreBackup(backup.Replace("CounterPoint/1", "CounterPoint/999")));
        Check(restored.ExportBackup() == restoreSnapshot, "Invalid backup modified store.");
        restored.OpenShift("Restoration guard", 0); Reject(() => restored.RestoreBackup(backup)); restored.CloseShift(0);
        var reloaded = new Store(path); Check(reloaded.Data.Coupons.Count == 2 && reloaded.Data.SavedCarts.Count == 1 && reloaded.Data.Shifts.Count == 1, "Retail state not persisted.");
        Console.WriteLine("PASS: held carts, quotations, coupons, loyalty, cash shifts, CSV, backup/restore, sales search, analytics and archiving.");
    }
}
