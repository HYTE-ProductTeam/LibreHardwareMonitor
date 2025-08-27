using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibreHardwareMonitor.Hardware.PawnIO
{
    public interface IHardwareAccessBackend
    {
        // Port IO（多數 SIO 控制/偵測只需要 8-bit；16/32 若需要可補）
        byte In8(ushort port);
        void Out8(ushort port, byte value);

        // SIO config 寄存器存取（Index/Data + Register 位址）
        byte SioRead(byte reg);
        void SioWrite(byte reg, byte value);
        void SioEnter();
        void SioExit();
        void SioDetect(byte slot /*0=0x2E/0x2F, 1=0x4E/0x4F*/);

        // EC
        byte EcRead(ushort offset);
        void EcWrite(ushort offset, byte value);

        // MSR
        ulong ReadMsr(uint msr);

        // SMBus（依你需要加：ReadByte/WriteByte/ReadWord…）
        byte SmbusReadByte(byte addr, byte reg);
        void SmbusWriteByte(byte addr, byte reg, byte val);
    }
}
