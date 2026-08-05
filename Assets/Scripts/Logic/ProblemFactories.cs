using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using POSTechSupport.Core;
using POSTechSupport.Data;
using POSTechSupport.Simulation;

namespace POSTechSupport.Logic
{
    // ============================================================================
    // Factory Method for ProblemInstance (Docs/manager.md ProblemGenerator).
    // The prototype's makeTicket(day, forcedIssueIds?) folded two ticket SOURCES into one
    // optional parameter. Split here: each source is its own IProblemFactory, while the shared
    // assembly step (desktop + persona + ticket seed) lives in ProblemAssembler so a new source
    // can be added without touching the existing ones — and each part is testable alone.
    // ============================================================================

    /// <summary>One contract, many sources of the issue combo. Every ticket is born here.</summary>
    public interface IProblemFactory
    {
        ProblemInstance Create(int day);
    }

    /// <summary>
    /// The one lookup ProblemAssembler needs from the knowledge base for onboarding guidance.
    /// KnowledgeBaseManager implements it; the assembler stays free of the Managers layer, and the
    /// KB itself stays ignorant of "day"/"tier" (Docs/manager.md KnowledgeBaseManager).
    /// </summary>
    public interface IGuidanceSource
    {
        KnowledgeArticleSO FindGuidanceArticle(IssueSO issue);
    }

    /// <summary>One rollable combo of issue ids (a wrapper because Unity can't serialize string[][]).</summary>
    [Serializable]
    public class IssueCombo
    {
        public string[] issueIds;
        public IssueCombo() { issueIds = Array.Empty<string>(); }
        public IssueCombo(params string[] ids) { issueIds = ids; }
    }

    /// <summary>Everything rollable up to and including <see cref="maxDay"/> (prototype poolForDay tier).</summary>
    [Serializable]
    public class IssuePool
    {
        public int maxDay;
        public IssueCombo[] combos;

        /// <summary>The validated prototype table (GDD §14). Tier boundaries double as difficulty gates.</summary>
        /// <summary>
        /// The 40-issue ladder. Each tier introduces issues that can be CONFUSED with ones the player has
        /// already met — that is the ordering rule, not raw difficulty. P1 alone; then P2 so "out of
        /// paper" has a rival; then P3/P19/P22 which all look like P2; and so on.
        /// </summary>
        public static IssuePool[] DefaultTable() => new[]
        {
            // Onboarding: one issue, one article, no ambiguity. Also the guidance auto-attach boundary.
            Tier(5,  new IssueCombo("P1"), new IssueCombo("P17"), new IssueCombo("P18")),

            // First discriminations, all inside the printer: jam vs empty vs driver vs cable.
            Tier(15, new IssueCombo("P1"), new IssueCombo("P2"), new IssueCombo("P17"),
                     new IssueCombo("P18"), new IssueCombo("P19"), new IssueCombo("P22"),
                     new IssueCombo("P24")),

            // Root cause starts moving away from the symptom: drawer, terminal identity, OS services,
            // the first permission ticket.
            Tier(30, new IssueCombo("P1"), new IssueCombo("P2"), new IssueCombo("P3"),
                     new IssueCombo("P6"), new IssueCombo("P7"), new IssueCombo("P8"),
                     new IssueCombo("P13"), new IssueCombo("P17"), new IssueCombo("P19"),
                     new IssueCombo("P20"), new IssueCombo("P22"), new IssueCombo("P24"),
                     new IssueCombo("P25"), new IssueCombo("P26"), new IssueCombo("P27"),
                     new IssueCombo("P30"), new IssueCombo("P33"), new IssueCombo("P34"),
                     new IssueCombo("P35"), new IssueCombo("P39"), new IssueCombo("P40")),

            // The money-shaped faults and the cross-layer ones, plus the first blocker chains.
            Tier(45, new IssueCombo("P2"), new IssueCombo("P3"), new IssueCombo("P5"),
                     new IssueCombo("P6"), new IssueCombo("P7"),
                     new IssueCombo("P8"), new IssueCombo("P9"), new IssueCombo("P10"),
                     new IssueCombo("P11"), new IssueCombo("P12"),
                     new IssueCombo("P13"), new IssueCombo("P15"), new IssueCombo("P16"),
                     new IssueCombo("P20"), new IssueCombo("P21"), new IssueCombo("P23"),
                     new IssueCombo("P25"), new IssueCombo("P26"), new IssueCombo("P27"),
                     new IssueCombo("P28"), new IssueCombo("P29"), new IssueCombo("P31"),
                     new IssueCombo("P32"), new IssueCombo("P34"), new IssueCombo("P36"),
                     new IssueCombo("P37"), new IssueCombo("P38"), new IssueCombo("P39"),
                     new IssueCombo("P4", "P1"), new IssueCombo("P4", "P2")),

            // Everything, including blocker-over-fault chains: clear the blocker before the real fault
            // is even readable, then notice there was a second one underneath.
            Tier(int.MaxValue,
                     new IssueCombo("P2"), new IssueCombo("P3"), new IssueCombo("P5"),
                     new IssueCombo("P6"), new IssueCombo("P7"), new IssueCombo("P8"),
                     new IssueCombo("P9"), new IssueCombo("P10"), new IssueCombo("P11"),
                     new IssueCombo("P12"), new IssueCombo("P13"), new IssueCombo("P14"),
                     new IssueCombo("P15"), new IssueCombo("P16"), new IssueCombo("P20"),
                     new IssueCombo("P21"), new IssueCombo("P23"), new IssueCombo("P26"),
                     new IssueCombo("P28"), new IssueCombo("P29"), new IssueCombo("P31"),
                     new IssueCombo("P32"), new IssueCombo("P36"), new IssueCombo("P37"),
                     new IssueCombo("P38"),
                     new IssueCombo("P4", "P3"), new IssueCombo("P4", "P5"), new IssueCombo("P4", "P6"),
                     new IssueCombo("P4", "P8"), new IssueCombo("P4", "P12"), new IssueCombo("P4", "P21"),
                     new IssueCombo("P14", "P2"), new IssueCombo("P14", "P28"),
                     new IssueCombo("P15", "P6"), new IssueCombo("P15", "P32")),
        };

