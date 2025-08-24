using System.Numerics;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace EmuChip;
    public class Chip8Window
    {
        private const string VertexShaderSource = @"#version 330 core
layout (location = 0) in vec2 aPos;
layout (location = 1) in vec2 aTex;
out vec2 vTex;
void main() { gl_Position = vec4(aPos, 0.0, 1.0); vTex = aTex; }";

        private const string FragmentShaderSource = @"#version 330 core
out vec4 FragColor;
in vec2 vTex;
uniform sampler2D uTex;
uniform vec4 uOn;
uniform vec4 uOff;
void main() { FragColor = mix(uOff, uOn, texture(uTex, vTex).r); }";

        private readonly Dictionary<Key, int> _keyMap = new()
        {
            { Key.Number1, 0x1 }, { Key.Number2, 0x2 }, { Key.Number3, 0x3 }, { Key.Number4, 0xC },
            { Key.Q, 0x4 }, { Key.W, 0x5 }, { Key.E, 0x6 }, { Key.R, 0xD },
            { Key.A, 0x7 }, { Key.S, 0x8 }, { Key.D, 0x9 }, { Key.F, 0xE },
            { Key.Z, 0xA }, { Key.X, 0x0 }, { Key.C, 0xB }, { Key.V, 0xF }
        };

        private byte[] _pixelBuffer;
        private float[] _phosphorBuffer;
        private int _displayWidth = 64;
        private int _displayHeight = 32;
        private GL _gl;
        private IKeyboard _keyboard;
        private bool _renderRequested;
        private uint _vao, _vbo, _shaderProgram, _texture;
        private readonly IWindow _window;
        private const float PhosphorDecay = 0.8f;

        public byte[] Keys { get; } = new byte[16];

        public event Action<double> Update;
        public event Action Load;

        public Chip8Window()
        {
            var options = WindowOptions.Default;
            options.Title = "Silk.NET Super-CHIP-8";
            options.Size = new Vector2D<int>(640, 320);
            _window = Window.Create(options);
            _window.Load += OnLoad;
            _window.Update += OnUpdate;
            _window.Render += OnRender;
            _window.Closing += OnClose;
        }

        public void PollInput()
        {
            Array.Clear(Keys, 0, Keys.Length);
            foreach (var (key, val) in _keyMap)
                if (_keyboard.IsKeyPressed(key))
                    Keys[val] = 1;
        }

        public void Run() => _window.Run();

        private int _prevWidth = 0;
        private int _prevHeight = 0;
        public void RequestRender(byte[] graphics, int width, int height)
        {
            if (_prevWidth != width || _prevHeight != height)
            {
                _prevWidth = width;
                _prevHeight = height;
                
                _pixelBuffer = new byte[graphics.Length];
                _phosphorBuffer = new float[graphics.Length];
                _displayWidth = width;
                _displayHeight = height;

                _gl.BindTexture(TextureTarget.Texture2D, _texture);
                _gl.TexImage2D<byte>(TextureTarget.Texture2D, 0, InternalFormat.R8, (uint)_displayWidth, (uint)_displayHeight, 0,
                    PixelFormat.Red, PixelType.UnsignedByte, _pixelBuffer);
            }

            // The C# buffer always has a memory width (stride) of 128
            const int sourceStride = 128;

            // Use nested loops to correctly map pixels
            for (var y = 0; y < height; y++) {
                for (var x = 0; x < width; x++) {
                    // Index in the source buffer from C#
                    var sourceIndex = (y * sourceStride) + x;

                    // Index in the destination phosphor buffer (which is packed)
                    var destIndex = ((height - y) * width) + x;

                    if (graphics[sourceIndex] != 0) _phosphorBuffer[destIndex] = 1f;
                }
            }

            _renderRequested = true;
        }

        private void OnLoad()
        {
            Load?.Invoke();
            _gl = _window.CreateOpenGL();
            _keyboard = _window.CreateInput().Keyboards[0];
            SetupGraphics();
        }

        private void OnUpdate(double dt) => Update?.Invoke(dt);

        private void OnRender(double dt)
        {
            // Update phosphor buffer -> pixel buffer
            for (int i = 0; i < _phosphorBuffer.Length; i++)
            {
                var b = _phosphorBuffer[i];
                _pixelBuffer[i] = (byte)(Math.Clamp(b, 0f, 1f) * 255f);
                _phosphorBuffer[i] *= PhosphorDecay;
            }

            if (_renderRequested)
            {
                _gl.BindTexture(TextureTarget.Texture2D, _texture);
                _gl.TexSubImage2D<byte>(TextureTarget.Texture2D, 0, 0, 0, (uint)_displayWidth, (uint)_displayHeight,
                    PixelFormat.Red, PixelType.UnsignedByte, _pixelBuffer);
                _renderRequested = false;
            }

            _gl.Clear(ClearBufferMask.ColorBufferBit);
            _gl.UseProgram(_shaderProgram);
            _gl.Uniform4(_gl.GetUniformLocation(_shaderProgram, "uOn"), new Vector4(0.9f, 0.9f, 0.8f, 1f));
            _gl.Uniform4(_gl.GetUniformLocation(_shaderProgram, "uOff"), new Vector4(0.1f, 0.1f, 0.2f, 1f));
            _gl.BindVertexArray(_vao);
            _gl.DrawArrays(PrimitiveType.TriangleFan, 0, 4);
        }

        private void OnClose()
        {
            _gl.DeleteBuffer(_vbo);
            _gl.DeleteVertexArray(_vao);
            _gl.DeleteProgram(_shaderProgram);
            _gl.DeleteTexture(_texture);
            _gl.Dispose();
        }

        private unsafe void SetupGraphics()
        {
            float[] vertices =
            {
                // posX, posY, texU, texV
                -1f,  1f, 0f, 1f,
                 1f,  1f, 1f, 1f,
                 1f, -1f, 1f, 0f,
                -1f, -1f, 0f, 0f
            };

            _vao = _gl.GenVertexArray();
            _gl.BindVertexArray(_vao);

            _vbo = _gl.GenBuffer();
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
            fixed (float* ptr = &vertices[0])
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertices.Length * sizeof(float)), ptr,
                    BufferUsageARB.StaticDraw);

            _shaderProgram = CompileShaders();

            _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), (void*)0);
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), (void*)(2 * sizeof(float)));
            _gl.EnableVertexAttribArray(1);

            _texture = _gl.GenTexture();
            _gl.BindTexture(TextureTarget.Texture2D, _texture);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.R8, (uint)_displayWidth, (uint)_displayHeight, 0, PixelFormat.Red, PixelType.UnsignedByte, null);
        }

        private uint CompileShaders()
        {
            var v = _gl.CreateShader(ShaderType.VertexShader);
            _gl.ShaderSource(v, VertexShaderSource);
            _gl.CompileShader(v);

            var f = _gl.CreateShader(ShaderType.FragmentShader);
            _gl.ShaderSource(f, FragmentShaderSource);
            _gl.CompileShader(f);

            var p = _gl.CreateProgram();
            _gl.AttachShader(p, v);
            _gl.AttachShader(p, f);
            _gl.LinkProgram(p);
            _gl.DeleteShader(v);
            _gl.DeleteShader(f);
            return p;
        }
    }