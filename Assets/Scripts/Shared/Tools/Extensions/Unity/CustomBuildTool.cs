#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using UnityEditor.Build.Reporting;
using Debug = UnityEngine.Debug;
using Server;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Migrations.Internal;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.EntityFrameworkCore.Design.Internal;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;

public class CustomBuildTool
{
	public const string SERVER_BUILD_NAME = "FishMMOServer";
	public const string CLIENT_BUILD_NAME = "FishMMOClient";

	public static readonly string[] SERVER_BOOTSTRAP_SCENES = new string[]
	{
		"Assets/Scenes/Bootstraps/ServerLauncher.unity",
		"Assets/Scenes/Bootstraps/LoginServer.unity",
		"Assets/Scenes/Bootstraps/WorldServer.unity",
		"Assets/Scenes/Bootstraps/SceneServer.unity",
	};

	public static readonly string[] CLIENT_BOOTSTRAP_SCENES = new string[]
	{
		"Assets/Scenes/Bootstraps/ClientBootstrap.unity",
	};

	public static string GetBuildTargetShortName(BuildTarget target)
	{
		switch (target)
		{
			case BuildTarget.StandaloneWindows:
				return "_win_32";
			case BuildTarget.StandaloneWindows64:
				return "_win_64";
			case BuildTarget.StandaloneLinux64:
				return "_linux_64";
			default:
				return "";
		}
	}

	private static void BuildExecutable(string executableName, string[] bootstrapScenes, BuildOptions buildOptions, StandaloneBuildSubtarget subTarget, BuildTarget buildTarget)
	{
		string rootPath = EditorUtility.SaveFolderPanel("Pick a save directory", "", "");
		if (string.IsNullOrWhiteSpace(rootPath))
		{
			return;
		}

		if (string.IsNullOrWhiteSpace(executableName) ||
			bootstrapScenes == null ||
			bootstrapScenes.Length < 1)
		{
			return;
		}

		// just incase buildpipeline bug is still present
		BuildTargetGroup targetGroup = BuildPipeline.GetBuildTargetGroup(buildTarget);
		EditorUserBuildSettings.SwitchActiveBuildTarget(targetGroup, buildTarget);
		EditorUserBuildSettings.standaloneBuildSubtarget = subTarget;

		// compile scenes list with bootstraps
		string[] scenes = GetBuildScenePaths(bootstrapScenes);

		string folderName = executableName + GetBuildTargetShortName(buildTarget);
		string buildPath = rootPath + "/" + folderName;

		// build the project
		BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions()
		{
			locationPathName = buildPath + "/" + executableName + ".exe",
			options = buildOptions,
			scenes = scenes,
			subtarget = (int)subTarget,
			target = buildTarget,
			targetGroup = targetGroup,
		});

