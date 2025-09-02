using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace LibreHardwareMonitor.Hardware.PawnIO
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct GROUP_AFFINITY_WIN32
    {
        public UIntPtr Mask;
        public ushort Group;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public ushort[] Reserved;
    }

    internal static class ThreadAffinity
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetThreadGroupAffinity(IntPtr hThread, out GROUP_AFFINITY_WIN32 GroupAffinity);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetThreadGroupAffinity(IntPtr hThread, ref GROUP_AFFINITY_WIN32 GroupAffinity, out GROUP_AFFINITY_WIN32 PreviousGroupAffinity);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentThread();

        public static GROUP_AFFINITY_WIN32 ToWin32(this LibreHardwareMonitor.Hardware.GroupAffinity a)
        {
            return new GROUP_AFFINITY_WIN32
            {
                Group = a.Group,
                Mask = (UIntPtr)a.Mask,
                Reserved = new ushort[3]
            };
        }

        public sealed class Scope : IDisposable
        {
            private readonly GROUP_AFFINITY_WIN32 _prev;
            private bool _active;

            public Scope(LibreHardwareMonitor.Hardware.GroupAffinity a)
            {
                if (a == LibreHardwareMonitor.Hardware.GroupAffinity.Undefined)
                    return; // 不強制 pin

                var target = a.ToWin32();
                IntPtr th = GetCurrentThread();

                if (!GetThreadGroupAffinity(th, out _prev))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "GetThreadGroupAffinity failed");

                if (!SetThreadGroupAffinity(th, ref target, out _))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "SetThreadGroupAffinity failed");

                _active = true;
            }

            public void Dispose()
            {
                if (!_active) return;
                IntPtr th = GetCurrentThread();
                //if (!SetThreadGroupAffinity(th, ref _prev, out _))
                //    throw new Win32Exception(Marshal.GetLastWin32Error(), "Restore SetThreadGroupAffinity failed");
                _active = false;
            }
        }
    }
}
