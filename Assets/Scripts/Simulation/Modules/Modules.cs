using POSTechSupport.Core;
using POSTechSupport.Data;

namespace POSTechSupport.Simulation
{
    // ============================================================================
    // Concrete modules. Each reports ONLY its own-fault state (LocalStatus); the
    // upstream Blocked cascade is added by DependencyGraph. Field keys match the
    // validated prototype's desktop object exactly. See Docs/app.md.
    // ============================================================================

    /// <summary>
    /// Windows itself — the floor everything else stands on. Two KINDS of fault live here and they must
    /// not be confused (Docs/app.md §7):
    /// - machine-wide (disk full, reboot pending) → blocks the entire chain, see DependencyGraph.OsBlocking;
    /// - service-level (print spooler, system clock) → surfaces as a local Error on the module that
    ///   actually needs that service, so it stays diagnosable instead of hiding behind Blocked.
    /// </summary>
    public class OSModule : ModuleBase
    {
        public override ModuleType Type => ModuleType.OS;

        public OSModule()
        {
            Set("diskSpace", "OK");
            Set("pendingReboot", "false");
            Set("spoolerService", "Running");
            Set("systemTime", "OK");
            Set("antivirusQuarantine", "false");   // AV ate a POS file → surfaces on POSSoftware
            Set("userAccount", "Admin");           // Windows-level rights, NOT the POS staff role
            Set("powerPlan", "AlwaysOn");          // a register that sleeps drops the terminal
        }

        /// <summary>True for faults that take the whole machine down with them.</summary>
        public bool HasMachineWideFault(out string reason)
        {
            if (Get("diskSpace") == "Full") { reason = "system drive is full"; return true; }
            if (GetBool("pendingReboot")) { reason = "a pending update needs a restart"; return true; }
            reason = "";
            return false;
        }

        public override StatusResult LocalStatus(VirtualDesktopInstance d)
        {
            if (HasMachineWideFault(out string reason))
                return new StatusResult(Status.Error, char.ToUpper(reason[0]) + reason.Substring(1));
            if (Get("spoolerService") == "Stopped")
                return new StatusResult(Status.Error, "Print Spooler service is stopped");
            if (Get("systemTime") == "Skewed")
                return new StatusResult(Status.Error, "System clock is wrong — secure connections will be rejected");
            if (GetBool("antivirusQuarantine"))
                return new StatusResult(Status.Error, "Antivirus has quarantined a file the POS needs");
            if (Get("userAccount") == "Standard")
                return new StatusResult(Status.Error, "Signed in as a Standard user — the POS needs administrator rights");
            if (Get("powerPlan") == "Sleep")
                return new StatusResult(Status.Error, "Power plan lets the machine sleep — attached devices drop with it");
            return new StatusResult(Status.OK);
        }
    }

    /// <summary>Root of the NETWORK chain. Depends only on the OS being up.</summary>
    public class NetworkModule : ModuleBase
    {
        public override ModuleType Type => ModuleType.Network;

        public NetworkModule(StoreIdentity id = null)
        {
            id ??= StoreIdentity.Generic;
            Set("isOnline", "true");
            Set("ssid", id.mainSsid);      // the shop's own network, named after the shop
            Set("signalStrength", "Good");
            Set("dnsServer", WifiTable.StoreDnsCorrect);
            Set("firewallBlocking", "false");
        }

        /// <summary>
        /// DOWN (nothing can talk to anything) vs merely DEGRADED (link is up, one thing about it is
        /// wrong). Only DOWN blocks the chain — a bad DNS entry doesn't stop the POS running, it stops
        /// one lookup, and hiding the rest of the machine behind Blocked would erase the trail.
        /// </summary>
        public bool IsDown() => !GetBool("isOnline");

        public override StatusResult LocalStatus(VirtualDesktopInstance d)
        {
            if (IsDown()) return new StatusResult(Status.Error, "Network offline");
            // Everything below is "the link is up but unusable in a specific way" — each fails a
            // different thing downstream, which is exactly what makes them worth telling apart.
            if (Get("signalStrength") == "Weak")
                return new StatusResult(Status.Error, "Wi-Fi signal is weak — the link keeps dropping under load");
            if (Get("dnsServer") != WifiTable.StoreDnsCorrect)
                return new StatusResult(Status.Error, $"DNS is set to {Get("dnsServer")} — internal names won't resolve");
            if (GetBool("firewallBlocking"))
                return new StatusResult(Status.Error, "Firewall is blocking the payment processor's outbound port");
            return new StatusResult(Status.OK);
        }
    }

    /// <summary>The HUB. Own-fault: broken receipt template. Also holds staff/db/terminal-registration state.</summary>
    public class POSSoftwareModule : ModuleBase
    {
        public override ModuleType Type => ModuleType.POSSoftware;

