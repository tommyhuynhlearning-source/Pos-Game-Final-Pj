using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using POSTechSupport.Core;
using POSTechSupport.Data;

namespace POSTechSupport.EditorTools
{
    /// <summary>
    /// Generates the sample content (P1–P7, store template + name table + CRM decoys, persona, the 14 desktop actions,
    /// config, and a wired ContentDatabase) as .asset files under Assets/Content/Generated.
    /// Data is ported verbatim from the validated web prototype (Docs/web-prototype/app.js) so the
    /// Unity build starts from the same known-good numbers. Menu: POS ▸ Generate Sample Content.
    /// </summary>
    public static class SampleContentBootstrap
    {
        private const string Dir = "Assets/Content/Generated";

        [MenuItem("POS/Generate Sample Content")]
        public static void Generate()
        {
            EnsureDir();

            var config = CreateConfig();
            var actions = CreateActions();
            var persona = CreatePersona();
            var store = CreateRealStore();
            var issues = CreateIssues();

            var db = ScriptableObject.CreateInstance<ContentDatabaseSO>();
            db.config = config;
            db.realStore = store;
            db.storeNames = CreateStoreNameTable();
            db.crmClusterCount = 6;          // confusable name families rolled into the CRM directory
            db.personaPool = new[] { persona };
            db.allIssues = issues;
            db.allActions = actions;
            db.receiptTemplates = CreateReceiptTemplates();
            db.knowledgeArticles = CreateKnowledgeArticles();
            db = Save(db, "ContentDatabase");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[SampleContentBootstrap] Generated content under {Dir}. Assign ContentDatabase to GameManager.");
            Selection.activeObject = db;
        }

        // --- Config ------------------------------------------------------------------------------
        private static GameConfigSO CreateConfig()
        {
            var c = ScriptableObject.CreateInstance<GameConfigSO>();
            c.shiftRealDurationSec = 150f;   // prototype's fast testing value (GDD design target is 480)
            c.shiftStartHour = 20f;
            c.shiftEndHour = 4f;
            c.ringTimeoutSec = 12f;
            c.totalDays = 60;
            c.minTotalTickets = 150;
            c.strikesPerNightFail = 3;
            c.warningsToGameOver = 3;
            // Call volume as a rate, not a hard-coded count: 0.25/in-game-hour over an 8h shift = 2 calls
            // on day 1, ramping to 5 by day 60. Retune on GameConfig.asset in the Inspector.
            c.callsPerHour = 0.25f;
            c.callsPerHourPerDay = 0.00625f;
            c.minCallsPerNight = 1;
            c.maxCallsPerNight = 120;        // 0 = no cap; this only catches a typo in the rate
            c.queuePatienceSec = 15f;        // busy line: waiting callers get taken by another tech
            // Calls bunch up toward dawn (GDD §1). Approximates the prototype's pow(rand, 1.4) skew;
            // flatten it toward a straight line for an evenly-paced night.
            c.ticketTempoOverNight = new AnimationCurve(
                new Keyframe(0f, 0f), new Keyframe(0.25f, 0.144f), new Keyframe(0.5f, 0.379f),
                new Keyframe(0.75f, 0.672f), new Keyframe(1f, 1f));
            return Save(c, "GameConfig");
        }

        // --- Actions -----------------------------------------------------------------------------
        private static DesktopActionSO[] CreateActions()
        {
            // appKey/appTab follow Docs/app.md "Action theo từng app". Note print_customer_copy targets the
            // Printer module but is hosted by POS Manager ▸ Database — it needs real transaction data.
            var list = new List<DesktopActionSO>
            {
                Diagnostic("check_print_queue", DesktopActionType.CheckPrintQueue, ModuleType.Printer, "printer", "queue",
                    "Print queue inspected."),
                Test("print_test_page", DesktopActionType.PrintTestPage, ModuleType.Printer, "printer", "queue", ReceiptType.TestPage,
                    "Test page sent to the printer — hardware/driver only, no transaction data."),
                Test("print_customer_copy", DesktopActionType.PrintCustomerCopy, ModuleType.Printer, "possoftware", "database", ReceiptType.Customer,
                    "Customer copy re-printed from the transaction record."),
                Fix("refill_paper_tray", ModuleType.Printer, "printer", "queue",
                    Pre(ModuleType.Printer, "paperLevel", ComparisonOp.Equals, "Empty"),
                    Change(ModuleType.Printer, "paperLevel", "OK"),
                    "Paper tray refilled — print a test page to confirm."),

                Diagnostic("open_device_manager", DesktopActionType.OpenDeviceManager, ModuleType.Printer, "devicemanager", "printer",
                    "Device Manager opened."),
                Fix("reinstall_printer_driver", ModuleType.Printer, "devicemanager", "printer",
                    Pre(ModuleType.Printer, "driverState", ComparisonOp.Equals, "Corrupted"),
                    Change(ModuleType.Printer, "driverState", "OK"),
                    "Printer driver reinstalled."),
                RiskyFix("remove_readd_printer", ModuleType.Printer, "devicemanager", "printer",
                    Change(ModuleType.Printer, "connection", "Removed"),
                    "Printer device removed — Windows has not re-detected it."),

                Diagnostic("check_port_config", DesktopActionType.CheckPortConfig, ModuleType.CashDrawer, "cashdrawer", "port",
                    "Port assignment read."),
                Fix("move_cash_drawer_port", ModuleType.CashDrawer, "cashdrawer", "port",
                    Pre(ModuleType.CashDrawer, "port", ComparisonOp.Equals, "COM3"),
                    Change(ModuleType.CashDrawer, "port", "COM4"),
                    "Cash drawer moved to COM4."),

                Diagnostic("check_network_status", DesktopActionType.CheckNetworkStatus, ModuleType.Network, "network", "adapter",
                    "Adapter and gateway checked."),
                Fix("reconnect_network", ModuleType.Network, "network", "adapter",
                    Pre(ModuleType.Network, "isOnline", ComparisonOp.Equals, "false"),
                    Change(ModuleType.Network, "isOnline", "true"),
                    "Network adapter reconnected."),

                Diagnostic("check_pos_receipt_config", DesktopActionType.CheckPosReceiptConfig, ModuleType.POSSoftware, "possoftware", "receipt",
                    "Receipt template configuration read."),
                Fix("reset_pos_receipt_template", ModuleType.POSSoftware, "possoftware", "receipt",
                    Pre(ModuleType.POSSoftware, "receiptTemplate", ComparisonOp.Equals, "Broken"),
                    Change(ModuleType.POSSoftware, "receiptTemplate", "OK"),
                    "Receipt template reset to the default field mapping."),

                Diagnostic("check_terminal_network", DesktopActionType.CheckTerminalNetwork, ModuleType.Terminal, "terminal", "status",
                    "Terminal Wi-Fi and IP read."),

                // The staff/connection FIXES are inline field edits on their sub-tabs (Docs/app.md: they
                // don't go through the shared action list) — but the READS are ordinary diagnostics.
                Diagnostic("check_staff_account", DesktopActionType.CheckStaffAccount, ModuleType.POSSoftware, "possoftware", "staff",
                    "Staff account read: role, terminal assignment and sync state."),
                Diagnostic("check_pos_connections", DesktopActionType.CheckPosConnections, ModuleType.POSSoftware, "possoftware", "connections",
                    "POS connection roster read: registered terminal IP and database host."),

                // System (Windows) — the floor everything else stands on.
                Diagnostic("check_system_health", DesktopActionType.CheckSystemHealth, ModuleType.OS, "system", "health",
                    "System health read: disk space, pending updates, clock."),
                Fix("free_disk_space", ModuleType.OS, "system", "health",
                    Pre(ModuleType.OS, "diskSpace", ComparisonOp.Equals, "Full"),
                    Change(ModuleType.OS, "diskSpace", "OK"),
                    "Temp files and old spool data cleared — the system drive has room again."),
                Fix("sync_system_clock", ModuleType.OS, "system", "health",
                    Pre(ModuleType.OS, "systemTime", ComparisonOp.Equals, "Skewed"),
                    Change(ModuleType.OS, "systemTime", "OK"),
                    "System clock re-synced to the time server."),
                // Risky not because it can break state, but because it drops the session and the register
                // mid-shift — the UI must make the player agree to that first.
                RiskyFix("restart_pc", ModuleType.OS, "system", "health",
                    Change(ModuleType.OS, "pendingReboot", "false"),
                    "Machine restarted — the pending update finished applying."),

                Diagnostic("check_services", DesktopActionType.CheckServices, ModuleType.OS, "system", "services",
                    "Service list read."),
                Fix("restart_print_spooler", ModuleType.OS, "system", "services",
                    Pre(ModuleType.OS, "spoolerService", ComparisonOp.Equals, "Stopped"),
                    Change(ModuleType.OS, "spoolerService", "Running"),
                    "Print Spooler service restarted — queued jobs should drain now."),

                // ===== P17–P40 =====================================================================
                // Every fix below is a plain DesktopActionSO, so the app windows pick them up with no
                // new UI: RenderActionsForTab draws whatever is filed under an (appKey, appTab).
                // A few "fixes" are things the AGENT walks the CUSTOMER through — reseating a cable,
                // turning a key — because not everything on a POS bench can be reached over a remote
                // session, and pretending otherwise would teach the wrong reflex.

                // --- Printer hardware & Windows-side config ---------------------------------------
                Diagnostic("check_printer_hardware", DesktopActionType.CheckPrinterHardware, ModuleType.Printer, "printer", "queue",
                    "Physical layer checked: cable, jam sensor, loaded roll width."),
                Fix("ask_customer_reseat_cable", ModuleType.Printer, "printer", "queue",
                    Pre(ModuleType.Printer, "cableConnected", ComparisonOp.Equals, "false"),
                    Change(ModuleType.Printer, "cableConnected", "true"),
                    "Walked the customer through reseating the data cable — Windows sees the device again."),
                Fix("ask_customer_clear_jam", ModuleType.Printer, "printer", "queue",
                    Pre(ModuleType.Printer, "paperJam", ComparisonOp.Equals, "Jammed"),
                    Change(ModuleType.Printer, "paperJam", "None"),
                    "Walked the customer through opening the cutter and clearing the caught receipt."),
                Fix("resume_print_queue", ModuleType.Printer, "printer", "queue",
                    Pre(ModuleType.Printer, "queuePaused", ComparisonOp.Equals, "true"),
                    Change(ModuleType.Printer, "queuePaused", "false"),
                    "Print queue resumed — held jobs are releasing."),
                Fix("set_printer_online", ModuleType.Printer, "devicemanager", "printer",
                    Pre(ModuleType.Printer, "connection", ComparisonOp.Equals, "Offline"),
                    Change(ModuleType.Printer, "connection", "Connected"),
                    "Cleared 'Use Printer Offline' — the device is back online in Windows."),
                Fix("set_default_printer", ModuleType.Printer, "devicemanager", "printer",
                    Pre(ModuleType.Printer, "defaultPrinter", ComparisonOp.NotEquals, "ReceiptPrinter"),
                    Change(ModuleType.Printer, "defaultPrinter", "ReceiptPrinter"),
                    "Default printer set back to the receipt printer."),
                Fix("set_paper_width", ModuleType.Printer, "printer", "queue",
                    Pre(ModuleType.Printer, "paperWidth", ComparisonOp.NotEquals, "80mm"),
                    Change(ModuleType.Printer, "paperWidth", "80mm"),
                    "Paper width set to 80mm to match the roll that's actually loaded."),
                Fix("register_printer_in_pos", ModuleType.POSSoftware, "possoftware", "receipt",
                    Pre(ModuleType.POSSoftware, "printerVisible", ComparisonOp.Equals, "false"),
                    Change(ModuleType.POSSoftware, "printerVisible", "true"),
                    "Receipt printer registered to this POS station."),

                // --- Cash drawer -------------------------------------------------------------------
                Diagnostic("check_drawer_hardware", DesktopActionType.CheckDrawerHardware, ModuleType.CashDrawer, "cashdrawer", "port",
                    "Drawer key lock and trigger mode checked."),
                Fix("ask_customer_unlock_drawer", ModuleType.CashDrawer, "cashdrawer", "port",
                    Pre(ModuleType.CashDrawer, "lockState", ComparisonOp.Equals, "Locked"),
                    Change(ModuleType.CashDrawer, "lockState", "Unlocked"),
                    "Asked the customer to turn the drawer key to the unlocked position."),
                Fix("set_drawer_trigger_onprint", ModuleType.CashDrawer, "cashdrawer", "port",
                    Pre(ModuleType.CashDrawer, "triggerMode", ComparisonOp.NotEquals, "OnPrint"),
                    Change(ModuleType.CashDrawer, "triggerMode", "OnPrint"),
                    "Drawer set to release when a receipt prints."),

                // --- Terminal ----------------------------------------------------------------------
                Diagnostic("check_terminal_pairing", DesktopActionType.CheckTerminalPairing, ModuleType.Terminal, "terminal", "status",
                    "Pairing, firmware, chip reader config and live/training mode checked."),
                Fix("repair_terminal", ModuleType.Terminal, "terminal", "status",
                    Pre(ModuleType.Terminal, "pairingState", ComparisonOp.Equals, "Unpaired"),
                    Change(ModuleType.Terminal, "pairingState", "Paired"),
                    "Terminal re-paired with the POS — a fresh token was issued."),
                Fix("update_terminal_firmware", ModuleType.Terminal, "terminal", "status",
                    Pre(ModuleType.Terminal, "firmwareVersion", ComparisonOp.LessThan, "4.0"),
                    Change(ModuleType.Terminal, "firmwareVersion", "4.2"),
                    "Terminal firmware updated to 4.2."),
                Fix("reload_emv_config", ModuleType.Terminal, "terminal", "status",
                    Pre(ModuleType.Terminal, "emvConfig", ComparisonOp.Equals, "Corrupt"),
                    Change(ModuleType.Terminal, "emvConfig", "OK"),
                    "Chip reader configuration reloaded from the processor."),
                Fix("switch_terminal_to_live", ModuleType.Terminal, "terminal", "status",
                    Pre(ModuleType.Terminal, "mode", ComparisonOp.NotEquals, "Live"),
                    Change(ModuleType.Terminal, "mode", "Live"),
                    "Terminal switched out of training mode — transactions are real again."),

                // --- POS ------------------------------------------------------------------------------
                Diagnostic("check_pos_licensing", DesktopActionType.CheckPosLicensing, ModuleType.POSSoftware, "possoftware", "receipt",
                    "Licence, tax rate, price sync and offline mode checked."),
                Fix("renew_pos_license", ModuleType.POSSoftware, "possoftware", "receipt",
                    Pre(ModuleType.POSSoftware, "licenseState", ComparisonOp.Equals, "Expired"),
                    Change(ModuleType.POSSoftware, "licenseState", "Valid"),
                    "Licence reactivated against the vendor's server."),
                Fix("exit_pos_offline_mode", ModuleType.POSSoftware, "possoftware", "receipt",
                    Pre(ModuleType.POSSoftware, "offlineMode", ComparisonOp.Equals, "true"),
                    Change(ModuleType.POSSoftware, "offlineMode", "false"),
                    "POS taken out of offline mode — queued sales are uploading."),
                Fix("retry_batch_settlement", ModuleType.POSSoftware, "possoftware", "receipt",
                    Pre(ModuleType.POSSoftware, "batchState", ComparisonOp.Equals, "SettleFailed"),
                    Change(ModuleType.POSSoftware, "batchState", "Open"),
                    "Failed batch re-submitted and accepted — funds are moving."),
                Fix("correct_tax_rate", ModuleType.POSSoftware, "possoftware", "receipt",
                    Pre(ModuleType.POSSoftware, "taxRate", ComparisonOp.NotEquals, "8.25"),
                    Change(ModuleType.POSSoftware, "taxRate", "8.25"),
                    "Sales tax set back to 8.25%."),
                Fix("resync_price_list", ModuleType.POSSoftware, "possoftware", "receipt",
                    Pre(ModuleType.POSSoftware, "priceSync", ComparisonOp.Equals, "Stale"),
                    Change(ModuleType.POSSoftware, "priceSync", "Current"),
                    "Price list re-synced from head office."),

                // --- Network -----------------------------------------------------------------------
                Fix("ask_customer_move_router", ModuleType.Network, "network", "adapter",
                    Pre(ModuleType.Network, "signalStrength", ComparisonOp.NotEquals, "Good"),
                    Change(ModuleType.Network, "signalStrength", "Good"),
                    "Had the customer move the access point clear of the metal shelving — signal is solid now."),
                Fix("fix_dns_server", ModuleType.Network, "network", "adapter",
                    Pre(ModuleType.Network, "dnsServer", ComparisonOp.NotEquals, "192.168.1.1"),
                    Change(ModuleType.Network, "dnsServer", "192.168.1.1"),
                    "DNS pointed back at the store's own resolver."),
                Fix("allow_processor_port", ModuleType.Network, "network", "adapter",
                    Pre(ModuleType.Network, "firewallBlocking", ComparisonOp.Equals, "true"),
                    Change(ModuleType.Network, "firewallBlocking", "false"),
                    "Outbound rule added for the payment processor."),

                // --- OS ------------------------------------------------------------------------------
                Fix("restore_quarantined_file", ModuleType.OS, "system", "services",
                    Pre(ModuleType.OS, "antivirusQuarantine", ComparisonOp.Equals, "true"),
                    Change(ModuleType.OS, "antivirusQuarantine", "false"),
                    "Quarantined POS file restored and excluded from future scans."),
                Fix("elevate_windows_account", ModuleType.OS, "system", "health",
                    Pre(ModuleType.OS, "userAccount", ComparisonOp.Equals, "Standard"),
                    Change(ModuleType.OS, "userAccount", "Admin"),
                    "Signed the register in under the account that has the rights the POS needs."),
                Fix("set_power_plan_always_on", ModuleType.OS, "system", "health",
                    Pre(ModuleType.OS, "powerPlan", ComparisonOp.NotEquals, "AlwaysOn"),
                    Change(ModuleType.OS, "powerPlan", "AlwaysOn"),
                    "Power plan set so the register never sleeps during a shift."),
            };
            return list.ToArray();
        }

