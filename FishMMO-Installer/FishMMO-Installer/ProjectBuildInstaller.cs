using FishMMO.Logging;

namespace FishMMO.Installer
{
	/// <summary>
	/// Builds all discovered C# project files under a selected directory hierarchy.
	/// The scan is dynamic and includes new projects automatically when added.
	/// </summary>
	public static class ProjectBuildInstaller
	{
		/// <summary>
		/// Build result payload for one project build execution.
		/// </summary>
		private sealed class ProjectBuildResult
		{
			/// <summary>
			/// Full path to the project that was built.
			/// </summary>
			public string projectPath = string.Empty;

			/// <summary>
			/// True when the build completed successfully.
			/// </summary>
			public bool succeeded;

			/// <summary>
			/// Full stdout captured from the DotNet process (printed to console live).
			/// </summary>
			public string buildOutput = string.Empty;

			/// <summary>
			/// Build error output captured from the DotNet process stderr.
			/// </summary>
			public string errorOutput = string.Empty;
		}

		/// <summary>
		/// Prompts for a root directory, discovers all .csproj files recursively,
		/// asks for confirmation, and builds each project with DotNet.
		/// </summary>
		public static async Task BuildAllProjectsInSelectedRootAsync()
		{
			Console.Clear();
			await Log.Info("FishMMOInstaller", "--- Build All C# Projects ---");

			string defaultRootDirectory = ResolveDefaultRootDirectory();
			string selectedRootDirectory = PromptRootDirectory(defaultRootDirectory);

			if (!Directory.Exists(selectedRootDirectory))
			{
				await Log.Warning("FishMMOInstaller", $"Directory '{selectedRootDirectory}' does not exist.");
				return;
			}

			List<string> projectPaths = FindProjectPaths(selectedRootDirectory);
			if (projectPaths.Count == 0)
			{
				await Log.Warning("FishMMOInstaller", "No .csproj files were found under the selected directory.");
				return;
			}

			await Log.Info("FishMMOInstaller", $"Found {projectPaths.Count} project(s):");
			foreach (string projectPath in projectPaths)
			{
				await Log.Info("FishMMOInstaller", $" - {projectPath}");
			}

			if (!InstallerProcessHelper.PromptForYesNo($"Build all {projectPaths.Count} project(s) now?"))
			{
				await Log.Info("FishMMOInstaller", "Build operation cancelled by user.");
				return;
			}

			await Log.Info("FishMMOInstaller", "Starting staged build: synchronous for prioritized projects, parallel for remaining projects...");

			var buildResults = new List<ProjectBuildResult>(projectPaths.Count);
			int firstAsyncIndex = projectPaths.FindIndex(path => GetProjectBuildPriority(path) >= 6);

			int synchronousBuildCount = firstAsyncIndex == -1 ? projectPaths.Count : firstAsyncIndex;
			for (int i = 0; i < synchronousBuildCount; i++)
			{
				string projectPath = projectPaths[i];
				await Log.Info("FishMMOInstaller", $"[{i + 1}/{projectPaths.Count}] Building synchronously (priority < 6): {projectPath}");
				buildResults.Add(await BuildProjectAsync(projectPath));
			}

			if (synchronousBuildCount < projectPaths.Count)
			{
				int asyncBuildCount = projectPaths.Count - synchronousBuildCount;
				await Log.Info("FishMMOInstaller", $"Starting parallel build for {asyncBuildCount} remaining project(s) (priority >= 6)...");

				List<Task<ProjectBuildResult>> asyncBuildTasks = projectPaths
					.Skip(synchronousBuildCount)
					.Select((projectPath, asyncIndex) => BuildProjectWithPhaseLoggingAsync(
						projectPath,
						synchronousBuildCount + asyncIndex + 1,
						projectPaths.Count))
					.ToList();

				ProjectBuildResult[] asyncBuildResults = await Task.WhenAll(asyncBuildTasks);
				buildResults.AddRange(asyncBuildResults);
			}

			int successCount = buildResults.Count(result => result.succeeded);
			int failureCount = buildResults.Count - successCount;
			List<ProjectBuildResult> failedResults = buildResults
				.Where(result => !result.succeeded)
				.ToList();

			await Log.Info("FishMMOInstaller", $"Build summary: {successCount} succeeded, {failureCount} failed.");
			if (failedResults.Count > 0)
			{
				await Log.Warning("FishMMOInstaller", "Failed projects:");
				foreach (ProjectBuildResult failedResult in failedResults)
				{
					await Log.Info("FishMMOInstaller", $" - {failedResult.projectPath}");
					string detail = !string.IsNullOrWhiteSpace(failedResult.errorOutput)
						? failedResult.errorOutput.Trim()
						: !string.IsNullOrWhiteSpace(failedResult.buildOutput)
							? failedResult.buildOutput.Trim()
							: "(no output captured — check console above for build errors)";
					await Log.Info("FishMMOInstaller", detail);
				}
			}

			if (successCount > 0)
			{
				await CopyRuntimeSourceDirectoriesAsync(selectedRootDirectory);
			}
		}

