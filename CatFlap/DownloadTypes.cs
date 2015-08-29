using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Catflap
{

    interface IDownloader
    {
    }

    class RSyncDownloader : IDownloader
    {
        public bool Simulate = false;
        public bool VerifyChecksums = false;
        public string appPath;
        public string tmpPath;

        private const string rsyncFlags =
            /* */
            "--one-file-system --copy-links " +
            
            /* We need to preserve +x so that .exe files can actually be run. */
            "--no-owner --no-group --no-acls --no-xattrs --no-perms --executability " +

            /* Remove empty dirs by default to keep it clean. */
            "--prune-empty-dirs " +

            /* These flags only concern stdout output and are needed for the parser. */
            "--no-human-readable --stats --out-format 'NEWFILE %i %l %n' --progress " +

            /* Compression helps a lot with speeding up transfers, obviously; esp. empty or sparse files.
             * Level 1 is enough to get nearly all the speed advantage while not hammering slow clients or servers. */
            "--compress --compress-level=1 " +

            /* This is quite expensive for the server. We turn it off by default. You can still configure it in
             * the manifest if you require that kind of accuracy - but that's what verify is for.
             " --checksum"
             */

            /* Sync times. This is important. Also, do fsyncs after each sync. */
            "--times " +

            /* Bufferbloat optimisation time!
             * As of this writing, the default sock recv buffers on Windows 7 are 8KB.
             * That's nowhere near enough to saturate even a moderately fast broadband connection.
             * Theoretically, to saturate 1Gbit/s, we need a buffer of about 1-1.5MB.
             * So .. we're basically just setting that.
             * TCP_NODELAY should help on higher-latency or otherwise overly saturated links.
             */
            "--sockopts SO_SNDBUF=65536,SO_RCVBUF=1572864,TCP_NODELAY ";

        private string rsyncFlagsDirectory = "--recursive";
        private string rsyncFlagsVerify = "--checksum";
        private string rsyncFlagsNoVerify = "";

        /*
            stdout:            0   0%    0.00kB/s    0:00:00
            stdout:      2916352   2%    2.75MB/s    0:00:47
            stdout:      6029312   4%    2.85MB/s    0:00:45
            stdout:      8978432   6%    2.83MB/s    0:00:44
            stdout:     12255232   8%    2.90MB/s    0:00:42
            stdout:     15007744  10%    2.86MB/s    0:00:41
            stdout:     17006592  12%    2.60MB/s    0:00:45
            stdout:     19333120  14%    2.46MB/s    0:00:46
            stdout:     28988893 100%   16.61MB/s    0:00:12 (xfr#1, to-chk=0/1)
         */
        private Regex rxRsyncProgress = new Regex(@"^\s*(\d+)\s+(\d+)%\s+(\d+\.\d+)([kM]B/s)\s+(.+)$");

        // NEWFILE .d..t...... 4096 ./
        // NEWFILE >f..T...... 80060515 silm_portraits.hak
        // NEWFILE >f+++++++++ 27302717 silm_tdm01.hak
        private Regex rxRsyncNewFile = new Regex(@"^NEWFILE (.{11}) (\d+) (.+)$");

        //  Literal data: 0 bytes
        private Regex rxRsyncLiteralData = new Regex(@"^Literal data: (\d+) bytes$");

        // Total bytes sent: 77270
        private Regex rxRsyncTotalBytesSent = new Regex(@"^Total bytes sent: (\d+)");

        // Total bytes received: 1286
        private Regex rxRsyncTotalBytesReceived = new Regex(@"^Total bytes received: (\d+)");

        private bool currentRunWasChanged;

        private string stdErr;
        private long bytesOnNetwork = 0;
        private bool cancelled = false;

        private Repository repository;

        public RSyncDownloader(Repository repo)
        {
            this.repository = repo;
        }

        private void SIGTERM(int id)
        {
            var pProcess = new System.Diagnostics.Process();
            pProcess.StartInfo.FileName = appPath + "\\kill.exe";
            pProcess.StartInfo.Arguments = "" + id;
            pProcess.StartInfo.CreateNoWindow = true;
            pProcess.StartInfo.UseShellExecute = false;
            pProcess.StartInfo.WorkingDirectory = appPath;

            if (pProcess.StartInfo.EnvironmentVariables.ContainsKey("PATH"))
                pProcess.StartInfo.EnvironmentVariables.Remove("PATH");

            if (pProcess.StartInfo.EnvironmentVariables.ContainsKey("CYGWIN"))
                pProcess.StartInfo.EnvironmentVariables.Remove("CYGWIN");

            pProcess.StartInfo.EnvironmentVariables.Add("CYGWIN", "nodosfilewarning");

            pProcess.Start();
            pProcess.WaitForExit();
        }
        
        // Returns true if the file was changed in any way.
        public Task<bool> Download(string source, Manifest.SyncItem syncItem, string modPath,
            Catflap.Repository.DownloadProgressChanged dpc, Catflap.Repository.DownloadEnd de, Catflap.Repository.DownloadMessage dm,
            CancellationTokenSource cts, string overrideDestination = null)
        {
            var ct = cts.Token;

            stdErr = "";

            return Task.Run<bool>(delegate() {
                currentRunWasChanged = false;
                var p = RunRSync(source, syncItem, modPath, dpc, dm, overrideDestination);
                (Application.Current as App).TrackProcess(p);
                
                // Wait for the pid to appear.
                while (0 == p.Id && !p.HasExited);

                while (!p.HasExited)
                {
                    if (ct.IsCancellationRequested)
                    {
                        cancelled = true;
                        dm.Invoke("<cancelling (patience)>", true);

                        /* Try Ctrl+C first so we can catch --replace/partial transfers */
                        SIGTERM(p.Id);

                        /* Lets wait for a generous amount of time to wait for rsync to gracefully
                         * terminate. This can happen on slow disks.
                         */
                        p.WaitForExit(20000);

                        App.KillProcessAndChildren(p.Id);
                        p.WaitForExit();
                        ct.ThrowIfCancellationRequested();
                    }
                    else
                        Thread.Sleep(100);
                }
                p.WaitForExit();
                de.Invoke(p.ExitCode != 0, stdErr, bytesOnNetwork);

                return currentRunWasChanged;
            }, ct);
        }

        private Process RunRSync(String rsyncUrl, Manifest.SyncItem syncItem, string modPath,
            Catflap.Repository.DownloadProgressChanged dpc, Catflap.Repository.DownloadMessage dm, string overrideDestination = null)
        {
            var targetFileName = syncItem.name;

            string targetDir = modPath + "\\" + Path.GetDirectoryName(targetFileName);
            Directory.CreateDirectory(targetDir);

            string rsyncTargetSpec =  overrideDestination != null ? overrideDestination :".";
            bool isDir = targetFileName.EndsWith("/");

            if (this.repository.Username != null)
                rsyncUrl = rsyncUrl.Replace("%user%", this.repository.Username);

            string va = rsyncFlags + " " + "\"" + rsyncUrl + "\"" + " " + "\"" + rsyncTargetSpec.ShellEscape() + "\"";
            
            if (VerifyChecksums) va += " " + rsyncFlagsVerify;
            if (!VerifyChecksums) va += " " + rsyncFlagsNoVerify;
            if (Simulate) va += " --dry-run";
            
            if (isDir) va += " " + rsyncFlagsDirectory;

            if (syncItem.ignoreExisting.GetValueOrDefault()) va += " --ignore-existing";

            // Only ever allow purge on directories, obviously.
            if (isDir && syncItem.purge.GetValueOrDefault()) va += " --delete-delay";

            if (syncItem.ignoreCase.GetValueOrDefault()) va += " --ignore-case";
            if (syncItem.fuzzy.GetValueOrDefault()) va += " --fuzzy";

            va += " \"--temp-dir=" + tmpPath.ShellEscape() + "\"";

            switch (syncItem.mode)
            {
                case "inplace":
                    va += " --inplace";
                    break;

                default: // "replace"
                    /* We cannot keep partials for ignore-existing .. */
                    if (!syncItem.ignoreExisting.GetValueOrDefault())
                        va += " --partial-dir=catflap.partials";

                    va += " --delay-updates";
                    break;
            }

            long thisFileTotalSize = 0;
            string thisFilename = targetFileName;

            dm.Invoke("(rsync) " + va);
            
            Process pProcess = new System.Diagnostics.Process();
            pProcess.StartInfo.FileName = appPath + "\\rsync.exe";
            pProcess.StartInfo.Arguments = va;
            pProcess.StartInfo.CreateNoWindow = true;
            pProcess.StartInfo.UseShellExecute = false;
            pProcess.StartInfo.RedirectStandardOutput = true;
            pProcess.StartInfo.RedirectStandardError = true;
            pProcess.StartInfo.RedirectStandardInput = true;            
            pProcess.StartInfo.WorkingDirectory = targetDir;

            if (pProcess.StartInfo.EnvironmentVariables.ContainsKey("PATH"))
                pProcess.StartInfo.EnvironmentVariables.Remove("PATH");

            if (pProcess.StartInfo.EnvironmentVariables.ContainsKey("CYGWIN"))
                pProcess.StartInfo.EnvironmentVariables.Remove("CYGWIN");

            pProcess.StartInfo.EnvironmentVariables.Add("CYGWIN", "nodosfilewarning");

            if (this.repository.Password != null)
                pProcess.StartInfo.EnvironmentVariables.Add("RSYNC_PASSWORD", this.repository.Password);

            Console.WriteLine("VA = " + va);

            pProcess.OutputDataReceived += (s, ee) =>
            {
                if (ee.Data != null)
                {
                    /* Send everything to the log as-is. */
                    dm.Invoke("(stdout) " + ee.Data);

                    switch (ee.Data)
                    {
                        case "receiving file list ... ":
                        case "receiving incremental file list":
                            dm.Invoke("<receiving list>", true);
                            break;

                        default:
                            Match mr;

                            // Progress indicator
                            if ((mr = rxRsyncProgress.Match(ee.Data)).Success)
                            {
                                long bytesDone = long.Parse(mr.Groups[1].Value, CultureInfo.InvariantCulture);
                                int percentage = int.Parse(mr.Groups[2].Value, CultureInfo.InvariantCulture);
                                double rate = double.Parse(mr.Groups[3].Value, CultureInfo.InvariantCulture);
                                string rateDesc = mr.Groups[4].Value;
                                string eta = mr.Groups[5].Value;
                                if (rateDesc == "kB/s")
                                    rate *= 1024;
                                if (rateDesc == "MB/s")
                                    rate *= 1024 * 1024;

                                dpc.Invoke(cancelled ? "<cancelling>" : thisFilename, percentage, bytesDone, thisFileTotalSize, (int)rate);
                            }

                            // new file
                            else if ((mr = rxRsyncNewFile.Match(ee.Data)).Success)
                            {
                                string flags = mr.Groups[1].Value;
                                thisFileTotalSize = long.Parse(mr.Groups[2].Value, CultureInfo.InvariantCulture);
                                string fname = mr.Groups[3].Value;
                                //if (isDir)
                                if (fname == "." || fname == "./")
                                    fname = targetFileName;
                                else if (isDir)
                                    fname = targetFileName + fname;
                                else
                                    fname = targetFileName;

                                // YXcstpogz
                                // X: update type (< > c .)
                                // Y: filetype (f d L D S)
                                // c: checksum, s: size, t: time, 
                                var action = "";
                                switch (flags[0])
                                {
                                    case '*': action = "deleting"; break;
                                    case '<': action = "sending"; break;
                                    case '>': action = ""; break;
                                    case 'c': action = "creating"; break;
                                }

                                /*var typeStr = "";
                                switch (flags[1])
                                {
                                    case 'f': typeStr = "file"; break;
                                    case 'd': typeStr = "directory"; break;
                                }*/

                                thisFilename = action + " " + fname;
                                var flagStr = flags.Substring(2).Replace(".", "").Replace("+", "").Trim();
                                if (flagStr != "")
                                    thisFilename += " [" + flagStr + "]";

                                dm.Invoke(thisFilename, true);
                            }


                            // Literal data: xx
                            else if ((mr = rxRsyncLiteralData.Match(ee.Data)).Success)
                            {
                                if (mr.Groups[1].Value != "0")
                                    currentRunWasChanged = true;
                            }

                            // Total bytes received
                            else if ((mr = rxRsyncTotalBytesReceived.Match(ee.Data)).Success)
                            {
                                if (mr.Groups[1].Value != "0")
                                    bytesOnNetwork += long.Parse(mr.Groups[1].Value, CultureInfo.InvariantCulture);
                            }

                            // Total bytes sent
                            else if ((mr = rxRsyncTotalBytesSent.Match(ee.Data)).Success)
                            {
                                if (mr.Groups[1].Value != "0")
                                    bytesOnNetwork += long.Parse(mr.Groups[1].Value, CultureInfo.InvariantCulture);
                            }

                            break;

                    }
                }
            };
            pProcess.ErrorDataReceived += (s, ee) =>
            {
                if (ee.Data != null)
                {
                    stdErr += ee.Data + "\n";
                    Console.WriteLine("STDERR: " + ee.Data);
                    dm.Invoke("ERROR: " + ee.Data);
                }
            };

            dm.Invoke("Verifying " + syncItem.name + " (checksumming/waiting for server)", true);

            pProcess.Start();
            pProcess.BeginOutputReadLine();
            pProcess.BeginErrorReadLine();

            // This eats "Password:", just in case the server is misconfigured.
            pProcess.StandardInput.Write("\n");

            return pProcess;
        }
    }
}
