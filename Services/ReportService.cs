using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using RegistroCassa.Data;
using RegistroCassa.DTOs;
using RegistroCassa.Models;

namespace RegistroCassa.Services;

public class ReportService {
  private readonly AppDbContext _db;

  public ReportService(AppDbContext db) => _db = db;

  public async Task<ReportDto> BuildReportAsync(int year, int month, string periodType) {
    var (startDate, endDate) = GetDateRange(year, month, periodType);

    var movimenti = await _db.Movimenti
        .Include(m => m.CreatedByUser)
        .Where(m => m.Date.Date >= startDate && m.Date.Date <= endDate)
        .OrderBy(m => m.Date).ThenBy(m => m.Id)
        .ToListAsync();

    var giornate = await _db.GiornateContabili
        .Where(g => g.Date >= startDate && g.Date <= endDate)
        .ToListAsync();

    var days = new List<ReportDayDto>();
    for (var d = startDate; d <= endDate; d = d.AddDays(1)) {
      var dayOnly = d;
      var dayMovimenti = movimenti.Where(m => m.Date.Date == d).ToList();
      var giornata = giornate.FirstOrDefault(g => g.Date == dayOnly);

      if (!dayMovimenti.Any() && giornata == null) continue;

      days.Add(new ReportDayDto(
          Date: dayOnly,
          CashAtStartOfDay: giornate.FirstOrDefault(g => g.Date == d.AddDays(-1))?.CashAtEndOfDay ?? 0,
          CashAtEndOfDay: giornata?.CashAtEndOfDay ?? 0,
          TotalEntrate: dayMovimenti.Where(m => m.Type == TipoMovimento.Entrata).Sum(m => m.Amount),
          TotalUscite: dayMovimenti.Where(m => m.Type == TipoMovimento.Uscita).Sum(m => m.Amount),
          Movimenti: dayMovimenti.Select(m => new ReportMovimentoDto(
              m.Id,
              m.Type,
              m.Type == TipoMovimento.Entrata ? "Entrata" : "Uscita",
              m.Amount,
              m.Description,
              m.InvoiceNumber)).ToList()
      ));
    }

    var label = periodType switch {
      "quindicinale-1" => $"1-15 {GetMonthName(month)} {year}",
      "quindicinale-2" => $"16-{endDate.Day} {GetMonthName(month)} {year}",
      _ => $"{GetMonthName(month)} {year}"
    };

    return new ReportDto(
        Year: year,
        Month: month,
        PeriodType: periodType,
        PeriodLabel: label,
        Days: days,
        GrandTotalEntrate: days.Sum(d => d.TotalEntrate),
        GrandTotalUscite: days.Sum(d => d.TotalUscite));
  }

  public byte[] ExportToExcel(ReportDto report) {
    using var workbook = new XLWorkbook();
    var ws = workbook.Worksheets.Add("Registro di Cassa");

    ws.Cell(1, 1).Value = $"Registro di Cassa — {report.PeriodLabel}";
    ws.Cell(1, 1).Style.Font.Bold = true;
    ws.Cell(1, 1).Style.Font.FontSize = 14;
    ws.Range(1, 1, 1, 6).Merge();

    int row = 3;

    foreach (var day in report.Days) {
      ws.Cell(row, 1).Value = day.Date.ToString("dd/MM/yyyy");
      ws.Cell(row, 1).Style.Font.Bold = true;
      ws.Cell(row, 2).Value = "Cassa inizio:";
      ws.Cell(row, 3).Value = day.CashAtStartOfDay;
      ws.Cell(row, 3).Style.NumberFormat.Format = "#,##0.00 €";
      ws.Cell(row, 4).Value = "Cassa fine:";
      ws.Cell(row, 5).Value = day.CashAtEndOfDay;
      ws.Cell(row, 5).Style.NumberFormat.Format = "#,##0.00 €";
      row++;

      if (day.Movimenti.Any()) {
        ws.Cell(row, 1).Value = "N°";
        ws.Cell(row, 2).Value = "Tipo";
        ws.Cell(row, 3).Value = "Importo";
        ws.Cell(row, 4).Value = "Descrizione";
        ws.Cell(row, 5).Value = "N° Fattura";
        var headerRange = ws.Range(row, 1, row, 5);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;
        row++;

        foreach (var m in day.Movimenti) {
          ws.Cell(row, 1).Value = m.Id;
          ws.Cell(row, 2).Value = m.TypeLabel;
          ws.Cell(row, 3).Value = m.Type == TipoMovimento.Entrata ? m.Amount : -m.Amount;
          ws.Cell(row, 3).Style.NumberFormat.Format = "#,##0.00 €";
          ws.Cell(row, 4).Value = m.Description;
          ws.Cell(row, 5).Value = m.InvoiceNumber ?? "";
          row++;
        }

        ws.Cell(row, 3).Value = "Tot. Entrate:";
        ws.Cell(row, 4).Value = day.TotalEntrate;
        ws.Cell(row, 4).Style.NumberFormat.Format = "#,##0.00 €";
        ws.Cell(row, 5).Value = "Tot. Uscite:";
        ws.Cell(row, 6).Value = day.TotalUscite;
        ws.Cell(row, 6).Style.NumberFormat.Format = "#,##0.00 €";
        ws.Range(row, 1, row, 6).Style.Font.Bold = true;
        row++;
      }

      row++;
    }

    row++;
    ws.Cell(row, 1).Value = "TOTALE PERIODO";
    ws.Cell(row, 2).Value = "Entrate:";
    ws.Cell(row, 3).Value = report.GrandTotalEntrate;
    ws.Cell(row, 3).Style.NumberFormat.Format = "#,##0.00 €";
    ws.Cell(row, 4).Value = "Uscite:";
    ws.Cell(row, 5).Value = report.GrandTotalUscite;
    ws.Cell(row, 5).Style.NumberFormat.Format = "#,##0.00 €";
    ws.Range(row, 1, row, 5).Style.Font.Bold = true;
    ws.Range(row, 1, row, 5).Style.Fill.BackgroundColor = XLColor.LightYellow;

    ws.Columns().AdjustToContents();

    using var stream = new MemoryStream();
    workbook.SaveAs(stream);
    return stream.ToArray();
  }