        private static IssuePool Tier(int maxDay, params IssueCombo[] combos) =>
            new() { maxDay = maxDay, combos = combos };

        /// <summary>
        /// Last day of the easiest tier. This doubles as the onboarding-guidance boundary on purpose —
        /// "easiest issues" and "training wheels" must fade on the same beat, so there is deliberately
        /// no separate guidanceDays config (Docs/manager.md KnowledgeBaseManager).
        /// </summary>
        public static int OnboardingMaxDay(IssuePool[] table) =>
            table != null && table.Length > 0 ? table[0].maxDay : 0;
    }

    /// <summary>
    /// Rolls who is on the phone for this ticket: which role, which name, and which identity facts
    /// they misremember (per PersonaProfileSO.memoryAccuracy). See Docs/app.md Caller Authorization.
    /// </summary>
    public class PersonaFactory
    {
        private readonly PersonaProfileSO[] pool;
        private readonly string[] staffCallerNames;

        public PersonaFactory(PersonaProfileSO[] pool, string[] staffCallerNames)
        {
            this.pool = pool;
            this.staffCallerNames = staffCallerNames;
        }

        /// <summary>
        /// Refund/void cases put a staff member on the line; everything else is the owner.
        /// </summary>
        /// <param name="store">The account calling — its facts are what a perfect memory would state.</param>
        /// <param name="confusable">
        /// A near-miss account from the same CRM directory. What a caller states WRONGLY comes from here,
        /// so a misremembered name is a shop that really exists two rows away, not a random string.
        /// </param>
        public PersonaInstance Create(bool isRefundVoidCase, StoreRecord store,
                                      MachineConfig machine, StoreRecord confusable)
        {
            var profile = pool != null && pool.Length > 0 ? pool[UnityEngine.Random.Range(0, pool.Length)] : null;
            float memAcc = profile != null ? profile.memoryAccuracy : 0.5f;

            var role = isRefundVoidCase ? CallerRole.Staff : CallerRole.Owner;
            string ownerName = store != null ? store.ownerName : "Owner";
            string machineId = machine != null ? machine.machineId : "REG-1";
            string storeName = store != null ? store.storeName : "Store";
            string callerName = role == CallerRole.Owner ? ownerName : PickStaffName();

            string wrongStore = confusable != null && confusable != store ? confusable.storeName : storeName;
            string wrongOwner = confusable != null && confusable != store ? confusable.ownerName : ownerName;

            return new PersonaInstance
            {
                profile = profile,
                role = role,
                name = callerName,
                statedStoreName = RandomPick(storeName, wrongStore, memAcc),
                statedOwnerName = role == CallerRole.Owner ? RandomPick(ownerName, wrongOwner, memAcc) : callerName,
                statedMachineId = RandomPick(machineId, WrongRegister(machineId), memAcc),
            };
        }

