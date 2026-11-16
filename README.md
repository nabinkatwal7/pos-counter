# CounterPoint

A Windows desktop point of sale app built with .NET 10 and WPF. No third-party packages or database server are required.

## Run

Install the .NET 10 SDK on Windows, then run:

```powershell
dotnet run --project CounterPoint
```

Start in **Inventory** and create products with unique barcodes/SKUs, prices and stock quantities. In **Sell**, search or scan an exact barcode and press Enter, or double-click a product. Adjust cart quantities, enter a discount amount and tax percentage, select Cash or Card, and complete the sale. Cash requires an amount received. Card records an externally processed payment; it does not charge a card. Saved receipts can be viewed and printed from Sales history.

All amounts use the computer's number format, with no currency assumption. Tax defaults to zero; configure it per sale to match your shop's requirements.

## Storage

Data is saved atomically to `%LOCALAPPDATA%\CounterPoint\store.json`. Back up this file regularly with the app closed. This version supports one running app at one counter; it does not coordinate multiple processes or counters. It has no staff authentication, refunds, payment terminal integration or jurisdiction-specific fiscal certification.

## Verify and publish

```powershell
dotnet run --project CounterPoint.Tests
dotnet build CounterPoint/CounterPoint.csproj
dotnet publish CounterPoint/CounterPoint.csproj -c Release -r win-x64 --self-contained true -o publish
```

The verification executable checks checkout calculations, validation, inventory updates, persistence and receipt content without external test packages.
