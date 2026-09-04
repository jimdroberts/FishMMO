using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Where a match is when a cue fires.
	/// </summary>
	public enum ArenaCuePhase : byte
	{
		/// <summary>A second of the start countdown, including 0 for the start itself.</summary>
		Countdown = 0,
		/// <summary>The match has ended; the results are about to show.</summary>
		Ended = 1,
	}

	/// <summary>
	/// Event data handed to arena cue triggers on the client: which moment this is.
	/// </summary>
	/// <remarks>
	/// Lets one trigger asset serve several cues — an action can read
	/// <see cref="SecondsRemaining"/> to pick a pitch or a particle count — and lets
	/// <c>MatchEndTriggers</c> tell a win from a loss.
	/// </remarks>
	public class ArenaEventData : EventData
	{
		/// <summary>The phase the cue belongs to.</summary>
		public ArenaCuePhase Phase { get; }

		/// <summary>Seconds left on the start timer; 0 at the start. Unused for the end.</summary>
		public int SecondsRemaining { get; }

		/// <summary>The local player's team.</summary>
		public int Team { get; }

		/// <summary>The winning team at the end, or -1 for a draw. -1 during the countdown.</summary>
		public int WinnerTeam { get; }

		public ArenaEventData(ICharacter initiator, ArenaCuePhase phase, int secondsRemaining, int team, int winnerTeam)
			: base(initiator, initiator?.GameObject)
		{
			Phase = phase;
			SecondsRemaining = secondsRemaining;
			Team = team;
			WinnerTeam = winnerTeam;
		}

		public override string ToString() => $"ArenaEventData (Phase: {Phase}, SecondsRemaining: {SecondsRemaining}, Team: {Team}, Winner: {WinnerTeam})";
	}
}
