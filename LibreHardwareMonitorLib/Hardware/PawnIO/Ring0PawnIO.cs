using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace LibreHardwareMonitor.Hardware.PawnIO
{
    public static class Ring0PawnIO
    {
        //static IntPtr _lib, _h; // PawnIOLib.dll handle, PawnIO session
        //static PawnioOpen _open; static PawnioClose _close;
        //static PawnioLoad _load; static PawnioExecute _exec;

        //// 1) 找安裝位置（先讀 Uninstall\PawnIO\InstallLocation，找不到再 %ProgramFiles%\PawnIO）
        //static string FindInstallLocation()
        //{
        //    using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
        //        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
        //    var p = k?.GetValue("InstallLocation") as string;
        //    if (!string.IsNullOrEmpty(p)) return p;
        //    return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PawnIO");
        //}

        //public static bool Open()
        //{
        //    var dir = FindInstallLocation();
        //    var dll = Path.Combine(dir, "PawnIOLib.dll");
        //    if (!File.Exists(dll)) return false;

        //    _lib = NativeLibrary.Load(dll);
        //    _open = Marshal.GetDelegateForFunctionPointer<PawnioOpen>(NativeLibrary.GetExport(_lib, "pawnio_open"));
        //    _close = Marshal.GetDelegateForFunctionPointer<PawnioClose>(NativeLibrary.GetExport(_lib, "pawnio_close"));
        //    _load = Marshal.GetDelegateForFunctionPointer<PawnioLoad>(NativeLibrary.GetExport(_lib, "pawnio_load"));
        //    _exec = Marshal.GetDelegateForFunctionPointer<PawnioExecute>(NativeLibrary.GetExport(_lib, "pawnio_execute"));

        //    if (_open(out _h) != 0) return false;

        //    // 載你需要的模組 blob（例：LpcIO / LpcACPIEC / SmbusI801 / IntelMSR）
        //    foreach (var mod in new[] { "LpcIO.amx", "LpcACPIEC.amx", "SmbusI801.amx", "IntelMSR.amx" })
        //    {
        //        var blob = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "modules", mod));
        //        if (_load(_h, blob, (UIntPtr)blob.Length) != 0) return false;
        //    }
        //    return true;
        //}

        //public static void Close()
        //{
        //    if (_h != IntPtr.Zero) { _close(_h); _h = IntPtr.Zero; }
        //    if (_lib != IntPtr.Zero) { NativeLibrary.Free(_lib); _lib = IntPtr.Zero; }
        //}

        //// 依模組 README/DEFINE_IOCTL_SIZED 呼叫：
        //public static byte SioRead(byte reg)
        //{
        //    byte val = 0; UIntPtr got;
        //    // in/out 打包略；核心是 _exec(_h, "ioctl_read", inPtr, inSize, outPtr, outSize, out got)
        //    return val;
        //}

        //// …其餘封裝略…
        //// delegate 宣告略（以 PawnIOLib.h 為準）
    }

}
