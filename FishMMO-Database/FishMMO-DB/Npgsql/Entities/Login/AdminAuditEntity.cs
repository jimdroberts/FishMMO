using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// One operator action, recorded permanently.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Append-only by construction. There is no update path and no delete path on
	/// <c>IAdminAuditService</c>, and no property here that an action could sensibly revise: a
	/// row describes a thing that happened, and what happened does not change afterwards. That
	/// is also why it carries no <c>xmin</c> concurrency token, unlike every other entity in
	/// this assembly — a concurrency token exists to mediate competing writers of one row, and
	/// this row has exactly one writer, once.
	/// </para>
	/// <para>
	/// <b>The actor is a plain string with no foreign key</b>, deliberately, and this is the
	/// single most important decision in the table. A cascade from <c>accounts</c> would mean
	/// that deleting an operator erases the record of everything that operator did, which is
	/// precisely the case the log exists for. The name is copied in, not referenced.
	/// </para>
	/// <para>
	/// Refusals are recorded as well as successes. An operator repeatedly failing to reach
	/// something is more interesting than one who succeeds, and a log that holds only
	/// successes cannot tell the difference between "never attempted" and "attempted and
	/// stopped".
	/// </para>
	/// </remarks>
	public class AdminAuditEntity
	{
		/// <summary>Surrogate key. Ascending, so it also orders ties within a timestamp.</summary>
		public long ID { get; set; }

		/// <summary>When the action was attempted, UTC.</summary>
		public DateTime OccurredUtc { get; set; }

		/// <summary>
		/// The account that acted, copied from <c>accounts.name</c> without a foreign key so the
		/// record survives the account.
		/// </summary>
		public string ActorName { get; set; }

		/// <summary>
		/// The actor's access level at the moment of the action, not now. A later demotion must
		/// not rewrite what the record says they were allowed to do at the time.
		/// </summary>
		public byte ActorAccessLevel { get; set; }

		/// <summary>
		/// The panel session the action came from, or null. Lets a run of actions be tied
		/// together as one sitting even when the session row is long gone.
		/// </summary>
		public long? ActorSessionID { get; set; }

		/// <summary>
		/// What was done, as a stable dotted identifier such as <c>character.rename</c>. Stable
		/// because queries and retention rules are written against it; the human wording belongs
		/// in the interface, not in the data.
		/// </summary>
		public string Action { get; set; }

		/// <summary>What kind of thing was acted on: <c>character</c>, <c>account</c>, and so on.</summary>
		public string TargetType { get; set; }

		/// <summary>
		/// The target's identifier, as text, because targets are keyed by number in some tables
		/// and by name in others and one column has to hold both.
		/// </summary>
		public string TargetID { get; set; }

		/// <summary>
		/// The target's name as it read at the time. Denormalised on purpose: a character renamed
		/// later must not make this row describe the wrong thing.
		/// </summary>
		public string TargetName { get; set; }

		/// <summary>The reason the operator gave. The point of the whole exercise.</summary>
		public string Reason { get; set; }

		/// <summary>Whether the action was carried out.</summary>
		public bool Succeeded { get; set; }

		/// <summary>Why it was not, when it was not.</summary>
		public string Outcome { get; set; }

		/// <summary>
		/// What actually changed, as compact JSON, or null. Free-form because the shape differs
		/// per action; it is for a human reading one row, never for querying.
		/// </summary>
		public string Details { get; set; }

		/// <summary>The address the request came from, as the panel resolved it.</summary>
		public string IpAddress { get; set; }

		/// <summary>
		/// Where the action was taken: <c>panel</c> or <c>game</c>.
		/// </summary>
		/// <remarks>
		/// Both write to this one table deliberately. An operator who cannot do something in the
		/// panel and then does it from a chat command should not thereby leave a gap in the
		/// record, and "show me everything this person did" must not require two queries against
		/// two logs with two different shapes.
		/// </remarks>
		public string Source { get; set; }
	}
}
