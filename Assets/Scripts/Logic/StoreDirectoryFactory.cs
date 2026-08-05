using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using POSTechSupport.Data;

namespace POSTechSupport.Logic
{
    /// <summary>
    /// The CRM directory for this campaign, plus the two picks a ticket needs from it: who is calling,
    /// and which neighbouring account they could plausibly be mistaken for.
    ///
    /// Shared by every ticket and never mutated — which is why "is this the account on the phone?" is
    /// answered by comparing against the ticket's own store (ProblemInstance.IsCallerRecord) instead of
    /// by a flag on the record: the shop that is genuine on this call is a decoy on the next one.
    /// </summary>
    public class StoreDirectory
    {
        public readonly List<StoreRecord> records;
        private readonly List<StoreRecord> callers;

        /// <param name="callers">Accounts allowed to be on the phone. Null = any record may call.</param>
        public StoreDirectory(List<StoreRecord> records, List<StoreRecord> callers = null)
        {
            this.records = records ?? new List<StoreRecord>();
            this.callers = callers != null && callers.Count > 0 ? callers : this.records;
        }

        public StoreRecord PickCaller() =>
            callers.Count == 0 ? null : callers[Random.Range(0, callers.Count)];

        /// <summary>
        /// A record the caller could be confused with: shares its first word or its trade word.
        /// This is what a misremembering caller states instead of their own shop name, so the wrong
        /// answer is a near miss the player has to catch, not a random unrelated shop.
        /// </summary>
        public StoreRecord PickConfusable(StoreRecord caller)
        {
            if (caller == null || records.Count < 2) return caller;

            string first = FirstWord(caller.storeName);
            string trade = LastWord(caller.storeName);
            var near = records.Where(r => r != caller &&
                                          (FirstWord(r.storeName) == first || LastWord(r.storeName) == trade))
                              .ToList();
            if (near.Count == 0) near = records.Where(r => r != caller).ToList();
            return near.Count == 0 ? caller : near[Random.Range(0, near.Count)];
        }

        private static string FirstWord(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            int i = s.IndexOf(' ');
            return i < 0 ? s : s.Substring(0, i);
        }

        private static string LastWord(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            int i = s.LastIndexOf(' ');
            return i < 0 ? s : s.Substring(i + 1);
        }
    }

    /// <summary>
    /// Builds a directory by CROSSING a StoreNameTableSO's word lists, so the shop on the phone differs
    /// from call to call and every account sits next to a near-miss (Docs/manager.md VerificationManager).
    ///
    /// One coupling is deliberate and load-bearing: every generated account carries the machine id from
    /// the authored baseline (REG-1), because the simulated desktop is built from that same baseline.
    /// A record claiming REG-7 while Terminal ▸ Status reads REG-1 would be a false mismatch, i.e. a bug
    /// dressed as a clue. The register trap comes from the CALLER misremembering, not from the record.
    /// </summary>
    public class StoreDirectoryFactory
    {
        private readonly StoreNameTableSO table;
        private readonly MachineConfig machineTemplate;

        public StoreDirectoryFactory(StoreNameTableSO table, MachineConfig machineTemplate)
        {
            this.table = table;
            this.machineTemplate = machineTemplate;
        }

        /// <param name="clusterCount">How many confusable families to roll; each yields 2–4 accounts.</param>
        public StoreDirectory Build(int clusterCount)
        {
            var clusters = Clusters();
            var trades = Words(table?.businessTypes, StoreNameTableSO.Defaults.BusinessTypes);

            int take = Mathf.Clamp(clusterCount, 1, clusters.Length);
            var order = Enumerable.Range(0, clusters.Length).OrderBy(_ => Random.value).Take(take);

            var records = new List<StoreRecord>();
            var usedNames = new HashSet<string>();
            var usedIds = new HashSet<string>();
            var usedOwners = new HashSet<string>();

            foreach (int c in order)
            {
                var variants = clusters[c].OrderBy(_ => Random.value).ToArray();
                string tradeA = trades[Random.Range(0, trades.Length)];
                string tradeB = tradeA;
                for (int guard = 0; guard < 8 && tradeB == tradeA; guard++)
                    tradeB = trades[Random.Range(0, trades.Length)];

                // Same first word, different trade — "Sunrise Diner" vs "Sunrise Bakery".
                Add(records, usedNames, usedIds, usedOwners, $"{variants[0]} {tradeA}");
                if (tradeB != tradeA)
                    Add(records, usedNames, usedIds, usedOwners, $"{variants[0]} {tradeB}");

                // Sibling first word, same trade — "Sunset Diner". The classic wrong pick.
                if (variants.Length > 1)
                    Add(records, usedNames, usedIds, usedOwners, $"{variants[1]} {tradeA}");
                if (variants.Length > 2 && Random.value < 0.5f)
                    Add(records, usedNames, usedIds, usedOwners, $"{variants[2]} {tradeA}");
            }

            return new StoreDirectory(records);
        }

