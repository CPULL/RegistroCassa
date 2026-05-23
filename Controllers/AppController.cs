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

		var result = await _signInManager.PasswordSignInAsync(
			user, request.Password, isPersistent: true, lockoutOnFailure: false);

		if (!result.Succeeded)
			return Unauthorized(new { message = "Nome o password non validi." });

		var roles = await _userManager.GetRolesAsync(user);
		var role = roles.FirstOrDefault() ?? "";
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

		var cashStart = prevGiornata?.CashAtEndOfDay ?? 0;

		return Ok(new GiornataDto(
			Date: targetDate,
			CashAtStartOfDay: cashStart,
			CashAtEndOfDay: giornata?.CashAtEndOfDay ?? 0,
			Notes: giornata?.Notes,
			Movimenti: movimenti.Select(MapMovimentoToDto).ToList(),
			TotalEntrate: movimenti.Where(m => !m.IsDeleted && m.Type == TipoMovimento.Entrata).Sum(m => m.Amount),
			TotalUscite: movimenti.Where(m => !m.IsDeleted && m.Type == TipoMovimento.Uscita).Sum(m => m.Amount)
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

		var days = new List<GiornataDto>();
		for (var d = startDate.Date; d <= endDate.Date; d = d.AddDays(1)) {
			var dayMovimenti = movimenti.Where(m => m.Date.Date == d).ToList();
			var giornata = giornate.FirstOrDefault(g => g.Date == d);
			var prevDate = d.AddDays(-1);
			var prevGiornata = giornate.FirstOrDefault(g => g.Date == prevDate)
				?? await _db.GiornateContabili.FirstOrDefaultAsync(g => g.Date == prevDate && g.Sede == sede);

			days.Add(new GiornataDto(
				Date: DateOnly.FromDateTime(d),
				CashAtStartOfDay: prevGiornata?.CashAtEndOfDay ?? 0,
				CashAtEndOfDay: giornata?.CashAtEndOfDay ?? 0,
				Notes: giornata?.Notes,
				Movimenti: dayMovimenti.Select(MapMovimentoToDto).ToList(),
				TotalEntrate: dayMovimenti.Where(m => !m.IsDeleted && m.Type == TipoMovimento.Entrata).Sum(m => m.Amount),
				TotalUscite: dayMovimenti.Where(m => !m.IsDeleted && m.Type == TipoMovimento.Uscita).Sum(m => m.Amount)
			));
		}

		return Ok(new PeriodDto(
			Days: days,
			GrandTotalEntrate: days.Sum(d => d.TotalEntrate),
			GrandTotalUscite: days.Sum(d => d.TotalUscite)
		));
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
}
