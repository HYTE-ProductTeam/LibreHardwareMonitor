using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using LibreHardwareMonitor.Hardware.PawnIO;
using Microsoft.Win32;

namespace PawnIOTest
{
    public class VerifyPawnIO
    {
        public string modulesDir = @"D:\Git\HYTE_Nexus\LibreHardwareMonitor\LibreHardwareMonitorLib\modules";

        // 使用正確的 ulong[] 格式測試 MSR
        public void Step8_TestCorrectFormat()
        {
            Console.WriteLine("=== Step 8: Testing Correct ulong[] Format ===");

            IntPtr handle;
            int openResult = PawnIOLib.pawnio_open(out handle);
            if (openResult != 0) return;

            try
            {
                // 載入模組
                string modPath = Path.Combine(modulesDir, "AMDFamily17.bin");
                byte[] moduleBlob = File.ReadAllBytes(modPath);
                int loadResult = PawnIOLib.pawnio_load(handle, moduleBlob, (UIntPtr)moduleBlob.Length);
                if (loadResult != 0) return;

                var testMsrs = new uint[] { 0x1B, 0xC0000080, 0xCE, 0x10 };
                var ioctlNames = new[] { "ioctl_read_msr", "msr_read", "read_msr" };

                foreach (var ioctl in ioctlNames)
                {
                    Console.WriteLine($"\n--- Testing {ioctl} with ulong[] format ---");

                    foreach (var msr in testMsrs)
                    {
                        Console.WriteLine($"  MSR 0x{msr:X}:");

                        // 使用正確的 ulong[] 格式
                        ulong[] input = { msr };       // 1個 ulong 元素
                        ulong[] output = new ulong[1]; // 1個 ulong 元素用於輸出

                        int rc = PawnIOLib.pawnio_execute(
                            handle, ioctl,
                            input, (UIntPtr)1,         // 1個輸入元素
                            output, (UIntPtr)1,        // 1個輸出元素
                            out UIntPtr returnCount);

                        Console.WriteLine($"    RC: 0x{rc:X8}, Returned: {returnCount} elements");

                        if (rc == 0)
                        {
                            Console.WriteLine($"    ✓ SUCCESS! MSR 0x{msr:X} = 0x{output[0]:X}");
                            Console.WriteLine($"    ** FOUND WORKING FORMAT: {ioctl} with ulong[] **");
                        }
                        else if (rc == unchecked((int)0x80070002))
                        {
                            Console.WriteLine($"    - {ioctl}: Does not exist");
                            break; // 不需要測試其他 MSR
                        }
                        else if (rc == unchecked((int)0x80070057))
                        {
                            Console.WriteLine($"    ? {ioctl}: Still invalid parameter");
                        }
                        else
                        {
                            Console.WriteLine($"    ? {ioctl}: Error 0x{rc:X8}");
                        }
                    }
                }

            }
            finally
            {
                PawnIOLib.pawnio_close(handle);
            }
        }

        // 測試不同的輸入元素數量
        public void Step9_TestElementCounts()
        {
            Console.WriteLine("=== Step 9: Testing Different Element Counts ===");

            bool isElevated = new System.Security.Principal.WindowsPrincipal(
            System.Security.Principal.WindowsIdentity.GetCurrent())
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);

            Console.WriteLine($"Running as Administrator: {isElevated}");

            if (!isElevated)
            {
                Console.WriteLine("❌ Please run as Administrator!");
                return;
            }

            IntPtr handle;
            int openResult = PawnIOLib.pawnio_open(out handle);
            Console.WriteLine($"pawnio_open result: 0x{openResult:X8}");

            if (openResult != 0)
            {
                Console.WriteLine("❌ Failed to open PawnIO");
                return;
            }

