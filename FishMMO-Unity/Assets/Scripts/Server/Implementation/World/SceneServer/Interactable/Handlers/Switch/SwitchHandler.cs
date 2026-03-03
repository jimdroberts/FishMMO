using FishMMO.Shared;
using FishMMO.Logging;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishNet.Transporting;
using FishNet.Connection;

namespace FishMMO.Server.Implementation.World.SceneServer.Interactable
{
	/// <summary>
	/// Handles switch interactions. Toggles an <see cref="ISwitchTarget"/> component on the switch's
	/// target GameObject (door, chest, trap, etc.) and broadcasts the new state to the interacting player.
	/// </summary>
	[HandlesInteractable(typeof(Switch))]
	public class SwitchHandler : IInteractableHandler
	{
		private readonly IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour> server;

		public SwitchHandler(IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour> server)
		{
			this.server = server;
		}

		public void HandleInteraction(IInteractable interactable, IPlayerCharacter character, ISceneObject sceneObject, IInteractableSystem interactableSystem)
		{
			ISwitch switchInteractable = interactable as ISwitch;
			if (switchInteractable == null || switchInteractable.SwitchTarget == null)
			{
				return;
			}

			ISwitchTarget target = switchInteractable.SwitchTarget;

			// Toggle or activate
			if (target.IsActivated && switchInteractable.IsToggle)
			{
				target.Deactivate(character);
			}
			else
			{
				target.Activate(character);
			}

			// Notify the client of the new state
			server.NetworkWrapper.Broadcast(character.Owner, new SwitchStateBroadcast()
			{
				InteractableID = sceneObject.ID,
				Activated = target.IsActivated,
			}, true, Channel.Reliable);

			// Increment achievement
			if (switchInteractable.AchievementTemplate != null &&
				character.TryGet(out IAchievementController achievementController))
			{
				achievementController.Increment(switchInteractable.AchievementTemplate, 1);
			}
		}
	}
}