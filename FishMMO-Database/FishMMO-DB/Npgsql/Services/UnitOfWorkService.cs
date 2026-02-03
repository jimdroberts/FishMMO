using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Storage;
using FishMMO.Database.Npgsql.Services.Interfaces;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// Npgsql implementation of <see cref="IUnitOfWorkService"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This service begins an explicit database transaction and establishes an ambient
	/// <see cref="NpgsqlDbContext"/> for the current logical async flow.
	/// Any Npgsql service method invoked inside the unit of work will reuse the ambient context
	/// via <c>DatabaseExecutionScope</c> and therefore participate in the same transaction.
	/// </para>
	/// <para>
	/// The transaction is finalized only when <see cref="IUnitOfWork.CommitAsync"/> or
	/// <see cref="IUnitOfWork.RollbackAsync"/> is called.
	/// </para>
	/// <para>
	/// Raw SQL statements execute immediately against the database connection but remain uncommitted
	/// until the unit of work commits (or are discarded on rollback).
	/// </para>
	/// </remarks>
	public sealed class UnitOfWorkService : IUnitOfWorkService
	{
		private sealed class NpgsqlUnitOfWork : IUnitOfWork
		{
			private readonly NpgsqlDbContext dbContext;
			private readonly IDbContextTransaction transaction;
			private readonly DatabaseExecutionScope.ScopeToken scopeToken;
			private bool isCompleted;
			private bool isDisposed;

			/// <summary>
			/// Initializes a new instance of the <see cref="NpgsqlUnitOfWork"/> class.
			/// </summary>
			/// <param name="dbContext">The ambient EF Core context used for all operations in the unit of work.</param>
			/// <param name="transaction">The explicit transaction associated with <paramref name="dbContext"/>.</param>
			/// <param name="scopeToken">The ambient scope token that must be disposed when the unit of work completes.</param>
			/// <exception cref="ArgumentNullException">
			/// Thrown when <paramref name="dbContext"/> or <paramref name="transaction"/> is null.
			/// </exception>
			public NpgsqlUnitOfWork(NpgsqlDbContext dbContext, IDbContextTransaction transaction, DatabaseExecutionScope.ScopeToken scopeToken)
			{
				this.dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
				this.transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
				this.scopeToken = scopeToken;
			}

			/// <summary>
			/// Saves any tracked EF Core changes and commits the underlying database transaction.
			/// </summary>
			/// <param name="cancellationToken">Token to cancel the operation.</param>
			/// <remarks>
			/// This is a one-way operation. After commit (or rollback), the unit of work is disposed
			/// and cannot be used again.
			/// </remarks>
			public async Task CommitAsync(CancellationToken cancellationToken = default)
			{
				ThrowIfDisposed();
				if (isCompleted)
				{
					return;
				}

				try
				{
					await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
					await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
					isCompleted = true;
				}
				finally
				{
					await DisposeAsync().ConfigureAwait(false);
				}
			}

			/// <summary>
			/// Rolls back the underlying database transaction.
			/// </summary>
			/// <param name="cancellationToken">Token to cancel the operation.</param>
			/// <remarks>
			/// This is a one-way operation. After rollback (or commit), the unit of work is disposed
			/// and cannot be used again.
			/// </remarks>
			public async Task RollbackAsync(CancellationToken cancellationToken = default)
			{
				ThrowIfDisposed();
				if (isCompleted)
				{
					return;
				}

				try
				{
					await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
					isCompleted = true;
				}
				finally
				{
					await DisposeAsync().ConfigureAwait(false);
				}
			}

			/// <summary>
			/// Disposes the unit of work.
			/// </summary>
			/// <remarks>
			/// If neither <see cref="CommitAsync"/> nor <see cref="RollbackAsync"/> has been called,
			/// the transaction is rolled back as a safety measure.
			/// </remarks>
			public void Dispose()
			{
				if (isDisposed)
				{
					return;
				}

				try
				{
					if (!isCompleted)
					{
						try { transaction.Rollback(); } catch { }
						isCompleted = true;
					}
				}
				finally
				{
					try { transaction.Dispose(); } catch { }
					try { dbContext.Dispose(); } catch { }
					scopeToken.Dispose();
					isDisposed = true;
				}
			}

			/// <summary>
			/// Asynchronously disposes the unit of work.
			/// </summary>
			/// <remarks>
			/// If neither <see cref="CommitAsync"/> nor <see cref="RollbackAsync"/> has been called,
			/// the transaction is rolled back as a safety measure.
			/// </remarks>
			public async ValueTask DisposeAsync()
			{
				if (isDisposed)
				{
					return;
				}

				try
				{
					if (!isCompleted)
					{
						try { await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
						isCompleted = true;
					}
				}
				finally
				{
					try { await transaction.DisposeAsync().ConfigureAwait(false); } catch { }
					try { await dbContext.DisposeAsync().ConfigureAwait(false); } catch { }
					scopeToken.Dispose();
					isDisposed = true;
				}
			}

			private void ThrowIfDisposed()
			{
				if (isDisposed)
				{
					throw new ObjectDisposedException(nameof(NpgsqlUnitOfWork));
				}
			}
		}

		private readonly INpgsqlDbContextFactory dbContextFactory;

		/// <summary>
		/// Initializes a new instance of the <see cref="UnitOfWorkService"/> class.
		/// </summary>
		/// <param name="dbContextFactory">Factory for creating database contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public UnitOfWorkService(INpgsqlDbContextFactory dbContextFactory)
		{
			this.dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
		}

		/// <inheritdoc/>
		/// <exception cref="InvalidOperationException">
		/// Thrown when a unit of work is started inside an existing ambient database execution scope.
		/// </exception>
		/// <remarks>
		/// BeginAsync must be the outermost database scope for the logical operation.
		/// Once started, service methods called within the scope will reuse the ambient context/transaction.
		/// </remarks>
		public async Task<IUnitOfWork> BeginAsync(CancellationToken cancellationToken = default)
		{
			if (DatabaseExecutionScope.IsActive)
			{
				throw new InvalidOperationException(
					"A unit of work cannot be started inside an ambient database execution scope. " +
					"Ensure BeginAsync is the outermost scope for the logical operation.");
			}

			var context = dbContextFactory.CreateDbContext();
			var scopeToken = DatabaseExecutionScope.Enter("UnitOfWork", context, isTransactionScope: true);

			IDbContextTransaction transaction;
			try
			{
				transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
			}
			catch
			{
				scopeToken.Dispose();
				context.Dispose();
				throw;
			}

			return new NpgsqlUnitOfWork(context, transaction, scopeToken);
		}
	}
}