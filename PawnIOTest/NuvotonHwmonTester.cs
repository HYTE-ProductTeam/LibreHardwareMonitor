using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using LibreHardwareMonitor.Hardware.PawnIO; // 這是你提供的 IoctlHelper 命名空間

class TestProgram
{
    private const string Dll = @"C:\Program Files\PawnIO\PawnIOLib.dll";
    // ---- PawnIOLib P/Invoke ----
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    static extern int pawnio_version(out uint version);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    static extern int pawnio_open(out IntPtr handle);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    static extern int pawnio_load(IntPtr handle, byte[] blob, UIntPtr size);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    static extern int pawnio_close(IntPtr handle);

    // ---- Runtime 暴露之函式名稱（請依你的 blob 實際名稱調整）----
    const string FN_INB = "inb";
    const string FN_OUTB = "outb";

    // ---- SIO 常數 ----
    const ushort SIO_IDX_2E = 0x2E;
    const ushort SIO_DAT_2F = 0x2F;
    const ushort SIO_IDX_4E = 0x4E;
    const ushort SIO_DAT_4F = 0x4F;

    const byte REG_LDN = 0x07;
    const byte LDN_HWMON = 0x0B;
    const byte REG_BASE_HI = 0x60;
    const byte REG_BASE_LO = 0x61;

    // NCT679x bank/index 讀法偏移
    const ushort ADDR_OFF = 0x05;  // base + 5 = address register
    const byte BANK_REG = 0x4E;  // bank select register index
    const ushort DATA_OFF = 0x06;  // base + 6 = data register

    // Nuvoton VendorID registers（與 LHM 一致）
    const ushort VENDOR_ID_HIGH = 0x804F;
    const ushort VENDOR_ID_LOW = 0x004F;
    const ushort NUVOTON_ID = 0x5CA3;

    public static void Main()
    {
        try
        {
            // 1) 顯示 PawnIOLib 版本
            Check(pawnio_version(out var ver), "pawnio_version");
            Console.WriteLine($"PawnIOLib version = {(ver >> 16)}.{(ver >> 8) & 0xFF}.{ver & 0xFF}");

            // 2) 開裝置
            Check(pawnio_open(out var h), "pawnio_open");
            Console.WriteLine("Opened PawnIO device ✔");

            try
            {
                // 3) 載入 runtime blob（請替換為你的實際檔名/路徑）
                var blobPath = @"D:\Git\Nexus\LibreHardwareMonitor\LibreHardwareMonitorLib\modules\LpcIO.bin";
                if (!File.Exists(blobPath))
                    throw new FileNotFoundException($"Runtime blob not found: {blobPath}");
                var blob = File.ReadAllBytes(blobPath);
                Check(pawnio_load(h, blob, (UIntPtr)blob.Length), $"pawnio_load({blobPath})");
                Console.WriteLine($"Loaded runtime blob ({blob.Length} bytes) ✔");

                // 4) RTC 測試：outb(0x70,0x00) 選秒數 → inb(0x71) 讀取，連續讀 5 次應會變動
                Console.WriteLine("\n=== RTC seconds test (robust) ===");
                // 嘗試的函式名候選（依你的 pawnrt 實際定義為準）
                string[] OUTB_CAND = { "outb", "io_outb", "outportb", "outp" };
                string[] INB_CAND = { "inb", "io_inb", "inportb", "inp" };

                (string name, int hr) TryOutb(IntPtr h, ulong port, ulong value)
                {
                    foreach (var fn in OUTB_CAND)
                    {
                        try
                        {
                            // 帶一個愚蠢的 out buffer（有些驅動會要求非空）
                            ulong[] dummyOut = new ulong[1];
                            int rc = PawnIOLib.pawnio_execute(
                                h, fn,
                                new ulong[] { port, value }, (UIntPtr)2,
                                dummyOut, (UIntPtr)dummyOut.Length,
                                out var retCount);
                            Console.WriteLine($"outb try '{fn}': rc={rc}, ret={retCount}");
                            if (rc == 0) return (fn, rc);
                        }
                        catch (Exception ex) { Console.WriteLine($"outb '{fn}' -> {ex.Message}"); }
                    }
                    return (null, 87); // invalid parameter
                }

                (string name, int hr, byte val) TryInb(IntPtr h, ulong port)
                {
                    foreach (var fn in INB_CAND)
                    {
                        try
                        {
                            ulong[] outBuf = new ulong[1];
                            int rc = PawnIOLib.pawnio_execute(
                                h, fn,
                                new ulong[] { port }, (UIntPtr)1,
                                outBuf, (UIntPtr)1,
                                out var retCount);
                            Console.WriteLine($"inb  try '{fn}': rc={rc}, ret={retCount}, val={(retCount.ToUInt64() > 0 ? ((byte)(outBuf[0] & 0xFF)).ToString("X2") : "--")}");
                            if (rc == 0 && retCount.ToUInt64() >= 1) return (fn, rc, (byte)(outBuf[0] & 0xFF));
                        }
                        catch (Exception ex) { Console.WriteLine($"inb  '{fn}' -> {ex.Message}"); }
                    }
                    return (null, 87, (byte)0x00);
                }
                // 先 outb(0x70, 0x00) 選秒數寄存器
                var outbRes = TryOutb(h, 0x70, 0x00);
                if (outbRes.name == null)
                {
                    Console.WriteLine("❌ 找不到可用的 outb 函式名稱；請確認 runtime 匯出的實際名稱。");
                    return;
                }

                // 再用 inb(0x71) 連續讀 5 次
                for (int i = 0; i < 5; i++)
                {
                    var inbRes = TryInb(h, 0x71);
                    if (inbRes.name == null)
                    {
                        Console.WriteLine("❌ 找不到可用的 inb 函式名稱；請確認 runtime 匯出的實際名稱。");
                        return;
                    }
                    Console.WriteLine($"t{i}: sec=0x{inbRes.val:X2}  (via '{inbRes.name}')");
                    Thread.Sleep(200);
                }

            }
            finally
            {
                pawnio_close(h);
            }

            Console.WriteLine("\nAll done.");
        }
        catch (Exception ex)
        {
            Console.WriteLine("[ERR] " + ex.Message);
        }
    }


    

