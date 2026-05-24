using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RegistroCassa.Data;
using RegistroCassa.DTOs;
using RegistroCassa.Models;
using RegistroCassa.Services;

namespace RegistroCassa.Controllers;

[ApiController]
[Authorize]
public class AppController : ControllerBase {
	private readonly AppDbContext _db;
	private readonly UserManager<ApplicationUser> _userManager;
	private readonly SignInManager<ApplicationUser> _signInManager;
	private readonly ReportService _reportService;
	private const string SessionKeySede = "AdminSede";

	public AppController(
			AppDbContext db,
			UserManager<ApplicationUser> userManager,
			SignInManager<ApplicationUser> signInManager,
			ReportService reportService) {
		_db = db;
		_userManager = userManager;
		_signInManager = signInManager;
		_reportService = reportService;
	}

	// -------------------------------------------------------------------------
	// ACCOUNT
	// -------------------------------------------------------------------------

	[HttpPost("api/account/login")]
	[AllowAnonymous]
	public async Task<IActionResult> Login([FromBody] LoginRequest request) {
		var user = _userManager.Users.FirstOrDefault(u => u.FullName == request.User);
		if (user == null || !user.IsActive)
			return Unauthorized(new { message = "Credenziali non valide o utente disabilitato." });

		var roles = await _userManager.GetRolesAsync(user);
		var role = roles.FirstOrDefault() ?? "";
		var isPersistent = role == DbSeeder.RoleAmministratore;

		var result = await _signInManager.PasswordSignInAsync(
			user, request.Password, isPersistent: isPersistent, lockoutOnFailure: false);

		if (!result.Succeeded)
			return Unauthorized(new { message = "Nome o password non validi." });

		var sede = role == DbSeeder.RoleAmministratore ? null : user.Sede;
		return Ok(new LoginResponse(user.Id, user.FullName, role, sede, user.Theme));
	}

	[HttpPost("api/account/logout")]
	public async Task<IActionResult> Logout() {
		await _signInManager.SignOutAsync();
		return Ok();
	}

	[HttpGet("api/account/me")]
	public async Task<IActionResult> Me() {
		var user = await _userManager.GetUserAsync(User);
		if (user == null) return Unauthorized();
		var roles = await _userManager.GetRolesAsync(user);
		var role = roles.FirstOrDefault() ?? "";
		var sede = role == DbSeeder.RoleAmministratore
			? HttpContext.Session.GetString(SessionKeySede)
			: user.Sede;
		return Ok(new LoginResponse(user.Id, user.FullName, role, sede, user.Theme));
	}

	[HttpPost("api/account/sede")]
	[Authorize(Roles = "Amministratore")]
	public IActionResult SetSede([FromBody] SetSedeRequest request) {
		if (!Sedi.All.Contains(request.Sede))
			return BadRequest(new { message = "Sede non valida." });
		HttpContext.Session.SetString(SessionKeySede, request.Sede);
		return Ok();
	}

	[HttpPost("api/account/theme")]
	public async Task<IActionResult> SetTheme([FromBody] SetThemeRequest request) {
		if (request.Theme != "light" && request.Theme != "dark")
			return BadRequest(new { message = "Tema non valido." });
		var user = await _userManager.GetUserAsync(User);
		if (user == null) return Unauthorized();
		user.Theme = request.Theme;
		await _userManager.UpdateAsync(user);
		return Ok();
	}

	[HttpPost("api/account/change-password")]
	public async Task<IActionResult> ChangeOwnPassword([FromBody] ChangeOwnPasswordRequest request) {
		var user = await _userManager.GetUserAsync(User);
		if (user == null) return Unauthorized();

		var result = await _userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
		if (!result.Succeeded)
			return BadRequest(new { errors = result.Errors.Select(e => e.Description) });

		return Ok();
	}

	// -------------------------------------------------------------------------
	// GIORNATA
	// -------------------------------------------------------------------------

