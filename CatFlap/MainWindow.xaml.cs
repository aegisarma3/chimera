using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Reflection;
using System.IO;
using System.Net;
using MahApps.Metro.Controls;
using Newtonsoft.Json;
using System.ComponentModel;
using System.Threading;
using System.Diagnostics;
using MahApps.Metro;
using MahApps.Metro.Controls.Dialogs;
using System.Windows.Shell;
using System.Windows.Navigation;
using System.Windows.Controls;
using vbAccelerator.Components.Shell;
using System.Text;
using System.Windows.Input;
using System.Windows.Media.Effects;

namespace Catflap
{
    public partial class MainWindow : MetroWindow
    {
        private Repository repository;
        private Dictionary<string, string> resolvedVariables = new Dictionary<string, string>();
       
        private string rootPath;
        private string appPath;
        private bool IgnoreRepositoryLock = false;

        private bool CloseAfterSync = false;

        private void Log(String str, bool showMessageBox = false) {
            Console.WriteLine(str);

            logTextBox.Dispatcher.Invoke((Action)(() =>
            {
            logTextBox.Text += DateTime.Now.ToString("HH:mm:ss") + "> " + str + "\n";
                logTextBox.ScrollToEnd();

            if (showMessageBox)
                this.ShowMessageAsync("Log", str);
            }));
        }


        private static string[] resourcesToUnpack =
        {
            "rsync.exe.gz" , "cygwin1.dll.gz",  "cyggcc_s-1.dll.gz", "kill.exe.gz"
        };
        private static string[] resourcesToPurge =
        {
            "cygintl-8.dll", "cygpopt-0.dll", "cygiconv-2.dll"
        };

        // UI colour states:
        // green/blue - all ok, repo up to date
        private Accent accentOK = ThemeManager.GetAccent("Olive");
        // orange     - repo not current
        private Accent accentWarning = ThemeManager.GetAccent("Amber");
        // red        - failure
        private Accent accentError = ThemeManager.GetAccent("Crimson");
        // mauve     - busy
        private Accent accentBusy = ThemeManager.GetAccent("Mauve");

        private Accent currTheme;

