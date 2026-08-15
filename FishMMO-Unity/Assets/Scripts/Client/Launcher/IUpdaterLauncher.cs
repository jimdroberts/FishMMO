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
		/// Launches an external updater executable and hands control of the install over
		/// to it.
		/// <para><b>IMPORTANT:</b> This method returns an IEnumerator and the caller
		/// MUST invoke it via <c>MonoBehaviour.StartCoroutine</c>. The implementation
		/// uses <c>yield return new WaitForSeconds(...)</c> which requires a Unity
		/// coroutine host with a main-thread UnitySynchronizationContext. Calling this
		/// method outside of a Unity coroutine context (e.g., from a thread-pool thread
		/// or without a MonoBehaviour) will result in undefined behavior.</para>
		/// <para>Implementations must not block until the updater exits. The updater
		/// terminates the calling process by PID before patching and relaunches the client
		/// afterwards, so waiting for its exit is a mutual wait that strands every
		/// completion path.</para>
		/// </summary>
		/// <param name="updaterPath">The full path to the updater executable.</param>
		/// <param name="currentClientVersion">The current version of the client.</param>
		/// <param name="latestServerVersion">The latest version available on the server.</param>
		/// <param name="onComplete">
		/// Callback invoked once the updater has started successfully and taken over. This
		/// signals a successful handoff, not a completed patch — the caller should shut the
		/// launcher down so the updater can replace the client binaries.
		/// </param>
		/// <param name="onError">Callback invoked with an error message if the updater fails to launch or exits with an error before handoff.</param>
		/// <returns>An IEnumerator for use in a Unity Coroutine.</returns>
		IEnumerator LaunchUpdater(string updaterPath, string currentClientVersion, string latestServerVersion, Action onComplete, Action<string> onError);
	}
}