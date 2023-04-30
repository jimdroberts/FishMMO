using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Server
{
    public class ServerDbContextFactory : IDesignTimeDbContextFactory<ServerDbContext>
    {
		private bool enableLogging = false;

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
				b.MigrationsAssembly(typeof(ServerDbContext).Assembly.GetName().Name);
			})
				.UseSnakeCaseNamingConvention();

			if (enableLogging)
			{
				optionsBuilder
					.EnableSensitiveDataLogging(true)
					.LogTo(UnityEngine.Debug.Log);
			}

			return new ServerDbContext(optionsBuilder.Options);
		}

		public Configuration GetOrCreateDatabaseConfiguration()
		{
			Configuration configuration = new Configuration();
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

		/*public static void GenerateMigrations(string connectionString, string outputPath)
		{
			// Create a service collection and add the necessary services for scaffolding with Npgsql
			var serviceCollection = new ServiceCollection()
				.AddDbContext<DbContext>(options => options.UseNpgsql(connectionString))
				.AddEntityFrameworkNpgsqlDesignTimeServices();

			// Create a service provider from the service collection
			var serviceProvider = serviceCollection.BuildServiceProvider();

			// Create a scaffolder from the service provider
			var scaffolder = serviceProvider.GetService<IReverseEngineerScaffolder>();

			// Set the options for the scaffolder
			var options = new ReverseEngineerOptions
			{
				UseDatabaseNames = true,
				UseInflector = true,
				UseNpgsqlLegacyDatabaseNameBehavior = true // Npgsql-specific option
			};

			// Scaffold the migration
			var scaffoldedModel = scaffolder.ScaffoldModel(connectionString, new DatabaseModelFactoryOptions(), options);

			// Create a migration
			var migration = new CSharpMigrationCodeGenerator().GenerateSnapshot(scaffoldedModel.Model, scaffoldedModel.Migrations);

			// Write the migration to the output path
			File.WriteAllText(outputPath, migration);
		}*/
	}
}