using System;
using System.Collections.Generic;

public static class Intel8080Disassembler
{
    /// <summary>
    /// Disassembles the instruction at pc.
    /// Returns (info, size) where info = [asm, description, encoding], and size ∈ {1,2,3}.
    /// </summary>
    public static (string[] info, int size) Decode(ReadOnlySpan<byte> code, int pc)
    {
        if ((uint)pc >= (uint)code.Length)
            return (new[] { "db   ?", "Out of bounds.", "??" }, 1);

        byte op = code[pc];
        return _dispatch[op](code, pc);
    }

    /// <summary>
    /// Cycles per opcode (byte -> cycles). For conditional CALL/RET the value is the
    /// minimum (not-taken) cycles: CALLcc = 11, RETcc = 5. Taken costs are 17 and 11.
    /// Conditional Jcc are always 10 on 8080. HLT = 7. Undocumented NOPs = 4.
    /// </summary>
    public static readonly IReadOnlyDictionary<byte, int> Cycles;

    // --------- Internal dispatch (one handler per opcode) --------------------
    private static readonly Func<ReadOnlySpan<byte>, int, (string[] info, int size)>[] _dispatch
        = new Func<ReadOnlySpan<byte>, int, (string[] info, int size)>[256];

    static Intel8080Disassembler()
    {
        // Default: DB xx
        for (int i = 0; i < 256; i++)
        {
            int local = i;
            _dispatch[i] = (code, pc) =>
            {
                byte b = (byte)local;
                return (new[] { $"db   0x{b:X2}", "Undefined/undocumented byte.", $"{b:X2}" }, 1);
            };
        }

        // Helpers
        string R(int n) => _regs[n & 7];
        string RP(int p) => _rp[p & 3];
        string RP2(int p) => _rp2[p & 3];

        // ---- 0x00..0x3F ----
        Install(0x00, (c, pc) => Ret("nop", "No operation.", "00", 1));
        foreach (var u in new byte[] { 0x08, 0x10, 0x18, 0x20, 0x28, 0x30, 0x38 })
            Install(u, (c, pc) => Ret("nop", "Undocumented NOP alias.", $"{u:X2}", 1));

        // Rotates/flag ops
        Install(0x07, (c, pc) => Ret("rlc", "Rotate A left (bit7→C).", "07", 1));
        Install(0x0F, (c, pc) => Ret("rrc", "Rotate A right (bit0→C).", "0F", 1));
        Install(0x17, (c, pc) => Ret("ral", "Rotate A left through carry.", "17", 1));
        Install(0x1F, (c, pc) => Ret("rar", "Rotate A right through carry.", "1F", 1));
        Install(0x27, (c, pc) => Ret("daa", "Decimal adjust accumulator.", "27", 1));
        Install(0x2F, (c, pc) => Ret("cma", "Complement A.", "2F", 1));
        Install(0x37, (c, pc) => Ret("stc", "Set carry.", "37", 1));
        Install(0x3F, (c, pc) => Ret("cmc", "Complement carry.", "3F", 1));

        // LXI rp,d16
        for (int p = 0; p < 4; p++)
        {
            int regPair = p;
            byte op = (byte)(0x01 | (regPair << 4));
            Install(op, (code, pc) =>
            {
                ushort d16 = Read16(code, pc + 1);
                return (new[] { $"lxi  {RP(regPair)}, 0x{d16:X4}", $"Load 16-bit immediate into {RP(regPair)}.", $"{op:X2} nn nn" }, 3);
            });
        }

        // STAX/LDAX (BC/DE only)
        Install(0x02, (c, pc) => Ret("stax b", "Store A into (BC).", "02", 1));
        Install(0x12, (c, pc) => Ret("stax d", "Store A into (DE).", "12", 1));
        Install(0x0A, (c, pc) => Ret("ldax b", "Load A from (BC).", "0A", 1));
        Install(0x1A, (c, pc) => Ret("ldax d", "Load A from (DE).", "1A", 1));

        // INX/DCX rp
        for (int p = 0; p < 4; p++)
        {
            int regPair = p;
            Install((byte)(0x03 | (regPair << 4)), (c, pc) => Ret($"inx  {RP(regPair)}", $"Increment {RP(regPair)}.", $"{(0x03 | (regPair << 4)):X2}", 1));
            Install((byte)(0x0B | (regPair << 4)), (c, pc) => Ret($"dcx  {RP(regPair)}", $"Decrement {RP(regPair)}.", $"{(0x0B | (regPair << 4)):X2}", 1));
        }

        // INR/DCR r
        for (int y = 0; y < 8; y++)
        {
            int reg = y;
            byte inr = (byte)(0x04 | (reg << 3));
            byte dcr = (byte)(0x05 | (reg << 3));
            Install(inr, (c, pc) => Ret($"inr  {R(reg)}", $"Increment {R(reg)}{(reg == 6 ? " (memory via HL)" : "")}.", $"{inr:X2}", 1));
            Install(dcr, (c, pc) => Ret($"dcr  {R(reg)}", $"Decrement {R(reg)}{(reg == 6 ? " (memory via HL)" : "")}.", $"{dcr:X2}", 1));
        }

        // MVI r,d8
        for (int y = 0; y < 8; y++)
        {
            int reg = y;
            byte op = (byte)(0x06 | (reg << 3));
            Install(op, (code, pc) =>
            {
                byte d8 = SafeRead8(code, pc + 1);
                return (new[] { $"mvi  {R(reg)}, 0x{d8:X2}", $"Move immediate into {R(reg)}{(reg == 6 ? " (memory via HL)" : "")}.", $"{op:X2} nn" }, 2);
            });
        }

        // DAD rp
        for (int p = 0; p < 4; p++)
        {
            int regPair = p;
            byte op = (byte)(0x09 | (regPair << 4));
            Install(op, (c, pc) => Ret($"dad  {RP(regPair)}", $"HL ← HL + {RP(regPair)}.", $"{op:X2}", 1));
        }

        // Direct 16-bit mem ops
        InstallImm16(0x22, "shld", "Store HL to direct address.");
        InstallImm16(0x2A, "lhld", "Load HL from direct address.");
        InstallImm16(0x32, "sta",  "Store A to direct address.");
        InstallImm16(0x3A, "lda",  "Load A from direct address.");

        // ---- 0x40..0x7F MOV table (+ HLT at 0x76) ----
        for (int d = 0; d < 8; d++)
        for (int s = 0; s < 8; s++)
        {
            byte op = (byte)(0x40 | (d << 3) | s);
            if (op == 0x76) continue; // HLT handled below
            
            int destReg = d;
            int sourceReg = s;

            Install(op, (c, pc) =>
            {
                return (new[]
                {
                    $"mov  {R(destReg)}, {R(sourceReg)}",
                    $"Copy {(sourceReg == 6 ? "memory via HL" : $"register {R(sourceReg)}")} to {(destReg == 6 ? "memory via HL" : $"register {R(destReg)}")}.",
                    $"{op:X2}"
                }, 1);
            });
        }
        Install(0x76, (c, pc) => Ret("hlt", "Halt until interrupt or reset.", "76", 1));

        // ---- 0x80..0xBF ALU ops (register/memory) ----
        string[] alu = { "add", "adc", "sub", "sbb", "ana", "xra", "ora", "cmp" };
        for (int g = 0; g < 8; g++)
        for (int s = 0; s < 8; s++)
        {
            int aluGroup = g;
            int sourceReg = s;
            byte op = (byte)(0x80 | (aluGroup << 3) | sourceReg);
            Install(op, (c, pc) =>
            {
                return (new[]
                {
                    $"{alu[aluGroup]} {R(sourceReg)}",
                    $"{alu[aluGroup].ToUpper()} A with {(sourceReg == 6 ? "memory via HL" : $"register {R(sourceReg)}")}.",
                    $"{op:X2}"
                }, 1);
            });
        }

        // ---- Immediate ALU ----
        InstallImm8(0xC6, "adi", "ADD immediate to A.");
        InstallImm8(0xCE, "aci", "ADD immediate + carry to A.");
        InstallImm8(0xD6, "sui", "SUB immediate from A.");
        InstallImm8(0xDE, "sbi", "SUB immediate + borrow from A.");
        InstallImm8(0xE6, "ani", "AND immediate with A.");
        InstallImm8(0xEE, "xri", "XOR immediate with A.");
        InstallImm8(0xF6, "ori", "OR immediate with A.");
        InstallImm8(0xFE, "cpi", "Compare immediate with A.");

        // ---- Stack & misc register pair ops ----
        for (int p = 0; p < 4; p++)
        {
            int regPair = p;
            byte pop = (byte)(0xC1 | (regPair << 4));
            byte push = (byte)(0xC5 | (regPair << 4));
            Install(pop,  (c, pc) => Ret($"pop  {RP2(regPair)}", $"Pop into {RP2(regPair)}.", $"{pop:X2}", 1));
            Install(push, (c, pc) => Ret($"push {RP2(regPair)}", $"Push {RP2(regPair)} onto stack.", $"{push:X2}", 1));
        }

        Install(0xDB, (code, pc) =>
        {
            byte port = SafeRead8(code, pc + 1);
            return (new[] { $"in   0x{port:X2}", "Input from port to A.", "DB nn" }, 2);
        });
        Install(0xD3, (code, pc) =>
        {
            byte port = SafeRead8(code, pc + 1);
            return (new[] { $"out  0x{port:X2}", "Output A to port.", "D3 nn" }, 2);
        });

        Install(0xE3, (c, pc) => Ret("xthl", "Exchange HL with (SP).", "E3", 1));
        Install(0xEB, (c, pc) => Ret("xchg", "Exchange HL and DE.", "EB", 1));
        Install(0xE9, (c, pc) => Ret("pchl", "PC ← HL (jump via register).", "E9", 1));
        Install(0xF9, (c, pc) => Ret("sphl", "SP ← HL.", "F9", 1));

        Install(0xF3, (c, pc) => Ret("di", "Disable interrupts.", "F3", 1));
        Install(0xFB, (c, pc) => Ret("ei", "Enable interrupts.", "FB", 1));

        // ---- Jumps / Calls / Returns / RST ----
        // Unconditional
        InstallJmp(0xC3, "jmp");
        InstallCall(0xCD, "call");
        Install(0xC9, (c, pc) => Ret("ret", "Return.", "C9", 1));

        // Conditional Jcc: JNZ,JZ,JNC,JC,JPO,JPE,JP,JM
        InstallJcc(0xC2, "jnz", "Not Zero (Z=0)");
        InstallJcc(0xCA, "jz",  "Zero (Z=1)");
        InstallJcc(0xD2, "jnc", "Not Carry (CY=0)");
        InstallJcc(0xDA, "jc",  "Carry (CY=1)");
        InstallJcc(0xE2, "jpo", "Parity Odd (P=0)");
        InstallJcc(0xEA, "jpe", "Parity Even (P=1)");
        InstallJcc(0xF2, "jp",  "Plus (S=0)");
        InstallJcc(0xFA, "jm",  "Minus (S=1)");

        // Conditional CALL: CNZ,CZ,CNC,CC,CPO,CPE,CP,CM
        InstallCallCc(0xC4, "cnz", "Not Zero (Z=0)");
        InstallCallCc(0xCC, "cz",  "Zero (Z=1)");
        InstallCallCc(0xD4, "cnc", "Not Carry (CY=0)");
        InstallCallCc(0xDC, "cc",  "Carry (CY=1)");
        InstallCallCc(0xE4, "cpo", "Parity Odd (P=0)");
        InstallCallCc(0xEC, "cpe", "Parity Even (P=1)");
        InstallCallCc(0xF4, "cp",  "Plus (S=0)");
        InstallCallCc(0xFC, "cm",  "Minus (S=1)");

        // Conditional RET: RNZ,RZ,RNC,RC,RPO,RPE,RP,RM
        Install(0xC0, (c, pc) => Ret("rnz", "Return if Not Zero (Z=0).", "C0", 1));
        Install(0xC8, (c, pc) => Ret("rz",  "Return if Zero (Z=1).",      "C8", 1));
        Install(0xD0, (c, pc) => Ret("rnc", "Return if Not Carry (CY=0).", "D0", 1));
        Install(0xD8, (c, pc) => Ret("rc",  "Return if Carry (CY=1).",     "D8", 1));
        Install(0xE0, (c, pc) => Ret("rpo", "Return if Parity Odd (P=0).", "E0", 1));
        Install(0xE8, (c, pc) => Ret("rpe", "Return if Parity Even (P=1).", "E8", 1));
        Install(0xF0, (c, pc) => Ret("rp",  "Return if Plus (S=0).",       "F0", 1));
        Install(0xF8, (c, pc) => Ret("rm",  "Return if Minus (S=1).",      "F8", 1));

        // RST n
        for (int n = 0; n < 8; n++)
        {
            int vector = n;
            byte op = (byte)(0xC7 | (vector << 3));
            Install(op, (c, pc) => Ret($"rst  {vector}", $"Restart to vector 0x{vector * 8:X2}.", $"{op:X2}", 1));
        }

        // Undocumented alternates used by arcade dumps
        InstallJmp(0xCB, "jmp");                     // CB a16
        Install(0xD9, (c, pc) => Ret("ret", "Return (undocumented alias).", "D9", 1));
        foreach (var altCall in new byte[] { 0xDD, 0xED, 0xFD })
            InstallCall(altCall, "call");            // DD/ED/FD a16

        // ---- Build cycles table (byte -> cycles) ----
        var cycles = new int[256];
        // Default unknown/DB: 4
        for (int i = 0; i < 256; i++) cycles[i] = 4;

        // NOP/undoc NOPs
        cycles[0x00] = 4; foreach (var u in new byte[] { 0x08, 0x10, 0x18, 0x20, 0x28, 0x30, 0x38 }) cycles[u] = 4;

        // Rotates/flags
        cycles[0x07] = cycles[0x0F] = cycles[0x17] = cycles[0x1F] = 4;
        cycles[0x27] = cycles[0x2F] = cycles[0x37] = cycles[0x3F] = 4;

        // LXI rp
        for (int p = 0; p < 4; p++) cycles[0x01 | (p << 4)] = 10;

        // STAX/LDAX
        cycles[0x02] = cycles[0x12] = cycles[0x0A] = cycles[0x1A] = 7;

        // INX/DCX
        for (int p = 0; p < 4; p++) { cycles[0x03 | (p << 4)] = 5; cycles[0x0B | (p << 4)] = 5; }

        // INR/DCR r (M variants slower)
        for (int y = 0; y < 8; y++)
        {
            int inr = 0x04 | (y << 3);
            int dcr = 0x05 | (y << 3);
            cycles[inr] = (y == 6) ? 10 : 5;
            cycles[dcr] = (y == 6) ? 10 : 5;
        }

        // MVI r,d8
        for (int y = 0; y < 8; y++) cycles[0x06 | (y << 3)] = (y == 6) ? 10 : 7;

        // DAD rp
        for (int p = 0; p < 4; p++) cycles[0x09 | (p << 4)] = 10;

        // Direct mem ops
        cycles[0x22] = 16; cycles[0x2A] = 16; cycles[0x32] = 13; cycles[0x3A] = 13;

        // MOV table (M involvement = 7 else 5), except HLT
        for (int d = 0; d < 8; d++)
        for (int s = 0; s < 8; s++)
        {
            int op = 0x40 | (d << 3) | s;
            if (op == 0x76) continue; // HLT
            cycles[op] = (d == 6 || s == 6) ? 7 : 5;
        }
        // HLT
        cycles[0x76] = 7;

        // ALU reg/mem
        for (int g = 0; g < 8; g++)
        for (int s = 0; s < 8; s++)
        {
            int op = 0x80 | (g << 3) | s;
            cycles[op] = (s == 6) ? 7 : 4;
        }
        // Immediate ALU
        foreach (var op in new byte[] { 0xC6, 0xCE, 0xD6, 0xDE, 0xE6, 0xEE, 0xF6, 0xFE }) cycles[op] = 7;

        // Stack & misc
        for (int p = 0; p < 4; p++) { cycles[0xC1 | (p << 4)] = 10; cycles[0xC5 | (p << 4)] = 11; }
        cycles[0xDB] = 10; cycles[0xD3] = 10;
        cycles[0xE3] = 18; cycles[0xEB] = 5; cycles[0xE9] = 5; cycles[0xF9] = 5;
        cycles[0xF3] = 4;  cycles[0xFB] = 4;

        // Jumps/Calls/Returns/RST
        cycles[0xC3] = 10; cycles[0xCB] = 10;              // JMP, undoc JMP
        foreach (var jcc in new byte[] { 0xC2, 0xCA, 0xD2, 0xDA, 0xE2, 0xEA, 0xF2, 0xFA }) cycles[jcc] = 10;

        cycles[0xCD] = 17; foreach (var ac in new byte[] { 0xDD, 0xED, 0xFD }) cycles[ac] = 17; // CALL, undoc CALL
        foreach (var ccc in new byte[] { 0xC4, 0xCC, 0xD4, 0xDC, 0xE4, 0xEC, 0xF4, 0xFC }) cycles[ccc] = 11; // min

        cycles[0xC9] = 10; cycles[0xD9] = 10; // RET + undoc RET
        foreach (var rcc in new byte[] { 0xC0, 0xC8, 0xD0, 0xD8, 0xE0, 0xE8, 0xF0, 0xF8 }) cycles[rcc] = 5; // min

        for (int n = 0; n < 8; n++) cycles[0xC7 | (n << 3)] = 11; // RST n

        // Freeze as read-only
        var dict = new Dictionary<byte, int>(256);
        for (int i = 0; i < 256; i++) dict[(byte)i] = cycles[i];
        Cycles = dict;
    }

