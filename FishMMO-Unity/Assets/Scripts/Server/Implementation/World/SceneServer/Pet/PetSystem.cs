using FishNet.Connection;
using FishNet.Object;
using FishNet.Transporting;
using FishNet.Utility.Performance;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared.Core;
using FishMMO.Shared;
using FishMMO.Logging;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Manages pet-related server logic, including pet summoning, following, staying, releasing, and persistence.
	/// Handles pet broadcasts, character events, and pet AI initialization for player characters.
	/// Game logic and Broadcasts run synchronously on the main thread.
	/// Database operations are async to avoid blocking the main thread.
	/// Results from async DB queries that require main-thread state changes or Broadcasts are marshalled
	/// via IPetSystemMainThreadQueueData.
	/// </summary>
	[CreateAssetMenu(fileName = "PetSystem", menuName = "FishMMO/Server/SceneServer/Pet System", order = 1)]
	[RequiresDataContainer(typeof(PetSystemMainThreadQueueData))]
	[RequiresDataContainer(typeof(PetSystemRuntimeData))]
	[RequiresDataContainer(typeof(AsyncWorkerData))]
	public class PetSystem : ServerBehaviour, IPetSystem
	{
		/// <summary>
		/// Maximum number of queued main-thread actions processed per frame.
		/// This time-slices queue draining to avoid frame spikes.
		/// </summary>
		[Header("Main Thread Dispatch")]
		[Tooltip("Max pet-system actions drained from main-thread queue per frame")]
		[SerializeField] private int maxMainThreadActionsPerFrame = 100;

		/// <summary>
		/// Debounce window in milliseconds for pet control ingress requests.
		/// </summary>
		[Header("Ingress Protection")]
		[Tooltip("Minimum milliseconds between pet control requests per connection")]
		[SerializeField] private int ingressDebounceMilliseconds = 80;

		/// <summary>
		/// Interval in seconds between ingress-guard cleanup sweeps.
		/// </summary>
		[Tooltip("Seconds between bounded ingress guard cleanup sweeps")]
		[SerializeField] private float ingressSweepIntervalSeconds = 5.0f;

		/// <summary>
		/// Guard entry time-to-live in seconds.
		/// </summary>
		[Tooltip("Seconds before stale ingress guard entries are removed")]
		[SerializeField] private float ingressEntryTtlSeconds = 30.0f;

		/// <summary>
		/// Maximum stale guard entries removed per cleanup sweep.
		/// </summary>
		[Tooltip("Maximum stale ingress guard entries removed per sweep")]
		[SerializeField] private int ingressSweepMaxRemovals = 128;

		[Header("Achievements")]
		/// <summary>
		/// Achievement template awarded when a character summons a pet.
		/// </summary>
		public AchievementTemplate PetSummonAchievementTemplate;

		/// <summary>
		/// Operation codes used by pet ingress guards.
		/// </summary>
		private enum IngressOperation : byte
		{
			Follow = 1,
			Stay = 2,
			Summon = 3,
			Release = 4,
			LoadPet = 5,
			Attack = 6,
			Stance = 7,
		}

		/// <summary>
		/// Called once to initialize the pet system. Registers broadcast handlers and subscribes to character and ability events.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			if (Server == null)
			{
				Log.Error("PetSystem", "InitializeOnce: Server is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (!Server.DataContainerRegistry.TryGet<IPetSystemMainThreadQueueData>(out _))
			{
				Log.Error("PetSystem", "Failed to initialize: IPetSystemMainThreadQueueData not found");
				return ServerComponentInitializationStatus.FailedToGetDataContainer;
			}

			if (!Server.DataContainerRegistry.TryGet<IPetSystemRuntimeData>(out var runtimeData))
			{
				Log.Error("PetSystem", "Failed to initialize: IPetSystemRuntimeData not found");
				return ServerComponentInitializationStatus.FailedToGetDataContainer;
			}

			// Network broadcasts
			Server.NetworkWrapper.RegisterBroadcast<PetFollowBroadcast>(OnPetFollowBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<PetStayBroadcast>(OnPetStayBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<PetSummonBroadcast>(OnPetSummonBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<PetReleaseBroadcast>(OnPetReleaseBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<PetAttackBroadcast>(OnPetAttackBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<PetStanceBroadcast>(OnPetStanceBroadcastReceived, true);

			// Ability events
			AbilityObject.OnPetSummon += AbilityObject_OnPetSummon;

			// Character system events
			if (Server.BehaviourRegistry.TryGet(out ICharacterSystem<NetworkConnection, Scene> characterSystem))
			{
				characterSystem.OnSpawnCharacter += CharacterSystem_OnSpawnCharacter;
				characterSystem.OnDespawnCharacter += CharacterSystem_OnDespawnCharacter;
				characterSystem.OnPetKilled += CharacterSystem_OnPetKilled;
			}

			maxMainThreadActionsPerFrame = Mathf.Max(1, maxMainThreadActionsPerFrame);
			ingressDebounceMilliseconds = Mathf.Max(0, ingressDebounceMilliseconds);
			ingressSweepIntervalSeconds = Mathf.Max(0.25f, ingressSweepIntervalSeconds);
			ingressEntryTtlSeconds = Mathf.Max(1.0f, ingressEntryTtlSeconds);
			ingressSweepMaxRemovals = Mathf.Max(1, ingressSweepMaxRemovals);

			Log.Debug("PetSystem", "Initialized");
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Called when the system is being destroyed. Unregisters broadcast handlers and unsubscribes from character and ability events.
		/// </summary>
		public override void OnDeinitialize()
		{
			if (Server == null)
			{
				Log.Error("PetSystem", "OnDeinitialize: Server is null");
				return;
			}

			// Drain any remaining queued main-thread actions
			DrainMainThreadQueue(drainAll: true);

			// Network broadcasts
			Server.NetworkWrapper.UnregisterBroadcast<PetFollowBroadcast>(OnPetFollowBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<PetStayBroadcast>(OnPetStayBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<PetSummonBroadcast>(OnPetSummonBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<PetReleaseBroadcast>(OnPetReleaseBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<PetAttackBroadcast>(OnPetAttackBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<PetStanceBroadcast>(OnPetStanceBroadcastReceived);

			// Ability events
			AbilityObject.OnPetSummon -= AbilityObject_OnPetSummon;

			// Character system events
			if (Server.BehaviourRegistry.TryGet(out ICharacterSystem<NetworkConnection, Scene> characterSystem))
			{
				characterSystem.OnSpawnCharacter -= CharacterSystem_OnSpawnCharacter;
				characterSystem.OnDespawnCharacter -= CharacterSystem_OnDespawnCharacter;
				characterSystem.OnPetKilled -= CharacterSystem_OnPetKilled;
			}

			if (Server.DataContainerRegistry.TryGet<IPetSystemRuntimeData>(out var runtimeData))
			{
				runtimeData.IngressGuard?.Clear();
			}
		}

		/// <summary>
		/// Attempts to acquire ingress debounce and in-flight guard for a connection operation.
		/// </summary>
		private bool TryBeginIngressGuard(int connectionId, IngressOperation operation, out long guardKey)
		{
			if (!Server.DataContainerRegistry.TryGet<IPetSystemRuntimeData>(out var runtimeData))
			{
				guardKey = 0;
				return false;
			}
			return runtimeData.IngressGuard.TryBegin(connectionId, (byte)operation, ingressDebounceMilliseconds, out guardKey);
		}

		/// <summary>
		/// Releases an ingress in-flight guard key.
		/// </summary>
		private void EndIngressGuard(long guardKey)
		{
			if (Server.DataContainerRegistry.TryGet<IPetSystemRuntimeData>(out var runtimeData))
			{
				runtimeData.IngressGuard.End(guardKey);
			}
		}

		/// <summary>
		/// Drains queued main-thread actions from the IPetSystemMainThreadQueueData container.
		/// </summary>
		private void DrainMainThreadQueue(bool drainAll)
		{
			DrainMainThreadQueue<IPetSystemMainThreadQueueData>(maxMainThreadActionsPerFrame, drainAll);
		}

		/// <summary>
		/// Enqueues an action to be executed on the main thread.
		/// </summary>
		/// <param name="action">The action to enqueue.</param>
		private bool TryEnqueueMainThread(Action action)
		{
			return TryEnqueueMainThread<IPetSystemMainThreadQueueData>(action);
		}

		/// <summary>
		/// Drains the main-thread queue each frame.
		/// </summary>
		protected override void OnUpdate(float deltaTime)
		{
			DrainMainThreadQueue(drainAll: false);

			if (Server.DataContainerRegistry.TryGet<IPetSystemRuntimeData>(out var runtimeData))
			{
				runtimeData.IngressGuard.Sweep(ingressSweepIntervalSeconds, ingressEntryTtlSeconds, ingressSweepMaxRemovals);
			}
		}

		/// <summary>
		/// Handles pet follow broadcast, updating pet AI to follow the character.
		/// </summary>
		private void OnPetFollowBroadcastReceived(NetworkConnection conn, PetFollowBroadcast msg, Channel channel)
		{
			if (conn == null || conn.FirstObject == null)
			{
				return;
			}

			IPlayerCharacter player = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (player == null || !CharacterStateValidation.CanAct(player))
				return;

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.Follow, out long guardKey))
			{
				return;
			}

			try
			{

				IPetController petController = conn.FirstObject.GetComponent<IPetController>();
				if (petController == null || petController.Pet == null)
				{
					// no pet exists
					return;
				}

				SetMovementOrder(conn, petController, PetMovementOrder.Follow);
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}

		/// <summary>
		/// Handles pet stay broadcast, updating pet AI to stay at its current position.
		/// </summary>
		private void OnPetStayBroadcastReceived(NetworkConnection conn, PetStayBroadcast msg, Channel channel)
		{
			if (conn == null || conn.FirstObject == null)
			{
				return;
			}

			IPlayerCharacter player = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (player == null || !CharacterStateValidation.CanAct(player))
				return;

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.Stay, out long guardKey))
			{
				return;
			}

			try
			{

				IPetController petController = conn.FirstObject.GetComponent<IPetController>();
				if (petController == null || petController.Pet == null)
				{
					// no pet exists
					return;
				}

				SetMovementOrder(conn, petController, PetMovementOrder.Stay);
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}

		/// <summary>
		/// Applies a movement order to the pet and confirms it to the owner.
		/// </summary>
		/// <remarks>
		/// Follow and Stay used to be expressed by writing <c>IAIController.Target</c> — the
		/// combat target — which conflated "who I am escorting" with "who I am killing". Stay set
		/// it to null, and the pet's idle state bailed out on a null target, so a pet told to stay
		/// never moved again even after being told to follow. Orders now live on the pet.
		/// </remarks>
		/// <param name="conn">The owning connection to confirm to.</param>
		/// <param name="petController">The owner's pet controller.</param>
		/// <param name="order">The order to apply.</param>
		private void SetMovementOrder(NetworkConnection conn, IPetController petController, PetMovementOrder order)
		{
			Pet pet = petController.Pet;
			if (pet == null)
			{
				return;
			}

			pet.MovementOrder = order;
			petController.MovementOrder = order;

			if (pet.TryGet(out IAIController aiController))
			{
				if (order == PetMovementOrder.Stay)
				{
					// Hold this spot: the pet's leash anchor becomes where it is standing.
					aiController.Home = pet.Transform.position;
				}
				else if (petController.Character != null)
				{
					aiController.Home = petController.Character.Transform.position;
				}
			}

			Server.NetworkWrapper.Broadcast(conn, new PetMovementOrderBroadcast() { MovementOrder = order }, true, Channel.Reliable);
		}

		/// <summary>
		/// Handles pet summon broadcast, warping pet to the character's position.
		/// </summary>
		private void OnPetSummonBroadcastReceived(NetworkConnection conn, PetSummonBroadcast msg, Channel channel)
		{
			if (conn == null || conn.FirstObject == null)
			{
				return;
			}

			IPlayerCharacter player = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (player == null || !CharacterStateValidation.CanAct(player))
				return;

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.Summon, out long guardKey))
			{
				return;
			}

			try
			{

				IPetController petController = conn.FirstObject.GetComponent<IPetController>();
				if (petController == null || petController.Pet == null)
				{
					// no pet exists
					return;
				}

				if (petController.Pet.TryGet(out IAIController aiController))
				{
					aiController.Agent.Warp(petController.Character.Transform.position);
				}
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}

		/// <summary>
		/// Handles pet release broadcast, saving pet state and despawning the pet object.
		/// </summary>
		private void OnPetReleaseBroadcastReceived(NetworkConnection conn, PetReleaseBroadcast msg, Channel channel)
		{
			if (conn == null || conn.FirstObject == null)
			{
				return;
			}

			IPlayerCharacter player = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (player == null || !CharacterStateValidation.CanAct(player))
				return;

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.Release, out long guardKey))
			{
				return;
			}

			try
			{
				if (Server?.Database?.ServiceRegistry == null)
				{
					return;
				}

				IPetController petController = conn.FirstObject.GetComponent<IPetController>();
				if (petController == null || petController.Pet == null)
				{
					// no pet exists
					return;
				}

				// Capture immutable data for the async path. CaptureKnownAbilities first, so
				// abilities granted at summon time from the PetAbilityTemplate are persisted
				// rather than silently lost on the next log in.
				petController.Pet.CaptureKnownAbilities();

				long characterID = petController.Character.ID;
				int templateID = petController.Pet.PetAbilityTemplate != null ? petController.Pet.PetAbilityTemplate.ID : 0;
				List<int> abilities = new List<int>(petController.Pet.PetAbilityIDs);

				if (petController.Pet != null &&
					petController.Pet.NetworkObject.IsSpawned)
				{
					ServerManager.Despawn(petController.Pet.NetworkObject, DespawnType.Pool);
				}
				petController.Pet.PetOwner = null;
				petController.Pet = null;
				petController.OnOwnerAttacked -= PetController_OnOwnerAttacked;

				Server.NetworkWrapper.Broadcast(conn, new PetRemoveBroadcast(), true, Channel.Reliable);

				// Async DB save with spawned=false, keyed by characterID to serialize with summon ops
				EnqueuePersistence(() => SavePetAsync(characterID, templateID, abilities, false), characterID);
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}

		/// <summary>
		/// Handles pet attack broadcast: sends the pet at whatever the owner is currently targeting.
		/// </summary>
		/// <remarks>
		/// The target is read from the owner's own <see cref="ITargetController"/> rather than
		/// taken from the message, so a client cannot name an arbitrary entity as its pet's
		/// victim. The target is then re-validated here: alive, not the owner, not the pet, and
		/// hostile by faction.
		/// </remarks>
		private void OnPetAttackBroadcastReceived(NetworkConnection conn, PetAttackBroadcast msg, Channel channel)
		{
			if (conn == null || conn.FirstObject == null)
			{
				return;
			}

			IPlayerCharacter player = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (player == null || !CharacterStateValidation.CanAct(player))
				return;

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.Attack, out long guardKey))
			{
				return;
			}

			try
			{
				IPetController petController = conn.FirstObject.GetComponent<IPetController>();
				if (petController == null || petController.Pet == null)
				{
					// no pet exists
					return;
				}

				if (!player.TryGet(out ITargetController targetController))
				{
					return;
				}

				Transform targetTransform = targetController.Current.Target;
				if (targetTransform == null)
				{
					return;
				}

				ICharacter target = targetTransform.GetComponent<ICharacter>();
				if (!IsValidPetTarget(petController, player, target))
				{
					return;
				}

				CommandPetAttack(petController.Pet, target);
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}

		/// <summary>
		/// Handles a stance change request from the owner.
		/// </summary>
		private void OnPetStanceBroadcastReceived(NetworkConnection conn, PetStanceBroadcast msg, Channel channel)
		{
			if (conn == null || conn.FirstObject == null)
			{
				return;
			}

			IPlayerCharacter player = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (player == null || !CharacterStateValidation.CanAct(player))
				return;

			// Reject values outside the enum rather than casting a hostile byte straight in.
			if (!Enum.IsDefined(typeof(PetStance), msg.Stance))
			{
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.Stance, out long guardKey))
			{
				return;
			}

			try
			{
				IPetController petController = conn.FirstObject.GetComponent<IPetController>();
				if (petController == null || petController.Pet == null)
				{
					// no pet exists
					return;
				}

				petController.Pet.Stance = msg.Stance;
				petController.Stance = msg.Stance;

				// A pet dropped to Passive stops what it is doing.
				if (msg.Stance == PetStance.Passive)
				{
					RecallPet(petController.Pet);
				}

				Server.NetworkWrapper.Broadcast(conn, new PetStanceBroadcast() { Stance = msg.Stance }, true, Channel.Reliable);
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}

		/// <summary>
		/// Returns true when a character is a legal thing to send a pet at.
		/// </summary>
		/// <param name="petController">The owner's pet controller.</param>
		/// <param name="owner">The pet's owner.</param>
		/// <param name="target">The candidate target.</param>
		/// <returns>True if the pet may attack the candidate.</returns>
		private static bool IsValidPetTarget(IPetController petController, IPlayerCharacter owner, ICharacter target)
		{
			if (target == null || target == owner)
			{
				return false;
			}

			// Never let a player order their pet onto itself.
			if (petController.Pet != null && ReferenceEquals(target, petController.Pet))
			{
				return false;
			}

			if (!target.TryGet(out ICharacterDamageController damageController) || !damageController.IsAlive)
			{
				return false;
			}

			// Faction gate: the pet copies its owner's factions at summon time, so asking the
			// pet's own faction controller gives the same answer the AI would give itself.
			if (petController.Pet != null &&
				petController.Pet.TryGet(out IFactionController petFaction) &&
				target.TryGet(out IFactionController targetFaction) &&
				targetFaction.GetAllianceLevel(petFaction) != FactionAllianceLevel.Enemy)
			{
				return false;
			}

			return true;
		}

		/// <summary>
		/// Points a pet at a target and pushes it into its attacking state.
		/// </summary>
		/// <param name="pet">The pet to command.</param>
		/// <param name="target">The character to attack.</param>
		private static void CommandPetAttack(Pet pet, ICharacter target)
		{
			if (!pet.TryGet(out IAIController aiController))
			{
				return;
			}

			aiController.Target = target.Transform;

			// An explicit order overrides a Stay: the pet cannot fight from its owner's heel.
			pet.MovementOrder = PetMovementOrder.Follow;

			if (aiController.AttackingState != null)
			{
				aiController.ChangeState(aiController.AttackingState);
			}
		}

		/// <summary>
		/// Breaks a pet off whatever it is doing and returns it to its owner.
		/// </summary>
		/// <param name="pet">The pet to recall.</param>
		private static void RecallPet(Pet pet)
		{
			if (pet == null || !pet.TryGet(out IAIController aiController))
			{
				return;
			}

			aiController.Target = null;

			if (pet.TryGet(out IAbilityController abilityController))
			{
				abilityController.Interrupt(null);
			}

			if (aiController.IdleState != null)
			{
				aiController.ChangeState(aiController.IdleState);
			}
		}

		/// <summary>
		/// Handles character spawn event, loading and spawning the pet for the character if available.
		/// </summary>
		private void CharacterSystem_OnSpawnCharacter(NetworkConnection conn, IPlayerCharacter character, Scene scene)
		{
			if (character == null)
			{
				return;
			}

			if (scene == null)
			{
				return;
			}

			if (!character.TryGet(out IPetController petController))
			{
				return;
			}

			if (Server?.Database?.ServiceRegistry == null)
			{
				return;
			}

			// Capture immutable data for the async path
			long characterID = character.ID;

			// In-flight guard to prevent duplicate pet loads on rapid reconnect.
			// Use negative characterID as key to avoid collision with positive ingress guard keys.
			if (!Server.DataContainerRegistry.TryGet<IPetSystemRuntimeData>(out var runtimeData))
			{
				return;
			}
			if (!runtimeData.IngressGuard.TryBegin(conn.ClientId, (byte)IngressOperation.LoadPet, 0, out long guardKey))
			{
				return;
			}

			if (!TryEnqueueAsyncWork(async () =>
			{
				try
				{
					await LoadAndSpawnPetAsync(conn, character, scene, characterID);
				}
				finally
				{
					if (Server?.DataContainerRegistry.TryGet<IPetSystemRuntimeData>(out var rt) == true)
					{
						rt.IngressGuard.End(guardKey);
					}
				}
			}, characterID))
			{
				Log.Warning("PetSystem", $"Failed to enqueue LoadPet async work for character {characterID} — queue may be full or shutting down.");
				runtimeData.IngressGuard.End(guardKey);
			}
		}

		/// <summary>
		/// Shared helper that despawns any existing pet, instantiates and initialises a new one,
		/// moves it to the owner's scene, spawns it on the network, copies faction data, and broadcasts the add.
		/// Called from both <see cref="LoadAndSpawnPetAsync"/> and <see cref="AbilityObject_OnPetSummon"/>.
		/// </summary>
		/// <param name="owner">The player character that owns the pet.</param>
		/// <param name="petController">The owner's pet controller component.</param>
		/// <param name="petAbilityTemplate">Template describing the pet prefab and abilities.</param>
		/// <param name="nob">Pooled NetworkObject already instantiated at the desired spawn position.</param>
		/// <param name="aiInitPosition">Position passed to <see cref="IAIController.Initialize"/>.</param>
		/// <param name="abilities">Optional ability list restored from the database; null when summoned fresh.</param>
		/// <param name="broadcastTarget">Network connection that should receive the PetAddBroadcast.</param>
		/// <returns>True if the pet was successfully spawned; false if the pooled object had no Pet component.</returns>
		private bool SpawnAndInitializePet(
			IPlayerCharacter owner,
			IPetController petController,
			PetAbilityTemplate petAbilityTemplate,
			NetworkObject nob,
			Vector3 aiInitPosition,
			List<int> abilities,
			NetworkConnection broadcastTarget)
		{
			// Despawn any existing pet before assigning the new one to prevent ghost pets.
			if (petController.Pet != null &&
				petController.Pet.NetworkObject != null &&
				petController.Pet.NetworkObject.IsSpawned)
			{
				ServerManager.Despawn(petController.Pet.NetworkObject, DespawnType.Pool);
				petController.Pet = null;
			}

			Pet pet = nob.GetComponent<Pet>();
			if (pet == null)
			{
				// Pool object has no Pet component — return it to the pool to prevent a leak.
				ServerManager.Despawn(nob, DespawnType.Pool);
				return false;
			}

			pet.PetOwner = owner;
			pet.PetAbilityTemplate = petAbilityTemplate;
			pet.Stance = petController.Stance;
			pet.MovementOrder = PetMovementOrder.Follow;
			petController.MovementOrder = PetMovementOrder.Follow;

			/* Build the ability list before the spawn: Pet.OnStartServer teaches whatever is in
			 * PetAbilityIDs, and OnStartServer runs inside ServerManager.Spawn below. */
			pet.PetAbilityIDs = BuildPetAbilityIDs(petAbilityTemplate, abilities);

			petController.Pet = pet;

			UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(pet.GameObject, owner.GameObject.scene);

			// Ensure the game object is active, pooled objects are disabled
			pet.GameObject.SetActive(true);

			/* Initialise the brain only once the GameObject is active and in its final scene.
			 * Initialize enters the pet's first state, and a state's Enter touches the
			 * NavMeshAgent (speed, isStopped, destination). Doing that on a pooled object that is
			 * still disabled and not yet placed on a NavMesh makes Unity reject each call with an
			 * "agent is not active / not on a NavMesh" error. */
			if (pet.TryGet(out IAIController aiController))
			{
				/* The pet's home is where it stands, not the world origin. The database load path
				 * passed Vector3.zero here, so a restored pet leashed to (0,0,0) and any state
				 * with leashing enabled marched it across the map to the map's corner. */
				aiController.Initialize(aiInitPosition);

				// Follow, do not fight: Target means "what I am attacking" to the whole AI.
				aiController.Target = null;
			}

			/* Spawned WITHOUT an owning connection, deliberately.
			 *
			 * A pet's brain runs on the server, and CharacterPredictionController only produces
			 * replicate input on the peer with input authority. Handing the pet to the summoner's
			 * connection made the summoner's client the owner, so the client was expected to
			 * supply the input for a brain that does not run there and the server's own decisions
			 * were discarded — the pet could never cast anything. Nothing in the pet system reads
			 * the pet's Owner, and its NetworkTransform is server-authoritative, so server
			 * ownership costs nothing and makes the pet behave exactly like any other NPC. */
			ServerManager.Spawn(pet.GameObject, null, owner.GameObject.scene);

			if (pet.TryGet(out IFactionController petFactionController))
			{
				if (owner.TryGet(out IFactionController ownerFactionController))
				{
					petFactionController.CopyFrom(ownerFactionController);
				}
			}

			// Wire the defensive-stance hook so the pet answers when its owner is attacked.
			SubscribeOwnerAttacked(petController);

			Server.NetworkWrapper.Broadcast(broadcastTarget, new PetAddBroadcast()
			{
				ID = pet.ID,
				Stance = pet.Stance,
				MovementOrder = pet.MovementOrder,
			}, true, Channel.Reliable);

			// Increment achievement for summoning a pet
			if (PetSummonAchievementTemplate != null &&
				owner.TryGet(out IAchievementController achievementController))
			{
				achievementController.Increment(PetSummonAchievementTemplate, 1);
			}

			return true;
		}

		/// <summary>
		/// Asynchronously fetches the spawned pet from the database and marshals pet instantiation back to the main thread.
		/// </summary>
		/// <param name="conn">Owning connection for pet broadcasts.</param>
		/// <param name="character">Owning character for pet spawn context.</param>
		/// <param name="scene">Scene where the character currently exists.</param>
		/// <param name="characterID">Character identifier used for persistence lookup.</param>
		/// <returns>Asynchronous load-and-spawn task.</returns>
		private async Task LoadAndSpawnPetAsync(NetworkConnection conn, IPlayerCharacter character, Scene scene, long characterID)
		{
			try
			{
				if (!Server.Database.ServiceRegistry.TryGet<ICharacterPetService>(out var charPetService))
				{
					return;
				}

				DatabaseResult<CharacterPetData?> fetchResult = await charPetService.FetchSpawnedAsync(characterID);
				if (!fetchResult.IsSuccess || !fetchResult.Data.HasValue)
				{
					return;
				}

				CharacterPetData petData = fetchResult.Data.Value;

				// Marshal pet instantiation back to the main thread
				TryEnqueueMainThread(() =>
				{
					// Guard against character being destroyed/despawned before the DB returned
					if (Server == null ||
						conn == null || !conn.IsActive ||
						character == null || character.NetworkObject == null || !character.NetworkObject.IsSpawned)
					{
						return;
					}

					// Look up the pet ability template by ID
					PetAbilityTemplate petAbilityTemplate = BaseAbilityTemplate.Get<PetAbilityTemplate>(petData.TemplateID);
					if (petAbilityTemplate == null || petAbilityTemplate.PetPrefab == null)
					{
						return;
					}

					if (!character.TryGet(out IPetController petController))
					{
						return;
					}

					// Instantiate the pet from the prefab pool
					Vector3 spawnPosition = character.Transform.position;
					NetworkObject nob = Server.NetworkWrapper.NetworkManager.GetPooledInstantiated(
						petAbilityTemplate.PetPrefab.PrefabId,
						petAbilityTemplate.PetPrefab.SpawnableCollectionId,
						ObjectPoolRetrieveOption.Unset,
						null, spawnPosition, character.Transform.rotation, null, true);

					List<int> abilities = petData.Abilities != null ? new List<int>(petData.Abilities) : new List<int>();

					/* spawnPosition, not Vector3.zero. This is the AI's home, and passing the origin
					 * leashed every database-restored pet to world (0,0,0): the moment it entered
					 * any state with leashing enabled it walked — or warped — to the corner of the
					 * map. The summon path always passed a real position; only this one did not. */
					SpawnAndInitializePet(character, petController, petAbilityTemplate, nob, spawnPosition, abilities, conn);
				});
			}
			catch (Exception ex)
			{
				await Log.Error("PetSystem", $"Error loading/spawning pet (CharID={characterID}): {ex}");
			}
		}

		/// <summary>
		/// Handles character despawn event, saving pet state and despawning the pet object if necessary.
		/// </summary>
		private void CharacterSystem_OnDespawnCharacter(NetworkConnection conn, IPlayerCharacter character)
		{
			if (character == null)
			{
				return;
			}

			if (!character.TryGet(out IPetController petController))
			{
				return;
			}

			if (Server?.Database?.ServiceRegistry == null)
			{
				return;
			}

			float currentHealth = 0.0f;
			if (petController.Pet != null &&
				petController.Pet.TryGet(out ICharacterAttributeController petAttributeController) &&
				petAttributeController.TryGetHealthAttribute(out CharacterResourceAttribute health))
			{
				currentHealth = health.CurrentValue;
			}

			petController.Pet?.CaptureKnownAbilities();

			// Capture immutable data for the async path
			long characterID = character.ID;
			int templateID = petController.Pet?.PetAbilityTemplate != null ? petController.Pet.PetAbilityTemplate.ID : 0;
			List<int> abilities = petController.Pet?.PetAbilityIDs != null ? new List<int>(petController.Pet.PetAbilityIDs) : new List<int>();
			bool spawned = petController.Pet != null && currentHealth > 0.0f;

			if (petController.Pet != null &&
				petController.Pet.NetworkObject.IsSpawned)
			{
				ServerManager.Despawn(petController.Pet.NetworkObject, DespawnType.Pool);
			}

			/* Drop the reference. Leaving it set meant that after a pet died, the owner's
			 * controller still pointed at a despawned, pooled object — so a Summon or Follow
			 * command would happily warp and re-task a pet that no longer existed. */
			if (petController.Pet != null)
			{
				petController.Pet.PetOwner = null;
				petController.Pet = null;
			}
			petController.OnOwnerAttacked -= PetController_OnOwnerAttacked;

			// Async DB save, keyed by characterID to serialize with other pet ops for the same character
			EnqueuePersistence(() => SavePetAsync(characterID, templateID, abilities, spawned), characterID);
		}

		/// <summary>
		/// Asynchronously persists pet state to the database.
		/// Fetches the existing pet record to obtain the version, then persists with version+1.
		/// </summary>
		/// <param name="characterID">Character identifier that owns the pet.</param>
		/// <param name="templateID">Pet template identifier.</param>
		/// <param name="abilities">Pet ability template identifiers.</param>
		/// <param name="spawned">Whether the pet should be marked as spawned.</param>
		/// <returns>Asynchronous persistence task.</returns>
		private async Task SavePetAsync(long characterID, int templateID, List<int> abilities, bool spawned)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null)
				{
					return;
				}
				if (!Server.Database.ServiceRegistry.TryGet<ICharacterPetService>(out var charPetService))
				{
					return;
				}

				if (templateID <= 0)
				{
					await Log.Warning("PetSystem", $"SavePetAsync skipped for CharID={characterID}: templateID={templateID} is invalid.");
					return;
				}

				// Fetch existing pet record to get ID and version for optimistic concurrency.
				DatabaseResult<CharacterPetData?> fetchResult = await charPetService.FetchAsync(characterID);
				long existingID = 0;
				long existingVersion = 0;
				if (fetchResult.IsSuccess && fetchResult.Data.HasValue)
				{
					existingID = fetchResult.Data.Value.ID;
					existingVersion = fetchResult.Data.Value.Version;
				}

				// Version is set to existingVersion + 1 for optimistic concurrency.
				// The DB layer should enforce WHERE version = existingVersion to prevent stale overwrites.
				CharacterPetData petData = new CharacterPetData(existingID, existingVersion + 1, characterID, templateID, abilities, spawned);
				DatabaseResult persistResult = await charPetService.PersistAsync(petData);
				if (!persistResult.IsSuccess)
				{
					await Log.Warning("PetSystem", $"SavePetAsync version conflict or DB error for CharID={characterID}: " +
						$"expectedVersion={existingVersion}, newVersion={existingVersion + 1}, " +
						$"error={persistResult.ErrorCode} - {persistResult.ErrorMessage}");
				}
			}
			catch (Exception ex)
			{
				await Log.Error("PetSystem", $"Error saving pet (CharID={characterID}): {ex}");
			}
		}

		/// <summary>
		/// Handles pet killed event, despawning the pet and broadcasting pet removal to the client.
		/// </summary>
		private void CharacterSystem_OnPetKilled(NetworkConnection conn, IPlayerCharacter character)
		{
			CharacterSystem_OnDespawnCharacter(conn, character);

			if (conn != null)
			{
				Server.NetworkWrapper.Broadcast(conn, new PetRemoveBroadcast(), true, Channel.Reliable);
			}
		}

		/// <summary>
		/// Handles pet summoning via ability, spawning the pet at a random position within the bounding box.
		/// </summary>
		private void AbilityObject_OnPetSummon(PetAbilityTemplate petAbilityTemplate, IPlayerCharacter caster)
		{
			if (petAbilityTemplate == null || caster == null)
			{
				return;
			}

			if (!caster.TryGet(out IPetController petController))
			{
				return;
			}

			if (petAbilityTemplate.PetPrefab == null)
			{
				return;
			}

			PhysicsScene physicsScene = caster.GameObject.scene.GetPhysicsScene();
			if (physicsScene == null)
			{
				return;
			}

			// Get a random point at the top of the bounding box
			Vector3 origin = new Vector3(DeterministicRNG.Shared.Range(-petAbilityTemplate.SpawnBoundingBox.x, petAbilityTemplate.SpawnBoundingBox.x),
									 petAbilityTemplate.SpawnBoundingBox.y,
									 DeterministicRNG.Shared.Range(-petAbilityTemplate.SpawnBoundingBox.z, petAbilityTemplate.SpawnBoundingBox.z));

			Vector3 spawnPosition = caster.Transform.position;

			// Add the spawner position
			origin += spawnPosition;

			// Constants.Layers.Ground is already a LayerMask bit mask, so it is passed as-is.
			// Shifting it again (1 << mask) selects the wrong layer: C# masks the shift count
			// to 5 bits, so e.g. a Ground mask of 64 becomes 1 << 0 — the Default layer.
			if (physicsScene.SphereCast(origin, petAbilityTemplate.SpawnDistance, Vector3.down, out RaycastHit hit, 20.0f, Constants.Layers.Ground, QueryTriggerInteraction.Ignore))
			{
				spawnPosition = hit.point;
			}

			NetworkObject nob = Server.NetworkWrapper.NetworkManager.GetPooledInstantiated(petAbilityTemplate.PetPrefab.PrefabId, petAbilityTemplate.PetPrefab.SpawnableCollectionId, ObjectPoolRetrieveOption.Unset, null, spawnPosition, caster.Transform.rotation, null, true);
			SpawnAndInitializePet(caster, petController, petAbilityTemplate, nob, spawnPosition, null, caster.Owner);
		}

		/// <summary>
		/// Merges the abilities restored from the database with the ones the summoning spell
		/// grants, into the list <see cref="Pet.OnStartServer"/> will learn from.
		/// </summary>
		/// <param name="petAbilityTemplate">The summoning spell's pet template.</param>
		/// <param name="persisted">Ability template IDs restored from the database, or null on a fresh summon.</param>
		/// <returns>A new list of ability template IDs, never null.</returns>
		private static List<int> BuildPetAbilityIDs(PetAbilityTemplate petAbilityTemplate, List<int> persisted)
		{
			List<int> result = persisted != null ? new List<int>(persisted) : new List<int>();

			if (petAbilityTemplate?.PetAbilities != null)
			{
				for (int i = 0; i < petAbilityTemplate.PetAbilities.Count; ++i)
				{
					AbilityTemplate template = petAbilityTemplate.PetAbilities[i];
					if (template == null || result.Contains(template.ID))
					{
						continue;
					}
					result.Add(template.ID);
				}
			}

			return result;
		}

		/// <summary>
		/// Subscribes the pet system to "my owner was attacked" for a defensive pet.
		/// </summary>
		/// <remarks>
		/// Idempotent: the handler is removed before being added, because a player can summon
		/// several pets over one session and each summon runs through here.
		/// </remarks>
		/// <param name="petController">The owner's pet controller.</param>
		private void SubscribeOwnerAttacked(IPetController petController)
		{
			petController.OnOwnerAttacked -= PetController_OnOwnerAttacked;
			petController.OnOwnerAttacked += PetController_OnOwnerAttacked;
		}

		/// <summary>
		/// Sends a defensive or aggressive pet at whoever just attacked its owner.
		/// </summary>
		/// <remarks>
		/// A passive pet ignores this entirely — that is what passive means. A pet already in
		/// combat is left alone rather than being yanked onto a new target every time its owner
		/// takes a hit.
		/// </remarks>
		/// <param name="petController">The pet controller whose owner was attacked.</param>
		/// <param name="attacker">The character that attacked the owner.</param>
		private void PetController_OnOwnerAttacked(IPetController petController, ICharacter attacker)
		{
			if (petController == null || attacker == null)
			{
				return;
			}

			Pet pet = petController.Pet;
			if (pet == null)
			{
				return;
			}

			if (pet.Stance == PetStance.Passive)
			{
				return;
			}

			IPlayerCharacter owner = petController.Character as IPlayerCharacter;
			if (owner == null)
			{
				return;
			}

			if (!pet.TryGet(out IAIController aiController))
			{
				return;
			}

			// Already fighting something — do not thrash the pet's target.
			if (aiController.CurrentState != null && aiController.CurrentState == aiController.AttackingState)
			{
				return;
			}

			if (!IsValidPetTarget(petController, owner, attacker))
			{
				return;
			}

			CommandPetAttack(pet, attacker);
		}

		// Uses ServerBehaviour.TryEnqueueAsyncWork
	}
}