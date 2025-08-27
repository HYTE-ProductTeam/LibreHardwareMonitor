using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibreHardwareMonitor.Hardware.PawnIO
{
    public sealed class PawnIoSession : IDisposable
    {
        public IntPtr Handle { get; private set; }

        public PawnIoSession()
        {
            if (PawnIOLib.pawnio_open(out var h) != 0)
                throw new Exception("pawnio_open failed");
            Handle = h;
        }

        public void LoadModule(string path)
        {
            var blob = File.ReadAllBytes(path);
            int rc = PawnIOLib.pawnio_load(Handle, blob, (UIntPtr)blob.Length);
            if (rc != 0) throw new Exception($"pawnio_load failed: {Path.GetFileName(path)}");
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero) { PawnIOLib.pawnio_close(Handle); Handle = IntPtr.Zero; }
        }
    }
}
