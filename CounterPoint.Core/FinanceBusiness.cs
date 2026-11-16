namespace CounterPoint.Core;

public sealed partial class Store
{
    public IReadOnlyList<PaymentPart> PaymentsFor(Sale sale) => sale.Payments.Count == 0 && sale.DepositPayments.Count == 0 ? [new(sale.Payment, sale.Total)] : sale.DepositPayments.Concat(sale.Payments).ToList();
    public decimal PaidBy(Sale sale, string method) => PaymentsFor(sale).Where(p => p.Method == method).Sum(p => p.Amount);
    public decimal Refunded(string receipt) => Data.Returns.Where(r => r.SaleNumber == receipt).Sum(r => r.Amount);
    public int ReturnedQuantity(Sale sale, Guid productId) => Data.Returns.Where(r => r.SaleNumber == sale.Number).Sum(r => r.Lines.Count == 0 ? sale.Lines.Single(l => l.ProductId == productId).Quantity : r.Lines.Where(l => l.ProductId == productId).Sum(l => l.Quantity));
    public bool FullyReturned(Sale sale) => sale.Lines.All(l => ReturnedQuantity(sale, l.ProductId) >= l.Quantity);
    public IReadOnlyList<PaymentPart> RefundPayments(SaleReturn returned)
    {
        if (returned.Payments.Count > 0) return returned.Payments;
        return [new(returned.Payment, returned.Amount)];
    }
    public decimal CustomerBalance(Guid customerId)
    {
        var sales = Data.Sales.Where(s => s.CustomerId == customerId).ToList();
        return sales.Sum(s => PaidBy(s, "Credit")) - Data.Returns.Where(r => sales.Any(s => s.Number == r.SaleNumber)).Sum(r => RefundPayments(r).Where(p => p.Method == "Credit").Sum(p => p.Amount)) - Data.Business.AccountPayments.Where(p => p.CustomerId == customerId).Sum(p => p.Amount);
    }
    public void SaveCreditAccount(Guid customerId, decimal limit, int terms, bool enabled)
    {
        Require(Data.Customers.Any(c => c.Id == customerId) && limit >= 0 && limit >= CustomerBalance(customerId) && terms >= 0 && terms <= 365, "Select a customer, a limit covering current debt and terms from 0 to 365 days.");
        Commit(() => { Data.Business.CreditAccounts.RemoveAll(a => a.CustomerId == customerId); Data.Business.CreditAccounts.Add(new(customerId, limit, terms, enabled)); });
    }
    public void ReceiveAccountPayment(Guid customerId, decimal amount, string method, string reference)
    {
        Require(amount > 0 && amount <= CustomerBalance(customerId) && method is "Cash" or "Card" && !string.IsNullOrWhiteSpace(reference), "Enter a positive payment within the customer balance and a reference.");
        Require(!Data.Business.AccountPayments.Any(p => p.CustomerId == customerId && p.Reference.Equals(reference.Trim(), StringComparison.OrdinalIgnoreCase)), "This customer payment reference already exists.");
        Commit(() => Data.Business.AccountPayments.Add(new(Guid.NewGuid(), DateTimeOffset.Now, customerId, amount, method, reference.Trim(), ActiveShiftId)));
    }
    public IReadOnlyList<AccountStatementRow> CustomerStatement(Guid customerId)
    {
        var sales = Data.Sales.Where(s => s.CustomerId == customerId).ToList();
        var entries = sales.Where(s => PaidBy(s, "Credit") != 0).Select(s => (s.Time, s.Number, Type: "Credit sale", Debit: PaidBy(s, "Credit"), Credit: 0m))
            .Concat(Data.Returns.Where(r => sales.Any(s => s.Number == r.SaleNumber)).Select(r => (r.Time, r.SaleNumber, "Return credit", 0m, RefundPayments(r).Where(p => p.Method == "Credit").Sum(p => p.Amount))))
            .Concat(Data.Business.AccountPayments.Where(p => p.CustomerId == customerId).Select(p => (p.Time, p.Reference, "Payment", 0m, p.Amount))).OrderBy(e => e.Time).ToList();
        decimal balance = 0; return entries.Select(e => new AccountStatementRow(e.Item1, e.Item2, e.Item3, e.Item4, e.Item5, balance += e.Item4 - e.Item5)).ToList();
    }
    public IReadOnlyList<object> CustomerAging(Guid customerId)
    {
        var paid = Data.Business.AccountPayments.Where(p => p.CustomerId == customerId).Sum(p => p.Amount);
        var rows = new List<object>();
        foreach (var sale in Data.Sales.Where(s => s.CustomerId == customerId && PaidBy(s, "Credit") > 0).OrderBy(s => s.Time))
        {
            var returned = Data.Returns.Where(r => r.SaleNumber == sale.Number).Sum(r => RefundPayments(r).Where(p => p.Method == "Credit").Sum(p => p.Amount));
            var remaining = Math.Max(0, PaidBy(sale, "Credit") - returned); var applied = Math.Min(paid, remaining); paid -= applied; remaining -= applied;
            if (remaining > 0) rows.Add(new { Receipt = sale.Number, sale.DueDate, Outstanding = remaining, OverdueDays = Math.Max(0, (DateTime.Today - sale.DueDate.Date).Days) });
        }
        return rows;
    }
    public decimal GiftBalance(string code) => Data.Business.GiftEntries.Where(e => e.Code.Equals(code.Trim(), StringComparison.OrdinalIgnoreCase)).Sum(e => e.Amount);
    public void IssueGiftCard(string code, string recipient, DateTime expires, decimal amount, string method, string reference)
    {
        code = code.Trim().ToUpperInvariant();
        Require(code.Length >= 4 && !Data.Business.GiftCards.Any(g => g.Code == code) && expires.Date >= DateTime.Today && amount > 0 && method is "Cash" or "Card" && !string.IsNullOrWhiteSpace(reference), "Enter a unique code (at least four characters), future expiry, positive funding and reference.");
        Commit(() => { Data.Business.GiftCards.Add(new(code, recipient, expires.Date)); Data.Business.GiftEntries.Add(new(Guid.NewGuid(), DateTimeOffset.Now, code, "Issue", amount, reference, method, ActiveShiftId)); });
    }
    public void TopUpGiftCard(string code, decimal amount, string method, string reference)
    {
        var card = Data.Business.GiftCards.Single(g => g.Code.Equals(code.Trim(), StringComparison.OrdinalIgnoreCase));
        Require(card.Active && card.Expires >= DateTime.Today && amount > 0 && method is "Cash" or "Card" && !string.IsNullOrWhiteSpace(reference), "Select an active unexpired card and enter positive funding with a reference.");
        Require(!Data.Business.GiftEntries.Any(e => e.Code == card.Code && e.Type is "Issue" or "Top-up" && e.Reference.Equals(reference.Trim(), StringComparison.OrdinalIgnoreCase)), "This funding reference already exists.");
        Commit(() => Data.Business.GiftEntries.Add(new(Guid.NewGuid(), DateTimeOffset.Now, card.Code, "Top-up", amount, reference.Trim(), method, ActiveShiftId)));
    }
    public void ToggleGiftCard(string code)
    {
        var card = Data.Business.GiftCards.Single(g => g.Code == code);
        Commit(() => Data.Business.GiftCards[Data.Business.GiftCards.IndexOf(card)] = card with { Active = !card.Active });
    }
    private void ValidatePayments(List<PaymentPart> payments, decimal due, decimal cashReceived, Guid? customerId)
    {
        Require(payments.All(p => p.Amount > 0 && p.Method is "Cash" or "Card" or "Credit" or "GiftCard") && payments.Sum(p => p.Amount) == due, "Payment portions must be positive and add up exactly to the amount due.");
        var cash = payments.Where(p => p.Method == "Cash").Sum(p => p.Amount);
        Require(cash == 0 || cashReceived >= cash, "Cash received is below the cash portion.");
        var credit = payments.Where(p => p.Method == "Credit").Sum(p => p.Amount);
        if (credit > 0)
        {
            var account = Data.Business.CreditAccounts.SingleOrDefault(a => a.CustomerId == customerId);
            Require(account != null && account.Enabled && CustomerBalance(account.CustomerId) + credit <= account.Limit, "Select a customer with an enabled credit account and sufficient credit limit.");
        }
        foreach (var group in payments.Where(p => p.Method == "GiftCard").GroupBy(p => p.Reference.Trim().ToUpperInvariant()))
        {
            var card = Data.Business.GiftCards.SingleOrDefault(g => g.Code == group.Key);
            Require(card != null && card.Active && card.Expires >= DateTime.Today && GiftBalance(group.Key) >= group.Sum(p => p.Amount), "Gift card is invalid, suspended, expired or has insufficient balance.");
        }
    }
    private List<PaymentPart> AllocateRefund(Sale sale, decimal before, decimal after)
    {
        var tenders = PaymentsFor(sale).GroupBy(p => (p.Method, Reference: p.Reference.Trim().ToUpperInvariant())).Select(g => new PaymentPart(g.Key.Method, g.Sum(p => p.Amount), g.Key.Reference)).ToList();
        if (sale.Total == 0) return [];
        decimal cumulative = 0;
        return tenders.Select(p =>
        {
            var start = cumulative; cumulative += p.Amount;
            decimal Allocation(decimal value) => Round(value * cumulative / sale.Total) - Round(value * start / sale.Total);
            return p with { Amount = Allocation(after) - Allocation(before) };
        }).Where(p => p.Amount > 0).ToList();
    }
    public SaleReturn ReturnLines(string saleNumber, IEnumerable<ReturnItem> requested, string reason)
    {
        var sale = Data.Sales.Single(s => s.Number == saleNumber); var lines = requested.ToList();
        Require(lines.Count > 0 && lines.Select(l => l.ProductId).Distinct().Count() == lines.Count && !string.IsNullOrWhiteSpace(reason), "Select distinct return items and enter a reason.");
        decimal amount = 0; decimal cumulative = 0;
        foreach (var original in sale.Lines)
        {
            var beforeLine = cumulative; cumulative += original.Total;
            var charge = sale.Subtotal == 0 ? 0 : Round(sale.Total * cumulative / sale.Subtotal) - Round(sale.Total * beforeLine / sale.Subtotal);
            var requestedLine = lines.SingleOrDefault(l => l.ProductId == original.ProductId);
            if (requestedLine == null) continue;
            var already = ReturnedQuantity(sale, original.ProductId);
            Require(requestedLine.Quantity > 0 && requestedLine.Quantity <= original.Quantity - already, "Return quantity exceeds the unreturned quantity on the receipt.");
            amount += Round(charge * (already + requestedLine.Quantity) / original.Quantity) - Round(charge * already / original.Quantity);
        }
        Require(lines.All(l => sale.Lines.Any(s => s.ProductId == l.ProductId)), "A return item does not belong to this sale.");
        var refunded = Refunded(saleNumber); var payments = AllocateRefund(sale, refunded, refunded + amount);
        var result = new SaleReturn(saleNumber, DateTimeOffset.Now, reason, amount, sale.Payment) { ShiftId = ActiveShiftId == Guid.Empty ? null : ActiveShiftId, Lines = lines, Payments = payments };
        Commit(() =>
        {
            foreach (var line in lines.Where(l => l.Restock))
            {
                var original = sale.Lines.Single(l => l.ProductId == line.ProductId);
                var components = original.Components.Count > 0 ? original.Components : UnitComponents(Data.Products.Single(p => p.Id == original.ProductId));
                foreach (var part in components) ChangeStock(part.ProductId, sale.LocationId, checked(part.Quantity * line.Quantity), "Return " + saleNumber + ": " + reason);
            }
            foreach (var gift in payments.Where(p => p.Method == "GiftCard")) Data.Business.GiftEntries.Add(new(Guid.NewGuid(), result.Time, gift.Reference, "Refund", gift.Amount, saleNumber, "GiftCard", ActiveShiftId));
            Data.Returns.Add(result);
        }); return result;
    }
}
