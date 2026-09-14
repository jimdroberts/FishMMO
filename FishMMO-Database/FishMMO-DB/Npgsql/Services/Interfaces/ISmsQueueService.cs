using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service for the outbound SMS queue. Messages are enqueued by the application and delivered
	/// asynchronously by a background sender. The method-for-method twin of
	/// <see cref="IEmailQueueService"/>.
	/// </summary>
	/// <remarks>
	/// No SMS provider exists yet; the sender that drains this queue only logs.
	/// </remarks>
	public interface ISmsQueueService
	{
		/// <summary>
		/// Enqueues a text message for asynchronous delivery.
		/// </summary>
		/// <param name="recipientPhone">Recipient number in E.164 form: <c>+</c> then up to 15 digits.</param>
		/// <param name="recipientUsername">Associated account username.</param>
		/// <param name="body">Message text, at most 480 characters.</param>
		/// <param name="kind">What the message is for. Defaults to <see cref="SmsKind.Verification"/>, the column default.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		Task<DatabaseResult> EnqueueAsync(
			string recipientPhone,
			string recipientUsername,
			string body,
			SmsKind kind = SmsKind.Verification,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Returns true if the specified user already has an unsent message of
		/// <paramref name="kind"/> in the queue. Prevents duplicate messages piling up.
		/// </summary>
		/// <remarks>
		/// <b>Pass the kind you are about to send.</b> A blind check answers a different question:
		/// a pending notification would suppress the verification code a player is waiting for.
		/// Null means any kind, and is almost never what a caller wants.
		/// </remarks>
		Task<DatabaseResult<bool>> HasPendingForUserAsync(
			string recipientUsername,
			SmsKind? kind = null,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Atomically claims and dequeues the next pending message using
		/// <c>FOR UPDATE SKIP LOCKED</c>. Safe for concurrent senders. Returns a NOT_FOUND failure
		/// when the queue is empty.
		/// </summary>
		/// <param name="claimedBy">Identifier of the claiming server (e.g. server name).</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		Task<DatabaseResult<SmsQueueData>> DequeueNextAsync(
			string claimedBy,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Marks a message as successfully sent.
		/// </summary>
		Task<DatabaseResult> MarkSentAsync(
			long id,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Marks a delivery attempt as failed and increments the attempt counter.
		/// </summary>
		/// <remarks>
		/// While attempts remain the claim is released, which is what puts the message back in
		/// line for another sender to pick up. Once <paramref name="maxAttempts"/> is reached the
		/// claim is kept, which takes the message out of circulation and leaves it in the queue
		/// with its error for an operator to investigate or retry by hand.
		/// </remarks>
		Task<DatabaseResult> MarkFailedAsync(
			long id,
			string error,
			int maxAttempts = 5,
			CancellationToken cancellationToken = default);
	}
}
