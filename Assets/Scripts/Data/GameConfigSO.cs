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
