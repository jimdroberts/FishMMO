using System;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Delayed two-factor reset requests.
	/// </summary>
	/// <remarks>
	/// For an account that lost both its authenticator and its recovery codes. A request only takes
	/// effect after a waiting period; a normal sign-in cancels it, staff may shorten or cancel it,
	/// and completing it removes the old factor without ever counting as passing one. This service
	/// records the request's lifecycle only — clearing the account's two-factor columns is the
	/// caller's job, and belongs in the same unit of work as <see cref="CompleteAsync"/>.
	/// </remarks>
	public interface ITwoFactorResetRequestService
	{
		/// <summary>
		/// Opens a request that takes effect after <paramref name="delay"/>, or returns the one
		/// already pending.
		/// </summary>
		/// <remarks>
		/// An existing pending request is returned <b>unchanged</b>: asking again never restarts
		/// the clock (which would let an attacker keep the owner's cancellation window open
		/// indefinitely) and never shortens it (which would let them ask their way past it). The
		/// one-pending-per-account rule is a partial unique index, so two simultaneous requests
		/// produce one row and both callers get it.
		/// </remarks>
		/// <param name="accountName">The account; stored lowercase.</param>
		/// <param name="delay">The waiting period. Must be greater than zero.</param>
		/// <param name="requestedIp">Client address that asked, or null.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		Task<DatabaseResult<TwoFactorResetRequestData>> RequestAsync(
			string accountName,
			TimeSpan delay,
			string? requestedIp,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// The account's pending request, or a successful null when there is none.
		/// </summary>
		Task<DatabaseResult<TwoFactorResetRequestData?>> FetchPendingAsync(
			string accountName,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// One request by id, in any state. NOT_FOUND when it does not exist.
		/// </summary>
		Task<DatabaseResult<TwoFactorResetRequestData>> FetchAsync(
			long id,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Cancels the account's pending request, if any.
		/// </summary>
		/// <remarks>
		/// Called by staff, and by the sign-in path whenever the account passes two-factor
		/// normally — that proves the owner still has their factor, which is the case the waiting
		/// period exists to catch. No pending request is not a failure: the sign-in path calls
		/// this unconditionally.
		/// </remarks>
		/// <param name="accountName">The account.</param>
		/// <param name="cancelledBy">The staff account, or the account itself.</param>
		/// <param name="staffReason">Why, for a staff cancellation; null keeps any earlier reason.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>True when a pending request was cancelled, false when there was none.</returns>
		Task<DatabaseResult<bool>> CancelAsync(
			string accountName,
			string cancelledBy,
			string? staffReason,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Brings a pending request's effective time forward.
		/// </summary>
		/// <remarks>
		/// Staff may shorten, never extend: <paramref name="newEffectiveUtc"/> must be earlier
		/// than the current effective time. Extending is a cancel plus a new request, so a longer
		/// wait always starts from a request the history shows. A time in the past is clamped to
		/// now. The reason is required because shortening removes the protection the delay gives.
		/// </remarks>
		/// <param name="id">The request.</param>
		/// <param name="newEffectiveUtc">The new, earlier, effective time.</param>
		/// <param name="shortenedBy">The staff account.</param>
		/// <param name="staffReason">Why. Required, at most 256 characters.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		Task<DatabaseResult> ShortenAsync(
			long id,
			DateTime newEffectiveUtc,
			string shortenedBy,
			string staffReason,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Marks a request completed, if it is still pending, belongs to the account, and has taken
		/// effect.
		/// </summary>
		/// <remarks>
		/// <para>
		/// One conditional UPDATE, so the checks and the mark cannot be separated: a request
		/// cancelled by a normal sign-in a moment earlier cannot then be completed, and two
		/// completions cannot both report success.
		/// </para>
		/// <para>
		/// <b>Completion never satisfies two-factor.</b> The session completing it has proved the
		/// password only. The caller removes the old factor and sends the player to enrol a new
		/// one; it must not treat the sign-in as having passed a second factor.
		/// </para>
		/// </remarks>
		/// <param name="id">The request.</param>
		/// <param name="accountName">The account that must own it.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>True when this call completed it; false when it was not completable.</returns>
		Task<DatabaseResult<bool>> CompleteAsync(
			long id,
			string accountName,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Pending requests, soonest to take effect first. Page size is clamped to 200.
		/// </summary>
		Task<DatabaseResult<TwoFactorResetRequestPage>> SearchPendingAsync(
			int page,
			int pageSize,
			CancellationToken cancellationToken = default);
	}
}
