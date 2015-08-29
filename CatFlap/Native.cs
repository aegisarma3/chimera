using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Catflap
{
    class Native
    {

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetDiskFreeSpaceEx(string lpDirectoryName,
            out ulong lpFreeBytesAvailable,
            out ulong lpTotalNumberOfBytes,
            out ulong lpTotalNumberOfFreeBytes);

        public static ulong GetDiskFreeSpace(string directoryName)
        {
            ulong freeAvail, totalBytes, totalFree;
            GetDiskFreeSpaceEx(directoryName, out freeAvail, out totalBytes, out totalFree);
            return freeAvail;
        }
    }
}
