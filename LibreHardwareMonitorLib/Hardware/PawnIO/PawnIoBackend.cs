using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace LibreHardwareMonitor.Hardware.PawnIO
{
    public sealed class PawnIoBackend : IHardwareAccessBackend, IDisposable
    {
        private readonly PawnIoSession _sess;

        public PawnIoBackend(string modulesDir)
        {
            _sess = new PawnIoSession();
            _sess.LoadModule(Path.Combine(modulesDir, "LpcIO.amx"));      // SIO + Port IO
            _sess.LoadModule(Path.Combine(modulesDir, "LpcACPIEC.amx"));  // EC
            _sess.LoadModule(Path.Combine(modulesDir, "SmbusI801.amx"));  // SMBus (依需要載)
            _sess.LoadModule(Path.Combine(modulesDir, "IntelMSR.amx"));   // MSR（讀）
        }

        public void Dispose() => _sess.Dispose();

        [StructLayout(LayoutKind.Sequential)] struct U32_Out { public uint Val; }
        [StructLayout(LayoutKind.Sequential)] struct U8_Out { public byte Val; }
        [StructLayout(LayoutKind.Sequential)] struct U16U8_In { public ushort A; public byte B; } // for pio_write: (port, value)

        public void SioDetect(byte slot) // 0->0x2E, 1->0x4E
        {
            var inb = BitConverter.GetBytes((uint)slot);
            IoctlHelper.ExecOut<U32_Out>(_sess.Handle, "ioctl_detect", inb); // out[0] = g_type（可保留查看ID）
        }

        public void SioEnter() => IoctlHelper.ExecNoOut(_sess.Handle, "ioctl_enter", []);
        public void SioExit() => IoctlHelper.ExecNoOut(_sess.Handle, "ioctl_exit", []);

        public byte SioRead(byte reg)
        {
            var inb = BitConverter.GetBytes((uint)reg);
            var o = IoctlHelper.ExecOut<U8_Out>(_sess.Handle, "ioctl_read", inb);
            return o.Val;
        }

        public void SioWrite(byte reg, byte val)
        {
            // ioctl_write 的 in_size = 2（reg, val）
            var inArr = new uint[] { reg, val };
            var inb = new byte[inArr.Length * sizeof(uint)];
            Buffer.BlockCopy(inArr, 0, inb, 0, inb.Length);
            IoctlHelper.ExecNoOut(_sess.Handle, "ioctl_write", inb);
        }

        public byte In8(ushort port)
        {
            var inb = BitConverter.GetBytes((uint)port);
            var o = IoctlHelper.ExecOut<U8_Out>(_sess.Handle, "ioctl_pio_read", inb);
            return o.Val; // 若 port 不在白名單，模組會回 ACCESS_DENIED -> rc != 0
        }

        public void Out8(ushort port, byte value)
        {
            var inbuf = new U16U8_In { A = port, B = value };
            var raw = IoctlHelper.StructToBytes(inbuf);
            IoctlHelper.ExecNoOut(_sess.Handle, "ioctl_pio_write", raw);
        }

        public byte EcRead(ushort offset)
        {
            // 典型 in=[offset] (u16), out=[value] (u8)
            var inb = BitConverter.GetBytes(offset);
            var o = IoctlHelper.ExecOut<U8_Out>(_sess.Handle, "ioctl_ec_read", inb);
            return o.Val;
        }

        public void EcWrite(ushort offset, byte value)
        {
            var inArr = new ushort[] { offset, value };
            var inb = new byte[inArr.Length * sizeof(ushort)];
            Buffer.BlockCopy(inArr, 0, inb, 0, inb.Length);
            IoctlHelper.ExecNoOut(_sess.Handle, "ioctl_ec_write", inb);
        }
        [StructLayout(LayoutKind.Sequential)] struct U64_Out { public ulong Val; }

        public ulong ReadMsr(uint msr)
        {
            var inb = BitConverter.GetBytes(msr);
            var o = IoctlHelper.ExecOut<U64_Out>(_sess.Handle, "ioctl_read_msr", inb);
            return o.Val;
        }
        public byte SmbusReadByte(byte addr, byte reg)
        {
            var inArr = new uint[] { addr, reg };
            var inb = new byte[inArr.Length * sizeof(uint)];
            Buffer.BlockCopy(inArr, 0, inb, 0, inb.Length);
            var o = IoctlHelper.ExecOut<U8_Out>(_sess.Handle, "ioctl_smbus_read_byte", inb);
            return o.Val;
        }
        public void SmbusWriteByte(byte addr, byte reg, byte val)
        {
            var inArr = new uint[] { addr, reg, val };
            var inb = new byte[inArr.Length * sizeof(uint)];
            Buffer.BlockCopy(inArr, 0, inb, 0, inb.Length);
            IoctlHelper.ExecNoOut(_sess.Handle, "ioctl_smbus_write_byte", inb);
        }
    }
}
