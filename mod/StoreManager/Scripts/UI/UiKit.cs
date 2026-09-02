#nullable enable
using System;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StoreManager.UI
{
    /// <summary>
    /// Runtime-uGUI builders for the HQ BizMan tab. No prefab/bundle dependency. Font comes from a
    /// donor vanilla <see cref="TMP_Text"/> (a cloned menu button); the colour palette is read from
    /// the game's own <c>Colors</c> singleton so the tab matches Purchasing Agents / HR / Logistics.
    /// </summary>
    public static class UiKit
    {
        /// <summary>The HQ canvas is ~2× a 1080p reference, so logical sizes are scaled up to match vanilla text.</summary>
        public static float Scale = 2.0f;
        private static float S(float v) => v * Scale;

        public static TMP_FontAsset? Font;

        // palette — sensible defaults, overwritten from the game's Colors singleton in AdoptStyleFrom
        public static Color TextColor = new(0.94f, 0.95f, 0.97f);
        public static Color MutedColor = new(0.78f, 0.80f, 0.84f);
        public static Color HeaderColor = new(1f, 1f, 1f);
        public static Color AccentColor = new(0.16f, 0.53f, 0.92f);   // vanilla blue
        public static Color DisabledColor = new(1f, 1f, 1f, 0.10f);
        public static Color RowColor = new(1f, 1f, 1f, 0.05f);
        public static Color RuleColor = new(1f, 1f, 1f, 0.16f);

        public static void AdoptStyleFrom(TMP_Text? donor)
        {
            if (donor != null)
            {
                try { if (donor.font != null) Font = donor.font; } catch { }
            }
            if (Font == null) { try { Font = TMP_Settings.defaultFontAsset; } catch { } }

            // pull the game's own colours (global type `Colors`, static Color32 props)
            TryColor("White", ref HeaderColor);
            TryColor("LightGrey", ref MutedColor);
            TryColor("Blue", ref AccentColor);
            var text = TextColor; TryColor("White", ref text); TextColor = text;
        }

        private static void TryColor(string prop, ref Color into)
        {
            try
            {
                var t = Type.GetType("Colors, BigAmbitions") ?? FindGlobal("Colors");
                var p = t?.GetProperty(prop, BindingFlags.Public | BindingFlags.Static);
                if (p?.GetValue(null) is Color32 c) into = c;
            }
            catch { }
        }

        private static Type? FindGlobal(string name)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { var t = a.GetType(name); if (t != null) return t; } catch { }
            }
            return null;
        }

        // ── structure ───────────────────────────────────────────────────────────
        public static RectTransform Rect(GameObject go) =>
            go.GetComponent<RectTransform>() ?? go.AddComponent<RectTransform>();

        public static GameObject Container(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            Rect(go).SetParent(parent, false);
            return go;
        }

        /// <summary>Vertical column inset from its parent (clears the menu bar + the info card).</summary>
        public static RectTransform Column(Transform parent, float left = 560f, float top = 30f, float right = 48f, float spacing = 10f)
        {
            var frame = Container("SM_Panel", parent);
            var frt = Rect(frame);
            frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
            frt.offsetMin = new Vector2(left, 40f);
            frt.offsetMax = new Vector2(-right, -top);
            frt.localScale = Vector3.one;

            var col = Container("Col", frame.transform);
            var crt = Rect(col);
            crt.anchorMin = new Vector2(0, 1); crt.anchorMax = new Vector2(1, 1); crt.pivot = new Vector2(0.5f, 1f);
            crt.offsetMin = Vector2.zero; crt.offsetMax = Vector2.zero;
            crt.localScale = Vector3.one;

            var vlg = col.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = S(spacing);
            vlg.childControlWidth = true; vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
            vlg.childAlignment = TextAnchor.UpperLeft;
            col.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return crt;
        }

        public static TextMeshProUGUI Label(Transform parent, string text, float size = 15f, Color? color = null, FontStyles style = FontStyles.Normal)
        {
            var go = Container("Label", parent);
            var t = go.AddComponent<TextMeshProUGUI>();
            var f = Font ?? TMP_Settings.defaultFontAsset;
            if (f != null) t.font = f;
            t.text = text;
            t.fontSize = S(size);
            t.color = color ?? TextColor;
            t.fontStyle = style;
            t.textWrappingMode = TextWrappingModes.Normal;
            go.AddComponent<LayoutElement>().minHeight = S(size) + 10f;
            return t;
        }

        /// <summary>Vanilla-style section header: an uppercase label with a thin rule to its right.</summary>
        public static void SectionHeader(Transform parent, string text)
        {
            Spacer(parent, 6f);
            var row = Container("Section", parent);
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = S(12f);
            hlg.childControlWidth = true; hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            row.AddComponent<LayoutElement>().minHeight = S(26f);

            var lab = Label(row.transform, text.ToUpperInvariant(), 13f, MutedColor, FontStyles.Bold);
            lab.characterSpacing = 6f;
            lab.GetComponent<LayoutElement>().flexibleWidth = 0;

            var rule = Container("Rule", row.transform);
            rule.AddComponent<Image>().color = RuleColor;
            var rle = rule.AddComponent<LayoutElement>();
            rle.minHeight = 2f; rle.preferredHeight = 2f; rle.flexibleWidth = 1f;
        }

        public static Button Button(Transform parent, string text, Action onClick, float height = 30f, Color? bg = null, bool enabled = true)
        {
            var go = Container("Button", parent);
            var img = go.AddComponent<Image>();
            img.color = enabled ? (bg ?? DisabledColor) : DisabledColor;
            var b = go.AddComponent<Button>();
            b.targetGraphic = img;
            b.interactable = enabled;
            if (enabled)
                b.onClick.AddListener(() => { try { onClick(); } catch (Exception e) { Debug.LogError("[StoreManager] tab button threw: " + e); } });
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = S(height); le.preferredHeight = S(height);

            var label = Label(go.transform, text, 14f, enabled ? Color.white : MutedColor);
            label.alignment = TextAlignmentOptions.Center;
            var lrt = Rect(label.gameObject);
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(18, 0); lrt.offsetMax = new Vector2(-18, 0);
            return b;
        }

        /// <summary>A horizontal row with a subtle card background (matches vanilla list entries).</summary>
        public static GameObject Row(Transform parent, bool card = false, float spacing = 12f, float height = 40f)
        {
            var go = Container("Row", parent);
            if (card) { var bg = go.AddComponent<Image>(); bg.color = RowColor; bg.raycastTarget = false; }
            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = S(spacing);
            hlg.padding = card ? new RectOffset((int)S(14), (int)S(14), 0, 0) : new RectOffset(0, 0, 0, 0);
            hlg.childControlWidth = true; hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = S(height); le.preferredHeight = S(height);
            return go;
        }

        /// <summary>An editable numeric field. <paramref name="onCommit"/> gets the raw string on end-edit.</summary>
        public static TMP_InputField NumberField(Transform parent, string value, float width, Action<string> onCommit, float height = 30f)
        {
            var go = Container("Field", parent);
            var bg = go.AddComponent<Image>();
            bg.color = new Color(1f, 1f, 1f, 0.12f);
            var le = go.AddComponent<LayoutElement>();
            le.minWidth = S(width); le.preferredWidth = S(width); le.flexibleWidth = 0;
            le.minHeight = S(height); le.preferredHeight = S(height);

            var textGo = Container("Text", go.transform);
            var trt = Rect(textGo);
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(12, 2); trt.offsetMax = new Vector2(-12, -2);
            var txt = textGo.AddComponent<TextMeshProUGUI>();
            var f = Font ?? TMP_Settings.defaultFontAsset;
            if (f != null) txt.font = f;
            txt.fontSize = S(14f);
            txt.color = TextColor;
            txt.alignment = TextAlignmentOptions.MidlineLeft;
            txt.textWrappingMode = TextWrappingModes.NoWrap;
            txt.overflowMode = TextOverflowModes.Ellipsis;

            var field = go.AddComponent<TMP_InputField>();
            field.targetGraphic = bg;
            field.textComponent = txt;
            field.textViewport = trt;
            field.contentType = TMP_InputField.ContentType.IntegerNumber;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.text = value;
            field.restoreOriginalTextOnEscape = true;
            field.onEndEdit.AddListener(s => { try { onCommit(s); } catch (Exception e) { Debug.LogError("[StoreManager] field commit threw: " + e); } });
            return field;
        }

        public static void Flexible(GameObject go, float flexW = 1f)
        {
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.flexibleWidth = flexW;
        }

        public static void FixedWidth(GameObject go, float w)
        {
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.minWidth = S(w); le.preferredWidth = S(w); le.flexibleWidth = 0;
        }

        public static void Spacer(Transform parent, float h = 6f) =>
            Container("Spacer", parent).AddComponent<LayoutElement>().minHeight = S(h);

        public static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
        }
    }
}
