namespace FishMMO.Database.Data.Enums
{
	/// <summary>
	/// Where an arena match is in its life. Forward-only; the numbers order the phases.
	/// </summary>
	public enum ArenaMatchStatus : int
	{
		/// <summary>Formed; waiting for every seat to arrive in the instance.</summary>
		Gathering = 0,
		/// <summary>Everyone is in; each player is asked to accept before the timer runs.</summary>
		ReadyCheck = 1,
		/// <summary>Everyone accepted; the start timer is running.</summary>
		Countdown = 2,
		/// <summary>Play.</summary>
		Live = 3,
		/// <summary>Finished with a result.</summary>
		Ended = 4,
		/// <summary>Abandoned before play: not enough players arrived, somebody declined, or the instance failed.</summary>
		Cancelled = 5,
	}
}
