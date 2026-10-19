using System.Collections.Generic;

namespace AvixLmuDash
{
    public class AvixSettings
    {
        public bool Enabled { get; set; }
        public List<DashRule> Rules { get; set; }
        public int RulesVersion { get; set; }
        /// <summary>Dashboard utilisé quand aucune règle ne correspond (vide = ne rien changer)</summary>
        public string FallbackDashboard { get; set; }
        /// <summary>Clés des écrans à ne PAS piloter</summary>
        public List<string> DisabledDevices { get; set; }
        /// <summary>CarId dont l'assignation a été créée par le plugin</summary>
        public List<string> ManagedCarIds { get; set; }
        /// <summary>Ne pas écraser une assignation "par voiture" faite à la main dans SimHub</summary>
        public bool RespectManualAssignments { get; set; }
        /// <summary>Au démarrage : installer les polices/dashboards livrés avec le plugin s'il en manque</summary>
        public bool AutoInstallAssets { get; set; }
        public bool InstallFontsInWindows { get; set; }
        /// <summary>Propriétés d'autres plugins à utiliser pour reconnaître le modèle</summary>
        public List<string> ExtraModelProperties { get; set; }
        /// <summary>true = un seul dashboard « AVIX LMU Auto » qui contient toutes les voitures (recommandé).
        /// false = le plugin remplit la playlist par voiture de chaque écran.</summary>
        public bool UseMasterDashboard { get; set; }
        /// <summary>Format de l'écran pour le dashboard unique (VoCore 7,85" = 1280×400)</summary>
        public int ScreenWidth { get; set; }
        public int ScreenHeight { get; set; }
        /// <summary>true = écarte les éléments pour remplir l'écran ; false = garde les proportions avec bandes noires</summary>
        public bool AdaptLayout { get; set; }

        public AvixSettings()
        {
            UseMasterDashboard = true;
            ScreenWidth = 1280;
            ScreenHeight = 400;
            AdaptLayout = true;
            Enabled = true;
            RespectManualAssignments = true;
            AutoInstallAssets = true;
            InstallFontsInWindows = true;
        }

        public List<string> MasterDashboardCodes()
        {
            var list = new List<string>();
            if (Rules != null)
                foreach (var r in Rules)
                    if (r != null && r.Enabled && !string.IsNullOrWhiteSpace(r.Dashboard)) list.Add(r.Dashboard.Trim());
            if (!string.IsNullOrWhiteSpace(FallbackDashboard)) list.Add(FallbackDashboard.Trim());
            var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            var res = new List<string>();
            foreach (var c in list) if (seen.Add(c)) res.Add(c);
            return res;
        }

        public void EnsureDefaults()
        {
            if (Rules == null || Rules.Count == 0)
            {
                Rules = Matcher.DefaultRules();
                RulesVersion = Matcher.DefaultsVersion;
            }
            if (DisabledDevices == null) DisabledDevices = new List<string>();
            if (ManagedCarIds == null) ManagedCarIds = new List<string>();
            if (ExtraModelProperties == null)
                ExtraModelProperties = new List<string>
                {
                    "LMU_NeoRedPlugin.VehicleInfos.Name",
                    "LMU_NeoRedPlugin.VehicleInfos.Manufacturer",
                };
        }
    }
}
