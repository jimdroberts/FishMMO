using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Serializable class containing a list of server ports and a one-time
	/// connection token for real-IP recovery on the game server.
	/// </summary>
	[Serializable]
	public class ServerAddresses
	{
		/// <summary>List of available server addresses (internal use).</summary>
		[field: SerializeField]
		public List<ServerAddress> Addresses { get; set; } = new List<ServerAddress>();
		/// <summary>List of available server ports (client use).</summary>
		[field: SerializeField]
		public List<ushort> Ports { get; set; } = new List<ushort>();
		/// <summary>
		/// One-time connection token issued by IPFetch. The client echoes this
		/// in the first ClientHandshake so the Login Server can recover the
		/// real client IP that was visible to the HTTP layer (X-Forwarded-For)
		/// but is lost through the L4 UDP proxy.
		/// </summary>
		[field: SerializeField]
		public string ConnectionToken { get; set; }
	}

	/// <summary>
	/// Internal server bind address. For client-facing communication,
	/// the address is always Constants.Configuration.GameHost — use
	/// Port directly or ServerAddresses.Ports.
	///
	/// Converted from a mutable struct to a class to avoid the
	/// pass-by-value silent data loss anti-pattern. The previous struct
	/// warning about this has been removed since the class semantics
	/// eliminate the issue: instances are passed by reference and
	/// mutations are not silently lost.
	/// </summary>
	[Serializable]
	public class ServerAddress
	{
		/// <summary>
		/// IP address or hostname the server binds to.
		///
		/// NOTE: Per class documentation, Address is always Constants.Configuration.GameHost
		/// for client-facing communication. This field exists for future multi-host support.
		/// </summary>
		public string Address;
		/// <summary>Port number for the server.</summary>
		public ushort Port;
	}
}