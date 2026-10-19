using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AvixLmuDash
{
    /// <summary>
    /// Adapte un dashboard à un écran plus large (ex. VoCore 7,85" 1280×400) sans rien déformer :
    /// - la hauteur reste la référence (tout garde la même taille) ;
    /// - chaque bloc (calque de premier niveau ou élément seul) est écarté horizontalement
    ///   proportionnellement à sa position ;
    /// - les éléments très larges (fonds, barres, bandeaux) sont étirés sur toute la largeur,
    ///   sauf les images/cercles hauts qui seraient déformés : eux sont seulement recentrés.
    /// Les coordonnées des enfants d'un calque sont absolues dans SimHub, d'où le décalage récursif.
    /// </summary>
    public static class Reflow
    {
        public const double WideRatio = 0.6;   // bloc ≥ 60 % de la largeur => étiré
        public const double BarRatio = 0.18;   // image/cercle moins haut que 18 % de la hauteur => peut être étiré

        public class Context
        {
            public double K;                       // facteur horizontal (nouvelle largeur / ancienne)
            public double OldW, OldH;
            public Func<string, JToken> LoadWidget; // charge un widget imbriqué (pour l'adapter aussi)
            public Action<string, JToken> SaveWidget;
            public HashSet<string> Done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        static bool HasBox(JObject o)
        {
            return o["Left"] != null && o["Width"] != null && o["Left"].Type != JTokenType.Null && o["Width"].Type != JTokenType.Null;
        }

        static double D(JToken t, double def = 0)
        {
            if (t == null || t.Type == JTokenType.Null) return def;
            try { return (double)t; } catch { return def; }
        }

        static string TypeName(JObject o)
        {
            var t = (string)o["$type"] ?? "";
            var i = t.IndexOf(',');
            if (i > 0) t = t.Substring(0, i);
            return t.Substring(t.LastIndexOf('.') + 1);
        }

        static IEnumerable<JObject> Children(JObject o)
        {
            var c = o["Childrens"] as JArray;
            if (c == null) yield break;
            foreach (var x in c.OfType<JObject>()) yield return x;
        }

        /// <summary>Boîte englobante (x1,x2,y1,y2) d'un élément et de tous ses enfants.</summary>
        static bool Bounds(JObject o, ref double x1, ref double x2, ref double y1, ref double y2)
        {
            bool any = false;
            if (HasBox(o))
            {
                double l = D(o["Left"]), w = D(o["Width"]), t = D(o["Top"]), h = D(o["Height"]);
                x1 = Math.Min(x1, l); x2 = Math.Max(x2, l + w); y1 = Math.Min(y1, t); y2 = Math.Max(y2, t + h);
                any = true;
            }
            foreach (var c in Children(o)) any |= Bounds(c, ref x1, ref x2, ref y1, ref y2);
            return any;
        }

        static void Translate(JObject o, double dx)
        {
            if (HasBox(o)) o["Left"] = Math.Round(D(o["Left"]) + dx, 2);
            foreach (var c in Children(o)) Translate(c, dx);
        }

        static bool Distortable(JObject o, Context ctx)
        {
            var t = TypeName(o);
            if (t.Contains("Image") || t.Contains("Ellipse") || t.Contains("Gauge") && !t.Contains("Linear") || t.Contains("Map") || t.Contains("Needle"))
                return D(o["Height"]) > ctx.OldH * BarRatio;
            return false;
        }

        /// <summary>Étire horizontalement (x' = x·K) un bloc large, en protégeant ce qui se déformerait.</summary>
        static void Stretch(JObject o, Context ctx)
        {
            if (HasBox(o))
            {
                double l = D(o["Left"]), w = D(o["Width"]);
                if (Distortable(o, ctx))
                {
                    var cx = (l + w / 2) * ctx.K;
                    o["Left"] = Math.Round(cx - w / 2, 2);
                }
                else
                {
                    o["Left"] = Math.Round(l * ctx.K, 2);
                    o["Width"] = Math.Round(w * ctx.K, 2);
                    if (TypeName(o) == "WidgetItem") AdaptNestedWidget(o, ctx);
                }
            }
            foreach (var c in Children(o)) Stretch(c, ctx);
        }

        static void AdaptNestedWidget(JObject item, Context ctx)
        {
            var fn = (string)item["FileName"];
            if (string.IsNullOrEmpty(fn) || ctx.LoadWidget == null) return;
            if (ctx.Done.Contains(fn)) return;
            var j = ctx.LoadWidget(fn) as JObject;
            if (j == null) return;
            double bw = D(j["BaseWidth"]), bh = D(j["BaseHeight"]);
            if (bw <= 0 || bh <= 0) return;
            ctx.Done.Add(fn);
            AdaptDashboard(j, bw * ctx.K / bw, ctx.LoadWidget, ctx.SaveWidget, ctx.Done);
            ctx.SaveWidget(fn, j);
        }

        /// <summary>Adapte un djson (racine) d'un facteur horizontal k. Modifie BaseWidth.</summary>
        public static void AdaptDashboard(JObject dash, double k, Func<string, JToken> load, Action<string, JToken> save, HashSet<string> done = null)
        {
            double w = D(dash["BaseWidth"]), h = D(dash["BaseHeight"]);
            if (w <= 0 || h <= 0 || Math.Abs(k - 1) < 0.01) return;
            var ctx = new Context { K = k, OldW = w, OldH = h, LoadWidget = load, SaveWidget = save, Done = done ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase) };
            var screens = dash["Screens"] as JArray;
            if (screens != null)
                foreach (var s in screens.OfType<JObject>())
                {
                    var items = s["Items"] as JArray;
                    if (items == null) continue;
                    foreach (var it in items.OfType<JObject>())
                    {
                        double x1 = double.MaxValue, x2 = double.MinValue, y1 = double.MaxValue, y2 = double.MinValue;
                        if (!Bounds(it, ref x1, ref x2, ref y1, ref y2)) continue;
                        double bw = x2 - x1, bh = y2 - y1;
                        if (bw >= w * WideRatio)
                        {
                            Stretch(it, ctx);
                        }
                        else
                        {
                            var cx = (x1 + x2) / 2;
                            Translate(it, cx * (k - 1));
                            // widgets imbriqués non étirés : on les laisse tels quels (ils gardent leur taille)
                        }
                    }
                }
            dash["BaseWidth"] = (int)Math.Round(w * k);
        }
    }
}
