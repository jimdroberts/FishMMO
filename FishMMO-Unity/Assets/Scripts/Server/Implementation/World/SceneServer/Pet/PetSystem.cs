using FishNet.Connection;
using FishNet.Object;
using FishNet.Transporting;
using FishNet.Utility.Performance;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
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

		/// <summary>
		/// Operation codes used by pet ingress guards.
		/// </summary>
		private enum IngressOperation : byte
		{
			Follow = 1,
			Stay = 2,
			Summon = 3,
			Release = 4,
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
			runtimeData.NextIngressSweepUtc = DateTime.UtcNow;

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
				runtimeData.NextAllowedIngressUtcByKey.Clear();
				runtimeData.IngressInFlightByKey.Clear();
				runtimeData.NextIngressSweepUtc = DateTime.UtcNow;
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

			guardKey = ((long)connectionId << 8) | (byte)operation;
			DateTime nowUtc = DateTime.UtcNow;

			if (runtimeData.NextAllowedIngressUtcByKey.TryGetValue(guardKey, out DateTime nextAllowedUtc) && nowUtc < nextAllowedUtc)
			{
				return false;
			}

			runtimeData.NextAllowedIngressUtcByKey[guardKey] = nowUtc.AddMilliseconds(ingressDebounceMilliseconds);
			return runtimeData.IngressInFlightByKey.TryAdd(guardKey, 0);
		}

		/// <summary>
		/// Releases an ingress in-flight guard key.
		/// </summary>
		private void EndIngressGuard(long guardKey)
		{
			if (Server.DataContainerRegistry.TryGet<IPetSystemRuntimeData>(out var runtimeData))
			{
				runtimeData.IngressInFlightByKey.TryRemove(guardKey, out _);
			}
		}

		/// <summary>
		/// Drains queued main-thread actions from the IPetSystemMainThreadQueueData container.
		/// </summary>
		private void DrainMainThreadQueue(bool drainAll)
		{
			if (Server?.DataContainerRegistry.TryGet<IPetSystemMainThreadQueueData>(out var queueData) == true)
			{
				if (drainAll)
				{
					queueData.Drain();
				}
				else
				{
					queueData.Drain(maxMainThreadActionsPerFrame);
				}
			}
		}

		/// <summary>
		/// Enqueues an action to be executed on the main thread.
		/// </summary>
		/// <param name="action">The action to enqueue.</param>
		private void EnqueueMainThread(Action action)
		{
			if (Server?.DataContainerRegistry.TryGet<IPetSystemMainThreadQueueData>(out var queueData) == true)
			{
				queueData.Enqueue(action);
			}
		}

		/// <summary>
		/// Drains the main-thread queue each frame.
		/// </summary>
		public override void OnLateUpdate(float deltaTime)
		{
			DrainMainThreadQueue(drainAll: false);

			if (!Server.DataContainerRegistry.TryGet<IPetSystemRuntimeData>(out var runtimeData))
			{
				return;
			}

			DateTime nowUtc = DateTime.UtcNow;
			if (nowUtc < runtimeData.NextIngressSweepUtc)
			{
				return;
			}

			runtimeData.NextIngressSweepUtc = nowUtc.AddSeconds(ingressSweepIntervalSeconds);
			DateTime staleBeforeUtc = nowUtc.AddSeconds(-ingressEntryTtlSeconds);
			int removed = 0;
			foreach (var kvp in runtimeData.NextAllowedIngressUtcByKey)
			{
				if (removed >= ingressSweepMaxRemovals)
				{
					break;
				}

				if (kvp.Value <= staleBeforeUtc && runtimeData.NextAllowedIngressUtcByKey.TryRemove(kvp.Key, out _))
				{
					runtimeData.IngressInFlightByKey.TryRemove(kvp.Key, out _);
					removed++;
				}
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

				if (petController.Pet.TryGet(out IAIController aiController))
				{
					aiController.Home = petController.Character.Transform.position;
					aiController.Target = petController.Character.Transform;
				}
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

				if (petController.Pet.TryGet(out IAIController aiController))
				{
					aiController.Home = petController.Pet.Transform.position;
					aiController.Target = null;
				}
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
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

			// Capture immutable data for the async path
			long characterID = petController.Character.ID;
			int templateID = petController.Pet.PetAbilityTemplate != null ? petController.Pet.PetAbilityTemplate.ID : 0;
			List<int> abilities = petController.Pet.Abilities != null ? new List<int>(petController.Pet.Abilities) : new List<int>();

			if (petController.Pet != null &&
				petController.Pet.NetworkObject.IsSpawned)
			{
				ServerManager.Despawn(petController.Pet.NetworkObject, DespawnType.Pool);
			}
			petController.Pet.PetOwner = null;
			petController.Pet = null;

			Server.NetworkWrapper.Broadcast(conn, new PetRemoveBroadcast(), true, Channel.Reliable);

			// Async DB save with spawned=false, keyed by characterID to serialize with summon ops
			TryEnqueueAsyncWork(() => SavePetAsync(characterID, templateID, abilities, false), characterID);
			}
			finally
			{
				EndIngressGuard(guardKey);
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

			TryEnqueueAsyncWork(() => LoadAndSpawnPetAsync(conn, character, scene, characterID), characterID);
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
				EnqueueMainThread(() =>
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

					Pet pet = nob.GetComponent<Pet>();
					if (pet == null)
					{
						return;
					}

					pet.PetOwner = character;
					pet.PetAbilityTemplate = petAbilityTemplate;
					pet.Abilities = petData.Abilities != null ? new List<int>(petData.Abilities) : new List<int>();
					petController.Pet = pet;

					if (pet.TryGet(out IAIController aiController))
					{
						// Initialize AI Controller
						aiController.Initialize(Vector3.zero);
						aiController.Target = character.Transform;
					}

					UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(pet.GameObject, character.GameObject.scene);

					// Ensure the game object is active, pooled objects are disabled
					pet.GameObject.SetActive(true);

					ServerManager.Spawn(pet.GameObject, character.NetworkObject.Owner, character.GameObject.scene);

					if (pet.TryGet(out IFactionController petFactionController))
					{
						if (character.TryGet(out IFactionController casterFactionController))
						{
							petFactionController.CopyFrom(casterFactionController);
						}
					}

					Server.NetworkWrapper.Broadcast(conn, new PetAddBroadcast() { ID = pet.ID }, true, Channel.Reliable);
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

			// Capture immutable data for the async path
			long characterID = character.ID;
			int templateID = petController.Pet?.PetAbilityTemplate != null ? petController.Pet.PetAbilityTemplate.ID : 0;
			List<int> abilities = petController.Pet?.Abilities != null ? new List<int>(petController.Pet.Abilities) : new List<int>();
			bool spawned = petController.Pet != null && currentHealth > 0.0f;

			if (petController.Pet != null &&
				petController.Pet.NetworkObject.IsSpawned)
			{
				ServerManager.Despawn(petController.Pet.NetworkObject, DespawnType.Pool);
			}

			// Async DB save, keyed by characterID to serialize with other pet ops for the same character
			TryEnqueueAsyncWork(() => SavePetAsync(characterID, templateID, abilities, spawned), characterID);
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
					return;
				}

				// Fetch existing pet record to get ID and version
				DatabaseResult<CharacterPetData?> fetchResult = await charPetService.FetchAsync(characterID);
				long existingID = 0;
				long existingVersion = 0;
				if (fetchResult.IsSuccess && fetchResult.Data.HasValue)
				{
					existingID = fetchResult.Data.Value.ID;
					existingVersion = fetchResult.Data.Value.Version;
				}

				CharacterPetData petData = new CharacterPetData(existingID, existingVersion + 1, characterID, templateID, abilities, spawned);
				await charPetService.PersistAsync(petData);
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
			Vector3 origin = new Vector3(UnityEngine.Random.Range(-petAbilityTemplate.SpawnBoundingBox.x, petAbilityTemplate.SpawnBoundingBox.x),
									 petAbilityTemplate.SpawnBoundingBox.y,
									 UnityEngine.Random.Range(-petAbilityTemplate.SpawnBoundingBox.z, petAbilityTemplate.SpawnBoundingBox.z));

			Vector3 spawnPosition = caster.Transform.position;

			// Add the spawner position
			origin += spawnPosition;

			if (physicsScene.SphereCast(origin, petAbilityTemplate.SpawnDistance, Vector3.down, out RaycastHit hit, 20.0f, 1 << Constants.Layers.Ground, QueryTriggerInteraction.Ignore))
			{
				spawnPosition = hit.point;
			}

			NetworkObject nob = Server.NetworkWrapper.NetworkManager.GetPooledInstantiated(petAbilityTemplate.PetPrefab.PrefabId, petAbilityTemplate.PetPrefab.SpawnableCollectionId, ObjectPoolRetrieveOption.Unset, null, spawnPosition, caster.Transform.rotation, null, true);
			Pet pet = nob.GetComponent<Pet>();
			if (pet == null)
			{
				//throw exception
				return;
			}
			pet.PetOwner = caster;
			pet.PetAbilityTemplate = petAbilityTemplate;
			petController.Pet = pet;

			if (pet.TryGet(out IAIController aiController))
			{
				// Initialize AI Controller
				aiController.Initialize(spawnPosition);
				aiController.Target = caster.Transform;
			}

			UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(nob.gameObject, caster.GameObject.scene);

			// Ensure the game object is active, pooled objects are disabled
			pet.GameObject.SetActive(true);

			ServerManager.Spawn(nob.gameObject, caster.NetworkObject.Owner, caster.GameObject.scene);

			if (pet.TryGet(out IFactionController petFactionController))
			{
				if (caster.TryGet(out IFactionController casterFactionController))
				{
					petFactionController.CopyFrom(casterFactionController);
				}
			}

			Server.NetworkWrapper.Broadcast(caster.Owner, new PetAddBroadcast() { ID = pet.ID }, true, Channel.Reliable);
		}

		/// <summary>
		/// Enqueues an async work item to the centralized async worker for controlled execution.
		/// Returns false when the queue is unavailable or rejected due to backpressure.
		/// </summary>
		/// <param name="work">Asynchronous work delegate to queue.</param>
		/// <param name="entityKey">Optional entity key for ordered execution.</param>
		/// <param name="callerName">Optional caller name used for diagnostics.</param>
		/// <returns>True if work was accepted by the queue; otherwise false.</returns>
		private bool TryEnqueueAsyncWork(Func<Task> work, long entityKey = 0, [CallerMemberName] string callerName = null)
		{
			if (Server?.DataContainerRegistry.TryGet<IAsyncWorkerData>(out var asyncWorker) == true)
			{
				if (entityKey != 0)
				{
					if (asyncWorker.Enqueue(work, entityKey, callerName))
					{
						return true;
					}

					Log.Warning("PetSystem", $"{callerName}: Async worker queue rejected work (entityKey={entityKey}).");
					return false;
				}
				else
				{
					if (asyncWorker.Enqueue(work, callerName))
					{
						return true;
					}

					Log.Warning("PetSystem", $"{callerName}: Async worker queue rejected work.");
					return false;
				}
			}

			Log.Warning("PetSystem", $"{callerName}: IAsyncWorkerData unavailable; work was not enqueued.");
			return false;
		}
	}
}