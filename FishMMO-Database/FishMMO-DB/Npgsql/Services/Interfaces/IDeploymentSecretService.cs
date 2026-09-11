using FishMMO.Database.Data;
using System.Threading;
using System.Threading.Tasks;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for managing deployment-global secrets.
	/// Servers load secrets from this table at startup instead of reading env files,
	/// eliminating the need to distribute secrets across machines.
	/// </summary>
	public interface IDeploymentSecretService
	{
		/// <summary>
		/// Fetches a secret value by its key.
		/// </summary>
		/// <param name="key">The logical key identifying the secret.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>A <see cref="DatabaseResult{T}"/> containing the secret value if found; otherwise a failure with ENTITY_NOT_FOUND.</returns>
		/// <summary>
		/// Lists the secrets a deployment holds, <b>without their values</b>.
		/// </summary>
		/// <remarks>
		/// The operator-facing read. <see cref="FetchAsync"/> returns the material itself and is
		/// for the servers that must decrypt with it; this returns only what an operator needs
		/// to answer "is it there, and how old is it". Keeping them as two methods means a
		/// caller has to ask for the value on purpose.
		/// </remarks>
		/// <param name="cancellationToken">Cancellation token.</param>
		Task<DatabaseResult<System.Collections.Generic.IReadOnlyList<DeploymentSecretInfo>>> FetchInventoryAsync(
			CancellationToken cancellationToken = default);

		Task<DatabaseResult<string>> FetchAsync(
			string key,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Inserts or updates a deployment secret.
		/// On insert, a new row is created with the current timestamp.
		/// On update, the value and <c>updated_at</c> are refreshed.
		/// </summary>
		/// <param name="key">The logical key identifying the secret.</param>
		/// <param name="value">The secret value to store.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>A <see cref="DatabaseResult"/> describing the outcome.</returns>
		Task<DatabaseResult> UpsertAsync(
			string key,
			string value,
			CancellationToken cancellationToken = default);
	}
}
