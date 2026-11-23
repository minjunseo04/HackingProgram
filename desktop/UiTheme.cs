namespace BoundaryCheck;

internal static class UiTheme
{
    public static readonly Color Canvas = Color.FromArgb(245, 247, 244);
    public static readonly Color Surface = Color.White;
    public static readonly Color SurfaceMuted = Color.FromArgb(239, 243, 239);
    public static readonly Color Border = Color.FromArgb(222, 229, 223);
    public static readonly Color Ink = Color.FromArgb(31, 43, 36);
    public static readonly Color MutedInk = Color.FromArgb(103, 117, 107);
    public static readonly Color Brand = Color.FromArgb(20, 103, 75);
    public static readonly Color BrandDark = Color.FromArgb(22, 45, 35);
    public static readonly Color BrandSoft = Color.FromArgb(229, 242, 234);
    public static readonly Color WarningSoft = Color.FromArgb(250, 242, 222);
    public static readonly Color WarningInk = Color.FromArgb(124, 92, 28);

    public static Panel CreateCard(int padding = 18) => new()
    {
        Dock = DockStyle.Fill,
        BackColor = Surface,
        Padding = new Padding(padding),
        Margin = new Padding(0, 0, 0, 14),
    };

    public static void StyleInput(TextBox input)
    {
        input.Dock = DockStyle.Fill;
        input.Margin = new Padding(3, 4, 8, 6);
        input.BorderStyle = BorderStyle.FixedSingle;
        input.BackColor = Surface;
        input.ForeColor = Ink;
        input.Font = new Font("Segoe UI", 10F);
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
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 9.5F);
        grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(8, 0, 8, 0);
        grid.ColumnHeadersHeight = 44;
        grid.RowTemplate.Height = 48;
        grid.DefaultCellStyle.BackColor = Surface;
        grid.DefaultCellStyle.ForeColor = Ink;
        grid.DefaultCellStyle.Font = new Font("Segoe UI", 9.5F);
        grid.DefaultCellStyle.Padding = new Padding(8, 0, 8, 0);
        grid.DefaultCellStyle.SelectionBackColor = BrandSoft;
        grid.DefaultCellStyle.SelectionForeColor = Ink;
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(250, 252, 250);
    }

    public static void DrawCardBorder(Panel panel, PaintEventArgs args) =>
        ControlPaint.DrawBorder(args.Graphics, panel.ClientRectangle, Border, ButtonBorderStyle.Solid);
}
