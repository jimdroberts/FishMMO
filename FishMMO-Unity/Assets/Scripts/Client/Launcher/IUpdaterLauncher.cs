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
		/// <para><b>IMPORTANT:</b> This method returns an IEnumerator and the caller
		/// MUST invoke it via <c>MonoBehaviour.StartCoroutine</c>. The implementation
		/// uses <c>yield return new WaitForSeconds(...)</c> which requires a Unity
		/// coroutine host with a main-thread UnitySynchronizationContext. Calling this
		/// method outside of a Unity coroutine context (e.g., from a thread-pool thread
		/// or without a MonoBehaviour) will result in undefined behavior.</para>
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