using System;
using System.ComponentModel.DataAnnotations;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Outbound email queue. Emails are inserted by the application layer and
	/// picked up by a background processor that delivers them via SMTP.
	/// Failed deliveries are retried up to a configurable maximum.
	/// </summary>
	public class EmailQueueEntity : IVersionedEntity
	{
		/// <summary>Primary key.</summary>
		public long ID { get; set; }

		/// <summary>
		/// Application-level concurrency token. Incremented on every write to detect stale updates.
		/// </summary>
		public long Version { get; set; }

		/// <summary>Recipient email address (max 320 chars).</summary>
		[MaxLength(320)]
		public string RecipientEmail { get; set; }

		/// <summary>Associated account username for logging and rate-limiting.</summary>
		public string RecipientUsername { get; set; }

		/// <summary>Email subject line (max 256 chars).</summary>
		[MaxLength(256)]
		public string Subject { get; set; }

		/// <summary>Plain-text or HTML email body.</summary>
		public string Body { get; set; }

		/// <summary>UTC timestamp when the email was enqueued.</summary>
		public DateTime CreatedAt { get; set; }

		/// <summary>UTC timestamp when the email was successfully sent, or null if still pending.</summary>
		public DateTime? SentAt { get; set; }

		/// <summary>Number of delivery attempts so far.</summary>
		public int Attempts { get; set; }

		/// <summary>Identifier of the LoginServer that claimed this email for delivery (server name).</summary>
		public string? ClaimedBy { get; set; }

		/// <summary>UTC timestamp when this email was claimed for delivery.</summary>
		public DateTime? ClaimedAt { get; set; }
		/// <summary>Error message from the most recent failed delivery attempt, or null.</summary>
		public string? LastError { get; set; }
	}
}