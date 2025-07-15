using System;

namespace FishMMO.Client
{
	public interface IUpdaterLauncher
	{
		/// <summary>
		/// Launches an external updater executable.
		/// </summary>
		/// <param name="updaterPath">The full path to the updater executable.</param>
		/// <param name="currentClientVersion">The current version of the client.</param>
		/// <param name="latestServerVersion">The latest version available on the server.</param>
		/// <param name="onComplete">Callback invoked when the updater process successfully exits.</param>
		/// <param name="onError">Callback invoked with an error message if the updater fails to launch or exits with an error.</param>
		void LaunchUpdater(string updaterPath, string currentClientVersion, string latestServerVersion, Action onComplete, Action<string> onError);
	}
}