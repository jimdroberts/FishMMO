using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Logging;
using FishMMO.Shared;
using FishMMO.Shared.Core;

[assembly: FishMMO.Server.Implementation.World.SceneServer.CharacterPersistence(typeof(FishMMO.Server.Implementation.World.SceneServer.MercenaryPersistenceParticipant))]

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Loads and saves a character's hired mercenary (<c>character_mercenaries</c>, issue #135) with the character, so it
	/// comes back as it left: after a logout, and on the far side of a scene change.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A live mercenary is captured on every save (its health and buffs move), a resting one only when it changed. Saved
	/// per character, behind anything queued for the character on the persistence lane: the hire, rest, return, release
	/// and death writes the mercenary system makes go on the same lane, so a save captured before a death can never land
	/// after the death's delete and bring the mercenary back.
	/// </para>
	/// <para>
	/// The departure save runs before the character leaves the world (and before the mercenary system despawns the
	/// mercenary), which is what lets a live mercenary's state reach the database before the session is released.
	/// </para>
	/// </remarks>
	public sealed class MercenaryPersistenceParticipant : CharacterPersistenceParticipant
	{
		/// <summary>One captured row, with the controller's change count when it was captured.</summary>
		public sealed class Row
		{
			/// <summary>The row; a kit of 0 marks a release or death, and the save deletes the character's row.</summary>
			public CharacterMercenaryData Data;
			/// <summary><see cref="MercenaryController.ChangeCount"/> at capture: the mark is cleared only if nothing changed since.</summary>
			public int ChangeCount;
		}

		/// <inheritdoc />
		public override string Name => "mercenaries";

		/// <inheritdoc />
		public override bool SavesPerCharacter => true;

		/// <inheritdoc />
		public override CharacterRows CreateRows() => new CharacterRows<Row>(row => row.Data.CharacterID);

		/// <inheritdoc />
		public override async Task<CharacterFetch> FetchAsync(ICharacterPersistenceHost host, long characterID)
		{
			if (host.Services == null || !host.Services.TryGet<ICharacterMercenaryService>(out var service))
			{
				return CharacterFetch.Ok(null);
			}
			DatabaseResult<CharacterMercenaryData> result = await service.FetchAsync(characterID);
			return result.IsSuccess
				? CharacterFetch.Ok(result.Data)
				: CharacterFetch.Failed(result.ErrorCode.ToString(), result.ErrorMessage);
		}

		/// <inheritdoc />
		public override void Apply(ICharacter character, object fetched)
		{
			if (!(fetched is CharacterMercenaryData data) ||
				!character.TryGet(out IMercenaryController controller) ||
				!(controller is MercenaryController concrete))
			{
				return;
			}
			MercenaryRecords.Apply(data, concrete);
		}

		/// <inheritdoc />
		public override void Capture(ICharacter character, CharacterRows rows)
		{
			if (character == null || character.ID <= 0 ||
				!character.TryGet(out IMercenaryController controller) ||
				!(controller is MercenaryController concrete))
			{
				return;
			}

			Mercenary live = concrete.Live;
			if (!concrete.HasMercenary)
			{
				// Released or dead since the last write: the row goes too. The system's own delete usually got there first.
				if (concrete.PersistenceDirty)
				{
					((CharacterRows<Row>)rows).Items.Add(new Row()
					{
						Data = new CharacterMercenaryData() { CharacterID = character.ID, KitID = 0 },
						ChangeCount = concrete.ChangeCount,
					});
				}
				return;
			}
			if (!concrete.PersistenceDirty && live == null)
			{
				return;
			}

			var timeManager = character.NetworkObject != null && character.NetworkObject.NetworkManager != null
				? character.NetworkObject.NetworkManager.TimeManager
				: null;
			CharacterMercenaryData data = MercenaryRecords.Capture(character.ID, concrete, live, timeManager);
			if (data != null)
			{
				((CharacterRows<Row>)rows).Items.Add(new Row() { Data = data, ChangeCount = concrete.ChangeCount });
			}
		}

		/// <inheritdoc />
		public override async Task<bool> SaveAsync(ICharacterPersistenceHost host, CharacterRows rows, IReadOnlyCollection<CharacterSessionLeaseData> claims)
		{
			List<Row> items = ((CharacterRows<Row>)rows).Items;
			if (items.Count == 0 || host.Services == null || !host.Services.TryGet<ICharacterMercenaryService>(out var service))
			{
				return true;
			}

			bool allLanded = true;
			var landed = new List<Row>(items.Count);
			foreach (Row row in items)
			{
				CharacterMercenaryData item = row.Data;
				try
				{
					DatabaseResult result = item.KitID == 0
						? await service.DeleteAsync(item.CharacterID)
						: await service.SaveAsync(item);
					if (result.IsSuccess)
					{
						landed.Add(row);
					}
					else
					{
						allLanded = false;
						await Log.Warning("CharacterSystem", $"Mercenary save failed for character {item.CharacterID}: [{result.ErrorCode}] {result.ErrorMessage}");
					}
				}
				catch (Exception ex)
				{
					allLanded = false;
					await Log.Error("CharacterSystem", $"Mercenary save threw for character {item.CharacterID}: {ex}");
				}
			}

			if (landed.Count > 0)
			{
				host.TryEnqueueMainThread(() =>
				{
					foreach (Row row in landed)
					{
						// Cleared only when nothing changed since the capture.
						if (host.TryGetResidentCharacter(row.Data.CharacterID, out IPlayerCharacter character) &&
							character.TryGet(out IMercenaryController controller) &&
							controller is MercenaryController concrete &&
							concrete.ChangeCount == row.ChangeCount)
						{
							concrete.PersistenceDirty = false;
						}
					}
				});
			}
			return allLanded;
		}
	}
}
