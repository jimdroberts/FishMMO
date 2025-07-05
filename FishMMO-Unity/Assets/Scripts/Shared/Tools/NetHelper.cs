using UnityEngine.Networking;
using System;
using System.Collections;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// A static utility class providing network-related helper methods, such as fetching the external IP address.
	/// IP address and hostname validation is delegated to the <see cref="Constants.IsAddressValid(string)"/> method.
	/// </summary>
	public static class NetHelper
	{
		/// <summary>
		/// Asynchronously fetches the external (public) IP address of the client using a specified web service.
		/// This method typically relies on a service that simply returns the client's public IP address as plain text.
		/// </summary>
		/// <param name="onSuccess">An optional callback action that receives the fetched IP address (string) upon success.</param>
		/// <param name="onError">An optional callback action that receives an error message (string) if the request fails or returns an invalid IP.</param>
		/// <param name="serviceUrl">The URL of the web service to query for the external IP address.
		/// Defaults to "https://checkip.amazonaws.com/", which returns a plain text IPv4 address.</param>
		/// <returns>An IEnumerator suitable for use with StartCoroutine to run the web request.</returns>
		public static IEnumerator FetchExternalIPAddress(Action<string> onSuccess = null, Action<string> onError = null, string serviceUrl = "https://checkip.amazonaws.com/")
		{
			// Log the attempt to fetch the IP address for debugging.
			Log.Debug("NetHelper", $"Fetching Remote IP Address from \"{serviceUrl}\"");

			// Use 'using' statement to ensure the UnityWebRequest is properly disposed.
			using (UnityWebRequest webRequest = UnityWebRequest.Get(serviceUrl))
			{
				// Yield until the web request is completed.
				yield return webRequest.SendWebRequest();

				// Check for network connection errors or HTTP protocol errors.
				if (webRequest.result == UnityWebRequest.Result.ConnectionError || webRequest.result == UnityWebRequest.Result.ProtocolError)
				{
					// Log the error more prominently.
					Log.Error("NetHelper", $"Request error: {webRequest.error}");

					// Invoke the error callback with the web request's error message.
					onError?.Invoke(webRequest.error);
				}
				else
				{
					// Extract the IP address from the downloaded text.
					string ipAddress = webRequest.downloadHandler.text;

					// Clean up the IP address by removing common line endings and trimming whitespace.
					ipAddress = ipAddress.Replace("\r\n", "").Replace("\n", "").Trim();

					// Validate the format of the received IP address.
					if (Constants.IsAddressValid(ipAddress))
					{
						// Log the successful retrieval and the IP address.
						Log.Debug("NetHelper", $"External IP: {ipAddress}");

						// Invoke the success callback with the validated IP address.
						onSuccess?.Invoke(ipAddress);
					}
					else
					{
						// Log an error if the received string is not a valid IP address.
						Log.Error("NetHelper", "Received string is not a valid IP address format.");

						// Invoke the error callback with a descriptive message.
						onError?.Invoke("NetHelper: Received string is not a valid IP address format.");
					}
				}
			}
		}
	}
}