using System;

namespace EmuChip;

public class Chip8Emulator
{
    // small (5-byte) font: 16 glyphs * 5 bytes = 80 bytes
    private const int SmallFontBase = 0x0000;

    // big SCHIP font: 16 glyphs * 10 bytes = 160 bytes (8x10 glyphs)
    private const int BigFontBase = SmallFontBase + 16 * 5; // 80
    private const int BigFontGlyphSize = 10;
    private readonly Action<ushort>[] _dispatch;
    private readonly byte[] _memory = new byte[4096];
    private readonly Random _random = new();

    private readonly byte[] _schipSprites =
    {
        0x7C, 0x82, 0x82, 0x82, 0x82, 0x82, 0x82, 0x82, 0x7C, 0x00,
        0x08, 0x18, 0x38, 0x08, 0x08, 0x08, 0x08, 0x08, 0x3C, 0x00,
        0x7C, 0x82, 0x02, 0x02, 0x04, 0x18, 0x20, 0x40, 0xFE, 0x00,
        0x7C, 0x82, 0x02, 0x02, 0x3C, 0x02, 0x02, 0x82, 0x7C, 0x00,
        0x84, 0x84, 0x84, 0x84, 0xFE, 0x04, 0x04, 0x04, 0x04, 0x00,
        0xFE, 0x80, 0x80, 0x80, 0xFC, 0x02, 0x02, 0x82, 0x7C, 0x00,
        0x7C, 0x82, 0x80, 0x80, 0xFC, 0x82, 0x82, 0x82, 0x7C, 0x00,
        0xFE, 0x02, 0x04, 0x08, 0x10, 0x20, 0x20, 0x20, 0x20, 0x00,
        0x7C, 0x82, 0x82, 0x82, 0x7C, 0x82, 0x82, 0x82, 0x7C, 0x00,
        0x7C, 0x82, 0x82, 0x82, 0x7E, 0x02, 0x02, 0x82, 0x7C, 0x00,
        0x10, 0x28, 0x44, 0x82, 0x82, 0xFE, 0x82, 0x82, 0x82, 0x00,
        0xFC, 0x82, 0x82, 0x82, 0xFC, 0x82, 0x82, 0x82, 0xFC, 0x00,
        0x7C, 0x82, 0x80, 0x80, 0x80, 0x80, 0x80, 0x82, 0x7C, 0x00,
        0xFC, 0x82, 0x82, 0x82, 0x82, 0x82, 0x82, 0x82, 0xFC, 0x00,
        0xFE, 0x80, 0x80, 0x80, 0xF8, 0x80, 0x80, 0x80, 0xFE, 0x00,
        0xFE, 0x80, 0x80, 0x80, 0xF8, 0x80, 0x80, 0x80, 0x80, 0x00
    };

    private readonly byte[] _sprites =
    {
        0xF0, 0x90, 0x90, 0x90, 0xF0, 0x20, 0x60, 0x20, 0x20, 0x70,
        0xF0, 0x10, 0xF0, 0x80, 0xF0, 0xF0, 0x10, 0xF0, 0x10, 0xF0,
        0x90, 0x90, 0xF0, 0x10, 0x10, 0xF0, 0x80, 0xF0, 0x10, 0xF0,
        0xF0, 0x80, 0xF0, 0x90, 0xF0, 0xF0, 0x10, 0x20, 0x40, 0x40,
        0xF0, 0x90, 0xF0, 0x90, 0xF0, 0xF0, 0x90, 0xF0, 0x10, 0xF0,
        0xF0, 0x90, 0xF0, 0x90, 0x90, 0xE0, 0x90, 0xE0, 0x90, 0xE0,
        0xF0, 0x80, 0x80, 0x80, 0xF0, 0xE0, 0x90, 0x90, 0x90, 0xE0,
        0xF0, 0x80, 0xF0, 0x80, 0xF0, 0xF0, 0x80, 0xF0, 0x80, 0x80
    };

    private readonly ushort[] _stack = new ushort[16];
    private readonly byte[] _v = new byte[16];
    private byte _delayTimer;

    // S-CHIP high-resolution mode.
    private bool _hires;
    private ushort _i;
    private bool _isWaitingForKey;
    private int _keyRegisterX;
    private ushort _pc;
    private byte _soundTimer;
    private ushort _sp;

