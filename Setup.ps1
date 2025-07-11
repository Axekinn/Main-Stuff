# --- USER VARIABLES ---
$UserName = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name.Split('\')[-1]
$UserPic = "$env:ProgramData\Microsoft\User Account Pictures\user.png"
if (!(Test-Path $UserPic)) {
    $UserPic = "user.png"
    [System.IO.File]::WriteAllBytes($UserPic, [Convert]::FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAQAAAD9CzEMAAAAJElEQVR42mNgGAX0gP8zA8M/AwPDfwYwEwZkBGYQZgBiAxhRzgAAAgwAAWgD1tAAAAAASUVORK5CYII="))
}
$prj = "XboxShellApp"
$solution = "XboxShellApp.sln"
if (!(Test-Path $prj)) { mkdir $prj }
Set-Location $prj

# --- PROJECT FILE ---
@'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net6.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
  </PropertyGroup>
</Project>
'@ | Set-Content XboxShellApp.csproj

# --- TILEBUTTONSTYLE ---
@'
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Style x:Key="TileButtonStyle" TargetType="Button">
        <Setter Property="FontSize" Value="14"/>
        <Setter Property="Foreground" Value="White"/>
        <Setter Property="Background" Value="#FF2D2D40"/>
        <Setter Property="BorderBrush" Value="#FF3F3F5A"/>
        <Setter Property="Height" Value="240"/>
        <Setter Property="Width" Value="170"/>
        <Setter Property="Margin" Value="10"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Button">
                    <Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="2" CornerRadius="16">
                        <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Bottom" Margin="0,0,0,16"/>
                    </Border>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
        <Setter Property="Cursor" Value="Hand"/>
    </Style>
</ResourceDictionary>
'@ | Set-Content TileButtonStyle.xaml

# --- APP XAML ---
@'
<Application x:Class="XboxShellApp.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             StartupUri="MainWindow.xaml">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="TileButtonStyle.xaml"/>
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
</Application>
'@ | Set-Content App.xaml

# --- APP XAML.CS ---
@'
using System.Windows;
namespace XboxShellApp
{
    public partial class App : Application
    {
        public App()
        {
            this.DispatcherUnhandledException += (s, e) => {
                MessageBox.Show(e.Exception.ToString(), "Unhandled Exception");
            };
        }
    }
}
'@ | Set-Content App.xaml.cs

# --- MAINWINDOW XAML ---
$mainWindowXaml = @"
<Window x:Class="XboxShellApp.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:local="clr-namespace:XboxShellApp"
        Title="Xbox One Style Shell" WindowStyle="None" WindowState="Maximized"
        Background="#FF101018" Topmost="True">
    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="240"/>
            <ColumnDefinition Width="*"/>
        </Grid.ColumnDefinitions>
        <!-- User circle and user name, upper left -->
        <StackPanel Orientation="Horizontal" HorizontalAlignment="Left" VerticalAlignment="Top" Margin="40,40,0,0" Grid.Column="1">
            <Ellipse Width="60" Height="60">
                <Ellipse.Fill>
                    <ImageBrush ImageSource="$UserPic"/>
                </Ellipse.Fill>
            </Ellipse>
            <TextBlock Text="$UserName" Margin="20,0,0,0" FontSize="24" Foreground="White" VerticalAlignment="Center"/>
        </StackPanel>
        <local:SidebarPanel Grid.Column="0"/>
        <local:MainPanel Grid.Column="1" x:Name="MainPanel"/>
    </Grid>
</Window>
"@
$mainWindowXaml | Set-Content MainWindow.xaml

# --- MAINWINDOW XAML.CS ---
@'
using System.Windows;
namespace XboxShellApp
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }
    }
}
'@ | Set-Content MainWindow.xaml.cs

# --- SIDEBARPANEL XAML ---
$sidebarPanelXaml = @"
<UserControl x:Class="XboxShellApp.SidebarPanel"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Height="Auto" Width="240" Background="#FF181828">
    <StackPanel VerticalAlignment="Stretch" HorizontalAlignment="Left" Background="#FF181828">
        <Ellipse Width="64" Height="64" Fill="Gray" Margin="24 32 24 16"/>
        <TextBlock Text="Welcome, $UserName" Margin="24 0 0 32" FontSize="18" Foreground="White"/>
        <Button x:Name="DashboardBtn" Content="Dashboard" Margin="12 8" Height="48" FontSize="18"/>
        <Button x:Name="MyGamesBtn" Content="My games &amp; apps" Margin="12 8" Height="48" FontSize="18"/>
        <Button x:Name="StoreBtn" Content="Store" Margin="12 8" Height="48" FontSize="18"/>
        <Button x:Name="SettingsBtn" Content="Settings" Margin="12 8" Height="48" FontSize="18"/>
        <StackPanel VerticalAlignment="Bottom" Margin="0 32 0 0">
            <TextBlock Text="Xbox Shell" Foreground="Gray" FontSize="14" Margin="24 48 0 0"/>
        </StackPanel>
    </StackPanel>
