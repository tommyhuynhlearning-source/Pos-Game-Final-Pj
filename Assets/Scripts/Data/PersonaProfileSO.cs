using UnityEngine;

namespace POSTechSupport.Data
{
    /// <summary>
    /// A reusable, static customer personality TEMPLATE (traits only). The per-ticket runtime
    /// instance (role/name/stated*) lives in PersonaInstance, never here (nguyên tắc bất biến #6).
    /// techLiteracy is capped low (&lt;= 0.7) so customers stay non-technical. See Docs/schema.md §5.
    /// </summary>
    [CreateAssetMenu(menuName = "POS/Persona", fileName = "Persona_")]
    public class PersonaProfileSO : ScriptableObject
    {
        public string personaId;
        public string displayName;

        [Range(0, 1)] public float techLiteracy;      // keep &lt;= 0.7 for regular customers
        [Range(0, 1)] public float cooperativeness;
        [Range(0, 1)] public float memoryAccuracy;    // drives stated* being wrong → player must verify
        [Range(0, 1)] public float emotionalState;
        [Range(0, 1)] public float honesty;           // drives SMS receipt trick (wrong receipt)

        public MisnameEntry[] misnaming;
        public string[] laymanVocabulary;
    }
}
