using gamevault.Helper;
using gamevault.Models;
using gamevault.ViewModels;
using MahApps.Metro.Controls.Dialogs;
using MahApps.Metro.Controls;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using gamevault.Helper.Integrations;

namespace gamevault.UserControls
{
    /// <summary>
    /// Interaction logic for DownloadsUserControl.xaml
    /// </summary>
    public partial class DownloadsUserControl : UserControl
    {
        private bool syncingDownloadsPanel = false;

        public DownloadsUserControl()
        {
            InitializeComponent();
            DataContext = DownloadsViewModel.Instance;

            DownloadsViewModel.Instance.DownloadedGames.CollectionChanged -= DownloadedGames_CollectionChanged;
            DownloadsViewModel.Instance.DownloadedGames.CollectionChanged += DownloadedGames_CollectionChanged;

            Loaded += (_, _) => SyncDownloadsPanel();
            Unloaded += (_, _) =>
            {
                DownloadsViewModel.Instance.DownloadedGames.CollectionChanged -= DownloadedGames_CollectionChanged;
            };
        }



        private void DownloadsContentHost_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateDownloadControlWidths();
        }

        private double GetDownloadControlWidth()
        {
            double hostWidth = uiDownloadsContentHost?.ActualWidth ?? 0;
            if (double.IsNaN(hostWidth) || double.IsInfinity(hostWidth) || hostWidth <= 0)
            {
                hostWidth = uiDownloadsScrollViewer?.ActualWidth ?? 0;
            }
            if (double.IsNaN(hostWidth) || double.IsInfinity(hostWidth) || hostWidth <= 0)
            {
                return double.NaN;
            }

            // Keep the developer-style compact row, but render it at a fixed readable scale.
            // It shrinks with the window only after the available width drops below the preferred size.
            return Math.Max(420, Math.Min(520, hostWidth));
        }

        private void UpdateDownloadControlWidths()
        {
            if (uiDownloadsPanel == null)
            {
                return;
            }

            double targetWidth = GetDownloadControlWidth();
            if (double.IsNaN(targetWidth))
            {
                return;
            }

            foreach (UIElement child in uiDownloadsPanel.Children)
            {
                if (child is FrameworkElement element)
                {
                    element.Width = targetWidth;
                    element.HorizontalAlignment = HorizontalAlignment.Center;
                }
            }
        }