    public Chip8Emulator()
    {
        _dispatch = new Action<ushort>[16];
        _dispatch[0x0] = Handle0;
        _dispatch[0x1] = op => { _pc = (ushort)(op & 0x0FFF); };
        _dispatch[0x2] = op =>
        {
            _stack[_sp++] = _pc;
            _pc = (ushort)(op & 0x0FFF);
        };
        _dispatch[0x3] = op =>
        {
            if (_v[(op & 0x0F00) >> 8] == (op & 0x00FF)) _pc += 2;
        };
        _dispatch[0x4] = op =>
        {
            if (_v[(op & 0x0F00) >> 8] != (op & 0x00FF)) _pc += 2;
        };
        _dispatch[0x5] = op =>
        {
            if ((op & 0x000F) == 0 && _v[(op & 0x0F00) >> 8] == _v[(op & 0x00F0) >> 4]) _pc += 2;
        };
        _dispatch[0x6] = op => { _v[(op & 0x0F00) >> 8] = (byte)(op & 0x00FF); };
        _dispatch[0x7] = op => { _v[(op & 0x0F00) >> 8] += (byte)(op & 0x00FF); };
        _dispatch[0x8] = Handle8;
        _dispatch[0x9] = op =>
        {
            if ((op & 0x000F) == 0 && _v[(op & 0x0F00) >> 8] != _v[(op & 0x00F0) >> 4]) _pc += 2;
        };
        _dispatch[0xA] = op => { _i = (ushort)(op & 0x0FFF); };
        _dispatch[0xB] = op => { _pc = (ushort)((op & 0x0FFF) + _v[0]); };
        _dispatch[0xC] = op =>
        {
            var x = (op & 0x0F00) >> 8;
            var kk = op & 0x00FF;
            _v[x] = (byte)(_random.Next(256) & kk);
        };
        _dispatch[0xD] = HandleD;
        _dispatch[0xE] = HandleE;
        _dispatch[0xF] = HandleF;
    }

    // Graphics backing buffer is always 128-wide for simplicity (row stride 128).
    // The renderer should request the active region via GetGraphics().
    public byte[] Graphics { get; } = new byte[128 * 64];

    public byte[] Keys { get; } = new byte[16];
    public bool DrawFlag { get; set; }
    public bool ShouldBeep { get; set; }

    // Expose resolution
    public int Width { get; private set; } = 64;

    public int Height { get; private set; } = 32;

    public void Initialize(byte[] romBytes)
    {
        _pc = 0x200;
        _i = 0;
        _sp = 0;
        _delayTimer = 0;
        _soundTimer = 0;
        _isWaitingForKey = false;
        _keyRegisterX = 0;
        DrawFlag = false;
        ShouldBeep = false;

        SetLowRes(); // sets _width/_height and will clear below

        Array.Clear(_stack, 0, _stack.Length);
        Array.Clear(_v, 0, _v.Length);
        Array.Clear(Graphics, 0, Graphics.Length);
        Array.Clear(_memory, 0, _memory.Length);
        Array.Clear(Keys, 0, Keys.Length);

        // load fonts: small then big right after
        Array.Copy(_sprites, 0, _memory, SmallFontBase, _sprites.Length);
        Array.Copy(_schipSprites, 0, _memory, BigFontBase, _schipSprites.Length);

        if (romBytes.Length > _memory.Length - 512)
            throw new ArgumentException("ROM too large for memory");

        Array.Copy(romBytes, 0, _memory, 512, romBytes.Length);
    }

    public void Step()
    {
        if (_isWaitingForKey)
        {
            for (byte i = 0; i < Keys.Length; i++)
                if (Keys[i] == 1)
                {
                    _v[_keyRegisterX] = i;
                    _isWaitingForKey = false;
                    break;
                }

            if (_isWaitingForKey) return;
        }

        // Safety: prevent reading past the end of memory
        if (_pc >= _memory.Length - 1) return;

        var opcode = (ushort)((_memory[_pc] << 8) | _memory[_pc + 1]);
        _pc += 2;

        var handler = _dispatch[(opcode & 0xF000) >> 12];
        if (handler == null) return;
        handler(opcode);
    }

