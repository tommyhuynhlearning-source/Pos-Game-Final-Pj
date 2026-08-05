using System;
using System.Collections.Generic;
using POSTechSupport.Core;

namespace POSTechSupport.Logic
{
    // ============================================================================
    // Transaction data model (Docs/app.md "Transaction data model"): a transaction
    // lives in TWO places — the live batch (settled money) and the archive/history
    // (lookup/reprint, survives batch close). Ported from the prototype.
    // ============================================================================

    [Serializable]
    public class Transaction
    {
        public TransType type;
        public double amount;
        public TransStatus status;
        public string day;              // archive rows carry a day label ("Today"/"Yesterday"/...)
        public string lastPrintResult;  // e.g. "PASS (Customer Copy)" / "FAIL (...) — reason"
    }

    [Serializable]
    public class Batch
    {
        public int batchId;
        public BatchStatus status = BatchStatus.Open;
        public List<Transaction> transactions = new();
    }

    [Serializable]
    public class PrintJob
    {
        public string doc;     // "Customer Copy — Sale $8.25"
        public string status;  // "Printed" / "Error"
    }

    /// <summary>
    /// Runtime transaction state for a ticket: today's live batch + the persistent archive.
    /// In the prototype these are ticket.transactions and ticket.dbArchive.
    /// </summary>
    public class TransactionState
    {
        public int batchId = 114;
        public List<Transaction> live = new();     // today's live batch (Terminal view)
        public List<Transaction> archive = new();  // persistent DB records (POS ▸ Database)
    }
}
