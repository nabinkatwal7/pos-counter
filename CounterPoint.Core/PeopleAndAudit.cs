using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CounterPoint.Core;
public sealed partial class Store
{
    public Employee? CurrentEmployee { get; private set; }
    private readonly Dictionary<string, (int Attempts, DateTimeOffset Until)> loginAttempts = new(StringComparer.OrdinalIgnoreCase);
    public bool HasStaffAccounts => Data.Business.Employees.Count > 0;
    public bool CanManage => !HasStaffAccounts || CurrentEmployee?.Role is "Manager" or "Administrator";
    public void EnsureAccess(string operation)
    {
        if (!HasStaffAccounts) return;
        Require(CurrentEmployee != null && Data.Business.Employees.Any(e => e.Id == CurrentEmployee.Id && e.Active), "Sign in with an active employee account.");
        if (CurrentEmployee!.Role == "Administrator") return;
        if (operation is nameof(SaveEmployee) or nameof(RestoreBackup)) throw new InvalidOperationException("Only an administrator can manage employees or restore backups.");
        if (CurrentEmployee.Role == "Manager") return;
        Require(new[] { nameof(Checkout), nameof(SaveCart), nameof(DeleteSavedCart), nameof(OpenShift), nameof(CloseShift), nameof(ClockIn), nameof(ClockOut), nameof(CreateOrder), nameof(PayOrder), nameof(UpdateOrderState), nameof(ReceiveAccountPayment), "SignIn", "SignOut" }.Contains(operation), "This action requires a manager or administrator.");
    }
    public void SaveEmployee(Guid id, string name, string username, string role, string password, bool active, decimal commission)
    {
        Require(!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(username) && role is "Administrator" or "Manager" or "Cashier" && commission >= 0 && commission <= 100, "Enter a name, username, supported role and commission rate from 0 to 100.");
        var old = Data.Business.Employees.SingleOrDefault(e => e.Id == id);
        Require(!Data.Business.Employees.Any(e => e.Id != id && e.Username.Equals(username.Trim(), StringComparison.OrdinalIgnoreCase)), "Username already exists.");
        Require(HasStaffAccounts || (role == "Administrator" && active), "The first employee must be an active administrator.");
        Require(old == null || old.Role != "Administrator" || (role == "Administrator" && active) || Data.Business.Employees.Any(e => e.Id != id && e.Role == "Administrator" && e.Active), "Keep at least one active administrator.");
        Require(old != null || password.Length >= 8, "Use a password of at least eight characters.");
        Require(password.Length == 0 || password.Length >= 8, "Use a password of at least eight characters, or leave blank to keep the current password.");
        var salt = password.Length == 0 ? old!.Salt : Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var hash = password.Length == 0 ? old!.PasswordHash : HashPassword(password, salt);
        var employee = new Employee(id == Guid.Empty ? Guid.NewGuid() : id, name.Trim(), username.Trim(), role, salt, hash, active, commission);
        Commit(() => { if (old != null) Data.Business.Employees.Remove(old); Data.Business.Employees.Add(employee); });
        if (CurrentEmployee?.Id == employee.Id) CurrentEmployee = employee.Active ? employee : null;
    }
    private static string HashPassword(string password, string salt) => Convert.ToHexString(Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromHexString(salt), 120_000, HashAlgorithmName.SHA256, 32));
    public void SignIn(string username, string password)
    {
        username = username.Trim();
        if (loginAttempts.TryGetValue(username, out var attempt) && attempt.Until > DateTimeOffset.Now) throw new InvalidOperationException("Too many unsuccessful attempts. Try again in five minutes.");
        var employee = Data.Business.Employees.SingleOrDefault(e => e.Active && e.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
        if (employee == null || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(HashPassword(password, employee.Salt)), Encoding.UTF8.GetBytes(employee.PasswordHash)))
        {
            var count = attempt.Attempts + 1; loginAttempts[username] = (count, count >= 5 ? DateTimeOffset.Now.AddMinutes(5) : DateTimeOffset.MinValue);
            throw new InvalidOperationException("Incorrect username or password.");
        }
        var previous = CurrentEmployee; CurrentEmployee = employee;
        try { Commit(() => { }); } catch { CurrentEmployee = previous; throw; }
        loginAttempts.Remove(username);
    }
    public void SignOut() { if (CurrentEmployee != null) Commit(() => { }); CurrentEmployee = null; }
    public void ClockIn()
    {
        Require(CurrentEmployee != null, "Sign in before clocking in.");
        Require(!Data.Business.Attendance.Any(a => a.EmployeeId == CurrentEmployee!.Id && a.ClockOut == null), "You are already clocked in.");
        Commit(() => Data.Business.Attendance.Add(new(Guid.NewGuid(), CurrentEmployee!.Id, CurrentEmployee.Name, DateTimeOffset.Now, null)));
    }
    public void ClockOut()
    {
        Require(CurrentEmployee != null, "Sign in before clocking out.");
        var entry = Data.Business.Attendance.SingleOrDefault(a => a.EmployeeId == CurrentEmployee!.Id && a.ClockOut == null);
        Require(entry != null, "There is no open attendance entry.");
        Commit(() => Data.Business.Attendance[Data.Business.Attendance.IndexOf(entry!)] = entry! with { ClockOut = DateTimeOffset.Now });
    }
    public void CorrectAttendance(Guid id, DateTimeOffset clockIn, DateTimeOffset clockOut, string reason)
    {
        var entry = Data.Business.Attendance.Single(a => a.Id == id);
        Require(clockOut >= clockIn && clockOut <= DateTimeOffset.Now && !string.IsNullOrWhiteSpace(reason), "Enter a valid time range ending in the past and a correction reason.");
        Require(!Data.Business.Attendance.Any(a => a.Id != id && a.EmployeeId == entry.EmployeeId && a.ClockIn < clockOut && (a.ClockOut ?? DateTimeOffset.MaxValue) > clockIn), "Attendance entries cannot overlap.");
        Commit(() => Data.Business.Attendance[Data.Business.Attendance.IndexOf(entry)] = entry with { ClockIn = clockIn, ClockOut = clockOut, CorrectionReason = reason });
    }
    public IReadOnlyList<CommissionBalance> CommissionBalances()
    {
        return Data.Business.Employees.Select(employee =>
        {
            var earned = Data.Sales.Where(s => s.EmployeeId == employee.Id).Sum(s => Round((s.Subtotal - s.Discount) * (s.Total == 0 ? 0 : 1 - Refunded(s.Number) / s.Total) * s.CommissionRate / 100));
            var paid = Data.Business.CommissionPayments.Where(p => p.EmployeeId == employee.Id).Sum(p => p.Amount);
            return new CommissionBalance(employee.Id, employee.Name, earned, paid, earned - paid);
        }).ToList();
    }
    public void PayCommission(Guid employeeId, decimal amount, string method, string reference)
    {
        Require(amount > 0 && amount <= CommissionBalances().Single(c => c.EmployeeId == employeeId).Outstanding && method is "Cash" or "Card" && !string.IsNullOrWhiteSpace(reference), "Enter a positive amount within the outstanding commission and a payment reference.");
        Commit(() =>
        {
            Data.Business.CommissionPayments.Add(new(Guid.NewGuid(), employeeId, DateTimeOffset.Now, amount, reference, method, ActiveShiftId));
            DrawerOut(amount, method, "Commission: " + reference);
        });
    }
    private void AppendAudit(string action, string before)
    {
        var previous = Data.Business.Audit.LastOrDefault()?.Hash ?? "";
        var sequence = (Data.Business.Audit.LastOrDefault()?.Sequence ?? 0) + 1;
        var time = DateTimeOffset.Now; var actor = CurrentEmployee?.Username ?? "Owner (local setup)";
        using var old = JsonDocument.Parse(before); using var updated = JsonDocument.Parse(JsonSerializer.Serialize(Data));
        var changes = new List<string>();
        void Compare(JsonElement a, JsonElement b, string prefix)
        {
            foreach (var property in b.EnumerateObject())
            {
                if (property.Name == "Audit") continue;
                var found = a.TryGetProperty(property.Name, out var original);
                if (found && original.GetRawText() == property.Value.GetRawText()) continue;
                if (property.Name == "Business" && found) { Compare(original, property.Value, "Business."); continue; }
                changes.Add(prefix + property.Name + (property.Value.ValueKind == JsonValueKind.Array ? $" ({(found ? original.GetArrayLength() : 0)} → {property.Value.GetArrayLength()} records)" : " updated"));
            }
        }
        Compare(old.RootElement, updated.RootElement, "");
        var detail = string.Join("; ", changes); var hash = AuditHash(sequence, time, actor, action, detail, previous);
        Data.Business.Audit.Add(new(sequence, time, actor, action, detail, previous, hash));
    }
    private static string AuditHash(long sequence, DateTimeOffset time, string actor, string action, string changes, string previous) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { sequence, time, actor, action, changes, previous }))));
    public bool VerifyAudit()
    {
        var previous = ""; long expected = 1;
        foreach (var a in Data.Business.Audit)
        {
            if (a.Sequence != expected++ || a.PreviousHash != previous || a.Hash != AuditHash(a.Sequence, a.Time, a.Actor, a.Action, a.Changes, a.PreviousHash)) return false;
            previous = a.Hash;
        }
        return true;
    }
    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    private Guid ActiveShiftId => Data.Shifts.SingleOrDefault(s => s.Closed == null)?.Id ?? Guid.Empty;
    private void DrawerOut(decimal amount, string method, string reason)
    {
        if (method == "Cash" && ActiveShiftId != Guid.Empty) Data.CashEntries.Add(new(ActiveShiftId, DateTimeOffset.Now, -amount, reason));
    }
}
