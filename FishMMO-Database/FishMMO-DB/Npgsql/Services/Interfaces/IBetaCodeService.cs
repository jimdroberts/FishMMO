using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Beta codes and the accounts that redeemed them.
	/// </summary>
	/// <remarks>
	/// Staff mint codes per program; a player redeems one at registration or later; while beta
	/// mode is on, the login gate asks <see cref="HasAccessAsync"/>. The link an account gets is
	/// permanent. What ends access is revoking the code, never deleting anything.
	/// </remarks>
	public interface IBetaCodeService
	{
		/// <summary>
		/// Mints a batch of codes under one program, in one transaction.
		/// </summary>
		/// <remarks>
		/// Either every code in the batch is created or none is. A generated code that collides
		/// with an existing one is regenerated (a bounded number of times) instead of failing the
		/// batch; at 2^60 possible codes that path exists for correctness, not because it runs.
		/// </remarks>
		/// <param name="program">Program name; normalised to lowercase, 1–32 of <c>[a-z0-9_-]</c>.</param>
		/// <param name="count">How many codes, 1–500.</param>
		/// <param name="maxUses">How many accounts each code admits, 1–100000.</param>
		/// <param name="expiresUtc">When the codes stop being redeemable, or null. Must be in the future.</param>
		/// <param name="createdBy">The staff account minting them.</param>
		/// <param name="note">Optional staff note, at most 256 characters.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>The minted codes.</returns>
		Task<DatabaseResult<IReadOnlyList<BetaCodeData>>> MintAsync(
			string program,
			int count,
			int maxUses,
			DateTime? expiresUtc,
			string createdBy,
			string? note,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Lists codes, newest first. Page size is clamped to 200.
		/// </summary>
		Task<DatabaseResult<BetaCodePage>> SearchAsync(
			BetaCodeQuery query,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Totals for every program that has codes, ordered by name.
		/// </summary>
		Task<DatabaseResult<IReadOnlyList<BetaProgramSummary>>> ListProgramsAsync(
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Revokes a code, ending redemption of it and the access of every account that redeemed it.
		/// </summary>
		/// <remarks>
		/// One conditional UPDATE on <c>revoked_utc IS NULL</c>, so the first revocation's who and
		/// when are never overwritten by a second. An unknown id fails NOT_FOUND; an already
		/// revoked code fails VALIDATION_ERROR.
		/// </remarks>
		Task<DatabaseResult> RevokeAsync(
			long id,
			string revokedBy,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Redeems a code for an account: takes one use and records the permanent link, atomically.
		/// </summary>
		/// <remarks>
		/// <para>
		/// An unknown, revoked, expired or used-up code all fail with the same NOT_FOUND and the
		/// same message, "That beta code is not valid.", and a malformed code fails with that
		/// message too. Registration calls this anonymously, so any difference between those
		/// answers would let a script tell a real code from a guess. The caller must pass the
		/// message through unaltered and must not add its own distinctions.
		/// </para>
		/// <para>
		/// Redeeming a code the account already holds fails VALIDATION_ERROR and consumes nothing.
		/// The use is taken by one conditional UPDATE (<c>use_count &lt; max_uses</c>, not revoked,
		/// not expired), and that WHERE clause is the concurrency control: two accounts racing for
		/// a code's last use cannot both succeed.
		/// </para>
		/// </remarks>
		/// <param name="accountName">The redeeming account; stored lowercase.</param>
		/// <param name="code">The code as the player typed it.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		Task<DatabaseResult<AccountBetaCodeData>> RedeemAsync(
			string accountName,
			string code,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Whether the account holds a non-revoked code of any of the given programs.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Expiry does not end access; revocation does.</b> A code's expiry is a deadline for
		/// redeeming it, so an account that redeemed in time stays admitted after the date passes.
		/// Ending a tester's access is done by revoking the code.
		/// </para>
		/// <para>
		/// Program names are normalised; a null or empty list, or one with no valid names, is
		/// false without querying — a gate configured with no programs admits nobody by code.
		/// </para>
		/// </remarks>
		/// <param name="accountName">The account.</param>
		/// <param name="programs">The programs currently active.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		Task<DatabaseResult<bool>> HasAccessAsync(
			string accountName,
			IReadOnlyCollection<string> programs,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Every code the account has redeemed, oldest first, with whether each has been revoked.
		/// </summary>
		Task<DatabaseResult<IReadOnlyList<AccountBetaCodeData>>> FetchForAccountAsync(
			string accountName,
			CancellationToken cancellationToken = default);
	
		/// <summary>
		/// Whether a code could be redeemed right now for one of <paramref name="programs"/>, without
		/// redeeming it.
		/// </summary>
		/// <remarks>
		/// For registration, which must refuse a bad code BEFORE it creates the account: redeeming first
		/// would attach a use to an account name the insert may then find already taken — handing beta
		/// access to whoever owns that name. A check can race the redeem that follows it; the redeem is
		/// still the authority, and an account whose redeem loses that race exists without access and
		/// can redeem another code. An empty or null <paramref name="programs"/> means any program. A
		/// malformed code is simply not redeemable.
		/// </remarks>
		Task<DatabaseResult<bool>> CheckRedeemableAsync(string code, IReadOnlyCollection<string> programs, CancellationToken cancellationToken = default);
}
}