        // --- Persona -----------------------------------------------------------------------------
        private static PersonaProfileSO CreatePersona()
        {
            var p = ScriptableObject.CreateInstance<PersonaProfileSO>();
            p.personaId = "sunrise-diner-default";
            p.displayName = "Night-shift caller";   // the shop is rolled per ticket; the persona is not tied to one
            p.techLiteracy = 0.3f;
            p.cooperativeness = 0.6f;
            p.memoryAccuracy = 0.5f;
            p.emotionalState = 0.4f;
            p.honesty = 0.6f;
            p.misnaming = new[]
            {
                Misname("POS software", "the till"),
                Misname("terminal", "the card machine"),
                Misname("receipt printer", "the printer thingy"),
                Misname("network", "the internet thingy"),
            };
            p.laymanVocabulary = new[] { "the till", "the card machine", "the printer thingy", "the internet thingy", "won't ring anything up" };
            return Save(p, "Persona_NightCaller");
        }

        // --- Stores ------------------------------------------------------------------------------
        /// <summary>
        /// Word lists the CRM directory is combined from. Authored as an asset so a designer can edit
        /// the vocabulary, but the values live in StoreNameTableSO.Defaults so a project with stale
        /// content assets still gets a varied directory (StoreDirectoryFactory falls back to them).
        /// </summary>
        private static StoreNameTableSO CreateStoreNameTable()
        {
            var t = ScriptableObject.CreateInstance<StoreNameTableSO>();
            t.LoadDefaults();
            return Save(t, "StoreNameTable");
        }

        /// <summary>
        /// The template account. It is not a customer at all any more — StoreDirectoryFactory rolls the
        /// whole directory — but it still supplies the healthy machine baseline that every generated
        /// account and the simulated desktop are both built from.
        /// </summary>
        private static StoreProfileSO CreateRealStore()
        {
            var s = ScriptableObject.CreateInstance<StoreProfileSO>();
            s.storeId = "ST-1042";
            s.storeName = "Sunrise Diner";
            s.ownerName = "Maria Alvarez";
            s.phoneNumber = "555-0142";
            s.address = "482 Elm St";
            s.remoteId = "482 913 706";
            s.machines = new[] { new MachineConfig { machineId = "REG-1", osVersion = "Win 10 IoT", posSoftwareVersion = "POS Suite 4.2.1", hardware = new HardwareSpec(), baseline = new ModuleBaseline() } };
            return Save(s, "Store_Sunrise");
        }

        // --- Receipt templates ----------------------------------------------------------------------
        /// <summary>
        /// Field layout per receipt type (Docs/app.md "Printer — 4 loại receipt"). The test page carries
        /// no transaction data; the customer copy does — which is why P5 (broken template) shows up on
        /// the customer copy while the test page still prints perfectly.
        /// </summary>
        private static ReceiptTemplateSO[] CreateReceiptTemplates()
        {
            var testPage = ScriptableObject.CreateInstance<ReceiptTemplateSO>();
            testPage.type = ReceiptType.TestPage;
            testPage.fields = new[]
            {
                Field("Printer model", true), Field("Driver version", true), Field("Alignment pattern", true),
            };
            testPage = Save(testPage, "Receipt_TestPage");

            var customer = ScriptableObject.CreateInstance<ReceiptTemplateSO>();
            customer.type = ReceiptType.Customer;
            customer.fields = new[]
            {
                Field("Store name", true), Field("Date / time", true), Field("Items", true),
                Field("Total", true),                 // the field P5's broken template drops
                Field("Card last 4", true), Field("Thank-you line", false),
            };
            customer = Save(customer, "Receipt_Customer");

            return new[] { testPage, customer };
        }

