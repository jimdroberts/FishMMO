using System.Collections.Generic;
using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Server.Core;
using FishMMO.Shared;

[assembly: FishMMO.Server.Implementation.LoginServer.CharacterCreationContributor(typeof(FishMMO.Server.Implementation.LoginServer.FactionCreationContributor))]

namespace FishMMO.Server.Implementation.LoginServer
{
	/// <summary>
	/// A new character's starting standings (Factions module): the allies, neutrals and enemies of the faction its race
	/// starts in (<see cref="FactionTemplate.StartingRaces"/>), at maximum, zero and minimum. Was hard-coded in
	/// <c>CharacterCreateSystem</c>.
	/// </summary>
	public sealed class FactionCreationContributor : CharacterCreationContributor
	{
		public override string Name => "factions";

		public override object Prepare(RaceTemplate race)
		{
			FactionTemplate initial = FactionTemplate.InitialFor(race);
			if (initial == null)
			{
				return null;
			}
			var rows = new List<KeyValuePair<int, int>>();
			Add(rows, initial.DefaultAllied, FactionTemplate.Maximum);
			Add(rows, initial.DefaultNeutral, 0);
			Add(rows, initial.DefaultHostile, FactionTemplate.Minimum);
			return rows.Count > 0 ? rows : null;
		}

		private static void Add(List<KeyValuePair<int, int>> rows, IEnumerable<FactionTemplate> factions, int value)
		{
			if (factions == null)
			{
				return;
			}
			foreach (FactionTemplate faction in factions)
			{
				if (faction != null)
				{
					rows.Add(new KeyValuePair<int, int>(faction.ID, value));
				}
			}
		}

		public override async Task<bool> PersistAsync(IDatabaseServiceRegistry services, long characterID, object prepared)
		{
			if (!(prepared is List<KeyValuePair<int, int>> rows) || rows.Count == 0)
			{
				return true;
			}
			if (services == null || !services.TryGet<ICharacterFactionService>(out var factionService))
			{
				return false;
			}
			var factions = new List<CharacterFactionData>(rows.Count);
			foreach (KeyValuePair<int, int> row in rows)
			{
				factions.Add(new CharacterFactionData(id: 0, version: 1, characterID: characterID, templateID: row.Key, value: row.Value));
			}
			/* Must land in full. These rows are being created for the first time, so nothing newer can exist to supersede
			 * them: a short write means rows were dropped. */
			return await BulkWriteReporting.RequireCompleteAsync(
				"CharacterCreateSystem", "Starting factions", await factionService.PersistAsync(factions), $"character {characterID}");
		}
	}
}
