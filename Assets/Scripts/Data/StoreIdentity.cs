using System.Linq;
using System.Text;

namespace POSTechSupport.Data
{
    /// <summary>
    /// Everything about a site that is NAMED after the shop: its Wi-Fi SSIDs and its record-store host.
    /// Derived from the store name, never hardcoded — a shop called "Corner House Bakery" has to be on
    /// "CornerHouseBakery-Main", not on some other shop's network.
    ///
    /// This is what lets the fault corpus stay store-agnostic. Authored assets write TOKENS
    /// (<c>{SSID}</c>, <c>{DB_HOST}</c>, …) and the values are substituted against the ticket's own
    /// identity at three chokepoints: state writes (VirtualDesktopInstance.Apply), state comparisons
    /// (DependencyGraph.CheckState) and display text (ActionManager / UI). One fault asset therefore
    /// works for every shop in the CRM instead of naming one of them.
    ///
    /// Subnets are deliberately NOT derived: "the store's own network is 192.168.1.x, the guest network
    /// is 192.168.50.x" is a property of how the site is wired, not of what it is called (see WifiTable).
    /// </summary>
    public class StoreIdentity
    {
        public const string TokenStore = "{STORE}";
        public const string TokenSsid = "{SSID}";
        public const string TokenSsidGuest = "{SSID_GUEST}";
        public const string TokenDbHost = "{DB_HOST}";
        public const string TokenDbHostTypo = "{DB_HOST_TYPO}";

        public readonly string storeName;
        public readonly string slug;         // "Sunrise Diner" → "SunriseDiner"
        public readonly string mainSsid;     // the shop's own network
        public readonly string guestSsid;    // the customer network next to it — P6's trap
        public readonly string dbHost;       // record store, POS Manager ▸ Connections must match
        public readonly string dbHostTypo;   // a believable mistyping of dbHost — P12's fault value

        /// <summary>Used when no store is in play (the cascade smoke test, a bare desktop).</summary>
        public static readonly StoreIdentity Generic = For("Demo Store");

        private StoreIdentity(string storeName, string slug, string mainSsid, string guestSsid,
                              string dbHost, string dbHostTypo)
        {
            this.storeName = storeName;
            this.slug = slug;
            this.mainSsid = mainSsid;
            this.guestSsid = guestSsid;
            this.dbHost = dbHost;
            this.dbHostTypo = dbHostTypo;
        }

        public static StoreIdentity For(string storeName)
        {
            if (string.IsNullOrWhiteSpace(storeName)) storeName = "Demo Store";
            string slug = Slug(storeName);
            string host = $"db.{slug.ToLowerInvariant()}.local";
            return new StoreIdentity(storeName, slug, $"{slug}-Main", $"{slug}-Guest", host, Typo(storeName, host));
        }

        /// <summary>Letters and digits only, words joined — how a shop actually names its access point.</summary>
        private static string Slug(string storeName)
        {
            var sb = new StringBuilder();
            foreach (char c in storeName) if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.Length > 0 ? sb.ToString() : "Store";
        }

        /// <summary>
        /// A wrong host that looks right at a glance: a hyphen where the words join
        /// ("db.sunrise-diner.local" for Sunrise Diner), or a doubled last letter for a one-word name.
        /// Guaranteed different from <see cref="dbHost"/>, or P12 would be born already fixed.
        /// </summary>
        private static string Typo(string storeName, string correctHost)
        {
            var words = storeName.Split(new[] { ' ', '\t', '-', '\'', '&' },
                                       System.StringSplitOptions.RemoveEmptyEntries)
                                 .Select(Slug).Where(w => w.Length > 0).ToArray();

            string body = words.Length > 1
                ? $"{words[0]}-{string.Concat(words.Skip(1))}"
                : (words.Length == 1 ? words[0] + words[0][^1] : "store");

            string typo = $"db.{body.ToLowerInvariant()}.local";
            return typo == correctHost ? $"db.{body.ToLowerInvariant()}x.local" : typo;
        }

        /// <summary>
        /// Substitutes the tokens in an authored string — a state value, an expected value, or clue text.
        /// Cheap and null-safe: strings without a brace return untouched, which is almost all of them.
        /// </summary>
        public string Resolve(string authored)
        {
            if (string.IsNullOrEmpty(authored) || authored.IndexOf('{') < 0) return authored;
            return authored
                .Replace(TokenSsidGuest, guestSsid)      // before {SSID}: it is not a prefix of it, but
                .Replace(TokenSsid, mainSsid)            // keeping the longer token first is the safe habit
                .Replace(TokenDbHostTypo, dbHostTypo)
                .Replace(TokenDbHost, dbHost)
                .Replace(TokenStore, storeName);
        }
    }
}
