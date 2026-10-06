using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// The written forms of world time, shared by every surface that reads or writes the world
	/// clock (the Control Panel, and available to the in-game <c>/admin time</c> commands) so the
	/// two can never disagree about what a typed value means.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A WORLD TIMESTAMP is the calendar epoch (Unix seconds) plus the world milliseconds, written
	/// <c>yyyy-MM-dd HH:mm:ss.fff</c> in the invariant culture with no zone: it is UTC-style
	/// arithmetic on a calendar, not anybody's local time.
	/// </para>
	/// <para>
	/// A CHANGE is a signed span: <c>+1h30m</c>, <c>-90s</c>, <c>+2d3h4m5.25s</c>, <c>+250ms</c>.
	/// Units d, h, m, s, ms, each at most once, largest first; a bare signed number is seconds.
	/// The sign is required, which is what tells a change from a timestamp.
	/// </para>
	/// </remarks>
	public static class WorldClockText
	{
		/// <summary>The one output format.</summary>
		public const string Format = "yyyy-MM-dd HH:mm:ss.fff";

		/// <summary>Formats accepted as input, most precise first. All invariant, all UTC-style.</summary>
		private static readonly string[] InputFormats =
		{
			"yyyy-MM-dd HH:mm:ss.fff",
			"yyyy-MM-dd HH:mm:ss.ff",
			"yyyy-MM-dd HH:mm:ss.f",
			"yyyy-MM-dd HH:mm:ss",
			"yyyy-MM-dd HH:mm",
			"yyyy-MM-dd",
			"yyyy-MM-dd'T'HH:mm:ss.fff",
			"yyyy-MM-dd'T'HH:mm:ss",
			"yyyy-MM-dd'T'HH:mm",
		};

		/// <summary>The latest instant a timestamp can name: 9999-12-31 23:59:59.999, Unix ms.</summary>
		public const long MaxCalendarUnixMs = 253_402_300_799_999L;

		/// <summary>The earliest epoch representable: 0001-01-01, Unix seconds.</summary>
		public const long MinEpochUnixSeconds = -62_135_596_800L;

		/// <summary>The latest epoch representable: 9999-12-31 23:59:59, Unix seconds.</summary>
		public const long MaxEpochUnixSeconds = 253_402_300_799L;

		private static readonly Regex ChangePattern = new Regex(
			@"^(?<sign>[+-])\s*(?:(?<bare>\d+(?:\.\d+)?)|(?:(?<d>\d+(?:\.\d+)?)d)?\s*(?:(?<h>\d+(?:\.\d+)?)h)?\s*(?:(?<m>\d+(?:\.\d+)?)m(?!s))?\s*(?:(?<s>\d+(?:\.\d+)?)s)?\s*(?:(?<ms>\d+(?:\.\d+)?)ms)?)$",
			RegexOptions.CultureInvariant);

		/// <summary>Whether an epoch can be written as a calendar date at all.</summary>
		public static bool IsValidEpoch(long epochUnixSeconds) =>
			epochUnixSeconds >= MinEpochUnixSeconds && epochUnixSeconds <= MaxEpochUnixSeconds;

		/// <summary>
		/// Formats world milliseconds as a world timestamp, or returns null when the instant falls
		/// outside the years 1–9999 (an epoch or a world time that no calendar date can name).
		/// </summary>
		public static string? FormatTimestamp(long epochUnixSeconds, long worldMs)
		{
			if (!TryToInstant(epochUnixSeconds, worldMs, out DateTimeOffset instant))
			{
				return null;
			}
			return instant.UtcDateTime.ToString(Format, CultureInfo.InvariantCulture);
		}

		/// <summary>
		/// Parses a world timestamp into world milliseconds since the epoch. Refuses an instant
		/// before the epoch (world time cannot be negative) and anything unparseable.
		/// </summary>
		/// <param name="text">The timestamp, in one of the accepted formats.</param>
		/// <param name="epochUnixSeconds">The calendar epoch, from the world clock row.</param>
		/// <param name="worldMs">World milliseconds since the epoch.</param>
		/// <param name="error">Why it was refused, written for the person who typed it.</param>
		public static bool TryParseTimestamp(string? text, long epochUnixSeconds, out long worldMs, out string? error)
		{
			worldMs = 0;
			error = null;
			if (!IsValidEpoch(epochUnixSeconds))
			{
				error = "The world clock's epoch is not a calendar date.";
				return false;
			}
			string trimmed = (text ?? string.Empty).Trim();
			if (!DateTime.TryParseExact(trimmed, InputFormats, CultureInfo.InvariantCulture,
				DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime parsed))
			{
				error = $"Write a world timestamp as {Format}, for example 1203-04-05 06:07:08.250.";
				return false;
			}
			long unixMs = new DateTimeOffset(DateTime.SpecifyKind(parsed, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
			long epochMs = epochUnixSeconds * 1000L;
			if (unixMs < epochMs)
			{
				error = $"That is before the calendar epoch ({FormatTimestamp(epochUnixSeconds, 0)}). World time cannot be negative.";
				return false;
			}
			worldMs = unixMs - epochMs;
			return true;
		}

		/// <summary>
		/// Parses a signed change such as <c>+1h30m</c>, <c>-90s</c> or <c>+2d3h4m5.25s</c> into
		/// milliseconds, rounded to the nearest millisecond.
		/// </summary>
		/// <param name="text">The change. The sign is required.</param>
		/// <param name="deltaMs">The signed change in milliseconds.</param>
		/// <param name="error">Why it was refused, written for the person who typed it.</param>
		public static bool TryParseChange(string? text, out long deltaMs, out string? error)
		{
			deltaMs = 0;
			error = null;
			string trimmed = (text ?? string.Empty).Trim();
			Match match = ChangePattern.Match(trimmed);
			if (trimmed.Length < 2 || !match.Success)
			{
				error = "Write a change as a sign and units d, h, m, s, ms — for example +1h30m, -90s or +2d3h4m5.25s.";
				return false;
			}

			decimal total = 0m;
			bool any = false;
			if (!Add(match, "bare", 1000m, ref total, ref any)
				|| !Add(match, "d", 86_400_000m, ref total, ref any)
				|| !Add(match, "h", 3_600_000m, ref total, ref any)
				|| !Add(match, "m", 60_000m, ref total, ref any)
				|| !Add(match, "s", 1000m, ref total, ref any)
				|| !Add(match, "ms", 1m, ref total, ref any))
			{
				error = "That change is too large.";
				return false;
			}
			if (!any)
			{
				error = "Write a change as a sign and units d, h, m, s, ms — for example +1h30m, -90s or +2d3h4m5.25s.";
				return false;
			}

			total = Math.Round(total, MidpointRounding.AwayFromZero);
			// Bounded by the calendar itself: no change larger than the whole representable range is meaningful.
			if (total > MaxCalendarUnixMs - MinEpochUnixSeconds * 1000m)
			{
				error = "That change is too large.";
				return false;
			}
			deltaMs = (long)total;
			if (match.Groups["sign"].Value == "-")
			{
				deltaMs = -deltaMs;
			}
			return true;
		}

		/// <summary>
		/// Writes a span of milliseconds in the change syntax, largest unit first: <c>+1h30m</c>,
		/// <c>-1d</c>, <c>+0s</c>.
		/// </summary>
		public static string FormatChange(long deltaMs)
		{
			string sign = deltaMs < 0 ? "-" : "+";
			// Unsigned arithmetic so long.MinValue cannot overflow on negation.
			ulong rest = deltaMs < 0 ? (ulong)(-(deltaMs + 1)) + 1UL : (ulong)deltaMs;
			ulong d = rest / 86_400_000UL; rest %= 86_400_000UL;
			ulong h = rest / 3_600_000UL; rest %= 3_600_000UL;
			ulong m = rest / 60_000UL; rest %= 60_000UL;
			ulong s = rest / 1000UL; rest %= 1000UL;
			var text = new System.Text.StringBuilder(sign);
			if (d > 0) text.Append(d).Append('d');
			if (h > 0) text.Append(h).Append('h');
			if (m > 0) text.Append(m).Append('m');
			if (s > 0) text.Append(s).Append('s');
			if (rest > 0) text.Append(rest).Append("ms");
			if (text.Length == 1) text.Append("0s");
			return text.ToString();
		}

		/// <summary>The instant a world time names, when one exists in years 1–9999.</summary>
		public static bool TryToInstant(long epochUnixSeconds, long worldMs, out DateTimeOffset instant)
		{
			instant = default;
			if (!IsValidEpoch(epochUnixSeconds) || worldMs < 0)
			{
				return false;
			}
			long epochMs = epochUnixSeconds * 1000L;
			if (worldMs > MaxCalendarUnixMs - epochMs)
			{
				return false;
			}
			instant = DateTimeOffset.FromUnixTimeMilliseconds(epochMs + worldMs);
			return true;
		}

		private static bool Add(Match match, string group, decimal unitMs, ref decimal total, ref bool any)
		{
			Group g = match.Groups[group];
			if (!g.Success)
			{
				return true;
			}
			if (!decimal.TryParse(g.Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal value))
			{
				return false;
			}
			try
			{
				total += value * unitMs;
			}
			catch (OverflowException)
			{
				return false;
			}
			any = true;
			return true;
		}
	}
}

namespace FishMMO.Database.Data
{
	/// <summary>Bounds every world clock write is held to, here and in the table's check constraints.</summary>
	public static class WorldClockLimits
	{
		/// <summary>
		/// Fastest pace a write may set, in world seconds per real second. Bounds a typo (a day per
		/// real second is 86 400×), not a design decision. Mirrored by the
		/// <c>ck_world_clock_control_rate</c> check constraint.
		/// </summary>
		public const double MaxRate = 100_000d;

		/// <summary>Longest account name recorded as the writer.</summary>
		public const int MaxActorLength = 255;
	}
}
