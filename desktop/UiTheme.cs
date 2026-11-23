namespace BoundaryCheck;

internal static class UiTheme
{
    public static readonly string FontFamily = ResolveFontFamily();

    public static readonly Color Canvas = Color.FromArgb(246, 247, 244);
    public static readonly Color Surface = Color.White;
    public static readonly Color SurfaceMuted = Color.FromArgb(240, 244, 240);
    public static readonly Color Border = Color.FromArgb(220, 227, 221);
    public static readonly Color Ink = Color.FromArgb(34, 46, 39);
    public static readonly Color MutedInk = Color.FromArgb(97, 111, 102);
    public static readonly Color Brand = Color.FromArgb(20, 103, 75);
    public static readonly Color BrandDark = Color.FromArgb(23, 53, 42);
    public static readonly Color BrandHover = Color.FromArgb(31, 77, 59);
    public static readonly Color BrandSoft = Color.FromArgb(232, 241, 234);
    public static readonly Color WarningSoft = Color.FromArgb(248, 241, 222);
    public static readonly Color WarningInk = Color.FromArgb(118, 88, 28);
    public static readonly Color Focus = Color.FromArgb(68, 133, 105);

    public static readonly Font BodyFont = new(FontFamily, 10.5F);
    public static readonly Font LabelFont = new(FontFamily, 10F);
    public static readonly Font SmallFont = new(FontFamily, 9F);
    public static readonly Font ButtonFont = new(FontFamily, 10F);
    public static readonly Font PageTitleFont = new(FontFamily, 18F, FontStyle.Bold);
    public static readonly Font SectionFont = new(FontFamily, 12.5F, FontStyle.Bold);
    public static readonly Font MetricFont = new(FontFamily, 16F, FontStyle.Bold);
    public static readonly Font GridFont = new(FontFamily, 10F);
    public static readonly Font GridHeaderFont = new(FontFamily, 10F, FontStyle.Bold);

    private static string ResolveFontFamily()
    {
        var installed = System.Drawing.FontFamily.Families
            .Select(family => family.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (installed.Contains("맑은 고딕")) return "맑은 고딕";
        if (installed.Contains("Malgun Gothic")) return "Malgun Gothic";
        if (installed.Contains("Segoe UI")) return "Segoe UI";
        return SystemFonts.MessageBoxFont?.Name ?? "Arial";
    }

    public static Panel CreateCard(int padding = 18) => new()
    {
        Dock = DockStyle.Fill,
        BackColor = Surface,
        Padding = new Padding(padding),
        Margin = new Padding(0, 0, 0, 12),
    };

    public static void StyleInput(TextBox input)
    {
        input.Dock = DockStyle.Fill;
        input.Margin = new Padding(0, 3, 0, 0);
        input.BorderStyle = BorderStyle.FixedSingle;
        input.BackColor = Surface;
        input.ForeColor = Ink;
        input.Font = BodyFont;
    }

    public static void StyleGrid(DataGridView grid)
    {
        grid.BackgroundColor = Surface;
        grid.BorderStyle = BorderStyle.None;
        grid.GridColor = Border;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grid.ColumnHeadersDefaultCellStyle.BackColor = SurfaceMuted;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = MutedInk;
        grid.ColumnHeadersDefaultCellStyle.Font = GridHeaderFont;
        grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(9, 0, 9, 0);
        grid.ColumnHeadersHeight = 42;
        grid.RowTemplate.Height = 46;
        grid.DefaultCellStyle.BackColor = Surface;
        grid.DefaultCellStyle.ForeColor = Ink;
        grid.DefaultCellStyle.Font = GridFont;
        grid.DefaultCellStyle.Padding = new Padding(8, 0, 8, 0);
        grid.DefaultCellStyle.SelectionBackColor = BrandSoft;
        grid.DefaultCellStyle.SelectionForeColor = Ink;
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(249, 251, 249);
    }

    public static void DrawCardBorder(Panel panel, PaintEventArgs args) =>
        ControlPaint.DrawBorder(args.Graphics, panel.ClientRectangle, Border, ButtonBorderStyle.Solid);
}
