using UnityEngine;

namespace POSTechSupport.Data
{
    /// <summary>
    /// Global tunables (dev-adjustable). Mirrors the prototype's CONFIG object. Note the prototype
    /// uses a shorter default shift (150s) for testing; the GDD's design target is 480s. See
    /// Docs/schema.md §5 and Docs/manager.md (ShiftManager / CampaignManager).
    /// </summary>
    [CreateAssetMenu(menuName = "POS/GameConfig", fileName = "GameConfig")]
    public class GameConfigSO : ScriptableObject
    {
        [Header("Shift timing")]
        public float shiftRealDurationSec = 480f;   // GDD design target (prototype uses 150 for testing)
        public float shiftStartHour = 20f;
        public float shiftEndHour = 4f;
        public float ringTimeoutSec = 12f;

        [Header("Campaign")]
        public int totalDays = 60;
        public int minTotalTickets = 150;
        public int strikesPerNightFail = 3;
        public int warningsToGameOver = 3;

        [Header("Tempo")]
        public AnimationCurve ticketTempoOverNight = AnimationCurve.Linear(0, 0, 1, 1);

        [Header("Call volume — how busy a night is (dev tunable, Inspector only)")]
        [Tooltip("Calls per IN-GAME hour on day 0. The shift is 8 in-game hours (20:00→04:00), so 0.25 " +
                 "means ~2 calls a night. Read by ShiftManager.BeginShift, so an edit takes effect on the " +
                 "next night that starts. Deliberately has no in-game control.")]
        public float callsPerHour = 0.25f;
        [Tooltip("Added to callsPerHour for every day survived. 0.00625 = +0.05 calls per night per day, " +
                 "the GDD ramp (2 calls on day 1 → 5 on day 60).")]
        public float callsPerHourPerDay = 0.00625f;
        [Tooltip("Floor/ceiling on the calls actually spawned in one night, after the per-hour rate is " +
                 "converted. The ceiling is only a typo rail (the day ramp never reaches it) — 0 = no cap.")]
        public int minCallsPerNight = 1;
        public int maxCallsPerNight = 120;

        [Tooltip("Real seconds a queued caller waits before giving up. Leaving the queue while you are on " +
                 "another call is neutral (a colleague takes it); leaving it while you are free is a " +
                 "missed call and files a complaint. Set high to make every call wait for you.")]
        public float queuePatienceSec = 15f;

        /// <summary>
        /// In-game hours in one shift — 20:00→04:00 = 8, wrapping past midnight. Falls back to 8 if the
        /// two hour fields are equal (which would otherwise mean a zero-length night, hence zero calls).
        /// </summary>
        public float ShiftHours
        {
            get
            {
                float h = Mathf.Repeat(shiftEndHour - shiftStartHour, 24f);
                return h > 0.01f ? h : 8f;
            }
        }

        /// <summary>Effective call rate on a given campaign day (base + ramp), never negative.</summary>
        public float CallsPerHourOnDay(int day) => Mathf.Max(0f, callsPerHour + day * callsPerHourPerDay);

        /// <summary>Calls to schedule for one night = rate × shift length, clamped to the min/max above.</summary>
        public int CallsForNight(int day)
        {
            int floor = Mathf.Max(0, minCallsPerNight);
            int raw = Mathf.Max(floor, Mathf.RoundToInt(CallsPerHourOnDay(day) * ShiftHours));
            return maxCallsPerNight > 0 ? Mathf.Min(raw, Mathf.Max(floor, maxCallsPerNight)) : raw;
        }

        [Header("Customer AI (M4)")]
        [Tooltip("Off = template phrasing only (GDD §13 Phương án A). The game is fully playable this way; " +
                 "the model can only reword lines DialoguePolicy already decided, never change them.")]
        public bool useLlm;
        [Tooltip("Self-hosted endpoint — GDD §13 Phương án B. Ollama's default generate route.")]
        public string llmEndpoint = "http://localhost:11434/api/generate";
        [Tooltip("Small English-first instruct model, ~1.5–3B (GDD §13).")]
        public string llmModel = "llama3.2:3b";
        [Tooltip("Give up and keep the template line after this long.")]
        public float llmTimeoutSec = 4f;
    }
}
