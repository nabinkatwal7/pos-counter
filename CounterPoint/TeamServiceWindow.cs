using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using CounterPoint.Core;

namespace CounterPoint;
public partial class MainWindow
{
    private TextBlock sessionLabel = null!, auditHint = null!;
    private TextBox auditSearch = null!;
    private TabControl teamTabs = null!;
    private void BuildTeamServices()
    {
        teamTabs = new TabControl { TabStripPlacement = Dock.Left }; MainTabs.Items.Add(new TabItem { Header = "Team & service", Content = teamTabs });
        var people = BusinessPage(teamTabs, "Employees & roles", "Create the first active administrator to enable sign-in. Roles protect business actions: cashiers sell, take account/order payments and manage their own attendance; managers operate the shop; administrators also manage employees and restoration. Passwords are salted and hashed, never stored as plain text.");
        BusinessAction(people.Tools, "+ Employee", () => EmployeeEditor(null));
        BusinessAction(people.Tools, "Edit employee", () => { var row = BusinessSelected<EmployeeRow>("Employees & roles"); EmployeeEditor(store.Data.Business.Employees.Single(e => e.Id == row.Id)); });
        BusinessAction(people.Tools, "Sign in / switch", SignInEditor);

        var attendance = BusinessPage(teamTabs, "Attendance", "Employees clock in and out under their own sign-in. Managers can correct a recorded time range with a reason, subject to overlap checks. Hours are elapsed time, not a statutory payroll calculation.");
        BusinessAction(attendance.Tools, "Clock in", () => { store.ClockIn(); Refresh(); });
        BusinessAction(attendance.Tools, "Clock out", () => { store.ClockOut(); Refresh(); });
        BusinessAction(attendance.Tools, "Correct time entry", AttendanceCorrectionEditor);
        BusinessAction(attendance.Tools, "Hours by employee", () => ShowTable("Recorded attendance totals", store.Data.Business.Attendance.Where(a => a.ClockOut != null).GroupBy(a => new { a.EmployeeId, a.Employee }).Select(g => new { g.Key.Employee, Shifts = g.Count(), Hours = g.Sum(a => a.Hours) })));

        var audit = BusinessPage(teamTabs, "Audit trail", "Successful mutations record actor, action, affected record collections and a chained hash. Verify the chain to detect changed audit entries. This local application log is not a substitute for secured server-side accounting records.");
        auditSearch = new TextBox { Width = 240, ToolTip = "Search actor, action or changed collection" }; audit.Tools.Children.Add(auditSearch);
        BusinessAction(audit.Tools, "Search audit", RefreshAudit);
        BusinessAction(audit.Tools, "Verify chain", () => { auditHint.Text = store.VerifyAudit() ? "Audit chain verified." : "Audit verification FAILED. Review the store data and recovery backups."; });
        BusinessAction(audit.Tools, "Export audit JSON", () => { store.EnsureAccess("ReadAudit"); SaveText("JSON|*.json", "audit.json", System.Text.Json.JsonSerializer.Serialize(store.Data.Business.Audit, new System.Text.Json.JsonSerializerOptions { WriteIndented = true })); });
        auditHint = new TextBlock { Margin = new Thickness(4, 10, 4, 4), TextWrapping = TextWrapping.Wrap }; DockPanel.SetDock(auditHint, Dock.Top); audit.Panel.Children.Insert(audit.Panel.Children.Count - 1, auditHint);

        var jobs = BusinessPage(teamTabs, "Service jobs", "Track repair intake, technician assignment, diagnosis, multi-part estimates, customer approval, work, invoicing and collection. Approved jobs reserve physical parts; ready jobs invoice parts and agreed labor through checkout.");
        BusinessAction(jobs.Tools, "New intake", NewServiceJobEditor);
        BusinessAction(jobs.Tools, "Estimate / diagnosis", ServiceEstimateEditor);
        BusinessAction(jobs.Tools, "Approve estimate", () => ChangeServiceJob("Approved"));
        BusinessAction(jobs.Tools, "Start work", () => ChangeServiceJob("In progress"));
        BusinessAction(jobs.Tools, "Mark ready", () => ChangeServiceJob("Ready"));
        BusinessAction(jobs.Tools, "Invoice at checkout", LoadServiceJob);
        BusinessAction(jobs.Tools, "Mark collected", () => ChangeServiceJob("Collected"));
        BusinessAction(jobs.Tools, "Cancel job", () => ChangeServiceJob("Cancelled"));
        BusinessAction(jobs.Tools, "Job sheet / estimate", ShowServiceJob);

        var commissions = BusinessPage(teamTabs, "Commissions", "Configure each employee's sales commission rate in their profile. Checkout captures the current rate, returns reverse the related accrual and settlements reduce the outstanding balance. Later returns can leave negative carry-forward. This is commission tracking, not payroll or tax filing.");
        BusinessAction(commissions.Tools, "Settle selected commission", () =>
        {
            var balance = BusinessSelected<CommissionBalance>("Commissions"); var e = Editor("Commission settlement · " + balance.Employee); e.Note($"Outstanding: {balance.Outstanding:N2}"); var amount = e.Text("Pay amount", Math.Max(0, balance.Outstanding).ToString()); var method = e.Options("Method", "Cash", "Cash", "Card"); var reference = e.Text("Settlement reference"); Edit(e, () => store.PayCommission(balance.EmployeeId, Number(amount.Text), Option(method), reference.Text));
        });
        BusinessAction(commissions.Tools, "Settlement ledger", () => ShowTable("Commission payments", store.Data.Business.CommissionPayments.OrderByDescending(p => p.Time)));
        BusinessAction(commissions.Tools, "Accrued sale detail", () => { var b = BusinessSelected<CommissionBalance>("Commissions"); ShowTable(b.Employee + " · Sale accruals", store.Data.Sales.Where(s => s.EmployeeId == b.EmployeeId).Select(s => new { s.Number, s.Time, s.Total, Refunds = store.Refunded(s.Number), s.CommissionRate, OriginalCommission = decimal.Round((s.Subtotal - s.Discount) * s.CommissionRate / 100, 2) })); });
        BuildSessionToolbar();
    }
    private void BuildSessionToolbar()
    {
        var header = (Grid)((DockPanel)Content).Children[0]; header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var row = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) }; Grid.SetRow(row, 1); header.Children.Add(row);
        sessionLabel = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 16, 0) }; row.Children.Add(sessionLabel);
        BusinessAction(row, "Sign in", SignInEditor);
        BusinessAction(row, "Sign out", () => { RequireEmptyCart(); store.SignOut(); Refresh(); });
        BusinessAction(row, "Clock in", () => { store.ClockIn(); Refresh(); });
        BusinessAction(row, "Clock out", () => { store.ClockOut(); Refresh(); });
    }
    private void SignInEditor()
    {
        RequireEmptyCart(); var e = Editor("Employee sign-in"); var username = e.Text("Username"); var password = e.Password("Password"); e.SaveButton.Content = "Sign in";
        Edit(e, () => store.SignIn(username.Text, password.Password));
    }
    private void EmployeeEditor(Employee? employee)
    {
        var e = Editor(employee == null ? "Create employee" : "Edit employee");
        if (!store.HasStaffAccounts) e.Note("Create an active administrator first. You will then sign in to continue managing the shop.");
        var name = e.Text("Name", employee?.Name ?? ""); var username = e.Text("Username", employee?.Username ?? ""); var role = e.Options("Role", employee?.Role ?? "Administrator", "Administrator", "Manager", "Cashier"); var password = e.Password(employee == null ? "Password (at least eight characters)" : "New password (leave blank to keep)"); var active = e.Check("Active", employee?.Active ?? true); var rate = e.Text("Sales commission %", (employee?.CommissionRate ?? 0).ToString());
        Edit(e, () => store.SaveEmployee(employee?.Id ?? Guid.Empty, name.Text, username.Text, Option(role), password.Password, active.IsChecked == true, Number(rate.Text)));
    }
    private void AttendanceCorrectionEditor()
    {
        var entry = BusinessSelected<Attendance>("Attendance"); var e = Editor("Correct attendance · " + entry.Employee); var from = e.Text("Clock in (yyyy-MM-dd HH:mm)", entry.ClockIn.LocalDateTime.ToString("yyyy-MM-dd HH:mm")); var to = e.Text("Clock out (yyyy-MM-dd HH:mm)", (entry.ClockOut ?? DateTimeOffset.Now).LocalDateTime.ToString("yyyy-MM-dd HH:mm")); var reason = e.Text("Correction reason");
        DateTimeOffset Parse(string value) => DateTime.TryParseExact(value, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? new DateTimeOffset(time) : throw new InvalidOperationException("Use yyyy-MM-dd HH:mm for times.");
        Edit(e, () => store.CorrectAttendance(entry.Id, Parse(from.Text), Parse(to.Text), reason.Text));
    }
    private void NewServiceJobEditor()
    {
        var e = Editor("Service intake"); var customer = e.Choice("Customer", CustomerChoices()); var employee = e.Choice("Technician", EmployeeChoices(true)); var location = e.Choice("Parts location", LocationChoices()); var device = e.Text("Device / item"); var serial = e.Text("Serial / identifying details"); var problem = e.Text("Reported problem", "", true); var due = e.Date("Promised date", DateTime.Today.AddDays(3));
        Edit(e, () => store.CreateServiceJob(Pick(customer), Pick(employee), Pick(location), device.Text, serial.Text, problem.Text, PickDate(due)));
    }
    private void ServiceEstimateEditor()
    {
        var job = BusinessSelected<ServiceJob>("Service jobs"); var e = Editor("Estimate · " + job.Number, 900); var diagnosis = e.Text("Diagnosis / work proposed", job.Diagnosis, true); var labor = e.Choice("Labor service product", ProductChoices(p => p.Kind == "Service"), job.LaborProductId); var charge = e.Text("Agreed labor charge", job.LaborCharge.ToString());
        e.Note("Create a zero-stock product and configure it as Service under Costing & margins for the labor line. Add physical parts below; the estimate reserves them only after approval.");
        var parts = new ProductLineEditor(store.Data.Products.Where(p => p.Kind == "Stock" && !p.Archived), job.Parts); e.Fields.Children.Add(parts.View);
        Edit(e, () => store.EstimateServiceJob(job.Id, diagnosis.Text, Pick(labor), Number(charge.Text), parts.Lines));
    }
    private void ChangeServiceJob(string state) { var job = BusinessSelected<ServiceJob>("Service jobs"); if (state == "Cancelled" && MessageBox.Show(this, "Cancel this job and release its reserved parts?", "Service job", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return; store.UpdateServiceJob(job.Id, state); Refresh(); }
    private void LoadServiceJob()
    {
        RequireEmptyCart(); var job = BusinessSelected<ServiceJob>("Service jobs"); if (job.Status != "Ready") throw new InvalidOperationException("Finish the work and mark the job ready before invoicing.");
        var lines = store.ServiceJobLines(job.Id); ResetSale(); fulfillingJobId = job.Id; cart.AddRange(lines); SaleCustomer.SelectedValue = job.CustomerId; saleLocation.SelectedValue = job.LocationId; LockFulfillment(); RefreshCart(); MainTabs.SelectedIndex = 0; Status.Text = "Invoicing service job " + job.Number;
    }
    private void ShowServiceJob()
    {
        var job = BusinessSelected<ServiceJob>("Service jobs"); var customer = store.Data.Customers.Single(c => c.Id == job.CustomerId);
        ShowDocument(job.Number, $"SERVICE JOB {job.Number}\nCustomer: {customer.Name} · {customer.Phone}\nDevice: {job.Device}\nSerial: {job.Serial}\nDue: {job.Due:yyyy-MM-dd} · {job.Status}\nReported: {job.Problem}\nDiagnosis: {job.Diagnosis}\n" + string.Join("\n", job.Parts.Select(p => $"{p.Name} · {p.Quantity} × {p.UnitPrice:N2}")) + $"\nLabor: {job.LaborCharge:N2}\nEstimate before tax: {job.Parts.Sum(p => p.Total) + job.LaborCharge:N2}\nInvoice: {job.SaleNumber}");
    }
    private void RefreshAudit()
    {
        var query = auditSearch.Text.Trim(); businessGrids["Audit trail"].ItemsSource = store.Data.Business.Audit.Where(a => $"{a.Actor} {a.Action} {a.Changes}".Contains(query, StringComparison.OrdinalIgnoreCase)).OrderByDescending(a => a.Sequence).Take(1000).ToList();
    }
    private void RefreshTeamServices()
    {
        businessGrids["Employees & roles"].ItemsSource = store.Data.Business.Employees.Select(e => new EmployeeRow(e.Id, e.Name, e.Username, e.Role, e.Active, e.CommissionRate)).ToList();
        businessGrids["Attendance"].ItemsSource = store.Data.Business.Attendance.Where(a => store.CanManage || a.EmployeeId == store.CurrentEmployee?.Id).OrderByDescending(a => a.ClockIn).ToList();
        businessGrids["Service jobs"].ItemsSource = store.Data.Business.Jobs.OrderByDescending(j => j.Number).ToList();
        businessGrids["Commissions"].ItemsSource = store.CommissionBalances(); RefreshAudit();
        sessionLabel.Text = store.CurrentEmployee is Employee employee ? employee.Name + " · " + employee.Role : store.HasStaffAccounts ? "Signed out · sign in to operate" : "Owner setup · create employee accounts to enable roles";
        MainTabs.IsEnabled = !store.HasStaffAccounts || store.CurrentEmployee != null;
        foreach (TabItem tab in teamTabs.Items) if (tab.Header is string header && header is "Employees & roles" or "Audit trail" or "Commissions") tab.Visibility = store.CanManage ? Visibility.Visible : Visibility.Collapsed;
    }
}
