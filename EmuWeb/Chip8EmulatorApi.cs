using System;
using System.Runtime.InteropServices.JavaScript;
using EmuChip;

public static partial class Chip8EmulatorApi
{
    private static readonly Chip8Emulator Emulator = new();

    [JSExport]
    internal static string Disassemble(int opcode)
    {
        var result = Chip8Disassembler.Disassemble((ushort)opcode);
        return $"{result[0]}|{result[1]}|{result[2]}";
    }
    
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
    internal static int GetWidth()
    {
       return Emulator.Width;
    }
    
        
    [JSExport]
    internal static int GetHeight()
    {
        return Emulator.Height;
    }
    
    [JSExport]
    internal static void UpdateTimers()
    {
        Emulator.UpdateTimers();
    }
    
    [JSExport]
    internal static byte[]? GetGraphics()
    {
        return Emulator.Graphics;
    }

    [JSExport]
    internal static bool ShouldBeep()
    {
        return Emulator.ShouldBeep;
    }
}