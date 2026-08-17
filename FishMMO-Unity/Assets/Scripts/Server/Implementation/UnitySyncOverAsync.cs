using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Logging;

namespace FishMMO.Server.Implementation
{
	/// <summary>
	/// Blocks the calling thread on an async operation without risking a
	/// <see cref="SynchronizationContext"/> deadlock.
	/// <para>
	/// Unity installs a <c>UnitySynchronizationContext</c> on the main thread; continuations
	/// posted to it only run when the player loop drains them. Any <c>await</c> in the callee
	/// that captures that context — i.e. any <c>await</c> without <c>ConfigureAwait(false)</c>,
	/// anywhere in the call chain — can therefore never resume while the main thread sits in
	/// <c>GetResult()</c>/<c>.Result</c>/<c>.Wait()</c>. The server then stays alive but never
	/// finishes <c>InitializeOnce</c>, so the transport never binds its port.
	/// </para>
	/// <para>
	/// <b>Shutdown paths only.</b> <c>OnDestroy</c>/<c>OnApplicationQuit</c> cannot yield and the
	/// process exits immediately afterwards, so there is no continuation to hand work to — a
	/// bounded block is the only way to flush pending state before exit. Startup has no such
	/// constraint and must not use this: behaviours initialize through
	/// <c>ServerBehaviour.InitializeOnceAsync</c>, driven by <c>Server</c>'s initialization
	/// coroutine, which leaves the main thread free to drain continuations.
	/// </para>
	/// <para>
	/// Where a bounded block genuinely is required, route it through here rather than
	/// hand-rolling <c>Task.Run(...).Wait(...)</c>: call sites should not have to audit an entire
	/// EF/Npgsql call chain to know whether blocking is safe.
	/// </para>
	/// </summary>
	/// <remarks>
	/// Behaviour that hand-rolled <c>Task.Run(...).Wait(timeout)</c> gets wrong:
	/// <list type="bullet">
	///   <item><description>
	///     <b>No pointless thread hop.</b> When the caller already has no synchronization
	///     context (a worker thread), the operation starts inline. <c>Task.Run</c> there would
	///     block one pool thread while requiring the pool to hand out another to complete the
	///     very work being waited on — self-inflicted starvation under load.
	///   </description></item>
	///   <item><description>
	///     <b>Original exceptions.</b> <see cref="Task.Wait(int)"/> throws
	///     <see cref="AggregateException"/>, so callers logging <c>ex.Message</c> get
	///     "One or more errors occurred." This waits without throwing and lets
	///     <c>GetAwaiter().GetResult()</c> rethrow the original exception.
	///   </description></item>
	///   <item><description>
	///     <b>Timeouts cancel.</b> The operation receives a token that is cancelled when the
	///     timeout expires, so the database work stops instead of running on unobserved with
	///     its result discarded. The abandoned task's exception is observed so it can never
	///     surface as an unobserved-task exception.
	///   </description></item>
	/// </list>
	/// </remarks>
	public static class UnitySyncOverAsync
	{
		/// <summary>
		/// Default wait for server startup/shutdown database calls. Matches the registration
		/// timeout used by <c>LoginServerSystem</c>.
		/// </summary>
		public const int DefaultTimeoutMilliseconds = 30_000;

		/// <summary>Log source for abandoned-operation diagnostics.</summary>
		private const string LogSource = "UnitySyncOverAsync";

		/// <summary>
		/// Absolute deadline (<see cref="Stopwatch.GetTimestamp"/> ticks) shared by every blocking
		/// call once shutdown begins, or 0 when no budget is active.
		/// </summary>
		private static long shutdownDeadlineTimestamp;

		/// <summary>
		/// Caps the <em>total</em> time shutdown may block the main thread across all call sites.
		/// </summary>
		/// <param name="totalMilliseconds">Budget for the whole teardown.</param>
		/// <remarks>
		/// Individual timeouts are each reasonable but unbounded in aggregate: a scene server can
		/// serialize a 5s database cleanup, a 10s chat flush and a 30s character save. On a wedged
		/// database that is ~45s, which exceeds a Kubernetes 30s grace period and a Docker 10s stop
		/// timeout — the process gets SIGKILLed mid-flush having accomplished nothing, which is
		/// strictly worse than flushing what fits and exiting cleanly. Clamping every call to the
		/// remaining budget keeps teardown inside the supervisor's window.
		/// </remarks>
		public static void BeginShutdownBudget(int totalMilliseconds)
		{
			if (totalMilliseconds <= 0)
			{
				shutdownDeadlineTimestamp = 0;
				return;
			}

			shutdownDeadlineTimestamp = Stopwatch.GetTimestamp() + (long)(totalMilliseconds / 1000.0 * Stopwatch.Frequency);
		}

