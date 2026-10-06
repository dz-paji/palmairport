using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace IslandAirport
{
    /// <summary>
    /// 右下交互大圆钮：按下帧 Pressed=true，按住期间 Held=true，松开复位。
    /// 文案由 AirportHudCanvas 每帧按 AirportGame.ActionLabel 刷新。
    /// </summary>
    public sealed class ActionHoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public bool Held { get; private set; }
        // 一帧有效的“按下”信号；HUD 组帧读取后清零。
        public bool Pressed { get; set; }
        public Text Label { get; private set; }
        Image circle;

        public static ActionHoldButton Create(Transform parent, string name)
        {
            var buttonObject = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)buttonObject.transform;
            rect.SetParent(parent, false);
            rect.sizeDelta = new Vector2(168, 168);
            var image = buttonObject.AddComponent<Image>();
            image.sprite = AirportStyle.DiscSprite;
            image.color = AirportStyle.Baggage;

            var labelObject = new GameObject("Label", typeof(RectTransform));
            var labelRect = (RectTransform)labelObject.transform;
            labelRect.SetParent(rect, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
            var text = labelObject.AddComponent<Text>();
            text.font = AirportStyle.ChineseFont;
            text.fontSize = 34;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.text = "交互";

            var button = buttonObject.AddComponent<ActionHoldButton>();
            button.circle = image;
            button.Label = text;
            return button;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            Held = true; Pressed = true;
            if (circle) circle.color = AirportStyle.Teal;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            Held = false;
            if (circle) circle.color = AirportStyle.Baggage;
        }
    }
}
