using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace Catflap
{
    public partial class Manifest
    {
        public class ManifestAction
        {
            // The displayed string on the button.
            public string name;
            // The path to a binary file to execute.
            // Supports substituting variables as described below.
            public string execute;
            // Argument string passed to binary.
            // Supports substituting variables as described below.
            public string arguments;

            // Set to true to allow client arguments being passed/appended
            // to the command line for run action.
            public bool passArguments;

            // Use the OS shell to run this command (see https://msdn.microsoft.com/en-us/library/system.diagnostics.processstartinfo.useshellexecute%28v=vs.110%29.aspx).
            // This needs to be true if you want to launch anything other than a .exe file.
            public bool shellExecute = true;

            // The verb to run this action as. Values that should work on all systems:
            // "open"      - Run/launch a file with the default association (this should be the same as "")
            // "runas"     - prompt to run this command as a administrator
            // "runasuser" - prompt for user/password to run program as
            // Note that shellExecute needs to be true for this to work.
            public string verb = "";

            /*
             * Substitutable variables:
             * 
             * %root%     - the root directory (e.g. where catflap.exe is located)
             * %app%      - the app directory, where catflap stores it's internal data (e.g. catflap.exe.catflap)
             * %user%     - the stored user credential, if any
             */
            private string SubstituteVars(Repository repository, string a)
            {
                return a.
                    Replace("%app%", repository.AppPath).
                    Replace("%root%", repository.RootPath).
                    Replace("%user%", repository.Username);
            }

            public async Task Run(Repository repository, string[] additionalArgs)
            {
                var cmd = SubstituteVars(repository, this.execute);
                var args = SubstituteVars(repository, this.arguments) + (" " + string.Join(" ", additionalArgs)).TrimEnd(' ');

                Console.WriteLine("verb: " + this.verb);

                Process pProcess = new System.Diagnostics.Process();
                pProcess.StartInfo.FileName = cmd;

                pProcess.StartInfo.UseShellExecute = this.shellExecute;

                if (pProcess.StartInfo.Verbs.Contains(this.verb))
                    pProcess.StartInfo.Verb = this.verb;
                
                pProcess.StartInfo.Arguments = args;
                pProcess.StartInfo.WorkingDirectory = repository.RootPath;

                await Task.Run(delegate()
                {
                    pProcess.Start();
                    pProcess.WaitForExit();
                });
            }
        }


    }
}
