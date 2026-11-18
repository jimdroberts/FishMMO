using System;
using System.Collections.Generic;

namespace FishMMO.Server.Implementation
{
	/// <summary>
	/// Counts the connections each IP address holds, so one address cannot occupy a server.
	/// </summary>
	internal sealed class ConnectionsPerIpLimiter
	{
		private readonly object gate = new object();
		private readonly Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<int, string> holders = new Dictionary<int, string>();

		/// <summary>
		/// Takes a slot for a connection. A connection holds one slot; presenting the same address
		/// again changes nothing, and a different address moves its slot.
		/// </summary>
		/// <param name="connectionId">The connection asking.</param>
		/// <param name="ip">The connection's address. Empty is always allowed, and takes no slot.</param>
		/// <param name="maximum">Slots an address may hold; zero or less means no limit.</param>
		/// <returns>False when the address already holds <paramref name="maximum"/> slots.</returns>
		public bool TryAcquire(int connectionId, string ip, int maximum)
		{
			if (string.IsNullOrEmpty(ip))
			{
				return true;
			}

			lock (gate)
			{
				if (holders.TryGetValue(connectionId, out string held))
				{
					if (string.Equals(held, ip, StringComparison.OrdinalIgnoreCase))
					{
						return true;
					}
				}

				counts.TryGetValue(ip, out int count);
				if (maximum > 0 && count >= maximum)
				{
					return false;
				}

				if (held != null)
				{
					ReleaseLocked(connectionId, held);
				}
				counts[ip] = count + 1;
				holders[connectionId] = ip;
				return true;
			}
		}

		/// <summary>Gives back a connection's slot, if it holds one.</summary>
		public void Release(int connectionId)
		{
			lock (gate)
			{
				if (holders.TryGetValue(connectionId, out string held))
				{
					ReleaseLocked(connectionId, held);
				}
			}
		}

		/// <summary>The slots an address holds.</summary>
		public int CountFor(string ip)
		{
			lock (gate)
			{
				return counts.TryGetValue(ip, out int count) ? count : 0;
			}
		}

		private void ReleaseLocked(int connectionId, string ip)
		{
			holders.Remove(connectionId);
			int remaining = counts[ip] - 1;
			if (remaining > 0)
			{
				counts[ip] = remaining;
			}
			else
			{
				counts.Remove(ip);
			}
		}
	}
}
