using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading;

namespace AvixLmuDash
{
    /// <summary>Installe le plugin, les polices et les dashboards dans SimHub.</summary>
    static class Program
    {
        const string DllName = "AvixLmuDashSwitcher.dll";

        static int Main(string[] args)
        {
            try { Console.OutputEncoding = new System.Text.UTF8Encoding(false); } catch { }
            Console.Title = "AVIX LMU Dash Switcher - installation";
            bool quiet = args.Any(a => a.Equals("/quiet", StringComparison.OrdinalIgnoreCase));
            bool elevatedChild = args.Any(a => a.Equals("/elevated", StringComparison.OrdinalIgnoreCase));
            try
            {
                Line("==============================================");
                Line("  AVIX LMU Dash Switcher - installation");
                Line("==============================================");
                var here = AppDomain.CurrentDomain.BaseDirectory;
                var pkgDll = Path.Combine(here, "Plugin", DllName);
                var pkgAssets = Path.Combine(here, "Plugin", "AvixLmuDash");
                if (!File.Exists(pkgDll)) { Line("ERREUR : " + pkgDll + " introuvable. Dézippe tout le dossier avant de lancer l'installeur."); return Pause(1, quiet); }

                var simhub = args.FirstOrDefault(a => !a.Equals("/quiet", StringComparison.OrdinalIgnoreCase) && !a.Equals("/elevated", StringComparison.OrdinalIgnoreCase)) ?? FindSimHub();
                if (simhub == null || !File.Exists(Path.Combine(simhub, "SimHubWPF.exe")))
                {
                    Line("SimHub introuvable. Lance l'installeur avec le chemin, ex. :");
                    Line("   AvixInstaller.exe \"D:\\SimHub\"");
                    return Pause(1, quiet);
                }
                Line("SimHub trouvé : " + simhub);

                // SimHub doit être fermé pour charger le nouveau plugin
                var running = Process.GetProcessesByName("SimHubWPF");
                bool wasRunning = running.Length > 0;
                if (wasRunning)
                {
                    Line("SimHub est ouvert : fermeture…");
                    foreach (var p in running) { try { p.CloseMainWindow(); } catch { } }
                    for (int i = 0; i < 40 && Process.GetProcessesByName("SimHubWPF").Length > 0; i++) Thread.Sleep(500);
                    foreach (var p in Process.GetProcessesByName("SimHubWPF")) { try { p.Kill(); p.WaitForExit(5000); } catch { } }
                }

                try
                {
                    Install(simhub, pkgDll, pkgAssets);
                }
                catch (UnauthorizedAccessException)
                {
                    if (!IsAdmin())
                    {
                        Line("Droits administrateur nécessaires, relance en admin…");
                        var psi = new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName, "\"" + simhub + "\" /elevated")
                        { Verb = "runas", UseShellExecute = true };
                        try { Process.Start(psi).WaitForExit(); } catch { Line("Annulé."); return Pause(1, quiet); }
                        // les polices Windows doivent être installées pour TON compte : on le refait ici, sans admin
                        var rep2 = new InstallReport();
                        FontInstaller.Install(Path.Combine(pkgAssets, "Fonts"), Path.Combine(simhub, "DashFonts"), true, rep2);
                        Line("✓ Polices installées pour ton compte Windows.");
                        wasRunning = true; // pour relancer SimHub normalement (pas en admin)
                        goto launch;
                    }
                    throw;
                }

                if (elevatedChild) return Pause(0, false);
            launch:
                if (wasRunning || !quiet)
                {
                    Line("");
                    Line("Lancement de SimHub… Au premier démarrage, SimHub demande d'activer le nouveau plugin :");
                    Line("réponds OUI, puis va dans le menu de gauche « AVIX LMU Dash ».");
                    try { Process.Start(new ProcessStartInfo(Path.Combine(simhub, "SimHubWPF.exe")) { UseShellExecute = true, WorkingDirectory = simhub }); } catch { }
                }
                return Pause(0, quiet);
            }
            catch (Exception ex)
            {
                Line("ERREUR : " + ex);
                return Pause(2, quiet);
            }
        }

        static void Install(string simhub, string pkgDll, string pkgAssets)
        {
            // 1) plugin
            var dest = Path.Combine(simhub, DllName);
            File.Copy(pkgDll, dest, true);
            FontInstaller.Unblock(dest);
            Line("✓ Plugin copié : " + dest);

            // 2) données du plugin (polices + dashboards) dans SimHub\AvixLmuDash
            var assetsDest = Path.Combine(simhub, "AvixLmuDash");
            CopyDir(pkgAssets, assetsDest);
            Line("✓ Données copiées : " + assetsDest);

            // 3) polices
            var rep = new InstallReport();
            FontInstaller.Install(Path.Combine(assetsDest, "Fonts"), Path.Combine(simhub, "DashFonts"), true, rep);
            // 4) dashboards
            DashInstaller.Install(Path.Combine(assetsDest, "Dashboards"), Path.Combine(simhub, "DashTemplates"), false, rep);
            Line("✓ " + rep.Summary().Replace(Environment.NewLine, Environment.NewLine + "✓ "));
            if (rep.InstalledDashboards.Count > 0) Line("   Nouveaux dashboards : " + string.Join(", ", rep.InstalledDashboards));
            Line("");
            Line("Installation terminée.");
        }

        static void CopyDir(string src, string dst)
        {
            if (!Directory.Exists(src)) return;
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src))
            {
                var d = Path.Combine(dst, Path.GetFileName(f));
                File.Copy(f, d, true);
                FontInstaller.Unblock(d);
            }
            foreach (var sub in Directory.GetDirectories(src))
                CopyDir(sub, Path.Combine(dst, Path.GetFileName(sub)));
        }

        static string FindSimHub()
        {
            try
            {
                var p = Process.GetProcessesByName("SimHubWPF").FirstOrDefault();
                if (p != null) return Path.GetDirectoryName(p.MainModule.FileName);
            }
            catch { }
            var candidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "SimHub"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "SimHub"),
                @"C:\Program Files (x86)\SimHub", @"C:\SimHub", @"D:\SimHub"
            };
            foreach (var c in candidates) if (File.Exists(Path.Combine(c, "SimHubWPF.exe"))) return c;
            foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                foreach (var path in new[] { @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall" })
                {
                    try
                    {
                        using (var k = root.OpenSubKey(path))
                        {
                            if (k == null) continue;
                            foreach (var n in k.GetSubKeyNames())
                            {
                                using (var sk = k.OpenSubKey(n))
                                {
                                    var name = sk != null ? sk.GetValue("DisplayName") as string : null;
                                    var loc = sk != null ? sk.GetValue("InstallLocation") as string : null;
                                    if (name != null && name.IndexOf("SimHub", StringComparison.OrdinalIgnoreCase) >= 0 && !string.IsNullOrEmpty(loc) &&
                                        File.Exists(Path.Combine(loc, "SimHubWPF.exe"))) return loc.TrimEnd('\\');
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
            return null;
        }

        static bool IsAdmin()
        {
            try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
            catch { return false; }
        }

        static void Line(string s) { Console.WriteLine(s); }

        static int Pause(int code, bool quiet)
        {
            if (!quiet) { Console.WriteLine(); Console.WriteLine("Appuie sur une touche pour fermer…"); try { Console.ReadKey(true); } catch { } }
            return code;
        }
    }
}
