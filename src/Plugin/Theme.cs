using System;
using System.IO;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;

namespace AvixLmuDash
{
    /// <summary>
    /// Thème « avix3d.com » pour la page du plugin : mêmes couleurs et polices que le site
    /// (--red #C1C107, --bg #0a0a0a, --surface #141414, --surface2 #1c1c1c, --border #2a2a2a,
    /// Orbitron pour les titres, Rajdhani pour les étiquettes, DM Sans pour le texte).
    /// </summary>
    public static class Theme
    {
        public const string Accent = "#C1C107";
        public const string AccentLight = "#D4D400";
        public const string Bg = "#0A0A0A";
        public const string Surface = "#141414";
        public const string Surface2 = "#1C1C1C";
        public const string Border = "#2A2A2A";
        public const string Text = "#F0F0F0";
        public const string Muted = "#AAAAAA";

        static readonly string[] FontFiles = { "Orbitron-Black.ttf", "Rajdhani-Bold.ttf", "Rajdhani-SemiBold.ttf", "DMSans-Regular.ttf", "DMSans-Bold.ttf" };

        static string fontDir;

        /// <summary>Extrait les polices embarquées dans %LOCALAPPDATA%\AvixLmuDash\ui-fonts.</summary>
        static string FontDir()
        {
            if (fontDir != null) return fontDir;
            try
            {
                var d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AvixLmuDash", "ui-fonts");
                Directory.CreateDirectory(d);
                foreach (var f in FontFiles)
                {
                    var p = Path.Combine(d, f);
                    var b = AvixLmuDashPlugin.LoadResource("font." + f);
                    if (b != null && (!File.Exists(p) || new FileInfo(p).Length != b.Length)) File.WriteAllBytes(p, b);
                }
                fontDir = d;
            }
            catch { fontDir = ""; }
            return fontDir;
        }

        static FontFamily Family(string family, string fallback)
        {
            try
            {
                var d = FontDir();
                if (!string.IsNullOrEmpty(d))
                    return new FontFamily(new Uri(d.TrimEnd('\\', '/') + Path.DirectorySeparatorChar), "./#" + family + ", " + fallback);
            }
            catch { }
            return new FontFamily(fallback);
        }

        public static FontFamily TitleFont { get { return Family("Orbitron AVIX", "Segoe UI Black"); } }
        public static FontFamily LabelFont { get { return Family("Rajdhani", "Segoe UI Semibold"); } }
        public static FontFamily BodyFont { get { return Family("DM Sans", "Segoe UI"); } }

        public static SolidColorBrush Brush(string hex)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }

        const string Xaml = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <SolidColorBrush x:Key='AvxAccent' Color='#C1C107'/>
  <SolidColorBrush x:Key='AvxAccentLight' Color='#D4D400'/>
  <SolidColorBrush x:Key='AvxBg' Color='#0A0A0A'/>
  <SolidColorBrush x:Key='AvxSurface' Color='#141414'/>
  <SolidColorBrush x:Key='AvxSurface2' Color='#1C1C1C'/>
  <SolidColorBrush x:Key='AvxBorder' Color='#2A2A2A'/>
  <SolidColorBrush x:Key='AvxText' Color='#F0F0F0'/>
  <SolidColorBrush x:Key='AvxMuted' Color='#AAAAAA'/>

