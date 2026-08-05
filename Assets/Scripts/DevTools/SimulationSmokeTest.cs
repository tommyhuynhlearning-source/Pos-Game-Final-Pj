using UnityEngine;
using POSTechSupport.Core;
using POSTechSupport.Data;
using POSTechSupport.Simulation;

namespace POSTechSupport.DevTools
{
    /// <summary>
    /// M1 done-criterion check (GDD §12): inject each authored fault into a fresh desktop and print the
    /// resolved cascade. Needs NO content assets — it drives the Simulation layer + DependencyGraph
    /// directly, so it still passes on a project where the .asset files were never generated.
    ///
    /// What to read in the output: a MACHINE-WIDE fault (P4, P14, P15) must show Blocked all the way
    /// down; every other fault must show Error on exactly the module that owns the symptom and OK
    /// elsewhere. An unexpected Blocked is the bug worth catching here — it means a clue the player
    /// needs has been hidden behind a dependency (Docs/app.md §7).
    /// Attach to any GameObject and press Play, or use the context-menu item.
    /// </summary>
    public class SimulationSmokeTest : MonoBehaviour
    {
        [SerializeField] private bool runOnStart = true;

        private void Start() { if (runOnStart) Run(); }

        /// <summary>One authored fault: which issue it belongs to and the single state it writes.</summary>
        private readonly struct Case
        {
            public readonly string label; public readonly ModuleType module;
            public readonly string field, value;
            public Case(string label, ModuleType module, string field, string value)
            { this.label = label; this.module = module; this.field = field; this.value = value; }
        }

        private static readonly Case[] Cases =
        {
            new("P1  out of paper",       ModuleType.Printer,     "paperLevel",          "Empty"),
            new("P2  driver corrupted",   ModuleType.Printer,     "driverState",         "Corrupted"),
            new("P3  drawer port clash",  ModuleType.CashDrawer,  "port",                "COM3"),
            new("P4  network offline",    ModuleType.Network,     "isOnline",            "false"),
            new("P5  receipt template",   ModuleType.POSSoftware, "receiptTemplate",     "Broken"),
            new("P6  wrong Wi-Fi",        ModuleType.Terminal,    "wifiNetwork",         StoreIdentity.TokenSsidGuest),
            new("P7  stale terminal IP",  ModuleType.POSSoftware, "registeredTerminalIp","192.168.1.77"),
            new("P8  staff has no role",  ModuleType.POSSoftware, "staffRole",           "None"),
            new("P9  no terminal assign", ModuleType.POSSoftware, "staffTerminal",       ""),
            new("P10 wrong register",     ModuleType.POSSoftware, "staffTerminal",       "REG-4"),
            new("P11 not synced",         ModuleType.POSSoftware, "terminalSynced",      "false"),
            new("P12 db host typo",       ModuleType.POSSoftware, "dbHost",              StoreIdentity.TokenDbHostTypo),
            new("P13 spooler stopped",    ModuleType.OS,          "spoolerService",      "Stopped"),
            new("P14 disk full",          ModuleType.OS,          "diskSpace",           "Full"),
            new("P15 pending reboot",     ModuleType.OS,          "pendingReboot",       "true"),
            new("P16 clock skew",         ModuleType.OS,          "systemTime",          "Skewed"),
            new("P17 paper jam",          ModuleType.Printer,     "paperJam",            "Jammed"),
            new("P18 cable unplugged",    ModuleType.Printer,     "cableConnected",      "false"),
            new("P19 queue paused",       ModuleType.Printer,     "queuePaused",         "true"),
            new("P20 wrong default",      ModuleType.Printer,     "defaultPrinter",      "OfficeInkjet"),
            new("P21 POS sees no printer",ModuleType.POSSoftware, "printerVisible",      "false"),
            new("P22 printer offline",    ModuleType.Printer,     "connection",          "Offline"),
            new("P23 paper width",        ModuleType.Printer,     "paperWidth",          "58mm"),
            new("P24 drawer key-locked",  ModuleType.CashDrawer,  "lockState",           "Locked"),
            new("P25 drawer manual",      ModuleType.CashDrawer,  "triggerMode",         "Manual"),
            new("P26 terminal unpaired",  ModuleType.Terminal,    "pairingState",        "Unpaired"),
            new("P27 firmware too old",   ModuleType.Terminal,    "firmwareVersion",     "3.1"),
            new("P28 EMV config corrupt", ModuleType.Terminal,    "emvConfig",           "Corrupt"),
            new("P29 training mode",      ModuleType.Terminal,    "mode",                "Training"),
            new("P30 licence expired",    ModuleType.POSSoftware, "licenseState",        "Expired"),
            new("P31 POS offline mode",   ModuleType.POSSoftware, "offlineMode",         "true"),
            new("P32 batch settle failed",ModuleType.POSSoftware, "batchState",          "SettleFailed"),
            new("P33 wrong tax rate",     ModuleType.POSSoftware, "taxRate",             "0"),
            new("P34 stale price list",   ModuleType.POSSoftware, "priceSync",           "Stale"),
            new("P35 weak signal",        ModuleType.Network,     "signalStrength",      "Weak"),
            new("P36 wrong DNS",          ModuleType.Network,     "dnsServer",           "8.8.8.8"),
            new("P37 firewall blocking",  ModuleType.Network,     "firewallBlocking",    "true"),
            new("P38 AV quarantine",      ModuleType.OS,          "antivirusQuarantine", "true"),
            new("P39 standard account",   ModuleType.OS,          "userAccount",         "Standard"),
            new("P40 machine sleeps",     ModuleType.OS,          "powerPlan",           "Sleep"),
        };

        [ContextMenu("Run Simulation Smoke Test")]
        public void Run()
        {
            Debug.Log("=== POS Sim smoke test — 40 authored faults ===");
            LogCascade("healthy baseline", VirtualDesktopInstance.BuildFresh());

            foreach (var c in Cases)
            {
                var d = VirtualDesktopInstance.BuildFresh();
                d.Apply(new FaultInjection { module = c.module, stateField = c.field, faultValue = c.value });
                LogCascade(c.label, d);
            }
        }

        private static void LogCascade(string label, VirtualDesktopInstance d)
        {
            string line = "";
            foreach (ModuleType m in new[] { ModuleType.OS, ModuleType.Network, ModuleType.POSSoftware,
                                             ModuleType.Terminal, ModuleType.Printer, ModuleType.CashDrawer })
            {
                var es = d.EffectiveStatus(m);
                line += $"{m}={es.status}{(string.IsNullOrEmpty(es.reason) ? "" : $"({es.reason})")}  ";
            }
            Debug.Log($"[{label}] {line}");
        }
    }
}