		/// <summary>
		/// Clears any active shutdown budget. Used when a teardown is aborted (Editor domain
		/// reload) so a later run is not clamped by a stale deadline.
		/// </summary>
		public static void ClearShutdownBudget()
		{
			shutdownDeadlineTimestamp = 0;
		}

		/// <summary>
		/// Clamps a requested timeout to whatever remains of the shutdown budget.
		/// </summary>
		/// <returns>
		/// The effective timeout, or 0 when the budget is spent — callers then fail immediately
		/// rather than blocking teardown further.
		/// </returns>
		private static int ClampToShutdownBudget(int timeoutMilliseconds)
		{
			long deadline = shutdownDeadlineTimestamp;
			if (deadline == 0)
			{
				return timeoutMilliseconds;
			}

			long remainingTicks = deadline - Stopwatch.GetTimestamp();
			if (remainingTicks <= 0)
			{
				return 0;
			}

			int remainingMs = (int)Math.Min(int.MaxValue, remainingTicks * 1000L / Stopwatch.Frequency);
			return Math.Min(timeoutMilliseconds, remainingMs);
		}

		/// <summary>
		/// Runs <paramref name="operation"/> off Unity's synchronization context and blocks
		/// until it completes or <paramref name="timeoutMilliseconds"/> elapses.
		/// </summary>
		/// <typeparam name="T">Result type of the async operation.</typeparam>
		/// <param name="operation">
		/// Async work to run. The supplied token is cancelled on timeout — forward it to the
		/// database call. Use <c>_ =&gt;</c> only when the work genuinely cannot be cancelled.
		/// </param>
		/// <param name="timeoutMilliseconds">Maximum wait. 30 seconds if omitted.</param>
		/// <returns>The operation result.</returns>
		/// <exception cref="ArgumentNullException"><paramref name="operation"/> is null.</exception>
		/// <exception cref="TimeoutException">The wait exceeded <paramref name="timeoutMilliseconds"/>.</exception>
		public static T Run<T>(Func<CancellationToken, Task<T>> operation, int timeoutMilliseconds = DefaultTimeoutMilliseconds)
		{
			if (!TryRun(operation, out T result, timeoutMilliseconds))
			{
				throw new TimeoutException($"Async operation timed out after {timeoutMilliseconds}ms.");
			}

			return result;
		}

		/// <summary>
		/// Runs <paramref name="operation"/> off Unity's synchronization context and blocks
		/// until it completes or <paramref name="timeoutMilliseconds"/> elapses.
		/// </summary>
		/// <param name="operation">
		/// Async work to run. The supplied token is cancelled on timeout.
		/// </param>
		/// <param name="timeoutMilliseconds">Maximum wait. 30 seconds if omitted.</param>
		/// <exception cref="ArgumentNullException"><paramref name="operation"/> is null.</exception>
		/// <exception cref="TimeoutException">The wait exceeded <paramref name="timeoutMilliseconds"/>.</exception>
		public static void Run(Func<CancellationToken, Task> operation, int timeoutMilliseconds = DefaultTimeoutMilliseconds)
		{
			if (!TryRun(operation, timeoutMilliseconds))
			{
				throw new TimeoutException($"Async operation timed out after {timeoutMilliseconds}ms.");
			}
		}

		/// <summary>
		/// Runs <paramref name="operation"/> off Unity's synchronization context and blocks
		/// until it completes or <paramref name="timeoutMilliseconds"/> elapses. Returns
		/// <c>false</c> on timeout instead of throwing, for call sites that degrade gracefully.
		/// </summary>
		/// <typeparam name="T">Result type of the async operation.</typeparam>
		/// <param name="operation">
		/// Async work to run. The supplied token is cancelled on timeout.
		/// </param>
		/// <param name="result">The operation result, or <c>default</c> on timeout.</param>
		/// <param name="timeoutMilliseconds">Maximum wait. 30 seconds if omitted.</param>
		/// <returns><c>true</c> if the operation completed within the timeout.</returns>
		/// <exception cref="ArgumentNullException"><paramref name="operation"/> is null.</exception>
		/// <remarks>
		/// Only a timeout returns <c>false</c>. Exceptions thrown by the operation propagate
		/// unwrapped, exactly as a direct <c>await</c> would surface them.
		/// </remarks>
		public static bool TryRun<T>(Func<CancellationToken, Task<T>> operation, out T result, int timeoutMilliseconds = DefaultTimeoutMilliseconds)
		{
			if (operation == null)
			{
				throw new ArgumentNullException(nameof(operation));
			}

			timeoutMilliseconds = ClampToShutdownBudget(timeoutMilliseconds);

			CancellationTokenSource cts = new CancellationTokenSource();
			Task<T> task;
			try
			{
				task = StartOffContext(operation, cts.Token);
			}
			catch
			{
				// The operation threw synchronously before returning a task.
				cts.Dispose();
				throw;
			}

			if (!WaitFor(task, timeoutMilliseconds))
			{
				CancelAndObserve(cts, task, timeoutMilliseconds);
				result = default;
				return false;
			}

			cts.Dispose();
			// Rethrows the original exception rather than an AggregateException.
			result = task.GetAwaiter().GetResult();
			return true;
		}

