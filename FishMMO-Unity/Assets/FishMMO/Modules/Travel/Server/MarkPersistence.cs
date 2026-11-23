using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Logging;
using FishMMO.Shared;
using FishMMO.Shared.Core;

[assembly: FishMMO.Server.Implementation.World.SceneServer.CharacterPersistence(typeof(FishMMO.Server.Implementation.World.SceneServer.MarkPersistenceParticipant))]

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Loads and saves a character's one marked location (<c>character_marks</c>, issue #241) with the character.
	/// </summary>
	/// <remarks>
	/// Read at load, written on a save only when the mark changed (<see cref="TravelMarkController.PersistenceDirty"/>). The
	/// write replaces the row; a character is resident on one scene server at a time and its saves run on its own keyed
	/// lane before its release, so there is no second writer to arbitrate against.
	/// </remarks>
	public sealed class MarkPersistenceParticipant : CharacterPersistenceParticipant
	{
		public override string Name => "marks";

		public override CharacterRows CreateRows() => new CharacterRows<CharacterMarkData>(row => row.CharacterID);

		public override async Task<CharacterFetch> FetchAsync(ICharacterPersistenceHost host, long characterID)
		{
			if (host.Services == null || !host.Services.TryGet<ICharacterMarkService>(out var markService))
			{
				return CharacterFetch.Ok(null);
			}
			DatabaseResult<CharacterMarkData?> result = await markService.FetchAsync(characterID);
			return result.IsSuccess
				? CharacterFetch.Ok(result.Data)
				: CharacterFetch.Failed(result.ErrorCode.ToString(), result.ErrorMessage);
		}

		public override void Apply(ICharacter character, object fetched)
		{
			if (!(fetched is CharacterMarkData mark) || !character.TryGet(out ITravelMarkController marks))
			{
				return;
			}
			marks.Restore(mark.SceneName, mark.SceneID, new Vector3(mark.X, mark.Y, mark.Z), mark.Heading);
		}

		public override void Capture(ICharacter character, CharacterRows rows)
		{
			if (character == null || !character.TryGet(out ITravelMarkController marks) || !marks.PersistenceDirty || !marks.HasMark)
			{
				return;
			}
			Vector3 p = marks.Position;
			((CharacterRows<CharacterMarkData>)rows).Items.Add(
				new CharacterMarkData(character.ID, marks.SceneName, marks.SceneID, p.x, p.y, p.z, marks.Heading));
		}

		public override async Task<bool> SaveAsync(ICharacterPersistenceHost host, CharacterRows rows, IReadOnlyCollection<CharacterSessionLeaseData> claims)
		{
			List<CharacterMarkData> marks = ((CharacterRows<CharacterMarkData>)rows).Items;
			if (marks.Count == 0 || host.Services == null || !host.Services.TryGet<ICharacterMarkService>(out var markService))
			{
				return true;
			}

			bool allLanded = true;
			var landed = new List<CharacterMarkData>(marks.Count);
			foreach (CharacterMarkData mark in marks)
			{
				try
				{
					DatabaseResult result = await markService.SaveAsync(mark);
					if (result.IsSuccess)
					{
						landed.Add(mark);
					}
					else
					{
						allLanded = false;
						await Log.Warning("CharacterSystem", $"Mark save failed for character {mark.CharacterID}: [{result.ErrorCode}] {result.ErrorMessage}");
					}
				}
				catch (Exception ex)
				{
					allLanded = false;
					await Log.Error("CharacterSystem", $"Mark save threw for character {mark.CharacterID}: {ex}");
				}
			}

			if (landed.Count > 0)
			{
				host.TryEnqueueMainThread(() =>
				{
					foreach (CharacterMarkData mark in landed)
					{
						if (host.TryGetResidentCharacter(mark.CharacterID, out IPlayerCharacter character) &&
							character.TryGet(out ITravelMarkController controller))
						{
							controller.MarkPersisted(mark.SceneName, mark.SceneID, new Vector3(mark.X, mark.Y, mark.Z), mark.Heading);
						}
					}
				});
			}
			return allLanded;
		}
	}
}
