using System;
using System.Globalization;
using FishMMO.Auth.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// Display formatting for the staff console: access levels, ages and distances.
	/// </summary>
	public static class StaffConsoleFormat
	{
		/// <summary>
		/// The word for an access level byte as it travels on the wire.
		/// </summary>
		/// <param name="level">The access level.</param>
		/// <returns>The enum member's name, or a numbered fallback for a value this build does not know.</returns>
		public static string AccessLevelName(byte level)
		{
			if (Enum.IsDefined(typeof(AccessLevel), level))
			{
				return ((AccessLevel)level).ToString();
			}
			return "Level " + level.ToString(CultureInfo.InvariantCulture);
		}

		/// <summary>
		/// How long ago a UTC tick count was, in the shortest useful unit.
		/// </summary>
		/// <param name="utcTicks">The moment, as UTC ticks. Zero or less means unknown.</param>
		/// <param name="nowUtcTicks">The present, as UTC ticks.</param>
		/// <returns>"just now", "5m ago", "3h ago", "2d ago", or "unknown".</returns>
		public static string FormatAge(long utcTicks, long nowUtcTicks)
		{
			if (utcTicks <= 0)
			{
				return "unknown";
			}

			/* A timestamp slightly ahead of this machine's clock is clock skew between the client
			 * and the server that stamped it, not a message from the future. */
			long delta = nowUtcTicks - utcTicks;
			if (delta < TimeSpan.TicksPerMinute)
			{
				return "just now";
			}
			if (delta < TimeSpan.TicksPerHour)
			{
				return (delta / TimeSpan.TicksPerMinute).ToString(CultureInfo.InvariantCulture) + "m ago";
			}
			if (delta < TimeSpan.TicksPerDay)
			{
				return (delta / TimeSpan.TicksPerHour).ToString(CultureInfo.InvariantCulture) + "h ago";
			}
			return (delta / TimeSpan.TicksPerDay).ToString(CultureInfo.InvariantCulture) + "d ago";
		}

		/// <summary>
		/// A distance in metres, rounded to the metre.
		/// </summary>
		/// <param name="metres">The distance.</param>
		/// <returns>For example "12 m"; empty when the distance is not a number.</returns>
		public static string FormatDistance(float metres)
		{
			if (float.IsNaN(metres) || float.IsInfinity(metres) || metres < 0f)
			{
				return string.Empty;
			}
			return Math.Round(metres).ToString("0", CultureInfo.InvariantCulture) + " m";
		}
	}
}
