using System;
using System.Text.RegularExpressions;
using FishMMO.Logging;

/// <summary>
/// Represents a semantic version (Major.Minor.Patch[.PreRelease]) and provides
/// parsing, comparison, and string representation utilities used by the patching system.
/// </summary>
public class VersionConfig : IComparable<VersionConfig?>
{
	public int Major { get; set; } = 0;
	public int Minor { get; set; } = 0;
	public int Patch { get; set; } = 0;
	/// <summary>
	/// Optional pre-release identifier (e.g., "alpha" in 1.2.3.alpha).
	/// Constrained to [A-Za-z0-9-] characters only; never contains path separators.
	/// </summary>
	public string PreRelease { get; set; } = "";

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

	// Pre-release is intentionally constrained to safe characters so parsed
	// values can never be used to construct a directory-escaping file path.
	private static readonly Regex versionRegex =
		new Regex(@"^(\d{1,9})\.(\d{1,9})\.(\d{1,9})(?:\.([A-Za-z0-9\-]{1,32}))?$", RegexOptions.Compiled);

	public static VersionConfig? Parse(string versionString)
	{
		if (string.IsNullOrWhiteSpace(versionString))
		{
			Log.Warning("VersionConfig", "VersionConfig.Parse: Cannot parse null or empty version string.");
			return null;
		}

		Match match = versionRegex.Match(versionString);

		if (!match.Success)
		{
			Log.Warning("VersionConfig", $"VersionConfig.Parse: Failed to parse version string '{versionString}'. Expected Major.Minor.Patch[.PreRelease] with PreRelease in [A-Za-z0-9-]{{1,32}}.");
			return null;
		}

		VersionConfig config = new VersionConfig
		{
			Major = int.Parse(match.Groups[1].Value),
			Minor = int.Parse(match.Groups[2].Value),
			Patch = int.Parse(match.Groups[3].Value),
			PreRelease = match.Groups[4].Success ? match.Groups[4].Value : "",
		};
		return config;
	}

	public int CompareTo(VersionConfig? other)
	{
		if (other == null) return 1;
		if (this.Major != other.Major) return this.Major.CompareTo(other.Major);
		if (this.Minor != other.Minor) return this.Minor.CompareTo(other.Minor);
		if (this.Patch != other.Patch) return this.Patch.CompareTo(other.Patch);

		// SemVer 2.0.0 §11: a normal version has higher precedence than any
		// pre-release. When both have pre-release identifiers we compare them
		// per dotted segment: purely-numeric segments compare numerically and
		// have lower precedence than alphanumeric segments; otherwise we
		// compare lexicographically with ordinal (case-sensitive) semantics.
		// Our regex currently restricts PreRelease to a single segment, but we
		// implement the full algorithm so future relaxation does not silently
		// invert ordering.
		bool aPre = !string.IsNullOrEmpty(this.PreRelease);
		bool bPre = !string.IsNullOrEmpty(other.PreRelease);
		if (aPre && bPre)
		{
			return ComparePreReleaseIdentifiers(this.PreRelease, other.PreRelease);
		}
		else if (aPre) return -1;
		else if (bPre) return 1;
		return 0;
	}

	private static int ComparePreReleaseIdentifiers(string a, string b)
	{
		string[] aParts = a.Split('.');
		string[] bParts = b.Split('.');
		int common = Math.Min(aParts.Length, bParts.Length);
		for (int i = 0; i < common; i++)
		{
			string ap = aParts[i];
			string bp = bParts[i];
			bool aIsNum = ulong.TryParse(ap, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out ulong aNum);
			bool bIsNum = ulong.TryParse(bp, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out ulong bNum);
			if (aIsNum && bIsNum)
			{
				int c = aNum.CompareTo(bNum);
				if (c != 0) return c;
			}
			else if (aIsNum) return -1; // numeric < alphanumeric
			else if (bIsNum) return 1;
			else
			{
				int c = string.CompareOrdinal(ap, bp);
				if (c != 0) return c;
			}
		}
		// Longer identifier list (with all leading parts equal) has higher precedence.
		return aParts.Length.CompareTo(bParts.Length);
	}

	public static bool operator ==(VersionConfig? a, VersionConfig? b)
	{
		if (ReferenceEquals(a, b)) return true;
		if (ReferenceEquals(a, null) || ReferenceEquals(b, null)) return false;
		return a.CompareTo(b) == 0;
	}
	public static bool operator !=(VersionConfig? a, VersionConfig? b) => !(a == b);
	public static bool operator <(VersionConfig? a, VersionConfig? b)
	{
		if (a is null) return b is not null;
		if (b is null) return false;
		return a.CompareTo(b) < 0;
	}
	public static bool operator >(VersionConfig? a, VersionConfig? b)
	{
		if (a is null) return false;
		if (b is null) return true;
		return a.CompareTo(b) > 0;
	}
	public static bool operator <=(VersionConfig? a, VersionConfig? b) => a < b || a == b;
	public static bool operator >=(VersionConfig? a, VersionConfig? b) => a > b || a == b;

	public override bool Equals(object? obj) => Equals(obj as VersionConfig);
	public bool Equals(VersionConfig? other)
	{
		if (other is null) return false;
		// SemVer pre-release identifiers are case-sensitive (Ordinal). Equality
		// must agree with CompareTo or the operators below produce contradictory
		// results (a == b but a.CompareTo(b) != 0).
		return this.Major == other.Major
			&& this.Minor == other.Minor
			&& this.Patch == other.Patch
			&& string.Equals(this.PreRelease, other.PreRelease, StringComparison.Ordinal);
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