        /// <summary>
        /// A register the caller might name instead of their own. Not read off another CRM record on
        /// purpose: every account files the same register id as the simulated desktop reports, so the
        /// only honest source of a wrong register is the caller's own memory.
        /// </summary>
        private static string WrongRegister(string correct)
        {
            for (int i = 0; i < 8; i++)
            {
                string candidate = "REG-" + UnityEngine.Random.Range(2, 10);
                if (candidate != correct) return candidate;
            }
            return "REG-9";
        }

        private string PickStaffName() =>
            staffCallerNames != null && staffCallerNames.Length > 0
                ? staffCallerNames[UnityEngine.Random.Range(0, staffCallerNames.Length)]
                : "Staff";

        private static string RandomPick(string correct, string wrong, float accuracy) =>
            UnityEngine.Random.value < accuracy ? correct : wrong;
    }

    /// <summary>Clones the machine's healthy baseline, then injects each issue's faults on top.</summary>
    public class DesktopFactory
    {
        private readonly ModuleBaseline baseline;

        public DesktopFactory(ModuleBaseline baseline) { this.baseline = baseline; }

        public VirtualDesktopInstance Create(IssueSO[] issues)
        {
            var d = VirtualDesktopInstance.BuildFresh();
            if (baseline != null) SeedBaseline(d, baseline);
            foreach (var issue in issues)
                if (issue != null && issue.faults != null)
                    foreach (var f in issue.faults) d.Apply(f);
            return d;
        }

        private static void SeedBaseline(VirtualDesktopInstance d, ModuleBaseline b)
        {
            var os = d.GetModule(ModuleType.OS);
            os.Set("diskSpace", b.osDiskSpace);
            os.Set("pendingReboot", b.osPendingReboot);
            os.Set("spoolerService", b.osSpoolerService);
            os.Set("systemTime", b.osSystemTime);
            os.Set("antivirusQuarantine", b.osAntivirusQuarantine);
            os.Set("userAccount", b.osUserAccount);
            os.Set("powerPlan", b.osPowerPlan);

            var net = d.GetModule(ModuleType.Network);
            net.Set("isOnline", b.networkIsOnline);
            net.Set("ssid", b.networkSsid);
            net.Set("signalStrength", b.networkSignalStrength);
            net.Set("dnsServer", b.networkDnsServer);
            net.Set("firewallBlocking", b.networkFirewallBlocking);

            var pos = d.GetModule(ModuleType.POSSoftware);
            pos.Set("receiptTemplate", b.posReceiptTemplate);
            pos.Set("staffRole", b.posStaffRole);
            pos.Set("staffTerminal", b.posStaffTerminal);
            pos.Set("terminalSynced", b.posTerminalSynced);
            pos.Set("dbHost", b.posDbHost);
            pos.Set("registeredTerminalIp", b.posRegisteredTerminalIp);
            pos.Set("licenseState", b.posLicenseState);
            pos.Set("offlineMode", b.posOfflineMode);
            pos.Set("batchState", b.posBatchState);
            pos.Set("taxRate", b.posTaxRate);
            pos.Set("priceSync", b.posPriceSync);
            pos.Set("printerVisible", b.posPrinterVisible);
            pos.Set("minTerminalFirmware", b.posMinTerminalFirmware);

            var term = d.GetModule(ModuleType.Terminal);
            term.Set("wifiNetwork", b.terminalWifiNetwork);
            term.Set("machineId", b.terminalMachineId);
            term.Set("pairingState", b.terminalPairingState);
            term.Set("firmwareVersion", b.terminalFirmwareVersion);
            term.Set("emvConfig", b.terminalEmvConfig);
            term.Set("mode", b.terminalMode);

            var pr = d.GetModule(ModuleType.Printer);
            pr.Set("paperLevel", b.printerPaperLevel);
            pr.Set("driverState", b.printerDriverState);
            pr.Set("connection", b.printerConnection);
            pr.Set("port", b.printerPort);
            pr.Set("cableConnected", b.printerCableConnected);
            pr.Set("paperJam", b.printerPaperJam);
            pr.Set("queuePaused", b.printerQueuePaused);
            pr.Set("defaultPrinter", b.printerDefaultPrinter);
            pr.Set("paperWidth", b.printerPaperWidth);

            var cd = d.GetModule(ModuleType.CashDrawer);
            cd.Set("port", b.cashDrawerPort);
            cd.Set("lockState", b.cashDrawerLockState);
            cd.Set("triggerMode", b.cashDrawerTriggerMode);
        }
    }

