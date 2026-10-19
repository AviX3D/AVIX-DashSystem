using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace AvixLmuDash
{
    /// <summary>Interface de réglages (construite en code, pas de XAML).</summary>
    public class SettingsControl : UserControl
    {
        readonly AvixLmuDashPlugin plugin;
        readonly ObservableCollection<DashRule> rules;
        readonly ObservableCollection<string> dashCodes = new ObservableCollection<string>();
        readonly DispatcherTimer timer;

        TextBlock tCarId, tTeam, tClass, tModel, tFile, tDash, tRule, tStatus, tFonts, tInstall, tMaster;
        DataGrid grid;
        ComboBox cbForce, cbFallback;
        StackPanel devicesPanel;

        static readonly string[] Classes = { "", "HYPER", "LMP2", "LMP3", "GTE", "GT3" };

        public SettingsControl(AvixLmuDashPlugin plugin)
        {
            this.plugin = plugin;
            var s = plugin.Settings;
            rules = new ObservableCollection<DashRule>(s.Rules);
            rules.CollectionChanged += (o, e) => SyncRules();

            try { Resources.MergedDictionaries.Add(Theme.Create()); }
            catch (Exception ex) { SimHub.Logging.Current.Error("[AVIX LMU Dash] Thème", ex); }
            Background = Theme.Brush(Theme.Bg);
            Foreground = Theme.Brush(Theme.Text);
            FontFamily = Theme.BodyFont;
            FontSize = 13;

            var root = new StackPanel { Margin = new Thickness(22, 18, 22, 22), MaxWidth = 1240, HorizontalAlignment = HorizontalAlignment.Stretch };
            Content = new ScrollViewer { Content = root, Background = Theme.Brush(Theme.Bg), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

            // ---------------- en-tête façon avix3d.com
            var header = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var logo = AvixLmuDashPlugin.LoadImage("avix_logo_600.png");
            if (logo != null)
            {
                var li = new System.Windows.Controls.Image { Source = logo, Height = 74, Margin = new Thickness(0, 0, 22, 0), VerticalAlignment = VerticalAlignment.Center };
                RenderOptions.SetBitmapScalingMode(li, BitmapScalingMode.HighQuality);
                header.Children.Add(li);
            }
            var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(titles, 1);
            var t1 = new TextBlock { FontFamily = Theme.TitleFont, FontWeight = FontWeights.ExtraBold, FontSize = 26 };
            t1.Inlines.Add(new System.Windows.Documents.Run("LMU ") { Foreground = Theme.Brush(Theme.Text) });
            t1.Inlines.Add(new System.Windows.Documents.Run("DASH") { Foreground = Theme.Brush(Theme.Accent) });
            t1.Inlines.Add(new System.Windows.Documents.Run(" SWITCHER") { Foreground = Theme.Brush(Theme.Text) });
            titles.Children.Add(t1);
            var sub = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            sub.Children.Add(Chip("v" + AvixLmuDashPlugin.Version));
            sub.Children.Add(new TextBlock
            {
                Text = "Le bon dashboard pour chaque voiture de Le Mans Ultimate, sur tous tes écrans.",
                Foreground = Theme.Brush(Theme.Muted), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), TextWrapping = TextWrapping.Wrap
            });
            titles.Children.Add(sub);
            header.Children.Add(titles);
            root.Children.Add(header);
            // barre d'accent dégradée (comme le bandeau du site)
            root.Children.Add(new Border
            {
                Height = 3, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 0, 16),
                Background = new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#8A8A05"), (Color)ColorConverter.ConvertFromString(Theme.Accent), 0)
            });

            var en = new CheckBox { Content = "Activer le changement automatique", IsChecked = s.Enabled, Margin = new Thickness(2, 0, 0, 10), FontWeight = FontWeights.Bold, FontSize = 14 };
            en.Checked += (o, e) => { s.Enabled = true; plugin.SaveSettings(); };
            en.Unchecked += (o, e) => { s.Enabled = false; plugin.SaveSettings(); };
            root.Children.Add(en);

            // ---------------- dashboard unique
            var mp = new StackPanel();
            mp.Children.Add(Wrap("Un seul dashboard, « " + MasterDashBuilder.MasterCode + " », contient toutes tes voitures : mets-le sur tous tes écrans, " +
                                 "il affiche tout seul le dash de la voiture pilotée. Tactile VoCore : touche la moitié GAUCHE de l'écran = page précédente, " +
                                 "moitié DROITE = page suivante (pour les dashs qui ont plusieurs pages).", 0.85));
            var useMaster = new CheckBox { Content = "Utiliser le dashboard unique (recommandé)", IsChecked = s.UseMasterDashboard, Margin = new Thickness(0, 6, 0, 4), FontWeight = FontWeights.SemiBold };
            useMaster.Checked += (o, e) => { s.UseMasterDashboard = true; plugin.SaveSettings(); RegenMaster(false); };
            useMaster.Unchecked += (o, e) => { s.UseMasterDashboard = false; plugin.SaveSettings(); };
            mp.Children.Add(useMaster);
            var fmt = new WrapPanel { Margin = new Thickness(0, 4, 0, 6) };
            fmt.Children.Add(new TextBlock { Text = "FORMAT DE L'ÉCRAN", FontFamily = Theme.LabelFont, FontWeight = FontWeights.Bold, Foreground = Theme.Brush(Theme.Muted), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
            var presets = new[]
            {
                new KeyValuePair<string, int[]>("VoCore 7,85\"  —  1280 × 400", new[] { 1280, 400 }),
                new KeyValuePair<string, int[]>("800 × 480 (VoCore 4,3\" / 5\")", new[] { 800, 480 }),
                new KeyValuePair<string, int[]>("854 × 480", new[] { 854, 480 }),
                new KeyValuePair<string, int[]>("1024 × 600", new[] { 1024, 600 }),
                new KeyValuePair<string, int[]>("1280 × 480", new[] { 1280, 480 }),
                new KeyValuePair<string, int[]>("1280 × 720", new[] { 1280, 720 }),
                new KeyValuePair<string, int[]>("1280 × 800 (10\")", new[] { 1280, 800 }),
                new KeyValuePair<string, int[]>("1920 × 720", new[] { 1920, 720 }),
            };
            var cbFmt = new ComboBox { Width = 280, DisplayMemberPath = "Key", ItemsSource = presets };
            cbFmt.SelectedItem = presets.FirstOrDefault(p => p.Value[0] == s.ScreenWidth && p.Value[1] == s.ScreenHeight);
            if (cbFmt.SelectedItem == null) cbFmt.SelectedIndex = 0;
            cbFmt.SelectionChanged += (o, e) =>
            {
                var kv = (KeyValuePair<string, int[]>)cbFmt.SelectedItem;
                if (kv.Value[0] == s.ScreenWidth && kv.Value[1] == s.ScreenHeight) return;
                s.ScreenWidth = kv.Value[0]; s.ScreenHeight = kv.Value[1]; plugin.SaveSettings(); RegenMaster(false);
            };
            fmt.Children.Add(cbFmt);
            mp.Children.Add(fmt);
            var adapt = new CheckBox { Content = "Remplir tout l'écran : écarter les blocs au lieu de laisser des bandes noires (rien n'est déformé)", IsChecked = s.AdaptLayout };
            adapt.Checked += (o, e) => { s.AdaptLayout = true; plugin.SaveSettings(); RegenMaster(false); };
            adapt.Unchecked += (o, e) => { s.AdaptLayout = false; plugin.SaveSettings(); RegenMaster(false); };
            mp.Children.Add(adapt);
            var mb = new WrapPanel();
            mb.Children.Add(BtnPrimary("Mettre « " + MasterDashBuilder.MasterCode + " » sur mes écrans + activer le tactile", (o, e) =>
            {
                if (MessageBox.Show("Remplacer le dashboard actuel des écrans cochés (bloc « Écrans ») par « " + MasterDashBuilder.MasterCode + " » et activer leur tactile ?",
                                    "AVIX LMU Dash", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                plugin.EnsureMasterDashboard(false);
                if (s.ManagedCarIds.Count > 0) DashApplier.RemoveManaged(plugin.PluginManager, s);
                var r = DashApplier.SetupScreensForMaster(plugin.PluginManager, s, MasterDashBuilder.MasterCode, true);
                plugin.SaveSettings();
                tMaster.Text = r;
                RefreshDevices();
            }));
            mb.Children.Add(Btn("Regénérer le dashboard unique", (o, e) => RegenMaster(true)));
            mp.Children.Add(mb);
            tMaster = Wrap(plugin.LastMasterReport, 0.85);
            mp.Children.Add(tMaster);
            root.Children.Add(Group("Dashboard unique « " + MasterDashBuilder.MasterCode + " »", mp));

            // ---------------- voiture actuelle
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            tCarId = AddRow(g, "ID voiture (SimHub)");
            tTeam = AddRow(g, "Équipe / livrée");
            tClass = AddRow(g, "Classe");
            tModel = AddRow(g, "Modèle détecté (LMU)");
            tFile = AddRow(g, "Fichier véhicule");
            tDash = AddRow(g, "Dashboard choisi");
            tRule = AddRow(g, "Règle utilisée");
            tStatus = AddRow(g, "État");
            tDash.Foreground = Theme.Brush(Theme.Accent); tDash.FontFamily = Theme.TitleFont; tDash.FontWeight = FontWeights.ExtraBold;
            var carPanel = new StackPanel();
            carPanel.Children.Add(g);

            var forceRow = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            var reapply = Btn("Réappliquer maintenant", (o, e) => plugin.ForceReapply());
            forceRow.Children.Add(reapply);
            cbForce = new ComboBox { Width = 300, IsEditable = true, ItemsSource = dashCodes, Margin = new Thickness(12, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
            forceRow.Children.Add(cbForce);
            forceRow.Children.Add(BtnPrimary("Utiliser ce dash pour cette voiture", (o, e) => ForceForCurrent()));
            carPanel.Children.Add(forceRow);
            carPanel.Children.Add(Wrap("Astuce : si une voiture tombe sur le mauvais dash, choisis le bon ci-dessus et clique sur le bouton : " +
                                       "une règle est ajoutée en haut de la liste avec le modèle détecté.", 0.7));
            root.Children.Add(Group("Voiture actuelle", carPanel));

            // ---------------- règles
            var rulesPanel = new StackPanel();
            rulesPanel.Children.Add(Wrap("Lues de haut en bas, la première qui correspond gagne. « Mots-clés modèle » sont cherchés dans le modèle " +
                                         "détecté (le plus fiable) ; « Mots-clés équipe » servent de secours dans l'ID / le nom d'équipe. " +
                                         "Une règle sans mot-clé = toutes les voitures de la classe. Séparer les mots-clés par des virgules.", 0.8));
            grid = BuildGrid();
            rulesPanel.Children.Add(grid);
            var rb = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            rb.Children.Add(Btn("Ajouter", (o, e) => { var r = new DashRule("", "", "", ""); rules.Insert(0, r); grid.SelectedItem = r; }));
            rb.Children.Add(Btn("Supprimer", (o, e) => { var r = grid.SelectedItem as DashRule; if (r != null) rules.Remove(r); }));
            rb.Children.Add(Btn("▲ Monter", (o, e) => Move(-1)));
            rb.Children.Add(Btn("▼ Descendre", (o, e) => Move(1)));
            rb.Children.Add(Btn("Remettre les règles par défaut", (o, e) =>
            {
                if (MessageBox.Show("Remplacer toutes les règles par celles d'origine ?", "AVIX LMU Dash", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                rules.Clear(); foreach (var r in Matcher.DefaultRules()) rules.Add(r);
            }));
            rulesPanel.Children.Add(rb);

            var fb = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            fb.Children.Add(new TextBlock { Text = "Si aucune règle ne correspond : ", VerticalAlignment = VerticalAlignment.Center });
            cbFallback = new ComboBox { Width = 300, IsEditable = true, ItemsSource = dashCodes, Text = s.FallbackDashboard ?? "" };
            cbFallback.LostFocus += (o, e) => { s.FallbackDashboard = cbFallback.Text; plugin.SaveSettings(); };
            fb.Children.Add(cbFallback);
            fb.Children.Add(new TextBlock { Text = "  (vide = garder le dash actuel)", Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center });
            rulesPanel.Children.Add(fb);
            root.Children.Add(Group("Règles voiture → dashboard", rulesPanel));

            // ---------------- écrans
            var devPanel = new StackPanel();
            devPanel.Children.Add(Wrap("Écrans que le plugin a le droit de piloter :", 0.8));
            devicesPanel = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
            devPanel.Children.Add(devicesPanel);
            var devButtons = new WrapPanel();
            devButtons.Children.Add(Btn("Actualiser la liste", (o, e) => RefreshDevices()));
            devButtons.Children.Add(Btn("Supprimer les assignations créées par le plugin", (o, e) =>
            {
                if (MessageBox.Show("Retirer de tous les écrans les assignations LMU « par voiture » créées par le plugin ?",
                                    "AVIX LMU Dash", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                var n = DashApplier.RemoveManaged(plugin.PluginManager, s);
                plugin.SaveSettings();
                MessageBox.Show(n + " assignation(s) supprimée(s).", "AVIX LMU Dash");
            }));
            devPanel.Children.Add(devButtons);
            var respect = new CheckBox { Content = "Ne pas écraser les assignations « par voiture » que j'ai faites à la main", IsChecked = s.RespectManualAssignments, Margin = new Thickness(0, 8, 0, 0) };
            respect.Checked += (o, e) => { s.RespectManualAssignments = true; plugin.SaveSettings(); };
            respect.Unchecked += (o, e) => { s.RespectManualAssignments = false; plugin.SaveSettings(); };
            devPanel.Children.Add(respect);
            root.Children.Add(Group("Écrans", devPanel));

            // ---------------- polices & dashboards
            var fontPanel = new StackPanel();
            fontPanel.Children.Add(Wrap("Le plugin embarque toutes les polices de tes dashboards (+ Roboto et Cascadia Code téléchargées). " +
                                        "Elles sont copiées dans SimHub\\DashFonts et installées dans Windows pour ton compte (pas besoin d'admin).", 0.8));
            var fbtn = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            fbtn.Children.Add(BtnPrimary("Installer polices + dashboards", (o, e) => RunInstall(false)));
            fbtn.Children.Add(Btn("Réinstaller les dashboards (écrase)", (o, e) =>
            {
                if (MessageBox.Show("Écraser les dashboards déjà présents par la version livrée avec le plugin ?", "AVIX LMU Dash", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                    RunInstall(true);
            }));
            fbtn.Children.Add(Btn("Vérifier les polices", (o, e) => CheckFonts()));
            fontPanel.Children.Add(fbtn);
            tInstall = Wrap(plugin.LastInstallReport, 0.85);
            fontPanel.Children.Add(tInstall);
            tFonts = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
            fontPanel.Children.Add(tFonts);
            var auto = new CheckBox { Content = "Au démarrage de SimHub, installer automatiquement ce qui manque", IsChecked = s.AutoInstallAssets, Margin = new Thickness(0, 8, 0, 0) };
            auto.Checked += (o, e) => { s.AutoInstallAssets = true; plugin.SaveSettings(); };
            auto.Unchecked += (o, e) => { s.AutoInstallAssets = false; plugin.SaveSettings(); };
            fontPanel.Children.Add(auto);
            var win = new CheckBox { Content = "Installer aussi les polices dans Windows", IsChecked = s.InstallFontsInWindows };
            win.Checked += (o, e) => { s.InstallFontsInWindows = true; plugin.SaveSettings(); };
            win.Unchecked += (o, e) => { s.InstallFontsInWindows = false; plugin.SaveSettings(); };
            fontPanel.Children.Add(win);
            root.Children.Add(Group("Polices & dashboards", fontPanel));

            // ---------------- avancé
            var adv = new StackPanel();
            adv.Children.Add(Wrap("Propriétés d'autres plugins utilisées aussi pour reconnaître le modèle (une par ligne) :", 0.8));
            var extra = new TextBox
            {
                AcceptsReturn = true, MinHeight = 44, TextWrapping = TextWrapping.NoWrap,
                Text = string.Join(Environment.NewLine, s.ExtraModelProperties ?? new List<string>())
            };
            extra.LostFocus += (o, e) =>
            {
                s.ExtraModelProperties = extra.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                plugin.SaveSettings();
            };
            adv.Children.Add(extra);
            adv.Children.Add(Wrap("Propriétés exposées par le plugin : AvixLmuDashPlugin.VehicleModel, .Dashboard, .CarClass, .Status… " +
                                  "Action mappable : AvixLmuDashPlugin.ReapplyDashboard.", 0.7));
            root.Children.Add(Group("Avancé", adv));

            Loaded += (o, e) => { RefreshDashCodes(); RefreshDevices(); Tick(); timer.Start(); };
            Unloaded += (o, e) => { timer.Stop(); SyncRules(); if (s.UseMasterDashboard) Task.Run(() => plugin.EnsureMasterDashboard(false)); };
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            timer.Tick += (o, e) => Tick();
        }

        // ------------------------------------------------------------------ construction UI

        static TextBlock Wrap(string text, double opacity)
        {
            return new TextBlock
            {
                Text = text ?? "", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 6), LineHeight = 19,
                Foreground = Theme.Brush(opacity >= 0.85 ? Theme.Text : Theme.Muted)
            };
        }

        static Border Chip(string text)
        {
            return new Border
            {
                Background = Theme.Brush(Theme.Accent), CornerRadius = new CornerRadius(20), Padding = new Thickness(9, 1, 9, 2), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = text, Foreground = Brushes.Black, FontFamily = Theme.LabelFont, FontWeight = FontWeights.Bold, FontSize = 13 }
            };
        }

        static Button Btn(string text, RoutedEventHandler click)
        {
            var b = new Button { Content = text };
            b.Click += click;
            return b;
        }

        Button BtnPrimary(string text, RoutedEventHandler click)
        {
            var b = Btn(text, click);
            var st = TryFindResource("AvxPrimary") as Style;
            if (st != null) b.Style = st;
            return b;
        }

        /// <summary>Carte façon « product-card » du site : fond #141414, bord #2a2a2a, coins 14 px, titre Orbitron + trait jaune.</summary>
        static Border Group(string header, UIElement content)
        {
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock
            {
                Text = header.ToUpperInvariant(), FontFamily = Theme.TitleFont, FontWeight = FontWeights.ExtraBold, FontSize = 15,
                Foreground = Theme.Brush(Theme.Text), TextWrapping = TextWrapping.Wrap
            });
            sp.Children.Add(new Border { Width = 34, Height = 3, CornerRadius = new CornerRadius(3), Background = Theme.Brush(Theme.Accent), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 7, 0, 12) });
            sp.Children.Add(content);
            return new Border
            {
                Background = Theme.Brush(Theme.Surface), BorderBrush = Theme.Brush(Theme.Border), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14), Padding = new Thickness(20, 18, 20, 18), Margin = new Thickness(0, 0, 0, 14), Child = sp
            };
        }

        static TextBlock AddRow(Grid g, string label)
        {
            int row = g.RowDefinitions.Count;
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var l = new TextBlock
            {
                Text = label.ToUpperInvariant(), Foreground = Theme.Brush(Theme.Muted), FontFamily = Theme.LabelFont, FontWeight = FontWeights.Bold,
                FontSize = 13, Margin = new Thickness(0, 4, 12, 4), VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(l, row); g.Children.Add(l);
            var v = new TextBlock { Text = "–", TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, Foreground = Theme.Brush(Theme.Text), Margin = new Thickness(0, 4, 0, 4), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(v, row); Grid.SetColumn(v, 1); g.Children.Add(v);
            return v;
        }

        DataGrid BuildGrid()
        {
            var dg = new DataGrid
            {
                AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, CanUserSortColumns = false,
                ItemsSource = rules, MaxHeight = 420, SelectionMode = DataGridSelectionMode.Single,
                HeadersVisibility = DataGridHeadersVisibility.Column, Margin = new Thickness(0, 6, 0, 0)
            };
            dg.Columns.Add(new DataGridCheckBoxColumn { Header = "Actif", Binding = new Binding("Enabled") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged } });
            dg.Columns.Add(new DataGridComboBoxColumn { Header = "Classe", ItemsSource = Classes, SelectedItemBinding = new Binding("CarClass"), Width = 70 });
            dg.Columns.Add(new DataGridTextColumn { Header = "Mots-clés modèle", Binding = new Binding("ModelKeywords"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            dg.Columns.Add(new DataGridTextColumn { Header = "Mots-clés équipe (secours)", Binding = new Binding("TeamKeywords"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });

            var f = new FrameworkElementFactory(typeof(ComboBox));
            f.SetValue(ComboBox.IsEditableProperty, true);
            f.SetValue(ComboBox.ItemsSourceProperty, dashCodes);
            f.SetBinding(ComboBox.TextProperty, new Binding("Dashboard") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus });
            dg.Columns.Add(new DataGridTemplateColumn { Header = "Dashboard", CellTemplate = new DataTemplate { VisualTree = f }, Width = new DataGridLength(1.4, DataGridLengthUnitType.Star) });
            dg.CellEditEnding += (o, e) => Dispatcher.BeginInvoke(new Action(SyncRules), DispatcherPriority.Background);
            dg.LostFocus += (o, e) => SyncRules();
            return dg;
        }

        // ------------------------------------------------------------------ logique UI

        void SyncRules()
        {
            plugin.Settings.Rules = rules.ToList();
            plugin.SaveSettings();
        }

        void Move(int dir)
        {
            var r = grid.SelectedItem as DashRule;
            if (r == null) return;
            int i = rules.IndexOf(r), j = i + dir;
            if (j < 0 || j >= rules.Count) return;
            rules.Move(i, j);
            grid.SelectedItem = r;
            grid.ScrollIntoView(r);
        }

        void RefreshDashCodes()
        {
            var codes = DashApplier.GetInstalledDashboards().Select(d => d.Code).Where(c => !c.Equals(MasterDashBuilder.MasterCode, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase)
                                   .OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList();
            dashCodes.Clear();
            foreach (var c in codes) dashCodes.Add(c);
        }

        void RefreshDevices()
        {
            devicesPanel.Children.Clear();
            var s = plugin.Settings;
            var targets = DashApplier.EnumerateTargets(plugin.PluginManager);
            if (targets.Count == 0)
            {
                devicesPanel.Children.Add(Wrap("Aucun écran avec dashboard trouvé dans l'onglet Appareils.", 0.8));
                return;
            }
            foreach (var t in targets)
            {
                var key = t.Key;
                string cur = "";
                try
                {
                    var cd = t.Settings.GetType().GetProperty("CurrentDashboard");
                    var sel = cd != null ? cd.GetValue(t.Settings, null) as SimHub.Plugins.OutputPlugins.GraphicalDash.DashboardSelection : null;
                    if (sel != null && !string.IsNullOrEmpty(sel.Dashboard)) cur = "   (dash actuel : " + sel.Dashboard + ")";
                }
                catch { }
                var cb = new CheckBox { Content = t.Name + cur, IsChecked = !s.DisabledDevices.Contains(key), Margin = new Thickness(0, 1, 0, 1) };
                cb.Checked += (o, e) => { s.DisabledDevices.Remove(key); plugin.SaveSettings(); };
                cb.Unchecked += (o, e) => { if (!s.DisabledDevices.Contains(key)) s.DisabledDevices.Add(key); plugin.SaveSettings(); };
                devicesPanel.Children.Add(cb);
            }
        }

        void Tick()
        {
            var c = plugin.CurrentCar ?? new CarInfo();
            var m = plugin.CurrentMatch;
            tCarId.Text = Or(c.CarId);
            tTeam.Text = Or(c.Team);
            tClass.Text = Or(c.NormalizedClass);
            tModel.Text = Or(c.VehicleModel) + (string.IsNullOrEmpty(c.Extra) ? "" : "   [" + c.Extra + "]");
            tFile.Text = Or(c.VehicleFile);
            tDash.Text = Or(plugin.CurrentDashboard);
            tRule.Text = m == null ? (string.IsNullOrEmpty(plugin.CurrentDashboard) ? "–" : "dash de secours")
                                   : "#" + (m.RuleIndex + 1) + " " + (m.Rule.CarClass ?? "") + " — " + m.How;
            tStatus.Text = Or(plugin.Status);
            if (!string.IsNullOrEmpty(plugin.LastInstallReport) && tInstall.Text != plugin.LastInstallReport) tInstall.Text = plugin.LastInstallReport;
        }

        static string Or(string s) { return string.IsNullOrEmpty(s) ? "–" : s; }

        void RegenMaster(bool force)
        {
            SyncRules();
            tMaster.Text = "Création du dashboard unique…";
            Task.Run(() =>
            {
                var r = plugin.EnsureMasterDashboard(force);
                var text = r != null ? r.Summary() : "Déjà à jour. " + plugin.LastMasterReport;
                Dispatcher.BeginInvoke(new Action(() => { tMaster.Text = text; RefreshDashCodes(); }));
            });
        }

        void ForceForCurrent()
        {
            var dash = (cbForce.Text ?? "").Trim();
            var c = plugin.CurrentCar;
            if (dash.Length == 0) { MessageBox.Show("Choisis d'abord un dashboard dans la liste.", "AVIX LMU Dash"); return; }
            if (c == null || string.IsNullOrEmpty(c.CarId)) { MessageBox.Show("Monte d'abord dans une voiture dans LMU.", "AVIX LMU Dash"); return; }
            DashRule r;
            if (!string.IsNullOrEmpty(c.VehicleModel))
                r = new DashRule(c.NormalizedClass, c.VehicleModel, "", dash, "Ajoutée depuis la voiture actuelle");
            else
                r = new DashRule(c.NormalizedClass, "", c.CarId, dash, "Ajoutée depuis la voiture actuelle (ID)");
            rules.Insert(0, r);
            SyncRules();
            if (plugin.Settings.UseMasterDashboard) RegenMaster(false);
            plugin.ForceReapply();
        }

        void RunInstall(bool overwrite)
        {
            tInstall.Text = "Installation en cours…";
            Task.Run(() =>
            {
                var rep = plugin.InstallAssets(overwrite, false);
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    tInstall.Text = rep.Summary();
                    RefreshDashCodes();
                    if (rep.FontsCopiedToDashFonts + rep.FontsInstalledWindows + rep.DashboardsInstalled > 0)
                        MessageBox.Show(rep.Summary() + Environment.NewLine + Environment.NewLine +
                                        "Redémarre SimHub pour que les nouvelles polices soient prises en compte partout.", "AVIX LMU Dash");
                }));
            });
        }

        // ------------------------------------------------------------------ vérification des polices

        static readonly Regex FontRx = new Regex("\"(?:\\w*Font)\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.Compiled);

        static IEnumerable<string> FamiliesOfFile(string path)
        {
            var res = new List<string>();
            try
            {
                var gt = new GlyphTypeface(new Uri(path));
                res.AddRange(gt.FamilyNames.Values);
                res.AddRange(gt.Win32FamilyNames.Values);
            }
            catch { }
            return res;
        }

        void CheckFonts()
        {
            tFonts.Text = "Analyse…";
            var dashes = rules.Where(r => r.Enabled && !string.IsNullOrWhiteSpace(r.Dashboard)).Select(r => r.Dashboard.Trim())
                              .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            Task.Run(() =>
            {
                var sb = new StringBuilder();
                try
                {
                    var avail = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Segoe UI", "Arial", "Calibri", "Bahnschrift", "Microsoft JhengHei", "Microsoft JhengHei UI", "Consolas" };
                    foreach (var ff in Fonts.SystemFontFamilies)
                    {
                        try { avail.Add(ff.Source); foreach (var n in ff.FamilyNames.Values) avail.Add(n); } catch { }
                    }
                    var dashFonts = Path.Combine(AvixLmuDashPlugin.SimHubDir, "DashFonts");
                    if (Directory.Exists(dashFonts))
                        foreach (var f in Directory.GetFiles(dashFonts).Where(FontInstaller.IsFontFile))
                            foreach (var n in FamiliesOfFile(f)) avail.Add(n);

                    var templates = Path.Combine(AvixLmuDashPlugin.SimHubDir, "DashTemplates");
                    int ok = 0;
                    foreach (var d in dashes)
                    {
                        var dir = Path.Combine(templates, d);
                        if (!Directory.Exists(dir)) { sb.AppendLine("✗ " + d + " : dashboard non installé"); continue; }
                        var local = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        var shf = Path.Combine(dir, "_SHFonts");
                        if (Directory.Exists(shf))
                            foreach (var f in Directory.GetFiles(shf).Where(FontInstaller.IsFontFile))
                                foreach (var n in FamiliesOfFile(f)) local.Add(n);
                        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var dj in Directory.GetFiles(dir, "*.djson", SearchOption.AllDirectories))
                            foreach (Match m in FontRx.Matches(File.ReadAllText(dj)))
                                used.Add(m.Groups[1].Value.Trim());
                        var missing = used.Where(u => u.Length > 0 && !avail.Contains(u) && !local.Contains(u)).ToList();
                        if (missing.Count == 0) ok++;
                        else sb.AppendLine("⚠ " + d + " : police(s) manquante(s) → " + string.Join(", ", missing));
                    }
                    sb.Insert(0, ok + " dashboard(s) sur " + dashes.Count + " ont toutes leurs polices." + Environment.NewLine);
                    sb.AppendLine("(Une police tout juste installée n'est vue qu'après un redémarrage de SimHub.)");
                }
                catch (Exception ex) { sb.AppendLine("Erreur : " + ex.Message); }
                var text = sb.ToString();
                Dispatcher.BeginInvoke(new Action(() => tFonts.Text = text));
            });
        }
    }
}
