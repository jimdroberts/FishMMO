**SERVER INITIALIZATION ORDER - COMPLETE FLOW**

**Overview**
This document traces the complete server initialization flow from the MainBootstrap scene through ServerLauncher, individual server scenes (LoginServer/WorldServer/SceneServer), and finally to the Server component initialization including RuntimeDataContainer discovery.

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

**PHASE 1: MAINBOOTSTRAP SCENE LOAD**

**Scene:** MainBootstrap.unity
**Components:** MainBootstrapSystem

**1.1 Unity Engine Loads MainBootstrap Scene**
• First scene loaded by Unity
• Contains MainBootstrapSystem GameObject
• Stays loaded for the whole process — every later scene is loaded additively on top of it

**1.2 MainBootstrapSystem.Awake()** (`protected override`)
```
→ base.Awake()
    • Log.OnInternalLogMessage = OnInternalLogCallback
→ StartBootstrap()
```
Awake is `protected virtual` on BootstrapSystem and `override` here. A second
`void Awake()` would only shadow the base — Unity dispatches the most-derived
member — which used to suppress the base entirely.

**1.3 MainBootstrapSystem.OnPreload()**
```
→ Check serialized VersionConfig
    • If null: log an error and CONTINUE (do not return)
      Aborting here left the load queue empty, so the boot chain stalled forever
→ Cap Application.targetFrameRate (#if !UNITY_SERVER only; headless servers skip it)
→ GameVersion = versionConfig?.FullVersion ?? "UNKNOWN"
→ Register editor PlayModeStateChanged handler
→ Register Application.wantsToQuit handler
→ Initialize Logging System
    • Register UnityConsoleLogger factory
    • Create UnityConsoleFormatter (editor only)
    • Log.Initialize(logging.json path, formatter, manual loggers, callback)
→ Determine next scene based on build type:
    #if UNITY_SERVER
        → Enqueue "ServerLauncher" scene (with OnBootstrapPostProcess)
    #else
        → Enqueue "ClientPreboot" scene (with OnBootstrapPostProcess)
    #endif
→ AddressableLoadProcessor.EnqueueLoad(initialScenes)
```

**1.4 BootstrapSystem.InitializePreload()**
```
→ Enqueue Editor/WebGL/Standalone Preload assets + scenes (OnBootstrapPostProcess)
→ OnPreload() (MainBootstrapSystem override called — enqueues the next scene)
→ AddressableLoadBatch batch = AddressableLoadProcessor.BeginProcessQueue()
    • The batch claims exactly the items enqueued above
    • Starts loading ServerLauncher scene additively
→ batch.Completed += OnPreloadBatchCompleted
```
Completion is per-batch, not per-processor. The global
`AddressableLoadProcessor.OnProgressUpdate` event is shared by every bootstrap
system and both loading screens, so it is display-only; a batch fires exactly
once for its own items.

**1.5 Preload Batch Completion**
```
→ OnPreloadBatchCompleted(batch)   [private]
    • hasCompletedPreload guard — returns if already run
    • If batch.HasFailures: log batch.FailedItems and CONTINUE
      (a batch completes whether items succeed, fail, or are dropped)
    • OnCompletePreload()
        → InitializePostload()
```

**1.6 BootstrapSystem.InitializePostload()**
```
→ Enqueue Editor/WebGL/Standalone Postload assets + scenes (OnBootstrapPostProcess)
→ OnPostLoad() (no-op in MainBootstrapSystem)
→ AddressableLoadBatch batch = AddressableLoadProcessor.BeginProcessQueue()
→ batch.Completed += OnPostloadBatchCompleted
→ OnPostloadBatchCompleted(batch)   [private]
    • hasCompletedPostload guard — returns if already run
    • If batch.HasFailures: log and CONTINUE
    • OnCompleteProcessing()
        → Iterate a snapshot copy of preloadedBootstrapSystems
        → Call StartBootstrap() on each discovered system
```
A batch with nothing to claim completes synchronously inside
`BeginProcessQueue()`, and `Completed` invokes late subscribers immediately, so
an empty postload still advances the chain.

