using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;
using System.IO;
using System.Linq;

namespace Catflap
{
    public partial class Manifest
    {
        public class SyncItem
        {
            // The filename or dirname of the sync item in question.
            // Make sure that directories ALWAYS terminate with a /.
            public string name;

            // The serverside revision. This is used to quickly match local revisions
            // is incremented by the serverside utility automatically.
            // Note that this is a internal uint, not a user-supplied string.
            public uint revision;

            // The sync type. Currently supported:
            // * "rsync": Download the given item via rsync (default).
            // * "delete": Delete the given directory or file from the local repository.
            public string type = "rsync";

            // Uncompressed size in bytes
            public long size;

            // Dir only: Number of files/directories in directory.
            public long count;
            // Dir only: Pass --delete to rsync when syncing directory/ items.
            // Will remove all untracked files that are not present
            // in the repository.
            public bool? purge;

            // Transfer mode. one of 'inplace', 'replace' (default)
            public string mode;

            [JsonProperty(ItemConverterType = typeof(IsoDateTimeConverter))]
            public DateTime mtime;

            // Makes the updater skip syncing this file or directory if it
            // exists on the client. Can be used for initial config-file syncs.
            public bool? ignoreExisting;

            // Do fuzzy-matching on the target. (--fuzzy)
            public bool? fuzzy;

            // Ignore case when updating/copying files (--ignore-case).
            // Note: This needs a special patch on the server!
            public bool? ignoreCase;

            // Returns true if this sync item is current on the given rootPath.
            public bool isCurrent(Repository repository)
            {
                return !isOutdated(repository);
            }

            public long SizeOnDisk(Repository repository)
            {
                long sz = 0;

                if (this.name.EndsWith("/"))
                    sz = (Utils.GetDirectoryElements(repository.RootPath + "/" + this.name).Sum(file => file.Length));

                else
                {
                    var fileInfo = new FileInfo(repository.RootPath + "/" + this.name);

                    if (fileInfo.Exists)
                        sz += fileInfo.Length;

                    // Check partial dir
                    var partialInfo = new FileInfo(fileInfo.Directory.FullName + "/catflap.partials/" + fileInfo.Name);

                    // Check the current-transfer temp dir for in-flight ransfers
                    if (Directory.Exists(repository.TmpPath))
                    {
                        sz += Directory.EnumerateFiles(repository.TmpPath).
                            Where(x => new FileInfo(x).Name.StartsWith(fileInfo.Name)).
                            Select(x => new FileInfo(x).Length).Sum();
                    }
                        // Otherwise, check for partials
                    else if (partialInfo.Exists)
                        sz += partialInfo.Length;
                }

                return sz.Clamp(0);
            }

            private bool isOutdated(Repository repository)
            {
                if (this.name.EndsWith("/"))
                {
                    var directoryElements = Utils.GetDirectoryElements(repository.RootPath + "/" + this.name);

                    // Always check dirs that ..
                    return (this.type == "rsync" && (
                        // Always check dirs that ..

                                // don't exist locally yet
                                !Directory.Exists(repository.RootPath + "/" + this.name) ||

                                (!this.ignoreExisting.GetValueOrDefault() && (

                                    // are not young enough mtime
                                    Math.Abs((new FileInfo(repository.RootPath + "/" + this.name).LastWriteTime - this.mtime).TotalSeconds) > 1 ||

                                    // have mismatching item count
                                    directoryElements.Count() < this.count ||

                                    // are not big enough
                                    directoryElements.Sum(file => file.Length) < this.size
                                ))
                            )) || (this.type == "delete" && (
                                Directory.Exists(repository.RootPath + "/" + this.name)
                            ));
                }
                else
                {
                    return (this.type == "rsync" && (
                                !File.Exists(repository.RootPath + "/" + this.name) ||

                                (!this.ignoreExisting.GetValueOrDefault() && (
                                    (this.mtime != null && Math.Abs((new FileInfo(repository.RootPath + "/" + this.name).LastWriteTime - this.mtime).TotalSeconds) > 1) ||

                                    (new FileInfo(repository.RootPath + "/" + this.name).Length != this.size)
                                ))
                            )) || (this.type == "delete" && (
                                File.Exists(repository.RootPath + "/" + this.name)
                            ));
                }
            }
        }
    }
}
