import { dotnet } from './_framework/dotnet.js'


const romsRespone = await fetch('./roms.json');
const roms = await romsRespone.json();

// Constants
let ROM_PATH = roms[0].filename;
let CPU_HZ = 1000;
const TIMER_HZ = 60;
const ON_COLOR = { r: 192, g: 255, b: 238 }; // #c0ffee
const OFF_COLOR = { r: 0, g: 0, b: 0 };     // #000000
const PHOSPHOR_DECAY_RATE = 0.80; // Lower = faster fade, Higher = longer trail

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
let imageData = ctx.createImageData(64, 32);
let pixelBuffer32 = new Uint32Array(imageData.data.buffer);
// This buffer will store the brightness of each pixel (0.0 to 1.0)
let phosphorBuffer = new Float32Array(64 * 32);
let currentWidth = 0, currentHeight = 0;

// Input Handling
const keys = new Uint8Array(16);
const keyMap = {
    '1': 0x1, '2': 0x2, '3': 0x3, '4': 0xC,
    'q': 0x4, 'w': 0x5, 'e': 0x6, 'r': 0xD,
    'a': 0x7, 's': 0x8, 'd': 0x9, 'f': 0xE,
    'z': 0xA, 'x': 0x0, 'c': 0xB, 'v': 0xF,
};

let keyChanged = false;
window.addEventListener('keydown', (e) => {
    const key = e.key.toLowerCase();
    if (keyMap[key] !== undefined) keys[keyMap[key]] = 1;
    keyChanged = true;
});
window.addEventListener('keyup', (e) => {
    const key = e.key.toLowerCase();
    if (keyMap[key] !== undefined) keys[keyMap[key]] = 0;
    keyChanged = true;
});
function pollInput() { chip8.SetKeys(keys); }

// Audio
let audioContext;

let isBeeping = false;
let oscillator = null;
function SetupAudio() {
    if (!audioContext) audioContext = new (window.AudioContext || window.webkitAudioContext)();

}

// Main Game Loop
let lastTime = 0, cpuAccumulator = 0, timerAccumulator = 0;
let cpuInterval = 1 / CPU_HZ;
const timerInterval = 1 / TIMER_HZ;
const maxDeltaTime = 1 / 15;
let debugTimer = 0, frameCount = 0, cycleCount = 0, timerTickCount = 0, renderCount = 0;

function gameLoop(currentTime) {
    requestAnimationFrame(gameLoop);
    const deltaTime = (currentTime - lastTime) / 1000;
    lastTime = currentTime;

    // Timer Updates
    timerAccumulator += deltaTime;
    if (timerAccumulator >= timerInterval) {
        chip8.UpdateTimers();
        timerAccumulator -= timerInterval;
    }

    // Add the time elapsed to our CPU cycle accumulator.
    cpuAccumulator += deltaTime;

    // Define a 'deadline' for this frame's CPU work to ensure smooth rendering.
    const maxExecutionTimeMs = 8;
    const loopStartTime = performance.now();

    while (cpuAccumulator >= cpuInterval) {
        if(keyChanged){
            pollInput();
            keyChanged = false;
        }

        chip8.Step(); 
        cycleCount++; 
        cpuAccumulator -= cpuInterval;
        // Bail out of the loop if we're taking too long.
        if (performance.now() - loopStartTime > maxExecutionTimeMs) {
            break; 
        }
    }

    // Check if the emulator's internal screen state has changed.
    const graphics = chip8.GetGraphics();
    if (graphics) {
        // If it has, update our brightness buffer and increment render count for debug.
        updatePhosphorBuffer(graphics);
        renderCount++;
    }

    // Always render the display on every frame to show the decay effect.
    renderDisplay();

    if (chip8.ShouldBeep()) {
        if (!isBeeping) {
            oscillator = audioContext.createOscillator();
            const gain = audioContext.createGain();
            oscillator.connect(gain); gain.connect(audioContext.destination);
            oscillator.type = 'sine'; oscillator.frequency.value = 440;
            const now = audioContext.currentTime;
            gain.gain.setValueAtTime(0, now);
            gain.gain.linearRampToValueAtTime(0.1, now + 0.02);
            gain.gain.linearRampToValueAtTime(0.1, now + 0.15);
            gain.gain.linearRampToValueAtTime(0, now + 0.3);
            oscillator.start(audioContext.currentTime);
            isBeeping = true;
        }
    } else {
        if (isBeeping) {
            oscillator.stop(audioContext.currentTime);
            isBeeping = false;
        }
    }

    debugTimer += deltaTime; frameCount++;
    if (debugTimer >= 1.0) {
        console.log(`FPS: ${frameCount} | CPU: ${cycleCount} | Timers: ${timerTickCount} | Renders: ${renderCount}`);
        debugTimer -= 1.0; frameCount = cycleCount = timerTickCount = renderCount = 0;
    }
}

