using FishNet.Transporting;
using FishNet.Broadcast;
using FishNet.Managing;
using FishNet.Managing.Transporting;
using FishNet.Transporting.Multipass;
using FishNet.Transporting.Tugboat;
using FishNet.Transporting.Bayou;
using FishNet.Managing.Scened;
using FishMMO.Shared;
using FishMMO.Auth.Core;
using FishMMO.Shared.Core;
using FishMMO.Logging;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using System;
using KinematicCharacterController;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FishMMO.Client
{
	/// <summary>
	/// Thin orchestrator for the client. Delegates connection lifecycle to
	/// <see cref="ClientConnectionManager"/>, combat display to <see cref="ClientCombatDisplay"/>,
	/// and fog to <see cref="ClientFogManager"/>. Handles login server discovery and character
	/// lifecycle events that must live on the root GameObject.
	/// </summary>
	public class Client : MonoBehaviour
	{
		// ── Orchestrated services ───────────────────────────────────────

		public ClientConnectionManager Connection { get; private set; }
		private ClientCombatDisplay combatDisplay;
		private ClientFogManager fogManager;

		// ── Login server discovery ──────────────────────────────────────

		public List<ServerAddress> LoginServerAddresses;
		private float loginServerAddressesFetchedAt = float.NegativeInfinity;
		public float LoginServerCacheTtlSeconds = 300f;
		public int LoginServerRequestTimeoutSeconds = 10;

		// ── Scene preloading ────────────────────────────────────────────

		public List<AddressableSceneLoadData> WorldPreloadScenes = new List<AddressableSceneLoadData>();
		private Dictionary<int, Scene> loadedWorldScenes = new Dictionary<int, Scene>();

		// ── Events ──────────────────────────────────────────────────────

		public event Action OnEnterGameWorld;
		public event Action OnQuitToLogin;

		/// <summary>Forwarded to <see cref="ClientConnectionManager.OnReconnectAttempt"/>.</summary>
		public event Action<byte, byte> OnReconnectAttempt
		{
			add { if (Connection != null) Connection.OnReconnectAttempt += value; }
			remove { if (Connection != null) Connection.OnReconnectAttempt -= value; }
		}
		/// <summary>Forwarded to <see cref="ClientConnectionManager.OnReconnectFailed"/>.</summary>
		public event Action OnReconnectFailed
		{
			add { if (Connection != null) Connection.OnReconnectFailed += value; }
			remove { if (Connection != null) Connection.OnReconnectFailed -= value; }
		}
		/// <summary>Forwarded to <see cref="ClientConnectionManager.OnConnectionSuccessful"/>.</summary>
		public event Action OnConnectionSuccessful
		{
			add { if (Connection != null) Connection.OnConnectionSuccessful += value; }
			remove { if (Connection != null) Connection.OnConnectionSuccessful -= value; }
		}

		// ── References ──────────────────────────────────────────────────

		public static NetworkManager NetworkManager;
		public ClientLoginAuthenticator LoginAuthenticator;
		public AudioListener AudioListener;
		public ClientPostbootSystem ClientPostbootSystem;
		public ServerConnectionType CurrentConnectionType => Connection?.CurrentConnectionType ?? ServerConnectionType.None;

		// ── Region display ──────────────────────────────────────────────

		private UIAdvancedLabel regionNameLabel;

		// ── Lifecycle ───────────────────────────────────────────────────

		void Awake()
		{
			if (!TryInitializeNetworkManager() || !TryInitializeAuthenticator() || !TryInitializeTransport())
			{ Quit(); return; }

			Application.logMessageReceived += OnLogMessage;

			if (AudioListener == null && Camera.main != null)
				AudioListener = Camera.main.gameObject.GetComponent<AudioListener>();

			ClientPostbootSystem?.SetClient(this);
			UIManager.SetClient(this);
			ClientNamingSystem.Initialize(this);

			KinematicCharacterSystem.EnsureCreation();
			KinematicCharacterSystem.Settings.AutoSimulation = false;

			Connection = new ClientConnectionManager(NetworkManager);
			Connection.OnReconnectFailed += () => OnQuitToLogin?.Invoke();

			combatDisplay = new ClientCombatDisplay();
			combatDisplay.Initialize();

			fogManager = new ClientFogManager(this);
			fogManager.Initialize();

	#if !UNITY_SERVER
			IPlayerCharacter.OnReadPayload += OnCharacterReadPayload;
			IPlayerCharacter.OnStartLocalClient += OnCharacterStartLocal;
			IPlayerCharacter.OnStopLocalClient += OnCharacterStopLocal;
			IGuildController.OnReadID += OnGuildReadId;
			Pet.OnReadID += OnPetReadId;
			regionNameLabel = UIAdvancedLabel.Create("", FontStyle.Normal, null, 0, Color.magenta, 0, false, false, Vector2.zero) as UIAdvancedLabel;
			DisplayRegionNameAction.OnDisplay2DLabel += OnRegionNameDisplay;
	#endif
		}

		void Update() => Connection?.Update();

		void OnDestroy()
		{
	#if UNITY_EDITOR
			PlayerInputHandler.MouseMode = true;
	#endif
	#if !UNITY_EDITOR
			try { Configuration.GlobalSettings.Save(); } catch (Exception ex) { Log.Warning("Client", $"Settings save failed: {ex.Message}"); }
	#endif
	#if !UNITY_SERVER
			IPlayerCharacter.OnReadPayload -= OnCharacterReadPayload;
			IPlayerCharacter.OnStartLocalClient -= OnCharacterStartLocal;
			IPlayerCharacter.OnStopLocalClient -= OnCharacterStopLocal;
			IGuildController.OnReadID -= OnGuildReadId;
			Pet.OnReadID -= OnPetReadId;
			if (regionNameLabel != null) { Destroy(regionNameLabel.gameObject); regionNameLabel = null; }
			DisplayRegionNameAction.OnDisplay2DLabel -= OnRegionNameDisplay;
	#endif
			AudioListener = null;
			combatDisplay?.Shutdown();
			fogManager?.Shutdown();
			DeinitializeAuthenticator();
			Connection?.Shutdown();
			ClientNamingSystem.Destroy();
			UIManager.SetClient(null);
			ClientPostbootSystem?.UnsetClient(this);
			Application.logMessageReceived -= OnLogMessage;
		}

		// ── Init helpers ────────────────────────────────────────────────

		private bool TryInitializeNetworkManager()
		{
			if (NetworkManager == null) NetworkManager = FindFirstObjectByType<NetworkManager>();
			if (NetworkManager == null) { Log.Error("Client", "NetworkManager not found."); return false; }
			NetworkManager.ClientManager.RegisterBroadcast<WorldSceneConnectBroadcast>(OnWorldSceneConnect);
			NetworkManager.ClientManager.RegisterBroadcast<ClientValidatedSceneBroadcast>(OnValidatedScene);
			NetworkManager.ClientManager.RegisterBroadcast<ServerBusyBroadcast>(OnServerBusy);
			NetworkManager.ClientManager.RegisterBroadcast<DeathBroadcast>(OnDeathBroadcast);
			NetworkManager.SceneManager.OnLoadStart += OnSceneLoadStart;
			NetworkManager.SceneManager.OnLoadEnd += OnSceneLoadEnd;
			NetworkManager.SceneManager.OnUnloadEnd += OnSceneUnloadEnd;
			return true;
		}

		private bool TryInitializeAuthenticator()
		{
			if (LoginAuthenticator == null) LoginAuthenticator = FindFirstObjectByType<ClientLoginAuthenticator>();
			if (LoginAuthenticator == null) { Log.Error("Client", "LoginAuthenticator not found."); return false; }
			LoginAuthenticator.SetClient(this);
			LoginAuthenticator.OnClientAuthenticationResult += OnAuthResult;
			return true;
		}

		private void DeinitializeAuthenticator()
		{
			if (LoginAuthenticator == null) return;
			LoginAuthenticator.SetClient(null);
			LoginAuthenticator.OnClientAuthenticationResult -= OnAuthResult;
		}

		private bool TryInitializeTransport()
		{
			var tm = NetworkManager.TransportManager;
			if (tm == null) { Log.Error("Client", "TransportManager not found."); return false; }
			var mp = tm.GetTransport<Multipass>();
			if (mp == null) { Log.Error("Client", "Multipass not found."); return false; }
	#if UNITY_WEBGL && !UNITY_EDITOR
			mp.SetClientTransport<Bayou>();
	#else
			mp.SetClientTransport<Tugboat>();
	#endif
			return true;
		}

		// ── Public API ──────────────────────────────────────────────────

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Quit()
		{
	#if UNITY_EDITOR
			EditorApplication.ExitPlaymode();
	#elif UNITY_WEBGL
			GetComponent<WebGLKeyHijack>()?.ClientQuit();
	#else
			Application.Quit();
	#endif
		}

		public void QuitToLogin(bool forceDisconnect = true)
		{
			StopAllCoroutines();
			fogManager?.Stop();
			AddressableLoadProcessor.UnloadSceneByLabelAsync(WorldPreloadScenes);
			UnloadWorldScenes();
			if (forceDisconnect) Connection?.ForceDisconnect();
			LoginAuthenticator?.RevokeAndClearAuthToken();
			Connection?.ResetReconnectState();
			OnQuitToLogin?.Invoke();
	#if UNITY_EDITOR
			PlayerInputHandler.MouseMode = true;
	#endif
		}

		public void ConnectToServer(string address, ushort port, bool isWorldServer = false)
		{
	#if UNITY_WEBGL && !UNITY_EDITOR
			address = Constants.Configuration.GameHost + "/ws/" + port; port = 443;
	#endif
			Connection?.ConnectToServer(address, port, isWorldServer);
		}

		public bool IsConnectionReady(bool requireAuth = true) => Connection?.IsConnectionReady(requireAuth) ?? false;

		/// <summary>Backward-compatible overload accepting LocalConnectionState.</summary>
		public bool IsConnectionReady(LocalConnectionState state, bool requireAuth = false) =>
			(Connection?.ClientState == state) && IsConnectionReady(requireAuth);

		/// <summary>Forwarded to <see cref="ClientConnectionManager.ForceDisconnect"/>.</summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void ForceDisconnect() => Connection?.ForceDisconnect();

		/// <summary>Forwarded to <see cref="ClientConnectionManager.CancelReconnect"/>.</summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void ReconnectCancel() => Connection?.CancelReconnect();

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void Broadcast<T>(T broadcast, Channel channel = Channel.Reliable) where T : struct, IBroadcast
			=> NetworkManager.ClientManager.Broadcast(broadcast, channel);

		// ── Login server discovery ──────────────────────────────────────

		public bool TryGetRandomLoginServerAddress(out ServerAddress addr)
		{
			if (LoginServerAddresses != null && LoginServerAddresses.Count > 0) { addr = LoginServerAddresses.GetRandom(); return true; }
			addr = default; return false;
		}

		public IEnumerator GetLoginServerList(Action<string> onFail, Action<List<ServerAddress>> onDone)
		{
			if (LoginServerAddresses != null && LoginServerAddresses.Count > 0)
			{
				float age = Time.realtimeSinceStartup - loginServerAddressesFetchedAt;
				if (LoginServerCacheTtlSeconds <= 0 || age < LoginServerCacheTtlSeconds) { onDone?.Invoke(LoginServerAddresses); yield break; }
			}
			var candidates = ApiHostResolver.GetCandidates();
			if (candidates.Count == 0) { onFail?.Invoke("Failed to configure APIHost."); yield break; }
			const float stagger = 0.25f;
			var pending = new List<PendingProbe>(candidates.Count);
			float lastStart = float.NegativeInfinity;
			int next = 0; string lastErr = null; List<ServerAddress> winner = null;
			try
			{
				while (winner == null)
				{
					if (next < candidates.Count && (pending.Count == 0 || Time.realtimeSinceStartup - lastStart >= stagger))
					{
						var url = candidates[next++] + "loginserver";
						var req = UnityWebRequest.Get(url);
						req.certificateHandler = new ClientSSLCertificateHandler();
						req.redirectLimit = 0;
						req.SetRequestHeader(ClientApiSigner.HeaderKey, ClientApiSigner.BuildHeaderValue(UnityWebRequest.kHttpVerbGET, url));
						if (LoginServerRequestTimeoutSeconds > 0) req.timeout = LoginServerRequestTimeoutSeconds;
						pending.Add(new PendingProbe { Request = req, Op = req.SendWebRequest() });
						lastStart = Time.realtimeSinceStartup;
					}
					bool any = false;
					for (int i = 0; i < pending.Count; i++)
					{
						if (pending[i].Request == null) continue;
						if (!pending[i].Op.isDone) { any = true; continue; }
						var done = pending[i]; pending[i] = default;
						try
						{
							if (done.Request.result != UnityWebRequest.Result.Success) { lastErr = done.Request.error; continue; }
							var parsed = JsonUtility.FromJson<ServerAddresses>(done.Request.downloadHandler.text);
							if (parsed?.Addresses == null) { lastErr = "Parse failed."; continue; }
							winner = parsed.Addresses;
							break;
						}
						finally { done.Request.Dispose(); }
					}
					if (winner != null) break;
					if (!any && next >= candidates.Count) break;
					yield return null;
				}
				if (winner != null) { LoginServerAddresses = winner; loginServerAddressesFetchedAt = Time.realtimeSinceStartup; onDone?.Invoke(winner); }
				else onFail?.Invoke(lastErr ?? "Failed to reach any APIHost.");
			}
			finally
			{
				foreach (var p in pending) { try { p.Request?.Abort(); } catch { } try { p.Request?.Dispose(); } catch { } }
			}
		}

		private struct PendingProbe { public UnityWebRequest Request; public UnityWebRequestAsyncOperation Op; }

		// ── Scene management ────────────────────────────────────────────

		private void OnSceneLoadStart(SceneLoadStartEventArgs args) => UnloadWorldScenes();
		private void OnSceneLoadEnd(SceneLoadEndEventArgs args) { if (args.LoadedScenes != null) foreach (var s in args.LoadedScenes) loadedWorldScenes[s.handle] = s; }
		private void OnSceneUnloadEnd(SceneUnloadEndEventArgs args)
		{
			if (args.UnloadedScenesV2 != null)
			{
				foreach (var us in args.UnloadedScenesV2) loadedWorldScenes.Remove(us.Handle);
				Client.Broadcast(new ClientScenesUnloadedBroadcast { UnloadedScenes = args.UnloadedScenesV2 });
			}
		}
		private void UnloadWorldScenes()
		{
			var sp = NetworkManager.SceneManager.GetSceneProcessor();
			if (sp == null || loadedWorldScenes.Count < 1) return;
			foreach (var s in loadedWorldScenes.Values) sp.BeginUnloadAsync(s);
			loadedWorldScenes.Clear();
		}

		// ── Broadcast handlers ──────────────────────────────────────────

		private void OnWorldSceneConnect(WorldSceneConnectBroadcast msg, Channel ch) { if (IsConnectionReady()) ConnectToServer(msg.Address, msg.Port); }
		private void OnValidatedScene(ClientValidatedSceneBroadcast msg, Channel ch)
		{
			AddressableLoadProcessor.EnqueueLoad(WorldPreloadScenes);
			try { AddressableLoadProcessor.OnProgressUpdate += OnValidatedSceneProgress; AddressableLoadProcessor.BeginProcessQueue(); }
			catch (UnityException ex) { Log.Error("Client", "Preload failed", ex); }
		}
		private void OnValidatedSceneProgress(float p) { if (p >= 1f) { AddressableLoadProcessor.OnProgressUpdate -= OnValidatedSceneProgress; Client.Broadcast(new ClientValidatedSceneBroadcast(), Channel.Reliable); } }
		private void OnServerBusy(ServerBusyBroadcast msg, Channel ch) { if (UIManager.TryGet("UIDialogBox", out UIDialogBox d)) d.Open("Server is busy. Please try again."); }
		private void OnDeathBroadcast(DeathBroadcast msg, Channel ch) { if (UIManager.TryGetTK("UITKDeathDialog", out UITKDeathDialog d)) d.ShowDeathDialog(); }

		// ── Auth ────────────────────────────────────────────────────────

		private void OnAuthResult(ClientAuthenticationResult r)
		{
			switch (r)
			{
				case ClientAuthenticationResult.LoginSuccess: Connection.CurrentConnectionType = ServerConnectionType.Login; break;
				case ClientAuthenticationResult.WorldLoginSuccess: Connection.CurrentConnectionType = ServerConnectionType.World; break;
				case ClientAuthenticationResult.SceneLoginSuccess: Connection.CurrentConnectionType = ServerConnectionType.Scene; OnEnterGameWorld?.Invoke(); break;
			}
		}

		// ── Log guard ───────────────────────────────────────────────────

		private void OnLogMessage(string condition, string stackTrace, LogType type)
		{
			if (type != LogType.Exception) return;
			Log.Error("Client", stackTrace);
			if (Connection?.ClientState == LocalConnectionState.Stopped) return;
			if (string.IsNullOrEmpty(stackTrace) || !IsNetworkStack(stackTrace)) return;
			Connection?.ForceDisconnect();
		}
		private static bool IsNetworkStack(string st) => st.IndexOf("FishNet.Managing.", StringComparison.Ordinal) >= 0 || st.IndexOf("FishNet.Transporting.", StringComparison.Ordinal) >= 0 || st.IndexOf("FishNet.Serializing.", StringComparison.Ordinal) >= 0 || st.IndexOf("FishMMO.Shared.Network.", StringComparison.Ordinal) >= 0 || st.IndexOf("FishMMO.Client.Authentication", StringComparison.Ordinal) >= 0 || st.IndexOf("LoginAuthenticator", StringComparison.Ordinal) >= 0 || st.IndexOf("SrpAuthenticator", StringComparison.Ordinal) >= 0 || st.IndexOf("ClientAuthenticator", StringComparison.Ordinal) >= 0;

		// ── App lifecycle ───────────────────────────────────────────────

		void OnApplicationPause(bool paused) { if (paused) try { LoginAuthenticator?.RevokeAndClearAuthToken(); } catch { } }
		void OnApplicationQuit() { try { LoginAuthenticator?.RevokeAndClearAuthToken(); } catch { } }

		// ── Character / guild / pet / region handlers ──────────────────

	#if !UNITY_SERVER
		private void OnCharacterReadPayload(IPlayerCharacter c)
		{
			ClientNamingSystem.SetName(NamingSystemType.CharacterName, c.ID, name => { c.GameObject.name = name; c.CharacterName = name; c.CharacterNameLower = name.ToLowerInvariant(); if (c.CharacterNameLabel != null) c.CharacterNameLabel.text = name; });
		}
		private void OnCharacterStartLocal(IPlayerCharacter c)
		{
			UIManager.SetCharacter(c);
			var input = c.GameObject.GetComponent<PlayerInputController>() ?? c.GameObject.AddComponent<PlayerInputController>();
			input.Initialize(c);
			PlayerInputHandler.MouseMode = false;
		}
		private void OnCharacterStopLocal(IPlayerCharacter c)
		{
			PlayerInputHandler.MouseMode = true;
			c.GameObject.GetComponent<PlayerInputController>()?.Deinitialize();
			UIManager.UnsetCharacter();
			if (regionNameLabel != null && regionNameLabel.gameObject != null) regionNameLabel.gameObject.SetActive(false);
			fogManager?.Stop();
			if (c?.GameObject != null) Destroy(c.GameObject);
		}
		private static void OnGuildReadId(long id, IPlayerCharacter c)
		{
			if (id != 0) ClientNamingSystem.SetName(NamingSystemType.GuildName, id, name => c.SetGuildName(name));
			else c.SetGuildName(null);
		}
		private static void OnPetReadId(long ownerId, Pet pet)
		{
			if (pet != null && ownerId != 0) ClientNamingSystem.SetName(NamingSystemType.CharacterName, ownerId, name => { if (pet.CharacterGuildLabel) pet.CharacterGuildLabel.text = $"<{name}'s pet>"; });
		}
		private void OnRegionNameDisplay(string text, FontStyle style, Font font, int size, Color color, float life, bool fade, bool up, Vector2 offset)
		{
			if (regionNameLabel != null) { regionNameLabel.gameObject.SetActive(true); regionNameLabel.Initialize(text, style, font, size, color, life, fade, up, offset); }
		}
	#endif
	}
}
