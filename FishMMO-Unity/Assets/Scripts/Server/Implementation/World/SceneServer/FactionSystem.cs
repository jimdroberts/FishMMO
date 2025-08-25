using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;

namespace FishMMO.Server.Implementation.SceneServer
{
	public class FactionSystem : ServerBehaviour, IFactionSystem
	{
		/// <summary>
		/// Initializes the faction system, subscribing to faction update events.
		/// </summary>
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

		/// <summary>
		/// Cleans up the faction system, unsubscribing from faction update events.
		/// </summary>
		public override void Destroying()
		{
			if (ServerManager != null)
			{
				IFactionController.OnUpdateFaction -= IFactionController_OnUpdateFaction;
			}
		}

		/// <summary>
		/// Handles faction update events for characters, validates input, and broadcasts faction changes to the player client.
		/// </summary>
		/// <param name="character">The character whose faction was updated.</param>
		/// <param name="faction">The updated faction data.</param>
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

			using var dbContext = Server.CoreServer.NpgsqlDbContextFactory.CreateDbContext();
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