		// check the results of the build
		BuildSummary summary = report.summary;
		if (summary.result == BuildResult.Succeeded)
		{
			Debug.Log("Build succeeded: " + summary.totalSize + " bytes " + DateTime.UtcNow);

			// copy the configuration files if it's a server build
			if (subTarget == StandaloneBuildSubtarget.Server)
			{
				string defaultFileDirectory = "";
#if UNITY_EDITOR
				defaultFileDirectory = Directory.GetParent(Application.dataPath).FullName;
#elif UNITY_ANDROID
				defaultFileDirectory = Application.persistentDataPath;
#elif UNITY_IOS
				defaultFileDirectory = Application.persistentDataPath;
#else
				defaultFileDirectory = Application.dataPath;
#endif

				if (buildTarget == BuildTarget.StandaloneWindows64)
				{
					FileUtil.ReplaceFile(defaultFileDirectory + "/START.bat", buildPath + "/START.bat");
				}
				else if (buildTarget == BuildTarget.StandaloneLinux64)
				{
					FileUtil.ReplaceFile(defaultFileDirectory + "/START.sh", buildPath + "/START.sh");
				}

				// append the data folder for configuration copy
				buildPath += "/" + executableName + "_Data";

				FileUtil.ReplaceFile(defaultFileDirectory + "/LoginServer.cfg", buildPath + "/LoginServer.cfg");
				FileUtil.ReplaceFile(defaultFileDirectory + "/WorldServer.cfg", buildPath + "/WorldServer.cfg");
				FileUtil.ReplaceFile(defaultFileDirectory + "/SceneServer.cfg", buildPath + "/SceneServer.cfg");
				FileUtil.ReplaceFile(defaultFileDirectory + "/Database.cfg", buildPath + "/Database.cfg");
			}
		}
		else if (summary.result == BuildResult.Failed)
		{
			Debug.Log("Build failed: " + report.summary.result + " " + report);
		}
	}

	private static string[] GetBuildScenePaths(string[] requiredPaths)
	{
		List<string> allPaths = new List<string>(requiredPaths);

		// add all of the WorldScenes
		foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
		{
			if (scene.path.Contains(WorldSceneDetailsCache.WORLD_SCENE_PATH))
			{
				allPaths.Add(scene.path);
			}
		}

		return allPaths.ToArray();
	}

	[MenuItem("FishMMO/Windows 32 Server Build")]
	public static void BuildWindows32Server()
	{
		BuildExecutable(SERVER_BUILD_NAME,
						SERVER_BOOTSTRAP_SCENES,
						BuildOptions.CleanBuildCache | BuildOptions.Development | BuildOptions.ShowBuiltPlayer,
						StandaloneBuildSubtarget.Server,
						BuildTarget.StandaloneWindows);
	}

	[MenuItem("FishMMO/Windows 32 Client Build")]
	public static void BuildWindows32Client()
	{
		BuildExecutable(CLIENT_BUILD_NAME,
						CLIENT_BOOTSTRAP_SCENES,
						BuildOptions.CleanBuildCache | BuildOptions.Development | BuildOptions.ShowBuiltPlayer,
						StandaloneBuildSubtarget.Player,
						BuildTarget.StandaloneWindows);
	}

	[MenuItem("FishMMO/Windows 64 Server Build")]
	public static void BuildWindows64Server()
	{
		BuildExecutable(SERVER_BUILD_NAME,
						SERVER_BOOTSTRAP_SCENES,
						BuildOptions.CleanBuildCache | BuildOptions.Development | BuildOptions.ShowBuiltPlayer,
						StandaloneBuildSubtarget.Server,
						BuildTarget.StandaloneWindows64);
	}

	[MenuItem("FishMMO/Windows 64 Client Build")]
	public static void BuildWindows64Client()
	{
		BuildExecutable(CLIENT_BUILD_NAME,
						CLIENT_BOOTSTRAP_SCENES,
						BuildOptions.CleanBuildCache | BuildOptions.Development | BuildOptions.ShowBuiltPlayer,
						StandaloneBuildSubtarget.Player,
						BuildTarget.StandaloneWindows64);
	}

	[MenuItem("FishMMO/Linux 64 Server Build")]
	public static void BuildLinux64Server()
	{
		BuildExecutable(SERVER_BUILD_NAME,
						SERVER_BOOTSTRAP_SCENES,
						BuildOptions.CleanBuildCache | BuildOptions.Development | BuildOptions.ShowBuiltPlayer,
						StandaloneBuildSubtarget.Server,
						BuildTarget.StandaloneLinux64);
	}

	[MenuItem("FishMMO/Linux 64 Client Build")]
	public static void BuildLinux64Client()
	{
		BuildExecutable(CLIENT_BUILD_NAME,
						CLIENT_BOOTSTRAP_SCENES,
						BuildOptions.CleanBuildCache | BuildOptions.Development | BuildOptions.ShowBuiltPlayer,
						StandaloneBuildSubtarget.Player,
						BuildTarget.StandaloneLinux64);
	}

	[MenuItem("FishMMO/Setup Docker Database Container")]
	public static async void Database()
	{
		ServerDbContextFactory dbContextFactory = new ServerDbContextFactory(true);

		// load configuration first
		Configuration configuration = dbContextFactory.GetOrCreateDatabaseConfiguration();

		if (configuration.TryGetString("DbName", out string dbName) &&
			configuration.TryGetString("DbUsername", out string dbUsername) &&
			configuration.TryGetString("DbPassword", out string dbPassword) &&
			configuration.TryGetString("DbAddress", out string dbAddress) &&
			configuration.TryGetString("DbPort", out string dbPort))
		{
			Debug.Log("Creating Docker container with postgresql 14");
			await Docker.RunAsync("run --name " + dbName +
								  " -e POSTGRES_USER=" + dbUsername +
								  " -e POSTGRES_PASSWORD=" + dbPassword +
								  " -p " + dbAddress + ":" + dbPort + ":" + dbPort +
								  " -d postgres:14");

			Debug.Log("Ensuring container exists... Please wait...");
			await Task.Delay(5000);

			using ServerDbContext dbContext = dbContextFactory.CreateDbContext();

			Debug.Log("Ensuring database exists... Please wait...");
			GenerateDatabaseSQL(dbContext);
			Debug.Log("Database setup completed!");
		}
	}

	[MenuItem("FishMMO/Migrate Database")]
	public static void MigrateDatabase()
	{
		ServerDbContextFactory dbContextFactory = new ServerDbContextFactory(true);

		using ServerDbContext dbContext = dbContextFactory.CreateDbContext();

		Debug.Log("Migrating...");

		var migrator = dbContext.Database.GetService<IMigrator>();
		if (migrator == null)
		{
			Debug.LogError("Database context does not have a Migrator service!");
			return;
		}
		migrator.Migrate();

		Debug.Log("Migration complete!");
	}

	public static void GenerateDatabaseSQL(DbContext dbContext)
	{
		var migrator = dbContext.Database.GetService<IMigrator>();
		if (migrator == null)
		{
			Debug.LogError("Database context does not have a Migrator service!");
			return;
		}
		migrator.Migrate();

		// Get the MigrationsAssembly from the DbContext's IInfrastructure
		var migrationsAssembly = dbContext.GetService<IMigrationsAssembly>();

		// Generate a migration script for the latest changes to the database
		var migrationSqlGenerator = dbContext.GetService<IMigrationsSqlGenerator>();

		// Get the latest migration
		var appliedMigrations = dbContext.Database.GetAppliedMigrations();

		var modelSnapshot = migrationsAssembly.ModelSnapshot;

		var currentModel = dbContext.Model.GetRelationalModel();
		var targetModel = modelSnapshot?.Model.GetRelationalModel();

		// Compare the current model with the target model to get the migration operations
		var modelDiffer = dbContext.GetService<IMigrationsModelDiffer>();
		var migrationOperations = modelDiffer.GetDifferences(targetModel, currentModel);

		// Add the NpgsqlDatabaseOperations provider if it's not already registered
		var migrationCommandList = migrationSqlGenerator.Generate(migrationOperations);
		var migrationCommands = migrationCommandList.Select(c => c.CommandText);

		// Execute the migration commands
		var migrationCommandExecutor = dbContext.GetService<IMigrationCommandExecutor>();
		var relationalConnection = dbContext.GetService<IRelationalConnection>();

		try
		{
			migrationCommandExecutor.ExecuteNonQuery(migrationCommandList, relationalConnection);
		}
		catch (Exception e)
		{
			// ignore errors with the query.. most of them are redundant race condition errors :D
			Debug.Log(e.Message);
		}

		// save script to sql file
		var script = string.Join(Environment.NewLine, migrationCommands);

		string filePath = "migrationScript.sql";
		File.WriteAllText(filePath, script);
	}

	public static void TEST2Migration(ServerDbContext dbContext)
	{
		var reporter = new OperationReporter(
				new OperationReportHandler(
					m => Console.WriteLine("  error: " + m),
					m => Console.WriteLine("   warn: " + m),
					m => Console.WriteLine("   info: " + m),
					m => Console.WriteLine("verbose: " + m)));

		var designTimeServices = new ServiceCollection()
			.AddSingleton(dbContext.GetService<IHistoryRepository>())
			.AddSingleton(dbContext.GetService<IMigrationsIdGenerator>())
			.AddSingleton(dbContext.GetService<IMigrationsModelDiffer>())
			.AddSingleton(dbContext.GetService<IMigrationsAssembly>())
			.AddSingleton(dbContext.Model)
			.AddSingleton(dbContext.GetService<ICurrentDbContext>())
			.AddSingleton(dbContext.GetService<IDatabaseProvider>())
			.AddSingleton<MigrationsCodeGeneratorDependencies>()
			.AddSingleton<ICSharpHelper, CSharpHelper>()
			.AddSingleton<CSharpMigrationOperationGeneratorDependencies>()
			.AddSingleton<ICSharpMigrationOperationGenerator, CSharpMigrationOperationGenerator>()
			.AddSingleton<CSharpSnapshotGeneratorDependencies>()
			.AddSingleton<ICSharpSnapshotGenerator, CSharpSnapshotGenerator>()
			.AddSingleton<CSharpMigrationsGeneratorDependencies>()
			.AddSingleton<IMigrationsCodeGenerator, CSharpMigrationsGenerator>()
			.AddSingleton<IOperationReporter>(reporter)
			.AddSingleton<MigrationsScaffolderDependencies>()
			.AddSingleton<ISnapshotModelProcessor, SnapshotModelProcessor>()
			.AddSingleton<MigrationsScaffolder>()
			.BuildServiceProvider();

		var scaffolderDependencies = designTimeServices.GetRequiredService<MigrationsScaffolderDependencies>();

		var modelSnapshot = scaffolderDependencies.MigrationsAssembly.ModelSnapshot;
		var lastModel = scaffolderDependencies.SnapshotModelProcessor.Process(modelSnapshot?.Model);
		var source = lastModel.GetRelationalModel();
		var target = scaffolderDependencies.Model.GetRelationalModel();
		var upOperations = scaffolderDependencies.MigrationsModelDiffer.GetDifferences(source, target);
		var downOperations = upOperations.Any() ? scaffolderDependencies.MigrationsModelDiffer.GetDifferences(target, source) : new List<MigrationOperation>();

		if (upOperations.Count() > 0 || downOperations.Count() > 0)
		{
			var scaffolder = designTimeServices.GetRequiredService<MigrationsScaffolder>();

			var migration = scaffolder.ScaffoldMigration(
				"MyMigration",
				"MyApp.Data");

			File.WriteAllText(
				migration.MigrationId + migration.FileExtension,
				migration.MigrationCode);
			File.WriteAllText(
				migration.MigrationId + ".Designer" + migration.FileExtension,
				migration.MetadataCode);
			File.WriteAllText(migration.SnapshotName + migration.FileExtension,
			   migration.SnapshotCode);
		}
	}
}
#endif