using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace AvixLmuDash
{
    public class InstallReport
    {
        public int FontsCopiedToDashFonts;
        public int FontsInstalledWindows;
        public int FontsAlreadyPresent;
        public int DashboardsInstalled;
        public int DashboardsSkipped;
        public List<string> InstalledDashboards = new List<string>();
        public List<string> Errors = new List<string>();

        public string Summary()
        {
            var sb = new StringBuilder();
            sb.AppendFormat("Polices : {0} copiées dans SimHub\\DashFonts, {1} installées dans Windows, {2} déjà présentes.",
                FontsCopiedToDashFonts, FontsInstalledWindows, FontsAlreadyPresent);
            sb.AppendLine();
            sb.AppendFormat("Dashboards : {0} installés, {1} déjà présents (conservés).", DashboardsInstalled, DashboardsSkipped);
            if (Errors.Count > 0)
            {
                sb.AppendLine();
                sb.Append("Erreurs : " + string.Join(" | ", Errors.Take(8)));
            }
            return sb.ToString();
        }
    }

    public static class FontInstaller
    {
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        static extern int AddFontResource(string lpFileName);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        const uint WM_FONTCHANGE = 0x001D;
        static readonly IntPtr HWND_BROADCAST = new IntPtr(0xffff);

        public static bool IsFontFile(string path)
        {
            var e = Path.GetExtension(path).ToLowerInvariant();
            return e == ".ttf" || e == ".otf" || e == ".ttc";
        }

        static string WindowsFontsDir
        {
            get { return Environment.GetFolderPath(Environment.SpecialFolder.Fonts); }
        }

        static string UserFontsDir
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Microsoft", "Windows", "Fonts");
            }
        }

        /// <summary>
        /// Copie les polices de srcDir dans SimHub\DashFonts (utilisé par Dash Studio)
        /// et les installe pour l'utilisateur Windows courant (pas besoin d'être admin).
        /// </summary>
        public static void Install(string srcDir, string dashFontsDir, bool alsoWindows, InstallReport report)
        {
            if (!Directory.Exists(srcDir)) return;
            try { Directory.CreateDirectory(dashFontsDir); } catch (Exception ex) { report.Errors.Add("DashFonts: " + ex.Message); }

            bool anyWindows = false;
            foreach (var file in Directory.GetFiles(srcDir).Where(IsFontFile))
            {
                var name = Path.GetFileName(file);
                // 1) SimHub\DashFonts
                try
                {
                    var dest = Path.Combine(dashFontsDir, name);
                    if (!File.Exists(dest))
                    {
                        File.Copy(file, dest, false);
                        Unblock(dest);
                        report.FontsCopiedToDashFonts++;
                    }
                    else report.FontsAlreadyPresent++;
                }
                catch (Exception ex) { report.Errors.Add(name + ": " + ex.Message); }

                // 2) Windows (utilisateur courant)
                if (!alsoWindows) continue;
                try
                {
                    if (File.Exists(Path.Combine(WindowsFontsDir, name))) continue;
                    var userDir = UserFontsDir;
                    Directory.CreateDirectory(userDir);
                    var dest = Path.Combine(userDir, name);
                    if (!File.Exists(dest))
                    {
                        File.Copy(file, dest, false);
                        Unblock(dest);
                    }
                    var valueName = Path.GetFileNameWithoutExtension(name) +
                                    (Path.GetExtension(name).ToLowerInvariant() == ".otf" ? " (OpenType)" : " (TrueType)");
                    using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\Fonts"))
                    {
                        if (key != null && !string.Equals(key.GetValue(valueName) as string, dest, StringComparison.OrdinalIgnoreCase))
                        {
                            key.SetValue(valueName, dest);
                            report.FontsInstalledWindows++;
                        }
                    }
                    try { AddFontResource(dest); anyWindows = true; } catch { }
                }
                catch (Exception ex) { report.Errors.Add("Windows " + name + ": " + ex.Message); }
            }
            if (anyWindows)
            {
                try { PostMessage(HWND_BROADCAST, WM_FONTCHANGE, IntPtr.Zero, IntPtr.Zero); } catch { }
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool DeleteFile(string name);

        /// <summary>Retire la marque "fichier téléchargé d'Internet" (Zone.Identifier).</summary>
        public static void Unblock(string path)
        {
            try { DeleteFile(path + ":Zone.Identifier"); } catch { }
        }
    }

    public static class DashInstaller
    {
        public static string GetRootFolder(ZipArchive zip)
        {
            foreach (var e in zip.Entries)
            {
                var n = e.FullName.Replace('/', '\\');
                var i = n.IndexOf('\\');
                if (i > 0) return n.Substring(0, i);
            }
            return null;
        }

        /// <summary>Installe chaque .simhubdash de srcDir dans SimHub\DashTemplates (sans écraser l'existant).</summary>
        public static void Install(string srcDir, string dashTemplatesDir, bool overwrite, InstallReport report)
        {
            if (!Directory.Exists(srcDir)) return;
            var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.GetFiles(srcDir, "*.simhubdash").OrderBy(f => f.Length))
            {
                try
                {
                    using (var zip = ZipFile.OpenRead(file))
                    {
                        var root = GetRootFolder(zip);
                        if (string.IsNullOrEmpty(root) || done.Contains(root)) continue;
                        done.Add(root);
                        var target = Path.Combine(dashTemplatesDir, root);
                        if (Directory.Exists(target) && !overwrite) { report.DashboardsSkipped++; continue; }

                        var fullTarget = Path.GetFullPath(dashTemplatesDir);
                        foreach (var e in zip.Entries)
                        {
                            var rel = e.FullName.Replace('/', '\\').TrimStart('\\');
                            if (rel.EndsWith("\\")) continue;
                            var dest = Path.GetFullPath(Path.Combine(dashTemplatesDir, rel.Replace('\\', Path.DirectorySeparatorChar)));
                            if (!dest.StartsWith(fullTarget, StringComparison.OrdinalIgnoreCase)) continue; // sécurité
                            Directory.CreateDirectory(Path.GetDirectoryName(dest));
                            e.ExtractToFile(dest, true);
                        }
                        report.DashboardsInstalled++;
                        report.InstalledDashboards.Add(root);
                    }
                }
                catch (Exception ex) { report.Errors.Add(Path.GetFileName(file) + ": " + ex.Message); }
            }
        }
    }
}