        public POSSoftwareModule(StoreIdentity id = null)
        {
            id ??= StoreIdentity.Generic;
            Set("receiptTemplate", "OK");
            Set("staffRole", "Sale");     // healthy default for a floor-staff account; Admin is over-privileged
            Set("staffTerminal", "REG-1");
            Set("terminalSynced", "true");
            Set("dbHost", id.dbHost);
            Set("registeredTerminalIp", WifiTable.TerminalIpCorrect);
            Set("licenseState", "Valid");
            Set("offlineMode", "false");
            Set("batchState", "Open");
            Set("taxRate", WifiTable.TaxRateCorrect);
            Set("priceSync", "Current");
            Set("printerVisible", "true");                  // app.md: POS only checks "can I see a printer"
            Set("minTerminalFirmware", WifiTable.MinTerminalFirmware);
        }

        public override StatusResult LocalStatus(VirtualDesktopInstance d)
        {
            // Ordered worst-first: a POS that won't start makes every other reading meaningless.
            if (Get("licenseState") == "Expired")
                return new StatusResult(Status.Error, "POS licence has expired — the application refuses to open");

            // OS-level faults the POS depends on. Same pattern as the spooler surfacing on the printer:
            // reported HERE so the trail stays followable, rather than hidden behind a Blocked OS.
            var os = d.GetModule(ModuleType.OS);
            if (os != null && os.GetBool("antivirusQuarantine"))
                return new StatusResult(Status.Error, "A core POS file was quarantined by antivirus");
            if (os != null && os.Get("userAccount") == "Standard")
                return new StatusResult(Status.Error, "POS needs administrator rights — current Windows account is Standard");

            if (Get("receiptTemplate") == "Broken")
                return new StatusResult(Status.Error, "Receipt template config broken");
            if (GetBool("offlineMode"))
                return new StatusResult(Status.Error, "POS is in offline mode — sales are queuing locally and never settling");
            if (Get("batchState") == "SettleFailed")
                return new StatusResult(Status.Error, "Last batch failed to settle — funds were never transferred");
            if (Get("taxRate") != WifiTable.TaxRateCorrect)
                return new StatusResult(Status.Error, $"Sales tax is configured as {Get("taxRate")}% — totals are wrong");
            if (Get("priceSync") == "Stale")
                return new StatusResult(Status.Error, "Price list hasn't synced — the register is ringing up old prices");
            if (!GetBool("printerVisible"))
                return new StatusResult(Status.Error, "POS cannot see a receipt printer — none is registered to this station");
            return new StatusResult(Status.OK);
        }
    }

    /// <summary>Register hardware. Own-faults: wrong Wi-Fi (P6) or stale IP registered on POS (P7).</summary>
    public class TerminalModule : ModuleBase
    {
        public override ModuleType Type => ModuleType.Terminal;

        public TerminalModule(StoreIdentity id = null)
        {
            id ??= StoreIdentity.Generic;
            Set("wifiNetwork", id.mainSsid);
            Set("machineId", "REG-1");   // not a fault surface itself; staff assignments are checked against it
            Set("pairingState", "Paired");
            Set("firmwareVersion", WifiTable.TerminalFirmwareCurrent);
            Set("emvConfig", "OK");
            Set("mode", "Live");         // Training mode looks like it works and moves no money
        }

        public override StatusResult LocalStatus(VirtualDesktopInstance d)
        {
            var net = d.GetModule(ModuleType.Network);
            var pos = d.GetModule(ModuleType.POSSoftware);
            string wifi = Get("wifiNetwork");
            string storeSsid = net.Get("ssid");

            if (wifi != storeSsid)
            {
                return new StatusResult(Status.Error,
                    $"connected to the wrong Wi-Fi (\"{wifi}\" instead of \"{storeSsid}\") — wrong network means a completely different IP range, see Terminal ▸ Network");
            }

            string actualIp = WifiTable.Resolve(d.Identity, wifi).ip;
            string registeredIp = pos.Get("registeredTerminalIp");
            if (actualIp != registeredIp)
            {
                return new StatusResult(Status.Error,
                    $"IP mismatch — terminal is actually at {actualIp}, POS has {registeredIp} registered");
            }

            if (Get("pairingState") == "Unpaired")
                return new StatusResult(Status.Error, "terminal is not paired with the POS — its pairing token is gone");

            string fw = Get("firmwareVersion"), minFw = pos.Get("minTerminalFirmware");
            if (VersionLess(fw, minFw))
                return new StatusResult(Status.Error, $"firmware {fw} is older than the {minFw} this POS build requires");

            if (Get("emvConfig") == "Corrupt")
                return new StatusResult(Status.Error, "chip reader config is corrupt — chip is refused, swipe still works");

            if (Get("mode") == "Training")
                return new StatusResult(Status.Error, "terminal is in TRAINING mode — approvals are fake and no money moves");

            // Card auth runs over TLS, so a wrong machine clock gets the handshake rejected. Reads as a
            // terminal fault, roots in the OS — the discrimination P16 is built on.
            var os = d.GetModule(ModuleType.OS);
            if (os?.Get("systemTime") == "Skewed")
            {
                return new StatusResult(Status.Error,
                    "card authorization rejected — the processor refused the secure handshake (machine clock is off)");
            }
            if (os?.Get("powerPlan") == "Sleep")
                return new StatusResult(Status.Error, "terminal keeps dropping — the PC it is attached to goes to sleep");

            // Degraded-network faults that specifically break card authorization.
            if (net.Get("signalStrength") == "Weak")
                return new StatusResult(Status.Error, "authorizations keep timing out — the wireless link drops under load");
            if (net.GetBool("firewallBlocking"))
                return new StatusResult(Status.Error, "cannot reach the payment processor — its outbound port is blocked");

            return new StatusResult(Status.OK);
        }

