namespace FishMMO.Patcher
{
	/// <summary>
	/// Thrown when a manifest-supplied path would resolve outside the install root.
	/// </summary>
	/// <remarks>
	/// A distinct exception type rather than a bool return at every call site. The patch
	/// application flow already treats a thrown exception as "roll back and fail the whole
	/// patch", which is exactly the required behaviour: silently skipping a rejected entry
	/// would let a hostile manifest suppress individual files (say, a security fix) while the
	/// rest of the patch still applied and the client still reported itself updated.
	/// </remarks>
	public sealed class PathContainmentException : System.Exception
	{
		/// <summary>The manifest-supplied path that was rejected, verbatim.</summary>
		public string OffendingPath { get; }

		public PathContainmentException(string offendingPath, string reason)
			: base($"Refusing manifest path '{offendingPath}': {reason}")
		{
			OffendingPath = offendingPath;
		}
	}

	/// <summary>
	/// Resolves a manifest-supplied relative path against the install root and proves the
	/// result stays inside it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This is the fix for the archive-extraction traversal ("zip slip") class of bug. Every
	/// path in a patch manifest is attacker-controlled the moment the patch host is spoofed or
	/// compromised, and <see cref="System.IO.Path.Combine(string,string)"/> is not a security
	/// boundary: it happily returns the second argument outright when that argument is rooted,
	/// and it does not collapse <c>..</c> at all. So
	/// <c>Path.Combine(installRoot, "../../.bashrc")</c> and
	/// <c>Path.Combine(installRoot, "/etc/cron.d/x")</c> both escape, and the updater writes,
	/// overwrites, or DELETES whatever they name — with whatever privileges the player's
	/// install runs under.
	/// </para>
	/// <para>
	/// The delete path matters as much as the write path. An unchecked delete is arbitrary file
	/// deletion, which needs no code execution to be destructive, so every site that turns a
	/// manifest string into a filesystem path goes through here — new files, patched files,
	/// deletions, and the directory pre-creation pass that runs ahead of all three.
	/// </para>
	/// <para>
	/// The checks are deliberately layered and deliberately strict. Each one alone has a known
	/// bypass: a textual <c>..</c> scan misses <c>C:\</c>, a prefix comparison against the
	/// resolved path misses a symlinked subdirectory, and rejecting rooted paths misses
	/// <c>..\</c> on a manifest authored on Windows and applied on Linux (where <c>\</c> is a
	/// legal filename character rather than a separator, so <see cref="System.IO.Path"/> would
	/// not split on it at all). Together they leave no gap, and the cost is a few string
	/// operations per manifest entry.
	/// </para>
	/// </remarks>
	public static class PathContainment
	{
		/// <summary>Both separators, because a manifest may have been authored on either OS.</summary>
		private static readonly char[] SeparatorChars = { '/', '\\' };

		/// <summary>
		/// Path comparison is case-insensitive only where the filesystem is. Using
		/// OrdinalIgnoreCase everywhere would let <c>/ROOT/x</c> pass a containment check
		/// against <c>/root</c> on Linux, where those are two different directories.
		/// </summary>
		private static System.StringComparison PathComparison =>
			System.OperatingSystem.IsWindows() || System.OperatingSystem.IsMacOS()
				? System.StringComparison.OrdinalIgnoreCase
				: System.StringComparison.Ordinal;

