using System;
using System.Collections;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public interface IPatchServerService
	{
		/// <summary>
		/// Asynchronously retrieves the main patch server address (IP and Port).
		/// </summary>
		/// <param name="ipFetchHost">The host URL to query for the patch server address.</param>
		/// <param name="onComplete">Callback invoked with the ServerAddress upon success.</param>
		/// <param name="onError">Callback invoked with an error message upon failure.</param>
		/// <returns>An IEnumerator for use in a Unity Coroutine.</returns>
		public abstract IEnumerator GetPatchServerAddress(string ipFetchHost, Action<ServerAddress> onComplete, Action<string> onError);

		/// <summary>
		/// Asynchronously retrieves the latest client version from the patch server.
		/// </summary>
		/// <param name="patcherHost">The base URL of the patch server.</param>
		/// <param name="onComplete">Callback invoked with the latest VersionConfig upon success.</param>
		/// <param name="onError">Callback invoked with an error message upon failure.</param>
		/// <returns>An IEnumerator for use in a Unity Coroutine.</returns>
		public abstract IEnumerator GetLatestVersion(string patcherHost, Action<VersionConfig> onComplete, Action<string> onError);

		/// <summary>
		/// Asynchronously downloads a patch file from the server.
		/// </summary>
		/// <param name="patchUrl">The full URL of the patch file to download.</param>
		/// <param name="tempFilePath">The temporary file path where the patch should be saved.</param>
		/// <param name="onComplete">Callback invoked upon successful download.</param>
		/// <param name="onError">Callback invoked with an error message upon failure.</param>
		/// <param name="onProgress">Callback invoked periodically with download progress (0.0 to 1.0) and a formatted string.</param>
		/// <returns>An IEnumerator for use in a Unity Coroutine.</returns>
		public abstract IEnumerator DownloadPatch(string patchUrl, string tempFilePath, Action onComplete, Action<string> onError, Action<float, string> onProgress);
	}
}