using System;
using System.Text.RegularExpressions;
using FishMMO.Logging;

/// <summary>
/// Represents a semantic version (Major.Minor.Patch[.PreRelease]) and provides
/// parsing, comparison, and string representation utilities used by the patching system.
/// </summary>
public class VersionConfig : IComparable<VersionConfig?>
{
	/// <summary>
	/// Major version component (e.g., the "1" in 1.2.3).
	/// </summary>
	public int Major = 0;

	/// <summary>
	/// Minor version component (e.g., the "2" in 1.2.3).
	/// </summary>
	public int Minor = 0;

	/// <summary>
	/// Patch version component (e.g., the "3" in 1.2.3).
	/// </summary>
	public int Patch = 0;

	/// <summary>
	/// Optional pre-release identifier (e.g., "alpha" in 1.2.3.alpha).
	/// An empty string indicates a normal (non pre-release) version.
	/// </summary>
	public string PreRelease = "";

	/// <summary>
	/// The full version string representation constructed from the components.
	/// Examples: "1.2.3" or "1.2.3.alpha".
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
	/// Parses a version string (e.g., "1.2.3" or "1.2.3.alpha") into a new VersionConfig instance.
	/// </summary>
	/// <param name="versionString">The version string to parse.</param>
	/// <returns>A new VersionConfig instance populated with the parsed version, or null if parsing fails.</returns>
	public static VersionConfig? Parse(string versionString)
	{
		if (string.IsNullOrWhiteSpace(versionString))
		{
			Log.Warning("VersionConfig", "VersionConfig.Parse: Cannot parse null or empty version string.");
			return null;
		}

		Match match = Regex.Match(versionString, @"^(\d+)\.(\d+)\.(\d+)(?:\.(.+))?$");

		if (match.Success)
		{
			VersionConfig config = new VersionConfig();
			config.Major = int.Parse(match.Groups[1].Value);
			config.Minor = int.Parse(match.Groups[2].Value);
			config.Patch = int.Parse(match.Groups[3].Value);
			config.PreRelease = match.Groups.Count > 4 && match.Groups[4].Success ? match.Groups[4].Value : "";
			return config;
		}
		else
		{
			Log.Warning("VersionConfig", $"VersionConfig.Parse: Failed to parse version string '{versionString}'. Expected format: Major.Minor.Patch[.PreRelease]");
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
	public int CompareTo(VersionConfig? other)
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
	/// Equality operator. Returns true when both versions represent the same
	/// semantic version (including pre-release equality, case-insensitive for pre-release).
	/// </summary>
	public static bool operator ==(VersionConfig? a, VersionConfig? b)
	{
		if (ReferenceEquals(a, b)) return true;
		if (ReferenceEquals(a, null) || ReferenceEquals(b, null)) return false;
		return a.CompareTo(b) == 0;
	}

	/// <summary>
	/// Inequality operator.
	/// </summary>
	public static bool operator !=(VersionConfig? a, VersionConfig? b)
	{
		return !(a == b);
	}

	/// <summary>
	/// Less-than operator based on semantic version ordering.
	/// </summary>
	public static bool operator <(VersionConfig? a, VersionConfig? b)
	{
		if (a is null) return b is not null; // null < any non-null
		if (b is null) return false;
		return a.CompareTo(b) < 0;
	}

	/// <summary>
	/// Greater-than operator based on semantic version ordering.
	/// </summary>
	public static bool operator >(VersionConfig? a, VersionConfig? b)
	{
		if (a is null) return false;
		if (b is null) return true;
		return a.CompareTo(b) > 0;
	}

	/// <summary>
	/// Less-than-or-equal operator.
	/// </summary>
	public static bool operator <=(VersionConfig? a, VersionConfig? b)
	{
		return a < b || a == b;
	}

	/// <summary>
	/// Greater-than-or-equal operator.
	/// </summary>
	public static bool operator >=(VersionConfig? a, VersionConfig? b)
	{
		return a > b || a == b;
	}

	/// <summary>
	/// Determines whether this instance is equal to another object.
	/// </summary>
	public override bool Equals(object? obj)
	{
		return Equals(obj as VersionConfig);
	}

	/// <summary>
	/// Determines whether this instance is equal to another <see cref="VersionConfig"/> instance.
	/// Comparison includes Major, Minor, Patch and a case-insensitive comparison of PreRelease.
	/// </summary>
	public bool Equals(VersionConfig? other)
	{
		if (other is null) return false;
		return this.Major == other.Major
			&& this.Minor == other.Minor
			&& this.Patch == other.Patch
			&& string.Equals(this.PreRelease, other.PreRelease, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// Returns a hash code for this version instance suitable for use in hashing structures.
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