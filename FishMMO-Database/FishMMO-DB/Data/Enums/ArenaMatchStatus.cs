namespace FishMMO.Database.Data.Enums
{
	/// <summary>
	/// Where an arena match is in its life.
	/// </summary>
	public enum ArenaMatchStatus : int
	{
		/// <summary>Formed; waiting for every seat to arrive in the instance.</summary>
		Gathering = 0,
		/// <summary>Everyone is in; the start timer is running.</summary>
		Countdown = 1,
		/// <summary>Play.</summary>
		Live = 2,
		/// <summary>Finished with a result.</summary>
		Ended = 3,
		/// <summary>Abandoned before play: not enough players arrived, or the instance failed.</summary>
		Cancelled = 4,
	}
}
