namespace EmuChip;
public class Chip8Process
{
    private const double UpdateStep = 1.0 / 1000.0;
    private double _updateAccumulator;
    private double _timerAccumulator;

    private readonly Chip8Emulator _emulator;
    private readonly Chip8Window _window;

    public Chip8Process(string romFilename)
    {
        _emulator = new Chip8Emulator();
        _window = new Chip8Window();
        _window.Update += OnUpdate;
        _window.Load += () =>
        {
            var romBytes = System.IO.File.ReadAllBytes(romFilename);
            _emulator.Initialize(romBytes);
        };
    }

    public void Run() => _window.Run();

    private void OnUpdate(double deltaTime)
    {
        _updateAccumulator += deltaTime;

        while (_updateAccumulator >= UpdateStep)
        {
            _window.PollInput();
            Array.Copy(_window.Keys, _emulator.Keys, _window.Keys.Length);
            _emulator.Step();

            // 60Hz timer update
            _timerAccumulator += UpdateStep;
            if (_timerAccumulator >= 1.0 / 60.0)
            {
                _emulator.UpdateTimers();
                _timerAccumulator -= 1.0 / 60.0;
            }

            _updateAccumulator -= UpdateStep;
        }

        if (_emulator.DrawFlag)
        {
            var width = _emulator.Width;
            var height = _emulator.Height;
            
            _window.RequestRender(_emulator.Graphics, width, height);
            _emulator.DrawFlag = false;
        }

        if (_emulator.ShouldBeep)
        {
            Console.Beep();
            _emulator.ShouldBeep = false;
        }
    }
}