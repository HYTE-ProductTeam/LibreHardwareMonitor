// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Partial Copyright (C) Michael Möller <mmoeller@openhardwaremonitor.org> and Contributors.
// All Rights Reserved.

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using LibreHardwareMonitor.Hardware.PawnIO;

namespace LibreHardwareMonitor.Hardware;

public static class Ring0
{
    private static KernelDriver _driver;
    private static string _filePath;

    private static readonly StringBuilder _report = new();

    public static bool IsOpen => _pawnIO != null;

    private static PawnIoBackend _pawnIO;

    public static void Open()
    {
        var modulesDir = @"D:\Git\Nexus\LibreHardwareMonitor\LibreHardwareMonitorLib\modules";
        _pawnIO = new PawnIoBackend(modulesDir);
        if (Directory.Exists(modulesDir) && _pawnIO != null)
        {
            _report.Length = 0;
            _report.AppendLine("Status: PawnIO backend active");
            return;
        }
        //try
        //{
        //    var modulesDir = @"D:\Git\HYTE_Nexus\LibreHardwareMonitor\LibreHardwareMonitorLib\modules";
        //    _pawnIO = new PawnIoBackend(modulesDir);
        //    if (Directory.Exists(modulesDir) && _pawnIO != null)
        //    {
        //        _report.Length = 0;
        //        _report.AppendLine("Status: PawnIO backend active");
        //        return;
        //    }
        //}
        //catch (Exception ex)
        //{
        //    _report.AppendLine("PawnIO init failed: " + ex.Message);
        //    // 繼續嘗試舊的 WinRing0…（如果你想完全替換，可直接 return）
        //}

        return;
    }

    private static bool Extract(string filePath)
    {
        string resourceName = $"{nameof(LibreHardwareMonitor)}.Resources.{(Software.OperatingSystem.Is64Bit ? "WinRing0x64.gz" : "WinRing0.gz")}";

        Assembly assembly = typeof(Ring0).Assembly;
        long requiredLength = 0;

        try
        {
            using Stream stream = assembly.GetManifestResourceStream(resourceName);

            if (stream != null)
            {
                using FileStream target = new(filePath, FileMode.Create);

                stream.Position = 1; // Skip first byte.

                using var gzipStream = new GZipStream(stream, CompressionMode.Decompress);

                gzipStream.CopyTo(target);

                requiredLength = target.Length;
            }
        }
        catch
        {
            return false;
        }

        if (HasValidFile())
            return true;

        // Ensure the file is actually written to the file system.
        var stopwatch = new Stopwatch();
        stopwatch.Start();

        while (stopwatch.ElapsedMilliseconds < 2000)
        {
            if (HasValidFile())
                return true;

            Thread.Yield();
        }

        return false;

        bool HasValidFile()
        {
            try
            {
                return File.Exists(filePath) && new FileInfo(filePath).Length == requiredLength;
            }
            catch
            {
                return false;
            }
        }
    }

    private static void Delete()
    {
        try
        {
            // Try to delete the driver file
            if (_filePath != null && File.Exists(_filePath))
                File.Delete(_filePath);

            _filePath = null;
        }
        catch
        {
            // Ignored.
        }
    }

    private static string GetServiceName()
    {
        string name;

        try
        {
            ProcessModule processModule = Process.GetCurrentProcess().MainModule;
            if (!string.IsNullOrEmpty(processModule?.FileName))
            {
                name = Path.GetFileNameWithoutExtension(processModule.FileName);
                if (!string.IsNullOrEmpty(name))
                    return GetName(name);
            }
        }
        catch
        {
            // Continue with the other options.
        }

        name = GetNameFromAssembly(Assembly.GetExecutingAssembly());
        if (!string.IsNullOrEmpty(name))
            return GetName(name);

        name = GetNameFromAssembly(typeof(Ring0).Assembly);
        if (!string.IsNullOrEmpty(name))
            return GetName(name);

        name = nameof(LibreHardwareMonitor);
        return GetName(name);

        static string GetNameFromAssembly(Assembly assembly)
        {
            return assembly?.GetName().Name;
        }

        static string GetName(string name)
        {
            return $"R0{name}".Replace(" ", string.Empty).Replace(".", "_");
        }
    }

