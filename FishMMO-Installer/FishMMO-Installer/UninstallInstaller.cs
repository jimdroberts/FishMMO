using FishMMO.Database;
using FishMMO.Logging;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.RegularExpressions;

namespace FishMMO.Installer
{
	/// <summary>
	/// The Uninstall screens: removing PostgreSQL, and deleting the secrets file.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Nothing here is undoable.</b> Removing PostgreSQL deletes the cluster data directory, and
	/// that directory is every account, character, guild, item and audit row the deployment has ever
	/// had. There is no recycle bin and no second copy. The confirmation flow is therefore the
	/// feature, not the packaging around it:
	/// </para>
	/// <list type="number">
	///   <item>Resolve the real commands and the real paths for <i>this</i> machine and show them.</item>
	///   <item>Say which services are running that will break the moment the database is gone.</item>
	///   <item>Offer a dump first, and stop dead if the dump is asked for and fails.</item>
	///   <item>Require a typed phrase nobody types by reflex — not a keypress.</item>
	///   <item>Run the steps in order, stop at the first real failure, and report every outcome, so
	///   a half-done uninstall is visible rather than silent.</item>
	/// </list>
	/// <para>
	/// The command and path resolution lives in <see cref="UninstallPlanner"/>, which touches
	/// nothing and can be asserted on its own. This file is the part that talks to the operator and
	/// runs what the planner decided.
	/// </para>
	/// </remarks>
	public static class UninstallInstaller
	{
		/// <summary>What the operator must type to destroy the database cluster.</summary>
		/// <remarks>
		/// Deliberately not <c>DELETE</c>: that is what the database-drop screen asks for, and a
		/// phrase the operator has already typed once today is a phrase they can type without
		/// reading. This one names the consequence.
		/// </remarks>
		private const string PostgreSQLConfirmationPhrase = "DESTROY ALL DATA";

		/// <summary>What the operator must type to delete the secrets file.</summary>
		private const string SecretsConfirmationPhrase = "DELETE SECRETS";

		/// <summary>The file name the secrets path must end with before anything deletes it.</summary>
		private const string SecretsFileName = "db-secrets.env";

		/// <summary>
		/// systemd units that stop working the moment PostgreSQL is gone.
		/// </summary>
		/// <remarks>
		/// The four web servers are registered by <see cref="SystemdServiceInstaller"/>; the game
		/// servers run under the AppHealthMonitor unit; pgbouncer is a pooler this installer also
		/// installs, and it is pointless without a server behind it.
		/// </remarks>
		private static readonly string[] LinuxDependantUnits =
		{
			InstallationConstants.PgBouncerLinuxServiceName,
			"fishmmo-ipfetch",
			"fishmmo-patcher",
			"fishmmo-webgl",
			"fishmmo-controlpanel",
			InstallationConstants.AppHealthMonitorSystemdServiceName,
		};

		/// <summary>Windows services that stop working the moment PostgreSQL is gone.</summary>
		private static readonly string[] WindowsDependantServices =
		{
			InstallationConstants.IpFetchWindowsServiceName,
			InstallationConstants.PatcherWindowsServiceName,
			InstallationConstants.WebGLWindowsServiceName,
			InstallationConstants.AppHealthMonitorWindowsServiceName,
			"pgbouncer",
		};

		/// <summary>Whether a directory is there, absent, or could not be determined without root.</summary>
		private enum PathState
		{
			/// <summary>Confirmed present.</summary>
			Present,

			/// <summary>Confirmed absent.</summary>
			Absent,

			/// <summary>Could not be determined — the parent is not readable by this user.</summary>
			Unknown,
		}

		// ──────────────────────────────────────────────────────────────────────
		//  PostgreSQL
		// ──────────────────────────────────────────────────────────────────────

		/// <summary>
		/// Removes PostgreSQL and its cluster data, after showing exactly what will run and taking a
		/// typed confirmation.
		/// </summary>
		public static async Task UninstallPostgreSQL()
		{
			Console.Clear();
			Console.WriteLine("=== Uninstall PostgreSQL ===");
			Console.WriteLine();

			PostgreSQLUninstallPlan? plan;
			string? windowsVersionDirectory = null;

			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				(plan, windowsVersionDirectory) = await BuildWindowsPlanInteractive();
			}
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
			{
				plan = await BuildLinuxPlanInteractive();
			}
			else
			{
				await Log.Warning("FishMMOInstaller", "Uninstalling PostgreSQL is only supported on Windows and Linux.");
				return;
			}

			if (plan == null)
			{
				return;
			}

			// Resolve every directory's state once, so the list the operator reads and the list the
			// executor acts on are the same list.
			var directoryStates = new Dictionary<string, PathState>(StringComparer.Ordinal);
			foreach (UninstallStep removal in plan.DirectoryRemovals)
			{
				if (removal.TargetPath != null && !directoryStates.ContainsKey(removal.TargetPath))
				{
					directoryStates[removal.TargetPath] = await ProbeDirectoryAsync(removal.TargetPath, plan.Platform);
				}
			}

			await PrintPlan(plan, directoryStates);

