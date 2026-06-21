using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Data transfer object for a pending email in the outbound queue.
	/// </summary>
	public struct EmailQueueData
	{
		public readonly long ID;
		public readonly string RecipientEmail;
		public readonly string RecipientUsername;
		public readonly string Subject;
		public readonly string Body;
		public readonly DateTime CreatedAt;
		public readonly int Attempts;
		public readonly string? ClaimedBy;
		public readonly DateTime? ClaimedAt;

		public EmailQueueData(long id, string recipientEmail, string recipientUsername,
			string subject, string body, DateTime createdAt, int attempts,
			string? claimedBy, DateTime? claimedAt)
		{
			ID = id;
			RecipientEmail = recipientEmail;
			RecipientUsername = recipientUsername;
			Subject = subject;
			Body = body;
			CreatedAt = createdAt;
			Attempts = attempts;
			ClaimedBy = claimedBy;
			ClaimedAt = claimedAt;
		}
	}
}