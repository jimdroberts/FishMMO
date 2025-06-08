using UnityEngine;

namespace FishMMO.Server
{
	[CreateAssetMenu(fileName = "FishMMO Interactable Handler Initializer", menuName = "FishMMO/Interactables/FishMMO Interactable Handler Initializer", order = 1)]
	public class InteractableHandlerInitializer : ScriptableObject, IInteractableHandlerInitializer
	{
		public void RegisterHandlers()
		{
			InteractableSystem.RegisterInteractableHandler<AbilityCrafterHandler>(new AbilityCrafterHandler());
			InteractableSystem.RegisterInteractableHandler<BankerHandler>(new BankerHandler());
			InteractableSystem.RegisterInteractableHandler<DungeonEntranceHandler>(new DungeonEntranceHandler());
			InteractableSystem.RegisterInteractableHandler<MerchantHandler>(new MerchantHandler());
			InteractableSystem.RegisterInteractableHandler<WorldItemHandler>(new WorldItemHandler());
		}
	}
}