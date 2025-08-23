using System;

namespace EmuChip;

public class Chip8Emulator
{
    private readonly Action<ushort>[] _dispatch;

    private readonly byte[] _memory = new byte[4096];
    private readonly Random _random = new();

    private readonly byte[] _sprites =
    [
        0xF0, 0x90, 0x90, 0x90, 0xF0, 0x20, 0x60, 0x20, 0x20, 0x70, 0xF0, 0x10, 0xF0, 0x80, 0xF0,
        0xF0, 0x10, 0xF0, 0x10, 0xF0, 0x90, 0x90, 0xF0, 0x10, 0x10, 0xF0, 0x80, 0xF0, 0x10, 0xF0,
        0xF0, 0x80, 0xF0, 0x90, 0xF0, 0xF0, 0x10, 0x20, 0x40, 0x40, 0xF0, 0x90, 0xF0, 0x90, 0xF0,
        0xF0, 0x90, 0xF0, 0x10, 0xF0, 0xF0, 0x90, 0xF0, 0x90, 0x90, 0xE0, 0x90, 0xE0, 0x90, 0xE0,
        0xF0, 0x80, 0x80, 0x80, 0xF0, 0xE0, 0x90, 0x90, 0x90, 0xE0, 0xF0, 0x80, 0xF0, 0x80, 0xF0,
        0xF0, 0x80, 0xF0, 0x80, 0x80
    ];

    private readonly ushort[] _stack = new ushort[16];
    private readonly byte[] _v = new byte[16];
    private byte _delayTimer;
    private ushort _i;

    // --- State for non-blocking key wait ---
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
            if (_v[(op & 0x0F00) >> 8] == _v[(op & 0x00F0) >> 4]) _pc += 2;
        };
        _dispatch[0x6] = op => { _v[(op & 0x0F00) >> 8] = (byte)(op & 0x00FF); };
        _dispatch[0x7] = op => { _v[(op & 0x0F00) >> 8] += (byte)(op & 0x00FF); };
        _dispatch[0x8] = Handle8;
        _dispatch[0x9] = op =>
        {
            if (_v[(op & 0x0F00) >> 8] != _v[(op & 0x00F0) >> 4]) _pc += 2;
        };
        _dispatch[0xA] = op => { _i = (ushort)(op & 0x0FFF); };
        _dispatch[0xB] = op => { _pc = (ushort)((op & 0x0FFF) + _v[0]); };
        _dispatch[0xC] = op => { _v[(op & 0x0F00) >> 8] = (byte)(_random.Next(256) & op & 0x00FF); };
        _dispatch[0xD] = HandleD;
        _dispatch[0xE] = HandleE;
        _dispatch[0xF] = HandleF;
    }

    public byte[] Keys { get; } = new byte[16];

    public byte[] Graphics { get; } = new byte[64 * 32];
    public bool DrawFlag { get; set; }
    public bool ShouldBeep { get; set; }

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

        Array.Clear(_stack, 0, _stack.Length);
        Array.Clear(_v, 0, _v.Length);
        Array.Clear(Graphics, 0, Graphics.Length);
        Array.Clear(_memory, 0, _memory.Length);

        Array.Copy(_sprites, _memory, _sprites.Length);
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

        var opcode = (ushort)((_memory[_pc] << 8) | _memory[_pc + 1]);
        _pc += 2;
        _dispatch[(opcode & 0xF000) >> 12](opcode);
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

    private void Handle0(ushort op)
    {
        switch (op)
        {
            case 0x00E0:
                Array.Clear(Graphics, 0, Graphics.Length);
                DrawFlag = true;
                break;
            case 0x00EE: _pc = _stack[--_sp]; break;
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
                _v[0xF] = (byte)(_v[x] + _v[y] > 255 ? 1 : 0);
                _v[x] += _v[y];
                break;
            case 0x5:
                _v[0xF] = (byte)(_v[x] > _v[y] ? 1 : 0);
                _v[x] -= _v[y];
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
        int x = _v[(op & 0x0F00) >> 8];
        int y = _v[(op & 0x00F0) >> 4];
        var height = op & 0x000F;
        _v[0xF] = 0;

        for (var yLine = 0; yLine < height; yLine++)
        {
            int pixelData = _memory[_i + yLine];
            var currentY = y + yLine;

            if (currentY >= 32) continue;

            var rowStartIndex = currentY * 64;

            for (var xLine = 0; xLine < 8; xLine++)
                if ((pixelData & (0x80 >> xLine)) != 0)
                {
                    var currentX = x + xLine;
                    if (currentX >= 64) continue;

                    var index = rowStartIndex + currentX;

                    // Check for collision and set the VF register.
                    if (Graphics[index] == 1) _v[0xF] = 1;

                    // XOR the pixel onto the graphics buffer.
                    Graphics[index] ^= 1;
                }
        }

        DrawFlag = true;
    }

    private void HandleE(ushort op)
    {
        var x = (op & 0x0F00) >> 8;
        switch (op & 0x00FF)
        {
            case 0x9E:
                if (Keys[_v[x]] != 0) _pc += 2;
                break;
            case 0xA1:
                if (Keys[_v[x]] == 0) _pc += 2;
                break;
        }
    }

    private void HandleF(ushort op)
    {
        var x = (op & 0x0F00) >> 8;
        switch (op & 0x00FF)
        {
            case 0x07: _v[x] = _delayTimer; break;
            case 0x0A: // LD Vx, K (Wait for key)
                _isWaitingForKey = true;
                _keyRegisterX = x;
                break;
            case 0x15: _delayTimer = _v[x]; break;
            case 0x18: _soundTimer = _v[x]; break;
            case 0x1E: _i += _v[x]; break;
            case 0x29: _i = (ushort)(_v[x] * 5); break;
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
        }
    }
}