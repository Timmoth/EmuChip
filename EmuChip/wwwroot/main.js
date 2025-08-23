import { dotnet } from './_framework/dotnet.js'

const roms =[
    {
        "name": "Maze",
        "filename": "Maze [David Winter, 199x].ch8",
        "description": "Generates and displays random mazes.",
        "controls": "No controls required; the maze draws automatically."
    },
    {
        "name": "Space Invaders",
        "filename": "Space Invaders [David Winter, 1997].ch8",
        "description": "Shoot the advancing alien invaders before they reach the ground.",
        "controls": "Use Q to move left, E to move right, and W to shoot."
    },
    {
        "name": "Sierpinski Triangle",
        "filename": "Sierpinski [Sergey Naydenov, 2010].ch8",
        "description": "Demo program that draws a fractal Sierpinski triangle.",
        "controls": "No controls required; the triangle draws automatically."
    },
    {
        "name": "Tetris",
        "filename": "Tetris [Fran Dachille, 1991].ch8",
        "description": "Classic falling block puzzle game adapted for Chip-8.",
        "controls": "Use Q to rotate, W to move left, E to move right, and A to drop faster."
    },
    {
        "name": "Pong",
        "filename": "Pong [Paul Vervalin, 1990].ch8",
        "description": "Two-player paddle and ball classic Pong.",
        "controls": "Player 1: 1 (up) and Q (down). Player 2: 4 (up) and R (down)."
    },
    {
        "name": "Particle Demo",
        "filename": "Particle Demo [zeroZshadow, 2008].ch8",
        "description": "Animated particle effect demo, used for benchmarking.",
        "controls": "No controls; particles animate automatically."
    },
    {
        "name": "Trip8 Demo",
        "filename": "Trip8 Demo (2008) [Revival Studios].ch8",
        "description": "Scrolling text and graphical effects demo.",
        "controls": "No controls; the demo plays automatically."
    },
    {
        "name": "Kaleidoscope",
        "filename": "Kaleidoscope [Joseph Weisbecker, 1978].ch8",
        "description": "Colorful kaleidoscopic pattern rendering demo.",
        "controls": "Use 2 (up), S (down), Q (left), and E (right). Press X to reset/replay."
    }
]


// Constants
let ROM_PATH = `./roms/${roms[0].filename}`;
const CPU_HZ = 700;
const TIMER_HZ = 60;
const CYCLES_PER_TIMER_TICK = Math.round(CPU_HZ / TIMER_HZ);
const ON_COLOR = '#c0ffee';
const OFF_COLOR = '#000000';

// .NET WASM Setup
const { getAssemblyExports, getConfig } = await dotnet
    .withDiagnosticTracing(false)
    .create();

const config = getConfig();
const exports = await getAssemblyExports(config.mainAssemblyName);
const chip8 = exports.Chip8EmulatorApi;

// HTML Canvas Setup
const canvas = document.getElementById('display');
const ctx = canvas.getContext('2d');
const imageData = ctx.createImageData(64, 32);
const pixelBuffer32 = new Uint32Array(imageData.data.buffer);

// Color Parsing
const onColorInt = parseInt(ON_COLOR.substring(1), 16);
const offColorInt = parseInt(OFF_COLOR.substring(1), 16);
const onColor32 = (0xFF << 24) | ((onColorInt & 0x0000FF) << 16) | (onColorInt & 0x00FF00) | ((onColorInt & 0xFF0000) >> 16);
const offColor32 = (0xFF << 24) | ((offColorInt & 0x0000FF) << 16) | (offColorInt & 0x00FF00) | ((offColorInt & 0xFF0000) >> 16);

// Input Handling
const keys = new Uint8Array(16);
const keyMap = {
    '1': 0x1, '2': 0x2, '3': 0x3, '4': 0xC,
    'q': 0x4, 'w': 0x5, 'e': 0x6, 'r': 0xD,
    'a': 0x7, 's': 0x8, 'd': 0x9, 'f': 0xE,
    'z': 0xA, 'x': 0x0, 'c': 0xB, 'v': 0xF,
};
window.addEventListener('keydown', (e) => {
    const key = e.key.toLowerCase();
    if (keyMap[key] !== undefined) keys[keyMap[key]] = 1;
});
window.addEventListener('keyup', (e) => {
    const key = e.key.toLowerCase();
    if (keyMap[key] !== undefined) keys[keyMap[key]] = 0;
});
function pollInput() { chip8.SetKeys(keys); }

