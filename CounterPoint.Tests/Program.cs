using CounterPoint.Core;
var directory = Path.Combine(Path.GetTempPath(), "CounterPointTests-" + Guid.NewGuid());
var path = Path.Combine(directory, "store.json");
try
{
    var store = new Store(path);
    var product = new Product { Name = "Tea", Barcode = "1001", Price = 10, Stock = 5 };
    store.SaveProduct(product);
    void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    void Reject(Action action) { try { action(); } catch (InvalidOperationException) { return; } throw new Exception("Invalid operation accepted"); }
    Reject(() => store.SaveProduct(new Product { Name = "Duplicate", Barcode = "1001" }));
    Reject(() => store.Checkout([new(product.Id, product.Name, 10, 6)], 0, 0, "Cash", 100));
    Reject(() => store.Checkout([new(product.Id, product.Name, 10, 2)], 0, 0, "Cash", 1));
    Check(product.Stock == 5 && store.Data.Sales.Count == 0, "Rejected sale changed data");
    var sale = store.Checkout([new(product.Id, product.Name, 10, 2)], 2, 10, "Cash", 25);
    Check(sale.Total == 19.8m && sale.Change == 5.2m && product.Stock == 3, "Incorrect checkout totals or stock");
    var reloaded = new Store(path);
    Check(reloaded.Data.Sales.Single().Total == 19.8m && reloaded.Data.Products.Single().Stock == 3, "Persistence failed");
    Check(Store.Receipt(sale).Contains(sale.Number), "Missing receipt identifier");
    store.Checkout([new(product.Id, product.Name, 10, 1)], 0, 0, "Card", 0);
    Check(store.Data.Sales.Last().Change == 0, "Card change incorrect");
    var customer = new Customer(Guid.NewGuid(), "Sam", "123", "sam@example.test");
    store.SaveCustomer(customer); store.SaveCustomer(customer with { Phone = "456" });
    Check(store.Data.Customers.Count == 1 && store.Data.Customers[0].Phone == "456", "Customer update failed");
    Reject(() => store.SaveCustomer(customer with { Name = "" }));
    store.SaveSettings(new() { Name = "Test Shop", Currency = "NPR", DefaultTax = 13 });
    Reject(() => store.SaveSettings(new() { DefaultTax = 101 }));
    var linked = store.Checkout([new(product.Id, product.Name, 10, 1)], 0, 13, "Card", 0, customer.Id);
    Check(linked.CustomerName == "Sam" && linked.CustomerId == customer.Id && Store.Receipt(linked).Contains("Test Shop"), "Customer/receipt snapshot missing");
    store.SaveCustomer(customer with { Name = "New name" }); Check(linked.CustomerName == "Sam", "Historical customer name changed");
    var supplier = new Supplier(Guid.NewGuid(), "Wholesale", "555", "Market"); store.SaveSupplier(supplier);
    store.ReceivePurchase(supplier.Id, product.Id, 10, 4, "INV-1");
    Check(product.Stock == 11 && store.Data.Purchases.Single().Total == 40, "Purchase receipt failed");
    Reject(() => store.ReceivePurchase(supplier.Id, product.Id, 10, 4, "INV-1"));
    Reject(() => store.ReceivePurchase(supplier.Id, product.Id, -1, 4, "INV-2"));
    store.AdjustStock(product.Id, -2, "Damaged");
    Check(product.Stock == 9 && store.Data.Movements.Last().Reason == "Damaged", "Stock audit failed");
    Reject(() => store.AdjustStock(product.Id, -100, "Invalid"));
    store.SetReorderLevel(product.Id, 9); Check(store.LowStock().Count == 1, "Low-stock boundary failed");
    store.SetReorderLevel(product.Id, 8); Check(store.LowStock().Count == 0, "Low-stock threshold failed");
    store.AddExpense("Rent", "Shop rent", 100); Reject(() => store.AddExpense("Rent", "Invalid", -1));
    var returned = store.ReturnSale(sale.Number, "Customer returned all items");
    Check(returned.Amount == 19.8m && product.Stock == 11, "Return failed to refund tax/discount or restore stock");
    Reject(() => store.ReturnSale(sale.Number, "Duplicate"));
    var report = store.Report(DateTime.Today, DateTime.Today).Single();
    Check(report.Gross == 41.1m && report.Refunds == 19.8m && report.Net == 21.3m && report.Expenses == 100 && report.Purchases == 40 && report.CashSales == 19.8m && report.CardSales == 21.3m, "Report totals incorrect");
    Reject(() => store.Report(DateTime.Today, DateTime.Today.AddDays(-1)));
    reloaded = new Store(path);
    Check(reloaded.Data.Returns.Count == 1 && reloaded.Data.Customers.Count == 1 && reloaded.Data.Suppliers.Count == 1 && reloaded.Data.Purchases.Count == 1 && reloaded.Data.Expenses.Count == 1 && reloaded.Data.Settings.DefaultTax == 13 && reloaded.Data.Movements.Count == 3, "New feature persistence failed");
    var legacyPath = Path.Combine(directory, "legacy.json"); File.WriteAllText(legacyPath, "{\"Products\":[],\"Sales\":[]}");
    Check(new Store(legacyPath).Data.Settings.Name == "COUNTERPOINT", "Legacy data compatibility failed");
    var failedPath = Path.Combine(directory, "blocked.json"); var failing = new Store(failedPath); Directory.CreateDirectory(failedPath);
    try { failing.AddExpense("Test", "Save failure", 1); throw new Exception("Expected save failure"); }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Check(failing.Data.Expenses.Count == 0, "Failed write left unsaved changes"); }
    RetailChecks.Run(directory);
    Console.WriteLine("PASS: all existing checks and ten new retail features, validation, persistence and rollback.");
}
finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
