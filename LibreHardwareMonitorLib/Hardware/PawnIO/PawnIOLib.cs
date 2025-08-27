using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace LibreHardwareMonitor.Hardware.PawnIO
{
    internal static class PawnIOLib
    {
        private const string Dll = "PawnIOLib.dll"; // 路徑請視安裝/部署調整
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int pawnio_open(out IntPtr h);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int pawnio_close(IntPtr h);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int pawnio_load(IntPtr h, byte[] blob, UIntPtr blobSize);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int pawnio_execute(
            IntPtr h, string ioctlName,
            IntPtr inBuf, UIntPtr inSize,
            IntPtr outBuf, UIntPtr outSize,
            out UIntPtr bytesReturned);


        public static void LoadModule(IntPtr h, string pathToBin)
        {
            var blob = File.ReadAllBytes(pathToBin);      // .bin
            int rc = pawnio_load(h, blob, (UIntPtr)blob.Length);
            if (rc != 0) throw new Exception($"pawnio_load failed: {Path.GetFileName(pathToBin)} rc=0x{rc:X}");
        }

        // 一次把資料夾裡的 .bin 都載入
        public static void LoadAllModulesIn(string dir, IntPtr h)
        {
            foreach (var bin in Directory.EnumerateFiles(dir, "*.bin"))
                LoadModule(h, bin);
        }
    }
}
