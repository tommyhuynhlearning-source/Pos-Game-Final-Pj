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

        public void Enqueue(ProblemInstance p, float elapsed = 0f, float patienceSec = float.MaxValue)
        {
            p.ticket.lifecycle = CallLifecycleStatus.Queued;
            p.ticket.queueDeadline = patienceSec >= float.MaxValue ? float.MaxValue : elapsed + patienceSec;
            queue.Add(p);
        }

        /// <summary>
        /// Callers don't wait forever. Who carries the blame depends on whether the one line was busy:
        /// a call was active or ringing → a colleague picks this one up (neutral, no complaint); the
        /// agent was sitting idle → a genuine missed call, one strike.
        ///
        /// The distinction is the whole point at a real call-centre volume: a strike must mean "a call
        /// rang at you and you ignored it" (ring timeout / decline), never "more people phoned than one
        /// person can talk to". Returns how many left the queue, so the UI can refresh.
        /// </summary>
        public int DrainImpatientQueue(float elapsed)
        {
            int left = 0;
            bool lineBusy = active != null || ringing != null;
            for (int i = queue.Count - 1; i >= 0; i--)
            {
                var p = queue[i];
                if (elapsed < p.ticket.queueDeadline) continue;
                queue.RemoveAt(i);
                if (lineBusy) RouteToOtherTech(p);
                else MissQueued(p, "gave up waiting while the line sat idle");
                left++;
            }
            return left;
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
            MissQueued(p, reason);
        }

        /// <summary>A call the player could have taken and didn't — one strike.</summary>
        private void MissQueued(ProblemInstance p, string reason)
        {
            p.ticket.lifecycle = CallLifecycleStatus.Missed;
            mailbox.FileComplaint(HarmType.MissedCall, p.ticket.ticketId,
                $"Missed call from {p.store.storeName} ({reason}) — customer complaint filed.");
            history.Add(p);
        }

        /// <summary>
        /// Someone else's ticket now. Neutral by design: it never happened because the player did
        /// something wrong, so it earns nothing and costs nothing.
        /// </summary>
        private void RouteToOtherTech(ProblemInstance p)
        {
            p.ticket.lifecycle = CallLifecycleStatus.HandledByOtherTech;
            p.ticket.closedOutcome = ClosedOutcome.None;
            history.Add(p);
        }

        /// <summary>Counts by lifecycle over the night's history — for the HUD and the end-of-night card.</summary>
        public int CountBy(CallLifecycleStatus lifecycle)
        {
            int n = 0;
            foreach (var p in history) if (p.ticket.lifecycle == lifecycle) n++;
            return n;
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

        /// <summary>
        /// End-of-night cleanup. A call still ringing or waiting when the clock runs out is NOT a missed
        /// call — the shift is over and the next tech inherits it. Only the call the player was holding
        /// open gets judged, because that one they really did cut off.
        /// </summary>
        public void FlushRemaining()
        {
            if (ringing != null)
            {
                var p = ringing; ringing = null;
                RouteToOtherTech(p);
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
            foreach (var p in queue) RouteToOtherTech(p);
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
