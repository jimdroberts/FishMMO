using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace FishMMO.Database.Npgsql
{
	public class NpgsqlDbContextFactory : IDesignTimeDbContextFactory<NpgsqlDbContext>
	{
		private string configPath = "";
		private bool enableLogging = false;

		public NpgsqlDbContextFactory()
		{
			this.configPath = Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory).FullName;
		}
		public NpgsqlDbContextFactory(string configPath)
		{
			this.configPath = configPath;
		}

		public NpgsqlDbContextFactory(string configPath, bool enableLogging)
		{
			this.configPath = configPath;
			this.enableLogging = enableLogging;
		}

		public NpgsqlDbContext CreateDbContext()
		{
			return CreateDbContext(new string[] { });
		}

		public NpgsqlDbContext CreateDbContext(string[] args)
		{
			string basePath = string.IsNullOrWhiteSpace(this.configPath) ? AppDomain.CurrentDomain.BaseDirectory : this.configPath;

			IConfiguration configuration = new ConfigurationBuilder()
				.SetBasePath(basePath)
				.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
				.Build();

			DbContextOptionsBuilder optionsBuilder = new DbContextOptionsBuilder<NpgsqlDbContext>().UseNpgsql(configuration.GetConnectionString("Npgsql"))
				.UseSnakeCaseNamingConvention();

			if (enableLogging)
			{
				optionsBuilder
					.EnableSensitiveDataLogging(true);
			}

			return new NpgsqlDbContext(optionsBuilder.Options);
		}
	}
}