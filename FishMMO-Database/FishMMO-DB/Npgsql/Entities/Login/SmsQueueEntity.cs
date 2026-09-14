using System;
using System.ComponentModel.DataAnnotations;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Outbound text message queue. Messages are inserted by the application layer and picked up
	/// by a background sender. Failed deliveries are retried up to a configurable maximum.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Shaped exactly like <see cref="EmailQueueEntity"/>, claim columns and all, so one drain
	/// pattern serves both and a player choosing verify-by-SMS goes through the same queue
	/// semantics as one choosing email.
	/// </para>
	/// <para>
	/// There is no SMS provider yet; the sender that drains this table only logs. The queue exists
	/// now so that callers record what would be sent, and adding a provider later changes the
	/// drain rather than every caller.
	/// </para>
	/// </remarks>
	public class SmsQueueEntity : IVersionedEntity
	{
		/// <summary>Primary key.</summary>
		public long ID { get; set; }

		/// <summary>
		/// Application-level concurrency token. Incremented on every write to detect stale updates.
		/// </summary>
		public long Version { get; set; }

		/// <summary>
		/// Recipient number in E.164 form: a <c>+</c> and at most 15 digits, so 16 characters.
		/// </summary>
		[MaxLength(16)]
		public string RecipientPhone { get; set; }

		/// <summary>Associated account username for logging and rate-limiting.</summary>
		public string RecipientUsername { get; set; }

		/// <summary>
		/// Message text (max 480 chars).
		/// </summary>
		/// <remarks>
		/// Bounded because a text message is billed per segment: an unbounded body is an unbounded
		/// bill for one enqueue. 480 is room for a code and a sentence with plenty to spare.
		/// </remarks>
		[MaxLength(480)]
		public string Body { get; set; }

		/// <summary>
		/// What this message is for; see <see cref="SmsKind"/>. Defaults to
		/// <see cref="SmsKind.Verification"/> (0) here and in the database.
		/// </summary>
		public SmsKind Kind { get; set; } = SmsKind.Verification;

		/// <summary>UTC timestamp when the message was enqueued.</summary>
		public DateTime CreatedAt { get; set; }

		/// <summary>UTC timestamp when the message was sent, or null if still pending.</summary>
		public DateTime? SentAt { get; set; }

		/// <summary>Number of delivery attempts so far.</summary>
		public int Attempts { get; set; }

		/// <summary>Identifier of the server that claimed this message for delivery.</summary>
		public string? ClaimedBy { get; set; }

		/// <summary>UTC timestamp when this message was claimed for delivery.</summary>
		public DateTime? ClaimedAt { get; set; }

		/// <summary>Error message from the most recent failed delivery attempt, or null.</summary>
		public string? LastError { get; set; }
	}
}
