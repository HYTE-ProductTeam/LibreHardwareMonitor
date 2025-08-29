using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace LibreHardwareMonitor.Hardware.PawnIO
{
    public static class IoctlHelper
    {
        /// <summary>
        /// 執行 ioctl 並返回結構體結果
        /// </summary>
        public static T ExecOut<T>(IntPtr h, string ioctl, ulong[] input = null) where T : struct
        {
            // 計算輸出需要多少個 ulong 元素
            int structSize = Marshal.SizeOf<T>();
            int outputElementCount = (structSize + 7) / 8; // 向上取整到 8-byte 邊界
            ulong[] outputBuffer = new ulong[outputElementCount];

            int rc = PawnIOLib.pawnio_execute(
                h, ioctl,
                input, (UIntPtr)(input?.Length ?? 0),
                outputBuffer, (UIntPtr)outputElementCount,
                out var returnCount);

            if (rc != 0)
                throw new Win32Exception(rc);

            if (returnCount.ToUInt64() == 0)
                throw new InvalidDataException("No data returned");

            // 將 ulong[] 轉換為 byte[] 再轉為結構體
            byte[] bytes = UlongArrayToBytes(outputBuffer, structSize);
            return BytesToStruct<T>(bytes);
        }

        /// <summary>
        /// 執行 ioctl 並返回 ulong[] 結果
        /// </summary>
        public static ulong[] ExecOutBytes(IntPtr h, string ioctl, ulong[] input = null, int expectedOutputCount = 1)
        {
            ulong[] output = new ulong[expectedOutputCount];

            int rc = PawnIOLib.pawnio_execute(
                h, ioctl,
                input, (UIntPtr)(input?.Length ?? 0),
                output, (UIntPtr)expectedOutputCount,
                out var returnCount);

            if (rc != 0)
                throw new Win32Exception(rc);

            // 如果實際返回的元素少於預期，調整陣列大小
            if (returnCount.ToUInt64() < (ulong)expectedOutputCount)
            {
                ulong[] result = new ulong[(int)returnCount.ToUInt64()];
                Array.Copy(output, result, (int)returnCount.ToUInt64());
                return result;
            }

            return output;
        }

        /// <summary>
        /// 執行 ioctl 不期望返回數據
        /// </summary>
        public static void ExecNoOut(IntPtr h, string ioctl, ulong[] input = null)
        {
            int rc = PawnIOLib.pawnio_execute(
                h, ioctl,
                input, (UIntPtr)(input?.Length ?? 0),
                null, UIntPtr.Zero,
                out var _);

            if (rc != 0)
                throw new Win32Exception(rc);
        }

        /// <summary>
        /// 執行 ioctl 並返回單個 ulong 值（適合 MSR 讀取）
        /// </summary>
        public static ulong ExecOutSingle(IntPtr h, string ioctl, ulong[] input = null)
        {
            ulong[] output = new ulong[1];

            int rc = PawnIOLib.pawnio_execute(
                h, ioctl,
                input, (UIntPtr)(input?.Length ?? 0),
                output, (UIntPtr)1,
                out var returnCount);

            if (rc != 0)
                throw new Win32Exception(rc);

            if (returnCount.ToUInt64() == 0)
                throw new InvalidDataException("No data returned");

            return output[0];
        }

        /// <summary>
        /// 便利方法：單個 uint MSR 讀取
        /// </summary>
        public static ulong ReadMsr(IntPtr h, string ioctl, uint msr)
        {
            ulong[] input = { msr };
            return ExecOutSingle(h, ioctl, input);
        }

        /// <summary>
        /// 便利方法：帶核心編號的 MSR 讀取
        /// </summary>
        public static ulong ReadMsrWithCore(IntPtr h, string ioctl, uint core, uint msr)
        {
            ulong[] input = { core, msr };
            return ExecOutSingle(h, ioctl, input);
        }

        /// <summary>
        /// 便利方法：傳入結構體作為輸入，返回結構體
        /// </summary>
        public static TOut ExecStructToStruct<TIn, TOut>(IntPtr h, string ioctl, TIn input)
            where TIn : struct
            where TOut : struct
        {
            ulong[] inputArray = StructToUlongArray(input);
            return ExecOut<TOut>(h, ioctl, inputArray);
        }

        /// <summary>
        /// 便利方法：傳入結構體作為輸入，無返回值
        /// </summary>
        public static void ExecStruct<TIn>(IntPtr h, string ioctl, TIn input) where TIn : struct
        {
            ulong[] inputArray = StructToUlongArray(input);
            ExecNoOut(h, ioctl, inputArray);
        }

        /// <summary>
        /// 結構體轉換為 byte[]
        /// </summary>
        public static byte[] StructToBytes<T>(T s) where T : struct
        {
            int sz = Marshal.SizeOf<T>();
            byte[] buf = new byte[sz];
            IntPtr p = Marshal.AllocHGlobal(sz);
            try
            {
                Marshal.StructureToPtr(s, p, false);
                Marshal.Copy(p, buf, 0, sz);
            }
            finally
            {
                Marshal.FreeHGlobal(p);
            }
            return buf;
        }

        /// <summary>
        /// byte[] 轉換為結構體
        /// </summary>
        public static T BytesToStruct<T>(byte[] bytes) where T : struct
        {
            int sz = Marshal.SizeOf<T>();
            if (bytes.Length < sz)
                throw new ArgumentException($"Buffer too small: got {bytes.Length}, need {sz}");

            IntPtr p = Marshal.AllocHGlobal(sz);
            try
            {
                Marshal.Copy(bytes, 0, p, sz);
                return Marshal.PtrToStructure<T>(p);
            }
            finally
            {
                Marshal.FreeHGlobal(p);
            }
        }

        /// <summary>
        /// 將 ulong[] 轉換為指定長度的 byte[]
        /// </summary>
        private static byte[] UlongArrayToBytes(ulong[] ulongs, int targetByteLength)
        {
            byte[] result = new byte[targetByteLength];
            int sourceByteLength = Math.Min(ulongs.Length * 8, targetByteLength);

            Buffer.BlockCopy(ulongs, 0, result, 0, sourceByteLength);
            return result;
        }

        /// <summary>
        /// 將結構體轉換為 ulong[]
        /// </summary>
        private static ulong[] StructToUlongArray<T>(T s) where T : struct
        {
            byte[] bytes = StructToBytes(s);
            int ulongCount = (bytes.Length + 7) / 8; // 向上取整
            ulong[] result = new ulong[ulongCount];

            Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length);
            return result;
        }
    }
}
