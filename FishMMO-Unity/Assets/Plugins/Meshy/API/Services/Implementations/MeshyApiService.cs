#if UNITY_EDITOR
using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using System.Threading;

namespace MeshyAI
{
	public class MeshyApiService : IMeshyApiService
	{
		private const string MESHY_GENERATE_URL = "https://api.meshy.ai/openapi/v2/text-to-3d";
		private const string MESHY_BALANCE_URL = "https://api.meshy.ai/openapi/v1/balance";

		public async Task<int> GetBalanceAsync(string apiKey, CancellationToken cancellationToken)
		{
			using (var www = await PerformRequest(MESHY_BALANCE_URL, "GET", apiKey, cancellationToken))
			{
				if (www.result != UnityWebRequest.Result.Success)
					throw new Exception("Error checking balance: " + www.error);
				var responseJson = JsonUtility.FromJson<MeshyBalanceResponse>(www.downloadHandler.text);
				return responseJson.balance;
			}
		}

		public async Task<bool> CheckBalanceAsync(string apiKey, int requiredCredits, CancellationToken cancellationToken)
		{
			int balance = await GetBalanceAsync(apiKey, cancellationToken);
			if (balance < requiredCredits)
			{
				throw new Exception($"Insufficient balance. Required: {requiredCredits}, available: {balance}");
			}
			return true;
		}

		public async Task<string> StartPreviewTaskAsync(string apiKey, PreviewTaskRequest request, CancellationToken cancellationToken)
		{
			request.mode = "preview";
			string jsonBody = JsonUtility.ToJson(request);
			byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
			using (var www = await PerformRequest(MESHY_GENERATE_URL, "POST", apiKey, cancellationToken, bodyRaw))
			{
				if (www.result != UnityWebRequest.Result.Success)
					throw new Exception("Error starting preview task: " + www.error);
				var response = JsonUtility.FromJson<MeshyV2TaskResponse>(www.downloadHandler.text);
				return response.result;
			}
		}

		public async Task<string> StartRefineTaskAsync(string apiKey, RefineTaskRequest request, CancellationToken cancellationToken)
		{
			request.mode = "refine";
			string jsonBody = JsonUtility.ToJson(request);
			byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
			using (var www = await PerformRequest(MESHY_GENERATE_URL, "POST", apiKey, cancellationToken, bodyRaw))
			{
				if (www.result != UnityWebRequest.Result.Success)
					throw new Exception("Error starting refine task: " + www.error);
				var response = JsonUtility.FromJson<MeshyV2TaskResponse>(www.downloadHandler.text);
				return response.result;
			}
		}

		public async Task<MeshyTaskDetailsResponse> GetTaskDetailsAsync(string apiKey, string taskId, CancellationToken cancellationToken)
		{
			string url = $"{MESHY_GENERATE_URL}/{taskId}";
			using (var www = await PerformRequest(url, "GET", apiKey, cancellationToken))
			{
				if (www.result != UnityWebRequest.Result.Success)
					throw new Exception("Error retrieving task details: " + www.error);
				return JsonUtility.FromJson<MeshyTaskDetailsResponse>(www.downloadHandler.text);
			}
		}

		public async Task<Texture2D> DownloadTextureAsync(string url, CancellationToken cancellationToken)
		{
			using (UnityWebRequest www = UnityWebRequestTexture.GetTexture(url))
			{
				var operation = www.SendWebRequest();
				while (!operation.isDone)
				{
					if (cancellationToken.IsCancellationRequested)
					{
						www.Abort();
						cancellationToken.ThrowIfCancellationRequested();
					}
					await Task.Yield();
				}

				if (www.result != UnityWebRequest.Result.Success)
					throw new Exception("Download Texture Error: " + www.error);
				return DownloadHandlerTexture.GetContent(www);
			}
		}

		public async Task<UnityWebRequest> DownloadFileAsync(string url, CancellationToken cancellationToken)
		{
			var www = UnityWebRequest.Get(url);
			var operation = www.SendWebRequest();
			while (!operation.isDone)
			{
				if (cancellationToken.IsCancellationRequested)
				{
					www.Abort();
					cancellationToken.ThrowIfCancellationRequested();
				}
				await Task.Yield();
			}

			if (www.result != UnityWebRequest.Result.Success)
				throw new Exception($"Error downloading file from {url}: {www.error}");
			return www;
		}

		private async Task<UnityWebRequest> PerformRequest(string url, string method, string apiKey, CancellationToken cancellationToken, byte[] body = null)
		{
			var www = new UnityWebRequest(url, method);
			www.downloadHandler = new DownloadHandlerBuffer();
			www.SetRequestHeader("Authorization", "Bearer " + apiKey);
			if (body != null)
			{
				www.uploadHandler = new UploadHandlerRaw(body);
				www.SetRequestHeader("Content-Type", "application/json");
			}

			var operation = www.SendWebRequest();
			while (!operation.isDone)
			{
				if (cancellationToken.IsCancellationRequested)
				{
					www.Abort();
					cancellationToken.ThrowIfCancellationRequested();
				}
				await Task.Yield();
			}

			// Improved error handling: throw if HTTP status is not 2xx, include response body if available
			bool isHttpSuccess = www.responseCode >= 200 && www.responseCode < 300;
			if (!isHttpSuccess)
			{
				string responseText = string.Empty;
				try { responseText = www.downloadHandler?.text; } catch { }
				throw new Exception($"HTTP Error {www.responseCode}: {www.error}\nResponse: {responseText}");
			}
			return www;
		}
	}
}
#endif