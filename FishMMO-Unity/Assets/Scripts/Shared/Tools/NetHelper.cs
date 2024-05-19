using System.Net;
using System.Net.Http;
using UnityEngine;

namespace FishMMO.Shared
{
	public static class NetHelper
	{
		public static IPAddress GetExternalIPAddress(string serviceUrl = "https://checkip.amazonaws.com/")
		{
			using (HttpClient client = new HttpClient())
			{
				try
				{
					HttpResponseMessage response = client.GetAsync(serviceUrl).Result;
					response.EnsureSuccessStatusCode();
					string ipAddress = response.Content.ReadAsStringAsync().Result;
					ipAddress = ipAddress.Replace("\r\n", "").Replace("\n", "").Trim();

					if (IPAddress.TryParse(ipAddress, out IPAddress address))
					{
						Debug.Log($"External IP: {ipAddress}");
						return address;
					}
				}
				catch (HttpRequestException e)
				{
					Debug.Log($"Request error: {e.Message}");
				}
			}
			return null;
		}
	}
}