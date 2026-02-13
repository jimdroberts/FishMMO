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
• DontDestroyOnLoad ensures persistence

**1.2 MainBootstrapSystem.Awake()**
```
→ StartBootstrap()
```

**1.3 MainBootstrapSystem.OnPreload()**
```
→ Load version.txt (standalone builds only)
→ Set GameVersion from VersionConfig
→ Register editor PlayModeStateChanged handler
→ Register Application.wantsToQuit handler
→ Initialize Logging System
    • Register UnityConsoleLogger factory
    • Create UnityConsoleFormatter (editor only)
    • Load logging.json config
    • Initialize Log manager with config
→ Determine next scene based on build type:
    #if UNITY_SERVER
        → Enqueue "ServerLauncher" scene
    #else
        → Enqueue "ClientPreboot" scene
    #endif
→ AddressableLoadProcessor.EnqueueLoad(initialScenes)
```

**1.4 BootstrapSystem.InitializePreload()**
```
→ OnPreload() (MainBootstrapSystem override called)
→ Subscribe to AddressableLoadProcessor.OnProgressUpdate
→ AddressableLoadProcessor.BeginProcessQueue()
    • Starts loading ServerLauncher scene additively
```

**1.5 Scene Load Progress**
```
→ AddressableLoadProcessor_OnPreloadProgressUpdate(progress)
→ When progress == 1.0:
    • Unsubscribe from OnProgressUpdate
    • OnCompletePreload()
        → InitializePostload()
```

**1.6 BootstrapSystem.InitializePostload()**
```
→ OnPostLoad() (no-op in MainBootstrapSystem)
→ Subscribe to AddressableLoadProcessor.OnProgressUpdate
→ AddressableLoadProcessor.BeginProcessQueue()
→ When postload progress == 1.0:
    • OnCompleteProcessing()
        → Find BootstrapSystem components in loaded scenes
        → Call StartBootstrap() on each found system
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
→ Subscribe to AddressableLoadProcessor events:
    • OnAddressableLoaded += AddressableLoadProcessor_OnAddressableLoaded
    • OnAddressableUnloaded += AddressableLoadProcessor_OnAddressableUnloaded
→ Enqueue TemplateTypeCache asset load
→ Determine which server scenes to load:
    
    #if UNITY_EDITOR
        → Load ALL servers from BootList:
            • LoginServer
            • WorldServer
            • SceneServer
    #else
        → Parse command-line arguments:
            args[1] == "LOGIN"  → Load LoginServer only
            args[1] == "WORLD"  → Load WorldServer only
            args[1] == "SCENE"  → Load SceneServer only
            No args or unknown  → Load ALL from BootList
    #endif

→ Create List<AddressableSceneLoadData> with selected scenes
→ AddressableLoadProcessor.EnqueueLoad(initialScenes)
```

**2.4 Server Scene Loading**
```
→ BootstrapSystem.InitializePreload()
    • Enqueues server scenes for loading
    • BeginProcessQueue() starts async scene loading
→ Progress updates via AddressableLoadProcessor_OnPreloadProgressUpdate
→ When complete:
    • OnCompletePreload()
    • InitializePostload()
→ When postload complete:
    • OnCompleteProcessing()
        → Find BootstrapSystem components in loaded server scenes
        → DO NOT call StartBootstrap() yet (server scenes don't have BootstrapSystem)
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
→ Log.Debug("Server", "Server is starting...")
→ Find NetworkManager in scene
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
```

**3.3 Fetch External IP Address**
```
→ NetHelper.FetchExternalIPAddress() (async web request)
→ When complete:
    • Calls OnFinalizeSetup(remoteAddress)
```

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

**PHASE 4: SERVER FINALIZE SETUP**

**4.1 Server.OnFinalizeSetup(remoteAddress)**
```
→ Validate remoteAddress is not null/empty
→ CoreServer.Initialize(remoteAddress, sceneName)
→ Create ServerAddressProvider
    • Wraps Transport, AddressOverride, PortOverride
    • Stores local and remote addresses
```

**4.2 Network Configuration**
```
→ NetworkWrapper.ApplyTransportConfiguration()
    • Configures FishNet transport with addresses/ports
→ NetworkWrapper.AttachLoginAuthenticator(this)
    • Sets up authenticator based on server type
→ NetworkWrapper.AttachServerConnectionStateEventHandler(ServerManager_OnServerConnectionState)
    • Subscribe to server connection state changes
```

