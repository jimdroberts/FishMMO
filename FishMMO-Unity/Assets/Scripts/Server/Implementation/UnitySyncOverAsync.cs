using System;
using System.Threading.Tasks;

namespace FishMMO.Server.Implementation
{
	/// <summary>
	/// Blocks the caller on an async operation after dispatching it to the thread pool.
	/// <para>
	/// Unity's <c>SynchronizationContext</c> posts async completions back to the main thread.
	/// Calling <c>task.GetAwaiter().GetResult()</c> (or <c>.Result</c> / <c>.Wait()</c>) on that
	/// thread while the task still needs the same context deadlocks: the continuation can never
	/// run, and LoginServer never reaches <c>NetworkWrapper.StartServer()</c>.
	/// </para>
	/// <para>
	/// <c>Task.Run</c> starts the work with a null sync context (thread-pool thread). That is
	/// the same pattern <c>LoginServerSystem</c>, <c>WorldServerSystem</c>, and
	/// <c>SceneServerSystem</c> already use for DB registration. Prefer this helper over a raw
	/// <c>GetResult()</c> at any Unity main-thread sync-over-async boundary.
	/// </para>
	/// </summary>
	public static class UnitySyncOverAsync
	{
		/// <summary>
		/// Default wait used by server startup DB calls. Matches
		/// <c>LoginServerSystem</c>'s registration timeout.
		/// </summary>
		public const int DefaultTimeoutMilliseconds = 30_000;

		/// <summary>
		/// Runs <paramref name="operation"/> on the thread pool and blocks until it completes
		/// or <paramref name="timeoutMilliseconds"/> elapses.
		/// </summary>
		/// <typeparam name="T">Result type of the async operation.</typeparam>
		/// <param name="operation">Async work to run off the Unity sync context.</param>
		/// <param name="timeoutMilliseconds">Maximum wait. 30 seconds if omitted.</param>
		/// <returns>The operation result.</returns>
		/// <exception cref="ArgumentNullException"><paramref name="operation"/> is null.</exception>
		/// <exception cref="TimeoutException">The wait exceeded <paramref name="timeoutMilliseconds"/>.</exception>
		public static T Run<T>(Func<Task<T>> operation, int timeoutMilliseconds = DefaultTimeoutMilliseconds)
		{
			if (operation == null)
			{
				throw new ArgumentNullException(nameof(operation));
			}

			// Task.Run(Func<Task<T>>) unwraps to Task<T> and runs the delegate on the pool.
			Task<T> task = Task.Run(operation);
			if (!task.Wait(timeoutMilliseconds))
			{
				throw new TimeoutException(
					$"UnitySyncOverAsync.Run timed out after {timeoutMilliseconds}ms.");
			}

			return task.GetAwaiter().GetResult();
		}

		/// <summary>
		/// Runs a non-generic async operation on the thread pool and blocks until it completes
		/// or <paramref name="timeoutMilliseconds"/> elapses.
		/// </summary>
		/// <param name="operation">Async work to run off the Unity sync context.</param>
		/// <param name="timeoutMilliseconds">Maximum wait. 30 seconds if omitted.</param>
		/// <exception cref="ArgumentNullException"><paramref name="operation"/> is null.</exception>
		/// <exception cref="TimeoutException">The wait exceeded <paramref name="timeoutMilliseconds"/>.</exception>
		public static void Run(Func<Task> operation, int timeoutMilliseconds = DefaultTimeoutMilliseconds)
		{
			if (operation == null)
			{
				throw new ArgumentNullException(nameof(operation));
			}

			Task task = Task.Run(operation);
			if (!task.Wait(timeoutMilliseconds))
			{
				throw new TimeoutException(
					$"UnitySyncOverAsync.Run timed out after {timeoutMilliseconds}ms.");
			}

			task.GetAwaiter().GetResult();
		}
	}
}
