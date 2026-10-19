using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace AvixLmuDash
{
    public class MasterBuildResult
    {
        public List<string> Included = new List<string>();
        public List<string> Missing = new List<string>();
        public List<string> Errors = new List<string>();
        public bool Built;
        public string Summary()
        {
            var s = "Dashboard « " + MasterDashBuilder.MasterCode + " » : " + Included.Count + " voitures intégrées";
            if (Missing.Count > 0) s += ", introuvables : " + string.Join(", ", Missing);
            if (Errors.Count > 0) s += " | erreurs : " + string.Join(" | ", Errors.Take(5));
            return s;
        }
    }

    /// <summary>
    /// Construit UN dashboard SimHub qui contient tous les dashboards voitures sous forme de widgets.
    /// Un seul widget est visible : celui de la voiture pilotée (propriété AvixLmuDashPlugin.MasterSlot).
    /// Les widgets sont dans le calque d'arrière-plan pour recevoir les commandes page suivante / précédente
    /// (tactile VoCore : moitié gauche = page précédente, moitié droite = page suivante).
    /// </summary>
    public static class MasterDashBuilder
    {
        public const string MasterCode = "AVIX LMU Auto";
        public const string SlotProperty = "AvixLmuDashPlugin.MasterSlot";
        public const int DefaultW = 1280, DefaultH = 400;
        static int BaseW = 1200, BaseH = 720;
        const string HashFile = "avix.hash";
        const string ListFile = "avix.list";
        const string T = "SimHub.Plugins.OutputPlugins.GraphicalDash.Models.";

        static readonly Encoding Utf8Bom = new UTF8Encoding(true);

        public static string MasterDir(string templatesDir) { return Path.Combine(templatesDir, MasterCode); }

        public static List<string> ReadIncluded(string templatesDir)
        {
            try
            {
                var p = Path.Combine(MasterDir(templatesDir), ListFile);
                if (File.Exists(p)) return File.ReadAllLines(p).Where(l => l.Trim().Length > 0).ToList();
            }
            catch { }
            return new List<string>();
        }

        public static string ComputeHash(string templatesDir, IEnumerable<string> codes, string layout = "")
        {
            var sb = new StringBuilder("v6|" + layout + "|");
            foreach (var c in codes.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                sb.Append(c).Append('|');
                try
                {
                    var dir = Path.Combine(templatesDir, c);
                    if (Directory.Exists(dir))
                        foreach (var f in Directory.GetFiles(dir, "*.djson").OrderBy(x => x))
                            sb.Append(Path.GetFileName(f)).Append(File.GetLastWriteTimeUtc(f).Ticks).Append(new FileInfo(f).Length).Append(';');
                    else sb.Append("missing");
                }
                catch { }
            }
            using (var md5 = MD5.Create())
                return BitConverter.ToString(md5.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()))).Replace("-", "");
        }

        public static bool IsUpToDate(string templatesDir, IEnumerable<string> codes, string layout = "")
        {
            try
            {
                var dir = MasterDir(templatesDir);
                var hp = Path.Combine(dir, HashFile);
                if (!File.Exists(Path.Combine(dir, MasterCode + ".djson")) || !File.Exists(hp)) return false;
                return File.ReadAllText(hp).Trim() == ComputeHash(templatesDir, codes, layout);
            }
            catch { return false; }
        }

        static string Prefix(string code)
        {
            var bad = Path.GetInvalidFileNameChars();
            var s = new string(code.Select(ch => bad.Contains(ch) ? '_' : ch).ToArray());
            return s + "__";
        }

        static void WriteJson(string path, JToken j)
        {
            File.WriteAllText(path, j.ToString(Formatting.None), Utf8Bom);
        }

        /// <summary>Remplace les FileName des widgets imbriqués par leur nouveau nom préfixé.</summary>
        static void RemapWidgets(JToken tok, Dictionary<string, string> map)
        {
            var o = tok as JObject;
            if (o != null)
            {
                var type = (string)o["$type"];
                if (type != null && type.Contains("WidgetItem"))
                {
                    var fn = o["FileName"] as JValue;
                    string newName;
                    if (fn != null && fn.Type == JTokenType.String && map.TryGetValue(Path.GetFileName((string)fn.Value), out newName))
                        o["FileName"] = newName;
                }
                foreach (var p in o.Properties()) RemapWidgets(p.Value, map);
                return;
            }
            var a = tok as JArray;
            if (a != null) foreach (var c in a) RemapWidgets(c, map);
        }

        public static string LayoutKey(int w, int h, bool reflow) { return w + "x" + h + (reflow ? "R" : "F"); }

        public static MasterBuildResult Build(string templatesDir, IList<string> codes, byte[] logoPng = null,
                                              int targetW = DefaultW, int targetH = DefaultH, bool reflow = true,
                                              string forceVisible = null, string masterCode = null)
        {
            BaseW = Math.Max(200, targetW); BaseH = Math.Max(120, targetH);
            var layout = LayoutKey(BaseW, BaseH, reflow);
            var code0 = masterCode ?? MasterCode;
            var res = new MasterBuildResult();
            var dir = Path.Combine(templatesDir, code0);
            Directory.CreateDirectory(dir);
            // nettoyage de l'ancienne version (on ignore les fichiers verrouillés)
            foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            {
                try { File.Delete(f); } catch { }
            }

            var widgets = new JArray();
            var jsHashes = new HashSet<string>();
            var distinct = codes.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim())
                                .Where(c => !c.Equals(MasterCode, StringComparison.OrdinalIgnoreCase))
                                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            foreach (var code in distinct)
            {
                var src = Path.Combine(templatesDir, code);
                var mainPath = Path.Combine(src, code + ".djson");
                if (!File.Exists(mainPath)) { res.Missing.Add(code); continue; }
                try
                {
                    var prefix = Prefix(code);
                    var files = Directory.GetFiles(src);
                    var djsons = files.Where(f => f.EndsWith(".djson", StringComparison.OrdinalIgnoreCase)).ToList();
                    var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var f in djsons) map[Path.GetFileName(f)] = prefix + Path.GetFileName(f);

                    int bw = BaseW, bh = BaseH;
                    string bg = "#FF000000";
                    var docs = new Dictionary<string, JToken>(StringComparer.OrdinalIgnoreCase);
                    string mainNew = map[code + ".djson"];
                    foreach (var f in djsons)
                    {
                        var j = JToken.Parse(File.ReadAllText(f));
                        RemapWidgets(j, map);
                        docs[map[Path.GetFileName(f)]] = j;
                        // ressources (images) et classes associées au djson
                        foreach (var ext in new[] { ".ressources", ".carclasses" })
                        {
                            var comp = f + ext;
                            if (File.Exists(comp)) File.Copy(comp, Path.Combine(dir, map[Path.GetFileName(f)] + ext), true);
                        }
                    }
                    var main = (JObject)docs[mainNew];
                    bw = (int?)main["BaseWidth"] ?? BaseW;
                    bh = (int?)main["BaseHeight"] ?? BaseH;
                    bg = (string)main["BackgroundColor"] ?? bg;
                    // adaptation au format de l'écran (ex. 1280×400) : on écarte les blocs au lieu de laisser des bandes noires
                    double k = ((double)BaseW / BaseH * bh) / bw;
                    if (reflow && k > 1.05)
                    {
                        JToken dummy;
                        Reflow.AdaptDashboard(main, k, n => docs.TryGetValue(n, out dummy) ? dummy : null, (n, t) => { docs[n] = t; });
                        bw = (int?)main["BaseWidth"] ?? bw;
                    }
                    foreach (var kv in docs) WriteJson(Path.Combine(dir, kv.Key), kv.Value);

                    // polices embarquées
                    var shf = Path.Combine(src, "_SHFonts");
                    if (Directory.Exists(shf))
                    {
                        var d = Path.Combine(dir, "_SHFonts");
                        Directory.CreateDirectory(d);
                        foreach (var f in Directory.GetFiles(shf))
                        {
                            var t = Path.Combine(d, Path.GetFileName(f));
                            if (!File.Exists(t)) File.Copy(f, t);
                        }
                    }
                    // extensions javascript
                    var js = Path.Combine(src, "JavascriptExtensions");
                    if (Directory.Exists(js))
                    {
                        var d = Path.Combine(dir, "JavascriptExtensions");
                        Directory.CreateDirectory(d);
                        foreach (var f in Directory.GetFiles(js))
                        {
                            var bytes = File.ReadAllBytes(f);
                            string h;
                            using (var md5 = MD5.Create()) h = BitConverter.ToString(md5.ComputeHash(bytes));
                            if (!jsHashes.Add(h)) continue;
                            File.WriteAllBytes(Path.Combine(d, prefix + Path.GetFileName(f)), bytes);
                        }
                    }

                    // widget plein écran, proportions conservées, centré
                    double s = Math.Min((double)BaseW / bw, (double)BaseH / bh);
                    double w = bw * s, h2 = bh * s;
                    widgets.Add(new JObject
                    {
                        ["$type"] = T + "WidgetItem, SimHub.Plugins",
                        ["NextScreenCommand"] = 7,      // DashActions.NextScreen
                        ["PreviousScreenCommand"] = 6,  // DashActions.PreviousScreen
                        ["AutoSize"] = true,
                        ["AutoSizeScale"] = Math.Round(s, 6),
                        ["FileName"] = map[code + ".djson"],
                        ["InitialScreenIndex"] = 0,
                        ["FreezePageChanges"] = false,
                        ["EnableScreenRolesAndActivation"] = true,
                        ["IgnoreSavedScreensEx"] = true,
                        ["BackgroundColor"] = bg,
                        ["Height"] = Math.Round(h2, 3),
                        ["Left"] = Math.Round((BaseW - w) / 2, 3),
                        ["Top"] = Math.Round((BaseH - h2) / 2, 3),
                        ["Visible"] = forceVisible != null && forceVisible.Equals(code, StringComparison.OrdinalIgnoreCase),
                        ["BlinkPhasisInverted"] = false,
                        ["Width"] = Math.Round(w, 3),
                        ["Name"] = code,
                        ["RenderingSkip"] = 0,
                        ["MinimumRefreshIntervalMS"] = 0.0,
                        ["Bindings"] = new JObject
                        {
                            ["Visible"] = new JObject
                            {
                                ["Formula"] = new JObject { ["Expression"] = "[" + SlotProperty + "] = '" + code.Replace("'", "''") + "'" },
                                ["Mode"] = 2
                            }
                        }
                    });
                    if (forceVisible != null) ((JObject)widgets.Last).Remove("Bindings");
                    res.Included.Add(code);
                }
                catch (Exception ex) { res.Errors.Add(code + ": " + ex.Message); }
            }

            // écran d'attente (aucune voiture / hors LMU) — mis en page pour 1200×720 puis adapté à l'écran
            double sc = Math.Min(BaseW / 1200.0, BaseH / 720.0), cxm = BaseW / 2.0;
            Func<double, double> X = x => Math.Round(cxm + (x - 600) * sc, 2);
            Func<double, double> Y = y => Math.Round(BaseH / 2.0 + (y - 360) * sc, 2);
            Func<double, double> S = v => Math.Round(v * sc, 2);
            var waiting = new JObject
            {
                ["$type"] = T + "Layer, SimHub.Plugins",
                ["Group"] = true,
                ["Repetitions"] = 0,
                ["Childrens"] = new JArray
                {
                    logoPng != null
                        ? (JToken)new JObject
                        {
                            ["$type"] = T + "ImageItem, SimHub.Plugins",
                            ["Image"] = "avix_logo", ["AutoSize"] = false, ["BackgroundColor"] = "#00FFFFFF",
                            ["Height"] = S(266), ["Left"] = X(180), ["Top"] = Y(150), ["Visible"] = true, ["BlinkPhasisInverted"] = false,
                            ["Width"] = S(840), ["Name"] = "Logo AVIX", ["RenderingSkip"] = 0, ["MinimumRefreshIntervalMS"] = 0.0,
                        }
                        : Text("AVIX LMU", 0, Y(250), BaseW, S(120), S(96), "Bold", "#FFFFFFFF", null),
                    new JObject
                    {
                        ["$type"] = T + "RectangleItem, SimHub.Plugins", ["IsRectangleItem"] = true, ["BackgroundColor"] = "#FFC1C107",
                        ["BorderStyle"] = new JObject { ["RadiusTopLeft"] = 3, ["RadiusTopRight"] = 3, ["RadiusBottomLeft"] = 3, ["RadiusBottomRight"] = 3 },
                        ["Height"] = Math.Max(3, S(5)), ["Left"] = X(540), ["Top"] = Y(436), ["Visible"] = true, ["BlinkPhasisInverted"] = false,
                        ["Width"] = S(120), ["Name"] = "Trait", ["RenderingSkip"] = 0, ["MinimumRefreshIntervalMS"] = 0.0,
                    },
                    Text("", 0, Y(462), BaseW, S(60), S(36), "Bold", "#FFAAAAAA", "isnull([AvixLmuDashPlugin.Status], 'En attente de Le Mans Ultimate')"),
                    Text("◀  GAUCHE : PAGE PRÉCÉDENTE      DROITE : PAGE SUIVANTE  ▶", 0, Y(640), BaseW, S(50), S(24), "Bold", "#FF5A5A5A", null),
                },
                ["Height"] = (double)BaseH, ["Left"] = 0.0, ["Top"] = 0.0, ["Visible"] = forceVisible == null, ["BlinkPhasisInverted"] = false,
                ["Width"] = (double)BaseW, ["Name"] = "Attente", ["RenderingSkip"] = 0, ["MinimumRefreshIntervalMS"] = 0.0,
            };
            if (forceVisible == null)
                waiting["Bindings"] = new JObject
                {
                    ["Visible"] = new JObject { ["Formula"] = new JObject { ["Expression"] = "isnull([" + SlotProperty + "], '') = ''" }, ["Mode"] = 2 }
                };
            widgets.Add(waiting);

            var bgScreen = Screen("Voitures (arrière-plan)", widgets, isBackground: true);
            var mainScreen = Screen("Principal", new JArray(), isBackground: false);
            // indispensable : sans ce lien SimHub n'affiche pas le calque et ne lui transmet pas le tactile
            mainScreen["BackgroundLayerScreenId"] = bgScreen["ScreenId"];
            var images = new JArray();
            if (logoPng != null)
            {
                int lw = 0, lh = 0;
                if (logoPng.Length > 24) { lw = (logoPng[16] << 24) | (logoPng[17] << 16) | (logoPng[18] << 8) | logoPng[19]; lh = (logoPng[20] << 24) | (logoPng[21] << 16) | (logoPng[22] << 8) | logoPng[23]; }
                string md5;
                using (var m = MD5.Create()) md5 = BitConverter.ToString(m.ComputeHash(logoPng)).Replace("-", "").ToLowerInvariant();
                images.Add(new JObject
                {
                    ["Name"] = "avix_logo", ["Extension"] = ".png", ["Modified"] = false, ["Optimized"] = true,
                    ["Width"] = lw, ["Height"] = lh, ["Length"] = logoPng.Length, ["MD5"] = md5,
                });
                using (var fs = File.Create(Path.Combine(dir, code0 + ".djson.ressources")))
                using (var zip = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create))
                {
                    var e = zip.CreateEntry("avix_logo.png");
                    using (var es = e.Open()) es.Write(logoPng, 0, logoPng.Length);
                }
            }
            var metadata = new JObject
            {
                ["ScreenCount"] = 2.0,
                ["InGameScreensIndexs"] = new JArray(1),
                ["IdleScreensIndexs"] = new JArray(1),
                ["MainPreviewIndex"] = 1,
                ["IsOverlay"] = false,
                ["OverlaySizeWarning"] = true,
                ["MetadataVersion"] = 2.0,
                ["EnableOnDashboardMessaging"] = true,
                ["PitScreensIndexs"] = new JArray(),
                ["SimHubVersion"] = "9.11.15",
                ["Category"] = "AVIX",
                ["Title"] = code0,
                ["Description"] = "Change automatiquement selon la voiture pilotée dans Le Mans Ultimate (plugin AVIX LMU Dash Switcher). " +
                                  "Tactile : gauche = page précédente, droite = page suivante.",
                ["Author"] = "AVIX 3D",
                ["Width"] = (double)BaseW,
                ["Height"] = (double)BaseH,
                ["DashboardVersion"] = "1",
                ["PreferredTouchMode"] = 1, // Simple : moitié gauche / droite
            };
            var root = new JObject
            {
                ["Version"] = 2,
                ["Id"] = Guid.NewGuid().ToString(),
                ["BaseHeight"] = BaseH,
                ["BaseWidth"] = BaseW,
                ["BackgroundColor"] = "#FF000000",
                ["Screens"] = new JArray(bgScreen, mainScreen),
                ["SnapToGrid"] = false,
                ["HideLabels"] = true,
                ["ShowForeground"] = true,
                ["ForegroundOpacity"] = 100.0,
                ["ShowBackground"] = true,
                ["BackgroundOpacity"] = 100.0,
                ["ShowBoundingRectangles"] = false,
                ["GridSize"] = 10,
                ["Images"] = images,
                ["Metadata"] = metadata,
                ["ShowOnScreenControls"] = true,
                ["IsOverlay"] = false,
                ["EnableClickThroughOverlay"] = true,
                ["EnableOnDashboardMessaging"] = true,
                ["UseStrictJSIsolation"] = false,
                ["UseStrictJSIsolationWarning"] = true,
            };
            WriteJson(Path.Combine(dir, code0 + ".djson"), root);
            File.WriteAllText(Path.Combine(dir, code0 + ".djson.metadata"), metadata.ToString(Formatting.Indented), Utf8Bom);
            File.WriteAllLines(Path.Combine(dir, ListFile), res.Included);
            File.WriteAllText(Path.Combine(dir, HashFile), ComputeHash(templatesDir, distinct, layout));
            res.Built = true;
            return res;
        }

        static JObject Screen(string name, JArray items, bool isBackground)
        {
            return new JObject
            {
                ["RenderingSkip"] = 0,
                ["Name"] = name,
                ["InGameScreen"] = true,
                ["IdleScreen"] = true,
                ["PitScreen"] = false,
                ["ScreenId"] = Guid.NewGuid().ToString(),
                ["AllowOverlays"] = true,
                ["IsForegroundLayer"] = false,
                ["IsOverlayLayer"] = false,
                ["OverlayTriggerExpression"] = new JObject { ["Expression"] = "" },
                ["ScreenEnabledExpression"] = new JObject { ["Expression"] = "" },
                ["OverlayMaxDuration"] = 0,
                ["OverlayMinDuration"] = 0,
                ["IsBackgroundLayer"] = isBackground,
                ["BackgroundColor"] = isBackground ? "#FF000000" : "#00000000",
                ["Items"] = items,
                ["MinimumRefreshIntervalMS"] = 0.0,
            };
        }

        static JObject Text(string text, double left, double top, double width, double height, double size, string weight, string color, string expression)
        {
            var o = new JObject
            {
                ["$type"] = T + "TextItem, SimHub.Plugins",
                ["IsTextItem"] = true,
                ["Font"] = "Rajdhani",
                ["FontWeight"] = weight,
                ["FontSize"] = size,
                ["Text"] = text,
                ["TextColor"] = color,
                ["HorizontalAlignment"] = 1,
                ["VerticalAlignment"] = 1,
                ["BackgroundColor"] = "#00FFFFFF",
                ["Height"] = height, ["Left"] = left, ["Top"] = top, ["Visible"] = true, ["BlinkPhasisInverted"] = false,
                ["Width"] = width, ["Name"] = string.IsNullOrEmpty(text) ? "Etat" : text, ["RenderingSkip"] = 0, ["MinimumRefreshIntervalMS"] = 0.0,
            };
            if (expression != null)
                o["Bindings"] = new JObject { ["Text"] = new JObject { ["Formula"] = new JObject { ["Expression"] = expression }, ["Mode"] = 2 } };
            return o;
        }
    }
}
