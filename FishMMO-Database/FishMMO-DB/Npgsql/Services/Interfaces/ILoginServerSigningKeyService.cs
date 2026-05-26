using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for managing per-LoginServer HMAC signing keys.
	/// The LoginServer inserts a fresh signing key on startup; WorldServers and SceneServers
	/// fetch the key by token-embedded key ID to validate auth tokens.
	/// </summary>
	public interface ILoginServerSigningKeyService
	{
		/// <summary>
		/// Inserts a fresh HMAC signing key for a LoginServer and returns its database ID.
		/// Called by the LoginServer on startup.
		/// </summary>
		Task<DatabaseResult<LoginServerSigningKeyData>> UpsertAsync(
			long loginServerId,
			byte[] hmacKey,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Fetches the signing key for a specific LoginServer.
		/// Called by WorldServers and SceneServers during token validation.
		/// </summary>
		Task<DatabaseResult<LoginServerSigningKeyData>> FetchByLoginServerIdAsync(
			long loginServerId,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Fetches a signing key by its token-embedded database ID.
		/// </summary>
		Task<DatabaseResult<LoginServerSigningKeyData>> FetchByIdAsync(
			long signingKeyId,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Deletes the signing key for a specific LoginServer.
		/// Called during LoginServer shutdown or key rotation.
		/// </summary>
		Task<DatabaseResult> DeleteAsync(
			long loginServerId,
			CancellationToken cancellationToken = default);
	}
}