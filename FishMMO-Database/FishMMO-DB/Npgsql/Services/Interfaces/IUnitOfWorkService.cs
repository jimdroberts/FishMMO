using System.Threading;
using System.Threading.Tasks;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Creates <see cref="IUnitOfWork"/> scopes to coordinate multiple service calls
	/// into a single explicit database transaction.
	/// </summary>
	/// <remarks>
	/// A unit of work establishes an ambient <c>NpgsqlDbContext</c> and an explicit transaction for the
	/// current logical async flow. Services invoked inside the scope reuse that context and must not commit
	/// independently; the transaction is finalized by calling <see cref="IUnitOfWork.CommitAsync"/> or
	/// <see cref="IUnitOfWork.RollbackAsync"/>.
	/// </remarks>
	public interface IUnitOfWorkService
	{
		/// <summary>
		/// Begins a new unit of work for the current logical async flow.
		/// </summary>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>An active <see cref="IUnitOfWork"/>.</returns>
		/// <remarks>
		/// The returned unit of work establishes an ambient <c>NpgsqlDbContext</c>.
		/// Service calls made within the scope will reuse that context and transaction.
		/// </remarks>
		Task<IUnitOfWork> BeginAsync(CancellationToken cancellationToken = default);
	}
}