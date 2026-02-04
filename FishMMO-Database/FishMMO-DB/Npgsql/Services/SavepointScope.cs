using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Storage;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// Encapsulates savepoint lifecycle management for ambient transaction handling.
	/// Provides automatic rollback on failure and release on success.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This struct is used internally by <see cref="BaseService{TEntity}"/> to simplify
	/// savepoint creation, rollback, and release logic when executing operations
	/// within an existing ambient transaction.
	/// </para>
	/// <para>
	/// If the transaction does not support savepoints, this struct acts as a no-op.
	/// </para>
	/// </remarks>
	internal readonly struct SavepointScope
	{
		private readonly IDbContextTransaction transaction;
		private readonly string savepointName;
		private readonly bool hasSavepoint;

		/// <summary>
		/// Gets a value indicating whether a savepoint was successfully created.
		/// </summary>
		public bool HasSavepoint => hasSavepoint;

		/// <summary>
		/// Initializes a new instance of the <see cref="SavepointScope"/> struct.
		/// </summary>
		/// <param name="transaction">The existing transaction to create a savepoint within.</param>
		/// <param name="savepointName">The name of the savepoint.</param>
		/// <param name="hasSavepoint">True if a savepoint was successfully created.</param>
		private SavepointScope(IDbContextTransaction transaction, string savepointName, bool hasSavepoint)
		{
			this.transaction = transaction;
			this.savepointName = savepointName;
			this.hasSavepoint = hasSavepoint;
		}

		/// <summary>
		/// Creates a savepoint within the given transaction if savepoints are supported.
		/// </summary>
		/// <param name="transaction">The existing transaction.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>A new <see cref="SavepointScope"/> instance.</returns>
		public static async Task<SavepointScope> CreateAsync(IDbContextTransaction transaction, CancellationToken cancellationToken)
		{
			if (transaction == null || !transaction.SupportsSavepoints)
			{
				return new SavepointScope(null, string.Empty, false);
			}

			var savepointName = "sp" + Guid.NewGuid().ToString("N");
			await transaction.CreateSavepointAsync(savepointName, cancellationToken).ConfigureAwait(false);
			return new SavepointScope(transaction, savepointName, true);
		}

		/// <summary>
		/// Rolls back to the savepoint, discarding any changes made after the savepoint.
		/// </summary>
		/// <param name="cancellationToken">Cancellation token.</param>
		public async Task RollbackAsync(CancellationToken cancellationToken)
		{
			if (!hasSavepoint || transaction == null)
			{
				return;
			}

			try
			{
				await transaction.RollbackToSavepointAsync(savepointName, cancellationToken).ConfigureAwait(false);
			}
			catch
			{
				// Ignore rollback failures - connection may be broken
			}
		}

		/// <summary>
		/// Releases the savepoint, indicating the operation succeeded.
		/// </summary>
		/// <param name="cancellationToken">Cancellation token.</param>
		public async Task ReleaseAsync(CancellationToken cancellationToken)
		{
			if (!hasSavepoint || transaction == null)
			{
				return;
			}

			await transaction.ReleaseSavepointAsync(savepointName, cancellationToken).ConfigureAwait(false);
		}
	}
}