        private static ReceiptField Field(string label, bool required) => new() { label = label, required = required };

        // --- Knowledge base ------------------------------------------------------------------------
        /// <summary>
        /// One article per authored issue (P1–P7). Two jobs, deliberately split:
        /// - KB-001 is the ONBOARDING article — the first pool tier is P1-only, so it is the only one that
        ///   ever auto-attaches (through day 5). The rest still carry guidanceForIssueIds so they are ready
        ///   if that tier boundary ever moves.
        /// - All of them are lookup material for after the training wheels come off (day 6+), when the
        ///   player opens the Knowledge Base themselves and searches by category or error code.
        /// Each one teaches the DISCRIMINATION that GDD §14 is built around (P3 looks like P2, P5 is not a
        /// printer fault at all, P7 is a POS-side record problem) — the clues only report raw symptoms.
        /// Only KB-002 lists an error code, because "Code 39" is the only code any clue text mentions;
        /// inventing more would make the search box return hits for codes the game never shows.
        /// </summary>
        private static KnowledgeArticleSO[] CreateKnowledgeArticles()
        {
            var kb1 = Article("KB-001", "Receipt printer won't print — start here", IssueCategory.Printer,
                "If the printer looks completely dead, check the print queue FIRST — Diagnostic ▸ Check print queue. "
                + "'Out of paper' means exactly that: refill the tray, then confirm with a test page. Don't jump straight "
                + "to reinstalling drivers or swapping ports until the queue message rules that out.",
                new[] { "P1" });

            var kb2 = Article("KB-002", "Printer reports Code 39 / device cannot start", IssueCategory.Printer,
                "Device Manager ▸ 'This device cannot start (Code 39)' is a DRIVER fault, not a hardware one. Reinstall "
                + "the printer driver, then confirm with a test page.\n"
                + "Before you do: open Device Manager and read the actual status. If the driver reports OK, the jam is "
                + "somewhere else — a port conflict looks almost identical from the customer's side (see KB-003). "
                + "Avoid 'Remove & re-add printer' as a first move: if the driver wasn't the cause you end up with a "
                + "removed device on top of the original fault, and the ticket closes Degraded.",
                new[] { "P2" }, "39", "Code 39");

            var kb3 = Article("KB-003", "Cash drawer stopped popping open", IssueCategory.CashDrawer,
                "The drawer is triggered by the printer, so a drawer that went quiet usually means the two are fighting "
                + "over a serial port. Compare Cash Drawer Config ▸ port with the printer's port — the same COM number "
                + "means a conflict. Move the drawer to a free port and confirm with a test page.\n"
                + "Tell this apart from a driver fault: with a port conflict the printer driver still reports OK. If the "
                + "driver is the thing complaining, you are looking at KB-002 instead.",
                new[] { "P3" });

            var kb4 = Article("KB-004", "Everything at the front is dead", IssueCategory.Network,
                "When the customer says nothing works at all, check the network before anything else — Network Settings ▸ "
                + "Check network status. A failed gateway ping takes down POS, and the terminal and printer with it.\n"
                + "While the network is down, downstream apps report Blocked rather than Error: that is the system saying "
                + "it cannot even see those modules yet, so there is nothing to diagnose there. Reconnect the network "
                + "FIRST, then re-read the other apps — a second, real fault is often waiting underneath.",
                new[] { "P4" });

            var kb5 = Article("KB-005", "Test page prints fine but the customer's receipt is wrong", IssueCategory.POS,
                "A test page exercises the printer and driver only — it carries no transaction data. So 'test page OK but "
                + "the customer copy is missing the total' rules the printer OUT and points at the POS receipt template.\n"
                + "Check POS Manager ▸ Receipt Config for a field dropped from the mapping, reset the template, then "
                + "confirm by printing a real customer copy from POS Manager ▸ Database — not another test page. Not "
                + "everything involving paper is a printer fault.",
                new[] { "P5" });

            var kb6 = Article("KB-006", "Register sits there and won't ring anything up", IssueCategory.Terminal,
                "A terminal can have perfectly good internet and still be unable to reach POS — if it joined the wrong "
                + "Wi-Fi. Every nearby network hands out its own IP range, so joining the guest network (or the shop next "
                + "door, or someone's hotspot) puts the terminal on a subnet POS cannot see.\n"
                + "Read the store's real SSID in Network Settings, compare it with POS Terminal ▸ Status, and re-join the "
                + "correct network. IP and gateway follow automatically — you never type those in. 'Not connecting' does "
                + "not have to mean the network is down.",
                new[] { "P6" });

            var kb7 = Article("KB-007", "Register is healthy but POS still refuses it", IssueCategory.POS,
                "POS keeps a registered roster of terminal addresses. After a router reboot the terminal can pick up a "
                + "new DHCP address while POS still has the old one on file — the terminal is on the right network with a "
                + "valid IP, and POS rejects it anyway. The fault is on the POS side, not the terminal side.\n"
                + "Compare POS Terminal ▸ Status (the terminal's actual IP) against POS Manager ▸ Connections (what POS "
                + "has registered), then re-register the current address. The real IP is shown for you — never guess it. "
                + "Keep this separate from a staff member who cannot log in: that is a role/assignment problem on one "
                + "account, not a connectivity problem.",
                new[] { "P7" });

            var kb8 = Article("KB-008", "A staff member can't log in (but everyone else can)", IssueCategory.Business,
                "This is a PERMISSION problem, not a hardware one, and it has a tell: the register keeps working for "
                + "everybody else. Confirm that first — a terminal on the right Wi-Fi still charging cards rules out "
                + "KB-004 and KB-006 in one look.\n"
                + "Then walk POS Manager ▸ Staff Mgmt top to bottom in order: does the account have a role, is it "
                + "assigned to a terminal, is that THIS terminal, has the change synced. The first line that reads wrong "
                + "is your fault; the ones below it are noise until you fix that one.\n"
                + "A blank role blocks login, so ANY role clears the symptom — that does not make any role correct. "
                + "Admin carries refund, void and close-batch rights; handing that to a new hire to save a minute is an "
                + "over-privilege incident and the ticket closes Degraded even though the login now works and the "
                + "customer is happy. Assign Sale unless they explicitly ask for a manager account.",
                new[] { "P8" });

            var kb9 = Article("KB-009", "Login refused and the account has no terminal at all", IssueCategory.Business,
                "A role on its own is not enough: POS also needs to know WHICH register the account may sign in at. An "
                + "account with a valid role and a blank terminal assignment is refused everywhere, which reads to the "
                + "customer exactly like a broken till.\n"
                + "Check POS Manager ▸ Staff Mgmt: if the role line is fine and the assignment line is empty, assign the "
                + "register they are standing at and have them retry. Don't touch the role — it isn't the fault, and "
                + "raising it is how you turn a clean ticket into an over-privilege one (KB-008).",
                new[] { "P9" });

            var kb10 = Article("KB-010", "Account is assigned — just not to this register", IssueCategory.Business,
                "'Not assigned' and 'assigned somewhere else' look identical to the customer and read very differently "
                + "in Staff Mgmt. Compare the assignment against the register ID the terminal reports about itself, not "
                + "against what the caller says — customers mix up which till is which constantly.\n"
                + "Re-assign to the register they are actually standing at. Watch for red herrings while you're in "
                + "there: a low paper warning on the same screen has nothing to do with a login.",
                new[] { "P10" });

            var kb11 = Article("KB-011", "The account looks right and login still fails", IssueCategory.Business,
                "POS pushes roster changes to the terminal; until that push lands, the terminal is still enforcing the "
                + "old permissions. So a screen where role and terminal both read correctly, with login still refused, "
                + "usually means the change was made but never synced.\n"
                + "Run Sync POS ▸ terminal, then have them try again. If someone 'already fixed it this morning' and it "
                + "never worked, this is almost always why — don't start re-assigning things that are already correct.",
                new[] { "P11" });

            var kb12 = Article("KB-012", "Can't look up or re-send an old receipt", IssueCategory.POS,
                "Receipt history lives in the database, not on the printer and not in the current batch. If reprints and "
                + "lookups fail while printing itself is fine, check POS Manager ▸ Connections for the database host.\n"
                + "Typos in the host name are the usual cause and they are easy to skim past — compare it character by "
                + "character against the correct address. A test page proves nothing here: it needs no transaction data, "
                + "so it will pass happily while the record store is unreachable.",
                new[] { "P12" });

            // The OS group is filed under the category the PLAYER will search — the symptom they can see,
            // not the layer the fault lives in. Someone chasing a dead printer looks under Printer; the
            // article is what tells them the root is a Windows service.
            var kb13 = Article("KB-013", "Jobs queue up and never print", IssueCategory.Printer,
                "Paper present, driver healthy, jobs stacking up at 'Spooling' — that combination is the Print Spooler "
                + "service, not the printer. Windows accepts the job and then has nothing running to hand it over.\n"
                + "Check System ▸ Services for Print Spooler, restart it, and confirm with a test page. Work out this "
                + "order before reinstalling anything: a driver reinstall (KB-002) will not start a stopped service, and "
                + "you will have changed the machine for nothing.",
                new[] { "P13" });

            var kb14 = Article("KB-014", "The whole machine is unusable", IssueCategory.OS,
                "When every app is frozen rather than one being broken, look at Windows before any device. A full system "
                + "drive stops the machine writing temp files, and nothing that needs to start can start.\n"
                + "System ▸ Health shows free space. Clear it, then re-read every other app — while the disk was full "
                + "they all reported Blocked, which means they were never actually diagnosed. There is very often a "
                + "second, real fault waiting underneath.",
                new[] { "P14" });

            var kb15 = Article("KB-015", "Machine is nagging about an update", IssueCategory.OS,
                "A staged update holds services in a half-applied state: the install finished, the restart didn't. Until "
                + "it reboots, the machine reports Blocked all the way down.\n"
                + "Restarting is the fix, but it is disruptive — it drops your remote session and takes the register "
                + "offline for a few minutes. Tell the customer before you do it, and check the batch first: settle it "
                + "or make sure they know what is still open.",
                new[] { "P15" });

            var kb16 = Article("KB-016", "Every card is declined but the terminal looks fine", IssueCategory.Terminal,
                "Card authorization runs over a secure connection, and secure connections check the machine's clock. If "
                + "Windows has drifted far enough, the processor rejects the handshake and every card comes back "
                + "declined — while Wi-Fi, IP and POS registration all read perfectly correct.\n"
                + "So when the terminal is on the store's own network (rules out KB-006) with its current IP registered "
                + "(rules out KB-007) and cards still fail, go to System ▸ Health and check the clock. Cash still working "
                + "while cards do not is the giveaway: the register is fine, the connection to the processor is not.",
                new[] { "P16" });

            // --- P17–P40. Each one exists to separate its issue from the one it is mistaken for, so the
            // discrimination is the first thing in the text, not a footnote at the end.
            var kb17 = Article("KB-017", "Grinding noise, paper hanging out", IssueCategory.Printer,
                "Paper present and a mechanical noise means a jam, not an empty roll (KB-001) — those two sound "
                + "completely different to the customer and the queue tells them apart in one look.\n"
                + "Walk them through opening the cutter and pulling the caught receipt out FORWARD, in the direction of "
                + "travel. Then a test page, because a jam that was cleared badly re-jams on the next print.",
                new[] { "P17" });

            var kb18 = Article("KB-018", "Windows can't see the printer at all", IssueCategory.Printer,
                "There is a difference between a device reporting an error and a device not being there. An error means "
                + "something to fix; an absence means nothing is connected. Device Manager not listing the printer is an "
                + "absence — there is nothing there to reinstall a driver onto (KB-002 does not apply).\n"
                + "That is a cable, and you cannot reach it from here. Have the customer unplug and firmly reseat the "
                + "data cable at both ends, then re-check. Some faults are only fixable by the person in the room.",
                new[] { "P18" });

            var kb19 = Article("KB-019", "Jobs are accepted but held", IssueCategory.Printer,
                "A paused queue and a stopped spooler (KB-013) look identical from the counter and are one word apart on "
                + "screen. Paused means the service is running and has been told to hold; stopped means there is no "
                + "service to hold anything.\n"
                + "Read the queue's own status line before acting. If it says Paused, resume it — restarting the spooler "
                + "for a paused queue changes nothing and costs you a minute you did not have.",
                new[] { "P19" });

            var kb20 = Article("KB-020", "Nothing comes out here, pages appear elsewhere", IssueCategory.Printer,
                "If the customer mentions pages turning up on another printer, stop diagnosing the receipt printer — it "
                + "is healthy and simply is not being sent anything. Windows' default printer has been changed, usually "
                + "by someone printing something ordinary from the back office.\n"
                + "Set the default back to the receipt printer. Worth asking who prints from that machine: if it happens "
                + "monthly, the real fix is a conversation, not a setting.",
                new[] { "P20" });

            var kb21 = Article("KB-021", "Windows prints, the POS says there's no printer", IssueCategory.POS,
                "These are two separate registrations. Windows having a working printer says nothing about whether the "
                + "POS has one assigned to this station — the POS only ever checks 'can I see a printer here'.\n"
                + "So a passing test page does not clear this; it proves the half that already worked. Register the "
                + "printer to the station in POS Manager, then print a real receipt rather than another test page.",
                new[] { "P21" });

            var kb22 = Article("KB-022", "\"Printer offline\" with the printer sitting right there", IssueCategory.Printer,
                "'Use Printer Offline' is a checkbox someone ticked, not a fault the printer developed. The driver is "
                + "healthy and the device is present — nothing needs reinstalling (KB-002) and nothing needs unplugging "
                + "(KB-018).\n"
                + "Untick it in the device's menu and print a test page. If it re-ticks itself later, that is a genuine "
                + "connectivity problem underneath and you are back to the cable.",
                new[] { "P22" });

            var kb23 = Article("KB-023", "Receipts print half-width with the edge cut off", IssueCategory.Printer,
                "Missing FIELDS is a template problem (KB-005). Content that is present but laid out too narrow and "
                + "clipped is a paper width problem: the printer is composing for a 58mm roll while an 80mm roll is "
                + "loaded, or the reverse.\n"
                + "Check the configured width against the roll actually in the machine and match them. Resetting the "
                + "receipt template will not help here — every field is already in the mapping.",
                new[] { "P23" });

            var kb24 = Article("KB-024", "Drawer clicks but stays shut", IssueCategory.CashDrawer,
                "If you can hear the release fire, the electronics have done their job: the signal arrived, the solenoid "
                + "moved. Something mechanical is holding the drawer. That rules out a port conflict (KB-003), which "
                + "produces no click at all.\n"
                + "Ask about the key lock on the front of the drawer. It gets turned during a cash count and forgotten, "
                + "and no amount of configuration will beat a physical lock.",
                new[] { "P24" });

            var kb25 = Article("KB-025", "Drawer stopped opening by itself", IssueCategory.CashDrawer,
                "'Has to be opened by hand now' is different from 'won't open at all'. The drawer works — it is just no "
                + "longer being told to open when a receipt prints, because its trigger mode was changed to Manual.\n"
                + "Set the trigger back to open-on-print. Check the port assignment while you are there, but a conflict "
                + "(KB-003) breaks it entirely rather than turning it into a button.",
                new[] { "P25" });

            var kb26 = Article("KB-026", "Card machine has lost the till", IssueCategory.Terminal,
                "Three different things can sit between a terminal and its POS, and they fail separately: the network "
                + "(KB-006), the address on file (KB-007), and the pairing between the two devices. A terminal on the "
                + "right Wi-Fi with the right IP registered and still no connection is the third one.\n"
                + "Re-pair the terminal from POS Terminal ▸ Status. It issues a fresh token; nothing about the network "
                + "needs touching.",
                new[] { "P26" });

            var kb27 = Article("KB-027", "Terminal can't talk to the till after a POS update", IssueCategory.Terminal,
                "When the trouble starts right after an update, suspect a version gap before anything else. POS builds "
                + "declare a minimum terminal firmware, and updating one side without the other leaves them unable to "
                + "negotiate.\n"
                + "Compare the terminal's firmware against the POS requirement and update the terminal. Re-pairing "
                + "(KB-026) will not help — pairing is not the thing failing.",
                new[] { "P27" });

            var kb28 = Article("KB-028", "Chip is refused, swipe goes through", IssueCategory.Terminal,
                "A terminal that fails ONE payment path and passes another is not disconnected — it is misconfigured. "
                + "Chip and swipe use different config; corrupt chip config declines chip cards while swipe keeps "
                + "working, which is why the customer describes it as 'sometimes'.\n"
                + "Reload the chip reader configuration from the processor. Anything network-shaped (KB-006, KB-037) "
                + "would take out both paths, not one.",
                new[] { "P28" });

            var kb29 = Article("KB-029", "Everything approves but no money arrives", IssueCategory.Terminal,
                "Approvals that all succeed while nothing reaches the bank is the signature of training mode. The "
                + "terminal simulates the whole exchange, prints a receipt, and sends nothing — it is designed to be "
                + "convincing, which is exactly why it costs a shop a night's takings.\n"
                + "Check the terminal's mode first whenever the complaint is about money rather than about errors. "
                + "A failed settlement (KB-032) looks similar in the bank account and completely different on screen.",
                new[] { "P29" });

            var kb30 = Article("KB-030", "The POS won't open at all", IssueCategory.POS,
                "An application that flashes up and closes is refusing to start, not crashing at random. Check the "
                + "licence before anything else: an expired one produces exactly this, and nothing downstream of the POS "
                + "can be assessed while it will not run.\n"
                + "Reactivate against the vendor, then re-read the rest of the machine. If the POS was down all evening, "
                + "nothing else has been genuinely tested yet.",
                new[] { "P30" });

            var kb31 = Article("KB-031", "Sales work but reports are empty", IssueCategory.POS,
                "Offline mode is store-and-forward: the POS keeps taking sales and holds them locally. Staff see nothing "
                + "wrong, which is why this is usually reported days late as a reporting problem.\n"
                + "Confirm the network is actually healthy first — if it is, the POS went offline and stayed there rather "
                + "than being pushed. Bring it back online and let the queue upload before you hang up.",
                new[] { "P31" });

            var kb32 = Article("KB-032", "Last night's takings never landed", IssueCategory.POS,
                "Authorization and settlement are two separate events. Cards can approve perfectly all evening and the "
                + "settlement run that actually moves the money can still fail at close — the transactions exist, they "
                + "were simply never submitted.\n"
                + "Check the batch state and re-submit the failed batch. Distinguish this from training mode (KB-029), "
                + "where the transactions never existed in the first place.",
                new[] { "P32" });

            var kb33 = Article("KB-033", "Totals are wrong on every receipt", IssueCategory.POS,
                "A missing field is a template problem (KB-005). A field that prints a wrong NUMBER is a configuration "
                + "problem — usually the tax rate. The receipt is structurally perfect and financially wrong, which is "
                + "worse, because nobody notices until a customer does the arithmetic.\n"
                + "Check the configured rate against what the shop should be charging, correct it, and tell them plainly "
                + "how long it has been wrong. That part matters more than the fix.",
                new[] { "P33" });

            var kb34 = Article("KB-034", "Register is ringing up old prices", IssueCategory.POS,
                "Prices come down from head office on a schedule. When the register is consistently a set behind, the "
                + "sync stopped running — not the connection, the job. A live connection and a stale price list sit "
                + "together quite happily.\n"
                + "Force a re-sync from POS Manager. Then ask when they last saw prices update: if the answer is 'never', "
                + "the schedule was never set up and this will come back.",
                new[] { "P34" });

            var kb35 = Article("KB-035", "It works, then it doesn't, then it works", IssueCategory.Network,
                "Intermittent is a diagnosis in itself. A dead network (KB-004) is dead every time you look; a weak "
                + "signal fails only under load, which is why it happens 'when we're busy' and clears the moment you "
                + "check it.\n"
                + "Look at signal strength rather than up/down state, and ask what is physically near the access point. "
                + "Metal shelving and microwaves win. Do not chase the terminal for this — it is downstream of the link.",
                new[] { "P35" });

            var kb36 = Article("KB-036", "Internet works but the till can't find our records", IssueCategory.Network,
                "'Can't find the database' has two causes that produce the identical message: the host name is wrong "
                + "(KB-012), or the name is right and nothing here can resolve it. Read the name character by character "
                + "first — if it is correct, the resolver is the problem.\n"
                + "A public DNS server resolves public sites happily and knows nothing about the shop's internal names, "
                + "which is exactly why the customer can browse the news while the till cannot see its own database.",
                new[] { "P36" });

            var kb37 = Article("KB-037", "Cards fail while the internet is fine", IssueCategory.Network,
                "Two different faults produce this and neither is 'the internet is down'. A firewall blocks one outbound "
                + "port and leaves everything else working; a wrong system clock (KB-016) fails the secure handshake with "
                + "everything else working. Both let the customer browse the news while cards decline.\n"
                + "Check the clock first — it takes a second and rules out half the problem. If the clock is right, look "
                + "at what is filtering outbound traffic to the processor.",
                new[] { "P37" });

            var kb38 = Article("KB-038", "The POS closes itself the moment it opens", IssueCategory.OS,
                "Started right after a security warning is the whole clue. Antivirus quarantines a file the POS needs at "
                + "startup, so the application is not damaged — a piece of it has been taken away from it, and it dies "
                + "the moment it reaches for that piece.\n"
                + "Restore the quarantined file and add an exclusion, or it will be taken again on the next scan. "
                + "Reinstalling the POS also works and takes an hour you do not have at 2am.",
                new[] { "P38" });

            var kb39 = Article("KB-039", "Wrong kind of permission", IssueCategory.OS,
                "There are two independent permission systems in play and they are easy to confuse. The POS staff role "
                + "(KB-008) governs what a person may do inside the till. The Windows account governs whether the "
                + "application may run at all.\n"
                + "A POS that will not start while the staff account reads perfectly correct is the Windows layer. Check "
                + "who the machine is signed in as before touching anything in Staff Management.",
                new[] { "P39" });

            var kb40 = Article("KB-040", "Card machine drops off when the counter is quiet", IssueCategory.OS,
                "'Only when nobody's touching it' is a power setting, not a fault. The PC sleeps, everything attached to "
                + "it drops, and it all comes back when someone nudges the mouse — which is why it always looks fine by "
                + "the time you connect.\n"
                + "Set the power plan so the register never sleeps during trading hours. Chasing this as a pairing "
                + "(KB-026) or signal (KB-035) problem will find nothing, because by then it has already woken up.",
                new[] { "P40" });

            return new[]
            {
                Save(kb17, "KB_017_PaperJam"), Save(kb18, "KB_018_CableUnplugged"),
                Save(kb19, "KB_019_QueuePaused"), Save(kb20, "KB_020_WrongDefaultPrinter"),
                Save(kb21, "KB_021_PosCannotSeePrinter"), Save(kb22, "KB_022_PrinterOffline"),
                Save(kb23, "KB_023_PaperWidth"), Save(kb24, "KB_024_DrawerLocked"),
                Save(kb25, "KB_025_DrawerManualTrigger"), Save(kb26, "KB_026_TerminalUnpaired"),
                Save(kb27, "KB_027_FirmwareTooOld"), Save(kb28, "KB_028_EmvConfig"),
                Save(kb29, "KB_029_TrainingMode"), Save(kb30, "KB_030_LicenseExpired"),
                Save(kb31, "KB_031_PosOfflineMode"), Save(kb32, "KB_032_BatchSettleFailed"),
                Save(kb33, "KB_033_WrongTaxRate"), Save(kb34, "KB_034_StalePrices"),
                Save(kb35, "KB_035_WeakSignal"), Save(kb36, "KB_036_WrongDns"),
                Save(kb37, "KB_037_FirewallBlocking"), Save(kb38, "KB_038_AntivirusQuarantine"),
                Save(kb39, "KB_039_WindowsAccount"), Save(kb40, "KB_040_MachineSleeps"),

                Save(kb1, "KB_001_PrinterStartHere"), Save(kb2, "KB_002_PrinterDriverCode39"),
                Save(kb3, "KB_003_CashDrawerPortConflict"), Save(kb4, "KB_004_NetworkDown"),
                Save(kb5, "KB_005_ReceiptTemplate"), Save(kb6, "KB_006_TerminalWrongWifi"),
                Save(kb7, "KB_007_StaleTerminalIp"),
                Save(kb8, "KB_008_StaffCannotLogIn"), Save(kb9, "KB_009_StaffNoTerminal"),
                Save(kb10, "KB_010_AssignedToWrongRegister"), Save(kb11, "KB_011_AssignmentNotSynced"),
                Save(kb12, "KB_012_ReceiptLookupFails"),
                Save(kb13, "KB_013_SpoolerStopped"), Save(kb14, "KB_014_DiskFull"),
                Save(kb15, "KB_015_PendingReboot"), Save(kb16, "KB_016_ClockSkew"),
            };
        }

