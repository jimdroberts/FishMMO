using FishNet.Transporting;
using System.Text;
using System.Runtime.InteropServices;
using UnityEngine;

namespace FishMMO.Server
{
	public class ServerWindowTitleUpdater : ServerBehaviour
	{
		public WorldSceneSystem WorldSceneSystem;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
		[DllImport("kernel32.dll")]
		private static extern bool SetConsoleTitle(string title);
#elif UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
		private const int PR_SET_NAME = 15;

		[DllImport("libc.so.6", SetLastError=true)]
		private static extern int prctl(int option, string arg2, IntPtr arg3, IntPtr arg4, IntPtr arg5);
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX

		[DllImport("libc.dylib", SetLastError=true)]
		private static extern void setproctitle(string fmt, string str_arg);
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
#elif UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
			prctl(PR_SET_NAME, title, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
			setproctitle("{0}", title);
#endif
		}

		public string BuildWindowTitle()
		{
			StringBuilder windowTitle = new StringBuilder();
			if (Server.configuration.TryGetString("ServerName", out string title))
			{
				windowTitle.Append(title);
			}

			Transport transport = Server.NetworkManager.TransportManager.Transport;
			if (transport != null)
			{
				windowTitle.Append(transport.GetConnectionState(true) == LocalConnectionState.Started ? " [Online]" : " [Offline]");

				int onlineCount = ServerManager.Clients.Count;
				int sceneServerCount = -1;
				if (WorldSceneSystem != null)
				{
					sceneServerCount = WorldSceneSystem.sceneServers.Count;
					onlineCount -= sceneServerCount;
				}
				
				if (Server.configuration.TryGetString("Address", out string address) &&
					address.Length  > 0 &&
					Server.configuration.TryGetUShort("Port", out ushort port))
				{
					windowTitle.Append(" [Server:");
					windowTitle.Append(address);
					windowTitle.Append(":");
					windowTitle.Append(port);
					windowTitle.Append(" Clients:");
					if (sceneServerCount > 0)
					{
						windowTitle.Append(onlineCount - sceneServerCount);
						windowTitle.Append(" SceneServers:");
						windowTitle.Append(sceneServerCount);
					}
					else
					{
						windowTitle.Append(onlineCount);
					}
					windowTitle.Append("]");
				}
				
				if (Server.configuration.TryGetString("RelayAddress", out address) &&
					address.Length > 0 &&
					Server.configuration.TryGetUShort("RelayPort", out port))
				{
					windowTitle.Append(" [ConnectedToRelay:");
					windowTitle.Append(address);
					windowTitle.Append(":");
					windowTitle.Append(port);
					windowTitle.Append("]");
				}
			}
			return windowTitle.ToString();
		}
	}
}