**4.3 Account Management**
```
→ AccountManager = new AccountManager()
```

**4.4 RUNTIME DATA CONTAINER INITIALIZATION**
```
→ DataContainerRegistry = new RuntimeDataContainerRegistry()
→ DiscoverAndCreateDataContainers()
    
    ┌─ Step 1: Scan ServerBehaviours ─┐
    │ foreach (behaviour in ServerBehaviours)
    │ {
    │     Get [RequiresDataContainer] attributes
    │     foreach (attribute)
    │     {
    │         Check if container type already discovered (HashSet<Type>)
    │         If new:
    │             Validate container type (RuntimeDataContainerFactory)
    │             Group by InitializationPriority
    │     }
    │ }
    └──────────────────────────────────┘
    
    ┌─ Step 2: Create Containers ─────┐
    │ foreach (priorityGroup in sorted order)
    │ {
    │     foreach (containerType)
    │     {
    │         container = factory.CreateContainer(containerType)
    │         DataContainers.Add(container)
    │         Log: "Auto-created RuntimeDataContainer: {type}"
    │     }
    │ }
    └──────────────────────────────────┘
    
→ RegisterAllDataContainers()
    • foreach container in DataContainers:
        • DataContainerRegistry.Register(container)
            → Registers by concrete type
            → Registers by all implemented interfaces
    
→ DataContainerRegistry.InitializeAll(this)
    • foreach container:
        • container.InternalInitializeOnce(server, serverManager)
            → Set Server reference
            → Set ServerManager reference
            → Call container.InitializeOnce()
            → Mark Initialized = true
            → Log initialization status
```

**Container Discovery Example:**
```csharp
// PartySystem.cs declares dependency
[RequiresDataContainer(typeof(PartyRuntimeData))]
public class PartySystem : ServerBehaviour { }

// ChatSystem.cs also declares same dependency
[RequiresDataContainer(typeof(PartyRuntimeData))]
public class ChatSystem : ServerBehaviour { }

// Result: Only ONE PartyRuntimeData instance created
// Both systems access it via:
Server.DataContainerRegistry.TryGet<IPartyRuntimeData>(out var data)
```

**4.5 SERVER BEHAVIOUR INITIALIZATION**
```
→ BehaviourRegistry = new ServerBehaviourRegistry()
→ RegisterAllBehaviours()
    • foreach behaviour in ServerBehaviours:
        • BehaviourRegistry.Register(behaviour)
            → Registers by concrete type
            → Registers by all implemented interfaces
    
→ BehaviourRegistry.InitializeAll(this)
    • foreach behaviour:
        • behaviour.InternalInitializeOnce(server, serverManager)
            → Set Server reference
            → Set ServerManager reference
            → Call behaviour.InitializeOnce()
                → Behaviours can now access initialized containers!
                → Subscribe to network broadcasts
                → Register event handlers
                → Set up periodic callbacks
            → Mark Initialized = true
            → Log initialization status
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

**4.6 Physics and Network Start**
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
    │                   │           │                   │   ╰─▶ BehaviourRegistry.InitializeAll(this)
    │                   │           │                   │       ╰─ foreach behaviour:
    │                   │           │                   │           ├─ Set Server reference
    │                   │           │                   │           ├─ Set ServerManager reference
    │                   │           │                   │           ├─ Call behaviour.InitializeOnce()
    │                   │           │                   │           │   ├─ Access DataContainerRegistry
    │                   │           │                   │           │   ├─ Register network broadcasts
    │                   │           │                   │           │   ├─ Subscribe to events
    │                   │           │                   │           │   ╰─ Register periodic callbacks
    │                   │           │                   │           ├─ Initialized = true
    │                   │           │                   │           ╰─ Log initialization status
    │                   │           │                   │
    │                   │           │                   ├─ KinematicCharacterSystem.EnsureCreation()
    │                   │           │                   ├─ KinematicCharacterSystem.Settings.AutoSimulation = false
    │                   │           │                   ├─ NetworkWrapper.StartServer()
    │                   │           │                   │   ╰─ FishNet ServerManager.StartConnection()
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