    /// <summary>
    /// The assembly step every IProblemFactory shares: given an issue combo, build the desktop, roll
    /// the persona, seed ticket + transaction state, and (during the onboarding tier) attach a
    /// guidance article. Factories differ only in WHERE the combo came from.
    /// </summary>
    public class ProblemAssembler
    {
        private readonly ContentDatabaseSO content;
        private readonly StoreDirectory directory;
        private readonly PersonaFactory personaFactory;
        private readonly DesktopFactory desktopFactory;
        private readonly IGuidanceSource guidance;
        private readonly int guidanceMaxDay;
        private int ticketSeq = 1;

        /// <param name="guidanceMaxDay">
        /// Last day a guidance article auto-attaches. NOT a config of its own — ProblemGenerator passes
        /// the first pool tier's maxDay so "easiest issues" and "training wheels" fade on the same beat
        /// (Docs/manager.md KnowledgeBaseManager).
        /// </param>
        public ProblemAssembler(ContentDatabaseSO content, StoreDirectory directory,
                                PersonaFactory personaFactory, DesktopFactory desktopFactory,
                                IGuidanceSource guidance, int guidanceMaxDay)
        {
            this.content = content;
            this.directory = directory;
            this.personaFactory = personaFactory;
            this.desktopFactory = desktopFactory;
            this.guidance = guidance;
            this.guidanceMaxDay = guidanceMaxDay;
        }

        public ProblemInstance Assemble(int day, string[] issueIds)
        {
            issueIds ??= Array.Empty<string>();
            var issues = issueIds.Select(content.FindIssue).Where(i => i != null).ToArray();

            bool isRefundVoid = UnityEngine.Random.value < 0.4f;

            // A different shop calls each time, drawn from the shared directory. Its confusable
            // neighbour is what a caller with a poor memory will name instead of their own shop.
            var callerStore = directory.PickCaller();
            var confusable = directory.PickConfusable(callerStore);
            var machine = callerStore?.machines != null && callerStore.machines.Length > 0
                ? callerStore.machines[0] : null;

            var p = new ProblemInstance
            {
                issues = issues,
                store = callerStore,
                persona = personaFactory.Create(isRefundVoid, callerStore, machine, confusable),
                desktop = desktopFactory.Create(issues),
                crmDirectory = directory.records,
            };

            foreach (var issue in issues)
            {
                var blockedBy = (issue.blockedByIssueIds ?? Array.Empty<string>())
                    .Where(id => issueIds.Contains(id)).ToList();
                p.faults.Add(new ActiveFault
                {
                    issue = issue,
                    status = blockedBy.Count > 0 ? FaultStatus.Latent : FaultStatus.Active,
                    blockedBy = blockedBy,
                });
            }

            InitTicket(p, day, isRefundVoid);
            SeedTransactions(p.transactions);
            return p;
        }

        private void InitTicket(ProblemInstance p, int day, bool isRefundVoid)
        {
            var t = p.ticket;
            t.ticketId = "TCK-" + (ticketSeq++).ToString("D4");
            t.day = day;
            t.lifecycle = CallLifecycleStatus.Queued;
            t.authorization.isRefundVoidCase = isRefundVoid;
            t.authorization.callerAuthorized = !isRefundVoid || UnityEngine.Random.value < 0.5f;
            t.remoteConnect.passcode = GenPasscode();
            t.appTabs = new Dictionary<string, string>
            {
                { "possoftware", "receipt" }, { "printer", "queue" }, { "devicemanager", "printer" },
                { "cashdrawer", "port" }, { "network", "adapter" }, { "terminal", "status" },
            };

            // Onboarding tier only: hand the player the article instead of making them go find it.
            // From guidanceMaxDay+1 on, attachedArticle stays null and the KB app is the only route.
            if (guidance != null && day <= guidanceMaxDay && p.issues.Length > 0)
                t.attachedArticle = guidance.FindGuidanceArticle(p.issues[0]);
        }

