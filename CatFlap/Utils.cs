using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace Catflap
{
    public class Utils
    {
        // TODO move to utils class
        public static FileInfo[] GetDirectoryElements(string parentDirectory)
        {
            // This throws IOException when directories are being locked by catflap
            // (like partial dirs, or delayed updates).
            try
            {
                if (Directory.Exists(parentDirectory))
                    return new DirectoryInfo(parentDirectory).GetFiles("*", SearchOption.AllDirectories);
                else if (File.Exists(parentDirectory))
                    return new FileInfo[] { new FileInfo(parentDirectory) };
                else
                    return new FileInfo[] { };
            }
            catch (IOException)
            {
                return new FileInfo[] { };
            }
        }

    }
}