// Audio
let audioContext;
function beep() {
    if (!audioContext) audioContext = new (window.AudioContext || window.webkitAudioContext)();
    const oscillator = audioContext.createOscillator();
    const gain = audioContext.createGain();
    oscillator.connect(gain); gain.connect(audioContext.destination);
    oscillator.type = 'sine'; oscillator.frequency.value = 440;
    const now = audioContext.currentTime;
    gain.gain.setValueAtTime(0, now);
    gain.gain.linearRampToValueAtTime(0.1, now + 0.02);
    gain.gain.linearRampToValueAtTime(0.1, now + 0.15);
    gain.gain.linearRampToValueAtTime(0, now + 0.3);
    oscillator.start(now); oscillator.stop(now + 0.3);
}

// Main Game Loop
let lastTime = 0, cpuAccumulator = 0, timerAccumulator = 0;
const cpuInterval = 1 / CPU_HZ;
const timerInterval = 1 / TIMER_HZ;
const maxDeltaTime = 1 / 15;
let debugTimer = 0, frameCount = 0, cycleCount = 0, timerTickCount = 0, renderCount = 0;

function gameLoop(currentTime) {
    requestAnimationFrame(gameLoop);
    const deltaTime = (currentTime - lastTime) / 1000;
    lastTime = currentTime;

    debugTimer += deltaTime; frameCount++;
    if (debugTimer >= 1.0) {
        console.log(`FPS: ${frameCount} | CPU: ${cycleCount} | Timers: ${timerTickCount} | Renders: ${renderCount}`);
        debugTimer -= 1.0; frameCount = cycleCount = timerTickCount = renderCount = 0;
    }

    const effectiveDeltaTime = Math.min(deltaTime, maxDeltaTime);
    cpuAccumulator += effectiveDeltaTime; timerAccumulator += effectiveDeltaTime;

    if (timerAccumulator >= timerInterval) {
        chip8.UpdateTimers(); timerTickCount++; timerAccumulator -= timerInterval;
    }
    while (cpuAccumulator >= cpuInterval) {
        pollInput(); chip8.Step(); cycleCount++; cpuAccumulator -= cpuInterval;
    }

    const graphics = chip8.GetGraphics();
    if (graphics) render(graphics);
    if (chip8.ShouldBeep()) beep();
}

function render(graphics) {
    for (let i = 0; i < graphics.length; i++) {
        pixelBuffer32[i] = graphics[i] ? onColor32 : offColor32;
    }
    ctx.putImageData(imageData, 0, 0);
}

// Initialization
async function start(romFilename) {
    try {
        const response = await fetch(`./roms/${romFilename}`);
        if (!response.ok) throw new Error(`HTTP error! status: ${response.status}`);
        const romBytes = new Uint8Array(await response.arrayBuffer());
        chip8.Initialize(romBytes);

        console.log(`Loaded ROM: ${romFilename}`);
        requestAnimationFrame((time) => { lastTime = time; requestAnimationFrame(gameLoop); });
    } catch (error) {
        console.error("Failed to load and start emulator:", error);
        document.body.innerHTML = `<h1 style="color: red;">Error loading ${romFilename}</h1>`;
    }
}

// ROM Selector Setup
const romSelector = document.getElementById("rom-selector");
const romDescription = document.getElementById("rom-description");
const romControls = document.getElementById("rom-controls");

roms.forEach((rom, i) => {
    const option = document.createElement("option");
    option.value = rom.filename;
    option.textContent = rom.name;
    romSelector.appendChild(option);
});
romSelector.addEventListener("change", (e) => {
    const selectedRom = roms.find(r => r.filename === e.target.value);
    romDescription.textContent = selectedRom.description;
    romControls.textContent = selectedRom.controls;
    start(selectedRom.filename);
});

// Initialize with first ROM
romSelector.value = roms[0].filename;
romDescription.textContent = roms[0].description;
start(roms[0].filename);
