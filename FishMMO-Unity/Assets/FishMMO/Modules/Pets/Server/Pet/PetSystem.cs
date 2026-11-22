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
using FishMMO.Server.Implementation.World.SceneServer.AI;

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
		[TemplateOf("AchievementTemplate")]
		public CachedScriptableObject PetSummonAchievementTemplate;

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
			AIController.OnPetTargetChanged += OnPetTargetChanged;

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
			AIController.OnPetTargetChanged -= OnPetTargetChanged;

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
			if (!TryBeginPlayerRequest(conn, out _))
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
				if (petController == null || petController.Pets.Count == 0)
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
			if (!TryBeginPlayerRequest(conn, out _))
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
				if (petController == null || petController.Pets.Count == 0)
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
		/// Applies a movement order to every pet and confirms it to the owner.
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
			petController.MovementOrder = order;

			for (int i = 0; i < petController.Pets.Count; ++i)
			{
				Pet pet = petController.Pets[i];
				if (pet == null)
				{
					continue;
				}

				pet.MovementOrder = order;

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
			}

			Server.NetworkWrapper.Broadcast(conn, new PetMovementOrderBroadcast() { MovementOrder = order }, true, Channel.Reliable);
		}

		/// <summary>
		/// Handles pet summon broadcast, warping pet to the character's position.
		/// </summary>
		private void OnPetSummonBroadcastReceived(NetworkConnection conn, PetSummonBroadcast msg, Channel channel)
		{
			if (!TryBeginPlayerRequest(conn, out _))
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
				if (petController == null || petController.Pets.Count == 0 || petController.Character == null)
				{
					// no pet exists
					return;
				}

				Vector3 ownerPosition = petController.Character.Transform.position;
				for (int i = 0; i < petController.Pets.Count; ++i)
				{
					WarpPetToOwner(petController.Pets[i], ownerPosition);
				}
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}

		/// <summary>
		/// Brings one pet to its owner's side.
		/// </summary>
		/// <param name="pet">The pet.</param>
		/// <param name="ownerPosition">Where its owner stands.</param>
		private static void WarpPetToOwner(Pet pet, Vector3 ownerPosition)
		{
			if (pet == null || !pet.TryGet(out IAIController aiController))
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
			aiController.WarpTo(ownerPosition);

			/* A pet told to Stay holds the spot it was standing in, and that spot is its
			 * leash anchor. Summoning it moves the pet without moving the anchor, which
			 * would leave it heeling to a position it is no longer at. Re-anchor. */
			if (pet.MovementOrder == PetMovementOrder.Stay)
			{
				aiController.Home = ownerPosition;
			}
		}

		/// <summary>
		/// Handles pet release broadcast, saving pet state and despawning the pet object.
		/// </summary>
		private void OnPetReleaseBroadcastReceived(NetworkConnection conn, PetReleaseBroadcast msg, Channel channel)
		{
			if (!TryBeginPlayerRequest(conn, out PlayerRequestContext request))
			{
				return;
			}
			IPlayerCharacter player = request.Character;

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
				if (petController == null || petController.Pets.Count == 0)
				{
					// no pet exists
					return;
				}

				DismissAllPets(player, petController, conn);
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
		/// <param name="petController">The owner's pet controller, listing the pet.</param>
		/// <param name="pet">The pet to dismiss.</param>
		/// <param name="conn">The owner's connection, or null when there is none to tell.</param>
		private void DismissPet(IPlayerCharacter owner, IPetController petController, Pet pet, NetworkConnection conn)
		{
			if (ReferenceEquals(pet, null))
			{
				return;
			}
			if (pet == null)
			{
				// Destroyed under us: nothing to persist or despawn, but it must not hold a slot.
				petController.RemovePet(pet);
				lastAnnouncedTarget.Remove(pet);
				return;
			}

			// Record the dismissal before the reference is dropped. Snapshots the pet's
			// abilities on the way past, so anything granted at summon time from the
			// PetAbilityTemplate survives rather than being silently lost at the next login.
			PersistPetDismissed(owner, pet);

			// Out of the world scene, not in place — see DespawnPet.
			long petID = pet.ID;
			DespawnPet(pet.NetworkObject);
			ForgetPet(petController, pet);

			if (conn != null)
			{
				Server.NetworkWrapper.Broadcast(conn, new PetRemoveBroadcast() { ID = petID }, true, Channel.Reliable);
			}

			// Server-side dismiss triggers — see InvokePetTriggers for why the client-side
			// raise in PetController is not enough on its own.
			InvokePetTriggers(petController.Character, petController.OnPetDismissTriggers, null);
		}

		/// <summary>
		/// Dismisses every pet a player has out; see <see cref="DismissPet"/>.
		/// </summary>
		private void DismissAllPets(IPlayerCharacter owner, IPetController petController, NetworkConnection conn)
		{
			for (int i = petController.Pets.Count - 1; i >= 0; --i)
			{
				if (i < petController.Pets.Count)
				{
					DismissPet(owner, petController, petController.Pets[i], conn);
				}
			}
		}

		/// <summary>
		/// Unlinks a pet from its owner on the server, once it has left the world.
		/// </summary>
		private void ForgetPet(IPetController petController, Pet pet)
		{
			pet.PetOwner = null;
			petController.RemovePet(pet);
			lastAnnouncedTarget.Remove(pet);
			if (petController.Pets.Count == 0)
			{
				petController.OnOwnerAttacked -= PetController_OnOwnerAttacked;
				petController.OnPetSlotsReduced -= PetController_OnPetSlotsReduced;
			}
		}

		/// <summary>
		/// The owner's Pet Slots attribute fell under a slot a pet holds (an item taken off, a buff ended): that pet is
		/// dismissed, as a restore would leave it in the database. The newest goes first, so the pets kept are the ones the
		/// player has had longest.
		/// </summary>
		private void PetController_OnPetSlotsReduced(IPetController petController)
		{
			if (petController == null || !(petController.Character is IPlayerCharacter owner))
			{
				return;
			}

			NetworkConnection conn = owner.Owner != null && owner.Owner.IsActive ? owner.Owner : null;
			int slots = petController.PetSlots;
			for (int i = petController.Pets.Count - 1; i >= 0; --i)
			{
				if (i < petController.Pets.Count)
				{
					Pet pet = petController.Pets[i];
					if (ReferenceEquals(pet, null) || pet == null || pet.Slot >= slots)
					{
						DismissPet(owner, petController, pet, conn);
					}
				}
			}
		}

		/// <summary>
		/// Dismisses the pets of a player who has just died.
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
				petController.Pets.Count == 0)
			{
				return;
			}

			NetworkConnection conn = owner.Owner != null && owner.Owner.IsActive ? owner.Owner : null;
			DismissAllPets(owner, petController, conn);
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
			if (!TryBeginPlayerRequest(conn, out PlayerRequestContext request))
			{
				return;
			}
			IPlayerCharacter player = request.Character;

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.Attack, out long guardKey))
			{
				return;
			}

			try
			{
				IPetController petController = conn.FirstObject.GetComponent<IPetController>();
				Pet lead = FirstLivingPet(petController);
				if (lead == null)
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
				bool chosenNeedsAggression = false;
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
								c => IsWithinTargetingRange(player, c) && IsValidPetTarget(petController, lead, player, c),
								out candidate);
							break;
					}

					// A target the owner NAMED (pinned or current) may cost them their peace; one found by threat is already hostile.
					bool named = order[i] == PetAttackTarget.Pinned || order[i] == PetAttackTarget.Current;
					if (candidate != null && IsValidPetTarget(petController, lead, player, candidate, named, out bool needsAggression))
					{
						chosen = candidate;
						chosenNeedsAggression = needsAggression;
					}
				}

				if (chosen != null && chosenNeedsAggression && !TurnOnAggressionFor(player, lead, chosen))
				{
					chosen = null;
				}

				if (chosen == null)
				{
					AnnouncePetAction(lead, PetAction.NoTarget, null);
					return;
				}

				/* Every pet goes, each answering the harm rule for itself: the target was chosen against the first
				 * living pet, and a pet of another kind may not be allowed what it is. */
				for (int i = 0; i < petController.Pets.Count; ++i)
				{
					Pet pet = petController.Pets[i];
					if (!IsAlive(pet) ||
						(!ReferenceEquals(pet, lead) && !IsValidPetTarget(petController, pet, player, chosen)))
					{
						continue;
					}
					commandingPet = pet;
					try
					{
						CommandPetAttack(pet, chosen);
					}
					finally
					{
						commandingPet = null;
					}
					AnnouncePetAction(pet, PetAction.Attacking, chosen);
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
			if (!TryBeginPlayerRequest(conn, out _))
			{
				return;
			}

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
					for (int i = 0; i < petController.Pets.Count; ++i)
					{
						if (petController.Pets[i] != null)
						{
							petController.Pets[i].AttackPriority = msg.Priority;
						}
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
			if (!TryBeginPlayerRequest(conn, out _))
			{
				return;
			}

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
				if (petController == null || petController.Pets.Count == 0)
				{
					// no pet exists
					return;
				}

				petController.Stance = msg.Stance;
				for (int i = 0; i < petController.Pets.Count; ++i)
				{
					Pet pet = petController.Pets[i];
					if (pet == null)
					{
						continue;
					}
					pet.Stance = msg.Stance;

					// A pet dropped to Passive stops what it is doing.
					if (msg.Stance == PetStance.Passive)
					{
						RecallPet(pet);
					}
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

		private static bool IsValidPetTarget(IPetController petController, Pet pet, IPlayerCharacter owner, ICharacter target)
		{
			return IsValidPetTarget(petController, pet, owner, target, false, out _);
		}

		/// <summary>
		/// <see cref="IsValidPetTarget(IPetController, Pet, IPlayerCharacter, ICharacter)"/>; with <paramref name="allowAggression"/>, a
		/// target refused only because the owner's aggression is off also passes, and <paramref name="needsAggression"/> says so.
		/// </summary>
		/// <param name="petController">The owner's pet controller: none of its pets is a target.</param>
		/// <param name="pet">The pet that would attack, answering the scene, faction and harm checks.</param>
		private static bool IsValidPetTarget(IPetController petController, Pet pet, IPlayerCharacter owner, ICharacter target, bool allowAggression, out bool needsAggression)
		{
			needsAggression = false;
			if (target == null || target == owner)
			{
				return false;
			}

			// Never let a player order their pets onto one another.
			for (int i = 0; i < petController.Pets.Count; ++i)
			{
				if (ReferenceEquals(target, petController.Pets[i]))
				{
					return false;
				}
			}

			if (!target.TryGet(out ICharacterDamageController damageController) || !damageController.IsAlive)
			{
				return false;
			}

			// Same world, same instance. SceneObject IDs and character references are process-wide
			// while scenes are stacked per instance, so without this a pet could be pointed at
			// something in another copy of the same dungeon.
			if (pet != null &&
				pet.GameObject.scene != target.GameObject.scene)
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
			if (pet == null ||
				!pet.TryGet(out IFactionController _) ||
				!target.TryGet(out IFactionController _))
			{
				return false;
			}

			// The harm rule, with the pet answering as its owner: an enemy, or anyone while the owner's aggression is on.
			if (!HarmRule.MayHarm(pet, target, out _))
			{
				/* Sending a pet at a neutral or allied character is choosing to fight it, as striking it yourself is:
				 * the caller turns the owner's aggression on. Only a refusal the toggle lifts qualifies; companions, an
				 * arena's teams and a pack refuse whatever any toggle says. */
				if (allowAggression && HarmRule.RefusedForWantOfAggression(pet, target))
				{
					needsAggression = true;
					return true;
				}
				return false;
			}

			return true;
		}

		/// <summary>
		/// Turns the owner's aggression on so their pet may attack <paramref name="target"/>, a neutral or allied character the
		/// owner named, and tells them. False when the toggle could not be turned on or the attack is still refused after it.
		/// </summary>
		private bool TurnOnAggressionFor(IPlayerCharacter owner, Pet pet, ICharacter target)
		{
			if (!owner.TryGet(out IFactionController factions) || !factions.TrySetAggression(true, out _))
			{
				return false;
			}
			if (!HarmRule.MayHarm(pet, target, out _))
			{
				return false;
			}
			if (owner.Owner != null && owner.Owner.IsActive)
			{
				Server.NetworkWrapper.Broadcast(owner.Owner, new ToastBroadcast()
				{
					Text = $"Aggression turned on: {PetName(pet)} is attacking {(target is IPlayerCharacter player ? player.CharacterName : target.GameObject.name)}.",
					Severity = ToastSeverity.Warning,
				}, true, Channel.Reliable);
			}
			return true;
		}

		/// <summary>The pet an attack order is being applied to right now: its own target change is announced as the order.</summary>
		private Pet commandingPet;

		/// <summary>True when the pet is spawned and alive.</summary>
		private static bool IsAlive(Pet pet)
		{
			return pet != null &&
				pet.TryGet(out ICharacterDamageController damageController) &&
				damageController.IsAlive;
		}

		/// <summary>The oldest living pet, which answers for the pack when an attack order picks its target; null when none.</summary>
		private static Pet FirstLivingPet(IPetController petController)
		{
			if (petController == null)
			{
				return null;
			}
			for (int i = 0; i < petController.Pets.Count; ++i)
			{
				if (IsAlive(petController.Pets[i]))
				{
					return petController.Pets[i];
				}
			}
			return null;
		}

		/// <summary>The pet's name as its owner sees it in a toast.</summary>
		private static string PetName(Pet pet)
		{
			string name = pet != null && pet.GameObject != null ? pet.GameObject.name : null;
			return string.IsNullOrEmpty(name) ? "your pet" : name;
		}

		/// <summary>Per pet: the target last announced to its owner, and when, so one fight is told once.</summary>
		private readonly Dictionary<Pet, KeyValuePair<long, float>> lastAnnouncedTarget = new Dictionary<Pet, KeyValuePair<long, float>>();

		/// <summary>Seconds before the same target is announced again.</summary>
		private const float RepeatAnnouncementSeconds = 15f;

		/// <summary>
		/// A pet's AI took a target on its own (not an attack order): its owner hears it was defending or, for an aggressive
		/// pet, engaging. The same target is told once per <see cref="RepeatAnnouncementSeconds"/>: a state change drops and
		/// restores it, and that is not a new fight.
		/// </summary>
		private void OnPetTargetChanged(IAICompanion companion, ICharacter target)
		{
			Pet pet = companion as Pet;
			if (pet == null || target == null || ReferenceEquals(pet, commandingPet))
			{
				return;
			}
			if (lastAnnouncedTarget.TryGetValue(pet, out KeyValuePair<long, float> last) &&
				last.Key == target.ID && Time.time - last.Value < RepeatAnnouncementSeconds)
			{
				return;
			}
			AnnouncePetAction(pet, pet.Stance == PetStance.Aggressive ? PetAction.Engaging : PetAction.Defending, target);
		}

		/// <summary>Tells the pet's owner what it has just started doing (<see cref="PetActionBroadcast"/>).</summary>
		private void AnnouncePetAction(Pet pet, PetAction action, ICharacter target)
		{
			if (pet == null || !(pet.PetOwner is IPlayerCharacter owner) || owner.Owner == null || !owner.Owner.IsActive)
			{
				return;
			}
			if (target != null)
			{
				lastAnnouncedTarget[pet] = new KeyValuePair<long, float>(target.ID, Time.time);
			}
			Server.NetworkWrapper.Broadcast(owner.Owner, new PetActionBroadcast()
			{
				PetID = pet.ID,
				Action = (byte)action,
				TargetID = target != null ? target.ID : 0,
			}, true, Channel.Reliable);
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

				/* Set again AFTER the transition. ChangeState re-enters an attacking state on purpose (that is how an NPC
				 * re-targets) and runs Exit first, and Exit drops the target unless the next state keeps it — so a pet
				 * already fighting, or in one of its combat sub-states, lost the target set above and was left with none.
				 * Its own sweep finds only characters hostile by faction, so an enemy came back and a NEUTRAL target sent
				 * with the owner's aggression on never did. Not through the candidate list: PickTarget refuses
				 * every target for a Passive pet, and an explicit order is obeyed in any stance. */
				aiController.Target = target.Transform;
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

			if (pet.TryGet(out IAbilityKnowledgeController abilityController))
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
		/// <param name="aiInitPosition">Home position the pet's brain is prepared with; see <see cref="AIBrainHost.Prepare"/>.</param>
		/// <param name="abilities">Optional ability list restored from the database; null when summoned fresh.</param>
		/// <param name="broadcastTarget">Network connection that should receive the PetAddBroadcast.</param>
		/// <param name="persistedAttributes">Attribute values restored from the database; null when summoned fresh.</param>
		/// <param name="persistedBuffs">Buffs restored from the database; null when summoned fresh.</param>
		/// <param name="summoningAbility">
		/// The cast that summoned the pet, carrying the effects the player crafted onto it; null when
		/// the pet is restored from the database.
		/// </param>
		/// <param name="restoredSlot">The slot a restored pet was saved in; -1 for a fresh summon, which takes the lowest free one.</param>
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
			List<PetPersistedBuff> persistedBuffs = null,
			Ability summoningAbility = null,
			int restoredSlot = -1)
		{
			NetworkConnection ownerConnection = owner.Owner != null && owner.Owner.IsActive ? owner.Owner : null;
			int slot = MakeRoomForPet(owner, petController, petAbilityTemplate, restoredSlot, ownerConnection);

			Pet pet = nob.GetComponent<Pet>();
			if (pet == null)
			{
				/* Pool object has no Pet component — return it to the pool to prevent a leak. It was
				 * retrieved and never spawned, so it goes back the way the character system returns a
				 * character it never spawned (StorePooledInstantiated) rather than through a despawn,
				 * and is kept out of the world scenes like every other pooled pet. */
				Server.NetworkWrapper.NetworkManager.StorePooledInstantiated(nob, true);
				PersistentPool.Keep(Server.NetworkWrapper.NetworkManager, nob);
				return false;
			}

			pet.PetOwner = owner;
			pet.PetAbilityTemplate = petAbilityTemplate;
			pet.Slot = slot;
			pet.Stance = petController.Stance;
			pet.MovementOrder = PetMovementOrder.Follow;
			petController.MovementOrder = PetMovementOrder.Follow;
			petController.AttackPriority = PetAttackPriority.Normalize(petController.AttackPriority);
			pet.AttackPriority = petController.AttackPriority;

			/* Build the ability list before the spawn: Pet.OnStartServer teaches whatever is in
			 * PetAbilityIDs, and OnStartServer runs inside ServerManager.Spawn below. The summoning
			 * ability adds what its On Summon effects teach. */
			pet.PetAbilityIDs = petAbilityTemplate != null
				? petAbilityTemplate.BuildPetAbilityIDs(summoningAbility, abilities)
				: (abilities != null ? new List<int>(abilities) : new List<int>());

			/* Restored health and buffs, staged for the same reason and applied at the same
			 * moment. Both are serialised into the spawn payload by their own controllers, and
			 * FishNet writes that payload after the start callbacks have run — so applying them
			 * from Pet.OnStartServer is what puts a wounded pet on its owner's screen at the
			 * health it actually had, rather than at full and then snapping down. Null on a fresh
			 * summon: a newly conjured pet arrives whole and unbuffed. */
			pet.PersistedAttributes = persistedAttributes;
			pet.PersistedBuffs = persistedBuffs;

			petController.AddPet(pet);

			UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(pet.GameObject, owner.GameObject.scene);

			// Ensure the game object is active, pooled objects are disabled
			pet.GameObject.SetActive(true);

			/* Initialise the brain only once the GameObject is active and in its final scene.
			 * Initialize enters the pet's first state, and a state's Enter touches the
			 * NavMeshAgent (speed, isStopped, destination). Doing that on a pooled object that is
			 * still disabled and not yet placed on a NavMesh makes Unity reject each call with an
			 * "agent is not active / not on a NavMesh" error. */
			if (AIBrainHost.TryGet(ServerManager.NetworkManager, out AIBrainHost brainHost))
			{
				/* The pet's home is where it stands, not the world origin. The database load path
				 * passed Vector3.zero here, so a restored pet leashed to (0,0,0) and any state
				 * with leashing enabled marched it across the map to the map's corner. */
				AIController aiController = brainHost.Prepare(pet, aiInitPosition);

				// Follow, do not fight: Target means "what I am attacking" to the whole AI.
				if (aiController != null)
				{
					aiController.Target = null;
				}
			}
			else
			{
				Log.Warning("PetSystem", $"No AI brain host is running; {pet.GameObject.name} will spawn without a brain.");
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
				Slot = (byte)pet.Slot,
				Stance = pet.Stance,
				AttackPriority = pet.AttackPriority,
				MovementOrder = pet.MovementOrder,
			}, true, Channel.Reliable);

			// Increment achievement for summoning a pet
			if (PetSummonAchievementTemplate != null &&
				owner != null)
			{
				Accomplishments.Record(owner, PetSummonAchievementTemplate);
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

			// The summon spell's own On Summon effects, as the player crafted it.
			InvokeSummonEvents(owner, summoningAbility, pet);

			return true;
		}

		/// <summary>
		/// Frees a slot for a pet about to arrive and returns it: dismisses the pet a summon replaces under the slot rules,
		/// or, for a restore, whatever holds the slot the pet was saved in.
		/// </summary>
		/// <remarks>
		/// A replaced pet goes through <see cref="DismissPet"/> like any other dismissal: its row is written as not out and
		/// its owner is told it left. When a character could have one pet, a summon despawned the old one silently and the
		/// new pet's add broadcast stood in for the removal; with several pets the client has to be told which one went.
		/// </remarks>
		/// <param name="owner">The summoner.</param>
		/// <param name="petController">The summoner's pet controller.</param>
		/// <param name="template">The summon spell; its <see cref="PetAbilityTemplate.MaxPets"/> applies.</param>
		/// <param name="restoredSlot">The saved slot of a restored pet, or -1 for a summon.</param>
		/// <param name="conn">The owner's connection, told of a replaced pet; null when there is none.</param>
		/// <returns>The slot the new pet takes.</returns>
		private int MakeRoomForPet(IPlayerCharacter owner, IPetController petController, PetAbilityTemplate template, int restoredSlot, NetworkConnection conn)
		{
			if (restoredSlot >= 0)
			{
				for (int i = petController.Pets.Count - 1; i >= 0; --i)
				{
					if (petController.Pets[i] != null && petController.Pets[i].Slot == restoredSlot)
					{
						DismissPet(owner, petController, petController.Pets[i], conn);
					}
				}
				return restoredSlot;
			}

			int templateID = template != null ? template.ID : 0;
			int spellMax = template != null ? template.MaxPets : 1;
			for (int guard = 0; guard <= PetSlotRules.MaxSlots; ++guard)
			{
				List<int> templateIDs = new List<int>(petController.Pets.Count);
				List<int> usedSlots = new List<int>(petController.Pets.Count);
				for (int i = 0; i < petController.Pets.Count; ++i)
				{
					Pet existing = petController.Pets[i];
					templateIDs.Add(existing != null && existing.PetAbilityTemplate != null ? existing.PetAbilityTemplate.ID : 0);
					usedSlots.Add(existing != null ? existing.Slot : -1);
				}

				int replace = PetSlotRules.IndexToReplace(templateIDs, templateID, spellMax, petController.PetSlots);
				if (replace < 0)
				{
					int free = PetSlotRules.LowestFreeSlot(usedSlots, petController.PetSlots);
					if (free >= 0)
					{
						return free;
					}
					replace = 0;
				}
				DismissPet(owner, petController, petController.Pets[replace], conn);
			}
			return 0;
		}

		/// <summary>
		/// Runs the On Summon effects of the ability that summoned a pet, on the server, now that the
		/// pet is spawned.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The event's target is the pet, so an action with no selector of its own (a buff, a heal)
		/// lands on the pet, and an <see cref="InitiatorTargetSelector"/> reaches the summoner. Run
		/// after the spawn for the same reason the summon triggers are: an action that changes the
		/// pet's state needs a spawned object to replicate from.
		/// </para>
		/// <para>
		/// The abilities those effects teach are not taught here. They were merged into
		/// <see cref="Pet.PetAbilityIDs"/> before the spawn, which is when the pet learns.
		/// </para>
		/// <para>
		/// A restore passes no ability and runs nothing: the pet comes back with the abilities,
		/// health and buffs it was saved with, and re-running the effects would apply them twice.
		/// </para>
		/// </remarks>
		/// <param name="owner">The summoner, and the event's initiator.</param>
		/// <param name="summoningAbility">The cast that summoned the pet, or null.</param>
		/// <param name="pet">The pet that arrived.</param>
		/// <summary>The owner's learned (crafted) copy of <paramref name="template"/>, or null: one per template, as the crafter allows.</summary>
		private static Ability FindLearnedSummon(ICharacter owner, PetAbilityTemplate template)
		{
			if (owner == null || template == null || !owner.TryGet(out IAbilityKnowledgeController knowledge) ||
				knowledge.KnownAbilities == null)
			{
				return null;
			}
			foreach (Ability ability in knowledge.KnownAbilities.Values)
			{
				if (ability != null && ReferenceEquals(ability.Template, template))
				{
					return ability;
				}
			}
			return null;
		}

		private static void InvokeSummonEvents(ICharacter owner, Ability summoningAbility, Pet pet)
		{
			if (owner == null ||
				pet == null ||
				summoningAbility == null ||
				summoningAbility.OnSummonEvents.Count < 1)
			{
				return;
			}

			foreach (AbilityOnSummonEvent summonEvent in summoningAbility.OnSummonEvents.Values)
			{
				PetEventData eventData = new PetEventData(owner, pet);
				eventData.SetTarget(pet.GameObject);
				summonEvent.Execute(eventData);
			}
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
			Trigger.InvokeAll(triggers, new PetEventData(owner, pet));
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

				DatabaseResult<IReadOnlyList<CharacterPetData>> fetchResult = await charPetService.FetchSpawnedAsync(characterID);
				if (!fetchResult.IsSuccess)
				{
					// Not restored this session, and said so. The rows still say the pets are out,
					// and nothing overwrites them while no pet is out, so the next login restores them.
					await Log.Warning("PetSystem", $"Could not read the spawned pets for CharID={characterID}: [{fetchResult.ErrorCode}] {fetchResult.ErrorMessage}; they are not restored this session.");
					return;
				}
				if (fetchResult.Data == null || fetchResult.Data.Count == 0)
				{
					return;
				}

				List<CharacterPetData> petRows = new List<CharacterPetData>(fetchResult.Data);

				/* Attributes and buffs are only accepted when their version matches the pet
				 * row's. All three tables are stamped together by the character save, so a row
				 * carrying an older version belongs to a pet that has since been dismissed — and
				 * a previous pet may well have known an attribute or carried a buff this one does
				 * not. Matching on the version is what keeps a hunter's new wolf from inheriting
				 * the mana pool of the elemental it replaced.
				 *
				 * A failed read of either ends the restore, for the reason a failed sub-entity
				 * fetch ends a character load. It used to read as "none saved", so the pet came
				 * back whole and unbuffed — and the next character save stamped that over the
				 * rows it had failed to read, making the loss permanent. Not restored, the pet
				 * writes nothing, and the next login reads the rows intact. */
				(bool attributesRead, IReadOnlyList<CharacterPetAttributeData> attributeRows) = await FetchPersistedPetAttributesAsync(characterID);
				(bool buffsRead, IReadOnlyList<CharacterPetBuffData> buffRows) = await FetchPersistedPetBuffsAsync(characterID);
				if (!attributesRead || !buffsRead)
				{
					return;
				}

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

					if (!character.TryGet(out IPetController petController))
					{
						return;
					}

					/* Slots past what the character has now are left in the database, not restored: the slot count can
					 * fall (an archetype dropped) and a restore must not put out more pets than a summon could. The rows
					 * keep saying the pet is out, so it returns if the slots do. */
					int slots = PetSlotRules.ClampSlots(petController.PetSlots);
					for (int i = 0; i < petRows.Count; ++i)
					{
						CharacterPetData petData = petRows[i];
						if (petData.Slot < 0 || petData.Slot >= slots)
						{
							Log.Warning("PetSystem", $"CharID={characterID} has a pet saved in slot {petData.Slot} but {slots} pet slot(s) now; it is not restored.");
							continue;
						}

						// Look up the pet ability template by ID
						PetAbilityTemplate petAbilityTemplate = BaseAbilityTemplate.Get<PetAbilityTemplate>(petData.TemplateID);
						if (petAbilityTemplate == null || petAbilityTemplate.PetPrefab == null)
						{
							continue;
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
						if (SpawnAndInitializePet(character, petController, petAbilityTemplate, nob, spawnPosition, abilities, conn,
							PersistedAttributesFor(attributeRows, petData.Slot, petData.Version),
							PersistedBuffsFor(buffRows, petData.Slot, petData.Version),
							restoredSlot: petData.Slot))
						{
							/* The summon spell's crafted On Summon effects again, from the owner's learned copy of it: a
							 * permanent effect they put on the pet (a regeneration) is not saved with it (permanent buffs never
							 * are, BuffController.IsPersistedBuff), so without this a pet came back from a login without it.
							 * Actions only: the abilities a summon taught are the saved list, as before. */
							InvokeSummonEvents(character, FindLearnedSummon(character, petAbilityTemplate), nob.GetComponent<Pet>());
						}
					}
				});
			}
			catch (Exception ex)
			{
				await Log.Error("PetSystem", $"Error loading/spawning pet (CharID={characterID}): {ex}");
			}
		}

		/// <summary>
		/// Reads the saved attribute values of all the character's pets.
		/// </summary>
		/// <param name="characterID">The owning character.</param>
		/// <returns>
		/// Whether the rows could be read, and the rows. A failed read is reported and returns false: it is not the same as none.
		/// </returns>
		private async Task<(bool Read, IReadOnlyList<CharacterPetAttributeData> Rows)> FetchPersistedPetAttributesAsync(long characterID)
		{
			if (!Server.Database.ServiceRegistry.TryGet<ICharacterPetAttributeService>(out var petAttributeService))
			{
				return (true, null);
			}

			DatabaseResult<IReadOnlyList<CharacterPetAttributeData>> result = await petAttributeService.FetchAsync(characterID);
			if (!result.IsSuccess)
			{
				await Log.Warning("PetSystem", $"Could not read the pet attributes for CharID={characterID}: [{result.ErrorCode}] {result.ErrorMessage}; the pets are not restored this session.");
				return (false, null);
			}
			return (true, result.Data);
		}

		/// <summary>
		/// Reads the saved buffs of all the character's pets.
		/// </summary>
		/// <param name="characterID">The owning character.</param>
		/// <returns>
		/// Whether the rows could be read, and the rows. A failed read is reported and returns false: it is not the same as none.
		/// </returns>
		private async Task<(bool Read, IReadOnlyList<CharacterPetBuffData> Rows)> FetchPersistedPetBuffsAsync(long characterID)
		{
			if (!Server.Database.ServiceRegistry.TryGet<ICharacterPetBuffService>(out var petBuffService))
			{
				return (true, null);
			}

			DatabaseResult<IReadOnlyList<CharacterPetBuffData>> result = await petBuffService.FetchAsync(characterID);
			if (!result.IsSuccess)
			{
				await Log.Warning("PetSystem", $"Could not read the pet buffs for CharID={characterID}: [{result.ErrorCode}] {result.ErrorMessage}; the pets are not restored this session.");
				return (false, null);
			}
			return (true, result.Data);
		}

		/// <summary>
		/// The attribute values to stage onto the pet restored from <paramref name="slot"/>: only rows of that slot written
		/// alongside its pet row (same version); null when there are none. Pure.
		/// </summary>
		/// <remarks>
		/// A row carrying another version belongs to a pet that has since been dismissed from the slot, and a previous pet
		/// may well have known an attribute this one does not. Matching the version is what keeps a hunter's new wolf from
		/// inheriting the mana pool of the elemental it replaced; matching the slot keeps two pets out at once apart.
		/// </remarks>
		public static List<PetPersistedAttribute> PersistedAttributesFor(IReadOnlyList<CharacterPetAttributeData> rows, int slot, long petVersion)
		{
			if (rows == null || rows.Count < 1)
			{
				return null;
			}

			List<PetPersistedAttribute> attributes = new List<PetPersistedAttribute>();
			for (int i = 0; i < rows.Count; ++i)
			{
				CharacterPetAttributeData row = rows[i];
				if (row.Slot != slot || row.Version != petVersion)
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
		/// The buffs to stage onto the pet restored from <paramref name="slot"/>; see <see cref="PersistedAttributesFor"/>. Pure.
		/// </summary>
		public static List<PetPersistedBuff> PersistedBuffsFor(IReadOnlyList<CharacterPetBuffData> rows, int slot, long petVersion)
		{
			if (rows == null || rows.Count < 1)
			{
				return null;
			}

			List<PetPersistedBuff> buffs = new List<PetPersistedBuff>();
			for (int i = 0; i < rows.Count; ++i)
			{
				CharacterPetBuffData row = rows[i];
				if (row.Slot != slot || row.Version != petVersion)
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
		/// Removes the character's pets from the world when the character leaves it.
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

			/* Drop the references. Leaving one set meant that after a pet died, the owner's
			 * controller still pointed at a despawned, pooled object — so a Summon or Follow
			 * command would happily warp and re-task a pet that no longer existed. */
			for (int i = petController.Pets.Count - 1; i >= 0; --i)
			{
				if (i >= petController.Pets.Count)
				{
					continue;
				}
				Pet pet = petController.Pets[i];
				if (pet != null)
				{
					DespawnPet(pet.NetworkObject);
				}
				if (!ReferenceEquals(pet, null))
				{
					ForgetPet(petController, pet);
				}
			}
			petController.ClearPets();
			petController.OnOwnerAttacked -= PetController_OnOwnerAttacked;
			petController.OnPetSlotsReduced -= PetController_OnPetSlotsReduced;
		}

		/// <summary>
		/// Returns a spawned pet to the pool, out of the world scene. Main thread only.
		/// </summary>
		/// <remarks>
		/// FishMMO pools through <see cref="PersistentPool"/>, as NPCs and loot do. FishNet leaves a
		/// despawned object wherever it was, so a pet pooled in place sat inside the world scene its
		/// owner had been in, and an instance scene unloads with its last occupant — destroying a
		/// pooled pet the pool still counted. The next summon then paid for a fresh instantiate.
		/// </remarks>
		/// <param name="petObject">The pet's network object, or null.</param>
		private void DespawnPet(NetworkObject petObject)
		{
			if (petObject != null && petObject.IsSpawned)
			{
				PersistentPool.Despawn(Server.NetworkWrapper.NetworkManager, petObject);
			}
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

			// Zero is "no template"; ids are signed hashes, so a negative one is valid.
			int templateID = pet.PetAbilityTemplate != null ? pet.PetAbilityTemplate.ID : 0;
			if (templateID == 0)
			{
				Log.Warning("PetSystem", $"Pet for character {owner.ID} was dismissed with no resolvable ability template; the database still lists it as out.");
				return;
			}

			// Abilities granted at summon time live only on the controller until this runs.
			pet.CaptureKnownAbilities();

			long characterID = owner.ID;

			/* The row quotes the session claim held for the owner now, and lands only while it is
			 * still held (the service's PersistOwnedAsync). Every dismissal runs while the owner is
			 * resident — connected, or a combat-logout body — so it holds one; the departure paths
			 * take the claim out of SessionTokens before they raise anything, and never dismiss a pet
			 * (a pet that is out at logout stays out). Without a claim the owner is not ours to
			 * write, and nothing is: the owner's own departure snapshot, taken before its release,
			 * already says what became of the pet. */
			if (!TryCaptureSessionClaim(characterID, out CharacterSessionLeaseData claim))
			{
				Log.Warning("PetSystem", $"Pet for character {characterID} was dismissed, but this server holds no session claim for it; the dismissal was not written.");
				return;
			}

			long version = ++owner.Version;
			int slot = pet.Slot;
			List<int> abilities = pet.PetAbilityIDs != null ? new List<int>(pet.PetAbilityIDs) : new List<int>();

			/* Keyed by characterID to serialize with any other pet op for the same character — and
			 * with the owner's save-and-release, which runs on the same lane and so after this. */
			EnqueuePersistence(() => SavePetDismissedAsync(characterID, slot, version, templateID, abilities, claim), characterID);
		}

		/// <summary>
		/// Writes the pet row with <c>spawned = false</c>.
		/// </summary>
		/// <param name="characterID">Character identifier that owns the pet.</param>
		/// <param name="slot">The pet's slot: only that slot's row is written.</param>
		/// <param name="version">Version to stamp the row with.</param>
		/// <param name="templateID">Pet template identifier.</param>
		/// <param name="abilities">Pet ability template identifiers.</param>
		/// <param name="claim">The owner's session claim when the pet was dismissed.</param>
		/// <returns>Asynchronous persistence task.</returns>
		private async Task SavePetDismissedAsync(long characterID, int slot, long version, int templateID, List<int> abilities, CharacterSessionLeaseData claim)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterPetService>(out var charPetService))
				{
					return;
				}

				/* The batched owned write with one row: the same upsert and the same prune of the rows
				 * the dismissed pet leaves unrestorable as the single-row write, behind the ownership
				 * gate. A refusal means the owner's claim is gone and so is this server's say over its
				 * pet; the report logs it, and the owner is evicted by the row save or the lease
				 * refresh. */
				CharacterPetData petData = new CharacterPetData(0, version, characterID, slot, templateID, abilities, false);
				DatabaseResult<BulkWriteResult> persistResult = await charPetService.PersistOwnedAsync(new[] { petData }, ClaimsOf(claim));
				await BulkWriteReporting.ReportAsync("PetSystem", "Pet dismissal save", persistResult, $"CharID={characterID}, slot {slot}, version {version}");
			}
			catch (Exception ex)
			{
				await Log.Error("PetSystem", $"Error recording pet dismissal (CharID={characterID}): {ex}");
			}
		}

		/// <summary>
		/// Handles pet killed event: the dead pet leaves (its owner's other pets stay) and its owner is told which one.
		/// </summary>
		private void CharacterSystem_OnPetKilled(NetworkConnection conn, IPlayerCharacter character, ICharacter killed)
		{
			Pet pet = killed as Pet;
			if (character == null || ReferenceEquals(pet, null) || !character.TryGet(out IPetController petController))
			{
				return;
			}

			bool listed = false;
			for (int i = 0; i < petController.Pets.Count; ++i)
			{
				listed |= ReferenceEquals(petController.Pets[i], pet);
			}
			if (!listed)
			{
				return;
			}

			/* A death is a dismissal the character save will never hear about. The owner is
			 * still logged in, so no save is coming to write spawned = false for them, and
			 * without this the row keeps saying the pet is out — handing the player their
			 * dead pet back, alive and whole, at the next login. DismissPet records it, despawns
			 * the body, tells the owner and fires the same dismiss triggers a release does. */
			DismissPet(character, petController, pet, conn);
		}

		/// <summary>
		/// Handles pet summoning via ability, spawning the pet at a random position within the bounding box.
		/// </summary>
		private void AbilityObject_OnPetSummon(Ability ability, IPlayerCharacter caster)
		{
			PetAbilityTemplate petAbilityTemplate = ability?.Template as PetAbilityTemplate;
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
			SpawnAndInitializePet(caster, petController, petAbilityTemplate, nob, spawnPosition, null, caster.Owner,
				summoningAbility: ability);
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
			petController.OnPetSlotsReduced -= PetController_OnPetSlotsReduced;
			petController.OnPetSlotsReduced += PetController_OnPetSlotsReduced;
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

			for (int i = 0; i < petController.Pets.Count; ++i)
			{
				DefendOwner(petController, petController.Pets[i], attacker);
			}
		}

		/// <summary>
		/// Sends one pet at whoever attacked its owner; see <see cref="PetController_OnOwnerAttacked"/>.
		/// </summary>
		private void DefendOwner(IPetController petController, Pet pet, ICharacter attacker)
		{
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

			if (!IsValidPetTarget(petController, pet, owner, attacker))
			{
				return;
			}

			CommandPetAttack(pet, attacker);
		}

		// Uses ServerBehaviour.TryEnqueueAsyncWork
	}
}