	[HttpGet("api/giornata")]
	public async Task<IActionResult> GetGiornata([FromQuery] DateOnly? date) {
		var sede = GetActiveSede();
		if (sede == null) return BadRequest(new { message = "Selezionare una sede." });

		var targetDate = date ?? DateOnly.FromDateTime(DateTime.Today);
		var targetDateTime = targetDate.ToDateTime(TimeOnly.MinValue);
		var prevDateTime = targetDateTime.AddDays(-1);

		var giornata = await _db.GiornateContabili
			.FirstOrDefaultAsync(g => g.Date == targetDateTime && g.Sede == sede);

		var prevGiornata = await _db.GiornateContabili
			.FirstOrDefaultAsync(g => g.Date == prevDateTime && g.Sede == sede);

		var movimenti = await _db.Movimenti
			.Include(m => m.CreatedByUser)
			.Where(m => m.Date.Date == targetDateTime.Date && m.Sede == sede)
			.OrderBy(m => m.Id)
			.ToListAsync();

		decimal cashStart;
		if (prevGiornata != null && prevGiornata.CashAtEndOfDay > 0) {
			cashStart = prevGiornata.CashAtEndOfDay;
		}
		else {
			var prevIsWeekend = prevDateTime.DayOfWeek == DayOfWeek.Saturday ||
													prevDateTime.DayOfWeek == DayOfWeek.Sunday;
			var prevIsClosure = await _db.ClosureDays
				.AnyAsync(c => c.Date.Date == prevDateTime.Date && c.Sede == sede);
			cashStart = (prevIsWeekend || prevIsClosure)
				? await GetEffectiveCashEnd(prevDateTime, sede)
				: 0;
		}

		var isWeekend = targetDateTime.DayOfWeek == DayOfWeek.Saturday ||
										targetDateTime.DayOfWeek == DayOfWeek.Sunday;

		var closure = await _db.ClosureDays
			.FirstOrDefaultAsync(c => c.Date.Date == targetDateTime.Date && c.Sede == sede);

		return Ok(new GiornataDto(
			Date: targetDate,
			CashAtStartOfDay: cashStart,
			CashAtEndOfDay: giornata?.CashAtEndOfDay ?? 0,
			Notes: giornata?.Notes,
			Movimenti: [.. movimenti.Select(MapMovimentoToDto)],
			TotalEntrate: movimenti.Where(m => !m.IsDeleted && m.Type == TipoMovimento.Entrata).Sum(m => m.Amount),
			TotalUscite: movimenti.Where(m => !m.IsDeleted && m.Type == TipoMovimento.Uscita).Sum(m => m.Amount),
			IsWeekend: isWeekend,
			IsClosure: closure != null,
			ClosureNotes: closure?.Notes
		));
	}

	[HttpPost("api/giornata")]
	public async Task<IActionResult> SaveGiornata([FromBody] GiornataRequest request) {
		var sede = GetActiveSede();
		if (sede == null) return BadRequest(new { message = "Selezionare una sede." });

		var userId = _userManager.GetUserId(User)!;
		var targetDateTime = request.Date.ToDateTime(TimeOnly.MinValue);

		var giornata = await _db.GiornateContabili
			.FirstOrDefaultAsync(g => g.Date == targetDateTime && g.Sede == sede);

		if (giornata == null) {
			giornata = new GiornataContabile { Date = targetDateTime, Sede = sede };
			_db.GiornateContabili.Add(giornata);
		}

		giornata.CashAtEndOfDay = request.CashAtEndOfDay;
		giornata.Notes = request.Notes;
		giornata.LastModifiedByUserId = userId;
		giornata.LastModifiedAt = DateTime.UtcNow;

		await _db.SaveChangesAsync();
		return Ok();
	}

