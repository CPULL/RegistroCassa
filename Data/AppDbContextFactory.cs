using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace RegistroCassa.Data;

public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext> {
	public AppDbContext CreateDbContext(string[] args) {
		var config = new ConfigurationBuilder()
			.SetBasePath(Directory.GetCurrentDirectory())
			.AddJsonFile("appsettings.json")
			.Build();

		var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
		optionsBuilder.UseMySQL(config.GetConnectionString("DefaultConnection")!);
		return new AppDbContext(optionsBuilder.Options);
	}
}