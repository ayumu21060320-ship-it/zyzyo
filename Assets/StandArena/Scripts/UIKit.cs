using System;
using UnityEngine;
using UnityEngine.UI;

namespace StandArena
{
    /// <summary>Palette for the whole game.</summary>
    public static class Pal
    {
        public static readonly Color Ink = Hex("110d16");
        public static readonly Color Panel = Hex("1c1623");
        public static readonly Color Deep = Hex("0b0810");
        public static readonly Color Line = Hex("3a3046");
        public static readonly Color Text = Hex("efe8f4");
        public static readonly Color Muted = Hex("a597b3");
        public static readonly Color Gold = Hex("ffc93c");
        public static readonly Color Cyan = Hex("45d9c8");
        public static readonly Color Rose = Hex("ff5d7a");
        public static readonly Color OnGold = Hex("1a1205");

        public static Color Hex(string h)
        {
            ColorUtility.TryParseHtmlString("#" + h, out var c);
            return c;
        }
    }

    /// <summary>Builds uGUI objects from code, so the game needs no scene or prefab setup.</summary>
    public static class UIKit
    {
        static Font font;

        public static Font Font
        {
            get
            {
                if (font == null)
                {
                    // OS fonts that can render Japanese, tried in order (Windows, macOS, Linux).
                    font = Font.CreateDynamicFontFromOSFont(new[]
                    {
                        "Yu Gothic UI", "Yu Gothic", "Meiryo", "MS Gothic",
                        "Hiragino Sans", "Hiragino Kaku Gothic ProN",
                        "Noto Sans CJK JP", "Noto Sans JP", "Arial"
                    }, 32);
                }
                return font;
            }
        }

        public static RectTransform New(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static void Stretch(RectTransform rt, float l = 0, float b = 0, float r = 0, float t = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(l, b);
            rt.offsetMax = new Vector2(-r, -t);
        }

        public static Image Bg(RectTransform rt, Color c, bool border = false)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.color = c;
            if (border)
            {
                var o = rt.gameObject.AddComponent<Outline>();
                o.effectColor = Pal.Line;
                o.effectDistance = new Vector2(2, -2);
            }
            return img;
        }

        public static RectTransform VStack(Transform parent, float spacing = 16, int pad = 0, Color? bg = null,
            TextAnchor align = TextAnchor.UpperLeft)
        {
            var rt = New("VStack", parent);
            if (bg.HasValue) Bg(rt, bg.Value, true);
            var g = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            g.spacing = spacing;
            g.padding = new RectOffset(pad, pad, pad, pad);
            g.childAlignment = align;
            g.childControlWidth = true;
            g.childControlHeight = true;
            g.childForceExpandWidth = true;
            g.childForceExpandHeight = false;
            return rt;
        }

        public static RectTransform HStack(Transform parent, float spacing = 16, bool expand = false,
            TextAnchor align = TextAnchor.MiddleLeft)
        {
            var rt = New("HStack", parent);
            var g = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            g.spacing = spacing;
            g.childAlignment = align;
            g.childControlWidth = true;
            g.childControlHeight = true;
            g.childForceExpandWidth = expand;
            g.childForceExpandHeight = false;
            return rt;
        }

        public static RectTransform Panel(Transform parent, float spacing = 16) =>
            VStack(parent, spacing, 28, Pal.Panel);

