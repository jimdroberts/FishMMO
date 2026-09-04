using System;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// The moments of an arena match, as C# events for code that wants to react to them: audio,
	/// camera, effects, analytics.
	/// </summary>
	/// <remarks>
	/// Designers get the same moments as ECA triggers on the <see cref="ArenaTemplate"/>; this is
	/// the programmer's side of the same hook. Raised by <c>UITKArenaHud</c> from the match state
	/// broadcast, so everything here is server-timed.
	/// </remarks>
	public static class ArenaClientEvents
	{
		/// <summary>Each second of the start countdown, with the seconds left. 0 is the start itself.</summary>
		public static event Action<int> OnCountdownTick;

		/// <summary>The match went live.</summary>
		public static event Action OnMatchLive;

		/// <summary>A team's score changed. Team index, new score.</summary>
		public static event Action<int, int> OnTeamScored;

		/// <summary>The match ended, with the full result.</summary>
		public static event Action<ArenaResultsBroadcast> OnMatchEnded;

		internal static void RaiseCountdownTick(int secondsRemaining) => OnCountdownTick?.Invoke(secondsRemaining);
		internal static void RaiseMatchLive() => OnMatchLive?.Invoke();
		internal static void RaiseTeamScored(int team, int score) => OnTeamScored?.Invoke(team, score);
		internal static void RaiseMatchEnded(ArenaResultsBroadcast results) => OnMatchEnded?.Invoke(results);
	}
}
