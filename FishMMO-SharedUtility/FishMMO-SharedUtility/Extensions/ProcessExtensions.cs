using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace FishMMO.Shared
{
	/// <summary>
	/// Extension methods for System.Diagnostics.Process, providing asynchronous integration.
	/// </summary>
	public static class ProcessExtensions
	{
		/// <summary>
		/// Asynchronously waits for the process to exit.
		/// Includes a safety check for processes that have already exited and supports cancellation.
		/// </summary>
		/// <param name="process">The process to wait for.</param>
		/// <param name="cancellationToken">Optional token to cancel the wait.</param>
		/// <returns>A Task that completes when the process exits.</returns>
		public static async Task WaitForExitAsync(this Process process, CancellationToken cancellationToken = default)
		{
			if (Configuration.DisableFileIO)
			{
				throw new PlatformNotSupportedException("Process.WaitForExitAsync is not supported on WebGL.");
			}
			// Safety check: if the process is already gone, return immediately.
			if (process.HasExited) return;

			var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

			EventHandler exitHandler = (s, e) => tcs.TrySetResult(true);

			try
			{
				process.EnableRaisingEvents = true;
				process.Exited += exitHandler;

				// Double-check after hooking the event to avoid a race condition
				// where the process exits between the first check and the event hook.
				if (process.HasExited)
				{
					tcs.TrySetResult(true);
				}

				// If a cancellation token is provided, allow the task to be aborted.
				using (cancellationToken.Register(() => tcs.TrySetCanceled()))
				{
					await tcs.Task.ConfigureAwait(false);
				}
			}
			finally
			{
				process.Exited -= exitHandler;
			}
		}
	}
}
