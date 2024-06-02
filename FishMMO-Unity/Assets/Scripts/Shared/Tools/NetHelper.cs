using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Collections;

namespace FishMMO.Shared
{
	public static class NetHelper
	{
		public static IEnumerator FetchExternalIPAddress(Action<string> onSuccess = null, Action<string> onError = null, string serviceUrl = "https://checkip.amazonaws.com/")
		{
			Debug.Log($"NetHelper: Fetching Remote IP Address from \"{serviceUrl}\"");
			using (UnityWebRequest webRequest = UnityWebRequest.Get(serviceUrl))
			{
				yield return webRequest.SendWebRequest();

				if (webRequest.result == UnityWebRequest.Result.ConnectionError || webRequest.result == UnityWebRequest.Result.ProtocolError)
				{
					Debug.Log($"NetHelper: Request error: {webRequest.error}");
					onError?.Invoke(webRequest.error);
				}
				else
				{
					string ipAddress = webRequest.downloadHandler.text;
					ipAddress = ipAddress.Replace("\r\n", "").Replace("\n", "").Trim();

					if (IsValidIPAddress(ipAddress))
					{
						Debug.Log($"NetHelper: External IP: {ipAddress}");
						onSuccess?.Invoke(ipAddress);
					}
					else
					{
						Debug.Log("NetHelper: Invalid IP address format.");
						onError?.Invoke("NetHelper: Invalid IP address format.");
					}
				}
			}
		}

		public static bool IsValidIPAddress(string ipAddress)
		{
			if (string.IsNullOrEmpty(ipAddress))
				return false;

			string[] splitValues = ipAddress.Split('.');
			if (splitValues.Length != 4)
				return false;

			foreach (string item in splitValues)
			{
				if (!int.TryParse(item, out int num) || num < 0 || num > 255)
					return false;
			}

			return true;
		}
	}
}