using System.ComponentModel.DataAnnotations;
using RegistroCassa.Models;

namespace RegistroCassa.DTOs;

// --- Auth ---

public record LoginRequest(
    [Required] string User,
    [Required] string Password);

public record LoginResponse(
	string UserId,
	string FullName,
	string Role,
	string? Sede,
	string Theme);

// --- Movimenti ---

public record MovimentoRequest(
    [Required] DateTime Date,
    [Required] TipoMovimento Type,
    [Required][Range(0.01, double.MaxValue)] decimal Amount,
    [Required][MaxLength(200)] string Description,
    string? InvoiceNumber);

public record MovimentoEditRequest(
    [Required] DateTime Date,
    [Required] TipoMovimento Type,
    [Required][Range(0.01, double.MaxValue)] decimal Amount,
    [Required][MaxLength(200)] string Description,
    string? InvoiceNumber,
    [Required][MaxLength(500)] string Justification);

public record MovimentoDto(
    int Id,
    DateTime Date,
    TipoMovimento Type,
    string TypeLabel,
    decimal Amount,
    string Description,
    string? InvoiceNumber,
    string CreatedByFullName,
    DateTime CreatedAt,
		bool IsDeleted);

public record MovimentoModificaDto(
    int Id,
    DateTime ModifiedAt,
    string ModifiedByFullName,
    TipoMovimento OldType,
    decimal OldAmount,
    string OldDescription,
    string? OldInvoiceNumber,
    string Justification);

public record MovimentoDetailDto(
    MovimentoDto Movimento,
    List<MovimentoModificaDto> Modifiche);

// --- Giornata ---

public record GiornataRequest(
    [Required] DateOnly Date,
    decimal CashAtEndOfDay,
    string? Notes);

public record GiornataDto(
	DateOnly Date,
	decimal CashAtStartOfDay,
	decimal CashAtEndOfDay,
	string? Notes,
	List<MovimentoDto> Movimenti,
	decimal TotalEntrate,
	decimal TotalUscite,
	bool IsWeekend,
	bool IsClosure,
	string? ClosureNotes);

public record PeriodDto(
		List<GiornataDto> Days,
		decimal GrandTotalEntrate,
		decimal GrandTotalUscite);

// --- Report ---

public record ReportRequest(
    [Required] int Year,
    [Required][Range(1, 12)] int Month,
    [Required] string PeriodType); // "mensile" | "quindicinale-1" | "quindicinale-2"

public record ReportMovimentoDto(
    int Id,
    TipoMovimento Type,
    string TypeLabel,
    decimal Amount,
    string Description,
    string? InvoiceNumber);

public record ReportDayDto(
    DateTime Date,
    decimal CashAtStartOfDay,
    decimal CashAtEndOfDay,
    decimal TotalEntrate,
    decimal TotalUscite,
    List<ReportMovimentoDto> Movimenti);

public record ReportDto(
    int Year,
    int Month,
    string PeriodType,
    string PeriodLabel,
    List<ReportDayDto> Days,
    decimal GrandTotalEntrate,
    decimal GrandTotalUscite);

// --- Admin Users ---

public record UserDto(
    string Id,
    string FullName,
    string Role,
		bool IsActive,
	  string? Sede);

public record CreateUserRequest(
	[Required] string FullName,
	[Required][MinLength(6)] string Password,
	[Required] string Role,
	string? Sede);

public record ChangePasswordRequest(
    [Required] string UserId,
    [Required][MinLength(6)] string NewPassword);

public record SetSedeRequest([Required] string Sede);

public record SetThemeRequest([Required] string Theme);

public record ClosureDayDto(
	int Id,
	DateTime Date,
	string Sede,
	string? Notes);

public record ClosureDayRequest(
	[Required] DateTime Date,
	string? Notes);

public record ChangeOwnPasswordRequest(
	[Required] string CurrentPassword,
	[Required][MinLength(6)] string NewPassword);