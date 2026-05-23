using Microsoft.AspNetCore.Identity;

namespace RegistroCassa.Models;

public static class Sedi {
	public const string PontiRossi = "PontiRossi";
	public const string Poggio = "Poggio";
	public const string Porcellane = "Porcellane";
	public static readonly string[] All = [PontiRossi, Poggio, Porcellane];
}

public class ApplicationUser : IdentityUser {
	public string FullName { get; set; } = string.Empty;
	public bool IsActive { get; set; } = true;
	public string? Sede { get; set; }
	public string Theme { get; set; } = "light";
}