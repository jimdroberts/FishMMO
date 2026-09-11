using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// One message on a support ticket: a reply, or a note only staff can see.
	/// </summary>
	/// <remarks>
	/// <b><see cref="Internal"/> is the field this whole type exists for.</b> Staff need
	/// somewhere to say "this is the third report this week, I am inclined to ban" without the
	/// reporter reading it, and without that they will say it somewhere the ticket cannot
	/// remember. Leaking an internal note is the classic failure of a support system, so the
	/// flag is a column enforced by the read projection on the server, never by whether a view
	/// remembered to hide it.
	/// </remarks>
	public class SupportTicketMessageEntity
	{
		/// <summary>Surrogate key.</summary>
		public long ID { get; set; }

		/// <summary>The ticket this belongs to.</summary>
		public long TicketID { get; set; }

		/// <summary>Navigation to the ticket.</summary>
		public SupportTicketEntity Ticket { get; set; }

		/// <summary>When it was written.</summary>
		public DateTime CreatedUtc { get; set; }

		/// <summary>The account that wrote it.</summary>
		public string AuthorAccount { get; set; }

		/// <summary>
		/// Whether the author was acting as staff.
		/// </summary>
		/// <remarks>
		/// Recorded at write time rather than derived from the author's access level now,
		/// because a game master who is later demoted did not retroactively stop being staff
		/// when they wrote the reply.
		/// </remarks>
		public bool AuthorIsStaff { get; set; }

		/// <summary>Whether this is staff-only. The player never receives it.</summary>
		public bool Internal { get; set; }

		/// <summary>The text.</summary>
		public string Body { get; set; }
	}
}
