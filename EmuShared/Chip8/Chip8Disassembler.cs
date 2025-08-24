using System;

namespace EmuChip;

public static class Chip8Disassembler
{
    private static readonly Func<ushort, string[]>[] _dispatch = new Func<ushort, string[]>[16];

   static Chip8Disassembler()
{
    _dispatch[0x0] = Handle0;
    _dispatch[0x1] = op => [$"jp   0x{op & 0x0FFF:X3}", "Set Program Counter (PC) to address NNN.", "1nnn"];
    _dispatch[0x2] = op => [$"call 0x{op & 0x0FFF:X3}", "Push current PC to stack, then jump to address NNN.", "2nnn"];
    _dispatch[0x3] = op => [$"se   v{(op & 0x0F00) >> 8}, 0x{op & 0x00FF:X2}", "Skip next instruction if register Vx equals byte KK.", "3xkk"];
    _dispatch[0x4] = op => [$"sne  v{(op & 0x0F00) >> 8}, 0x{op & 0x00FF:X2}", "Skip next instruction if register Vx does not equal byte KK.", "4xkk"];
    _dispatch[0x5] = op => [$"se   v{(op & 0x0F00) >> 8}, v{(op & 0x00F0) >> 4}", "Skip next instruction if register Vx equals register Vy.", "5xy0"];
    _dispatch[0x6] = op => [$"ld   v{(op & 0x0F00) >> 8}, 0x{op & 0x00FF:X2}", "Load the value of byte KK into register Vx.", "6xkk"];
    _dispatch[0x7] = op => [$"add  v{(op & 0x0F00) >> 8}, 0x{op & 0x00FF:X2}", "Add the value of byte KK to register Vx.", "7xkk"];
    _dispatch[0x8] = Handle8;
    _dispatch[0x9] = op => [$"sne  v{(op & 0x0F00) >> 8}, v{(op & 0x00F0) >> 4}", "Skip next instruction if register Vx does not equal register Vy.", "9xy0"];
    _dispatch[0xA] = op => [$"ld   I, 0x{op & 0x0FFF:X3}", "Load the address NNN into the 16-bit index register I.", "Annn"];
    _dispatch[0xB] = op => [$"jp   v0, 0x{op & 0x0FFF:X3}", "Jump to address NNN plus the value in register V0.", "Bnnn"];
    _dispatch[0xC] = op => [$"rnd  v{(op & 0x0F00) >> 8}, 0x{op & 0x00FF:X2}", "Set Vx to a random byte ANDed with byte KK.", "Cxkk"];
    _dispatch[0xD] = HandleD;
    _dispatch[0xE] = HandleE;
    _dispatch[0xF] = HandleF;
}

private static string[]  Handle0(ushort op)
{
    return op switch
    {
        0x00E0 => ["cls", "Clear the entire display to black.", "00E0"],
        0x00EE => ["rts", "Pop address from stack to PC to return from a subroutine.", "00EE"],
        _ when (op & 0xFFF0) == 0x00C0 => [$"scdown {op & 0x000F}", "Scroll display down by N pixels. (SCHIP)", "00Cn"],
        0x00FB => ["scright", "Scroll display right by 4 pixels. (SCHIP)", "00FB"],
        0x00FC => ["scleft", "Scroll display left by 4 pixels. (SCHIP)", "00FC"],
        0x00FD => ["exit", "Halt program execution. (SCHIP)", "00FD"],
        0x00FE => ["low", "Switch to 64x32 resolution mode. (SCHIP)", "00FE"],
        0x00FF => ["high", "Switch to 128x64 resolution mode. (SCHIP)", "00FF"],
        _      => [$"ERROR 0x{op:X4}", "Unknown 0-group instruction", "0nnn"]
    };
}

private static string[]  Handle8(ushort op)
{
    int x = (op & 0x0F00) >> 8;
    int y = (op & 0x00F0) >> 4;
    int n = op & 0x000F;

    return n switch
    {
        0x0 => [$"ld   v{x}, v{y}", "Set register Vx to the value of register Vy.", "8xy0"],
        0x1 => [$"or   v{x}, v{y}", "Perform bitwise OR on Vx and Vy, store result in Vx.", "8xy1"],
        0x2 => [$"and  v{x}, v{y}", "Perform bitwise AND on Vx and Vy, store result in Vx.", "8xy2"],
        0x3 => [$"xor  v{x}, v{y}", "Perform bitwise XOR on Vx and Vy, store result in Vx.", "8xy3"],
        0x4 => [$"add  v{x}, v{y}", "Add Vy to Vx. Set VF=1 on carry, else 0.", "8xy4"],
        0x5 => [$"sub  v{x}, v{y}", "Subtract Vy from Vx. Set VF=1 if no borrow, else 0.", "8xy5"],
        0x6 => [$"shr  v{x}", "Shift Vx right by 1. Set VF to the least significant bit of Vx before shift.", "8xy6"],
        0x7 => [$"subn v{x}, v{y}", "Subtract Vx from Vy, store in Vx. Set VF=1 if no borrow, else 0.", "8xy7"],
        0xE => [$"shl  v{x}", "Shift Vx left by 1. Set VF to the most significant bit of Vx before shift.", "8xyE"],
        _   => [$"ERROR 0x{op:X4}", "Unknown arithmetic/logic op", "8xy?"]
    };
}

private static string[]  HandleD(ushort op)
{
    int x = (op & 0x0F00) >> 8;
    int y = (op & 0x00F0) >> 4;
    int n = op & 0x000F;
    return [$"drw  v{x}, v{y}, 0x{n:X1}", "Draw N-byte sprite from I at (Vx, Vy). VF=1 on collision.", "Dxyn"];
}

private static string[]  HandleE(ushort op)
{
    int x = (op & 0x0F00) >> 8;
    int nn = op & 0x00FF;
    return nn switch
    {
        0x9E => [$"skp  v{x}", "Skip next instruction if key with value of Vx is pressed.", "Ex9E"],
        0xA1 => [$"sknp v{x}", "Skip next instruction if key with value of Vx is not pressed.", "ExA1"],
        _    => [$"ERROR 0x{op:X4}", "Unknown key instruction", "Ex??"]
    };
}

private static string[]  HandleF(ushort op)
{
    var x  = (op & 0x0F00) >> 8;
    var nn = op & 0x00FF;

    return nn switch
    {
        0x07 =>  [$"ld   v{x}, DT", "Set Vx to the value of the delay timer.", "Fx07"],
        0x0A =>  [$"ld   v{x}, K", "Wait for a key press, store the value of the key in Vx.", "Fx0A"],
        0x15 =>  [$"ld   DT, v{x}", "Set the delay timer to the value of Vx.", "Fx15"],
        0x18 =>  [$"ld   ST, v{x}", "Set the sound timer to the value of Vx.", "Fx18"],
        0x1E =>  [$"add  I, v{x}", "Add the value of Vx to the index register I.", "Fx1E"],
        0x29 =>  [$"ld   F, v{x}", "Set I to location of 5-byte sprite for digit Vx.", "Fx29"],
        0x30 =>  [$"ld   HF, v{x}", "Set I to location of 10-byte sprite for digit Vx. (SCHIP)", "Fx30"],
        0x33 =>  [$"bcd  v{x}", "Store BCD representation of Vx in memory at I, I+1, I+2.", "Fx33"],
        0x55 =>  [$"ld   [I], v0-v{x}", "Store registers V0 through Vx in memory starting at location I.", "Fx55"],
        0x65 =>  [$"ld   v0-v{x}, [I]", "Load registers V0 through Vx from memory starting at location I.", "Fx65"],
        0x75 =>  [$"ld   R, v0-v{x}", "Save registers V0 through Vx to RPL user flags. (SCHIP)", "Fx75"],
        0x85 =>  [$"ld   v0-v{x}, R", "Load registers V0 through Vx from RPL user flags. (SCHIP)", "Fx85"],
        _    =>  [$"ERROR 0x{op:X4}", "Unknown F-group instruction", "Fx??"]
    };
}
    public static string[] Disassemble(ushort opcode)
    {
        var hi = (opcode & 0xF000) >> 12;
        return _dispatch[hi](opcode);
    }

}

