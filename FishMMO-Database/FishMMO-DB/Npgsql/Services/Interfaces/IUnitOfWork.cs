using System;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Represents a unit of work for coordinating multiple service calls into a single database transaction.
	/// </summary>
	/// <remarks>
	/// A unit of work creates a shared <c>NpgsqlDbContext</c> and an explicit database transaction.
	/// Services invoked within the unit of work reuse the ambient context and do not commit independently.
	/// The transaction is finalized only when <see cref="CommitAsync"/> or <see cref="RollbackAsync"/> is called.
	/// </remarks>
	public interface IUnitOfWork : IDisposable, IAsyncDisposable
	{
		/// <summary>
		/// Persists any tracked EF Core changes and commits the underlying transaction.
		/// </summary>
		Task<DatabaseResult> CommitAsync(CancellationToken cancellationToken = default);

		/// <summary>
		/// Rolls back the underlying transaction.
		/// </summary>
		Task<DatabaseResult> RollbackAsync(CancellationToken cancellationToken = default);
	}
}