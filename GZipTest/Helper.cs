using System;
using System.IO;
using System.Management;

namespace GZipTest
{
    public static class Helper
    {
        private static readonly ManagementObjectSearcher RamMonitor = new ManagementObjectSearcher("SELECT FreePhysicalMemory FROM Win32_OperatingSystem");

        /// <summary>
        /// Returns RAM free memory
        /// </summary>
        /// <returns></returns>
        public static long GetAvailableMemory()
        {
            long available = 0;
            foreach (var objRam in RamMonitor.Get())
            {
                available = Convert.ToInt64(objRam["FreePhysicalMemory"]) * 1024;
            }
#if !x64
            long maxX86Memory = 1024L * 1024 * 1024 * 3;
            if (available > maxX86Memory)
                available = maxX86Memory;
#endif
            return (long)(available * 0.8);
        }

        public static int ReadInt(this FileStream fileStream)
        {
            byte[] data = new byte[4];

            fileStream.Read(data, 0, 4);

            return BitConverter.ToInt32(data, 0);
        }

        public static long ReadLong(this FileStream fileStream)
        {
            byte[] data = new byte[8];

            fileStream.Read(data, 0, 8);

            return BitConverter.ToInt64(data, 0);
        }

        public static void WriteInt(this FileStream fileStream, int value)
        {
            fileStream.Write(BitConverter.GetBytes(value), 0, 4);
        }

        public static void WriteLong(this FileStream fileStream, long value)
        {
            fileStream.Write(BitConverter.GetBytes(value), 0, 8);
        }
    }
}
