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

[assembly: FishMMO.Server.Implementation.World.SceneServer.CharacterPersistence(typeof(FishMMO.Server.Implementation.World.SceneServer.AchievementPersistence))]

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Achievement progress in a character's save and load: one row per achievement, written when its progress
	/// changes and versioned so a stale write cannot overwrite a newer one.
	/// </summary>
	/// <remarks>
	/// Achievements were once fetched on login and never written back, so progress reset to the loaded
	/// values every session. The save leg is shaped like the ability one: <see cref="Achievement.MarkChanged"/>
	/// advances the version on mutation, <see cref="Capture"/> advances it again, and the confirmed write
	/// clears the mark only when the version still matches.
	/// </remarks>
	public sealed class AchievementPersistence : CharacterPersistenceParticipant
	{
		/// <inheritdoc />
		public override string Name => "achievements";

		/// <inheritdoc />
		public override CharacterRows CreateRows() => new CharacterRows<CharacterAchievementData>(row => row.CharacterID);

		/// <inheritdoc />
		public override async Task<CharacterFetch> FetchAsync(ICharacterPersistenceHost host, long characterID)
		{
			if (host.Services == null || !host.Services.TryGet<ICharacterAchievementService>(out var achievementService))
			{
				return CharacterFetch.Ok(null);
			}
			var result = await achievementService.FetchAsync(characterID);
			return result.IsSuccess
				? CharacterFetch.Ok(result.Data)
				: CharacterFetch.Failed(result.ErrorCode.ToString(), result.ErrorMessage);
		}

		/// <inheritdoc />
		public override void Apply(ICharacter character, object fetched)
		{
			if (!(fetched is IReadOnlyList<CharacterAchievementData> achievementData) || achievementData.Count == 0 ||
				!character.TryGet(out IAchievementController achievementController))
			{
				return;
			}
			foreach (CharacterAchievementData achievement in achievementData)
			{
				achievementController.SetAchievement(achievement.TemplateID, achievement.Tier, achievement.Value, true);
				if (achievementController.Achievements.TryGetValue(achievement.TemplateID, out Achievement ach))
				{
					ach.Version = achievement.Version;
				}
			}
		}

		/// <inheritdoc />
		public override void Capture(ICharacter character, CharacterRows rows)
		{
			if (!character.TryGet(out IAchievementController achievementController) ||
				achievementController.Achievements.Count == 0)
			{
				return;
			}
			List<CharacterAchievementData> achievements = ((CharacterRows<CharacterAchievementData>)rows).Items;
			foreach (var kvp in achievementController.Achievements)
			{
				Achievement achievement = kvp.Value;
				if (achievement == null || !achievement.PersistenceDirty)
				{
					continue;
				}
				/* Keyed by the dictionary key, not achievement.Template.ID: a template this
				 * server cannot resolve (a partial content rollout) still has progress worth
				 * writing, and the key is the same value. */
				achievement.Version++;
				achievements.Add(new CharacterAchievementData(
					id: 0,
					version: achievement.Version,
					characterID: character.ID,
					templateID: kvp.Key,
					tier: achievement.CurrentTier,
					value: achievement.CurrentValue));
			}
		}

		/// <inheritdoc />
		public override async Task<bool> SaveAsync(ICharacterPersistenceHost host, CharacterRows rows, IReadOnlyCollection<CharacterSessionLeaseData> claims)
		{
			List<CharacterAchievementData> achievements = ((CharacterRows<CharacterAchievementData>)rows).Items;
			try
			{
				if (host.Services == null || !host.Services.TryGet<ICharacterAchievementService>(out var achievementService))
				{
					return true;
				}
				DatabaseResult<BulkWriteResult> result = await achievementService.PersistOwnedAsync(achievements, claims);
				bool written = await BulkWriteReporting.ReportAsync("CharacterSystem", "Achievement save", result);
				// Same rule as abilities: filtered rows were never offered to the database.
				if (BulkWriteReporting.MayClearDirtyMarks(result))
				{
					host.TryEnqueueMainThread(() => MarkPersisted(host, achievements));
				}
				return written || !result.IsTransient;
			}
			catch (Exception ex)
			{
				await Log.Error("CharacterSystem", $"Achievement save failed: {ex}");
				return false;
			}
		}

		/// <summary>Clears the dirty mark on every achievement a completed write covered. Main thread.</summary>
		private static void MarkPersisted(ICharacterPersistenceHost host, List<CharacterAchievementData> achievements)
		{
			for (int i = 0; i < achievements.Count; ++i)
			{
				CharacterAchievementData saved = achievements[i];
				if (!host.TryGetResidentCharacter(saved.CharacterID, out IPlayerCharacter character) ||
					!character.TryGet(out IAchievementController achievementController))
				{
					continue;
				}
				if (achievementController.Achievements.TryGetValue(saved.TemplateID, out Achievement achievement) &&
					achievement != null)
				{
					achievement.MarkPersisted(saved.Version);
				}
			}
		}
	}
}