    // ------------------------ helpers ------------------------

    private static void Install(byte opcode, Func<ReadOnlySpan<byte>, int, (string[] info, int size)> f)
        => _dispatch[opcode] = f;

    private static void InstallImm16(byte opcode, string mnem, string desc)
    {
        Install(opcode, (code, pc) =>
        {
            ushort a16 = Read16(code, pc + 1);
            return (new[] { $"{mnem}  0x{a16:X4}", desc, $"{opcode:X2} nn nn" }, 3);
        });
    }

    private static void InstallImm8(byte opcode, string mnem, string desc)
    {
        Install(opcode, (code, pc) =>
        {
            byte d8 = SafeRead8(code, pc + 1);
            return (new[] { $"{mnem}  0x{d8:X2}", desc, $"{opcode:X2} nn" }, 2);
        });
    }

    private static void InstallJmp(byte opcode, string mnem)
    {
        Install(opcode, (code, pc) =>
        {
            ushort a16 = Read16(code, pc + 1);
            return (new[] { $"{mnem}  0x{a16:X4}", "Unconditional jump.", $"{opcode:X2} nn nn" }, 3);
        });
    }

    private static void InstallCall(byte opcode, string mnem)
    {
        Install(opcode, (code, pc) =>
        {
            ushort a16 = Read16(code, pc + 1);
            return (new[] { $"{mnem}  0x{a16:X4}", "Unconditional subroutine call.", $"{opcode:X2} nn nn" }, 3);
        });
    }