			if (!await OfferBackup(plan, windowsVersionDirectory))
			{
				await Log.Warning("FishMMOInstaller", "Backup failed. Nothing has been uninstalled.");
				return;
			}

			Console.WriteLine();
			Console.WriteLine("This deletes the PostgreSQL cluster. Every account, character, guild,");
			Console.WriteLine("item and audit record in it is gone permanently. There is no undo.");
			Console.WriteLine();

			string? typed = InstallerProcessHelper.PromptForInput($"Type '{PostgreSQLConfirmationPhrase}' exactly to proceed, or anything else to cancel: ");
			if (typed?.Trim().Equals(PostgreSQLConfirmationPhrase, StringComparison.Ordinal) != true)
			{
				await Log.Info("FishMMOInstaller", "PostgreSQL uninstall cancelled. Nothing was changed.");
				return;
			}

			await ExecutePlan(plan, directoryStates);
		}

		/// <summary>
		/// Detects the package manager, reports the dependants that are running, and builds the Linux
		/// plan — or returns null when there is nothing to work with.
		/// </summary>
		private static async Task<PostgreSQLUninstallPlan?> BuildLinuxPlanInteractive()
		{
			PackageManagerInfo? detected = await LinuxPackageManagerHelper.DetectAsync(UninstallPlanner.PostgreSQLPackageNames());
			if (detected == null)
			{
				await Log.Warning("FishMMOInstaller",
					"No supported package manager (pacman, apt-get, dnf, yum) was found, so the packages " +
					"PostgreSQL was installed with cannot be identified. Remove it with your distribution's tools.");
				return null;
			}

			if (!UninstallPlanner.TryResolvePackageManagerKind(detected.ManagerName, out PackageManagerKind kind))
			{
				await Log.Warning("FishMMOInstaller", $"Package manager '{detected.ManagerName}' is not one this uninstaller knows how to undo.");
				return null;
			}

			Console.WriteLine($"Package manager: {detected.ManagerName}");

			if (!await PostgreSQLInstaller.IsPostgreSQLInstalledAsync())
			{
				Console.WriteLine();
				Console.WriteLine("PostgreSQL does not appear to be installed (no psql on PATH).");
				Console.WriteLine("Its data directory can still be on disk from a previous install.");
				if (!InstallerProcessHelper.PromptForYesNo("Continue, to look for and remove leftovers?"))
				{
					return null;
				}
			}

			IReadOnlyList<string> running = await FindRunningLinuxDependants();
			IReadOnlyList<string> toStop = await ReportDependants(running, "systemd units");

			return UninstallPlanner.BuildLinuxPlan(kind, toStop);
		}

