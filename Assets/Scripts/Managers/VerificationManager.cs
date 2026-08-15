using System.Linq;
using System.Text;
using POSTechSupport.Core;
using POSTechSupport.Data;
using POSTechSupport.Logic;

namespace POSTechSupport.Managers
{
    /// <summary>
    /// Two independent verification layers (Docs/manager.md VerificationManager): "right store" (CRM
    /// lookup → the remote connect either works or doesn't) and "right person" (click-to-compare
    /// identity → Caller Authorization). Nothing is auto-revealed; the player actively compares a CRM
    /// field against a chat statement.
    ///
    /// Neither layer keeps a "verified" flag. The game holds exactly two consequences — did the remote
    /// session connect, and was authorization confirmed — and both live on the ticket. A separate
    /// VerificationState existed for a while and nothing ever read it; a bookkeeping mirror of state
    /// that already exists is just a second thing to keep in sync.
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

        /// <summary>
        /// Picking a record only changes what the CRM panel shows — including which credentials. Nothing
        /// is "verified" by the act of selecting: whether it was the right account shows up at Connect.
        /// The stale failure message is cleared, since it was about the previous record's credentials.
        /// </summary>
        public void SelectCrmResult(ProblemInstance p, int index)
        {
            p.ticket.crmLookup.selectedIndex = index;
            if (!p.ticket.remoteConnect.connected)
                p.ticket.remoteConnect.outcome = RemoteConnectOutcome.None;
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

                // Only the owner-name match carries state. A store-name or register match is information
                // for the PLAYER, not a verdict: the customer may be naming the near-miss shop, and a
                // match against that shop's record would then be a match on the wrong account.
                if (type == FactType.OwnerName && match) p.ticket.authorization.confirmed = true;
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
            rc.queryId = remoteId ?? "";
            rc.queryPass = passcode ?? "";
            // Both sides must be non-empty: normalising an unset credential and an empty box to "" would
            // otherwise make a blank form connect.
            string typedId = Normalize(rc.queryId);
            string id = Normalize(p.store?.remoteId), pass = Normalize(rc.passcode);
            rc.passcodeMatched = pass.Length > 0 && Normalize(rc.queryPass) == pass;
            bool ok = id.Length > 0 && typedId == id && rc.passcodeMatched;

            // Three distinct failures, each named for what actually went wrong: an ID no site owns (the
            // digits are wrong), another site's device (the record is wrong), or the right device with a
            // code it won't take (they need to ask the customer to read it out). None of them says WHICH
            // record is the right one.
            bool deviceExists = typedId.Length > 0 && p.crmDirectory != null &&
                                p.crmDirectory.Any(r => Normalize(r.remoteId) == typedId);

            rc.connected = ok;
            rc.outcome = ok ? RemoteConnectOutcome.Connected
                       : typedId.Length > 0 && typedId == id ? RemoteConnectOutcome.PasscodeRejected
                       : deviceExists ? RemoteConnectOutcome.NoSession
                       : RemoteConnectOutcome.UnknownDevice;
            return ok;
        }

        /// <summary>
        /// Credentials are compared by their characters alone: "585 966 535", "585966535" and
        /// "585-966-535" are the same device, and passcode case never matters. How an ID is grouped on
        /// screen is presentation — making the player reproduce the spacing tests typing, not verifying.
        /// </summary>
        private static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
                if (char.IsLetterOrDigit(c)) sb.Append(char.ToUpperInvariant(c));
            return sb.ToString();
        }
    }
}