**1.7 Bootstrap System Discovery**
```
→ OnBootstrapPostProcess(scene) → CollectBootstrapSystems(scene)
    • Skips invalid or unloaded scenes (logs a warning)
    • GetComponentsInChildren<BootstrapSystem>(true) — includes inactive children
    • Skips self and systems already collected
    • APPENDS to preloadedBootstrapSystems (accumulated across every scene this
      system loads; it used to be reassigned per scene, so a postload scene's
      bootstraps silently displaced the preload scene's)
```

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

**PHASE 2: SERVERLAUNCHER SCENE LOAD**

**Scene:** ServerLauncher.unity
**Components:** ServerLauncher (BootstrapSystem)

**2.1 ServerLauncher Scene Loaded**
• Loaded additively by MainBootstrapSystem
• MainBootstrapSystem finds ServerLauncher component

**2.2 ServerLauncher.StartBootstrap()** (called by MainBootstrapSystem)
```
→ BootstrapSystem.StartBootstrap()
    → InitializePreload()
```

**2.3 ServerLauncher.OnPreload()**
```
→ Unsubscribe then subscribe to AddressableLoadProcessor events (idempotent):
    • OnAddressableLoaded += AddressableLoadProcessor_OnAddressableLoaded
    • OnAddressableUnloaded += AddressableLoadProcessor_OnAddressableUnloaded
      (each adds/removes ICachedObject assets from the template cache)
→ Enqueue static permanent addressable labels BEFORE any scene:
    • "Server_Static_Permanent"
    • Constants.SharedStaticLabel ("Shared_Static_Permanent")
→ Determine which server scenes to load:
    
    #if UNITY_EDITOR
        → Load ALL servers from BootList:
            • LoginServer
            • WorldServer
            • SceneServer
    #else
        → No command-line args (args.Length < 2) → Load ALL from BootList
        → Otherwise map args[1].ToUpper():
            "LOGIN"  → LoginServer only
            "WORLD"  → WorldServer only
            "SCENE"  → SceneServer only
            unknown  → Close() → Server.Quit()   (does NOT fall back to BootList)
    #endif

→ Create List<AddressableSceneLoadData> with selected scenes
→ AddressableLoadProcessor.EnqueueLoad(initialScenes)
```

**2.4 Server Scene Loading**
```
→ BootstrapSystem.InitializePreload()
    • Enqueues static labels + server scenes for loading
    • BeginProcessQueue() returns this launcher's own AddressableLoadBatch
→ batch.Completed → OnPreloadBatchCompleted (guarded by hasCompletedPreload)
    • OnCompletePreload()
    • InitializePostload()
→ Postload batch.Completed → OnPostloadBatchCompleted (hasCompletedPostload)
    • OnCompleteProcessing()
        → preloadedBootstrapSystems is EMPTY here: ServerLauncher enqueues its
          scenes without a post-process callback, and the server scenes contain
          no BootstrapSystem anyway (the only subclasses are MainBootstrapSystem,
          ServerLauncher, and ClientPostbootSystem)
        → Nothing further is started; the Server components drive themselves
          from Unity's Start() message
```

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

**PHASE 3: SERVER SCENE(S) LOAD**

**Scenes:** LoginServer.unity, WorldServer.unity, and/or SceneServer.unity
**Components:** Server (MonoBehaviour)

Each server scene contains:
• NetworkManager GameObject (FishNet)
• Server GameObject with Server component
• ServerBehaviours (ScriptableObject assets) attached to Server

**3.1 Server Scene Loaded**
• Loaded additively by ServerLauncher
• Multiple scenes may load simultaneously (editor) or single scene (standalone)

**3.2 Server.Start()**
```
→ hasShutdownFlag = 0
→ Log.Debug("Server", "Server is starting...")
→ FindFirstObjectByType<NetworkManager>()
    • Throw UnityException if not found
→ Create FileServerConfiguration
→ Create ServerEvents
→ Create CoreServer(Configuration, ServerEvents)
→ Create FishNetNetworkWrapper(networkManager, Configuration, this)
→ Subscribe to ServerEvents:
    • OnLoginServerInitialized
    • OnWorldServerInitialized
    • OnSceneServerInitialized
→ StartCoroutine(NetHelper.FetchExternalIPAddress(OnFinalizeSetup))
→ StartCoroutine(ExternalIpFetchTimeout(OnFinalizeSetup))
```