  public static (DateTime start, DateTime end) GetDateRange(int year, int month, string periodType) =>
      periodType switch {
        "quindicinale-1" => (new DateTime(year, month, 1), new DateTime(year, month, 15)),
        "quindicinale-2" => (new DateTime(year, month, 16), new DateTime(year, month, DateTime.DaysInMonth(year, month))),
        _ => (new DateTime(year, month, 1), new DateTime(year, month, DateTime.DaysInMonth(year, month)))
      };

  private static string GetMonthName(int month) =>
      new DateTime(2000, month, 1).ToString("MMMM", new System.Globalization.CultureInfo("it-IT"));

	public async Task<byte[]> ExportFinalReportAsync(int year, int month, string periodType, string sede) {
		var (startDate, endDate) = GetDateRange(year, month, periodType);

		// Get opening balance = CashAtEndOfDay of the day before the period
		var prevDate = startDate.AddDays(-1);
		var prevGiornata = await _db.GiornateContabili
			.FirstOrDefaultAsync(g => g.Date == prevDate && g.Sede == sede);
		var openingBalance = prevGiornata?.CashAtEndOfDay ?? 0;

		var movimenti = await _db.Movimenti
			.Where(m => m.Date.Date >= startDate && m.Date.Date <= endDate
							 && m.Sede == sede && !m.IsDeleted)
			.OrderBy(m => m.Date).ThenBy(m => m.Id)
			.ToListAsync();

		var giornate = await _db.GiornateContabili
			.Where(g => g.Date >= startDate && g.Date <= endDate && g.Sede == sede)
			.ToListAsync();

		var label = periodType switch {
			"quindicinale-1" => $"1-15 {GetMonthName(month)} {year}",
			"quindicinale-2" => $"16-{endDate.Day} {GetMonthName(month)} {year}",
			_ => $"{GetMonthName(month)} {year}"
		};

		using var workbook = new XLWorkbook();
		var ws = workbook.Worksheets.Add("Foglio Cassa");

		var fmt = "#,##0.00";
		var lightGray = XLColor.FromHtml("#F2F2F2");
		var lightBlue = XLColor.FromHtml("#DDEEFF");
		var lightYellow = XLColor.FromHtml("#FFFACD");

		// Col widths
		ws.Column(1).Width = 12;  // Data
		ws.Column(2).Width = 40;  // Descrizione
		ws.Column(3).Width = 12;  // Contanti
		ws.Column(4).Width = 12;  // POS
		ws.Column(5).Width = 12;  // Bonifico
		ws.Column(6).Width = 12;  // Uscite
		ws.Column(7).Width = 14;  // Saldo

		int row = 1;

		// Title
		ws.Cell(row, 1).Value = $"FOGLIO CASSA SEDE DI {sede.ToUpper()}";
		ws.Cell(row, 1).Style.Font.Bold = true;
		ws.Cell(row, 1).Style.Font.FontSize = 13;
		ws.Range(row, 1, row, 7).Merge();
		row++;

		ws.Cell(row, 1).Value = $"MESE: {label.ToUpper()}";
		ws.Cell(row, 1).Style.Font.Bold = true;
		ws.Range(row, 1, row, 7).Merge();
		row += 2;

		// Opening balance
		ws.Cell(row, 1).Value = "SALDO CASSA MESE PRECEDENTE";
		ws.Cell(row, 1).Style.Font.Bold = true;
		ws.Range(row, 1, row, 6).Merge();
		ws.Cell(row, 7).Value = openingBalance;
		ws.Cell(row, 7).Style.NumberFormat.Format = fmt;
		ws.Cell(row, 7).Style.Font.Bold = true;
		ws.Range(row, 1, row, 7).Style.Fill.BackgroundColor = lightYellow;
		row += 2;

		// Column headers
		ws.Cell(row, 1).Value = "DATA";
		ws.Cell(row, 2).Value = "DESCRIZIONE MOVIMENTO";
		ws.Cell(row, 3).Value = "Contanti";
		ws.Cell(row, 4).Value = "POS";
		ws.Cell(row, 5).Value = "Bonifico";
		ws.Cell(row, 6).Value = "Uscite";
		ws.Cell(row, 7).Value = "Saldo cassa contanti";
		var hdrRange = ws.Range(row, 1, row, 7);
		hdrRange.Style.Font.Bold = true;
		hdrRange.Style.Fill.BackgroundColor = lightBlue;
		hdrRange.Style.Border.BottomBorder = XLBorderStyleValues.Medium;
		row++;

		decimal runningBalance = openingBalance;

		for (var d = startDate; d <= endDate; d = d.AddDays(1)) {
			var dayMovimenti = movimenti.Where(m => m.Date.Date == d).ToList();
			var giornata = giornate.FirstOrDefault(g => g.Date == d);

			if (!dayMovimenti.Any() && giornata == null) continue;

			var dateStr = d.ToString("dd/MM/yyyy");
			var uscite = dayMovimenti.Where(m => m.Type == TipoMovimento.Uscita).ToList();
			var entrateTotale = dayMovimenti.Where(m => m.Type == TipoMovimento.Entrata).Sum(m => m.Amount);

			// Uscite rows
			foreach (var m in uscite) {
				runningBalance -= m.Amount;
				ws.Cell(row, 1).Value = dateStr;
				ws.Cell(row, 2).Value = m.Description;
				ws.Cell(row, 6).Value = m.Amount;
				ws.Cell(row, 6).Style.NumberFormat.Format = fmt;
				ws.Cell(row, 7).Value = runningBalance;
				ws.Cell(row, 7).Style.NumberFormat.Format = fmt;
				row++;
			}

			// Totale incasso row
			if (entrateTotale > 0) {
				runningBalance += entrateTotale;
				ws.Cell(row, 1).Value = dateStr;
				ws.Cell(row, 2).Value = "totale incasso fatture giornaliero";
				ws.Cell(row, 2).Style.Font.Italic = true;
				ws.Cell(row, 3).Value = entrateTotale; // all in Contanti for now
				ws.Cell(row, 3).Style.NumberFormat.Format = fmt;
				ws.Cell(row, 7).Value = runningBalance;
				ws.Cell(row, 7).Style.NumberFormat.Format = fmt;
				ws.Range(row, 1, row, 7).Style.Fill.BackgroundColor = lightGray;
				row++;
			}

			// Saldo cassa row
			var cashEnd = giornata?.CashAtEndOfDay ?? runningBalance;
			ws.Cell(row, 1).Value = dateStr;
			ws.Cell(row, 2).Value = "saldo cassa";
			ws.Cell(row, 2).Style.Font.Bold = true;
			ws.Cell(row, 7).Value = cashEnd;
			ws.Cell(row, 7).Style.NumberFormat.Format = fmt;
			ws.Cell(row, 7).Style.Font.Bold = true;
			ws.Range(row, 1, row, 7).Style.Fill.BackgroundColor = lightYellow;
			ws.Range(row, 1, row, 7).Style.Border.BottomBorder = XLBorderStyleValues.Thin;
			runningBalance = cashEnd;
			row++;
		}

		// Grand totals
		row++;
		var grandEntrate = movimenti.Where(m => m.Type == TipoMovimento.Entrata).Sum(m => m.Amount);
		var grandUscite = movimenti.Where(m => m.Type == TipoMovimento.Uscita).Sum(m => m.Amount);

		ws.Cell(row, 1).Value = "TOTALE PERIODO";
		ws.Cell(row, 1).Style.Font.Bold = true;
		ws.Range(row, 1, row, 2).Merge();
		ws.Cell(row, 3).Value = grandEntrate;
		ws.Cell(row, 3).Style.NumberFormat.Format = fmt;
		ws.Cell(row, 3).Style.Font.Bold = true;
		ws.Cell(row, 6).Value = grandUscite;
		ws.Cell(row, 6).Style.NumberFormat.Format = fmt;
		ws.Cell(row, 6).Style.Font.Bold = true;
		ws.Cell(row, 7).Value = runningBalance;
		ws.Cell(row, 7).Style.NumberFormat.Format = fmt;
		ws.Cell(row, 7).Style.Font.Bold = true;
		ws.Range(row, 1, row, 7).Style.Fill.BackgroundColor = lightBlue;

		using var stream = new MemoryStream();
		workbook.SaveAs(stream);
		return stream.ToArray();
	}
}