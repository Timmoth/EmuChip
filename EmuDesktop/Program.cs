using EmuChip;

var romBytes = File.ReadAllBytes("./invaders.h");
var pc = 0;
while (pc < romBytes.Length)
{
    var (info, size) = Intel8080Disassembler.Decode(romBytes, pc);
    Console.WriteLine($"{pc:X}\t0x{romBytes[pc]:X}\t{info[0]}\t{info[1]}\t{info[2]}");
    pc += size;
}

var romFilename = "benchmark.ch8";
if (args.Length > 0)
    romFilename = args[0];
else
    Console.WriteLine("No arguments provided.");

var process = new Chip8Process(romFilename);
process.Run();