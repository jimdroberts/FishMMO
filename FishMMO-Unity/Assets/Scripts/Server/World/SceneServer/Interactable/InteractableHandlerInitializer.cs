using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.Server
{
	[CreateAssetMenu(fileName = "FishMMO Interactable Handler Initializer", menuName = "FishMMO/Interactables/FishMMO Interactable Handler Initializer", order = 1)]
	public class InteractableHandlerInitializer : ScriptableObject, IInteractableHandlerInitializer
	{
		public void RegisterHandlers()
		{
			InteractableSystem.RegisterInteractableHandler<AbilityCrafter>(new AbilityCrafterHandler());
			InteractableSystem.RegisterInteractableHandler<Banker>(new BankerHandler());
			InteractableSystem.RegisterInteractableHandler<DungeonEntrance>(new DungeonEntranceHandler());
			InteractableSystem.RegisterInteractableHandler<Merchant>(new MerchantHandler());
			InteractableSystem.RegisterInteractableHandler<WorldItem>(new WorldItemHandler());
			InteractableSystem.RegisterInteractableHandler<Bindstone>(new BindstoneHandler());
			InteractableSystem.RegisterInteractableHandler<Teleporter>(new TeleporterHandler());
		}
	}
}