		/// <summary>
		/// Resolves <paramref name="relativePath"/> under <paramref name="rootDirectory"/>.
		/// </summary>
		/// <param name="rootDirectory">The install root. Must be an existing, rooted directory.</param>
		/// <param name="relativePath">The manifest-supplied path, which is untrusted.</param>
		/// <param name="fullPath">The resolved absolute path when the call returns true.</param>
		/// <param name="rejectionReason">Why the path was refused, when the call returns false.</param>
		/// <returns>True when the path is safe to act on.</returns>
		public static bool TryResolve(string rootDirectory, string relativePath, out string fullPath, out string rejectionReason)
		{
			// Assigned rather than left null so callers under nullable-enabled compilation get a
			// non-null string on every path; the bool is the answer, not the strings.
			fullPath = string.Empty;
			rejectionReason = string.Empty;

			if (string.IsNullOrWhiteSpace(rootDirectory))
			{
				rejectionReason = "the install root is not set";
				return false;
			}
			if (string.IsNullOrWhiteSpace(relativePath))
			{
				rejectionReason = "the path is empty";
				return false;
			}

			// A NUL truncates the path at the syscall boundary on POSIX, so "safe.txt\0../../evil"
			// can pass a managed string check and name a different file to the kernel.
			if (relativePath.IndexOf('\0') >= 0)
			{
				rejectionReason = "the path contains a NUL character";
				return false;
			}

			// Absolute, drive-qualified, UNC and device paths all defeat Path.Combine outright:
			// it discards the root and returns the second argument. Checked against BOTH
			// platforms' notions of "rooted", because the manifest is not necessarily authored
			// on the platform applying it.
			if (System.IO.Path.IsPathRooted(relativePath) ||
				relativePath[0] == '/' || relativePath[0] == '\\')
			{
				rejectionReason = "the path is absolute";
				return false;
			}
			if (relativePath.Length >= 2 &&
				relativePath[1] == ':' &&
				char.IsLetter(relativePath[0]))
			{
				// "C:x" is drive-RELATIVE on Windows: it resolves against that drive's own
				// current directory, which is not the install root.
				rejectionReason = "the path is drive-qualified";
				return false;
			}

			// Segment scan. This is what catches "..\" on Linux, where Path.GetFullPath would
			// treat the backslash as an ordinary character and the containment check below
			// would pass a path that escapes once the archive is applied on Windows.
			string[] segments = relativePath.Split(SeparatorChars);
			for (int i = 0; i < segments.Length; ++i)
			{
				string segment = segments[i];
				if (segment.Length == 0)
				{
					// "a//b" — harmless, and normalised away below.
					continue;
				}
				if (segment == ".")
				{
					continue;
				}
				if (segment == "..")
				{
					rejectionReason = "the path contains a '..' segment";
					return false;
				}
				// Windows silently strips trailing dots and spaces from a component, so
				// "config." and "config" name the same file — a way to write a path that
				// looks new but overwrites an existing one. Also rejects the reserved-name
				// shapes that end in a dot.
				char last = segment[segment.Length - 1];
				if (last == '.' || last == ' ')
				{
					rejectionReason = "a path segment ends with '.' or a space";
					return false;
				}
				// An NTFS alternate data stream rides along on an otherwise innocuous name.
				if (segment.IndexOf(':') >= 0)
				{
					rejectionReason = "a path segment contains ':'";
					return false;
				}
			}

			string root;
			try
			{
				root = CanonicalizeRoot(rootDirectory);
			}
			catch (System.Exception ex)
			{
				rejectionReason = $"the install root could not be resolved ({ex.Message})";
				return false;
			}

			string rootWithSeparator = root.EndsWith(System.IO.Path.DirectorySeparatorChar)
				? root
				: root + System.IO.Path.DirectorySeparatorChar;

			string combined;
			try
			{
				// Normalise the manifest's separators to this platform's before combining, so a
				// Windows-authored "Managed\\x.dll" becomes a real subdirectory on Linux rather
				// than a single file with a backslash in its name.
				string normalized = relativePath
					.Replace('\\', System.IO.Path.DirectorySeparatorChar)
					.Replace('/', System.IO.Path.DirectorySeparatorChar);

				combined = System.IO.Path.GetFullPath(System.IO.Path.Combine(rootWithSeparator, normalized));
			}
			catch (System.Exception ex)
			{
				rejectionReason = $"the path could not be resolved ({ex.Message})";
				return false;
			}

			// The authoritative check. Everything above narrows the input; this proves the
			// result. Compared against root + separator so that a sibling directory sharing a
			// name prefix ("/opt/fishmmo-evil" against a root of "/opt/fishmmo") does not pass.
			if (!combined.StartsWith(rootWithSeparator, PathComparison))
			{
				rejectionReason = $"it resolves to '{combined}', outside the install root '{root}'";
				return false;
			}
			if (combined.Length <= rootWithSeparator.Length)
			{
				rejectionReason = "it resolves to the install root itself";
				return false;
			}

			// Last: a symlink anywhere along the resolved path defeats every textual check,
			// because Path.GetFullPath does not follow links. A patch that first writes
			// "Data/logs" as a link to /etc and then writes "Data/logs/cron.d/x" would pass
			// containment on both entries and still escape.
			if (TryFindLinkComponent(rootWithSeparator, combined, out string link))
			{
				rejectionReason = $"'{link}' is a symbolic link or reparse point";
				return false;
			}

			fullPath = combined;
			return true;
		}

