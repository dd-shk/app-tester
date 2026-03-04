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

        // ===== UI =====
        private readonly ComboBox _cmbCategory = new();
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

        private readonly Dictionary<string, RunOutcome> _runOutcomes = new(StringComparer.OrdinalIgnoreCase);

        private readonly SplitContainer _split = new();
        private readonly PictureBox _previewExpected = new();
        private readonly PictureBox _previewActual = new();

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
            _status.Items.Add(_lbl);
            _status.BackColor = Color.FromArgb(23, 32, 42);
            _status.ForeColor = Color.Gainsboro;
            _status.Height = 28;
            _lbl.Font = new Font("Segoe UI", 9f);
            _lbl.Spring = true;
            _lbl.TextAlign = ContentAlignment.MiddleLeft;

            var lblTime = new ToolStripStatusLabel
            {
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.Gray
            };
            _status.Items.Add(lblTime);

            _clock.Tick += (_, __) => lblTime.Text = DateTime.Now.ToString("HH:mm:ss");
            _clock.Start();

            Controls.Add(_status);

            InitializeFlowSelector();

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

            StyleButton(_btnNew, "📝", "New", Color.FromArgb(52, 152, 219));
            StyleButton(_btnRecord, "⏺", "Record (F9)", Color.FromArgb(231, 76, 60));
            StyleButton(_btnPause, "⏸", "Pause (F10)", Color.FromArgb(243, 156, 18));
            StyleButton(_btnSave, "💾", "Save", Color.FromArgb(46, 204, 113));
            StyleButton(_btnRun, "▶", "Run (F11)", Color.FromArgb(155, 89, 182));
            StyleButton(_btnLoad, "📂", "Load...", Color.FromArgb(52, 73, 94));
            StyleButton(_btnDelete, "🗑", "Delete", Color.FromArgb(192, 57, 43));

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

            InitializeTestSuiteUI();

            _hotkeys.KeyPressed += OnHotkey;
            _hotkeys.Start();
            FormClosed += (_, __) => { try { _hotkeys.Dispose(); } catch { } };

            Directory.CreateDirectory(FlowStorage.FlowsDir);
            LoadCategories();

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

        private static void StyleButton(Button btn, string emoji, string text, Color color)
        {
            btn.Text = $"{emoji} {text}";
            btn.Height = 38;
            btn.Dock = DockStyle.Top;
            btn.BackColor = color;
            btn.ForeColor = Color.White;
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderSize = 0;
            btn.Font = new Font("Segoe UI", 10f, FontStyle.Regular);
            btn.Cursor = Cursors.Hand;
            btn.Padding = new Padding(8, 0, 8, 0);

            btn.MouseEnter += (s, e) => btn.BackColor = ControlPaint.Light(color, 0.1f);
            btn.MouseLeave += (s, e) => btn.BackColor = color;
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
        private void InitializeFlowSelector()
        {
            _flowPanel.Dock = DockStyle.Left;
            _flowPanel.Width = 320;
            _flowPanel.BackColor = Color.FromArgb(18, 22, 36);
            _flowPanel.Padding = new Padding(12);
            Controls.Add(_flowPanel);

            var lblCategory = new Label
            {
                Text = "📁 Category",
                Dock = DockStyle.Top,
                Height = 30,
                ForeColor = Color.Gainsboro,
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };
            _flowPanel.Controls.Add(lblCategory);

            _cmbCategory.Dock = DockStyle.Top;
            _cmbCategory.Height = 35;
            _cmbCategory.BackColor = Color.FromArgb(30, 34, 46);
            _cmbCategory.ForeColor = Color.Gainsboro;
            _cmbCategory.FlatStyle = FlatStyle.Flat;
            _cmbCategory.Font = new Font("Segoe UI", 10f);
            _cmbCategory.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbCategory.SelectedIndexChanged += (_, __) => LoadFlowsForCategory();
            _flowPanel.Controls.Add(_cmbCategory);
            _flowPanel.Controls.SetChildIndex(_cmbCategory, 0);

            var spacer1 = new Panel { Dock = DockStyle.Top, Height = 12 };
            _flowPanel.Controls.Add(spacer1);
            _flowPanel.Controls.SetChildIndex(spacer1, 0);

            var lblFlows = new Label
            {
                Text = "📄 Flows",
                Dock = DockStyle.Top,
                Height = 30,
                ForeColor = Color.Gainsboro,
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };
            _flowPanel.Controls.Add(lblFlows);
            _flowPanel.Controls.SetChildIndex(lblFlows, 0);

            _lstFlows.Dock = DockStyle.Fill;
            _lstFlows.BackColor = Color.FromArgb(30, 34, 46);
            _lstFlows.ForeColor = Color.Gainsboro;
            _lstFlows.BorderStyle = BorderStyle.None;
            _lstFlows.Font = new Font("Segoe UI", 10f);
            _lstFlows.ItemHeight = 28;
            _lstFlows.DrawMode = DrawMode.OwnerDrawFixed;
            _lstFlows.DrawItem += FlowList_DrawItem;
            _lstFlows.SelectedIndexChanged += (_, __) => LoadSelectedFlowToEditor();
            _lstFlows.DoubleClick += (_, __) => DoRunSelected();
            _flowPanel.Controls.Add(_lstFlows);

            var btnPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 45,
                BackColor = Color.FromArgb(18, 22, 36),
                Padding = new Padding(0, 8, 0, 0)
            };

            var btnRefresh = new Button
            {
                Text = "🔄",
                Width = 45,
                Height = 35,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(52, 73, 94),
                ForeColor = Color.Gainsboro,
                Font = new Font("Segoe UI", 12f)
            };
            btnRefresh.FlatAppearance.BorderColor = Color.FromArgb(70, 90, 110);
            btnRefresh.Click += (_, __) => { LoadCategories(); SetStatus("Flow list refreshed"); };
            btnPanel.Controls.Add(btnRefresh);

            var btnOpenFolder = new Button
            {
                Text = "📂",
                Width = 45,
                Height = 35,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(52, 73, 94),
                ForeColor = Color.Gainsboro,
                Font = new Font("Segoe UI", 12f),
                Margin = new Padding(4, 0, 0, 0)
            };
            btnOpenFolder.FlatAppearance.BorderColor = Color.FromArgb(70, 90, 110);
            btnOpenFolder.Click += (_, __) =>
            {
                try
                {
                    if (!Directory.Exists(FlowStorage.FlowsDir))
                        Directory.CreateDirectory(FlowStorage.FlowsDir);
                    System.Diagnostics.Process.Start("explorer.exe", FlowStorage.FlowsDir);
                }
                catch (Exception ex)
                {
                    AppLog.Exception("OpenFlowsFolder failed", ex);
                }
            };
            btnPanel.Controls.Add(btnOpenFolder);

            _flowPanel.Controls.Add(btnPanel);
        }

        private void FlowList_DrawItem(object? sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;

            e.DrawBackground();

            var item = _lstFlows.Items[e.Index].ToString();
            if (item == null) return;

            var bgColor = (e.State & DrawItemState.Selected) != 0
                ? Color.FromArgb(41, 128, 185)
                : Color.FromArgb(30, 34, 46);

            using (var brush = new SolidBrush(bgColor))
                e.Graphics.FillRectangle(brush, e.Bounds);

            var icon = "📄";
            var flowPath = GetFlowPathFromItem(item);
            if (_runOutcomes.TryGetValue(flowPath, out var outcome))
                icon = outcome.HasMismatch ? "❌" : "✅";

            using (var iconFont = new Font("Segoe UI Emoji", 10f))
            using (var textFont = new Font("Segoe UI", 10f))
            using (var brush = new SolidBrush(Color.Gainsboro))
            {
                e.Graphics.DrawString(icon, iconFont, brush, e.Bounds.Left + 4, e.Bounds.Top + 6);
                e.Graphics.DrawString(item, textFont, brush, e.Bounds.Left + 28, e.Bounds.Top + 6);
            }

            e.DrawFocusRectangle();
        }

        private void LoadFlowsForCategory()
        {
            _lstFlows.Items.Clear();

            if (_cmbCategory.SelectedItem == null) return;

            var category = _cmbCategory.SelectedItem.ToString();
            if (category == null) return;

            var categoryDir = Path.Combine(FlowStorage.FlowsDir, FlowStorage.SafeFileName(category));
            if (!Directory.Exists(categoryDir)) return;

            foreach (var flowDir in Directory.GetDirectories(categoryDir))
            {
                var flowJsonPath = Path.Combine(flowDir, "flow.json");
                if (File.Exists(flowJsonPath))
                    _lstFlows.Items.Add(Path.GetFileName(flowDir));
            }
        }

        private string GetFlowPathFromItem(string item)
        {
            if (_cmbCategory.SelectedItem == null) return "";
            var category = _cmbCategory.SelectedItem.ToString();
            if (category == null) return "";
            return FlowStorage.GetFlowJsonPath(category, item);
        }

        private void LoadCategories()
        {
            var prevCategory = _cmbCategory.SelectedItem?.ToString();

            _cmbCategory.Items.Clear();

            if (!Directory.Exists(FlowStorage.FlowsDir))
            {
                Directory.CreateDirectory(FlowStorage.FlowsDir);
                Directory.CreateDirectory(Path.Combine(FlowStorage.FlowsDir, "General"));
            }

            var categories = Directory.GetDirectories(FlowStorage.FlowsDir)
                .Select(Path.GetFileName)
                .Where(c => c != null)
                .OrderBy(c => c)
                .ToList();

            foreach (var category in categories)
                _cmbCategory.Items.Add(category!);

            if (_cmbCategory.Items.Count == 0)
            {
                Directory.CreateDirectory(Path.Combine(FlowStorage.FlowsDir, "General"));
                _cmbCategory.Items.Add("General");
            }

            // Try to restore previous selection
            if (prevCategory != null)
            {
                int idx = _cmbCategory.Items.IndexOf(prevCategory);
                _cmbCategory.SelectedIndex = idx >= 0 ? idx : 0;
            }
            else if (_cmbCategory.Items.Count > 0)
            {
                _cmbCategory.SelectedIndex = 0;
            }
        }

        private string? GetSelectedFlowJsonPath()
        {
            if (_lstFlows.SelectedItem == null || _cmbCategory.SelectedItem == null)
                return null;

            var category = _cmbCategory.SelectedItem.ToString();
            var flowName = _lstFlows.SelectedItem.ToString();

            if (category == null || flowName == null) return null;

            var path = FlowStorage.GetFlowJsonPath(category, flowName);
            return File.Exists(path) ? path : null;
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
            else if (k == Keys.F12) CreateCheckpoint();
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

        private void CreateCheckpoint()
        {
            if (!_isRecording)
            {
                SetStatus("Checkpoint ignored (not recording).");
                return;
            }

            bool prevPaused = _isPaused;

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
                using var frozen = CaptureVirtualScreen();

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
                LoadCategories();

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

                LoadCategories();
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
                SetStatus("Select a flow in the list (double-click also runs).");
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
            _lstFlows.Invalidate();

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

                                            ShowImagesOnCanvas(expOut, showPath);
                                            SetStatus($"CP FAIL {cpFail}/{cpTotal}  {flow.Name} / {s.Name}");
                                            _lstFlows.Invalidate();

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
                _lstFlows.Invalidate();
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

                _lstFlows.Invalidate();
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

            _btnRecord.Text = _isRecording ? "Stop (F9)" : $"Record (F9)  CP {_hotkeys.CheckpointHotkeyText}";
            _btnPause.Text = _isPaused ? "Resume (F10)" : "Pause (F10)";
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
            _tooltip.SetToolTip(_cmbCategory, "Select flow category");
            _tooltip.SetToolTip(_lstFlows, "Double-click to run a flow");
        }

        private void SetupPreviewBox(PictureBox pb)
        {
            pb.Dock = DockStyle.Fill;
            pb.SizeMode = PictureBoxSizeMode.Zoom;
            pb.BackColor = Color.FromArgb(10, 12, 20);
        }

        private void ShowImagesOnCanvas(string? expectedPath, string? actualOrOverlayPath)
        {
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