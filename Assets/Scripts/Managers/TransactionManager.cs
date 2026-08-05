using System.Linq;
using POSTechSupport.Core;
using POSTechSupport.Logic;

namespace POSTechSupport.Managers
{
    public class TxActionResult
    {
        public bool performed;
        public bool unauthorizedHarm;   // caller should file a MailboxManager complaint if true
        public string log;
    }

    /// <summary>
    /// Models a real POS transaction lifecycle (Docs/manager.md TransactionManager, Docs/app.md
    /// "Transaction data model"). Void only while Open; Refund even after Settled. Refund/Void go
    /// through the Caller Authorization gate — doing one unconfirmed against an unauthorized caller is a
    /// business-logic HarmEvent that caps the ticket at Degraded. Ported from the prototype.
    /// </summary>
    public class TransactionManager
    {
        public Transaction Authorize(ProblemInstance p, double amount, TransType type)
        {
            var t = new Transaction { type = type, amount = amount, status = TransStatus.Open };
            p.transactions.live.Add(t);
            return t;
        }

        /// <summary>
        /// Attempt a Void/Refund. Pass proceedUnconfirmed=true only after the UI has warned the player
        /// that authorization isn't confirmed and they chose to continue anyway.
        /// </summary>
        public TxActionResult TryTransaction(ProblemInstance p, int liveIndex, TransType action, bool proceedUnconfirmed)
        {
            var res = new TxActionResult();
            if (liveIndex < 0 || liveIndex >= p.transactions.live.Count) return res;
            var t = p.transactions.live[liveIndex];
            var auth = p.ticket.authorization;

            if (!auth.confirmed)
            {
                if (!proceedUnconfirmed) { res.log = "Authorization not confirmed — action blocked."; return res; }
                if (!auth.callerAuthorized)
                {
                    auth.unauthorizedActionTaken = true;
                    res.unauthorizedHarm = true;
                }
            }

            if (action == TransType.Void && t.status == TransStatus.Open)
            {
                t.status = TransStatus.Voided;
                res.performed = true;
                res.log = $"{t.type} ${t.amount:0.00} voided.";
            }
            else if (action == TransType.Refund && (t.status == TransStatus.Open || t.status == TransStatus.Settled))
            {
                t.status = TransStatus.Refunded;
                res.performed = true;
                res.log = $"{t.type} ${t.amount:0.00} refunded.";
            }
            else
            {
                res.log = $"{action} not valid for a {t.status} transaction.";
            }
            return res;
        }

        public void CloseBatch(ProblemInstance p)
        {
            var tx = p.transactions;
            foreach (var t in tx.live.Where(t => t.status == TransStatus.Open)) t.status = TransStatus.Settled;
            foreach (var t in tx.live) tx.archive.Add(new Transaction { day = "Today", type = t.type, amount = t.amount, status = t.status });
            tx.live.Clear();
            tx.batchId += 1;
        }

        /// <summary>Reprint a receipt from the DB archive — needs a live DB connection (SMS-receipt trick).</summary>
        public bool Reprint(ProblemInstance p, Transaction record, ReceiptType docType, out string reason)
        {
            reason = "";
            var db = p.desktop.graph.DbConnected();
            if (!db.ok) { reason = db.reason; return false; }

            bool pass = p.desktop.graph.RunTest(docType);
            if (!pass)
            {
                var pr = p.desktop.EffectiveStatus(ModuleType.Printer);
                reason = pr.IsOk ? "receipt template misconfigured" : pr.reason;
            }
            record.lastPrintResult = pass ? $"PASS ({docType})" : $"FAIL ({docType}) — {reason}";
            return pass;
        }
    }
}
