using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service for the outbound email queue. Emails are enqueued by the application
	/// and delivered asynchronously by a background SMTP processor.
	/// </summary>
	public interface IEmailQueueService
	{
		/// <summary>
		/// Enqueues an email for asynchronous delivery.
		/// </summary>
		/// <param name="recipientEmail">Recipient email address.</param>
		/// <param name="recipientUsername">Associated account username.</param>
		/// <param name="subject">Email subject line.</param>
		/// <param name="body">Email body (plain-text or HTML).</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		Task<DatabaseResult> EnqueueAsync(
			string recipientEmail,
			string recipientUsername,
			string subject,
			string body,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Returns true if the specified user already has a pending (unsent, unclaimed)
		/// verification email in the queue. Prevents duplicate/spam emails.
		/// </summary>
		Task<DatabaseResult<bool>> HasPendingForUserAsync(
			string recipientUsername,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Atomically claims and dequeues the next pending email using
		/// <c>FOR UPDATE SKIP LOCKED</c>. Safe for concurrent LoginServers.
		/// Throws <see cref="DatabaseEntityNotFoundException"/> if the queue is empty.
		/// </summary>
		/// <param name="claimedBy">Identifier of the claiming server (e.g. server name).</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		Task<DatabaseResult<EmailQueueData>> DequeueNextAsync(
			string claimedBy,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Marks an email as successfully sent.
		/// </summary>
		Task<DatabaseResult> MarkSentAsync(
			long id,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Marks an email delivery attempt as failed and increments the attempt counter.
		/// Emails exceeding <paramref name="maxAttempts"/> are left in the queue
		/// with the error recorded so operators can investigate.
		/// </summary>
		Task<DatabaseResult> MarkFailedAsync(
			long id,
			string error,
			int maxAttempts = 5,
			CancellationToken cancellationToken = default);
	}
}