            try
            {
                string modPath = Path.Combine(modulesDir, "IntelMSR.bin");
                byte[] moduleBlob = File.ReadAllBytes(modPath);
                PawnIOLib.pawnio_load(handle, moduleBlob, (UIntPtr)moduleBlob.Length);

                uint testMsr = 0x1B;

                // 測試不同的輸入元素數量
                for (int elementCount = 1; elementCount <= 4; elementCount++)
                {
                    Console.WriteLine($"\nTesting {elementCount} input element(s):");

                    ulong[] input = new ulong[elementCount];
                    input[0] = testMsr;
                    // 其他元素保持為 0

                    ulong[] output = new ulong[1];

                    int rc = PawnIOLib.pawnio_execute(
                        handle, "ioctl_read_msr",
                        input, (UIntPtr)elementCount,
                        output, (UIntPtr)1,
                        out UIntPtr returnCount);

                    Console.WriteLine($"  RC: 0x{rc:X8}, Returned: {returnCount}");

                    if (rc == 0)
                    {
                        Console.WriteLine($"  ✓ SUCCESS with {elementCount} elements! Value: 0x{output[0]:X}");
                        break;
                    }
                }

            }
            finally
            {
                PawnIOLib.pawnio_close(handle);
            }
        }

        // 測試替代模組
        public void Step10_TestAlternativeModulesCorrectFormat()
        {
            Console.WriteLine("=== Step 10: Testing Alternative Modules with Correct Format ===");

            var amdModules = new[] {
                "AMDFamily17.bin",
                "AMDReset.bin",
                "RyzenSMU.bin"
            };

            foreach (var moduleName in amdModules)
            {
                Console.WriteLine($"\n--- Testing {moduleName} with ulong[] format ---");
                TestModuleWithCorrectFormat(moduleName);
            }
        }

        private void TestModuleWithCorrectFormat(string moduleName)
        {
            IntPtr handle;
            int openResult = PawnIOLib.pawnio_open(out handle);
            if (openResult != 0) return;

            try
            {
                string modPath = Path.Combine(modulesDir, moduleName);
                if (!File.Exists(modPath))
                {
                    Console.WriteLine($"  Module not found: {modPath}");
                    return;
                }

                byte[] moduleBlob = File.ReadAllBytes(modPath);
                int loadResult = PawnIOLib.pawnio_load(handle, moduleBlob, (UIntPtr)moduleBlob.Length);

                if (loadResult != 0)
                {
                    Console.WriteLine($"  Load failed: 0x{loadResult:X8}");
                    return;
                }

                Console.WriteLine($"  Module loaded successfully ({moduleBlob.Length} bytes)");

                // 使用正確格式測試 MSR 讀取
                var ioctlNames = new[] { "ioctl_read_msr", "msr_read", "read_msr", "smu_read" };
                uint testMsr = 0x1B;

                foreach (var ioctl in ioctlNames)
                {
                    ulong[] input = { testMsr };
                    ulong[] output = new ulong[1];

                    int rc = PawnIOLib.pawnio_execute(
                        handle, ioctl,
                        input, (UIntPtr)1,
                        output, (UIntPtr)1,
                        out UIntPtr returnCount);

                    if (rc == 0)
                    {
                        Console.WriteLine($"  ✓ {ioctl}: SUCCESS! MSR 0x{testMsr:X} = 0x{output[0]:X}");
                    }
                    else if (rc == unchecked((int)0x80070002))
                    {
                        Console.WriteLine($"  - {ioctl}: Does not exist");
                    }
                    else if (rc == unchecked((int)0x80070057))
                    {
                        Console.WriteLine($"  ? {ioctl}: Still invalid parameter (even with correct format)");
                    }
                    else
                    {
                        Console.WriteLine($"  ? {ioctl}: Error 0x{rc:X8}");
                    }
                }

            }
            finally
            {
                PawnIOLib.pawnio_close(handle);
            }
        }