        private static KnowledgeArticleSO Article(string id, string title, IssueCategory cat, string content,
                                                 string[] guidanceFor, params string[] errorCodes)
        {
            var a = ScriptableObject.CreateInstance<KnowledgeArticleSO>();
            a.articleId = id;
            a.title = title;
            a.category = cat;
            a.content = content;
            a.relatedErrorCodes = errorCodes ?? new string[0];
            a.guidanceForIssueIds = guidanceFor ?? new string[0];
            return a;
        }

        // --- Issues ------------------------------------------------------------------------------
        private static IssueSO[] CreateIssues()
        {
            // Store-named values are TOKENS, resolved per ticket against the calling shop
            // (StoreIdentity). Authoring a literal SSID here would name one shop in the CRM
            // and make the fault unfixable for the other nineteen.
            string mainSsid = StoreIdentity.TokenSsid;
            string guestSsid = StoreIdentity.TokenSsidGuest;

            var p1 = Issue("P1", IssueCategory.Printer, DifficultyTier.Basic, false,
                Fault(ModuleType.Printer, "paperLevel", "Empty"),
                new[]
                {
                    Sym("The receipt printer just won't print anything, and the paper tray light is on.", "Printer.paperLevel = Empty."),
                },
                new[]
                {
                    Clue(DesktopActionType.CheckPrintQueue, "Print queue shows: 'Out of paper'.", false),
                    Clue(DesktopActionType.CheckPrintQueue, "Toner light is blinking (looks unrelated to this job).", true),
                },
                Resolution(true, ReceiptType.TestPage, Check(ModuleType.Printer, "paperLevel", ComparisonOp.Equals, "OK")),
                null);

            var p2 = Issue("P2", IssueCategory.Printer, DifficultyTier.Medium, false,
                Fault(ModuleType.Printer, "driverState", "Corrupted"),
                new[] { Sym("The printer is jammed or something — nothing comes out and there's a red light.", "Printer.driverState = Corrupted (Device Manager Code 39).") },
                new[] { Clue(DesktopActionType.OpenDeviceManager, "Device Manager: 'This device cannot start (Code 39)'. Print queue looks stuck.", false) },
                Resolution(true, ReceiptType.TestPage,
                    Check(ModuleType.Printer, "driverState", ComparisonOp.Equals, "OK"),
                    Check(ModuleType.Printer, "connection", ComparisonOp.NotEquals, "Removed")),
                new[] { Fault(ModuleType.Printer, "connection", "Removed") });

            var p3 = Issue("P3", IssueCategory.CashDrawer, DifficultyTier.Hard, false,
                Fault(ModuleType.CashDrawer, "port", "COM3"),
                new[] { Sym("Weird — now the cash drawer doesn't pop open automatically anymore.", "CashDrawer.port conflicts with Printer.port (COM3).") },
                new[] { Clue(DesktopActionType.CheckPortConfig, "Cash Drawer is set to COM3 — same port as the Printer. Printer driver status looks OK though.", false) },
                Resolution(true, ReceiptType.TestPage, Check(ModuleType.CashDrawer, "port", ComparisonOp.NotEquals, "COM3")),
                null);

            var p4 = Issue("P4", IssueCategory.Network, DifficultyTier.Hard, true,
                Fault(ModuleType.Network, "isOnline", "false"),
                new[] { Sym("Everything at the front feels dead — the internet thingy shows no bars, and the printer's not working either.", "Network.isOnline = false — gateway unreachable.") },
                new[] { Clue(DesktopActionType.CheckNetworkStatus, "Ping to gateway: Request timed out. Network adapter shows Disconnected.", false) },
                Resolution(false, ReceiptType.TestPage, Check(ModuleType.Network, "isOnline", ComparisonOp.Equals, "true")),
                null);

            var p5 = Issue("P5", IssueCategory.POS, DifficultyTier.Hard, false,
                Fault(ModuleType.POSSoftware, "receiptTemplate", "Broken"),
                new[] { Sym("The test print looks fine, but the customer's copy is missing stuff, like the total is cut off.", "POSSoftware.receiptTemplate = Broken — customer-copy field mapping corrupted.") },
                new[]
                {
                    Clue(DesktopActionType.PrintTestPage, "Test page prints perfectly — hardware and driver look fine.", false),
                    Clue(DesktopActionType.PrintCustomerCopy, "Customer copy prints, but the total field is cut off.", false),
                    Clue(DesktopActionType.CheckPosReceiptConfig, "POS receipt template config shows a corrupted field mapping (missing total field).", false),
                },
                Resolution(true, ReceiptType.Customer, Check(ModuleType.POSSoftware, "receiptTemplate", ComparisonOp.Equals, "OK")),
                null);

            var p6 = Issue("P6", IssueCategory.Terminal, DifficultyTier.Medium, false,
                Fault(ModuleType.Terminal, "wifiNetwork", guestSsid),
                new[] { Sym("The register just sits there — it won't let me ring anything up, like it's not talking to the system at all.", "Terminal.wifiNetwork ≠ Network.ssid — joined the wrong SSID.") },
                new[] { Clue(DesktopActionType.CheckTerminalNetwork, $"Terminal ▸ Network shows Wi-Fi \"{guestSsid}\" — Network Settings shows the store's actual Wi-Fi is \"{mainSsid}\".", false) },
                Resolution(false, ReceiptType.TestPage, Check(ModuleType.Terminal, "wifiNetwork", ComparisonOp.Equals, mainSsid)),
                null);

            var p7 = Issue("P7", IssueCategory.POS, DifficultyTier.Medium, false,
                Fault(ModuleType.POSSoftware, "registeredTerminalIp", "192.168.1.77"),
                new[] { Sym("The register won't connect — it worked fine yesterday, nothing's changed on our end.", "POSSoftware.registeredTerminalIp stale vs the terminal's actual DHCP-leased IP.") },
                new[] { Clue(DesktopActionType.CheckTerminalNetwork, "Terminal ▸ Network shows the terminal's actual IP is 192.168.1.50 — POS Manager ▸ Connections still has 192.168.1.77 registered (stale).", false) },
                Resolution(false, ReceiptType.TestPage, Check(ModuleType.POSSoftware, "registeredTerminalIp", ComparisonOp.Equals, "192.168.1.50")),
                null);

            // --- "Soft" group: business/permission faults, GDD §15. These live OUTSIDE the hardware
            // cascade (Docs/app.md "Layer riêng"): POS stays OK, the terminal stays OK, and only one
            // person's login fails. The fixes are the inline editors on POS Manager ▸ Staff Mgmt /
            // Connections, so these issues carry a diagnostic clue but no Fix action of their own.
            var p8 = Issue("P8", IssueCategory.Business, DifficultyTier.Medium, false,
                Fault(ModuleType.POSSoftware, "staffRole", "None"),
                new[] { Sym("My new girl can't get into the register, I think the machine's broken.", "POSSoftware.staffRole = None — the account has no role.") },
                new[]
                {
                    Clue(DesktopActionType.CheckStaffAccount, "Staff Management: the account exists but its role is blank. Other staff sign in fine on this register.", false),
                    Clue(DesktopActionType.CheckTerminalNetwork, "Terminal is on the right Wi-Fi and charging cards normally — hardware is not the problem.", false),
                },
                Resolution(false, ReceiptType.TestPage, Check(ModuleType.POSSoftware, "staffRole", ComparisonOp.Equals, "Sale")),
                // GDD §15's trap: Admin also "fixes" the login, but hands a new hire refund/void/settle rights.
                new[] { Fault(ModuleType.POSSoftware, "staffRole", "Admin") });

            var p9 = Issue("P9", IssueCategory.Business, DifficultyTier.Medium, false,
                Fault(ModuleType.POSSoftware, "staffTerminal", ""),
                new[] { Sym("She's got her login and everything, it just won't take her at the front register.", "POSSoftware.staffTerminal is empty — no terminal assignment.") },
                new[] { Clue(DesktopActionType.CheckStaffAccount, "Staff Management: role is set, but the account isn't assigned to any terminal.", false) },
                Resolution(false, ReceiptType.TestPage, Check(ModuleType.POSSoftware, "staffTerminal", ComparisonOp.Equals, "REG-1")),
                null);

            var p10 = Issue("P10", IssueCategory.Business, DifficultyTier.Medium, false,
                Fault(ModuleType.POSSoftware, "staffTerminal", "REG-4"),
                new[] { Sym("One of the kids set her up on the other till last week, now she's on this one and it won't have her.", "POSSoftware.staffTerminal points at a different register than Terminal.machineId.") },
                new[]
                {
                    Clue(DesktopActionType.CheckStaffAccount, "Staff Management: the account IS assigned — to REG-4. This register is REG-1.", false),
                    Clue(DesktopActionType.CheckPrintQueue, "Paper is running low in the tray (unrelated to the login).", true),
                },
                Resolution(false, ReceiptType.TestPage, Check(ModuleType.POSSoftware, "staffTerminal", ComparisonOp.Equals, "REG-1")),
                null);

            var p11 = Issue("P11", IssueCategory.Business, DifficultyTier.Hard, false,
                Fault(ModuleType.POSSoftware, "terminalSynced", "false"),
                new[] { Sym("We fixed her account this morning and it still won't let her on.", "POSSoftware.terminalSynced = false — the roster change never reached the terminal.") },
                new[] { Clue(DesktopActionType.CheckStaffAccount, "Staff Management: role and terminal assignment both look correct, but the terminal hasn't picked up the change yet.", false) },
                Resolution(false, ReceiptType.TestPage, Check(ModuleType.POSSoftware, "terminalSynced", ComparisonOp.Equals, "true")),
                null);

            var p12 = Issue("P12", IssueCategory.POS, DifficultyTier.Hard, false,
                Fault(ModuleType.POSSoftware, "dbHost", StoreIdentity.TokenDbHostTypo),
                new[] { Sym("A customer wants his receipt sent again and the system says it can't find anything.", "POSSoftware.dbHost misspelled — the record store is unreachable.") },
                new[]
                {
                    Clue(DesktopActionType.CheckPosConnections, $"Connections: database host is \"{StoreIdentity.TokenDbHostTypo}\" and won't resolve. Compare it character by character with {StoreIdentity.TokenDbHost}.", false),
                    Clue(DesktopActionType.PrintTestPage, "Test page prints fine — the printer itself is healthy.", false),
                },
                Resolution(false, ReceiptType.TestPage, Check(ModuleType.POSSoftware, "dbHost", ComparisonOp.Equals, StoreIdentity.TokenDbHost)),
                null);

            // --- Windows / OS group. Two kinds, deliberately different (Docs/app.md §7):
            // P14/P15 are MACHINE-WIDE blockers — everything downstream reads Blocked, exactly like P4.
            // P13/P16 are service-level: the OS is the root cause but the Error surfaces on the module that
            // needed the service, so the player can still follow the trail instead of hitting a wall.
            var p13 = Issue("P13", IssueCategory.OS, DifficultyTier.Medium, false,
                Fault(ModuleType.OS, "spoolerService", "Stopped"),
                new[] { Sym("Nothing prints. There's paper in it, the light's green, it just sits there.", "OS.spoolerService = Stopped — jobs queue but never spool.") },
                new[]
                {
                    Clue(DesktopActionType.CheckPrintQueue, "Jobs are piling up and every one is stuck at 'Spooling' — none of them ever reach the printer.", false),
                    Clue(DesktopActionType.OpenDeviceManager, "Device Manager: printer driver reports OK, device present and healthy.", false),
                    Clue(DesktopActionType.CheckServices, "Services: Print Spooler — Stopped.", false),
                },
                Resolution(true, ReceiptType.TestPage, Check(ModuleType.OS, "spoolerService", ComparisonOp.Equals, "Running")),
                null);

            var p14 = Issue("P14", IssueCategory.OS, DifficultyTier.Hard, true,
                Fault(ModuleType.OS, "diskSpace", "Full"),
                new[] { Sym("The whole computer's gone stupid. Everything's frozen, nothing opens.", "OS.diskSpace = Full — the machine cannot write anything.") },
                new[] { Clue(DesktopActionType.CheckSystemHealth, "System drive: 0 GB free. Windows cannot write temp files, so nothing else can start properly.", false) },
                Resolution(false, ReceiptType.TestPage, Check(ModuleType.OS, "diskSpace", ComparisonOp.Equals, "OK")),
                null);

            var p15 = Issue("P15", IssueCategory.OS, DifficultyTier.Medium, true,
                Fault(ModuleType.OS, "pendingReboot", "true"),
                new[] { Sym("It keeps nagging about an update and nothing works right until we deal with it.", "OS.pendingReboot = true — the update is staged but not applied.") },
                new[] { Clue(DesktopActionType.CheckSystemHealth, "Updates installed and staged — a restart is required before services come back up.", false) },
                Resolution(false, ReceiptType.TestPage, Check(ModuleType.OS, "pendingReboot", ComparisonOp.Equals, "false")),
                null);

            var p16 = Issue("P16", IssueCategory.OS, DifficultyTier.Hard, false,
                Fault(ModuleType.OS, "systemTime", "Skewed"),
                new[] { Sym("Every card we run comes back declined. Cash is fine. It's not the customers, it's us.", "OS.systemTime = Skewed — TLS handshake to the processor is refused.") },
                new[]
                {
                    Clue(DesktopActionType.CheckTerminalNetwork, "Terminal is on the store's own Wi-Fi and POS has its current IP registered — connectivity checks out, yet every authorization is refused.", false),
                    Clue(DesktopActionType.CheckSystemHealth, "System clock reads three days behind. Secure connections reject a certificate this far out of date.", false),
                },
                Resolution(false, ReceiptType.TestPage, Check(ModuleType.OS, "systemTime", ComparisonOp.Equals, "OK")),
                null);

            // ===== P17–P40 ==========================================================================
            // Every one of these is built around a DISCRIMINATION against an issue that already exists.
            // A fault that produces a symptom the player has already learned to read isn't content, it's
            // padding — so each entry below names the issue it can be mistaken for.

            // --- Printer: the physical layer, then Windows-side config ---------------------------
            var p17 = Issue("P17", IssueCategory.Printer, DifficultyTier.Basic, false,
                Fault(ModuleType.Printer, "paperJam", "Jammed"),
                new[] { Sym("It's making a horrible grinding noise and there's paper hanging out the front.", "Printer.paperJam = Jammed at the cutter.") },
                new[] { Clue(DesktopActionType.CheckPrintQueue, "Queue reports a paper jam at the cutter. There IS paper loaded — the roll isn't the problem.", false) },
                Resolution(true, ReceiptType.TestPage, Check(ModuleType.Printer, "paperJam", ComparisonOp.Equals, "None")),
                null);

            var p18 = Issue("P18", IssueCategory.Printer, DifficultyTier.Basic, false,
                Fault(ModuleType.Printer, "cableConnected", "false"),
                new[] { Sym("The printer's just dead. No lights doing anything, nothing.", "Printer.cableConnected = false — no device on the port.") },
                new[]
                {
                    Clue(DesktopActionType.CheckPrinterHardware, "Windows sees no device on the port at all — not an error state, an absence. That's a cable, not software.", false),
                    Clue(DesktopActionType.OpenDeviceManager, "The printer isn't listed in Device Manager. There is nothing there to reinstall.", false),
                },
                Resolution(true, ReceiptType.TestPage, Check(ModuleType.Printer, "cableConnected", ComparisonOp.Equals, "true")),
                null);

            var p19 = Issue("P19", IssueCategory.Printer, DifficultyTier.Medium, false,
                Fault(ModuleType.Printer, "queuePaused", "true"),
                new[] { Sym("Receipts just aren't coming out. Everything else seems okay.", "Printer.queuePaused = true — someone paused the queue in Windows.") },
                new[] { Clue(DesktopActionType.CheckPrintQueue, "Queue is marked Paused. Jobs are being accepted and held — the spooler is running fine, it's simply been told to stop sending.", false) },
                Resolution(true, ReceiptType.TestPage, Check(ModuleType.Printer, "queuePaused", ComparisonOp.Equals, "false")),
                null);

            var p20 = Issue("P20", IssueCategory.Printer, DifficultyTier.Medium, false,
                Fault(ModuleType.Printer, "defaultPrinter", "OfficeInkjet"),
                new[] { Sym("We print a receipt and nothing happens up front. Someone said pages turned up in the back office?", "Printer.defaultPrinter changed — jobs route to the office inkjet.") },
                new[] { Clue(DesktopActionType.OpenDeviceManager, "Windows default printer is \"OfficeInkjet\". The receipt printer is healthy — it just isn't being sent anything.", false) },
                Resolution(true, ReceiptType.TestPage, Check(ModuleType.Printer, "defaultPrinter", ComparisonOp.Equals, "ReceiptPrinter")),
                null);

            var p21 = Issue("P21", IssueCategory.POS, DifficultyTier.Hard, false,
                Fault(ModuleType.POSSoftware, "printerVisible", "false"),
                new[] { Sym("Windows prints a test page fine, but the till says there's no printer.", "POSSoftware.printerVisible = false — no printer registered to the station.") },
                new[]
                {
                    Clue(DesktopActionType.CheckPosReceiptConfig, "POS reports no printer registered to this station. Windows disagrees — it has a healthy one.", false),
                    Clue(DesktopActionType.PrintTestPage, "Test page prints perfectly, straight from Windows, bypassing the POS entirely.", false),
                },
                Resolution(false, ReceiptType.TestPage, Check(ModuleType.POSSoftware, "printerVisible", ComparisonOp.Equals, "true")),
                null);

            var p22 = Issue("P22", IssueCategory.Printer, DifficultyTier.Medium, false,
                Fault(ModuleType.Printer, "connection", "Offline"),
                new[] { Sym("It says the printer's offline, but it's right here and it's plugged in.", "Printer.connection = Offline — Windows 'Use Printer Offline' is ticked.") },
                new[] { Clue(DesktopActionType.OpenDeviceManager, "Windows has 'Use Printer Offline' ticked. Driver is healthy and the device is present — this is a checkbox, not a fault.", false) },
                Resolution(true, ReceiptType.TestPage, Check(ModuleType.Printer, "connection", ComparisonOp.Equals, "Connected")),
                null);

            var p23 = Issue("P23", IssueCategory.Printer, DifficultyTier.Hard, false,
                Fault(ModuleType.Printer, "paperWidth", "58mm"),
                new[] { Sym("The receipts come out half the width of the paper and the right side's chopped off.", "Printer.paperWidth mismatched against the loaded roll.") },
                new[]
                {
                    Clue(DesktopActionType.CheckPrinterHardware, "Printer is configured for a 58mm roll; an 80mm roll is loaded. Output is being laid out for the narrower size.", false),
                    Clue(DesktopActionType.CheckPosReceiptConfig, "Receipt template is intact — every field is present in the mapping.", false),
                },
                Resolution(true, ReceiptType.TestPage, Check(ModuleType.Printer, "paperWidth", ComparisonOp.Equals, "80mm")),
                null);

            // --- Cash drawer ---------------------------------------------------------------------
            var p24 = Issue("P24", IssueCategory.CashDrawer, DifficultyTier.Basic, false,
                Fault(ModuleType.CashDrawer, "lockState", "Locked"),
                new[] { Sym("The drawer won't open. You can hear it click but it stays shut.", "CashDrawer.lockState = Locked — the physical key lock is engaged.") },
                new[] { Clue(DesktopActionType.CheckDrawerHardware, "The release IS firing — the solenoid clicks on every print. Something mechanical is holding the drawer closed.", false) },
                Resolution(false, ReceiptType.TestPage, Check(ModuleType.CashDrawer, "lockState", ComparisonOp.Equals, "Unlocked")),
                null);

            var p25 = Issue("P25", IssueCategory.CashDrawer, DifficultyTier.Medium, false,
                Fault(ModuleType.CashDrawer, "triggerMode", "Manual"),
                new[] { Sym("We have to push the little button under the counter every time now. It used to just pop.", "CashDrawer.triggerMode = Manual — no longer released by printing.") },
                new[] { Clue(DesktopActionType.CheckDrawerHardware, "Drawer trigger is set to Manual. Ports are clean and there's no conflict with the printer.", false) },
                Resolution(false, ReceiptType.TestPage, Check(ModuleType.CashDrawer, "triggerMode", ComparisonOp.Equals, "OnPrint")),
                null);

            // --- Terminal ---------------------------------------------------------------------------
            var p26 = Issue("P26", IssueCategory.Terminal, DifficultyTier.Medium, false,
                Fault(ModuleType.Terminal, "pairingState", "Unpaired"),
                new[] { Sym("The card machine's lost the till. It was fine yesterday.", "Terminal.pairingState = Unpaired — the pairing token is gone.") },
                new[] { Clue(DesktopActionType.CheckTerminalPairing, "Terminal reports no pairing token. Wi-Fi is correct and POS has the right IP on file — the network layer is clean, the trust between them isn't.", false) },
                Resolution(false, ReceiptType.TestPage, Check(ModuleType.Terminal, "pairingState", ComparisonOp.Equals, "Paired")),
                null);

            var p27 = Issue("P27", IssueCategory.Terminal, DifficultyTier.Medium, false,
                Fault(ModuleType.Terminal, "firmwareVersion", "3.1"),
                new[] { Sym("The card reader keeps saying it can't talk to the till. We did an update on the till last week.", "Terminal.firmwareVersion below POSSoftware.minTerminalFirmware.") },
                new[] { Clue(DesktopActionType.CheckTerminalPairing, "Terminal is on firmware 3.1; this POS build requires 4.0 or newer. The POS was updated, the terminal wasn't.", false) },
                Resolution(false, ReceiptType.TestPage, Check(ModuleType.Terminal, "firmwareVersion", ComparisonOp.Equals, "4.2")),
                null);

            var p28 = Issue("P28", IssueCategory.Terminal, DifficultyTier.Hard, false,
                Fault(ModuleType.Terminal, "emvConfig", "Corrupt"),
                new[] { Sym("Chip cards get refused but if we swipe the same card it goes through.", "Terminal.emvConfig = Corrupt — chip path only.") },
                new[] { Clue(DesktopActionType.CheckTerminalPairing, "Chip reader configuration is corrupt. Swipe uses a different path, which is why one works and the other doesn't — the terminal is only half broken.", false) },
                Resolution(false, ReceiptType.TestPage, Check(ModuleType.Terminal, "emvConfig", ComparisonOp.Equals, "OK")),
                null);

            var p29 = Issue("P29", IssueCategory.Terminal, DifficultyTier.Hard, false,
                Fault(ModuleType.Terminal, "mode", "Training"),
                new[] { Sym("Everything's been approving fine all night, but the bank says no money's come in.", "Terminal.mode = Training — approvals are simulated.") },
                new[]
                {
                    Clue(DesktopActionType.CheckTerminalPairing, "Terminal is in TRAINING mode. Every approval tonight was simulated — nothing was ever sent to the processor.", false),
                    Clue(DesktopActionType.CheckPrintQueue, "Printer is fine and has been printing all evening.", true),
                },
                Resolution(false, ReceiptType.TestPage, Check(ModuleType.Terminal, "mode", ComparisonOp.Equals, "Live")),
                null);

            // --- POS ----------------------------------------------------------------------------------
            var p30 = Issue("P30", IssueCategory.POS, DifficultyTier.Medium, false,
                Fault(ModuleType.POSSoftware, "licenseState", "Expired"),
                new[] { Sym("The till won't even open. It flashes something up and closes again.", "POSSoftware.licenseState = Expired.") },
                new[] { Clue(DesktopActionType.CheckPosLicensing, "POS licence expired. The application refuses to start — nothing downstream of it can be assessed until this is cleared.", false) },
                Resolution(false, ReceiptType.TestPage, Check(ModuleType.POSSoftware, "licenseState", ComparisonOp.Equals, "Valid")),
                null);

            var p31 = Issue("P31", IssueCategory.POS, DifficultyTier.Hard, false,
                Fault(ModuleType.POSSoftware, "offlineMode", "true"),
                new[] { Sym("Sales are going through okay but nothing's showing up in our reports.", "POSSoftware.offlineMode = true — sales queue locally, never settle.") },
                new[]
                {
                    Clue(DesktopActionType.CheckPosLicensing, "POS is running in offline mode: sales are being stored locally and nothing is being sent up.", false),
                    Clue(DesktopActionType.CheckNetworkStatus, "Network is up and healthy — the POS chose offline mode and stayed there.", false),
                },
                Resolution(false, ReceiptType.TestPage, Check(ModuleType.POSSoftware, "offlineMode", ComparisonOp.Equals, "false")),
                null);

            var p32 = Issue("P32", IssueCategory.POS, DifficultyTier.Hard, false,
                Fault(ModuleType.POSSoftware, "batchState", "SettleFailed"),
                new[] { Sym("Last night's takings never landed in the account. Everything looked normal when we closed.", "POSSoftware.batchState = SettleFailed.") },
                new[] { Clue(DesktopActionType.CheckPosLicensing, "Last batch is marked settle-failed. The transactions exist and were authorized; the settlement run that moves the money never completed.", false) },
                Resolution(false, ReceiptType.TestPage, Check(ModuleType.POSSoftware, "batchState", ComparisonOp.Equals, "Open")),
                null);

            var p33 = Issue("P33", IssueCategory.POS, DifficultyTier.Medium, false,
                Fault(ModuleType.POSSoftware, "taxRate", "0"),
                new[] { Sym("Customers are saying the totals look wrong. Cheaper than they should be.", "POSSoftware.taxRate misconfigured — totals computed without tax.") },
                new[]
                {
                    Clue(DesktopActionType.CheckPosLicensing, "Sales tax is configured as 0%. Every receipt tonight has undercharged.", false),
                    Clue(DesktopActionType.CheckPosReceiptConfig, "Receipt template is intact — the total field is present and printing. The number in it is simply wrong.", false),
                },
                Resolution(false, ReceiptType.TestPage, Check(ModuleType.POSSoftware, "taxRate", ComparisonOp.Equals, "8.25")),
                null);

            var p34 = Issue("P34", IssueCategory.POS, DifficultyTier.Medium, false,
                Fault(ModuleType.POSSoftware, "priceSync", "Stale"),
                new[] { Sym("The register's ringing up last month's prices. Head office changed them ages ago.", "POSSoftware.priceSync = Stale.") },
                new[] { Clue(DesktopActionType.CheckPosLicensing, "Price list hasn't synced since last month. The connection is fine — the scheduled sync simply hasn't run.", false) },
                Resolution(false, ReceiptType.TestPage, Check(ModuleType.POSSoftware, "priceSync", ComparisonOp.Equals, "Current")),
                null);

            // --- Network: up, but degraded in three different ways -----------------------------------
            var p35 = Issue("P35", IssueCategory.Network, DifficultyTier.Medium, false,
                Fault(ModuleType.Network, "signalStrength", "Weak"),
                new[] { Sym("It works, then it doesn't, then it works again. Mostly when we're busy.", "Network.signalStrength = Weak — link drops under load.") },
                new[] { Clue(DesktopActionType.CheckNetworkStatus, "Adapter is connected but the signal is weak and packets are dropping under load. This is intermittent, not down.", false) },
                Resolution(false, ReceiptType.TestPage, Check(ModuleType.Network, "signalStrength", ComparisonOp.Equals, "Good")),
                null);

            var p36 = Issue("P36", IssueCategory.Network, DifficultyTier.Hard, false,
                Fault(ModuleType.Network, "dnsServer", "8.8.8.8"),
                new[] { Sym("We can get on the internet fine but the till can't find our own records.", "Network.dnsServer points at a public resolver — internal names don't resolve.") },
                new[]
                {
                    Clue(DesktopActionType.CheckNetworkStatus, "DNS is set to a public resolver. Public sites resolve; the shop's own internal names do not.", false),
                    Clue(DesktopActionType.CheckPosConnections, "Database host name is spelled correctly — it simply cannot be resolved from here.", false),
                },
                Resolution(false, ReceiptType.TestPage, Check(ModuleType.Network, "dnsServer", ComparisonOp.Equals, "192.168.1.1")),
                null);

            var p37 = Issue("P37", IssueCategory.Network, DifficultyTier.Hard, false,
                Fault(ModuleType.Network, "firewallBlocking", "true"),
                new[] { Sym("Cards won't go through. The internet's fine, I'm looking at the news right now.", "Network.firewallBlocking = true — processor's outbound port blocked.") },
                new[]
                {
                    Clue(DesktopActionType.CheckNetworkStatus, "General traffic passes; the payment processor's outbound port is being refused. Selective, not total.", false),
                    Clue(DesktopActionType.CheckSystemHealth, "System clock is correct and in sync.", false),
                },
                Resolution(false, ReceiptType.TestPage, Check(ModuleType.Network, "firewallBlocking", ComparisonOp.Equals, "false")),
                null);

            // --- OS: three more service-level faults, each surfacing somewhere else -------------------
            var p38 = Issue("P38", IssueCategory.OS, DifficultyTier.Hard, false,
                Fault(ModuleType.OS, "antivirusQuarantine", "true"),
                new[] { Sym("The till closes itself the second we open it. Started after some security thing popped up.", "OS.antivirusQuarantine = true — a POS file was quarantined.") },
                new[] { Clue(DesktopActionType.CheckServices, "Antivirus quarantined a file the POS needs at startup. The POS isn't damaged — a piece of it has been taken away from it.", false) },
                Resolution(false, ReceiptType.TestPage, Check(ModuleType.OS, "antivirusQuarantine", ComparisonOp.Equals, "false")),
                null);

            var p39 = Issue("P39", IssueCategory.OS, DifficultyTier.Medium, false,
                Fault(ModuleType.OS, "userAccount", "Standard"),
                new[] { Sym("Someone logged the computer in under a different account and now the till won't run.", "OS.userAccount = Standard — Windows-level rights, not a POS role.") },
                new[]
                {
                    Clue(DesktopActionType.CheckSystemHealth, "Windows is signed in as a Standard user. The POS needs administrator rights to start at all.", false),
                    Clue(DesktopActionType.CheckStaffAccount, "The POS staff account itself is fine — role assigned, terminal assigned, synced.", false),
                },
                Resolution(false, ReceiptType.TestPage, Check(ModuleType.OS, "userAccount", ComparisonOp.Equals, "Admin")),
                null);

            var p40 = Issue("P40", IssueCategory.OS, DifficultyTier.Medium, false,
                Fault(ModuleType.OS, "powerPlan", "Sleep"),
                new[] { Sym("If nobody touches it for a bit, the card machine drops off and we have to poke the computer.", "OS.powerPlan = Sleep — attached devices drop with the host.") },
                new[] { Clue(DesktopActionType.CheckSystemHealth, "Power plan allows the machine to sleep. Anything attached to it drops when it does, and comes back when it wakes.", false) },
                Resolution(false, ReceiptType.TestPage, Check(ModuleType.OS, "powerPlan", ComparisonOp.Equals, "AlwaysOn")),
                null);

            var all = new[]
            {
                p1, p2, p3, p4, p5, p6, p7, p8, p9, p10, p11, p12, p13, p14, p15, p16,
                p17, p18, p19, p20, p21, p22, p23, p24, p25, p26, p27, p28, p29, p30,
                p31, p32, p33, p34, p35, p36, p37, p38, p39, p40,
            };
            WireBlockers(all);

            return new[]
            {
                Save(p1, "Issue_P1_OutOfPaper"), Save(p2, "Issue_P2_DriverCorrupted"),
                Save(p3, "Issue_P3_CashDrawerPortConflict"), Save(p4, "Issue_P4_NetworkBlocker"),
                Save(p5, "Issue_P5_ReceiptTemplateBroken"), Save(p6, "Issue_P6_TerminalWrongWifi"),
                Save(p7, "Issue_P7_StaleTerminalIp"),
                Save(p8, "Issue_P8_StaffNoRole"), Save(p9, "Issue_P9_StaffNoTerminal"),
                Save(p10, "Issue_P10_StaffWrongTerminal"), Save(p11, "Issue_P11_AssignmentNotSynced"),
                Save(p12, "Issue_P12_DbHostTypo"),
                Save(p13, "Issue_P13_SpoolerStopped"), Save(p14, "Issue_P14_DiskFull"),
                Save(p15, "Issue_P15_PendingReboot"), Save(p16, "Issue_P16_ClockSkew"),

                Save(p17, "Issue_P17_PaperJam"), Save(p18, "Issue_P18_CableUnplugged"),
                Save(p19, "Issue_P19_QueuePaused"), Save(p20, "Issue_P20_WrongDefaultPrinter"),
                Save(p21, "Issue_P21_PosCannotSeePrinter"), Save(p22, "Issue_P22_PrinterOffline"),
                Save(p23, "Issue_P23_PaperWidthMismatch"),
                Save(p24, "Issue_P24_DrawerKeyLocked"), Save(p25, "Issue_P25_DrawerManualTrigger"),
                Save(p26, "Issue_P26_TerminalUnpaired"), Save(p27, "Issue_P27_FirmwareTooOld"),
                Save(p28, "Issue_P28_EmvConfigCorrupt"), Save(p29, "Issue_P29_TerminalTrainingMode"),
                Save(p30, "Issue_P30_LicenseExpired"), Save(p31, "Issue_P31_PosOfflineMode"),
                Save(p32, "Issue_P32_BatchSettleFailed"), Save(p33, "Issue_P33_WrongTaxRate"),
                Save(p34, "Issue_P34_StalePriceList"),
                Save(p35, "Issue_P35_WeakSignal"), Save(p36, "Issue_P36_WrongDns"),
                Save(p37, "Issue_P37_FirewallBlocking"),
                Save(p38, "Issue_P38_AntivirusQuarantine"), Save(p39, "Issue_P39_StandardUserAccount"),
                Save(p40, "Issue_P40_MachineSleeps"),
            };
        }

