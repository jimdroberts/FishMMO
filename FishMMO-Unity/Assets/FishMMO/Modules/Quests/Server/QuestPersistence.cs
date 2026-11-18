using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Shared;
using FishMMO.Shared.Core;

[assembly: FishMMO.Server.Implementation.World.SceneServer.CharacterPersistence(typeof(FishMMO.Server.Implementation.World.SceneServer.QuestPersistenceParticipant))]

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Puts a character's quests on it as the character loads, each at its row's version.
	/// </summary>
	/// <remarks>
	/// Load only: <see cref="QuestSystem"/> writes a quest when it changes, not the character save. This was hard-coded in
	/// <c>CharacterSystem.Loading</c>; a failed read abandons the load, as it did there.
	/// </remarks>
	public sealed class QuestPersistenceParticipant : CharacterPersistenceParticipant
	{
		/// <inheritdoc />
		public override string Name => "quests";

		/// <inheritdoc />
		public override CharacterRows CreateRows() => new CharacterRows<CharacterQuestData>(row => row.CharacterID);

		/// <inheritdoc />
		public override async Task<CharacterFetch> FetchAsync(ICharacterPersistenceHost host, long characterID)
		{
			if (host.Services == null || !host.Services.TryGet<ICharacterQuestService>(out var questService))
			{
				return CharacterFetch.Ok(null);
			}
			var result = await questService.FetchAsync(characterID);
			return result.IsSuccess
				? CharacterFetch.Ok(result.Data)
				: CharacterFetch.Failed(result.ErrorCode.ToString(), result.ErrorMessage);
		}

		/// <inheritdoc />
		public override void Apply(ICharacter character, object fetched)
		{
			if (!(fetched is IReadOnlyList<CharacterQuestData> rows) || rows.Count == 0 ||
				!character.TryGet(out IQuestController questController))
			{
				return;
			}
			foreach (CharacterQuestData quest in rows)
			{
				QuestTemplate questTemplate = QuestTemplate.Get<QuestTemplate>(quest.TemplateID);
				if (questTemplate == null)
				{
					continue;
				}

				long[] objectiveValues = ParseObjectiveValues(quest.ObjectiveValues);
				questController.SetQuest(questTemplate, (QuestStatus)quest.Status, objectiveValues);

				/* The row's version, restored exactly as achievements and factions restore theirs.
				 *
				 * It used to be dropped here, so every loaded quest restarted at version 0 while
				 * its row stood at whatever the last session had reached. Each write this session
				 * then quoted a version the row already outranked: progress and completion were
				 * refused as stale (at Debug, so nobody saw), and the turn-in's delete was refused
				 * the same way — the rewards were paid, the quest left memory, and the row came
				 * back as Complete at the next login to be turned in again, every login. */
				if (questController.Quests.TryGetValue(questTemplate.Name, out QuestInstance questInstance))
				{
					questInstance.Version = quest.Version;
				}
			}
			
		}

		/// <summary>
		/// Parses a comma-separated objective values string into a long array.
		/// </summary>
		public static long[] ParseObjectiveValues(string objectiveValues)
		{
			if (string.IsNullOrEmpty(objectiveValues))
			{
				return Array.Empty<long>();
			}

			string[] parts = objectiveValues.Split(',');
			long[] values = new long[parts.Length];
			for (int i = 0; i < parts.Length; i++)
			{
				long.TryParse(parts[i], out values[i]);
			}
			return values;
		}

		/// <inheritdoc />
		public override void Capture(ICharacter character, CharacterRows rows)
		{
		}

		/// <inheritdoc />
		public override Task<bool> SaveAsync(ICharacterPersistenceHost host, CharacterRows rows, IReadOnlyCollection<CharacterSessionLeaseData> claims)
		{
			return Task.FromResult(true);
		}
	}
}
