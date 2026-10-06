using System;
using System.Globalization;

namespace FishMMO.Shared.Celestial
{
	/// <summary>
	/// The world time written as text, to the millisecond: what an admin types to set the clock, and
	/// what every readout shows. One form for the in-game commands, the Control Panel and the editor.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>A world timestamp.</b> World time is seconds since the calendar epoch, and the epoch is a real
	/// instant (<see cref="CalendarProfile.WorldEpochUnixSeconds"/>), so a moment is written as the
	/// ISO date and time it would be on that count: <c>2026-10-06 14:30:00.250</c>. It is exact, needs no
	/// calendar asset (the Control Panel has none), and sorts and compares as text does. The game's own
	/// calendar date is a reading of it, shown beside it where the calendar is loaded.
	/// </para>
	/// <para>
	/// <b>Or a change.</b> <c>+1h</c>, <c>-90s</c>, <c>+2d3h4m5.25s</c>: that much world time from the
	/// moment given as "now". Units d, h, m, s and ms; a bare number is seconds.
	/// </para>
	/// </remarks>
	public static class WorldTimeText
	{
		public const string Format = "yyyy-MM-dd HH:mm:ss.fff";
		private static readonly string[] Accepted =
		{
			"yyyy-MM-dd HH:mm:ss.FFFFFFF", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF", "yyyy-MM-dd HH:mm", "yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd",
		};

		/// <summary>The world timestamp of a moment, to the millisecond.</summary>
		public static string Write(double worldSeconds, long epochUnixSeconds)
		{
			DateTime epoch = DateTimeOffset.FromUnixTimeSeconds(epochUnixSeconds).UtcDateTime;
			long milliseconds = (long)Math.Round(worldSeconds * 1000.0);
			return epoch.AddTicks(milliseconds * TimeSpan.TicksPerMillisecond).ToString(Format, CultureInfo.InvariantCulture);
		}

		/// <summary>
		/// Reads a world timestamp, or a change from <paramref name="now"/>, into world seconds. False with
		/// a reason when it is neither.
		/// </summary>
		public static bool TryRead(string text, long epochUnixSeconds, double now, out double worldSeconds, out string error)
		{
			worldSeconds = now;
			error = null;
			if (string.IsNullOrWhiteSpace(text))
			{
				error = "no time given";
				return false;
			}
			text = text.Trim();
			if (text[0] == '+' || text[0] == '-')
			{
				if (!TryReadDuration(text.Substring(1), out double seconds))
				{
					error = $"'{text}' is not a change of time (e.g. +1h30m, -90s, +2d)";
					return false;
				}
				worldSeconds = now + (text[0] == '-' ? -seconds : seconds);
				return true;
			}
			if (DateTime.TryParseExact(text, Accepted, CultureInfo.InvariantCulture,
				DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime at))
			{
				DateTime epoch = DateTimeOffset.FromUnixTimeSeconds(epochUnixSeconds).UtcDateTime;
				worldSeconds = (at - epoch).Ticks / (double)TimeSpan.TicksPerSecond;
				return true;
			}
			error = $"'{text}' is not a world time (yyyy-MM-dd HH:mm:ss.fff) or a change (+1h, -90s)";
			return false;
		}

		/// <summary>Seconds from "2d3h4m5.25s", "90", "500ms". A bare number is seconds.</summary>
		public static bool TryReadDuration(string text, out double seconds)
		{
			seconds = 0.0;
			if (string.IsNullOrWhiteSpace(text))
			{
				return false;
			}
			text = text.Trim().ToLowerInvariant();
			if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds))
			{
				return seconds >= 0.0 && !double.IsInfinity(seconds);
			}
			int i = 0;
			bool any = false;
			while (i < text.Length)
			{
				int start = i;
				while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.'))
				{
					i++;
				}
				if (i == start || !double.TryParse(text.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double amount))
				{
					return false;
				}
				int unitStart = i;
				while (i < text.Length && char.IsLetter(text[i]))
				{
					i++;
				}
				double scale;
				switch (text.Substring(unitStart, i - unitStart))
				{
					case "d": scale = 86400.0; break;
					case "h": scale = 3600.0; break;
					case "m": scale = 60.0; break;
					case "s": case "": scale = 1.0; break;
					case "ms": scale = 0.001; break;
					default: return false;
				}
				seconds += amount * scale;
				any = true;
			}
			return any;
		}

		/// <summary>A pace as text: "held", "real time", "60×".</summary>
		public static string Pace(double rate)
		{
			if (rate <= 0.0)
			{
				return "held";
			}
			return Math.Abs(rate - 1.0) < 1e-9 ? "real time" : rate.ToString("0.###", CultureInfo.InvariantCulture) + "×";
		}
	}
}
