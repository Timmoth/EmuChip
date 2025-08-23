using System;
using System.Runtime.InteropServices.JavaScript;
using EmuChip;

public static partial class Chip8EmulatorApi
{
    private static readonly Chip8Emulator Emulator = new();

    [JSExport]
    internal static void Initialize(byte[] romBytes)
    {
        Emulator.Initialize(romBytes);
    }

    [JSExport]
    internal static void SetKeys(byte[] keys)
    {
        Array.Copy(keys, Emulator.Keys, keys.Length);
    }
    
    [JSExport]
    internal static void Step()
    {
        Emulator.Step();
    }
    
    [JSExport]
    internal static void UpdateTimers()
    {
        Emulator.UpdateTimers();
    }
    
    [JSExport]
    internal static byte[]? GetGraphics()
    {
        if (Emulator.DrawFlag)
        {
            Emulator.DrawFlag = false;
            return Emulator.Graphics;
        }

        return null;
    }

    [JSExport]
    internal static bool ShouldBeep()
    {
        if (Emulator.ShouldBeep)
        {
            Emulator.ShouldBeep = false;
            return true;
        }
        return false;
    }
}