// This function is now only called when the emulator's state changes.
function updatePhosphorBuffer(graphics) {
    const width = chip8.GetWidth();
    const height = chip8.GetHeight();

    if (width !== currentWidth || height !== currentHeight) {
        canvas.width = width;
        canvas.height = height;
        const aspectRatio = width / height;
        canvas.style.width = `${640}px`;
        canvas.style.height = `${640 / aspectRatio}px`;

        imageData = ctx.createImageData(width, height);
        pixelBuffer32 = new Uint32Array(imageData.data.buffer);
        phosphorBuffer = new Float32Array(width * height);
        currentWidth = width;
        currentHeight = height;
        console.log(`size changed ${width}x${height}`);
    }

    // The C# buffer always has a memory width (stride) of 128
    const sourceStride = 128;

    // Use nested loops to correctly map pixels
    for (let y = 0; y < height; y++) {
        for (let x = 0; x < width; x++) {
            // Index in the source buffer from C#
            const sourceIndex = (y * sourceStride) + x;

            // Index in the destination phosphor buffer (which is packed)
            const destIndex = (y * width) + x;

            if (graphics[sourceIndex]) {
                phosphorBuffer[destIndex] = 1.0;
            }
        }
    }
}


// This function now runs on every single animation frame.
function renderDisplay() {
    if (!phosphorBuffer) return; // Don't render if not initialized

    for (let i = 0; i < phosphorBuffer.length; i++) {
        const brightness = phosphorBuffer[i];
        const r = OFF_COLOR.r + (ON_COLOR.r - OFF_COLOR.r) * brightness;
        const g = OFF_COLOR.g + (ON_COLOR.g - OFF_COLOR.g) * brightness;
        const b = OFF_COLOR.b + (ON_COLOR.b - OFF_COLOR.b) * brightness;
        pixelBuffer32[i] = (255 << 24) | (b << 16) | (g << 8) | r;
        phosphorBuffer[i] *= PHOSPHOR_DECAY_RATE;
    }
    ctx.putImageData(imageData, 0, 0);
}

// Initialization
async function start(romFilename) {
    try {
        const response = await fetch(romFilename);
        if (!response.ok) throw new Error(`HTTP error! status: ${response.status}`);
        const romBytes = new Uint8Array(await response.arrayBuffer());
        chip8.Initialize(romBytes);

        console.log(`Loaded ROM: ${romFilename}`);
        // Only start the loop if it's the first time.
        if (lastTime === 0) {
            requestAnimationFrame((time) => { lastTime = time; requestAnimationFrame(gameLoop); });
        }
    } catch (error) {
        console.error("Failed to load and start emulator:", error);
        document.body.innerHTML = `<h1 style="color: red;">Error loading ${romFilename}</h1>`;
    }
}

// ROM Selector Setup
const romSelector = document.getElementById("rom-selector");
const romDescription = document.getElementById("rom-description");
const romControls = document.getElementById("rom-controls");
const clockSelector = document.getElementById("clock-selector");
const disassembleBtn = document.getElementById("disassemble-btn");
const romUpload = document.getElementById("rom-upload");

let selectedRomName = null;
if (romSelector) {
    roms.forEach((rom) => {
        const option = document.createElement("option");
        option.value = rom.filename;
        option.textContent = rom.name;
        romSelector.appendChild(option);
    });
    romSelector.addEventListener("change", (e) => {
        const selectedRom = roms.find(r => r.filename === e.target.value);
        selectedRomName = selectedRom.name;
        if (selectedRom) {
            romDescription.textContent = selectedRom.description;
            romControls.textContent = selectedRom.controls;
            start(selectedRom.filename);
        }
    });
    clockSelector.addEventListener("change", (e) => {
        CPU_HZ = e.target.value
        cpuInterval = 1 / CPU_HZ;
    })

    disassembleBtn.addEventListener("click", (e) => {
        window.location.href = `disassemble.html?rom=${encodeURIComponent(selectedRomName)}`;
    })
    // Initialize with first ROM
    const initialRom = roms[0];
    selectedRomName = initialRom.name;
    romSelector.value = initialRom.filename;
    romDescription.textContent = initialRom.description;
    romControls.textContent = initialRom.controls;
    start(initialRom.filename);
} else {
    // Fallback if the ROM selector isn't present in the HTML
    start(ROM_PATH);
}

  // Handle file upload
  romUpload.addEventListener("change", async (e) => {
    const file = e.target.files[0];
    if (!file) return;

    document.getElementById("rom-description").textContent = `Uploaded: ${file.name}`;
    const buffer = await file.arrayBuffer();
    const romBytes = new Uint8Array(buffer);
    chip8.Initialize(romBytes);

    console.log(`Loaded ROM: ${romFilename}`);
    // Only start the loop if it's the first time.
    if (lastTime === 0) {
        requestAnimationFrame((time) => { lastTime = time; requestAnimationFrame(gameLoop); });
    }

  });


SetupAudio();
