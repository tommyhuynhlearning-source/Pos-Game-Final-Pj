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

        public ModuleBase GetModule(ModuleType t) => modules.TryGetValue(t, out var m) ? m : null;

        /// <summary>Build a fresh, all-healthy desktop with every active module (prototype freshDesktop()).</summary>
        public static VirtualDesktopInstance BuildFresh()
        {
            var d = new VirtualDesktopInstance();
            d.Add(new OSModule());
            d.Add(new NetworkModule());
            d.Add(new POSSoftwareModule());
            d.Add(new TerminalModule());
            d.Add(new PrinterModule());
            d.Add(new CashDrawerModule());
            d.graph = new DependencyGraph(d);
            return d;
        }

        private void Add(ModuleBase m) => modules[m.Type] = m;

        /// <summary>Apply a state change (inject a fault, or a Fix's write) into the target module.</summary>
        public void Apply(FaultInjection change)
        {
            GetModule(change.module)?.Set(change.stateField, change.faultValue);
        }

        /// <summary>Convenience: effective (dependency-resolved) status of a module.</summary>
        public StatusResult EffectiveStatus(ModuleType t) => graph.EffectiveStatus(t);
    }
}
