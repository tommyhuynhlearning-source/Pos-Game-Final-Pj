using POSTechSupport.Data;

namespace POSTechSupport.Simulation
{
    /// <summary>
    /// Nearby Wi-Fi networks, each with its OWN subnet (real DHCP behaviour): the terminal picks an
    /// SSID and the network hands it an IP/gateway from that range — the player never types an IP.
    /// Ported from the prototype's WIFI_NETWORKS. Used by <see cref="DependencyGraph.TerminalNetInfo"/>.
    ///
    /// The two store networks are addressed by ROLE (own network / guest network) and named by the
    /// shop via <see cref="StoreIdentity"/>: how the site is wired is fixed, what it is called is not.
    /// </summary>
    public static class WifiTable
    {
        public readonly struct NetInfo
        {
            public readonly string ip;
            public readonly string gateway;
            public NetInfo(string ip, string gateway) { this.ip = ip; this.gateway = gateway; }
        }

        // Networks that belong to no particular shop — the ones a terminal can wander onto.
        public const string PublicCafeSsid = "CoffeeShop-Public";
        public const string HotspotSsid = "iPhone-Hotspot";

        private static readonly NetInfo StoreNet = new("192.168.1.50", "192.168.1.1");
        private static readonly NetInfo GuestNet = new("192.168.50.23", "192.168.50.1");
        private static readonly NetInfo PublicCafeNet = new("10.10.10.87", "10.10.10.1");
        private static readonly NetInfo HotspotNet = new("172.20.10.4", "172.20.10.1");

        /// <summary>What the terminal sees in "join a network", this shop's own SSIDs first.</summary>
        public static string[] NearbyNetworks(StoreIdentity id)
        {
            id ??= StoreIdentity.Generic;
            return new[] { id.mainSsid, id.guestSsid, PublicCafeSsid, HotspotSsid };
        }

        /// <summary>DHCP: the SSID the terminal joined decides its address range.</summary>
        public static NetInfo Resolve(StoreIdentity id, string ssid)
        {
            id ??= StoreIdentity.Generic;
            if (ssid == id.mainSsid) return StoreNet;
            if (ssid == id.guestSsid) return GuestNet;
            if (ssid == PublicCafeSsid) return PublicCafeNet;
            if (ssid == HotspotSsid) return HotspotNet;
            return new NetInfo("unassigned", "unassigned");
        }

        /// <summary>The correct IP for the store's own network — what POS should have registered.</summary>
        public static string TerminalIpCorrect => StoreNet.ip;

        // --- Other "what does healthy look like" constants -------------------------------------------
        // Kept next to the network table because they're all the same kind of thing: a single correct
        // value the player has to compare an observed value against.

        /// <summary>The store's own resolver. A public DNS can't resolve the internal db host.</summary>
        public const string StoreDnsCorrect = "192.168.1.1";   // the gateway of StoreNet, by definition

        /// <summary>Minimum terminal firmware this POS build will talk to.</summary>
        public const string MinTerminalFirmware = "4.0";
        public const string TerminalFirmwareCurrent = "4.2";

        /// <summary>Configured sales tax. A wrong value totals receipts incorrectly without breaking any field.</summary>
        public const string TaxRateCorrect = "8.25";

        /// <summary>Receipt roll width the printer is set up for.</summary>
        public const string PaperWidthCorrect = "80mm";

        /// <summary>Windows default printer that receipt jobs must go to.</summary>
        public const string DefaultPrinterCorrect = "ReceiptPrinter";

    }
}