        /// <summary>
        /// Fills in blockedByIssueIds by RULE rather than per-issue, so a new issue can't silently forget it.
        /// Two blocker tiers: an OS-wide fault takes the machine down (so it hides even the network fault),
        /// and the network fault hides everything below it. Without this the Latent → Active reveal in
        /// DesktopManager.OnFixApplied never fires on a chained ticket (GDD §7).
        /// </summary>
        private static void WireBlockers(IssueSO[] all)
        {
            var osBlockers = new List<string>();
            var otherBlockers = new List<string>();
            foreach (var i in all)
            {
                if (!i.isBlocker) continue;
                if (i.category == IssueCategory.OS) osBlockers.Add(i.issueId);
                else otherBlockers.Add(i.issueId);
            }

            foreach (var i in all)
            {
                if (i.category == IssueCategory.OS && i.isBlocker) { i.blockedByIssueIds = new string[0]; continue; }
                var blockedBy = new List<string>(osBlockers);
                if (!i.isBlocker) blockedBy.AddRange(otherBlockers);
                i.blockedByIssueIds = blockedBy.ToArray();
            }
        }

        // --- Builders ----------------------------------------------------------------------------
        private static IssueSO Issue(string id, IssueCategory cat, DifficultyTier tier, bool blocker,
            FaultInjection fault, Symptom[] symptoms, DiagnosticClue[] clues, ResolutionCondition res, FaultInjection[] worsening)
        {
            var i = ScriptableObject.CreateInstance<IssueSO>();
            i.issueId = id; i.category = cat; i.tier = tier; i.isBlocker = blocker;
            i.faults = new[] { fault };
            i.symptoms = symptoms; i.clues = clues; i.resolution = res;
            i.blockedByIssueIds = new string[0];
            i.worseningFaults = worsening ?? new FaultInjection[0];
            return i;
        }

