namespace FishMMO.Shared
{
	/// <summary>
	/// The bytes of whichever scene download is in flight: a world scene through FishNet's scene manager
	/// (<see cref="AddressableSceneProcessor"/>), or a boot or login scene through <see cref="AddressableLoadProcessor"/>.
	/// </summary>
	public static class SceneDownloadProgress
	{
		/// <summary>True while a scene with something left to download is loading; its bytes so far and in all.</summary>
		public static bool TryGetBytes(out long downloadedBytes, out long totalBytes)
		{
			if (AddressableSceneProcessor.TryGetDownload(out downloadedBytes, out totalBytes) && totalBytes > 0)
			{
				return true;
			}
			if (AddressableLoadProcessor.TryGetSceneDownload(out UnityEngine.ResourceManagement.AsyncOperations.DownloadStatus status) &&
				status.TotalBytes > 0)
			{
				downloadedBytes = status.DownloadedBytes;
				totalBytes = status.TotalBytes;
				return true;
			}
			downloadedBytes = 0;
			totalBytes = 0;
			return false;
		}
	}
}
