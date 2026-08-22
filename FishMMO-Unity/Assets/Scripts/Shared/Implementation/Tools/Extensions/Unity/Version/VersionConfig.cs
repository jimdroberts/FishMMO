using UnityEngine;
using System;
using System.Text.RegularExpressions;

namespace FishMMO.Shared
{
	/// <summary>
	/// ScriptableObject holding semantic versioning information for FishMMO builds.
	/// Supports parsing, comparison, and equality operations.
	/// </summary>
	[CreateAssetMenu(fileName = "VersionConfig", menuName = "FishMMO/Version/Version Configuration")]
	public class VersionConfig : ScriptableObject, IComparable<VersionConfig>
	{
		/// <summary>
		/// Major version: Incremented for incompatible API changes or major features.
		/// </summary>
		[Tooltip("Major version: Incremented for incompatible API changes, Major features.")]
		public int Major = 0;

		/// <summary>
		/// Minor version: Incremented for new, backward-compatible functionality.
		/// </summary>
		[Tooltip("Minor version: Incremented for new, backward-compatible functionality.")]
		public int Minor = 0;

		/// <summary>
		/// Patch version: Incremented for backward-compatible bug fixes.
		/// </summary>
		[Tooltip("Patch version: Incremented for backward-compatible bug fixes.")]
		public int Patch = 0;

		/// <summary>
		/// Optional pre-release identifier (e.g., "alpha", "beta", "rc.1").
		/// </summary>
		[Tooltip("Optional: Pre-release identifier (e.g., 'alpha', 'beta', 'rc.1').")]
		public string PreRelease = "";

		/// <summary>
		/// Returns the full version string in the format Major.Minor.Patch[.PreRelease].
		/// </summary>
		public string FullVersion
		{
			get
			{
				string version = $"{Major}.{Minor}.{Patch}";
				if (!string.IsNullOrEmpty(PreRelease))
				{
					version += $".{PreRelease}";
				}
				return version;
			}
		}

		/// <summary>
		/// Longest version string this will even attempt to parse.
		/// </summary>
		/// <remarks>
		/// A version arrives from the update server, so its length is not something this client
		/// controls. The numeric groups are bounded by <see cref="int"/> anyway, but the
		/// pre-release group is a character class over an unbounded run, and the result is
		/// interpolated into file names, log lines and UI labels. A real semantic version is
		/// tens of characters.
		/// </remarks>
		private const int MaxVersionStringLength = 64;

		/// <summary>
		/// Longest pre-release identifier accepted.
		/// </summary>
		private const int MaxPreReleaseLength = 32;

		/// <summary>
		/// Version grammar: <c>Major.Minor.Patch</c> with an optional dot-separated pre-release.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The pre-release group used to be <c>(.+)</c>. That is not a version grammar — it is
		/// "anything at all", and the parsed value does not stay inside this class. A server that
		/// answers <c>/latest_version</c> with <c>1.0.0../../../../etc/cron.d/x</c> parses
		/// successfully, <see cref="FullVersion"/> reproduces it verbatim, and it reaches
		/// <c>Path.Combine</c> in the launcher's patch-download path via
		/// <c>Constants.GetPatchFileName</c> — so the "patch archive" is written wherever the
		/// server chose. The attacker also supplies the matching SHA-256, so the integrity check
		/// passes and the file is kept.
		/// </para>
		/// <para>
		/// So the group is now a real SemVer pre-release charset: alphanumerics, hyphen, and dot
		/// as an identifier separator. That excludes every path separator, every drive
		/// qualifier, and the whole of <c>..</c>-as-a-segment (a lone <c>.</c> cannot start or
		/// end an identifier, and two dots cannot be adjacent). It is deliberately the narrow
		/// half of the defence-in-depth pair — <c>Constants.GetPatchFileName</c> validates the
		/// constructed file name independently, because a grammar fix in one file is exactly the
		/// kind of thing a later "let's allow build metadata" commit relaxes without noticing
		/// what depends on it.
		/// </para>
		/// </remarks>
		private static readonly Regex VersionPattern = new Regex(
			@"^(\d{1,9})\.(\d{1,9})\.(\d{1,9})(?:\.([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$",
			RegexOptions.CultureInvariant);

		/// <summary>
		/// Parses a version string (e.g., "1.2.3" or "1.2.3.alpha") into a new VersionConfig instance.
		/// </summary>
		/// <param name="versionString">The version string to parse.</param>
		/// <returns>A new VersionConfig instance populated with the parsed version, or null if parsing fails.</returns>
		public static VersionConfig Parse(string versionString)
		{
			if (string.IsNullOrWhiteSpace(versionString))
			{
				Debug.LogError("VersionConfig.Parse: Cannot parse null or empty version string.");
				return null;
			}

			// Length-checked before the regex runs, not after. The pattern is anchored and has
			// no nested quantifier that could backtrack catastrophically, but refusing an
			// absurd input outright costs one comparison and removes the question.
			if (versionString.Length > MaxVersionStringLength)
			{
				Debug.LogError($"VersionConfig.Parse: Version string is {versionString.Length} characters (max {MaxVersionStringLength}); refusing it.");
				return null;
			}

			Match match = VersionPattern.Match(versionString);

			if (match.Success)
			{
				string preRelease = match.Groups[4].Success ? match.Groups[4].Value : "";
				if (preRelease.Length > MaxPreReleaseLength)
				{
					Debug.LogError($"VersionConfig.Parse: Pre-release identifier is {preRelease.Length} characters (max {MaxPreReleaseLength}); refusing '{versionString}'.");
					return null;
				}

				// int.Parse cannot overflow here — the pattern caps each numeric group at nine
				// digits, and int.MaxValue is ten.
				VersionConfig config = ScriptableObject.CreateInstance<VersionConfig>();
				config.Major = int.Parse(match.Groups[1].Value);
				config.Minor = int.Parse(match.Groups[2].Value);
				config.Patch = int.Parse(match.Groups[3].Value);
				config.PreRelease = preRelease;
				return config;
			}
			else
			{
				Debug.LogError($"VersionConfig.Parse: Failed to parse version string '{versionString}'. Expected format: Major.Minor.Patch[.PreRelease] where PreRelease is dot-separated alphanumerics or hyphens.");
				return null;
			}
		}

