using Microsoft.AspNetCore.Identity;
using RegistroCassa.Models;

namespace RegistroCassa.Data;

public static class DbSeeder {
  public const string RoleAmministratore = "Amministratore";
  public const string RoleOperatore = "Operatore";

  public static async Task SeedAsync(IServiceProvider services) {
    var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
    var config = services.GetRequiredService<IConfiguration>();

    foreach (var role in new[] { RoleAmministratore, RoleOperatore }) {
      if (!await roleManager.RoleExistsAsync(role))
        await roleManager.CreateAsync(new IdentityRole(role));
    }

    var adminPassword = config["AdminSeed:Password"];
    var adminFullName = config["AdminSeed:FullName"] ?? "Amministratore";

    if (string.IsNullOrEmpty(adminFullName) || string.IsNullOrEmpty(adminPassword))
      throw new InvalidOperationException(
          "Admin seed credentials missing. Set AdminSeed:Email and AdminSeed:Password in appsettings.json.");

    if (await userManager.FindByEmailAsync(adminFullName) == null) {
      var admin = new ApplicationUser {
        UserName = adminFullName,
        FullName = adminFullName,
        IsActive = true,
        EmailConfirmed = true
      };
      var result = await userManager.CreateAsync(admin, adminPassword);
      if (result.Succeeded)
        await userManager.AddToRoleAsync(admin, RoleAmministratore);
    }
  }
}
