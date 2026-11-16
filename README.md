# CounterPoint

A Windows desktop point of sale app built with .NET 10 and WPF, using Microsoft's built-in **PresentationFramework.Fluent** light theme and native Fluent control templates. No third-party packages or database server are required.

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

## Retail tools: ten more features

1. **Held carts:** use **Hold cart** on Sell, then resume from Retail tools → Held carts. Carts persist across restarts without reserving stock. Successful checkout removes the saved entry atomically. Clearing a resumed cart leaves the saved entry available.
2. **Quotations:** save an expiring quotation from Sell, view/print it, and load it into checkout. Quotes are estimates, not receipts; current prices and stock are rechecked when loading. Coupons and loyalty points are applied at checkout rather than saved in quotations or held carts.
3. **Coupons:** create fixed or percentage codes with a minimum subtotal, expiry, total usage limit and enable/disable control. Apply a code in Sell. Discounts apply in this order: manual amount, coupon, then points. Returned sales still count toward the coupon usage limit.
4. **Loyalty points:** linked customers earn one point per full 100 of the paid total; each point discounts one currency unit before tax. Select a customer and enter points on Sell. Returns reverse the original points transaction. A balance can become negative if points earned on a returned sale were already spent; further redemption is blocked until sufficient points accrue.
5. **Cash shift reconciliation:** open a named shift with a starting float, record cash in/out, then close it with a counted balance to see the variance. Cash sales/refunds are attached to the currently open shift. Sales without an open shift are excluded. Purchases and expenses do not automatically change drawer cash; record a cash-out movement when paid from the drawer.
6. **Product CSV import/export:** export a template under Data & backup. Headers are `Barcode,Name,Category,Price,Stock,ReorderLevel,Archived`; decimal amounts use a dot. Matching barcodes update products, including stock and archive status; omitted products are preserved. Imports validate all rows before saving, preserve leading barcode zeros, support quoted fields, and log stock differences. Hold or clear the active cart first.
7. **Backup and restore:** create a `.cpbackup` file containing the complete shop state and checksum. Restore validates the file, requires an empty cart and closed current shift, and saves a recovery backup beside `store.json` before replacing data. Recovery filenames start with `store.json.before-restore-`. Backups contain customer details and financial history; keep them in your chosen secure location.
8. **Sales search and filters:** search receipt number, customer or product under Sales history; optionally filter dates, payment method and returned/active status. Press Search sales to apply or Reset filters to clear.
9. **Product performance analytics:** rank products for a date range by net sales before tax, with sold, returned and net units. Discounts are allocated proportionally across items. Returns are counted on the return date; figures are revenue, not profit. Run analysis to refresh after transactions.
10. **Product archiving:** remove discontinued products from the selling catalog and reorder alerts while preserving stock, receipt and report history. Restore them from Product archive to sell again.

The two management areas use side navigation to keep the growing feature set accessible. The app uses Microsoft's WPF Fluent implementation, rather than embedding the web-only Fluent UI React components.

## Storage

Data is saved atomically to `%LOCALAPPDATA%\CounterPoint\store.json`. Back up this file regularly with the app closed. Existing store files load with defaults for the new features. This version supports one running app at one counter; it does not coordinate multiple processes or counters. It has no staff authentication, payment terminal integration or jurisdiction-specific fiscal certification.

## Verify and publish

```powershell
dotnet run --project CounterPoint.Tests
dotnet run --project CounterPoint.UiChecks
dotnet build CounterPoint/CounterPoint.csproj
dotnet publish CounterPoint/CounterPoint.csproj -c Release -r win-x64 --self-contained true -o publish
```

The verification executables check checkout calculations, validation, inventory updates, all twenty added workflows, persistence, legacy data, write rollback, CSV/backup round trips, and WPF page initialization/layout with populated sample data. UI checks use an isolated temporary shop and do not change your shop data.
