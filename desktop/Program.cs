using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BoundaryCheck;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

public sealed class Scenario
{
    public string Method { get; set; } = "GET";
    public string Path { get; set; } = "/";
    public string IdentityId { get; set; } = "";
    public string Expectation { get; set; } = "미지정";
    public int Occurrences { get; set; } = 1;
}

public sealed class TestIdentity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Organization { get; set; } = "";
    public string Role { get; set; } = "";
}

public sealed class ProjectFile
{
    public string Name { get; set; } = "내 첫 프로젝트";
    public string Origin { get; set; } = "https://app.example.test";
    public string Prefix { get; set; } = "/api/";
    public List<TestIdentity> Identities { get; set; } = [];
    public List<Scenario> Scenarios { get; set; } = [];
}

public sealed class MainForm : Form
{
    private static readonly string DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BoundaryCheck");
    private static readonly string DataFile = Path.Combine(DataDirectory, "project.json");
    private readonly TextBox _name = new() { Text = "내 첫 프로젝트" };
    private readonly TextBox _origin = new() { Text = "https://app.example.test" };
    private readonly TextBox _prefix = new() { Text = "/api/" };
    private readonly CheckBox _allowLocalFixtureWrites = new()
    {
        AutoSize = true,
        Text = "내장 예제 메모리 쓰기 검사 허용 (기본 꺼짐)",
        ForeColor = Color.FromArgb(54, 68, 60),
    };
    private readonly List<TestIdentity> _identities = [];
    private readonly BindingList<Scenario> _scenarios = [];
    private readonly DataGridView _grid = new();
    private readonly Label _summary = new();
    private bool _updatingWriteSettings;

