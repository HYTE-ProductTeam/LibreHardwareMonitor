// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Partial Copyright (C) Michael Möller <mmoeller@openhardwaremonitor.org> and Contributors.
// All Rights Reserved.

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using LibreHardwareMonitor.Hardware.PawnIO;

namespace LibreHardwareMonitor.Hardware;

public static class Ring0
{
    private static readonly StringBuilder _report = new();

    public static bool IsOpen => _pawnIO != null;

    private static PawnIoBackend _pawnIO;

    public static void Open()
    {
        var modulesDir = Path.Combine(Directory.GetCurrentDirectory(), "modules");
        _pawnIO = new PawnIoBackend(modulesDir);
        if (Directory.Exists(modulesDir) && _pawnIO != null)
        {
            _report.Length = 0;
            _report.AppendLine("Status: PawnIO backend active");
            return;
        }

        return;
    }

    public static void Close()
    {
        if (IsOpen)
        {
            _pawnIO.Dispose();
        }
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
        if (IsOpen)
        {
            ulong v = _pawnIO.ReadMsr(index);

            eax = (uint)(v & 0xFFFFFFFFUL);
            edx = (uint)(v >> 32);
            return true;
        }
        else
        {
            eax = 0;
            edx = 0;
            return false;
        }
    }

    public static bool ReadMsr(uint index, out ulong edxeax)
    {
        edxeax = 0;

        if (IsOpen)
        {
            try
            {
                edxeax = _pawnIO.ReadMsr(index);
                //Debug.WriteLine($"MSR 0x{index:X} = 0x{edxeax:X16}");
                return true;
            }
            catch (Exception ex)
            {
                //Debug.WriteLine($"MSR 0x{index:X} failed: {ex.Message}");
                return false;
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

        GroupAffinity prev = ThreadAffinity.Set(affinity);
        try
        {
            ulong v = _pawnIO.ReadMsr(index);
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
            if (prev != GroupAffinity.Undefined)
                ThreadAffinity.Set(prev);
        }
    }

    public static bool WriteMsr(uint index, uint eax, uint edx)
    {
        if (!IsOpen)
            return false;

        return _pawnIO.WriteMsr(index, eax, edx);
    }

    public static byte ReadIoPort(uint port)
    {
        if (IsOpen)
        {
            try
            {
                return _pawnIO.In8((ushort)port);
            }
            catch
            {
                return 0xFF;
            }
        }
        return 0xFF;
    }

    public static void WriteIoPort(uint port, byte value)
    {
        if (IsOpen)
        {
            try
            {
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
        if (!IsOpen || (regAddress & 3) != 0)
        {
            value = 0;
            return false;
        }

        return _pawnIO.PciReadConfigDword(pciAddress, regAddress, out value);
    }

    public static bool WritePciConfig(uint pciAddress, uint regAddress, uint value)
    {
        if (!IsOpen || (regAddress & 3) != 0)
            return false;

        return _pawnIO.PciWriteConfigDword(pciAddress, regAddress, value);
    }

    public static bool ReadMemory<T>(ulong address, ref T buffer)
    where T : unmanaged
    {
        if (!IsOpen) return false;
        return _pawnIO.ReadPhysicalMemory(address, ref buffer);
    }

    public static bool ReadMemory<T>(ulong address, ref T[] buffer)
        where T : unmanaged
    {
        if (!IsOpen) return false;
        return _pawnIO.ReadPhysicalMemory(address, ref buffer);
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