        private static void SeedTransactions(TransactionState tx)
        {
            tx.batchId = 114;
            tx.live = new List<Transaction>
            {
                new() { type = TransType.Sale,   amount = 12.50, status = TransStatus.Settled },
                new() { type = TransType.Refund, amount = 4.00,  status = TransStatus.Settled },
                new() { type = TransType.Sale,   amount = 8.25,  status = TransStatus.Open },
            };
            tx.archive = new List<Transaction>
            {
                new() { day = "Yesterday",     type = TransType.Sale,   amount = 22.00, status = TransStatus.Settled },
                new() { day = "Yesterday",     type = TransType.Refund, amount = 5.00,  status = TransStatus.Settled },
                new() { day = "2 nights ago",  type = TransType.Sale,   amount = 9.75,  status = TransStatus.Settled },
            };
        }

        private static string GenPasscode()
        {
            const string chars = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
            var s = new char[5];
            for (int i = 0; i < 5; i++) s[i] = chars[UnityEngine.Random.Range(0, chars.Length)];
            return new string(s);
        }
    }

    /// <summary>Auto-spawn source: rolls a combo from the day's pool (prototype poolForDay/pickIssueCombo).</summary>
    public class RandomPoolProblemFactory : IProblemFactory
    {
        public IssuePool[] poolByDayThreshold;
        private readonly ProblemAssembler assembler;

        public RandomPoolProblemFactory(ProblemAssembler assembler, IssuePool[] poolByDayThreshold = null)
        {
            this.assembler = assembler;
            this.poolByDayThreshold = poolByDayThreshold ?? IssuePool.DefaultTable();
        }

        public ProblemInstance Create(int day) => assembler.Assemble(day, PickCombo(day));

        public IssuePool PoolForDay(int day) =>
            poolByDayThreshold.FirstOrDefault(t => day <= t.maxDay) ?? poolByDayThreshold.Last();

        public string[] PickCombo(int day)
        {
            var tier = PoolForDay(day);
            if (tier?.combos == null || tier.combos.Length == 0) return Array.Empty<string>();
            return tier.combos[UnityEngine.Random.Range(0, tier.combos.Length)].issueIds;
        }
    }

    /// <summary>Dev-picker source: skips the roll entirely and assembles the exact combo asked for.</summary>
    public class ForcedIssueProblemFactory : IProblemFactory
    {
        public string[] issueIds;
        private readonly ProblemAssembler assembler;

        public ForcedIssueProblemFactory(ProblemAssembler assembler, string[] issueIds)
        {
            this.assembler = assembler;
            this.issueIds = issueIds;
        }

        public ProblemInstance Create(int day) => assembler.Assemble(day, issueIds);
    }

    /// <summary>
    /// Cross-night source: when ConsequenceManager has a temp-fixed issue due back today, that issue
    /// wins the slot; otherwise the wrapped factory rolls normally. Added as a NEW factory precisely so
    /// RandomPoolProblemFactory never learns about recurrence (Docs/manager.md ConsequenceManager).
    /// </summary>
    public class RecurringProblemFactory : IProblemFactory
    {
        private readonly ProblemAssembler assembler;
        private readonly IProblemFactory fallback;
        private readonly Func<int, List<string>> dueToday;
        private readonly Action<string> consume;

        public RecurringProblemFactory(ProblemAssembler assembler, IProblemFactory fallback,
                                       Func<int, List<string>> dueToday, Action<string> consume)
        {
            this.assembler = assembler;
            this.fallback = fallback;
            this.dueToday = dueToday;
            this.consume = consume;
        }

        public ProblemInstance Create(int day)
        {
            var due = dueToday?.Invoke(day);
            if (due == null || due.Count == 0) return fallback.Create(day);

            string issueId = due[0];
            consume?.Invoke(issueId);              // one recurrence per pending entry, not every spawn
            return assembler.Assemble(day, new[] { issueId });
        }
    }
}
