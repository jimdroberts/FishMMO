using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Logging;
using UnityEngine;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Manages faction relationships and updates, broadcasting faction changes to player clients.
	/// </summary>
	[CreateAssetMenu(fileName = "FactionSystem", menuName = "FishMMO/Server/SceneServer/Faction System", order = 1)]
	public class FactionSystem : ServerBehaviour, IFactionSystem
	{
		/// <summary>
		/// Initializes the faction system, subscribing to faction update events.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			if (Server == null)
			{
				Log.Error("FactionSystem", "InitializeOnce: Server is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			// Faction events
			IFactionController.OnUpdateFaction += IFactionController_OnUpdateFaction;

			Log.Debug("FactionSystem", "Initialized");
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Cleans up the faction system, unsubscribing from faction update events.
		/// </summary>
		public override void OnDeinitialize()
		{
			if (Server == null)
			{
				Log.Error("FactionSystem", "OnDeinitialize: Server is null");
				return;
			}

			// Faction events
			IFactionController.OnUpdateFaction -= IFactionController_OnUpdateFaction;
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