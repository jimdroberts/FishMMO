using System;
using FishNet.Broadcast;

namespace FishMMO.Shared
{
	/// <summary>
	/// Client → Server: one page of one leaderboard, and the sender's own standing on it.
	/// </summary>
	/// <remarks>
	/// Needs no interactable: boards are readable from anywhere, including by a dead or stunned
	/// character, because reading one is not an action and leads to none.
	/// </remarks>
	public struct LeaderboardPageRequestBroadcast : IBroadcast
	{
		/// <summary>The board's <see cref="LeaderboardTemplate"/> ID.</summary>
		public int TemplateID;
		/// <summary>1-based page. The server clamps it to the pages anyone may browse.</summary>
		public int Page;
	}

	/// <summary>One ranked character.</summary>
	/// <remarks>
	/// A plain struct with no custom serializer, so FishNet generates the array serializer for
	/// <see cref="LeaderboardPageBroadcast.Entries"/> itself.
	/// </remarks>
	[Serializable]
	public struct LeaderboardEntry
	{
		/// <summary>Competition rank: equal scores share a rank and the next one skips (1, 2, 2, 4).</summary>
		public int Rank;
		public long CharacterID;
		public string CharacterName;
		/// <summary>The ranked number. A <c>long</c>: achievement progress can exceed an <c>int</c>.</summary>
		public long Score;
		/// <summary>Arena boards only: season wins.</summary>
		public int Wins;
		/// <summary>Arena boards only: season losses.</summary>
		public int Losses;
	}

	/// <summary>
	/// Server → Client: one page of a board, how big the board is, and where the recipient stands.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Every field describes the same moment: the page was read from the database
	/// <see cref="AgeSeconds"/> ago and the server has been sharing that read with every player who
	/// asked since. The age is sent rather than a timestamp so the client can show it without
	/// trusting its own clock to agree with the server's.
	/// </para>
	/// <para>
	/// Any peer showing this board at this page may use the reply, whichever panel asked for it;
	/// the rows are the same for everyone. A panel keeps only replies for the board and page it is
	/// currently showing.
	/// </para>
	/// </remarks>
	public struct LeaderboardPageBroadcast : IBroadcast
	{
		public int TemplateID;
		/// <summary>The page served, after clamping.</summary>
		public int Page;
		/// <summary>Pages a client may ask for, as of this read.</summary>
		public int PageCount;
		/// <summary>Every character on the board, not only those anyone may page to.</summary>
		public int TotalRanked;
		/// <summary>Arena boards: the season the rows belong to. Empty otherwise, or when no season is active.</summary>
		public string SeasonName;
		/// <summary>Seconds since the rows were read from the database, when the server sent them.</summary>
		public int AgeSeconds;
		/// <summary>The page's rows, best first. Empty past the end of the board.</summary>
		public LeaderboardEntry[] Entries;
		/// <summary>The recipient's rank, or 0 when they are not on the board.</summary>
		public int YourRank;
		/// <summary>The recipient's score, when ranked.</summary>
		public long YourScore;
		/// <summary>
		/// True when the server could not answer: an unknown board, or the database could not be
		/// read. Every other field is empty. Sent so a panel can say so instead of loading forever.
		/// </summary>
		public bool Unavailable;
	}
}