		/// <summary>
		/// Finds the installed PostgreSQL versions, lets the operator pick one when there is more than
		/// one, and builds the Windows plan around EnterpriseDB's uninstaller.
		/// </summary>
		/// <returns>The plan and the version directory (used to locate pg_dumpall), or nulls.</returns>
		private static async Task<(PostgreSQLUninstallPlan? Plan, string? VersionDirectory)> BuildWindowsPlanInteractive()
		{
			string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
			string installRoot = string.IsNullOrWhiteSpace(programFiles)
				? UninstallPlanner.WindowsDefaultInstallRoot
				: $@"{programFiles.TrimEnd('\\')}\PostgreSQL";

			Console.WriteLine($"Looking for PostgreSQL under {installRoot}");

			var found = new List<(string Version, string Directory)>();
			if (Directory.Exists(installRoot))
			{
				foreach (string directory in Directory.GetDirectories(installRoot).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
				{
					string name = Path.GetFileName(directory);
					if (string.IsNullOrWhiteSpace(name))
					{
						continue;
					}

					if (File.Exists(Path.Combine(directory, UninstallPlanner.WindowsUninstallerFileName)))
					{
						found.Add((name, directory));
					}
					else
					{
						Console.WriteLine($"  {directory} has no {UninstallPlanner.WindowsUninstallerFileName} — skipping it.");
					}
				}
			}

			IReadOnlyList<string> services = await FindWindowsPostgreSQLServices();
			if (services.Count > 0)
			{
				Console.WriteLine($"Windows services found: {string.Join(", ", services)}");
			}

			if (found.Count == 0)
			{
				Console.WriteLine();
				Console.WriteLine($"No PostgreSQL installation with an {UninstallPlanner.WindowsUninstallerFileName} was found under {installRoot}.");
				Console.WriteLine("Nothing is being guessed at and nothing will be deleted. If PostgreSQL is installed");
				Console.WriteLine("somewhere else, uninstall it from Settings > Apps, or run its uninstaller directly.");
				await Log.Warning("FishMMOInstaller", "PostgreSQL uninstall: no installation discovered.");
				return (null, null);
			}

			(string version, string directory) chosen = found[0];
			if (found.Count > 1)
			{
				Console.WriteLine();
				Console.WriteLine("More than one PostgreSQL version is installed:");
				for (int i = 0; i < found.Count; i++)
				{
					Console.WriteLine($"  {i + 1} : {found[i].Version}   ({found[i].Directory})");
				}

				string? answer = InstallerProcessHelper.PromptForInput($"Which one should be uninstalled? (1-{found.Count}, anything else cancels): ");
				if (!int.TryParse(answer?.Trim(), out int index) || index < 1 || index > found.Count)
				{
					await Log.Info("FishMMOInstaller", "PostgreSQL uninstall cancelled at the version choice.");
					return (null, null);
				}

				chosen = found[index - 1];
			}

			Console.WriteLine($"Selected: PostgreSQL {chosen.version} in {chosen.directory}");

			// Prefer the service name actually registered over the conventional spelling.
			string? serviceName = services.FirstOrDefault(s => s.EndsWith($"-{chosen.version}", StringComparison.OrdinalIgnoreCase));

			if (!IsWindowsElevated())
			{
				Console.WriteLine();
				Console.WriteLine("This installer is not running elevated. EnterpriseDB's uninstaller needs administrator");
				Console.WriteLine("rights: Windows will show a UAC prompt when it starts, and stopping the service or");
				Console.WriteLine("deleting the leftover data directory from here will fail without it.");
				Console.WriteLine("Re-running the installer as administrator is the smoother path.");
				InstallerProcessHelper.LogElevatedProcessEnvironmentWarning("PostgreSQL uninstaller");
			}

			IReadOnlyList<string> running = await FindRunningWindowsDependants();
			IReadOnlyList<string> toStop = await ReportDependants(running, "Windows services");

			PostgreSQLUninstallPlan plan = UninstallPlanner.BuildWindowsPlan(installRoot, chosen.version, serviceName, toStop);
			return (plan, chosen.directory);
		}

		/// <summary>
		/// Tells the operator which dependants are running and asks whether to stop them first.
		/// </summary>
		/// <returns>The units or services to stop, which is empty when the operator declines.</returns>
		private static async Task<IReadOnlyList<string>> ReportDependants(IReadOnlyList<string> running, string label)
		{
			Console.WriteLine();
			if (running.Count == 0)
			{
				Console.WriteLine($"No FishMMO {label} are running.");
				Console.WriteLine("Anything that connects to this database will still fail to start once it is gone:");
				Console.WriteLine("the web servers, the Control Panel, the game servers, and pgbouncer.");
				return Array.Empty<string>();
			}

			Console.WriteLine($"These {label} are running now and depend on this database:");
			foreach (string unit in running)
			{
				Console.WriteLine($"  - {unit}");
			}
			Console.WriteLine("They will break the moment PostgreSQL is removed. Stopping them first means they");
			Console.WriteLine("fail cleanly instead of mid-query, and it releases their open connections.");

			if (!InstallerProcessHelper.PromptForYesNo("Stop them as part of the uninstall?"))
			{
				await Log.Warning("FishMMOInstaller", "Dependent services will be left running through the PostgreSQL uninstall.");
				Console.WriteLine("They will be left running. They will start failing as soon as the server stops.");
				return Array.Empty<string>();
			}

			return running;
		}

		/// <summary>Returns the FishMMO/pgbouncer units systemd reports as active.</summary>
		private static async Task<IReadOnlyList<string>> FindRunningLinuxDependants()
		{
			var running = new List<string>();
			foreach (string unit in LinuxDependantUnits)
			{
				// Read-only: is-active changes nothing.
				bool active = await InstallerProcessHelper.RunProcessAsync(
					"systemctl",
					$"is-active --quiet {unit}",
					(exitCode, _, _) => exitCode == 0);

				if (active)
				{
					running.Add(unit);
				}
			}

			return running;
		}

		/// <summary>Returns the FishMMO/pgbouncer Windows services reported as RUNNING.</summary>
		private static async Task<IReadOnlyList<string>> FindRunningWindowsDependants()
		{
			var running = new List<string>();
			foreach (string service in WindowsDependantServices)
			{
				bool active = await InstallerProcessHelper.RunProcessAsync(
					"sc.exe",
					$"query \"{service}\"",
					(exitCode, output, _) => exitCode == 0 && output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase));

				if (active)
				{
					running.Add(service);
				}
			}

			return running;
		}

		/// <summary>Returns the <c>postgresql-x64-*</c> services registered on this machine.</summary>
		private static async Task<IReadOnlyList<string>> FindWindowsPostgreSQLServices()
		{
			var names = new List<string>();
			await InstallerProcessHelper.RunProcessAsync(
				"sc.exe",
				"query type= service state= all",
				(exitCode, output, _) =>
				{
					if (exitCode != 0)
					{
						return false;
					}

					foreach (Match match in Regex.Matches(output, @"SERVICE_NAME:\s*(postgresql\S*)", RegexOptions.IgnoreCase))
					{
						string name = match.Groups[1].Value.Trim();
						if (!names.Contains(name, StringComparer.OrdinalIgnoreCase))
						{
							names.Add(name);
						}
					}

					return true;
				});

			return names;
		}

