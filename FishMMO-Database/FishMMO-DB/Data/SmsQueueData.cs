using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Data transfer object for a pending text message in the outbound SMS queue.
	/// </summary>
	public readonly struct SmsQueueData
	{
		/// <summary>Primary key.</summary>
		public readonly long ID;

		/// <summary>Recipient number in E.164 form.</summary>
		public readonly string RecipientPhone;

		/// <summary>Associated account username.</summary>
		public readonly string RecipientUsername;

		/// <summary>Message text.</summary>
		public readonly string Body;

		/// <summary>What the message is for.</summary>
		public readonly SmsKind Kind;

		/// <summary>When it was enqueued.</summary>
		public readonly DateTime CreatedAt;

		/// <summary>Delivery attempts so far.</summary>
		public readonly int Attempts;

		/// <summary>The server that claimed it for delivery.</summary>
		public readonly string? ClaimedBy;

		/// <summary>When it was claimed.</summary>
		public readonly DateTime? ClaimedAt;

		/// <summary>Creates a queue row DTO.</summary>
		public SmsQueueData(long id, string recipientPhone, string recipientUsername,
			string body, DateTime createdAt, int attempts,
			string? claimedBy, DateTime? claimedAt, SmsKind kind = SmsKind.Verification)
		{
			ID = id;
			RecipientPhone = recipientPhone;
			RecipientUsername = recipientUsername;
			Body = body;
			CreatedAt = createdAt;
			Attempts = attempts;
			ClaimedBy = claimedBy;
			ClaimedAt = claimedAt;
			Kind = kind;
		}
	}
}
