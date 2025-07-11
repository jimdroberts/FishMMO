using UnityEngine;
using System;
using System.Text.RegularExpressions;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "VersionConfig", menuName = "FishMMO/Version/Version Configuration")]
	public class VersionConfig : ScriptableObject, IComparable<VersionConfig>
	{
		[Tooltip("Major version: Incremented for incompatible API changes, Major features.")]
		public int Major = 0;

		[Tooltip("Minor version: Incremented for new, backward-compatible functionality.")]
		public int Minor = 0;

		[Tooltip("Patch version: Incremented for backward-compatible bug fixes.")]
		public int Patch = 0;

		[Tooltip("Optional: Pre-release identifier (e.g., 'alpha', 'beta', 'rc.1').")]
		public string PreRelease = "";

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

			// Regex to match versions like X.Y.Z or X.Y.Z.PreRelease
			// Group 1: Major, Group 2: Minor, Group 3: Patch, Group 4: PreRelease (optional)
			Match match = Regex.Match(versionString, @"^(\d+)\.(\d+)\.(\d+)(?:\.(.+))?$");

			if (match.Success)
			{
				VersionConfig config = ScriptableObject.CreateInstance<VersionConfig>();
				config.Major = int.Parse(match.Groups[1].Value);
				config.Minor = int.Parse(match.Groups[2].Value);
				config.Patch = int.Parse(match.Groups[3].Value);
				config.PreRelease = match.Groups.Count > 4 && match.Groups[4].Success ? match.Groups[4].Value : "";
				return config;
			}
			else
			{
				Debug.LogError($"VersionConfig.Parse: Failed to parse version string '{versionString}'. Expected format: Major.Minor.Patch[.PreRelease]");
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

		public static bool operator ==(VersionConfig a, VersionConfig b)
		{
			if (ReferenceEquals(a, b)) return true;
			if (ReferenceEquals(a, null) || ReferenceEquals(b, null)) return false;
			return a.CompareTo(b) == 0;
		}

		public static bool operator !=(VersionConfig a, VersionConfig b)
		{
			return !(a == b);
		}

		public static bool operator <(VersionConfig a, VersionConfig b)
		{
			if (ReferenceEquals(a, null)) return !ReferenceEquals(b, null); // null < any non-null
			return a.CompareTo(b) < 0;
		}

		public static bool operator >(VersionConfig a, VersionConfig b)
		{
			if (ReferenceEquals(b, null)) return !ReferenceEquals(a, null); // any non-null > null
			return a.CompareTo(b) > 0;
		}

		public static bool operator <=(VersionConfig a, VersionConfig b)
		{
			return a < b || a == b;
		}

		public static bool operator >=(VersionConfig a, VersionConfig b)
		{
			return a > b || a == b;
		}

		public override bool Equals(object obj)
		{
			return Equals(obj as VersionConfig);
		}

		public bool Equals(VersionConfig other)
		{
			if (ReferenceEquals(other, null)) return false;
			return this.CompareTo(other) == 0;
		}

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