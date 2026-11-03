using MultipleFormat.Domain.Common.Enums;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using System.Globalization;

namespace MultipleFormat.Application.Modules.FormRequests
{
    public class MultipleFormatPdfData
    {
        public DateTime Date { get; set; }
        public string EmployeeName { get; set; } = string.Empty;
        public string EmployeeNumber { get; set; } = string.Empty;
        public DateTime? HireDate { get; set; }
        public string Position { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public DateTime? EffectiveDate { get; set; }
        public string? SupervisorName { get; set; }
        public string? CurrentPlant { get; set; }
        public string? CurrentShift { get; set; }

        public FormType FormType { get; set; }
        public string? NewPlant { get; set; }
        public string? NewShift { get; set; }
        public string? NewDepartment { get; set; }
        public string? NewSupervisor { get; set; }
        public string? NewArea { get; set; }

        public int? Days { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public DateTime? ReturnDate { get; set; }
        public PermissionType? PermissionType { get; set; }

        public string? Comments { get; set; }

        public string? PunchType { get; set; }
        public TimeSpan? EntryTime { get; set; }
        public TimeSpan? ExitTime { get; set; }
        public string? Justification { get; set; }
    }

    public static class MultipleFormatPdfBuilder
    {
        private const double Left = 30;
        private const double Width = 552;
        private static readonly CultureInfo DateCulture = new CultureInfo("es-MX");

        static MultipleFormatPdfBuilder()
        {
            if (OperatingSystem.IsWindows())
            {
                GlobalFontSettings.UseWindowsFontsUnderWindows = true;
            }
        }

        public static byte[] Build(MultipleFormatPdfData d)
        {
            if (d.FormType == FormType.TimeAdjustment)
            {
                return BuildTimeAdjustment(d);
            }

            using var document = new PdfDocument();
            document.Info.Title = "Formato Multiple de Personal";
            var page = document.AddPage();
            page.Size = PdfSharp.PageSize.Letter;
            using var g = XGraphics.FromPdfPage(page);

            var normal = new XFont("Arial", 7);
            var bold = new XFont("Arial", 7, XFontStyleEx.Bold);
            var title = new XFont("Arial", 11, XFontStyleEx.Bold);
            var logo = new XFont("Arial", 18, XFontStyleEx.BoldItalic);

            double y = 30;

            // Encabezado
            DrawLogo(g, Left, y, 130, 36);
            Cell(g, Left + 140, y, 200, 18, "FECHA", bold, center: true, fill: XBrushes.LightGray);
            Cell(g, Left + 340, y, 212, 18, Fmt(d.Date), bold, center: true);
            Cell(g, Left + 140, y + 18, 412, 18, "FORMATO MULTIPLE DE PERSONAL", title, center: true);
            y += 44;

            // Datos empleado
            Bar(g, y, "DATOS EMPLEADO", bold); y += 12;
            LabeledCell(g, Left, y, 150, "NOMBRE DEL EMPLEADO", d.EmployeeName, normal, bold);
            LabeledCell(g, Left + 150, y, 80, "NUMERO DE RELOJ", d.EmployeeNumber, normal, bold);
            LabeledCell(g, Left + 250, y, 302, "FECHA DE INGRESO", Fmt(d.HireDate), normal, bold);
            y += 36;
            LabeledCell(g, Left, y, 150, "PUESTO ACTUAL", d.Position, normal, bold);
            LabeledCell(g, Left + 150, y, 80, "DEPARTAMENTO", d.Department, normal, bold);
            LabeledCell(g, Left + 250, y, 302, "FECHA EFECTIVA DEL MOVIMIENTO", Fmt(d.EffectiveDate), normal, bold);
            y += 40;

            // Tipo de movimiento
            Bar(g, y, "TIPO DE MOVIMIENTO", bold); y += 12;
            CheckBox(g, Left + 40, y + 6, d.FormType == FormType.DepartmentShiftChange, "CAMBIO DEP. Y/O TURNO", normal);
            CheckBox(g, Left + 220, y + 6, d.FormType == FormType.Vacation, "VACACIONES", normal);
            CheckBox(g, Left + 370, y + 6, d.FormType == FormType.Permission, "PERMISO", normal);
            y += 24;

            // Cambio de depto / turno
            Bar(g, y, "CAMBIO DE DEPARTAMENTO Y/O TURNO", bold); y += 12;
            g.DrawString("DATOS ACTUALES", normal, XBrushes.Black, new XRect(Left, y, 260, 10), XStringFormats.TopCenter);
            g.DrawString("DATOS NUEVOS", bold, XBrushes.Black, new XRect(Left + 290, y, 260, 10), XStringFormats.TopCenter);
            y += 12;
            var isChange = d.FormType == FormType.DepartmentShiftChange;
            var rows = new (string Label, string? Current, string? New)[]
            {
                ("PLANTA", d.CurrentPlant, d.NewPlant),
                ("TURNO", d.CurrentShift, d.NewShift),
                ("DEPTO.", d.Department, d.NewDepartment),
                ("SUPERVISOR", d.SupervisorName, d.NewSupervisor),
                ("AREA", null, d.NewArea),
            };
            foreach (var row in rows)
            {
                Cell(g, Left, y, 70, 16, row.Label, bold, center: true);
                Cell(g, Left + 70, y, 190, 16, isChange ? row.Current : null, normal);
                Cell(g, Left + 290, y, 70, 16, row.Label, bold, center: true);
                Cell(g, Left + 360, y, 192, 16, isChange ? row.New : null, normal);
                y += 22;
            }
            g.DrawString("NOTA: SOLO INDICAR EL CAMPO QUE SE VA A MODIFICAR", new XFont("Arial", 6.5, XFontStyleEx.BoldItalic),
                XBrushes.Black, new XRect(Left + 4, y - 4, 300, 10), XStringFormats.TopLeft);
            y += 12;

            // Vacaciones
            Bar(g, y, "VACACIONES", bold); y += 14;
            var isVacation = d.FormType == FormType.Vacation;
            DaysTable(g, y, isVacation ? d : null, normal, bold);
            y += 74;

            // Permisos
            Bar(g, y, "PERMISOS", bold); y += 14;
            CheckBox(g, Left + 50, y + 4, d.PermissionType == PermissionType.Marriage, "MATRIMONIO", normal);
            CheckBox(g, Left + 50, y + 16, d.PermissionType == PermissionType.Birth, "NACIMIENTO", normal);
            CheckBox(g, Left + 50, y + 28, d.PermissionType == PermissionType.Death, "FALLECIMIENTO FAMILIAR", normal);
            CheckBox(g, Left + 290, y + 4, d.PermissionType == PermissionType.WithoutPay, "PERMISO SIN GOCE DE SUELDO", normal);
            CheckBox(g, Left + 290, y + 16, d.PermissionType == PermissionType.Swap, "PERMUTA (ESPECIFICAR LAS HORAS DE LA SEMANA)", normal);
            g.DrawString("**ANEXAR COMPROBANTE (CERTIFICADO/ACTA/ETC)", new XFont("Arial", 6.5, XFontStyleEx.BoldItalic),
                XBrushes.Black, new XRect(Left + 4, y + 44, 300, 10), XStringFormats.TopLeft);
            y += 62;
            DaysTable(g, y, d.FormType == FormType.Permission ? d : null, normal, bold);
            y += 74;

            // Comentarios y firma
            Cell(g, Left, y, 276, 12, "COMENTARIOS", bold, center: true, fill: XBrushes.LightGray);
            Cell(g, Left + 276, y, 276, 12, "FIRMA EMPLEADO", bold, center: true, fill: XBrushes.LightGray);
            y += 12;
            var tf = new PdfSharp.Drawing.Layout.XTextFormatter(g);
            g.DrawRectangle(XPens.Black, new XRect(Left, y, 276, 60));
            tf.DrawString(d.Comments ?? string.Empty, normal, XBrushes.Black, new XRect(Left + 3, y + 3, 270, 54), XStringFormats.TopLeft);
            g.DrawRectangle(XPens.Black, new XRect(Left + 276, y, 276, 40));
            Cell(g, Left + 276, y + 40, 80, 20, "NOMBRE:", normal, center: true);
            Cell(g, Left + 356, y + 40, 196, 20, d.EmployeeName, normal);
            y += 70;

            // Autorizaciones
            Bar(g, y, "AUTORIZACIONES", bold); y += 12;
            g.DrawRectangle(XPens.Black, new XRect(Left, y, Width, 100));
            double lineY = y + 70;
            g.DrawLine(XPens.Black, Left + 100, lineY, Left + 250, lineY);
            g.DrawLine(XPens.Black, Left + 300, lineY, Left + 450, lineY);
            g.DrawString("SUPERVISOR", normal, XBrushes.Black, new XRect(Left + 100, lineY + 2, 150, 10), XStringFormats.TopCenter);
            g.DrawString("RECURSOS HUMANOS", normal, XBrushes.Black, new XRect(Left + 300, lineY + 2, 150, 10), XStringFormats.TopCenter);

            g.DrawString("J-HR-FRM-133 Rev.1 8-Jul-2026", bold, XBrushes.Black,
                new XRect(Left, 770, Width, 10), XStringFormats.TopRight);
            DrawFooter(g);

            using var stream = new MemoryStream();
            document.Save(stream, false);
            return stream.ToArray();
        }

        private static void DrawLogo(XGraphics g, double x, double y, double maxW, double maxH)
        {
            using var resource = typeof(MultipleFormatPdfBuilder).Assembly.GetManifestResourceStream("prodriven-logo.png");
            if (resource == null)
            {
                return;
            }

            using var image = XImage.FromStream(resource);
            var scale = Math.Min(maxW / image.PointWidth, maxH / image.PointHeight);
            g.DrawImage(image, x, y, image.PointWidth * scale, image.PointHeight * scale);
        }

        private static void DrawFooter(XGraphics g)
        {
            g.DrawString($"Aplicación desarrollada por el departamento de IT © {DateTime.Now.Year}",
                new XFont("Arial", 6.5), XBrushes.Gray, new XRect(Left, 780, Width, 10), XStringFormats.TopCenter);
        }

        private static byte[] BuildTimeAdjustment(MultipleFormatPdfData d)
        {
            using var document = new PdfDocument();
            document.Info.Title = "Notificacion y Aprobacion Ajuste de Checadas";
            var page = document.AddPage();
            page.Size = PdfSharp.PageSize.Letter;
            using var g = XGraphics.FromPdfPage(page);

            var normal = new XFont("Arial", 8);
            var bold = new XFont("Arial", 8, XFontStyleEx.Bold);
            var title = new XFont("Arial", 12, XFontStyleEx.Bold);
            var logo = new XFont("Arial", 20, XFontStyleEx.BoldItalic);

            double y = 30;
            DrawLogo(g, Left, y, 180, 36);
            Cell(g, Left + 200, y, 352, 30, "NOTIFICACION Y APROBACION AJUSTE DE CHECADAS", title, center: true);
            y += 40;
            Cell(g, Left + 340, y, 80, 16, "FECHA", bold, center: true, fill: XBrushes.LightGray);
            Cell(g, Left + 420, y, 132, 16, Fmt(d.Date), bold, center: true);
            y += 36;

            // Columnas: no. empleado, nombre, fecha, entrada, salida, motivo
            double[] w = { 62, 136, 76, 48, 48, 182 };
            string[] heads = { "NUMERO DE\nEMPLEADO", "NOMBRE", "FECHA", "HORA\nENTRADA", "HORA\nSALIDA", "MOTIVO DE AJUSTE" };
            double x = Left;
            var headFont = new XFont("Arial", 6.5, XFontStyleEx.Bold);
            for (int i = 0; i < w.Length; i++)
            {
                g.DrawRectangle(XPens.Black, XBrushes.LightGray, new XRect(x, y, w[i], 30));
                var lines = heads[i].Split('\n');
                double lineH = 8;
                double ty = y + (30 - lines.Length * lineH) / 2;
                foreach (var line in lines)
                {
                    g.DrawString(line, headFont, XBrushes.Black, new XRect(x, ty, w[i], lineH), XStringFormats.TopCenter);
                    ty += lineH;
                }
                x += w[i];
            }
            y += 30;

            var entry = d.EntryTime.HasValue && d.PunchType != "Exit" ? d.EntryTime.Value.ToString(@"hh\:mm") : string.Empty;
            var exit = d.ExitTime.HasValue && d.PunchType != "Entry" ? d.ExitTime.Value.ToString(@"hh\:mm") : string.Empty;
            string[] values = { d.EmployeeNumber, d.EmployeeName, Fmt(d.EffectiveDate), entry, exit, d.Justification ?? d.Comments ?? string.Empty };
            x = Left;
            for (int i = 0; i < w.Length; i++)
            {
                Cell(g, x, y, w[i], 24, values[i], normal, center: i != 5);
                x += w[i];
            }
            y += 24;
            for (int r = 0; r < 20; r++)
            {
                x = Left;
                for (int i = 0; i < w.Length; i++)
                {
                    Cell(g, x, y, w[i], 18, null, normal);
                    x += w[i];
                }
                y += 18;
            }

            y += 40;
            double lineY = y + 40;
            g.DrawLine(XPens.Black, Left + 40, lineY, Left + 240, lineY);
            g.DrawLine(XPens.Black, Left + 310, lineY, Left + 510, lineY);
            g.DrawString(d.SupervisorName ?? string.Empty, normal, XBrushes.Black, new XRect(Left + 40, lineY - 14, 200, 12), XStringFormats.TopCenter);
            g.DrawString("NOMBRE Y FIRMA DE GERENTE DEL AREA", normal, XBrushes.Black, new XRect(Left + 40, lineY + 2, 200, 10), XStringFormats.TopCenter);
            g.DrawString("RECURSOS HUMANOS", normal, XBrushes.Black, new XRect(Left + 310, lineY + 2, 200, 10), XStringFormats.TopCenter);

            DrawFooter(g);

            using var stream = new MemoryStream();
            document.Save(stream, false);
            return stream.ToArray();
        }

        private static string Fmt(DateTime? value) =>
            value.HasValue ? value.Value.ToString("dd MMMM yyyy", DateCulture) : string.Empty;

        private static void Bar(XGraphics g, double y, string text, XFont font) =>
            Cell(g, Left, y, Width, 12, text, font, center: true, fill: XBrushes.LightGray);

        private static void Cell(XGraphics g, double x, double y, double w, double h, string? text, XFont font,
            bool center = false, XBrush? fill = null)
        {
            var rect = new XRect(x, y, w, h);
            if (fill != null) g.DrawRectangle(XPens.Black, fill, rect);
            else g.DrawRectangle(XPens.Black, rect);
            if (!string.IsNullOrEmpty(text))
            {
                var inner = new XRect(x + 3, y, w - 6, h);
                var size = font.Size;
                while (size > 5 && g.MeasureString(text, font).Width > inner.Width)
                {
                    size -= 0.5;
                    font = new XFont(font.FontFamily.Name, size, font.Style);
                }
                g.DrawString(text, font, XBrushes.Black, inner,
                    center ? XStringFormats.Center : XStringFormats.CenterLeft);
            }
        }

        private static void LabeledCell(XGraphics g, double x, double y, double w, string label, string? value,
            XFont normal, XFont bold)
        {
            Cell(g, x, y, w, 14, label, normal, center: true);
            Cell(g, x, y + 14, w, 18, value, bold, center: true);
        }

        private static void CheckBox(XGraphics g, double x, double y, bool isChecked, string label, XFont font)
        {
            var box = new XRect(x, y, 9, 9);
            g.DrawRectangle(XPens.Black, box);
            if (isChecked)
            {
                g.DrawLine(XPens.Black, x + 1.5, y + 1.5, x + 7.5, y + 7.5);
                g.DrawLine(XPens.Black, x + 7.5, y + 1.5, x + 1.5, y + 7.5);
            }
            g.DrawString(label, font, XBrushes.Black, new XRect(x + 13, y - 1, 250, 10), XStringFormats.TopLeft);
        }

        private static void DaysTable(XGraphics g, double y, MultipleFormatPdfData? d, XFont normal, XFont bold)
        {
            var x = Left + 100;
            Cell(g, x, y, 50, 14, "NUM. DE DIAS", bold, center: true);
            Cell(g, x + 50, y, 100, 14, "FECHA DE INICIO", bold, center: true);
            Cell(g, x + 150, y, 100, 14, "FECHA DE T\u00C9RMINO", bold, center: true);
            Cell(g, x, y + 14, 50, 18, d?.Days?.ToString(), normal, center: true);
            Cell(g, x + 50, y + 14, 100, 18, Fmt(d?.StartDate), normal, center: true);
            Cell(g, x + 150, y + 14, 100, 18, Fmt(d?.EndDate), normal, center: true);
            Cell(g, x, y + 32, 250, 14, "SE PRESENTAR\u00C1 A TRABAJAR EL DIA:", bold, center: true);
            Cell(g, x, y + 46, 250, 18, Fmt(d?.ReturnDate), normal, center: true);
        }
    }
}

