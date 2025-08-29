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

            IntPtr handle;
            PawnIOLib.pawnio_open(out handle);

            try
            {
                string modPath = Path.Combine(modulesDir, "AMDFamily17.bin");
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
    }
}
