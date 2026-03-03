using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FlowRunner.CLI
{
    /// <summary>
    /// اجراکننده Flow بدون نیاز به GUI
    /// </summary>
    public class HeadlessFlowExecutor
    {
        private readonly FlowDefinition _flow;
        private readonly bool _verbose;

        // Win32 API imports
        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, int dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);

        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        private const uint MOUSEEVENTF_WHEEL = 0x0800;

        public HeadlessFlowExecutor(FlowDefinition flow, bool verbose = false)
        {
            _flow = flow;
            _verbose = verbose;
        }

        public bool Execute()
        {
            try
            {
                if (_verbose)
                    Console.WriteLine($"📝 Flow loaded: {_flow.Steps.Count} steps");

                Console.WriteLine("⏱️  Starting execution...");
                var stopwatch = Stopwatch.StartNew();

                for (int loop = 0; loop < _flow.Loops; loop++)
                {
                    if (_flow.Loops > 1)
                        Console.WriteLine($"\n🔁 Loop {loop + 1}/{_flow.Loops}");

                    for (int i = 0; i < _flow.Steps.Count; i++)
                    {
                        var step = _flow.Steps[i];
                        
                        if (_verbose)
                            Console.WriteLine($"   Step {i + 1}/{_flow.Steps.Count}: {step.Kind}");

                        // Delay قبل از step
                        if (step.DelayMs > 0)
                        {
                            if (_verbose)
                                Console.WriteLine($"      Delay: {step.DelayMs}ms");
                            Thread.Sleep(step.DelayMs);
                        }

                        ExecuteStep(step);
                    }
                }

                stopwatch.Stop();

                Console.WriteLine("\n=== Execution Results ===");
                Console.WriteLine($"Status: ✅ SUCCESS");
                Console.WriteLine($"Duration: {stopwatch.ElapsedMilliseconds}ms");
                Console.WriteLine($"Steps Executed: {_flow.Steps.Count * _flow.Loops}/{_flow.Steps.Count * _flow.Loops}");

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ Execution failed: {ex.Message}");
                if (_verbose)
                    Console.WriteLine(ex.StackTrace);
                return false;
            }
        }

        private void ExecuteStep(FlowStep step)
        {
            switch (step.Kind)
            {
                case FlowStepKind.Move:
                    SetCursorPos(step.X, step.Y);
                    if (_verbose)
                        Console.WriteLine($"      Move to ({step.X}, {step.Y})");
                    break;

                case FlowStepKind.ClickLeft:
                    SetCursorPos(step.X, step.Y);
                    Thread.Sleep(50);
                    mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
                    Thread.Sleep(50);
                    mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
                    if (_verbose)
                        Console.WriteLine($"      Left click at ({step.X}, {step.Y})");
                    break;

                case FlowStepKind.ClickRight:
                    SetCursorPos(step.X, step.Y);
                    Thread.Sleep(50);
                    mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, 0);
                    Thread.Sleep(50);
                    mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, 0);
                    if (_verbose)
                        Console.WriteLine($"      Right click at ({step.X}, {step.Y})");
                    break;

                case FlowStepKind.Wheel:
                    mouse_event(MOUSEEVENTF_WHEEL, 0, 0, (uint)step.WheelDelta, 0);
                    if (_verbose)
                        Console.WriteLine($"      Wheel: {step.WheelDelta}");
                    break;

                case FlowStepKind.Checkpoint:
                    if (_verbose)
                        Console.WriteLine($"      Checkpoint: {step.Name} (skipped in CLI mode)");
                    break;

                case FlowStepKind.ExpectTemplate:
                    if (_verbose)
                        Console.WriteLine($"      ExpectTemplate (skipped in CLI mode)");
                    break;

                default:
                    if (_verbose)
                        Console.WriteLine($"      Unknown step type: {step.Kind}");
                    break;
            }
        }
    }
}