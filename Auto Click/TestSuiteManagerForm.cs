using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Drawing;

namespace FlowRunner
{
    public sealed class TestSuiteManagerForm : Form
    {
        private readonly ListBox _lstSuites = new();
        private readonly ListBox _lstTests = new();
        private readonly Button _btnNew = new();
        private readonly Button _btnAddTest = new();
        private readonly Button _btnRun = new();
        private readonly Button _btnDelete = new();
        private readonly TextBox _txtName = new();

        private TestSuite? _currentSuite;

        public TestSuiteManagerForm()
        {
            Text = "Test Suite Manager";
            Width = 800;
            Height = 600;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(14, 18, 30);
            ForeColor = Color.Gainsboro;

            var split = new SplitContainer 
            { 
                Dock = DockStyle.Fill, 
                SplitterDistance = 300,
                BackColor = Color.FromArgb(14, 18, 30)
            };
            Controls.Add(split);

            _lstSuites.Dock = DockStyle.Fill;
            _lstSuites.BackColor = Color.FromArgb(18, 22, 36);
            _lstSuites.ForeColor = Color.Gainsboro;
            _lstSuites.SelectedIndexChanged += (_, __) => LoadSelectedSuite();
            split.Panel1.Controls.Add(_lstSuites);

            var leftButtons = new FlowLayoutPanel 
            { 
                Dock = DockStyle.Bottom, 
                Height = 40,
                BackColor = Color.FromArgb(18, 22, 36)
            };
            _btnNew.Text = "New Suite";
            _btnNew.Click += (_, __) => CreateNewSuite();
            leftButtons.Controls.Add(_btnNew);
            split.Panel1.Controls.Add(leftButtons);

            _txtName.Dock = DockStyle.Top;
            _txtName.PlaceholderText = "Suite Name";
            _txtName.BackColor = Color.FromArgb(18, 22, 36);
            _txtName.ForeColor = Color.Gainsboro;
            split.Panel2.Controls.Add(_txtName);

            _lstTests.Dock = DockStyle.Fill;
            _lstTests.BackColor = Color.FromArgb(18, 22, 36);
            _lstTests.ForeColor = Color.Gainsboro;
            split.Panel2.Controls.Add(_lstTests);

            var rightButtons = new FlowLayoutPanel 
            { 
                Dock = DockStyle.Bottom, 
                Height = 40,
                BackColor = Color.FromArgb(18, 22, 36)
            };
            _btnAddTest.Text = "Add Test";
            _btnAddTest.Click += (_, __) => AddTest();
            _btnRun.Text = "Run Suite";
            _btnRun.Click += async (_, __) => await RunSuite();
            _btnDelete.Text = "Delete";
            _btnDelete.Click += (_, __) => DeleteSuite();
            
            rightButtons.Controls.AddRange(new Control[] { _btnAddTest, _btnRun, _btnDelete });
            split.Panel2.Controls.Add(rightButtons);

            LoadSuites();
        }

        private void LoadSuites()
        {
            _lstSuites.Items.Clear();
            var suites = TestSuiteManager.GetAllTestSuites();
            foreach (var s in suites)
                _lstSuites.Items.Add(s);
        }

        private void LoadSelectedSuite()
        {
            if (_lstSuites.SelectedItem == null) return;

            var suiteName = _lstSuites.SelectedItem.ToString();
            if (suiteName == null) return;

            var path = Path.Combine(TestSuiteManager.TestSuitesDir, suiteName);
            _currentSuite = TestSuiteManager.LoadTestSuite(path);
            
            _txtName.Text = _currentSuite.Name;
            _lstTests.Items.Clear();
            
            foreach (var test in _currentSuite.TestCases)
                _lstTests.Items.Add($"{(test.Enabled ? "✅" : "⏸️")} {test.TestName} - {test.FlowPath}");
        }

        private void CreateNewSuite()
        {
            var name = Prompt.ShowDialog("Suite Name:", "New Test Suite", "TestSuite1");
            if (string.IsNullOrWhiteSpace(name)) return;

            var suite = new TestSuite { Name = name };
            TestSuiteManager.SaveTestSuite(suite);
            LoadSuites();
        }

        private void AddTest()
        {
            if (_currentSuite == null) return;

            using var ofd = new OpenFileDialog
            {
                InitialDirectory = FlowStorage.FlowsDir,
                Filter = "flow.json|flow.json",
                Title = "Select Flow"
            };

            if (ofd.ShowDialog() != DialogResult.OK) return;

            var relativePath = Path.GetRelativePath(FlowStorage.FlowsDir, ofd.FileName);
            var testName = Prompt.ShowDialog("Test Name:", "Add Test", Path.GetFileNameWithoutExtension(ofd.FileName));
            if (string.IsNullOrWhiteSpace(testName)) return;

            _currentSuite.TestCases.Add(new TestCase
            {
                FlowPath = relativePath,
                TestName = testName,
                Enabled = true
            });

            TestSuiteManager.SaveTestSuite(_currentSuite);
            LoadSelectedSuite();
        }

        private async System.Threading.Tasks.Task RunSuite()
        {
            if (_currentSuite == null) return;

            var report = await TestSuiteManager.RunTestSuiteAsync(_currentSuite);
            
            MessageBox.Show(
                $"Tests Completed!\n\n✅ Passed: {report.PassedTests}\n❌ Failed: {report.FailedTests}",
                "Results",
                MessageBoxButtons.OK,
                report.FailedTests == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning
            );
        }

        private void DeleteSuite()
        {
            if (_lstSuites.SelectedItem == null) return;

            var result = MessageBox.Show("Delete this suite?", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (result != DialogResult.Yes) return;

            var suiteName = _lstSuites.SelectedItem.ToString();
            if (suiteName == null) return;

            var path = Path.Combine(TestSuiteManager.TestSuitesDir, suiteName);
            try
            {
                File.Delete(path);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to delete suite: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            LoadSuites();
        }
    }

    // Simple input dialog helper
    public static class Prompt
    {
        public static string ShowDialog(string text, string caption, string defaultValue = "")
        {
            Form prompt = new Form()
            {
                Width = 400,
                Height = 150,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                Text = caption,
                StartPosition = FormStartPosition.CenterScreen,
                BackColor = Color.FromArgb(18, 22, 36),
                ForeColor = Color.Gainsboro
            };
            
            Label textLabel = new Label() 
            { 
                Left = 20, 
                Top = 20, 
                Text = text,
                Width = 360,
                ForeColor = Color.Gainsboro
            };
            
            TextBox textBox = new TextBox() 
            { 
                Left = 20, 
                Top = 50, 
                Width = 340,
                Text = defaultValue,
                BackColor = Color.FromArgb(30, 34, 46),
                ForeColor = Color.Gainsboro
            };
            
            Button confirmation = new Button() 
            { 
                Text = "OK", 
                Left = 260, 
                Width = 100, 
                Top = 80, 
                DialogResult = DialogResult.OK 
            };
            
            confirmation.Click += (sender, e) => { prompt.Close(); };
            
            prompt.Controls.Add(textLabel);
            prompt.Controls.Add(textBox);
            prompt.Controls.Add(confirmation);
            prompt.AcceptButton = confirmation;

            return prompt.ShowDialog() == DialogResult.OK ? textBox.Text : "";
        }
    }
}
