using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RegistroCassa.Data;
using RegistroCassa.Models;
using RegistroCassa.Services;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySQL(connectionString));

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequiredLength = 6;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.SignIn.RequireConfirmedAccount = false;
})
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/login.html";
    options.Events.OnRedirectToLogin = ctx =>
    {
        // Return 401 for API calls instead of redirecting
        if (ctx.Request.Path.StartsWithSegments("/api"))
        {
            ctx.Response.StatusCode = 401;
            return Task.CompletedTask;
        }
        ctx.Response.Redirect(ctx.RedirectUri);
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = ctx =>
    {
        if (ctx.Request.Path.StartsWithSegments("/api"))
        {
            ctx.Response.StatusCode = 403;
            return Task.CompletedTask;
        }
        ctx.Response.Redirect("/login.html");
        return Task.CompletedTask;
    };
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
});

builder.Services.AddScoped<ReportService>();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options => {
	options.IdleTimeout = TimeSpan.FromHours(8);
	options.Cookie.HttpOnly = true;
	options.Cookie.IsEssential = true;
});
builder.Services.AddControllers();

var app = builder.Build();

using (var scope = app.Services.CreateScope()) {
	var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
	var pendingMigrations = db.Database.GetPendingMigrations().ToList();
	if (pendingMigrations.Any()) {
		db.Database.Migrate();
	}
	else {
		db.Database.EnsureCreated();
	}
	await DbSeeder.SeedAsync(scope.ServiceProvider);
}

app.UsePathBase("/RegistroDiCassa");
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
