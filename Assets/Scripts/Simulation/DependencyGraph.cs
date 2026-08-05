using System.Globalization;
using POSTechSupport.Core;
using POSTechSupport.Data;

namespace POSTechSupport.Simulation
{
    /// <summary>
    /// The ONE place that resolves the Blocked-vs-Error cascade (GDD nguyên tắc bất biến #4;
    /// Docs/app.md §7). Ported 1:1 from the prototype's effectiveStatus / staffLoginStatus /
    /// dbConnected / runTest / checkState. Every other system reads these results — no one
    /// re-derives dependency logic on its own.
    ///
    /// Cascade: Network → POSSoftware → { Terminal, Printer → CashDrawer }.
    /// Blocked only propagates from a Blocked (or offline-Network) upstream; a module's own
    /// misconfiguration stays Error so it remains diagnosable.
    /// </summary>
    public class DependencyGraph
    {
        private readonly VirtualDesktopInstance d;
        public DependencyGraph(VirtualDesktopInstance desktop) { d = desktop; }

        public StatusResult EffectiveStatus(ModuleType module)
        {
            switch (module)
            {
                case ModuleType.OS:
                    return d.GetModule(ModuleType.OS).LocalStatus(d);

                case ModuleType.Network:
                {
                    if (OsBlocking(out string why))
                        return new StatusResult(Status.Blocked, $"cannot operate — reason: {why}");
                    return d.GetModule(ModuleType.Network).LocalStatus(d);
                }

                case ModuleType.POSSoftware:
                {
                    if (OsBlocking(out string why))
                        return new StatusResult(Status.Blocked, $"cannot operate — reason: {why}");
                    // Blocked only when the network is DOWN. A degraded link (weak signal, bad DNS,
                    // firewall) leaves POS running — and leaves its clues readable, which is the point.
                    if (d.GetModule(ModuleType.Network) is NetworkModule net && net.IsDown())
                        return new StatusResult(Status.Blocked, "cannot operate — reason: Network offline");
                    return d.GetModule(ModuleType.POSSoftware).LocalStatus(d);
                }

                case ModuleType.Terminal:
                {
                    var pos = EffectiveStatus(ModuleType.POSSoftware);
                    if (pos.status == Status.Blocked)
                        return new StatusResult(Status.Blocked, "cannot operate — reason: POS not connected");
                    return d.GetModule(ModuleType.Terminal).LocalStatus(d);
                }

                case ModuleType.Printer:
                {
                    var pos = EffectiveStatus(ModuleType.POSSoftware);
                    if (pos.status == Status.Blocked)
                        return new StatusResult(Status.Blocked, "cannot operate — reason: POS not connected");
                    return d.GetModule(ModuleType.Printer).LocalStatus(d);
                }

                case ModuleType.CashDrawer:
                {
                    var printer = EffectiveStatus(ModuleType.Printer);
                    if (printer.status == Status.Blocked)
                        return new StatusResult(Status.Blocked, "cannot operate — reason: POS not connected");
                    return d.GetModule(ModuleType.CashDrawer).LocalStatus(d);
                }

                default:
                    return new StatusResult(Status.OK);
            }
        }

        /// <summary>
        /// Only MACHINE-WIDE OS faults (disk full, pending reboot) block the chain. A stopped spooler or a
        /// skewed clock deliberately does NOT — those surface as a local Error on the Printer / Terminal so
        /// the player can still diagnose them (Docs/app.md §7 Blocked vs Error).
        /// </summary>
        public bool OsBlocking(out string reason)
        {
            reason = "";
            return d.GetModule(ModuleType.OS) is OSModule os && os.HasMachineWideFault(out reason);
        }

        /// <summary>The terminal's DHCP-derived IP/gateway, always from its current Wi-Fi choice.</summary>
        public WifiTable.NetInfo TerminalNetInfo() =>
            WifiTable.Resolve(d.GetModule(ModuleType.Terminal).Get("wifiNetwork"));

        /// <summary>
        /// Per-staff login (GDD Mục 15) — a separate failure domain from the terminal's own connectivity.
        /// A staff member can be denied while the terminal is perfectly healthy, and vice versa.
        /// </summary>
        public (bool ok, string reason) StaffLoginStatus()
        {
            var term = EffectiveStatus(ModuleType.Terminal);
            if (!term.IsOk) return (false, "Terminal unreachable — " + term.reason);
            var pos = d.GetModule(ModuleType.POSSoftware);
            string role = pos.Get("staffRole");
            if (string.IsNullOrEmpty(role) || role == "None")
                return (false, "Login failed: permission denied by POS — no role assigned");

            string assigned = pos.Get("staffTerminal");
            if (string.IsNullOrEmpty(assigned))
                return (false, "Login failed: permission denied by POS — not assigned to any terminal");

            // Assigned, but to a DIFFERENT register: the account is fine, it just isn't valid here.
            string thisMachine = d.GetModule(ModuleType.Terminal).Get("machineId");
            if (!string.IsNullOrEmpty(thisMachine) && assigned != thisMachine)
                return (false, $"Login failed: account is assigned to {assigned}, this register is {thisMachine}");

            if (!pos.GetBool("terminalSynced"))
                return (false, "Login failed: assignment changed but not synced yet");
            return (true, "");
        }

        /// <summary>DB connectivity — depends on the upstream chain AND the editable dbHost field.</summary>
        public (bool ok, string reason) DbConnected()
        {
            var pos = EffectiveStatus(ModuleType.POSSoftware);
            if (pos.status == Status.Blocked) return (false, pos.reason);

            string host = (d.GetModule(ModuleType.POSSoftware).Get("dbHost") ?? "").Trim().ToLowerInvariant();
            if (host != WifiTable.PosDbHostCorrect.ToLowerInvariant())
                return (false, $"Cannot resolve host \"{d.GetModule(ModuleType.POSSoftware).Get("dbHost")}\"");

            // Same failure text, different cause: the name is right but nothing can resolve it. That the
            // two look identical from here is the whole point of the P12 / P35 pair.
            if (d.GetModule(ModuleType.Network).Get("dnsServer") != WifiTable.StoreDnsCorrect)
                return (false, $"Cannot resolve host \"{d.GetModule(ModuleType.POSSoftware).Get("dbHost")}\" (no internal resolver)");

            return (true, "");
        }

        /// <summary>Receipt test. TestPage checks hardware/driver only; the rest also need a good template.</summary>
        public bool RunTest(ReceiptType testType)
        {
            bool printerOk = EffectiveStatus(ModuleType.Printer).IsOk;
            if (testType == ReceiptType.TestPage) return printerOk;
            return printerOk && d.GetModule(ModuleType.POSSoftware).Get("receiptTemplate") == "OK";
        }

        /// <summary>Data-driven state comparison used by resolution checks and action preconditions.</summary>
        public bool CheckState(StateCheck check)
        {
            string actual = d.GetModule(check.module)?.Get(check.field);
            switch (check.op)
            {
                case ComparisonOp.Equals:    return actual == check.expectedValue;
                case ComparisonOp.NotEquals: return actual != check.expectedValue;
                case ComparisonOp.GreaterThan:
                case ComparisonOp.LessThan:
                    if (float.TryParse(actual, NumberStyles.Any, CultureInfo.InvariantCulture, out var a) &&
                        float.TryParse(check.expectedValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                        return check.op == ComparisonOp.GreaterThan ? a > b : a < b;
                    return false;
                default: return false;
            }
        }
    }
}