**3.3 Fetch External IP Address**
```
→ NetHelper.FetchExternalIPAddress() (async web request)
→ When complete:
    • Calls OnFinalizeSetup(remoteAddress)
→ In parallel, ExternalIpFetchTimeout waits externalIpFetchTimeoutSeconds (30s)
    • On expiry: warn, set usedFallbackAddress, call OnFinalizeSetup("127.0.0.1")
    • Whichever fires first wins; the other is rejected by the CAS guard in 4.1
```

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

**PHASE 4: SERVER FINALIZE SETUP**

**4.1 Server.OnFinalizeSetup(remoteAddress)**
```
→ Interlocked.CompareExchange(ref setupFinalized, 1, 0) guard
    • Rejects the second caller (real fetch vs. 30s timeout fallback)
    • If usedFallbackAddress, warn the operator that the real external IP
      arrived too late and must be set as Address in the config
→ Stop the timeout coroutine
→ Throw UnityException if remoteAddress is null/whitespace
→ If NetHelper.IsLoopbackAddress(remoteAddress):
    • Prefer a non-loopback "Address" from the server config
    • Otherwise warn (release builds only) that loopback will be registered
→ CoreServer.Initialize(remoteAddress, gameObject.scene.name)
```

**4.2 Database**
```
→ DatabaseConfigurationHelper.BuildDesignTimeConfiguration()
    • appsettings.json + appsettings.{env}.json + environment variables
→ Database = new Database.Database(dbConfig)
→ VerifyDatabaseSchema()
    • ValidateSchemaAsync on a thread-pool thread — NOT awaited, NOT fatal
    • Reports pending migrations and model drift, with the command that fixes each
    • A check that cannot run at all logs a warning; it never stops startup
```

> The schema check is deliberately fire-and-forget. Migrations are generated per developer and
> applied locally, so pulling an entity change brings no migration with it — the server starts
> and authenticates perfectly and the mismatch surfaces much later as a query failing on a
> column that does not exist, which reaches a player as missing data rather than as a schema
> problem. A server whose schema is stale still serves everything that does not touch the
> changed tables, so refusing to start would be a worse outcome than a loud log line.

**4.3 Address Provider, Account Manager, and Network Configuration**
```
→ AddressProvider = new ServerAddressProvider(
      TransportManager.Transport, AddressOverride, PortOverride,
      CoreServer.Address, CoreServer.RemoteAddress)

→ AccountManager comes FROM the authenticator, not from a direct construction:
    • authenticator = ServerManager.GetAuthenticator()
    • if (authenticator is IServerAuthenticator serverAuth)
          AccountManager = serverAuth.CreateAccountManager()
      else throw InvalidOperationException
    • This MUST precede AttachLoginAuthenticator, which synchronously starts
      workers that validate Server.AccountManager

→ if (authenticator is ServerAuthenticator srpAuth):
    • Pre-bootstrap TokenSigningKey (CryptoHelper.GenerateKey)
    • Derive TotpMasterKey via LocalDeriveKmsProvider
    • Required because InitializeWorkersCore hard-throws on a missing
      TotpMasterKey, and LoginServerSystem only assigns one later during
      BehaviourRegistry.InitializeAll

→ NetworkWrapper.ApplyTransportConfiguration(AddressOverride, PortOverride > 0 ? PortOverride : null)
→ NetworkWrapper.AttachLoginAuthenticator(this)
→ NetworkWrapper.RegisterServerConnectionStateEventHandler(ServerManager_OnServerConnectionState)
```

**4.4 RUNTIME DATA CONTAINER INITIALIZATION**
```
→ DataContainerRegistry = new RuntimeDataContainerRegistry()
→ DiscoverAndCreateDataContainers()
    
    ┌─ Step 1: Scan ServerBehaviours ─┐
    │ dataContainers.Clear()   // domain reload may leave stale entries
    │ foreach (behaviour in serverBehaviors)
    │ {
    │     Skip null entries
    │     Get [RequiresDataContainer] attributes
    │     foreach (attribute)
    │     {
    │         containerTypes.Add(type) — skip if already discovered
    │         factory.IsValidContainerType(type)
    │             → warn and skip if abstract or no parameterless ctor
    │         Group by InitializationPriority (SortedDictionary<int, List<Type>>)
    │     }
    │ }
    └──────────────────────────────────┘
    
    ┌─ Step 2: Create Containers ─────┐
    │ foreach (priorityGroup in sorted order)
    │ {
    │     foreach (containerType)
    │     {
    │         container = factory.CreateContainer(containerType)
    │         if (container is RuntimeDataContainer rdc)
    │             dataContainers.Add(rdc)
    │             Log: "Auto-created RuntimeDataContainer: {type}"
    │         else warn and skip
    │     }
    │ }
    └──────────────────────────────────┘
    
→ RegisterAllDataContainers()
    • foreach container in dataContainers (skipping already-Initialized ones):
        • DataContainerRegistry.Register(container)
            → Registers by concrete type
            → Registers by every IRuntimeDataContainer-derived interface
    
→ DataContainerRegistry.InitializeAll(this)
    • foreach registered component:
        • container.Initialize(typedServer, NetworkWrapper.NetworkManager.ServerManager)
            → AlreadyInitialized / FailedToFindServer / FailedToFindServerManager guards
            → Set Server reference
            → Set ServerManager reference
            → Call container.InitializeOnce()
            → Mark Initialized = true only on status == Initialized
            → Log initialization status; registry warns on any other status
```

