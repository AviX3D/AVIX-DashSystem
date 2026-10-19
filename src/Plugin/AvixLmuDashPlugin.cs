using GameReaderCommon;
using SimHub.Plugins;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace AvixLmuDash
{
    [PluginDescription("Change automatiquement le dashboard de tes écrans selon la voiture que tu pilotes dans Le Mans Ultimate.")]
    [PluginAuthor("AVIX 3D")]
    [PluginName("AVIX LMU Dash Switcher")]
    public class AvixLmuDashPlugin : IPlugin, IDataPlugin, IWPFSettingsV2
    {
        public const string Version = "1.4.0";

        public PluginManager PluginManager { get; set; }
        public AvixSettings Settings;

        // État courant (lu par l'UI et exposé en propriétés SimHub)
        public volatile CarInfo CurrentCar = new CarInfo();
        public volatile MatchResult CurrentMatch;
        public volatile string CurrentDashboard = "";
        public volatile string Status = "En attente de Le Mans Ultimate…";
        public volatile string LastInstallReport = "";
        public volatile string LastMasterReport = "";
        volatile HashSet<string> masterIncluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly object masterLock = new object();

        public static string TemplatesDir { get { return Path.Combine(SimHubDir, "DashTemplates"); } }

        /// <summary>Code de dashboard à afficher dans « AVIX LMU Auto » ('' = écran d'attente).</summary>
        public string MasterSlot
        {
            get
            {
                var d = CurrentDashboard;
                if (string.IsNullOrEmpty(d)) return "";
                var set = masterIncluded;
                if (set.Contains(d)) return set.First(x => string.Equals(x, d, StringComparison.OrdinalIgnoreCase));
                return "";
            }
        }

        string lastKey;
        DateTime nextRawRead = DateTime.MinValue;
        DateTime nextApply = DateTime.MinValue;
        bool inLmu;

        public string LeftMenuTitle { get { return "AVIX LMU Dash"; } }

        public ImageSource PictureIcon { get { return MakeIcon(); } }

        public static string SimHubDir
        {
            get { return Path.GetDirectoryName(typeof(AvixLmuDashPlugin).Assembly.Location); }
        }

        public static string AssetsDir { get { return Path.Combine(SimHubDir, "AvixLmuDash"); } }

        public void Init(PluginManager pluginManager)
        {
            SimHub.Logging.Current.Info("[AVIX LMU Dash] Démarrage v" + Version);
            Settings = this.ReadCommonSettings<AvixSettings>("AvixLmuDashSettings", () => new AvixSettings());
            Settings.EnsureDefaults();

            this.AttachDelegate("CarId", () => CurrentCar.CarId);
            this.AttachDelegate("Team", () => CurrentCar.Team);
            this.AttachDelegate("CarClass", () => CurrentCar.NormalizedClass);
            this.AttachDelegate("VehicleModel", () => CurrentCar.VehicleModel);
            this.AttachDelegate("VehicleFile", () => CurrentCar.VehicleFile);
            this.AttachDelegate("Dashboard", () => CurrentDashboard);
            this.AttachDelegate("Status", () => Status);
            this.AttachDelegate("MasterSlot", () => MasterSlot);

            this.AddAction("ReapplyDashboard", (pm, a) => ForceReapply(), (pm, a) => { });

            masterIncluded = new HashSet<string>(MasterDashBuilder.ReadIncluded(TemplatesDir), StringComparer.OrdinalIgnoreCase);
            Task.Run(() =>
            {
                if (Settings.AutoInstallAssets) InstallAssets(false, true);
                if (Settings.UseMasterDashboard) EnsureMasterDashboard(false);
            });
        }

        /// <summary>(Re)construit le dashboard unique si les règles ou les dashboards ont changé.</summary>
        public MasterBuildResult EnsureMasterDashboard(bool force)
        {
            lock (masterLock)
            {
                var codes = Settings.MasterDashboardCodes();
                try
                {
                    var layout = MasterDashBuilder.LayoutKey(Settings.ScreenWidth, Settings.ScreenHeight, Settings.AdaptLayout);
                    if (!force && MasterDashBuilder.IsUpToDate(TemplatesDir, codes, layout))
                    {
                        masterIncluded = new HashSet<string>(MasterDashBuilder.ReadIncluded(TemplatesDir), StringComparer.OrdinalIgnoreCase);
                        return null;
                    }
                    var res = MasterDashBuilder.Build(TemplatesDir, codes, LoadResource("avix_logo_600.png"), Settings.ScreenWidth, Settings.ScreenHeight, Settings.AdaptLayout);
                    masterIncluded = new HashSet<string>(res.Included, StringComparer.OrdinalIgnoreCase);
                    LastMasterReport = res.Summary();
                    SimHub.Logging.Current.Info("[AVIX LMU Dash] " + LastMasterReport);
                    RunOnUi(() =>
                    {
                        DashApplier.RefreshDashboardList();
                        var item = DashApplier.GetInstalledDashboards().FirstOrDefault(i => string.Equals(i.Code, MasterDashBuilder.MasterCode, StringComparison.OrdinalIgnoreCase));
                        if (item != null) { try { item.ReloadEverywhere(); } catch { } }
                    });
                    return res;
                }
                catch (Exception ex)
                {
                    LastMasterReport = "Erreur création du dashboard unique : " + ex.Message;
                    SimHub.Logging.Current.Error("[AVIX LMU Dash] Master", ex);
                    return null;
                }
            }
        }

        public void End(PluginManager pluginManager)
        {
            SaveSettings();
        }

        public void SaveSettings()
        {
            try { if (Settings != null) this.SaveCommonSettings("AvixLmuDashSettings", Settings); }
            catch (Exception ex) { SimHub.Logging.Current.Error("[AVIX LMU Dash] Save", ex); }
        }

        public System.Windows.Controls.Control GetWPFSettingsControl(PluginManager pluginManager)
        {
            return new SettingsControl(this);
        }

        // ------------------------------------------------------------------ boucle de données

        public void DataUpdate(PluginManager pluginManager, ref GameData data)
        {
            try
            {
                if (Settings == null || !Settings.Enabled) return;
                if (data == null || !data.GameRunning || data.NewData == null || !DashApplier.IsLmu(data.GameName))
                {
                    if (inLmu) { inLmu = false; lastKey = null; Status = "En attente de Le Mans Ultimate…"; }
                    return;
                }
                inLmu = true;
                var nd = data.NewData;
                var carId = nd.CarId;
                if (string.IsNullOrEmpty(carId)) return;

                var now = DateTime.UtcNow;
                if (carId != CurrentCar.CarId || now >= nextRawRead)
                {
                    nextRawRead = now.AddMilliseconds(1000);
                    var info = ReadCar(pluginManager, nd);
                    if (info.Key != lastKey)
                    {
                        lastKey = info.Key;
                        CurrentCar = info;
                        Resolve(nd, false);
                        nextApply = now.AddSeconds(3);
                    }
                    else if (now >= nextApply)
                    {
                        // ré-essai périodique (écran branché après coup, appareil pas encore passé sur LMU…)
                        nextApply = now.AddSeconds(3);
                        Resolve(nd, false);
                    }
                }
            }
            catch (Exception ex)
            {
                Status = "Erreur : " + ex.Message;
                SimHub.Logging.Current.Error("[AVIX LMU Dash] DataUpdate", ex);
            }
        }

        void Resolve(StatusDataBase nd, bool force)
        {
            var car = CurrentCar;
            var m = Matcher.Match(Settings.Rules, car);
            CurrentMatch = m;
            string dash = m != null ? m.Dashboard : null;
            if (string.IsNullOrWhiteSpace(dash) && !string.IsNullOrWhiteSpace(Settings.FallbackDashboard))
                dash = Settings.FallbackDashboard.Trim();
            if (string.IsNullOrWhiteSpace(dash))
            {
                CurrentDashboard = "";
                Status = "Aucune règle ne correspond à cette voiture → dashboard inchangé.";
                return;
            }
            CurrentDashboard = dash;
            if (Settings.UseMasterDashboard)
            {
                Status = MasterSlot.Length > 0
                    ? "→ " + dash + "  (dans « " + MasterDashBuilder.MasterCode + " »)"
                    : "Le dash \"" + dash + "\" n'est pas dans « " + MasterDashBuilder.MasterCode + " » : clique sur « Regénérer ».";
                return;
            }
            var carSettings = nd != null ? nd.CarSettings : null;
            var carId = car.CarId;
            var team = car.Team;
            RunOnUi(() =>
            {
                var installed = DashApplier.GetInstalledDashboards();
                var item = installed.FirstOrDefault(i => string.Equals(i.Code, dash, StringComparison.OrdinalIgnoreCase));
                if (item == null)
                {
                    Status = "Le dashboard \"" + dash + "\" n'est pas installé dans SimHub.";
                    return;
                }
                var r = DashApplier.Apply(PluginManager, Settings, carId, team, carSettings, item.Code, force);
                if (r.Targets == 0) Status = "Aucun écran SimHub compatible trouvé.";
                else if (r.Error != null) Status = "Erreur : " + r.Error;
                else
                {
                    var parts = new List<string>();
                    if (r.Changed > 0) parts.Add(r.Changed + " écran(s) basculé(s)");
                    if (r.AlreadyOk > 0) parts.Add(r.AlreadyOk + " déjà OK");
                    if (r.SkippedManual > 0) parts.Add(r.SkippedManual + " ignoré(s) (assignation manuelle)");
                    if (r.NotInLmu > 0) parts.Add(r.NotInLmu + " pas encore sur LMU");
                    Status = "→ " + item.Code + " (" + string.Join(", ", parts) + ")";
                }
                if (r.Changed > 0)
                    SimHub.Logging.Current.Info("[AVIX LMU Dash] " + carId + " / modèle '" + car.VehicleModel + "' → " + item.Code);
            });
        }

        public void ForceReapply()
        {
            lastKey = null;
            var pm = PluginManager;
            StatusDataBase nd = null;
            try { nd = pm != null && pm.Status != null ? pm.Status.NewData : null; } catch { }
            if (nd != null && inLmu) Resolve(nd, true);
        }

        // ------------------------------------------------------------------ lecture voiture

        static readonly ConcurrentDictionary<string, MemberInfo> memberCache = new ConcurrentDictionary<string, MemberInfo>();

        static object GetMember(object o, string name)
        {
            if (o == null) return null;
            var t = o.GetType();
            var key = t.FullName + "::" + name;
            MemberInfo mi;
            if (!memberCache.TryGetValue(key, out mi))
            {
                mi = (MemberInfo)t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                     ?? t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                memberCache[key] = mi;
            }
            try
            {
                var f = mi as FieldInfo;
                if (f != null) return f.GetValue(o);
                var p = mi as PropertyInfo;
                if (p != null && p.GetIndexParameters().Length == 0) return p.GetValue(o, null);
            }
            catch { }
            return null;
        }

        static string BytesToString(object o)
        {
            var b = o as byte[];
            if (b == null) return o is string ? ((string)o).Trim() : "";
            int len = Array.IndexOf(b, (byte)0);
            if (len < 0) len = b.Length;
            if (len == 0) return "";
            try { return Encoding.UTF8.GetString(b, 0, len).Trim(); }
            catch { return Encoding.Default.GetString(b, 0, len).Trim(); }
        }

        CarInfo ReadCar(PluginManager pm, StatusDataBase nd)
        {
            var c = new CarInfo
            {
                CarId = nd.CarId ?? "",
                Team = nd.CarModel ?? "",
                SimHubClass = nd.CarClass ?? "",
            };
            object raw = null;
            try { raw = nd.GetRawDataObject(); } catch { }
            if (raw != null)
            {
                var tel = GetMember(raw, "PlayerNativeTelemetry");
                if (tel != null)
                {
                    c.VehicleModel = BytesToString(GetMember(tel, "mVehicleModel"));
                    var vc = GetMember(tel, "mVehicleClass");
                    if (vc != null)
                    {
                        var s = vc.ToString();
                        if (!s.Equals("Unknown", StringComparison.OrdinalIgnoreCase)) c.NativeClass = s;
                    }
                }
                var sc = GetMember(raw, "PlayerNativeScoring");
                if (sc != null)
                {
                    c.VehicleFile = BytesToString(GetMember(sc, "mVehFilename"));
                    c.VehicleName = BytesToString(GetMember(sc, "mVehicleName"));
                    if (string.IsNullOrEmpty(c.NativeClass)) c.NativeClass = BytesToString(GetMember(sc, "mVehicleClass"));
                }
            }
            if (Settings.ExtraModelProperties != null && Settings.ExtraModelProperties.Count > 0)
            {
                var extras = new List<string>();
                foreach (var p in Settings.ExtraModelProperties)
                {
                    if (string.IsNullOrWhiteSpace(p)) continue;
                    try
                    {
                        var v = pm.GetPropertyValue(p.Trim());
                        if (v != null && v.ToString().Trim().Length > 0) extras.Add(v.ToString().Trim());
                    }
                    catch { }
                }
                c.Extra = string.Join(" | ", extras);
            }
            return c;
        }

        // ------------------------------------------------------------------ polices / dashboards

        public InstallReport InstallAssets(bool overwriteDashboards, bool silentIfNothing)
        {
            var report = new InstallReport();
            try
            {
                FontInstaller.Install(Path.Combine(AssetsDir, "Fonts"), Path.Combine(SimHubDir, "DashFonts"),
                                      Settings.InstallFontsInWindows, report);
                DashInstaller.Install(Path.Combine(AssetsDir, "Dashboards"), Path.Combine(SimHubDir, "DashTemplates"),
                                      overwriteDashboards, report);
                if (report.DashboardsInstalled > 0) RunOnUi(DashApplier.RefreshDashboardList);
            }
            catch (Exception ex) { report.Errors.Add(ex.Message); }
            LastInstallReport = report.Summary();
            SimHub.Logging.Current.Info("[AVIX LMU Dash] " + LastInstallReport.Replace(Environment.NewLine, " "));
            bool somethingNew = report.FontsCopiedToDashFonts > 0 || report.FontsInstalledWindows > 0 || report.DashboardsInstalled > 0;
            if (somethingNew || !silentIfNothing)
            {
                try
                {
                    if (somethingNew)
                        RunOnUi(() =>
                        {
                            var tm = PluginManager != null ? PluginManager.ToastManager : null;
                            if (tm != null) tm.ShowInformation("AVIX LMU Dash : polices/dashboards installés. Redémarre SimHub pour que toutes les polices s'affichent.");
                        });
                }
                catch { }
            }
            return report;
        }

        // ------------------------------------------------------------------ utilitaires

        public static void RunOnUi(Action a)
        {
            try
            {
                var app = Application.Current;
                if (app == null || app.Dispatcher == null) { a(); return; }
                if (app.Dispatcher.CheckAccess()) a();
                else app.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try { a(); } catch (Exception ex) { SimHub.Logging.Current.Error("[AVIX LMU Dash] UI", ex); }
                }));
            }
            catch (Exception ex) { SimHub.Logging.Current.Error("[AVIX LMU Dash] RunOnUi", ex); }
        }

        public static byte[] LoadResource(string name)
        {
            try
            {
                using (var st = typeof(AvixLmuDashPlugin).Assembly.GetManifestResourceStream(name))
                {
                    if (st == null) return null;
                    var ms = new MemoryStream(); st.CopyTo(ms); return ms.ToArray();
                }
            }
            catch { return null; }
        }

        public static ImageSource LoadImage(string name)
        {
            try
            {
                var b = LoadResource(name);
                if (b == null) return null;
                var img = new System.Windows.Media.Imaging.BitmapImage();
                img.BeginInit();
                img.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                img.StreamSource = new MemoryStream(b);
                img.EndInit();
                img.Freeze();
                return img;
            }
            catch { return null; }
        }

        static ImageSource icon;
        static ImageSource MakeIcon()
        {
            if (icon != null) return icon;
            icon = LoadImage("avix_icon24.png");
            if (icon != null) return icon;
            try
            {
                var g = new DrawingGroup();
                var white = new SolidColorBrush(Colors.White);
                var pen = new Pen(white, 2.2);
                // cadran
                g.Children.Add(new GeometryDrawing(null, pen, Geometry.Parse("M 3,17 A 9,9 0 1 1 21,17")));
                // aiguille
                g.Children.Add(new GeometryDrawing(white, null, Geometry.Parse("M 11,16 L 17,8 L 13,17 Z")));
                // écran
                g.Children.Add(new GeometryDrawing(null, new Pen(white, 1.6), Geometry.Parse("M 4,19.5 L 20,19.5")));
                g.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 24, 24))));
                var img = new DrawingImage(g);
                img.Freeze();
                icon = img;
            }
            catch { }
            return icon;
        }
    }
}
