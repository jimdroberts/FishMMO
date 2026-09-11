using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// The operator's read of the two queues the shard makes players wait in, and the one
	/// write that unsticks a message.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A read-only service spanning two unrelated tables, separate from
	/// <see cref="IEmailQueueService"/> and <see cref="IGroupFinderQueueService"/>, for the same
	/// reason <see cref="IServerBoardService"/> is separate from the per-tier server services.
	/// Those exist to serve one process doing its job — claim the next email, form the next
	/// group — and their methods are shaped for that: <c>DequeueNextAsync</c> takes a row and
	/// hides it from everyone else, which is precisely what an operator looking at the queue must
	/// not do. This exists to answer "is the queue moving", which is a different question with a
	/// different shape, and folding it into them would hand a login server a method for
	/// enumerating other people's email addresses.
	/// </para>
	/// <para>
	/// <b>Why the email half matters more than it looks.</b> Account verification reaches a
	/// player through <c>email_queue</c> and through nothing else. If no login server is claiming
	/// rows, every new registration silently fails to complete: the site accepts the sign-up, the
	/// row is written, and the mail never arrives. Nothing anywhere raises a hand — no error is
	/// logged, because from the database's point of view nothing went wrong. The age of the
	/// oldest unclaimed row is the only signal that distinguishes that outage from a quiet
	/// Tuesday, which is why <see cref="EmailQueuePage"/> carries it and why it is computed in
	/// the same read as the counts rather than derived from the page.
	/// </para>
	/// <para>
	/// <b>There is no method here that empties a queue.</b> Not an oversight. A queue that can be
	/// cleared from a browser is how the outage above becomes invisible: the rows that prove
	/// registration is broken are the first thing somebody reaches for when the number looks
	/// alarming, and deleting them destroys both the evidence and the emails those accounts are
	/// still owed. The email half has exactly one write — <see cref="RetryEmailAsync"/> — and it
	/// only ever moves a row <em>back into</em> the queue. The group finder half has none at all:
	/// removing a queue row out from under the matching pump is how a character ends up in a
	/// party that nothing will ever transfer them into, and the safe paths for that
	/// (<c>DeleteReturningAsync</c> and the stale sweep) already exist on the game's own service,
	/// where the caller can deal with the consequences.
	/// </para>
	/// </remarks>
	public interface IQueueBoardService
	{
		/// <summary>
		/// One page of the outbound email queue, with per-state counts and the age of the oldest
		/// message nothing has picked up.
		/// </summary>
		/// <remarks>
		/// The counts and the two oldest-row timestamps are computed over the whole table and are
		/// unaffected by <paramref name="state"/>, <paramref name="search"/> or the page: they are
		/// the alarm, and an alarm that changed when the operator narrowed a filter would be
		/// worse than none.
		/// </remarks>
		/// <param name="state">Filter to one state, or null for any. See <see cref="EmailQueueState"/>.</param>
		/// <param name="search">
		/// Case-insensitive substring of the recipient address or the account name, or null for
		/// all. For the question "did this player's mail ever go out", which is the other reason
		/// somebody opens this page.
		/// </param>
		/// <param name="page">1-based page number. Values below 1 are treated as 1.</param>
		/// <param name="pageSize">Rows per page. Clamped by the service.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		Task<DatabaseResult<EmailQueuePage>> FetchEmailQueueAsync(
			EmailQueueState? state,
			string search,
			int page,
			int pageSize,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Puts a claimed email back in the queue by releasing the claim on it.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>This is the only thing "retry" can mean here, and it works because of a defect.</b>
		/// <c>EmailQueueService.MarkFailedAsync</c> increments <c>attempts</c> and records
		/// <c>last_error</c> but never clears <c>claimed_at</c>, and <c>DequeueNextAsync</c> only
		/// ever selects rows where <c>claimed_at IS NULL</c>. So a failed delivery is not retried
		/// by anything, ever, despite the entity and the interface both documenting a retry limit
		/// — the <c>maxAttempts</c> parameter is not read. Until that is fixed in the login
		/// server, releasing the claim from here is the only way a failed verification email is
		/// ever sent, and every row it leaves behind is an account that cannot finish registering.
		/// </para>
		/// <para>
		/// <b>A row merely claimed, with no error, is retryable too.</b> A login server that died
		/// between claiming a message and reporting on it leaves exactly that, and it is stuck
		/// just as permanently and far more quietly than a failure — nothing wrote an error,
		/// because nothing got far enough to have one. Refusing to unstick it would leave the
		/// most likely form of this outage with no remedy on the page built to surface it.
		/// </para>
		/// <para>
		/// <b>What it does not do:</b> it does not send anything — delivery is the login server's,
		/// and the row simply becomes eligible again on that server's next pass — and it does not
		/// reset <c>attempts</c> or clear <c>last_error</c>. Those are the evidence of what went
		/// wrong, and an operator pressing retry a second time needs to see that the first one
		/// came back.
		/// </para>
		/// <para>
		/// Two rows are refused rather than acted on, and both come back as a successful result
		/// carrying <see cref="EmailRetryResult.Refusal"/>: a message already sent, and a message
		/// still unclaimed. Retrying the first would re-send mail somebody already acted on;
		/// "retrying" the second would change nothing, because it is already in line and the
		/// thing it is waiting for is a login server. Neither is a database failure, and both are
		/// worth telling the operator precisely.
		/// </para>
		/// <para>
		/// The read, the guard and the write are one statement, so a login server claiming the
		/// same row at the same moment produces one winner rather than a claim released out from
		/// under a delivery already in flight.
		/// </para>
		/// </remarks>
		/// <param name="id">The email queue row.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>
		/// What happened, including a refusal. A failure result with
		/// <see cref="DatabaseErrorCodes.NotFound"/> when no such row exists.
		/// </returns>
		Task<DatabaseResult<EmailRetryResult>> RetryEmailAsync(
			long id,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// One page of the group finder queue, with per-state counts.
		/// </summary>
		/// <remarks>
		/// Read-only, and there is no companion write. See the remarks on this interface.
		/// </remarks>
		/// <param name="status">Filter by <see cref="Data.Enums.GroupFinderQueueStatus"/>, or null for any.</param>
		/// <param name="page">1-based page number. Values below 1 are treated as 1.</param>
		/// <param name="pageSize">Rows per page. Clamped by the service.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		Task<DatabaseResult<GroupFinderQueuePage>> FetchGroupFinderQueueAsync(
			int? status,
			int page,
			int pageSize,
			CancellationToken cancellationToken = default);
	}
}
