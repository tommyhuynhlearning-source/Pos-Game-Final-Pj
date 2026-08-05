using UnityEngine;
using POSTechSupport.Core;

namespace POSTechSupport.Data
{
    /// <summary>
    /// The field layout of a receipt type. Used to render receipt previews and to detect a
    /// broken template (e.g. P5: customer copy missing the total field). See Docs/schema.md §5.
    /// </summary>
    [CreateAssetMenu(menuName = "POS/ReceiptTemplate", fileName = "Receipt_")]
    public class ReceiptTemplateSO : ScriptableObject
    {
        public ReceiptType type;
        public ReceiptField[] fields;
    }
}