	[HttpGet("api/periodo")]
	public async Task<IActionResult> GetPeriodo([FromQuery] string start, [FromQuery] string end) {
		var sede = GetActiveSede();
		if (sede == null) return BadRequest(new { message = "Selezionare una sede." });

		if (!DateTime.TryParse(start, out var startDate) || !DateTime.TryParse(end, out var endDate))
			return BadRequest(new { message = "Date non valide." });

		var movimenti = await _db.Movimenti
			.Include(m => m.CreatedByUser)
			.Where(m => m.Date.Date >= startDate.Date && m.Date.Date <= endDate.Date && m.Sede == sede)
			.OrderBy(m => m.Date).ThenBy(m => m.Id)
			.ToListAsync();

		var giornate = await _db.GiornateContabili
			.Where(g => g.Date >= startDate && g.Date <= endDate.AddDays(1) && g.Sede == sede)
			.ToListAsync();

		var closureDays = await _db.ClosureDays
			.Where(c => c.Date >= startDate && c.Date <= endDate && c.Sede == sede)
			.ToListAsync();

		var days = new List<GiornataDto>();
		for (var d = startDate.Date; d <= endDate.Date; d = d.AddDays(1)) {
			var dayMovimenti = movimenti.Where(m => m.Date.Date == d).ToList();
			var giornata = giornate.FirstOrDefault(g => g.Date == d);
			var prevDate = d.AddDays(-1);
			var prevGiornata = giornate.FirstOrDefault(g => g.Date == prevDate)
				?? await _db.GiornateContabili.FirstOrDefaultAsync(g => g.Date == prevDate && g.Sede == sede);

			decimal cashStart;
			if (prevGiornata != null && prevGiornata.CashAtEndOfDay > 0) {
				cashStart = prevGiornata.CashAtEndOfDay;
			}
			else {
				var prevIsWeekend = prevDate.DayOfWeek == DayOfWeek.Saturday ||
														prevDate.DayOfWeek == DayOfWeek.Sunday;
				var prevIsClosure = await _db.ClosureDays
					.AnyAsync(c => c.Date.Date == prevDate.Date && c.Sede == sede);
				cashStart = (prevIsWeekend || prevIsClosure)
					? await GetEffectiveCashEnd(prevDate, sede)
					: 0;
			}

			var isWeekendDay = d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday;
			var closure = closureDays.FirstOrDefault(c => c.Date.Date == d);

			days.Add(new GiornataDto(
				Date: DateOnly.FromDateTime(d),
				CashAtStartOfDay: cashStart,
				CashAtEndOfDay: giornata?.CashAtEndOfDay ?? 0,
				Notes: giornata?.Notes,
				Movimenti: dayMovimenti.Select(MapMovimentoToDto).ToList(),
				TotalEntrate: dayMovimenti.Where(m => !m.IsDeleted && m.Type == TipoMovimento.Entrata).Sum(m => m.Amount),
				TotalUscite: dayMovimenti.Where(m => !m.IsDeleted && m.Type == TipoMovimento.Uscita).Sum(m => m.Amount),
				IsWeekend: isWeekendDay,
				IsClosure: closure != null,
				ClosureNotes: closure?.Notes
			));
		}

		return Ok(new PeriodDto(
			Days: days,
			GrandTotalEntrate: days.Sum(d => d.TotalEntrate),
			GrandTotalUscite: days.Sum(d => d.TotalUscite)
		));
	}

	// GET /api/missing-cash
	[HttpGet("api/missing-cash")]
	public async Task<IActionResult> GetMissingCash() {
		var sede = GetActiveSede();
		if (sede == null) return Ok(new List<string>());

		// Find the first movimento for this sede
		var firstMov = await _db.Movimenti
			.Where(m => m.Sede == sede)
			.OrderBy(m => m.Date)
			.FirstOrDefaultAsync();

		if (firstMov == null) return Ok(new List<string>());

		// Only check current and previous month, but not before first movimento
		var twoMonthsAgo = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-1);
		var startDate = firstMov.Date.Date > twoMonthsAgo ? firstMov.Date.Date : twoMonthsAgo;
		var endDate = DateTime.Today.AddDays(-1); // exclude today

		var giornate = await _db.GiornateContabili
			.Where(g => g.Sede == sede && g.Date >= startDate && g.Date <= endDate)
			.ToListAsync();

		var closures = await _db.ClosureDays
			.Where(c => c.Sede == sede && c.Date >= startDate && c.Date <= endDate)
			.Select(c => c.Date.Date)
			.ToListAsync();

		var missing = new List<string>();
		for (var d = startDate; d <= endDate; d = d.AddDays(1)) {
			var isWeekend = d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday;
			if (isWeekend) continue;
			if (closures.Contains(d)) continue;
			var giornata = giornate.FirstOrDefault(g => g.Date.Date == d);
			if (giornata == null || giornata.CashAtEndOfDay == 0)
				missing.Add(d.ToString("yyyy-MM-dd"));
		}

