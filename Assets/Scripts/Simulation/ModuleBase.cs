using System.Collections.Generic;
using POSTechSupport.Core;

namespace POSTechSupport.Simulation
{
    /// <summary>
    /// Runtime state of one simulated module (GDD nguyên tắc bất biến #3: a fault is WRONG STATE
    /// in a module, not a boolean flag). State is a string dictionary so IssueSO / DesktopActionSO
    /// stay data-driven (bools stored as "true"/"false").
    ///
    /// A module knows ONLY its own local health (<see cref="LocalStatus"/>) — it deliberately does
    /// NOT know about upstream dependencies. The Blocked-vs-Error cascade lives in one place only:
    /// <see cref="DependencyGraph"/> (Docs/app.md §7, Docs/manager.md DesktopManager).
    /// </summary>
    public abstract class ModuleBase
    {
        public abstract ModuleType Type { get; }

        protected readonly Dictionary<string, string> fields = new();

        public string Get(string name) => fields.TryGetValue(name, out var v) ? v : null;
        public void Set(string name, string value) => fields[name] = value;
        public bool GetBool(string name) => Get(name) == "true";
        public IReadOnlyDictionary<string, string> Fields => fields;

        /// <summary>
        /// This module's OWN-FAULT status, ignoring dependencies. Returns Error (+reason) for a local
        /// misconfiguration, otherwise OK. Never returns Blocked — that's the cascade's job.
        /// </summary>
        public abstract Data.StatusResult LocalStatus(VirtualDesktopInstance desktop);

        /// <summary>Deep copy for cloning a baseline into a fresh ticket (SO state must never be mutated).</summary>
        public void CopyFieldsFrom(IReadOnlyDictionary<string, string> src)
        {
            fields.Clear();
            foreach (var kv in src) fields[kv.Key] = kv.Value;
        }
    }
}
