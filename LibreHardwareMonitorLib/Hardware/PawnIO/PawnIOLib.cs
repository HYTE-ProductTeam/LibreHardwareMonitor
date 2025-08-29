using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace LibreHardwareMonitor.Hardware.PawnIO
{
    public static class PawnIOLib
    {
        private const string Dll = @"C:\Program Files\PawnIO\PawnIOLib.dll"; // 路徑請視安裝/部署調整
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int pawnio_open(out IntPtr h);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int pawnio_close(IntPtr h);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int pawnio_load(IntPtr h, byte[] blob, UIntPtr blobSize);

        [DllImport("PawnIOLib.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int pawnio_execute(
            IntPtr handle,
            string name,
            ulong[] input,        // 改為 ulong[]
            UIntPtr inputCount,   // 這是元素個數，不是位元組數
            ulong[] output,       // 改為 ulong[]  
            UIntPtr outputCount,  // 這是元素個數，不是位元組數
            out UIntPtr returnCount);

        static readonly int E_ACCESSDENIED = unchecked((int)0x80070005);
        static readonly int E_NOTSUPPORTED = unchecked((int)0x80070032); // 你現在看到的
        static readonly int STATUS_NOT_SUPPORTED = unchecked((int)0xC00000BB); // 某些模組會回 NTSTATUS

        static bool TryLoadModule(IntPtr h, string path, out int rc)
        {
            var blob = File.ReadAllBytes(path);
            rc = pawnio_load(h, blob, (UIntPtr)blob.Length);
            if (rc == 0) return true;

            // 對「不支援」的 rc 直接忽略；其餘才當錯誤
            if (rc == E_NOTSUPPORTED || rc == STATUS_NOT_SUPPORTED)
                return false;

            throw new Exception($"pawnio_load failed: {Path.GetFileName(path)} rc=0x{rc:X8}");
        }

        public static void LoadModule(IntPtr h, string pathToBin)
        {
            try
            {
                var blob = File.ReadAllBytes(pathToBin);      // .bin
                int rc = pawnio_load(h, blob, (UIntPtr)blob.Length);
                if (rc != 0) throw new Exception($"pawnio_load failed: {Path.GetFileName(pathToBin)} rc=0x{rc:X}");
            }
            catch (Exception e)
            { }
        }

        // 一次把資料夾裡的 .bin 都載入
        public static void LoadAllModulesIn(string dir, IntPtr h)
        {
            if (!Directory.Exists(dir))
                throw new DirectoryNotFoundException($"Modules directory not found: {dir}");

            foreach (var bin in Directory.EnumerateFiles(dir, "*.bin"))
            {
                try { TryLoadModule(h, bin, out var rc); }
                catch (Exception ex)
                {
                    // 建議至少記錄：哪些模組載入失敗
                    Debug.WriteLine($"[PawnIO] load {Path.GetFileName(bin)} failed: {ex.Message}");
                }
            }
        }

        static string GetPawnIoInstallPath()
        {
            const string key = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO";
            var val = Microsoft.Win32.Registry.GetValue(key, "InstallLocation", null) as string;
            return string.IsNullOrEmpty(val) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PawnIO") : val;
        }
    }
}
