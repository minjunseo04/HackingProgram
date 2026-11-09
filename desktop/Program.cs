using System.ComponentModel;
using System.Net;
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
    public string Expectation { get; set; } = "미지정";
    public int Occurrences { get; set; } = 1;
}

public sealed class ProjectFile
{
    public string Name { get; set; } = "내 첫 프로젝트";
    public string Origin { get; set; } = "https://app.example.test";
    public string Prefix { get; set; } = "/api/";
    public List<Scenario> Scenarios { get; set; } = [];
}

public sealed class MainForm : Form
{
    private static readonly string DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BoundaryCheck");
    private static readonly string DataFile = Path.Combine(DataDirectory, "project.json");
    private readonly TextBox _name = new() { Text = "내 첫 프로젝트" };
    private readonly TextBox _origin = new() { Text = "https://app.example.test" };
    private readonly TextBox _prefix = new() { Text = "/api/" };
    private readonly BindingList<Scenario> _scenarios = [];
    private readonly DataGridView _grid = new();
    private readonly Label _summary = new();

    public MainForm()
    {
        Text = "BoundaryCheck";
        MinimumSize = new Size(900, 650);
        Size = new Size(1120, 780);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(246, 248, 246);
        Font = new Font("Segoe UI", 10F);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(28, 20, 28, 22) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 184));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var header = new Panel { Dock = DockStyle.Fill };
        var title = new Label { Text = "BoundaryCheck", Font = new Font("Segoe UI Semibold", 20F), ForeColor = Color.FromArgb(23, 76, 55), AutoSize = true, Location = new Point(0, 0) };
        var subtitle = new Label { Text = "앱의 사용자·역할 사이 접근 규칙을 정리하는 로컬 도구", ForeColor = Color.FromArgb(93, 107, 99), AutoSize = true, Location = new Point(2, 39) };
        var localBadge = new Label { Text = "PC 안에서 처리", AutoSize = true, BackColor = Color.FromArgb(229, 242, 234), ForeColor = Color.FromArgb(23, 107, 80), Padding = new Padding(10, 6, 10, 6), Anchor = AnchorStyles.Top | AnchorStyles.Right, Location = new Point(820, 12) };
        header.Controls.AddRange([title, subtitle, localBadge]);
        root.Controls.Add(header, 0, 0);

        var project = Card();
        var projectGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 3, Padding = new Padding(18) };
        projectGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        projectGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        projectGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
        projectGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        projectGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        projectGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        projectGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        projectGrid.Controls.Add(FieldLabel("프로젝트 이름"), 0, 0);
        projectGrid.Controls.Add(StyleInput(_name), 1, 0);
        projectGrid.Controls.Add(FieldLabel("대상 주소"), 2, 0);
        projectGrid.Controls.Add(StyleInput(_origin), 3, 0);
        projectGrid.Controls.Add(FieldLabel("API 경로"), 0, 1);
        projectGrid.Controls.Add(StyleInput(_prefix), 1, 1);
        var hint = new Label { Text = "승인된 테스트 환경의 주소와 API 경로를 입력하세요. 아직 요청은 실행되지 않습니다.", Dock = DockStyle.Fill, ForeColor = Color.FromArgb(93, 107, 99), TextAlign = ContentAlignment.MiddleLeft };
        projectGrid.Controls.Add(hint, 2, 1);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(0, 4, 0, 0) };
        var harButton = Button("HAR 가져오기", false);
        harButton.Click += (_, _) => ImportFile(true);
        var openApiButton = Button("OpenAPI 가져오기", false);
        openApiButton.Click += (_, _) => ImportFile(false);
        var saveButton = Button("프로젝트 저장", true);
        saveButton.Click += (_, _) => SaveProject(showMessage: true);
        actions.Controls.AddRange([harButton, openApiButton, saveButton]);
        projectGrid.Controls.Add(actions, 0, 2);
        projectGrid.SetColumnSpan(actions, 4);
        project.Controls.Add(projectGrid);
        root.Controls.Add(project, 0, 1);

        var scenarioCard = Card();
        var scenarioLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(18) };
        scenarioLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        scenarioLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        scenarioLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        var scenarioHeader = new Panel { Dock = DockStyle.Fill };
        scenarioHeader.Controls.Add(new Label { Text = "가져온 요청", Font = new Font("Segoe UI Semibold", 13F), AutoSize = true, Location = new Point(0, 4) });
        var addButton = Button("요청 직접 추가", false);
        addButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        addButton.Location = new Point(815, 0);
        addButton.Click += (_, _) => { _scenarios.Add(new Scenario()); UpdateSummary(); SaveProject(false); };
        scenarioHeader.Controls.Add(addButton);
        scenarioLayout.Controls.Add(scenarioHeader, 0, 0);

        ConfigureGrid();
        scenarioLayout.Controls.Add(_grid, 0, 1);
        _summary.Dock = DockStyle.Fill;
        _summary.ForeColor = Color.FromArgb(93, 107, 99);
        _summary.TextAlign = ContentAlignment.MiddleLeft;
        scenarioLayout.Controls.Add(_summary, 0, 2);
        scenarioCard.Controls.Add(scenarioLayout);
        root.Controls.Add(scenarioCard, 0, 2);

        LoadProject();
    }

    private static Panel Card() => new() { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(1), BorderStyle = BorderStyle.FixedSingle };
    private static Label FieldLabel(string text) => new() { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(54, 68, 60) };
    private static TextBox StyleInput(TextBox input) { input.Dock = DockStyle.Fill; input.Margin = new Padding(2, 7, 12, 7); return input; }

    private static Button Button(string text, bool primary)
    {
        var button = new Button { Text = text, AutoSize = true, Height = 34, FlatStyle = FlatStyle.Flat, Padding = new Padding(11, 2, 11, 2), Margin = new Padding(0, 0, 9, 0), Cursor = Cursors.Hand };
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
        _grid.EnableHeadersVisualStyles = false;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(247, 250, 247);
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(75, 91, 81);
        _grid.ColumnHeadersHeight = 38;
        _grid.RowTemplate.Height = 40;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(Scenario.Method), HeaderText = "방식", Width = 90, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(Scenario.Path), HeaderText = "요청 경로", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewComboBoxColumn { DataPropertyName = nameof(Scenario.Expectation), HeaderText = "기대 접근", Width = 145, DataSource = new[] { "미지정", "허용", "거부" } });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(Scenario.Occurrences), HeaderText = "발견 횟수", Width = 105, ReadOnly = true });
        _grid.DataSource = _scenarios;
        _grid.CellValueChanged += (_, _) => SaveProject(false);
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
        return scenarios.GroupBy(s => $"{s.Method} {s.Path}", StringComparer.Ordinal)
            .Select(g => new Scenario { Method = g.First().Method, Path = g.First().Path, Occurrences = g.Sum(s => s.Occurrences) })
            .ToList();
    }

    private void MergeScenario(Scenario incoming)
    {
        var existing = _scenarios.FirstOrDefault(s => s.Method == incoming.Method && s.Path == incoming.Path);
        if (existing is null) _scenarios.Add(incoming);
        else existing.Occurrences += incoming.Occurrences;
    }

    private void SaveProject(bool showMessage)
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            var project = new ProjectFile { Name = _name.Text.Trim(), Origin = _origin.Text.Trim(), Prefix = _prefix.Text.Trim(), Scenarios = _scenarios.ToList() };
            File.WriteAllText(DataFile, JsonSerializer.Serialize(project, new JsonSerializerOptions { WriteIndented = true }));
            UpdateSummary();
            if (showMessage) MessageBox.Show(this, $"프로젝트를 이 PC에 저장했습니다.\n{DataFile}", "저장 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { if (showMessage) MessageBox.Show(this, ex.Message, "저장 실패", MessageBoxButtons.OK, MessageBoxIcon.Error); }
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
                    foreach (var item in project.Scenarios) _scenarios.Add(item);
                }
            }
        }
        catch { /* Keep the clean starter workspace if a saved file is unreadable. */ }
        UpdateSummary();
    }

    private void UpdateSummary() => _summary.Text = $"{_scenarios.Count}개 요청 · 기대 접근값 { _scenarios.Count(s => s.Expectation != "미지정") }개 지정 · 저장 위치: 이 PC";
}