    private static void InstallJcc(byte opcode, string mnem, string condDesc)
    {
        Install(opcode, (code, pc) =>
        {
            ushort a16 = Read16(code, pc + 1);
            return (new[] { $"{mnem}  0x{a16:X4}", $"Jump if {condDesc}.", $"{opcode:X2} nn nn" }, 3);
        });
    }

    private static void InstallCallCc(byte opcode, string mnem, string condDesc)
    {
        Install(opcode, (code, pc) =>
        {
            ushort a16 = Read16(code, pc + 1);
            return (new[] { $"{mnem}  0x{a16:X4}", $"Call if {condDesc}.", $"{opcode:X2} nn nn" }, 3);
        });
    }

    private static (string[] info, int size) Ret(string asm, string desc, string enc, int size)
        => ([asm, desc, enc], size);

    private static ushort Read16(ReadOnlySpan<byte> code, int index)
    {
        byte lo = SafeRead8(code, index);
        byte hi = SafeRead8(code, index + 1);
        return (ushort)(lo | (hi << 8));
    }
    private static byte SafeRead8(ReadOnlySpan<byte> code, int index)
        => (uint)index < (uint)code.Length ? code[index] : (byte)0x00;

    // The order corresponds to the 3-bit encoding: 000=B, 001=C, ..., 110=M, 111=A
    private static readonly string[] _regs = { "b", "c", "d", "e", "h", "l", "m", "a" };
    
    // The order corresponds to the 2-bit encoding: 00=BC, 01=DE, 10=HL, 11=SP
    private static readonly string[] _rp   = { "b", "d", "h", "sp" };
    
    // The order corresponds to the 2-bit encoding: 00=BC, 01=DE, 10=HL, 11=PSW(AF)
    private static readonly string[] _rp2  = { "b", "d", "h", "psw" };
}