        private Accent SetTheme(Accent t)
        {
            Accent c = currTheme;
            if (currTheme != t)
            {
                currTheme = t;
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var themestr = (repository.LatestManifest != null && repository.LatestManifest.darkTheme) ?
                        "BaseDark" : "BaseLight";

                    ThemeManager.ChangeAppStyle(Application.Current, t, ThemeManager.GetAppTheme(themestr));
                });
            }
            return c;
        }

        private void SetUIState(bool enabled)
        {
            btnVerify.IsEnabled = enabled && repository.Status.current;

            var wantEnabled = false;

            if (repository.LatestManifest != null)
                wantEnabled = true;

            if (repository.Status.current)
            {
                if (repository.LatestManifest.runAction == null)
                {
                    btnRun.Content = "nothing to do";
                    wantEnabled = false;
                }
                else
                {
                    btnRun.Content = "run";
                    wantEnabled = true;
                }
            }
            else
            {
                btnRun.Content = "sync";
                wantEnabled = true;
            }

            btnRun.IsEnabled = enabled && wantEnabled;
        }

        private void SetUIProgressState(bool indeterminate, double percentage = -1, string message = null)
        {
            // globalProgress.IsIndeterminate = indeterminate;
            if (percentage >= 0)
            {
                // int p = (int) (percentage * 100).Clamp(0, 100);
                // labelDLSize.Text = p + "%";
                // globalProgress.Value = (percentage * 100).Clamp(0, 100);
            }

            if (message != null)
                labelDownloadStatus.Dispatcher.Invoke((Action)(() => labelDownloadStatus.Text = message.Trim()));

            taskBarItemInfo.Dispatcher.Invoke((Action)(() =>
            {
                if (indeterminate)
                {
                    taskBarItemInfo.ProgressState = TaskbarItemProgressState.Indeterminate;
                }                    
                else
                {
                    if (percentage == -1)
                        taskBarItemInfo.ProgressState = TaskbarItemProgressState.None;
                    else
                    {
                        taskBarItemInfo.ProgressState = TaskbarItemProgressState.Normal;
                        taskBarItemInfo.ProgressValue = percentage.Clamp(0, 1);
                    }
                }
            }));
        }

        private void SetGlobalStatus(bool lastOperationOK = true, string message = null, double percent = -1, string progressMsg = null)
        {
            if (cts != null)
                 SetTheme(accentBusy);
            else if (!lastOperationOK)
                SetTheme(accentError);
            else if (repository.Status.current)
                SetTheme(accentOK);
            else
                SetTheme(accentWarning);

            this.Dispatcher.Invoke(() =>
            {
                var title = repository.LatestManifest != null && repository.LatestManifest.title != null ? repository.LatestManifest.title : "Catflap";
                if (message != null)
                    this.Title = message;
                else
                    this.Title = title;
            });

            labelDLSize.Dispatcher.Invoke(() =>
            {
                if (repository.LatestManifest != null)
                {
                    
                    labelDLSize.ToolTip = string.Format("{0} files in {1} sync items, last updated {2}",
                        repository.LatestManifest.sync.Select(f => f.count > 0 ? f.count : 1).Sum(),
                        repository.LatestManifest.sync.Count(),
                        repository.LatestManifest.sync.Select(f => f.mtime).Max().PrettyInterval()
                    );

                    if (repository.Status.directoriesToVerify.Any() || repository.Status.filesToVerify.Any())
                    {
                        labelDLSize.ToolTip += "\nThe following items are outdated:";
                        repository.Status.directoriesToVerify.ForEach(e => labelDLSize.ToolTip += "\n" + e.name);
                        repository.Status.filesToVerify.ForEach(e => labelDLSize.ToolTip += "\n" + e.name);
                    }

                    labelDLSize.Text = "";

                    if (repository.AlwaysAssumeCurrent)
                    {
                        labelDLSize.Text = "-nocheck";
                    }

                    else if (repository.Status.guesstimatedBytesToVerify > 0 || repository.Status.maxBytesToVerify > 0)
                    {
                        if (repository.Status.guesstimatedBytesToVerify < 1)
                            labelDLSize.Text = "objects need syncing";
                        else
                            labelDLSize.Text += string.Format("{0} need syncing",
                                repository.Status.guesstimatedBytesToVerify.BytesToHuman()
                            );
                    }
                    else
                    {
                        labelDLSize.Text = repository.Status.sizeOnRemote.BytesToHuman() + " in sync";
                    }


                }
                else
                    labelDLSize.Text = "?";
            });
        }

        private void RefreshBackgroundImage()
        {
            ImageBrush myBrush = new ImageBrush();
            myBrush.Stretch = Stretch.Uniform;
            Image image = new Image();

            if (File.Exists(appPath + "/catflap.bgimg"))
            {
                var bytes = System.IO.File.ReadAllBytes(appPath + "/catflap.bgimg");
                var ms = new MemoryStream(bytes);
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.StreamSource = ms;
                bi.EndInit();
                image.Source = bi;
            }
            else
                image.Source = new BitmapImage(new Uri("pack://application:,,,/Resources/bgimg.png"));

            myBrush.ImageSource = image.Source;
            gridMainWindow.Background = myBrush;

            if (repository.LatestManifest != null && repository.LatestManifest.textColor != null)
            {
                var fgBrush = (SolidColorBrush)(new BrushConverter().ConvertFrom(repository.LatestManifest.textColor));
                labelDLSize.Foreground = fgBrush;
                labelDownloadStatus.Foreground = fgBrush;
            }

            if (File.Exists(appPath + "/favicon.ico"))
            {
                var bytes = System.IO.File.ReadAllBytes(appPath + "/favicon.ico");
                var ms = new MemoryStream(bytes);
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.StreamSource = ms;
                bi.EndInit();
                this.Icon = bi;
            }
            else
            {
                this.Icon = new BitmapImage(new Uri("pack://application:,,,/Resources/app.ico"));
            }
        }

        public MainWindow() {
            InitializeComponent();

            labelDownloadStatus.Text = "";
            btnCancel.Visibility = System.Windows.Visibility.Hidden;

            var fi = new FileInfo(Assembly.GetExecutingAssembly().Location);
            rootPath = Directory.GetCurrentDirectory();

            Log("%root% = " + rootPath);
            appPath = rootPath + "\\" + fi.Name + ".catflap";
            Log("%app% = " + appPath);
            Directory.SetCurrentDirectory(rootPath);

            if (!File.Exists(appPath + "\\catflap.json"))
            {
                var sw = new SetupWindow();
                if (!sw.SetupOk)
                {
                    var ret = sw.ShowDialog();
                    if (!ret.Value)
                    {
                        Application.Current.Shutdown();
                        return;
                    }
                }
            }

            Assembly assembly = Assembly.GetExecutingAssembly();
            FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(assembly.Location);
            string major = String.Join(".", fvi.FileVersion.Split('.').Take(3));
            string point = String.Join(".", fvi.FileVersion.Split('.').Skip(3));
            btnHelp.Content = major + (point == "0" ? "" : "." + point);

            foreach (string src in resourcesToPurge)
            {
                var x = appPath + "\\" + src;
                if (File.Exists(x))
                {
                    Log("Deleting obsolete bundled file: " + src);
                    File.Delete(x);
                }                    
            }

            foreach (string src in resourcesToUnpack)
            {
                var dst = src;
                if (dst.EndsWith(".gz"))
                    dst = dst.Substring(0, dst.Length - 3);

                if (!File.Exists(appPath + "\\" + dst) || File.GetLastWriteTime(appPath + "\\" + dst) != File.GetLastWriteTime(fi.FullName))
                {
                    Log("Extracting bundled file: " + src);
                    App.ExtractResource(src, appPath + "\\" + dst);
                    File.SetLastWriteTime(appPath + "\\" + dst, File.GetLastWriteTime(fi.FullName));
                }
            }
            
            this.Activated += new EventHandler((o, ea) =>
            {
                if (repository.LatestManifest != null)
                {
                    repository.UpdateStatus();
                    this.SetGlobalStatus();
                }
            });

            this.KeyDown += new KeyEventHandler(async (o, kea) => {
                if (kea.Key == Key.F5)
                {
                    await UpdateRootManifest();
                    repository.UpdateStatus();
                    this.SetGlobalStatus();
                }
            });

            this.repository = new Repository(rootPath, appPath);

            this.repository.OnDownloadStatusInfoChanged += OnDownloadStatusInfoChangedHandler;

            this.repository.OnDownloadMessage += (string message, bool show) =>
            {
                if (show)
                    labelDownloadStatus.Dispatcher.Invoke((Action)(() => labelDownloadStatus.Text = message.Trim()));
                Log(message);
            };

            if (App.mArgs.Contains("-nolock"))
            {
                App.mArgs = App.mArgs.Where(x => x != "-nolock").ToArray();
                IgnoreRepositoryLock = true;
            }

            if (App.mArgs.Contains("-simulate"))
            {
                repository.Simulate = true;
            }

            if (App.mArgs.Contains("-nocheck"))
            {
                App.mArgs = App.mArgs.Where(x => x != "-nocheck").ToArray();
                this.repository.AlwaysAssumeCurrent = true;
            }

            if (App.mArgs.Contains("-run"))
            {
                App.mArgs = App.mArgs.Where(x => x != "-run").ToArray();
                UpdateAndRun(false);
            }
            else if (App.mArgs.Contains("-runwait"))
            {
                App.mArgs = App.mArgs.Where(x => x != "-runwait").ToArray();
                UpdateAndRun(false);
            }
            else
                UpdateRootManifest();
        }

        private void OnDownloadStatusInfoChangedHandler(Catflap.Repository.DownloadStatusInfo info)
        {
            if (info.currentFile != null)
            {
                var msg = info.currentFile.PathEllipsis(60);
                if (info.currentBps > 0)
                    msg += " - " + info.currentBps.BytesToHuman() + "/s";
                if (info.currentPercentage > 0)
                    msg += ", " + ((int)(info.currentPercentage * 100)) + "%";

                SetUIProgressState(info.globalFileTotal == 0, info.globalPercentage, msg);
            }
            else
            {
                SetUIProgressState(info.globalFileTotal == 0, info.globalPercentage, null);
            }

            if (info.globalFileTotal > 0)
                SetGlobalStatus(true, string.Format("{0}%", (int)(info.globalPercentage * 100).Clamp(0, 100), info.globalPercentage));
            else
            {
                SetGlobalStatus(true);
            }              
        }

        private async void UpdateAndRun(bool waitForExit)
        {
            await UpdateRootManifest();
            await Sync(false);
            if (!repository.Status.current)
                return;
            await Task.Delay(100);
            WindowState = WindowState.Minimized;

            var t = RunAction();
            if (waitForExit)
                await t;
            else
                await Task.Delay(1000);

            Application.Current.Shutdown();
        }

        private static FileInfo[] GetDirectoryElements(string parentDirectory)
        {
            return new DirectoryInfo(parentDirectory).GetFiles("*", SearchOption.AllDirectories);
        }

        private async Task UpdateRootManifest(bool setNewAsCurrent = false)
        {
            SetUIProgressState(true);
            SetUIState(false);

            Retry:

            try
            {
                await repository.RefreshManifest(setNewAsCurrent);
            }
            catch (Exception err)
            {
                if (err is WebException)
                {
                    // WebException wex = (WebException) err;

                    MessageBox.Show("Could not retrieve repository manifest: " + err.Message +
                                    ". Switching to offline mode.");
                    repository.AlwaysAssumeCurrent = true;
                    goto Retry;
                }
                else if (err is ValidationException)
                {
                    MessageBox.Show("There are problems with the repository manifest " +
                                    "(This is probably not your fault, it needs to be fixed in the repository!):" +
                                    "\n\n" + err.Message);
                }
                else
                    MessageBox.Show("There has been some problem downloading/parsing the repository manifest:\n\n" +
                                    err.ToString());

                Application.Current.Shutdown();
                return;
            }
            
            RefreshBackgroundImage();

            SetGlobalStatus(true);
            SetUIProgressState(false);

            var revText = string.Format("{0} -> {1}",
                repository.CurrentManifest != null && repository.CurrentManifest.revision != null ? repository.CurrentManifest.revision.ToString() : "?",
                repository.LatestManifest != null && repository.LatestManifest.revision != null ? repository.LatestManifest.revision.ToString() : "?"
            );
            Log("Revision: " + revText);

            /*if (repository.Status.current)
                btnDownload.Content = "verify";
            else
                btnDownload.Content = "sync";*/

            btnCancel.Visibility = System.Windows.Visibility.Hidden;

            /* Verify if we need to restart ourselves. */
            if (repository.RequireRestart)
            {
                await this.ShowMessageAsync("Restart required", "The updater needs to restart.");

                System.Diagnostics.Process.Start(Application.ResourceAssembly.Location);
                Application.Current.Shutdown();
            }

            if (!IgnoreRepositoryLock && repository.LatestManifest.locked != "")
            {
                await this.ShowMessageAsync("Repository locked", repository.LatestManifest.locked);
                Application.Current.Shutdown();
            }

            this.BorderThickness = new Thickness(repository.LatestManifest.border ? 1 : 0);

            var effect = repository.LatestManifest.dropShadows ? new DropShadowEffect() {
                // Color = (Color) ColorConverter.ConvertFromString(repository.LatestManifest.textColor),
                Opacity = 0.5,
                BlurRadius = 10 } : null;
            btnRun.Effect = effect;
            labelDLSize.Effect = effect;
            labelDownloadStatus.Effect = effect;

            SetUIState(true);
        }

        private CancellationTokenSource cts;
        private async Task Sync(bool fullVerify)
        {
            cts = new CancellationTokenSource();

            btnCancel.Visibility = System.Windows.Visibility.Visible;
            SetUIState(false);
            SetGlobalStatus(true, null, 0);
            SetUIProgressState(true);

            long bytesOnNetwork = 0;
            try
            {
                bytesOnNetwork = await repository.UpdateEverything(fullVerify, cts);
                SetUIProgressState(false, 1, null);
            }
            catch (Exception eee)
            {
                if (eee is AggregateException)
                    eee = eee.InnerException;

                if (eee.Message.StartsWith("@ERROR: auth failed on module "))
                {
                }

                SetGlobalStatus(false, "ERROR");
                SetUIProgressState(false, -1, null);
                Log("Error while downloading: " + eee.Message, true);
                Console.WriteLine(eee.ToString());

                return;
            }
            finally
            {
                btnCancel.Visibility = System.Windows.Visibility.Hidden;
                SetUIState(true);
            }
            
            bool wasCancel = cts.IsCancellationRequested;
            cts = null;

            if (wasCancel)
            {
                SetGlobalStatus(true, "ABORTED");
                SetUIProgressState(false, -1, "ABORTED (" + bytesOnNetwork.BytesToHuman() + " of actual network traffic)");
            } 
            else
            {
                SetGlobalStatus(true, "100%", 1);
                SetUIProgressState(false, 1, "Done (" + bytesOnNetwork.BytesToHuman() + " of actual network traffic)");
                Log("Verify/download complete.");

                await UpdateRootManifest(!repository.Simulate);
            }

            if (CloseAfterSync)
                this.Close();
        }

        private async Task RunAction()
        {
            Accent old = SetTheme(accentBusy);
            SetGlobalStatus(true, "Running");
            SetUIState(false);
            try
            {
                await repository.LatestManifest.runAction.Run(repository,
                    repository.LatestManifest.runAction.passArguments ? App.mArgs : new string[] { });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error executing runAction: " + ex.Message);
            }
            finally
            {
                SetUIState(true);
                SetTheme(old);
                SetGlobalStatus(true);
            }
        }

        private async void btnRun_Click(object sender, RoutedEventArgs e)
        {
            long free = (long) Native.GetDiskFreeSpace(rootPath);
            long needed = repository.Status.guesstimatedBytesToVerify + (200 * 1024 * 1024);
            if (free < needed)
            {
                var ret = await this.ShowMessageAsync("Disk space?",
                        "You seem to be running out of disk space on " + rootPath + ". " +
                        "Advanced calculations indicate you might not be able to " +
                        "sync everything: \n\n" +
                        free.BytesToHuman() + " free, but \n" +
                        repository.Status.guesstimatedBytesToVerify.BytesToHuman() + " needed (plus change for temporary files).\n\n" +
                        "Do you still want to run this sync?",
                    MessageDialogStyle.AffirmativeAndNegative);
                if (MessageDialogResult.Negative == ret)
                    return;
            }

            if (repository.Status.current)
                await RunAction();
            else
                await Sync(false);
        }
        
        private async void btnVerify_Click(object sender, RoutedEventArgs e)
        {
            if (repository.Status.current)
            {
                var ret = await this.ShowMessageAsync("Verify?", "Running a full sync will take longer, " +
                    "since it will verify checksums.\n\n" +
                    "This is usually not needed, except when you suspect corruption. You can cancel at any time.\n\n" +
                    "Are you sure this is what you want?",
                    MessageDialogStyle.AffirmativeAndNegative);
                if (MessageDialogResult.Negative == ret)
                    return;
            }
            var fullVerify = repository.Status.current;

            if (fullVerify && repository.Simulate)
            {
                await this.ShowMessageAsync("Cannot simulate", "Full verify does not support simulate-mode, sorry!");
                return;
            }

            await Sync(true);
        }

        private void btnShowHideLog_Click(object sender, RoutedEventArgs e)
        {
            var flyout = this.Flyouts.Items[0] as Flyout;
            flyout.IsOpen = !flyout.IsOpen;
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            if (cts != null && !cts.IsCancellationRequested)
                cts.Cancel();
        }

        protected override async void OnClosing(CancelEventArgs e)
        {
            if (cts != null && !cts.IsCancellationRequested)
            {
                cts.Cancel();
                e.Cancel = true;
                CloseAfterSync = true;
            }
        }

        private void btnHelp_Click(object sender, RoutedEventArgs e)
        {
            Process myProcess = new Process();
            myProcess.StartInfo.UseShellExecute = true;
            myProcess.StartInfo.FileName = "https://github.com/niv/catflap";
            myProcess.Start();
        }

        private void btnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            Process myProcess = new Process();
            myProcess.StartInfo.UseShellExecute = true;
            myProcess.StartInfo.FileName = repository.RootPath;
            myProcess.Start();
        }

        private void btnMakeShortcut_Click(object sender, RoutedEventArgs e)
        {
            repository.MakeDesktopShortcut();
            this.ShowMessageAsync("Shortcut created", "A shortcut to update & run this repository was created on your Desktop.\n\n" +
                "Feel free to rename it and/or change the icon.");
        }
    }
}
