using FishMMO.Shared;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Server.Core;
using FishMMO.Shared.Core;
using FishNet.Transporting;
using FishNet.Connection;

namespace FishMMO.Server.Implementation.World.SceneServer.Interactable
{
	/// <summary>
	/// Handles shrine interactions. Heals health, mana, or both and optionally applies a buff
	/// based on the shrine's <see cref="ShrineTemplate"/>.
	/// </summary>
	[HandlesInteractable(typeof(Shrine))]
	public class ShrineHandler : IInteractableHandler
	{
		private readonly IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour> server;

		public ShrineHandler(IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour> server)
		{
			this.server = server;
		}

		public void HandleInteraction(IInteractable interactable, IPlayerCharacter character, ISceneObject sceneObject, IInteractableSystem interactableSystem)
		{
			IShrine shrine = interactable as IShrine;
			if (shrine == null || shrine.Template == null)
			{
				return;
			}

			ShrineTemplate template = shrine.Template;

			if (!character.TryGet(out ICharacterAttributeController attributeController))
			{
				return;
			}

			// Heal health
			if (template.HealHealth)
			{
				if (attributeController.TryGetHealthAttribute(out CharacterResourceAttribute health))
				{
					float healAmount = health.FinalValue * template.HealthHealPercent;
					health.AddToCurrentValue(healAmount);
				}
			}

			// Heal mana
			if (template.HealMana)
			{
				if (attributeController.TryGetManaAttribute(out CharacterResourceAttribute mana))
				{
					float healAmount = mana.FinalValue * template.ManaHealPercent;
					mana.AddToCurrentValue(healAmount);
				}
			}

			// Apply buff
			if (template.Buff != null)
			{
				if (character.TryGet(out IBuffController buffController))
				{
					for (int i = 0; i < template.BuffStackCount; i++)
					{
						buffController.Apply(template.Buff);
					}
				}
			}

			// Notify the client for VFX/SFX feedback
			server.NetworkWrapper.Broadcast(character.Owner, new ShrineBroadcast()
			{
				InteractableID = sceneObject.ID,
				TemplateID = template.ID,
			}, true, Channel.Reliable);

			// Increment achievement
			if (shrine.AchievementTemplate != null &&
				character.TryGet(out IAchievementController achievementController))
			{
				achievementController.Increment(shrine.AchievementTemplate, 1);
			}
		}
	}
}