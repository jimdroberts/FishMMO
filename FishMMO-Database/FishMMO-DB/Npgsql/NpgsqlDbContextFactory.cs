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
		private DbContextOptionsBuilder optionsBuilder = null;

		public NpgsqlDbContextFactory()
		{
			this.configPath = Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory).FullName;

			if (this.optionsBuilder == null)
			{
				this.optionsBuilder = LoadDbContextOptionsBuilder();
			}
		}
		public NpgsqlDbContextFactory(string configPath)
		{
			this.configPath = configPath;

			if (this.optionsBuilder == null)
			{
				this.optionsBuilder = LoadDbContextOptionsBuilder();
			}
		}

		public NpgsqlDbContextFactory(string configPath, bool enableLogging)
		{
			this.configPath = configPath;
			this.enableLogging = enableLogging;

			if (this.optionsBuilder == null)
			{
				this.optionsBuilder = LoadDbContextOptionsBuilder();
			}
		}

		internal DbContextOptionsBuilder LoadDbContextOptionsBuilder()
		{
			string basePath = string.IsNullOrWhiteSpace(this.configPath) ? AppDomain.CurrentDomain.BaseDirectory : this.configPath;

			IConfiguration configuration = new ConfigurationBuilder()
				.SetBasePath(basePath)
				.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
				.Build();

			string? database = configuration.GetSection("Npgsql")["Database"] ?? "fish_mmo_postgresql";
			string? userID = configuration.GetSection("Npgsql")["Username"] ?? "user";
			string? password = configuration.GetSection("Npgsql")["Password"] ?? "pass";
			string? host = configuration.GetSection("Npgsql")["Host"] ?? "127.0.0.1";
			string? port = configuration.GetSection("Npgsql")["Port"] ?? "5432";

			string connectionString = $"Host={host};Port={port};Database={database};Username={userID};Password={password}";

			DbContextOptionsBuilder dbContextOptionsBuilder = new DbContextOptionsBuilder<NpgsqlDbContext>()
				.UseNpgsql(connectionString)
				.UseSnakeCaseNamingConvention();

			return dbContextOptionsBuilder;
		}

		public NpgsqlDbContext CreateDbContext()
		{
			if (this.optionsBuilder == null)
			{
				this.optionsBuilder = LoadDbContextOptionsBuilder();
			}

			if (this.enableLogging)
			{
				this.optionsBuilder
					.EnableSensitiveDataLogging(true);
			}

			return new NpgsqlDbContext(this.optionsBuilder.Options);
		}

		public NpgsqlDbContext CreateDbContext(string[] args)
		{
			if (this.optionsBuilder == null)
			{
				this.optionsBuilder = LoadDbContextOptionsBuilder();
			}

			if (this.enableLogging)
			{
				this.optionsBuilder
					.EnableSensitiveDataLogging(true);
			}

			return new NpgsqlDbContext(this.optionsBuilder.Options);
		}
	}
}