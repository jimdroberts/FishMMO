using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FishMMO_DB
{
	public class ServerDbContextFactory : IDesignTimeDbContextFactory<ServerDbContext>
	{
		private string configPath = "";
		private bool enableLogging = false;

		public ServerDbContextFactory()
		{
			this.configPath = Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory).FullName;
		}
		public ServerDbContextFactory(string configPath)
		{
			this.configPath = configPath;
		}

		public ServerDbContextFactory(string configPath, bool enableLogging)
		{
			this.configPath = configPath;
			this.enableLogging = enableLogging;
		}

		public ServerDbContext CreateDbContext()
		{
			return CreateDbContext(new string[] { });
		}

		/*public ServerDbContext CreateDbContext(string connectionString)
		{
			string basePath = string.IsNullOrWhiteSpace(this.configPath) ? AppDomain.CurrentDomain.BaseDirectory : this.configPath;

			IConfigurationRoot configuration = new ConfigurationBuilder()
				.SetBasePath(basePath)
				.AddJsonFile("appsettings.json")
				.Build();

			DbContextOptionsBuilder optionsBuilder = new DbContextOptionsBuilder<ServerDbContext>().UseNpgsql(configuration.GetConnectionString(connectionString))
				.UseSnakeCaseNamingConvention();

			if (enableLogging)
			{
				optionsBuilder
					.EnableSensitiveDataLogging(true);
			}

			return new ServerDbContext(optionsBuilder.Options);
		}*/

		public ServerDbContext CreateDbContext(string[] args)
		{
			Configuration configuration = GetOrCreateDatabaseConfiguration();
			string hostString = CreateDatabaseHostString(configuration);

			DbContextOptionsBuilder optionsBuilder = new DbContextOptionsBuilder<ServerDbContext>().UseNpgsql(hostString)
				.UseSnakeCaseNamingConvention();

			if (enableLogging)
			{
				optionsBuilder
					.EnableSensitiveDataLogging(true);
			}

			return new ServerDbContext(optionsBuilder.Options);
		}

		public Configuration GetOrCreateDatabaseConfiguration()
		{
			Configuration configuration = new Configuration(configPath);
			if (!configuration.Load("Database.cfg"))
			{
				// if we failed to load the file.. save a new one
				configuration.Set("DbName", "fish_mmo_postgresql");
				configuration.Set("DbUsername", "user");
				configuration.Set("DbPassword", "pass");
				configuration.Set("DbAddress", "127.0.0.1");
				configuration.Set("DbPort", "5432");
				configuration.Save();
			}
			return configuration;
		}

		public string CreateDatabaseHostString(Configuration configuration)
		{
			string hostString = "";
			if (configuration != null &&
				configuration.TryGetString("DbName", out string dbName) &&
				configuration.TryGetString("DbUsername", out string dbUsername) &&
				configuration.TryGetString("DbPassword", out string dbPassword) &&
				configuration.TryGetString("DbAddress", out string dbAddress) &&
				configuration.TryGetString("DbPort", out string dbPort))
			{
				hostString = "Host=" + dbAddress + ";" +
							 "Port=" + dbPort + ";" +
							 "Database=" + dbName + ";" +
							 "Username=" + dbUsername + ";" +
							 "Password=" + dbPassword + ";";
			}
			return hostString;
		}
	}
}