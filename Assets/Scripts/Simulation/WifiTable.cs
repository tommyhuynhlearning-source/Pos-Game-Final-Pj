using System.Collections.Generic;

namespace POSTechSupport.Simulation
{
    /// <summary>
    /// Nearby Wi-Fi networks, each with its OWN subnet (real DHCP behaviour): the terminal picks an
    /// SSID and the network hands it an IP/gateway from that range — the player never types an IP.
    /// Ported from the prototype's WIFI_NETWORKS. Used by <see cref="DependencyGraph.TerminalNetInfo"/>.
    /// </summary>
    public static class WifiTable
    {
        public const string StoreSsid = "SunriseDiner-Main";

        public readonly struct NetInfo
        {
            public readonly string ip;
            public readonly string gateway;
            public NetInfo(string ip, string gateway) { this.ip = ip; this.gateway = gateway; }
        }

        public static readonly Dictionary<string, NetInfo> Networks = new()
        {
            { "SunriseDiner-Main",  new NetInfo("192.168.1.50", "192.168.1.1") },
            { "SunriseDiner-Guest", new NetInfo("192.168.50.23", "192.168.50.1") },
            { "CoffeeShop-Public",  new NetInfo("10.10.10.87", "10.10.10.1") },
            { "iPhone-Hotspot",     new NetInfo("172.20.10.4", "172.20.10.1") },
        };

        public static readonly string[] NearbyNetworks =
        {
            "SunriseDiner-Main", "SunriseDiner-Guest", "CoffeeShop-Public", "iPhone-Hotspot"
        };

        /// <summary>The correct IP for the store's own network — what POS should have registered.</summary>
        public static string TerminalIpCorrect => Networks[StoreSsid].ip;

        /// <summary>The real, correct DB server address (POS Manager ▸ Connections must match this).</summary>
        public const string PosDbHostCorrect = "db.sunrisediner.local";

        // --- Other "what does healthy look like" constants -------------------------------------------
        // Kept next to the network table because they're all the same kind of thing: a single correct
        // value the player has to compare an observed value against.

        /// <summary>The store's own resolver. A public DNS can't resolve the internal db host.</summary>
        public const string StoreDnsCorrect = "192.168.1.1";

        /// <summary>Minimum terminal firmware this POS build will talk to.</summary>
        public const string MinTerminalFirmware = "4.0";
        public const string TerminalFirmwareCurrent = "4.2";

        /// <summary>Configured sales tax. A wrong value totals receipts incorrectly without breaking any field.</summary>
        public const string TaxRateCorrect = "8.25";

        /// <summary>Receipt roll width the printer is set up for.</summary>
        public const string PaperWidthCorrect = "80mm";

        /// <summary>Windows default printer that receipt jobs must go to.</summary>
        public const string DefaultPrinterCorrect = "ReceiptPrinter";

        public static NetInfo Resolve(string ssid) =>
            Networks.TryGetValue(ssid, out var info) ? info : new NetInfo("unassigned", "unassigned");
    }
}
