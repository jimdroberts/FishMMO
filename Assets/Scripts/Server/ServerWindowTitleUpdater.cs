using FishNet.Transporting;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Server
{
	public class ServerWindowTitleUpdater : ServerBehaviour
	{
		public WorldSceneSystem WorldSceneSystem;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
		[DllImport("kernel32.dll")]
		private static extern bool SetConsoleTitle(string title);
#elif UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
		[DllImport("libc", EntryPoint = "setproctitle")]
		private static extern void SetProcTitleLinux(string title);
#endif

		public string title = "";
		public float updateRate = 2.0f;
		public float nextUpdate = 0.0f;

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

		private void ServerManager_OnServerConnectionState(ServerConnectionStateArgs obj)
		{
			if (obj.ConnectionState == LocalConnectionState.Started)
			{
				UpdateWindowTitle();
			}
		}

		void LateUpdate()
		{
			if (!ServerManager.Started)
				return;

			nextUpdate -= Time.deltaTime;
			if (nextUpdate < 0)
			{
				nextUpdate = updateRate;

				UpdateWindowTitle();
			}
		}

		public void UpdateWindowTitle()
		{
			title = BuildWindowTitle();

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
			SetConsoleTitle(title);
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
			SetWindowTitle(title);
#elif UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
			SetProcTitleLinux(title);
#endif
		}

		public string BuildWindowTitle()
		{
			string windowTitle;
			if (!Server.configuration.TryGetString("ServerName", out windowTitle))
			{
				windowTitle = "NAMELOADFAIL";
			}

			Transport transport = Server.NetworkManager.TransportManager.Transport;
			if (transport != null)
			{
				int onlineCount = ServerManager.Clients.Count;
				string sceneServerCountText = "";
				if (WorldSceneSystem != null)
				{
					int sceneServerCount = WorldSceneSystem.sceneServers.Count;
					sceneServerCountText = " SceneServers:" + sceneServerCount;
					onlineCount -= sceneServerCount;
				}

				windowTitle += " " + (transport.GetConnectionState(true) == LocalConnectionState.Started ? "[Online]" : "[Offline]");
				
				if (Server.configuration.TryGetString("Address", out string address) &&
					address.Length  > 0 &&
					Server.configuration.TryGetUShort("Port", out ushort port))
				{
					windowTitle += " [Server:" + address + ":" + port + " Clients:" + onlineCount + sceneServerCountText + "]";
				}
				
				if (Server.configuration.TryGetString("RelayAddress", out address) &&
					address.Length > 0 &&
					Server.configuration.TryGetUShort("RelayPort", out port))
				{
					windowTitle += " [ConnectedToRelay:" + address + ":" + port + "]";
				}
			}
			return windowTitle;
		}
	}
}