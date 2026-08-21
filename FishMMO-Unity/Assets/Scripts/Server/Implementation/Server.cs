using System;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using FishNet.Managing;
using FishNet.Transporting;
using KinematicCharacterController;
using FishMMO.Database;
using FishMMO.Database.Npgsql;
using FishMMO.Logging;
using FishMMO.Server.Core;
using FishMMO.Shared;
using FishMMO.Auth.Core;
using FishMMO.Auth.Implementation;
using FishNet.Connection;

namespace FishMMO.Server.Implementation
{
	/// <summary>
	/// Composition root: orchestrates Core and Implementation into a running server.
	/// </summary>
	public class Server : MonoBehaviour,
		IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour>,
		IServer<INetworkManagerWrapper, NetworkConnection, IRuntimeDataContainer>,
		IPeriodicUpdateSystem
	{
		/// <summary>
		/// Optional override for the server's bind address.
		/// </summary>
		[Header("Overrides")]
		[field: SerializeField]
		public string AddressOverride { get; set; }

		/// <summary>
		/// Optional override for the server's bind port.
		/// </summary>
		[field: SerializeField]
		public ushort PortOverride { get; set; }

		/// <summary>
		/// Gets the core server logic instance.
		/// </summary>
		public ICoreServer CoreServer { get; private set; }

		/// <summary>
		/// Gets the database orchestrator providing centralized access to
		/// the service registry, health monitoring, metrics, and the context factory.
		/// </summary>
		public IDatabase Database { get; private set; }

		/// <summary>
		/// Gets the network manager wrapper instance.
		/// </summary>
		public INetworkManagerWrapper NetworkWrapper { get; private set; }

		/// <summary>
		/// Gets the server address provider instance.
		/// </summary>
		public IServerAddressProvider AddressProvider { get; private set; }

		/// <summary>
		/// Gets the server configuration instance.
		/// </summary>
		public IServerConfiguration Configuration { get; private set; }

		/// <summary>
		/// Gets the account manager instance.
		/// </summary>
		public IAccountManager<NetworkConnection> AccountManager { get; private set; }

		/// <summary>
		/// Gets the server events instance.
		/// </summary>
		public IServerEvents ServerEvents { get; private set; }

		/// <summary>
		/// Current connection state of the server.
		/// </summary>
		private volatile ConnectionState serverState;

		/// <summary>
		/// Gets the current connection state of the server.
		/// </summary>
		public ConnectionState ServerState => serverState;

		/// <summary>
		/// List of all server behaviours attached to the server.
		/// </summary>
		[SerializeField]
		private List<ServerBehaviour> serverBehaviors = new List<ServerBehaviour>();

		/// <summary>
		/// List of all runtime data containers managed by the server.
		/// </summary>
		private List<RuntimeDataContainer> dataContainers = new List<RuntimeDataContainer>();

		/// <summary>
		/// Registry that manages all server behaviours.
		/// </summary>
		public IServerBehaviourRegistry<INetworkManagerWrapper, NetworkConnection, IServerBehaviour> BehaviourRegistry { get; private set; }

		/// <summary>
		/// Registry that manages all runtime data containers.
		/// </summary>
		public IServerComponentRegistry<INetworkManagerWrapper, NetworkConnection, IRuntimeDataContainer> DataContainerRegistry { get; private set; }

		/// <summary>
		/// Dictionary of registered periodic callbacks indexed by their callback delegate.
		/// </summary>
		private Dictionary<Action<float>, PeriodicCallbackData> periodicCallbacks = new Dictionary<Action<float>, PeriodicCallbackData>();

		/// <summary>
		/// Reusable scratch list for periodic callbacks ready to invoke, avoiding per-frame allocation.
		/// </summary>
		private readonly List<PeriodicCallbackData> readyCallbacks = new List<PeriodicCallbackData>();

		/// <summary>
		/// Reusable snapshot of server behaviours, taken before dispatch to avoid
		/// list mutation if a behaviour registers or unregisters during OnLateUpdate.
		/// </summary>
		private readonly List<ServerBehaviour> behaviourSnapshot = new List<ServerBehaviour>();

		/// <summary>
		/// Cached server-initialized log callback for login server initialization.
		/// </summary>
		private static readonly Action loginServerInitializedLogHandler = () => Log.Debug("Server", "LoginServer initialized.");

		/// <summary>
		/// Cached server-initialized log callback for world server initialization.
		/// </summary>
		private static readonly Action worldServerInitializedLogHandler = () => Log.Debug("Server", "WorldServer initialized.");

		/// <summary>
		/// Cached server-initialized log callback for scene server initialization.
		/// </summary>
		private static readonly Action sceneServerInitializedLogHandler = () => Log.Debug("Server", "SceneServer initialized.");

		/// <summary>
		/// Flag indicating whether the server has already performed shutdown.
		/// </summary>
		private int hasShutdownFlag = 0;

		/// <summary>
		/// Unity Start method. Initializes and composes all server components.
		/// </summary>
		private void Start()
		{
			hasShutdownFlag = 0;

			// Static state survives Editor play sessions when domain reload is disabled. A stale,
			// already-expired budget would clamp every shutdown flush to zero on the next run.
			UnitySyncOverAsync.ClearShutdownBudget();

			Log.Debug("Server", "Server is starting...");

			NetworkManager networkManager = FindFirstObjectByType<NetworkManager>();
			if (networkManager == null)
				throw new UnityException("Server: NetworkManager could not be found! Make sure you have a NetworkManager in your scene.");

			Configuration = new FileServerConfiguration();
			ServerEvents = new ServerEvents();

			CoreServer = new CoreServer(Configuration, ServerEvents);
			NetworkWrapper = new FishNetNetworkWrapper(networkManager, Configuration, this);

			// Subscribe server-initialized log handlers.
			// Start() is called exactly once by Unity; duplicate subscription
			// is not a concern here. Domain reload guards are handled by OnDestroy.
			ServerEvents.OnLoginServerInitialized += loginServerInitializedLogHandler;
			ServerEvents.OnWorldServerInitialized += worldServerInitializedLogHandler;
			ServerEvents.OnSceneServerInitialized += sceneServerInitializedLogHandler;

			StartCoroutine(NetHelper.FetchExternalIPAddress(OnFinalizeSetup));

			// Guard against indefinite hang if the external IP service is unreachable.
			// Falls back to loopback after externalIpFetchTimeoutSeconds so the server
			// can start accepting local connections.
			externalIpTimeoutCoroutine = StartCoroutine(ExternalIpFetchTimeout(OnFinalizeSetup));
		}

		/// <summary>
		/// Maximum seconds to wait for the external IP address fetch before falling
		/// back to loopback. Prevents the server from hanging indefinitely at startup
		/// when the IP-check service is unreachable.
		/// </summary>
		private const float externalIpFetchTimeoutSeconds = 30f;

		/// <summary>
		/// Coroutine that fires <paramref name="onDone"/> with the loopback fallback
		/// address if the external IP fetch does not complete within
		/// <see cref="externalIpFetchTimeoutSeconds"/>.
		/// </summary>
		private System.Collections.IEnumerator ExternalIpFetchTimeout(System.Action<string> onDone)
		{
			yield return new WaitForSeconds(externalIpFetchTimeoutSeconds);
			if (hasShutdownFlag == 0)
			{
				Log.Warning("Server", $"External IP fetch timed out after {externalIpFetchTimeoutSeconds}s; falling back to loopback.");
				usedFallbackAddress = true;
				onDone("127.0.0.1");
			}
		}

		private int setupFinalized = 0;
		private Coroutine externalIpTimeoutCoroutine;

		/// <summary>
		/// Set to true when the external IP timeout fires before the real fetch completes.
		/// Used in the CAS guard of <see cref="OnFinalizeSetup"/> to warn operators that a
		/// real external IP arrived after the loopback fallback was already applied.
		/// </summary>
		private bool usedFallbackAddress = false;

		/// <summary>
		/// Finalizes server setup after fetching the external IP address.
		/// </summary>
		/// <param name="remoteAddress">The external remote address of the server.</param>
		private void OnFinalizeSetup(string remoteAddress)
		{
			// Guard against double-initialization — the timeout coroutine may fire
			// after the real fetch has already completed, or vice versa.
			if (System.Threading.Interlocked.CompareExchange(ref setupFinalized, 1, 0) != 0)
			{
				// The CAS guard rejected this call -- another invocation already
				// initialized the server. If that call used the loopback fallback,
				// the real external IP is now arriving too late for registration.
				if (usedFallbackAddress)
				{
					Log.Warning("Server",
						$"External IP was received AFTER the loopback fallback was already applied. " +
						$"The real external IP ({remoteAddress}) could not be used. " +
						$"Operator: update the server config with Address={remoteAddress}");
				}
				return;
			}

			// Cancel the timeout coroutine — the IP fetch succeeded, so the fallback
			// is not needed. Prevents the coroutine from running for the full 30s
			// after the real fetch has already completed.
			if (externalIpTimeoutCoroutine != null)
			{
				StopCoroutine(externalIpTimeoutCoroutine);
				externalIpTimeoutCoroutine = null;
			}

			if (string.IsNullOrWhiteSpace(remoteAddress))
				throw new UnityException("Server: Failed to retrieve Remote IP Address.");

			// When the external IP fetch returns a loopback address (127.0.0.1, ::1,
			// 127.0.1.1, localhost, etc.) — timeout fallback or local-only deployment —
			// prefer the explicitly configured address from the server config.
			// This prevents a production server from registering loopback as its public IP.
			// Uses IPAddress.TryParse + IsLoopback to catch all loopback variants,
			// not just the two magic strings "127.0.0.1" and "localhost".
			if (NetHelper.IsLoopbackAddress(remoteAddress))
			{
				string configuredAddress = Configuration.GetString("Address", null);
				if (!string.IsNullOrWhiteSpace(configuredAddress) && !NetHelper.IsLoopbackAddress(configuredAddress))
				{
					remoteAddress = configuredAddress;
					Log.Debug("Server", $"Using configured Address \'{remoteAddress}\' instead of loopback fallback.");
				}
#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
				else
				{
					Log.Warning("Server", "External IP is 127.0.0.1 in production. Set \'Address\' in the server config " +
						"or AddressOverride on the Server component to register a public-facing address.");
				}
#endif
			}

			CoreServer.Initialize(remoteAddress, gameObject.scene.name);

			// Build the configuration using our shared helper
			// This automatically handles appsettings.json, appsettings.{env}.json, 
			// and Environment Variables using the BaseDirectory.
			// Despite the "DesignTime" name, this method is safe for runtime use.
			// It builds an IConfiguration from appsettings.json + environment variables
			// using the same provider pipeline as the ASP.NET servers.
			// The name reflects its origin as a shared helper for both design-time
			// tooling (EF Core migrations) and runtime server startup.
			IConfiguration dbConfig = DatabaseConfigurationHelper.BuildDesignTimeConfiguration();

			// Initialize Database with the configuration object
			// The Database class will now pull Npgsql settings directly from dbConfig
			Log.Debug("Server", $"Initializing Database with Environment: {DatabaseConfigurationHelper.ResolveEnvironmentName()}");
			Database = new Database.Database(dbConfig);

			VerifyDatabaseSchema();

			AddressProvider = new ServerAddressProvider(
				NetworkWrapper.NetworkManager.TransportManager.Transport,
				AddressOverride,
				PortOverride,
				CoreServer.Address,
				CoreServer.RemoteAddress);

			// Create the appropriate AccountManager subtype based on the authenticator.
			// This must happen before AttachLoginAuthenticator because AttachLoginAuthenticator
			// synchronously calls InitializeWorkers() → InitializeWorkersCore(), which validates
			// Server.AccountManager. If AccountManager is null at that point, an
			// InvalidOperationException is thrown before workers can start.
			// Each authenticator subtype provides its own AccountManager via
			// IServerAuthenticator.CreateAccountManager(), removing the fragile
			// type-check that previously lived here.
			var authenticator = NetworkWrapper.NetworkManager.ServerManager.GetAuthenticator();
			if (authenticator is IServerAuthenticator serverAuth)
				AccountManager = serverAuth.CreateAccountManager();
			else
				throw new InvalidOperationException(
					"Server: Authenticator does not implement IServerAuthenticator.");

			// SrpAuthenticatorCore.InitializeWorkersCore hard-throws if TotpMasterKey is null /
			// wrong length. LoginServerSystem historically assigned TotpMasterKey only during
			// BehaviourRegistry.InitializeAll — which runs AFTER AttachLoginAuthenticator
			// starts workers. Pre-bootstrap a process-local signing key + derived TotpMasterKey
			// here so Login can open the transport. LoginServerSystem reuses TokenSigningKey
			// when already set (no second generate) and re-derives Totp after DB registration.
			if (authenticator is ServerAuthenticator srpAuth)
			{
				byte[] signingKey = CryptoHelper.GenerateKey(CryptoHelper.HmacKeyLength);
				srpAuth.TokenSigningKey = signingKey;
				using (var localKms = new LocalDeriveKmsProvider(signingKey))
				{
					srpAuth.TotpMasterKey = localKms.DeriveKey("fishmmo-totp-master-key-v1");
				}
				Log.Debug("Server", "Pre-bootstrapped TokenSigningKey + TotpMasterKey before InitializeWorkers.");
			}

			NetworkWrapper.ApplyTransportConfiguration(AddressOverride, PortOverride > 0 ? PortOverride : null);
			NetworkWrapper.AttachLoginAuthenticator(this);
			NetworkWrapper.RegisterServerConnectionStateEventHandler(ServerManager_OnServerConnectionState);

			// Initialize all registered runtime data containers
			DataContainerRegistry = new RuntimeDataContainerRegistry();
			DiscoverAndCreateDataContainers();
			RegisterAllDataContainers();
			DataContainerRegistry.InitializeAll(this);

			// Initialize all registered server behaviours.
			// Behaviour initialization performs database I/O, so it runs asynchronously and the
			// transport is started from its completion callback — the same start/callback chain
			// used above for the external IP fetch. The main thread is never blocked on I/O:
			// blocking it would stall the very continuations the work depends on.
			BehaviourRegistry = new ServerBehaviourRegistry();
			RegisterAllBehaviours();

			behaviourInitCoroutine = StartCoroutine(InitializeBehavioursThenStartServer());
		}

		/// <summary>
		/// Checks the database schema against the entity model and reports any mismatch.
		/// </summary>
		/// <remarks>
		/// Migrations are generated per developer and applied locally rather than shared through
		/// source control, so pulling someone else's entity change does not bring a migration with
		/// it and nothing applies one on your behalf. The database stays where it was, and the
		/// mismatch only surfaces later as a query failing on a column that does not exist —
		/// which typically reaches a player as missing data rather than as a schema problem.
		/// <para>
		/// Deliberately neither awaited nor fatal. This is a diagnostic: a server whose schema is
		/// stale still serves everything that does not touch the changed tables, and refusing to
		/// start would be a worse outcome than a loud log line. It costs one query and reports
		/// within a second or so of startup.
		/// </para>
		/// </remarks>
		private void VerifyDatabaseSchema()
		{
			INpgsqlDbContextFactory factory = Database?.DbContextFactory;
			if (factory == null)
			{
				return;
			}

			_ = Task.Run(async () =>
			{
				try
				{
					SchemaValidationResult result = await factory.ValidateSchemaAsync();
					string problem = result.DescribeProblem();
					if (problem == null)
					{
						_ = Log.Debug("Server", "Database schema matches the entity model.");
					}
					else if (result.UnavailableReason != null)
					{
						_ = Log.Warning("Server", problem);
					}
					else
					{
						_ = Log.Error("Server", problem);
					}
				}
				catch (Exception ex)
				{
					// A diagnostic must never be the thing that takes a server down.
					_ = Log.Warning("Server", $"Database schema check threw and was skipped: {ex.Message}");
				}
			});
		}

		/// <summary>
		/// Number of times behaviour initialization is attempted before the process gives up.
		/// Retries cover a database that is still coming up when the game servers start.
		/// </summary>
		[Header("Startup")]
		[Tooltip("Attempts at behaviour initialization (database registration) before exiting")]
		[SerializeField][Min(1)] private int startupInitializationAttempts = 5;

		/// <summary>
		/// Delay before the second initialization attempt, in seconds. Each further attempt
		/// doubles the delay, capped at <see cref="startupInitializationMaxRetryDelaySeconds"/>.
		/// </summary>
		[Tooltip("Delay before the first retry; doubles each attempt up to the cap")]
		[SerializeField][Min(0.1f)] private float startupInitializationRetryDelaySeconds = 2f;

		/// <summary>Upper bound on the exponential retry delay, in seconds.</summary>
		[Tooltip("Maximum delay between initialization attempts")]
		[SerializeField][Min(0.1f)] private float startupInitializationMaxRetryDelaySeconds = 30f;

		/// <summary>Coroutine driving asynchronous behaviour initialization.</summary>
		private Coroutine behaviourInitCoroutine;

		/// <summary>
		/// Cancels in-flight startup work when the server shuts down before initialization
		/// finishes (early play-mode exit, SIGTERM during boot, failed configuration).
		/// </summary>
		private System.Threading.CancellationTokenSource startupCancellation;

		/// <summary>
		/// Drives behaviour initialization without blocking the main thread, retrying with
		/// exponential backoff, then either starts the transport or exits the process.
		/// </summary>
		/// <remarks>
		/// Polling the task with <c>yield return null</c> keeps the player loop running, which is
		/// what lets Unity's SynchronizationContext drain the continuations the behaviours are
		/// waiting on. This is the structural fix for the startup hang: the main thread stays
		/// free instead of parking inside a database call.
		/// </remarks>
		private System.Collections.IEnumerator InitializeBehavioursThenStartServer()
		{
			startupCancellation = new System.Threading.CancellationTokenSource();
			float retryDelay = startupInitializationRetryDelaySeconds;

			for (int attempt = 1; attempt <= startupInitializationAttempts; attempt++)
			{
				if (hasShutdownFlag != 0)
				{
					yield break;
				}

				var initTask = BehaviourRegistry.InitializeAllAsync(this, startupCancellation.Token);

				while (!initTask.IsCompleted)
				{
					if (hasShutdownFlag != 0)
					{
						yield break;
					}
					yield return null;
				}

				if (initTask.IsCanceled)
				{
					Log.Debug("Server", "Behaviour initialization cancelled during shutdown.");
					yield break;
				}

				if (initTask.IsFaulted)
				{
					Exception failure = initTask.Exception?.GetBaseException();
					Log.Error("Server", $"Behaviour initialization threw on attempt {attempt}/{startupInitializationAttempts}: {failure}");
				}
				else if (initTask.Result.Count == 0)
				{
					KinematicCharacterSystem.EnsureCreation();
					KinematicCharacterSystem.Settings.AutoSimulation = false;

					NetworkWrapper.StartServer();

					Log.Debug("Server", "Initialization Complete");
					behaviourInitCoroutine = null;
					yield break;
				}
				else
				{
					string failed = string.Join(", ", initTask.Result.Select(f => $"{f.Name} ({f.Status})"));
					Log.Error("Server", $"Behaviour initialization failed on attempt {attempt}/{startupInitializationAttempts}: {failed}");
				}

				if (attempt < startupInitializationAttempts)
				{
					Log.Warning("Server", $"Retrying behaviour initialization in {retryDelay:0.#}s...");
					yield return new WaitForSeconds(retryDelay);
					retryDelay = Mathf.Min(retryDelay * 2f, startupInitializationMaxRetryDelaySeconds);
				}
			}

			// Every attempt failed. A live process that never binds its port is worse than a dead
			// one: an orchestrator can restart a dead process, but a silent zombie just looks up.
			Log.Error("Server",
				$"Behaviour initialization failed after {startupInitializationAttempts} attempt(s). " +
				"The server cannot start; exiting so the supervisor can restart it.");

			behaviourInitCoroutine = null;
			QuitWithFailure();
		}

		/// <summary>
		/// Terminates the process with a non-zero exit code after unrecoverable startup failure.
		/// </summary>
		private void QuitWithFailure()
		{
#if UNITY_EDITOR
			EditorApplication.isPlaying = false;
#else
			Application.Quit(1);
#endif
		}

		/// <summary>
		/// Handles server connection state changes and logs address information.
		/// </summary>
		/// <param name="args">The server connection state arguments.</param>
		private void ServerManager_OnServerConnectionState(ServerConnectionStateArgs args)
		{
			serverState = MapConnectionState(args.ConnectionState);

			if (AddressProvider.TryGetServerIPAddress(out ServerAddress address))
			{
				Log.Debug("Server",
					$"Local: {address.Address}:{address.Port} Remote: {CoreServer.RemoteAddress}:{address.Port} - {args.ConnectionState}");
			}
		}

		/// <summary>
		/// Maps FishNet's LocalConnectionState to the server's ConnectionState enum.
		/// </summary>
		/// <param name="fishNetState">FishNet local connection state.</param>
		/// <returns>Mapped internal server connection state.</returns>
		private ConnectionState MapConnectionState(LocalConnectionState fishNetState)
		{
			return fishNetState switch
			{
				LocalConnectionState.Started => ConnectionState.Started,
				LocalConnectionState.Starting => ConnectionState.Starting,
				LocalConnectionState.Stopping => ConnectionState.Stopping,
				LocalConnectionState.Stopped => ConnectionState.Stopped,
				_ => ConnectionState.Stopped
			};
		}

		/// <summary>
		/// Unity LateUpdate method. Orchestrates server behaviour updates and periodic callback dispatch.
		/// </summary>
		private void LateUpdate()
		{
			float deltaTime = Time.deltaTime;
			UpdateServerBehaviours(deltaTime);
			UpdatePeriodicCallbacks(deltaTime);
		}

		/// <summary>
		/// Updates all registered ServerBehaviours.
		/// </summary>
		/// <param name="deltaTime">Time elapsed since last frame.</param>
		private void UpdateServerBehaviours(float deltaTime)
		{
			if (this.serverBehaviors == null)
				return;

			// Belt-and-suspenders: snapshot the list so that if a behaviour's
			// OnLateUpdate mutates serverBehaviors (register/unregister), the
			// iteration continues safely against the frozen copy. In practice
			// behaviours should not register/unregister during OnLateUpdate, but
			// the defensive copy prevents a hard-to-debug collection-mutation crash.
			this.behaviourSnapshot.Clear();
			for (int i = 0; i < this.serverBehaviors.Count; i++)
			{
				this.behaviourSnapshot.Add(this.serverBehaviors[i]);
			}

			for (int i = 0; i < behaviourSnapshot.Count; i++)
			{
				var behaviour = behaviourSnapshot[i];
				if (behaviour != null && behaviour.Initialized)
				{
					behaviour.OnLateUpdate(deltaTime);
				}
			}
		}

		/// <summary>
		/// Processes and dispatches periodic callbacks that are ready to execute.
		/// The foreach over the dictionary is safe because the invoke loop only
		/// mutates TimeRemaining on existing entries, not the dictionary itself.
		/// If a callback calls Register/Unregister, the dictionary is not being
		/// enumerated at that point — enumeration finishes before invocation starts.
		/// </summary>
		/// <param name="deltaTime">Time elapsed since last frame.</param>
		private void UpdatePeriodicCallbacks(float deltaTime)
		{
			if (this.periodicCallbacks.Count == 0)
				return;

			this.readyCallbacks.Clear();

			foreach (var kvp in this.periodicCallbacks)
			{
				var data = kvp.Value;
				data.TimeRemaining -= deltaTime;

				if (data.TimeRemaining <= 0f)
				{
					readyCallbacks.Add(data);
				}
			}

				for (int i = 0; i < this.readyCallbacks.Count; i++)
				{
					var data = this.readyCallbacks[i];
				try
				{
					data.Callback?.Invoke(data.Interval);
				}
				catch (Exception ex)
				{
					Log.Error("Server", $"Error invoking periodic callback {data.CallbackName}: {ex.Message}");
				}
				data.TimeRemaining = data.Interval;
			}
		}

		/// <summary>
		/// Unity OnDestroy callback. Deinitializes all server components and cleans up resources.
		/// </summary>
		private void OnDestroy()
		{
			PerformShutdown();
		}

		/// <summary>
		/// Unity OnApplicationQuit callback. Deinitializes all server components and cleans up resources.
		/// </summary>
		private void OnApplicationQuit()
		{
			PerformShutdown();
		}

		/// <summary>
		/// Performs one-time shutdown of server subsystems, workers, network, and persistence resources.
		/// </summary>
		private void PerformShutdown()
		{
			if (System.Threading.Interlocked.CompareExchange(ref this.hasShutdownFlag, 1, 0) != 0) return;

			this.periodicCallbacks.Clear();

			// 0. Cancel the external-IP-fetch timeout coroutine.  If the server
			//    is destroyed before the external IP fetch completes (e.g. early
			//    play-mode exit, invalid config), the coroutine would otherwise
			//    fire after 30s and call OnFinalizeSetup on a destroyed GameObject,
			//    producing MissingReferenceException or silent zombie-object access.
			if (externalIpTimeoutCoroutine != null)
			{
				StopCoroutine(externalIpTimeoutCoroutine);
				externalIpTimeoutCoroutine = null;
			}

			// 0b. Cancel in-flight behaviour initialization. The server can be destroyed while
			//     startup database work is still running (early play-mode exit, SIGTERM during
			//     boot); cancelling stops the I/O instead of leaving it running against a
			//     database that is about to be shut down below.
			if (behaviourInitCoroutine != null)
			{
				StopCoroutine(behaviourInitCoroutine);
				behaviourInitCoroutine = null;
			}
			if (startupCancellation != null)
			{
				try
				{
					startupCancellation.Cancel();
				}
				catch (ObjectDisposedException) { }
				startupCancellation.Dispose();
				startupCancellation = null;
			}

			// 1. Signal worker cancellation FIRST — in-flight auth operations can
			//    finish broadcasting before the transport is stopped.
			IServerAuthenticator authenticator =
				NetworkWrapper?.NetworkManager?.ServerManager?.GetAuthenticator() as IServerAuthenticator;
			if (authenticator != null)
			{
				authenticator.ShutdownWorkers();
			}

			// 2. Synchronous worker drain (no coroutine — OnDestroy can't yield).
#if UNITY_WEBGL && !UNITY_EDITOR
			// WebGL has no worker threads, so there is nothing to drain and nothing that could
			// make the exit condition true. Spinning here would only freeze the browser's single
			// thread for the full timeout, so skip the drain entirely.
#else
			if (authenticator != null)
			{
				// Thread.Sleep is the right primitive here despite blocking: PerformShutdown runs
				// during OnDestroy, which cannot yield, and sleeping releases the CPU so the very
				// worker threads being waited on can finish. A spin-wait would compete with them.
				// Bounded by workerDrainTimeoutSeconds, and only on the teardown path.
				//
				// Time.realtimeSinceStartup is read from the OS clock rather than the frame timer,
				// so it keeps advancing while this thread sleeps and the loop always terminates.
				float deadline = Time.realtimeSinceStartup + workerDrainTimeoutSeconds;
				while (!authenticator.AreWorkersDrained() && Time.realtimeSinceStartup < deadline)
				{
					System.Threading.Thread.Sleep(10);
				}
			}
#endif

			// Cap the total time the remaining teardown steps may block the main thread. The
			// per-call timeouts below are individually sane but serialize into far more than a
			// supervisor's grace period on a wedged database.
			UnitySyncOverAsync.BeginShutdownBudget(shutdownBlockingBudgetMilliseconds);

			// 3. Stop accepting new connections.
			NetworkWrapper?.StopServer();

			// 4. Deinitialize behaviours and data containers.
			DeinitializeAllBehaviours();
			UnregisterAllBehaviours();

			DeinitializeAllDataContainers();
			UnregisterAllDataContainers();
			Database?.Shutdown();
			Database = null;
			CoreServer?.Deinitialize();
			AccountManager?.Clear();

			if (ServerEvents != null)
			{
				ServerEvents.OnLoginServerInitialized -= loginServerInitializedLogHandler;
				ServerEvents.OnWorldServerInitialized -= worldServerInitializedLogHandler;
				ServerEvents.OnSceneServerInitialized -= sceneServerInitializedLogHandler;
			}

			NetworkWrapper?.UnregisterServerConnectionStateEventHandler(ServerManager_OnServerConnectionState);
		}

		/// <summary>Max seconds to poll for worker teardown before forcing stop.
		/// Workers might not fully drain if pending items exceed this window;
		/// remaining work items are abandoned on process exit.</summary>
		private const float workerDrainTimeoutSeconds = 1f;

		/// <summary>
		/// Total time the teardown steps after the worker drain may block the main thread,
		/// across every behaviour's <c>OnDeinitialize</c> combined.
		/// </summary>
		/// <remarks>
		/// Sized to fit inside a supervisor's stop window (Kubernetes defaults to a 30s grace
		/// period, Docker Compose to 10s) with the 1s worker drain and process teardown alongside
		/// it. Without this cap the per-call timeouts serialize — a scene server can spend 5s on
		/// database cleanup, 10s on the chat flush and 30s on character saves — and the supervisor
		/// SIGKILLs mid-flush, losing the work the flush was protecting.
		/// </remarks>
		private const int shutdownBlockingBudgetMilliseconds = 8_000;

		/// <summary>
		/// Registers all server behaviours in order.
		/// </summary>
		private void RegisterAllBehaviours()
		{
			if (this.serverBehaviors != null && this.BehaviourRegistry != null)
			{
				// Register in order
				for (int i = 0; i < this.serverBehaviors.Count; i++)
				{
					var behaviour = this.serverBehaviors[i];
					if (behaviour != null && !behaviour.Initialized)
					{
						// Register to registry before initializing
						this.BehaviourRegistry.Register(behaviour);
					}
				}
			}
		}

		/// <summary>
		/// Discovers and creates all RuntimeDataContainers required by ServerBehaviours.
		/// Automatically instantiates containers based on RequiresDataContainer attributes.
		/// Multiple systems can declare the same container type, and only one instance will be created.
		/// </summary>
		private void DiscoverAndCreateDataContainers()
		{
			// Clear to prevent accumulation if Unity domain reload is disabled.
			this.dataContainers.Clear();

			var factory = new RuntimeDataContainerFactory();
			var containerTypes = new HashSet<Type>(); // Prevent duplicates
			var containersByPriority = new SortedDictionary<int, List<Type>>();

			// Scan all ServerBehaviours for RequiresDataContainer attributes
			foreach (var behaviour in serverBehaviors)
			{
				if (behaviour == null)
					continue;

				var attributes = behaviour.GetType()
					.GetCustomAttributes(typeof(RequiresDataContainerAttribute), false)
					.Cast<RequiresDataContainerAttribute>();

				foreach (var attr in attributes)
				{
					if (!containerTypes.Add(attr.ContainerType))
						continue; // Already registered - skip duplicate

					if (!factory.IsValidContainerType(attr.ContainerType))
					{
						Log.Warning("Server",
							$"Invalid container type {attr.ContainerType.Name} required by {behaviour.GetType().Name}. " +
							"Container must be a non-abstract class with a parameterless constructor.");
						continue;
					}

					// Group by priority
					if (!containersByPriority.ContainsKey(attr.InitializationPriority))
						containersByPriority[attr.InitializationPriority] = new List<Type>();

					containersByPriority[attr.InitializationPriority].Add(attr.ContainerType);
				}
			}

			// Create containers in priority order
			foreach (var priorityGroup in containersByPriority.Values)
			{
				foreach (var containerType in priorityGroup)
				{
					var container = factory.CreateContainer(containerType);
					if (container is RuntimeDataContainer rdc)
					{
						this.dataContainers.Add(rdc);
						Log.Debug("Server", $"Auto-created RuntimeDataContainer: {containerType.Name}");
					}
					else
					{
						Log.Warning("Server", $"Factory returned non-RuntimeDataContainer for type {containerType.Name}. Skipping.");
					}
				}
			}
		}

		/// <summary>
		/// Registers all runtime data containers in order.
		/// </summary>
		private void RegisterAllDataContainers()
		{
			if (this.dataContainers != null && this.DataContainerRegistry != null)
			{
				// Register in order
				for (int i = 0; i < this.dataContainers.Count; i++)
				{
					var container = this.dataContainers[i];
					if (container != null && !container.Initialized)
					{
						// Register to registry before initializing
						this.DataContainerRegistry.Register(container);
					}
				}
			}
		}

		/// <summary>
		/// Deinitializes all registered server behaviours in reverse order.
		/// </summary>
		private void DeinitializeAllBehaviours()
		{
			if (this.serverBehaviors != null && this.BehaviourRegistry != null)
			{
				// Deinitialize in reverse order to ensure proper cleanup dependencies
				for (int i = this.serverBehaviors.Count - 1; i >= 0; i--)
				{
					var behaviour = this.serverBehaviors[i];
					if (behaviour != null && behaviour.Initialized)
					{
						// Deinitialize the behaviour (calls OnDeinitialize and clears references)
						behaviour.Deinitialize();
					}
				}
			}
		}

		/// <summary>
		/// Unregisters all registered server behaviours in reverse order.
		/// </summary>
		private void UnregisterAllBehaviours()
		{
			if (this.serverBehaviors != null && this.BehaviourRegistry != null)
			{
				// Unregister in reverse order to ensure proper cleanup dependencies
				for (int i = this.serverBehaviors.Count - 1; i >= 0; i--)
				{
					var behaviour = this.serverBehaviors[i];
					if (behaviour != null)
					{
						// Unregister from registry before deinitializing
						this.BehaviourRegistry.Unregister(behaviour);
					}
				}
			}
		}

		/// <summary>
		/// Deinitializes all registered runtime data containers in reverse order.
		/// </summary>
		private void DeinitializeAllDataContainers()
		{
			if (this.DataContainerRegistry != null)
			{
				this.DataContainerRegistry.DeinitializeAll();
			}
		}

		/// <summary>
		/// Unregisters all registered runtime data containers in reverse order.
		/// </summary>
		private void UnregisterAllDataContainers()
		{
			if (this.dataContainers != null && this.DataContainerRegistry != null)
			{
				// Unregister in reverse order to ensure proper cleanup dependencies
				for (int i = this.dataContainers.Count - 1; i >= 0; i--)
				{
					var container = this.dataContainers[i];
					if (container != null)
					{
						// Unregister from registry
						this.DataContainerRegistry.Unregister(container);
					}
				}
			}
		}

		/// <summary>
		/// Quits the application or exits play mode in the Unity Editor.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void Quit()
		{
#if UNITY_EDITOR
			EditorApplication.ExitPlaymode();
#else
			Application.Quit();
#endif
		}

		#region IPeriodicUpdateSystem Implementation

		/// <summary>
		/// Registers a callback to be invoked periodically at the specified interval.
		/// </summary>
		/// <param name="interval">Time in seconds between callback invocations.</param>
		/// <param name="callback">The callback to invoke. Receives delta time since last invocation.</param>
		public void RegisterPeriodicCallback(float interval, Action<float> callback)
		{
			if (callback == null)
			{
				Log.Error("Server", "Cannot register null periodic callback.");
				return;
			}

			if (interval <= 0)
			{
				Log.Error("Server", $"Cannot register periodic callback with non-positive interval: {interval}");
				return;
			}

			if (periodicCallbacks.TryGetValue(callback, out var existing))
			{
				Log.Warning("Server", $"Periodic callback {existing.CallbackName} is already registered. Updating interval.");
				existing.Interval = interval;
				existing.TimeRemaining = interval;
				return;
			}

			var entry = new PeriodicCallbackData(interval, callback);
			periodicCallbacks[callback] = entry;
			Log.Debug("Server", $"Registered periodic callback: {entry.CallbackName} (interval: {interval}s)");
		}

		/// <summary>
		/// Unregisters a previously registered periodic callback.
		/// </summary>
		/// <param name="callback">The callback to unregister.</param>
		public void UnregisterPeriodicCallback(Action<float> callback)
		{
			if (callback == null)
			{
				Log.Error("Server", "Cannot unregister null periodic callback.");
				return;
			}

			// Resolve name before removal so the log message is meaningful.
			string name = periodicCallbacks.TryGetValue(callback, out var data)
				? data.CallbackName
				: $"{callback.Method.DeclaringType?.Name}.{callback.Method.Name}";

			if (periodicCallbacks.Remove(callback))
			{
				Log.Debug("Server", $"Unregistered periodic callback: {name}");
			}
			else
			{
				Log.Warning("Server", $"Attempted to unregister non-existent periodic callback: {name}");
			}
		}

		/// <summary>
		/// Updates the interval for an existing periodic callback.
		/// </summary>
		/// <param name="callback">The callback whose interval to update.</param>
		/// <param name="newInterval">The new interval in seconds.</param>
		public void UpdateCallbackInterval(Action<float> callback, float newInterval)
		{
			if (callback == null)
			{
				Log.Error("Server", "Cannot update interval for null periodic callback.");
				return;
			}

			if (newInterval <= 0)
			{
				Log.Error("Server", $"Cannot update periodic callback with non-positive interval: {newInterval}");
				return;
			}

			if (periodicCallbacks.TryGetValue(callback, out var data))
			{
				data.Interval = newInterval;
				data.TimeRemaining = newInterval;
				Log.Debug("Server", $"Updated periodic callback interval: {data.CallbackName} (new interval: {newInterval}s)");
			}
			else
			{
				Log.Warning("Server", $"Attempted to update interval for non-existent periodic callback.");
			}
		}

		#endregion
	}
}