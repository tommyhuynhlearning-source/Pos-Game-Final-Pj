using System;
using System.Collections.Generic;
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

            Directory = new StoreDirectoryFactory(content.storeNames, machine)
                .Build(Mathf.Max(1, content.crmClusterCount));

            var pool = IssuePool.DefaultTable();
            var personaFactory = new PersonaFactory(content.personaPool, content.staffCallerNames);
            var desktopFactory = new DesktopFactory(machine?.baseline);

            // The onboarding-guidance boundary IS the first pool tier's boundary — one number, one place.
            assembler = new ProblemAssembler(content, Directory, personaFactory, desktopFactory, guidance,
                                             IssuePool.OnboardingMaxDay(pool));
            randomPool = new RandomPoolProblemFactory(assembler, pool);
            autoFactory = randomPool;
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

        /// <summary>
        /// Calls to schedule tonight. The rate lives on <see cref="GameConfigSO"/> as calls per IN-GAME
        /// hour so it can be retuned from the Inspector without touching code; the hard-coded prototype
        /// ramp (2–6/night) is only the fallback when no config is wired.
        /// </summary>
        public static int TicketCountForDay(int day, GameConfigSO config = null) =>
            config != null ? config.CallsForNight(day)
                           : Mathf.Clamp(Mathf.RoundToInt(2 + day * 0.05f), 1, 6);
    }
}
