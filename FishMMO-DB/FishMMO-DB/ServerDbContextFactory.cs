using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

#if UNITY_STANDALONE
using UnityEngine;
#endif

namespace FishMMO_DB
{
	public class ServerDbContextFactory : IDesignTimeDbContextFactory<ServerDbContext>
	{
		private bool enableLogging = false;

		public ServerDbContextFactory()
		{
		}

		public ServerDbContextFactory(bool enableLogging)
		{
			this.enableLogging = enableLogging;
		}

		public ServerDbContext CreateDbContext()
		{
			return CreateDbContext(new string[] { });
		}

		public ServerDbContext CreateDbContext(string[] args)
		{
			Configuration configuration = GetOrCreateDatabaseConfiguration();
			string hostString = CreateDatabaseHostString(configuration);

			DbContextOptionsBuilder optionsBuilder = new DbContextOptionsBuilder<ServerDbContext>().UseNpgsql(hostString, b =>
			{
				b.MigrationsHistoryTable("__EFMigrationsHistory", "public");
				b.MigrationsAssembly(typeof(ServerDbContext).Name);
			})
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
			string path = "";
#if UNITY_EDITOR
			path = Directory.GetParent(Application.dataPath).FullName;
#elif UNITY_ANDROID
			path = Application.persistentDataPath;
#elif UNITY_IOS
			path = Application.persistentDataPath;
#elif UNITY_STANDALONE
			path = Application.dataPath;
#endif

			Configuration configuration = new Configuration(path);
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