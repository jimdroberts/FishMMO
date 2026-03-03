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
		/// <param name="recipientCharacterId">The recipient character ID.</param>
		/// <param name="subject">The mail subject.</param>
		/// <param name="message">The mail body text.</param>
		/// <param name="itemAttachmentTemplateID">The attached item template ID (0 for none).</param>
		/// <param name="itemAttachmentSeed">The attached item seed (0 for none).</param>
		/// <param name="itemAttachmentAmount">The attached item amount (0 for none).</param>
		/// <param name="incomingVersion">The authoritative, monotonic version for this persist operation.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>A <see cref="DatabaseResult"/> indicating success or failure.</returns>
		Task<DatabaseResult> SendAsync(
			long senderCharacterId,
			long recipientCharacterId,
			string subject,
			string message,
			int itemAttachmentTemplateID,
			int itemAttachmentSeed,
			uint itemAttachmentAmount,
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