using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FlowRunner
{
    public sealed partial class MainForm : Form
    {
        // ===== State =====
        private FlowDefinition _flow = new();
        private bool _isRecording;
        private bool _isPaused;
        private volatile bool _stopRequested;
        private volatile bool _isSelectingRegion;
        private volatile bool _isRunning;
        private CancellationTokenSource? _runCts;
        private int _printNameStepCounter;

        // timing
        private long _lastEventTick;

        private readonly GlobalHotkeys _hotkeys = new();

        // ===== Batch Run State =====
        private readonly Button _btnRunSelected = new();
        private readonly Button _btnRunAllChecked = new();
        private readonly Button _btnStopBatch = new();
        private readonly Label _lblBatchProgress = new();
        private readonly Label _lblBatchStats = new();
        private CancellationTokenSource? _batchCts;
        private bool _isBatchRunning;

        // ===== UI =====
        private readonly CheckedListBox _chkCategories = new();
        private readonly ListBox _lstFlows = new();
        private readonly Panel _flowPanel = new();
        private readonly Panel _right = new();
        private readonly Panel _canvas = new();

        private readonly TextBox _txtCategory = new();
        private readonly TextBox _txtName = new();
        private readonly NumericUpDown _numLoops = new();

        private readonly Button _btnNew = new();
        private readonly Button _btnRecord = new();
        private readonly Button _btnPause = new();
        private readonly Button _btnSave = new();
        private readonly Button _btnRun = new();
        private readonly Button _btnLoad = new();
        private readonly Button _btnDelete = new();

        private readonly StatusStrip _status = new();
        private readonly ToolStripStatusLabel _lbl = new();
        private readonly ToolStripStatusLabel _lblClock = new();
        private readonly System.Windows.Forms.Timer _clockTimer = new();

        private readonly ToolTip _tooltips = new();
        private readonly Dictionary<string, RunOutcome> _runOutcomes = new(StringComparer.OrdinalIgnoreCase);

        private readonly SplitContainer _split = new();
        private readonly PictureBox _previewExpected = new();
        private readonly PictureBox _previewActual = new();

        private readonly Panel _canvasView = new();

        // Zoom / comparison canvas state
        private enum ZoomMode { FitToCanvas, ActualSize, Custom }
        private ZoomMode _canvasZoomMode = ZoomMode.ActualSize;
        private float _canvasZoomLevel = 1.0f;
        private Bitmap? _canvasImage;
        private string? _canvasLabel;

        private readonly System.Windows.Forms.Timer _clock = new() { Interval = 1000 };
        private readonly ToolTip _tooltip = new();

        public MainForm()
        {
            Text = "FlowRunner";
            Width = 1200;
            Height = 760;
            StartPosition = FormStartPosition.CenterScreen;

            BackColor = Color.FromArgb(14, 18, 30);
            ForeColor = Color.Gainsboro;
            Font = new Font("Segoe UI", 10f, FontStyle.Regular);

            _lbl.Text = "Ready";
            _lblClock.Text = DateTime.Now.ToString("HH:mm:ss");
            _lblClock.Alignment = ToolStripItemAlignment.Right;
            _status.Items.Add(_lbl);
            _status.Items.Add(new ToolStripStatusLabel { Spring = true });
            _status.Items.Add(_lblClock);
            Controls.Add(_status);

            _clockTimer.Interval = 1000;
            _clockTimer.Tick += (_, __) => _lblClock.Text = DateTime.Now.ToString("HH:mm:ss");
            _clockTimer.Start();

            // ===== Flow Selector Panel (Left) =====
            _flowPanel.Dock = DockStyle.Left;
            _flowPanel.Width = 280;
            _flowPanel.BackColor = Color.FromArgb(18, 22, 36);

            _chkCategories.Dock = DockStyle.Top;
            _chkCategories.Height = 120;
            _chkCategories.BackColor = Color.FromArgb(30, 34, 46);
            _chkCategories.ForeColor = Color.Gainsboro;
            _chkCategories.BorderStyle = BorderStyle.None;
            _chkCategories.Font = new Font("Segoe UI", 9.5f);
            _chkCategories.CheckOnClick = true;
            _chkCategories.ItemCheck += ChkCategories_ItemCheck;

            var flowBtnsPanel = new Panel { Dock = DockStyle.Bottom, Height = 36, BackColor = Color.FromArgb(18, 22, 36) };
            var btnRefresh = new Button
            {
                Text = "🔄",
                Width = 40,
                Dock = DockStyle.Left,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(22, 28, 45),
                ForeColor = Color.Gainsboro,
                Cursor = Cursors.Hand
            };
            btnRefresh.FlatAppearance.BorderColor = Color.FromArgb(40, 60, 100);
            var btnOpenFolder = new Button
            {
                Text = "📁 Open Folder",
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(22, 28, 45),
                ForeColor = Color.Gainsboro,
                Cursor = Cursors.Hand
            };
            btnOpenFolder.FlatAppearance.BorderColor = Color.FromArgb(40, 60, 100);
            btnRefresh.Click += (_, __) => RefreshFlowSelector();
            btnOpenFolder.Click += (_, __) => OpenFlowsFolder();
            _tooltips.SetToolTip(btnRefresh, "Refresh flow list");
            _tooltips.SetToolTip(btnOpenFolder, "Open flows folder in Explorer");
            flowBtnsPanel.Controls.Add(btnOpenFolder);
            flowBtnsPanel.Controls.Add(btnRefresh);

            _lstFlows.Dock = DockStyle.Fill;
            _lstFlows.BackColor = Color.FromArgb(18, 22, 36);
            _lstFlows.ForeColor = Color.Gainsboro;
            _lstFlows.BorderStyle = BorderStyle.None;
            _lstFlows.DrawMode = DrawMode.OwnerDrawFixed;
            _lstFlows.ItemHeight = 24;
            _lstFlows.SelectionMode = SelectionMode.MultiExtended;
            _lstFlows.DrawItem += LstFlows_DrawItem;
            _lstFlows.SelectedIndexChanged += (_, __) => { LoadSelectedFlowToEditor(); UpdateBatchButtons(); };
            _lstFlows.DoubleClick += (_, __) => DoRunSelected();

            _flowPanel.Controls.Add(_lstFlows);
            _flowPanel.Controls.Add(flowBtnsPanel);
            _flowPanel.Controls.Add(_chkCategories);
            Controls.Add(_flowPanel);

            _right.Dock = DockStyle.Right;
            _right.Width = 300;
            _right.Padding = new Padding(12);
            _right.BackColor = Color.FromArgb(18, 22, 36);
            Controls.Add(_right);

            _canvas.Dock = DockStyle.Fill;
            _canvas.BackColor = Color.FromArgb(14, 18, 30);
            _canvas.Padding = new Padding(12);
            Controls.Add(_canvas);

            // ===== SplitContainer (Expected | Actual/Overlay) =====
            _split.Dock = DockStyle.Fill;
            _split.Orientation = Orientation.Vertical;
            _split.SplitterWidth = 6;
            _split.BackColor = Color.FromArgb(14, 18, 30);

            // ✅ مهم: MinSize ها را از اول 0 کن تا هیچ وقت SplitterDistance crash نده
            _split.Panel1MinSize = 0;
            _split.Panel2MinSize = 0;

            _canvas.Controls.Add(_split);

            SetupPreviewBox(_previewExpected);
            SetupPreviewBox(_previewActual);

            _split.Panel1.Controls.Add(_previewExpected);
            _split.Panel2.Controls.Add(_previewActual);

            // Custom-painted canvas view for zoom/comparison display
            _canvasView.Dock = DockStyle.Fill;
            _canvasView.BackColor = Color.FromArgb(18, 22, 36);
            _canvasView.Visible = false;
            _canvasView.Paint += Canvas_Paint;
            _canvasView.MouseWheel += Canvas_MouseWheel;
            _canvas.Controls.Add(_canvasView);

            // Zoom controls panel (added last = processed first = docks to bottom)
            var zoomPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 35,
                BackColor = Color.FromArgb(23, 32, 42),
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(5)
            };

            var btnZoomOut = CreateZoomButton("➖", "Zoom out");
            btnZoomOut.Click += (_, __) => AdjustZoom(-0.1f);
            var btnZoomReset = CreateZoomButton("1:1", "Actual size (100%)");
            btnZoomReset.Click += (_, __) => SetZoom(ZoomMode.ActualSize);
            var btnZoomIn = CreateZoomButton("➕", "Zoom in");
            btnZoomIn.Click += (_, __) => AdjustZoom(0.1f);
            var btnZoomFit = CreateZoomButton("⊡", "Fit to window");
            btnZoomFit.Click += (_, __) => SetZoom(ZoomMode.FitToCanvas);

            zoomPanel.Controls.Add(btnZoomOut);
            zoomPanel.Controls.Add(btnZoomReset);
            zoomPanel.Controls.Add(btnZoomIn);
            zoomPanel.Controls.Add(btnZoomFit);
            _canvas.Controls.Add(zoomPanel);

            // ✅ SplitterDistance را فقط بعد از اینکه فرم واقعاً اندازه گرفت ست کن
            Shown += (_, __) => BeginInvoke(new Action(SafeInitSplit));
            _split.SizeChanged += (_, __) => SafeInitSplit();

            _txtCategory.Text = "General";
            _txtName.Text = "MyFlow";

            _txtCategory.Dock = DockStyle.Top;
            _txtName.Dock = DockStyle.Top;

            _txtCategory.BackColor = Color.FromArgb(22, 28, 45);
            _txtCategory.ForeColor = Color.Gainsboro;
            _txtName.BackColor = Color.FromArgb(22, 28, 45);
            _txtName.ForeColor = Color.Gainsboro;

            _numLoops.Dock = DockStyle.Top;
            _numLoops.Minimum = 1;
            _numLoops.Maximum = 999;
            _numLoops.Value = 1;
            _numLoops.BackColor = Color.FromArgb(22, 28, 45);
            _numLoops.ForeColor = Color.Gainsboro;

            SetupButton(_btnNew, "📝 New");
            SetupButton(_btnRecord, "⏺️ Record (F9)", accent: true);
            SetupButton(_btnPause, "⏸️ Pause (F10)");
            SetupButton(_btnSave, "💾 Save", accent: true);
            SetupButton(_btnRun, "▶️ Run (F11)");
            SetupButton(_btnLoad, "📂 Load...");
            SetupButton(_btnDelete, "🗑️ Delete", danger: true);

            _right.Controls.Add(MakeLabel("Actions"));
            _right.Controls.Add(_btnDelete);
            _right.Controls.Add(_btnLoad);
            _right.Controls.Add(_btnRun);
            _right.Controls.Add(_btnSave);
            _right.Controls.Add(_btnPause);
            _right.Controls.Add(_btnRecord);
            _right.Controls.Add(_btnNew);

            _right.Controls.Add(Spacer(14));
            _right.Controls.Add(MakeLabel("Loops"));
            _right.Controls.Add(_numLoops);

            _right.Controls.Add(Spacer(14));
            _right.Controls.Add(MakeLabel("Flow name"));
            _right.Controls.Add(_txtName);

            _right.Controls.Add(Spacer(14));
            _right.Controls.Add(MakeLabel("Category"));
            _right.Controls.Add(_txtCategory);

            _btnNew.Click += (_, __) => DoNew();
            _btnRecord.Click += (_, __) => ToggleRecord();
            _btnPause.Click += (_, __) => TogglePause();
            _btnSave.Click += (_, __) => DoSave();
            _btnRun.Click += (_, __) => DoRunSelected();
            _btnLoad.Click += (_, __) => DoLoadDialog();
            _btnDelete.Click += (_, __) => DoDeleteSelected();

            _tooltips.SetToolTip(_btnNew, "Create a new empty flow");
            _tooltips.SetToolTip(_btnRecord, "Start/stop recording mouse events (F9)");
            _tooltips.SetToolTip(_btnPause, "Pause or resume recording (F10)");
            _tooltips.SetToolTip(_btnSave, "Save current flow to disk");
            _tooltips.SetToolTip(_btnRun, "Run the selected flow (F11)");
            _tooltips.SetToolTip(_btnLoad, "Load a flow from file");
            _tooltips.SetToolTip(_btnDelete, "Permanently delete the selected flow");

            InitializeTestSuiteUI();
            AddBatchButtons();

            _hotkeys.KeyPressed += OnHotkey;
            _hotkeys.Start();
            FormClosed += (_, __) => { try { _hotkeys.Dispose(); } catch { } try { _clockTimer.Dispose(); } catch { } };

            Directory.CreateDirectory(FlowStorage.FlowsDir);
            RefreshFlowSelector();

            DoNew();
            UpdateUi();
            AddTooltips();

            AppLog.Info("MainForm initialized.");
        }

        private sealed class RunOutcome
        {
            public bool HasMismatch { get; set; }
            public string? LastMismatchExpectedPath { get; set; }
            public string? LastMismatchImagePath { get; set; } // overlay/actual
            public string? LastMismatchStep { get; set; }
            public DateTime LastRunUtc { get; set; }
        }

        private static void SetupButton(Button b, string text, bool accent = false, bool danger = false)
        {
            b.Text = text;
            b.Dock = DockStyle.Top;
            b.Height = 40;

            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.BorderColor = Color.FromArgb(40, 60, 100);

            b.ForeColor = Color.White;
            b.Cursor = Cursors.Hand;

            if (danger)
                b.BackColor = Color.FromArgb(110, 35, 45);
            else if (accent)
                b.BackColor = Color.FromArgb(60, 40, 140);
            else
                b.BackColor = Color.FromArgb(22, 28, 45);

            var normal = b.BackColor;
            b.MouseEnter += (_, __) => b.BackColor = ControlPaint.Light(normal, 0.15f);
            b.MouseLeave += (_, __) => b.BackColor = normal;
        }

        private static Control Spacer(int h) => new Panel { Dock = DockStyle.Top, Height = h };

        private static Label MakeLabel(string text) => new Label
        {
            Text = text,
            Dock = DockStyle.Top,
            Height = 18,
            ForeColor = Color.Gainsboro
        };

        private void SetStatus(string s) => _lbl.Text = s;

        // ============= Flow Selector =============
        private void RefreshFlowSelector()
        {
            Directory.CreateDirectory(FlowStorage.FlowsDir);

            // Remember which categories were checked
            var checkedNames = _chkCategories.CheckedItems
                .Cast<object>()
                .Select(o => o.ToString() ?? "")
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            _chkCategories.ItemCheck -= ChkCategories_ItemCheck;
            _chkCategories.Items.Clear();

            var categories = Directory.GetDirectories(FlowStorage.FlowsDir)
                .Select(d =>
                {
                    var name = Path.GetFileName(d);
                    var flowCount = Directory.GetDirectories(d)
                        .Count(fd => File.Exists(Path.Combine(fd, "flow.json")));
                    return (name, flowCount);
                })
                .Where(c => !string.IsNullOrEmpty(c.name))
                .OrderBy(c => c.name)
                .ToList();

            if (categories.Count == 0)
            {
                Directory.CreateDirectory(Path.Combine(FlowStorage.FlowsDir, "General"));
                categories.Add(("General", 0));
            }

            foreach (var (name, count) in categories)
                _chkCategories.Items.Add($"{name} ({count} flows)");

            // Restore checked state
            for (int i = 0; i < _chkCategories.Items.Count; i++)
            {
                var rawName = GetCategoryNameFromItem(_chkCategories.Items[i]?.ToString() ?? "");
                if (checkedNames.Contains(rawName))
                    _chkCategories.SetItemChecked(i, true);
            }

            _chkCategories.ItemCheck += ChkCategories_ItemCheck;

            UpdateFlowsFromCheckedCategories();
        }

        private void ChkCategories_ItemCheck(object? sender, ItemCheckEventArgs e)
        {
            BeginInvoke(() => UpdateFlowsFromCheckedCategories());
        }

        private static string GetCategoryNameFromItem(string item)
        {
            // Parse "CategoryName (N flows)" format
            var match = System.Text.RegularExpressions.Regex.Match(item, @"^(.+?)\s*\(\d+ flows\)$");
            return match.Success ? match.Groups[1].Value.Trim() : item.Trim();
        }

        private void UpdateFlowsFromCheckedCategories()
        {
            _lstFlows.Items.Clear();

            foreach (var item in _chkCategories.CheckedItems)
            {
                var category = GetCategoryNameFromItem(item?.ToString() ?? "");
                if (string.IsNullOrEmpty(category)) continue;

                var categoryDir = Path.Combine(FlowStorage.FlowsDir, FlowStorage.SafeFileName(category));
                if (!Directory.Exists(categoryDir)) continue;

                foreach (var flowDir in Directory.GetDirectories(categoryDir))
                {
                    var flowJsonPath = Path.Combine(flowDir, "flow.json");
                    if (File.Exists(flowJsonPath))
                    {
                        var flowName = Path.GetFileName(flowDir);
                        _lstFlows.Items.Add(new FlowItem { Name = flowName, Category = category, JsonPath = flowJsonPath });
                    }
                }
            }

            UpdateBatchButtons();
        }

        private void UpdateBatchButtons()
        {
            var selectedCount = _lstFlows.SelectedItems.Count;
            var totalCount = _lstFlows.Items.Count;

            _btnRunSelected.Text = $"▶️ Run Selected ({selectedCount})";
            _btnRunSelected.Enabled = selectedCount > 0 && !_isBatchRunning;

            _btnRunAllChecked.Text = $"▶️ Run All Checked ({totalCount})";
            _btnRunAllChecked.Enabled = totalCount > 0 && !_isBatchRunning;
        }

        private void AddBatchButtons()
        {
            var batchPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 120,
                BackColor = Color.FromArgb(18, 22, 36),
                Padding = new Padding(6),
                ColumnCount = 1,
                RowCount = 5
            };

            _btnRunSelected.Text = "▶️ Run Selected (0)";
            _btnRunSelected.Dock = DockStyle.Fill;
            _btnRunSelected.FlatStyle = FlatStyle.Flat;
            _btnRunSelected.BackColor = Color.FromArgb(155, 89, 182);
            _btnRunSelected.ForeColor = Color.White;
            _btnRunSelected.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            _btnRunSelected.Enabled = false;
            _btnRunSelected.Cursor = Cursors.Hand;
            _btnRunSelected.Click += async (_, __) => await RunSelectedFlowsAsync();
            batchPanel.Controls.Add(_btnRunSelected, 0, 0);

            _btnRunAllChecked.Text = "▶️ Run All Checked (0)";
            _btnRunAllChecked.Dock = DockStyle.Fill;
            _btnRunAllChecked.FlatStyle = FlatStyle.Flat;
            _btnRunAllChecked.BackColor = Color.FromArgb(46, 204, 113);
            _btnRunAllChecked.ForeColor = Color.White;
            _btnRunAllChecked.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            _btnRunAllChecked.Enabled = false;
            _btnRunAllChecked.Cursor = Cursors.Hand;
            _btnRunAllChecked.Click += async (_, __) => await RunAllCheckedFlowsAsync();
            batchPanel.Controls.Add(_btnRunAllChecked, 0, 1);

            _btnStopBatch.Text = "⏹️ Stop Batch";
            _btnStopBatch.Dock = DockStyle.Fill;
            _btnStopBatch.FlatStyle = FlatStyle.Flat;
            _btnStopBatch.BackColor = Color.FromArgb(192, 57, 43);
            _btnStopBatch.ForeColor = Color.White;
            _btnStopBatch.Font = new Font("Segoe UI", 9f);
            _btnStopBatch.Visible = false;
            _btnStopBatch.Cursor = Cursors.Hand;
            _btnStopBatch.Click += (_, __) => StopBatchRun();
            batchPanel.Controls.Add(_btnStopBatch, 0, 2);

            _lblBatchProgress.Text = "Ready";
            _lblBatchProgress.Dock = DockStyle.Fill;
            _lblBatchProgress.ForeColor = Color.Gainsboro;
            _lblBatchProgress.Font = new Font("Segoe UI", 9f);
            _lblBatchProgress.TextAlign = ContentAlignment.MiddleLeft;
            batchPanel.Controls.Add(_lblBatchProgress, 0, 3);

            _lblBatchStats.Text = "✅ 0 | ❌ 0";
            _lblBatchStats.Dock = DockStyle.Fill;
            _lblBatchStats.ForeColor = Color.Gray;
            _lblBatchStats.Font = new Font("Segoe UI", 8.5f);
            _lblBatchStats.TextAlign = ContentAlignment.MiddleLeft;
            batchPanel.Controls.Add(_lblBatchStats, 0, 4);

            _flowPanel.Controls.Add(batchPanel);
        }

        private async Task RunSelectedFlowsAsync()
        {
            if (_isBatchRunning || _lstFlows.SelectedItems.Count == 0) return;

            var flows = _lstFlows.SelectedItems.Cast<FlowItem>().ToList();
            await RunBatchAsync(flows);
        }

        private async Task RunAllCheckedFlowsAsync()
        {
            if (_isBatchRunning || _lstFlows.Items.Count == 0) return;

            var flows = _lstFlows.Items.Cast<FlowItem>().ToList();
            await RunBatchAsync(flows);
        }

        private async Task RunBatchAsync(List<FlowItem> flows)
        {
            if (flows.Count == 0) return;

            _isBatchRunning = true;
            _batchCts = new CancellationTokenSource();

            _btnRunSelected.Enabled = false;
            _btnRunAllChecked.Enabled = false;
            _btnStopBatch.Visible = true;

            int total = flows.Count;
            int passed = 0;
            int failed = 0;
            var failedFlows = new List<(string flow, string reason)>();
            var startTime = DateTime.Now;

            AppLog.Info($"Starting batch run of {total} flows");

            for (int i = 0; i < total; i++)
            {
                if (_batchCts.Token.IsCancellationRequested)
                {
                    SetStatus("⏹️ Batch run cancelled");
                    break;
                }

                var flowItem = flows[i];

                _lblBatchProgress.Text = $"⏳ Running {i + 1}/{total}: {flowItem.Name}";
                SetStatus($"Batch {i + 1}/{total}: {flowItem.Category}/{flowItem.Name}");

                try
                {
                    if (!File.Exists(flowItem.JsonPath))
                    {
                        failed++;
                        failedFlows.Add(($"{flowItem.Category}/{flowItem.Name}", "Flow file not found"));
                        _lblBatchStats.Text = $"✅ {passed} | ❌ {failed}";
                        continue;
                    }

                    var flow = FlowStorage.LoadFlow(flowItem.JsonPath);

                    _runOutcomes[flowItem.JsonPath] = new RunOutcome
                    {
                        HasMismatch = false,
                        LastRunUtc = DateTime.UtcNow
                    };

                    await RunFlowAsync(flow, flowItem.JsonPath);

                    if (_runOutcomes.TryGetValue(flowItem.JsonPath, out var outcome) && !outcome.HasMismatch)
                    {
                        passed++;
                        AppLog.Info($"✅ {flowItem.Category}/{flowItem.Name} passed");
                    }
                    else
                    {
                        failed++;
                        var reason = (outcome?.HasMismatch == true ? outcome.LastMismatchStep : null) ?? "Run failed";
                        failedFlows.Add(($"{flowItem.Category}/{flowItem.Name}", reason));
                        AppLog.Warn($"❌ {flowItem.Category}/{flowItem.Name} failed: {reason}");
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    failedFlows.Add(($"{flowItem.Category}/{flowItem.Name}", ex.Message));
                    AppLog.Exception($"Batch run error: {flowItem.Category}/{flowItem.Name}", ex);
                }

                _lblBatchStats.Text = $"✅ {passed} | ❌ {failed}";

                if (i < total - 1)
                {
                    try { await Task.Delay(500, _batchCts.Token); }
                    catch (OperationCanceledException) { break; }
                }
            }

            var duration = DateTime.Now - startTime;
            ShowBatchSummary(total, passed, failed, failedFlows, duration);

            _isBatchRunning = false;
            _btnStopBatch.Visible = false;
            _batchCts?.Dispose();
            _batchCts = null;
            UpdateBatchButtons();
        }

        private void StopBatchRun()
        {
            _batchCts?.Cancel();
            AppLog.Info("Batch run stop requested");
        }

        private void ShowBatchSummary(int total, int passed, int failed,
            List<(string flow, string reason)> failedFlows, TimeSpan duration)
        {
            var summary = new System.Text.StringBuilder();
            summary.AppendLine("Batch Test Summary");
            summary.AppendLine($"Total: {total} flows");
            if (total > 0)
            {
                summary.AppendLine($"✅ Passed: {passed} ({(double)passed / total * 100:F1}%)");
                summary.AppendLine($"❌ Failed: {failed} ({(double)failed / total * 100:F1}%)");
            }
            summary.AppendLine($"⏱️ Duration: {duration:mm\\:ss}");

            if (failedFlows.Count > 0)
            {
                summary.AppendLine();
                summary.AppendLine("Failed Flows:");
                foreach (var (flow, reason) in failedFlows)
                {
                    var shortReason = reason.Length > 60 ? reason[..57] + "..." : reason;
                    summary.AppendLine($"  • {flow}: {shortReason}");
                }
            }

            AppLog.Info(summary.ToString());

            _lblBatchProgress.Text = $"✅ Completed: {passed}/{total} passed";

            MessageBox.Show(summary.ToString(), "Batch Test Summary",
                MessageBoxButtons.OK,
                failed == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        private void LstFlows_DrawItem(object? sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _lstFlows.Items.Count) return;
            var item = _lstFlows.Items[e.Index] as FlowItem;
            if (item == null) return;

            bool selected = (e.State & DrawItemState.Selected) != 0;
            using (var backBrush = new SolidBrush(selected ? Color.FromArgb(60, 40, 140) : Color.FromArgb(18, 22, 36)))
                e.Graphics.FillRectangle(backBrush, e.Bounds);

            string icon;
            if (_runOutcomes.TryGetValue(item.JsonPath, out var o))
                icon = o.HasMismatch ? "❌ " : "✅ ";
            else
                icon = "   ";

            using var textBrush = new SolidBrush(Color.Gainsboro);
            e.Graphics.DrawString(icon + item.ToString(), e.Font ?? _lstFlows.Font, textBrush,
                e.Bounds.X + 4, e.Bounds.Y + 4);
            e.DrawFocusRectangle();
        }

        private void OpenFlowsFolder()
        {
            try
            {
                Directory.CreateDirectory(FlowStorage.FlowsDir);
                System.Diagnostics.Process.Start("explorer.exe", FlowStorage.FlowsDir);
            }
            catch (Exception ex)
            {
                AppLog.Exception("OpenFlowsFolder failed", ex);
                SetStatus("Could not open folder: " + ex.Message);
            }
        }

        private sealed class FlowItem
        {
            public string Name { get; set; } = "";
            public string Category { get; set; } = "";
            public string JsonPath { get; set; } = "";
            public override string ToString() => $"[{Category}] {Name}";
        }

        private string? GetSelectedFlowJsonPath()
        {
            return (_lstFlows.SelectedItem as FlowItem)?.JsonPath;
        }

        private void LoadSelectedFlowToEditor()
        {
            var path = GetSelectedFlowJsonPath();
            if (path == null) return;

            try
            {
                _flow = FlowStorage.LoadFlow(path);
                PushModelToEditor();
                ShowLastCheckpointIfAny(path);

                if (_runOutcomes.TryGetValue(path, out var o) &&
                    o.HasMismatch &&
                    !string.IsNullOrWhiteSpace(o.LastMismatchImagePath) &&
                    File.Exists(o.LastMismatchImagePath))
                {
                    ShowImagesOnCanvas(o.LastMismatchExpectedPath, o.LastMismatchImagePath);
                }

                SetStatus($"Loaded: {_flow.Category}/{_flow.Name}  Steps={_flow.Steps.Count}");
                AppLog.Info($"Loaded flow: {_flow.Category}/{_flow.Name} steps={_flow.Steps.Count} from={path}");
                UpdateUi();
            }
            catch (Exception ex)
            {
                AppLog.Exception("LoadSelectedFlowToEditor failed", ex);
                SetStatus("Load failed: " + ex.Message);
            }
        }

        // ============= Model <-> Editor =============
        private void PullEditorToModel()
        {
            _flow.Category = string.IsNullOrWhiteSpace(_txtCategory.Text) ? "General" : _txtCategory.Text.Trim();
            _flow.Name = string.IsNullOrWhiteSpace(_txtName.Text) ? "MyFlow" : _txtName.Text.Trim();
            _flow.Loops = (int)_numLoops.Value;
        }

        private void PushModelToEditor()
        {
            _txtCategory.Text = string.IsNullOrWhiteSpace(_flow.Category) ? "General" : _flow.Category;
            _txtName.Text = string.IsNullOrWhiteSpace(_flow.Name) ? "MyFlow" : _flow.Name;
            _numLoops.Value = Math.Max(_numLoops.Minimum, Math.Min(_numLoops.Maximum, _flow.Loops <= 0 ? 1 : _flow.Loops));
        }

        // ============= Hotkeys =============
        private void OnHotkey(Keys k)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => OnHotkey(k)));
                return;
            }

            bool isStopKey = (k == Keys.Escape || k == Keys.F8);

            if (_isRunning && !isStopKey)
            {
                SetStatus($"Running... (Only {_hotkeys.StopHotkeyText} works)");
                return;
            }

            if (k == Keys.F9) ToggleRecord();
            else if (k == Keys.F10) TogglePause();
            else if (k == Keys.F11) DoRunSelected();
            else if (k == Keys.F12) _ = CreateCheckpointAsync();
            else if (k == Keys.F7) AddTypePrintFileNameStep();
            else if (isStopKey) EmergencyStop();
        }

        // ============= Actions =============
        private void DoNew()
        {
            _flow = new FlowDefinition
            {
                Category = string.IsNullOrWhiteSpace(_txtCategory.Text) ? "General" : _txtCategory.Text.Trim(),
                Name = string.IsNullOrWhiteSpace(_txtName.Text) ? "MyFlow" : _txtName.Text.Trim(),
                Loops = (int)_numLoops.Value
            };

            _isRecording = false;
            _isPaused = false;
            _stopRequested = false;

            _previewExpected.Image = null;
            _previewActual.Image = null;

            SetStatus("New flow.");
            AppLog.Info($"New flow: {_flow.Category}/{_flow.Name} loops={_flow.Loops}");
            UpdateUi();
        }

        private void ToggleRecord()
        {
            if (!_isRecording)
            {
                PullEditorToModel();

                if (string.IsNullOrWhiteSpace(_flow.Name) || _flow.Name == "MyFlow")
                {
                    _flow.Name = "flow_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    _txtName.Text = _flow.Name;
                }

                _flow.Steps.Clear();
                _printNameStepCounter = 0;

                _isRecording = true;
                _isPaused = false;
                _stopRequested = false;

                _lastEventTick = Environment.TickCount64;

                Win32Hooks.MouseEvent += OnMouseEvent;
                Win32Hooks.Start();

                SetStatus($"Recording... ({_hotkeys.CheckpointHotkeyText} checkpoint ROI)  F9 stop");
                AppLog.Info($"Recording START: {_flow.Category}/{_flow.Name}");
            }
            else
            {
                StopRecording(saveAfterStop: false);
            }

            UpdateUi();
        }

        private void TogglePause()
        {
            if (!_isRecording)
            {
                SetStatus("Not recording.");
                return;
            }

            _isPaused = !_isPaused;
            _lastEventTick = Environment.TickCount64;

            SetStatus(_isPaused ? "Paused." : "Recording...");
            AppLog.Info(_isPaused ? "Recording PAUSED" : "Recording RESUMED");
            UpdateUi();
        }

        private void StopRecording(bool saveAfterStop)
        {
            try
            {
                var p = Cursor.Position;
                _flow.Steps.Add(new FlowStep { Kind = FlowStepKind.Move, X = p.X, Y = p.Y, DelayMs = 0 });
            }
            catch { }

            _isRecording = false;
            _isPaused = false;

            try
            {
                Win32Hooks.MouseEvent -= OnMouseEvent;
                Win32Hooks.Stop();
            }
            catch { }

            SetStatus($"Recording stopped. Steps={_flow.Steps.Count}");
            AppLog.Info($"Recording STOP: {_flow.Category}/{_flow.Name} steps={_flow.Steps.Count}");

            if (saveAfterStop)
                DoSave();

            UpdateUi();
        }

        private void EmergencyStop()
        {
            if (!_isRunning && !_isRecording)
            {
                SetStatus("STOP ignored (not running).");
                return;
            }

            _stopRequested = true;

            try { _runCts?.Cancel(); } catch { }

            AppLog.Warn("STOP requested (ESC).");

            if (_isRecording)
                StopRecording(saveAfterStop: false);
            else
                SetStatus("STOP requested.");
        }

        private void OnMouseEvent(int x, int y, int msg, int mouseData)
        {
            if (_isSelectingRegion) return;
            if (!_isRecording || _isPaused) return;

            long now = Environment.TickCount64;
            int delay = (int)Math.Clamp(now - _lastEventTick, 0, 5000);
            _lastEventTick = now;

            if (msg == Win32Hooks.WM_LBUTTONDOWN)
            {
                if (_flow.Steps.Count > 0)
                {
                    var last = _flow.Steps[^1];
                    if (last.Kind == FlowStepKind.ClickLeft &&
                        last.X == x && last.Y == y &&
                        delay < 80)
                        return;
                }

                _flow.Steps.Add(new FlowStep { Kind = FlowStepKind.ClickLeft, X = x, Y = y, DelayMs = delay });
                return;
            }

            if (msg == Win32Hooks.WM_RBUTTONDOWN)
            {
                if (_flow.Steps.Count > 0)
                {
                    var last = _flow.Steps[^1];
                    if (last.Kind == FlowStepKind.ClickRight &&
                        last.X == x && last.Y == y &&
                        delay < 80)
                        return;
                }

                _flow.Steps.Add(new FlowStep { Kind = FlowStepKind.ClickRight, X = x, Y = y, DelayMs = delay });
                return;
            }

            if (msg == Win32Hooks.WM_MOUSEMOVE)
            {
                _flow.Steps.Add(new FlowStep { Kind = FlowStepKind.Move, X = x, Y = y, DelayMs = delay });
                return;
            }

            if (msg == Win32Hooks.WM_MOUSEWHEEL)
            {
                int delta = mouseData;
                _flow.Steps.Add(new FlowStep { Kind = FlowStepKind.Wheel, WheelDelta = delta, DelayMs = delay });
                return;
            }
        }

        private async Task CreateCheckpointAsync()
        {
            if (!_isRecording)
            {
                SetStatus("Checkpoint ignored (not recording).");
                return;
            }

            bool prevPaused = _isPaused;
            Bitmap? frozen = null;

            try
            {
                PullEditorToModel();

                var flowDir = FlowStorage.GetFlowDir(_flow.Category, _flow.Name);
                var imagesDir = Path.Combine(flowDir, "images");
                Directory.CreateDirectory(imagesDir);

                var fileName = $"cp_{_flow.Steps.Count + 1:000}_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                var fullPath = Path.Combine(imagesDir, fileName);

                _isSelectingRegion = true;
                _isPaused = true;

                var vs = GetVirtualScreenRect();
                SetStatus("Capturing screen...");
                frozen = await Task.Run(() => CaptureVirtualScreen());

                SetStatus($"Select region ({_hotkeys.CheckpointHotkeyText}) - ESC cancel...");
                var roi = RegionSelectorForm.Pick(frozen, vs);

                if (roi.IsEmpty)
                {
                    SetStatus("Checkpoint canceled.");
                    return;
                }

                var cropRect = new Rectangle(
                    roi.X - vs.Left,
                    roi.Y - vs.Top,
                    roi.Width,
                    roi.Height);

                cropRect.Intersect(new Rectangle(0, 0, frozen.Width, frozen.Height));
                if (cropRect.Width <= 0 || cropRect.Height <= 0)
                {
                    SetStatus("Checkpoint failed: invalid ROI.");
                    return;
                }

                using var cropped = frozen.Clone(cropRect, PixelFormat.Format32bppArgb);
                cropped.Save(fullPath, ImageFormat.Png);

                _flow.Steps.Add(new FlowStep
                {
                    Kind = FlowStepKind.Checkpoint,
                    Name = $"Checkpoint {_flow.Steps.Count + 1}",
                    ImagePath = Path.Combine("images", fileName),
                    DelayMs = 0,
                    RoiX = roi.X,
                    RoiY = roi.Y,
                    RoiW = roi.Width,
                    RoiH = roi.Height
                });

                ShowImageOnCanvas(fullPath);
                SetStatus($"Checkpoint saved (ROI): {fileName}");
                AppLog.Info($"Checkpoint saved ROI: flow={_flow.Category}/{_flow.Name} roi={roi} file={fullPath}");
            }
            catch (Exception ex)
            {
                AppLog.Exception("CreateCheckpoint failed", ex);
                SetStatus("Checkpoint failed: " + ex.Message);
            }
            finally
            {
                frozen?.Dispose();
                _isSelectingRegion = false;
                _isPaused = prevPaused;
                _lastEventTick = Environment.TickCount64;
            }
        }

        private void AddTypePrintFileNameStep()
        {
            if (!_isRecording)
            {
                SetStatus("F7 ignored (not recording).");
                return;
            }

            string prefix = (_printNameStepCounter % 2 == 0) ? "Kuche" : "Rechnung";
            _printNameStepCounter++;

            _flow.Steps.Add(new FlowStep
            {
                Kind = FlowStepKind.TypeFlowNameAndEnter,
                Name = prefix,
                DelayMs = 0
            });

            SetStatus($"Added: {prefix} filename step (F7)");
            AppLog.Info($"Added TypeFlowNameAndEnter step prefix={prefix}");
        }

        private void DoSave()
        {
            try
            {
                PullEditorToModel();

                if (_flow.Steps.Count == 0)
                {
                    SetStatus("Nothing to save (0 steps).");
                    return;
                }

                FlowStorage.SaveFlow(_flow);
                RefreshFlowSelector();

                SetStatus($"Saved: Documents\\FlowRunner\\flows\\{FlowStorage.SafeFileName(_flow.Category)}\\{FlowStorage.SafeFileName(_flow.Name)}\\flow.json");
                AppLog.Info($"Saved flow: {_flow.Category}/{_flow.Name} steps={_flow.Steps.Count}");
            }
            catch (Exception ex)
            {
                AppLog.Exception("DoSave failed", ex);
                MessageBox.Show(this, ex.ToString(), "SAVE FAILED", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetStatus("Save failed: " + ex.Message);
            }
        }

        private void DoLoadDialog()
        {
            try
            {
                using var ofd = new OpenFileDialog
                {
                    InitialDirectory = FlowStorage.FlowsDir,
                    Filter = "flow.json|flow.json|JSON (*.json)|*.json",
                    Title = "Select flow.json"
                };

                if (ofd.ShowDialog(this) != DialogResult.OK) return;

                _flow = FlowStorage.LoadFlow(ofd.FileName);
                PushModelToEditor();
                ShowLastCheckpointIfAny(ofd.FileName);

                RefreshFlowSelector();
                SetStatus($"Loaded: {_flow.Category}/{_flow.Name}");
                AppLog.Info($"Loaded via dialog: {_flow.Category}/{_flow.Name} from={ofd.FileName}");

                UpdateUi();
            }
            catch (Exception ex)
            {
                AppLog.Exception("DoLoadDialog failed", ex);
                SetStatus("Load failed: " + ex.Message);
            }
        }

        private void DoRunSelected()
        {
            if (_isRunning)
            {
                SetStatus("Already running.");
                return;
            }

            _stopRequested = false;
            try { _runCts?.Cancel(); } catch { }
            try { _runCts?.Dispose(); } catch { }
            _runCts = null;

            var path = GetSelectedFlowJsonPath();
            if (path == null)
            {
                SetStatus("Select a flow from the list (double-click also runs).");
                return;
            }

            try
            {
                _flow = FlowStorage.LoadFlow(path);
                PushModelToEditor();

                AppLog.Info($"Run requested: {_flow.Category}/{_flow.Name} steps={_flow.Steps.Count} loops={_flow.Loops} from={path}");
                _ = RunFlowAsync(_flow, path);
            }
            catch (Exception ex)
            {
                AppLog.Exception("DoRunSelected failed", ex);
                SetStatus("Run load failed: " + ex.Message);
            }
        }

        private async Task RunFlowAsync(FlowDefinition flow, string flowJsonPath)
        {
            AppLog.Info($"Run started: {flow.Category}/{flow.Name} steps={flow.Steps.Count} loops={flow.Loops}");

            _runOutcomes[flowJsonPath] = new RunOutcome
            {
                HasMismatch = false,
                LastMismatchExpectedPath = null,
                LastMismatchImagePath = null,
                LastMismatchStep = null,
                LastRunUtc = DateTime.UtcNow
            };
            RefreshFlowSelector();

            if (flow.Steps.Count == 0)
            {
                SetStatus("Flow has 0 steps.");
                return;
            }

            _isRunning = true;
            _stopRequested = false;

            var newCts = new CancellationTokenSource();
            var old = Interlocked.Exchange(ref _runCts, newCts);
            try { old?.Cancel(); } catch { }
            try { old?.Dispose(); } catch { }

            var ct = newCts.Token;

            int loops = Math.Max(1, flow.Loops);
            var stepsSnapshot = flow.Steps.ToArray();

            var flowDirForRun = FlowStorage.GetFlowDir(flow.Category, flow.Name);
            Directory.CreateDirectory(flowDirForRun);
            var runArtifactsDir = Path.Combine(flowDirForRun, "run_artifacts", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(runArtifactsDir);

            SetStatus($"Running: {flow.Category}/{flow.Name} loops={loops} (ESC stop)");
            SetControlsEnabled(false);

            int cpTotal = 0, cpOk = 0, cpFail = 0;

            try
            {
                for (int l = 1; l <= loops; l++)
                {
                    int printNameIndex = 0;
                    if (_stopRequested) break;

                    foreach (var s in stepsSnapshot)
                    {
                        if (_stopRequested) break;

                        if (s.DelayMs > 0)
                            await Task.Delay(s.DelayMs, ct);

                        switch (s.Kind)
                        {
                            case FlowStepKind.TypeFlowNameAndEnter:
                                {
                                    string n = (s.Name ?? "").Trim();
                                    bool isKnown = n.Equals("Kuche", StringComparison.OrdinalIgnoreCase) ||
                                                   n.Equals("Rechnung", StringComparison.OrdinalIgnoreCase);

                                    string prefix = isKnown ? n : (printNameIndex % 2 == 0 ? "Kuche" : "Rechnung");
                                    printNameIndex++;

                                    string flowName = SanitizeFileName(flow.Name);
                                    string loopPart = $"Loop{l:00}";
                                    string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm");
                                    string fileName = $"{prefix} - {flowName} - {loopPart} - {stamp}";
                                    fileName = SanitizeFileName(fileName);

                                    Win32SendInput.CtrlA();
                                    Win32SendInput.TypeText(fileName);
                                    Win32SendInput.KeyPress(Keys.Enter);
                                    break;
                                }

                            case FlowStepKind.Move:
                                Win32SendInput.Move(s.X, s.Y);
                                break;

                            case FlowStepKind.ClickLeft:
                                Win32SendInput.ClickLeft(s.X, s.Y);
                                break;

                            case FlowStepKind.ClickRight:
                                Win32SendInput.ClickRight(s.X, s.Y);
                                break;

                            case FlowStepKind.Wheel:
                                Win32SendInput.Wheel(s.WheelDelta);
                                break;

                            case FlowStepKind.Checkpoint:
                                {
                                    cpTotal++;

                                    const int timeoutMs = 5000;
                                    const int pollMs = 200;

                                    const double allowedDiffPercent = 0.05;
                                    const int sampleStep = 1;
                                    const int perChannelThreshold = 10;
                                    const int minBadSamples = 1;

                                    if (string.IsNullOrWhiteSpace(s.ImagePath))
                                        break;

                                    if (s.RoiW <= 0 || s.RoiH <= 0)
                                        throw new InvalidOperationException("Checkpoint has no ROI. Re-record checkpoint with F12.");

                                    var flowDir = FlowStorage.GetFlowDir(flow.Category, flow.Name);
                                    var expectedPath = Path.Combine(flowDir, s.ImagePath);

                                    if (!File.Exists(expectedPath))
                                        throw new FileNotFoundException("Checkpoint image not found", expectedPath);

                                    using var expected = LoadBitmapNoLock(expectedPath);

                                    var roiScreen = new Rectangle(s.RoiX, s.RoiY, s.RoiW, s.RoiH);
                                    var vs = GetVirtualScreenRect();

                                    if (!vs.Contains(roiScreen))
                                        throw new InvalidOperationException("Checkpoint ROI outside VirtualScreen. Check monitor layout.");

                                    var start = Environment.TickCount64;

                                    var cpShotsDir = Path.Combine(runArtifactsDir, "checkpoint_actual");
                                    Directory.CreateDirectory(cpShotsDir);

                                    Bitmap? lastActual = null;
                                    CheckpointComparer.Result? lastRes = null;

                                    while (true)
                                    {
                                        if (_stopRequested) break;

                                        lastActual?.Dispose();
                                        lastActual = CaptureScreenRect(roiScreen);

                                        // ✅ save last actual (overwrite)
                                        try
                                        {
                                            var safeStepName2 = (s.Name ?? "checkpoint").Replace(' ', '_');
                                            var latestActualPath = Path.Combine(cpShotsDir, $"LATEST_step_{safeStepName2}_actual.png");
                                            lastActual.Save(latestActualPath, ImageFormat.Png);
                                        }
                                        catch (Exception ex)
                                        {
                                            AppLog.Exception("Saving LATEST actual failed (ignored).", ex);
                                        }

                                        lastRes?.DiffImage?.Dispose();
                                        lastRes = CheckpointComparer.Compare(
                                            expected,
                                            lastActual,
                                            sampleStep: sampleStep,
                                            perChannelThreshold: perChannelThreshold,
                                            allowedDiffPercent: allowedDiffPercent,
                                            minBadSamples: minBadSamples,
                                            generateDiff: true
                                        );

                                        AppLog.Info($"Checkpoint diff: {s.Name} bad={lastRes.BadSamples}/{lastRes.TotalSamples} percent={lastRes.DiffPercent:0.000}%");

                                        bool ok = lastRes.DiffPercent <= allowedDiffPercent;

                                        if (ok)
                                        {
                                            cpOk++;
                                            SetStatus($"CP OK {cpOk}/{cpTotal}  {s.Name}  diff={lastRes.DiffPercent:0.000}%");

                                            ShowCheckpointSuccess(expected, lastRes.DiffPercent, allowedDiffPercent, cpTotal - 1);

                                            lastRes?.DiffImage?.Dispose();
                                            lastRes = null;

                                            lastActual.Dispose();
                                            lastActual = null;

                                            break;
                                        }

                                        if (Environment.TickCount64 - start >= timeoutMs)
                                        {
                                            cpFail++;

                                            var stampFail = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                                            var safeStepName = (s.Name ?? "checkpoint").Replace(' ', '_');
                                            var prefixFail = $"{stampFail}_step_{safeStepName}";

                                            var expOut = Path.Combine(runArtifactsDir, $"{prefixFail}_expected.png");
                                            var actOut = Path.Combine(runArtifactsDir, $"{prefixFail}_actual.png");
                                            var diffOut = Path.Combine(runArtifactsDir, $"{prefixFail}_diff.png");
                                            var metaOut = Path.Combine(runArtifactsDir, $"{prefixFail}_meta.txt");

                                            expected.Save(expOut, ImageFormat.Png);
                                            lastActual.Save(actOut, ImageFormat.Png);
                                            lastRes?.DiffImage?.Save(diffOut, ImageFormat.Png);

                                            string showPath = actOut;

                                            try
                                            {
                                                if (lastRes?.DiffImage != null)
                                                {
                                                    var overlayOut = Path.Combine(runArtifactsDir, $"{prefixFail}_overlay.png");
                                                    using (var overlay = CreateOverlayFastSafe(lastActual, lastRes.DiffImage))
                                                    {
                                                        overlay.Save(overlayOut, ImageFormat.Png);
                                                    }

                                                    if (File.Exists(overlayOut))
                                                        showPath = overlayOut;
                                                }
                                                else
                                                {
                                                    AppLog.Warn("Checkpoint mismatch: DiffImage was null (showing actual only).");
                                                }
                                            }
                                            catch (Exception ex)
                                            {
                                                AppLog.Exception("Overlay generation failed (showing actual only).", ex);
                                                showPath = actOut;
                                            }

                                            if (_runOutcomes.TryGetValue(flowJsonPath, out var o))
                                            {
                                                o.HasMismatch = true;
                                                o.LastMismatchImagePath = showPath;
                                                o.LastMismatchStep = s.Name ?? "Checkpoint";
                                                o.LastRunUtc = DateTime.UtcNow;
                                                o.LastMismatchExpectedPath = expOut;
                                            }

                                            ShowCheckpointComparison(expected, lastActual, lastRes?.DiffPercent ?? 100.0, allowedDiffPercent, cpTotal - 1);
                                            SetStatus($"CP FAIL {cpFail}/{cpTotal}  {flow.Name} / {s.Name}");
                                            RefreshFlowSelector();

                                            lastRes?.DiffImage?.Dispose();
                                            lastRes = null;

                                            lastActual.Dispose();
                                            lastActual = null;

                                            break;
                                        }

                                        await Task.Delay(pollMs, ct);
                                    }

                                    lastRes?.DiffImage?.Dispose();
                                    lastActual?.Dispose();
                                    break;
                                }

                            default:
                                break;
                        }
                    }

                    if (!_stopRequested)
                        SetStatus($"Loop {l}/{loops} done. CP OK={cpOk} FAIL={cpFail}");
                }

                bool hasMismatch = _runOutcomes.TryGetValue(flowJsonPath, out var o2) && o2.HasMismatch;
                if (_stopRequested) SetStatus("Run aborted.");
                else SetStatus(hasMismatch ? "Run completed (with mismatches)." : "Run completed.");

                AppLog.Info(_stopRequested ? "Run aborted." : "Run completed.");
                RefreshFlowSelector();
            }
            catch (OperationCanceledException)
            {
                SetStatus("Run aborted.");
                AppLog.Warn("Run cancelled.");
            }
            catch (Exception ex)
            {
                AppLog.Exception("RunFlowAsync exception", ex);
                SetStatus("Run failed: " + ex.Message);
            }
            finally
            {
                _isRunning = false;
                SetControlsEnabled(true);
                UpdateUi();

                var cur = Interlocked.Exchange(ref _runCts, null);
                try { cur?.Dispose(); } catch { }
            }
        }

        private void SetControlsEnabled(bool enabled)
        {
            _btnRun.Enabled = enabled;
            _btnRecord.Enabled = enabled;
            _btnPause.Enabled = enabled && _isRecording;
            _btnSave.Enabled = enabled;
            _btnLoad.Enabled = enabled;
            _btnDelete.Enabled = enabled;
            _btnNew.Enabled = enabled;
        }

        private void DoDeleteSelected()
        {
            if (_isRunning || _isRecording)
            {
                SetStatus("Stop Run/Record first.");
                return;
            }

            var flowJson = GetSelectedFlowJsonPath();
            if (flowJson == null)
            {
                SetStatus("Select a flow or a category to delete.");
                return;
            }

            try
            {
                var flow = FlowStorage.LoadFlow(flowJson);

                var ok = MessageBox.Show(this,
                    $"Delete flow '{flow.Category}/{flow.Name}'?",
                    "Confirm delete",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (ok != DialogResult.Yes) return;

                FlowStorage.DeleteFlow(flow.Category, flow.Name);

                RefreshFlowSelector();
                SetStatus("Flow deleted.");
                AppLog.Info($"Deleted flow: {flow.Category}/{flow.Name}");

                DoNew();
            }
            catch (Exception ex)
            {
                AppLog.Exception("DoDeleteSelected(flow) failed", ex);
                SetStatus("Delete failed: " + ex.Message);
            }
        }

        private void UpdateUi()
        {
            _btnPause.Enabled = _isRecording;

            _btnRecord.Text = _isRecording ? "⏹️ Stop (F9)" : $"⏺️ Record (F9)  CP {_hotkeys.CheckpointHotkeyText}";
            _btnPause.Text = _isPaused ? "▶️ Resume (F10)" : "⏸️ Pause (F10)";
        }

        // ============= Preview helpers =============
        private void ShowLastCheckpointIfAny(string flowJsonPath)
        {
            if (_flow.Steps.Count == 0) return;

            for (int i = _flow.Steps.Count - 1; i >= 0; i--)
            {
                var s = _flow.Steps[i];
                if (s.Kind != FlowStepKind.Checkpoint) continue;
                if (string.IsNullOrWhiteSpace(s.ImagePath)) continue;

                var dir = Path.GetDirectoryName(flowJsonPath)!;
                var imgPath = Path.Combine(dir, s.ImagePath);

                if (File.Exists(imgPath))
                {
                    ShowImageOnCanvas(imgPath);
                    return;
                }
            }
        }

        // ✅ نمایش تکی = فقط سمت چپ
        private void ShowImageOnCanvas(string path)
        {
            ShowImagesOnCanvas(path, null);
        }

        private static Bitmap CaptureVirtualScreen()
        {
            var b = GetVirtualScreenRect();
            var bmp = new Bitmap(b.Width, b.Height);
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(b.Left, b.Top, 0, 0, b.Size);
            return bmp;
        }

        private static Bitmap CaptureScreenRect(Rectangle screenRect)
        {
            if (screenRect.Width <= 0 || screenRect.Height <= 0)
                throw new ArgumentException("Invalid screenRect size.");

            var bmp = new Bitmap(screenRect.Width, screenRect.Height, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(screenRect.Left, screenRect.Top, 0, 0, screenRect.Size, CopyPixelOperation.SourceCopy);
            return bmp;
        }

        private static Bitmap CreateOverlayFastSafe(Bitmap actual, Bitmap diffMask)
        {
            int w = actual.Width;
            int h = actual.Height;

            var overlay = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(overlay))
                g.DrawImageUnscaled(actual, 0, 0);

            var rect = new Rectangle(0, 0, w, h);

            var oData = overlay.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            var mData = diffMask.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            try
            {
                int oStride = oData.Stride;
                int mStride = mData.Stride;

                byte[] oBuf = new byte[oStride * h];
                byte[] mBuf = new byte[mStride * h];

                Marshal.Copy(oData.Scan0, oBuf, 0, oBuf.Length);
                Marshal.Copy(mData.Scan0, mBuf, 0, mBuf.Length);

                int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
                const int alpha = 120;

                for (int y = 0; y < h; y++)
                {
                    int oRow = y * oStride;
                    int mRow = y * mStride;

                    for (int x = 0; x < w; x++)
                    {
                        int oi = oRow + x * 4;
                        int mi = mRow + x * 4;

                        byte mb = mBuf[mi + 0];
                        byte mg = mBuf[mi + 1];
                        byte mr = mBuf[mi + 2];

                        bool isBad = (mr > 200 && mg < 80 && mb < 80);
                        if (!isBad) continue;

                        if (x < minX) minX = x;
                        if (y < minY) minY = y;
                        if (x > maxX) maxX = x;
                        if (y > maxY) maxY = y;

                        int ob = oBuf[oi + 0];
                        int og = oBuf[oi + 1];
                        int orr = oBuf[oi + 2];

                        oBuf[oi + 0] = (byte)((ob * (255 - alpha) + 0 * alpha) / 255);
                        oBuf[oi + 1] = (byte)((og * (255 - alpha) + 0 * alpha) / 255);
                        oBuf[oi + 2] = (byte)((orr * (255 - alpha) + 255 * alpha) / 255);
                        oBuf[oi + 3] = 255;
                    }
                }

                Marshal.Copy(oBuf, 0, oData.Scan0, oBuf.Length);

                if (maxX >= 0)
                {
                    int pad = 6;
                    var box = Rectangle.FromLTRB(
                        Math.Max(0, minX - pad),
                        Math.Max(0, minY - pad),
                        Math.Min(w - 1, maxX + pad),
                        Math.Min(h - 1, maxY + pad));

                    using var g = Graphics.FromImage(overlay);
                    using var pen = new Pen(Color.Yellow, 3);
                    g.DrawRectangle(pen, box);
                }

                return overlay;
            }
            finally
            {
                overlay.UnlockBits(oData);
                diffMask.UnlockBits(mData);
            }
        }

        private static Bitmap LoadBitmapNoLock(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var img = Image.FromStream(fs);
            return new Bitmap(img);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try
            {
                AppLog.Info("MainForm closing...");
                _stopRequested = true;
                _clock.Stop();
                _clock.Dispose();
                _tooltip.Dispose();

                if (_isRecording)
                {
                    try { Win32Hooks.MouseEvent -= OnMouseEvent; } catch { }
                    try { Win32Hooks.Stop(); } catch { }
                }
            }
            catch (Exception ex)
            {
                AppLog.Exception("OnFormClosing exception", ex);
            }

            base.OnFormClosing(e);
        }

        private const int SM_XVIRTUALSCREEN = 76;
        private const int SM_YVIRTUALSCREEN = 77;
        private const int SM_CXVIRTUALSCREEN = 78;
        private const int SM_CYVIRTUALSCREEN = 79;

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        private static Rectangle GetVirtualScreenRect()
        {
            int x = GetSystemMetrics(SM_XVIRTUALSCREEN);
            int y = GetSystemMetrics(SM_YVIRTUALSCREEN);
            int w = GetSystemMetrics(SM_CXVIRTUALSCREEN);
            int h = GetSystemMetrics(SM_CYVIRTUALSCREEN);
            return new Rectangle(x, y, w, h);
        }

        private static string SanitizeFileName(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "file";
            foreach (var c in Path.GetInvalidFileNameChars())
                s = s.Replace(c, '_');
            s = s.Trim();
            if (s.Length == 0) s = "file";
            if (s.Length > 120) s = s.Substring(0, 120);
            return s;
        }

        private void AddTooltips()
        {
            _tooltip.AutoPopDelay = 5000;
            _tooltip.InitialDelay = 500;
            _tooltip.ReshowDelay = 200;
            _tooltip.ShowAlways = true;
            _tooltip.BackColor = Color.FromArgb(30, 34, 46);
            _tooltip.ForeColor = Color.Gainsboro;

            _tooltip.SetToolTip(_btnNew, "Create a new flow (Ctrl+N)");
            _tooltip.SetToolTip(_btnRecord, "Start recording actions (F9)");
            _tooltip.SetToolTip(_btnPause, "Pause/Resume recording (F10)");
            _tooltip.SetToolTip(_btnSave, "Save current flow (Ctrl+S)");
            _tooltip.SetToolTip(_btnRun, "Run the current flow (F11)");
            _tooltip.SetToolTip(_btnLoad, "Load an existing flow (Ctrl+O)");
            _tooltip.SetToolTip(_btnDelete, "Delete selected flow (Delete)");
            _tooltip.SetToolTip(_chkCategories, "Check categories to load their flows");
            _tooltip.SetToolTip(_lstFlows, "Ctrl+Click or Shift+Click to multi-select; double-click to run a flow");
        }

        private void SetupPreviewBox(PictureBox pb)
        {
            pb.Dock = DockStyle.Fill;
            pb.SizeMode = PictureBoxSizeMode.Zoom;
            pb.BackColor = Color.FromArgb(10, 12, 20);
        }

        // ============= Canvas Paint / Zoom =============
        private void Canvas_Paint(object? sender, PaintEventArgs e)
        {
            if (_canvasImage == null) return;

            var g = e.Graphics;
            g.Clear(Color.FromArgb(18, 22, 36));

            float scale = _canvasZoomLevel;

            if (_canvasZoomMode == ZoomMode.FitToCanvas)
            {
                float scaleX = (float)_canvasView.Width / _canvasImage.Width;
                float scaleY = (float)_canvasView.Height / _canvasImage.Height;
                scale = Math.Min(scaleX, scaleY);
            }

            int drawW = (int)(_canvasImage.Width * scale);
            int drawH = (int)(_canvasImage.Height * scale);
            int drawX = (_canvasView.Width - drawW) / 2;
            int drawY = (_canvasView.Height - drawH) / 2;

            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            g.DrawImage(_canvasImage, drawX, drawY, drawW, drawH);

            // Draw label
            if (!string.IsNullOrEmpty(_canvasLabel))
            {
                using var font = new Font("Segoe UI", 11f, FontStyle.Bold);
                using var brush = new SolidBrush(Color.White);
                using var bgBrush = new SolidBrush(Color.FromArgb(200, 0, 0, 0));

                var size = g.MeasureString(_canvasLabel, font);
                g.FillRectangle(bgBrush, 10, 10, size.Width + 10, size.Height + 6);
                g.DrawString(_canvasLabel, font, brush, 15, 13);
            }

            // Draw zoom level indicator
            var zoomText = $"Zoom: {scale * 100:F0}%";
            using (var font = new Font("Segoe UI", 9f))
            using (var brush = new SolidBrush(Color.Gray))
            {
                var size = g.MeasureString(zoomText, font);
                g.DrawString(zoomText, font, brush, _canvasView.Width - size.Width - 10, _canvasView.Height - size.Height - 10);
            }
        }

        private void Canvas_MouseWheel(object? sender, MouseEventArgs e)
        {
            if (_canvasImage == null) return;

            float delta = e.Delta > 0 ? 0.1f : -0.1f;
            AdjustZoom(delta);
        }

        private Button CreateZoomButton(string text, string tooltip)
        {
            var btn = new Button
            {
                Text = text,
                Width = 50,
                Height = 28,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(52, 73, 94),
                ForeColor = Color.Gainsboro,
                Font = new Font("Segoe UI", 10f),
                Cursor = Cursors.Hand,
                Margin = new Padding(2)
            };
            btn.FlatAppearance.BorderColor = Color.FromArgb(70, 90, 110);

            _tooltips.SetToolTip(btn, tooltip);

            btn.MouseEnter += (s, e) => btn.BackColor = Color.FromArgb(70, 90, 110);
            btn.MouseLeave += (s, e) => btn.BackColor = Color.FromArgb(52, 73, 94);

            return btn;
        }

        private void AdjustZoom(float delta)
        {
            _canvasZoomMode = ZoomMode.Custom;
            _canvasZoomLevel = Math.Max(0.1f, Math.Min(5.0f, _canvasZoomLevel + delta));
            _canvasView.Invalidate();
        }

        private void SetZoom(ZoomMode mode)
        {
            _canvasZoomMode = mode;
            if (mode == ZoomMode.ActualSize)
                _canvasZoomLevel = 1.0f;
            _canvasView.Invalidate();
        }

        private void ShowCheckpointComparison(Bitmap expected, Bitmap actual, double diffPercent, double allowedDiffPercent, int checkpointIndex)
        {
            int spacing = 20;
            int labelHeight = 40;
            int footerHeight = 40;

            int maxWidth = Math.Max(expected.Width, actual.Width);
            int maxHeight = Math.Max(expected.Height, actual.Height);

            var combined = new Bitmap(
                maxWidth * 2 + spacing * 3,
                maxHeight + labelHeight + footerHeight,
                PixelFormat.Format32bppArgb
            );

            using (var g = Graphics.FromImage(combined))
            {
                g.Clear(Color.FromArgb(30, 34, 46));

                // Left side - Expected
                int leftX = spacing;
                int leftY = labelHeight;

                using (var headerBrush = new SolidBrush(Color.FromArgb(46, 204, 113)))
                    g.FillRectangle(headerBrush, leftX, 0, maxWidth, labelHeight);

                using (var font = new Font("Segoe UI", 14f, FontStyle.Bold))
                using (var brush = new SolidBrush(Color.White))
                    g.DrawString("✅ Expected", font, brush, leftX + 10, 10);

                g.DrawImage(expected, leftX, leftY);

                // Right side - Actual
                int rightX = maxWidth + spacing * 2;
                int rightY = labelHeight;

                using (var headerBrush = new SolidBrush(Color.FromArgb(231, 76, 60)))
                    g.FillRectangle(headerBrush, rightX, 0, maxWidth, labelHeight);

                using (var font = new Font("Segoe UI", 14f, FontStyle.Bold))
                using (var brush = new SolidBrush(Color.White))
                    g.DrawString("❌ Actual", font, brush, rightX + 10, 10);

                g.DrawImage(actual, rightX, rightY);

                // Footer with match info
                int footerY = maxHeight + labelHeight;
                using (var footerBrush = new SolidBrush(Color.FromArgb(23, 32, 42)))
                    g.FillRectangle(footerBrush, 0, footerY, combined.Width, footerHeight);

                var matchText = $"Checkpoint #{checkpointIndex + 1} Failed | Diff: {diffPercent:F3}% | Allowed: {allowedDiffPercent:F3}% | Excess: {(diffPercent - allowedDiffPercent):F3}%";
                using (var font = new Font("Segoe UI", 11f, FontStyle.Bold))
                using (var brush = new SolidBrush(Color.FromArgb(231, 76, 60)))
                    g.DrawString(matchText, font, brush, spacing, footerY + 10);
            }

            _canvasImage?.Dispose();
            _canvasImage = combined;
            _canvasLabel = $"❌ Checkpoint {checkpointIndex + 1} Failed - Comparison View";
            _canvasZoomMode = ZoomMode.FitToCanvas;
            _split.Visible = false;
            _canvasView.Visible = true;
            _canvasView.Invalidate();
        }

        private void ShowCheckpointSuccess(Bitmap checkpoint, double diffPercent, double allowedDiffPercent, int checkpointIndex)
        {
            _canvasImage?.Dispose();
            _canvasImage = (Bitmap)checkpoint.Clone();
            _canvasLabel = $"✅ Checkpoint {checkpointIndex + 1} Passed | Diff: {diffPercent:F3}% (Allowed: {allowedDiffPercent:F3}%)";
            _canvasZoomMode = ZoomMode.ActualSize;
            _canvasZoomLevel = 1.0f;
            _split.Visible = false;
            _canvasView.Visible = true;
            _canvasView.Invalidate();
        }

        private void ShowImagesOnCanvas(string? expectedPath, string? actualOrOverlayPath)
        {
            // Restore split view when called for traditional file-based display
            _canvasImage?.Dispose();
            _canvasImage = null;
            _canvasView.Visible = false;
            _split.Visible = true;

            try
            {
                // dispose قبلی (leak نشه)
                _previewExpected.Image?.Dispose();
                _previewExpected.Image = null;

                if (!string.IsNullOrWhiteSpace(expectedPath) && File.Exists(expectedPath))
                    _previewExpected.Image = LoadBitmapNoLock(expectedPath);
            }
            catch (Exception ex)
            {
                AppLog.Exception($"Show expected failed: {expectedPath}", ex);
                try { _previewExpected.Image?.Dispose(); } catch { }
                _previewExpected.Image = null;
            }

            try
            {
                _previewActual.Image?.Dispose();
                _previewActual.Image = null;

                if (!string.IsNullOrWhiteSpace(actualOrOverlayPath) && File.Exists(actualOrOverlayPath))
                    _previewActual.Image = LoadBitmapNoLock(actualOrOverlayPath);
            }
            catch (Exception ex)
            {
                AppLog.Exception($"Show actual/overlay failed: {actualOrOverlayPath}", ex);
                try { _previewActual.Image?.Dispose(); } catch { }
                _previewActual.Image = null;
            }
        }

        private void SafeInitSplit()
        {
            try
            {
                // وقتی هنوز اندازه درست نیست، هیچ کاری نکن
                if (_split.Width <= _split.SplitterWidth + 2)
                    return;

                int max = Math.Max(1, _split.Width - _split.SplitterWidth - 1);
                int target = _split.Width / 2;

                if (target < 1) target = 1;
                if (target > max) target = max;

                _split.SplitterDistance = target;
            }
            catch (Exception ex)
            {
                AppLog.Exception("SafeInitSplit failed", ex);
            }
        }
    }
}