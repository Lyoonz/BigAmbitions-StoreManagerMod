#nullable enable
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StoreManager.UI
{
    /// <summary>
    /// Minimal runtime-uGUI builders for the HQ BizMan tab content. Deliberately plain — no
    /// prefab/bundle dependency. Typography and colours are lifted from a donor vanilla
    /// <see cref="TMP_Text"/> (a cloned menu button) so it blends in without guessing font assets.
    /// </summary>
    public static class UiKit
    {
        /// <summary>The HQ canvas is ~2× a 1080p reference, so logical sizes are scaled up to match vanilla text.</summary>
        public static float Scale = 2.0f;
        private static float S(float v) => v * Scale;

        public static TMP_FontAsset? Font;
        public static Color TextColor = new(0.93f, 0.93f, 0.93f);
        public static Color MutedColor = new(0.62f, 0.64f, 0.68f);
        public static Color AccentColor = new(0.20f, 0.55f, 0.90f);
        public static Color PanelColor = new(1f, 1f, 1f, 0.05f);
        public static Color ButtonColor = new(1f, 1f, 1f, 0.12f);

        public static void AdoptStyleFrom(TMP_Text? donor)
        {
            if (donor != null)
            {
                try
                {
                    if (donor.font != null) Font = donor.font;
                    TextColor = donor.color;
                }
                catch { }
            }
            if (Font == null)
            {
                try { Font = TMP_Settings.defaultFontAsset; } catch { }
            }
        }

        public static RectTransform Rect(GameObject go)
        {
            var rt = go.GetComponent<RectTransform>() ?? go.AddComponent<RectTransform>();
            return rt;
        }

        public static GameObject Container(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            Rect(go).SetParent(parent, false);
            return go;
        }

        /// <summary>
        /// A plain vertical column (no scrolling) inset from its parent by (left, top, right).
        /// Returns the transform to fill. Insets clear the BizMan menu bar (top) and the floating
        /// business-info card (left).
        /// </summary>
        public static RectTransform Column(Transform parent, float left = 560f, float top = 40f, float right = 40f, float spacing = 12f)
        {
            // fixed inset frame
            var frame = Container("SM_Panel", parent);
            var frt = Rect(frame);
            frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
            frt.offsetMin = new Vector2(left, 40f);
            frt.offsetMax = new Vector2(-right, -top);
            frt.localScale = Vector3.one;

            // column that grows downward from the frame's top
            var col = Container("Col", frame.transform);
            var crt = Rect(col);
            crt.anchorMin = new Vector2(0, 1); crt.anchorMax = new Vector2(1, 1); crt.pivot = new Vector2(0.5f, 1f);
            crt.offsetMin = new Vector2(0, 0); crt.offsetMax = new Vector2(0, 0);
            crt.localScale = Vector3.one;

            var vlg = col.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = S(spacing);
            vlg.padding = new RectOffset(0, 0, 0, 0);
            vlg.childControlWidth = true; vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
            vlg.childAlignment = TextAnchor.UpperLeft;
            var fit = col.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return crt;
        }

        /// <summary>A vertical scroll view. Returns the Content transform to fill.</summary>
        public static RectTransform ScrollColumn(Transform parent, float spacing = 8f, int pad = 16)
        {
            var viewport = Container("Viewport", parent);
            var vrt = Rect(viewport);
            vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one;
            vrt.offsetMin = Vector2.zero; vrt.offsetMax = Vector2.zero;
            var vpImg = viewport.AddComponent<Image>(); vpImg.color = new Color(0, 0, 0, 0.001f);
            viewport.AddComponent<Mask>().showMaskGraphic = false;

            var content = Container("Content", viewport.transform);
            var crt = Rect(content);
            crt.anchorMin = new Vector2(0, 1); crt.anchorMax = new Vector2(1, 1); crt.pivot = new Vector2(0.5f, 1f);
            crt.offsetMin = new Vector2(0, 0); crt.offsetMax = new Vector2(0, 0);

            var vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = spacing;
            vlg.padding = new RectOffset(pad, pad, pad, pad);
            vlg.childControlWidth = true; vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
            var fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var sr = (parent.GetComponent<ScrollRect>() ?? parent.gameObject.AddComponent<ScrollRect>());
            sr.viewport = vrt;
            sr.content = crt;
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 20f;
            return crt;
        }

        public static TextMeshProUGUI Label(Transform parent, string text, float size = 18f, Color? color = null, FontStyles style = FontStyles.Normal)
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
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = S(size) + 8f;
            return t;
        }

        public static Button Button(Transform parent, string text, Action onClick, float height = 34f, Color? bg = null)
        {
            var go = Container("Button", parent);
            var img = go.AddComponent<Image>();
            img.color = bg ?? ButtonColor;
            var b = go.AddComponent<Button>();
            b.targetGraphic = img;
            b.onClick.AddListener(() => { try { onClick(); } catch (Exception e) { Debug.LogError("[StoreManager] tab button threw: " + e); } });
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = S(height); le.preferredHeight = S(height);

            var label = Label(go.transform, text, 15f, TextColor, FontStyles.Normal);
            label.alignment = TextAlignmentOptions.Center;
            var lrt = Rect(label.gameObject);
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(16, 0); lrt.offsetMax = new Vector2(-16, 0);
            return b;
        }

        public static GameObject Row(Transform parent, float spacing = 10f, float height = 34f)
        {
            var go = Container("Row", parent);
            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = S(spacing);
            hlg.childControlWidth = true; hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = S(height); le.preferredHeight = S(height);
            return go;
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

        public static void Spacer(Transform parent, float h = 6f)
        {
            var go = Container("Spacer", parent);
            go.AddComponent<LayoutElement>().minHeight = S(h);
        }

        public static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
        }
    }
}
