using System.Net;
using System.Net.Sockets;

namespace FishMMO.WebServer
{
	/// <summary>
	/// The game server's public address, for the connection help page.
	/// </summary>
	/// <remarks>
	/// Some security software excludes traffic by IP address only, so the help page has to show
	/// the address the game connects to. It is resolved from the configured <c>GameHost</c>
	/// rather than written into the page, because the address can change. Results are cached
	/// briefly so the page cannot be used to make this server hammer DNS.
	/// </remarks>
	public static class GameAddress
	{
		/// <summary>How long a lookup is reused.</summary>
		private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(5);

		private static readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
		private static string[] cachedAddresses = Array.Empty<string>();
		private static string? cachedHost;
		private static DateTime cachedAt = DateTime.MinValue;

		/// <summary>
		/// The IPv4 and IPv6 addresses <paramref name="host"/> resolves to, IPv4 first. Empty when it cannot be resolved.
		/// </summary>
		public static async Task<string[]> ResolveAsync(string host, CancellationToken token)
		{
			if (string.IsNullOrWhiteSpace(host))
			{
				return Array.Empty<string>();
			}
			if (IPAddress.TryParse(host, out IPAddress? literal))
			{
				return new[] { literal.ToString() };
			}

			await gate.WaitAsync(token);
			try
			{
				if (cachedHost == host && DateTime.UtcNow - cachedAt < CacheFor)
				{
					return cachedAddresses;
				}

				string[] addresses;
				try
				{
					IPAddress[] found = await Dns.GetHostAddressesAsync(host, token);
					addresses = found
						.Where(a => !IPAddress.IsLoopback(a))
						.OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1)
						.Select(a => a.ToString())
						.Distinct()
						.ToArray();
				}
				catch (SocketException)
				{
					addresses = Array.Empty<string>();
				}

				cachedHost = host;
				cachedAddresses = addresses;
				cachedAt = DateTime.UtcNow;
				return addresses;
			}
			finally
			{
				gate.Release();
			}
		}
	}
}
