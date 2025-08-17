using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using UnityEditor;

namespace MeshyAI
{
	public class MeshyModelIO : IMeshyModelIO
	{
		public void EnsureDirectory(string path)
		{
			if (!Directory.Exists(path))
				Directory.CreateDirectory(path);
		}

		public async Task SaveFileAsync(UnityWebRequest www, string path)
		{
			if (www == null || www.result != UnityWebRequest.Result.Success)
				throw new System.Exception($"Error saving file to {path}: {www?.error}");
			File.WriteAllBytes(path, www.downloadHandler.data);
		}

		public void RefreshAssetDatabase()
		{
			AssetDatabase.Refresh();
		}

		public async void DownloadPreviewImageAsync(string url, Action<Texture2D> onDownloaded)
		{
			try
			{
				using (UnityWebRequest www = UnityWebRequestTexture.GetTexture(url))
				{
					await www.SendWebRequest();
					if (www.result != UnityWebRequest.Result.Success)
						throw new Exception("Error downloading preview image: " + www.error);

					Texture2D texture = DownloadHandlerTexture.GetContent(www);
					onDownloaded?.Invoke(texture);
				}
			}
			catch (Exception e)
			{
				Debug.LogError("Failed to download preview image: " + e.Message);
			}
		}
	}
}