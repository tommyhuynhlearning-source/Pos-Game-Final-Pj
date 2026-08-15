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

        public StoreDirectory(List<StoreRecord> records)
        {
            this.records = records ?? new List<StoreRecord>();
        }

        /// <summary>Any account in the directory may be the one on the phone tonight.</summary>
        public StoreRecord PickCaller() =>
            records.Count == 0 ? null : records[Random.Range(0, records.Count)];

        /// <summary>
        /// A record the caller could be confused with — what a misremembering caller states instead of
        /// their own shop name, so the wrong answer is a near miss the player has to catch.
        ///
        /// Candidates are RANKED, not pooled. Sharing a trade word alone is the weakest kind of near miss
        /// and there are usually many of those, so an unranked pick reaches for "Station Road Bookshop"
        /// when "Fairview Bookshop" — same trade AND a sibling first word — was sitting right there.
        /// Ranking keeps the hardest confusion the most likely one.
        /// </summary>
        public StoreRecord PickConfusable(StoreRecord caller)
        {
            if (caller == null || records.Count < 2) return caller;

            string first = FirstWord(caller.storeName);
            string trade = LastWord(caller.storeName);
            var others = records.Where(r => r != caller).ToList();
            if (others.Count == 0) return caller;

            bool SameFamily(StoreRecord r) => r.familyKey != null && r.familyKey == caller.familyKey;

            var tiers = new[]
            {
                // 1. Same authored family AND one word literally shared — "Corner House Barbers" against
                //    "Corner House Pharmacy" or "Cornerstone Barbers". The tightest confusion available.
                others.Where(r => SameFamily(r) &&
                                  (FirstWord(r.storeName) == first || LastWord(r.storeName) == trade)).ToList(),
                // 2. Same family, no shared word: sibling first words under different trades.
                others.Where(SameFamily).ToList(),
                // 3. Same first word from another family (possible once families share a word).
                others.Where(r => FirstWord(r.storeName) == first).ToList(),
                // 4. Same trade only — a real but softer near miss.
                others.Where(r => LastWord(r.storeName) == trade).ToList(),
                others,
            };

            var pool = tiers.First(t => t.Count > 0);
            return pool[Random.Range(0, pool.Count)];
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
            var used = new UsedValues();
            var usedTrades = new HashSet<string>();

            foreach (int c in order)
            {
                var variants = clusters[c].OrderBy(_ => Random.value).ToArray();
                string family = variants[0];      // the family's own label, for PickConfusable
                string tradeA = PickTrade(trades, usedTrades);
                string tradeB = PickTrade(trades, usedTrades, avoid: tradeA);

                // Same first word, different trade — "Sunrise Diner" vs "Sunrise Bakery".
                Add(records, used, family, $"{variants[0]} {tradeA}");
                if (tradeB != tradeA)
                    Add(records, used, family, $"{variants[0]} {tradeB}");

                // Sibling first word, same trade — "Sunset Diner". The classic wrong pick.
                if (variants.Length > 1)
                    Add(records, used, family, $"{variants[1]} {tradeA}");
                if (variants.Length > 2 && Random.value < 0.5f)
                    Add(records, used, family, $"{variants[2]} {tradeA}");
            }

            return new StoreDirectory(records);
        }

        /// <summary>
        /// Prefers a trade no family has used yet. Without this, several families can land on the same
        /// trade and the directory reads as five bookshops — which both looks wrong and turns the trade
        /// word into a coincidence rather than a discriminator.
        /// </summary>
        private static string PickTrade(string[] trades, HashSet<string> used, string avoid = null)
        {
            var fresh = trades.Where(t => t != avoid && !used.Contains(t)).ToList();
            var pool = fresh.Count > 0 ? fresh : trades.Where(t => t != avoid).ToList();
            if (pool.Count == 0) return avoid;
            string pick = pool[Random.Range(0, pool.Count)];
            used.Add(pick);
            return pick;
        }

        /// <summary>
        /// Every field a player might use to tell two accounts apart has to be unique across the whole
        /// directory — including the remote ID, since two shops sharing one would make the wrong record
        /// connect to the right machine and quietly break the only check the game actually enforces.
        /// </summary>
        private class UsedValues
        {
            public readonly HashSet<string> names = new();
            public readonly HashSet<string> ids = new();
            public readonly HashSet<string> owners = new();
            public readonly HashSet<string> remoteIds = new();
        }

        private void Add(List<StoreRecord> into, UsedValues used, string familyKey, string storeName)
        {
            if (string.IsNullOrWhiteSpace(storeName) || !used.names.Add(storeName)) return;

            into.Add(new StoreRecord
            {
                storeId = UniqueId(used.ids),
                storeName = storeName,
                ownerName = UniqueOwner(used.owners),
                phoneNumber = $"555-0{Random.Range(100, 1000)}",
                address = $"{Random.Range(3, 900)} {Pick(table?.streetNames, StoreNameTableSO.Defaults.StreetNames)}",
                remoteId = UniqueRemoteId(used.remoteIds),
                familyKey = familyKey,
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

        private static string UniqueRemoteId(HashSet<string> used)
        {
            for (int i = 0; i < 64; i++)
            {
                string id = $"{Random.Range(100, 1000)} {Random.Range(100, 1000)} {Random.Range(100, 1000)}";
                if (used.Add(id)) return id;
            }
            return $"{Random.Range(100, 1000)} {Random.Range(100, 1000)} {used.Count + 100}";
        }

    }
}
