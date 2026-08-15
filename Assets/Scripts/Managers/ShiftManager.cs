using System;
using System.Collections.Generic;
using UnityEngine;
using POSTechSupport.Data;
using POSTechSupport.Logic;

namespace POSTechSupport.Managers
{
    /// <summary>Per-night runtime clock/tempo state (reset every night, unlike CampaignState).</summary>
    public class NightState
    {
        public int day;
        public int ticketsTarget;
        public List<float> spawnTimes = new();
        public int spawnedCount;
        public float elapsed;
        public bool ended;
    }

    /// <summary>
    /// Converts real seconds into in-game night time (20:00→04:00) and drives ticket tempo
    /// (Docs/manager.md ShiftManager). Ported from the prototype's startNight / tickNight / endNight
    /// (night portion only — CampaignManager handles the cross-night advance).
    /// </summary>
    public class ShiftManager
    {
        private readonly GameConfigSO config;
        private readonly ProblemGenerator generator;
        private readonly TicketManager tickets;
        private readonly MailboxManager mailbox;
        private readonly ScoreManager scorer;

        public NightState night;

        /// <summary>Fired when a queued call starts ringing (UI shows the incoming-call popup).</summary>
        public event Action<ProblemInstance> OnIncomingCall;
        /// <summary>Fired once at end of shift with the night's score + whether it failed.</summary>
        public event Action<ScoreBreakdown, bool> OnNightEnded;

        public ShiftManager(GameConfigSO config, ProblemGenerator generator, TicketManager tickets,
                            MailboxManager mailbox, ScoreManager scorer)
        {
            this.config = config;
            this.generator = generator;
            this.tickets = tickets;
            this.mailbox = mailbox;
            this.scorer = scorer;
        }

        public void BeginShift(int day)
        {
            mailbox.ResetNight();
            tickets.ResetNight();

            int target = ProblemGenerator.TicketCountForDay(day, config);
            var fractions = new List<float>();
            for (int i = 0; i < target; i++) fractions.Add(SampleTempo());
            fractions.Sort();

            night = new NightState { day = day, ticketsTarget = target };
            foreach (var f in fractions) night.spawnTimes.Add(f * config.shiftRealDurationSec);
        }

        /// <summary>
        /// One spawn moment as a 0..1 fraction of the shift, shaped by
        /// <see cref="GameConfigSO.ticketTempoOverNight"/> (GDD: calls bunch up toward dawn). The curve
        /// maps a uniform roll to a shift position, so an ease-in curve pushes calls later. A flat/absent
        /// curve falls back to the prototype's pow(rand, 1.4) skew.
        /// </summary>
        private float SampleTempo()
        {
            float u = UnityEngine.Random.value;
            var curve = config != null ? config.ticketTempoOverNight : null;
            if (curve == null || curve.length < 2) return Mathf.Pow(u, 1.4f);
            return Mathf.Clamp01(curve.Evaluate(u));
        }

        public void Tick(float deltaTime)
        {
            if (night == null || night.ended) return;
            night.elapsed += deltaTime;

            while (night.spawnedCount < night.spawnTimes.Count &&
                   night.elapsed >= night.spawnTimes[night.spawnedCount])
            {
                tickets.Enqueue(generator.GenerateAuto(night.day), night.elapsed, config.queuePatienceSec);
                night.spawnedCount++;
            }

            // Before promoting: callers who ran out of patience leave the line (another tech takes them
            // if the agent is busy, otherwise it is a real missed call). At a high calls-per-hour this is
            // what keeps the queue from growing forever.
            tickets.DrainImpatientQueue(night.elapsed);

            var newlyRinging = tickets.TryPromote(night.elapsed, config.ringTimeoutSec);
            if (newlyRinging != null) OnIncomingCall?.Invoke(newlyRinging);

            tickets.CheckRingTimeout(night.elapsed);

            if (night.elapsed >= config.shiftRealDurationSec) EndShift();
        }

        public void EndShift()
        {
            if (night == null || night.ended) return;
            night.ended = true;

            tickets.FlushRemaining();
            var score = scorer.Compute(tickets.history);
            bool nightFailed = mailbox.NightFailed(config);
            OnNightEnded?.Invoke(score, nightFailed);
        }

        /// <summary>Remaining ring time as a 0..1 fraction, for the incoming-call countdown bar.</summary>
        public float RingFractionLeft()
        {
            if (tickets.ringing == null) return 0f;
            float remaining = tickets.ringing.ticket.ringDeadline - night.elapsed;
            return Mathf.Clamp01(remaining / config.ringTimeoutSec);
        }

        public float NightProgress() => night == null ? 0f : Mathf.Clamp01(night.elapsed / config.shiftRealDurationSec);

        public string ClockLabel()
        {
            float frac = night == null ? 0f : night.elapsed / config.shiftRealDurationSec;
            float hour24 = 20f + frac * 8f;
            float wrapped = hour24 % 24f;
            int h = Mathf.FloorToInt(wrapped);
            int m = Mathf.FloorToInt((wrapped - h) * 60f);
            string ampm = h >= 12 ? "PM" : "AM";
            int h12 = h % 12; if (h12 == 0) h12 = 12;
            return $"{h12}:{m:00} {ampm}";
        }
    }
}
