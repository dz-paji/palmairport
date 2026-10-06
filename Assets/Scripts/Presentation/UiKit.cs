using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace IslandAirport
{
    /// <summary>
    /// 程序化 uGUI 构件库。基础九件（Node/Panel/Label/LabelAt/RoundButton/Bar/SetBar/
    /// EnsureEventSystem）自 AirportHudCanvas 原样抽出，签名与样式语义不变；
    /// 大厅新构件（TextField/Swatch/Chip/RoomRow/DobPicker）为 M2 增量，步骤 5 的
    /// LobbyCanvas 使用。不引入 TMP；中文字体一律走 AirportStyle.ChineseFont。
    /// 约定：Panel/LabelAt/RoundButton/Bar 及派生构件的 pos.y 一律为「自锚点向下」的像素数。
    /// </summary>
    public static class UiKit
    {
        // ---------- 通用构件 ----------
        public static RectTransform Node(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var node = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)node.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin; rect.anchorMax = anchorMax; rect.pivot = pivot;
            rect.anchoredPosition = pos; rect.sizeDelta = size;
            return rect;
        }

        public static Image Panel(RectTransform parent, string name, Color color, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size, bool raycast = true)
        {
            // 约定：pos.y 一律为「自锚点向下」的像素数（uGUI 原生为向上，这里取反统一）。
            pos.y = -pos.y;
            var rect = Node(parent, name, anchorMin, anchorMax, pivot, pos, size);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = AirportStyle.RoundedSprite;
            image.type = Image.Type.Sliced;
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        public static Text Label(RectTransform parent, string name, string content, int size, Color color, FontStyle style = FontStyle.Normal, TextAnchor align = TextAnchor.MiddleLeft, bool raycast = false)
        {
            var rect = Node(parent, name, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var text = rect.gameObject.AddComponent<Text>();
            text.font = AirportStyle.ChineseFont;
            text.text = content;
            text.fontSize = size;
            // Dynamic CJK fonts synthesize bold by expanding every glyph. At HUD sizes
            // this closes small counters; size and colour provide the visual hierarchy.
            text.fontStyle = style == FontStyle.Bold ? FontStyle.Normal : style;
            text.lineSpacing = 1.12f;
            text.alignment = align;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = raycast;
            return text;
        }

        public static Text LabelAt(RectTransform parent, string name, string content, Vector2 pos, Vector2 size, int fontSize, Color color, FontStyle style = FontStyle.Normal, TextAnchor align = TextAnchor.MiddleLeft)
        {
            pos.y = -pos.y; // 与 Panel 相同的 y 向下约定
            var host = Node(parent, name, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), pos, size);
            return Label(host, "Text", content, fontSize, color, style, align);
        }

        public static Button RoundButton(RectTransform parent, string name, string label, Color color, Vector2 pos, Vector2 size, UnityEngine.Events.UnityAction onClick, int fontSize = 24)
        {
            var image = Panel(parent, name, color, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), pos, size);
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);
            StyleSelectable(button);
            Label(image.transform as RectTransform, "Label", label, fontSize, Color.white, FontStyle.Bold, TextAnchor.MiddleCenter);
            return button;
        }

        /// <summary>Shared, material-free card finish; apply only to major surfaces.</summary>
        public static void Elevate(Image image, bool dark = false)
        {
            var shadow = image.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0.06f, 0.17f, 0.20f, dark ? 0.20f : 0.13f);
            shadow.effectDistance = new Vector2(0, -4);
            shadow.useGraphicAlpha = true;
        }

        public static void StyleSelectable(Selectable selectable)
        {
            var colors = selectable.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.94f, 0.98f, 0.97f, 1);
            colors.pressedColor = new Color(0.80f, 0.89f, 0.86f, 1);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.80f, 0.83f, 0.82f, 0.90f);
            colors.fadeDuration = 0.12f;
            selectable.colors = colors;
        }

        /// <summary>Inset rounded decoration; never intercepts an input target.</summary>
        public static Image Inset(RectTransform parent, string name, Color color, Vector2 min, Vector2 max)
        {
            var image = Panel(parent, name, color, Vector2.zero, Vector2.one,
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, false);
            image.rectTransform.offsetMin = min;
            image.rectTransform.offsetMax = -max;
            return image;
        }

        public static Image Bar(RectTransform parent, string name, Vector2 pos, Vector2 size, Color track, out Image fill)
        {
            var bg = Panel(parent, name + " Track", track, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), pos, size, false);
            var fillRect = Node(bg.transform as RectTransform, name + " Fill", new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, size);
            fill = fillRect.gameObject.AddComponent<Image>();
            fill.sprite = AirportStyle.RoundedSprite;
            fill.type = Image.Type.Sliced;
            fill.raycastTarget = false;
            return bg;
        }

        public static void SetBar(Image fill, float width, float height, float value, Color color)
        {
            value = Mathf.Clamp01(value);
            fill.rectTransform.sizeDelta = new Vector2(width * value, height);
            fill.color = color;
            fill.enabled = value > 0.001f;
        }

        public static void EnsureEventSystem()
        {
            if (Object.FindObjectOfType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        // ---------- 大厅新构件（M2 步骤 4 纯增量，步骤 5 的 LobbyCanvas 使用） ----------

        /// <summary>
        /// 单行输入框：白底圆角 + 占位灰字 + 彩色 caret；password=true 走密码掩码。
        /// 返回 InputField，调用方读 field.text。
        /// </summary>
        public static InputField TextField(RectTransform parent, string name, Vector2 pos, Vector2 size, string placeholder, UnityEngine.Events.UnityAction<string> onChanged = null, bool password = false, int fontSize = 22)
        {
            var bg = Panel(parent, name, Color.white, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), pos, size);
            var host = (RectTransform)bg.transform;

            var placeholderText = Label(host, "Placeholder", placeholder, fontSize, AirportStyle.Muted, FontStyle.Normal, TextAnchor.MiddleLeft);
            placeholderText.supportRichText = false;
            var placeholderRect = (RectTransform)placeholderText.transform;
            placeholderRect.offsetMin = new Vector2(18, 0);
            placeholderRect.offsetMax = new Vector2(-18, 0);

            var text = Label(host, "Text", "", fontSize, AirportStyle.Ink, FontStyle.Normal, TextAnchor.MiddleLeft);
            text.supportRichText = false;
            var textRect = (RectTransform)text.transform;
            textRect.offsetMin = new Vector2(18, 0);
            textRect.offsetMax = new Vector2(-18, 0);

            var field = bg.gameObject.AddComponent<InputField>();
            field.textComponent = text;
            field.placeholder = placeholderText;
            field.contentType = password ? InputField.ContentType.Password : InputField.ContentType.Standard;
            field.customCaretColor = true;
            field.caretColor = AirportStyle.Teal;
            field.selectionColor = new Color(0.16f, 0.62f, 0.56f, 0.35f);
            field.targetGraphic = bg;
            StyleSelectable(field);
            field.caretWidth = 2;
            if (onChanged != null) field.onValueChanged.AddListener(onChanged);
            return field;
        }

        /// <summary>圆形色板钮（衣柜换色用）：DiscSprite 纯色圆点，点击回调由调用方给。</summary>
        public static Button Swatch(RectTransform parent, string name, Color color, Vector2 pos, float diameter, UnityEngine.Events.UnityAction onClick)
        {
            var rect = Node(parent, name, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(pos.x, -pos.y), new Vector2(diameter, diameter));
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = AirportStyle.DiscSprite;
            image.color = color;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            StyleSelectable(button);
            // A paper inset and a coloured centre form a clean, inexpensive selection rim.
            var ring = Inset(rect, "Selection rim", AirportStyle.Paper, new Vector2(2, 2), new Vector2(2, 2));
            ring.sprite = AirportStyle.DiscSprite;
            ring.type = Image.Type.Simple;
            var center = Inset(rect, "Colour centre", color, new Vector2(4, 4), new Vector2(4, 4));
            center.sprite = AirportStyle.DiscSprite;
            center.type = Image.Type.Simple;
            ring.gameObject.SetActive(false);
            center.gameObject.SetActive(false);
            if (onClick != null) button.onClick.AddListener(onClick);
            return button;
        }

        /// <summary>小胶囊钮（岗位/标签选择）：纸色底 + 墨色小字；color 可换底色。</summary>
        public static Button Chip(RectTransform parent, string name, string label, Vector2 pos, Vector2 size, UnityEngine.Events.UnityAction onClick, Color? color = null)
        {
            var button = RoundButton(parent, name, label, color ?? AirportStyle.Paper, pos, size, onClick, 18);
            button.GetComponentInChildren<Text>().color = AirportStyle.Ink;
            return button;
        }

        /// <summary>房间列表一行的可交互部件。</summary>
        public sealed class RoomRowParts
        {
            public RectTransform Root;
            public Text Title;
            public Text Meta;
            public Button Join;
            public Text JoinLabel;
        }

        /// <summary>
        /// 局域网房间行：房名 + 房主/席位状态 + 右侧加入钮。
        /// 灰态/文案由调用方改 Join.interactable 与 JoinLabel.text。
        /// </summary>
        public static RoomRowParts RoomRow(RectTransform parent, string name, Vector2 pos, Vector2 size, UnityEngine.Events.UnityAction onJoin)
        {
            var bg = Panel(parent, name, Color.white, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), pos, size, false);
            var host = (RectTransform)bg.transform;
            var title = LabelAt(host, "Title", "", new Vector2(20, 10), new Vector2(size.x - 160, 28), 20, AirportStyle.Ink, FontStyle.Bold);
            var meta = LabelAt(host, "Meta", "", new Vector2(20, 40), new Vector2(size.x - 160, 22), 15, AirportStyle.Muted);
            var join = RoundButton(host, "Join", "加入", AirportStyle.Teal, new Vector2(size.x - 116, (size.y - 44) * 0.5f), new Vector2(96, 44), onJoin, 18);
            return new RoomRowParts { Root = host, Title = title, Meta = meta, Join = join, JoinLabel = join.GetComponentInChildren<Text>() };
        }

        /// <summary>出生日期三下拉的可交互部件。</summary>
        public sealed class DobPickerParts
        {
            public RectTransform Root;
            public Dropdown Year;
            public Dropdown Month;
            public Dropdown Day;
        }

        /// <summary>
        /// 出生日期三下拉（年/月/日）。年范围闭区间 [minYear, maxYear]，降序排列（近年在前）；
        /// 月 1–12、日 1–31。选中值读 parts.Year.options[parts.Year.value].text。
        /// </summary>
        public static DobPickerParts DobPicker(RectTransform parent, string name, Vector2 pos, Vector2 size, int minYear, int maxYear)
        {
            var root = Node(parent, name, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(pos.x, -pos.y), size);
            float gap = 12f;
            float cell = (size.x - gap * 2f) / 3f;

            var year = DropdownBox(root, "Year", new Vector2(0, 0), new Vector2(cell, size.y));
            var month = DropdownBox(root, "Month", new Vector2(cell + gap, 0), new Vector2(cell, size.y));
            var day = DropdownBox(root, "Day", new Vector2((cell + gap) * 2f, 0), new Vector2(cell, size.y));

            var years = new System.Collections.Generic.List<Dropdown.OptionData>();
            years.Add(new Dropdown.OptionData("选择年份"));
            for (int y = maxYear; y >= minYear; y--) years.Add(new Dropdown.OptionData(y + " 年"));
            year.options = years;
            var months = new System.Collections.Generic.List<Dropdown.OptionData>();
            months.Add(new Dropdown.OptionData("选择月份"));
            for (int m = 1; m <= 12; m++) months.Add(new Dropdown.OptionData(m + " 月"));
            month.options = months;
            var days = new System.Collections.Generic.List<Dropdown.OptionData>();
            days.Add(new Dropdown.OptionData("选择日期"));
            for (int d = 1; d <= 31; d++) days.Add(new Dropdown.OptionData(d + " 日"));
            day.options = days;

            year.RefreshShownValue();
            month.RefreshShownValue();
            day.RefreshShownValue();

            return new DobPickerParts { Root = root, Year = year, Month = month, Day = day };
        }

        /// <summary>
        /// 单个下拉框：白底圆角钮 + 居中 caption + 标准 Template/Viewport/Content/Item 层级
        /// （uGUI Dropdown.SetupTemplate 要求模板内含 Toggle 项，itemText 挂在其子级）。
        /// </summary>
        static Dropdown DropdownBox(RectTransform parent, string name, Vector2 pos, Vector2 size)
        {
            var bg = Panel(parent, name, Color.white, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), pos, size);
            var host = (RectTransform)bg.transform;
            var caption = Label(host, "Label", "", 18, AirportStyle.Ink, FontStyle.Normal, TextAnchor.MiddleCenter);

            // 下拉模板（默认隐藏，Dropdown.Show 时实例化）。
            var template = Node(host, "Template", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 1), new Vector2(0, -2), new Vector2(0, 220));
            var templateBg = template.gameObject.AddComponent<Image>();
            templateBg.sprite = AirportStyle.RoundedSprite;
            templateBg.type = Image.Type.Sliced;
            templateBg.color = Color.white;
            var scroll = template.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            var viewport = Node(template, "Viewport", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            viewport.offsetMin = viewport.offsetMax = Vector2.zero;
            var viewportImage = viewport.gameObject.AddComponent<Image>();
            viewportImage.color = Color.white;
            var mask = viewport.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            var content = Node(viewport, "Content", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(0, 36));
            scroll.viewport = viewport;
            scroll.content = content;

            var item = Node(content, "Item", new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0, 36));
            var itemBgImage = item.gameObject.AddComponent<Image>();
            itemBgImage.color = AirportStyle.Paper;
            var toggle = item.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = itemBgImage;
            var check = Node(item, "Item Checkmark", new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-18, 0), new Vector2(20, 20));
            var checkImage = check.gameObject.AddComponent<Image>();
            checkImage.sprite = AirportStyle.DiscSprite;
            checkImage.color = AirportStyle.Teal;
            toggle.graphic = checkImage;
            var itemLabel = Label(item, "Item Label", "", 18, AirportStyle.Ink, FontStyle.Normal, TextAnchor.MiddleLeft);
            var itemLabelRect = (RectTransform)itemLabel.transform;
            itemLabelRect.offsetMin = new Vector2(14, 0);
            itemLabelRect.offsetMax = new Vector2(-44, 0);

            template.gameObject.SetActive(false);

            var dropdown = bg.gameObject.AddComponent<Dropdown>();
            dropdown.targetGraphic = bg;
            dropdown.template = template;
            dropdown.captionText = caption;
            dropdown.itemText = itemLabel;
            StyleSelectable(dropdown);
            return dropdown;
        }
    }
}
