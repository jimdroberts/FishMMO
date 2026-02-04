using System;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces.Actions;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for party management operations.
	/// Provides async methods for party creation, deletion, and retrieval.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Write operations (Create*, Delete*) in this service use execution strategies to ensure transient
	/// database failures are automatically retried according to the retry policy configured on the DbContext.
	/// Execution is wrapped by BaseService for retries and exception mapping.
	/// </para>
	/// <para>
	/// All methods return <see cref="DatabaseResult"/> or <see cref="DatabaseResult{T}"/> to provide
	/// structured error information through the DatabaseException system, helping distinguish between:
	/// - Validation failures (invalid parameters)
	/// - Not found scenarios (party doesn't exist)
	/// - Database errors (connection issues, constraint violations, timeouts)
	/// - Entity not found errors
	/// - Unexpected runtime errors
	/// </para>
	/// </remarks>
	public interface IPartyService :
		IExistsByKeyAction<long>,
		IDeleteByKeyAction<long>,
		IFetchByKeyAction<long, PartyData>
	{
		/// <summary>
		/// Creates a new party and returns the generated party ID.
		/// </summary>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>A <see cref="DatabaseResult{T}"/> containing the new party ID on success.</returns>
		/// <remarks>
		/// Parties are independent entities that do not belong to a specific account.
		/// Characters join parties through the party membership relationship.
		/// </remarks>
		Task<DatabaseResult<long>> CreateAsync(CancellationToken cancellationToken = default);
	}
}