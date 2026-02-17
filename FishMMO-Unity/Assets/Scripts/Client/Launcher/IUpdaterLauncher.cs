using System;
using System.Collections;

namespace FishMMO.Client
{
	/// <summary>
	/// Contract for launching and monitoring the external updater process.
	/// </summary>
	public interface IUpdaterLauncher
	{
		/// <summary>
		/// Launches an external updater executable and polls for its exit.
		/// Must be run as a Unity coroutine so that callbacks execute on the main thread.
		/// </summary>
		/// <param name="updaterPath">The full path to the updater executable.</param>
		/// <param name="currentClientVersion">The current version of the client.</param>
		/// <param name="latestServerVersion">The latest version available on the server.</param>
		/// <param name="onComplete">Callback invoked when the updater process successfully exits.</param>
		/// <param name="onError">Callback invoked with an error message if the updater fails to launch or exits with an error.</param>
		/// <returns>An IEnumerator for use in a Unity Coroutine.</returns>
		IEnumerator LaunchUpdater(string updaterPath, string currentClientVersion, string latestServerVersion, Action onComplete, Action<string> onError);
	}
}