    // === 小工具 ===

    static void Check(int hr, string where)
    {
        if (hr != 0) throw new Win32Exception(hr, where + " failed");
    }

    // SIO 進入/退出（用 outb 實作）
    static void SioEnter(IntPtr h, ushort idx)
    {
        // 進入序列：0x87, 0x87
        IoctlHelper.ExecNoOut(h, FN_OUTB, new ulong[] { idx, 0x87 });
        IoctlHelper.ExecNoOut(h, FN_OUTB, new ulong[] { idx, 0x87 });
    }

    static void SioExit(IntPtr h, ushort idx)
    {
        IoctlHelper.ExecNoOut(h, FN_OUTB, new ulong[] { idx, 0xAA });
    }

    static void SioWrite(IntPtr h, ushort idx, ushort dat, byte reg, byte val)
    {
        IoctlHelper.ExecNoOut(h, FN_OUTB, new ulong[] { idx, reg });
        IoctlHelper.ExecNoOut(h, FN_OUTB, new ulong[] { dat, val });
    }

    static byte SioRead(IntPtr h, ushort idx, ushort dat, byte reg)
    {
        IoctlHelper.ExecNoOut(h, FN_OUTB, new ulong[] { idx, reg });
        ulong v = IoctlHelper.ExecOutSingle(h, FN_INB, new ulong[] { dat });
        return (byte)(v & 0xFF);
    }

    static bool TryProbeSioBase(IntPtr h, ushort idx, ushort dat, out ushort basePort)
    {
        basePort = 0;
        try
        {
            Console.WriteLine($"-- Try SIO {idx:X2}/{dat:X2} --");
            SioEnter(h, idx);

            // 選 H/W Monitor LDN
            SioWrite(h, idx, dat, REG_LDN, LDN_HWMON);

            byte baseHi = SioRead(h, idx, dat, REG_BASE_HI);
            byte baseLo = SioRead(h, idx, dat, REG_BASE_LO);
            basePort = (ushort)((baseHi << 8) | baseLo);

            SioExit(h, idx);

            Console.WriteLine($"BASE = 0x{basePort:X4}");
            return basePort != 0 && basePort != 0xFFFF;
        }
        catch (Exception ex)
        {
            Console.WriteLine("  (probe failed) " + ex.Message);
            try { SioExit(h, idx); } catch { }
            return false;
        }
    }

    // 以 bank/index 讀一個 H/W Monitor 寄存器（NCT679x 風格）
    static byte ReadHwmonByte(IntPtr h, ushort basePort, ushort address)
    {
        byte bank = (byte)(address >> 8);
        byte reg = (byte)(address & 0xFF);

        // 選 bank
        IoctlHelper.ExecNoOut(h, FN_OUTB, new ulong[] { (ulong)(basePort + ADDR_OFF), BANK_REG });
        IoctlHelper.ExecNoOut(h, FN_OUTB, new ulong[] { (ulong)(basePort + DATA_OFF), bank });

        // 選寄存器 index
        IoctlHelper.ExecNoOut(h, FN_OUTB, new ulong[] { (ulong)(basePort + ADDR_OFF), reg });

        // 讀 data
        ulong v = IoctlHelper.ExecOutSingle(h, FN_INB, new ulong[] { (ulong)(basePort + DATA_OFF) });
        return (byte)(v & 0xFF);
    }
}
