using System;
using FishNet.Managing.Timing;

namespace FishMMO.Shared.Celestial
{
	/// <summary>The world time, in real hours since the calendar epoch, from wherever it is known.</summary>
	public static class WorldTime
	{
		/// <summary>
		/// From the world clock at the current (fractional) tick when it is anchored; otherwise
		/// from this machine's clock, which is only good enough for offline previews.
		/// </summary>
		public static double CurrentHours(TimeManager timeManager)
		{
			WorldClock clock = WorldClock.Shared;
			if (clock.HasAnchor && timeManager != null)
			{
				return clock.WorldHoursAt(timeManager.Tick + timeManager.GetTickPercentAsDouble());
			}
			return UnanchoredHours();
		}

		/// <summary>World hours from the local UTC clock.</summary>
		public static double UnanchoredHours()
		{
			SolarSystemProfile system = SolarSystemProfile.Active;
			long epoch = system != null ? system.EpochUnixSeconds : CalendarProfile.DefaultEpochUnixSeconds;
			return WorldClock.WorldSecondsFromUtc(DateTime.UtcNow, epoch) / 3600.0;
		}
	}
}