        private static ResolutionCondition Resolution(bool requiresTest, ReceiptType testType, params StateCheck[] rootCause)
        {
            return new ResolutionCondition
            {
                symptomCleared = rootCause,
                rootCauseFixed = rootCause,
                requiresTestPass = requiresTest,
                testReceiptType = testType,
            };
        }

        private static DesktopActionSO Diagnostic(string id, DesktopActionType type, ModuleType mod, string app, string tab, string result) =>
            Save(MakeAction(id, ActionKind.Diagnostic, type, mod, app, tab, false, false, ReceiptType.TestPage, result), "Action_" + id);

        private static DesktopActionSO Test(string id, DesktopActionType type, ModuleType mod, string app, string tab, ReceiptType receipt, string result) =>
            Save(MakeAction(id, ActionKind.Diagnostic, type, mod, app, tab, false, true, receipt, result), "Action_" + id);

        private static DesktopActionSO Fix(string id, ModuleType mod, string app, string tab, StateCheck pre, FaultInjection change, string result)
        {
            var a = MakeAction(id, ActionKind.Fix, DesktopActionType.None, mod, app, tab, false, false, ReceiptType.TestPage, result);
            a.preconditions = new[] { pre };
            a.stateChanges = new[] { change };
            return Save(a, "Action_" + id);
        }

