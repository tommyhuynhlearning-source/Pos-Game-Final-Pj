using System;
using UnityEngine;

namespace POSTechSupport.Data
{
    /// <summary>
    /// The word lists a CRM directory is built from: shop-name parts, owner names, streets.
    /// Names are COMBINED at runtime rather than authored one by one, because the point of the CRM
    /// step is that neighbouring records look alike — and authoring every near-miss by hand is what
    /// left the game with a single caller ("Sunrise Diner") on every ticket.
    ///
    /// A cluster is a set of first words that are easy to mistake for each other
    /// ("Sunrise" / "Sunset" / "Sunnyside"). StoreDirectoryFactory crosses a cluster with the trade
    /// words to produce a family of confusable accounts: same word + different trade, sibling word +
    /// same trade. Everything here is static authored data (P6); the rolled records are plain objects.
    /// </summary>
    [CreateAssetMenu(menuName = "POS/StoreNameTable", fileName = "StoreNameTable")]
    public class StoreNameTableSO : ScriptableObject
    {
        /// <summary>One family of mistakeable first words (Unity can't serialize string[][]).</summary>
        [Serializable]
        public class NameCluster
        {
            public string[] variants;
            public NameCluster() { variants = Array.Empty<string>(); }
            public NameCluster(params string[] variants) { this.variants = variants; }
        }

        [Tooltip("Families of confusable first words. Empty = use the built-in defaults.")]
        public NameCluster[] nameClusters;

        [Tooltip("Trade words appended to a first word: Diner, Bakery, …")]
        public string[] businessTypes;

        public string[] ownerFirstNames;
        public string[] ownerLastNames;
        public string[] streetNames;

        /// <summary>
        /// The built-in lists. Kept in code so a project whose content assets are stale (or absent)
        /// still gets a varied directory — the generator writes these into the asset, it doesn't own them.
        /// </summary>
        public static class Defaults
        {
            public static readonly string[][] Clusters =
            {
                new[] { "Sunrise", "Sunset", "Sunnyside" },
                new[] { "Riverside", "Riverview", "Rivergate" },
                new[] { "Oakwood", "Oakdale", "Oakridge" },
                new[] { "Northgate", "Northfield", "Northbridge" },
                new[] { "Blue Door", "Blue Dove", "Bluebird" },
                new[] { "Maple Grove", "Maple Street" },
                new[] { "Cornerstone", "Corner House" },
                new[] { "Kingsway", "Kingsland", "Kings Cross" },
                new[] { "Old Mill", "Old Market" },
                new[] { "Harbour Light", "Harbour Point" },
                new[] { "Silver Birch", "Silver Beech" },
                new[] { "Greenway", "Greenfield", "Green Lane" },
                new[] { "Station Road", "Station Yard" },
                new[] { "White Horse", "White Hart" },
                new[] { "Elmwood", "Elmhurst" },
                new[] { "Fairview", "Fairfield" },
            };

            public static readonly string[] BusinessTypes =
            {
                "Diner", "Cafe", "Bakery", "Grill", "Deli", "Market", "Mini Mart", "Bistro",
                "Pizzeria", "Butchers", "Pharmacy", "Barbers", "Laundry", "Florist",
                "Bookshop", "Hardware", "Wine Store", "Creamery", "Noodle Bar", "Coffee House",
            };

            public static readonly string[] FirstNames =
            {
                "Maria", "Tom", "Priya", "Andre", "Nadia", "Lucas", "Ivy", "Hassan", "Elena",
                "Marcus", "Grace", "Diego", "Yuki", "Omar", "Fiona", "Peter", "Anita", "Colin",
                "Rosa", "Samir", "Helen", "Joel", "Mina", "Victor",
            };

            public static readonly string[] LastNames =
            {
                "Alvarez", "Reyes", "Nair", "Whitmore", "Okafor", "Delgado", "Brennan", "Karim",
                "Petrova", "Hollis", "Chan", "Moreau", "Sorensen", "Bianchi", "Nakamura", "Duarte",
                "Lindqvist", "Farrell", "Osei", "Vance",
            };

            public static readonly string[] StreetNames =
            {
                "Elm St", "Oak Ave", "Birch Rd", "Cedar Ln", "Market St", "Pine Way", "Hill Rd",
                "Quarry St", "Bell Ave", "Harbour Rd", "Willow Cl", "Station Rd", "Mill Ln",
                "Kingsway", "Fern Ct", "Granite St",
            };
        }

        /// <summary>Fills every list with <see cref="Defaults"/> — used by the content generator.</summary>
        public void LoadDefaults()
        {
            var clusters = new NameCluster[Defaults.Clusters.Length];
            for (int i = 0; i < clusters.Length; i++)
                clusters[i] = new NameCluster((string[])Defaults.Clusters[i].Clone());
            nameClusters = clusters;
            businessTypes = (string[])Defaults.BusinessTypes.Clone();
            ownerFirstNames = (string[])Defaults.FirstNames.Clone();
            ownerLastNames = (string[])Defaults.LastNames.Clone();
            streetNames = (string[])Defaults.StreetNames.Clone();
        }
    }
}
