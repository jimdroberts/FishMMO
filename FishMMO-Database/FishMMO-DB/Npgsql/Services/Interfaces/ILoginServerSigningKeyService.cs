using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for managing per-LoginServer HMAC signing keys.
	/// The LoginServer upserts its signing key on startup; WorldServers and SceneServers
	/// fetch the key by LoginServerId to validate auth tokens.
	/// </summary>
	public interface ILoginServerSigningKeyService
	{
		/// <summary>
		/// Inserts or updates the HMAC signing key for a LoginServer.
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
		/// Deletes the signing key for a specific LoginServer.
		/// Called during LoginServer shutdown or key rotation.
		/// </summary>
		Task<DatabaseResult> DeleteAsync(
			long loginServerId,
			CancellationToken cancellationToken = default);
	}
}