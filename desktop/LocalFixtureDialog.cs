using System.ComponentModel;

namespace BoundaryCheck;

internal sealed class LocalFixtureDialog : Form
{
    private readonly BindingList<LocalFixtureResult> _results = [];
    private readonly DataGridView _grid = new();
    private readonly Label _emptyState = new();
    private readonly Label _status = new();
    private readonly Button _secureButton = MainForm.Button("안전 예제 실행", true);
    private readonly Button _vulnerableButton = MainForm.Button("취약 예제 실행", false);
    private readonly Button _saveReportButton = MainForm.Button("보고서 저장", false);
    private readonly Button _openReportButton = MainForm.Button("보고서 열기", false);
    private readonly CancellationTokenSource _closing = new();
    private LocalFixtureReport? _currentReport;
    private bool _running;

    public LocalFixtureDialog()
    {
        Text = "내장 로컬 테스트 예제";
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(1000, 640);
        Size = new Size(1240, 760);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(246, 248, 246);
        Font = new Font("Segoe UI", 10F);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(22), Margin = new Padding(0) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 122));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var notice = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(232, 244, 238), Padding = new Padding(18, 12, 18, 12), Margin = new Padding(0, 0, 0, 8) };
        var noticeLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
        noticeLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        noticeLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        noticeLayout.Controls.Add(new Label
        {
            Text = "내장된 가상 계정과 예제 문서만 실행합니다",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 12F),
            ForeColor = Color.FromArgb(23, 76, 55),
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, 0);
        noticeLayout.Controls.Add(new Label
        {
            Text = "저장 프로젝트, 대상 주소, 가져온 HAR/OpenAPI는 예제 실행에 쓰지 않습니다. 요청은 127.0.0.1의 임시 포트로만 보내며, 인터넷 연결과 자동 리디렉션을 사용하지 않습니다. 보고서는 범위·결과·상태 코드만 선택한 JSON 파일에 저장하고, 응답 본문은 저장하지 않습니다. 보고서 파일은 수정할 수 있습니다.",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(70, 91, 79),
            TextAlign = ContentAlignment.TopLeft,
        }, 0, 1);
        notice.Controls.Add(noticeLayout);
        root.Controls.Add(notice, 0, 0);

        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _secureButton.Click += async (_, _) => await RunExampleAsync(vulnerable: false);
        _vulnerableButton.Click += async (_, _) => await RunExampleAsync(vulnerable: true);
        _saveReportButton.Enabled = false;
        _saveReportButton.Click += (_, _) => SaveReport();
        _openReportButton.Click += (_, _) => OpenReport();
        var actionButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        actionButtons.Controls.AddRange([_secureButton, _vulnerableButton, _saveReportButton, _openReportButton]);
        actions.Controls.Add(actionButtons, 0, 0);
        _status.Dock = DockStyle.Fill;
        _status.ForeColor = Color.FromArgb(93, 107, 99);
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.Padding = new Padding(8, 0, 0, 0);
        actions.Controls.Add(_status, 1, 0);
        root.Controls.Add(actions, 0, 1);

        ConfigureGrid();
        var resultsPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Margin = new Padding(0) };
        resultsPanel.Controls.Add(_grid);
        _emptyState.Text = "안전 예제 또는 취약 예제를 실행해 결과를 확인하세요";
        _emptyState.Dock = DockStyle.Fill;
        _emptyState.TextAlign = ContentAlignment.MiddleCenter;
        _emptyState.ForeColor = Color.FromArgb(93, 107, 99);
        _emptyState.BackColor = Color.White;
        resultsPanel.Controls.Add(_emptyState);
        root.Controls.Add(resultsPanel, 0, 2);
    }

    private void ConfigureGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = false;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.ReadOnly = true;
        _grid.MultiSelect = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.BackgroundColor = Color.White;
        _grid.BorderStyle = BorderStyle.None;
        _grid.GridColor = Color.FromArgb(235, 240, 236);
        _grid.EnableHeadersVisualStyles = false;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(243, 247, 244);
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(75, 91, 81);
        _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 9.5F);
        _grid.ColumnHeadersHeight = 42;
        _grid.RowTemplate.Height = 52;
        _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(232, 244, 238);
        _grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(23, 76, 55);
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(LocalFixtureResult.Scenario), HeaderText = "검사 항목", Width = 155 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(LocalFixtureResult.Identity), HeaderText = "예제 신원", Width = 125 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(LocalFixtureResult.Method), HeaderText = "방식", Width = 58 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(LocalFixtureResult.Path), HeaderText = "요청 경로", Width = 190 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(LocalFixtureResult.Expected), HeaderText = "기대", Width = 65 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(LocalFixtureResult.Actual), HeaderText = "HTTP 결과", Width = 85 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(LocalFixtureResult.ControlHttpStatusCode), HeaderText = "소유자 대조", Width = 85 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(LocalFixtureResult.Verdict), HeaderText = "판정", Width = 90 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(LocalFixtureResult.Note), HeaderText = "설명", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 220 });
        _grid.DataSource = _results;
        _grid.CellFormatting += (_, args) =>
        {
            if (_grid.Columns[args.ColumnIndex].DataPropertyName != nameof(LocalFixtureResult.Verdict) || args.Value is not string verdict) return;
            (args.CellStyle.BackColor, args.CellStyle.ForeColor) = verdict switch
            {
                "기대와 일치" => (Color.FromArgb(229, 242, 234), Color.FromArgb(23, 107, 80)),
                "검토 필요" => (Color.FromArgb(253, 234, 232), Color.FromArgb(150, 48, 42)),
                _ => (Color.FromArgb(255, 246, 222), Color.FromArgb(112, 84, 20)),
            };
            args.FormattingApplied = true;
        };
        _grid.DataError += (_, args) => { args.ThrowException = false; };
        _grid.Visible = false;
    }

    private async Task RunExampleAsync(bool vulnerable)
    {
        if (_running) return;
        _running = true;
        _currentReport = null;
        _secureButton.Enabled = false;
        _vulnerableButton.Enabled = false;
        _saveReportButton.Enabled = false;
        _openReportButton.Enabled = false;
        _status.Text = vulnerable ? "취약 예제 실행 중…" : "안전 예제 실행 중…";
        _emptyState.Visible = false;
        _grid.Visible = false;
        _results.Clear();

        try
        {
            var results = await LocalFixtureRunner.RunAsync(vulnerable, _closing.Token);
            if (IsDisposed) return;
            foreach (var result in results) _results.Add(result);
            _currentReport = LocalFixtureReportStore.Create(vulnerable, results);
            _saveReportButton.Enabled = results.Count > 0;
            _grid.Visible = _results.Count > 0;
            _emptyState.Visible = _results.Count == 0;

            var mode = vulnerable ? "취약 예제" : "안전 예제";
            _status.Text = FormatSummary(mode, results);
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested) { }
        catch
        {
            if (!IsDisposed)
            {
                _status.Text = "예제 실행을 완료하지 못했습니다.";
                MessageBox.Show(this, "로컬 예제를 실행하지 못했습니다. 프로젝트 자료는 사용하거나 전송하지 않았습니다.", "실행 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _emptyState.Text = "실행하지 못했어요. 다시 시도해 주세요.";
                _emptyState.Visible = true;
            }
        }
        finally
        {
            if (!IsDisposed)
            {
                _running = false;
                _secureButton.Enabled = true;
                _vulnerableButton.Enabled = true;
                _openReportButton.Enabled = true;
            }
        }
    }

    private void SaveReport()
    {
        if (_currentReport is null || _running) return;

        using var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = "json",
            Filter = "BoundaryCheck 보고서 (*.json)|*.json",
            FileName = $"BoundaryCheck-{_currentReport.Mode.Replace(' ', '-')}-{_currentReport.CreatedAtUtc:yyyyMMdd-HHmmss}Z.json",
            OverwritePrompt = true,
            Title = "검사 보고서 저장",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            LocalFixtureReportStore.Save(dialog.FileName, _currentReport);
            _status.Text = $"보고서 저장 완료 · {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"보고서를 저장하지 못했습니다.\n\n{ex.Message}", "보고서 저장 실패", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OpenReport()
    {
        if (_running) return;

        using var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = "BoundaryCheck 보고서 (*.json)|*.json|모든 파일 (*.*)|*.*",
            Multiselect = false,
            Title = "검사 보고서 열기",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var report = LocalFixtureReportStore.Load(dialog.FileName);
            _currentReport = report;
            _results.Clear();
            foreach (var result in report.Results) _results.Add(result);
            _grid.Visible = _results.Count > 0;
            _emptyState.Visible = _results.Count == 0;
            _saveReportButton.Enabled = _results.Count > 0;
            _status.Text = $"보고서 불러옴 · {report.Mode} · {report.CreatedAtUtc:yy-MM-dd HH:mm}Z";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"보고서를 열지 못했습니다. 파일 형식과 내용을 확인해 주세요.\n\n{ex.Message}", "보고서 열기 실패", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static string FormatSummary(string mode, IReadOnlyCollection<LocalFixtureResult> results) =>
        $"{mode} · {FormatCounts(results)}";

    private static string FormatCounts(IEnumerable<LocalFixtureResult> results)
    {
        var summary = results.GroupBy(result => result.Verdict)
            .ToDictionary(group => group.Key, group => group.Count());
        return $"기대와 일치 {summary.GetValueOrDefault("기대와 일치")} · 검토 필요 {summary.GetValueOrDefault("검토 필요")} · 판정 보류 {summary.GetValueOrDefault("판정 보류")}";
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _closing.Cancel();
        base.OnFormClosed(e);
    }
}
