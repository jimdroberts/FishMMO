namespace FishMMO.Installer
{
	/// <summary>The operating system an uninstall plan targets.</summary>
	public enum UninstallPlatform
	{
		/// <summary>Linux: systemd units, a system package manager, and directory removals.</summary>
		Linux,

		/// <summary>Windows: the Windows service controller and EnterpriseDB's own uninstaller.</summary>
		Windows,
	}

	/// <summary>The Linux package managers PostgreSQL can have been installed with.</summary>
	/// <remarks>
	/// The same four <see cref="LinuxPackageManagerHelper"/> detects, in the same order. The install
	/// path chooses package names by manager; the uninstall path has to undo exactly that choice,
	/// so the two lists must stay in step.
	/// </remarks>
	public enum PackageManagerKind
	{
		/// <summary>Arch/CachyOS.</summary>
		Pacman,

		/// <summary>Debian/Ubuntu.</summary>
		AptGet,

		/// <summary>Fedora/RHEL.</summary>
		Dnf,

		/// <summary>Older RHEL.</summary>
		Yum,
	}

	/// <summary>How a planned step is carried out.</summary>
	public enum UninstallStepKind
	{
		/// <summary>A command line for the OS shell. Linux only.</summary>
		Shell,

		/// <summary>An executable run directly, elevated. Windows only.</summary>
		Executable,

		/// <summary>
		/// Removal of one directory. Never runs unless the directory has been displayed to the
		/// operator and confirmed to exist.
		/// </summary>
		DirectoryRemoval,
	}

	/// <summary>One step of an uninstall, in the order it must run.</summary>
	/// <param name="Kind">How the step is carried out.</param>
	/// <param name="Description">What the step does, in the operator's terms.</param>
	/// <param name="CommandLine">
	/// The exact command line, as it is shown to the operator and as it is run. For
	/// <see cref="UninstallStepKind.Executable"/> this is the quoted executable path followed by its
	/// arguments; <see cref="ExecutablePath"/> and <see cref="Arguments"/> split the same thing.
	/// </param>
	/// <param name="TargetPath">The directory a <see cref="UninstallStepKind.DirectoryRemoval"/> removes.</param>
	/// <param name="ExecutablePath">The executable an <see cref="UninstallStepKind.Executable"/> step runs.</param>
	/// <param name="Arguments">Arguments for <see cref="ExecutablePath"/>.</param>
	/// <param name="ContinueOnFailure">
	/// True for steps whose failure is not a reason to abandon the uninstall — stopping a service
	/// that is already stopped, for instance. Everything else stops the run.
	/// </param>
	public sealed record UninstallStep(
		UninstallStepKind Kind,
		string Description,
		string CommandLine,
		string? TargetPath = null,
		string? ExecutablePath = null,
		string? Arguments = null,
		bool ContinueOnFailure = false);

	/// <summary>An ordered, fully resolved uninstall. Built without touching anything.</summary>
	/// <param name="Platform">The OS the steps are written for.</param>
	/// <param name="TargetDescription">What is being removed, for the confirmation screen.</param>
	/// <param name="Steps">Every step, in the order it must run.</param>
	public sealed record PostgreSQLUninstallPlan(
		UninstallPlatform Platform,
		string TargetDescription,
		IReadOnlyList<UninstallStep> Steps)
	{
		/// <summary>The directory removals in this plan, in order.</summary>
		public IReadOnlyList<UninstallStep> DirectoryRemovals =>
			Steps.Where(s => s.Kind == UninstallStepKind.DirectoryRemoval).ToList();
	}

	/// <summary>
	/// Builds the uninstall plan for PostgreSQL. Pure: it reads nothing, runs nothing, and deletes
	/// nothing.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The planning is separated from the doing on purpose. Every command and every path this
	/// installer would run <c>rm -rf</c> or an unattended uninstaller over is decided here, by
	/// functions that can be called and asserted without a PostgreSQL anywhere near them — and the
	/// same list is what the operator is shown before being asked to confirm. A plan that is shown
	/// and a plan that is run cannot drift apart, because they are the same object.
	/// </para>
	/// <para>
	/// <b>The two platforms never mix.</b> A Linux plan contains no uninstaller executable and a
	/// Windows plan contains no <c>sudo</c>, <c>systemctl</c> or <c>rm -rf</c>. Distribution layouts
	/// differ too: <c>/etc/postgresql/</c> is the Debian layout and does not exist on Arch, where the
	/// configuration lives inside the data directory, so it appears only in the apt-get plan.
	/// </para>
	/// </remarks>
	public static class UninstallPlanner
	{
		/// <summary>PostgreSQL's own systemd unit name on every supported distribution.</summary>
		public const string LinuxServiceName = "postgresql";

		/// <summary>Where EnterpriseDB's one-click installer puts PostgreSQL by default.</summary>
		/// <remarks>
		/// The install path (<c>InstallPostgreSQLWindows</c>) accepts the installer's default prefix,
		/// which is this directory with a version-numbered subdirectory under it.
		/// </remarks>
		public const string WindowsDefaultInstallRoot = @"C:\Program Files\PostgreSQL";

		/// <summary>The uninstaller EnterpriseDB leaves in each version directory.</summary>
		public const string WindowsUninstallerFileName = "uninstall-postgresql.exe";

		/// <summary>
		/// Directories that must never be handed to a recursive delete, whatever else says otherwise.
		/// </summary>
		/// <summary>
		/// Final path segments that name a CONTAINER of installations rather than an installation.
		/// </summary>
		/// <remarks>
		/// The Windows counterpart of <c>LinuxForbiddenPaths</c>. Deleting one of these takes every
		/// PostgreSQL version on the machine, including any this installer never put there.
		/// </remarks>
		private static readonly string[] WindowsContainerSegments = { "PostgreSQL", "Program Files", "Program Files (x86)" };

		private static readonly string[] LinuxForbiddenPaths =
		{
			"/", "/bin", "/boot", "/dev", "/etc", "/home", "/lib", "/lib64", "/media", "/mnt",
			"/opt", "/proc", "/root", "/run", "/sbin", "/srv", "/sys", "/tmp", "/usr", "/var",
			"/var/lib", "/var/log", "/var/run", "/usr/bin", "/usr/lib", "/usr/local", "/usr/share",
		};

		/// <summary>
		/// Roots a PostgreSQL directory removal is allowed to live under on Linux. Anything else is
		/// refused, however plausible it looks.
		/// </summary>
		private static readonly string[] LinuxAllowedRoots = { "/var/lib/", "/etc/" };

		// ──────────────────────────────────────────────────────────────────────
		//  Package manager identity
		// ──────────────────────────────────────────────────────────────────────

		/// <summary>
		/// Maps the human-readable name <see cref="LinuxPackageManagerHelper.DetectAsync"/> reports
		/// onto the kind the plan is built from.
		/// </summary>
		/// <param name="managerName">For example <c>"pacman (Arch/CachyOS)"</c>.</param>
		/// <param name="kind">The matching kind when this returns true.</param>
		/// <returns>True when the name is recognised.</returns>
		public static bool TryResolvePackageManagerKind(string? managerName, out PackageManagerKind kind)
		{
			kind = default;
			if (string.IsNullOrWhiteSpace(managerName))
			{
				return false;
			}

			/* Ordered like DetectAsync: apt-get before dnf/yum matters on a machine that has more
			 * than one of them installed, and the detector already made that choice. */
			if (managerName.Contains("pacman", StringComparison.OrdinalIgnoreCase)) { kind = PackageManagerKind.Pacman; return true; }
			if (managerName.Contains("apt-get", StringComparison.OrdinalIgnoreCase)) { kind = PackageManagerKind.AptGet; return true; }
			if (managerName.Contains("dnf", StringComparison.OrdinalIgnoreCase)) { kind = PackageManagerKind.Dnf; return true; }
			if (managerName.Contains("yum", StringComparison.OrdinalIgnoreCase)) { kind = PackageManagerKind.Yum; return true; }

			return false;
		}

		/// <summary>
		/// The package names the install path installs, keyed by manager. Passed to
		/// <see cref="LinuxPackageManagerHelper.DetectAsync"/> so detection here picks the same
		/// manager the install picked.
		/// </summary>
		public static Dictionary<string, string> PostgreSQLPackageNames() => new()
		{
			["pacman"] = "postgresql",
			["apt-get"] = "postgresql postgresql-contrib",
			["dnf"] = "postgresql-server postgresql-contrib",
			["yum"] = "postgresql-server postgresql-contrib",
		};

		/// <summary>The command that removes the PostgreSQL packages for the given manager.</summary>
		public static string PackageRemovalCommand(PackageManagerKind kind) => kind switch
		{
			PackageManagerKind.Pacman => "sudo pacman -Rns --noconfirm postgresql",
			PackageManagerKind.AptGet => "sudo apt-get purge -y postgresql postgresql-contrib",
			PackageManagerKind.Dnf => "sudo dnf remove -y postgresql-server postgresql-contrib",
			PackageManagerKind.Yum => "sudo yum remove -y postgresql-server postgresql-contrib",
			_ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown package manager."),
		};

		/// <summary>
		/// The directories PostgreSQL's data and configuration live in for the given manager, in
		/// removal order.
		/// </summary>
		/// <remarks>
		/// Only Debian keeps configuration in <c>/etc/postgresql/</c>. On Arch and on RHEL the
		/// configuration files live inside the data directory, so naming <c>/etc/postgresql</c> there
		/// would be a recursive delete aimed at a directory the distribution never created.
		/// </remarks>
		public static IReadOnlyList<(string Path, string What)> DataAndConfigDirectories(PackageManagerKind kind) => kind switch
		{
			PackageManagerKind.Pacman => new[]
			{
				("/var/lib/postgres/data", "cluster data directory (configuration lives inside it on Arch)"),
			},
			PackageManagerKind.AptGet => new[]
			{
				("/etc/postgresql", "configuration directory (Debian layout)"),
				("/var/lib/postgresql", "cluster data directory"),
			},
			PackageManagerKind.Dnf or PackageManagerKind.Yum => new[]
			{
				("/var/lib/pgsql", "cluster data directory (configuration lives inside it)"),
			},
			_ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown package manager."),
		};

		// ──────────────────────────────────────────────────────────────────────
		//  Path guards
		// ──────────────────────────────────────────────────────────────────────

		/// <summary>
		/// Decides whether a path may be handed to a recursive delete.
		/// </summary>
		/// <remarks>
		/// The accident this exists to prevent is an unset or empty value interpolated into a delete:
		/// an empty version turning <c>C:\Program Files\PostgreSQL\&lt;version&gt;\data</c> into
		/// <c>C:\Program Files\PostgreSQL</c>, or an empty path turning <c>rm -rf $DIR</c> into
		/// <c>rm -rf</c> with whatever the shell puts next. Nothing is deleted without passing here
		/// first, and this refuses on the safe side.
		/// </remarks>
		/// <param name="path">Candidate directory.</param>
		/// <param name="platform">Which layout rules apply.</param>
		/// <param name="refusal">Why it was refused, when this returns false.</param>
		/// <returns>True when the path is specific enough to delete.</returns>
		public static bool IsRemovablePath(string? path, UninstallPlatform platform, out string refusal)
		{
			if (string.IsNullOrWhiteSpace(path))
			{
				refusal = "the path is empty — refusing to delete anything";
				return false;
			}

			string candidate = path.Trim();

			if (candidate.Contains("..", StringComparison.Ordinal))
			{
				refusal = "the path contains '..'";
				return false;
			}

			if (candidate.Contains('*') || candidate.Contains('?'))
			{
				refusal = "the path contains a wildcard";
				return false;
			}

			if (platform == UninstallPlatform.Linux)
			{
				if (!candidate.StartsWith('/'))
				{
					refusal = "the path is not absolute";
					return false;
				}

				string normalized = candidate.TrimEnd('/');
				if (normalized.Length == 0)
				{
					refusal = "the path is the filesystem root";
					return false;
				}

				if (normalized.Contains("//", StringComparison.Ordinal))
				{
					refusal = "the path has an empty segment, which usually means a variable was unset";
					return false;
				}

				if (LinuxForbiddenPaths.Contains(normalized, StringComparer.Ordinal))
				{
					refusal = $"'{normalized}' is a system directory";
					return false;
				}

				if (!LinuxAllowedRoots.Any(root => normalized.StartsWith(root, StringComparison.Ordinal)))
				{
					refusal = $"'{normalized}' is not under {string.Join(" or ", LinuxAllowedRoots)}, where PostgreSQL keeps its data";
					return false;
				}

				refusal = string.Empty;
				return true;
			}

			// Windows: a drive-qualified path with at least two segments below the drive.
			if (candidate.Length < 4 || candidate[1] != ':' || (candidate[2] != '\\' && candidate[2] != '/'))
			{
				refusal = "the path is not a drive-qualified Windows path";
				return false;
			}

			string tail = candidate[3..].Replace('/', '\\').TrimEnd('\\');
			string[] segments = tail.Split('\\');
			if (segments.Length < 2 || segments.Any(string.IsNullOrWhiteSpace))
			{
				refusal = segments.Any(string.IsNullOrWhiteSpace)
					? "the path has an empty segment, which usually means a value was unset"
					: "the path is too close to the drive root";
				return false;
			}

			/* Refuse the directory that HOLDS installations rather than being one. Windows had
			 * no equivalent of the Linux forbidden list, so a two-segment rule alone accepted
			 * "C:\Program Files\PostgreSQL" — the parent of every installed version, and a
			 * recursive delete there takes all of them, including ones this installer never
			 * touched. Everything this planner removes is at least a version below that root, so
			 * refusing the root itself costs nothing and is exact. */
			if (WindowsContainerSegments.Contains(segments[^1], StringComparer.OrdinalIgnoreCase))
			{
				refusal = $"'{candidate}' is the directory that holds PostgreSQL installations, not one of them";
				return false;
			}

			refusal = string.Empty;
			return true;
		}

		/// <summary>
		/// The Linux command that removes one directory, with its own existence test built in.
		/// </summary>
		/// <remarks>
		/// The <c>if test -d</c> is not redundant with the caller's existence check. The caller often
		/// cannot see inside <c>/var/lib/postgres</c> (mode 700, owned by postgres) without root, so
		/// its answer can be "unknown"; this form deletes only what is actually there and exits 0
		/// either way, so an absent directory reads as "nothing to remove" rather than a failure that
		/// aborts the run.
		/// </remarks>
		/// <param name="path">Directory to remove. Must have passed <see cref="IsRemovablePath"/>.</param>
		public static string LinuxDirectoryRemovalCommand(string path)
		{
			if (!IsRemovablePath(path, UninstallPlatform.Linux, out string refusal))
			{
				throw new ArgumentException($"Refusing to build a delete command: {refusal}.", nameof(path));
			}

			string safe = path.Trim().TrimEnd('/');
			return $"sudo sh -c 'if test -d \"{safe}\"; then rm -rf \"{safe}\"; else echo \"{safe} is not present\"; fi'";
		}

		/// <summary>The Windows command that removes one leftover directory.</summary>
		public static string WindowsDirectoryRemovalCommand(string path)
		{
			if (!IsRemovablePath(path, UninstallPlatform.Windows, out string refusal))
			{
				throw new ArgumentException($"Refusing to build a delete command: {refusal}.", nameof(path));
			}

			return $"rmdir /s /q \"{path.Trim().TrimEnd('\\')}\"";
		}

		// ──────────────────────────────────────────────────────────────────────
		//  Plans
		// ──────────────────────────────────────────────────────────────────────

		/// <summary>
		/// Builds the Linux uninstall: stop, disable, remove the packages, then remove the data and
		/// configuration directories — in that order, because each step depends on the one before it.
		/// </summary>
		/// <param name="kind">The detected package manager.</param>
		/// <param name="dependantUnitsToStop">
		/// FishMMO and pooler units to stop first. Only units observed to be running should be listed;
		/// their failure does not abort the run.
		/// </param>
		public static PostgreSQLUninstallPlan BuildLinuxPlan(
			PackageManagerKind kind,
			IReadOnlyList<string>? dependantUnitsToStop = null)
		{
			var steps = new List<UninstallStep>();

			foreach (string unit in dependantUnitsToStop ?? Array.Empty<string>())
			{
				if (string.IsNullOrWhiteSpace(unit))
				{
					continue;
				}

				steps.Add(new UninstallStep(
					UninstallStepKind.Shell,
					$"Stop {unit} (it will lose its database)",
					$"sudo systemctl stop {unit.Trim()}",
					ContinueOnFailure: true));
			}

			steps.Add(new UninstallStep(
				UninstallStepKind.Shell,
				"Stop the PostgreSQL server",
				$"sudo systemctl stop {LinuxServiceName}"));

			steps.Add(new UninstallStep(
				UninstallStepKind.Shell,
				"Disable PostgreSQL so it does not start on boot",
				$"sudo systemctl disable {LinuxServiceName}"));

			steps.Add(new UninstallStep(
				UninstallStepKind.Shell,
				"Remove the PostgreSQL packages",
				PackageRemovalCommand(kind)));

			foreach ((string path, string what) in DataAndConfigDirectories(kind))
			{
				steps.Add(new UninstallStep(
					UninstallStepKind.DirectoryRemoval,
					$"Delete the {what}",
					LinuxDirectoryRemovalCommand(path),
					TargetPath: path));
			}

			return new PostgreSQLUninstallPlan(
				UninstallPlatform.Linux,
				$"PostgreSQL installed by {kind}",
				steps);
		}

		/// <summary>
		/// Builds the Windows uninstall around EnterpriseDB's own uninstaller, which is the supported
		/// way to undo the one-click installer the install path runs.
		/// </summary>
		/// <remarks>
		/// Deleting the program files by hand would leave the registry entries and the service behind.
		/// The uninstaller removes both; what it routinely leaves is the <c>data</c> directory, which
		/// is why that is a separate, separately confirmed step.
		/// </remarks>
		/// <param name="installRoot">
		/// The directory holding the version subdirectories, normally
		/// <see cref="WindowsDefaultInstallRoot"/>.
		/// </param>
		/// <param name="version">The installed version directory name, for example <c>16</c>.</param>
		/// <param name="serviceName">
		/// The Windows service name, when it was discovered. Defaults to EnterpriseDB's
		/// <c>postgresql-x64-&lt;version&gt;</c>.
		/// </param>
		/// <param name="dependantServicesToStop">FishMMO services observed running, stopped first.</param>
		/// <exception cref="ArgumentException">
		/// If the version or install root is empty or malformed. An empty version is the accident this
		/// throws for: it would aim the leftover-data step at the whole PostgreSQL directory.
		/// </exception>
		public static PostgreSQLUninstallPlan BuildWindowsPlan(
			string installRoot,
			string version,
			string? serviceName = null,
			IReadOnlyList<string>? dependantServicesToStop = null)
		{
			if (string.IsNullOrWhiteSpace(installRoot))
			{
				throw new ArgumentException("The PostgreSQL install root is empty.", nameof(installRoot));
			}

			if (string.IsNullOrWhiteSpace(version))
			{
				throw new ArgumentException("The PostgreSQL version is empty; without it every resolved path would point at the parent directory.", nameof(version));
			}

			string trimmedVersion = version.Trim();
			if (trimmedVersion.Contains('\\') || trimmedVersion.Contains('/') || trimmedVersion.Contains("..", StringComparison.Ordinal))
			{
				throw new ArgumentException($"'{version}' is not a version directory name.", nameof(version));
			}

			/* Built with explicit backslashes rather than Path.Combine: this plan describes a Windows
			 * machine and must read identically wherever it is built, including when it is asserted
			 * from a Linux build host. */
			string root = installRoot.Trim().TrimEnd('\\', '/');
			string versionDirectory = $@"{root}\{trimmedVersion}";
			string uninstaller = $@"{versionDirectory}\{WindowsUninstallerFileName}";
			string dataDirectory = $@"{versionDirectory}\data";
			string service = string.IsNullOrWhiteSpace(serviceName) ? $"postgresql-x64-{trimmedVersion}" : serviceName.Trim();

			var steps = new List<UninstallStep>();

			foreach (string dependant in dependantServicesToStop ?? Array.Empty<string>())
			{
				if (string.IsNullOrWhiteSpace(dependant))
				{
					continue;
				}

				steps.Add(new UninstallStep(
					UninstallStepKind.Shell,
					$"Stop {dependant} (it will lose its database)",
					$"net stop \"{dependant.Trim()}\"",
					ContinueOnFailure: true));
			}

			steps.Add(new UninstallStep(
				UninstallStepKind.Shell,
				"Stop the PostgreSQL service",
				$"net stop \"{service}\"",
				ContinueOnFailure: true));

			steps.Add(new UninstallStep(
				UninstallStepKind.Executable,
				"Run EnterpriseDB's uninstaller (removes the service and the program files)",
				$"\"{uninstaller}\" --mode unattended",
				ExecutablePath: uninstaller,
				Arguments: "--mode unattended"));

			steps.Add(new UninstallStep(
				UninstallStepKind.Shell,
				"Delete the service registration, if the uninstaller left it behind",
				$"sc.exe delete \"{service}\"",
				ContinueOnFailure: true));

			steps.Add(new UninstallStep(
				UninstallStepKind.DirectoryRemoval,
				"Delete the data directory the uninstaller leaves behind",
				WindowsDirectoryRemovalCommand(dataDirectory),
				TargetPath: dataDirectory));

			return new PostgreSQLUninstallPlan(
				UninstallPlatform.Windows,
				$"PostgreSQL {trimmedVersion} in {versionDirectory}",
				steps);
		}
	}
}
