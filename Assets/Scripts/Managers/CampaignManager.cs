using System;
using POSTechSupport.Core;
using POSTechSupport.Data;

namespace POSTechSupport.Managers
{
    /// <summary>Persistent state that lives across all 60 days (Docs/manager.md CampaignManager).</summary>
    [Serializable]
    public class CampaignState
    {
        public int day = 1;
        public int ticketsResolved;
        public int warnings;
        public int currency;
    }

    /// <summary>
    /// Owns the only cross-night state. After each night: accumulate resolved/currency, +1 warning on a
    /// failed night, advance the day, then check win/lose. Ported from the prototype's continue/end flow.
    /// (Plain class owned by GameManager — see GameManager for the MonoBehaviour lifecycle.)
    /// </summary>
    public class CampaignManager
    {
        public GameConfigSO config;
        public CampaignState state = new();

        public CampaignManager(GameConfigSO config) { this.config = config; }

        public void StartNewCampaign() => state = new CampaignState();

        /// <summary>Called by ShiftManager at end of night. Returns the win/lose result to act on.</summary>
        public GameResult OnNightEnded(ScoreBreakdown score, bool nightFailed)
        {
            state.ticketsResolved += score.resolvedCount;
            state.currency += Math.Max(0, score.currencyEarned);
            if (nightFailed) state.warnings += 1;

            if (state.warnings > config.warningsToGameOver) return GameResult.Lose;

            state.day += 1;
            if (state.day > config.totalDays)
                return state.ticketsResolved >= config.minTotalTickets ? GameResult.Win : GameResult.Lose;

            return GameResult.None;
        }
    }
}
