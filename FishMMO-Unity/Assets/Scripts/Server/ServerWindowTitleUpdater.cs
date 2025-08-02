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

namespace FishMMO.Server
{
	/// <summary>
	/// Updates the server window or console title to reflect current server status, including transport type, connection state, and client count.
	/// Supports Windows, Linux, and OSX platforms.
	/// </summary>
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
		/// The current window or console title for the server.
		/// </summary>
		public string Title = "";
		/// <summary>
		/// How often (in seconds) to update the window title.
		/// </summary>
		public float UpdateRate = 15.0f;
		/// <summary>
		/// Time remaining until the next window title update.
		/// </summary>
		public float NextUpdate = 0.0f;

		/// <summary>
		/// Called once to initialize the server window title updater.
		/// Disables the component if ServerManager is not available.
		/// </summary>
		public override void InitializeOnce()
		{
			if (ServerManager != null)
			{
				UpdateWindowTitle();
			}
			else
			{
				enabled = false;
			}
		}

		/// <summary>
		/// Called when the object is being destroyed. No custom logic implemented.
		/// </summary>
		public override void Destroying()
		{
		}

		/// <summary>
		/// Updates the window title at the specified rate while the server is running.
		/// </summary>
		void LateUpdate()
		{
			if (ServerManager == null ||
				!ServerManager.Started)
			{
				return;
			}
			// Only update when NextUpdate is less than zero.
			if (NextUpdate < 0)
			{
				NextUpdate = UpdateRate;

				UpdateWindowTitle();
			}
			NextUpdate -= Time.deltaTime;
		}

		/// <summary>
		/// Updates the window or console title to reflect current server status.
		/// Uses platform-specific APIs to set the title.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void UpdateWindowTitle()
		{
			Title = null;
			Title = BuildWindowTitle();

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
			SetConsoleTitle(Title);
#elif UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
			prctl(PR_SET_NAME, Title, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
			setproctitle("{0}", Title);
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
				if (Configuration.GlobalSettings.TryGetString("ServerName", out string title))
				{
					windowTitle.Append(title);
				}

				// Add transport type and connection state.
				if (Server.NetworkManager != null &&
					Server.NetworkManager.TransportManager != null)
				{
					Multipass multipass = Server.NetworkManager.TransportManager.GetTransport<Multipass>();
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
						Transport transport = Server.NetworkManager.TransportManager.Transport;
						if (transport != null)
						{
							windowTitle.Append($" [{transport.GetType().Name}]");
							windowTitle.Append(transport.GetConnectionState(true) == LocalConnectionState.Started ? "[Online]" : "[Offline]");
						}
					}

					// Add port, remote address, and client count.
					if (Configuration.GlobalSettings.TryGetUShort("Port", out ushort port))
					{
						windowTitle.Append(" [Server:");
						windowTitle.Append(Server.RemoteAddress);
						windowTitle.Append(":");
						windowTitle.Append(port);
						windowTitle.Append(" Clients:");
						// Use WorldSceneSystem's ConnectionCount if available, otherwise fallback to ServerManager.Clients.Count.
						windowTitle.Append(ServerBehaviour.TryGet(out WorldSceneSystem worldSceneSystem) ? worldSceneSystem.ConnectionCount : ServerManager.Clients.Count);
						windowTitle.Append("]");
					}
				}
				return windowTitle.ToString();
			}
		}
	}
}