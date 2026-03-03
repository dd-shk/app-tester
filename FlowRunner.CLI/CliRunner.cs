using System.Text.Json;

namespace FlowRunner.CLI
{
    /// <summary>
    /// مدیر اصلی اجرای CLI
    /// </summary>
    public class CliRunner
    {
        private readonly CommandLineOptions _options;
        private readonly string _flowsPath;

        public CliRunner(CommandLineOptions options)
        {
            _options = options;
            
            // پوشه پیش‌فرض flows
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _flowsPath = Path.Combine(appData, "FlowRunner", "Flows");
        }

        public int Run()
        {
            try
            {
                if (_options.ShowHelp)
                {
                    ShowHelp();
                    return ExitCodes.Success;
                }

                if (_options.ShowList)
                {
                    ShowFlowsList();
                    return ExitCodes.Success;
                }

                if (!_options.IsValid())
                {
                    Console.WriteLine("❌ Error: Category and Flow name are required.");
                    Console.WriteLine("Use --help for usage information.");
                    return ExitCodes.InvalidArguments;
                }

                return ExecuteFlow();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Fatal error: {ex.Message}");
                if (_options.Verbose)
                    Console.WriteLine(ex.StackTrace);
                return ExitCodes.ExecutionError;
            }
        }

        private void ShowHelp()
        {
            Console.WriteLine(@"
╔═══════════════════════════════════════════════════════╗
║           FlowRunner CLI - Help                       ║
╚═══════════════════════════════════════════════════════╝

Usage:
  FlowRunner.CLI.exe [options]

Options:
  --help, -h, /?           Show this help message
  --list, -l               List all available flows
  --category, -c <name>    Flow category (required)
  --flow, -f <name>        Flow name (required)
  --loops <number>         Number of times to repeat (default: 1)
  --verbose, -v            Show detailed execution logs

Examples:
  FlowRunner.CLI.exe --list
  FlowRunner.CLI.exe -c "Test" -f "MyFlow"
  FlowRunner.CLI.exe -c "Test" -f "MyFlow" --loops 3 --verbose

Exit Codes:
  0 = Success
  1 = Flow execution failed
  2 = Execution error
  3 = Invalid arguments
  4 = Flow not found
");
        }

        private void ShowFlowsList()
        {
            Console.WriteLine("\n=== Available Flows ===\n");

            if (!Directory.Exists(_flowsPath))
            {
                Console.WriteLine("No flows found. Flows directory does not exist.");
                return;
            }

            var categories = Directory.GetDirectories(_flowsPath);
            
            if (categories.Length == 0)
            {
                Console.WriteLine("No flows found.");
                return;
            }

            foreach (var categoryDir in categories)
            {
                var categoryName = Path.GetFileName(categoryDir);
                Console.WriteLine($"📁 Category: {categoryName}");

                var flowFiles = Directory.GetFiles(categoryDir, "*.json");
                
                if (flowFiles.Length == 0)
                {
                    Console.WriteLine("   (empty)");
                }
                else
                {
                    foreach (var flowFile in flowFiles)
                    {
                        var flowName = Path.GetFileNameWithoutExtension(flowFile);
                        Console.WriteLine($"   ✓ {flowName}");
                    }
                }

                Console.WriteLine();
            }
        }

        private int ExecuteFlow()
        {
            var flowPath = Path.Combine(_flowsPath, _options.Category!, $"{_options.FlowName}.json");

            if (!File.Exists(flowPath))
            {
                Console.WriteLine($"❌ Flow not found: {_options.Category}/{_options.FlowName}");
                Console.WriteLine($"   Path: {flowPath}");
                Console.WriteLine("\nUse --list to see available flows.");
                return ExitCodes.FlowNotFound;
            }

            FlowDefinition flow;
            try
            {
                var json = File.ReadAllText(flowPath);
                flow = JsonSerializer.Deserialize<FlowDefinition>(json) 
                    ?? throw new Exception("Failed to deserialize flow");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error loading flow: {ex.Message}");
                return ExitCodes.ExecutionError;
            }

            // Override loops اگر در command line تنظیم شده
            if (_options.Loops > 1)
                flow.Loops = _options.Loops;

            Console.WriteLine($"\n🚀 Running flow: {_options.Category}/{_options.FlowName}");
            Console.WriteLine($"   Loops: {flow.Loops}");
            Console.WriteLine();

            var executor = new HeadlessFlowExecutor(flow, _options.Verbose);
            var success = executor.Execute();

            Console.WriteLine();
            if (success)
            {
                Console.WriteLine("✅ Flow completed successfully!");
                return ExitCodes.Success;
            }
            else
            {
                Console.WriteLine("❌ Flow execution failed!");
                return ExitCodes.FlowFailed;
            }
        }
    }
}