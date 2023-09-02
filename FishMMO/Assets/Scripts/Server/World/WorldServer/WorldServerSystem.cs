using FishNet.Managing.Server;
using FishNet.Transporting;
using System;
using FishMMO.Server.Services;
using UnityEngine;

namespace FishMMO.Server
{
	// World Manager handles the database heartbeat for Login Service
	public class WorldServerSystem : ServerBehaviour
	{
		private LocalConnectionState serverState;

		private bool locked = false;
		private float pulseRate = 10.0f;
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
			using var dbContext = Server.DbContextFactory.CreateDbContext();

			if (args.ConnectionState == LocalConnectionState.Started)
			{
				if (TryGetServerIPv4AddressFromTransport(out ServerAddress server))
				{
					int characterCount = ServerManager.Clients.Count;

					if (Server.Configuration.TryGetString("ServerName", out string name))
					{
						Debug.Log("Adding World Server to Database: " + name + ":" + server.address + ":" + server.port);
						WorldServerService.AddWorldServer(dbContext, name, server.address, server.port, characterCount, locked);
						dbContext.SaveChanges();
					}
				}
			}
			else if (args.ConnectionState == LocalConnectionState.Stopped)
			{
				if (Server.Configuration.TryGetString("ServerName", out string name))
				{
					Debug.Log("Removing World Server from Database: " + name);
					WorldServerService.DeleteWorldServer(dbContext, name);
					dbContext.SaveChanges();
				}
			}
		}

		void LateUpdate()
		{
			if (serverState == LocalConnectionState.Started &&
				Server.Configuration.TryGetString("ServerName", out string name))
			{
				nextPulse -= Time.deltaTime;
				if (nextPulse < 0)
				{
					// TODO: maybe this one should exist....how expensive will this be to run on update?
					using var dbContext = Server.DbContextFactory.CreateDbContext();
				
					nextPulse = pulseRate;
					Debug.Log("[" + DateTime.UtcNow + "] " + name + ": Pulse");
					int characterCount = ServerManager.Clients.Count;
					WorldServerService.WorldServerPulse(dbContext, name, characterCount);
					dbContext.SaveChanges();
				}
			}
		}

		private void OnApplicationQuit()
		{
			if (Server.Configuration.TryGetString("ServerName", out string name))
			{
				using var dbContext = Server.DbContextFactory.CreateDbContext();
				Debug.Log("Removing World Server: " + name);
				WorldServerService.DeleteWorldServer(dbContext, name);
				dbContext.SaveChanges();
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
