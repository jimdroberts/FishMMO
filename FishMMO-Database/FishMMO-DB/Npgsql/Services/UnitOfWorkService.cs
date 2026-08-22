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
			public async Task<DatabaseResult> CommitAsync(CancellationToken cancellationToken = default)
			{
				if (isDisposed)
				{
					return DatabaseResult.Failure(DatabaseErrorCodes.ObjectDisposed, "Unit of work is already disposed.");
				}
				if (isCompleted)
				{
					return DatabaseResult.Success();
				}

				try
				{
					await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
					await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
					isCompleted = true;
					return DatabaseResult.Success();
				}
				catch (OperationCanceledException)
				{
					return DatabaseResult.Failure(DatabaseErrorCodes.Canceled, "Operation was canceled.");
				}
				catch (Exception ex)
				{
					return DatabaseResult.Failure(DatabaseErrorCodes.DatabaseError, $"Failed to commit the unit of work ({ExceptionDiagnosticHelper.SanitizeExceptionMessage(ex.Message)}) ({ExceptionDiagnosticHelper.BuildSafeExceptionDiagnostic(ex)}).", isTransient: true);
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
			public async Task<DatabaseResult> RollbackAsync(CancellationToken cancellationToken = default)
			{
				if (isDisposed)
				{
					return DatabaseResult.Failure(DatabaseErrorCodes.ObjectDisposed, "Unit of work is already disposed.");
				}
				if (isCompleted)
				{
					return DatabaseResult.Success();
				}

				try
				{
					await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
					isCompleted = true;
					return DatabaseResult.Success();
				}
				catch (OperationCanceledException)
				{
					return DatabaseResult.Failure(DatabaseErrorCodes.Canceled, "Operation was canceled.");
				}
				catch (Exception ex)
				{
					return DatabaseResult.Failure(DatabaseErrorCodes.DatabaseError, $"Failed to roll back the unit of work ({ExceptionDiagnosticHelper.SanitizeExceptionMessage(ex.Message)}) ({ExceptionDiagnosticHelper.BuildSafeExceptionDiagnostic(ex)}).", isTransient: true);
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
			public DatabaseResult? DisposeFault { get; private set; }

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
						try
						{
							transaction.Rollback();
						}
						catch (Exception ex)
						{
							// Dispose must not throw, so this is recorded rather than raised.
							// Left unrecorded it vanished entirely: a transaction abandoned
							// because the rollback itself failed, with no return value and no
							// logger to say so.
							DisposeFault = DatabaseResult.Failure(
								DatabaseErrorCodes.RollbackFailed,
								"Implicit rollback during disposal failed. Rollback error: " +
								$"{ExceptionDiagnosticHelper.SanitizeExceptionMessage(ex.Message)} ({ex.GetType().Name})");
						}
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
						try
						{
							await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
						}
						catch (Exception ex)
						{
							// See Dispose: recorded rather than raised, for the same reason.
							DisposeFault = DatabaseResult.Failure(
								DatabaseErrorCodes.RollbackFailed,
								"Implicit rollback during disposal failed. Rollback error: " +
								$"{ExceptionDiagnosticHelper.SanitizeExceptionMessage(ex.Message)} ({ex.GetType().Name})");
						}
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
		/// <remarks>
		/// <para>
		/// BeginAsync must be the outermost database scope for the logical operation.
		/// Once started, service methods called within the scope will reuse the ambient context/transaction.
		/// </para>
		/// <para>
		/// THIS METHOD IS DELIBERATELY NOT <c>async</c>. <see cref="DatabaseExecutionScope"/> stores the
		/// ambient context in an <see cref="AsyncLocal{T}"/>, and the async state machine RESTORES the
		/// execution context when the synchronous part of an <c>async</c> method finishes — so a write
		/// to an <c>AsyncLocal</c> made inside an <c>async</c> method is invisible to its caller. While
		/// this method was <c>async</c> the scope it entered was therefore never seen by anyone: every
		/// service call made "inside" the unit of work found no ambient context, created its own, and
		/// committed on its own. The unit of work rolled back an empty transaction and reported
		/// success. Nothing was atomic and nothing said so. (Reproduced against PostgreSQL 18.6: an
		/// ownership assertion issued immediately after <c>BeginAsync</c> could not see the
		/// transaction at all.)
		/// </para>
		/// <para>
		/// Splitting the method in two fixes it: everything up to and including
		/// <see cref="DatabaseExecutionScope.Enter"/> runs synchronously on the caller's execution
		/// context, so the ambient scope is established for the caller, and only the transaction
		/// begin — which sets no ambient state — is awaited. Any future edit that merges these two
		/// halves back into one <c>async</c> method silently reintroduces the bug, so do not.
		/// </para>
		/// </remarks>
		public Task<DatabaseResult<IUnitOfWork>> BeginAsync(CancellationToken cancellationToken = default)
		{
			if (DatabaseExecutionScope.IsActive)
			{
				return Task.FromResult(DatabaseResult<IUnitOfWork>.Failure(
					DatabaseErrorCodes.InvalidOperation,
					"A unit of work cannot be started inside an ambient database execution scope."));
			}

			NpgsqlDbContext context;
			try
			{
				context = dbContextFactory.CreateDbContext();
			}
			catch (Exception ex)
			{
				return Task.FromResult(DatabaseResult<IUnitOfWork>.Failure(DatabaseErrorCodes.DatabaseError, $"Failed to create database context ({ExceptionDiagnosticHelper.SanitizeExceptionMessage(ex.Message)}) ({ExceptionDiagnosticHelper.BuildSafeExceptionDiagnostic(ex)}).", isTransient: true));
			}

			// Must happen here, in the caller's execution context. See the remarks above.
			var scopeToken = DatabaseExecutionScope.Enter(context, isTransactionScope: true);

			return BeginTransactionAsync(context, scopeToken, cancellationToken);
		}

		/// <summary>
		/// Awaits the transaction begin for a scope that has already been entered.
		/// </summary>
		/// <remarks>
		/// Sets no ambient state, which is the only reason it is safe for this half to be
		/// <c>async</c>. See the remarks on <see cref="BeginAsync"/>.
		/// </remarks>
		private static async Task<DatabaseResult<IUnitOfWork>> BeginTransactionAsync(
			NpgsqlDbContext context,
			DatabaseExecutionScope.ScopeToken scopeToken,
			CancellationToken cancellationToken)
		{
			IDbContextTransaction transaction;
			try
			{
				transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				scopeToken.Dispose();
				context.Dispose();
				return DatabaseResult<IUnitOfWork>.Failure(DatabaseErrorCodes.Canceled, "Operation was canceled.");
			}
			catch (Exception ex)
			{
				scopeToken.Dispose();
				context.Dispose();
				return DatabaseResult<IUnitOfWork>.Failure(DatabaseErrorCodes.DatabaseError, $"Failed to begin transaction ({ExceptionDiagnosticHelper.SanitizeExceptionMessage(ex.Message)}) ({ExceptionDiagnosticHelper.BuildSafeExceptionDiagnostic(ex)}).", isTransient: true);
			}

			return DatabaseResult<IUnitOfWork>.Success(new NpgsqlUnitOfWork(context, transaction, scopeToken));
		}
	}
}