**Container Discovery Example:**
```csharp
// PartySystem.cs declares its dependencies
[RequiresDataContainer(typeof(PartySystemRuntimeData))]
[RequiresDataContainer(typeof(PartyCharacterMappingData))]
[RequiresDataContainer(typeof(PartySystemMainThreadQueueData))]
[RequiresDataContainer(typeof(PartyCombatMeterData))]
[RequiresDataContainer(typeof(AsyncWorkerData))]
public class PartySystem : ServerBehaviour { }

// CharacterSystem.cs, QuestSystem.cs, FriendSystem.cs, ... also declare AsyncWorkerData
[RequiresDataContainer(typeof(AsyncWorkerData))]
public class CharacterSystem : ServerBehaviour { }

// Result: Only ONE AsyncWorkerData instance created for the whole server
// Systems access containers via their interfaces:
Server.DataContainerRegistry.TryGet<IPartySystemRuntimeData>(out var data)
```

**4.5 SERVER BEHAVIOUR INITIALIZATION**
```
→ BehaviourRegistry = new ServerBehaviourRegistry()
→ RegisterAllBehaviours()
    • foreach behaviour in ServerBehaviours:
        • BehaviourRegistry.Register(behaviour)
            → Registers by concrete type
            → Registers by all implemented interfaces
    
→ StartCoroutine(InitializeBehavioursThenStartServer())
    • Behaviour initialization performs database I/O, so it runs asynchronously and the
      transport starts from its completion callback — the same start/callback chain used
      above for the external IP fetch. The main thread is never blocked on I/O: it is the
      thread that drains async continuations, so blocking it stalls the work being awaited.

    • for attempt in 1..startupInitializationAttempts (default 5):
        → BehaviourRegistry.InitializeAllAsync(this, cancellationToken)
            • foreach behaviour (deduplicated — components are registered under their
              concrete type AND every interface, so Values repeats instances):
                • await behaviour.InternalInitializeOnceAsync(server, serverManager, ct)
                    → Set Server reference
                    → Set ServerManager reference
                    → await behaviour.InitializeOnceAsync(ct)
                        → Default implementation runs the synchronous InitializeOnce()
                        → Behaviours can now access initialized containers!
                        → Subscribe to network broadcasts
                        → Register event handlers
                        → Set up periodic callbacks
                    → Mark Initialized = true
            • Behaviours are awaited ONE AT A TIME in registration order, so a behaviour may
              depend on state published by an earlier one (LoginServerSystem's registered
              server ID, for example).

        → if no failures: start the transport and stop
        → else: log, wait (backoff doubles from 2s, capped at 30s), retry

    • After the final attempt: log and exit the process with code 1. A live server that
      never binds its port is worse than a dead one — a supervisor can restart a dead
      process, but a silent zombie just looks up.
```

**Behaviour Initialization Example:**
```csharp
public override ServerComponentInitializationStatus InitializeOnce()
{
    // Get the auto-created container
    if (!Server.DataContainerRegistry.TryGet<IPartyRuntimeData>(out var runtimeData))
        return ServerComponentInitializationStatus.FailedToGetDataContainer;
    
    // Register network broadcasts
    Server.NetworkWrapper.RegisterBroadcast<PartyCreateBroadcast>(
        OnServerPartyCreateBroadcastReceived, true);
    
    // Register periodic callbacks
    if (Server is IPeriodicUpdateSystem periodicSystem)
    {
        periodicSystem.RegisterPeriodicCallback(UpdatePumpRate, OnPeriodicUpdate);
    }
    
    return ServerComponentInitializationStatus.Initialized;
}
```

