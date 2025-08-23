# EmuChip – CHIP-8 WebAssembly Emulator

**EmuChip** is a browser-based CHIP-8 emulator written in **C#** and compiled to **WebAssembly (WASM)**.  
It runs classic CHIP-8 games directly in your web browser with **fast rendering**, **sound**, and **keyboard controls**.

[Live Demo](https://emuchip.com)

## About CHIP-8

CHIP-8 is a lightweight interpreted virtual machine designed for 8-bit microcomputers.  
It features:
- 4K memory
- 16 8-bit registers
- 64×32 monochrome display
- Stack for subroutines
- Delay and sound timers

This emulator replicates the CHIP-8 hardware in software, allowing retro games and demos to run in a modern browser.

## Features

- Play classic CHIP-8 games like **Pong**, **Tetris**, **Space Invaders**, and more.
- Optimized canvas rendering for smooth graphics.
- Built-in **keyboard input mapping**: 

```
1 2 3 4   →  1 2 3 C
Q W E R   →  4 5 6 D
A S D F   →  7 8 9 E
Z X C V   →  A 0 B F
```

- Audio support with smooth beeps.
- ROM selector for easy switching between games.


## ROMs Included

- Maze
- Space Invaders
- Sierpinski Triangle
- Tetris
- Pong
- Particle Demo
- Trip8 Demo
- Kaleidoscope

## Development

The emulator is written in C# and compiled to WebAssembly using [dotnet-wasm](https://github.com/dotnet/wasm).  
To modify or add features:

```bash
git clone https://github.com/Timmoth/EmuChip.git
cd EmuChip
dotnet build