        // 原有方法保持不變...
        public void Step1_VerifyPawnIOBasics()
        {
            Console.WriteLine("=== Step 1: PawnIO Basic Verification ===");

            string modPath = Path.Combine(modulesDir, "AMDFamily17.bin");
            Console.WriteLine($"Module exists: {File.Exists(modPath)}");
            if (File.Exists(modPath))
            {
                Console.WriteLine($"Module size: {new FileInfo(modPath).Length} bytes");
            }

            IntPtr testHandle;
            int rc = PawnIOLib.pawnio_open(out testHandle);
            Console.WriteLine($"pawnio_open result: 0x{rc:X8}");
            Console.WriteLine($"Handle: 0x{testHandle.ToInt64():X}");

            if (rc == 0 && testHandle != IntPtr.Zero)
            {
                Console.WriteLine("✓ PawnIO basic functionality working");
                PawnIOLib.pawnio_close(testHandle);
            }
            else
            {
                Console.WriteLine("✗ PawnIO basic functionality failed");
            }
        }

        // 所有其他現有方法保持不變
        public void CheckActualCpu()
        {
            Console.WriteLine("=== CPU Information ===");

            try
            {
                var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                if (key != null)
                {
                    Console.WriteLine($"CPU Name: {key.GetValue("ProcessorNameString")}");
                    Console.WriteLine($"Vendor: {key.GetValue("VendorIdentifier")}");
                    Console.WriteLine($"Identifier: {key.GetValue("Identifier")}");
                }

                Console.WriteLine($"Machine Name: {Environment.MachineName}");
                Console.WriteLine($"Processor Count: {Environment.ProcessorCount}");

                try
                {
                    var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Processor");
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        Console.WriteLine($"WMI Name: {obj["Name"]}");
                        Console.WriteLine($"WMI Family: {obj["Family"]}");
                        Console.WriteLine($"WMI Model: {obj["Model"]}");
                        Console.WriteLine($"WMI Stepping: {obj["Stepping"]}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"WMI failed: {ex.Message}");
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine($"CPU check failed: {ex.Message}");
            }
        }

        public void FullDiagnostics()
        {
            Console.WriteLine("=== Full PawnIO Diagnostics ===");
            Console.WriteLine($"Running as Administrator: {IsElevated()}");

            IntPtr handle;
            int openResult = PawnIOLib.pawnio_open(out handle);
            Console.WriteLine($"pawnio_open result: 0x{openResult:X8}");

            if (openResult != 0)
            {
                Console.WriteLine("❌ Failed to open PawnIO");
                return;
            }

            try
            {
                // 1. 检查系统信息
                CheckSystemInfo();

                // 2. 测试Intel模块
                TestIntelModule(handle);

                // 3. 测试AMD模块作为对比
                TestAMDModule(handle);

                // 4. 尝试其他模块
                TestOtherModules(handle);
            }
            finally
            {
                PawnIOLib.pawnio_close(handle);
                Console.WriteLine("PawnIO closed");
            }
        }

        private void CheckSystemInfo()
        {
            Console.WriteLine("\n=== System Information ===");
            Console.WriteLine($"Processor: {Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER")}");
            Console.WriteLine($"Architecture: {Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE")}");
            Console.WriteLine($"OS: {Environment.OSVersion}");

            try
            {
                using (var searcher = new System.Management.ManagementObjectSearcher("SELECT * FROM Win32_Processor"))
                {
                    foreach (var item in searcher.Get())
                    {
                        Console.WriteLine($"CPU: {item["Name"]}");
                        Console.WriteLine($"Manufacturer: {item["Manufacturer"]}");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not get CPU info: {ex.Message}");
            }
        }

        private void TestIntelModule(IntPtr handle)
        {
            Console.WriteLine("\n=== Testing Intel Module ===");

            string modPath = Path.Combine(modulesDir, "IntelMSR.bin");

            if (!File.Exists(modPath))
            {
                Console.WriteLine($"❌ Intel module not found: {modPath}");
                return;
            }

            Console.WriteLine($"✓ Intel module found: {new FileInfo(modPath).Length} bytes");

            byte[] moduleBlob = File.ReadAllBytes(modPath);
            int loadResult = PawnIOLib.pawnio_load(handle, moduleBlob, (UIntPtr)moduleBlob.Length);

            Console.WriteLine($"Intel module load result: 0x{loadResult:X8}");

            if (loadResult != 0)
            {
                Console.WriteLine("❌ Failed to load Intel module");
                return;
            }

            Console.WriteLine("✓ Intel module loaded successfully");

            // 测试不同的MSR和函数名
            TestMSRFunctions(handle, "Intel");
        }

        private void TestAMDModule(IntPtr handle)
        {
            Console.WriteLine("\n=== Testing AMD Module for Comparison ===");

            string modPath = Path.Combine(modulesDir, "AMDFamily17.bin");

            if (!File.Exists(modPath))
            {
                Console.WriteLine($"❌ AMD module not found: {modPath}");
                return;
            }

            byte[] moduleBlob = File.ReadAllBytes(modPath);
            int loadResult = PawnIOLib.pawnio_load(handle, moduleBlob, (UIntPtr)moduleBlob.Length);

            Console.WriteLine($"AMD module load result: 0x{loadResult:X8}");

            if (loadResult != 0)
            {
                Console.WriteLine("❌ Failed to load AMD module");
                return;
            }

            Console.WriteLine("✓ AMD module loaded successfully");
            TestMSRFunctions(handle, "AMD");
        }

        private void TestOtherModules(IntPtr handle)
        {
            Console.WriteLine("\n=== Testing Other Available Modules ===");

            string[] otherModules = {
        "Echo.bin",
        "ARMMSR.bin",
        "LpcIO.bin"
    };

            foreach (string moduleName in otherModules)
            {
                string modPath = Path.Combine(modulesDir, moduleName);
                if (File.Exists(modPath))
                {
                    Console.WriteLine($"\nTesting {moduleName}:");
                    byte[] moduleBlob = File.ReadAllBytes(modPath);
                    int loadResult = PawnIOLib.pawnio_load(handle, moduleBlob, (UIntPtr)moduleBlob.Length);
                    Console.WriteLine($"  Load result: 0x{loadResult:X8}");

                    if (loadResult == 0)
                    {
                        // 尝试一个简单的测试
                        TestSimpleFunction(handle, moduleName);
                    }
                }
            }
        }

        private void TestMSRFunctions(IntPtr handle, string moduleType)
        {
            Console.WriteLine($"\n--- Testing {moduleType} MSR Functions ---");

            string[] functionNames = {
                "ioctl_read_msr",
                "read_msr",
                "rdmsr",
                "msr_read",
                "intel_read_msr",
                "amd_read_msr"
            };

            uint[] testMsrs = {
                0x10,   // IA32_TIME_STAMP_COUNTER (should be safe on both)
                0x1B,   // IA32_APIC_BASE
                0x1A0   // IA32_MISC_ENABLE
            };

            foreach (uint msr in testMsrs)
            {
                Console.WriteLine($"\nTesting MSR 0x{msr:X}:");

                foreach (string funcName in functionNames)
                {
                    // 尝试不同的参数组合
                    TestMSRWithDifferentParams(handle, funcName, msr);
                }

                // 如果找到一个工作的，就停止测试这个MSR
                break;
            }
        }

        private void TestMSRWithDifferentParams(IntPtr handle, string funcName, uint msr)
        {
            // 测试1: 只有MSR地址
            {
                ulong[] input = { msr };
                ulong[] output = new ulong[2];

                int rc = PawnIOLib.pawnio_execute(
                    handle, funcName,
                    input, (UIntPtr)1,
                    output, (UIntPtr)2,
                    out UIntPtr returnCount);

                if (rc == 0)
                {
                    Console.WriteLine($"  ✓ SUCCESS: {funcName}(msr=0x{msr:X})");
                    Console.WriteLine($"    Value: 0x{output[0]:X16}");
                    Console.WriteLine($"    Returned elements: {returnCount}");
                    return;
                }
                else if (rc != 0x80070057) // 不是"无效参数"错误才显示
                {
                    Console.WriteLine($"  {funcName}(msr): 0x{rc:X8}");
                }
            }

            // 测试2: MSR地址 + CPU核心ID
            {
                ulong[] input = { msr, 0 }; // CPU core 0
                ulong[] output = new ulong[2];

                int rc = PawnIOLib.pawnio_execute(
                    handle, funcName,
                    input, (UIntPtr)2,
                    output, (UIntPtr)2,
                    out UIntPtr returnCount);

                if (rc == 0)
                {
                    Console.WriteLine($"  ✓ SUCCESS: {funcName}(msr=0x{msr:X}, core=0)");
                    Console.WriteLine($"    Value: 0x{output[0]:X16}");
                    return;
                }
            }
        }

        private void TestSimpleFunction(IntPtr handle, string moduleName)
        {
            // 对于Echo.bin，尝试echo函数
            if (moduleName == "Echo.bin")
            {
                ulong[] input = { 0x12345678 };
                ulong[] output = new ulong[1];

                int rc = PawnIOLib.pawnio_execute(
                    handle, "echo",
                    input, (UIntPtr)1,
                    output, (UIntPtr)1,
                    out UIntPtr returnCount);

                Console.WriteLine($"  Echo test: 0x{rc:X8}, returned: {returnCount}");
                if (rc == 0)
                {
                    Console.WriteLine($"  ✓ Echo returned: 0x{output[0]:X}");
                }
            }
        }

        private bool IsElevated()
        {
            return new System.Security.Principal.WindowsPrincipal(
                System.Security.Principal.WindowsIdentity.GetCurrent())
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }

        public void TestCorrectMSRFormat()
        {
            Console.WriteLine("=== Testing Correct MSR Format Based on Error Pattern ===");

            IntPtr handle;
            PawnIOLib.pawnio_open(out handle);

            try
            {
                string modPath = Path.Combine(modulesDir, "IntelMSR.bin");
                byte[] moduleBlob = File.ReadAllBytes(modPath);
                PawnIOLib.pawnio_load(handle, moduleBlob, (UIntPtr)moduleBlob.Length);

                Console.WriteLine("✅ Module loaded successfully");

                // 基于错误模式，我们知道：
                // - ioctl_read_msr 是正确的函数名
                // - 单个输入参数是正确的格式
                // - 输出缓冲区大小1会触发 ACCESS_DENIED
                // - 更大的输出缓冲区会触发 INVALID_PARAMETER

                // 这可能意味着输出缓冲区需要特定大小，让我们尝试所有可能的组合
                TestSpecificMSRCombinations(handle);

            }
            finally
            {
                PawnIOLib.pawnio_close(handle);
            }
        }

        private void TestSpecificMSRCombinations(IntPtr handle)
        {
            Console.WriteLine("\n=== Testing Specific MSR Input/Output Combinations ===");

            // 测试不同的输入参数数量和输出缓冲区大小组合
            uint[] testMSRs = { 0x00, 0x01, 0x10, 0x1B };

            foreach (uint msr in testMSRs)
            {
                Console.WriteLine($"\nTesting MSR 0x{msr:X}:");

                // 测试不同的输入格式
                ulong[][] inputFormats = {
            new ulong[] { msr },                    // 单个MSR
            new ulong[] { msr, 0 },                // MSR + 0
            new ulong[] { 0, msr },                // 0 + MSR
            new ulong[] { msr, 0, 0 },             // MSR + 两个0
            new ulong[] { 0, msr, 0 },             // 0 + MSR + 0
            new ulong[] { 0, 0, msr },             // 两个0 + MSR
            new ulong[] { 1, msr },                // 1 + MSR (可能的操作码)
            new ulong[] { msr, 1 },                // MSR + 1
        };

                foreach (var input in inputFormats)
                {
                    string inputDesc = $"[{string.Join(",", input.Select(x => $"0x{x:X}"))}]";

                    // 对每种输入格式，测试不同的输出缓冲区大小
                    for (int outputSize = 1; outputSize <= 16; outputSize++)
                    {
                        TestSingleMSRCombination(handle, "ioctl_read_msr", input, outputSize, inputDesc);
                    }
                }
            }
        }

        private void TestSingleMSRCombination(IntPtr handle, string funcName, ulong[] input, int outputSize, string inputDesc)
        {
            ulong[] output = new ulong[outputSize];

            try
            {
                int rc = PawnIOLib.pawnio_execute(
                    handle, funcName,
                    input, (UIntPtr)input.Length,
                    output, (UIntPtr)outputSize,
                    out UIntPtr returnCount);

                // 特别关注不是常见错误的结果
                if (rc == 0)
                {
                    Console.WriteLine($"  ✅ SUCCESS! Input: {inputDesc}, Output size: {outputSize}");
                    Console.WriteLine($"     Returned {returnCount} values:");
                    for (int i = 0; i < (int)returnCount && i < output.Length; i++)
                    {
                        Console.WriteLine($"     output[{i}]: 0x{output[i]:X16}");
                    }

                    // 如果成功了，测试更多MSR
                    TestAdditionalMSRsWithWorkingFormat(handle, funcName, input, outputSize);
                    return; // 找到工作格式就返回
                }
                else if (rc == 0x80070005) // ACCESS_DENIED - 这意味着我们很接近了
                {
                    Console.WriteLine($"  🔄 ACCESS_DENIED: {inputDesc}, out{outputSize} - Function recognized but access denied");
                }
                else if (rc != 0x80070057) // 不是 INVALID_PARAMETER 的其他错误
                {
                    Console.WriteLine($"  ❓ Other error: {inputDesc}, out{outputSize}: 0x{rc:X8}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ❌ Exception: {inputDesc}, out{outputSize}: {ex.Message}");
            }
        }

        private void TestAdditionalMSRsWithWorkingFormat(IntPtr handle, string funcName, ulong[] inputTemplate, int outputSize)
        {
            Console.WriteLine($"\n  🎉 Found working format! Testing additional MSRs...");

            uint[] additionalMSRs = {
        0x10,   // IA32_TIME_STAMP_COUNTER
        0x1B,   // IA32_APIC_BASE
        0x1A0,  // IA32_MISC_ENABLE
        0x198,  // IA32_PERF_STATUS
        0x199,  // IA32_PERF_CTL
        0x19C,  // IA32_THERM_STATUS
        0x8B,   // IA32_BIOS_SIGN_ID
        0x10A   // IA32_ARCH_CAPABILITIES
    };

            foreach (uint msr in additionalMSRs)
            {
                ulong[] input = new ulong[inputTemplate.Length];
                Array.Copy(inputTemplate, input, inputTemplate.Length);

                // 替换模板中的MSR值（假设MSR在特定位置）
                for (int i = 0; i < input.Length; i++)
                {
                    if (input[i] == inputTemplate[0] || (inputTemplate[0] == 0 && i == 1))
                    {
                        input[i] = msr;
                        break;
                    }
                }

                ulong[] output = new ulong[outputSize];

                int rc = PawnIOLib.pawnio_execute(
                    handle, funcName,
                    input, (UIntPtr)input.Length,
                    output, (UIntPtr)outputSize,
                    out UIntPtr returnCount);

                if (rc == 0 && returnCount > 0)
                {
                    Console.WriteLine($"    MSR 0x{msr:X3}: 0x{output[0]:X16}");
                    if (returnCount > 1)
                    {
                        for (int i = 1; i < (int)returnCount && i < output.Length; i++)
                        {
                            Console.WriteLine($"              [+{i}]: 0x{output[i]:X16}");
                        }
                    }
                }
                else if (rc == 0x80070005)
                {
                    Console.WriteLine($"    MSR 0x{msr:X3}: Access denied (may require different privileges)");
                }
                else
                {
                    Console.WriteLine($"    MSR 0x{msr:X3}: Failed (0x{rc:X8})");
                }
            }
        }
        public void TestMoreIntelMSRs()
        {
            Console.WriteLine("=== 測試更多Intel MSR寄存器 ===");

            IntPtr handle;
            PawnIOLib.pawnio_open(out handle);

            try
            {
                string modPath = Path.Combine(modulesDir, "IntelMSR.bin");
                byte[] moduleBlob = File.ReadAllBytes(modPath);
                PawnIOLib.pawnio_load(handle, moduleBlob, (UIntPtr)moduleBlob.Length);

                // 擴展的MSR列表 - 包含更多可能可讀的寄存器
                uint[] msrList = {
            // 基本MSR
            0x000, 0x001, 0x002, 0x003, 0x004, 0x005,
            0x010, 0x01B, 0x02A, 0x02C,
            
            // 性能相關
            0x198, 0x199, 0x19A, 0x19B, 0x19C, 0x19D,
            
            // 溫度和電源
            0x1A0, 0x1A2, 0x1A4, 0x1A6, 0x1A8, 0x1AA,
            0x19C, 0x1A1, 0x606, 0x611, 0x639,
            
            // 架構MSR
            0x17, 0x8B, 0x10A, 0x140, 0x17A, 0x179,
            
            // 平台相關
            0xCE, 0x17D, 0x186, 0x187, 0x188, 0x189,
            
            // 時間和頻率
            0x15, 0x2A, 0x2B, 0x2C, 0xE7, 0xE8,
            
            // 緩存和內存
            0x1A4, 0x1A6, 0x1A8, 0x400, 0x401, 0x402
        };

                Console.WriteLine("\n掃描MSR寄存器...");
                Console.WriteLine("MSR地址    結果                     值");
                Console.WriteLine("-------    ------                   ----------------");

                int successCount = 0;
                int accessDeniedCount = 0;

                foreach (uint msr in msrList)
                {
                    ulong[] input = { msr };
                    ulong[] output = new ulong[1];

                    int rc = PawnIOLib.pawnio_execute(
                        handle, "ioctl_read_msr",
                        input, (UIntPtr)1,
                        output, (UIntPtr)1,
                        out UIntPtr returnCount);

                    if (rc == 0 && returnCount > 0)
                    {
                        Console.WriteLine($"0x{msr:X3}      ✅ 成功                 0x{output[0]:X16}");
                        successCount++;

                        // 解釋一些已知的MSR
                        InterpretMSRValue(msr, output[0]);
                    }
                    else if (rc == 0x80070005)
                    {
                        Console.WriteLine($"0x{msr:X3}      🔒 拒絕訪問");
                        accessDeniedCount++;
                    }
                    else if (rc != 0x80070057) // 不記錄"無效參數"錯誤
                    {
                        Console.WriteLine($"0x{msr:X3}      ❌ 錯誤 0x{rc:X8}");
                    }
                }

                Console.WriteLine($"\n總結:");
                Console.WriteLine($"  成功讀取: {successCount} 個MSR");
                Console.WriteLine($"  拒絕訪問: {accessDeniedCount} 個MSR");

            }
            finally
            {
                PawnIOLib.pawnio_close(handle);
            }
        }

        private void InterpretMSRValue(uint msr, ulong value)
        {
            switch (msr)
            {
                case 0x198: // IA32_PERF_STATUS
                    uint currentRatio = (uint)(value & 0xFFFF);
                    Console.WriteLine($"           → 當前性能狀態: 比率 {currentRatio}");
                    break;

                case 0x19C: // IA32_THERM_STATUS
                    if ((value & 0x80000000) != 0)
                        Console.WriteLine($"           → 溫度狀態: 溫度警報激活");
                    else
                        Console.WriteLine($"           → 溫度狀態: 正常");
                    break;

                case 0x1A0: // IA32_MISC_ENABLE
                    if ((value & 0x1) != 0)
                        Console.WriteLine($"           → 快速字符串操作: 啟用");
                    break;

                default:
                    // 其他MSR不解釋，只顯示值
                    break;
            }
        }
    }
}
