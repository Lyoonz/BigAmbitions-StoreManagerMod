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
            t.fontSize = size;
            t.color = color ?? TextColor;
            t.fontStyle = style;
            t.textWrappingMode = TextWrappingModes.Normal;
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = size + 6f;
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
            le.minHeight = height; le.preferredHeight = height;

            var label = Label(go.transform, text, 16f, TextColor, FontStyles.Normal);
            label.alignment = TextAlignmentOptions.Center;
            var lrt = Rect(label.gameObject);
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(8, 0); lrt.offsetMax = new Vector2(-8, 0);
            return b;
        }

        public static GameObject Row(Transform parent, float spacing = 8f, float height = 32f)
        {
            var go = Container("Row", parent);
            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = spacing;
            hlg.childControlWidth = true; hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = height; le.preferredHeight = height;
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
            le.minWidth = w; le.preferredWidth = w; le.flexibleWidth = 0;
        }

        public static void Spacer(Transform parent, float h = 6f)
        {
            var go = Container("Spacer", parent);
            go.AddComponent<LayoutElement>().minHeight = h;
        }

        public static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
        }
    }
}
