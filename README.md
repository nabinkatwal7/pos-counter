# CounterPoint

A Windows desktop point of sale app built with .NET 10 and WPF. No third-party packages or database server are required.

## Run

Install the .NET 10 SDK on Windows, then run:

```powershell
dotnet run --project CounterPoint
```

Start in **Inventory** and create products with unique barcodes/SKUs, prices and stock quantities. In **Sell**, search or scan an exact barcode and press Enter, or double-click a product. Adjust cart quantities, enter a discount amount and tax percentage, select Cash or Card, and complete the sale. Cash requires an amount received. Card records an externally processed payment; it does not charge a card. Saved receipts can be viewed and printed from Sales history.

All amounts use the computer's number format. Set a currency label and default tax under Shop operations → Settings, or override tax per sale.

## Shop operations

Ten added features are available through **Shop operations** and the customer selector on **Sell**:

1. **Customer directory:** add and edit names, phone numbers and email addresses.
2. **Customer purchase history:** link a customer at checkout and inspect their purchases, return status and net spending.
3. **Full-sale returns:** select an original sale, enter a reason and record its full refund including tax and discounts. Stock is restored and duplicate returns are blocked. All items must be suitable for restocking. Issue actual refunds separately through cash or your terminal.
4. **Stock audit:** increase or decrease stock with a reason and review the movement ledger. Existing product quantities are adjusted here rather than in the product editor.
5. **Low-stock alerts:** see products at or below configurable reorder thresholds; defaults to five units.
6. **Supplier directory:** add and edit supplier contact details.
7. **Purchase receiving:** select a supplier and product, enter delivery reference, quantity and unit cost; stock and purchase totals are saved together. Receive multi-product deliveries as separate entries sharing a reference.
8. **Expense tracking:** record categorized shop operating expenses.
9. **Daily reports and CSV export:** select a date range to see sales, refunds, net sales, expenses, purchases and cash/card sales. Refunds are counted on the return date. Net sales includes tax; it is not profit. Rerun the report after new transactions.
10. **Shop settings:** configure shop identity, currency label, default tax and receipt footer. New sales capture these details so historical receipts remain unchanged.

Returns and stock deliveries affect inventory immediately. Cash/card totals in reports show original sales before refunds; refunds are shown separately. Returns are full-sale only; partial returns are not supported.

## Storage

Data is saved atomically to `%LOCALAPPDATA%\CounterPoint\store.json`. Back up this file regularly with the app closed. Existing store files load with defaults for the new features. This version supports one running app at one counter; it does not coordinate multiple processes or counters. It has no staff authentication, payment terminal integration or jurisdiction-specific fiscal certification.

## Verify and publish

```powershell
dotnet run --project CounterPoint.Tests
dotnet build CounterPoint/CounterPoint.csproj
dotnet publish CounterPoint/CounterPoint.csproj -c Release -r win-x64 --self-contained true -o publish
```

The verification executable checks checkout calculations, validation, inventory updates, persistence and receipt content without external test packages.