    private static string GetFilePath()
    {
        string filePath = null;

        try
        {
            ProcessModule processModule = Process.GetCurrentProcess().MainModule;
            if (!string.IsNullOrEmpty(processModule?.FileName))
            {
                filePath = Path.ChangeExtension(processModule.FileName, ".sys");
                if (CanCreate(filePath))
                    return filePath;
            }
        }
        catch
        {
            // Continue with the other options.
        }

        string previousFilePath = filePath;
        filePath = GetPathFromAssembly(Assembly.GetExecutingAssembly());
        if (previousFilePath != filePath && !string.IsNullOrEmpty(filePath) && CanCreate(filePath))
            return filePath;

        previousFilePath = filePath;
        filePath = GetPathFromAssembly(typeof(Ring0).Assembly);
        if (previousFilePath != filePath && !string.IsNullOrEmpty(filePath) && CanCreate(filePath))
            return filePath;

        try
        {
            filePath = Path.GetTempFileName();
            if (!string.IsNullOrEmpty(filePath))
            {
                filePath = Path.ChangeExtension(filePath, ".sys");
                if (CanCreate(filePath))
                    return filePath;
            }
        }
        catch
        {
            return null;
        }

        return null;

        static string GetPathFromAssembly(Assembly assembly)
        {
            try
            {
                string location = assembly?.Location;
                return !string.IsNullOrEmpty(location) ? Path.ChangeExtension(location, ".sys") : null;
            }
            catch
            {
                return null;
            }
        }

        static bool CanCreate(string path)
        {
            try
            {
                using (File.Create(path, 1, FileOptions.DeleteOnClose))
                    return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public static void Close()
    {
        if (_driver != null)
        {
            uint refCount = 0;
            _driver.DeviceIOControl(Interop.Ring0.IOCTL_OLS_GET_REFCOUNT, null, ref refCount);
            _driver.Close();

            if (refCount <= 1)
                _driver.Delete();

            _driver = null;
        }

        // try to delete temporary driver file again if failed during open
        Delete();
    }

    public static string GetReport()
    {
        if (_report.Length > 0)
        {
            StringBuilder r = new();
            r.AppendLine("Ring0");
            r.AppendLine();
            r.Append(_report);
            r.AppendLine();
            return r.ToString();
        }

        return null;
    }

    public static bool ReadMsr(uint index, out uint eax, out uint edx)
    {
        if (_driver == null)
        {
            eax = 0;
            edx = 0;
            return false;
        }

        ulong buffer = 0;
        bool result = _driver.DeviceIOControl(Interop.Ring0.IOCTL_OLS_READ_MSR, index, ref buffer);
        edx = (uint)((buffer >> 32) & 0xFFFFFFFF);
        eax = (uint)(buffer & 0xFFFFFFFF);
        return result;
    }

    public static bool ReadMsr(uint index, out ulong edxeax)
    {
        edxeax = 0; // 預設值

        if (IsOpen && _pawnIO != null)
        {
            try
            {
                edxeax = _pawnIO.ReadMsr(index);
                Debug.WriteLine($"MSR 0x{index:X} = 0x{edxeax:X16}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MSR 0x{index:X} failed: {ex.Message}");
                return false; // edxeax 保持為 0
            }
        }
        return false;
    }

    public static bool ReadMsr(uint index, out uint eax, out uint edx, GroupAffinity affinity)
    {
        if (!IsOpen)
        {
            eax = edx = 0;
            return false;
        }

        // 先把目前執行緒 pin 到指定邏輯處理器，取得「舊的」affinity 以便復原
        GroupAffinity prev = ThreadAffinity.Set(affinity);
        try
        {
            ulong v = _pawnIO.ReadMsr(index); // ← 這行會在被 pin 的 CPU 上執行
            edx = (uint)(v >> 32);
            eax = (uint)v;
            return true;
        }
        catch
        {
            eax = edx = 0;
            return false;
        }
        finally
        {
            // 復原：如果 Set 成功回了有效值，就切回去
            if (prev != GroupAffinity.Undefined)
                ThreadAffinity.Set(prev);
        }
    }

    public static bool WriteMsr(uint index, uint eax, uint edx)
    {
        if (_driver == null)
            return false;

        WriteMsrInput input = new() { Register = index, Value = ((ulong)edx << 32) | eax };
        return _driver.DeviceIOControl(Interop.Ring0.IOCTL_OLS_WRITE_MSR, input);
    }

    public static byte ReadIoPort(uint port)
    {
        if (IsOpen)
        {
            try
            {
                // 這裡要用 In8 (ioctl_pio_read)，而不是 SioRead
                return _pawnIO.In8((ushort)port);
            }
            catch
            {
                return 0xFF; // port 無效就回 0xFF，比較符合硬體習慣
            }
        }

        if (_driver == null) return 0;
        uint value = 0;
        _driver.DeviceIOControl(Interop.Ring0.IOCTL_OLS_READ_IO_PORT_BYTE, port, ref value);
        return (byte)(value & 0xFF);
    }

    public static void WriteIoPort(uint port, byte value)
    {
        if (IsOpen)
        {
            try
            {
                // 同樣改成 Out8 (ioctl_pio_write)
                _pawnIO.Out8((ushort)port, value);
            }
            catch
            {
                // 這裡可以 log
            }
            return;
        }
    }

    public static uint GetPciAddress(byte bus, byte device, byte function)
    {
        return (uint)(((bus & 0xFF) << 8) | ((device & 0x1F) << 3) | (function & 7));
    }

    public static bool ReadPciConfig(uint pciAddress, uint regAddress, out uint value)
    {
        if (_driver == null || (regAddress & 3) != 0)
        {
            value = 0;
            return false;
        }

        ReadPciConfigInput input = new() { PciAddress = pciAddress, RegAddress = regAddress };

        value = 0;
        return _driver.DeviceIOControl(Interop.Ring0.IOCTL_OLS_READ_PCI_CONFIG, input, ref value);
    }

    public static bool WritePciConfig(uint pciAddress, uint regAddress, uint value)
    {
        if (_driver == null || (regAddress & 3) != 0)
            return false;

        WritePciConfigInput input = new() { PciAddress = pciAddress, RegAddress = regAddress, Value = value };
        return _driver.DeviceIOControl(Interop.Ring0.IOCTL_OLS_WRITE_PCI_CONFIG, input);
    }

    public static bool ReadMemory<T>(ulong address, ref T buffer)
    {
        if (_driver == null)
            return false;

        ReadMemoryInput input = new() { Address = address, UnitSize = 1, Count = (uint)Marshal.SizeOf(buffer) };
        return _driver.DeviceIOControl(Interop.Ring0.IOCTL_OLS_READ_MEMORY, input, ref buffer);
    }

    public static bool ReadMemory<T>(ulong address, ref T[] buffer)
    {
        if (_driver == null)
            return false;

        ReadMemoryInput input = new() { Address = address, UnitSize = (uint)Marshal.SizeOf(typeof(T)), Count = (uint)buffer.Length };
        return _driver.DeviceIOControl(Interop.Ring0.IOCTL_OLS_READ_MEMORY, input, ref buffer);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct WriteMsrInput
    {
        public uint Register;
        public ulong Value;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct WriteIoPortInput
    {
        public uint PortNumber;
        public byte Value;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct ReadPciConfigInput
    {
        public uint PciAddress;
        public uint RegAddress;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct WritePciConfigInput
    {
        public uint PciAddress;
        public uint RegAddress;
        public uint Value;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct ReadMemoryInput
    {
        public ulong Address;
        public uint UnitSize;
        public uint Count;
    }
}
