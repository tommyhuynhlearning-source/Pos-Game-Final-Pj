using System.Collections.Generic;
using POSTechSupport.Core;
using POSTechSupport.Data;

namespace POSTechSupport.Simulation
{
    /// <summary>
    /// The runtime desktop for one ticket: a set of modules with mutable state, built by cloning a
    /// healthy baseline and injecting faults (ProblemGenerator / DesktopManager). Never reads/writes
    /// SO assets — state lives only here (GDD nguyên tắc bất biến #6). Mirrors the prototype's
    /// freshDesktop() + Object.assign(fault) build.
    /// </summary>
    public class VirtualDesktopInstance
    {
        public readonly Dictionary<ModuleType, ModuleBase> modules = new();
        public DependencyGraph graph;

        /// <summary>
        /// Whose shop this desktop belongs to — the source of its SSIDs and record-store host. Every
        /// authored token is resolved against this, so one fault asset fits any store in the CRM.
        /// </summary>
        public StoreIdentity Identity { get; private set; } = StoreIdentity.Generic;

        public ModuleBase GetModule(ModuleType t) => modules.TryGetValue(t, out var m) ? m : null;

        /// <summary>Build a fresh, all-healthy desktop with every active module (prototype freshDesktop()).</summary>
        /// <param name="identity">The calling shop. Null = the generic site, for a store-less smoke test.</param>
        public static VirtualDesktopInstance BuildFresh(StoreIdentity identity = null)
        {
            var d = new VirtualDesktopInstance { Identity = identity ?? StoreIdentity.Generic };
            d.Add(new OSModule());
            d.Add(new NetworkModule(d.Identity));
            d.Add(new POSSoftwareModule(d.Identity));
            d.Add(new TerminalModule(d.Identity));
            d.Add(new PrinterModule());
            d.Add(new CashDrawerModule());
            d.graph = new DependencyGraph(d);
            return d;
        }

        private void Add(ModuleBase m) => modules[m.Type] = m;

        /// <summary>
        /// Apply a state change (inject a fault, or a Fix's write) into the target module. Chokepoint #1
        /// for token substitution: an authored "{SSID_GUEST}" becomes this shop's guest network here.
        /// </summary>
        public void Apply(FaultInjection change)
        {
            GetModule(change.module)?.Set(change.stateField, Identity.Resolve(change.faultValue));
        }

        /// <summary>Convenience: effective (dependency-resolved) status of a module.</summary>
        public StatusResult EffectiveStatus(ModuleType t) => graph.EffectiveStatus(t);
    }
}
