using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Logging;
using FishMMO.Server.Core;
using FishMMO.Shared.Core;

[assembly: FishMMO.Server.Implementation.World.SceneServer.CharacterPersistence(typeof(FishMMO.Server.Implementation.World.SceneServer.ArchetypePersistenceParticipant))]

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Loads and saves a character's archetype (one row per character, versioned) through the character save.
	/// </summary>
	/// <remarks>
	/// Moved from <c>CharacterSystem.Loading</c> and <c>Saving</c> with the archetype (2026-11-17), unchanged: the row is
	/// written when the archetype changed since the database last confirmed it, and the dirty mark is cleared only by a
	/// write that landed.
	/// </remarks>
	public sealed class ArchetypePersistenceParticipant : CharacterPersistenceParticipant
	{
		/// <inheritdoc />
		public override string Name => "archetype";

		/// <inheritdoc />
		public override CharacterRows CreateRows() => new CharacterRows<CharacterArchetypeData>(row => row.CharacterID);

		/// <inheritdoc />
		public override async Task<CharacterFetch> FetchAsync(ICharacterPersistenceHost host, long characterID)
		{
			if (host.Services == null || !host.Services.TryGet<ICharacterArchetypeService>(out var archetypeService))
			{
				return CharacterFetch.Ok(null);
			}
			var result = await archetypeService.FetchAsync(characterID);
			return result.IsSuccess
				? CharacterFetch.Ok(result.Data)
				: CharacterFetch.Failed(result.ErrorCode.ToString(), result.ErrorMessage);
		}

		/// <inheritdoc />
		/// <remarks>One row per character; if there are several, the newest version wins.</remarks>
		public override void Apply(ICharacter character, object fetched)
		{
			if (!(fetched is IReadOnlyList<CharacterArchetypeData> rows) || rows.Count == 0 ||
				!character.TryGet(out IArchetypeController controller))
			{
				return;
			}
			CharacterArchetypeData newest = rows[0];
			for (int i = 1; i < rows.Count; ++i)
			{
				if (rows[i].Version > newest.Version)
				{
					newest = rows[i];
				}
			}
			controller.Restore(newest.TemplateID, newest.Version);
		}

		/// <inheritdoc />
		/// <remarks>Main thread. Appends the row only when the archetype changed since the database last confirmed it.</remarks>
		public override void Capture(ICharacter character, CharacterRows rows)
		{
			if (character == null || !character.TryGet(out IArchetypeController controller) ||
				!controller.PersistenceDirty || controller.Template == null)
			{
				return;
			}

			controller.Version++;
			((CharacterRows<CharacterArchetypeData>)rows).Items.Add(new CharacterArchetypeData(
				id: 0,
				version: controller.Version,
				characterID: character.ID,
				templateID: controller.Template.ID));
		}

		/// <inheritdoc />
		public override async Task<bool> SaveAsync(ICharacterPersistenceHost host, CharacterRows rows, IReadOnlyCollection<CharacterSessionLeaseData> claims)
		{
			List<CharacterArchetypeData> archetypes = ((CharacterRows<CharacterArchetypeData>)rows).Items;
			try
			{
				if (archetypes.Count == 0 || host.Services == null || !host.Services.TryGet<ICharacterArchetypeService>(out var archetypeService))
				{
					return true;
				}

				DatabaseResult<BulkWriteResult> result = await archetypeService.PersistOwnedAsync(archetypes, claims);
				bool written = await BulkWriteReporting.ReportAsync("CharacterSystem", "Archetype save", result);

				if (BulkWriteReporting.MayClearDirtyMarks(result))
				{
					host.TryEnqueueMainThread(() => MarkPersisted(host, archetypes));
				}

				return written || !result.IsTransient;
			}
			catch (Exception ex)
			{
				await Log.Error("CharacterSystem", $"Archetype save failed: {ex}");
				return false;
			}
		}

		/// <summary>Clears the dirty mark on every archetype a completed write covered. Main thread.</summary>
		private static void MarkPersisted(ICharacterPersistenceHost host, List<CharacterArchetypeData> archetypes)
		{
			for (int i = 0; i < archetypes.Count; ++i)
			{
				CharacterArchetypeData saved = archetypes[i];
				if (host.TryGetResidentCharacter(saved.CharacterID, out IPlayerCharacter character) &&
					character.TryGet(out IArchetypeController controller))
				{
					controller.MarkPersisted(saved.Version);
				}
			}
		}
	}
}
