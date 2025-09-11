using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

// WMI：所有 TFM 都引用 NuGet 的 System.Management 套件
using System.Management;

// 只有 .NET 6/8（或 NET5_0_OR_GREATER）才引入硬體 Intrinsics
#if NET5_0_OR_GREATER
using System.Runtime.Intrinsics.X86;
#endif

namespace LibreHardwareMonitor.Hardware.PawnIO
{
    public static class PawnIoBootstrap
    {

        public static int Model;
        public static int Family;
        public static List<string> Logs;

        public sealed class InitResult
        {
            public PawnIoModule? Msr { get; private set; }
            public PawnIoModule? RyzenSmu { get; private set; } // AMD 可選
            public PawnIoModule? LpcIo { get; private set; }
            public PawnIoModule? Ec { get; private set; }
            public PawnIoModule? Smbus { get; private set; }
            public List<string> Log { get; } = new();

            internal void SetMsr(PawnIoModule? m) => Msr = m;
            internal void SetRyzen(PawnIoModule? m) => RyzenSmu = m;
            internal void SetLpcIo(PawnIoModule? m) => LpcIo = m;
            internal void SetEc(PawnIoModule? m) => Ec = m;
            internal void SetSmbus(PawnIoModule? m) => Smbus = m;
        }

        public static InitResult InitializeAll(string modulesDir)
        {
            var r = new InitResult();

            // 0) 健診（可選）
            TryLoad("Echo.bin", modulesDir, out _, r.Log);

            // 1) CPU / MSR
            var cpu = DetectCpu();
            r.Log.Add($"CPU Vendor={cpu.Vendor}, Family=0x{cpu.Family:X}, Model=0x{cpu.Model:X}");
            Model = cpu.Model;
            Family = cpu.Family;

            if (cpu.Vendor == CpuVendor.Intel)
            {
                TryLoad("IntelMSR.bin", modulesDir, out var intelMsr, r.Log);
                r.SetMsr(intelMsr);
            }
            else if (cpu.Vendor == CpuVendor.AMD)
            {
                var amdMsrBin = PickAmdMsrModule(cpu.Family);
                TryLoad(amdMsrBin, modulesDir, out var amdMsr, r.Log);
                r.SetMsr(amdMsr);

                // 可選：更完整 Ryzen 監控/控制
                TryLoad("RyzenSMU.bin", modulesDir, out var ryzen, r.Log);
                r.SetRyzen(ryzen);
            }

            // 2) Super I/O（桌機風扇/電壓）
            TryLoad("LpcIO.bin", modulesDir, out var lpcIo, r.Log);
            r.SetLpcIo(lpcIo);

            // 3) EC（筆電優先）
            if (DetectEmbeddedController(out var ecInfo))
            {
                r.Log.Add($"EC detected: {ecInfo}");
                TryLoad("LpcACPIEC.bin", modulesDir, out var ec, r.Log);
                r.SetEc(ec);
            }
            else
            {
                r.Log.Add("EC not detected (likely desktop or EC hidden).");
            }

            // 4) SMBus（SPD/VRM/I²C 感測器）
            var smbusBin = DetectSmbusController() switch
            {
                SmbusKind.IntelI801 => "SmbusI801.bin",
                SmbusKind.AmdPiix4 => "SmbusPIIX4.bin",
                _ => cpu.Vendor == CpuVendor.Intel ? "SmbusI801.bin" : "SmbusPIIX4.bin" // 回退
            };
            TryLoad(smbusBin, modulesDir, out var smbus, r.Log);
            r.SetSmbus(smbus);

            Logs = r.Log;
            return r;
        }

        // ------------------- helpers -------------------

        private static bool TryLoad(string binName, string dir, out PawnIoModule? mod, List<string> log)
        {
            var path = Path.Combine(dir, binName);
            if (File.Exists(path))
            {
                try
                {
                    mod = new PawnIoModule(path, binName);
                    log.Add($"Loaded {binName}");
                    return true;
                }
                catch (Exception ex)
                {
                    log.Add($"Failed to load {binName}: {ex.Message}");
                }
            }
            else
            {
                log.Add($"Missing {binName} at {path}");
            }
            mod = null;
            return false;
        }

        private enum CpuVendor { Unknown, Intel, AMD }

        private readonly struct CpuInfo
        {
            public CpuVendor Vendor { get; }
            public int Family { get; }
            public int Model { get; }
            public CpuInfo(CpuVendor v, int fam, int model) { Vendor = v; Family = fam; Model = model; }
        }