    public void UpdateTimers()
    {
        if (_delayTimer > 0) _delayTimer--;
        if (_soundTimer > 0)
        {
            _soundTimer--;
            if (_soundTimer == 0) ShouldBeep = true;
        }
    }

    // --- Opcode handlers ---
    private void Handle0(ushort op)
    {
        switch (op)
        {
            case 0x00E0:
                // Clear entire backing buffer (safe)
                Array.Clear(Graphics, 0, Graphics.Length);
                DrawFlag = true;
                break;
            case 0x00EE:
                _pc = _stack[--_sp];
                break;
            default:
                if ((op & 0xFFF0) == 0x00C0)
                    ScrollDown(op & 0x000F);
                else
                    switch (op)
                    {
                        case 0x00FB: ScrollRight4(); break;
                        case 0x00FC: ScrollLeft4(); break;
                        case 0x00FD: /* interpreter exit - no-op here */
                            break;
                        case 0x00FE: SetLowRes(); break;
                        case 0x00FF: SetHighRes(); break;
                    }

                break;
        }
    }

    private void Handle8(ushort op)
    {
        int x = (op & 0x0F00) >> 8, y = (op & 0x00F0) >> 4;
        switch (op & 0x000F)
        {
            case 0x0: _v[x] = _v[y]; break;
            case 0x1: _v[x] |= _v[y]; break;
            case 0x2: _v[x] &= _v[y]; break;
            case 0x3: _v[x] ^= _v[y]; break;
            case 0x4:
            {
                var sum = _v[x] + _v[y];
                _v[0xF] = (byte)(sum > 255 ? 1 : 0);
                _v[x] = (byte)sum;
                break;
            }
            case 0x5:
                _v[0xF] = (byte)(_v[x] > _v[y] ? 1 : 0);
                _v[x] = (byte)(_v[x] - _v[y]);
                break;
            case 0x6:
                _v[0xF] = (byte)(_v[x] & 1);
                _v[x] >>= 1;
                break;
            case 0x7:
                _v[0xF] = (byte)(_v[y] > _v[x] ? 1 : 0);
                _v[x] = (byte)(_v[y] - _v[x]);
                break;
            case 0xE:
                _v[0xF] = (byte)(_v[x] >> 7);
                _v[x] <<= 1;
                break;
        }
    }

    private void HandleD(ushort op)
    {
        var vx = (op & 0x0F00) >> 8;
        var vy = (op & 0x00F0) >> 4;
        int startX = _v[vx];
        int startY = _v[vy];
        var height = op & 0x000F;
        _v[0xF] = 0;

        if (_hires && height == 0)
        {
            // 16x16 SCHIP mode
            DrawSpriteGeneric(startX, startY, 16, 16);
        }
        else
        {
            if (height == 0) height = 16; // defensive
            DrawSpriteGeneric(startX, startY, 8, height);
        }

        DrawFlag = true;
    }

    private void HandleE(ushort op)
    {
        var x = (op & 0x0F00) >> 8;
        switch (op & 0x00FF)
        {
            case 0x9E:
                if (_v[x] < Keys.Length && Keys[_v[x]] != 0) _pc += 2;
                break;
            case 0xA1:
                if (_v[x] < Keys.Length && Keys[_v[x]] == 0) _pc += 2;
                break;
        }
    }

    private void HandleF(ushort op)
    {
        var x = (op & 0x0F00) >> 8;
        switch (op & 0x00FF)
        {
            case 0x07: _v[x] = _delayTimer; break;
            case 0x0A:
                _isWaitingForKey = true;
                _keyRegisterX = x;
                break;
            case 0x15: _delayTimer = _v[x]; break;
            case 0x18: _soundTimer = _v[x]; break;
            case 0x1E: _i = (ushort)(_i + _v[x]); break;
            case 0x29: _i = (ushort)(SmallFontBase + _v[x] * 5); break;
            case 0x30: _i = (ushort)(BigFontBase + _v[x] * BigFontGlyphSize); break;
            case 0x33:
                _memory[_i] = (byte)(_v[x] / 100);
                _memory[_i + 1] = (byte)(_v[x] / 10 % 10);
                _memory[_i + 2] = (byte)(_v[x] % 10);
                break;
            case 0x55:
                for (var j = 0; j <= x; j++) _memory[_i + j] = _v[j];
                break;
            case 0x65:
                for (var j = 0; j <= x; j++) _v[j] = _memory[_i + j];
                break;
            case 0x75:
                for (var j = 0; j <= x && j < 8; j++) _memory[0xF00 + j] = _v[j];
                break;
            case 0x85:
                for (var j = 0; j <= x && j < 8; j++) _v[j] = _memory[0xF00 + j];
                break;
        }
    }

