using FishNet.Transporting;
using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Controller for managing pet entities attached to a character. Handles pet state, network
	/// broadcasts, and event invocation.
	/// </summary>
	public class PetController : CharacterBehaviour, IPetController
	{
		/// <summary>Backing list for <see cref="Pets"/>, oldest first.</summary>
		private readonly List<Pet> pets = new List<Pet>();

		/// <inheritdoc />
		/// <remarks>
		/// Changes go through <see cref="AddPet"/> and <see cref="RemovePet"/>, which drive the server's damage subscription:
		/// see <see cref="UpdateDamageSubscription"/>.
		/// </remarks>
		public IReadOnlyList<Pet> Pets => pets;

		/// <summary>
		/// The character attribute that holds the pet slots. Zero: the "Pet Slots" asset (<see cref="PetSlotRules.DefaultAttributeID"/>).
		/// </summary>
		[TemplateReference(typeof(CharacterAttributeTemplate))]
		[Tooltip("The character attribute that holds this character's pet slots. Empty: the \"Pet Slots\" attribute.")]
		public int PetSlotsTemplateID;

		/// <summary>The template ID <see cref="PetSlots"/> reads.</summary>
		public int PetSlotsAttributeID => PetSlotsTemplateID != 0 ? PetSlotsTemplateID : PetSlotRules.DefaultAttributeID;

		/// <inheritdoc />
		/// <remarks>
		/// Read from the attribute every time, never cached: equipment, buffs and (later) archetypes move it through the
		/// attribute's modifiers, and the saved base value is the character's own.
		/// </remarks>
		public int PetSlots
		{
			get
			{
				CharacterAttribute attribute = null;
				bool has = Character != null &&
					Character.TryGet(out ICharacterAttributeController attributes) &&
					attributes.TryGetAttribute(PetSlotsAttributeID, out attribute);
				return PetSlotRules.SlotsFrom(has, has ? attribute.FinalValue : 0);
			}
		}

		/// <inheritdoc />
		public event Action<IPetController> OnPetSlotsReduced;

		/// <summary>The Pet Slots attribute this controller is listening to on the server, while it has pets.</summary>
		private CharacterAttribute subscribedSlots;

		/// <inheritdoc />
		public bool AddPet(Pet pet)
		{
			if (pet == null || pets.Contains(pet))
			{
				return false;
			}
			pets.Add(pet);
			UpdateDamageSubscription();
			return true;
		}

		/// <inheritdoc />
		/// <remarks>By reference, so a pet whose object Unity has already destroyed can still be unlisted.</remarks>
		public bool RemovePet(Pet pet)
		{
			for (int i = 0; i < pets.Count; ++i)
			{
				if (ReferenceEquals(pets[i], pet))
				{
					pets.RemoveAt(i);
					UpdateDamageSubscription();
					return true;
				}
			}
			return false;
		}

		/// <inheritdoc />
		public void ClearPets()
		{
			pets.Clear();
			UpdateDamageSubscription();
		}

		/// <summary>True when <paramref name="character"/> is one of this controller's pets.</summary>
		private bool IsOurPet(ICharacter character)
		{
			for (int i = 0; i < pets.Count; ++i)
			{
				if (ReferenceEquals(pets[i], character))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// The pets' combat stance as this peer currently understands it.
		/// </summary>
		/// <remarks>
		/// Mirrored here as well as on each pet so the UI has something to bind to
		/// between a pet being dismissed and re-summoned.
		/// </remarks>
		public PetStance Stance { get; set; } = PetStance.Defensive;

		/// <summary>
		/// The pets' movement order as this peer currently understands it.
		/// </summary>
		public PetMovementOrder MovementOrder { get; set; } = PetMovementOrder.Follow;

		/// <inheritdoc />
		public int AttackPriority { get; set; } = PetAttackPriority.Default;

		[Header("ECA - Pet")]
		[Tooltip("Triggers invoked when a pet is summoned.")]
		[SerializeField]
		private List<Trigger> onPetSummonTriggers = new List<Trigger>();
		[Tooltip("Triggers invoked when a pet is dismissed or destroyed.")]
		[SerializeField]
		private List<Trigger> onPetDismissTriggers = new List<Trigger>();

		/// <inheritdoc />
		public List<Trigger> OnPetSummonTriggers => onPetSummonTriggers;
		/// <inheritdoc />
		public List<Trigger> OnPetDismissTriggers => onPetDismissTriggers;

		/// <summary>
		/// Raised on the server when this controller's owner is damaged by a hostile character.
		/// </summary>
		/// <remarks>
		/// A defensive pet has to know its owner is under attack, and the owner is a player with
		/// no aggression table of its own to read. Subscribing here — one handler per player,
		/// mirroring what the server's <c>AggressionState</c> already does per NPC — is what lets a
		/// defensive pet come to its owner's aid instead of watching.
		/// </remarks>
		public event Action<IPetController, ICharacter> OnOwnerAttacked;

		/// <summary>
		/// True once the server-side damage subscription has been taken, so it is released exactly
		/// once.
		/// </summary>
		private bool subscribedToDamage;

		/// <summary>
		/// Called when the network object starts. The damage subscription is taken lazily, when a
		/// pet actually exists.
		/// </summary>
		public override void OnStartNetwork()
		{
			base.OnStartNetwork();

			UpdateDamageSubscription();
		}

		/// <summary>
		/// Holds the global damage subscription for exactly as long as this controller has a pet.
		/// </summary>
		/// <remarks>
		/// <see cref="ICharacterDamageController.OnDamaged"/> is a single static event raised for
		/// every point of damage dealt anywhere on the server. Subscribing one handler per player
		/// for the whole of that player's session — which is what taking the subscription in
		/// OnStartNetwork amounted to — makes the cost of a single hit proportional to the number
		/// of players logged in, and all but a handful of those handlers exist only to compare
		/// two references and return. On a populated scene server that is millions of delegate
		/// invocations a second spent discovering that nobody cares.
		/// <para>
		/// Scoping it to "has a pet" costs one comparison per assignment and leaves the handler
		/// list the length of the number of players who actually have a pet out.
		/// </para>
		/// </remarks>
		private void UpdateDamageSubscription()
		{
			bool wanted = pets.Count > 0 && base.IsServerStarted;

			UpdateSlotsSubscription(wanted);

			if (wanted == subscribedToDamage)
			{
				return;
			}

			if (wanted)
			{
				ICharacterDamageController.OnDamaged += CharacterDamageController_OnDamaged;
				subscribedToDamage = true;
			}
			else
			{
				ReleaseDamageSubscription();
			}
		}

		/// <summary>
		/// Releases the global damage subscription.
		/// </summary>
		public override void OnStopNetwork()
		{
			ReleaseDamageSubscription();

			base.OnStopNetwork();
		}

		/// <inheritdoc />
		public override void OnDestroying()
		{
			ReleaseDamageSubscription();
			OnOwnerAttacked = null;
			OnPetSlotsReduced = null;

			base.OnDestroying();
		}

		/// <summary>
		/// Drops the global damage subscription if it is held.
		/// </summary>
		private void ReleaseDamageSubscription()
		{
			UpdateSlotsSubscription(false);

			if (!subscribedToDamage)
			{
				return;
			}
			ICharacterDamageController.OnDamaged -= CharacterDamageController_OnDamaged;
			subscribedToDamage = false;
		}

		/// <summary>
		/// Listens to the Pet Slots attribute on the server for as long as there is a pet, so a slot count that falls under
		/// a pet (an item taken off, a buff ending) can dismiss it. Per character, so it costs nothing to anyone else.
		/// </summary>
		private void UpdateSlotsSubscription(bool wanted)
		{
			CharacterAttribute target = null;
			if (wanted && Character != null && Character.TryGet(out ICharacterAttributeController attributes))
			{
				attributes.TryGetAttribute(PetSlotsAttributeID, out target);
			}

			if (ReferenceEquals(target, subscribedSlots))
			{
				return;
			}
			if (subscribedSlots != null)
			{
				subscribedSlots.OnAttributeUpdated -= OnPetSlotsAttributeUpdated;
			}
			subscribedSlots = target;
			if (subscribedSlots != null)
			{
				subscribedSlots.OnAttributeUpdated += OnPetSlotsAttributeUpdated;
			}
		}

		/// <summary>Raises <see cref="OnPetSlotsReduced"/> when the new slot count leaves a pet outside it.</summary>
		private void OnPetSlotsAttributeUpdated(CharacterAttribute attribute)
		{
			int slots = PetSlots;
			for (int i = 0; i < pets.Count; ++i)
			{
				if (pets[i] != null && pets[i].Slot >= slots)
				{
					OnPetSlotsReduced?.Invoke(this);
					return;
				}
			}
		}

		/// <summary>
		/// Forwards "my owner was just hit" to whoever is listening, for defensive pets.
		/// </summary>
		/// <param name="attacker">The character that dealt the damage.</param>
		/// <param name="defender">The character that took the damage.</param>
		/// <param name="amount">Damage dealt.</param>
		/// <param name="damageAttribute">The damage type.</param>
		private void CharacterDamageController_OnDamaged(ICharacter attacker, ICharacter defender, int amount, DamageAttributeTemplate damageAttribute)
		{
			// This is a global event; ignore everything that is not an attack on our character.
			if (defender != Character || attacker == null || attacker == Character)
			{
				return;
			}

			// Never turn a pet on its own owner.
			if (IsOurPet(attacker))
			{
				return;
			}

			OnOwnerAttacked?.Invoke(this, attacker);
		}

		/// <summary>
		/// Resets the controller's state, clearing the pet reference and orders.
		/// </summary>
		/// <param name="asServer">Whether the reset is performed on the server.</param>
		public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);

			/* Released explicitly rather than left to the Pet setter. By the time a pooled
			 * character is reset the network object may already have stopped, so
			 * UpdateDamageSubscription's IsServerStarted test would decline to touch a
			 * subscription that is still held — and the handler would outlive the pet, the
			 * character, and this controller's usefulness. */
			pets.Clear();
			ReleaseDamageSubscription();

			Stance = PetStance.Defensive;
			MovementOrder = PetMovementOrder.Follow;
			AttackPriority = PetAttackPriority.Default;
		}

#if !UNITY_SERVER
		/// <summary>
		/// Called when the character starts. Registers broadcast listeners for pet events if owner.
		/// </summary>
		public override void OnStartCharacter()
		{
			base.OnStartCharacter();

			if (base.IsOwner)
			{
				ClientManager.RegisterBroadcast<PetAddBroadcast>(OnClientPetAddBroadcastReceived);
				ClientManager.RegisterBroadcast<PetRemoveBroadcast>(OnClientPetRemoveBroadcastReceived);
				ClientManager.RegisterBroadcast<PetStanceBroadcast>(OnClientPetStanceBroadcastReceived);
				ClientManager.RegisterBroadcast<PetMovementOrderBroadcast>(OnClientPetMovementOrderBroadcastReceived);
				ClientManager.RegisterBroadcast<PetAttackPriorityBroadcast>(OnClientPetAttackPriorityBroadcastReceived);
				ClientManager.RegisterBroadcast<PetActionBroadcast>(OnClientPetActionBroadcastReceived);
				SceneObject.Registered -= SceneObject_PetRespawned;
				SceneObject.Registered += SceneObject_PetRespawned;
			}
		}

		/// <summary>
		/// Called when the character stops. Unregisters broadcast listeners for pet events if owner.
		/// </summary>
		public override void OnStopCharacter()
		{
			base.OnStopCharacter();

			ClearPendingPetAdd();
			if (base.IsOwner)
			{
				ClientManager.UnregisterBroadcast<PetAddBroadcast>(OnClientPetAddBroadcastReceived);
				ClientManager.UnregisterBroadcast<PetRemoveBroadcast>(OnClientPetRemoveBroadcastReceived);
				ClientManager.UnregisterBroadcast<PetStanceBroadcast>(OnClientPetStanceBroadcastReceived);
				ClientManager.UnregisterBroadcast<PetMovementOrderBroadcast>(OnClientPetMovementOrderBroadcastReceived);
				ClientManager.UnregisterBroadcast<PetAttackPriorityBroadcast>(OnClientPetAttackPriorityBroadcastReceived);
				ClientManager.UnregisterBroadcast<PetActionBroadcast>(OnClientPetActionBroadcastReceived);
			}
			SceneObject.Registered -= SceneObject_PetRespawned;
		}

		/// <summary>
		/// A listed pet's object arriving again on this client: it takes the old object's place in the list, carrying the
		/// slot and orders, and the pet panel rebinds to it (OnPetSummoned).
		/// </summary>
		/// <remarks>
		/// The server can drop a pet from its owner's view (a crowd evicting it from the observer budget, before own pets
		/// were pinned there; or a step out of range) and send it again later. The client then despawns the old object to
		/// the pool and spawns the pet as a new one, but no PetAdd comes with it, so the list and the panel kept the old
		/// object: its health frozen while the live pet fought on (2026-11-18). Registration is the moment the new one has
		/// its ID and payload.
		/// </remarks>
		private void SceneObject_PetRespawned(ISceneObject sceneObject)
		{
			if (sceneObject == null || sceneObject.GameObject == null || pets.Count == 0 ||
				!sceneObject.GameObject.TryGetComponent(out Pet arrived))
			{
				return;
			}
			Pet listed = FindPet(sceneObject.ID);
			if (listed == null || ReferenceEquals(listed, arrived))
			{
				return;
			}
			arrived.Slot = listed.Slot;
			arrived.Stance = listed.Stance;
			arrived.MovementOrder = listed.MovementOrder;
			arrived.AttackPriority = listed.AttackPriority;
			RemovePet(listed);
			AddPet(arrived);
			IPetController.OnPetSummoned?.Invoke(arrived);
		}

		/// <summary>Pets the server announced whose objects have not reached this client yet; see <see cref="OnClientPetAddBroadcastReceived"/>.</summary>
		private readonly List<PetAddBroadcast> pendingPetAdds = new List<PetAddBroadcast>();

		/// <summary>
		/// Handles the broadcast when a pet is added. Lists the pet and invokes the OnPetSummoned event.
		/// </summary>
		/// <remarks>
		/// The pet object may not be here yet. After a scene change the server spawns the pet in the new scene and announces
		/// it at once, but the pet reaches this client through the observer system, which can deliver it a moment later.
		/// The announcement used to be dropped then: the client never learned it had a pet, so the pet panel kept the old
		/// scene's pet and its controls did nothing. It is now held until the pet registers (<see cref="SceneObject.Registered"/>).
		/// Several can wait at once: every pet that was out comes back together.
		/// </remarks>
		/// <param name="msg">The broadcast message containing the pet ID and orders.</param>
		/// <param name="channel">The network channel.</param>
		public void OnClientPetAddBroadcastReceived(PetAddBroadcast msg, Channel channel)
		{
			/* Only a LIVE object is the pet. A client unregisters a scene object when it is destroyed, not when it is
			 * despawned to the pool, and the server keeps a re-spawned pet's ID — so after any despawn (a scene change, a
			 * dismiss and re-summon, a death, an observer drop) this ID could still name the previous, pooled copy until
			 * the new one arrived and overwrote the entry. That copy was bound: the pet panel drew its frozen health (full)
			 * while the target frame, reading the live pet, showed 70% (2026-11-17). A dead copy now waits like a missing
			 * one, for the live object's registration. */
			if (SceneObject.Objects.TryGetValue(msg.ID, out ISceneObject sceneObject) &&
				sceneObject != null && sceneObject.GameObject != null && sceneObject.GameObject.TryGetComponent(out Pet pet) &&
				IsLive(pet))
			{
				RemovePendingPetAdd(msg.ID);
				ApplyPetAdd(msg, pet);
				return;
			}
			RemovePendingPetAdd(msg.ID);
			pendingPetAdds.Add(msg);
			SceneObject.Registered -= SceneObject_Registered;
			SceneObject.Registered += SceneObject_Registered;
		}

		private void SceneObject_Registered(ISceneObject sceneObject)
		{
			if (sceneObject == null || sceneObject.GameObject == null)
			{
				return;
			}
			for (int i = 0; i < pendingPetAdds.Count; ++i)
			{
				if (pendingPetAdds[i].ID != sceneObject.ID)
				{
					continue;
				}
				if (!sceneObject.GameObject.TryGetComponent(out Pet pet))
				{
					return;
				}
				PetAddBroadcast msg = pendingPetAdds[i];
				RemovePendingPetAdd(msg.ID);
				ApplyPetAdd(msg, pet);
				return;
			}
		}

		/// <summary>Forgets a held announcement; stops listening for registrations once none is held.</summary>
		private void RemovePendingPetAdd(long id)
		{
			for (int i = pendingPetAdds.Count - 1; i >= 0; --i)
			{
				if (pendingPetAdds[i].ID == id)
				{
					pendingPetAdds.RemoveAt(i);
				}
			}
			if (pendingPetAdds.Count == 0)
			{
				SceneObject.Registered -= SceneObject_Registered;
			}
		}

		private void ClearPendingPetAdd()
		{
			pendingPetAdds.Clear();
			SceneObject.Registered -= SceneObject_Registered;
		}

		/// <summary>True when <paramref name="pet"/> is a spawned, active object rather than a pooled copy still registered under its ID.</summary>
		private static bool IsLive(Pet pet)
		{
			return pet != null && pet.gameObject.activeInHierarchy && pet.NetworkObject != null && pet.NetworkObject.IsSpawned;
		}

		private void ApplyPetAdd(PetAddBroadcast msg, Pet pet)
		{
			/* One entry per pet: a stale copy listed under this ID before (see OnClientPetAddBroadcastReceived) gives way
			 * to the live object, and the panel's rebind on OnPetSummoned drops its row. */
			for (int i = pets.Count - 1; i >= 0; --i)
			{
				if (pets[i] != null && !ReferenceEquals(pets[i], pet) && pets[i].ID == msg.ID)
				{
					RemovePet(pets[i]);
				}
			}
			AddPet(pet);

			Stance = msg.Stance;
			MovementOrder = msg.MovementOrder;
			AttackPriority = PetAttackPriority.Normalize(msg.AttackPriority);
			pet.Slot = msg.Slot;
			pet.Stance = msg.Stance;
			pet.MovementOrder = msg.MovementOrder;
			pet.AttackPriority = AttackPriority;

			IPetController.OnPetSummoned?.Invoke(pet);
			Trigger.InvokeAll(onPetSummonTriggers, new PetEventData(Character, pet));
		}

		/// <summary>The listed pet with this ID, or null.</summary>
		public Pet FindPet(long id)
		{
			for (int i = 0; i < pets.Count; ++i)
			{
				if (pets[i] != null && pets[i].ID == id)
				{
					return pets[i];
				}
			}
			return null;
		}

		/// <summary>
		/// Handles the broadcast when a pet is removed. Unlists it (every pet for ID 0) and invokes the OnPetDestroyed event.
		/// </summary>
		/// <param name="msg">The broadcast message for pet removal.</param>
		/// <param name="channel">The network channel.</param>
		public void OnClientPetRemoveBroadcastReceived(PetRemoveBroadcast msg, Channel channel)
		{
			if (msg.ID == 0)
			{
				ClearPendingPetAdd();
				ClearPets();
				IPetController.OnPetDestroyed?.Invoke(null);
				Trigger.InvokeAll(onPetDismissTriggers, new PetEventData(Character, null));
				return;
			}

			RemovePendingPetAdd(msg.ID);
			Pet pet = FindPet(msg.ID);
			if (pet != null)
			{
				RemovePet(pet);
			}
			IPetController.OnPetDestroyed?.Invoke(pet);
			Trigger.InvokeAll(onPetDismissTriggers, new PetEventData(Character, null));
		}

		/// <summary>The first pet, or null: what the order-changed event carries.</summary>
		private Pet FirstPet => pets.Count > 0 ? pets[0] : null;

		/// <summary>
		/// Applies the server's authoritative stance so the UI reflects what the pets are really doing.
		/// </summary>
		/// <param name="msg">The stance broadcast.</param>
		/// <param name="channel">The network channel.</param>
		public void OnClientPetStanceBroadcastReceived(PetStanceBroadcast msg, Channel channel)
		{
			Stance = msg.Stance;
			for (int i = 0; i < pets.Count; ++i)
			{
				pets[i].Stance = msg.Stance;
			}
			IPetController.OnPetOrdersChanged?.Invoke(FirstPet);
		}

		/// <summary>Client: one of the owner's pets started doing something (<see cref="PetActionBroadcast"/>): the pet, the action, the target's ID.</summary>
		public static event System.Action<Pet, PetAction, long> OnClientPetAction;

		/// <summary>Client: tells the panel what a pet has just started doing, so the pet can say it in chat.</summary>
		public void OnClientPetActionBroadcastReceived(PetActionBroadcast msg, Channel channel)
		{
			Pet pet = FindPet(msg.PetID);
			if (pet == null)
			{
				return;
			}
			OnClientPetAction?.Invoke(pet, (PetAction)msg.Action, msg.TargetID);
		}

		/// <summary>
		/// The server confirmed (or corrected) the attack priority.
		/// </summary>
		public void OnClientPetAttackPriorityBroadcastReceived(PetAttackPriorityBroadcast msg, Channel channel)
		{
			AttackPriority = PetAttackPriority.Normalize(msg.Priority);
			for (int i = 0; i < pets.Count; ++i)
			{
				pets[i].AttackPriority = AttackPriority;
			}
			IPetController.OnPetOrdersChanged?.Invoke(FirstPet);
		}

		/// <summary>
		/// Applies the server's authoritative movement order.
		/// </summary>
		/// <param name="msg">The movement order broadcast.</param>
		/// <param name="channel">The network channel.</param>
		public void OnClientPetMovementOrderBroadcastReceived(PetMovementOrderBroadcast msg, Channel channel)
		{
			MovementOrder = msg.MovementOrder;
			for (int i = 0; i < pets.Count; ++i)
			{
				pets[i].MovementOrder = msg.MovementOrder;
			}
			IPetController.OnPetOrdersChanged?.Invoke(FirstPet);
		}
#endif
	}
}