        private void Add(List<StoreRecord> into, HashSet<string> names, HashSet<string> ids,
                         HashSet<string> owners, string storeName)
        {
            if (string.IsNullOrWhiteSpace(storeName) || !names.Add(storeName)) return;

            into.Add(new StoreRecord
            {
                storeId = UniqueId(ids),
                storeName = storeName,
                ownerName = UniqueOwner(owners),
                phoneNumber = $"555-0{Random.Range(100, 1000)}",
                address = $"{Random.Range(3, 900)} {Pick(table?.streetNames, StoreNameTableSO.Defaults.StreetNames)}",
                remoteId = $"{Random.Range(100, 1000)} {Random.Range(100, 1000)} {Random.Range(100, 1000)}",
                fixedPasscode = Passcode(),
                isRealAccount = false,     // meaningless here: the caller is chosen per ticket
                machines = MachinesFromTemplate(),
            });
        }

        private MachineConfig[] MachinesFromTemplate()
        {
            var t = machineTemplate;
            return new[]
            {
                new MachineConfig
                {
                    machineId = !string.IsNullOrEmpty(t?.machineId) ? t.machineId : "REG-1",
                    osVersion = !string.IsNullOrEmpty(t?.osVersion) ? t.osVersion : "Win 10 IoT",
                    posSoftwareVersion = !string.IsNullOrEmpty(t?.posSoftwareVersion) ? t.posSoftwareVersion : "POS Suite 4.2.1",
                    hardware = t?.hardware ?? new HardwareSpec(),
                    // Shared, not cloned: DesktopFactory only READS a baseline and writes into the
                    // module instances it builds, so one baseline can back every account.
                    baseline = t?.baseline ?? new ModuleBaseline(),
                },
            };
        }

        private string[][] Clusters()
        {
            var authored = table?.nameClusters?
                .Where(c => c?.variants != null && c.variants.Length > 0)
                .Select(c => c.variants.Where(v => !string.IsNullOrWhiteSpace(v)).ToArray())
                .Where(v => v.Length > 0)
                .ToArray();
            return authored != null && authored.Length > 0 ? authored : StoreNameTableSO.Defaults.Clusters;
        }

        private static string[] Words(string[] authored, string[] fallback)
        {
            var clean = authored?.Where(w => !string.IsNullOrWhiteSpace(w)).ToArray();
            return clean != null && clean.Length > 0 ? clean : fallback;
        }

        private static string Pick(string[] authored, string[] fallback)
        {
            var pool = Words(authored, fallback);
            return pool[Random.Range(0, pool.Length)];
        }

        private static string UniqueId(HashSet<string> used)
        {
            for (int i = 0; i < 64; i++)
            {
                string id = $"ST-{Random.Range(1000, 10000)}";
                if (used.Add(id)) return id;
            }
            return $"ST-{used.Count + 1000}";
        }

        private string UniqueOwner(HashSet<string> used)
        {
            for (int i = 0; i < 64; i++)
            {
                string name = $"{Pick(table?.ownerFirstNames, StoreNameTableSO.Defaults.FirstNames)} " +
                              $"{Pick(table?.ownerLastNames, StoreNameTableSO.Defaults.LastNames)}";
                if (used.Add(name)) return name;
            }
            return $"{Pick(table?.ownerFirstNames, StoreNameTableSO.Defaults.FirstNames)} {used.Count}";
        }

        private static string Passcode()
        {
            const string chars = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
            var s = new char[5];
            for (int i = 0; i < 5; i++) s[i] = chars[Random.Range(0, chars.Length)];
            return new string(s);
        }
    }
}
