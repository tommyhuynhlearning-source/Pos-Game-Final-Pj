using System.Linq;
using UnityEditor;
using UnityEngine;
using POSTechSupport.Data;
using POSTechSupport.Logic;

namespace POSTechSupport.EditorTools
{
    /// <summary>
    /// Rolls a CRM directory and prints it, so the name generator can be judged without playing a shift.
    /// Needs no content assets: with no StoreNameTableSO selected it falls back to
    /// StoreNameTableSO.Defaults, exactly as StoreDirectoryFactory does at runtime.
    ///
    /// What to read in the output: accounts must arrive in confusable families — the same first word
    /// under two trades, and a sibling first word under the same trade ("Sunrise Diner" / "Sunrise
    /// Bakery" / "Sunset Diner"). A directory of unrelated names means the CRM step has no trap in it.
    /// Menu: POS ▸ Debug ▸ Print CRM Directory.
    /// </summary>
    public static class StoreDirectoryPreview
    {
        [MenuItem("POS/Debug/Print CRM Directory")]
        public static void Print()
        {
            var table = AssetDatabase.FindAssets("t:StoreNameTableSO")
                .Select(g => AssetDatabase.LoadAssetAtPath<StoreNameTableSO>(AssetDatabase.GUIDToAssetPath(g)))
                .FirstOrDefault(t => t != null);

            var directory = new StoreDirectoryFactory(table, null).Build(6);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[StoreDirectoryPreview] {directory.records.Count} accounts " +
                          $"(name table: {(table != null ? table.name : "built-in defaults")})");
            foreach (var r in directory.records.OrderBy(r => r.storeName))
                sb.AppendLine($"  {r.storeId}  {r.storeName,-28} {r.ownerName,-20} {r.address,-20} " +
                              $"remote {r.remoteId}  reg {r.MachineId}");

            var caller = directory.PickCaller();
            sb.AppendLine($"  → sample caller: {caller?.storeName} ({caller?.ownerName}) — " +
                          $"could be mistaken for: {directory.PickConfusable(caller)?.storeName}");
            Debug.Log(sb.ToString());
        }
    }
}
