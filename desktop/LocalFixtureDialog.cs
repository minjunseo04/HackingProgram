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
    private readonly CancellationTokenSource _closing = new();
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
            Text = "저장 프로젝트, 대상 주소, 가져온 HAR/OpenAPI는 사용하지 않습니다. 요청은 127.0.0.1의 임시 포트로만 보내며, 인터넷 연결과 자동 리디렉션을 사용하지 않습니다. 응답 본문은 화면이나 파일에 저장하지 않습니다.",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(70, 91, 79),
            TextAlign = ContentAlignment.TopLeft,
        }, 0, 1);
        notice.Controls.Add(noticeLayout);
        root.Controls.Add(notice, 0, 0);

        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0) };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _secureButton.Click += async (_, _) => await RunExampleAsync(vulnerable: false);
        _vulnerableButton.Click += async (_, _) => await RunExampleAsync(vulnerable: true);
        actions.Controls.Add(_secureButton, 0, 0);
        actions.Controls.Add(_vulnerableButton, 1, 0);
        _status.Dock = DockStyle.Fill;
        _status.ForeColor = Color.FromArgb(93, 107, 99);
        _status.TextAlign = ContentAlignment.MiddleLeft;
        actions.Controls.Add(_status, 2, 0);
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
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(LocalFixtureResult.Scenario), HeaderText = "검사 항목", Width = 180 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(LocalFixtureResult.Identity), HeaderText = "예제 신원", Width = 140 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(LocalFixtureResult.Method), HeaderText = "방식", Width = 65 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(LocalFixtureResult.Path), HeaderText = "요청 경로", Width = 220 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(LocalFixtureResult.Expected), HeaderText = "기대", Width = 72 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(LocalFixtureResult.Actual), HeaderText = "HTTP 결과", Width = 90 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(LocalFixtureResult.Verdict), HeaderText = "판정", Width = 100 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(LocalFixtureResult.Note), HeaderText = "설명", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 250 });
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
        _secureButton.Enabled = false;
        _vulnerableButton.Enabled = false;
        _status.Text = vulnerable ? "취약 예제 실행 중…" : "안전 예제 실행 중…";
        _emptyState.Visible = false;
        _grid.Visible = false;
        _results.Clear();

        try
        {
            var results = await LocalFixtureRunner.RunAsync(vulnerable, _closing.Token);
            if (IsDisposed) return;
            foreach (var result in results) _results.Add(result);
            _grid.Visible = _results.Count > 0;
            _emptyState.Visible = _results.Count == 0;

            var matching = _results.Count(result => result.Verdict == "기대와 일치");
            var review = _results.Count(result => result.Verdict == "검토 필요");
            var inconclusive = _results.Count(result => result.Verdict == "판정 보류");
            var mode = vulnerable ? "취약 예제" : "안전 예제";
            _status.Text = $"{mode} · 기대와 일치 {matching} · 검토 필요 {review} · 판정 보류 {inconclusive}";
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
            }
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _closing.Cancel();
        base.OnFormClosed(e);
    }
}
