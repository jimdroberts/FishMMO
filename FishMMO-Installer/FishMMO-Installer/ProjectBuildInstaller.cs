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
			/// Build error output captured from the DotNet process.
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
			InstallerProcessHelper.Log("--- Build All C# Projects ---");

			string defaultRootDirectory = ResolveDefaultRootDirectory();
			string selectedRootDirectory = PromptRootDirectory(defaultRootDirectory);

			if (!Directory.Exists(selectedRootDirectory))
			{
				InstallerProcessHelper.Log($"Directory '{selectedRootDirectory}' does not exist.");
				return;
			}

			List<string> projectPaths = FindProjectPaths(selectedRootDirectory);
			if (projectPaths.Count == 0)
			{
				InstallerProcessHelper.Log("No .csproj files were found under the selected directory.");
				return;
			}

			InstallerProcessHelper.Log($"Found {projectPaths.Count} project(s):");
			foreach (string projectPath in projectPaths)
			{
				InstallerProcessHelper.Log($" - {projectPath}");
			}

			if (!InstallerProcessHelper.PromptForYesNo($"Build all {projectPaths.Count} project(s) now?"))
			{
				InstallerProcessHelper.Log("Build operation cancelled by user.");
				return;
			}

			InstallerProcessHelper.Log("Starting parallel build for all discovered projects...");

			List<Task<ProjectBuildResult>> buildTasks = projectPaths
				.Select(BuildProjectAsync)
				.ToList();

			ProjectBuildResult[] buildResults = await Task.WhenAll(buildTasks);

			int successCount = buildResults.Count(result => result.succeeded);
			int failureCount = buildResults.Length - successCount;
			List<ProjectBuildResult> failedResults = buildResults
				.Where(result => !result.succeeded)
				.ToList();

			InstallerProcessHelper.Log($"Build summary: {successCount} succeeded, {failureCount} failed.");
			if (failedResults.Count > 0)
			{
				InstallerProcessHelper.Log("Failed projects:");
				foreach (ProjectBuildResult failedResult in failedResults)
				{
					InstallerProcessHelper.Log($" - {failedResult.projectPath}");
					if (!string.IsNullOrWhiteSpace(failedResult.errorOutput))
					{
						InstallerProcessHelper.Log(failedResult.errorOutput.Trim());
					}
				}
			}
		}

		/// <summary>
		/// Cleans and then builds a single project file, capturing the final outcome.
		/// </summary>
		/// <param name="projectPath">Absolute path to the project file.</param>
		/// <returns>Build outcome payload for the requested project.</returns>
		private static async Task<ProjectBuildResult> BuildProjectAsync(string projectPath)
		{
			InstallerProcessHelper.Log($"Cleaning: {projectPath}");

			var result = new ProjectBuildResult
			{
				projectPath = projectPath
			};

			bool cleanSucceeded = await InstallerProcessHelper.RunProcessAsync(
				"dotnet",
				$"clean \"{projectPath}\" -nologo",
				(exitCode, output, error) =>
				{
					if (!string.IsNullOrWhiteSpace(output))
					{
						Console.WriteLine(output);
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

			InstallerProcessHelper.Log($"Building: {projectPath}");

			result.succeeded = await InstallerProcessHelper.RunProcessAsync(
				"dotnet",
				$"build \"{projectPath}\" -nologo",
				(exitCode, output, error) =>
				{
					if (!string.IsNullOrWhiteSpace(output))
					{
						Console.WriteLine(output);
					}

					if (!string.IsNullOrWhiteSpace(error))
					{
						result.errorOutput = error;
					}

					return exitCode == 0;
				});

			return result;
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
					InstallerProcessHelper.Log($"Skipping inaccessible directory: {currentDirectory}");
				}
				catch (IOException)
				{
					InstallerProcessHelper.Log($"Skipping directory due to IO error: {currentDirectory}");
				}
			}

			projectPaths.Sort(StringComparer.OrdinalIgnoreCase);
			return projectPaths;
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
				|| directoryName.Equals("FishMMO-Unity", StringComparison.OrdinalIgnoreCase);
		}
	}
}