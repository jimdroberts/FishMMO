using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Networking;
using UnityEngine;

namespace MeshyAI
{
	public interface IMeshyApiService
	{
		Task<int> GetBalanceAsync(string apiKey, CancellationToken cancellationToken);
		Task<string> StartPreviewTaskAsync(string apiKey, PreviewTaskRequest request, CancellationToken cancellationToken);
		Task<string> StartRefineTaskAsync(string apiKey, RefineTaskRequest request, CancellationToken cancellationToken);
		Task<MeshyTaskDetailsResponse> GetTaskDetailsAsync(string apiKey, string taskId, CancellationToken cancellationToken);
		Task<Texture2D> DownloadTextureAsync(string url, CancellationToken cancellationToken);
		Task<UnityWebRequest> DownloadFileAsync(string url, CancellationToken cancellationToken);
		Task<bool> CheckBalanceAsync(string apiKey, int requiredCredits, CancellationToken cancellationToken);
	}
}