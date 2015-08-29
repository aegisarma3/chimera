using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Windows;

namespace Catflap
{
    public class ValidationException : Exception { public ValidationException(string message) : base(message) { } };
    
    public partial class Manifest
    {
        public void Validate(string rootPath)
        {
            // Basic sanity checks for all sync items
            foreach (var syncItem in this.sync)
            {
                var fullPath = (rootPath + "/" + syncItem.name).NormalizePath();

                if (fullPath.EndsWith(".catflap"))
                    throw new ValidationException("cannot sync items into catflap directories at " + syncItem.name);

                if (fullPath == rootPath)
                    throw new ValidationException("cannot sync the root path directly at " + syncItem.name);

                if (!fullPath.StartsWith(rootPath))
                    throw new ValidationException("would place synced item outside of root path: " + syncItem.name);

                if (syncItem.type != "delete" && syncItem.type != "rsync")
                    throw new ValidationException("invalid sync item type: " + syncItem.type + " for " + syncItem.name);
            }

            if (!this.baseUrl.StartsWith("http://") && !this.baseUrl.StartsWith("https://"))
                throw new ValidationException("baseUrl does not start with http(s)://");
            if (!this.rsyncUrl.StartsWith("rsync://"))
                throw new ValidationException("rsyncUrl does not start with rsync://");

            if (this.version != Manifest.VERSION)
                throw new ValidationException("Your catflap.exe is of a different version than this repository (Expected: " +
                    this.version + ", you: " + Manifest.VERSION + "). Please make sure you're using the right version.");

            if (this.ignoreCase.HasValue)
                foreach (var syncItem in this.sync)
                    if (!syncItem.ignoreCase.HasValue)
                        syncItem.ignoreCase = this.ignoreCase.Value;

            if (this.fuzzy.HasValue)
                foreach (var syncItem in this.sync)
                    if (!syncItem.fuzzy.HasValue)
                        syncItem.fuzzy = this.fuzzy.Value;

            if (this.ignoreExisting.HasValue)
                foreach (var syncItem in this.sync)
                    if (!syncItem.ignoreExisting.HasValue)
                        syncItem.ignoreExisting = this.ignoreExisting.Value;

            if (this.purge.HasValue)
                foreach (var syncItem in this.sync)
                    if (!syncItem.purge.HasValue)
                        syncItem.purge = this.purge.Value;
        }
    }
}