		/// <summary>Prints the resolved plan: every command, every path, and whether each path is there.</summary>
		private static async Task PrintPlan(PostgreSQLUninstallPlan plan, IReadOnlyDictionary<string, PathState> directoryStates)
		{
			Console.WriteLine();
			Console.WriteLine("!!! DANGER ZONE: COMPLETE POSTGRESQL UNINSTALL !!!");
			Console.WriteLine($"Target: {plan.TargetDescription}");
			Console.WriteLine();
			Console.WriteLine("These are the exact commands that will run, in this order:");
			Console.WriteLine();

			int number = 1;
			foreach (UninstallStep step in plan.Steps)
			{
				string suffix = "";
				if (step.Kind == UninstallStepKind.DirectoryRemoval && step.TargetPath != null)
				{
					suffix = directoryStates.TryGetValue(step.TargetPath, out PathState state)
						? state switch
						{
							PathState.Present => "   [directory exists — it WILL be deleted]",
							PathState.Absent => "   [not present — this step will be skipped]",
							_ => "   [cannot tell without root — checked again, under root, before deleting]",
						}
						: "";
				}
				else if (step.ContinueOnFailure)
				{
					suffix = "   [failure here does not stop the uninstall]";
				}

				Console.WriteLine($"  {number,2}. {step.Description}{suffix}");
				Console.WriteLine($"      {step.CommandLine}");
				number++;
			}

			await Log.Info("FishMMOInstaller", $"PostgreSQL uninstall plan prepared: {plan.Steps.Count} steps for {plan.TargetDescription}.");
		}

		/// <summary>
		/// Offers a dump before anything is destroyed.
		/// </summary>
		/// <returns>
		/// False only when a backup was asked for and did not produce one — in which case the caller
		/// must not continue. Declining a backup returns true.
		/// </returns>
		private static async Task<bool> OfferBackup(PostgreSQLUninstallPlan plan, string? windowsVersionDirectory)
		{
			Console.WriteLine();
			Console.WriteLine("A dump taken now is the only way back from this. It is taken before anything stops.");

			if (!InstallerProcessHelper.PromptForYesNo("Take a pg_dumpall backup of every database first?"))
			{
				Console.WriteLine("No backup will be taken. The data will be unrecoverable.");
				await Log.Warning("FishMMOInstaller", "Operator declined a backup before the PostgreSQL uninstall.");
				return true;
			}

			string defaultPath = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
				$"fishmmo-postgres-{DateTime.Now:yyyyMMdd-HHmmss}.sql");

			string? input = InstallerProcessHelper.PromptForInput($"Write the dump to (default: {defaultPath}): ");
			string target = string.IsNullOrWhiteSpace(input) ? defaultPath : input.Trim();

			if (File.Exists(target))
			{
				await Log.Error("FishMMOInstaller", $"'{target}' already exists. Refusing to overwrite it; nothing has been uninstalled.");
				return false;
			}

			string? directory = Path.GetDirectoryName(Path.GetFullPath(target));
			if (string.IsNullOrWhiteSpace(directory))
			{
				await Log.Error("FishMMOInstaller", "Could not resolve a directory for the dump; nothing has been uninstalled.");
				return false;
			}

			try
			{
				Directory.CreateDirectory(directory);
			}
			catch (Exception ex)
			{
				await Log.Error("FishMMOInstaller", $"Could not create '{directory}' for the dump: {ex.Message}");
				return false;
			}

			bool dumped = plan.Platform == UninstallPlatform.Windows
				? await RunWindowsDumpAll(windowsVersionDirectory, target)
				: await RunLinuxDumpAll(target);

			if (!dumped)
			{
				await Log.Error("FishMMOInstaller", "pg_dumpall failed. Stopping: the uninstall must not run without the backup that was asked for.");
				return false;
			}

			var info = new FileInfo(target);
			if (!info.Exists || info.Length == 0)
			{
				await Log.Error("FishMMOInstaller", $"pg_dumpall reported success but '{target}' is empty. Stopping.");
				return false;
			}

