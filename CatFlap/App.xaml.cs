using Greg.WPF.Utility;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Catflap
{
    public partial class App : Application
    {
        public static string[] mArgs = new string[] {};

        protected override void OnStartup(StartupEventArgs e)
        {
            this.ShutdownMode = System.Windows.ShutdownMode.OnMainWindowClose;

            if (e.Args.Length > 0)
                mArgs = e.Args;

            ToolTipService.InitialShowDelayProperty.OverrideMetadata(
                typeof(FrameworkElement), new FrameworkPropertyMetadata(0));
            ToolTipService.ShowDurationProperty.OverrideMetadata(
               typeof(FrameworkElement), new FrameworkPropertyMetadata(int.MaxValue));

            Application.Current.DispatcherUnhandledException += Application_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException +=
                new UnhandledExceptionEventHandler(CurrentDomain_UnhandledException);
        }

        private void Application_DispatcherUnhandledException(object sender,
                       System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            ReportException(e.Exception);
            Environment.Exit(1);
        }
        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            ReportException(e.ExceptionObject as Exception);
            Environment.Exit(1);
        }

        private void ReportException(Exception e)
        {
            ExceptionMessageBox window = new ExceptionMessageBox(e, "A exception has occurred. This is a BUG! Pretty please report it, " +
                "so that it can be fixed. Just press 'copy to clipboard' and send it to n@e-ix.net or post it on the GitHub issue tracker.");
            window.ShowDialog();
        }


        public static void ExtractResource(string resource, string destination)
        {
            Stream stream = Application.Current.GetType().Assembly.GetManifestResourceStream("Catflap.Resources." + resource);
            if (resource.EndsWith(".gz"))
                stream = new GZipStream(stream, CompressionMode.Decompress);

            var dest = File.Create(destination);
            stream.CopyTo(dest);
            dest.Close();
        }


        private Job job = new Job();
        private List<Process> trackedProcesses = new List<Process>();
        protected override void OnExit(ExitEventArgs e)
        {
            foreach (Process p in trackedProcesses)
            {
                try
                {
                    if (!p.HasExited)
                        p.Kill();
                }
                catch (Exception)
                {
                    //Handle the exception as you wish
                }
            }

            base.OnExit(e);
        }

        /*
         * Track the given process and kill it forcefully when Catflap exits.
         */
        public void TrackProcess(Process p)
        {
            job.AddProcess(p.Handle);
            trackedProcesses.Add(p);
        }

        public static void KillProcessAndChildren(int pid)
        {
            try
            {
                Process proc = Process.GetProcessById(pid);

                ManagementObjectSearcher searcher = new ManagementObjectSearcher("Select * From Win32_Process Where ParentProcessID=" + pid);
                ManagementObjectCollection moc = searcher.Get();

                foreach (ManagementObject mo in moc)
                    KillProcessAndChildren(Convert.ToInt32(mo["ProcessID"]));

                if (!proc.HasExited)
                    proc.Kill();
            }
            catch (ArgumentException)
            {
                // Process already exited.
            }
        }
    }
}