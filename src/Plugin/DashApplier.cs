using GameReaderCommon.GameSettings.Car;
using SimHub.Plugins;
using SimHub.Plugins.Devices;
using SimHub.Plugins.OutputPlugins.GraphicalDash;
using SimHub.Plugins.OutputPlugins.GraphicalDash.DashPlaylist;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace AvixLmuDash
{
    public class DashTarget
    {
        public string Key;
        public string Name;
        public DeviceInstance Device;
        public object Settings;
        public DashPlaylistManager Manager;
    }

    public class ApplyResult
    {
        public int Changed;
        public int AlreadyOk;
        public int SkippedManual;
        public int NotInLmu;
        public int Targets;
        public string Error;
    }

    /// <summary>
    /// Pilote les écrans SimHub en écrivant dans leur "playlist par voiture" (Per car playlist) pour LMU,
    /// exactement comme si on l'avait fait à la main dans l'onglet Appareils. SimHub fait ensuite le changement.
    /// </summary>
    public static class DashApplier
    {
        public const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        public static bool IsLmu(string gameCode)
        {
            if (string.IsNullOrEmpty(gameCode)) return false;
            var g = gameCode.ToUpperInvariant();
            return g == "LMU" || g.Contains("LEMANSULTIMATE");
        }

        public static List<GraphicalDashItem> GetInstalledDashboards()
        {
            try
            {
                var s = GraphicalDashPlugin.GetSettings();
                if (s == null || s.Items == null) return new List<GraphicalDashItem>();
                return s.Items.Where(i => i != null && !string.IsNullOrEmpty(i.Code)).ToList();
            }
            catch { return new List<GraphicalDashItem>(); }
        }

        public static void RefreshDashboardList()
        {
            try { var s = GraphicalDashPlugin.GetSettings(); if (s != null) s.RefreshList(); }
            catch (Exception ex) { SimHub.Logging.Current.Warn("[AVIX LMU Dash] RefreshList: " + ex.Message); }
        }

        static object GetSettingsWithPlaylist(DeviceInstance inst)
        {
            foreach (var p in inst.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (p.Name != "Settings" || p.GetIndexParameters().Length != 0) continue;
                object s;
                try { s = p.GetValue(inst, null); } catch { continue; }
                if (s == null) continue;
                var mp = s.GetType().GetProperty("DashPlaylistManager", BindingFlags.Public | BindingFlags.Instance);
                if (mp != null && typeof(DashPlaylistManager).IsAssignableFrom(mp.PropertyType)) return s;
            }
            return null;
        }

        public static List<DashTarget> EnumerateTargets(PluginManager pm)
        {
            var res = new List<DashTarget>();
            if (pm == null) return res;
            IEnumerable<DeviceInstance> devices;
            try { devices = pm.GetAllDevices(false).ToList(); }
            catch (Exception ex) { SimHub.Logging.Current.Warn("[AVIX LMU Dash] GetAllDevices: " + ex.Message); return res; }

            foreach (var dev in devices)
            {
                if (dev == null) continue;
                List<DeviceInstance> insts;
                try { insts = dev.GetInstances().ToList(); } catch { insts = new List<DeviceInstance> { dev }; }
                for (int i = 0; i < insts.Count; i++)
                {
                    var inst = insts[i];
                    if (inst == null) continue;
                    var s = GetSettingsWithPlaylist(inst);
                    if (s == null) continue;
                    DashPlaylistManager mgr = null;
                    try { mgr = s.GetType().GetProperty("DashPlaylistManager").GetValue(s, null) as DashPlaylistManager; } catch { }
                    if (mgr == null) continue;
                    string name;
                    try { name = dev.MainDisplayName; } catch { name = dev.GetType().Name; }
                    if (inst != dev)
                    {
                        try
                        {
                            var lbl = inst.GetType().GetProperty("CompositeLabel");
                            var l = lbl != null ? lbl.GetValue(inst, null) as string : null;
                            name += " – " + (string.IsNullOrEmpty(l) ? "écran " + (i + 1) : l);
                        }
                        catch { }
                    }
                    res.Add(new DashTarget { Key = dev.InstanceId + "#" + i, Name = name, Device = inst, Settings = s, Manager = mgr });
                }
            }
            return res;
        }

        static CarSelection MakeCarSelection(CarSettings carSettings, string carId, string carName)
        {
            CarSelection cs = null;
            if (carSettings != null)
            {
                try { cs = new CarSelection(carSettings); } catch { cs = null; }
            }
            if (cs == null) cs = new CarSelection();
            if (cs.CarId != carId) SetProp(cs, "CarId", carId);
            if (string.IsNullOrEmpty(cs.CarName)) SetProp(cs, "CarName", string.IsNullOrEmpty(carName) ? carId : carName);
            return cs;
        }

        static void SetProp(object o, string name, object value)
        {
            try
            {
                var p = o.GetType().GetProperty(name, All);
                var setter = p != null ? p.GetSetMethod(true) : null;
                if (setter != null) { setter.Invoke(o, new[] { value }); return; }
                var f = o.GetType().GetField("<" + name + ">k__BackingField", All);
                if (f != null) f.SetValue(o, value);
            }
            catch { }
        }

        /// <summary>Force SimHub à réévaluer la voiture au prochain frame.</summary>
        public static void Reapply(DashPlaylistManager mgr)
        {
            try
            {
                var m = typeof(DashPlaylistManager).GetMethod("ReapplyCarSettings", All, null, Type.EmptyTypes, null);
                if (m != null) { m.Invoke(mgr, null); return; }
            }
            catch { }
            try
            {
                var f = typeof(DashPlaylistManager).GetField("lastCarId", All);
                if (f != null) f.SetValue(mgr, null);
            }
            catch { }
        }

        static bool Same(string a, string b) { return string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase); }

        /// <summary>À appeler sur le thread UI.</summary>
        public static ApplyResult Apply(PluginManager pm, AvixSettings settings, string carId, string carName,
                                        CarSettings carSettings, string dashCode, bool force)
        {
            var r = new ApplyResult();
            if (string.IsNullOrEmpty(carId) || string.IsNullOrEmpty(dashCode)) return r;
            bool managed = settings.ManagedCarIds.Contains(carId);
            bool createdSomething = false;

            foreach (var t in EnumerateTargets(pm))
            {
                if (settings.DisabledDevices.Contains(t.Key)) continue;
                r.Targets++;
                try
                {
                    var ps = t.Manager.DashPlaylistSettings;
                    if (ps == null) continue;
                    if (!IsLmu(ps.CurrentGameCode)) { r.NotInLmu++; continue; }
                    var gp = ps.CurrentGamePlaylist;
                    if (gp == null || gp.CarDashes == null) { r.NotInLmu++; continue; }

                    var existing = gp.CarDashes.FirstOrDefault(cd => cd != null && cd.Cars != null && cd.Cars.Any(c => c != null && c.CarId == carId));
                    if (existing != null && !managed && !force && settings.RespectManualAssignments)
                    {
                        r.SkippedManual++;
                        continue;
                    }
                    if (existing != null && existing.Items.Count == 1 && existing.Items[0] != null &&
                        existing.Items[0].DashboardSelection != null && Same(existing.Items[0].DashboardSelection.Dashboard, dashCode))
                    {
                        r.AlreadyOk++;
                        if (force) Reapply(t.Manager);
                        continue;
                    }
                    if (existing == null)
                    {
                        existing = new CarDash();
                        existing.Cars.Add(MakeCarSelection(carSettings, carId, carName));
                        gp.CarDashes.Add(existing);
                    }
                    existing.Items.Clear();
                    var item = new PlaylistItem();
                    item.DashboardSelection.Dashboard = dashCode;
                    existing.Items.Add(item);
                    createdSomething = true;
                    Reapply(t.Manager);
                    r.Changed++;
                }
                catch (Exception ex)
                {
                    r.Error = t.Name + ": " + ex.Message;
                    SimHub.Logging.Current.Error("[AVIX LMU Dash] Apply " + t.Name, ex);
                }
            }
            if (createdSomething && !managed) settings.ManagedCarIds.Add(carId);
            return r;
        }

        static void SetBool(object o, string name, bool value)
        {
            try
            {
                var p = o.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (p != null && p.CanWrite && p.PropertyType == typeof(bool)) p.SetValue(o, value, null);
            }
            catch { }
        }

        static object GetVal(object o, string name)
        {
            try
            {
                var p = o.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                return p != null && p.GetIndexParameters().Length == 0 ? p.GetValue(o, null) : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// Met le dashboard unique sur les écrans cochés et active le tactile en mode simple
        /// (moitié gauche = page précédente, moitié droite = page suivante).
        /// </summary>
        public static string SetupScreensForMaster(PluginManager pm, AvixSettings settings, string masterCode, bool touch)
        {
            int screens = 0, touchOn = 0;
            var errors = new List<string>();
            foreach (var t in EnumerateTargets(pm))
            {
                if (settings.DisabledDevices.Contains(t.Key)) continue;
                try
                {
                    var s = t.Settings;
                    var cur = GetVal(s, "CurrentDashboard") as DashboardSelection;
                    if (cur == null) cur = GetVal(s, "Dashboard") as DashboardSelection;
                    var prop = s.GetType().GetProperty("CurrentDashboard", BindingFlags.Public | BindingFlags.Instance);
                    if (prop != null && prop.CanWrite)
                    {
                        var sel = new DashboardSelection();
                        sel.Dashboard = masterCode;
                        prop.SetValue(s, sel, null);
                    }
                    else if (cur != null) cur.Dashboard = masterCode;
                    screens++;

                    if (touch && Equals(GetVal(s, "SupportsTouch"), true))
                    {
                        SetBool(s, "EnableTouch", true);
                        // mode « Auto » : SimHub suit le mode préféré du dashboard (= simple) ; sinon on force « Simple »
                        if (!Equals(GetVal(s, "TouchModeAuto"), true)) SetBool(s, "TouchModeSimplified", true);
                        touchOn++;
                    }
                }
                catch (Exception ex) { errors.Add(t.Name + ": " + ex.Message); }
            }
            var r = screens + " écran(s) réglé(s) sur « " + masterCode + " »";
            if (touch) r += ", tactile activé sur " + touchOn;
            if (errors.Count > 0) r += " | erreurs : " + string.Join(" | ", errors);
            return r;
        }

        /// <summary>Supprime de tous les écrans les assignations LMU créées par le plugin.</summary>
        public static int RemoveManaged(PluginManager pm, AvixSettings settings)
        {
            int removed = 0;
            var ids = new HashSet<string>(settings.ManagedCarIds);
            foreach (var t in EnumerateTargets(pm))
            {
                try
                {
                    var ps = t.Manager.DashPlaylistSettings;
                    if (ps == null) continue;
                    var gpProp = typeof(DashPlaylistSettings).GetProperty("GamePlaylists", All);
                    var dict = gpProp != null ? gpProp.GetValue(ps, null) as Dictionary<string, GameDashPlaylist> : null;
                    var lists = new List<GameDashPlaylist>();
                    if (dict != null) lists.AddRange(dict.Where(kv => IsLmu(kv.Key)).Select(kv => kv.Value));
                    else if (IsLmu(ps.CurrentGameCode) && ps.CurrentGamePlaylist != null) lists.Add(ps.CurrentGamePlaylist);
                    foreach (var gp in lists)
                    {
                        if (gp == null || gp.CarDashes == null) continue;
                        foreach (var cd in gp.CarDashes.ToList())
                        {
                            if (cd != null && cd.Cars != null && cd.Cars.Count > 0 && cd.Cars.All(c => c != null && ids.Contains(c.CarId)))
                            {
                                gp.CarDashes.Remove(cd);
                                removed++;
                            }
                        }
                    }
                    Reapply(t.Manager);
                }
                catch (Exception ex) { SimHub.Logging.Current.Error("[AVIX LMU Dash] RemoveManaged", ex); }
            }
            settings.ManagedCarIds.Clear();
            return removed;
        }
    }
}
