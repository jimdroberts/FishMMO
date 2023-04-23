using FishNet.Transporting;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Server
{
	public class ServerWindowTitleUpdater : ServerBehaviour
	{
		public WorldSceneSystem WorldSceneSystem;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
		[DllImport("user32.dll")]
		static extern bool SetWindowText(System.IntPtr hWnd, string lpString);
		[DllImport("user32.dll")]
		static extern System.IntPtr GetActiveWindow();
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        [DllImport("__Internal")]
        static extern void SetWindowTitle(string title);
#elif UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
        [DllImport("libX11")]
        static extern System.IntPtr XOpenDisplay(string display_name);
        [DllImport("libX11")]
        static extern void XCloseDisplay(System.IntPtr display);
        [DllImport("libX11")]
        static extern void XStoreName(System.IntPtr display, System.IntPtr w, string title);
        [DllImport("libX11")]
        static extern System.IntPtr XRootWindow(System.IntPtr display, int screen_number);
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
			SetWindowText(GetActiveWindow(), title);
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
			SetWindowTitle(title);
#elif UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
			// Get the display and root window for the default screen
			System.IntPtr display = XOpenDisplay(null);
			System.IntPtr root = XRootWindow(display, 0);

			// Set the window title using XStoreName
			XStoreName(display, root, title);

			// Clean up
			XCloseDisplay(display);
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