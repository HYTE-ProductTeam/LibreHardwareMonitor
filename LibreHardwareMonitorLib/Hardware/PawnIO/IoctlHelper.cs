using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace LibreHardwareMonitor.Hardware.PawnIO
{
    internal static class IoctlHelper
    {
        public static T ExecOut<T>(IntPtr h, string name, byte[] input) where T : struct
        {
            IntPtr inPtr = IntPtr.Zero, outPtr = IntPtr.Zero;
            try
            {
                int inLen = input?.Length ?? 0;
                int outLen = Marshal.SizeOf<T>();

                if (inLen > 0)
                {
                    inPtr = Marshal.AllocHGlobal(inLen);
                    Marshal.Copy(input, 0, inPtr, inLen);
                }

                outPtr = Marshal.AllocHGlobal(outLen);

                int rc = PawnIOLib.pawnio_execute(
                    h, name,
                    inPtr, (UIntPtr)inLen,
                    outPtr, (UIntPtr)outLen,
                    out var got);

                if (rc != 0)
                    throw new Exception($"{name} failed: 0x{rc:X}");

                if ((int)got != outLen)
                    throw new Exception($"{name} unexpected out size: got {(int)got}, want {outLen}");

                return Marshal.PtrToStructure<T>(outPtr);
            }
            finally
            {
                if (inPtr != IntPtr.Zero) Marshal.FreeHGlobal(inPtr);
                if (outPtr != IntPtr.Zero) Marshal.FreeHGlobal(outPtr);
            }
        }

        public static void ExecNoOut(IntPtr h, string name, byte[] input)
        {
            IntPtr inPtr = IntPtr.Zero;
            try
            {
                int inLen = input?.Length ?? 0;
                if (inLen > 0)
                {
                    inPtr = Marshal.AllocHGlobal(inLen);
                    Marshal.Copy(input, 0, inPtr, inLen);
                }

                int rc = PawnIOLib.pawnio_execute(
                    h, name,
                    inPtr, (UIntPtr)inLen,
                    IntPtr.Zero, UIntPtr.Zero,
                    out _);

                if (rc != 0)
                    throw new Exception($"{name} failed: 0x{rc:X}");
            }
            finally
            {
                if (inPtr != IntPtr.Zero) Marshal.FreeHGlobal(inPtr);
            }
        }

        public static byte[] StructToBytes<T>(T s) where T : struct
        {
            int size = Marshal.SizeOf<T>();
            var buf = new byte[size];
            IntPtr p = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(s, p, false);
                Marshal.Copy(p, buf, 0, size);
                return buf;
            }
            finally
            {
                Marshal.FreeHGlobal(p);
            }
        }
    }
}