		/// <summary>
		/// Compares this VersionConfig instance with another.
		/// Returns:
		/// -1 if this version is older
		///  0 if versions are equal
		///  1 if this version is newer
		/// </summary>
		/// <param name="other">The other VersionConfig to compare against.</param>
		public int CompareTo(VersionConfig other)
		{
			if (other == null) return 1; // Any version is newer than null

			// Compare Major
			if (this.Major != other.Major)
				return this.Major.CompareTo(other.Major);

			// Compare Minor
			if (this.Minor != other.Minor)
				return this.Minor.CompareTo(other.Minor);

			// Compare Patch
			if (this.Patch != other.Patch)
				return this.Patch.CompareTo(other.Patch);

			// Handle pre-release identifiers (e.g., 1.0.0-alpha < 1.0.0-beta < 1.0.0)
			// SemVer rules: A pre-release version has lower precedence than a normal version.
			// Pre-release comparison is lexicographical ASCII sort.
			if (!string.IsNullOrEmpty(this.PreRelease) && !string.IsNullOrEmpty(other.PreRelease))
			{
				// Both have pre-release tags, compare them lexicographically
				return string.Compare(this.PreRelease, other.PreRelease, StringComparison.OrdinalIgnoreCase);
			}
			else if (!string.IsNullOrEmpty(this.PreRelease))
			{
				// This has pre-release, other does not -> other is newer
				return -1;
			}
			else if (!string.IsNullOrEmpty(other.PreRelease))
			{
				// Other has pre-release, this does not -> this is newer
				return 1;
			}

			return 0; // Versions are effectively equal
		}

		/// <summary>
		/// Equality operator for VersionConfig. Returns true if both are equal.
		/// </summary>
		public static bool operator ==(VersionConfig a, VersionConfig b)
		{
			if (ReferenceEquals(a, b)) return true;
			if (ReferenceEquals(a, null) || ReferenceEquals(b, null)) return false;
			return a.CompareTo(b) == 0;
		}

		/// <summary>
		/// Inequality operator for VersionConfig. Returns true if not equal.
		/// </summary>
		public static bool operator !=(VersionConfig a, VersionConfig b)
		{
			return !(a == b);
		}

		/// <summary>
		/// Less-than operator for VersionConfig.
		/// </summary>
		public static bool operator <(VersionConfig a, VersionConfig b)
		{
			if (ReferenceEquals(a, null)) return !ReferenceEquals(b, null); // null < any non-null
			return a.CompareTo(b) < 0;
		}

		/// <summary>
		/// Greater-than operator for VersionConfig.
		/// </summary>
		public static bool operator >(VersionConfig a, VersionConfig b)
		{
			if (ReferenceEquals(b, null)) return !ReferenceEquals(a, null); // any non-null > null
			return a.CompareTo(b) > 0;
		}

		/// <summary>
		/// Less-than-or-equal operator for VersionConfig.
		/// </summary>
		public static bool operator <=(VersionConfig a, VersionConfig b)
		{
			return a < b || a == b;
		}

		/// <summary>
		/// Greater-than-or-equal operator for VersionConfig.
		/// </summary>
		public static bool operator >=(VersionConfig a, VersionConfig b)
		{
			return a > b || a == b;
		}

		/// <summary>
		/// Determines whether this instance is equal to another object.
		/// </summary>
		/// <param name="obj">The object to compare with.</param>
		/// <returns>True if equal, otherwise false.</returns>
		public override bool Equals(object obj)
		{
			return Equals(obj as VersionConfig);
		}

		/// <summary>
		/// Determines whether this instance is equal to another VersionConfig.
		/// </summary>
		/// <param name="other">The VersionConfig to compare with.</param>
		/// <returns>True if equal, otherwise false.</returns>
		public bool Equals(VersionConfig other)
		{
			if (ReferenceEquals(other, null)) return false;
			return this.CompareTo(other) == 0;
		}

		/// <summary>
		/// Returns a hash code for this instance.
		/// </summary>
		public override int GetHashCode()
		{
			unchecked
			{
				int hash = 17;
				hash = hash * 23 + Major.GetHashCode();
				hash = hash * 23 + Minor.GetHashCode();
				hash = hash * 23 + Patch.GetHashCode();
				hash = hash * 23 + (PreRelease != null ? PreRelease.GetHashCode() : 0);
				return hash;
			}
		}
	}
}