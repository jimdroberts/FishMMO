using FishNet.Managing;
using FishNet.Transporting;
using FishNet.Managing.Scened;
using FishNet.Managing.Timing;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.SceneManagement;
using System;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FishMMO.Client
{
	/// <summary>
	/// Client controls connecting to servers, 
	/// </summary>
	public class Client : MonoBehaviour
	{
		private LocalConnectionState clientState = LocalConnectionState.Stopped;
		private Dictionary<string, Scene> serverLoadedScenes = new Dictionary<string, Scene>();

		private const byte reconnectAttempts = 10;
		private const float firstReconnectAttemptWaitTime = 10f;
		private byte reconnectsAttempted = 0;
		private bool forceDisconnect = false;
		private string lastAddress = "";
		private ushort lastPort = 0;
		private float timeTillFirstReconnectAttempt = 0;
		private bool reconnectActive = false;

		public List<ServerAddress> LoginServerAddresses;
		public Configuration Configuration = null;

		public event Action OnConnectionSuccessful;
		public event Action<byte, byte> OnReconnectAttempt;
		public event Action OnReconnectFailed;
		public event Action OnQuitToLogin;

		public static TimeManager TimeManager { get; private set; }
		public NetworkManager NetworkManager;
		public ClientLoginAuthenticator LoginAuthenticator;

		void Awake()
		{
			if (NetworkManager == null)
			{
				Debug.LogError("Client: NetworkManager not found.");
				Quit();
			}
			else if (LoginAuthenticator == null)
			{
				Debug.LogError("Client: LoginAuthenticator not found.");
				Quit();
			}
			else
			{
				// initialize naming service
				ClientNamingSystem.InitializeOnce(this);

				// assign the clients TimeManager
				TimeManager = NetworkManager.TimeManager;

				// assign the client to the Login Authenticator
				LoginAuthenticator.SetClient(this);

				string path = Client.GetWorkingDirectory();

				// load configuration
				Configuration = new Configuration(path);
				if (!Configuration.Load(Configuration.DEFAULT_FILENAME + Configuration.EXTENSION))
				{
					// if we failed to load the file.. save a new one
					Configuration.Set("Resolution Width", 1280);
					Configuration.Set("Resolution Height", 800);
					Configuration.Set("Refresh Rate", 60);
					Configuration.Set("Fullscreen", false);
					Configuration.Save();
				}

				if (Configuration.TryGetInt("Resolution Width", out int width) &&
					Configuration.TryGetInt("Resolution Height", out int height) &&
					Configuration.TryGetInt("Refresh Rate", out int refreshRate) &&
					Configuration.TryGetBool("Fullscreen", out bool fullscreen))
				{
#if !UNITY_WEBGL
					Screen.SetResolution(width, height, fullscreen ? FullScreenMode.ExclusiveFullScreen : FullScreenMode.Windowed, refreshRate);
#endif
				}
				
				// do dependency injection here if needed
				UIManager.SetClient(this);

				UnityEngine.SceneManagement.SceneManager.sceneLoaded += UnitySceneManager_OnSceneLoaded;
				UnityEngine.SceneManagement.SceneManager.sceneUnloaded += UnitySceneManager_OnSceneUnloaded;
				NetworkManager.ClientManager.OnClientConnectionState += ClientManager_OnClientConnectionState;
				NetworkManager.SceneManager.OnLoadStart += SceneManager_OnLoadStart;
				NetworkManager.SceneManager.OnLoadPercentChange += SceneManager_OnLoadPercentChange;
				NetworkManager.SceneManager.OnLoadEnd += SceneManager_OnLoadEnd;
				NetworkManager.SceneManager.OnUnloadStart += SceneManager_OnUnloadStart;
				NetworkManager.SceneManager.OnUnloadEnd += SceneManager_OnUnloadEnd;
				LoginAuthenticator.OnClientAuthenticationResult += Authenticator_OnClientAuthenticationResult;

				NetworkManager.ClientManager.RegisterBroadcast<SceneWorldReconnectBroadcast>(OnClientSceneWorldReconnectBroadcastReceived);
			}
		}

		private void Update()
		{
			if(!forceDisconnect &&
				clientState == LocalConnectionState.Stopped &&
				timeTillFirstReconnectAttempt > 0)
			{
				timeTillFirstReconnectAttempt -= Time.deltaTime;

				if (timeTillFirstReconnectAttempt < 0)
				{
					reconnectActive = true;
					OnTryReconnect();
				}
			}
		}

		public static string GetWorkingDirectory()
		{
#if UNITY_EDITOR
			return Directory.GetParent(Directory.GetParent(Application.dataPath).FullName).FullName;
#else
			return AppDomain.CurrentDomain.BaseDirectory;
#endif
		}

		public void OnDestroy()
		{
			ClientNamingSystem.Destroy();
		}

		public void Quit()
		{
#if UNITY_EDITOR
			EditorApplication.ExitPlaymode();
#else
			Application.Quit();
#endif
		}

		public void QuitToLogin()
		{
			OnQuitToLogin?.Invoke();

			ForceDisconnect();

			UnloadServerLoadedScenes();
		}

		/// <summary>
		/// SceneServer told the client to reconnect to the World server
		/// </summary>
		private void OnClientSceneWorldReconnectBroadcastReceived(SceneWorldReconnectBroadcast msg)
		{
			ConnectToServer(msg.address, msg.port);
		}

		/// <summary>
		/// Checks if the current connection is valid and started. Optional authentication check (Default True).
		/// </summary>
		public bool IsConnectionReady()
		{
			return IsConnectionReady(LocalConnectionState.Started, true);
		}
		/// <summary>
		/// Checks if the current connection is valid and started. Optional authentication check (Default True).
		/// </summary>
		public bool IsConnectionReady(bool requireAuthentication)
		{
			return IsConnectionReady(LocalConnectionState.Started, requireAuthentication);
		}
		/// <summary>
		/// Checks if the current connection is valid and started. Optional authentication check (Default True).
		/// </summary>
		public bool IsConnectionReady(LocalConnectionState clientState = LocalConnectionState.Started)
		{
			return IsConnectionReady(clientState, false);
		}
		/// <summary>
		/// Checks if the current connection is valid and started. Optional authentication check (Default True).
		/// </summary>
		public bool IsConnectionReady(LocalConnectionState clientState, bool requireAuthentication)
		{
			if (LoginAuthenticator == null ||
				NetworkManager == null ||
				this.clientState != clientState)
			{
				return false;
			}

			if (requireAuthentication &&
				(!NetworkManager.ClientManager.Connection.IsValid ||
				!NetworkManager.ClientManager.Connection.Authenticated))
			{
				return false;
			}

			return true;
		}

		private void ClientManager_OnClientConnectionState(ClientConnectionStateArgs obj)
		{
			clientState = obj.ConnectionState;

			switch (clientState)
			{
				case LocalConnectionState.Stopped:
					if (!forceDisconnect && reconnectActive)
					{
						OnTryReconnect();
					}
					else if (!forceDisconnect)
					{
						timeTillFirstReconnectAttempt = firstReconnectAttemptWaitTime;
					}
					forceDisconnect = false;
					break;
				case LocalConnectionState.Started:
					OnConnectionSuccessful?.Invoke();
					reconnectsAttempted = 0;
					timeTillFirstReconnectAttempt = -1;
					reconnectActive = false;
					forceDisconnect = false;
					break;
			}
		}

		private void Authenticator_OnClientAuthenticationResult(ClientAuthenticationResult result)
		{
			switch (result)
			{
				case ClientAuthenticationResult.AccountCreated:
					break;
				case ClientAuthenticationResult.InvalidUsernameOrPassword:
					break;
				case ClientAuthenticationResult.AlreadyOnline:
					break;
				case ClientAuthenticationResult.Banned:
					break;
				case ClientAuthenticationResult.LoginSuccess:
					break;
				case ClientAuthenticationResult.WorldLoginSuccess:
					break;
				case ClientAuthenticationResult.SceneLoginSuccess:
					break;
				case ClientAuthenticationResult.ServerFull:
					break;
				default:
					break;
			}
		}

		public void ConnectToServer(string address, ushort port)
		{
			// stop current connection if any
			NetworkManager.ClientManager.StopConnection();

			// connect to the server
			StartCoroutine(OnAwaitingConnectionReady(address, port));
		}

		public void OnTryReconnect()
		{
			if (reconnectsAttempted < reconnectAttempts)
			{
				if (IsAddressValid(lastAddress) && lastPort != 0)
				{
					++reconnectsAttempted;
					OnReconnectAttempt?.Invoke(reconnectsAttempted, reconnectAttempts);
					ConnectToServer(lastAddress, lastPort);
				}
			}
			else
			{
				reconnectsAttempted = 0;
				OnReconnectFailed?.Invoke();
			}
		}

		IEnumerator OnAwaitingConnectionReady(string address, ushort port)
		{
			// wait for the connection to the current server to stop
			while (clientState != LocalConnectionState.Stopped)
			{
				yield return new WaitForSeconds(0.2f);
			}

			if (forceDisconnect)
			{
				forceDisconnect = false;
				yield return null;
			}

			lastAddress = address;
			lastPort = port;

			// connect to the next server
			NetworkManager.ClientManager.StartConnection(address, port);

			yield return null;
		}

		public void ReconnectCancel()
		{
			OnReconnectFailed?.Invoke();
			ForceDisconnect();
		}

		public void ForceDisconnect()
		{
			forceDisconnect = true;

			// stop current connection if any
			NetworkManager.ClientManager.StopConnection();
		}

		public bool TryGetRandomLoginServerAddress(out ServerAddress serverAddress)
		{
			if (LoginServerAddresses != null && LoginServerAddresses.Count > 0)
			{
				// pick a random login server
				serverAddress = LoginServerAddresses.GetRandom();
				return true;
			}
			serverAddress = default;
			return false;
		}

		/// <summary>
		/// IPv4 Regex, can we get IPv6 support???
		/// </summary>
		public bool IsAddressValid(string address)
		{
			const string ValidIpAddressRegex = "^(([0-9]|[1-9][0-9]|1[0-9]{2}|2[0-4][0-9]|25[0-5])\\.){3}([0-9]|[1-9][0-9]|1[0-9]{2}|2[0-4][0-9]|25[0-5])$";
			Match match = Regex.Match(address, ValidIpAddressRegex);
			return match.Success;
		}

		private void UnitySceneManager_OnSceneLoaded(Scene scene, LoadSceneMode mode)
		{
			// ClientBootstrap overrides active scene if it is ever loaded.
			if (scene.name.Contains("ClientBootstrap"))
			{
				UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
			}
		}

		private void UnitySceneManager_OnSceneUnloaded(Scene scene)
		{
		}

		private void SceneManager_OnLoadStart(SceneLoadStartEventArgs args)
		{
			// unload previous scene
			UnloadServerLoadedScenes();
		}

		private void UnloadServerLoadedScenes()
		{
			foreach (Scene scene in serverLoadedScenes.Values)
			{
				UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(scene);
			}
			serverLoadedScenes.Clear();
		}

		private void SceneManager_OnLoadPercentChange(SceneLoadPercentEventArgs args)
		{
		}

		private void SceneManager_OnLoadEnd(SceneLoadEndEventArgs args)
		{
			// add loaded scenes to list
			if (args.LoadedScenes != null)
			{
				foreach (Scene scene in args.LoadedScenes)
				{
					if (!serverLoadedScenes.ContainsKey(scene.name))
					{
						serverLoadedScenes.Add(scene.name, scene);
					}
				}
			}
		}

		private void SceneManager_OnUnloadStart(SceneUnloadStartEventArgs args)
		{
		}

		private void SceneManager_OnUnloadEnd(SceneUnloadEndEventArgs args)
		{
		}
	}
}