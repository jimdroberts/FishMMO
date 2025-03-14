using FishMMO.Shared;

namespace FishMMO.Server
{
	public class FactionSystem : ServerBehaviour
	{
		public override void InitializeOnce()
		{
			if (ServerManager != null)
			{
				IFactionController.OnUpdateFaction += IFactionController_OnUpdateFaction;
			}
			else
			{
				enabled = false;
			}
		}

		public override void Destroying()
		{
			if (ServerManager != null)
			{
				IFactionController.OnUpdateFaction -= IFactionController_OnUpdateFaction;
			}
		}

		private void IFactionController_OnUpdateFaction(ICharacter character, Faction faction)
		{
			if (character == null || faction == null)
			{
				return;
			}

			IPlayerCharacter playerCharacter = character as IPlayerCharacter;
			if (playerCharacter == null)
			{
				return;
			}

			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
			if (dbContext == null)
			{
				return;
			}

			playerCharacter.Owner.Broadcast(new FactionUpdateBroadcast()
			{
				TemplateID = faction.Template.ID,
				NewValue = faction.Value,
			});
		}
	}
}
