using FishNet.Transporting;

namespace FishMMO.Shared
{
	/// <summary>
	/// Character guild controller.
	/// </summary>
	public class PetController : CharacterBehaviour, IPetController
	{
		public PetAbilityTemplate PetAbilityTemplate { get; set; }
		public Pet Pet { get; set;}

        public override void ResetState(bool asServer)
        {
            base.ResetState(asServer);

			Pet = null;
        }

#if !UNITY_SERVER
        public override void OnStartCharacter()
		{
			base.OnStartCharacter();

			if (base.IsOwner)
			{
				ClientManager.RegisterBroadcast<PetAddBroadcast>(OnClientPetAddBroadcastReceived);
				ClientManager.RegisterBroadcast<PetRemoveBroadcast>(OnClientPetRemoveBroadcastReceived);
			}
		}

		public override void OnStopCharacter()
		{
			base.OnStopCharacter();

			if (base.IsOwner)
			{
				ClientManager.UnregisterBroadcast<PetAddBroadcast>(OnClientPetAddBroadcastReceived);
				ClientManager.UnregisterBroadcast<PetRemoveBroadcast>(OnClientPetRemoveBroadcastReceived);
			}
		}

		public void OnClientPetAddBroadcastReceived(PetAddBroadcast msg, Channel channel)
		{
			if (SceneObject.Objects.TryGetValue(msg.ID, out ISceneObject sceneObject))
			{
				Pet pet = sceneObject.GameObject.GetComponent<Pet>();

				IPetController.OnPetSummoned?.Invoke(pet);
			}
		}

		public void OnClientPetRemoveBroadcastReceived(PetRemoveBroadcast msg, Channel channel)
		{
			IPetController.OnPetDestroyed?.Invoke();
		}
#endif
	}
}