  <!-- Bouton contour (btn-outline du site) -->
  <Style TargetType='Button'>
    <Setter Property='Foreground' Value='#F0F0F0'/>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='BorderBrush' Value='#2A2A2A'/>
    <Setter Property='BorderThickness' Value='1.5'/>
    <Setter Property='Padding' Value='16,8'/>
    <Setter Property='Margin' Value='0,4,8,4'/>
    <Setter Property='FontFamily' Value='{DynamicResource AvxBodyFont}'/>
    <Setter Property='FontWeight' Value='SemiBold'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='b' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}'
                  BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='9' Padding='{TemplateBinding Padding}'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center' RecognizesAccessKey='False'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='b' Property='Background' Value='#1C1C1C'/>
              <Setter TargetName='b' Property='BorderBrush' Value='#AAAAAA'/>
            </Trigger>
            <Trigger Property='IsPressed' Value='True'>
              <Setter TargetName='b' Property='BorderBrush' Value='#C1C107'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'>
              <Setter Property='Opacity' Value='0.4'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- Bouton principal jaune (btn-primary du site) -->
  <Style x:Key='AvxPrimary' TargetType='Button' BasedOn='{StaticResource {x:Type Button}}'>
    <Setter Property='Foreground' Value='#000000'/>
    <Setter Property='FontWeight' Value='Bold'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='b' Background='#C1C107' CornerRadius='9' Padding='{TemplateBinding Padding}'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center' RecognizesAccessKey='False'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='b' Property='Background' Value='#D4D400'/>
              <Setter TargetName='b' Property='Effect'>
                <Setter.Value><DropShadowEffect Color='#C1C107' BlurRadius='18' ShadowDepth='0' Opacity='0.45'/></Setter.Value>
              </Setter>
            </Trigger>
            <Trigger Property='IsPressed' Value='True'>
              <Setter TargetName='b' Property='Background' Value='#A8A806'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- Case à cocher : carré arrondi, coche noire sur jaune -->
  <Style TargetType='CheckBox'>
    <Setter Property='Foreground' Value='#F0F0F0'/>
    <Setter Property='FontFamily' Value='{DynamicResource AvxBodyFont}'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='Margin' Value='0,4,0,4'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='CheckBox'>
          <StackPanel Orientation='Horizontal' Background='Transparent'>
            <Border x:Name='box' Width='18' Height='18' CornerRadius='5' BorderThickness='1.5' BorderBrush='#2A2A2A' Background='#1C1C1C' VerticalAlignment='Center'>
              <Path x:Name='tick' Data='M 3.5,9 L 7.5,13 L 14.5,5' Stroke='#000000' StrokeThickness='2.4' StrokeStartLineCap='Round' StrokeEndLineCap='Round' Visibility='Collapsed'/>
            </Border>
            <ContentPresenter Margin='10,0,0,0' VerticalAlignment='Center' RecognizesAccessKey='False'/>
          </StackPanel>
          <ControlTemplate.Triggers>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='box' Property='Background' Value='#C1C107'/>
              <Setter TargetName='box' Property='BorderBrush' Value='#C1C107'/>
              <Setter TargetName='tick' Property='Visibility' Value='Visible'/>
            </Trigger>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='box' Property='BorderBrush' Value='#C1C107'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='TextBox'>
    <Setter Property='Background' Value='#1C1C1C'/>
    <Setter Property='Foreground' Value='#F0F0F0'/>
    <Setter Property='BorderBrush' Value='#2A2A2A'/>
    <Setter Property='CaretBrush' Value='#C1C107'/>
    <Setter Property='SelectionBrush' Value='#C1C107'/>
    <Setter Property='Padding' Value='6,4'/>
    <Setter Property='FontFamily' Value='Consolas'/>
  </Style>

  <Style TargetType='ComboBox'>
    <Setter Property='Background' Value='#1C1C1C'/>
    <Setter Property='Foreground' Value='#F0F0F0'/>
    <Setter Property='BorderBrush' Value='#2A2A2A'/>
    <Setter Property='FontFamily' Value='{DynamicResource AvxBodyFont}'/>
    <Setter Property='Height' Value='32'/>
    <Setter Property='VerticalContentAlignment' Value='Center'/>
  </Style>

  <Style TargetType='DataGrid'>
    <Setter Property='Background' Value='#141414'/>
    <Setter Property='Foreground' Value='#F0F0F0'/>
    <Setter Property='BorderBrush' Value='#2A2A2A'/>
    <Setter Property='BorderThickness' Value='1'/>
    <Setter Property='RowBackground' Value='#141414'/>
    <Setter Property='AlternatingRowBackground' Value='#181818'/>
    <Setter Property='HorizontalGridLinesBrush' Value='#2A2A2A'/>
    <Setter Property='VerticalGridLinesBrush' Value='#222222'/>
    <Setter Property='GridLinesVisibility' Value='Horizontal'/>
    <Setter Property='RowHeight' Value='32'/>
    <Setter Property='FontFamily' Value='{DynamicResource AvxBodyFont}'/>
    <Setter Property='FontSize' Value='13'/>
  </Style>
  <Style TargetType='DataGridColumnHeader'>
    <Setter Property='Background' Value='#1C1C1C'/>
    <Setter Property='Foreground' Value='#AAAAAA'/>
    <Setter Property='BorderBrush' Value='#2A2A2A'/>
    <Setter Property='BorderThickness' Value='0,0,1,1'/>
    <Setter Property='Padding' Value='8,6'/>
    <Setter Property='FontFamily' Value='{DynamicResource AvxLabelFont}'/>
    <Setter Property='FontWeight' Value='Bold'/>
    <Setter Property='FontSize' Value='13'/>
  </Style>
  <Style TargetType='DataGridCell'>
    <Setter Property='BorderThickness' Value='0'/>
    <Setter Property='Padding' Value='6,0'/>
    <Style.Triggers>
      <Trigger Property='IsSelected' Value='True'>
        <Setter Property='Background' Value='#2A2A08'/>
        <Setter Property='Foreground' Value='#F0F0F0'/>
      </Trigger>
    </Style.Triggers>
  </Style>

  <Style TargetType='ScrollBar'>
    <Setter Property='Background' Value='#0A0A0A'/>
  </Style>
</ResourceDictionary>";

        public static ResourceDictionary Create()
        {
            var rd = (ResourceDictionary)XamlReader.Parse(Xaml);
            rd["AvxTitleFont"] = TitleFont;
            rd["AvxLabelFont"] = LabelFont;
            rd["AvxBodyFont"] = BodyFont;
            return rd;
        }
    }
}
