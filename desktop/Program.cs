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

internal sealed class MainForm : DpiAwareForm
{
    private static readonly string DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BoundaryCheck");
    private static readonly string DataFile = Path.Combine(DataDirectory, "project.json");
    private readonly TextBox _name = new() { Text = "내 첫 프로젝트" };
    private readonly TextBox _origin = new() { Text = "https://app.example.test" };
    private readonly TextBox _prefix = new() { Text = "/api/" };
    private readonly CheckBox _allowLocalFixtureWrites = new()
    {
        AutoSize = true,
        Text = "내장 예제 쓰기 허용 (고급 · 기본 꺼짐)",
        ForeColor = Color.FromArgb(54, 68, 60),
    };
    private readonly List<TestIdentity> _identities = [];
    private readonly BindingList<Scenario> _scenarios = [];
    private readonly DataGridView _grid = new();
    private readonly Label _summary = new();
    private readonly Label _requestStepStatus = new();
    private readonly Label _identityStepStatus = new();
    private FlowLayoutPanel? _workspaceScroll;
    private Panel? _capabilityBanner;
    private Panel? _requestCard;
    private bool _updatingWriteSettings;

    public MainForm()
    {
        Text = "BoundaryCheck";
        MinimumSize = new Size(880, 620);
        Size = new Size(1420, 900);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = UiTheme.Canvas;
        Font = UiTheme.BodyFont;
        _allowLocalFixtureWrites.Checked = LocalFixtureWriteSettings.IsEnabled();

        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0), BackColor = UiTheme.Canvas };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 248));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shell.Controls.Add(BuildWorkspace(), 1, 0);
        shell.Controls.Add(BuildSidebar(), 0, 0);
        Controls.Add(shell);
        ConfigureGrid();
        LoadProject();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (_workspaceScroll is not null) FitWorkspaceCards(_workspaceScroll);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_workspaceScroll is not null) FitWorkspaceCards(_workspaceScroll);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        if (_workspaceScroll is not null) FitWorkspaceCards(_workspaceScroll);
    }

    private readonly Label _emptyState = new();

    private Control BuildSidebar()
    {
        var sidebar = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.BrandDark, Padding = new Padding(18, 22, 18, 18) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = UiTheme.BrandDark, Margin = new Padding(0) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));

        var brand = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
        brand.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42));
        brand.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var mark = new Label { Text = "B", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font(UiTheme.FontFamily, 14F, FontStyle.Bold), ForeColor = Color.White, BackColor = UiTheme.Brand };
        brand.Controls.Add(mark, 0, 0);
        var brandCopy = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(10, 2, 0, 0) };
        brandCopy.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        brandCopy.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        brandCopy.Controls.Add(new Label { Text = "BoundaryCheck", Dock = DockStyle.Fill, ForeColor = Color.White, Font = new Font(UiTheme.FontFamily, 9F, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true }, 0, 0);
        brandCopy.Controls.Add(new Label { Text = "API 권한 경계 점검", Dock = DockStyle.Fill, ForeColor = Color.FromArgb(183, 201, 190), Font = UiTheme.SmallFont, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        brand.Controls.Add(brandCopy, 1, 0);
        layout.Controls.Add(brand, 0, 0);

        var nav = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = false, BackColor = UiTheme.BrandDark, Margin = new Padding(0) };
        nav.Controls.Add(SidebarSectionLabel("시작 순서"));
        var start = SidebarAction("처음 시작", active: true);
        start.Click += (_, _) => { if (_capabilityBanner is not null) _workspaceScroll?.ScrollControlIntoView(_capabilityBanner); };
        nav.Controls.Add(start);
        var requests = SidebarAction("요청 목록", active: false);
        requests.Click += (_, _) => { if (_requestCard is not null) _workspaceScroll?.ScrollControlIntoView(_requestCard); };
        nav.Controls.Add(requests);
        var identities = SidebarAction("테스트 신원", active: false);
        identities.Click += (_, _) => ManageIdentities();
        nav.Controls.Add(identities);
        var fixtures = SidebarAction("로컬 예제", active: false);
        fixtures.Click += (_, _) => OpenLocalFixture();
        nav.Controls.Add(fixtures);
        layout.Controls.Add(nav, 0, 1);

        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        footer.Controls.Add(new Label { Text = "프로젝트와 검사 자료는\n이 PC 안에 저장됩니다.", Dock = DockStyle.Fill, ForeColor = Color.FromArgb(193, 208, 199), Font = UiTheme.SmallFont, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        footer.Controls.Add(new Label { Text = $"v{UpdateClient.CurrentVersion()}", Dock = DockStyle.Fill, ForeColor = Color.FromArgb(149, 170, 157), Font = UiTheme.SmallFont, TextAlign = ContentAlignment.BottomLeft }, 0, 1);
        layout.Controls.Add(footer, 0, 2);
        sidebar.Controls.Add(layout);
        return sidebar;
    }

    private Control BuildWorkspace()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = UiTheme.Canvas, Margin = new Padding(0) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = UiTheme.Surface, Padding = new Padding(26, 12, 26, 12), Margin = new Padding(0) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 270));
        var heading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
        heading.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        heading.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        heading.Controls.Add(new Label { Text = "권한 점검 시작", Dock = DockStyle.Fill, ForeColor = UiTheme.Ink, Font = UiTheme.PageTitleFont, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        heading.Controls.Add(new Label { Text = "요청을 정리하고 테스트 신원별 기대 접근값을 준비합니다.", Dock = DockStyle.Fill, ForeColor = UiTheme.MutedInk, Font = UiTheme.BodyFont, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        header.Controls.Add(heading, 0, 0);
        var headerActions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(0, 8, 0, 0), Margin = new Padding(0) };
        var updateButton = Button("업데이트 확인", false);
        updateButton.Click += async (_, _) => await CheckForUpdatesAsync(updateButton);
        headerActions.Controls.Add(updateButton);
        var saveButton = Button("저장", true);
        saveButton.Click += (_, _) => SaveProject(showMessage: true);
        headerActions.Controls.Add(saveButton);
        header.Controls.Add(headerActions, 1, 0);
        layout.Controls.Add(header, 0, 0);

        var body = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(24, 18, 24, 18), BackColor = UiTheme.Canvas, Margin = new Padding(0) };
        _capabilityBanner = BuildCapabilityBanner();
        body.Controls.Add(_capabilityBanner);
        body.Controls.Add(BuildProjectCard());
        body.Controls.Add(BuildGettingStartedCard());
        _requestCard = BuildRequestCard();
        body.Controls.Add(_requestCard);
        body.SizeChanged += (_, _) => FitWorkspaceCards(body);
        FitWorkspaceCards(body);
        _workspaceScroll = body;
        layout.Controls.Add(body, 0, 1);
        return layout;
    }

    private static Panel BuildCapabilityBanner()
    {
        var banner = new Panel { Height = 78, BackColor = UiTheme.WarningSoft, Padding = new Padding(16, 9, 16, 9), Margin = new Padding(0, 0, 0, 10) };
        var copy = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
        copy.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        copy.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        copy.Controls.Add(new Label { Text = "현재 검사 범위", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiTheme.WarningInk, Font = UiTheme.LabelFont }, 0, 0);
        var details = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
        details.RowStyles.Add(new RowStyle(SizeType.Percent, 52));
        details.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
        details.Controls.Add(new Label { Text = "가져온 요청은 실제 프로젝트로 전송되지 않습니다.", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiTheme.Ink, Font = UiTheme.LabelFont }, 0, 0);
        details.Controls.Add(new Label { Text = "지금 실행할 수 있는 검사는 내장 로컬 예제뿐입니다. 프로젝트 자료는 이 PC에 저장됩니다.", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiTheme.WarningInk, Font = UiTheme.SmallFont }, 0, 1);
        copy.Controls.Add(details, 1, 0);
        banner.Controls.Add(copy);
        return banner;
    }

    private Panel BuildGettingStartedCard()
    {
        var card = UiTheme.CreateCard(16);
        card.Height = 186;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var heading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        heading.Controls.Add(new Label { Text = "시작하기", Dock = DockStyle.Fill, ForeColor = UiTheme.Ink, Font = UiTheme.SectionFont, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        var importHelp = new LinkLabel { Text = "필요한 파일 알아보기", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Font = UiTheme.SmallFont, LinkColor = UiTheme.Brand, ActiveLinkColor = UiTheme.BrandHover };
        importHelp.LinkClicked += (_, _) => ShowImportGuide();
        heading.Controls.Add(importHelp, 1, 0);
        layout.Controls.Add(heading, 0, 0);

        var steps = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0, 3, 0, 0) };
        steps.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
        steps.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        steps.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));

        var harButton = Button("브라우저 기록 파일", true);
        harButton.Click += (_, _) => ImportFile(true);
        var openApiButton = Button("API 문서 파일", false);
        openApiButton.Click += (_, _) => ImportFile(false);
        steps.Controls.Add(BuildWorkflowStep("02", "파일 가져오기", "둘 중 하나면 됩니다: 브라우저 기록(.har) 또는 API 문서(.json)", _requestStepStatus, harButton, openApiButton), 0, 0);

        var identityButton = Button("신원 설정", false);
        identityButton.Click += (_, _) => ManageIdentities();
        steps.Controls.Add(BuildWorkflowStep("03", "신원·규칙", "신원을 만든 뒤 표에서 허용·거부를 지정합니다.", _identityStepStatus, identityButton), 1, 0);

        var exampleButton = Button("예제 보기", false);
        exampleButton.Click += (_, _) => OpenLocalFixture();
        steps.Controls.Add(BuildWorkflowStep("04", "결과 미리보기", "가상 서비스 · 가져온 요청과 별도", null, exampleButton), 2, 0);
        layout.Controls.Add(steps, 0, 1);
        card.Controls.Add(layout);
        card.Paint += (_, args) => UiTheme.DrawCardBorder(card, args);
        return card;
    }

    private static Control BuildWorkflowStep(string number, string title, string description, Label? status, params Button[] actions)
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.SurfaceMuted, Padding = new Padding(12, 8, 12, 8), Margin = new Padding(0, 0, 8, 0) };
        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = new Padding(0) };
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        var heading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = status is null ? 1 : 2, RowCount = 1, Margin = new Padding(0) };
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        if (status is not null) heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
        heading.Controls.Add(new Label { Text = $"{number}  {title}", Dock = DockStyle.Fill, ForeColor = UiTheme.BrandDark, Font = UiTheme.StepFont, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true }, 0, 0);
        if (status is not null)
        {
            status.Dock = DockStyle.Fill;
            status.TextAlign = ContentAlignment.MiddleRight;
            status.ForeColor = UiTheme.Brand;
            status.Font = UiTheme.SmallFont;
            heading.Controls.Add(status, 1, 0);
        }
        content.Controls.Add(heading, 0, 0);
        content.Controls.Add(new Label { Text = description, Dock = DockStyle.Fill, ForeColor = UiTheme.MutedInk, Font = UiTheme.LabelFont, TextAlign = ContentAlignment.TopLeft, AutoEllipsis = true }, 0, 1);
        var actionRow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Margin = new Padding(0, 2, 0, 0) };
        actionRow.Controls.AddRange(actions);
        content.Controls.Add(actionRow, 0, 2);
        panel.Controls.Add(content);
        panel.Paint += (_, args) => UiTheme.DrawCardBorder(panel, args);
        return panel;
    }

    private Panel BuildProjectCard()
    {
        var card = UiTheme.CreateCard(18);
        card.Height = 190;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = new Padding(0) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(SectionHeading("1. 가져올 범위", "이 주소와 경로에 맞는 항목만 정리합니다 · 서버로 보내지 않음"), 0, 0);

        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0, 2, 0, 0) };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        fields.Controls.Add(FieldStack("프로젝트 이름", _name), 0, 0);
        fields.Controls.Add(FieldStack("웹사이트 주소", _origin), 1, 0);
        fields.Controls.Add(FieldStack("API 경로 시작 부분", _prefix), 2, 0);
        layout.Controls.Add(fields, 0, 1);

        _allowLocalFixtureWrites.ForeColor = UiTheme.Ink;
        _allowLocalFixtureWrites.Margin = new Padding(2, 7, 12, 0);
        _allowLocalFixtureWrites.CheckedChanged += (_, _) => SaveLocalFixtureWriteSetting();
        var writeSettings = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Margin = new Padding(0), Padding = new Padding(0, 4, 0, 0) };
        writeSettings.Controls.Add(_allowLocalFixtureWrites);
        writeSettings.Controls.Add(new Label { Text = "기본 꺼짐 · 내장 예제만", AutoSize = true, ForeColor = UiTheme.WarningInk, BackColor = UiTheme.WarningSoft, Padding = new Padding(9, 5, 9, 5), Margin = new Padding(4, 1, 0, 0), Font = UiTheme.SmallFont });
        layout.Controls.Add(writeSettings, 0, 2);
        card.Controls.Add(layout);
        card.Paint += (_, args) => UiTheme.DrawCardBorder(card, args);
        return card;
    }

    private Panel BuildRequestCard()
    {
        var card = UiTheme.CreateCard(18);
        card.Height = 390;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = new Padding(0) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.Controls.Add(SectionHeading("요청 목록", "각 요청에 테스트 신원과 허용·거부 기대값을 지정하세요."), 0, 0);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Margin = new Padding(0), Padding = new Padding(0, 4, 0, 0) };
        var harButton = Button("브라우저 기록 파일", false);
        harButton.Click += (_, _) => ImportFile(true);
        var openApiButton = Button("API 문서 JSON 파일", false);
        openApiButton.Click += (_, _) => ImportFile(false);
        var duplicateButton = Button("선택 요청 복제", false);
        duplicateButton.Click += (_, _) => DuplicateSelectedScenario();
        var addButton = Button("요청 직접 추가", true);
        addButton.Click += (_, _) => { _scenarios.Add(new Scenario()); UpdateSummary(); SaveProject(false); };
        actions.Controls.AddRange([harButton, openApiButton, duplicateButton, addButton]);
        layout.Controls.Add(actions, 0, 1);

        var body = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface, Margin = new Padding(0) };
        _grid.Dock = DockStyle.Fill;
        body.Controls.Add(_grid);
        _emptyState.Text = "아직 가져온 경로가 없습니다\n내 앱에서 내보낸 브라우저 기록(.har) 또는 개발 API 문서(.json)를 가져오세요.\n필요한 파일을 모르겠다면 위 ‘필요한 파일 알아보기’를 선택하세요.";
        _emptyState.Dock = DockStyle.Fill;
        _emptyState.TextAlign = ContentAlignment.MiddleCenter;
        _emptyState.ForeColor = UiTheme.MutedInk;
        _emptyState.Font = UiTheme.BodyFont;
        _emptyState.BackColor = UiTheme.Surface;
        body.Controls.Add(_emptyState);
        layout.Controls.Add(body, 0, 2);

        _summary.Dock = DockStyle.Fill;
        _summary.ForeColor = UiTheme.MutedInk;
        _summary.TextAlign = ContentAlignment.MiddleLeft;
        _summary.Font = UiTheme.SmallFont;
        layout.Controls.Add(_summary, 0, 3);
        card.Controls.Add(layout);
        card.Paint += (_, args) => UiTheme.DrawCardBorder(card, args);
        return card;
    }

    private static Control SectionHeading(string title, string detail)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        row.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, ForeColor = UiTheme.Ink, Font = UiTheme.SectionFont, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        row.Controls.Add(new Label { Text = detail, Dock = DockStyle.Fill, ForeColor = UiTheme.MutedInk, Font = UiTheme.SmallFont, TextAlign = ContentAlignment.MiddleRight }, 1, 0);
        return row;
    }

    private void FitWorkspaceCards(FlowLayoutPanel body)
    {
        var width = Math.Max(560, body.ClientSize.Width - body.Padding.Horizontal - 4);
        foreach (Control control in body.Controls) control.Width = width;

        if (_requestCard is null || body.ClientSize.Height <= 0) return;

        var precedingHeight = 0;
        foreach (Control control in body.Controls)
        {
            if (ReferenceEquals(control, _requestCard)) break;
            precedingHeight += control.Height + control.Margin.Vertical;
        }
        var availableHeight = body.ClientSize.Height
            - body.Padding.Vertical
            - precedingHeight
            - _requestCard.Margin.Vertical;
        var minHeight = UiTheme.ScaleLogical(260, DeviceDpi);
        var preferredHeight = UiTheme.ScaleLogical(390, DeviceDpi);
        var fittedHeight = Math.Max(minHeight, Math.Min(preferredHeight, availableHeight));
        if (_requestCard.Height != fittedHeight) _requestCard.Height = fittedHeight;
    }

    private static Control FieldStack(string label, TextBox input)
    {
        var stack = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0, 0, 12, 0) };
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        stack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        stack.Controls.Add(FieldLabel(label), 0, 0);
        UiTheme.StyleInput(input);
        stack.Controls.Add(input, 0, 1);
        return stack;
    }

    private static Label SidebarSectionLabel(string text) => new()
    {
        Text = text,
        Width = 200,
        Height = 34,
        Margin = new Padding(4, 2, 0, 8),
        ForeColor = Color.FromArgb(149, 170, 157),
        Font = UiTheme.SmallFont,
        TextAlign = ContentAlignment.MiddleLeft,
    };

    private static Button SidebarAction(string text, bool active)
    {
        var button = new Button
        {
            Text = text,
            Width = 200,
            Height = 44,
            AutoSize = false,
            FlatStyle = FlatStyle.Flat,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(14, 0, 8, 0),
            Margin = new Padding(0, 0, 0, 6),
            BackColor = active ? Color.FromArgb(35, 76, 57) : UiTheme.BrandDark,
            ForeColor = Color.FromArgb(232, 239, 234),
            Font = UiTheme.ButtonFont,
            Cursor = Cursors.Hand,
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = UiTheme.BrandHover;
        button.GotFocus += (_, _) =>
        {
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = UiTheme.Focus;
        };
        button.LostFocus += (_, _) =>
        {
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.BorderColor = UiTheme.BrandDark;
        };
        return button;
    }

    private void OpenLocalFixture()
    {
        using var dialog = new LocalFixtureDialog();
        dialog.ShowDialog(this);
    }

    private void ShowImportGuide()
    {
        using var dialog = new ImportGuideDialog();
        dialog.ShowDialog(this);
    }

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
            var helper = MaterializeEmbeddedUpdateHelper();
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

    private static string MaterializeEmbeddedUpdateHelper()
    {
        const string resourceName = "BoundaryCheck.apply-update.ps1";
        var helperDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BoundaryCheck",
            "Updates");
        Directory.CreateDirectory(helperDirectory);

        var helperPath = Path.Combine(helperDirectory, $"apply-update-{Guid.NewGuid():N}.ps1");
        using var resource = typeof(Program).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("앱에 내장된 업데이트 도우미를 찾을 수 없습니다. 공식 설치 ZIP을 다시 받아 주세요.");
        using var destination = new FileStream(helperPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        resource.CopyTo(destination);
        return helperPath;
    }

    private static Panel Card() => UiTheme.CreateCard(1);
    private static Label FieldLabel(string text) => new() { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiTheme.MutedInk, Font = UiTheme.LabelFont };
    private static TextBox StyleInput(TextBox input) { UiTheme.StyleInput(input); return input; }

    internal static Button Button(string text, bool primary)
    {
        var button = new Button { Text = text, AutoSize = true, Height = 40, FlatStyle = FlatStyle.Flat, Padding = new Padding(14, 3, 14, 3), Margin = new Padding(0, 0, 8, 0), Cursor = Cursors.Hand, Font = UiTheme.ButtonFont, UseVisualStyleBackColor = false };
        button.FlatAppearance.BorderSize = primary ? 0 : 1;
        button.FlatAppearance.BorderColor = UiTheme.Border;
        button.FlatAppearance.MouseOverBackColor = primary ? UiTheme.BrandHover : UiTheme.SurfaceMuted;
        button.FlatAppearance.MouseDownBackColor = primary ? UiTheme.BrandDark : UiTheme.Border;
        button.BackColor = primary ? UiTheme.Brand : UiTheme.Surface;
        button.ForeColor = primary ? Color.White : UiTheme.Ink;
        button.GotFocus += (_, _) =>
        {
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = UiTheme.Focus;
        };
        button.LostFocus += (_, _) =>
        {
            button.FlatAppearance.BorderSize = primary ? 0 : 1;
            button.FlatAppearance.BorderColor = UiTheme.Border;
        };
        return button;
    }

    private void ConfigureGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = false;
        _grid.RowHeadersVisible = false;
        _grid.AllowUserToAddRows = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.BackgroundColor = UiTheme.Surface;
        UiTheme.StyleGrid(_grid);
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(Scenario.Method), HeaderText = "방식", Width = 92, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(Scenario.Path), HeaderText = "요청 경로", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 180, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewComboBoxColumn
        {
            Name = "Identity",
            DataPropertyName = nameof(Scenario.IdentityId),
            HeaderText = "테스트 신원",
            Width = 195,
            DataSource = IdentityChoicesForProject(),
            ValueMember = nameof(IdentityChoice.Id),
            DisplayMember = nameof(IdentityChoice.Label),
        });
        _grid.Columns.Add(new DataGridViewComboBoxColumn { DataPropertyName = nameof(Scenario.Expectation), HeaderText = "기대 접근", Width = 142, DataSource = new[] { "미지정", "허용", "거부" } });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(Scenario.Occurrences), HeaderText = "횟수", Width = 78, ReadOnly = true });
        _grid.DataSource = _scenarios;
        _grid.CellValueChanged += (_, _) => { UpdateSummary(); SaveProject(false); };
        _grid.CurrentCellDirtyStateChanged += (_, _) => { if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        _grid.DataError += (_, e) => { e.ThrowException = false; };
    }

    private void ImportFile(bool isHar)
    {
        using var picker = new OpenFileDialog
        {
            Filter = isHar ? "브라우저 기록 파일 (*.har;*.json)|*.har;*.json|모든 파일 (*.*)|*.*" : "개발 API 문서 (*.json)|*.json|모든 파일 (*.*)|*.*",
            Title = isHar ? "브라우저에서 내보낸 기록 파일 선택" : "개발 API 문서 JSON 파일 선택",
        };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var imported = isHar ? ParseHar(picker.FileName) : ParseOpenApi(picker.FileName);
            foreach (var item in imported) MergeScenario(item);
            UpdateSummary();
            SaveProject(false);
            var title = isHar ? "브라우저 기록 가져오기" : "API 명세 가져오기";
            var message = imported.Count > 0
            ? $"{imported.Count}개 경로를 목록에 반영했습니다.\n가져온 요청은 실제 프로젝트로 전송되지 않습니다."
                : isHar
                    ? "조건에 맞는 요청을 찾지 못했습니다.\n웹사이트 주소와 API 경로가 기록 파일과 맞는지 확인해 주세요. GET/PATCH 요청만 가져옵니다."
                    : "조건에 맞는 경로를 찾지 못했습니다.\nAPI 경로를 확인해 주세요. OpenAPI 3.0/3.1 JSON의 GET/PATCH만 가져옵니다.";
            MessageBox.Show(this, message, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "파일을 읽지 못했습니다", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private List<Scenario> ParseHar(string file)
    {
        using var doc = ReadJson(file, "유효한 브라우저 기록 JSON이 아닙니다. .har 파일을 선택하세요.");
        if (doc.RootElement.ValueKind != JsonValueKind.Object
            || !doc.RootElement.TryGetProperty("log", out var log)
            || log.ValueKind != JsonValueKind.Object
            || !log.TryGetProperty("entries", out var entries)
            || entries.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("이 파일은 브라우저 요청 기록(.har)이 아닙니다. Chrome 또는 Edge 개발자 도구의 네트워크 탭에서 HAR로 내보낸 파일을 선택하세요.");
        if (entries.GetArrayLength() > 5000) throw new InvalidDataException("HAR 파일은 최대 5,000개 항목까지 가져올 수 있습니다.");
        var origin = ReadOrigin(); var prefix = ReadPrefix();
        var list = new List<Scenario>();
        foreach (var entry in entries.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object
                || !entry.TryGetProperty("request", out var request)
                || request.ValueKind != JsonValueKind.Object
                || !request.TryGetProperty("url", out var urlValue)
                || !request.TryGetProperty("method", out var methodValue)
                || urlValue.ValueKind != JsonValueKind.String
                || methodValue.ValueKind != JsonValueKind.String) continue;
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
        using var doc = ReadJson(file, "이 파일을 읽을 수 없습니다. 앱은 OpenAPI 3.0/3.1 형식의 개발 API JSON 문서만 지원합니다. YAML은 사용할 수 없습니다. 파일을 찾기 어렵다면 개발 담당자에게 openapi.json을 요청하세요.");
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("이 파일은 API 문서 JSON 형식이 아닙니다. 개발 담당자에게 OpenAPI 3.0/3.1 JSON 파일을 요청하세요.");
        if (!root.TryGetProperty("openapi", out var version)
            || version.ValueKind != JsonValueKind.String
            || !Regex.IsMatch(version.GetString() ?? "", @"^3\.(0|1)\.")) throw new InvalidDataException("이 API 문서는 지원되는 형식이 아닙니다. OpenAPI 3.0/3.1 JSON만 읽을 수 있어요. 파일을 찾기 어렵다면 개발 담당자에게 openapi.json을 요청하세요.");
        if (!root.TryGetProperty("paths", out var paths) || paths.ValueKind != JsonValueKind.Object) throw new InvalidDataException("API 문서에서 요청 경로 목록을 찾지 못했습니다. OpenAPI 3.0/3.1 JSON 파일인지 확인해 주세요.");
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

    private static JsonDocument ReadJson(string file, string invalidJsonMessage)
    {
        var info = new FileInfo(file);
        if (info.Length > 10 * 1024 * 1024) throw new InvalidDataException("입력 파일은 10 MiB를 넘을 수 없습니다.");
        try { return JsonDocument.Parse(File.ReadAllBytes(file)); }
        catch (JsonException) { throw new InvalidDataException(invalidJsonMessage); }
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
        _requestStepStatus.Text = $"{_scenarios.Count}개";
        _identityStepStatus.Text = $"{_identities.Count}명";
        _summary.Text = $"요청 {_scenarios.Count}개 · 테스트 신원 {_identities.Count}개 · 신원 연결 {assigned}개 · 기대 접근값 지정 {specified}개 · 자료는 이 PC에 저장";
    }

    private sealed record IdentityChoice(string Id, string Label);
}

internal sealed class ImportGuideDialog : DpiAwareForm
{
    public ImportGuideDialog()
    {
        Text = "어떤 파일을 가져오나요?";
        MinimumSize = new Size(640, 520);
        Size = new Size(780, 700);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = UiTheme.Canvas;
        Font = UiTheme.BodyFont;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(22), Margin = new Padding(0), BackColor = UiTheme.Canvas };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        var intro = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
        intro.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        intro.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        intro.Controls.Add(new Label { Text = "둘 중 하나만 있으면 돼요", Dock = DockStyle.Fill, ForeColor = UiTheme.Ink, Font = UiTheme.SectionFont, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        intro.Controls.Add(new Label { Text = "가져올 파일이 없다면 ‘예제 보기’를 눌러 가상 결과를 볼 수 있어요. 이 결과는 내 앱 검사 결과가 아닙니다.", Dock = DockStyle.Fill, ForeColor = UiTheme.MutedInk, Font = UiTheme.LabelFont, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        layout.Controls.Add(intro, 0, 0);

        var body = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Margin = new Padding(0), Padding = new Padding(0, 2, 0, 2), BackColor = UiTheme.Canvas };
        body.Controls.Add(GuideCard(
            "1. 내 앱에서 만든 브라우저 기록 (.har)",
            "Chrome 또는 Edge에서 내 앱을 열고 F12를 누른 뒤 네트워크(Network) 탭을 선택하세요. 테스트 계정으로 로그인해 필요한 화면을 사용하고, 네트워크 요청 목록을 HAR 파일로 내보냅니다.\n\n파일에는 실제 사용 중 발생한 요청만 들어 있습니다. 본인 소유이거나 검사 승인을 받은 테스트 앱에서 기록하세요.",
            164));
        body.Controls.Add(GuideCard(
            "2. 개발 API 문서 (.json)",
            "개발 폴더나 API 문서 사이트에서 openapi.json을 찾거나, 개발 담당자에게 “OpenAPI 3.0/3.1 JSON 파일”을 요청하세요. 파일 안의 버전이 3.0 또는 3.1이어야 합니다.\n\nYAML(.yaml)과 Swagger 2.0 JSON은 지원하지 않습니다. 이름이 swagger.json이어도 파일 안에 OpenAPI 3.0/3.1이라고 표시된 경우에만 사용할 수 있습니다.",
            164));
        body.Controls.Add(GuideCard(
            "가져오기 전에 확인하세요",
            "코드 폴더, 압축 파일, 화면 캡처, 비밀번호는 가져올 필요가 없습니다. HAR에는 로그인 쿠키나 토큰이 포함될 수 있으니 테스트 계정으로 기록하고 파일을 공개하지 마세요.\n\n웹사이트 주소에는 도메인만 입력하세요(예: https://myapp.com, /login은 제외). API 경로는 /api/ 또는 /v1/처럼 시작 부분을 입력합니다. 조건에 맞는 GET/PATCH의 방식·경로만 이 PC에 저장합니다. 헤더·쿠키·검색어·본문은 저장하지 않고 실제 서버로 요청도 보내지 않습니다. 경로에 사용자 ID 같은 정보가 있는지 가져온 뒤 확인하세요.",
            190,
            warning: true));
        layout.Controls.Add(body, 0, 1);

        body.SizeChanged += (_, _) =>
        {
            var width = Math.Max(300, body.ClientSize.Width - body.Padding.Horizontal - 8);
            foreach (Control control in body.Controls) control.Width = width;
        };

        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = new Padding(0), Padding = new Padding(0, 6, 0, 0) };
        var close = MainForm.Button("닫기", true);
        close.Click += (_, _) => Close();
        footer.Controls.Add(close);
        layout.Controls.Add(footer, 0, 2);
        Controls.Add(layout);
    }

    private static Panel GuideCard(string title, string description, int height, bool warning = false)
    {
        var card = new Panel
        {
            Height = height,
            BackColor = warning ? UiTheme.WarningSoft : UiTheme.Surface,
            Padding = new Padding(15, 10, 15, 10),
            Margin = new Padding(0, 0, 0, 10),
        };
        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, ForeColor = warning ? UiTheme.WarningInk : UiTheme.BrandDark, Font = UiTheme.StepFont, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        content.Controls.Add(new Label { Text = description, Dock = DockStyle.Fill, ForeColor = warning ? UiTheme.WarningInk : UiTheme.Ink, Font = UiTheme.LabelFont, TextAlign = ContentAlignment.TopLeft }, 0, 1);
        card.Controls.Add(content);
        card.Paint += (_, args) => UiTheme.DrawCardBorder(card, args);
        return card;
    }
}

internal sealed class TestIdentityDialog : DpiAwareForm
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
        MinimumSize = new Size(720, 520);
        Size = new Size(820, 600);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = UiTheme.Canvas;
        Font = UiTheme.BodyFont;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, Padding = new Padding(24), BackColor = UiTheme.Canvas };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        Controls.Add(layout);

        var title = new Label { Text = "테스트 신원", Font = UiTheme.SectionFont, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiTheme.Ink };
        layout.Controls.Add(title, 0, 0);
        layout.SetColumnSpan(title, 2);

        _list.Format += (_, args) =>
        {
            if (args.ListItem is TestIdentity identity) args.Value = string.IsNullOrWhiteSpace(FormatIdentity(identity)) ? "이름 없는 신원" : FormatIdentity(identity);
        };
        _list.BackColor = UiTheme.Surface;
        _list.ForeColor = UiTheme.Ink;
        _list.BorderStyle = BorderStyle.FixedSingle;
        _list.Font = UiTheme.BodyFont;
        _list.SelectedIndexChanged += (_, _) => LoadSelection();
        layout.Controls.Add(_list, 0, 1);

        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, Padding = new Padding(16, 0, 0, 0) };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < 3; row++) fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        fields.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        AddField(fields, "이름", _name, 0);
        AddField(fields, "조직", _organization, 1);
        AddField(fields, "역할", _role, 2);
        var privacy = new Label
        {
            Text = "비밀번호·토큰 같은 인증 비밀은 입력하지 마세요. 신원을 삭제하면 연결된 요청의 신원과 기대 접근값이 미지정으로 초기화됩니다.",
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.MutedInk,
            BackColor = UiTheme.SurfaceMuted,
            Padding = new Padding(12),
            Font = UiTheme.SmallFont,
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
        fields.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiTheme.MutedInk }, 0, row);
        UiTheme.StyleInput(input);
        input.Margin = new Padding(0, 3, 0, 0);
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