        private void DownloadedGames_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke((Action)SyncDownloadsPanel, DispatcherPriority.Background);
                return;
            }

            SyncDownloadsPanel();
        }

        private void SyncDownloadsPanel()
        {
            if (syncingDownloadsPanel || uiDownloadsPanel == null)
            {
                return;
            }

            syncingDownloadsPanel = true;
            try
            {
                uiDownloadsPanel.Children.Clear();

                foreach (GameDownloadUserControl control in DownloadsViewModel.Instance.DownloadedGames.ToList())
                {
                    if (control == null)
                    {
                        continue;
                    }

                    DetachFromCurrentParent(control);
                    control.HorizontalAlignment = HorizontalAlignment.Center;
                    control.Margin = new Thickness(0, 0, 0, 8);
                    double targetWidth = GetDownloadControlWidth();
                    if (!double.IsNaN(targetWidth))
                    {
                        control.Width = targetWidth;
                    }
                    uiDownloadsPanel.Children.Add(control);
                }

                UpdateDownloadControlWidths();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to sync downloads panel: {ex}");
                MainWindowViewModel.Instance.AppBarText = "The downloads list could not be rendered";
            }
            finally
            {
                syncingDownloadsPanel = false;
            }
        }

        private static void DetachFromCurrentParent(FrameworkElement element)
        {
            try
            {
                if (element.Parent is Panel parentPanel)
                {
                    parentPanel.Children.Remove(element);
                }
                else if (element.Parent is ContentControl parentContent && ReferenceEquals(parentContent.Content, element))
                {
                    parentContent.Content = null;
                }
                else if (element.Parent is Decorator parentDecorator && ReferenceEquals(parentDecorator.Child, element))
                {
                    parentDecorator.Child = null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to detach download control from parent: {ex}");
            }
        }

        public async Task RestoreDownloadedGames()
        {
            List<DirectoryEntry> rootDirectories = await Dispatcher.InvokeAsync(() => SettingsViewModel.Instance.RootDirectories.ToList());

            Dictionary<Game, string>? games = await Task.Run<Dictionary<Game, string>?>(async () =>
             {
                 if (rootDirectories.Count == 0)
                     return null;

                 List<string> allDirectoriesFromRootDirectories = new List<string>();
                 foreach (DirectoryEntry dirEntry in rootDirectories)
                 {
                     if (Directory.Exists(Path.Combine(dirEntry.Uri, "GameVault", "Downloads")))
                         allDirectoriesFromRootDirectories.AddRange(Directory.GetDirectories(Path.Combine(dirEntry.Uri, "GameVault", "Downloads")));
                 }

                 Dictionary<int, string> foundPathsById = new Dictionary<int, string>();
                 foreach (string dir in allDirectoriesFromRootDirectories)
                 {
                     try
                     {
                         if (new DirectoryInfo(dir).GetFiles().Length == 0)
                             continue;

                         string dirName = dir.Substring(dir.LastIndexOf('\\'));
                         int closeIndex = dirName.IndexOf(')');
                         if (closeIndex <= 2)
                             continue;

                         string gameId = dirName.Substring(2, closeIndex - 2);

                         if (int.TryParse(gameId, out int id))
                         {
                             string? rootPath = rootDirectories
                                .Where(x => dir.Contains(x.Uri))
                                .OrderByDescending(x => x.Uri.Length)
                                .FirstOrDefault()?.Uri;

                             if (!string.IsNullOrWhiteSpace(rootPath))
                             {
                                 foundPathsById[id] = rootPath;
                             }
                         }
                     }
                     catch { continue; }
                 }

                 if (foundPathsById.Count == 0)
                     return null;

                 try
                 {
                     if (LoginManager.Instance.IsLoggedIn())
                     {
                         string gameList = await WebHelper.GetAsync(@$"{SettingsViewModel.Instance.ServerUrl}/api/games?filter.id=$in:{string.Join(',', foundPathsById.Keys)}");
                         Dictionary<Game, string> foundGames = new Dictionary<Game, string>();
                         foreach (Game game in JsonSerializer.Deserialize<PaginatedData<Game>>(gameList)?.Data ?? Enumerable.Empty<Game>())
                         {
                             if (foundPathsById.TryGetValue(game.ID, out string path))
                             {
                                 foundGames[game] = path;
                             }
                         }
                         return foundGames;
                     }

                     Dictionary<Game, string> offlineCacheGames = new Dictionary<Game, string>();
                     foreach (int id in foundPathsById.Keys)
                     {
                         string objectFromFile = Preferences.Get(id.ToString(), LoginManager.Instance.GetUserProfile().OfflineCache);
                         if (objectFromFile == string.Empty)
                             continue;

                         string decompressedObject = StringCompressor.DecompressString(objectFromFile);
                         Game? deserializedObject = JsonSerializer.Deserialize<Game>(decompressedObject);
                         if (deserializedObject != null && foundPathsById.TryGetValue(deserializedObject.ID, out string path))
                         {
                             offlineCacheGames[deserializedObject] = path;
                         }
                     }
                     return offlineCacheGames;
                 }
                 catch (FormatException)
                 {
                     App.Current.Dispatcher.BeginInvoke((Action)(() => MainWindowViewModel.Instance.AppBarText = "The offline cache is corrupted"));
                 }
                 catch (Exception ex)
                 {
                     string webMsg = WebExceptionHelper.TryGetServerMessage(ex);
                     App.Current.Dispatcher.BeginInvoke((Action)(() => MainWindowViewModel.Instance.AppBarText = webMsg));
                 }
                 return null;
             });

            if (games == null)
                return;

            await Dispatcher.InvokeAsync(() =>
            {
                var validGameIds = new HashSet<int>(games.Keys.Select(g => g.ID));

                for (int i = DownloadsViewModel.Instance.DownloadedGames.Count - 1; i >= 0; i--)
                {
                    var control = DownloadsViewModel.Instance.DownloadedGames[i];
                    int gameId = control.GetGameId();

                    bool existsInDict = validGameIds.Contains(gameId);

                    if (control.IsDownloading())
                    {
                        control.PauseDownload();
                    }
                    if (!existsInDict)
                    {
                        DownloadsViewModel.Instance.DownloadedGames.RemoveAt(i);
                    }
                }

                var existingIds = new HashSet<int>(DownloadsViewModel.Instance.DownloadedGames.Select(c => c.GetGameId()));

                foreach (var game in games)
                {
                    if (!existingIds.Contains(game.Key.ID))
                    {
                        DownloadsViewModel.Instance.DownloadedGames.Add(new GameDownloadUserControl(game.Key, game.Value, false));
                    }
                }

                SyncDownloadsPanel();
            });
        }

        public void RefreshGame(Game game)
        {
            if (game == null)
                return;

            foreach (var download in DownloadsViewModel.Instance.DownloadedGames.ToList())
            {
                if (download?.GetGameId() == game.ID)
                {
                    download.Refresh(game);
                    return;
                }
            }
        }

        public void CancelAllDownloads()
        {
            foreach (var download in DownloadsViewModel.Instance.DownloadedGames.ToList())
            {
                download?.CancelDownload();
            }
        }

        public async Task TryStartDownload(Game game)
        {
            try
            {
                if (game == null)
                {
                    MainWindowViewModel.Instance.AppBarText = "Could not start download: missing game data";
                    return;
                }

                if (SettingsViewModel.Instance.RootDirectories.Count == 0)
                {
                    MainWindowViewModel.Instance.AppBarText = "No Root Directory configured! Go to ⚙️Settings->Data";
                    return;
                }

                var installLocationPicker = new InstallLocationUserControl();
                MainWindowViewModel.Instance.OpenPopup(installLocationPicker);
                string selectedDirectory = await installLocationPicker.SelectInstallLocation();
                if (selectedDirectory == string.Empty)
                    return;

                if (!Directory.Exists(selectedDirectory))
                {
                    MainWindowViewModel.Instance.AppBarText = "Selected directory does not exist";
                    return;
                }
                if (!LoginManager.Instance.IsLoggedIn())
                {
                    MainWindowViewModel.Instance.AppBarText = "You are not logged in or offline";
                    return;
                }
                if (IsAlreadyDownloading(game.ID))
                {
                    MainWindowViewModel.Instance.AppBarText = $"'{game.Title}' is already in the download queue";
                    return;
                }
                if (await IsAlreadyDownloaded(game.ID))
                {
                    return;
                }
                if (!IsEnoughDriveSpaceAvailable(Convert.ToInt64(game.Size), selectedDirectory))
                {
                    string? driveName = Path.GetPathRoot(selectedDirectory);
                    MainWindowViewModel.Instance.AppBarText = $"Not enough space available for drive {driveName}";
                    return;
                }

                GameDownloadUserControl? newDownload = null;
                await Dispatcher.InvokeAsync(() =>
                {
                    GameDownloadUserControl? oldDownloadEntry = DownloadsViewModel.Instance.DownloadedGames.FirstOrDefault(g => g.GetGameId() == game.ID);
                    if (oldDownloadEntry != null)
                    {
                        DownloadsViewModel.Instance.DownloadedGames.Remove(oldDownloadEntry);
                    }

                    newDownload = new GameDownloadUserControl(game, selectedDirectory, false);
                    DownloadsViewModel.Instance.DownloadedGames.Insert(0, newDownload);
                    SyncDownloadsPanel();
                }, DispatcherPriority.Send);

                if (newDownload != null)
                {
                    await Dispatcher.InvokeAsync(() => newDownload.StartDownload(), DispatcherPriority.ContextIdle);
                }

                MainWindowViewModel.Instance.AppBarText = $"'{game.Title}' has been added to the download queue";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to start download: {ex}");
                MainWindowViewModel.Instance.AppBarText = $"Could not start download: {ex.Message}";
            }
        }

        private async Task<bool> IsAlreadyDownloaded(int id)
        {
            if (DownloadsViewModel.Instance.DownloadedGames.Any(gameUC => gameUC?.GetGameId() == id))
            {
                MessageDialogResult result = await ((MetroWindow)App.Current.MainWindow).ShowMessageAsync("This game was already downloaded. Do you want to overwrite this file?",
                    string.Empty, MessageDialogStyle.AffirmativeAndNegative, new MetroDialogSettings() { AffirmativeButtonText = "Yes", NegativeButtonText = "No", AnimateHide = false });
                return result != MessageDialogResult.Affirmative;
            }
            return false;
        }

        private bool IsAlreadyDownloading(int id)
        {
            return DownloadsViewModel.Instance.DownloadedGames.Any(gameUC => gameUC?.IsGameIdDownloading(id) == true);
        }

        private bool IsEnoughDriveSpaceAvailable(long gameSize, string directory)
        {
            string? driveName = Path.GetPathRoot(directory);
            if (string.IsNullOrWhiteSpace(driveName))
                return false;

            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                if (drive.IsReady && string.Equals(drive.Name, driveName, StringComparison.OrdinalIgnoreCase))
                {
                    return (drive.AvailableFreeSpace - 1000) > gameSize;
                }
            }
            return false;
        }

        private async void DeleteAllDownloads_Click(object sender, RoutedEventArgs e)
        {
            MessageDialogResult result = await ((MetroWindow)App.Current.MainWindow).ShowMessageAsync("Are you sure you want to delete all canceled and completed downloads?\n\nThis cannot be undone.", string.Empty, MessageDialogStyle.AffirmativeAndNegative, new MetroDialogSettings() { AffirmativeButtonText = "Yes", NegativeButtonText = "No", AnimateHide = false });

            if (result == MessageDialogResult.Affirmative)
            {
                foreach (var download in DownloadsViewModel.Instance.DownloadedGames.ToList())
                {
                    try
                    {
                        await download.DeleteFile(false);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to delete download: {ex}");
                    }
                }
                SyncDownloadsPanel();
            }
        }
    }
}
