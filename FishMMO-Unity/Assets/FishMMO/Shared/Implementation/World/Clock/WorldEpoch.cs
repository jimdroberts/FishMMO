using System;

namespace FishMMO.Shared.Celestial
{
	/// <summary>
	/// The real instant the world clock counts from (Unix seconds, UTC): world seconds are seconds since it.
	/// </summary>
	/// <remarks>
	/// Core. The world's calendar (the Sky &amp; Time module) installs <see cref="Source"/> with its own epoch; without one
	/// the clock counts from <see cref="DefaultUnixSeconds"/>, which is also the calendar's default, so a world that never
	/// changed its calendar reads the same time with the module or without it. Every server, client and tool asks here
	/// rather than looking up a calendar, so the clock never depends on which world content is installed.
	/// </remarks>
	public static class WorldEpoch
	{
		/// <summary>2026-01-01T00:00:00Z.</summary>
		public const long DefaultUnixSeconds = 1767225600L;

		/// <summary>The installed calendar's epoch, or null for <see cref="DefaultUnixSeconds"/>.</summary>
		public static Func<long> Source { get; set; }

		/// <summary>The epoch now in use, Unix seconds.</summary>
		public static long UnixSeconds
		{
			get
			{
				Func<long> source = Source;
				return source != null ? source() : DefaultUnixSeconds;
			}
		}
	}
}