		return Ok(missing);
	}

	// -------------------------------------------------------------------------
	// MOVIMENTI
	// -------------------------------------------------------------------------

	[HttpPost("api/movimenti")]
	public async Task<IActionResult> CreateMovimento([FromBody] MovimentoRequest request) {
		if (!ModelState.IsValid) return BadRequest(ModelState);

		var sede = GetActiveSede();
		if (sede == null) return BadRequest(new { message = "Selezionare una sede." });

		if (request.Type == TipoMovimento.Entrata && string.IsNullOrWhiteSpace(request.InvoiceNumber))
			return BadRequest(new { message = "Il numero fattura è obbligatorio per le entrate." });

		var userId = _userManager.GetUserId(User)!;
		var movimento = new Movimento {
			Date = request.Date,
			Type = request.Type,
			Amount = request.Amount,
			Description = request.Description,
			InvoiceNumber = request.Type == TipoMovimento.Entrata ? request.InvoiceNumber : null,
			Sede = sede,
			CreatedByUserId = userId,
			CreatedAt = DateTime.UtcNow
		};

		_db.Movimenti.Add(movimento);
		await _db.SaveChangesAsync();
		return Ok(new { id = movimento.Id });
	}

	[HttpPut("api/movimenti/{id:int}")]
	public async Task<IActionResult> EditMovimento(int id, [FromBody] MovimentoEditRequest request) {
		if (!ModelState.IsValid) return BadRequest(ModelState);

		if (request.Type == TipoMovimento.Entrata && string.IsNullOrWhiteSpace(request.InvoiceNumber))
			return BadRequest(new { message = "Il numero fattura è obbligatorio per le entrate." });

		var movimento = await _db.Movimenti.FindAsync(id);
		if (movimento == null) return NotFound();

		var userId = _userManager.GetUserId(User)!;

		_db.MovimentoModifiche.Add(new MovimentoModifica {
			MovimentoId = movimento.Id,
			ModifiedAt = DateTime.UtcNow,
			ModifiedByUserId = userId,
			OldAmount = movimento.Amount,
			OldDescription = movimento.Description,
			OldInvoiceNumber = movimento.InvoiceNumber,
			OldType = movimento.Type,
			Justification = request.Justification
		});

		movimento.Date = request.Date;
		movimento.Type = request.Type;
		movimento.Amount = request.Amount;
		movimento.Description = request.Description;
		movimento.InvoiceNumber = request.Type == TipoMovimento.Entrata ? request.InvoiceNumber : null;

		await _db.SaveChangesAsync();
		return Ok();
	}

	[HttpGet("api/movimenti/{id:int}")]
	public async Task<IActionResult> GetMovimento(int id) {
		var movimento = await _db.Movimenti
				.Include(m => m.CreatedByUser)
				.FirstOrDefaultAsync(m => m.Id == id);

		if (movimento == null) return NotFound();

		var modifiche = await _db.MovimentoModifiche
				.Include(m => m.ModifiedByUser)
				.Where(m => m.MovimentoId == id)
				.OrderByDescending(m => m.ModifiedAt)
				.ToListAsync();

		return Ok(new MovimentoDetailDto(
				Movimento: MapMovimentoToDto(movimento),
				Modifiche: modifiche.Select(m => new MovimentoModificaDto(
						m.Id, m.ModifiedAt,
						m.ModifiedByUser?.FullName ?? "",
						m.OldType, m.OldAmount, m.OldDescription, m.OldInvoiceNumber,
						m.Justification)).ToList()
		));
	}

	[HttpDelete("api/movimenti/{id:int}")]
	[Authorize(Roles = "Amministratore")]
	public async Task<IActionResult> DeleteMovimento(int id) {
		var movimento = await _db.Movimenti.FindAsync(id);
		if (movimento == null) return NotFound();

		movimento.IsDeleted = true;
		movimento.DeletedAt = DateTime.UtcNow;
		movimento.DeletedByUserId = _userManager.GetUserId(User);

		await _db.SaveChangesAsync();
		return Ok();
	}

	// -------------------------------------------------------------------------
	// REPORT  (Amministratore only)
	// -------------------------------------------------------------------------

	[HttpPost("api/report/view")]
	[Authorize(Roles = "Amministratore")]
	public async Task<IActionResult> ReportView([FromBody] ReportRequest request) {
		if (!ModelState.IsValid) return BadRequest(ModelState);
		var report = await _reportService.BuildReportAsync(request.Year, request.Month, request.PeriodType);
		return Ok(report);
	}

	[HttpPost("api/report/export")]
	[Authorize(Roles = "Amministratore")]
	public async Task<IActionResult> ReportExport([FromBody] ReportRequest request) {
		if (!ModelState.IsValid) return BadRequest(ModelState);
		var report = await _reportService.BuildReportAsync(request.Year, request.Month, request.PeriodType);
		var bytes = _reportService.ExportToExcel(report);
		var filename = $"registro_cassa_{report.PeriodLabel.Replace(" ", "_").Replace("-", "_")}.xlsx";
		return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", filename);
	}

	// -------------------------------------------------------------------------
	// ADMIN — Users  (Amministratore only)
	// -------------------------------------------------------------------------

	[HttpGet("api/admin/users")]
	[Authorize(Roles = "Amministratore")]
	public async Task<IActionResult> GetUsers() {
		var users = _userManager.Users.ToList();
		var result = new List<UserDto>();
		foreach (var user in users) {
			var roles = await _userManager.GetRolesAsync(user);
			result.Add(new UserDto(user.Id, user.FullName, roles.FirstOrDefault() ?? "", user.IsActive, user.Sede));
		}
		return Ok(result);
	}

	[HttpPost("api/admin/users")]
	[Authorize(Roles = "Amministratore")]
	public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request) {
		if (!ModelState.IsValid) return BadRequest(ModelState);

		if (request.Role == DbSeeder.RoleOperatore && string.IsNullOrEmpty(request.Sede))
			return BadRequest(new { message = "La sede è obbligatoria per gli operatori." });

		var username = request.FullName.Replace(" ", ".").ToLower() + "_" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		var user = new ApplicationUser {
			UserName = username,
			Email = username + "@interno.local",
			FullName = request.FullName,
			IsActive = true,
			EmailConfirmed = true,
			Sede = request.Role == DbSeeder.RoleOperatore ? request.Sede : null
		};

		var result = await _userManager.CreateAsync(user, request.Password);
		if (!result.Succeeded)
			return BadRequest(new { errors = result.Errors.Select(e => e.Description) });

		await _userManager.AddToRoleAsync(user, request.Role);
		return Ok(new { id = user.Id });
	}

	[HttpPost("api/admin/users/change-password")]
	[Authorize(Roles = "Amministratore")]
	public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request) {
		if (!ModelState.IsValid) return BadRequest(ModelState);

		var user = await _userManager.FindByIdAsync(request.UserId);
		if (user == null) return NotFound();

		var token = await _userManager.GeneratePasswordResetTokenAsync(user);
		var result = await _userManager.ResetPasswordAsync(user, token, request.NewPassword);

		if (!result.Succeeded)
			return BadRequest(new { errors = result.Errors.Select(e => e.Description) });

		return Ok();
	}

	[HttpPost("api/admin/users/{id}/toggle-active")]
	[Authorize(Roles = "Amministratore")]
	public async Task<IActionResult> ToggleActive(string id) {
		var currentUserId = _userManager.GetUserId(User);
		if (id == currentUserId)
			return BadRequest(new { message = "Non puoi disabilitare il tuo account." });

		var user = await _userManager.FindByIdAsync(id);
		if (user == null) return NotFound();

		user.IsActive = !user.IsActive;
		await _userManager.UpdateAsync(user);
		return Ok(new { isActive = user.IsActive });
	}

	// -------------------------------------------------------------------------
	// HELPERS
	// -------------------------------------------------------------------------

	private string? GetActiveSede() {
		var user = _userManager.GetUserAsync(User).Result;
		if (user == null) return null;
		var roles = _userManager.GetRolesAsync(user).Result;
		if (roles.Contains(DbSeeder.RoleAmministratore))
			return HttpContext.Session.GetString(SessionKeySede);
		return user.Sede;
	}

	private static MovimentoDto MapMovimentoToDto(Movimento m) => new(
		m.Id, m.Date, m.Type,
		m.Type == TipoMovimento.Entrata ? "Entrata" : "Uscita",
		m.Amount, m.Description, m.InvoiceNumber,
		m.CreatedByUser?.FullName ?? "", m.CreatedAt, m.IsDeleted);


	[HttpGet("api/chiusure")]
	public async Task<IActionResult> GetChiusure([FromQuery] int year) {
		var sede = GetActiveSede();
		if (sede == null) return BadRequest(new { message = "Selezionare una sede." });

		var start = new DateTime(year, 1, 1);
		var end = new DateTime(year, 12, 31);

		var days = await _db.ClosureDays
			.Where(c => c.Date >= start && c.Date <= end && c.Sede == sede)
			.OrderBy(c => c.Date)
			.ToListAsync();

		return Ok(days.Select(c => new ClosureDayDto(c.Id, c.Date, c.Sede, c.Notes)));
	}

	// POST /api/chiusure
	[HttpPost("api/chiusure")]
	public async Task<IActionResult> AddChiusura([FromBody] ClosureDayRequest request) {
		var sede = GetActiveSede();
		if (sede == null) return BadRequest(new { message = "Selezionare una sede." });

		var existing = await _db.ClosureDays
			.FirstOrDefaultAsync(c => c.Date.Date == request.Date.Date && c.Sede == sede);
		if (existing != null)
			return BadRequest(new { message = "Giorno già marcato come chiusura." });

		var userId = _userManager.GetUserId(User)!;
		_db.ClosureDays.Add(new ClosureDay {
			Date = request.Date.Date,
			Sede = sede,
			Notes = request.Notes,
			CreatedByUserId = userId,
			CreatedAt = DateTime.UtcNow
		});

		await _db.SaveChangesAsync();
		return Ok();
	}

	// DELETE /api/chiusure/{id}
	[HttpDelete("api/chiusure/{id:int}")]
	public async Task<IActionResult> DeleteChiusura(int id) {
		var sede = GetActiveSede();
		var day = await _db.ClosureDays.FindAsync(id);
		if (day == null) return NotFound();
		if (day.Sede != sede) return Forbid();

		_db.ClosureDays.Remove(day);
		await _db.SaveChangesAsync();
		return Ok();
	}

	// PUT /api/chiusure/{id}
	[HttpPut("api/chiusure/{id:int}")]
	public async Task<IActionResult> UpdateChiusura(int id, [FromBody] ClosureDayRequest request) {
		var sede = GetActiveSede();
		var day = await _db.ClosureDays.FindAsync(id);
		if (day == null) return NotFound();
		if (day.Sede != sede) return Forbid();

		// Check no duplicate on the new date
		var existing = await _db.ClosureDays
			.FirstOrDefaultAsync(c => c.Date.Date == request.Date.Date && c.Sede == sede && c.Id != id);
		if (existing != null)
			return BadRequest(new { message = "Esiste già una chiusura per questa data." });

		day.Date = request.Date.Date;
		day.Notes = request.Notes;
		await _db.SaveChangesAsync();
		return Ok();
	}

	private async Task<decimal> GetEffectiveCashEnd(DateTime date, string sede) {
		// Walk back up to 14 days to find the last non-zero cash end
		for (int i = 1; i <= 14; i++) {
			var prevDate = date.AddDays(-i);
			var prevGiornata = await _db.GiornateContabili
				.FirstOrDefaultAsync(g => g.Date == prevDate && g.Sede == sede);
			if (prevGiornata != null && prevGiornata.CashAtEndOfDay > 0)
				return prevGiornata.CashAtEndOfDay;

			// Stop if we hit a day that is not weekend and not a closure day
			var isWeekend = prevDate.DayOfWeek == DayOfWeek.Saturday ||
											prevDate.DayOfWeek == DayOfWeek.Sunday;
			var isClosure = await _db.ClosureDays
				.AnyAsync(c => c.Date.Date == prevDate.Date && c.Sede == sede);
			if (!isWeekend && !isClosure) break;
		}
		return 0;
	}

	[HttpPost("api/report/final")]
	[Authorize(Roles = "Amministratore")]
	public async Task<IActionResult> ReportFinal([FromBody] ReportRequest request) {
		if (!ModelState.IsValid) return BadRequest(ModelState);
		var sede = GetActiveSede();
		if (sede == null) return BadRequest(new { message = "Selezionare una sede." });

		var bytes = await _reportService.ExportFinalReportAsync(
			request.Year, request.Month, request.PeriodType, sede);

		var filename = $"foglio_cassa_{sede}_{request.Year}_{request.Month}_{request.PeriodType}.xlsx";
		return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", filename);
	}
}