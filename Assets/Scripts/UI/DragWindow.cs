using UnityEngine;
using UnityEngine.EventSystems;

namespace POSTechSupport.UI
{
    /// <summary>
    /// Put on an XP title bar: dragging it moves <see cref="target"/> (the window frame) and clamps it so
    /// the caption can never be pushed off the desktop and left ungrabbable. Pointer-down also raises the
    /// window above its siblings, which is what makes clicking a partly covered window feel like Windows.
    ///
    /// Title-bar buttons sit on top of this and handle their own clicks — Button does not implement
    /// IDragHandler, so a drag started on the close box still bubbles up here.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class DragWindow : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler
    {
        public RectTransform target;

        private RectTransform Bounds => target != null ? target.parent as RectTransform : null;
        private Vector2 grabOffset;

        public void OnPointerDown(PointerEventData e)
        {
            if (target != null) target.SetAsLastSibling();
        }

        public void OnBeginDrag(PointerEventData e)
        {
            if (!LocalPoint(e, out var p)) return;
            grabOffset = p - target.anchoredPosition;
        }

        public void OnDrag(PointerEventData e)
        {
            if (!LocalPoint(e, out var p)) return;
            target.anchoredPosition = Clamp(p - grabOffset);
        }

        private bool LocalPoint(PointerEventData e, out Vector2 local)
        {
            local = Vector2.zero;
            var bounds = Bounds;
            if (target == null || bounds == null) return false;
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(bounds, e.position, e.pressEventCamera, out local);
        }

        /// <summary>
        /// Keeps at least the title bar on screen: the window may hang off any edge, but never far enough
        /// that the bar it is dragged by leaves the desktop.
        /// </summary>
        private Vector2 Clamp(Vector2 pos)
        {
            var bounds = Bounds;
            if (bounds == null) return pos;

            var size = target.rect.size;
            var area = bounds.rect.size;
            // Anchors are a single point, so anchoredPosition is measured from that point on the parent.
            var anchor = new Vector2(target.anchorMin.x * area.x, target.anchorMin.y * area.y);
            var pivot = target.pivot;

            float left = anchor.x + pos.x - size.x * pivot.x;
            float bottom = anchor.y + pos.y - size.y * pivot.y;

            const float keepVisible = 80f;                        // horizontal grab margin
            float minLeft = -size.x + keepVisible;
            float maxLeft = area.x - keepVisible;
            float minBottom = -size.y + XPFactory.TaskbarHeight + XPFactory.TitleBarHeight;
            float maxBottom = area.y - size.y;                    // never above the desktop's top edge

            left = Mathf.Clamp(left, minLeft, maxLeft);
            bottom = Mathf.Clamp(bottom, minBottom, Mathf.Max(minBottom, maxBottom));

            return new Vector2(left + size.x * pivot.x - anchor.x, bottom + size.y * pivot.y - anchor.y);
        }
    }
}
