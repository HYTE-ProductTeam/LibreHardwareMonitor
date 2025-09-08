// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Partial Copyright (C) Michael Möller <mmoeller@openhardwaremonitor.org> and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.ComponentModel;
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
        var modulesDir = Path.Combine(Directory.GetCurrentDirectory(), "resources", "PawnIO", "modules");
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
        eax = edx = 0;
        if (!IsOpen) return false;

        try
        {
            ulong v = _pawnIO.ReadMsr(index);
            eax = (uint)v;
            edx = (uint)(v >> 32);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ReadMsr 0x{index:X} failed: {ex.Message}");
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
                Debug.WriteLine($"MSR 0x{index:X} = 0x{edxeax:X16}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MSR 0x{index:X} failed: {ex.Message}");
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
        if (!IsOpen) return 0xFF;
        try { return _pawnIO.In8(checked((ushort)port)); }
        catch (Win32Exception ex)
        {
            Debug.WriteLine($"ReadIoPort 0x{port:X}: 0x{ex.NativeErrorCode:X8}");
            return 0xFF;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ReadIoPort 0x{port:X} failed: {ex.Message}");
            return 0xFF;
        }
    }

    public static void WriteIoPort(uint port, byte value)
    {
        if (!IsOpen) return;
        try { _pawnIO.Out8(checked((ushort)port), value); }
        catch (Exception ex) { Debug.WriteLine($"WriteIoPort 0x{port:X} failed: {ex.Message}"); }
    }

    public static uint GetPciAddress(byte bus, byte device, byte function)
    {
        return (uint)(((bus & 0xFF) << 8) | ((device & 0x1F) << 3) | (function & 7));
    }

    private static readonly HashSet<FnKey> _absent = new();

    //public static bool ReadPciConfig(uint pciAddress, uint regAddress, out uint value)
    //{
    //    if (!IsOpen || (regAddress & 3) != 0)
    //    {
    //        value = 0;
    //        return false;
    //    }

    //    return _pawnIO.PciReadConfigDword(pciAddress, regAddress, out value);
    //}

    public static bool ReadPciConfigOrigin(uint pciAddress, uint regAddress, out uint value)
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


    public readonly struct FnKey : IEquatable<FnKey>
    {
        public readonly byte Bus, Dev, Fn;
        public FnKey(byte bus, byte dev, byte fn) { Bus = bus; Dev = dev; Fn = fn; }
        public bool Equals(FnKey o) => Bus == o.Bus && Dev == o.Dev && Fn == o.Fn;
        public override bool Equals(object o) => o is FnKey k && Equals(k);
        public override int GetHashCode() => (Bus << 16) | (Dev << 8) | Fn;
    }
    public readonly struct PciKey : IEquatable<PciKey>
    {
        public readonly uint Addr, Reg;
        public PciKey(uint addr, uint reg) { Addr = addr; Reg = reg; }
        public bool Equals(PciKey o) => Addr == o.Addr && Reg == o.Reg;
        public override bool Equals(object o) => o is PciKey k && Equals(k);
        public override int GetHashCode() => unchecked((int)(Addr * 397) ^ (int)Reg);
    }

    static readonly Dictionary<PciKey, (uint val, long stamp)> _pciCache = new();
    const long PciTtlMs = 5_000; // you can raise this to 60_000+ for static regs

    static bool TryEnsurePresent(uint addr)
    {
        var key = new FnKey((byte)(addr >> 8), (byte)((addr >> 3) & 0x1F), (byte)(addr & 7));
        if (_absent.Contains(key)) return false;

        if (!Ring0.ReadPciConfigOrigin(addr, 0x00, out var v) || ((v & 0xFFFF) == 0xFFFF)) { _absent.Add(key); return false; }
        return true;
    }

    public static bool ReadPciConfig(uint addr, uint reg, out uint value)
    {
        value = 0;
        if (!TryEnsurePresent(addr)) return false;

        var key = new PciKey(addr, reg);
        var now = Environment.TickCount;
        if (_pciCache.TryGetValue(key, out var e) && (now - e.stamp) < PciTtlMs) { value = e.val; return true; }

        var ok = ReadPciConfigOrigin(addr, reg, out value);
        if (ok) _pciCache[key] = (value, now);
        return ok;
    }
}

public readonly struct FnKey : IEquatable<FnKey>
{
    public readonly byte Bus;
    public readonly byte Dev;
    public readonly byte Fn;

    public FnKey(byte bus, byte dev, byte fn)
    {
        Bus = bus;
        Dev = dev;
        Fn = fn;
    }

    public bool Equals(FnKey other) => Bus == other.Bus && Dev == other.Dev && Fn == other.Fn;
    public override bool Equals(object obj) => obj is FnKey other && Equals(other);
    public override int GetHashCode() => (Bus << 16) | (Dev << 8) | Fn;
}
