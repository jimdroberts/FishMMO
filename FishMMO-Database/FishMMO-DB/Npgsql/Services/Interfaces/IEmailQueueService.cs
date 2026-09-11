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
		/// <param name="kind">
		/// What the mail is for. Defaults to <see cref="EmailKind.Verification"/>, which is both
		/// the column default and the meaning every row had before kinds existed, so a caller
		/// that does not care keeps the behaviour it always had. A password reset MUST pass
		/// <see cref="EmailKind.PasswordReset"/>: the drain stamps
		/// <c>verification_email_sent_at</c> after delivering a verification mail, and stamping
		/// it for a reset would end an unverified account's grace period — locking the player out
		/// of the game and the panel at the moment they recovered their password.
		/// </param>
		/// <param name="cancellationToken">Cancellation token.</param>
		Task<DatabaseResult> EnqueueAsync(
			string recipientEmail,
			string recipientUsername,
			string subject,
			string body,
			EmailKind kind = EmailKind.Verification,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Returns true if the specified user already has an unsent email of
		/// <paramref name="kind"/> in the queue. Prevents duplicate mail piling up.
		/// </summary>
		/// <remarks>
		/// <b>Pass the kind you are about to send.</b> The queue carries more than one kind, so
		/// a blind check answers a different question than the caller asked: a pending password
		/// reset would suppress the verification mail an unverified player is waiting for.
		/// Null means any kind, and is almost never what a caller wants.
		/// </remarks>
		Task<DatabaseResult<bool>> HasPendingForUserAsync(
			string recipientUsername,
			EmailKind? kind = null,
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
		/// </summary>
		/// <remarks>
		/// While attempts remain the claim is released, which is what puts the message back in
		/// line for another login server to pick up. Once <paramref name="maxAttempts"/> is
		/// reached the claim is kept, which takes the message out of circulation and leaves it
		/// in the queue with its error for an operator to investigate or retry by hand.
		/// </remarks>
		Task<DatabaseResult> MarkFailedAsync(
			long id,
			string error,
			int maxAttempts = 5,
			CancellationToken cancellationToken = default);
	}
}