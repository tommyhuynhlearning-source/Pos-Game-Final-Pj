using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using POSTechSupport.Data;

namespace POSTechSupport.Logic
{
    /// <summary>
    /// Composition root for ticket creation (Docs/manager.md ProblemGenerator). It no longer builds a
    /// ProblemInstance itself — it holds the shared sub-factories and points each caller at the right
    /// IProblemFactory. Adding a new ticket source means adding a factory, not editing this class.
    /// </summary>
    public class ProblemGenerator
    {
        private readonly ProblemAssembler assembler;
        private readonly RandomPoolProblemFactory randomPool;

        /// <summary>Factory used by auto-spawn. Swapped by <see cref="EnableRecurring"/>.</summary>
        public IProblemFactory autoFactory;

        /// <summary>The CRM directory every ticket this session searches against.</summary>
        public readonly StoreDirectory Directory;

        public ProblemGenerator(ContentDatabaseSO content, IGuidanceSource guidance = null)
        {
            // realStore is now a TEMPLATE, not the only customer: it supplies the healthy machine
            // baseline that every generated account shares (so the desktop and the record agree).
            var template = content.realStore;
            var machine = template != null && template.machines != null && template.machines.Length > 0
                ? template.machines[0] : null;

            Directory = BuildDirectory(content, machine);

            var pool = IssuePool.DefaultTable();
            var personaFactory = new PersonaFactory(content.personaPool, content.staffCallerNames);
            var desktopFactory = new DesktopFactory(machine?.baseline);

            // The onboarding-guidance boundary IS the first pool tier's boundary — one number, one place.
            assembler = new ProblemAssembler(content, Directory, personaFactory, desktopFactory, guidance,
                                             IssuePool.OnboardingMaxDay(pool));
            randomPool = new RandomPoolProblemFactory(assembler, pool);
            autoFactory = randomPool;
        }

        /// <summary>
        /// Rolled directory by default (a different shop on every call, each sitting beside a near-miss).
        /// Setting crmClusterCount to 0 falls back to the authored realStore + crmDecoys, where only the
        /// real record may call — the old fixed-customer behaviour, kept for scripted demos and tests.
        /// </summary>
        private static StoreDirectory BuildDirectory(ContentDatabaseSO content, MachineConfig machineTemplate)
        {
            if (content.crmClusterCount > 0)
                return new StoreDirectoryFactory(content.storeNames, machineTemplate)
                    .Build(content.crmClusterCount);

            var authored = content.CrmDirectory();
            var callers = authored.Where(r => r.isRealAccount).ToList();
            return new StoreDirectory(authored, callers);
        }

        /// <summary>Normal spawn during a shift — rolls from the day's pool (or a due recurrence).</summary>
        public ProblemInstance GenerateAuto(int day) => autoFactory.Create(day);

        /// <summary>Dev picker ("Force this call now") — exact combo, no roll.</summary>
        public ProblemInstance GenerateForced(int day, string[] issueIds) =>
            new ForcedIssueProblemFactory(assembler, issueIds).Create(day);

        /// <summary>
        /// Wraps auto-spawn so ConsequenceManager's due recurrences get priority. Wired by GameManager;
        /// leaves RandomPoolProblemFactory untouched.
        /// </summary>
        public void EnableRecurring(Func<int, List<string>> dueToday, Action<string> consume) =>
            autoFactory = new RecurringProblemFactory(assembler, randomPool, dueToday, consume);

        /// <summary>Roughly 2–6 tickets/night, scaling up with the day (prototype ticketCountForDay).</summary>
        public static int TicketCountForDay(int day) => Mathf.Clamp(Mathf.RoundToInt(2 + day * 0.05f), 1, 6);
    }
}