        /// <summary>Dotted-version compare, just enough for "4.2" vs "4.0". Non-numeric parts sort as 0.</summary>
        private static bool VersionLess(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            var pa = a.Split('.'); var pb = b.Split('.');
            for (int i = 0; i < System.Math.Max(pa.Length, pb.Length); i++)
            {
                int x = i < pa.Length && int.TryParse(pa[i], out var vx) ? vx : 0;
                int y = i < pb.Length && int.TryParse(pb[i], out var vy) ? vy : 0;
                if (x != y) return x < y;
            }
            return false;
        }
    }

    /// <summary>Own-faults: device removed / driver corrupted / out of paper.</summary>
    public class PrinterModule : ModuleBase
    {
        public override ModuleType Type => ModuleType.Printer;

        public PrinterModule()
        {
            Set("paperLevel", "OK");
            Set("driverState", "OK");
            Set("connection", "Connected");
            Set("port", "COM3");
            Set("cableConnected", "true");
            Set("paperJam", "None");
            Set("queuePaused", "false");
            Set("defaultPrinter", WifiTable.DefaultPrinterCorrect);
            Set("paperWidth", WifiTable.PaperWidthCorrect);
        }

        /// <summary>
        /// Ordered physical → device → service → consumable → config, because that is the order in which
        /// a fault makes the ones below it unreadable. A printer with no cable tells you nothing about
        /// its driver, so reporting the cable first is what keeps the reason line honest.
        /// </summary>
        public override StatusResult LocalStatus(VirtualDesktopInstance d)
        {
            if (!GetBool("cableConnected"))
                return new StatusResult(Status.Error, "Data cable is unplugged — Windows sees no device on the port");
            if (Get("connection") == "Removed") return new StatusResult(Status.Error, "Device removed");
            if (Get("connection") == "Offline")
                return new StatusResult(Status.Error, "Windows has the printer set to 'Use Printer Offline'");

            // The spooler is an OS service, but a dead spooler shows up HERE — as a printer Error, not as
            // Blocked — because the diagnosis trail has to stay open for the player to follow (Docs/app.md §7).
            if (d.GetModule(ModuleType.OS)?.Get("spoolerService") == "Stopped")
                return new StatusResult(Status.Error, "Print Spooler service is not running — jobs never leave the queue");

            if (Get("driverState") == "Corrupted") return new StatusResult(Status.Error, "Driver error (Code 39)");
            if (Get("paperJam") == "Jammed") return new StatusResult(Status.Error, "Paper jam — a receipt is caught in the cutter");
            if (Get("paperLevel") == "Empty") return new StatusResult(Status.Error, "Out of paper");
            if (GetBool("queuePaused")) return new StatusResult(Status.Error, "Print queue is paused — jobs are held, not sent");
            if (Get("defaultPrinter") != WifiTable.DefaultPrinterCorrect)
                return new StatusResult(Status.Error, $"Windows default printer is \"{Get("defaultPrinter")}\" — receipts are going elsewhere");
            if (Get("paperWidth") != WifiTable.PaperWidthCorrect)
                return new StatusResult(Status.Error, $"Configured for {Get("paperWidth")} paper — the loaded roll doesn't match, output is cut off");
            return new StatusResult(Status.OK);
        }
    }

    /// <summary>Own-fault: port conflicts with the printer's port.</summary>
    public class CashDrawerModule : ModuleBase
    {
        public override ModuleType Type => ModuleType.CashDrawer;

        public CashDrawerModule()
        {
            Set("port", "COM4");
            Set("lockState", "Unlocked");     // the physical key lock — no amount of config beats it
            Set("triggerMode", "OnPrint");
        }

        public override StatusResult LocalStatus(VirtualDesktopInstance d)
        {
            var printer = d.GetModule(ModuleType.Printer);
            if (Get("port") == printer.Get("port"))
                return new StatusResult(Status.Error, $"Port conflict with printer ({Get("port")})");
            if (Get("lockState") == "Locked")
                return new StatusResult(Status.Error, "Drawer is key-locked — the release fires but the drawer is held shut");
            if (Get("triggerMode") != "OnPrint")
                return new StatusResult(Status.Error, "Drawer is set to open manually — printing a receipt no longer releases it");
            return new StatusResult(Status.OK);
        }
    }
}
