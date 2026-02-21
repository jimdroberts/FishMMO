using FishNet.Connection;
using FishNet.Transporting;
using FishNet.Transporting.Multipass;
#if UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
using System;
#endif
using System.Runtime.InteropServices;
using UnityEngine;
using Cysharp.Text;
using System.Runtime.CompilerServices;
using FishMMO.Shared;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.WorldServer;

namespace FishMMO.Server.Implementation
{
	/// <summary>
	/// Updates the server window or console title to reflect current server status, including transport type, connection state, and client count.
	/// Supports Windows, Linux, and OSX platforms.
	/// </summary>
	[CreateAssetMenu(fileName = "ServerWindowTitleUpdater", menuName = "FishMMO/Server/Server Window Title Updater", order = 1)]
	[RequiresDataContainer(typeof(ServerWindowTitleUpdaterRuntimeData))]
	public class ServerWindowTitleUpdater : ServerBehaviour
	{
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
		/// <summary>
		/// Sets the console title on Windows platforms.
		/// </summary>
		[DllImport("kernel32.dll")]
		private static extern bool SetConsoleTitle(string title);
#elif UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
		/// <summary>
		/// Option value for prctl to set process name on Linux.
		/// </summary>
		private const int PR_SET_NAME = 15;

		/// <summary>
		/// Sets the process title on Linux platforms.
		/// </summary>
		[DllImport("libc.so.6", SetLastError=true)]
		private static extern int prctl(int option, string arg2, IntPtr arg3, IntPtr arg4, IntPtr arg5);
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
		/// <summary>
		/// Sets the process title on OSX platforms.
		/// </summary>
		[DllImport("libc.dylib", SetLastError=true)]
		private static extern void setproctitle(string fmt, string str_arg);
#endif

		/// <summary>
		/// How often (in seconds) to update the window title.
		/// </summary>
		[SerializeField] private float updateRate = 15.0f;

		/// <summary>
		/// Called once to initialize the server window title updater.
		/// Validates that the required RuntimeDataContainer is available.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			if (Server == null)
			{
				return ServerComponentInitializationStatus.FailedToFindServer;
			}

			if (!Server.DataContainerRegistry.TryGet<ServerWindowTitleUpdaterRuntimeData>(out _))
			{
				return ServerComponentInitializationStatus.FailedToGetDataContainer;
			}

			UpdateWindowTitle();

			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Called when the object is being destroyed. No custom logic implemented.
		/// </summary>
		public override void OnDeinitialize()
		{
		}

		/// <summary>
		/// Called by the server's LateUpdate. Updates the window title at the specified rate while the server is running.
		/// </summary>
		/// <param name="deltaTime">Time elapsed since last frame.</param>
		public override void OnLateUpdate(float deltaTime)
		{
			if (ServerManager == null ||
				!ServerManager.Started)
			{
				return;
			}

			if (!Server.DataContainerRegistry.TryGet<ServerWindowTitleUpdaterRuntimeData>(out var runtimeData))
			{
				return;
			}

			// Only update when NextUpdate is less than zero.
			if (runtimeData.NextUpdate < 0)
			{
				runtimeData.NextUpdate = updateRate;

				UpdateWindowTitle();
			}
			runtimeData.NextUpdate -= deltaTime;
		}

		/// <summary>
		/// Updates the window or console title to reflect current server status.
		/// Uses platform-specific APIs to set the title.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void UpdateWindowTitle()
		{
			if (!Server.DataContainerRegistry.TryGet<ServerWindowTitleUpdaterRuntimeData>(out var runtimeData))
			{
				return;
			}

			runtimeData.Title = BuildWindowTitle();

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
			SetConsoleTitle(runtimeData.Title);
#elif UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
			prctl(PR_SET_NAME, runtimeData.Title, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
			setproctitle("{0}", runtimeData.Title);
#endif
		}

		/// <summary>
		/// Builds the window title string based on server configuration, transport type, connection state, port, and client count.
		/// </summary>
		/// <returns>The formatted window title string.</returns>
		public string BuildWindowTitle()
		{
			if (Server == null)
			{
				return "";
			}
			using (var windowTitle = ZString.CreateStringBuilder())
			{
				// Add server name from configuration if available.
				if (Server.Configuration.TryGetString("ServerName", out string title))
				{
					windowTitle.Append(title);
				}

				// Add transport type and connection state.
				if (Server.NetworkWrapper.NetworkManager != null &&
					Server.NetworkWrapper.NetworkManager.TransportManager != null)
				{
					Multipass multipass = Server.NetworkWrapper.NetworkManager.TransportManager.GetTransport<Multipass>();
					if (multipass != null)
					{
						for (int i = 0; i < multipass.Transports.Count; ++i)
						{
							Transport transport = multipass.Transports[i];

							windowTitle.Append($" [{transport.GetType().Name}]");
							windowTitle.Append(transport.GetConnectionState(true) == LocalConnectionState.Started ? "[Online]" : "[Offline]");
						}
					}
					else
					{
						Transport transport = Server.NetworkWrapper.NetworkManager.TransportManager.Transport;
						if (transport != null)
						{
							windowTitle.Append($" [{transport.GetType().Name}]");
							windowTitle.Append(transport.GetConnectionState(true) == LocalConnectionState.Started ? "[Online]" : "[Offline]");
						}
					}

					// Add port, remote address, and client count.
					if (Server.Configuration.TryGetUShort("Port", out ushort port))
					{
						windowTitle.Append(" [Server:");
						windowTitle.Append(Server.CoreServer.RemoteAddress);
						windowTitle.Append(":");
						windowTitle.Append(port);
						windowTitle.Append(" Clients:");
						// Use WorldSceneMappingData's ConnectionCount if available, otherwise fallback to ServerManager.Clients.Count.
						windowTitle.Append(Server.DataContainerRegistry.TryGet<IWorldSceneMappingData<NetworkConnection>>(out var sceneData) ? sceneData.ConnectionCount : ServerManager.Clients.Count);
						windowTitle.Append("]");
					}
				}
				return windowTitle.ToString();
			}
		}
	}
}