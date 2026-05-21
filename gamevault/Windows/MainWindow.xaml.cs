using gamevault.ViewModels;
using MahApps.Metro.Controls;
using gamevault.UserControls;
using System.Linq;
using gamevault.Helper;
using System.Windows;
using MahApps.Metro.Controls.Dialogs;
using System.IO;
using System.Diagnostics;
using System;
using Microsoft.Toolkit.Uwp.Notifications;
using System.Windows.Forms;
using gamevault.Models;
using ControlzEx.Standard;
using System.Text;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Text.Json;

namespace gamevault.Windows
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : IDisposable
    {
        private GameTimeTracker GameTimeTracker;
        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = MainWindowViewModel.Instance;
            InitBootTasks();
        }
        private void InitBootTasks()
        {
            App.HideToSystemTray = true;
            RestoreTheme();
            Task.Run(async () =>
            {
                if (GameTimeTracker == null)
                {
                    GameTimeTracker = new GameTimeTracker();
                    await GameTimeTracker.Start();
                }
            });
            AnalyticsHelper.Instance.SendCustomEvent(CustomAnalyticsEventKeys.USER_SETTINGS, AnalyticsHelper.Instance.PrepareSettingsForAnalytics());
            PipeServiceHandler.Instance.IsReadyForCommands = true;
        }
        private async void HamburgerMenuControl_OnItemInvoked(object sender, HamburgerMenuItemInvokedEventArgs args)
        {
            MainControl activeControlIndex = (MainControl)MainWindowViewModel.Instance.ActiveControlIndex;

            switch (activeControlIndex)
            {
                case MainControl.Library:
                    {
                        MainWindowViewModel.Instance.ActiveControl = MainWindowViewModel.Instance.Library;
                        break;
                    }
                case MainControl.Settings:
                    {
                        MainWindowViewModel.Instance.ActiveControl = MainWindowViewModel.Instance.Settings;
                        break;
                    }
                case MainControl.Downloads:
                    {
                        MainWindowViewModel.Instance.ActiveControl = MainWindowViewModel.Instance.Downloads;
                        break;
                    }
                case MainControl.Community:
                    {
                        MainWindowViewModel.Instance.ActiveControl = MainWindowViewModel.Instance.Community;
                        break;
                    }
                case MainControl.AdminConsole:
                    {
                        MainWindowViewModel.Instance.ActiveControl = MainWindowViewModel.Instance.AdminConsole;
                        break;
                    }
            }
            MainWindowViewModel.Instance.LastMainControl = activeControlIndex;
        }

        private async void MetroWindow_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            VisualHelper.AdjustWindowChrome(this);
            MainWindowViewModel.Instance.SetActiveControl(MainControl.Library);
            LoginState state = LoginManager.Instance.GetState();
            if (LoginState.Success == state)
            {
                if (Preferences.Get(AppConfigKey.LibStartup, LoginManager.Instance.GetUserProfile().UserConfigFile) == "1")
                {
                    await MainWindowViewModel.Instance.Library.LoadLibrary();
                }
            }
            else if (LoginState.Unauthorized == state || LoginState.Forbidden == state)
            {
                MainWindowViewModel.Instance.AppBarText = "You are not logged in";
            }
            else if (LoginState.Error == state)
            {
                MainWindowViewModel.Instance.AppBarText = LoginManager.Instance.GetServerLoginResponseMessage();
                MainWindowViewModel.Instance.Library.ShowLibraryError();
            }
            await MainWindowViewModel.Instance.Library.GetGameInstalls().RestoreInstalledGames();
            await MainWindowViewModel.Instance.Downloads.RestoreDownloadedGames();
            LoginManager.Instance.InitOnlineTimer();
            MainWindowViewModel.Instance.UserAvatar = LoginManager.Instance.GetCurrentUser();


        }
        private void MetroWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (App.HideToSystemTray)
            {
                e.Cancel = true;
                this.Hide();
                if (Preferences.Get(AppConfigKey.RunningInTrayMessage, LoginManager.Instance.GetUserProfile().UserConfigFile) != "1")
                {
                    Preferences.Set(AppConfigKey.RunningInTrayMessage, "1", LoginManager.Instance.GetUserProfile().UserConfigFile);
                    ToastMessageHelper.CreateToastMessage("Information", "GameVault is still running in the background");
                }
            }
        }

        private void UserAvatar_Clicked(object sender, RoutedEventArgs e)
        {
            MainWindowViewModel.Instance.Community.ShowUser(LoginManager.Instance.GetCurrentUser());
        }



        private void CopyMessage_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Windows.Clipboard.SetText(MainWindowViewModel.Instance.AppBarText);
            }
            catch { }
        }
        private void RestoreTheme()
        {
            try
            {
                string currentThemeString = Preferences.Get(AppConfigKey.Theme, LoginManager.Instance.GetUserProfile().UserConfigFile, true);
                if (currentThemeString != string.Empty)
                {
                    ThemeItem currentTheme = JsonSerializer.Deserialize<ThemeItem>(currentThemeString)!;

                    if (App.Current.Resources.MergedDictionaries[0].Source.OriginalString != currentTheme.Path)
                    {
                        App.Instance.SetTheme(currentTheme.Path);
                    }
                }
            }
            catch { }
        }
        public void Dispose()
        {
            GameTimeTracker.Stop();
            MainWindowViewModel.Instance.Downloads.CancelAllDownloads();
            InstallViewModel.Instance.InstalledGames.Clear();
            DownloadsViewModel.Instance.DownloadedGames.Clear();
            ProcessShepherd.Instance.KillAllChildProcesses();
            App.HideToSystemTray = false;
            App.Instance.ResetToDefaultTheme();
            LoginManager.Instance.StopOnlineTimer();
            App.Instance.ResetJumpListGames();
            PipeServiceHandler.Instance.IsReadyForCommands = false;
            MainWindowViewModel.Instance.UserAvatar = null;
            this.Close();
        }
    }
}
