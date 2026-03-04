using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing;

namespace FlowRunner
{
    public sealed partial class MainForm
    {
        private Button? _btnTestSuite;
        private Button? _btnRunAllTests;

        private void InitializeTestSuiteUI()
        {
            // Add spacing
            var spacer = new Label { Height = 20, Dock = DockStyle.Top };
            _right.Controls.Add(spacer);
            _right.Controls.SetChildIndex(spacer, 0);

            // Test Suite Manager button
            _btnTestSuite = new Button
            {
                Text = "🧪 Test Suites",
                Height = 35,
                Dock = DockStyle.Top,
                BackColor = Color.FromArgb(52, 73, 94),
                ForeColor = Color.Gainsboro,
                FlatStyle = FlatStyle.Flat
            };
            _btnTestSuite.FlatAppearance.BorderColor = Color.FromArgb(70, 90, 110);
            _btnTestSuite.Click += (_, __) => ShowTestSuiteManager();
            _right.Controls.Add(_btnTestSuite);
            _right.Controls.SetChildIndex(_btnTestSuite, 0);

            // Run All Tests button
            _btnRunAllTests = new Button
            {
                Text = "▶️ Run All Tests",
                Height = 35,
                Dock = DockStyle.Top,
                BackColor = Color.FromArgb(39, 174, 96),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _btnRunAllTests.FlatAppearance.BorderColor = Color.FromArgb(50, 190, 110);
            _btnRunAllTests.Click += async (_, __) => await RunAllTestsAsync();
            _right.Controls.Add(_btnRunAllTests);
            _right.Controls.SetChildIndex(_btnRunAllTests, 0);
        }

        private void ShowTestSuiteManager()
        {
            try
            {
                var form = new TestSuiteManagerForm();
                form.ShowDialog(this);
            }
            catch (Exception ex)
            {
                AppLog.Exception("ShowTestSuiteManager failed", ex);
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task RunAllTestsAsync()
        {
            try
            {
                var suites = TestSuiteManager.GetAllTestSuites();
                
                if (suites.Count == 0)
                {
                    MessageBox.Show("No test suites found!\n\nCreate a test suite first using '🧪 Test Suites' button.", 
                        "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                SetStatus("Running all test suites...");
                _isRunning = true;
                UpdateUi();

                int totalPassed = 0;
                int totalFailed = 0;
                int totalSkipped = 0;

                foreach (var suiteName in suites)
                {
                    var fullPath = Path.Combine(TestSuiteManager.TestSuitesDir, suiteName);
                    var suite = TestSuiteManager.LoadTestSuite(fullPath);

                    var report = await TestSuiteManager.RunTestSuiteAsync(
                        suite, 
                        status => SetStatus(status),
                        CancellationToken.None
                    );

                    totalPassed += report.PassedTests;
                    totalFailed += report.FailedTests;
                    totalSkipped += report.SkippedTests;
                }

                SetStatus($"All tests completed: {totalPassed} passed, {totalFailed} failed, {totalSkipped} skipped");
                
                var icon = totalFailed == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning;
                MessageBox.Show(
                    $"Test Suites Completed!\n\n" +
                    $"✅ Passed: {totalPassed}\n" +
                    $"❌ Failed: {totalFailed}\n" +
                    $"⏭️ Skipped: {totalSkipped}\n\n" +
                    $"Reports saved to:\n{TestSuiteManager.TestReportsDir}",
                    "Test Results",
                    MessageBoxButtons.OK,
                    icon
                );

                // Open reports folder
                if (Directory.Exists(TestSuiteManager.TestReportsDir))
                {
                    var result = MessageBox.Show(
                        "Would you like to open the reports folder?",
                        "Open Reports",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question
                    );

                    if (result == DialogResult.Yes)
                    {
                        try
                        {
                            System.Diagnostics.Process.Start("explorer.exe", TestSuiteManager.TestReportsDir);
                        }
                        catch (Exception ex)
                        {
                            AppLog.Exception("Failed to open reports folder", ex);
                            MessageBox.Show($"Could not open reports folder: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Exception("RunAllTests failed", ex);
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetStatus("Test run failed: " + ex.Message);
            }
            finally
            {
                _isRunning = false;
                UpdateUi();
            }
        }
    }
}