		/// <summary>
		/// Runs <paramref name="operation"/> off Unity's synchronization context and blocks
		/// until it completes or <paramref name="timeoutMilliseconds"/> elapses. Returns
		/// <c>false</c> on timeout instead of throwing.
		/// </summary>
		/// <param name="operation">
		/// Async work to run. The supplied token is cancelled on timeout.
		/// </param>
		/// <param name="timeoutMilliseconds">Maximum wait. 30 seconds if omitted.</param>
		/// <returns><c>true</c> if the operation completed within the timeout.</returns>
		/// <exception cref="ArgumentNullException"><paramref name="operation"/> is null.</exception>
		public static bool TryRun(Func<CancellationToken, Task> operation, int timeoutMilliseconds = DefaultTimeoutMilliseconds)
		{
			if (operation == null)
			{
				throw new ArgumentNullException(nameof(operation));
			}

			timeoutMilliseconds = ClampToShutdownBudget(timeoutMilliseconds);

			CancellationTokenSource cts = new CancellationTokenSource();
			Task task;
			try
			{
				task = StartOffContext(operation, cts.Token);
			}
			catch
			{
				cts.Dispose();
				throw;
			}

			if (!WaitFor(task, timeoutMilliseconds))
			{
				CancelAndObserve(cts, task, timeoutMilliseconds);
				return false;
			}

			cts.Dispose();
			task.GetAwaiter().GetResult();
			return true;
		}

		/// <summary>
		/// Starts the operation on a thread with no synchronization context, so nothing it
		/// awaits can need the thread that is about to block.
		/// </summary>
		private static Task<T> StartOffContext<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
		{
			// Already context-free (worker thread): start inline. Hopping to the pool here
			// would block this thread while requiring the pool to supply another thread to
			// finish the work being waited on.
			if (SynchronizationContext.Current == null)
			{
				return operation(cancellationToken);
			}

			// Unity main thread: the delegate runs on a pool thread whose current context is
			// null, so every continuation in the chain can complete without this thread.
			return Task.Run(() => operation(cancellationToken), cancellationToken);
		}

		/// <inheritdoc cref="StartOffContext{T}(Func{CancellationToken, Task{T}}, CancellationToken)"/>
		private static Task StartOffContext(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
		{
			if (SynchronizationContext.Current == null)
			{
				return operation(cancellationToken);
			}

			return Task.Run(() => operation(cancellationToken), cancellationToken);
		}

		/// <summary>
		/// Waits for the task without throwing. <see cref="Task.Wait(int)"/> would raise an
		/// <see cref="AggregateException"/> on fault, hiding the original exception from the
		/// caller's <c>GetAwaiter().GetResult()</c>.
		/// </summary>
		/// <returns><c>true</c> if the task settled within the timeout.</returns>
		private static bool WaitFor(Task task, int timeoutMilliseconds)
		{
			if (task.IsCompleted)
			{
				return true;
			}

			return ((IAsyncResult)task).AsyncWaitHandle.WaitOne(timeoutMilliseconds);
		}

		/// <summary>
		/// Cancels an operation that outlived its timeout, then observes its outcome so an
		/// abandoned fault cannot resurface as an unobserved-task exception, and disposes the
		/// token source once the task has actually settled.
		/// </summary>
		private static void CancelAndObserve(CancellationTokenSource cts, Task task, int timeoutMilliseconds)
		{
			try
			{
				cts.Cancel();
			}
			catch (Exception ex)
			{
				SafeLogWarning($"Failed to cancel an operation abandoned after {timeoutMilliseconds}ms: {ex.Message}");
			}

			task.ContinueWith(
				completed =>
				{
					// Reading Exception marks the fault observed.
					Exception failure = completed.Exception?.GetBaseException();
					if (failure != null && !(failure is OperationCanceledException))
					{
						SafeLogWarning(
							$"An operation abandoned after a {timeoutMilliseconds}ms timeout later faulted: {failure.Message}");
					}

					cts.Dispose();
				},
				CancellationToken.None,
				TaskContinuationOptions.ExecuteSynchronously,
				TaskScheduler.Default);
		}

		/// <summary>
		/// Logs without ever throwing — this runs on abandoned-task continuations, which may
		/// complete after the logger has been torn down during shutdown.
		/// </summary>
		private static void SafeLogWarning(string message)
		{
			try
			{
				_ = Log.Warning(LogSource, message);
			}
			catch
			{
				// Diagnostics must never destabilize shutdown.
			}
		}
	}
}