using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace IslandAirport
{
    /// <summary>
    /// 左下虚拟摇杆：按下/拖动输出归一化方向（死区 0.15），松开归零。
    /// 纯程序化 UI，键盘输入在 AirportGame.ReadInput 里与本输出按玩家合并。
    /// </summary>
    public sealed class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public Vector2 Value { get; private set; }
        RectTransform handle;
        float radius = 110.0f;

        public static VirtualJoystick Create(Transform parent, string name)
        {
            var baseObject = new GameObject(name, typeof(RectTransform));
            var baseRect = (RectTransform)baseObject.transform;
            baseRect.SetParent(parent, false);
            baseRect.sizeDelta = new Vector2(240, 240);
            var baseImage = baseObject.AddComponent<Image>();
            baseImage.sprite = AirportStyle.DiscSprite;
            baseImage.color = new Color(1, 1, 1, 0.28f);

            var handleObject = new GameObject("Handle", typeof(RectTransform));
            var handleRect = (RectTransform)handleObject.transform;
            handleRect.SetParent(baseRect, false);
            handleRect.anchorMin = handleRect.anchorMax = new Vector2(0.5f, 0.5f);
            handleRect.sizeDelta = new Vector2(104, 104);
            var handleImage = handleObject.AddComponent<Image>();
            handleImage.sprite = AirportStyle.DiscSprite;
            handleImage.color = new Color(1, 1, 1, 0.85f);

            var joystick = baseObject.AddComponent<VirtualJoystick>();
            joystick.handle = handleRect;
            joystick.radius = baseRect.sizeDelta.x * 0.5f;
            return joystick;
        }

        public void OnPointerDown(PointerEventData eventData) { Drag(eventData); }
        public void OnDrag(PointerEventData eventData) { Drag(eventData); }
        public void OnPointerUp(PointerEventData eventData)
        {
            Value = Vector2.zero;
            if (handle) handle.anchoredPosition = Vector2.zero;
        }

        void Drag(PointerEventData eventData)
        {
            Vector2 local;
            var rect = (RectTransform)transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, eventData.position, eventData.pressEventCamera, out local)) return;
            // local 原点随 pivot 走：底座 pivot 在 (0,0) 时圆心位于 (w/2,h/2)，
            // 不扣除会把圆心映射成 (0.7,0.7) 的恒定右上满幅偏移，左下象限永远输出不了负值。
            local -= new Vector2((0.5f - rect.pivot.x) * rect.rect.width, (0.5f - rect.pivot.y) * rect.rect.height);
            Vector2 clamped = Vector2.ClampMagnitude(local, radius);
            Vector2 normalized = clamped / radius;
            Value = normalized.magnitude < 0.15f ? Vector2.zero : normalized;
            if (handle) handle.anchoredPosition = clamped * 0.55f;
        }
    }
}
