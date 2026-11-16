using System;
using System.Collections.Generic;

namespace FishMMO.Client
{
	/// <summary>One row of the arena board's season standings.</summary>
	public struct ArenaStandingRow
	{
		public int Rank;
		public string Name;
		public long Score;
		public int Wins;
		public int Losses;
		/// <summary>True for the viewing player's own row.</summary>
		public bool Mine;
	}

	/// <summary>What the arena board's standings show, as the source describes it now.</summary>
	public struct ArenaStandingsView
	{
		/// <summary>Column title over the names (the season's name), or null when there are no rows.</summary>
		public string SeasonTitle;
		/// <summary>Column title over the scores.</summary>
		public string ScoreTitle;
		/// <summary>A line shown instead of the rows when there are none, or under them; null for none.</summary>
		public string Notice;
	}

	/// <summary>
	/// The season standings the arena board shows in its Leaderboard view, supplied by the Leaderboards module.
	/// </summary>
	/// <remarks>
	/// The arena board does not know how standings are read: with no source (the Leaderboards module is not
	/// installed) its Leaderboard tab is hidden. A source asks the server when the board opens the view, keeps one
	/// request outstanding (<see cref="Tick"/>) and raises <see cref="Changed"/> when what it would show changes.
	/// </remarks>
	public interface IArenaStandingsSource
	{
		/// <summary>Raised when <see cref="Describe"/> would answer differently.</summary>
		event Action Changed;

		/// <summary>The board opened its standings view: start asking (does nothing when already asked).</summary>
		void Open(Client client);

		/// <summary>The board was opened for another arena or closed: forget what was read.</summary>
		void Reset();

		/// <summary>Sends or re-sends the request when due; called each tick while the view shows.</summary>
		void Tick(Client client, float now);

		/// <summary>Fills <paramref name="rows"/> (cleared first) and returns the titles and notice to show.</summary>
		ArenaStandingsView Describe(long viewerCharacterID, List<ArenaStandingRow> rows);

		/// <summary>Called once when the client is set, so the source can listen for replies.</summary>
		void Attach(Client client);

		/// <summary>Called once when the client is unset.</summary>
		void Detach(Client client);
	}

	/// <summary>The installed <see cref="IArenaStandingsSource"/>, if any.</summary>
	public static class ArenaStandings
	{
		/// <summary>The source, or null when no module supplies standings.</summary>
		public static IArenaStandingsSource Source { get; set; }
	}
}
