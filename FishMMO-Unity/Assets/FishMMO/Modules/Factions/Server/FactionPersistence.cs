using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Logging;
using FishMMO.Server.Core;
using FishMMO.Shared;
using FishMMO.Shared.Core;

[assembly: FishMMO.Server.Implementation.World.SceneServer.CharacterPersistence(typeof(FishMMO.Server.Implementation.World.SceneServer.FactionPersistence))]

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Faction standings in the character load and save (Factions module), over the existing <c>character_factions</c>
	/// table. Was hard-coded in <c>CharacterSystem.Loading</c>/<c>Saving</c>; removing the module leaves the table and its
	/// rows as they are.
	/// </summary>
	/// <remarks>
	/// Same shape as achievements: <see cref="Faction.MarkChanged"/> on mutation, a version bump at capture, and a
	/// confirmation that clears only a still-matching version. Restored as persisted, so nothing is dirty after a login.
	/// </remarks>
	public sealed class FactionPersistence : CharacterPersistenceParticipant
	{
		public override string Name => "factions";

		public override CharacterRows CreateRows() => new CharacterRows<CharacterFactionData>(row => row.CharacterID, 8);

		public override async Task<CharacterFetch> FetchAsync(ICharacterPersistenceHost host, long characterID)
		{
			if (host.Services == null || !host.Services.TryGet<ICharacterFactionService>(out var factionService))
			{
				return CharacterFetch.Ok(null);
			}
			var result = await factionService.FetchAsync(characterID);
			return result.IsSuccess
				? CharacterFetch.Ok(result.Data)
				: CharacterFetch.Failed(result.ErrorCode.ToString(), result.ErrorMessage);
		}

		public override void Apply(ICharacter character, object fetched)
		{
			if (!(fetched is IReadOnlyList<CharacterFactionData> factionData) || factionData.Count == 0 ||
				!character.TryGet(out IFactionStandings factionController))
			{
				return;
			}
			foreach (CharacterFactionData faction in factionData)
			{
				factionController.SetFaction(faction.TemplateID, faction.Value, true);
				if (factionController.Factions.TryGetValue(faction.TemplateID, out Faction fac))
				{
					fac.Version = faction.Version;
					// Restored, not changed: the database already holds this.
					fac.MarkPersisted(faction.Version);
				}
			}
		}

		public override void Capture(ICharacter character, CharacterRows rows)
		{
			if (!character.TryGet(out IFactionStandings factionController) || factionController.Factions.Count == 0)
			{
				return;
			}
			List<CharacterFactionData> factions = ((CharacterRows<CharacterFactionData>)rows).Items;
			foreach (var kvp in factionController.Factions)
			{
				Faction faction = kvp.Value;
				if (faction == null || !faction.PersistenceDirty)
				{
					continue;
				}
				faction.Version++;
				factions.Add(new CharacterFactionData(
					id: 0,
					version: faction.Version,
					characterID: character.ID,
					templateID: kvp.Key,
					value: faction.Value));
			}
		}

		public override async Task<bool> SaveAsync(ICharacterPersistenceHost host, CharacterRows rows, IReadOnlyCollection<CharacterSessionLeaseData> claims)
		{
			List<CharacterFactionData> factions = ((CharacterRows<CharacterFactionData>)rows).Items;
			try
			{
				if (factions.Count == 0 || host.Services == null || !host.Services.TryGet<ICharacterFactionService>(out var factionService))
				{
					return true;
				}
				DatabaseResult<BulkWriteResult> result = await factionService.PersistOwnedAsync(factions, claims);
				bool written = await BulkWriteReporting.ReportAsync("CharacterSystem", "Faction save", result);
				if (BulkWriteReporting.MayClearDirtyMarks(result))
				{
					host.TryEnqueueMainThread(() => MarkPersisted(host, factions));
				}
				return written || !result.IsTransient;
			}
			catch (Exception ex)
			{
				await Log.Error("CharacterSystem", $"Faction save failed: {ex}");
				return false;
			}
		}

		private static void MarkPersisted(ICharacterPersistenceHost host, List<CharacterFactionData> factions)
		{
			for (int i = 0; i < factions.Count; ++i)
			{
				CharacterFactionData saved = factions[i];
				if (!host.TryGetResidentCharacter(saved.CharacterID, out IPlayerCharacter character) ||
					!character.TryGet(out IFactionStandings factionController))
				{
					continue;
				}
				if (factionController.Factions.TryGetValue(saved.TemplateID, out Faction faction) && faction != null)
				{
					faction.MarkPersisted(saved.Version);
				}
			}
		}
	}
}
