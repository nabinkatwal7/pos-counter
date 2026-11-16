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
    Console.WriteLine("PASS: product validation, stock limits, insufficient cash, checkout math, persistence, receipts and card sales.");
}
finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
