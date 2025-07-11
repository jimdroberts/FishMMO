using System;
using YamlDotNet.Serialization;

public class VersionConfig : IComparable<VersionConfig>
{
	public int Major { get; set; } = 0;
	public int Minor { get; set; } = 0;
	public int Patch { get; set; } = 0;
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

	public int CompareTo(VersionConfig other)
	{
		if (other == null) return 1;

		if (this.Major != other.Major)
			return this.Major.CompareTo(other.Major);

		if (this.Minor != other.Minor)
			return this.Minor.CompareTo(other.Minor);

		if (this.Patch != other.Patch)
			return this.Patch.CompareTo(other.Patch);

		if (!string.IsNullOrEmpty(this.PreRelease) && !string.IsNullOrEmpty(other.PreRelease))
		{
			return string.Compare(this.PreRelease, other.PreRelease, StringComparison.OrdinalIgnoreCase);
		}
		else if (!string.IsNullOrEmpty(this.PreRelease))
		{
			return -1;
		}
		else if (!string.IsNullOrEmpty(other.PreRelease))
		{
			return 1;
		}

		return 0;
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
		if (ReferenceEquals(a, null)) return !ReferenceEquals(b, null);
		return a.CompareTo(b) < 0;
	}

	public static bool operator >(VersionConfig a, VersionConfig b)
	{
		if (ReferenceEquals(b, null)) return !ReferenceEquals(a, null);
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