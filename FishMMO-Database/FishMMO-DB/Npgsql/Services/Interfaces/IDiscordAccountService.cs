using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// The Discord bot's side of an account: delivering the one verification DM, and the account link.
	/// </summary>
	/// <remarks>
	/// Codes are issued by <see cref="IAccountService.PersistDiscordVerifyCodeAsync"/> and redeemed by
	/// <see cref="IAccountService.PersistVerifiedByCodeAsync"/>, alongside email and SMS. This service
	/// only moves a delivery through claimed, sent or released; see <see cref="DiscordVerification"/>
	/// for why a claim is never reclaimed.
	/// </remarks>
	public interface IDiscordAccountService
	{
		/// <summary>DMs owed and not yet claimed, oldest account first.</summary>
		Task<DatabaseResult<IReadOnlyList<DiscordVerificationDelivery>>> FetchPendingDeliveriesAsync(int limit, CancellationToken cancellationToken = default);

		/// <summary>DMs owed to one Discord username: what the bot asks when a member joins the server.</summary>
		Task<DatabaseResult<IReadOnlyList<DiscordVerificationDelivery>>> FetchPendingDeliveriesForUsernameAsync(string discordUsername, CancellationToken cancellationToken = default);

		/// <summary>
		/// Takes one account's DM to send it. False when it has been sent, is already taken, is no longer
		/// owed (the account verified another way), or has used up its attempts.
		/// </summary>
		Task<DatabaseResult<bool>> ClaimDeliveryAsync(string accountName, CancellationToken cancellationToken = default);

		/// <summary>Records that the DM was delivered, and to which Discord user. No further DM is ever sent for the account.</summary>
		Task<DatabaseResult> MarkDeliveredAsync(string accountName, long discordUserId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Gives a claimed DM back unsent, recording why.
		/// </summary>
		/// <param name="accountName">The account.</param>
		/// <param name="reason">Why it did not send, for staff.</param>
		/// <param name="countAttempt">
		/// True when Discord refused the message, which counts towards <see cref="DiscordVerification.MaxSendAttempts"/>.
		/// False when the player simply is not reachable yet (not in the server), which does not.
		/// </param>
		/// <param name="cancellationToken">Cancellation token.</param>
		Task<DatabaseResult> ReleaseDeliveryAsync(string accountName, string reason, bool countAttempt, CancellationToken cancellationToken = default);

		/// <summary>The account a Discord user is linked to, or null.</summary>
		Task<DatabaseResult<DiscordAccountLink?>> FetchLinkByDiscordUserAsync(long discordUserId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Links an account to a Discord user, replacing any earlier link on that account.
		/// </summary>
		/// <returns><c>UNIQUE_VIOLATION</c> when that Discord user is already linked to a different account.</returns>
		Task<DatabaseResult> LinkAsync(string accountName, long discordUserId, string? discordUsername, CancellationToken cancellationToken = default);

		/// <summary>Removes a Discord user's link. True when there was one.</summary>
		Task<DatabaseResult<bool>> UnlinkAsync(long discordUserId, CancellationToken cancellationToken = default);
	}
}