		/// <summary>
		/// <see cref="TryResolve"/>, throwing <see cref="PathContainmentException"/> on refusal.
		/// </summary>
		/// <remarks>
		/// The throwing form is what the patch phases use: it fails the whole patch and triggers
		/// the existing rollback, rather than skipping the offending entry and committing a
		/// partially-applied update the manifest author chose the shape of.
		/// </remarks>
		public static string ResolveOrThrow(string rootDirectory, string relativePath)
		{
			if (!TryResolve(rootDirectory, relativePath, out string fullPath, out string reason))
			{
				throw new PathContainmentException(relativePath ?? "<null>", reason);
			}
			return fullPath;
		}

		/// <summary>
		/// Resolves the install root to a real, link-free absolute path.
		/// </summary>
		/// <remarks>
		/// The root itself is allowed to be reached through a symlink — plenty of installs live
		/// under one — but it has to be resolved before the prefix comparison, or every child
		/// path (which <see cref="System.IO.Path.GetFullPath"/> does not resolve links for)
		/// would fail to match a root that does.
		/// </remarks>
		private static string CanonicalizeRoot(string rootDirectory)
		{
			string full = System.IO.Path.GetFullPath(rootDirectory);

			try
			{
				var info = new System.IO.DirectoryInfo(full);
				System.IO.FileSystemInfo? target = info.ResolveLinkTarget(returnFinalTarget: true);
				if (target != null)
				{
					full = System.IO.Path.GetFullPath(target.FullName);
				}
			}
			catch (System.IO.IOException)
			{
				// Not a link, or the link is broken. Either way the unresolved path is the
				// best answer available and the containment check below still applies.
			}
			catch (System.UnauthorizedAccessException)
			{
			}

			// Trim a trailing separator so the caller can append exactly one. Guarded because
			// trimming "/" or "C:\" would leave an empty string that matches every prefix test.
			string trimmed = full.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
			return trimmed.Length == 0 ? full : trimmed;
		}

		/// <summary>
		/// Walks each component of <paramref name="fullPath"/> below
		/// <paramref name="rootWithSeparator"/> looking for one that already exists and is a
		/// symbolic link / reparse point.
		/// </summary>
		/// <param name="link">The offending component when the method returns true.</param>
		/// <returns>True when a link was found, which means the path must be refused.</returns>
		private static bool TryFindLinkComponent(string rootWithSeparator, string fullPath, out string link)
		{
			link = string.Empty;

			string relative = fullPath.Substring(rootWithSeparator.Length);
			string current = rootWithSeparator.TrimEnd(System.IO.Path.DirectorySeparatorChar);

			foreach (string segment in relative.Split(System.IO.Path.DirectorySeparatorChar))
			{
				if (segment.Length == 0)
				{
					continue;
				}

				current = System.IO.Path.Combine(current, segment);

				try
				{
					// Directory first: a link to a directory reports as both, and DirectoryInfo
					// is the one that resolves it.
					if (System.IO.Directory.Exists(current))
					{
						if (new System.IO.DirectoryInfo(current).LinkTarget != null)
						{
							link = current;
							return true;
						}
						continue;
					}

					var file = new System.IO.FileInfo(current);
					if (file.Exists && file.LinkTarget != null)
					{
						link = current;
						return true;
					}
				}
				catch (System.Exception)
				{
					// An unreadable component cannot be proven safe, so it is not treated as
					// safe. Refusing here costs a failed patch; assuming here costs containment.
					link = current;
					return true;
				}
			}

			return false;
		}
	}
}
