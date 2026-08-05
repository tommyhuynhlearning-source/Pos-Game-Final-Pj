using System.Linq;
using POSTechSupport.Core;
using POSTechSupport.Data;
using POSTechSupport.Logic;

namespace POSTechSupport.Managers
{
    /// <summary>
    /// Two independent verification layers (Docs/manager.md VerificationManager): "right store" (CRM
    /// lookup) and "right person" (click-to-compare identity → Caller Authorization). Nothing is
    /// auto-revealed; the player actively compares a CRM field against a chat statement. Ported from
    /// the prototype's searchCrmDirectory / handleCompareClick / remote-connect form.
    /// </summary>
    public class VerificationManager
    {
        public void SetCrmQuery(ProblemInstance p, string query)
        {
            p.ticket.crmLookup.query = query;
            string q = (query ?? "").Trim().ToLowerInvariant();
            p.ticket.crmLookup.results = string.IsNullOrEmpty(q)
                ? new System.Collections.Generic.List<StoreRecord>()
                : p.crmDirectory.Where(r =>
                        (r.storeId ?? "").ToLowerInvariant().Contains(q) ||
                        (r.storeName ?? "").ToLowerInvariant().Contains(q))
                    .ToList();
            p.ticket.crmLookup.selectedIndex = -1;
        }

        public void SelectCrmResult(ProblemInstance p, int index)
        {
            p.ticket.crmLookup.selectedIndex = index;
            // The directory is shared, so "right record" is identity with THIS ticket's caller, not a
            // flag on the row: the same shop is the genuine account tonight and a decoy on another call.
            if (index >= 0 && index < p.ticket.crmLookup.results.Count)
                p.verification.storeVerified = p.IsCallerRecord(p.ticket.crmLookup.results[index]);
        }

        /// <summary>
        /// One click selects a fact; a second click of the SAME type from the OTHER source compares them.
        /// A MATCH on Owner Name establishes caller authorization (as real caller-ID + CRM would).
        /// </summary>
        public void CompareClick(ProblemInstance p, CompareSource source, FactType type, string value)
        {
            var cmp = p.ticket.compare;
            bool haveDifferentSideSameType = cmp.pending != null &&
                                             cmp.pendingSource != source &&
                                             cmp.pendingType == type;
            if (haveDifferentSideSameType)
            {
                string crmVal = cmp.pendingSource == CompareSource.Crm ? cmp.pending.value : value;
                string chatVal = cmp.pendingSource == CompareSource.Chat ? cmp.pending.value : value;
                bool match = crmVal.Trim().ToLowerInvariant() == chatVal.Trim().ToLowerInvariant();

                cmp.result = match ? CompareResult.Match : CompareResult.Mismatch;
                cmp.resultType = type;
                cmp.crmValue = crmVal;
                cmp.chatValue = chatVal;
                cmp.pending = null;

                if (type == FactType.OwnerName && match) p.ticket.authorization.confirmed = true;
                if (type == FactType.StoreName && match) p.verification.storeVerified = true;
                if (type == FactType.MachineId && match) p.verification.machineVerified = true;
            }
            else
            {
                cmp.pending = new FactRef { type = type, value = value };
                cmp.pendingType = type;
                cmp.pendingSource = source;
                cmp.result = CompareResult.None;
            }
        }

        /// <summary>
        /// Connect succeeds only with the REAL store's remote ID + this ticket's one-time passcode.
        /// Picking a decoy record just fails to connect — verification is the player's job, not a hard block.
        /// </summary>
        public bool TryRemoteConnect(ProblemInstance p, string remoteId, string passcode)
        {
            var rc = p.ticket.remoteConnect;
            rc.queryId = remoteId?.Trim() ?? "";
            rc.queryPass = passcode?.Trim() ?? "";
            bool ok = rc.queryId == p.store.remoteId &&
                      rc.queryPass.ToLowerInvariant() == (rc.passcode ?? "").ToLowerInvariant();
            rc.connected = ok;
            rc.connectFailed = !ok;
            return ok;
        }

        public bool CanGrantRemote(ProblemInstance p) => p.verification.CanGrantRemote();
    }
}