    // --- Helpers ---

    private void SetHighRes()
    {
        _hires = true;
        Width = 128;
        Height = 64;
        // clear the entire backing buffer to avoid artifacts
        Array.Clear(Graphics, 0, Graphics.Length);
        DrawFlag = true;
    }

    private void SetLowRes()
    {
        _hires = false;
        Width = 64;
        Height = 32;
        // clear the entire backing buffer to avoid artifacts
        Array.Clear(Graphics, 0, Graphics.Length);
        DrawFlag = true;
    }

    // Generic sprite drawer using explicit row stride of 128.
    private void DrawSpriteGeneric(int startX, int startY, int spriteWidth, int spriteHeight)
    {
        var rowBytes = spriteWidth / 8; // 1 for 8px width, 2 for 16px width
        var maxSpriteBytes = rowBytes * spriteHeight;

        // bounds guard for sprite read
        if (_i + maxSpriteBytes > _memory.Length) return;

        const int stride = 128;

        for (var row = 0; row < spriteHeight; row++)
        {
            var rowBaseIndex = row * rowBytes;
            for (var byteCol = 0; byteCol < rowBytes; byteCol++)
            {
                int spriteByte = _memory[_i + rowBaseIndex + byteCol];

                for (var bit = 0; bit < 8; bit++)
                {
                    if ((spriteByte & (0x80 >> bit)) == 0) continue;

                    var px = (startX + byteCol * 8 + bit) % Width;
                    var py = (startY + row) % Height;
                    if (px < 0) px += Width;
                    if (py < 0) py += Height;

                    var index = py * stride + px;
                    if (index < 0 || index >= Graphics.Length) continue;

                    if (Graphics[index] == 1) _v[0xF] = 1;
                    Graphics[index] ^= 1;
                }
            }
        }
    }

    private void ScrollDown(int n)
    {
        if (n <= 0) return;
        if (n >= Height)
        {
            Array.Clear(Graphics, 0, Graphics.Length);
            DrawFlag = true;
            return;
        }

        // Shift rows down (bottom-up) into the backing buffer (stride=128)
        for (var row = Height - 1; row >= n; row--)
        {
            var src = (row - n) * 128;
            var dst = row * 128;
            // copy only active columns
            Array.Copy(Graphics, src, Graphics, dst, Width);
            // clear the rest of the row beyond active width if hi-res
            if (Width < 128)
                Array.Clear(Graphics, dst + Width, 128 - Width);
        }

        // clear top n rows
        for (var row = 0; row < n; row++)
            Array.Clear(Graphics, row * 128, Width);

        DrawFlag = true;
    }

    private void ScrollLeft4()
    {
        for (var row = 0; row < Height; row++)
        {
            var baseIdx = row * 128;
            // move pixels left by 4 inside active width
            for (var col = 0; col < Width; col++)
            {
                var src = baseIdx + col + 4;
                var dst = baseIdx + col;
                Graphics[dst] = src < baseIdx + Width ? Graphics[src] : (byte)0;
            }

            // clear any extra right columns if hi-res
            if (Width < 128) Array.Clear(Graphics, baseIdx + Width, 128 - Width);
        }

        DrawFlag = true;
    }

    private void ScrollRight4()
    {
        for (var row = 0; row < Height; row++)
        {
            var baseIdx = row * 128;
            for (var col = Width - 1; col >= 0; col--)
            {
                var src = baseIdx + col - 4;
                var dst = baseIdx + col;
                Graphics[dst] = src >= baseIdx ? Graphics[src] : (byte)0;
            }

            if (Width < 128) Array.Clear(Graphics, baseIdx + Width, 128 - Width);
        }

        DrawFlag = true;
    }
}