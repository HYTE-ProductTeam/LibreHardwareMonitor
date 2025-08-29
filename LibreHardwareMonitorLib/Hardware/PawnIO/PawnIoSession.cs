using System;
using System.Collections.Generic;
using System.Diagnostics;
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
            int rc = PawnIOLib.pawnio_open(out var h);
            if (rc != 0)
                throw new Exception("pawnio_open failed");
            Handle = h;
        }

        public void LoadModule(string path)
        {
            var blob = File.ReadAllBytes(path);
            int rc = PawnIOLib.pawnio_load(Handle, blob, (UIntPtr)blob.Length);
            if (rc != 0)
            {
                Debug.WriteLine($"can't load file {path}, error: {rc}");
            }
                //throw new Exception($"pawnio_load failed: {Path.GetFileName(path)}");
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero) { PawnIOLib.pawnio_close(Handle); Handle = IntPtr.Zero; }
        }
    }
}