			await Log.Info("FishMMOInstaller", $"Backup written to {target} ({info.Length:N0} bytes). Restore with: psql -U postgres -f \"{target}\"");
			Console.WriteLine($"Backup written to {target} ({info.Length:N0} bytes).");
			Console.WriteLine($"Restore it into a fresh cluster with: psql -U postgres -f \"{target}\"");
			return true;
		}

		/// <summary>Dumps every database as the postgres system user.</summary>
		/// <remarks>
		/// The redirection is performed by this shell, as the invoking user, so the dump file is owned
		/// by the operator and lands wherever they can write — <c>pg_dumpall -f</c> run through
		/// <c>sudo -u postgres</c> would need the postgres user to be able to write there instead.
		/// </remarks>
		private static async Task<bool> RunLinuxDumpAll(string target)
		{
			(string shell, string argPrefix) = InstallerProcessHelper.GetShellCommand();
			Console.WriteLine("Running pg_dumpall (this can take a while on a large database)...");
			return await InstallerProcessHelper.RunShellCommandAsync(
				shell,
				argPrefix,
				$"sudo -u postgres pg_dumpall > \"{target}\"",
				"pg_dumpall failed.");
		}

		/// <summary>Dumps every database with the bundled pg_dumpall.exe.</summary>
		/// <remarks>
		/// The superuser password goes into the child process's environment as <c>PGPASSWORD</c>, not
		/// onto its command line, where <c>tasklist /v</c> would show it — the same reasoning as the
		/// install path's option file.
		/// </remarks>
		private static async Task<bool> RunWindowsDumpAll(string? versionDirectory, string target)
		{
			if (string.IsNullOrWhiteSpace(versionDirectory))
			{
				await Log.Error("FishMMOInstaller", "The PostgreSQL install directory is not known, so pg_dumpall cannot be located.");
				return false;
			}

			string dumpAll = Path.Combine(versionDirectory, "bin", "pg_dumpall.exe");
			if (!File.Exists(dumpAll))
			{
				await Log.Error("FishMMOInstaller", $"pg_dumpall.exe was not found at '{dumpAll}'.");
				return false;
			}

			string superUsername = InstallationConstants.PostgreSQLDefaultSuperuser;
			string password = InstallerProcessHelper.PromptForPassword($"PostgreSQL superuser password (username is '{superUsername}', blank if not required): ");

			Console.WriteLine("Running pg_dumpall (this can take a while on a large database)...");

			var startInfo = new ProcessStartInfo
			{
				FileName = dumpAll,
				Arguments = $"-U {superUsername} -f \"{target}\"",
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardError = true,
			};
			startInfo.Environment["PGPASSWORD"] = password;

			try
			{
				using Process? process = Process.Start(startInfo);
				if (process == null)
				{
					await Log.Error("FishMMOInstaller", "Could not start pg_dumpall.exe.");
					return false;
				}

				string error = await process.StandardError.ReadToEndAsync();
				await process.WaitForExitAsync();

				if (process.ExitCode != 0)
				{
					await Log.Error("FishMMOInstaller", $"pg_dumpall exited with code {process.ExitCode}. {error}");
					return false;
				}

				return true;
			}
			catch (Exception ex)
			{
				await Log.Error("FishMMOInstaller", "pg_dumpall could not be run", ex);
				return false;
			}
		}

		/// <summary>
		/// Runs the plan in order, stopping at the first failure that is not marked survivable, and
		/// reports what happened to each step.
		/// </summary>
		private static async Task ExecutePlan(PostgreSQLUninstallPlan plan, IReadOnlyDictionary<string, PathState> directoryStates)
		{
			(string shell, string argPrefix) = InstallerProcessHelper.GetShellCommand();
			var outcomes = new List<(string Description, string Outcome)>();
			bool aborted = false;

			foreach (UninstallStep step in plan.Steps)
			{
				Console.WriteLine();
				Console.WriteLine($"> {step.Description}");
				Console.WriteLine($"  {step.CommandLine}");

				if (step.Kind == UninstallStepKind.DirectoryRemoval)
				{
					string path = step.TargetPath ?? "";

					// The planner already refused to build a command for a path that fails this, but
					// the check is repeated at the moment of deletion: this is the call that cannot be
					// allowed to run against an empty or unexpected path.
					if (!UninstallPlanner.IsRemovablePath(path, plan.Platform, out string refusal))
					{
						await Log.Error("FishMMOInstaller", $"Refusing to delete '{path}': {refusal}.");
						outcomes.Add((step.Description, $"REFUSED — {refusal}"));
						aborted = true;
						break;
					}

					if (directoryStates.TryGetValue(path, out PathState state) && state == PathState.Absent)
					{
						Console.WriteLine("  Not present — nothing to delete.");
						outcomes.Add((step.Description, "skipped (not present)"));
						continue;
					}
				}

				bool ok;
				if (step.Kind == UninstallStepKind.Executable)
				{
					ok = await RunExecutableStep(step);
				}
				else if (plan.Platform == UninstallPlatform.Windows)
				{
					/* Not RunShellCommandAsync: it escapes embedded quotes as \" for a POSIX shell,
					 * and cmd.exe has no backslash escape — it would take \"postgresql-x64-16\" as a
					 * literal service name with backslashes in it. cmd parses the raw command line,
					 * so the command is handed over exactly as it was shown. */
					ok = await InstallerProcessHelper.RunProcessAsync(
						"cmd.exe",
						$"/c {step.CommandLine}",
						(exitCode, output, error) =>
						{
							if (exitCode != 0)
							{
								_ = Log.Warning("FishMMOInstaller", $"{step.Description} failed (exit {exitCode}). {error}{output}");
								return false;
							}
							return true;
						});
				}
				else
				{
					ok = await InstallerProcessHelper.RunShellCommandAsync(shell, argPrefix, step.CommandLine, $"{step.Description} failed.");
				}

				if (ok)
				{
					Console.WriteLine("  OK.");
					outcomes.Add((step.Description, "done"));
					continue;
				}

				if (step.ContinueOnFailure)
				{
					Console.WriteLine("  Failed, continuing (this step is not required).");
					outcomes.Add((step.Description, "failed (not required)"));
					continue;
				}

				await Log.Error("FishMMOInstaller", $"'{step.Description}' failed. Stopping here rather than running the rest of the uninstall.");
				outcomes.Add((step.Description, "FAILED — stopped here"));
				aborted = true;
				break;
			}

			Console.WriteLine();
			Console.WriteLine("--- Uninstall report ---");
			foreach ((string description, string outcome) in outcomes)
			{
				Console.WriteLine($"  {outcome,-26} {description}");
			}

			foreach (UninstallStep removal in plan.DirectoryRemovals)
			{
				if (removal.TargetPath == null)
				{
					continue;
				}

				PathState after = await ProbeDirectoryAsync(removal.TargetPath, plan.Platform);
				string verdict = after switch
				{
					PathState.Absent => "gone",
					PathState.Present => "STILL PRESENT",
					_ => "unknown (needs root to check)",
				};
				Console.WriteLine($"  {verdict,-26} {removal.TargetPath}");
			}

			bool stillInstalled = await PostgreSQLInstaller.IsPostgreSQLInstalledAsync();
			Console.WriteLine($"  {(stillInstalled ? "STILL PRESENT" : "gone"),-26} PostgreSQL client binaries on PATH");

			Console.WriteLine();
			if (aborted)
			{
				Console.WriteLine("The uninstall stopped early. What is listed above as done has been done; the rest");
				Console.WriteLine("has not. Fix the failure and run this again — the steps are safe to repeat.");
				await Log.Warning("FishMMOInstaller", "PostgreSQL uninstall stopped early; the removal is partial.");
			}
			else if (stillInstalled || plan.DirectoryRemovals.Any(r => r.TargetPath != null && Directory.Exists(r.TargetPath)))
			{
				Console.WriteLine("Every step ran, but something is still on disk (see above). That is a partial");
				Console.WriteLine("uninstall, not a finished one.");
				await Log.Warning("FishMMOInstaller", "PostgreSQL uninstall completed with leftovers.");
			}
			else
			{
				Console.WriteLine("PostgreSQL has been removed.");
				Console.WriteLine("The FishMMO services cannot start until a database exists again: install PostgreSQL,");
				Console.WriteLine("then run Database > Install FishMMO Database.");
				await Log.Info("FishMMOInstaller", "PostgreSQL uninstalled.");
			}
		}

		/// <summary>Runs an executable step elevated, the way the Windows install path runs the installer.</summary>
		private static async Task<bool> RunExecutableStep(UninstallStep step)
		{
			if (string.IsNullOrWhiteSpace(step.ExecutablePath))
			{
				await Log.Error("FishMMOInstaller", "The step has no executable path.");
				return false;
			}

			if (!File.Exists(step.ExecutablePath))
			{
				await Log.Error("FishMMOInstaller", $"'{step.ExecutablePath}' does not exist.");
				return false;
			}

			InstallerProcessHelper.LogElevatedProcessEnvironmentWarning("PostgreSQL uninstaller");
			Console.WriteLine("  Windows will ask for administrator rights; the uninstaller runs without a UI.");

			try
			{
				var startInfo = new ProcessStartInfo
				{
					FileName = step.ExecutablePath,
					Arguments = step.Arguments ?? "",
					CreateNoWindow = true,
					WorkingDirectory = Path.GetDirectoryName(step.ExecutablePath) ?? InstallerProcessHelper.GetWorkingDirectory(),
					UseShellExecute = true,
					Verb = "runas",
				};

				using Process? process = Process.Start(startInfo);
				if (process == null)
				{
					await Log.Error("FishMMOInstaller", "The uninstaller process could not be started (the elevation prompt may have been declined).");
					return false;
				}

				await process.WaitForExitAsync();
				if (process.ExitCode != 0)
				{
					await Log.Error("FishMMOInstaller", $"The uninstaller exited with code {process.ExitCode}. Its log is in the PostgreSQL install directory.");
					return false;
				}

				return true;
			}
			catch (Exception ex)
			{
				await Log.Error("FishMMOInstaller", "The uninstaller could not be run", ex);
				return false;
			}
		}

		/// <summary>
		/// Works out whether a directory is there, asking root only if this user cannot see it.
		/// </summary>
		/// <remarks>
		/// <c>Directory.Exists</c> answers false both for "not there" and for "your user cannot
		/// traverse the parent" — and <c>/var/lib/postgres</c> is mode 700 owned by the postgres user,
		/// so the second case is the normal one here. Reporting that as "not present" would tell the
		/// operator their data was already gone.
		/// </remarks>
		private static async Task<PathState> ProbeDirectoryAsync(string path, UninstallPlatform platform)
		{
			if (string.IsNullOrWhiteSpace(path))
			{
				return PathState.Unknown;
			}

			if (Directory.Exists(path))
			{
				return PathState.Present;
			}

			if (platform == UninstallPlatform.Windows)
			{
				return PathState.Absent;
			}

			// -n so this never blocks the screen on a password prompt.
			bool present = await InstallerProcessHelper.RunProcessAsync(
				"sudo",
				$"-n test -d {path}",
				(exitCode, _, _) => exitCode == 0);

			if (present)
			{
				return PathState.Present;
			}

			bool sudoAnswered = await InstallerProcessHelper.RunProcessAsync(
				"sudo",
				"-n true",
				(exitCode, _, _) => exitCode == 0);

			return sudoAnswered ? PathState.Absent : PathState.Unknown;
		}

		/// <summary>True when this process holds the Administrators role.</summary>
		private static bool IsWindowsElevated()
		{
			if (!OperatingSystem.IsWindows())
			{
				return false;
			}

			try
			{
				using WindowsIdentity identity = WindowsIdentity.GetCurrent();
				return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
			}
			catch
			{
				return false;
			}
		}

		// ──────────────────────────────────────────────────────────────────────
		//  Secrets file
		// ──────────────────────────────────────────────────────────────────────

		/// <summary>
		/// Deletes the secrets and environment file that every FishMMO process reads its database
		/// credentials from.
		/// </summary>
		/// <remarks>
		/// The key names are listed because knowing what has to be re-entered is useful. The values
		/// are never printed: a password or an API key on a terminal ends up in scrollback, in a
		/// screen recording, and in whatever the terminal logs.
		/// </remarks>
		public static async Task DeleteSecretsFile()
		{
			Console.Clear();
			Console.WriteLine("=== Delete Secrets & Environment File ===");
			Console.WriteLine();

			string path = DatabaseSecrets.DefaultSecretsFilePath;

			if (string.IsNullOrWhiteSpace(path) ||
				!string.Equals(Path.GetFileName(path), SecretsFileName, StringComparison.OrdinalIgnoreCase))
			{
				await Log.Error("FishMMOInstaller", $"The resolved secrets path '{path}' is not a {SecretsFileName} file. Refusing to delete it.");
				return;
			}

			Console.WriteLine($"Secrets file: {path}");

			(bool readable, string? content) = await SecretsEnvironmentInstaller.TryReadSecretsFileAsync(path);

			if (readable && content == null)
			{
				Console.WriteLine();
				Console.WriteLine("It is not there. Nothing to delete.");
				await Log.Info("FishMMOInstaller", $"Secrets file {path} does not exist; nothing to delete.");
				return;
			}

			var keys = new List<string>();
			if (!readable)
			{
				Console.WriteLine();
				Console.WriteLine("The file exists but cannot be read by this user, so its keys cannot be listed and");
				Console.WriteLine("no copy can be made from here. Fix its permissions first if you want either.");
			}
			else
			{
				keys = SecretsEnvironmentInstaller.ParseKeyValues(content).Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
				Console.WriteLine();
				Console.WriteLine($"It holds {keys.Count} setting(s). These key names will be lost (values are never shown):");
				foreach (string key in keys)
				{
					Console.WriteLine($"  - {key}");
				}
			}

			Console.WriteLine();
			Console.WriteLine("Every FishMMO process resolves its database credentials from this file: the login,");
			Console.WriteLine("world and scene servers, the web servers, the Control Panel, the Discord bot and the");
			Console.WriteLine("migrator. Without it, and without the same variables set in the environment, they");
			Console.WriteLine("will fail to start. The database itself is untouched — only the credentials for it.");

			if (readable && content != null && InstallerProcessHelper.PromptForYesNo("Save a copy of the file somewhere first?"))
			{
				if (!await SaveSecretsCopy(content))
				{
					await Log.Warning("FishMMOInstaller", "The copy was not written, so nothing has been deleted.");
					return;
				}
			}

			Console.WriteLine();
			string? typed = InstallerProcessHelper.PromptForInput($"Type '{SecretsConfirmationPhrase}' exactly to delete it, or anything else to cancel: ");
			if (typed?.Trim().Equals(SecretsConfirmationPhrase, StringComparison.Ordinal) != true)
			{
				await Log.Info("FishMMOInstaller", "Secrets file deletion cancelled. Nothing was changed.");
				return;
			}

			await DeleteSecretsFileAt(path);
			await DeleteExportedSnippets();
		}

		/// <summary>
		/// Offers to remove the exported copies that KEEP SETTING the variables after the secrets
		/// file is gone.
		/// </summary>
		/// <remarks>
		/// Deleting the secrets file unsets nothing. The installer can also export the same values
		/// as shell snippets, and the fish one lands in <c>conf.d</c>, which every new shell sources
		/// — so an operator who has just "deleted the secrets" can still have a live database
		/// password in the environment of every terminal they open, in a file that now outlives the
		/// database it belonged to. Found by checking a real machine after an uninstall, where
		/// exactly that had happened.
		/// <para>
		/// Each file is confirmed separately and only offered when it exists. The <c>.env</c> export
		/// is deliberately not hunted for: it is written to whatever directory the installer was run
		/// from, which cannot be known later, so it is named as a caveat instead of guessed at.
		/// </para>
		/// </remarks>
		private static async Task DeleteExportedSnippets()
		{
			var present = SecretsEnvironmentInstaller.ExportedSnippetPaths()
				.Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p))
				.ToList();

			Console.WriteLine();
			if (present.Count == 0)
			{
				Console.WriteLine("No exported shell snippets were found in your home directory.");
			}
			else
			{
				Console.WriteLine("These exported copies also DEFINE those variables, and will keep setting them");
				Console.WriteLine("in every new shell until they are removed:");
				foreach (string p in present)
				{
					Console.WriteLine($"  - {p}");
				}

				foreach (string p in present)
				{
					if (!InstallerProcessHelper.PromptForYesNo($"Delete {p}?"))
					{
						await Log.Warning("FishMMOInstaller", $"Left in place: {p} — it still sets the variables.");
						continue;
					}

					try
					{
						File.Delete(p);
						await Log.Info("FishMMOInstaller", $"Deleted {p}");
					}
					catch (Exception ex)
					{
						await Log.Error("FishMMOInstaller", $"Could not delete {p}: {ex.Message}");
					}
				}
			}

			// The credential can also have been pasted into a config file by hand, where deleting
			// the secrets file does not reach it and where staging the tree would commit it.
			var scrubbed = await SecretsEnvironmentInstaller.ClearSmtpCredentialsInConfigsAsync();
			Console.WriteLine();
			if (scrubbed.Count == 0)
			{
				Console.WriteLine("No SMTP credential was found in any configuration file.");
			}
			else
			{
				Console.WriteLine("An SMTP credential was found in these configuration files and has been cleared");
				Console.WriteLine("(the files are kept — they hold the host, port and From address too):");
				foreach (string p in scrubbed)
				{
					Console.WriteLine($"  - {p}");
					await Log.Info("FishMMOInstaller", $"Cleared the SMTP credential in {p}");
				}
			}

			Console.WriteLine();
			Console.WriteLine("Two things this cannot do for you:");
			Console.WriteLine("  - A 'systemd / .env' export went to whatever directory you ran the installer from.");
			Console.WriteLine("    Look for a fishmmo-secrets.env there.");
			Console.WriteLine("  - Variables already exported into THIS shell stay set until you open a new one.");
			Console.WriteLine("    Check with:  env | grep '^FISHMMO_'");
		}

		/// <summary>Writes the secrets to an operator-chosen path, refusing to overwrite anything.</summary>
		private static async Task<bool> SaveSecretsCopy(string content)
		{
			string defaultPath = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
				$"fishmmo-secrets-{DateTime.Now:yyyyMMdd-HHmmss}.env");

			string? input = InstallerProcessHelper.PromptForInput($"Write the copy to (default: {defaultPath}): ");
			string target = string.IsNullOrWhiteSpace(input) ? defaultPath : input.Trim();

			if (File.Exists(target))
			{
				await Log.Error("FishMMOInstaller", $"'{target}' already exists. Refusing to overwrite it.");
				return false;
			}

			try
			{
				string? directory = Path.GetDirectoryName(Path.GetFullPath(target));
				if (!string.IsNullOrWhiteSpace(directory))
				{
					Directory.CreateDirectory(directory);
				}

				await File.WriteAllTextAsync(target, content);

				if (!OperatingSystem.IsWindows())
				{
					try
					{
						File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.UserWrite);
					}
					catch
					{
						// Best effort: the file still inherits the user's umask.
					}
				}

				Console.WriteLine($"Copy written to {target}. It contains the live secrets — treat it as the file itself.");
				await Log.Info("FishMMOInstaller", $"Secrets copied to {target} before deletion.");
				return true;
			}
			catch (Exception ex)
			{
				await Log.Error("FishMMOInstaller", $"Could not write the copy to '{target}': {ex.Message}");
				return false;
			}
		}

		/// <summary>
		/// Deletes the secrets file, elevating on Linux when the directory is root-owned — the same
		/// elevation the write path uses to put the file there.
		/// </summary>
		private static async Task DeleteSecretsFileAt(string path)
		{
			try
			{
				File.Delete(path);
				if (!File.Exists(path))
				{
					Console.WriteLine($"Deleted {path}.");
					await Log.Info("FishMMOInstaller", $"Secrets file {path} deleted.");
					return;
				}
			}
			catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
			{
				// Expected for /etc/fishmmo, which is root-owned. Fall through to sudo.
			}
			catch (Exception ex)
			{
				await Log.Error("FishMMOInstaller", $"Could not delete '{path}'", ex);
				return;
			}

			if (OperatingSystem.IsWindows())
			{
				await Log.Error("FishMMOInstaller",
					$"'{path}' could not be deleted. It is under ProgramData, so re-run this installer as " +
					"administrator, or delete the file from Explorer.");
				return;
			}

			Console.WriteLine();
			Console.WriteLine($"{path} is root-owned; sudo is needed to remove it.");

			(string shell, string argPrefix) = InstallerProcessHelper.GetShellCommand();
			bool removed = await InstallerProcessHelper.RunShellCommandAsync(
				shell,
				argPrefix,
				$"sudo rm -f \"{path}\"",
				$"Could not delete {path}.");

			// Report what is actually true, not what the command returned: File.Exists is false for a
			// file inside a directory this user cannot traverse, so ask root as well.
			bool stillThere = File.Exists(path) || await InstallerProcessHelper.RunProcessAsync(
				"sudo",
				$"-n test -f {path}",
				(exitCode, _, _) => exitCode == 0);

			if (removed && !stillThere)
			{
				Console.WriteLine($"Deleted {path}.");
				await Log.Info("FishMMOInstaller", $"Secrets file {path} deleted via sudo.");
			}
			else
			{
				await Log.Error("FishMMOInstaller", $"'{path}' is still present. It has not been deleted.");
			}
		}
	}
}
