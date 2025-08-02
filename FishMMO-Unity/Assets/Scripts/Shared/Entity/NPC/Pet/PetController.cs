using FishNet.Transporting;

namespace FishMMO.Shared
{
	/// <summary>
	/// Controller for managing pet entities attached to a character. Handles pet state, network broadcasts, and event invocation.
	/// </summary>
	public class PetController : CharacterBehaviour, IPetController
	{
		/// <summary>
		/// The pet instance managed by this controller.
		/// </summary>
		public Pet Pet { get; set; }

		/// <summary>
		/// Resets the controller's state, clearing the pet reference.
		/// </summary>
		/// <param name="asServer">Whether the reset is performed on the server.</param>
		public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);
			Pet = null;
		}

#if !UNITY_SERVER
		/// <summary>
		/// Called when the character starts. Registers broadcast listeners for pet add/remove events if owner.
		/// </summary>
		public override void OnStartCharacter()
		{
			base.OnStartCharacter();

			if (base.IsOwner)
			{
				ClientManager.RegisterBroadcast<PetAddBroadcast>(OnClientPetAddBroadcastReceived);
				ClientManager.RegisterBroadcast<PetRemoveBroadcast>(OnClientPetRemoveBroadcastReceived);
			}
		}

		/// <summary>
		/// Called when the character stops. Unregisters broadcast listeners for pet add/remove events if owner.
		/// </summary>
		public override void OnStopCharacter()
		{
			base.OnStopCharacter();

			if (base.IsOwner)
			{
				ClientManager.UnregisterBroadcast<PetAddBroadcast>(OnClientPetAddBroadcastReceived);
				ClientManager.UnregisterBroadcast<PetRemoveBroadcast>(OnClientPetRemoveBroadcastReceived);
			}
		}

		/// <summary>
		/// Handles the broadcast when a pet is added. Sets the pet reference and invokes the OnPetSummoned event.
		/// </summary>
		/// <param name="msg">The broadcast message containing the pet ID.</param>
		/// <param name="channel">The network channel.</param>
		public void OnClientPetAddBroadcastReceived(PetAddBroadcast msg, Channel channel)
		{
			if (SceneObject.Objects.TryGetValue(msg.ID, out ISceneObject sceneObject))
			{
				Pet = sceneObject.GameObject.GetComponent<Pet>();

				IPetController.OnPetSummoned?.Invoke(Pet);
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
		}
#endif
	}
}