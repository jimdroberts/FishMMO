#if UNITY_EDITOR
using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace MeshyAI
{
	public interface IMeshyModelIO
	{
		void EnsureDirectory(string path);
		Task SaveFileAsync(UnityWebRequest www, string path);
		void RefreshAssetDatabase();
		void DownloadPreviewImageAsync(string url, Action<Texture2D> onDownloaded);
	}
}
#endif