    public MainForm()
    {
        Text = "BoundaryCheck";
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(1000, 760);
        Size = new Size(1280, 900);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(246, 248, 246);
        Font = new Font("Segoe UI", 10F);
        _allowLocalFixtureWrites.Checked = LocalFixtureWriteSettings.IsEnabled();

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(30, 22, 30, 24) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 116));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 316));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 72));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        var brand = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
        brand.RowStyles.Add(new RowStyle(SizeType.Percent, 62));
        brand.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
        brand.Controls.Add(new Label { Text = "BoundaryCheck", Font = new Font("Segoe UI Semibold", 23F), ForeColor = Color.FromArgb(23, 76, 55), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        brand.Controls.Add(new Label { Text = $"v{UpdateClient.CurrentVersion()} · 앱 권한 경계 점검", ForeColor = Color.FromArgb(93, 107, 99), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        header.Controls.Add(brand, 0, 0);
        var headerActions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(0, 34, 0, 0), Margin = new Padding(0) };
        var updateButton = Button("업데이트 확인", false);
        updateButton.Click += async (_, _) => await CheckForUpdatesAsync(updateButton);
        var localBadge = new Label { Text = "●  로컬 저장", AutoSize = true, BackColor = Color.FromArgb(229, 242, 234), ForeColor = Color.FromArgb(23, 107, 80), Padding = new Padding(12, 9, 12, 9), Margin = new Padding(0, 2, 8, 0) };
        headerActions.Controls.AddRange([updateButton, localBadge]);
        header.Controls.Add(headerActions, 1, 0);
        root.Controls.Add(header, 0, 0);

        var privacy = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(232, 244, 238), Padding = new Padding(18, 12, 18, 12), Margin = new Padding(0, 4, 0, 8) };
        var privacyCopy = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
        privacyCopy.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        privacyCopy.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        privacyCopy.Controls.Add(new Label { Text = "프로젝트와 가져온 파일은 이 컴퓨터에 저장됩니다", Font = new Font("Segoe UI Semibold", 10.5F), ForeColor = Color.FromArgb(23, 76, 55), Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomLeft }, 0, 0);
        privacyCopy.Controls.Add(new Label { Text = "업데이트 확인은 직접 눌러야 시작됩니다. GitHub는 IP와 앱 버전을 볼 수 있지만 프로젝트 자료는 전송되지 않습니다.", ForeColor = Color.FromArgb(70, 91, 79), Dock = DockStyle.Fill, TextAlign = ContentAlignment.TopLeft }, 0, 1);
        privacy.Controls.Add(privacyCopy);
        root.Controls.Add(privacy, 0, 1);

        var project = Card();
        var projectGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 6, Padding = new Padding(20, 12, 20, 14), Margin = new Padding(0) };
        projectGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        projectGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        projectGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        projectGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        projectGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        projectGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        projectGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        projectGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        projectGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        projectGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        projectGrid.Controls.Add(new Label { Text = "프로젝트·로컬 검사 설정", Font = new Font("Segoe UI Semibold", 12F), ForeColor = Color.FromArgb(34, 54, 43), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        projectGrid.SetColumnSpan(projectGrid.GetControlFromPosition(0, 0)!, 4);
        projectGrid.Controls.Add(FieldLabel("프로젝트 이름"), 0, 1);
        projectGrid.Controls.Add(StyleInput(_name), 1, 1);
        projectGrid.Controls.Add(FieldLabel("대상 주소"), 2, 1);
        projectGrid.Controls.Add(StyleInput(_origin), 3, 1);
        projectGrid.Controls.Add(FieldLabel("API 경로"), 0, 2);
        projectGrid.Controls.Add(StyleInput(_prefix), 1, 2);
        var hint = new Label { Text = "가져온 요청은 실행하지 않습니다. 쓰기 검사는 내장 127.0.0.1 예제에만 별도 동의 후 적용됩니다.", Dock = DockStyle.Fill, ForeColor = Color.FromArgb(93, 107, 99), TextAlign = ContentAlignment.MiddleLeft };
        projectGrid.Controls.Add(hint, 0, 3);
        projectGrid.SetColumnSpan(hint, 4);
        projectGrid.Controls.Add(_allowLocalFixtureWrites, 0, 4);
        projectGrid.SetColumnSpan(_allowLocalFixtureWrites, 4);
        _allowLocalFixtureWrites.CheckedChanged += (_, _) => SaveLocalFixtureWriteSetting();
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(0, 8, 0, 0), Margin = new Padding(0) };
        var harButton = Button("HAR 가져오기", false);
        harButton.Click += (_, _) => ImportFile(true);
        var openApiButton = Button("OpenAPI 가져오기", false);
        openApiButton.Click += (_, _) => ImportFile(false);
        var saveButton = Button("프로젝트 저장", true);
        saveButton.Click += (_, _) => SaveProject(showMessage: true);
        actions.Controls.AddRange([harButton, openApiButton, saveButton]);
        projectGrid.Controls.Add(actions, 0, 5);
        projectGrid.SetColumnSpan(actions, 4);
        project.Controls.Add(projectGrid);
        root.Controls.Add(project, 0, 2);

        var scenarioCard = Card();
        var scenarioLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(20, 14, 20, 14), Margin = new Padding(0) };
        scenarioLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        scenarioLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        scenarioLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        var scenarioHeader = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1, Margin = new Padding(0) };
        scenarioHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        scenarioHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        scenarioHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        scenarioHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        scenarioHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        scenarioHeader.Controls.Add(new Label { Text = "요청 목록", Font = new Font("Segoe UI Semibold", 14F), ForeColor = Color.FromArgb(34, 54, 43), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        var identityButton = Button("테스트 신원 관리", false);
        identityButton.Click += (_, _) => ManageIdentities();
        scenarioHeader.Controls.Add(identityButton, 1, 0);
        var localFixtureButton = Button("로컬 예제 실행", false);
        localFixtureButton.Click += (_, _) =>
        {
            using var dialog = new LocalFixtureDialog();
            dialog.ShowDialog(this);
        };
        scenarioHeader.Controls.Add(localFixtureButton, 2, 0);
        var duplicateButton = Button("선택 요청 복제", false);
        duplicateButton.Click += (_, _) => DuplicateSelectedScenario();
        scenarioHeader.Controls.Add(duplicateButton, 3, 0);
        var addButton = Button("요청 직접 추가", false);
        addButton.Click += (_, _) => { _scenarios.Add(new Scenario()); UpdateSummary(); SaveProject(false); };
        scenarioHeader.Controls.Add(addButton, 4, 0);
        scenarioLayout.Controls.Add(scenarioHeader, 0, 0);

        ConfigureGrid();
        var scenarioBody = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Margin = new Padding(0) };
        scenarioBody.Controls.Add(_grid);
        var emptyState = new Label { Text = "아직 요청이 없어요\nHAR 또는 OpenAPI 파일을 가져와 첫 요청 목록을 만들어 보세요\n테스트 신원은 먼저 만들거나 나중에 연결할 수 있어요", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.FromArgb(93, 107, 99), Font = new Font("Segoe UI", 12F), BackColor = Color.White };
        scenarioBody.Controls.Add(emptyState);
        _emptyState = emptyState;
        scenarioLayout.Controls.Add(scenarioBody, 0, 1);
        _summary.Dock = DockStyle.Fill;
        _summary.ForeColor = Color.FromArgb(93, 107, 99);
        _summary.TextAlign = ContentAlignment.MiddleLeft;
        scenarioLayout.Controls.Add(_summary, 0, 2);
        scenarioCard.Controls.Add(scenarioLayout);
        root.Controls.Add(scenarioCard, 0, 3);

        LoadProject();
    }

    private readonly Label _emptyState = new();

    private async Task CheckForUpdatesAsync(Button button)
    {
        var approval = MessageBox.Show(
            this,
            "업데이트를 확인하면 GitHub에 연결됩니다. GitHub는 요청에 사용된 IP 주소와 앱 버전을 볼 수 있습니다. 프로젝트 이름, 대상 주소, 요청 목록은 보내지 않습니다.\n\n지금 업데이트를 확인할까요?",
            "업데이트 연결 안내",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Information);
        if (approval != DialogResult.Yes) return;

        button.Enabled = false;
        var originalText = button.Text;
        try
        {
            button.Text = "버전 확인 중…";
            var latest = await UpdateClient.FindLatestAsync();
            if (latest is null)
            {
                MessageBox.Show(this, "아직 공개된 업데이트가 없습니다.", "업데이트 확인", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var current = UpdateClient.CurrentVersion();
            var currentComparable = new Version(current.Major, Math.Max(0, current.Minor), Math.Max(0, current.Build));
            if (latest.Version <= currentComparable)
            {
                MessageBox.Show(this, $"현재 버전(v{currentComparable})이 최신입니다.", "업데이트 확인", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var releaseNotes = string.IsNullOrWhiteSpace(latest.Notes) ? "변경 내역이 등록되지 않았습니다." : latest.Notes.Trim();
            if (releaseNotes.Length > 900) releaseNotes = releaseNotes[..900] + "…";
            var installDirectory = AppContext.BaseDirectory;
            var helper = Path.Combine(installDirectory, "apply-update.ps1");
            if (!File.Exists(helper)) throw new InvalidOperationException("이 프로그램에는 업데이트 도우미가 없습니다. 웹사이트에서 최신 설치 파일을 다시 받아 주세요.");
            EnsureInstallDirectoryWritable(installDirectory);

            var install = MessageBox.Show(
                this,
                $"새 버전 v{latest.Version}을(를) 찾았습니다.\n현재 버전: v{currentComparable}\n\n변경 내용:\n{releaseNotes}\n\n업데이트 파일을 확인하고 설치할까요?",
                "업데이트 가능",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (install != DialogResult.Yes) return;

            var progress = new Progress<int>(percent => button.Text = $"다운로드 {percent}%");
            var archivePath = await UpdateClient.DownloadVerifiedPackageAsync(latest, progress);
            button.Text = "교체 준비 중…";

            var startInfo = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(helper);
            startInfo.ArgumentList.Add("-ParentPid");
            startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
            startInfo.ArgumentList.Add("-ArchivePath");
            startInfo.ArgumentList.Add(archivePath);
            startInfo.ArgumentList.Add("-InstallDirectory");
            startInfo.ArgumentList.Add(installDirectory);
            startInfo.ArgumentList.Add("-ExpectedSha256");
            startInfo.ArgumentList.Add(latest.Package.Digest!["sha256:".Length..]);
            if (Process.Start(startInfo) is null) throw new InvalidOperationException("업데이트 도우미를 시작하지 못했습니다.");

            MessageBox.Show(this, "파일 확인이 끝났습니다. 프로그램을 닫으면 업데이트한 뒤 다시 실행합니다.", "업데이트 준비 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"업데이트를 완료하지 못했습니다.\n\n{ex.Message}", "업데이트 실패", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            if (!IsDisposed)
            {
                button.Enabled = true;
                button.Text = originalText;
            }
        }
    }

    private static void EnsureInstallDirectoryWritable(string directory)
    {
        var probe = Path.Combine(directory, $".boundarycheck-write-{Guid.NewGuid():N}.tmp");
        try { File.WriteAllText(probe, "update permission check"); }
        catch (UnauthorizedAccessException) { throw new InvalidOperationException("앱 폴더에 쓸 권한이 없습니다. ZIP을 사용자 폴더(예: 문서)에 풀어 다시 실행해 주세요."); }
        finally { if (File.Exists(probe)) File.Delete(probe); }
    }

    private static Panel Card() => new() { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(1), Margin = new Padding(0, 4, 0, 4) };
    private static Label FieldLabel(string text) => new() { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(54, 68, 60), Font = new Font("Segoe UI", 9.5F) };
    private static TextBox StyleInput(TextBox input) { input.Dock = DockStyle.Fill; input.Margin = new Padding(2, 5, 12, 5); input.BorderStyle = BorderStyle.FixedSingle; input.BackColor = Color.FromArgb(251, 252, 251); return input; }

    internal static Button Button(string text, bool primary)
    {
        var button = new Button { Text = text, AutoSize = true, Height = 40, FlatStyle = FlatStyle.Flat, Padding = new Padding(14, 3, 14, 3), Margin = new Padding(0, 0, 10, 0), Cursor = Cursors.Hand, Font = new Font("Segoe UI Semibold", 9.5F) };
        button.FlatAppearance.BorderSize = primary ? 0 : 1;
        button.FlatAppearance.BorderColor = Color.FromArgb(221, 229, 223);
        button.BackColor = primary ? Color.FromArgb(23, 107, 80) : Color.White;
        button.ForeColor = primary ? Color.White : Color.FromArgb(35, 54, 44);
        return button;
    }

    private void ConfigureGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = false;
        _grid.AllowUserToAddRows = false;
        _grid.RowHeadersVisible = false;
        _grid.BackgroundColor = Color.White;
        _grid.BorderStyle = BorderStyle.None;
        _grid.GridColor = Color.FromArgb(235, 240, 236);
        _grid.EnableHeadersVisualStyles = false;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(243, 247, 244);
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(75, 91, 81);
        _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 9.5F);
        _grid.ColumnHeadersHeight = 44;
        _grid.RowTemplate.Height = 46;
        _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(232, 244, 238);
        _grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(23, 76, 55);
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(Scenario.Method), HeaderText = "방식", Width = 110, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(Scenario.Path), HeaderText = "요청 경로", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewComboBoxColumn
        {
            Name = "Identity",
            DataPropertyName = nameof(Scenario.IdentityId),
            HeaderText = "테스트 신원",
            Width = 220,
            DataSource = IdentityChoicesForProject(),
            ValueMember = nameof(IdentityChoice.Id),
            DisplayMember = nameof(IdentityChoice.Label),
        });
        _grid.Columns.Add(new DataGridViewComboBoxColumn { DataPropertyName = nameof(Scenario.Expectation), HeaderText = "기대 접근", Width = 165, DataSource = new[] { "미지정", "허용", "거부" } });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(Scenario.Occurrences), HeaderText = "발견 횟수", Width = 125, ReadOnly = true });
        _grid.DataSource = _scenarios;
        _grid.CellValueChanged += (_, _) => { UpdateSummary(); SaveProject(false); };
        _grid.CurrentCellDirtyStateChanged += (_, _) => { if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        _grid.DataError += (_, e) => { e.ThrowException = false; };
    }

    private void ImportFile(bool isHar)
    {
        using var picker = new OpenFileDialog { Filter = isHar ? "HAR 파일 (*.har;*.json)|*.har;*.json|모든 파일 (*.*)|*.*" : "OpenAPI JSON (*.json)|*.json|모든 파일 (*.*)|*.*", Title = isHar ? "HAR 파일 선택" : "OpenAPI JSON 파일 선택" };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var imported = isHar ? ParseHar(picker.FileName) : ParseOpenApi(picker.FileName);
            foreach (var item in imported) MergeScenario(item);
            UpdateSummary();
            SaveProject(false);
            MessageBox.Show(this, $"{imported.Count}개 요청 템플릿을 확인했습니다. 실제 요청은 보내지 않았습니다.", "가져오기 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "가져오지 못했습니다", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private List<Scenario> ParseHar(string file)
    {
        using var doc = ReadJson(file);
        var entries = doc.RootElement.GetProperty("log").GetProperty("entries");
        if (entries.GetArrayLength() > 5000) throw new InvalidDataException("HAR 파일은 최대 5,000개 항목까지 가져올 수 있습니다.");
        var origin = ReadOrigin(); var prefix = ReadPrefix();
        var list = new List<Scenario>();
        foreach (var entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("request", out var request) || !request.TryGetProperty("url", out var urlValue) || !request.TryGetProperty("method", out var methodValue)) continue;
            if (!Uri.TryCreate(urlValue.GetString(), UriKind.Absolute, out var uri) || uri.GetLeftPart(UriPartial.Authority) != origin) continue;
            var method = methodValue.GetString()?.ToUpperInvariant();
            var path = uri.AbsolutePath;
            if (!path.StartsWith(prefix, StringComparison.Ordinal) || method is not ("GET" or "PATCH")) continue;
            list.Add(new Scenario { Method = method, Path = RedactPath(path) });
        }
        return Deduplicate(list);
    }

    private List<Scenario> ParseOpenApi(string file)
    {
        using var doc = ReadJson(file);
        var root = doc.RootElement;
        if (!root.TryGetProperty("openapi", out var version) || !Regex.IsMatch(version.GetString() ?? "", @"^3\.(0|1)\.")) throw new InvalidDataException("OpenAPI 3.0 또는 3.1 JSON 명세만 지원합니다.");
        if (!root.TryGetProperty("paths", out var paths) || paths.ValueKind != JsonValueKind.Object) throw new InvalidDataException("명세에 paths 항목이 없습니다.");
        var prefix = ReadPrefix(); var list = new List<Scenario>();
        foreach (var pathEntry in paths.EnumerateObject())
        {
            var path = pathEntry.Name;
            if (!path.StartsWith('/') || path.StartsWith("//") || path.Contains('\\') || path.Contains('?') || path.Contains('#') || path.Split('/').Any(s => s == "..")) continue;
            if (!path.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (pathEntry.Value.ValueKind != JsonValueKind.Object) continue;
            foreach (var operation in pathEntry.Value.EnumerateObject())
            {
                var method = operation.Name.ToUpperInvariant();
                if (method is "GET" or "PATCH") list.Add(new Scenario { Method = method, Path = RedactPath(path) });
            }
        }
        return Deduplicate(list);
    }

    private static JsonDocument ReadJson(string file)
    {
        var info = new FileInfo(file);
        if (info.Length > 10 * 1024 * 1024) throw new InvalidDataException("입력 파일은 10 MiB를 넘을 수 없습니다.");
        try { return JsonDocument.Parse(File.ReadAllBytes(file)); }
        catch (JsonException) { throw new InvalidDataException("유효한 JSON 파일이 아닙니다. OpenAPI YAML은 지원하지 않습니다."); }
    }

    private string ReadOrigin()
    {
        if (!Uri.TryCreate(_origin.Text.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.UserInfo)) throw new InvalidDataException("대상 주소에는 경로 없이 HTTP(S) origin만 입력하세요.");
        return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
    }

    private string ReadPrefix()
    {
        var value = _prefix.Text.Trim();
        if (!value.StartsWith('/') || value.StartsWith("//") || value.Contains('\\') || value.Contains('?') || value.Contains('#')) throw new InvalidDataException("API 경로는 /api/ 형식의 경로 접두사여야 합니다.");
        foreach (var segment in value.Split('/'))
        {
            string decoded;
            try { decoded = Uri.UnescapeDataString(segment); } catch { throw new InvalidDataException("API 경로에 잘못된 인코딩이 있습니다."); }
            if (decoded is "." or ".." || decoded.Contains('/') || decoded.Contains('\\')) throw new InvalidDataException("API 경로에 상위 경로나 인코딩된 구분자를 넣을 수 없습니다.");
        }
        return value.EndsWith('/') ? value : value + "/";
    }

    private static string RedactPath(string path)
    {
        return string.Join('/', path.Split('/').Select(segment =>
        {
            if (segment.Length == 0) return "";
            var decoded = Uri.UnescapeDataString(segment);
            if (decoded == "{redacted}") return decoded;
            var sensitive = decoded.Length > 80 || decoded.Contains('@') || decoded.Contains('/') || decoded.Contains('\\')
                || Regex.IsMatch(decoded, @"^[0-9a-f]{8}-[0-9a-f-]{27,}$", RegexOptions.IgnoreCase)
                || Regex.IsMatch(decoded, @"^\d{5,}$") || Regex.IsMatch(decoded, @"^[A-Za-z0-9_-]{28,}$")
                || Regex.IsMatch(decoded, @"^(bearer|token|secret|key)[-_:.]", RegexOptions.IgnoreCase)
                || decoded is "." or ".." || decoded.Any(char.IsControl)
                || !Regex.IsMatch(decoded, @"^[A-Za-z0-9._~-]+$");
            return sensitive ? "{redacted}" : decoded;
        }));
    }

    private static List<Scenario> Deduplicate(IEnumerable<Scenario> scenarios)
    {
        return scenarios.GroupBy(s => $"{s.Method} {s.Path} {s.IdentityId}", StringComparer.Ordinal)
            .Select(g => new Scenario { Method = g.First().Method, Path = g.First().Path, IdentityId = g.First().IdentityId, Occurrences = g.Sum(s => s.Occurrences) })
            .ToList();
    }

    private void MergeScenario(Scenario incoming)
    {
        var existing = _scenarios.FirstOrDefault(s => s.Method == incoming.Method && s.Path == incoming.Path && s.IdentityId == incoming.IdentityId);
        if (existing is null) _scenarios.Add(incoming);
        else existing.Occurrences += incoming.Occurrences;
    }

    private void DuplicateSelectedScenario()
    {
        if (_grid.CurrentRow?.DataBoundItem is not Scenario selected)
        {
            MessageBox.Show(this, "복제할 요청을 먼저 선택해 주세요.", "요청 선택", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var duplicate = new Scenario
        {
            Method = selected.Method,
            Path = selected.Path,
            IdentityId = "",
            Expectation = "미지정",
            Occurrences = selected.Occurrences,
        };
        _scenarios.Add(duplicate);
        UpdateSummary();
        SaveProject(false);
        var row = _scenarios.IndexOf(duplicate);
        if (row >= 0 && row < _grid.Rows.Count) _grid.CurrentCell = _grid.Rows[row].Cells[0];
    }

    private void RefreshIdentityChoices()
    {
        if (_grid.Columns["Identity"] is not DataGridViewComboBoxColumn column) return;
        column.DataSource = IdentityChoicesForProject();
    }

    private List<IdentityChoice> IdentityChoicesForProject() =>
        [new IdentityChoice("", "신원 미지정"), .. _identities.Select(identity => new IdentityChoice(identity.Id, FormatIdentity(identity)))];

    private static string FormatIdentity(TestIdentity identity) =>
        string.Join(" · ", new[] { identity.Name, identity.Organization, identity.Role }.Where(part => !string.IsNullOrWhiteSpace(part)));

    private void ManageIdentities()
    {
        _grid.EndEdit();
        using var dialog = new TestIdentityDialog(_identities);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var retainedIds = dialog.Identities.Select(identity => identity.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var scenario in _scenarios.Where(scenario => !string.IsNullOrEmpty(scenario.IdentityId) && !retainedIds.Contains(scenario.IdentityId)))
        {
            scenario.IdentityId = "";
            scenario.Expectation = "미지정";
        }

        _identities.Clear();
        _identities.AddRange(dialog.Identities.Select(CloneIdentity));
        RefreshIdentityChoices();
        _grid.Refresh();
        UpdateSummary();
        SaveProject(false);
    }

    private static TestIdentity CloneIdentity(TestIdentity identity) => new()
    {
        Id = identity.Id,
        Name = identity.Name,
        Organization = identity.Organization,
        Role = identity.Role,
    };

    private void SaveProject(bool showMessage)
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            var project = new ProjectFile { Name = _name.Text.Trim(), Origin = _origin.Text.Trim(), Prefix = _prefix.Text.Trim(), Identities = _identities.Select(CloneIdentity).ToList(), Scenarios = _scenarios.ToList() };
            File.WriteAllText(DataFile, JsonSerializer.Serialize(project, new JsonSerializerOptions { WriteIndented = true }));
            UpdateSummary();
            if (showMessage) MessageBox.Show(this, $"프로젝트를 이 PC에 저장했습니다.\n{DataFile}", "저장 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { if (showMessage) MessageBox.Show(this, ex.Message, "저장 실패", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void SaveLocalFixtureWriteSetting()
    {
        if (_updatingWriteSettings) return;
        try
        {
            LocalFixtureWriteSettings.Save(_allowLocalFixtureWrites.Checked);
        }
        catch (Exception ex)
        {
            _updatingWriteSettings = true;
            _allowLocalFixtureWrites.Checked = !_allowLocalFixtureWrites.Checked;
            _updatingWriteSettings = false;
            MessageBox.Show(this, $"안전 설정을 저장하지 못했습니다. 기존 설정을 유지합니다.\n\n{ex.Message}", "설정 저장 실패", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void LoadProject()
    {
        try
        {
            if (File.Exists(DataFile))
            {
                var project = JsonSerializer.Deserialize<ProjectFile>(File.ReadAllText(DataFile));
                if (project is not null)
                {
                    _name.Text = project.Name; _origin.Text = project.Origin; _prefix.Text = project.Prefix;
                    _identities.AddRange((project.Identities ?? []).Where(identity => !string.IsNullOrWhiteSpace(identity.Id)).Select(CloneIdentity));
                    var identityIds = _identities.Select(identity => identity.Id).ToHashSet(StringComparer.Ordinal);
                    foreach (var item in project.Scenarios ?? [])
                    {
                        if (!string.IsNullOrEmpty(item.IdentityId) && !identityIds.Contains(item.IdentityId)) item.IdentityId = "";
                        _scenarios.Add(item);
                    }
                }
            }
        }
        catch { /* Keep the clean starter workspace if a saved file is unreadable. */ }
        RefreshIdentityChoices();
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        _emptyState.Visible = _scenarios.Count == 0;
        _grid.Visible = _scenarios.Count > 0;
        var assigned = _scenarios.Count(s => !string.IsNullOrEmpty(s.IdentityId));
        var specified = _scenarios.Count(s => s.Expectation != "미지정");
        _summary.Text = $"요청 {_scenarios.Count}개 · 테스트 신원 {_identities.Count}개 · 신원 연결 {assigned}개 · 기대 접근값 지정 {specified}개 · 자료는 이 PC에 저장";
    }

    private sealed record IdentityChoice(string Id, string Label);
}

internal sealed class TestIdentityDialog : Form
{
    private readonly ListBox _list = new() { Dock = DockStyle.Fill, FormattingEnabled = true };
    private readonly TextBox _name = new();
    private readonly TextBox _organization = new();
    private readonly TextBox _role = new();
    private readonly List<TestIdentity> _working;

    public IReadOnlyList<TestIdentity> Identities => _working;

    public TestIdentityDialog(IEnumerable<TestIdentity> identities)
    {
        _working = identities.Select(identity => new TestIdentity
        {
            Id = identity.Id,
            Name = identity.Name,
            Organization = identity.Organization,
            Role = identity.Role,
        }).ToList();

        Text = "테스트 신원 관리";
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(680, 500);
        Size = new Size(760, 560);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(246, 248, 246);
        Font = new Font("Segoe UI", 10F);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, Padding = new Padding(22), BackColor = Color.White };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        Controls.Add(layout);

        var title = new Label { Text = "테스트 신원", Font = new Font("Segoe UI Semibold", 15F), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(23, 76, 55) };
        layout.Controls.Add(title, 0, 0);
        layout.SetColumnSpan(title, 2);

        _list.Format += (_, args) =>
        {
            if (args.ListItem is TestIdentity identity) args.Value = string.IsNullOrWhiteSpace(FormatIdentity(identity)) ? "이름 없는 신원" : FormatIdentity(identity);
        };
        _list.SelectedIndexChanged += (_, _) => LoadSelection();
        layout.Controls.Add(_list, 0, 1);

        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, Padding = new Padding(16, 0, 0, 0) };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < 3; row++) fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        fields.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        AddField(fields, "이름", _name, 0);
        AddField(fields, "조직", _organization, 1);
        AddField(fields, "역할", _role, 2);
        var privacy = new Label
        {
            Text = "이 버전은 이름·조직·역할만 관리합니다. 로그인 비밀번호, 토큰 등 인증 정보는 입력하거나 저장하지 않습니다. 신원을 삭제하면 연결된 요청의 신원과 기대 접근값이 미지정으로 돌아갑니다.",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(93, 107, 99),
            TextAlign = ContentAlignment.TopLeft,
        };
        fields.Controls.Add(privacy, 0, 3);
        fields.SetColumnSpan(privacy, 2);
        layout.Controls.Add(fields, 1, 1);

        var editActions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 8, 0, 0) };
        var newButton = MainForm.Button("새 신원", false);
        newButton.Click += (_, _) => ClearForNewIdentity();
        var addButton = MainForm.Button("신원 추가", false);
        addButton.Click += (_, _) => AddIdentity();
        var saveButton = MainForm.Button("선택 신원 저장", false);
        saveButton.Click += (_, _) => SaveSelection();
        var deleteButton = MainForm.Button("선택 신원 삭제", false);
        deleteButton.Click += (_, _) => DeleteSelection();
        editActions.Controls.AddRange([newButton, addButton, saveButton, deleteButton]);
        layout.Controls.Add(editActions, 0, 2);
        layout.SetColumnSpan(editActions, 2);

        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0) };
        var applyButton = MainForm.Button("변경 적용", true);
        applyButton.Click += (_, _) => ApplyChanges();
        var cancelButton = MainForm.Button("취소", false);
        cancelButton.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        footer.Controls.AddRange([applyButton, cancelButton]);
        layout.Controls.Add(footer, 0, 3);
        layout.SetColumnSpan(footer, 2);

        RefreshList();
    }

    private static void AddField(TableLayoutPanel fields, string label, TextBox input, int row)
    {
        fields.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(54, 68, 60) }, 0, row);
        input.Dock = DockStyle.Fill;
        input.Margin = new Padding(2, 7, 2, 7);
        fields.Controls.Add(input, 1, row);
    }

    private void ClearForNewIdentity()
    {
        if (_list.SelectedItem is TestIdentity selected)
        {
            if (!TryReadFields(out var name, out var organization, out var role)) return;
            selected.Name = name;
            selected.Organization = organization;
            selected.Role = role;
        }
        _list.ClearSelected();
        _name.Clear();
        _organization.Clear();
        _role.Clear();
        _name.Focus();
    }

    private bool AddIdentity()
    {
        if (!TryReadFields(out var name, out var organization, out var role)) return false;
        var identity = new TestIdentity { Name = name, Organization = organization, Role = role };
        _working.Add(identity);
        RefreshList(identity);
        return true;
    }

    private void SaveSelection()
    {
        if (_list.SelectedItem is not TestIdentity identity) return;
        if (!TryReadFields(out var name, out var organization, out var role)) return;
        identity.Name = name;
        identity.Organization = organization;
        identity.Role = role;
        RefreshList(identity);
    }

    private void ApplyChanges()
    {
        if (_list.SelectedItem is TestIdentity identity)
        {
            if (!TryReadFields(out var name, out var organization, out var role)) return;
            identity.Name = name;
            identity.Organization = organization;
            identity.Role = role;
        }
        else if (!string.IsNullOrWhiteSpace(_name.Text) || !string.IsNullOrWhiteSpace(_organization.Text) || !string.IsNullOrWhiteSpace(_role.Text))
        {
            if (!AddIdentity()) return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }

    private void DeleteSelection()
    {
        if (_list.SelectedItem is not TestIdentity identity) return;
        var index = _list.SelectedIndex;
        _working.Remove(identity);
        RefreshList(_working.Count == 0 ? null : _working[Math.Min(index, _working.Count - 1)]);
    }

    private bool TryReadFields(out string name, out string organization, out string role)
    {
        name = _name.Text.Trim();
        organization = _organization.Text.Trim();
        role = _role.Text.Trim();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(organization) || string.IsNullOrWhiteSpace(role))
        {
            MessageBox.Show(this, "이름, 조직, 역할을 모두 입력해 주세요.", "입력 확인", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }
        return true;
    }

    private void RefreshList(TestIdentity? selected = null)
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var identity in _working) _list.Items.Add(identity);
        _list.EndUpdate();
        if (selected is not null) _list.SelectedItem = selected;
        else if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        else { _name.Clear(); _organization.Clear(); _role.Clear(); }
    }

    private void LoadSelection()
    {
        if (_list.SelectedItem is not TestIdentity identity) return;
        _name.Text = identity.Name;
        _organization.Text = identity.Organization;
        _role.Text = identity.Role;
    }

    private static string FormatIdentity(TestIdentity identity) =>
        string.Join(" · ", new[] { identity.Name, identity.Organization, identity.Role }.Where(part => !string.IsNullOrWhiteSpace(part)));
}
