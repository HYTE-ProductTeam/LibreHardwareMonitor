using System;
using System.IO;

namespace LibreHardwareMonitor.Hardware.PawnIO
{
    public sealed class PawnIoModule : IDisposable
    {
        public IntPtr Handle { get; private set; } = IntPtr.Zero;

        public string Name { get; private set; }

        public PawnIoModule(string binPath, string name)
        {
            Name = name;
            int rc = PawnIOLib.pawnio_open(out var h);
            if (rc != 0)
                throw new Exception($"pawnio_open rc=0x{rc:X8}");
            Handle = h;

            var blob = File.ReadAllBytes(binPath);
            rc = PawnIOLib.pawnio_load(Handle, blob, (UIntPtr)blob.Length);
            if (rc != 0)
                throw new Exception($"pawnio_load {Path.GetFileName(binPath)} rc=0x{rc:X8}");
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero)
            {
                PawnIOLib.pawnio_close(Handle);
                Handle = IntPtr.Zero;
            }
        }
    }
}