        private static CpuInfo DetectCpu()
        {
            // 先嘗試 CPUID（只有 NET5_0_OR_GREATER 且 x86/x64 Intrinsics 可用時）
            #if NET5_0_OR_GREATER
            try
            {
                // X86Base 是否存在取決於編譯 TF 與執行平台
                if (X86Base.IsSupported)
                {
                    string vendorId = GetCpuidVendor();
                    var v = vendorId switch
                    {
                        "GenuineIntel" => CpuVendor.Intel,
                        "AuthenticAMD" => CpuVendor.AMD,
                        _              => CpuVendor.Unknown
                    };

                    var f1 = X86Base.CpuId(1, 0);
                    int baseFamily = (f1.Eax >> 8) & 0xF;
                    int extFamily  = (f1.Eax >> 20) & 0xFF;
                    int family     = baseFamily == 0xF ? baseFamily + extFamily : baseFamily;

                    int baseModel = (f1.Eax >> 4) & 0xF;
                    int extModel  = (f1.Eax >> 16) & 0xF;
                    int model     = (baseFamily == 6 || baseFamily == 0xF) ? (baseModel | (extModel << 4)) : baseModel;

                    return new CpuInfo(v, family, model);
                }
            }
            catch
            {
                // ignore → fallback to WMI
            }
            #endif
            // Fallback：WMI（所有 TFM 都能編譯；非 Windows 執行期會回 Unknown）
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Family, Stepping, Name FROM Win32_Processor");
                foreach (ManagementObject mo in searcher.Get())
                {
                    string mfg = (mo["Manufacturer"] as string) ?? "";
                    string name = (mo["Name"] as string) ?? "";

                    var v = CpuVendor.Unknown;
                    if (mfg.IndexOf("Intel", StringComparison.OrdinalIgnoreCase) >= 0) v = CpuVendor.Intel;
                    else if (mfg.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) >= 0) v = CpuVendor.AMD;

                    // WMI 沒有直接給 x86 family/model（有的也不可靠），設為 0 作為未知
                    return new CpuInfo(v, 0, 0);
                }
            }
            catch { }

            return new CpuInfo(CpuVendor.Unknown, 0, 0);
        }

        #if NET5_0_OR_GREATER
        private static string GetCpuidVendor()
        {
            var leaf0 = X86Base.CpuId(0, 0);
            return ToAscii(leaf0.Ebx) + ToAscii(leaf0.Edx) + ToAscii(leaf0.Ecx);

            static string ToAscii(int reg)
            {
                Span<byte> bytes = stackalloc byte[4];
                BitConverter.TryWriteBytes(bytes, reg);
                return System.Text.Encoding.ASCII.GetString(bytes);
            }
        }
        #endif

        private static string PickAmdMsrModule(int family)
        {
            // 若能從 CPUID 取得 family（NET6/8 路徑）則精準選；否則回退到 17
            if (family >= 0x17) return "AMDFamily17.bin";
            if (family >= 0x10) return "AMDFamily10.bin";
            if (family > 0) return "AMDFamily0F.bin";

            // 未知（WMI 路徑）→ 先選較新的 17；若載入失敗你可在外層再降級嘗試
            return "AMDFamily17.bin";
        }

        private enum SmbusKind { Unknown, IntelI801, AmdPiix4 }

        private static SmbusKind DetectSmbusController()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name,PNPDeviceID FROM Win32_PnPEntity WHERE PNPDeviceID LIKE 'PCI\\\\VEN_%'");

                foreach (ManagementObject mo in searcher.Get())
                {
                    string name = (mo["Name"] as string) ?? "";
                    string id = (mo["PNPDeviceID"] as string) ?? "";

                    bool isSmb = name.IndexOf("SMBus", StringComparison.OrdinalIgnoreCase) >= 0
                                 || name.IndexOf("SM Bus", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!isSmb) continue;

                    var m = Regex.Match(id, @"VEN_([0-9A-F]{4})", RegexOptions.IgnoreCase);
                    if (m.Success)
                    {
                        var ven = m.Groups[1].Value.ToUpperInvariant();
                        if (ven == "8086") return SmbusKind.IntelI801; // Intel
                        if (ven == "1022") return SmbusKind.AmdPiix4;  // AMD
                    }
                }
            }
            catch
            {
                // 非 Windows 或權限/環境問題 → Unknown
            }
            return SmbusKind.Unknown;
        }

        private static bool DetectEmbeddedController(out string info)
        {
            info = string.Empty;
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name,PNPDeviceID FROM Win32_PnPEntity WHERE PNPDeviceID LIKE 'ACPI\\\\%'");

                foreach (ManagementObject mo in searcher.Get())
                {
                    string name = (mo["Name"] as string) ?? "";
                    string id = (mo["PNPDeviceID"] as string) ?? "";

                    // 常見：ACPI\PNP0C09 = Embedded Controller
                    if (id.Contains("PNP0C09") ||
                        name.IndexOf("Embedded Controller", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        info = $"{name} ({id})";
                        return true;
                    }
                }
            }
            catch { /* 非 Windows / 權限問題 */ }

            return false;
        }
    }
}