</UserControl>
"@
$sidebarPanelXaml | Set-Content SidebarPanel.xaml

# --- SIDEBARPANEL XAML.CS ---
@'
using System.Windows.Controls;
using System.Windows;
namespace XboxShellApp
{
    public partial class SidebarPanel : UserControl
    {
        public SidebarPanel()
        {
            InitializeComponent();
            Loaded += SidebarPanel_Loaded;
        }
        private void SidebarPanel_Loaded(object sender, RoutedEventArgs e)
        {
            var mw = Window.GetWindow(this) as MainWindow;
            if (mw != null)
            {
                var mainPanel = mw.MainPanel as MainPanel;
                if (mainPanel != null)
                {
                    DashboardBtn.Click += (s,e2) => mainPanel.ShowDashboard();
                    MyGamesBtn.Click   += (s,e2) => mainPanel.ShowLibrary();
                    StoreBtn.Click     += (s,e2) => MessageBox.Show("Store not implemented.");
                    SettingsBtn.Click  += (s,e2) => MessageBox.Show("Settings not implemented.");
                }
            }
        }
    }
}
'@ | Set-Content SidebarPanel.xaml.cs

# --- MAINPANEL XAML ---
@'
<UserControl x:Class="XboxShellApp.MainPanel"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Background="Transparent">
    <Grid>
        <!-- Main Dashboard, hidden when My games & apps is open -->
        <Grid x:Name="DashboardGrid" Visibility="Visible">
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="*"/>
            </Grid.RowDefinitions>
            <StackPanel VerticalAlignment="Top" HorizontalAlignment="Center" Margin="0,100,0,0">
                <TextBlock Text="Recent Games" Foreground="White" FontSize="28" HorizontalAlignment="Left" Margin="20,0,0,12"/>
                <ItemsControl x:Name="RecentGamesItemsControl" Margin="0,0,0,32">
                    <ItemsControl.ItemsPanel>
                        <ItemsPanelTemplate>
                            <UniformGrid Rows="1" Columns="5"/>
                        </ItemsPanelTemplate>
                    </ItemsControl.ItemsPanel>
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <Button Style="{StaticResource TileButtonStyle}" Width="170" Height="240">
                                <TextBlock Text="{Binding}" FontSize="14" TextAlignment="Center" TextTrimming="CharacterEllipsis" VerticalAlignment="Bottom" Margin="0,0,0,16"/>
                            </Button>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
                <StackPanel Orientation="Horizontal" HorizontalAlignment="Left" Margin="0,12,0,0">
                    <Button x:Name="MyGamesAppsBtn"
                            Content="My games &amp; apps"
                            Width="320" Height="100"
                            Margin="12,0"
                            Background="#FF232328" Foreground="White"
                            FontSize="24" FontWeight="SemiBold"
                            BorderThickness="0"/>
                    <Border Width="320" Height="100" Margin="12,0" Background="#FF107C10">
                        <TextBlock Text="Behind every good club is a great admin. See why"
                                   VerticalAlignment="Center" HorizontalAlignment="Center"
                                   FontSize="18" Foreground="White" TextWrapping="Wrap"/>
                    </Border>
                    <Border Width="320" Height="100" Margin="12,0" Background="#FF232328">
                        <TextBlock Text="Play Night Call now"
                                   VerticalAlignment="Center" HorizontalAlignment="Center"
                                   FontSize="18" Foreground="White"/>
                    </Border>
                    <Border Width="320" Height="100" Margin="12,0" Background="#FF232328">
                        <TextBlock Text="Tenet - Only in cinemas 26 August"
                                   VerticalAlignment="Center" HorizontalAlignment="Center"
                                   FontSize="18" Foreground="White"/>
                    </Border>
                </StackPanel>
            </StackPanel>
            <Border Grid.Row="1" Background="Transparent"/>
        </Grid>
        <!-- My games & apps: Playnite v11-style grid, vertical scroll, fills bottom, hidden by default -->
        <Grid x:Name="LibraryGrid" Visibility="Collapsed" Background="#FF181828">
            <Grid>
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="*"/>
                </Grid.RowDefinitions>
                <Button Content="&lt; Back to Dashboard" Click="Back_Click" Width="220" Height="40" Margin="10"/>
                <ScrollViewer Grid.Row="1" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled" Margin="0,20,0,0">
                    <ItemsControl x:Name="GamesItemsControl">
                        <ItemsControl.ItemsPanel>
                            <ItemsPanelTemplate>
                                <WrapPanel Orientation="Horizontal" ItemWidth="170" ItemHeight="240" MinWidth="900"/>
                            </ItemsPanelTemplate>
                        </ItemsControl.ItemsPanel>
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Button Style="{StaticResource TileButtonStyle}" Width="170" Height="240">
                                    <TextBlock Text="{Binding}" FontSize="14" TextAlignment="Center" TextTrimming="CharacterEllipsis" VerticalAlignment="Bottom" Margin="0,0,0,16"/>
                                </Button>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </ScrollViewer>
            </Grid>
        </Grid>
    </Grid>