        public static Text Label(Transform parent, string text, int size = 26, Color? color = null,
            FontStyle style = FontStyle.Normal, TextAnchor align = TextAnchor.UpperLeft)
        {
            var rt = New("Text", parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Font;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color ?? Pal.Text;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.lineSpacing = 1.15f;
            t.supportRichText = true;
            t.raycastTarget = false;
            t.text = text;
            return t;
        }

        public static Text Eyebrow(Transform parent, string text, Color? color = null) =>
            Label(parent, text, 18, color ?? Pal.Muted, FontStyle.Bold);

        public static Button Btn(Transform parent, string label, Action onClick, bool primary = true,
            int size = 26, TextAnchor align = TextAnchor.MiddleCenter, bool interactable = true)
        {
            var rt = New("Button", parent);
            var img = Bg(rt, primary ? Pal.Gold : Pal.Panel, !primary);
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            var cb = b.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(0.88f, 0.88f, 0.88f);
            cb.selectedColor = Color.white;
            cb.pressedColor = new Color(0.72f, 0.72f, 0.72f);
            cb.disabledColor = new Color(1, 1, 1, 0.35f);
            b.colors = cb;
            var hl = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.padding = new RectOffset(28, 28, 16, 16);
            hl.childAlignment = align;
            hl.childControlWidth = true;
            hl.childControlHeight = true;
            hl.childForceExpandWidth = true;
            hl.childForceExpandHeight = false;
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.minHeight = 72;
            var t = Label(rt, label, size, primary ? Pal.OnGold : Pal.Text, FontStyle.Bold, align);
            if (!interactable) t.color = new Color(t.color.r, t.color.g, t.color.b, 0.4f);
            b.interactable = interactable;
            b.onClick.AddListener(() => onClick?.Invoke());
            return b;
        }

        public static InputField Input(Transform parent, string placeholder, string value = "",
            bool password = false, bool multiline = false, int charLimit = 0)
        {
            var rt = New("Input", parent);
            var img = Bg(rt, Pal.Deep, true);
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.minHeight = multiline ? 170 : 76;

            Text Make(string name, Color col, FontStyle st)
            {
                var trt = New(name, rt);
                Stretch(trt, 20, 12, 20, 12);
                var t = trt.gameObject.AddComponent<Text>();
                t.font = Font;
                t.fontSize = 26;
                t.fontStyle = st;
                t.color = col;
                t.supportRichText = false;
                t.alignment = multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft;
                t.horizontalOverflow = multiline ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
                t.verticalOverflow = VerticalWrapMode.Truncate;
                return t;
            }

            var text = Make("Text", Pal.Text, FontStyle.Normal);
            var ph = Make("Placeholder", Pal.Muted, FontStyle.Italic);
            ph.text = placeholder;

            var f = rt.gameObject.AddComponent<InputField>();
            f.targetGraphic = img;
            f.textComponent = text;
            f.placeholder = ph;
            f.lineType = multiline ? InputField.LineType.MultiLineNewline : InputField.LineType.SingleLine;
            f.contentType = password ? InputField.ContentType.Password : InputField.ContentType.Standard;
            f.characterLimit = charLimit;
            f.caretColor = Pal.Gold;
            f.selectionColor = new Color(1f, 0.79f, 0.24f, 0.35f);
            f.text = value ?? "";
            return f;
        }

        /// <summary>A horizontal bar. Returns the fill so its width can be changed later.</summary>
        public static RectTransform Bar(Transform parent, float fraction, Color fill, float height = 14)
        {
            var rt = New("Bar", parent);
            Bg(rt, Pal.Line);
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;
            le.flexibleWidth = 1;
            var f = New("Fill", rt);
            f.anchorMin = Vector2.zero;
            f.anchorMax = new Vector2(Mathf.Clamp01(fraction), 1);
            f.offsetMin = Vector2.zero;
            f.offsetMax = Vector2.zero;
            Bg(f, fill);
            return f;
        }

        /// <summary>The sigil image, centered, with an optional letter on top.</summary>
        public static RawImage SigilImage(Transform parent, Texture tex, float size, string letter = null)
        {
            var row = HStack(parent, 0, false, TextAnchor.MiddleCenter);
            var rt = New("Sigil", row);
            var img = rt.gameObject.AddComponent<RawImage>();
            img.texture = tex;
            img.raycastTarget = false;
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = size;
            le.preferredHeight = size;
            le.minWidth = size;
            le.minHeight = size;
            if (!string.IsNullOrEmpty(letter))
            {
                var t = Label(rt, letter, Mathf.RoundToInt(size * 0.12f), Pal.Gold, FontStyle.Bold, TextAnchor.MiddleCenter);
                Stretch((RectTransform)t.transform);
                t.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            }
            return img;
        }

        /// <summary>Make a child of a horizontal row share the row's width evenly with its siblings.</summary>
        public static T Flex<T>(T c) where T : Component
        {
            var le = c.GetComponent<LayoutElement>();
            if (le == null) le = c.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = 0;
            le.flexibleWidth = 1;
            return c;
        }

        /// <summary>A full-screen vertical scroll view. Returns (root, content column).</summary>
        public static (RectTransform root, RectTransform content) ScrollScreen(RectTransform canvas, int sidePad)
        {
            var root = New("Screen", canvas);
            Stretch(root);
            Bg(root, Pal.Ink);
            var sr = root.gameObject.AddComponent<ScrollRect>();
            sr.horizontal = false;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 40;

            var vp = New("Viewport", root);
            Stretch(vp);
            vp.gameObject.AddComponent<RectMask2D>();

            var content = New("Content", vp);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.sizeDelta = Vector2.zero;
            content.anchoredPosition = Vector2.zero;
            var g = content.gameObject.AddComponent<VerticalLayoutGroup>();
            g.padding = new RectOffset(sidePad, sidePad, 40, 80);
            g.spacing = 24;
            g.childAlignment = TextAnchor.UpperCenter;
            g.childControlWidth = true;
            g.childControlHeight = true;
            g.childForceExpandWidth = true;
            g.childForceExpandHeight = false;
            var fit = content.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            sr.viewport = vp;
            sr.content = content;
            return (root, content);
        }
    }
}
