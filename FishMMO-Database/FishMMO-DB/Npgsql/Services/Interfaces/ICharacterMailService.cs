using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces.Actions;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for managing character mail in the database.
	/// </summary>
	/// <remarks>
	/// Mail deletion is expected to be version-gated via the logical <c>Version</c>
	/// so stale updates are rejected and newer authoritative updates win.
	/// </remarks>
	public interface ICharacterMailService :
		ICountByKeyAction<long>,
		IDeleteByKeyVersionedAction<long>,
		IFetchCollectionByKeyAction<long, CharacterMailData>
	{
		/// <summary>
		/// Sends a new mail message from one character to another.
		/// </summary>
		/// <param name="senderCharacterId">The sending character ID.</param>
		/// <param name="senderName">The display name of the sender.</param>
		/// <param name="recipientCharacterId">The recipient character ID.</param>
		/// <param name="subject">The mail subject.</param>
		/// <param name="body">The mail body text.</param>
		/// <param name="itemAttachmentTemplateID">The attached item template ID (0 for none).</param>
		/// <param name="itemAttachmentSeed">The attached item seed (0 for none).</param>
		/// <param name="itemAttachmentAmount">The attached item amount (0 for none).</param>
		/// <param name="incomingVersion">The authoritative, monotonic version for this persist operation.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>A <see cref="DatabaseResult"/> indicating success or failure.</returns>
		Task<DatabaseResult> SendAsync(
			long senderCharacterId,
			string senderName,
			long recipientCharacterId,
			string subject,
			string body,
			int itemAttachmentTemplateID,
			int itemAttachmentSeed,
			uint itemAttachmentAmount,
			int currencyAttachment,
			long incomingVersion,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Takes the attachment off one mail, returning what was on it.
		/// </summary>
		/// <param name="mailId">The mail to claim from.</param>
		/// <param name="characterId">The owning character ID, for authorization.</param>
		/// <param name="incomingVersion">The authoritative, monotonic version for this write.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>
		/// The attachment as it stood before the clear, or no data when the mail had nothing
		/// attached, does not belong to this character, or has already been claimed.
		/// </returns>
		/// <remarks>
		/// <para>
		/// <b>Read and clear are one statement.</b> The attachment columns are zeroed and their
		/// previous values returned by the same <c>UPDATE</c>, whose <c>WHERE</c> additionally
		/// requires that something is still attached. Two claims racing on one mail therefore
		/// serialise on the row lock and only the first affects a row — the second matches nothing
		/// and returns no data. Fetching the mail and then clearing it would leave a window in
		/// which both reads see the same item and both callers grant it.
		/// </para>
		/// <para>
		/// The caller grants what comes back. That ordering means a crash between the clear and the
		/// grant loses the attachment rather than duplicating it, which is the correct direction
		/// for an authoritative server — the same reasoning the merchant and corpse loot paths use.
		/// </para>
		/// </remarks>
		Task<DatabaseResult<CharacterMailAttachmentData?>> ClaimAttachmentAsync(
			long mailId,
			long characterId,
			long incomingVersion,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Deletes a specific mail message by its ID if <paramref name="incomingVersion"/> is newer.
		/// </summary>
		/// <param name="mailId">The mail ID to delete.</param>
		/// <param name="characterId">The owning character ID (for authorization).</param>
		/// <param name="incomingVersion">The authoritative, monotonic version for this delete operation.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>A <see cref="DatabaseResult"/> indicating success or failure.</returns>
		Task<DatabaseResult> DeleteAsync(long mailId, long characterId, long incomingVersion, CancellationToken cancellationToken = default);
	}
}