</UserControl>
'@ | Set-Content MainPanel.xaml

# --- MAINPANEL XAML.CS ---
@'
using System.Windows.Controls;
using System.Windows;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System;

namespace XboxShellApp
{
    public class RecentApp
    {
        public string Title { get; set; }
        public string Image { get; set; }
    }
    public partial class MainPanel : UserControl
    {
        public MainPanel()
        {
            InitializeComponent();
            MyGamesAppsBtn.Click += MyGamesAppsBtn_Click;
            LoadRecentGames();
        }
        public void ShowDashboard()
        {
            DashboardGrid.Visibility = Visibility.Visible;
            LibraryGrid.Visibility = Visibility.Collapsed;
            LoadRecentGames();
        }
        public void ShowLibrary()
        {
            DashboardGrid.Visibility = Visibility.Collapsed;
            LibraryGrid.Visibility = Visibility.Visible;
            LoadGames();
        }
        private void MyGamesAppsBtn_Click(object sender, RoutedEventArgs e)
        {
            ShowLibrary();
        }
        private void Back_Click(object sender, RoutedEventArgs e)
        {
            ShowDashboard();
        }

        public void LoadRecentGames()
        {
            var allGames = GetAllGames();
            var recentGames = allGames
                .GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(x => x.LastWriteTime).First())
                .OrderByDescending(x => x.LastWriteTime)
                .Take(5)
                .Select(d => d.Name)
                .ToList();

            RecentGamesItemsControl.ItemsSource = recentGames;
        }

        public void LoadGames()
        {
            var allGames = GetAllGames()
                .GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(d => d.Name)
                .Select(d => d.Name)
                .ToList();

            GamesItemsControl.ItemsSource = allGames;
        }

        private List<DirectoryInfo> GetAllGames()
        {
            var allGames = new List<DirectoryInfo>();
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    var dir = Path.Combine(drive.Name, "Games");
                    if (Directory.Exists(dir))
                        allGames.AddRange(new DirectoryInfo(dir).GetDirectories());
                }
                catch { }
            }
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Network))
            {
                try
                {
                    var dir = Path.Combine(drive.Name, "Games");
                    if (Directory.Exists(dir))
                        allGames.AddRange(new DirectoryInfo(dir).GetDirectories());
                }
                catch { }
            }
            return allGames;
        }
    }
}
'@ | Set-Content MainPanel.xaml.cs

# --- PLACEHOLDER IMAGES ---
$imgList = @("halo.png", "battletoads.png", "forza.png", "eaplay.png", "ori.png", "rainbow.png")
$validBase64 = "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAQAAAD9CzEMAAAAJElEQVR42mNgGAX0gP8zA8M/AwPDfwYwEwZkBGYQZgBiAxhRzgAAAgwAAWgD1tAAAAAASUVORK5CYII="
foreach ($img in $imgList) {
    if (!(Test-Path $img)) {
        [System.IO.File]::WriteAllBytes($img, [Convert]::FromBase64String($validBase64))
    }
}

# --- DOTNET RESTORE & BUILD ---
dotnet restore
dotnet build -c Release

# --- DOTNET PUBLISH ---
dotnet publish -c Release -r win-x64 --self-contained false

cd ..

# --- CREATE SOLUTION AND ADD PROJECT ---
if (!(Test-Path $solution)) {
    dotnet new sln -n $prj
}
dotnet sln $solution add "$prj\$prj.csproj"

# --- OPEN IN VISUAL STUDIO ---
Start-Process "$solution"

Write-Host "`nThe Xbox-style shell project is fully created, built, and opened in Visual Studio!"
Write-Host "Published EXE is in: $prj\bin\Release\net6.0-windows\win-x64\publish\XboxShellApp.exe`n"
pause