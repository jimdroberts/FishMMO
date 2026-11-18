using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA event data for world-level day/night cycle transitions.
	/// Initiator is null for world events.
	/// </summary>
	public class DayNightEventData : EventData
	{
		/// <summary>
		/// True if it is now daytime; false if it is now night.
		/// </summary>
		public bool IsDaytime { get; }

		/// <summary>
		/// Creates a new DayNightEventData for a world-level day/night transition.
		/// </summary>
		/// <param name="isDaytime">Whether it is now daytime.</param>
		public DayNightEventData(bool isDaytime)
			: base(null)
		{
			IsDaytime = isDaytime;
		}
	}
}