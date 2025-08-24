using EmuChip;

var romFilename = "Space Invaders [David Winter, 1997].ch8";
if (args.Length > 0)
    romFilename = args[0];
else
    Console.WriteLine("No arguments provided.");

var process = new Chip8Process(romFilename);
process.Run();