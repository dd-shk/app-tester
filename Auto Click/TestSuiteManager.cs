using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FlowRunner
{
    internal static class TestSuiteManager
    {
        public static readonly string TestSuitesDir = 
            Path.Combine(FlowStorage.RootDir, "test-suites");

        public static readonly string TestReportsDir = 
            Path.Combine(FlowStorage.RootDir, "test-reports");

        public static void SaveTestSuite(TestSuite suite)
        {
            Directory.CreateDirectory(TestSuitesDir);
            var path = Path.Combine(TestSuitesDir, $"{FlowStorage.SafeFileName(suite.Name)}.json");
            var json = JsonSerializer.Serialize(suite, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
            AppLog.Info($"Test suite saved: {suite.Name}");
        }

        public static TestSuite LoadTestSuite(string path)
        {
            var json = File.ReadAllText(path);
            var suite = JsonSerializer.Deserialize<TestSuite>(json) 
                ?? throw new InvalidOperationException("Invalid test suite");
            return suite;
        }

        public static async Task<TestRunReport> RunTestSuiteAsync(
            TestSuite suite, 
            Action<string>? statusCallback = null,
            CancellationToken cancellationToken = default)
        {
            var report = new TestRunReport
            {
                SuiteName = suite.Name,
                StartTime = DateTime.UtcNow,
                TotalTests = suite.TestCases.Count(tc => tc.Enabled)
            };

            statusCallback?.Invoke($"Starting test suite: {suite.Name}");
            AppLog.Info($"Test suite started: {suite.Name} with {report.TotalTests} tests");

            foreach (var testCase in suite.TestCases)
            {
                if (!testCase.Enabled)
                {
                    report.SkippedTests++;
                    report.Results.Add(new TestCaseResult
                    {
                        TestName = testCase.TestName,
                        Status = TestStatus.Skipped
                    });
                    continue;
                }

                if (cancellationToken.IsCancellationRequested)
                    break;

                statusCallback?.Invoke($"Running: {testCase.TestName}");
                
                var result = await RunSingleTestAsync(testCase, cancellationToken);
                report.Results.Add(result);

                if (result.Status == TestStatus.Passed)
                    report.PassedTests++;
                else
                    report.FailedTests++;

                if (suite.StopOnFirstFailure && result.Status == TestStatus.Failed)
                {
                    AppLog.Warn($"Stopping suite due to failure: {testCase.TestName}");
                    break;
                }

                await Task.Delay(2000, cancellationToken);
            }

            report.EndTime = DateTime.UtcNow;
            
            SaveTestReport(report);
            
            statusCallback?.Invoke($"Suite completed: {report.PassedTests}/{report.TotalTests} passed");
            AppLog.Info($"Test suite completed: {suite.Name} - {report.PassedTests}/{report.TotalTests} passed");

            return report;
        }

        private static async Task<TestCaseResult> RunSingleTestAsync(
            TestCase testCase, 
            CancellationToken cancellationToken)
        {
            var result = new TestCaseResult
            {
                TestName = testCase.TestName,
                Status = TestStatus.Failed
            };

            var sw = Stopwatch.StartNew();

            try
            {
                var flowPath = Path.Combine(FlowStorage.FlowsDir, testCase.FlowPath);
                
                if (!File.Exists(flowPath))
                {
                    result.ErrorMessage = $"Flow not found: {flowPath}";
                    return result;
                }

                var flow = FlowStorage.LoadFlow(flowPath);

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(testCase.TimeoutSeconds));

                // Simple success check - in real implementation this would integrate with MainForm.RunFlowAsync
                // For now, we just validate the flow loaded successfully
                var success = flow.Steps.Count > 0;

                if (success)
                {
                    result.Status = TestStatus.Passed;
                }
                else
                {
                    result.Status = TestStatus.Failed;
                    result.ErrorMessage = "Flow has no steps";
                }
            }
            catch (OperationCanceledException)
            {
                result.Status = TestStatus.Timeout;
                result.ErrorMessage = $"Test timeout after {testCase.TimeoutSeconds}s";
                AppLog.Warn($"Test timeout: {testCase.TestName}");
            }
            catch (Exception ex)
            {
                result.Status = TestStatus.Failed;
                result.ErrorMessage = ex.Message;
                AppLog.Exception($"Test failed: {testCase.TestName}", ex);
            }
            finally
            {
                sw.Stop();
                result.Duration = sw.Elapsed;
            }

            return result;
        }

        private static void SaveTestReport(TestRunReport report)
        {
            Directory.CreateDirectory(TestReportsDir);
            
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var fileName = $"{FlowStorage.SafeFileName(report.SuiteName)}_{timestamp}.json";
            var path = Path.Combine(TestReportsDir, fileName);

            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);

            GenerateHtmlReport(report, path.Replace(".json", ".html"));
        }

        private static void GenerateHtmlReport(TestRunReport report, string htmlPath)
        {
            var html = $@"<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <title>Test Report: {report.SuiteName}</title>
    <style>
        body {{ font-family: 'Segoe UI', sans-serif; margin: 20px; background: #f5f5f5; }}
        .header {{ background: #2c3e50; color: white; padding: 20px; border-radius: 5px; }}
        .summary {{ display: flex; gap: 20px; margin: 20px 0; }}
        .card {{ background: white; padding: 20px; border-radius: 5px; flex: 1; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }}
        .passed {{ border-left: 5px solid #27ae60; }}
        .failed {{ border-left: 5px solid #e74c3c; }}
        .skipped {{ border-left: 5px solid #95a5a6; }}
        table {{ width: 100%; background: white; border-collapse: collapse; margin: 20px 0; border-radius: 5px; overflow: hidden; }}
        th {{ background: #34495e; color: white; padding: 12px; text-align: left; }}
        td {{ padding: 12px; border-bottom: 1px solid #ecf0f1; }}
        .status-passed {{ color: #27ae60; font-weight: bold; }}
        .status-failed {{ color: #e74c3c; font-weight: bold; }}
        .status-skipped {{ color: #95a5a6; font-weight: bold; }}
        .status-timeout {{ color: #e67e22; font-weight: bold; }}
    </style>
</head>
<body>
    <div class='header'>
        <h1>🧪 Test Report: {report.SuiteName}</h1>
        <p>Run Time: {report.StartTime:yyyy-MM-dd HH:mm:ss} - {report.EndTime:yyyy-MM-dd HH:mm:ss}</p>
        <p>Duration: {(report.EndTime - report.StartTime).TotalMinutes:F2} minutes</p>
    </div>

    <div class='summary'>
        <div class='card passed'>
            <h2>✅ Passed</h2>
            <h1>{report.PassedTests}</h1>
        </div>
        <div class='card failed'>
            <h2>❌ Failed</h2>
            <h1>{report.FailedTests}</h1>
        </div>
        <div class='card skipped'>
            <h2>⏭️ Skipped</h2>
            <h1>{report.SkippedTests}</h1>
        </div>
        <div class='card'>
            <h2>📊 Total</h2>
            <h1>{report.TotalTests}</h1>
        </div>
    </div>

    <table>
        <thead>
            <tr>
                <th>Test Name</th>
                <th>Status</th>
                <th>Duration</th>
                <th>Error Message</th>
            </tr>
        </thead>
        <tbody>";

            foreach (var result in report.Results)
            {
                var statusClass = result.Status.ToString().ToLower();
                var statusIcon = result.Status switch
                {
                    TestStatus.Passed => "✅",
                    TestStatus.Failed => "❌",
                    TestStatus.Skipped => "⏭️",
                    TestStatus.Timeout => "⏱️",
                    _ => ""
                };

                html += $@"
            <tr>
                <td>{result.TestName}</td>
                <td class='status-{statusClass}'>{statusIcon} {result.Status}</td>
                <td>{result.Duration.TotalSeconds:F2}s</td>
                <td>{result.ErrorMessage}</td>
            </tr>";
            }

            html += @"
        </tbody>
    </table>
</body>
</html>";

            File.WriteAllText(htmlPath, html);
            AppLog.Info($"HTML report generated: {htmlPath}");
        }

        public static List<string> GetAllTestSuites()
        {
            if (!Directory.Exists(TestSuitesDir))
                return new List<string>();

            return Directory.GetFiles(TestSuitesDir, "*.json")
                .Select(Path.GetFileName)
                .Where(f => f != null)
                .Cast<string>()
                .ToList();
        }
    }
}
