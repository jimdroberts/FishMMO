using FishNet.Managing.Server;
using FishNet.Transporting;
using System;
using UnityEngine;

namespace Server
{
	// World Manager handles the database heartbeat for Login Service
	public class WorldServerSystem : ServerBehaviour
	{
		private LocalConnectionState serverState;

		public bool locked = false;
		public float pulseRate = 10.0f;
		private float nextPulse = 0.0f;

		public override void InitializeOnce()
		{
			if (ServerManager != null)
			{
				ServerManager.OnServerConnectionState += ServerManager_OnServerConnectionState;
			}
			else
			{
				enabled = false;
			}
		}

		private void ServerManager_OnServerConnectionState(ServerConnectionStateArgs args)
		{
			serverState = args.ConnectionState;

			if (args.ConnectionState == LocalConnectionState.Started)
			{
				if (TryGetServerIPv4AddressFromTransport(out ServerAddress server))
				{
					int characterCount = ServerManager.Clients.Count;

					if (Server.configuration.TryGetString("ServerName", out string name))
					{
						Debug.Log("Adding World Server to Database: " + name + ":" + server.address + ":" + server.port);
						Database.Instance.AddWorldServer(name, server.address, server.port, characterCount, locked);
					}
				}
			}
			else if (args.ConnectionState == LocalConnectionState.Stopped)
			{
				if (Server.configuration.TryGetString("ServerName", out string name))
				{
					Debug.Log("Removing World Server from Database: " + name);
					Database.Instance.DeleteWorldServer(name);
				}
			}
		}

		void LateUpdate()
		{
			if (serverState == LocalConnectionState.Started &&
				Server.configuration.TryGetString("ServerName", out string name))
			{
				nextPulse -= Time.deltaTime;
				if (nextPulse < 0)
				{
					nextPulse = pulseRate;
					Debug.Log("[" + DateTime.UtcNow + "] " + name + ": Pulse");
					Database.Instance.WorldServerPulse(name);
				}
			}
		}

		private void OnApplicationQuit()
		{
			if (Server.configuration.TryGetString("ServerName", out string name))
			{
				Debug.Log("Removing World Server: " + name);
				Database.Instance.DeleteWorldServer(name);
			}
		}

		private bool TryGetServerIPv4AddressFromTransport(out ServerAddress address)
		{
			Transport transport = Server.NetworkManager.TransportManager.Transport;
			if (transport == null)
			{
				address = default;
				return false;
			}
			address = new ServerAddress()
			{
				address = transport.GetServerBindAddress(IPAddressType.IPv4),
				port = transport.GetPort(),
			};
			return true;
		}

		private bool TryGetServerIPv6AddressFromTransport(out ServerAddress address)
		{
			Transport transport = Server.NetworkManager.TransportManager.Transport;
			if (transport == null)
			{
				address = default;
				return false;
			}
			address = new ServerAddress()
			{
				address = transport.GetServerBindAddress(IPAddressType.IPv6),
				port = transport.GetPort(),
			};
			return true;
		}
	}
}