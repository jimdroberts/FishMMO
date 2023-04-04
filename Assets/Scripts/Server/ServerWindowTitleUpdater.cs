using FishNet;
using FishNet.Transporting;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Server
{
	public class ServerWindowTitleUpdater : ServerBehaviour
	{
		public WorldSceneSystem WorldSceneSystem;

		//Import the following.
		[DllImport("user32.dll", EntryPoint = "SetWindowText", CharSet = CharSet.Unicode)]
		public static extern bool SetWindowText(IntPtr hwnd, String lpString);

		public string windowTitle = "";
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
				string title = BuildWindowTitle();

				//Set the title text using the window handle.
				SetWindowText(Process.GetCurrentProcess().MainWindowHandle, title);
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

				string title = BuildWindowTitle();

				SetWindowText(Process.GetCurrentProcess().MainWindowHandle, title);
			}
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