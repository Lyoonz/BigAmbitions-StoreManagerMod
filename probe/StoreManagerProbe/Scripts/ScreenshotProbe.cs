#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using BAModAPI;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  SCREENSHOT PROBE — gated on a marker file "sm-autoshot" next to the probe DLL.
//  It does NOT load a save (the author kept picking the wrong one). YOU load your
//  own save; then on city load it opens BizMan → HQ → "StoreManagers" tab,
//  captures sm-tab-shot.png next to Player.log, and dumps the tab UI geometry.
// ─────────────────────────────────────────────────────────────────────────────

[assembly: RegisterModClass(typeof(StoreManagerProbe.AutoShotCity))]

namespace StoreManagerProbe
{
    internal static class AutoShot
    {
        public static bool Enabled(ModContext c) =>
            File.Exists(Path.Combine(c.ModRootPath ?? ".", "sm-autoshot"));

        public static string OutDir =>
            Path.GetDirectoryName(Application.consoleLogPath) ?? Application.persistentDataPath;
    }

    [ModEntryOnCityLoad]
    public sealed class AutoShotCity : IModBigAmbitions
    {
        private GameObject? _host;
        public string[] RelativeAssetBundlePaths => Array.Empty<string>();
        public Task OnLoadAsync(ModContext c)
        {
            if (!AutoShot.Enabled(c)) return Task.CompletedTask;
            _host = new GameObject("SMAutoShotCity");
            UnityEngine.Object.DontDestroyOnLoad(_host);
            _host.AddComponent<ShotRunner>().Logger = c.Logger;
            return Task.CompletedTask;
        }
        public Task OnUnloadAsync() { if (_host) UnityEngine.Object.Destroy(_host); return Task.CompletedTask; }
    }

    public sealed class ShotRunner : MonoBehaviour
    {
        public IModLogger? Logger;
        private Assembly[] _asms = Array.Empty<Assembly>();

        private void Start() => StartCoroutine(Run());

        private IEnumerator Run()
        {
            yield return new WaitForSeconds(12f);
            _asms = AppDomain.CurrentDomain.GetAssemblies();

            object? fullMenu = null, bizMan = null;
            try
            {
                var uisType = FindType("UIs");
                var uis = uisType != null ? UnityEngine.Object.FindObjectOfType(uisType) : null;
                fullMenu = uis?.GetType().GetField("fullMenu")?.GetValue(uis);
                bizMan = fullMenu?.GetType().GetField("bizMan")?.GetValue(fullMenu);
                L($"UIs={uis != null} fullMenu={fullMenu != null} bizMan={bizMan != null}");
            }
            catch (Exception e) { L("resolve UI: " + e.Message); }

            // HQ address
            object? hqAddr = null;
            try
            {
                var gi = SaveGameManagerCurrent();
                var regs = gi?.GetType().GetProperty("BuildingRegistrations")?.GetValue(gi) as IEnumerable;
                if (regs != null)
                    foreach (var b in regs)
                    {
                        var bt = b.GetType();
                        if (bt.GetField("businessTypeName")?.GetValue(b) as string != "ba:businesstype_headquarters") continue;
                        bool rented = (bool)(bt.GetProperty("RentedByPlayer")?.GetValue(b) ?? false);
                        if (!rented) continue;
                        hqAddr = bt.GetProperty("Address")?.GetValue(b);
                        break;
                    }
            }
            catch (Exception e) { L("hq addr: " + e.Message); }

            try { fullMenu?.GetType().GetMethod("Toggle", new[] { typeof(bool) })?.Invoke(fullMenu, new object[] { true }); }
            catch (Exception e) { L("toggle: " + e.Message); }
            yield return new WaitForSeconds(1f);

            try
            {
                var open = bizMan?.GetType().GetMethods()
                    .FirstOrDefault(m => m.Name == "Open" && m.GetParameters().Length == 2);
                open?.Invoke(bizMan, new object?[] { hqAddr, "StoreManagers" });
                L($"opened BizMan HQ tab (hqAddr={hqAddr})");
            }
            catch (Exception e) { L("open bizman: " + e.Message); }

            yield return new WaitForSeconds(1.5f);
            yield return new WaitForEndOfFrame();

            // dump geometry
            try { DumpTabGeometry(); } catch (Exception e) { L("geometry dump: " + e.Message); }

            // screenshot
            try
            {
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                var png = tex.EncodeToPNG();
                UnityEngine.Object.Destroy(tex);
                var path = Path.Combine(AutoShot.OutDir, "sm-tab-shot.png");
                File.WriteAllBytes(path, png);
                L("SCREENSHOT -> " + path + " (" + png.Length + " bytes)");
            }
            catch (Exception e) { L("screenshot: " + e.Message); }
        }

        private void DumpTabGeometry()
        {
            var bizType = FindType("BizManBusiness");
            var biz = bizType != null ? UnityEngine.Object.FindObjectOfType(bizType) : null;
            var containers = bizType?.GetField("containers", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(biz) as Transform;
            var tab = containers?.Find("StoreManagers");
            if (tab == null) { L("geometry: containers/StoreManagers not found"); return; }
            var sb = new StringBuilder("\n[AUTOSHOT] tab geometry:\n");
            Walk(tab, 0, sb);
            L(sb.ToString());
        }

        private static void Walk(Transform t, int depth, StringBuilder sb)
        {
            var rt = t as RectTransform;
            string pad = new string(' ', depth * 2);
            string extra = "";
            var tmp = t.GetComponent(FindTypeStatic("TextMeshProUGUI") ?? typeof(Component));
            if (tmp != null)
            {
                try
                {
                    var txt = tmp.GetType().GetProperty("text")?.GetValue(tmp) as string;
                    var fs = tmp.GetType().GetProperty("fontSize")?.GetValue(tmp);
                    extra = $"  TMP fs={fs} \"{(txt != null && txt.Length > 40 ? txt.Substring(0, 40) + "…" : txt)}\"";
                }
                catch { }
            }
            var img = t.GetComponent<UnityEngine.UI.Image>();
            if (img != null) extra += $"  img a={img.color.a:0.00}";
            sb.AppendLine($"{pad}{t.name}  rect={(rt != null ? rt.rect.size.ToString() : "n/a")} active={t.gameObject.activeSelf}{extra}");
            if (depth < 6)
                for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i), depth + 1, sb);
        }

        private object? SaveGameManagerCurrent()
        {
            var t = FindType("SaveGameManager");
            return t?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        }

        private Type? FindType(string n) => _asms.SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
            .FirstOrDefault(x => x.Name == n && (x.Namespace == null || !x.Namespace.StartsWith("System")));

        private static Type? FindTypeStatic(string n) => AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
            .FirstOrDefault(x => x.Name == n);

        private void L(string s) { Logger?.Info(s); Debug.Log("[AUTOSHOT] " + s); }
    }
}
