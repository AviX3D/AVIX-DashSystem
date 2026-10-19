using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace AvixLmuDash
{
    /// <summary>Une règle : si la classe correspond ET qu'un mot-clé est trouvé -> ce dashboard.</summary>
    public class DashRule
    {
        public bool Enabled { get; set; }
        /// <summary>HYPER, LMP2, LMP3, GTE, GT3 ou vide (= toutes classes)</summary>
        public string CarClass { get; set; }
        /// <summary>Mots-clés cherchés dans le modèle de la voiture (séparés par des virgules)</summary>
        public string ModelKeywords { get; set; }
        /// <summary>Mots-clés de secours cherchés dans l'ID / le nom d'équipe (séparés par des virgules)</summary>
        public string TeamKeywords { get; set; }
        /// <summary>Nom (code) du dashboard SimHub = nom du dossier dans DashTemplates</summary>
        public string Dashboard { get; set; }
        public string Comment { get; set; }

        public DashRule() { Enabled = true; }

        public DashRule(string cls, string model, string team, string dash, string comment = null)
        {
            Enabled = true; CarClass = cls; ModelKeywords = model; TeamKeywords = team; Dashboard = dash; Comment = comment;
        }

        public DashRule Clone()
        {
            return (DashRule)MemberwiseClone();
        }
    }

    public class CarInfo
    {
        public string CarId = "";
        public string Team = "";          // CarModel côté SimHub (= nom de l'équipe/livrée dans LMU)
        public string SimHubClass = "";   // CarClass côté SimHub
        public string NativeClass = "";   // mVehicleClass (enum LMU)
        public string VehicleModel = "";  // mVehicleModel (télémétrie native LMU)
        public string VehicleFile = "";   // mVehFilename
        public string VehicleName = "";   // mVehicleName
        public string Extra = "";         // propriétés d'autres plugins

        public string Key
        {
            get { return CarId + "|" + VehicleModel + "|" + NativeClass + "|" + SimHubClass + "|" + Extra; }
        }

        public string NormalizedClass
        {
            get
            {
                var c = Matcher.NormalizeClass(NativeClass);
                if (string.IsNullOrEmpty(c)) c = Matcher.NormalizeClass(SimHubClass);
                if (string.IsNullOrEmpty(c)) c = Matcher.NormalizeClass(CarId);
                return c;
            }
        }
    }

    public class MatchResult
    {
        public DashRule Rule;
        public int RuleIndex = -1;
        public string Dashboard;
        public string How = "";
    }

    public static class Matcher
    {
        public const int DefaultsVersion = 1;

        public static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var d = s.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(d.Length);
            foreach (var ch in d)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
                if (char.IsLetterOrDigit(ch)) sb.Append(char.ToUpperInvariant(ch));
            }
            return sb.ToString();
        }

        /// <summary>Ramène toutes les écritures de classe à HYPER / LMP2 / LMP3 / GTE / GT3.</summary>
        public static string NormalizeClass(string s)
        {
            var n = Normalize(s);
            if (n.Length == 0) return "";
            if (n.StartsWith("HYPER") || n.StartsWith("LMH") || n.StartsWith("LMDH")) return "HYPER";
            if (n.StartsWith("LMP2")) return "LMP2";
            if (n.StartsWith("LMP3")) return "LMP3";
            if (n.StartsWith("LMGTE") || n.StartsWith("GTE")) return "GTE";
            if (n.StartsWith("LMGT3") || n.StartsWith("GT3")) return "GT3";
            if (n.StartsWith("PACE") || n.StartsWith("SAFETY")) return "PACE";
            return "";
        }

        public static List<string> SplitKeywords(string s)
        {
            var res = new List<string>();
            if (string.IsNullOrEmpty(s)) return res;
            foreach (var part in s.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var k = Normalize(part);
                if (k.Length > 0) res.Add(k);
            }
            return res;
        }

        static bool ClassOk(DashRule r, string cls)
        {
            var rc = NormalizeClass(r.CarClass);
            if (rc.Length == 0) return true;      // règle toutes classes
            if (cls.Length == 0) return true;     // classe inconnue : on laisse passer
            return rc == cls;
        }

        static string FirstHit(List<string> keywords, string text)
        {
            foreach (var k in keywords) if (text.Contains(k)) return k;
            return null;
        }

        /// <summary>
        /// Passe 1 : mots-clés "modèle" dans le modèle détecté (le plus fiable).
        /// Passe 2 : mots-clés "modèle" + "équipe" dans tout (ID voiture, équipe, fichier .veh...).
        /// Passe 3 : règles sans mot-clé = "toute voiture de cette classe".
        /// </summary>
        public static MatchResult Match(IList<DashRule> rules, CarInfo car)
        {
            if (rules == null || car == null) return null;
            var cls = car.NormalizedClass;
            var modelText = Normalize(car.VehicleModel) + "|" + Normalize(car.Extra);
            var fullText = modelText + "|" + Normalize(car.VehicleFile) + "|" + Normalize(car.VehicleName) + "|" +
                           Normalize(car.CarId) + "|" + Normalize(car.Team);

            if (modelText.Replace("|", "").Length > 0)
            {
                for (int i = 0; i < rules.Count; i++)
                {
                    var r = rules[i];
                    if (r == null || !r.Enabled || string.IsNullOrWhiteSpace(r.Dashboard) || !ClassOk(r, cls)) continue;
                    var hit = FirstHit(SplitKeywords(r.ModelKeywords), modelText);
                    if (hit != null) return new MatchResult { Rule = r, RuleIndex = i, Dashboard = r.Dashboard.Trim(), How = "modèle contient \"" + hit + "\"" };
                }
            }

            for (int i = 0; i < rules.Count; i++)
            {
                var r = rules[i];
                if (r == null || !r.Enabled || string.IsNullOrWhiteSpace(r.Dashboard) || !ClassOk(r, cls)) continue;
                var kw = SplitKeywords(r.ModelKeywords);
                kw.AddRange(SplitKeywords(r.TeamKeywords));
                var hit = FirstHit(kw, fullText);
                if (hit != null) return new MatchResult { Rule = r, RuleIndex = i, Dashboard = r.Dashboard.Trim(), How = "ID/équipe contient \"" + hit + "\"" };
            }

            if (cls.Length > 0)
            {
                for (int i = 0; i < rules.Count; i++)
                {
                    var r = rules[i];
                    if (r == null || !r.Enabled || string.IsNullOrWhiteSpace(r.Dashboard)) continue;
                    if (SplitKeywords(r.ModelKeywords).Count > 0 || SplitKeywords(r.TeamKeywords).Count > 0) continue;
                    if (NormalizeClass(r.CarClass) != cls) continue;
                    return new MatchResult { Rule = r, RuleIndex = i, Dashboard = r.Dashboard.Trim(), How = "toute voiture " + cls };
                }
            }
            return null;
        }

        /// <summary>Règles par défaut construites à partir des 33 dashboards fournis.</summary>
        public static List<DashRule> DefaultRules()
        {
            return new List<DashRule>
            {
                // ---------- HYPERCAR ----------
                new DashRule("HYPER", "499P, Ferrari", "AF Corse, Ferrari", "Ferrari 499P - V2.0"),
                new DashRule("HYPER", "GR010, Toyota", "Toyota", "Toyota GR010 Hybrid"),
                new DashRule("HYPER", "963, Porsche", "Penske, Proton, Porsche", "HY Porsche 963 LMDh"),
                new DashRule("HYPER", "M Hybrid, BMWMH, BMW", "BMW, WRT", "HY BMW Hybrid V8 new version"),
                new DashRule("HYPER", "V-Series, VLMDH, Cadillac", "Cadillac, Jota", "Cadillac V-Series - LMU"),
                new DashRule("HYPER", "9X8, Peugeot", "Peugeot, TotalEnergies", "Peugeot 9X8"),
                new DashRule("HYPER", "A424, Alpine", "Alpine", "Alpine A424 LMDh"),
                new DashRule("HYPER", "SC63, Lamborghini", "Lamborghini, Iron Lynx", "Lamborghini SC63"),
                new DashRule("HYPER", "Isotta, Tipo 6, Tipo6", "Isotta", "Isotta Fraschini Tipo 6 LMH"),
                new DashRule("HYPER", "Glickenhaus, SCG", "Glickenhaus", "Glickenhaus"),
                new DashRule("HYPER", "Vanwall, Vandervell", "Vanwall, Floyd", "Vanwall 680"),
                new DashRule("HYPER", "Valkyrie, Aston", "Valkyrie, AM Valkyrie, Heart of Racing, Aston", "HY Aston Martin Valkyrie LMH"),
                new DashRule("HYPER", "Genesis, GMR", "Genesis, Magma", "HY Genesis GMR-001"),

                // ---------- LMGTE ----------
                new DashRule("GTE", "488, Ferrari", "AF Corse, Kessel, Richard Mille, Ferrari", "Ferrari 488GTE"),
                new DashRule("GTE", "C8.R, C8R, Corvette, Chevrolet", "Corvette", "Corvette C8.R - LMU"),
                new DashRule("GTE", "Vantage, Aston, AMR", "TF Sport, ORT, GMB, Heart of Racing, Northwest AMR, D'station, Aston", "Aston Martin Vantage GTE - LMU"),
                new DashRule("GTE", "RSR, Porsche, 911", "Proton, Iron Dames, Iron Lynx, GR Racing, Project 1, Porsche", "Porsche 911 RSR"),

                // ---------- LMGT3 ----------
                new DashRule("GT3", "M4, BMW", "WRT, BMW", "LMGT3 BMW M4"),
                new DashRule("GT3", "296, Ferrari", "AF Corse, Vista, JMW, Richard Mille, Ferrari", "LMGT3 Ferrari 296"),
                new DashRule("GT3", "911, Porsche", "Manthey, Iron Dames 2025, Porsche", "LMGT3 Porsche 911 GT3 R - standard size DDU"),
                new DashRule("GT3", "RC F, RCF, Lexus", "Akkodis, ASP, Lexus", "LMGT3 Lexus RC F GT3"),
                new DashRule("GT3", "720S, McLaren", "United Autosports, Inception, McLaren", "LMGT3 McLaren 720S"),
                new DashRule("GT3", "AMG, Mercedes", "Iron Lynx 2025, Iron Lynx 2026, Mercedes", "LMGT3 Mercedes AMG GT3"),
                new DashRule("GT3", "Z06, Corvette, Chevrolet", "TF Sport, Corvette", "LMGT3 Chevrolet Corvette Z06"),
                new DashRule("GT3", "Huracan, Lamborghini", "Iron Dames 2024, Iron Lynx 2024, Huracan, Lamborghini", "LMGT3 Lamborghini Huracan GT3 Evo 2"),
                new DashRule("GT3", "Vantage, Aston, AMR", "Heart of Racing, Racing Spirit, AMR GT3, Aston", "LMGT3 Aston Martin"),

                // ---------- LMP2 / LMP3 ----------
                new DashRule("LMP2", "", "", "LMP2 LMU", "Toutes les LMP2 (Oreca 07)"),
                new DashRule("LMP3", "Ginetta, G61", "Ginetta, G61", "LMP3 Ginetta G61"),
                new DashRule("LMP3", "Ligier, P325", "Ligier, P325", "LMP3 Ligier JS P325.simhubdash"),
                new DashRule("LMP3", "", "", "LMP3 Ligier JS P325.simhubdash", "Autres LMP3 (Duqueine, Adess...)"),
            };
        }
    }
}
