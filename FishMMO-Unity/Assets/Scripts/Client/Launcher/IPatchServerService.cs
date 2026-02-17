using System;
using System.Collections;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// Contract for version lookups and patch binary downloads.
	/// All methods use a single API host URL (NGINX routes by path to the correct backend).
	/// </summary>
	public interface IPatchServerService
	{
		/// <summary>
		/// Asynchronously retrieves the latest client version from the API gateway.
		/// </summary>
		/// <param name="apiHost">The unified API host URL.</param>
		/// <param name="onComplete">Callback invoked with the latest VersionConfig upon success.</param>
		/// <param name="onError">Callback invoked with an error message upon failure.</param>
		/// <returns>An IEnumerator for use in a Unity Coroutine.</returns>
		public abstract IEnumerator GetLatestVersion(string apiHost, Action<VersionConfig> onComplete, Action<string> onError);

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