		/// <summary>
		/// Cleans and then builds a single project file, capturing the final outcome.
		/// </summary>
		/// <param name="projectPath">Absolute path to the project file.</param>
		/// <returns>Build outcome payload for the requested project.</returns>
		private static async Task<ProjectBuildResult> BuildProjectAsync(string projectPath)
		{
			await Log.Info("FishMMOInstaller", $"Cleaning: {projectPath}");

			var result = new ProjectBuildResult
			{
				projectPath = projectPath
			};

			bool cleanSucceeded = await DotNetInstaller.RunDotNetCommandAsync(
				$"clean \"{projectPath}\" -nologo",
				(exitCode, output, error) =>
				{
					if (!string.IsNullOrWhiteSpace(output))
					{
						Console.WriteLine(output);
						result.buildOutput = output;
					}

					if (!string.IsNullOrWhiteSpace(error))
					{
						result.errorOutput = error;
					}

					return exitCode == 0;
				});

			if (!cleanSucceeded)
			{
				result.succeeded = false;
				if (string.IsNullOrWhiteSpace(result.errorOutput))
				{
					result.errorOutput = "dotnet clean failed.";
				}
				return result;
			}

			await Log.Info("FishMMOInstaller", $"Building: {projectPath}");

			result.succeeded = await DotNetInstaller.RunDotNetCommandAsync(
				$"build \"{projectPath}\" -nologo",
				(exitCode, output, error) =>
				{
					if (!string.IsNullOrWhiteSpace(output))
					{
						Console.WriteLine(output);
					}

					if (!string.IsNullOrWhiteSpace(error))
						result.buildOutput = output;
					{
						result.errorOutput = error;
					}

					return exitCode == 0;
				});

			return result;
		}

		/// <summary>
		/// Builds one project while emitting phase-specific progress logging.
		/// </summary>
		private static async Task<ProjectBuildResult> BuildProjectWithPhaseLoggingAsync(string projectPath, int buildIndex, int totalBuildCount)
		{
			await Log.Info("FishMMOInstaller", $"[{buildIndex}/{totalBuildCount}] Building in parallel bucket (priority >= 6): {projectPath}");
			return await BuildProjectAsync(projectPath);
		}