        private static DesktopActionSO RiskyFix(string id, ModuleType mod, string app, string tab, FaultInjection change, string result)
        {
            var a = MakeAction(id, ActionKind.Fix, DesktopActionType.None, mod, app, tab, true, false, ReceiptType.TestPage, result);
            a.stateChanges = new[] { change };
            return Save(a, "Action_" + id);
        }

        private static DesktopActionSO MakeAction(string id, ActionKind kind, DesktopActionType type, ModuleType mod,
            string app, string tab, bool risky, bool isTest, ReceiptType receipt, string result)
        {
            var a = ScriptableObject.CreateInstance<DesktopActionSO>();
            a.actionId = id; a.actionType = type; a.kind = kind; a.targetModule = mod;
            a.appKey = app; a.appTab = tab; a.resultText = result;
            a.isRisky = risky; a.isTest = isTest; a.testReceiptType = receipt;
            a.preconditions = new StateCheck[0]; a.stateChanges = new FaultInjection[0];
            return a;
        }

        private static FaultInjection Fault(ModuleType m, string field, string val) =>
            new() { module = m, stateField = field, faultValue = val };

        private static FaultInjection Change(ModuleType m, string field, string val) =>
            new() { module = m, stateField = field, faultValue = val };

        private static StateCheck Check(ModuleType m, string field, ComparisonOp op, string val) =>
            new() { module = m, field = field, op = op, expectedValue = val };

