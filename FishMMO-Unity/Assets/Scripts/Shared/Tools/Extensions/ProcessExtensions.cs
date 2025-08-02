using System.Diagnostics;
using System.Threading.Tasks;

namespace FishMMO.Shared
{
	/// <summary>
	/// Extension methods for System.Diagnostics.Process, including asynchronous waiting.
	/// </summary>
	public static class ProcessExtensions
	{
		/// <summary>
		/// Asynchronously waits for the process to exit.
		/// </summary>
		/// <param name="process">The process to wait for.</param>
		/// <returns>A Task that completes when the process exits.</returns>
		public static Task WaitForExitAsync(this Process process)
		{
			var tcs = new TaskCompletionSource<object>();
			process.EnableRaisingEvents = true;
			process.Exited += (s, e) => tcs.SetResult(null);
			return tcs.Task;
		}
	}
}