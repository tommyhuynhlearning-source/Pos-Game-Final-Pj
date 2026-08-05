using System.Collections.Generic;
using POSTechSupport.Core;
using POSTechSupport.Logic;

namespace POSTechSupport.Managers
{
    /// <summary>
    /// Source of truth for every ticket's CallLifecycleStatus during a night — a simple FIFO line
    /// (queue → ringing → active → history). Distinct from the health verdict (ResolutionChecker).
    /// Files MissedCall/Degraded/Abandoned complaints via MailboxManager. Docs/manager.md TicketManager.
    /// </summary>
    public class TicketManager
    {
        private readonly MailboxManager mailbox;
        public TicketManager(MailboxManager mailbox) { this.mailbox = mailbox; }

        public readonly List<ProblemInstance> queue = new();
        public ProblemInstance ringing;
        public ProblemInstance active;
        public readonly List<ProblemInstance> history = new();

        public void Enqueue(ProblemInstance p)
        {
            p.ticket.lifecycle = CallLifecycleStatus.Queued;
            queue.Add(p);
        }

        /// <summary>Promote the next queued call to ringing when the line is free. Returns it, or null.</summary>
        public ProblemInstance TryPromote(float elapsed, float ringTimeout)
        {
            if (ringing != null || active != null || queue.Count == 0) return null;
            var p = queue[0];
            queue.RemoveAt(0);
            p.ticket.lifecycle = CallLifecycleStatus.Ringing;
            p.ticket.ringDeadline = elapsed + ringTimeout;
            ringing = p;
            return p;
        }

        /// <summary>True (and auto-misses) if the ringing call timed out unanswered.</summary>
        public bool CheckRingTimeout(float elapsed)
        {
            if (ringing == null || elapsed < ringing.ticket.ringDeadline) return false;
            MissRinging("no answer");
            return true;
        }

        public void Answer(float elapsed)
        {
            if (ringing == null) return;
            active = ringing;
            ringing = null;
            active.ticket.lifecycle = CallLifecycleStatus.Active;
            active.ticket.answeredAtElapsed = elapsed;
        }

        public void MissRinging(string reason)
        {
            if (ringing == null) return;
            var p = ringing;
            ringing = null;
            p.ticket.lifecycle = CallLifecycleStatus.Missed;
            mailbox.FileComplaint(HarmType.MissedCall, p.ticket.ticketId,
                $"Missed call from {p.store.storeName} ({reason}) — customer complaint filed.");
            history.Add(p);
        }

        /// <summary>
        /// Close the active call, computing its outcome. The UI is expected to have confirmed an
        /// unresolved hang-up first (abandoned). Returns the final outcome.
        /// </summary>
        public ClosedOutcome CloseActive()
        {
            if (active == null) return ClosedOutcome.None;
            var p = active;
            ClosedOutcome outcome;

            if (p.ticket.authorization.customerHungUp)
            {
                // Correctly refusing an unverified caller: no strike, no resolved credit.
                outcome = ClosedOutcome.Unauthorized;
            }
            else
            {
                var verdict = ResolutionChecker.EvaluateTicket(p);
                if (verdict == TicketStatus.Resolved) outcome = ClosedOutcome.Resolved;
                else if (verdict == TicketStatus.Degraded)
                {
                    outcome = ClosedOutcome.Degraded;
                    FileDegradedComplaint(p);
                }
                else
                {
                    outcome = ClosedOutcome.None; // abandoned mid-call
                    mailbox.FileComplaint(HarmType.AbandonedCall, p.ticket.ticketId,
                        $"Ticket {p.ticket.ticketId} was abandoned mid-call — customer complaint filed.");
                }
            }

            FinalizeClose(p, outcome, outcome == ClosedOutcome.None ? CallLifecycleStatus.Abandoned : CallLifecycleStatus.Closed);
            active = null;
            return outcome;
        }

        /// <summary>End-of-night cleanup: close ringing/active/queue that were left dangling.</summary>
        public void FlushRemaining()
        {
            if (ringing != null)
            {
                var p = ringing; ringing = null;
                p.ticket.lifecycle = CallLifecycleStatus.Missed;
                mailbox.FileComplaint(HarmType.MissedCall, p.ticket.ticketId,
                    $"Missed call from {p.store.storeName} (shift ended before it was answered).");
                history.Add(p);
            }
            if (active != null)
            {
                var p = active;
                var verdict = ResolutionChecker.EvaluateTicket(p);
                if (verdict == TicketStatus.Resolved) FinalizeClose(p, ClosedOutcome.Resolved, CallLifecycleStatus.Closed);
                else if (verdict == TicketStatus.Degraded)
                {
                    FileDegradedComplaint(p);
                    FinalizeClose(p, ClosedOutcome.Degraded, CallLifecycleStatus.Closed);
                }
                else
                {
                    mailbox.FileComplaint(HarmType.AbandonedCall, p.ticket.ticketId,
                        $"Call with {p.store.storeName} was cut off by end of shift — customer complaint filed.");
                    FinalizeClose(p, ClosedOutcome.None, CallLifecycleStatus.Abandoned);
                }
                active = null;
            }
            foreach (var p in queue)
            {
                p.ticket.lifecycle = CallLifecycleStatus.Missed;
                mailbox.FileComplaint(HarmType.MissedCall, p.ticket.ticketId,
                    $"Missed call from {p.store.storeName} (never reached before shift ended).");
                history.Add(p);
            }
            queue.Clear();
        }

        public void ResetNight()
        {
            queue.Clear();
            ringing = active = null;
            history.Clear();
        }

        /// <summary>
        /// One strike per degraded ticket, but named for what actually went wrong: an unverified
        /// Refund/Void is a business harm (Docs/app.md Caller Authorization), not a botched repair.
        /// </summary>
        private void FileDegradedComplaint(ProblemInstance p)
        {
            bool unauthorized = p.ticket.authorization.unauthorizedActionTaken;
            mailbox.FileComplaint(
                unauthorized ? HarmType.UnauthorizedTransaction : HarmType.DegradedTicket,
                p.ticket.ticketId,
                unauthorized
                    ? $"Ticket {p.ticket.ticketId}: a Refund/Void was processed for a caller who was never authorized."
                    : $"Ticket {p.ticket.ticketId} closed degraded — customer complaint filed.");
        }

        private void FinalizeClose(ProblemInstance p, ClosedOutcome outcome, CallLifecycleStatus lifecycle)
        {
            p.ticket.closedOutcome = outcome;
            p.ticket.lifecycle = lifecycle;
            p.ticket.verdict = ResolutionChecker.EvaluateTicket(p);
            p.ticket.openAppKey = null;
            history.Add(p);
        }
    }
}
