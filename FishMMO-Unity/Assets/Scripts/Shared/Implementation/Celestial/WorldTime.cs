using System;
using FishNet.Managing.Timing;

namespace FishMMO.Shared.Celestial
{
	/// <summary>The world time, in real hours since the calendar epoch, from wherever it is known.</summary>
	public static class WorldTime
	{
		/// <summary>
		/// The world time now, hours, at the tick the connection's clock is read at (<see cref="WorldMotion.TimeManager"/>):
		/// for code that has no <see cref="TimeManager"/> of its own, or no reference to FishNet to name one.
		/// </summary>
		public static double Hours => CurrentHours(WorldMotion.TimeManager);

		/// <summary>
		/// From the world clock at the current (fractional) tick when it is anchored; offline, at the
		/// <see cref="LocalWorldClock"/>'s tick when one is active; otherwise from this machine's clock.
		/// </summary>
		public static double CurrentHours(TimeManager timeManager)
		{
			WorldClock clock = WorldClock.Shared;
			if (clock.HasAnchor && timeManager != null)
			{
				return clock.WorldHoursAt(timeManager.Tick + timeManager.GetTickPercentAsDouble());
			}
			// Offline (the editor's World Sim bed): its own tick, driving the same clock.
			LocalWorldClock local = LocalWorldClock.Active;
			if (local != null && clock.HasAnchor)
			{
				return clock.WorldHoursAt(local.Tick);
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
