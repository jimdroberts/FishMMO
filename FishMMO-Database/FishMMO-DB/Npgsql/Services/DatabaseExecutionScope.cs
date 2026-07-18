using System;
using System.Runtime.CompilerServices;
using System.Threading;
using FishMMO.Database.Exceptions;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// Guards against nested database execution scopes within the same logical async flow.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This guard must be shared across all services, regardless of <c>TEntity</c> generic arguments.
	/// Therefore it is implemented as a non-generic static holder.
	/// </para>
	/// </remarks>
	internal static class DatabaseExecutionScope
	{
		private enum ExecutionMode
		{
			ReadOnly = 0,
			Write = 1,
			Transaction = 2,
		}

		private sealed class ScopeState
		{
			public int Depth;
			public ExecutionMode Mode;
			public NpgsqlDbContext? DbContext;
		}

		private static readonly AsyncLocal<ScopeState?> State = new AsyncLocal<ScopeState?>();

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool TryGetCurrentDbContext(out NpgsqlDbContext dbContext)
		{
			var state = State.Value;
			if (state?.DbContext != null)
			{
				dbContext = state.DbContext;
				return true;
			}

			dbContext = null!;
			return false;
		}

		public static bool IsActive => State.Value?.Depth > 0;

		/// <summary>
		/// Enters a database execution scope for the current logical async flow.
		/// </summary>
		/// <returns>A token that must be disposed to exit the scope.</returns>
		/// <exception cref="DatabaseException">
		/// Thrown when a write scope is requested while the ambient scope is read-only.
		/// </exception>
		public static ScopeToken Enter(NpgsqlDbContext? dbContext, bool isTransactionScope)
		{
			var requestedMode = isTransactionScope ? ExecutionMode.Transaction : ExecutionMode.Write;
			var state = State.Value;
			if (state == null)
			{
				state = new ScopeState
				{
					Depth = 0,
					Mode = requestedMode,
					DbContext = dbContext,
				};
				State.Value = state;
			}
			else
			{
				if (state.Mode == ExecutionMode.ReadOnly && requestedMode != ExecutionMode.ReadOnly)
				{
					throw new DatabaseException(
						"A write operation was attempted inside an ambient read-only database scope. " +
						"Ensure the outermost scope is ExecuteWriteAsync/ExecuteTransactionAsync when writes are required.",
						errorCode: DatabaseErrorCodes.InvalidOperation);
				}

				// Promote mode from Write to Transaction when an inner scope requests it.
				// This is informational only; transaction ownership remains with the outermost transaction wrapper.
				if (requestedMode == ExecutionMode.Transaction && state.Mode == ExecutionMode.Write)
				{
					state.Mode = ExecutionMode.Transaction;
				}

				// Inner scopes never override the ambient DbContext.
			}

			state.Depth++;
			return new ScopeToken();
		}

		public static ScopeToken EnterReadOnly(NpgsqlDbContext? dbContext)
		{
			var state = State.Value;
			if (state == null)
			{
				state = new ScopeState
				{
					Depth = 0,
					Mode = ExecutionMode.ReadOnly,
					DbContext = dbContext,
				};
				State.Value = state;
			}
			else
			{
				// Read-only nested inside write/transaction is allowed and reuses the ambient context.
			}

			state.Depth++;
			return new ScopeToken();
		}

		/// <summary>
		/// Scope-exit token returned by <see cref="Enter"/>.
		/// </summary>
		/// <remarks>
		/// IMPORTANT: This token MUST be disposed when the scope is exited (typically via a <c>using</c>
		/// declaration or statement). Failure to dispose will leave the <see cref="AsyncLocal{ScopeState}"/>
		/// in a corrupted state, causing all subsequent database operations in the same async flow to
		/// incorrectly believe they are executing inside an ambient scope. This can lead to:
		/// <list type="bullet">
		///   <item><description>Write operations silently reusing stale DbContext instances</description></item>
		///   <item><description>Retry logic being skipped inside what appears to be an ambient scope</description></item>
		///   <item><description>Transaction state corruption when a disposed context is unexpectedly reused</description></item>
		/// </list>
		/// The <see cref="BaseService{TEntity}"/> methods always wrap their scope entry in a <c>using</c>
		/// block. Manual callers of <see cref="Enter"/> or <see cref="EnterReadOnly"/> must do the same.
		/// </remarks>
		public readonly struct ScopeToken : IDisposable
		{
			/// <inheritdoc />
			public void Dispose()
			{
				var state = State.Value;
				if (state == null)
				{
					return;
				}

				state.Depth = state.Depth <= 0 ? 0 : state.Depth - 1;
				if (state.Depth == 0)
				{
					state.DbContext = null;
					State.Value = null;
				}
			}
		}
	}
}