        private static StateCheck Pre(ModuleType m, string field, ComparisonOp op, string val) =>
            new() { module = m, field = field, op = op, expectedValue = val };

        private static Symptom Sym(string layman, string technical) => new() { layman = layman, technical = technical };

        private static DiagnosticClue Clue(DesktopActionType by, string text, bool herring) =>
            new() { revealedBy = by, clueText = text, isRedHerring = herring };

        private static MisnameEntry Misname(string correct, string customer) =>
            new() { correctTerm = correct, customerTerm = customer };

        // --- Asset IO ----------------------------------------------------------------------------
        private static void EnsureDir()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Content")) AssetDatabase.CreateFolder("Assets", "Content");
            if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/Content", "Generated");
        }

        /// <summary>
        /// Write an asset, REUSING the file already at that path if there is one. `CreateAsset` on an
        /// existing path deletes and recreates it, which mints a new GUID and silently breaks every scene
        /// reference to it — regenerating content would leave GameManager holding a missing
        /// ContentDatabase. Copying into the existing object keeps the GUID, so "regeneration is
        /// idempotent" is true of the project and not only of the asset contents.
        /// </summary>
        private static T Save<T>(T asset, string name) where T : Object
        {
            string path = $"{Dir}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(asset, path);
                return asset;
            }

            EditorUtility.CopySerialized(asset, existing);
            EditorUtility.SetDirty(existing);
            // The freshly built instance is deliberately NOT destroyed: destroying it would null out any
            // reference a caller still holds to it, which is a silent bug. Callers must use the RETURN
            // value so that every cross-reference points at the persisted asset.
            return existing;
        }
    }
}
