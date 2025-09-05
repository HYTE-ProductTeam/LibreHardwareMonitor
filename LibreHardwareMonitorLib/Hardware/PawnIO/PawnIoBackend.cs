using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace LibreHardwareMonitor.Hardware.PawnIO
{
    public sealed class PawnIoBackend : IHardwareAccessBackend, IDisposable
    {
        private readonly PawnIoModule _modLpcIo;
        private readonly PawnIoModule _modEc;        // 可選：筆電/特定主板才需要
        private readonly PawnIoModule _modSmbus;     // AMD 晶片組可選
        //private readonly PawnIoModule _modRyzenSmu;  // 需要 CPU 功耗才載
        private readonly PawnIoModule _modMsr;
        private U32 _lpcDetectedType;
        public PawnIoBackend(string modulesDir)
        {
            try
            {
                var result = PawnIoBootstrap.InitializeAll(modulesDir);

                // 取得已載入的模組
                var msr = result.Msr;     // 可能是 IntelMSR / AMDFamilyXX / null
                var lpcIo = result.LpcIo;   // LpcIO（桌機風扇/電壓）
                var ec = result.Ec;      // LpcACPIEC（筆電 EC）
                var smbus = result.Smbus;   // SmbusI801 或 SmbusPIIX4
                                            // （可選）AMD 更完整監控/控制
                var ryzen = result.RyzenSmu;

                // 看看偵測輸出
                foreach (var line in result.Log) Console.WriteLine(line);

                //_modMsr = new PawnIoModule(Path.Combine(modulesDir, "IntelMSR.bin"), "IntelMSR.bin");
                //_modLpcIo = new PawnIoModule(Path.Combine(modulesDir, "LpcIO.bin"), "LpcIO.bin");
                //_modEc = new PawnIoModule(Path.Combine(modulesDir, "LpcACPIEC.bin"), "LpcACPIEC.bin");
                //_modSmbus = new PawnIoModule(Path.Combine(modulesDir, "SmbusI801.bin"), "SmbusPIIX4.bin");

                _modMsr = result.Msr;
                _modLpcIo = result.LpcIo;
                _modEc = result.Ec;
                _modSmbus = result.Smbus;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading PawnIO modules: {ex.Message}");
                Dispose();
                throw;
            }

            _lpcDetectedType = DetectLpcIo(_modLpcIo.Handle, 0);
            if (_lpcDetectedType.Val == 0)
                _lpcDetectedType = DetectLpcIo(_modLpcIo.Handle, 1);

            Debug.WriteLine($"LpcIO detected type=0x{_lpcDetectedType.Val:X}");

            Probe(_modLpcIo);
            //Probe(_modEc);
            Probe(_modSmbus);
            Probe(_modMsr);
        }

        [StructLayout(LayoutKind.Sequential)] struct U32 { public uint Val; }
        [StructLayout(LayoutKind.Sequential)] struct U64 { public ulong Val; }
        [StructLayout(LayoutKind.Sequential)] struct U8 { public byte Val; }

        public void Dispose()
        {
            //_modRyzenSmu?.Dispose();
            _modSmbus?.Dispose();
            _modEc?.Dispose();
            _modLpcIo?.Dispose();
            _modMsr?.Dispose();
        }

        void Probe(PawnIoModule mod)
        {
            try
            {
                switch (mod.Name.ToLowerInvariant())
                {
                    case "lpcio.bin":
                        Debug.WriteLine("Probing LpcIO...");
                        try
                        {
                            var t0 = DetectLpcIo(mod.Handle, 0);
                            Debug.WriteLine($"LpcIO g_type(slot0)=0x{t0.Val:X}");

                            if (t0.Val == 0) // 如果 slot0 沒有檢測到，試試 slot1
                            {
                                var t1 = DetectLpcIo(mod.Handle, 1);
                                Debug.WriteLine($"LpcIO g_type(slot1)=0x{t1.Val:X}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"LpcIO detection failed: {ex.Message}");
                        }
                        break;

                    case "amdfamily17.bin":
                        Debug.WriteLine("Probing AMD MSR...");
                        try
                        {
                            // 使用新的 ulong[] 格式讀取穩定 MSR（例如 0xCE）
                            ulong msrValue = IoctlHelper.ReadMsr(mod.Handle, "ioctl_read_msr", 0xCE);
                            Debug.WriteLine($"{mod.Name} MSR 0xCE=0x{msrValue:X} (OK)");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"MSR probe failed: {ex.Message}");
                        }
                        break;

                    case "lpcacpiec.bin":
                        Debug.WriteLine("Probing EC...");
                        try
                        {
                            var input = new U32 { Val = 0 };
                            var v = IoctlHelper.ExecStructToStruct<U32, U32>(mod.Handle, "ioctl_ec_read", input);
                            Debug.WriteLine($"EC read test ok (val=0x{v.Val:X})");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"EC not supported or blocked: {ex.Message}");
                        }
                        break;

                    case "smbuspiix4.bin":
                        Debug.WriteLine("Probing SMBus...");
                        try
                        {
                            // 範例：addr=0x2D, reg=0x00
                            ulong[] input = { 0x2D, 0x00 };
                            var rv = IoctlHelper.ExecOut<U32>(mod.Handle, "ioctl_smbus_read_byte", input);
                            Debug.WriteLine($"SMBus probe ok (0x2D:0)=0x{rv.Val:X2}");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"SMBus not supported on this platform: {ex.Message}");
                        }
                        break;

                    default:
                        Debug.WriteLine($"{mod.Name}: no probe defined, skip");
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Probe failed for {mod.Name}: {ex.Message}");
            }
        }

        // 使用 IoctlHelper 重寫的檢測方法
        static U32 DetectLpcIo(IntPtr h, uint slot)
        {
            try
            {
                var input = new U32 { Val = slot };
                var result = IoctlHelper.ExecStructToStruct<U32, U32>(h, "ioctl_detect", input);

                Debug.WriteLine($"ioctl_detect(slot={slot}) successful, result=0x{result.Val:X8}");
                return result;
            }
            catch (Win32Exception ex)
            {
                Debug.WriteLine($"ioctl_detect(slot={slot}) failed with rc=0x{ex.NativeErrorCode:X8}");

                // 常見的錯誤碼處理
                switch (ex.NativeErrorCode)
                {
                    case unchecked((int)0x80070005): // E_ACCESSDENIED
                        Debug.WriteLine("Access denied - try running as administrator");
                        break;
                    case unchecked((int)0x80070032): // ERROR_NOT_SUPPORTED
                        Debug.WriteLine("Operation not supported on this hardware");
                        return new U32 { Val = 0 }; // 返回 0 表示未檢測到
                    case unchecked((int)0xC00000BB): // STATUS_NOT_SUPPORTED
                        Debug.WriteLine("NTSTATUS: Not supported");
                        return new U32 { Val = 0 }; // 返回 0 表示未檢測到
                    default:
                        Debug.WriteLine($"Unknown error code: 0x{ex.NativeErrorCode:X8}");
                        break;
                }

                throw; // 對於其他錯誤，重新拋出異常
            }
        }

        public void SioDetect(byte slot) // 0->0x2E, 1->0x4E
        {
            var input = new U32 { Val = slot };
            IoctlHelper.ExecStructToStruct<U32, U32>(_modLpcIo.Handle, "ioctl_detect", input);
        }

        public void SioEnter() => IoctlHelper.ExecNoOut(_modLpcIo.Handle, "ioctl_enter");
        public void SioExit() => IoctlHelper.ExecNoOut(_modLpcIo.Handle, "ioctl_exit");

        public byte SioRead(byte reg)
        {
            var input = new U32 { Val = reg };
            var result = IoctlHelper.ExecStructToStruct<U32, U8>(_modLpcIo.Handle, "ioctl_read", input);
            return result.Val;
        }

        public void SioWrite(byte reg, byte val)
        {
            // 使用 ulong[] 格式傳遞兩個參數
            ulong[] input = { reg, val };
            IoctlHelper.ExecNoOut(_modLpcIo.Handle, "ioctl_write", input);
        }

        public byte In8(ushort port)
        {
            var input = new U32 { Val = port };
            var result = IoctlHelper.ExecStructToStruct<U32, U8>(_modLpcIo.Handle, "ioctl_pio_read", input);
            return result.Val;
        }

        public void Out8(ushort port, byte value)
        {
            ulong[] input = { port, value };
            IoctlHelper.ExecNoOut(_modLpcIo.Handle, "ioctl_pio_write", input);
        }

        public byte EcRead(ushort offset)
        {
            var input = new U32 { Val = offset };
            var result = IoctlHelper.ExecStructToStruct<U32, U8>(_modEc.Handle, "ioctl_ec_read", input);
            return result.Val;
        }

        public void EcWrite(ushort offset, byte value)
        {
            ulong[] input = { offset, value };
            IoctlHelper.ExecNoOut(_modEc.Handle, "ioctl_ec_write", input);
        }

        public ulong ReadMsr(uint msr)
        {
            try
            {
                Debug.WriteLine($"Reading MSR 0x{msr:X} with correct ulong[] format");

                // 嘗試不同的 ioctl 名稱和格式
                var ioctlNames = new[] { "ioctl_read_msr", "msr_read", "read_msr" };

                foreach (var ioctlName in ioctlNames)
                {
                    try
                    {
                        Debug.WriteLine($"Trying IOCTL: {ioctlName}");
                        ulong result = IoctlHelper.ReadMsr(_modMsr.Handle, ioctlName, msr);
                        Debug.WriteLine($"Success with {ioctlName}: MSR 0x{msr:X} = 0x{result:X}");
                        return result;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed with {ioctlName}: {ex.Message}");
                    }
                }

                Debug.WriteLine("All IOCTL attempts failed");
                return 0;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == unchecked((int)0x80070032))
            {
                // ERROR_NOT_SUPPORTED - 這個 MSR 在此硬件上不支持
                Debug.WriteLine($"MSR 0x{msr:X} not supported on this hardware");
                throw new NotSupportedException($"MSR 0x{msr:X} not supported", ex);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MSR 0x{msr:X} read failed: {ex}");
                throw;
            }
        }

        // 新增一個方法來驗證模組是否正確載入
        public void ValidateMsrModule()
        {
            try
            {
                Debug.WriteLine("=== MSR Module Validation ===");

                // 1. 檢查 handle 是否有效
                if (_modMsr.Handle == IntPtr.Zero)
                {
                    Debug.WriteLine("ERROR: MSR module handle is null!");
                    return;
                }
                Debug.WriteLine($"MSR module handle: 0x{_modMsr.Handle.ToInt64():X}");

                // 2. 嘗試讀取一個通常存在的 MSR (APIC_BASE)
                Debug.WriteLine("Testing with APIC_BASE MSR (0x1B)...");
                try
                {
                    ulong testResult = IoctlHelper.ReadMsr(_modMsr.Handle, "ioctl_read_msr", 0x1B);
                    Debug.WriteLine($"APIC_BASE read successful: 0x{testResult:X}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"APIC_BASE read failed: {ex.Message}");

                    // 如果連基本的 MSR 都讀不了，模組可能有問題
                    if (ex.Message.Contains("parameter is incorrect"))
                    {
                        Debug.WriteLine("CRITICAL: Basic MSR read fails with 'parameter incorrect'");
                        Debug.WriteLine("This suggests the module interface or CPU compatibility issue");
                    }
                }

                // 3. 檢查是否為 AMD 處理器
                Debug.WriteLine("Checking processor vendor...");
                // 你可能需要添加處理器檢測邏輯

            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MSR module validation failed: {ex}");
            }
        }

        private static bool IsKnownProblematicMsr(uint msr)
        {
            // 這些 MSR 在某些 Zen 架構上可能不可用或返回 0
            return msr switch
            {
                0xC0010063 => true, // P-State Control
                0xC0010064 => true, // P-State Status  
                0xC001029A => true, // PWR_REPORTING_POLICY
                0xC001029B => true, // CORE_ENERGY_STAT
                _ => false
            };
        }

        public byte SmbusReadByte(byte addr, byte reg)
        {
            ulong[] input = { addr, reg };
            var result = IoctlHelper.ExecOut<U8>(_modSmbus.Handle, "ioctl_smbus_read_byte", input);
            return result.Val;
        }

        public void SmbusWriteByte(byte addr, byte reg, byte val)
        {
            ulong[] input = { addr, reg, val };
            IoctlHelper.ExecNoOut(_modSmbus.Handle, "ioctl_smbus_write_byte", input);
        }

        public bool WriteMsr(uint msr, uint eax, uint edx)
        {
            if (_modMsr == null || _modMsr.Handle == IntPtr.Zero)
                return false;

            // PawnIO builds can use different IOCTL symbol names; try a few
            var ioctlNames = new[] { "ioctl_write_msr", "msr_write", "write_msr" };

            foreach (var name in ioctlNames)
            {
                try
                {
                    IoctlHelper.WriteMsr(_modMsr.Handle, name, msr, eax, edx);
                    Debug.WriteLine($"MSR write OK via {name}: msr=0x{msr:X}, eax=0x{eax:X8}, edx=0x{edx:X8}");
                    return true;
                }
                catch (Win32Exception ex) when (
                       ex.NativeErrorCode == unchecked((int)0x80070032)   // ERROR_NOT_SUPPORTED
                    || ex.NativeErrorCode == unchecked((int)0xC00000BB))  // STATUS_NOT_SUPPORTED
                {
                    Debug.WriteLine($"MSR 0x{msr:X} write not supported via {name}: 0x{ex.NativeErrorCode:X8}");
                    // Try next ioctlName; if all fail, return false.
                }
                catch (Win32Exception ex) when (ex.NativeErrorCode == unchecked((int)0x80070005)) // E_ACCESSDENIED
                {
                    Debug.WriteLine("Access denied writing MSR. Run elevated or ensure PawnIO service/driver is installed.");
                    return false;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"MSR write failed via {name}: {ex}");
                    // Try next name
                }
            }

            return false;
        }

        public bool PciReadConfigDword(uint pciAddress, uint regAddress, out uint value)
        {
            value = 0;
            if (_modMsr == null || _modMsr.Handle == IntPtr.Zero) // or whichever module exports PCI
                return false;

            // Guard for DWORD alignment (to mirror your old behavior)
            if ((regAddress & 3u) != 0)
                return false;

            // Common IOCTL names & shapes seen in PawnIO builds
            var ioctlNames = new[] { "ioctl_pci_read_config", "pci_read_cfg", "read_pci_cfg" };

            foreach (var name in ioctlNames)
            {
                try
                {
                    // Try (addr, reg, width)
                    // width=4 for DWORD
                    ulong[] args = { pciAddress, regAddress, 4 };
                    var rv = IoctlHelper.ExecOut<U32>(_modMsr.Handle, name, args);
                    value = rv.Val;
                    return true;
                }
                catch { /* try next shape/name */ }

                try
                {
                    // Try (addr, reg) -> returns DWORD
                    ulong[] args = { pciAddress, regAddress };
                    var rv = IoctlHelper.ExecOut<U32>(_modMsr.Handle, name, args);
                    value = rv.Val;
                    return true;
                }
                catch { /* next */ }
            }

            return false;
        }

        public bool PciWriteConfigDword(uint pciAddress, uint regAddress, uint value)
        {
            if (_modMsr == null || _modMsr.Handle == IntPtr.Zero) // or your PCI-capable module
                return false;

            if ((regAddress & 3u) != 0)
                return false;

            var ioctlNames = new[] { "ioctl_pci_write_config", "pci_write_cfg", "write_pci_cfg" };

            foreach (var name in ioctlNames)
            {
                try
                {
                    // Try (addr, reg, width, value)
                    ulong[] args = { pciAddress, regAddress, 4, value };
                    IoctlHelper.ExecNoOut(_modMsr.Handle, name, args);
                    return true;
                }
                catch { /* next */ }

                try
                {
                    // Try (addr, reg, value)
                    ulong[] args = { pciAddress, regAddress, value };
                    IoctlHelper.ExecNoOut(_modMsr.Handle, name, args);
                    return true;
                }
                catch { /* next */ }
            }

            return false;
        }

        public bool ReadPhysicalMemory<T>(ulong address, ref T buffer) where T : struct
        {
            if (_modMsr == null || _modMsr.Handle == IntPtr.Zero)
                return false;

            int size = Marshal.SizeOf<T>();
            var ioctlNames = new[] { "ioctl_mem_read", "mem_read", "read_mem", "phys_read" };

            foreach (var name in ioctlNames)
            {
                try
                {
                    // Shape A: (address, unitSize=1, count=byteCount)
                    var a = new ulong[] { address, 1, (uint)size };
                    var bytes = IoctlHelper.ExecOutRawBytes(_modMsr.Handle, name, a, size);
                    if (bytes.Length == size)
                    {
                        buffer = IoctlHelper.BytesToStruct<T>(bytes);
                        return true;
                    }
                }
                catch { /* try next */ }

                try
                {
                    // Shape B: (address, totalBytes)
                    var b = new ulong[] { address, (uint)size };
                    var bytes = IoctlHelper.ExecOutRawBytes(_modMsr.Handle, name, b, size);
                    if (bytes.Length == size)
                    {
                        buffer = IoctlHelper.BytesToStruct<T>(bytes);
                        return true;
                    }
                }
                catch { /* next */ }
            }

            return false;
        }

        public bool ReadPhysicalMemory<T>(ulong address, ref T[] buffer) where T : struct
        {
            if (_modMsr == null || _modMsr.Handle == IntPtr.Zero)
                return false;

            int elem = Marshal.SizeOf(typeof(T));
            int total = elem * buffer.Length;
            var ioctlNames = new[] { "ioctl_mem_read", "mem_read", "read_mem", "phys_read" };

            foreach (var name in ioctlNames)
            {
                try
                {
                    // Shape A
                    var a = new ulong[] { address, (uint)elem, (uint)buffer.Length };
                    var bytes = IoctlHelper.ExecOutRawBytes(_modMsr.Handle, name, a, total);
                    if (bytes.Length == total) { BytesToArray(bytes, ref buffer, elem); return true; }
                }
                catch { /* next */ }

                try
                {
                    // Shape B
                    var b = new ulong[] { address, (uint)total };
                    var bytes = IoctlHelper.ExecOutRawBytes(_modMsr.Handle, name, b, total);
                    if (bytes.Length == total) { BytesToArray(bytes, ref buffer, elem); return true; }
                }
                catch { /* next */ }
            }

            return false;
        }

        // unchanged helper
        private static void BytesToArray<T>(byte[] data, ref T[] dst, int elemSize) where T : struct
        {
            if (data.Length < elemSize * dst.Length)
                throw new ArgumentException("Insufficient data length");

            var handle = GCHandle.Alloc(dst, GCHandleType.Pinned);
            try { Marshal.Copy(data, 0, handle.AddrOfPinnedObject(), elemSize * dst.Length); }
            finally { handle.Free(); }
        }
    }
}
