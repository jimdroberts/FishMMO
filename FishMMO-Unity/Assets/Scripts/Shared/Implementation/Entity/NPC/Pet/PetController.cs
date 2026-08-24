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
		/// <summary>
		/// The pet instance managed by this controller.
		/// </summary>
		public Pet Pet { get; set; }

		/// <summary>
		/// The pet's combat stance as this peer currently understands it.
		/// </summary>
		/// <remarks>
		/// Mirrored here as well as on <see cref="Pet"/> so the UI has something to bind to
		/// between a pet being dismissed and re-summoned.
		/// </remarks>
		public PetStance Stance { get; set; } = PetStance.Defensive;

		/// <summary>
		/// The pet's movement order as this peer currently understands it.
		/// </summary>
		public PetMovementOrder MovementOrder { get; set; } = PetMovementOrder.Follow;

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
		/// mirroring what <see cref="AggressionState"/> already does per NPC — is what lets a
		/// defensive pet come to its owner's aid instead of watching.
		/// </remarks>
		public event Action<IPetController, ICharacter> OnOwnerAttacked;

		/// <summary>
		/// True once the server-side damage subscription has been taken, so it is released exactly
		/// once.
		/// </summary>
		private bool subscribedToDamage;

		/// <summary>
		/// Subscribes to the global damage event on the server.
		/// </summary>
		public override void OnStartNetwork()
		{
			base.OnStartNetwork();

			if (base.IsServerStarted && !subscribedToDamage)
			{
				ICharacterDamageController.OnDamaged += CharacterDamageController_OnDamaged;
				subscribedToDamage = true;
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

			base.OnDestroying();
		}

		/// <summary>
		/// Drops the global damage subscription if it is held.
		/// </summary>
		private void ReleaseDamageSubscription()
		{
			if (!subscribedToDamage)
			{
				return;
			}
			ICharacterDamageController.OnDamaged -= CharacterDamageController_OnDamaged;
			subscribedToDamage = false;
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

			// Never turn the pet on its own owner.
			if (Pet != null && ReferenceEquals(attacker, Pet))
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
			Pet = null;
			Stance = PetStance.Defensive;
			MovementOrder = PetMovementOrder.Follow;
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
			}
		}

		/// <summary>
		/// Called when the character stops. Unregisters broadcast listeners for pet events if owner.
		/// </summary>
		public override void OnStopCharacter()
		{
			base.OnStopCharacter();

			if (base.IsOwner)
			{
				ClientManager.UnregisterBroadcast<PetAddBroadcast>(OnClientPetAddBroadcastReceived);
				ClientManager.UnregisterBroadcast<PetRemoveBroadcast>(OnClientPetRemoveBroadcastReceived);
				ClientManager.UnregisterBroadcast<PetStanceBroadcast>(OnClientPetStanceBroadcastReceived);
				ClientManager.UnregisterBroadcast<PetMovementOrderBroadcast>(OnClientPetMovementOrderBroadcastReceived);
			}
		}

		/// <summary>
		/// Handles the broadcast when a pet is added. Sets the pet reference and invokes the OnPetSummoned event.
		/// </summary>
		/// <param name="msg">The broadcast message containing the pet ID and orders.</param>
		/// <param name="channel">The network channel.</param>
		public void OnClientPetAddBroadcastReceived(PetAddBroadcast msg, Channel channel)
		{
			if (SceneObject.Objects.TryGetValue(msg.ID, out ISceneObject sceneObject))
			{
				Pet = sceneObject.GameObject.GetComponent<Pet>();

				Stance = msg.Stance;
				MovementOrder = msg.MovementOrder;
				if (Pet != null)
				{
					Pet.Stance = msg.Stance;
					Pet.MovementOrder = msg.MovementOrder;
				}

				IPetController.OnPetSummoned?.Invoke(Pet);
				Character.Invoke(onPetSummonTriggers, new PetEventData(Character, Pet));
			}
		}

		/// <summary>
		/// Handles the broadcast when a pet is removed. Clears the pet reference and invokes the OnPetDestroyed event.
		/// </summary>
		/// <param name="msg">The broadcast message for pet removal.</param>
		/// <param name="channel">The network channel.</param>
		public void OnClientPetRemoveBroadcastReceived(PetRemoveBroadcast msg, Channel channel)
		{
			Pet = null;
			IPetController.OnPetDestroyed?.Invoke();
			Character.Invoke(onPetDismissTriggers, new PetEventData(Character, null));
		}

		/// <summary>
		/// Applies the server's authoritative stance so the UI reflects what the pet is really doing.
		/// </summary>
		/// <param name="msg">The stance broadcast.</param>
		/// <param name="channel">The network channel.</param>
		public void OnClientPetStanceBroadcastReceived(PetStanceBroadcast msg, Channel channel)
		{
			Stance = msg.Stance;
			if (Pet != null)
			{
				Pet.Stance = msg.Stance;
			}
			IPetController.OnPetOrdersChanged?.Invoke(Pet);
		}

		/// <summary>
		/// Applies the server's authoritative movement order.
		/// </summary>
		/// <param name="msg">The movement order broadcast.</param>
		/// <param name="channel">The network channel.</param>
		public void OnClientPetMovementOrderBroadcastReceived(PetMovementOrderBroadcast msg, Channel channel)
		{
			MovementOrder = msg.MovementOrder;
			if (Pet != null)
			{
				Pet.MovementOrder = msg.MovementOrder;
			}
			IPetController.OnPetOrdersChanged?.Invoke(Pet);
		}
#endif
	}
}