		/// <summary>
		/// Copies FishMMO-Database and FishMMO-SharedUtility source trees from the build root
		/// into the installer's runtime directory so migration services can resolve them at runtime.
		/// Skips bin, obj, .git and .vs subdirectories.
		/// Migration files inside FishMMO-Database/FishMMO-DB/Migrations/ are preserved across copies
		/// so that previously-created migrations are not lost when the source tree is refreshed.
		/// </summary>
		/// <param name="rootDirectory">The root directory that was scanned for projects.</param>
		private static async Task CopyRuntimeSourceDirectoriesAsync(string rootDirectory)
		{
			string runtimeDirectory = InstallerProcessHelper.GetWorkingDirectory();
			string[] sourceDirectoryNames = ["FishMMO-Database", "FishMMO-SharedUtility"];

			foreach (string dirName in sourceDirectoryNames)
			{
				string sourceDir = Path.Combine(rootDirectory, dirName);
				string destDir = Path.Combine(runtimeDirectory, dirName);

				if (!Directory.Exists(sourceDir))
				{
					await Log.Info("FishMMOInstaller", $"Skipping runtime copy — source not found: {sourceDir}");
					continue;
				}

				await Log.Info("FishMMOInstaller", $"Copying {dirName} to runtime directory...");

				// Snapshot any migration files before wiping the destination so they survive the refresh.
				var savedMigrations = new Dictionary<string, byte[]>();
				if (dirName.Equals("FishMMO-Database", StringComparison.OrdinalIgnoreCase) && Directory.Exists(destDir))
				{
					string migrationsDir = Path.Combine(destDir, "FishMMO-DB", InstallationConstants.MigrationsOutputDirectory);
					if (Directory.Exists(migrationsDir))
					{
						foreach (string file in Directory.EnumerateFiles(migrationsDir, "*", SearchOption.AllDirectories))
						{
							savedMigrations[Path.GetRelativePath(destDir, file)] = File.ReadAllBytes(file);
						}
					}
				}

				if (Directory.Exists(destDir))
				{
					Directory.Delete(destDir, recursive: true);
				}

				await Task.Run(() => CopyDirectoryRecursive(sourceDir, destDir));
				await Log.Info("FishMMOInstaller", $"Copied {dirName} → {destDir}");

				// Restore migration files that were present before the copy.
				if (savedMigrations.Count > 0)
				{
					foreach (var (relativePath, content) in savedMigrations)
					{
						string restoredPath = Path.Combine(destDir, relativePath);
						Directory.CreateDirectory(Path.GetDirectoryName(restoredPath)!);
						File.WriteAllBytes(restoredPath, content);
					}
					await Log.Info("FishMMOInstaller", $"Restored {savedMigrations.Count} migration file(s) in {dirName}.");
				}
			}
		}

		/// <summary>
		/// Recursively copies a directory tree, skipping common non-source subdirectories.
		/// </summary>
		private static void CopyDirectoryRecursive(string sourceDir, string destDir)
		{
			Directory.CreateDirectory(destDir);

			foreach (string file in Directory.EnumerateFiles(sourceDir))
			{
				File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true);
			}

