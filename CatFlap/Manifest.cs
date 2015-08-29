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
    [Serializable]
    public partial class Manifest
    {
        // Repositories can be locked so that clients will be denied with a
        // appropriate message. Set to "" to unlock.
        public string locked = "";

        // The manifest file version. Do not touch.
        public int version;

        // Optional title, which will be displayed in the title bar.
        public string title;

        // The base URL where catflap.json can be found.
        public string baseUrl;

        // The rsync URL where all sync data can be found.
        // Supports a variable %user% for the current user, if any.
        public string rsyncUrl;

        // Setting a background: have a file named "catflap.bgimg" in the repository root, directly
        // under baseUrl. Make sure the webserver observes If-Modified-Since or clients will re-
        // download it on each check!
        // Supported file formats: jpg, png, gif, animated-gif, and everything else wpf-Image does.
        // This fills the white background completely, so the recommended image size is exactly
        // 400x470 px.
        // Leave empty to use the default background image.

        // Likewise, you can set a application icon that will be used for shortcuts and the taskbar
        // by having a favicon.ico on your webserver.

        // Text color in hexadecimal notation (for example, "#ffee33"). Can be used to adjust
        // to background images where black does not work.
        public string textColor = "#000000";

        // Use a dark theme instead of light.
        public bool darkTheme = true;

        // Make buttons and text have drop shadows.
        public bool dropShadows = true;

        // Show a 1px border around the main window.
        public bool border = false;

        // A optional revision string, which will be printed to the log, but has no bearing on syncing.
        // Useful for debugging or informational displays on clients.
        public string revision;

        // Warn the user if he's doing setup in a directory that contains data not tracked by this repository.
        public Boolean warnWhenSetupWithUntracked = false;

        // Expect files to be present when setting up repo, give warning otherwise.
        public string[] warnWhenSetupWithoutFiles = { };

        // This sets a default for "fuzzy" on all sync items, unless otherwise given in each item.
        public bool? fuzzy;

        // This sets a default for "ignoreCase" on all sync items, unless otherwise given in each item.
        public bool? ignoreCase;

        // This sets a default for "ignoreExisting" on all sync items, unless otherwise given in each item.
        public bool? ignoreExisting;

        // This sets a default for "purge" on all sync items, unless otherwise given in each item.
        public bool? purge;

        public List<SyncItem> sync;

        public ManifestAction runAction;
    }
}
