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
			AttackPriority = 8,
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
			Server.NetworkWrapper.RegisterBroadcast<PetAttackPriorityBroadcast>(OnPetAttackPriorityBroadcastReceived, true);

			// Ability events
			AbilityObject.OnPetSummon += AbilityObject_OnPetSummon;

			// Character system events
			if (Server.BehaviourRegistry.TryGet(out ICharacterSystem<NetworkConnection, Scene> characterSystem))
			{
				characterSystem.OnSpawnCharacter += CharacterSystem_OnSpawnCharacter;
				characterSystem.OnDespawnCharacter += CharacterSystem_OnDespawnCharacter;
				characterSystem.OnPetKilled += CharacterSystem_OnPetKilled;
			}

			ICharacterDamageController.OnKilled -= DamageController_OnKilled;
			ICharacterDamageController.OnKilled += DamageController_OnKilled;

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
			Server.NetworkWrapper.UnregisterBroadcast<PetAttackPriorityBroadcast>(OnPetAttackPriorityBroadcastReceived);

			// Ability events
			AbilityObject.OnPetSummon -= AbilityObject_OnPetSummon;

			// Character system events
			if (Server.BehaviourRegistry.TryGet(out ICharacterSystem<NetworkConnection, Scene> characterSystem))
			{
				characterSystem.OnSpawnCharacter -= CharacterSystem_OnSpawnCharacter;
				characterSystem.OnDespawnCharacter -= CharacterSystem_OnDespawnCharacter;
				characterSystem.OnPetKilled -= CharacterSystem_OnPetKilled;
			}

			ICharacterDamageController.OnKilled -= DamageController_OnKilled;

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
				if (petController == null || petController.Pet == null || petController.Character == null)
				{
					// no pet exists
					return;
				}

				if (!petController.Pet.TryGet(out IAIController aiController))
				{
					return;
				}

				/* AIController.WarpTo, not NavMeshAgent.Warp.
				 *
				 * The raw agent call does three things wrong here. It dereferences Agent, which
				 * is null on a pet whose brain never initialised; it hands Unity a point that
				 * has not been sampled onto the NavMesh, so a summon issued while the owner
				 * stands on a gap in the mesh silently fails; and it leaves the agent's existing
				 * path intact, so the pet arrives beside its owner and immediately walks back to
				 * wherever it was heading. WarpTo samples, warps and resets the movement state. */
				Vector3 ownerPosition = petController.Character.Transform.position;
				aiController.WarpTo(ownerPosition);

				/* A pet told to Stay holds the spot it was standing in, and that spot is its
				 * leash anchor. Summoning it moves the pet without moving the anchor, which
				 * would leave it heeling to a position it is no longer at. Re-anchor. */
				if (petController.Pet.MovementOrder == PetMovementOrder.Stay)
				{
					aiController.Home = ownerPosition;
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

				DismissPet(player, petController, conn);
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}

		/// <summary>
		/// Dismisses a player's pet: persists the dismissal, despawns the body, unlinks both
		/// sides, tells the owner and fires the dismiss triggers. The one path every dismissal
		/// takes, whether the player asked for it or death forced it.
		/// </summary>
		/// <param name="owner">The pet's owner.</param>
		/// <param name="petController">The owner's pet controller, holding a live pet.</param>
		/// <param name="conn">The owner's connection, or null when there is none to tell.</param>
		private void DismissPet(IPlayerCharacter owner, IPetController petController, NetworkConnection conn)
		{
			Pet pet = petController.Pet;
			if (pet == null)
			{
				return;
			}

			// Record the dismissal before the reference is dropped. Snapshots the pet's
			// abilities on the way past, so anything granted at summon time from the
			// PetAbilityTemplate survives rather than being silently lost at the next login.
			PersistPetDismissed(owner, pet);

			if (pet.NetworkObject != null &&
				pet.NetworkObject.IsSpawned)
			{
				ServerManager.Despawn(pet.NetworkObject, DespawnType.Pool);
			}
			pet.PetOwner = null;
			petController.Pet = null;
			petController.OnOwnerAttacked -= PetController_OnOwnerAttacked;

			if (conn != null)
			{
				Server.NetworkWrapper.Broadcast(conn, new PetRemoveBroadcast(), true, Channel.Reliable);
			}

			// Server-side dismiss triggers — see InvokePetTriggers for why the client-side
			// raise in PetController is not enough on its own.
			InvokePetTriggers(petController.Character, petController.OnPetDismissTriggers, null);
		}

		/// <summary>
		/// Dismisses the pet of a player who has just died.
		/// </summary>
		/// <remarks>
		/// Death is a full reset for the character, and a pet is part of that: without this the
		/// summon outlived its owner, leashed to a corpse and still fighting whatever had killed
		/// it. Routed through <see cref="DismissPet"/> so it persists and notifies exactly as a
		/// voluntary release does. A pet's own death is <see cref="CharacterSystem_OnPetKilled"/>'s
		/// concern, and NPC deaths are not this handler's at all.
		/// </remarks>
		private void DamageController_OnKilled(ICharacter killer, ICharacter victim)
		{
			IPlayerCharacter owner = victim as IPlayerCharacter;
			if (owner == null ||
				!owner.TryGet(out IPetController petController) ||
				petController.Pet == null)
			{
				return;
			}

			NetworkConnection conn = owner.Owner != null && owner.Owner.IsActive ? owner.Owner : null;
			DismissPet(owner, petController, conn);
		}

		/// <summary>
		/// Handles pet attack broadcast: sends the pet at the owner's target.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The pet goes at the first of three choices that resolves to a valid target, tried in
		/// the order the owner set (<see cref="PetAttackPriority"/>; by default pinned, then
		/// current, then highest threat). The pinned and hovered ids arrive in the click, the
		/// server's own copy of the reported frame backs the "current" step (the click can beat
		/// the report by up to an interval), and the highest-threat step is resolved here from
		/// the threat tables. There is no raycast down the camera: that sent the pet at whatever
		/// was under the crosshair, which is not a target the player chose.
		/// </para>
		/// <para>
		/// Every route is a claim until verified. A frame id must resolve to a spawned character
		/// in the owner's own scene within <see cref="TargetController.MAX_TARGET_DISTANCE"/> of
		/// the owner, and whatever any route produces is then re-validated: alive, in the pet's
		/// scene, not the owner, not the pet, and hostile by faction. A client can therefore point
		/// at, or name, only something it could actually reach.
		/// </para>
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

				/* The owner's attack priority decides the order the three choices are tried in.
				 * Each step is a claim until verified: a frame id must resolve to a spawned
				 * character in the owner's scene within targeting range, the threat step is
				 * range-bound the same way, and whatever a step produces must pass the
				 * pet-target rules. The first step that yields a valid target wins; a step that
				 * yields nothing, or an invalid something, hands over to the next. */
				PetAttackTarget[] order = new PetAttackTarget[PetAttackPriority.StepCount];
				if (!PetAttackPriority.TryDecode(petController.AttackPriority, order))
				{
					PetAttackPriority.TryDecode(PetAttackPriority.Default, order);
				}

				ICharacter chosen = null;
				for (int i = 0; i < order.Length && chosen == null; ++i)
				{
					ICharacter candidate = null;
					switch (order[i])
					{
						case PetAttackTarget.Pinned:
							TryResolveFrameTarget(player, msg.PinnedTargetObjectID, out candidate);
							break;

						case PetAttackTarget.Current:
							/* What the click named, else what the server already holds from the
							 * frame report — which the click can beat by up to a report interval. */
							if (!TryResolveFrameTarget(player, msg.HoveredTargetObjectID, out candidate) &&
								targetController.HasClientSelectedTarget)
							{
								TryResolveFrameTarget(player, targetController.ClientSelectedTargetObjectId, out candidate);
							}
							break;

						case PetAttackTarget.HighestThreat:
							/* Whatever hates the owner most. An NPC's threat toward the owner is
							 * built from the owner's hits on it and, because a pet's hits are
							 * credited to its owner, from the pet's hits too — so this is the thing
							 * the owner and pet have been attacking. */
							AggressionDispatcher.TryFindHighestThreatAgainst(player,
								c => IsWithinTargetingRange(player, c) && IsValidPetTarget(petController, player, c),
								out candidate);
							break;
					}

					if (candidate != null && IsValidPetTarget(petController, player, candidate))
					{
						chosen = candidate;
					}
				}

				if (chosen != null)
				{
					CommandPetAttack(petController.Pet, chosen);
				}
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}

		/// <summary>
		/// Handles an attack-priority change request from the owner.
		/// </summary>
		/// <remarks>
		/// Session state on the owner's controller, like the stance: the client keeps the
		/// preference in its own settings and replays it on every summon, so nothing here touches
		/// the database. Anything that is not a permutation of the three steps is refused and the
		/// order in force is confirmed back, so a bad request cannot leave the panel out of step.
		/// </remarks>
		private void OnPetAttackPriorityBroadcastReceived(NetworkConnection conn, PetAttackPriorityBroadcast msg, Channel channel)
		{
			if (conn == null || conn.FirstObject == null)
			{
				return;
			}

			IPlayerCharacter player = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (player == null || !CharacterStateValidation.CanAct(player))
				return;

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.AttackPriority, out long guardKey))
			{
				return;
			}
			try
			{
				IPetController petController = conn.FirstObject.GetComponent<IPetController>();
				if (petController == null)
				{
					return;
				}

				if (PetAttackPriority.IsValid(msg.Priority))
				{
					petController.AttackPriority = msg.Priority;
					if (petController.Pet != null)
					{
						petController.Pet.AttackPriority = msg.Priority;
					}
				}

				Server.NetworkWrapper.Broadcast(conn, new PetAttackPriorityBroadcast() { Priority = petController.AttackPriority }, true, Channel.Reliable);
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
		/// <summary>
		/// Resolves a claimed target-frame id to a character the owner could actually be
		/// targeting: spawned, in the owner's own scene, and within targeting range of the owner.
		/// </summary>
		/// <remarks>
		/// The range check is the part the frame report deliberately leaves to the point of use:
		/// the report is stored without one so a moving value does not flap, and this is the point
		/// of use. Distance is measured from the owner rather than the pet — the frame is the
		/// owner's, and it is the owner who must be able to see what it names.
		/// </remarks>
		/// <param name="owner">The pet's owner.</param>
		/// <param name="objectId">The claimed NetworkObject id, or 0 for none.</param>
		/// <param name="target">The resolved character.</param>
		/// <returns>True when the id names a character the owner could be targeting.</returns>
		private static bool TryResolveFrameTarget(IPlayerCharacter owner, int objectId, out ICharacter target)
		{
			target = null;
			if (objectId == 0 ||
				owner == null ||
				owner.NetworkObject == null ||
				owner.NetworkObject.NetworkManager == null ||
				!owner.NetworkObject.NetworkManager.ServerManager.Objects.Spawned.TryGetValue(objectId, out NetworkObject targetObject) ||
				targetObject == null ||
				targetObject.gameObject.scene != owner.GameObject.scene)
			{
				return false;
			}

			ICharacter character = targetObject.GetComponent<ICharacter>();
			if (character == null || character.Transform == null || owner.Transform == null)
			{
				return false;
			}

			if (!IsWithinTargetingRange(owner, character))
			{
				return false;
			}

			target = character;
			return true;
		}

		/// <summary>
		/// True when <paramref name="candidate"/> is within <see cref="TargetController.MAX_TARGET_DISTANCE"/>
		/// of <paramref name="owner"/>: the bound on how far an owner can send its pet.
		/// </summary>
		private static bool IsWithinTargetingRange(IPlayerCharacter owner, ICharacter candidate)
		{
			if (owner == null || candidate == null || owner.Transform == null || candidate.Transform == null)
			{
				return false;
			}
			float maxDistance = TargetController.MAX_TARGET_DISTANCE;
			return (candidate.Transform.position - owner.Transform.position).sqrMagnitude <= maxDistance * maxDistance;
		}

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

			// Same world, same instance. SceneObject IDs and character references are process-wide
			// while scenes are stacked per instance, so without this a pet could be pointed at
			// something in another copy of the same dungeon.
			if (petController.Pet != null &&
				petController.Pet.GameObject.scene != target.GameObject.scene)
			{
				return false;
			}

			/* Faction gate: the pet copies its owner's factions at summon time, so asking the
			 * pet's own faction controller gives the same answer the AI would give itself.
			 *
			 * Fails CLOSED. The previous form was one long && chain that returned false only
			 * when every link held, so a target — or a pet — with no faction controller at all
			 * short-circuited the whole test and fell through to "valid". A missing faction
			 * controller is not permission to attack. */
			if (petController.Pet == null ||
				!petController.Pet.TryGet(out IFactionController petFaction) ||
				!target.TryGet(out IFactionController targetFaction) ||
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
			if (character == null || conn == null)
			{
				return;
			}

			/* IsValid, not a null test. Scene is a struct, so `scene == null` bound to the lifted
			 * == the struct's own operator generates and answered false unconditionally — the
			 * guard could never fire. An unloaded or never-loaded scene handle is what actually
			 * has to be rejected here. */
			if (!scene.IsValid())
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
		/// <param name="persistedAttributes">Attribute values restored from the database; null when summoned fresh.</param>
		/// <param name="persistedBuffs">Buffs restored from the database; null when summoned fresh.</param>
		/// <returns>True if the pet was successfully spawned; false if the pooled object had no Pet component.</returns>
		private bool SpawnAndInitializePet(
			IPlayerCharacter owner,
			IPetController petController,
			PetAbilityTemplate petAbilityTemplate,
			NetworkObject nob,
			Vector3 aiInitPosition,
			List<int> abilities,
			NetworkConnection broadcastTarget,
			List<PetPersistedAttribute> persistedAttributes = null,
			List<PetPersistedBuff> persistedBuffs = null)
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
			petController.AttackPriority = PetAttackPriority.Normalize(petController.AttackPriority);
			pet.AttackPriority = petController.AttackPriority;

			/* Build the ability list before the spawn: Pet.OnStartServer teaches whatever is in
			 * PetAbilityIDs, and OnStartServer runs inside ServerManager.Spawn below. */
			pet.PetAbilityIDs = BuildPetAbilityIDs(petAbilityTemplate, abilities);

			/* Restored health and buffs, staged for the same reason and applied at the same
			 * moment. Both are serialised into the spawn payload by their own controllers, and
			 * FishNet writes that payload after the start callbacks have run — so applying them
			 * from Pet.OnStartServer is what puts a wounded pet on its owner's screen at the
			 * health it actually had, rather than at full and then snapping down. Null on a fresh
			 * summon: a newly conjured pet arrives whole and unbuffed. */
			pet.PersistedAttributes = persistedAttributes;
			pet.PersistedBuffs = persistedBuffs;

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

			/* Factions BEFORE the spawn, not after.
			 *
			 * FactionController.WritePayload serialises the faction table into the spawn payload,
			 * and CopyFrom does not mark anything dirty — the per-tick flush only ever sends
			 * factions marked dirty, and MarkAllFactionsDirty runs once, in OnStartNetwork.
			 * Copying after ServerManager.Spawn therefore shipped every observer the PREFAB's
			 * factions and never corrected them, so a summoned pet read as hostile (or neutral)
			 * to its own owner's client for its entire life. */
			if (pet.TryGet(out IFactionController petFactionController) &&
				owner.TryGet(out IFactionController ownerFactionController))
			{
				petFactionController.CopyFrom(ownerFactionController);
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

			// Wire the defensive-stance hook so the pet answers when its owner is attacked.
			SubscribeOwnerAttacked(petController);

			Server.NetworkWrapper.Broadcast(broadcastTarget, new PetAddBroadcast()
			{
				ID = pet.ID,
				Stance = pet.Stance,
				AttackPriority = pet.AttackPriority,
				MovementOrder = pet.MovementOrder,
			}, true, Channel.Reliable);

			// Increment achievement for summoning a pet
			if (PetSummonAchievementTemplate != null &&
				owner.TryGet(out IAchievementController achievementController))
			{
				achievementController.Increment(PetSummonAchievementTemplate, 1);
			}

			/* Fire the summon triggers HERE, on the server.
			 *
			 * PetController raises them too, but only from inside its `#if !UNITY_SERVER` arm — so
			 * they ran exclusively on the owning client, and every action a designer can put in
			 * one of those lists opens with BaseAction.IsServer and returns immediately off the
			 * server. The whole OnPetSummonTriggers / OnPetDismissTriggers feature was therefore
			 * inert: the lists could be authored on the prefab and nothing they contained would
			 * ever execute. The client-side invocation is left alone, so purely presentational
			 * actions still fire there. */
			InvokePetTriggers(owner, petController.OnPetSummonTriggers, pet);

			return true;
		}

		/// <summary>
		/// Runs one of the pet controller's ECA trigger lists on the server.
		/// </summary>
		/// <param name="owner">The pet's owner, and the event's initiator.</param>
		/// <param name="triggers">The list to fire. A null or empty list is a no-op.</param>
		/// <param name="pet">The pet involved, or null for a dismissal.</param>
		private static void InvokePetTriggers(ICharacter owner, List<Trigger> triggers, Pet pet)
		{
			if (owner == null || triggers == null || triggers.Count < 1)
			{
				return;
			}
			owner.Invoke(triggers, new PetEventData(owner, pet));
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

				/* Attributes and buffs are only accepted when their version matches the pet
				 * row's. All three tables are stamped together by the character save, so a row
				 * carrying an older version belongs to a pet that has since been dismissed — and
				 * a previous pet may well have known an attribute or carried a buff this one does
				 * not. Matching on the version is what keeps a hunter's new wolf from inheriting
				 * the mana pool of the elemental it replaced. */
				List<PetPersistedAttribute> persistedAttributes = await FetchPersistedPetAttributesAsync(characterID, petData.Version);
				List<PetPersistedBuff> persistedBuffs = await FetchPersistedPetBuffsAsync(characterID, petData.Version);

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

					/* The character object is pooled. Between the fetch above and this drain it
					 * can be despawned, returned to the pool and handed to an entirely different
					 * player — at which point every check above still passes, because it is the
					 * same C# object and it IS spawned, just not for us. Comparing the ID is what
					 * tells the two apart, and getting it wrong hands one player another
					 * player's pet. */
					if (character.ID != characterID)
					{
						return;
					}

					// Same reasoning for the connection: FirstObject must still be this character.
					if (conn.FirstObject != character.NetworkObject)
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
					SpawnAndInitializePet(character, petController, petAbilityTemplate, nob, spawnPosition, abilities, conn,
						persistedAttributes, persistedBuffs);
				});
			}
			catch (Exception ex)
			{
				await Log.Error("PetSystem", $"Error loading/spawning pet (CharID={characterID}): {ex}");
			}
		}

		/// <summary>
		/// Reads the pet's saved attribute values, keeping only the rows written alongside the
		/// pet row being restored.
		/// </summary>
		/// <param name="characterID">The owning character.</param>
		/// <param name="petVersion">The version stamped on the pet row.</param>
		/// <returns>Attribute values to stage onto the pet, or null when there are none.</returns>
		private async Task<List<PetPersistedAttribute>> FetchPersistedPetAttributesAsync(long characterID, long petVersion)
		{
			if (!Server.Database.ServiceRegistry.TryGet<ICharacterPetAttributeService>(out var petAttributeService))
			{
				return null;
			}

			DatabaseResult<IReadOnlyList<CharacterPetAttributeData>> result = await petAttributeService.FetchAsync(characterID);
			if (!result.IsSuccess || result.Data == null || result.Data.Count < 1)
			{
				return null;
			}

			List<PetPersistedAttribute> attributes = new List<PetPersistedAttribute>(result.Data.Count);
			for (int i = 0; i < result.Data.Count; ++i)
			{
				CharacterPetAttributeData row = result.Data[i];
				if (row.Version != petVersion)
				{
					continue;
				}

				attributes.Add(new PetPersistedAttribute()
				{
					TemplateID = row.TemplateID,
					Value = row.Value,
					CurrentValue = row.CurrentValue,
				});
			}

			return attributes.Count > 0 ? attributes : null;
		}

		/// <summary>
		/// Reads the pet's saved buffs, keeping only the rows written alongside the pet row being
		/// restored.
		/// </summary>
		/// <param name="characterID">The owning character.</param>
		/// <param name="petVersion">The version stamped on the pet row.</param>
		/// <returns>Buffs to stage onto the pet, or null when there are none.</returns>
		private async Task<List<PetPersistedBuff>> FetchPersistedPetBuffsAsync(long characterID, long petVersion)
		{
			if (!Server.Database.ServiceRegistry.TryGet<ICharacterPetBuffService>(out var petBuffService))
			{
				return null;
			}

			DatabaseResult<IReadOnlyList<CharacterPetBuffData>> result = await petBuffService.FetchAsync(characterID);
			if (!result.IsSuccess || result.Data == null || result.Data.Count < 1)
			{
				return null;
			}

			List<PetPersistedBuff> buffs = new List<PetPersistedBuff>(result.Data.Count);
			for (int i = 0; i < result.Data.Count; ++i)
			{
				CharacterPetBuffData row = result.Data[i];
				if (row.Version != petVersion)
				{
					continue;
				}

				buffs.Add(new PetPersistedBuff()
				{
					TemplateID = row.TemplateID,
					RemainingTime = row.RemainingTime,
					TickTime = row.TickTime,
					Stacks = row.Stacks,
					TickCount = row.TickCount,
				});
			}

			return buffs.Count > 0 ? buffs : null;
		}

		/// <summary>
		/// Removes the character's pet from the world when the character leaves it.
		/// </summary>
		/// <remarks>
		/// Deliberately writes nothing. A live pet's state — its row, its attributes and its
		/// buffs — is snapshotted by the character save that precedes this event, and written
		/// before the character's session claim is released; see
		/// <c>CharacterSystem.AppendPetData</c>. Persisting again from here would be a second,
		/// unordered write racing the one that matters, which is exactly what let a player zone
		/// between scene servers and arrive without the pet they had out.
		/// </remarks>
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

			Pet pet = petController.Pet;
			if (pet == null)
			{
				petController.OnOwnerAttacked -= PetController_OnOwnerAttacked;
				return;
			}

			if (pet.NetworkObject != null && pet.NetworkObject.IsSpawned)
			{
				ServerManager.Despawn(pet.NetworkObject, DespawnType.Pool);
			}

			/* Drop the reference. Leaving it set meant that after a pet died, the owner's
			 * controller still pointed at a despawned, pooled object — so a Summon or Follow
			 * command would happily warp and re-task a pet that no longer existed. */
			pet.PetOwner = null;
			petController.Pet = null;
			petController.OnOwnerAttacked -= PetController_OnOwnerAttacked;
		}

		/// <summary>
		/// Records that a pet is no longer out, so it is not restored on the owner's next login.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Only the dismissal paths need this. Everything about a <em>live</em> pet — its row, its
		/// attributes and its buffs — is snapshotted and written by the character save itself
		/// (<c>CharacterSystem.AppendPetData</c>), which is what puts the pet's state in the
		/// database before the character's session claim is released and therefore before the
		/// destination scene server can read it. A pet released or killed mid-session has no
		/// character save to ride along with, and leaving the row alone would mean a pet that
		/// died at noon was waiting, alive, at the next login.
		/// </para>
		/// <para>
		/// Versions come from the owner's own counter for the reason described on
		/// <c>AppendPetData</c>: a pooled pet has no durable identity to hang a version stream on.
		/// Bumping it here costs nothing — the counter only has to increase, not be contiguous —
		/// and keeps this write ordered against the character saves that share it.
		/// </para>
		/// </remarks>
		/// <param name="owner">The pet's owner.</param>
		/// <param name="pet">The pet being dismissed.</param>
		private void PersistPetDismissed(IPlayerCharacter owner, Pet pet)
		{
			if (owner == null || pet == null || Server?.Database?.ServiceRegistry == null)
			{
				return;
			}

			int templateID = pet.PetAbilityTemplate != null ? pet.PetAbilityTemplate.ID : 0;
			if (templateID <= 0)
			{
				Log.Warning("PetSystem", $"Pet for character {owner.ID} was dismissed with no resolvable ability template; the database still lists it as out.");
				return;
			}

			// Abilities granted at summon time live only on the controller until this runs.
			pet.CaptureKnownAbilities();

			long characterID = owner.ID;
			long version = ++owner.Version;
			List<int> abilities = pet.PetAbilityIDs != null ? new List<int>(pet.PetAbilityIDs) : new List<int>();

			// Keyed by characterID to serialize with any other pet op for the same character.
			EnqueuePersistence(() => SavePetDismissedAsync(characterID, version, templateID, abilities), characterID);
		}

		/// <summary>
		/// Writes the pet row with <c>spawned = false</c>.
		/// </summary>
		/// <param name="characterID">Character identifier that owns the pet.</param>
		/// <param name="version">Version to stamp the row with.</param>
		/// <param name="templateID">Pet template identifier.</param>
		/// <param name="abilities">Pet ability template identifiers.</param>
		/// <returns>Asynchronous persistence task.</returns>
		private async Task SavePetDismissedAsync(long characterID, long version, int templateID, List<int> abilities)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterPetService>(out var charPetService))
				{
					return;
				}

				CharacterPetData petData = new CharacterPetData(0, version, characterID, templateID, abilities, false);
				DatabaseResult persistResult = await charPetService.PersistAsync(petData);
				if (!persistResult.IsSuccess)
				{
					await Log.Warning("PetSystem", $"SavePetDismissedAsync failed for CharID={characterID} at version {version}: " +
						$"{persistResult.ErrorCode} - {persistResult.ErrorMessage}");
				}
			}
			catch (Exception ex)
			{
				await Log.Error("PetSystem", $"Error recording pet dismissal (CharID={characterID}): {ex}");
			}
		}

		/// <summary>
		/// Handles pet killed event, despawning the pet and broadcasting pet removal to the client.
		/// </summary>
		private void CharacterSystem_OnPetKilled(NetworkConnection conn, IPlayerCharacter character)
		{
			/* Read the trigger list BEFORE the despawn path clears the controller's pet reference,
			 * so a dismiss caused by death fires the same triggers a voluntary release does. */
			List<Trigger> dismissTriggers = null;
			if (character != null && character.TryGet(out IPetController petController))
			{
				dismissTriggers = petController.OnPetDismissTriggers;

				/* A death is a dismissal the character save will never hear about. The owner is
				 * still logged in, so no save is coming to write spawned = false for them, and
				 * without this the row keeps saying the pet is out — handing the player their
				 * dead pet back, alive and whole, at the next login. */
				PersistPetDismissed(character, petController.Pet);
			}

			CharacterSystem_OnDespawnCharacter(conn, character);

			if (conn != null)
			{
				Server.NetworkWrapper.Broadcast(conn, new PetRemoveBroadcast(), true, Channel.Reliable);
			}

			InvokePetTriggers(character, dismissTriggers, null);
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

			/* A dead pet does not come to anyone's aid. The controller's reference is cleared by
			 * the death path, but that runs from the kill handler and this event is raised from
			 * the damage handler — the blow that kills the pet and the next blow that lands on its
			 * owner can be in the same frame, so the reference can still be live here. Commanding
			 * a corpse pushes it into its attacking state, which then drives a NavMeshAgent on an
			 * object that is about to be despawned. */
			if (!pet.TryGet(out ICharacterDamageController petDamageController) ||
				!petDamageController.IsAlive)
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