			foreach (string subDir in Directory.EnumerateDirectories(sourceDir))
			{
				string subDirName = Path.GetFileName(subDir);
				if (subDirName.Equals("bin", StringComparison.OrdinalIgnoreCase)
					|| subDirName.Equals("obj", StringComparison.OrdinalIgnoreCase)
					|| subDirName.Equals(".git", StringComparison.OrdinalIgnoreCase)
					|| subDirName.Equals(".vs", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				CopyDirectoryRecursive(subDir, Path.Combine(destDir, subDirName));
			}
		}

		/// <summary>
		/// Prompts for a root directory using a default value.
		/// </summary>
		/// <param name="defaultRootDirectory">Default root directory suggestion.</param>
		/// <returns>Selected root directory.</returns>
		private static string PromptRootDirectory(string defaultRootDirectory)
		{
			string? input = InstallerProcessHelper.PromptForInput($"Enter project hierarchy root directory [{defaultRootDirectory}]: ");
			if (string.IsNullOrWhiteSpace(input))
			{
				return defaultRootDirectory;
			}

			return input.Trim();
		}

		/// <summary>
		/// Resolves a practical default hierarchy directory by finding the installer repository
		/// and returning its parent directory.
		/// </summary>
		/// <returns>Default root directory for dynamic project scanning.</returns>
		private static string ResolveDefaultRootDirectory()
		{
			string workingDirectory = InstallerProcessHelper.GetWorkingDirectory();
			DirectoryInfo? currentDirectory = new DirectoryInfo(workingDirectory);

			while (currentDirectory != null)
			{
				string solutionPath = Path.Combine(currentDirectory.FullName, "FishMMO-Installer.slnx");
				if (File.Exists(solutionPath))
				{
					return currentDirectory.Parent?.FullName ?? currentDirectory.FullName;
				}

				currentDirectory = currentDirectory.Parent;
			}

			return Directory.GetParent(workingDirectory)?.FullName ?? workingDirectory;
		}

		/// <summary>
		/// Recursively discovers all .csproj files under the provided root directory.
		/// </summary>
		/// <param name="rootDirectory">Hierarchy root directory to scan.</param>
		/// <returns>Sorted list of project file paths.</returns>
		private static List<string> FindProjectPaths(string rootDirectory)
		{
			var projectPaths = new List<string>();
			var pendingDirectories = new Stack<string>();
			pendingDirectories.Push(rootDirectory);

			while (pendingDirectories.Count > 0)
			{
				string currentDirectory = pendingDirectories.Pop();

				try
				{
					foreach (string projectPath in Directory.EnumerateFiles(currentDirectory, "*.csproj", SearchOption.TopDirectoryOnly))
					{
						projectPaths.Add(projectPath);
					}

					foreach (string childDirectory in Directory.EnumerateDirectories(currentDirectory, "*", SearchOption.TopDirectoryOnly))
					{
						if (!ShouldSkipDirectory(childDirectory))
						{
							pendingDirectories.Push(childDirectory);
						}
					}
				}
				catch (UnauthorizedAccessException)
				{
					_ = Log.Warning("FishMMOInstaller", $"Skipping inaccessible directory: {currentDirectory}");
				}
				catch (IOException)
				{
					_ = Log.Warning("FishMMOInstaller", $"Skipping directory due to IO error: {currentDirectory}");
				}
			}

			projectPaths.Sort((left, right) =>
			{
				int leftPriority = GetProjectBuildPriority(left);
				int rightPriority = GetProjectBuildPriority(right);
				int priorityComparison = leftPriority.CompareTo(rightPriority);
				if (priorityComparison != 0)
				{
					return priorityComparison;
				}

				return StringComparer.OrdinalIgnoreCase.Compare(left, right);
			});
			return projectPaths;
		}

		/// <summary>
		/// Assigns a priority bucket to each project so dependency projects build first.
		/// Lower numbers are built earlier.
		/// </summary>
		/// <param name="projectPath">Absolute path to a project file.</param>
		/// <returns>Priority bucket for build ordering.</returns>
		private static int GetProjectBuildPriority(string projectPath)
		{
			string normalizedPath = projectPath.Replace('\\', '/').ToLowerInvariant();
			string projectName = Path.GetFileNameWithoutExtension(projectPath).ToLowerInvariant();

			if (projectName.Contains("fishmmo-dependencies") || normalizedPath.Contains("fishmmo-dependencies"))
			{
				return 0;
			}

			if (projectName.Contains("fishmmo-logger") || normalizedPath.Contains("fishmmo-logger"))
			{
				return 1;
			}

			if (projectName.Contains("fishmmo-database")
				|| projectName.Contains("fishmmo-db")
				|| normalizedPath.Contains("fishmmo-database")
				|| normalizedPath.Contains("fishmmo-db"))
			{
				return 2;
			}

			if (projectName.Contains("fishmmo-sharedutility") || normalizedPath.Contains("fishmmo-sharedutility"))
			{
				return 3;
			}

			if (projectName.Contains("fishmmo-auth") || normalizedPath.Contains("fishmmo-auth"))
			{
				return 4;
			}

			if (projectName.Contains("fishmmo-cms") || normalizedPath.Contains("fishmmo-cms"))
			{
				return 5;
			}

			return 6;
		}

		/// <summary>
		/// Determines whether a directory should be skipped during recursive project discovery.
		/// </summary>
		/// <param name="directoryPath">Directory path to evaluate.</param>
		/// <returns>True when the directory should be skipped.</returns>
		private static bool ShouldSkipDirectory(string directoryPath)
		{
			string directoryName = Path.GetFileName(directoryPath);
			return directoryName.Equals("bin", StringComparison.OrdinalIgnoreCase)
				|| directoryName.Equals("obj", StringComparison.OrdinalIgnoreCase)
				|| directoryName.Equals(".git", StringComparison.OrdinalIgnoreCase)
				|| directoryName.Equals(".vs", StringComparison.OrdinalIgnoreCase)
				|| directoryName.Equals("node_modules", StringComparison.OrdinalIgnoreCase)
				|| directoryName.Equals("FishMMO-Unity", StringComparison.OrdinalIgnoreCase)
				|| directoryName.Equals("FishMMO-Installer", StringComparison.OrdinalIgnoreCase);
		}
	}
}