**4.6 Physics and Network Start** *(inside the initialization coroutine's completion path)*
```
→ KinematicCharacterSystem.EnsureCreation()
→ KinematicCharacterSystem.Settings.AutoSimulation = false
→ NetworkWrapper.StartServer()
    • FishNet ServerManager.StartConnection()
    • Server begins listening for client connections
→ Log.Debug("Server", "Initialization Complete")
```

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

**PHASE 5: RUNTIME OPERATION**

**5.1 Server.LateUpdate() (Every Frame)**
```
→ Calculate deltaTime
→ Update all ServerBehaviours:
    • foreach behaviour in ServerBehaviours:
        if (behaviour.Initialized)
            behaviour.OnLateUpdate(deltaTime)
→ Process periodic callbacks:
    • foreach callback in periodicCallbacks:
        callback.TimeRemaining -= deltaTime
        if (TimeRemaining <= 0)
            callback.Action.Invoke(deltaTime)
            callback.TimeRemaining = callback.Interval
```

**5.2 Client Connections**
```
→ Client connects to server
→ ServerManager_OnRemoteConnectionState(conn, RemoteConnectionStateArgs)
→ Authenticator validates client
→ Character spawning/loading begins
→ ServerBehaviours handle client requests via broadcasts
→ RuntimeDataContainers store mutable state
```

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

**COMPLETE INITIALIZATION TREE**

```
Unity Engine Start
│
╰─▶ Load MainBootstrap.unity
    │
    ├─▶ MainBootstrapSystem.Awake()
    │   │
    │   ╰─▶ StartBootstrap()
    │       │
    │       ├─▶ OnPreload()
    │       │   ├─ Load version.txt (standalone)
    │       │   ├─ Set GameVersion from VersionConfig
    │       │   ├─ Register PlayModeStateChanged (editor)
    │       │   ├─ Register Application.wantsToQuit
    │       │   ├─ Initialize Logging System
    │       │   │   ├─ Register UnityConsoleLogger factory
    │       │   │   ├─ Create UnityConsoleFormatter (editor)
    │       │   │   ├─ Load logging.json config
    │       │   │   ╰─ Initialize Log manager
    │       │   ╰─ Enqueue Scene Load
    │       │       ├─ #if UNITY_SERVER → "ServerLauncher"
    │       │       ╰─ #else → "ClientPreboot"
    │       │
    │       ├─▶ InitializePreload()
    │       │   ╰─ AddressableLoadProcessor.BeginProcessQueue()
    │       │       │
    │       │       ╰─▶ [ASYNC LOAD]
    │       │           │
    │       │           ╰─▶ ServerLauncher.unity loaded (additive)
    │       │
    │       ╰─▶ OnCompleteProcessing()
    │           ╰─ Find BootstrapSystem in loaded scenes
    │               │
    │               ╰─▶ ServerLauncher.StartBootstrap()
    │                   │
    │                   ├─▶ OnPreload()
    │                   │   ├─ Subscribe to AddressableLoadProcessor events
    │                   │   ├─ Enqueue TemplateTypeCache asset
    │                   │   ├─ Determine server scenes to load:
    │                   │   │   │
    │                   │   │   ├─ #if UNITY_EDITOR
    │                   │   │   │   ╰─ Load ALL from BootList:
    │                   │   │   │       ├─ LoginServer
    │                   │   │   │       ├─ WorldServer
    │                   │   │   │       ╰─ SceneServer
    │                   │   │   │
    │                   │   │   ╰─ #else (Standalone)
    │                   │   │       ├─ args[1] == "LOGIN"  → LoginServer only
    │                   │   │       ├─ args[1] == "WORLD"  → WorldServer only
    │                   │   │       ├─ args[1] == "SCENE"  → SceneServer only
    │                   │   │       ╰─ No args/unknown     → ALL from BootList
    │                   │   │
    │                   │   ╰─ AddressableLoadProcessor.EnqueueLoad(selectedScenes)
    │                   │
    │                   ├─▶ InitializePreload()
    │                   │   ╰─ AddressableLoadProcessor.BeginProcessQueue()
    │                   │       │
    │                   │       ╰─▶ [ASYNC LOAD - Server Scenes]
    │                   │           │
    │                   │           ├─▶ LoginServer.unity loaded (additive)
    │                   │           │   │
    │                   │           │   ╰─▶ Server.Start()
    │                   │           │       ├─ Find NetworkManager in scene
    │                   │           │       ├─ Create FileServerConfiguration
    │                   │           │       ├─ Create ServerEvents
    │                   │           │       ├─ Create CoreServer(config, events)
    │                   │           │       ├─ Create FishNetNetworkWrapper
    │                   │           │       ├─ Subscribe to ServerEvents
    │                   │           │       ╰─ StartCoroutine(FetchExternalIPAddress)
    │                   │           │           │
    │                   │           │           ╰─▶ [ASYNC WEB REQUEST]
    │                   │           │               │
    │                   │           │               ╰─▶ OnFinalizeSetup(remoteAddress)
    │                   │           │                   ├─ CoreServer.Initialize(remoteAddress, sceneName)
    │                   │           │                   ├─ Create ServerAddressProvider
    │                   │           │                   ├─ NetworkWrapper.ApplyTransportConfiguration()
    │                   │           │                   ├─ NetworkWrapper.AttachLoginAuthenticator()
    │                   │           │                   ├─ NetworkWrapper.AttachServerConnectionStateEventHandler()
    │                   │           │                   ├─ AccountManager = new AccountManager()
    │                   │           │                   │
    │                   │           │                   ├─▶ DATA CONTAINER INITIALIZATION
    │                   │           │                   │   ├─ DataContainerRegistry = new()
    │                   │           │                   │   │
    │                   │           │                   │   ├─▶ DiscoverAndCreateDataContainers()
    │                   │           │                   │   │   ├─ foreach ServerBehaviour:
    │                   │           │                   │   │   │   ├─ Get [RequiresDataContainer] attributes
    │                   │           │                   │   │   │   ├─ Check HashSet<Type> for duplicates
    │                   │           │                   │   │   │   ├─ Validate container type (factory)
    │                   │           │                   │   │   │   ╰─ Group by InitializationPriority
    │                   │           │                   │   │   ├─ foreach priority group (sorted):
    │                   │           │                   │   │   │   ├─ factory.CreateContainer(type)
    │                   │           │                   │   │   │   ├─ DataContainers.Add(container)
    │                   │           │                   │   │   │   ╰─ Log: "Auto-created: {type}"
    │                   │           │                   │   │   ╰─ Result: Deduplicated container list
    │                   │           │                   │   │
    │                   │           │                   │   ├─▶ RegisterAllDataContainers()
    │                   │           │                   │   │   ╰─ foreach container:
    │                   │           │                   │   │       ├─ Register by concrete type
    │                   │           │                   │   │       ╰─ Register by all interfaces
    │                   │           │                   │   │
    │                   │           │                   │   ╰─▶ DataContainerRegistry.InitializeAll(this)
    │                   │           │                   │       ╰─ foreach container:
    │                   │           │                   │           ├─ Set Server reference
    │                   │           │                   │           ├─ Set ServerManager reference
    │                   │           │                   │           ├─ Call container.InitializeOnce()
    │                   │           │                   │           ├─ Initialized = true
    │                   │           │                   │           ╰─ Log initialization status
    │                   │           │                   │
    │                   │           │                   ├─▶ BEHAVIOUR INITIALIZATION
    │                   │           │                   │   ├─ BehaviourRegistry = new()
    │                   │           │                   │   │
    │                   │           │                   │   ├─▶ RegisterAllBehaviours()
    │                   │           │                   │   │   ╰─ foreach behaviour:
    │                   │           │                   │   │       ├─ Register by concrete type
    │                   │           │                   │   │       ╰─ Register by all interfaces
    │                   │           │                   │   │
    │                   │           │                   │   ╰─▶ StartCoroutine(InitializeBehavioursThenStartServer())
    │                   │           │                   │       │   (main thread stays free; the player loop keeps
    │                   │           │                   │       │    running so async continuations can be drained)
    │                   │           │                   │       ╰─ attempt 1..5, backoff 2s→30s:
    │                   │           │                   │           ╰─ await BehaviourRegistry.InitializeAllAsync(this, ct)
    │                   │           │                   │               ╰─ foreach behaviour (deduplicated, in order):
    │                   │           │                   │                   ├─ Set Server reference
    │                   │           │                   │                   ├─ Set ServerManager reference
    │                   │           │                   │                   ├─ await behaviour.InitializeOnceAsync(ct)
    │                   │           │                   │                   │   ├─ Access DataContainerRegistry
    │                   │           │                   │                   │   ├─ Register/await database work
    │                   │           │                   │                   │   ├─ Register network broadcasts
    │                   │           │                   │                   │   ├─ Subscribe to events
    │                   │           │                   │                   │   ╰─ Register periodic callbacks
    │                   │           │                   │                   ╰─ Initialized = true
    │                   │           │                   │
    │                   │           │                   ├─ on success ─▶ KinematicCharacterSystem.EnsureCreation()
    │                   │           │                   ├─ KinematicCharacterSystem.Settings.AutoSimulation = false
    │                   │           │                   ├─ NetworkWrapper.StartServer()
    │                   │           │                   │   ╰─ FishNet ServerManager.StartConnection()
    │                   │           │                   │
    │                   │           │                   ├─ on exhausting all attempts ─▶ Application.Quit(1)
    │                   │           │                   │
    │                   │           │                   ╰─ Log: "Initialization Complete"
    │                   │           │
    │                   │           ├─▶ WorldServer.unity loaded (additive)
    │                   │           │   ╰─ [Same initialization as LoginServer]
    │                   │           │
    │                   │           ╰─▶ SceneServer.unity loaded (additive)
    │                   │               ╰─ [Same initialization as LoginServer]
    │                   │
    │                   ╰─▶ OnCompleteProcessing()
    │                       ╰─ (Server scenes don't have BootstrapSystem)
    │
    ╰─▶ SERVER(S) FULLY INITIALIZED ✓
        │
        ╰─▶ RUNTIME OPERATION (every frame)
            │
            ├─▶ Server.LateUpdate()
            │   ├─ Calculate deltaTime
            │   ├─ Update all ServerBehaviours:
            │   │   ╰─ behaviour.OnLateUpdate(deltaTime)
            │   ╰─ Process periodic callbacks:
            │       ├─ Decrement TimeRemaining
            │       ╰─ Invoke callback when TimeRemaining <= 0
            │
            ╰─▶ READY FOR CLIENT CONNECTIONS
                ├─ Client connects
                ├─ Authenticator validates
                ├─ Character spawning/loading
                ├─ ServerBehaviours handle broadcasts
                ╰─ RuntimeDataContainers store state
```

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

**KEY INITIALIZATION GUARANTEES**

**1. Containers Before Behaviours**
✅ RuntimeDataContainers are ALWAYS initialized before ServerBehaviours
✅ Behaviours can safely access containers in InitializeOnce()
✅ No race conditions between logic and data

**2. Attribute-Based Discovery**
✅ Zero manual configuration required
✅ Declare dependencies with [RequiresDataContainer(typeof(...))]
✅ Automatic deduplication (multiple systems, one instance)
✅ Priority ordering for container dependencies

**3. Hierarchical Bootstrap**
✅ MainBootstrap → ServerLauncher → Server Scene(s)
✅ Each BootstrapSystem can load additional scenes
✅ Parent systems trigger child system initialization
✅ Graceful handling of additive scene loading

**4. Build-Specific Behavior**
✅ Editor: Loads all servers simultaneously (BootList)
✅ Standalone: Uses command-line args to select server type
✅ WebGL: Separate asset/scene lists (WebGLPreloadScenes, etc.)

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

**SCENE HIERARCHY**

```
MainBootstrap (persistent, DontDestroyOnLoad)
    ├─ MainBootstrapSystem
    │   └─ Logging, Version, Config
    │
    └─ Loads → ServerLauncher (additive)
        ├─ ServerLauncher (BootstrapSystem)
        │   ├─ Asset loading (TemplateTypeCache)
        │   └─ Command-line arg parsing
        │
        └─ Loads → Server Scene(s) (additive)
            ├─ LoginServer.unity
            │   ├─ NetworkManager
            │   ├─ Server (MonoBehaviour)
            │   └─ ServerBehaviours (LoginServerSystem, etc.)
            │
            ├─ WorldServer.unity
            │   ├─ NetworkManager
            │   ├─ Server (MonoBehaviour)
            │   └─ ServerBehaviours (WorldServerSystem, WorldSceneSystem, etc.)
            │
            └─ SceneServer.unity
                ├─ NetworkManager
                ├─ Server (MonoBehaviour)
                └─ ServerBehaviours (CharacterSystem, PartySystem, etc.)
```

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

**SHUTDOWN SEQUENCE**

```
Application wants to quit (or Editor exits Play Mode)
    ↓
MainBootstrapSystem.OnApplicationWantsToQuit()
    • Returns false (defer quitting)
    ↓
InitiateShutdown()
    • Graphics cleanup (release addressables)
    • UnityLoggerBridge.Shutdown()
    ↓
PerformAsyncShutdown() (standalone only)
    • Save logging.json config
    • Log.Shutdown() (flush logs)
    • Set canQuitApplication = true
    ↓
Server.OnDestroy()
    ↓
Server.OnApplicationQuit()
    • DeinitializeAllBehaviours()
        → foreach behaviour (reverse order):
            behaviour.Deinitialize()
    • UnregisterAllBehaviours()
    • DeinitializeAllDataContainers()
        → foreach container (reverse order):
            container.Clear()
            container.Deinitialize()
    • UnregisterAllDataContainers()
    • periodicCallbacks.Clear()
    ↓
Application.Quit()
```

> **`Deinitialize()` must clear a behaviour's own fields, not just its registrations.**
> ServerBehaviours are ScriptableObject *assets*, so any state held in their fields survives a
> play-session restart in the editor — while FishNet reissues `ClientId`s from zero. A
> per-connection map left populated is therefore read as the *next* session's state, and for a
> deadline map every surviving entry is already expired, so the first sweep of the new run
> disconnects clients that have done nothing wrong. `CharacterSystem.OnDeinitialize` clears its
> watchdog and rate-limit maps for exactly this reason — `characterResidencyDeadlines`,
> `sceneLoadDeadlines`, `startScenesAckedClientIds`, `pendingTransferDisconnects`,
> `deliberateTransferClientIds` and the three rate-limit maps; `WorldSceneSystem.OnDeinitialize`
> does the same before its early-return guards, since that state has no dependencies to check
> first. The consequence is not always a spurious disconnect: `startScenesAckedClientIds` records
> that a connection has *completed* a handshake step, so a stale entry makes the next session on
> that id skip a step it never performed. Data containers are separate — the registry calls
> `Clear()` on each one for you.

> **Behaviours are torn down before data containers, and that ordering is load-bearing.**
> `CharacterSystem.OnDeinitialize` performs the final synchronous flush — every resident
> character saved and every session claim released — and it needs its mapping data intact to do
> it. Two consequences follow for anything that queues work during teardown. First, `Clear()` on
> `AsyncWorkerData` deliberately does **not** discard the accepted backlog: its only caller is
> this path, immediately before the drain, so discarding would throw away precisely the saves and
> session releases the behaviours had just enqueued — and an unreleased claim leaves the character
> Online until its lease expires, which the fail-closed duplicate-login gate turns into a
> two-minute lockout from every server. Second, every blocking wait on this path is charged
> against one shared budget (`UnitySyncOverAsync.BeginShutdownBudget`, 8s), including the worker
> drain, because the total is sized to fit inside a supervisor's stop timeout — overrunning it
> means being SIGKILLed mid-flush having accomplished nothing, which is strictly worse than
> flushing what fits and exiting cleanly.

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

**SUMMARY**

The FishMMO server initialization follows a robust multi-phase approach:

**Phase 1:** MainBootstrap establishes logging, versioning, and core systems
**Phase 2:** ServerLauncher determines which server(s) to load based on build type
**Phase 3:** Individual Server scenes load with NetworkManager and Server components
**Phase 4:** Server.OnFinalizeSetup() performs the critical initialization:
• Discovers and creates RuntimeDataContainers (data first)
• Initializes ServerBehaviours (logic second, with data available)
**Phase 5:** Runtime operation with frame-based updates and periodic callbacks

This architecture ensures:
✅ Proper initialization order (logging → network → data → logic)
✅ Separation of concerns (BootstrapSystem → Server → Behaviour/Container)
✅ Flexible deployment (editor multi-server vs standalone single-server)
✅ Graceful shutdown with async cleanup
✅ Type-safe dependency injection via attributes
