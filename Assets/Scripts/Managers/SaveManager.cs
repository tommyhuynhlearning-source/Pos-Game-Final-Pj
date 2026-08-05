using System;
using UnityEngine;

namespace POSTechSupport.Managers
{
    /// <summary>
    /// The ONLY thing that touches storage (Docs/manager.md SaveManager). Serializes the persistent
    /// campaign state + consequence ledger to PlayerPrefs (prototype used localStorage with SAVE_KEY).
    /// Mid-night state is intentionally not persisted — quitting mid-shift loses that night.
    /// </summary>
    public class SaveManager
    {
        private const string SaveKey = "pos_tech_support_save_v1";

        [Serializable]
        private class SaveBlob
        {
            public CampaignState campaign;
            public ConsequenceLedger ledger;
        }

        public void Persist(CampaignState state, ConsequenceLedger ledger)
        {
            var blob = new SaveBlob { campaign = state, ledger = ledger };
            PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(blob));
            PlayerPrefs.Save();
        }

        public (CampaignState campaign, ConsequenceLedger ledger)? Load()
        {
            if (!PlayerPrefs.HasKey(SaveKey)) return null;
            try
            {
                var blob = JsonUtility.FromJson<SaveBlob>(PlayerPrefs.GetString(SaveKey));
                if (blob?.campaign == null) return null;
                return (blob.campaign, blob.ledger ?? new ConsequenceLedger());
            }
            catch { return null; }
        }

        public void ResetSave() => PlayerPrefs.DeleteKey(SaveKey);
    }
}
