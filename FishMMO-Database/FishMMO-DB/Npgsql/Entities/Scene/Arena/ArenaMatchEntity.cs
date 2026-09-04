using System;
using System.Collections.Generic;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// One arena match: the instance it runs in, the format it was formed for, and its outcome.
	/// </summary>
	/// <remarks>
	/// Created inside the same transaction that takes the players out of the group finder queue
	/// and opens the instance, so a match row always has its instance and its members, and the
	/// scene server that ends up hosting the instance can read the whole match from this table
	/// without having taken part in forming it. Ended matches are kept as history.
	/// </remarks>
	public class ArenaMatchEntity
	{
		/// <summary>Primary key.</summary>
		public long ID { get; set; }

		/// <summary>World server the match belongs to.</summary>
		public long WorldServerID { get; set; }

		/// <summary>The <c>scenes</c> row of the instance the match runs in.</summary>
		public long InstanceID { get; set; }

		/// <summary>Arena scene name.</summary>
		public string SceneName { get; set; }

		/// <summary>Arena template ID, so the hosting server can resolve the rules.</summary>
		public int TemplateID { get; set; }

		/// <summary>Format index into the template's own list.</summary>
		public int Format { get; set; }

		/// <summary>Teams in the match.</summary>
		public int TeamCount { get; set; }

		/// <summary>Seats per team.</summary>
		public int TeamSize { get; set; }

		/// <summary>Where the match is in its life. See <c>ArenaMatchStatus</c>.</summary>
		public int Status { get; set; }

		/// <summary>Winning team index, or -1 when undecided or drawn.</summary>
		public int WinnerTeam { get; set; }

		/// <summary>Whether this match moves ratings.</summary>
		public bool Ranked { get; set; }

		/// <summary>Season the match counts towards, or 0 for an unranked match.</summary>
		public long SeasonID { get; set; }

		/// <summary>Until when a vacated seat may be filled from the queue (UTC), or null.</summary>
		public DateTime? BackfillUntilUtc { get; set; }

		/// <summary>When the match was formed (UTC).</summary>
		public DateTime TimeCreated { get; set; }

		/// <summary>When play began (UTC), or null.</summary>
		public DateTime? TimeStarted { get; set; }

		/// <summary>When the match ended or was cancelled (UTC), or null.</summary>
		public DateTime? TimeEnded { get; set; }

		/// <summary>Navigation collection of members.</summary>
		public List<ArenaMatchMemberEntity> Members { get; set; }
	}
}
