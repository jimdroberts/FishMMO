using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Logging;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;

[assembly: FishMMO.Server.Implementation.World.SceneServer.CharacterPersistence(typeof(FishMMO.Server.Implementation.World.SceneServer.HotkeyPersistenceParticipant))]

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// The hotkey bar's part of the character load and save (moved out of <c>CharacterSystem</c> with the Hotkeys module,
	/// 2026-11-20). The bar loads with the character; a resident's changes are written by the hotkey system's own pump,
	/// so the periodic save captures nothing, and a departing character's bar is written whole from the live bar
	/// (<see cref="CaptureDeparture"/>), keyed behind anything the pump queued for it.
	/// </summary>
	public sealed class HotkeyPersistenceParticipant : CharacterPersistenceParticipant
	{
		/// <inheritdoc />
		public override string Name => "hotkeys";

		/// <inheritdoc />
		public override bool SavesPerCharacter => true;

		/// <inheritdoc />
		public override CharacterRows CreateRows() => new CharacterRows<CharacterHotkeyData>(row => row.CharacterID);

		/// <inheritdoc />
		public override async Task<CharacterFetch> FetchAsync(ICharacterPersistenceHost host, long characterID)
		{
			if (host.Services == null || !host.Services.TryGet<ICharacterHotkeyService>(out var hotkeyService))
			{
				return CharacterFetch.Ok(null);
			}
			var result = await hotkeyService.FetchAsync(characterID);
			return result.IsSuccess
				? CharacterFetch.Ok(result.Data)
				: CharacterFetch.Failed(result.ErrorCode.ToString(), result.ErrorMessage);
		}

		/// <inheritdoc />
		public override void Apply(ICharacter character, object fetched)
		{
			if (!(fetched is IReadOnlyList<CharacterHotkeyData> hotkeyData) || hotkeyData.Count < 1 || character == null)
			{
				return;
			}
			List<HotkeyData> slots = character.Component<HotkeyBar>().Slots;
			if (slots == null)
			{
				return;
			}
			foreach (CharacterHotkeyData hotkey in hotkeyData)
			{
				if (hotkey.Slot >= 0 && hotkey.Slot < slots.Count)
				{
					slots[hotkey.Slot] = new HotkeyData()
					{
						Type = hotkey.Type,
						Slot = hotkey.Slot,
						ReferenceID = hotkey.ReferenceID,
					};
				}
			}
		}

		/// <summary>Nothing on the periodic save: the hotkey system's pump writes a resident's changes.</summary>
		public override void Capture(ICharacter character, CharacterRows rows)
		{
		}

		/// <summary>
		/// The departing character's whole bar, from the live bar, through the hotkey system (it also drops whatever was still
		/// staged for the character, so nothing older is written after it).
		/// </summary>
		public override void CaptureDeparture(ICharacterPersistenceHost host, ICharacter character, CharacterRows rows)
		{
			if (!(character is IPlayerCharacter player) ||
				!(rows is CharacterRows<CharacterHotkeyData> hotkeyRows) ||
				!host.TryGetSystem(out IHotkeySystem hotkeySystem) || hotkeySystem == null)
			{
				return;
			}
			List<CharacterHotkeyData> bar = hotkeySystem.TakeDepartingBar(player);
			if (bar != null)
			{
				hotkeyRows.Items.AddRange(bar);
			}
		}

		/// <summary>
		/// Writes a departing character's bar under the claim it was captured with. No dirty marks to clear: the bar is
		/// written whole and the character is on its way out. A superseded row is a newer bar already stored (the pump's,
		/// taken later) and is fine.
		/// </summary>
		public override async Task<bool> SaveAsync(ICharacterPersistenceHost host, CharacterRows rows, IReadOnlyCollection<CharacterSessionLeaseData> claims)
		{
			try
			{
				if (!(rows is CharacterRows<CharacterHotkeyData> hotkeys) || hotkeys.Count < 1 ||
					host.Services == null || !host.Services.TryGet<ICharacterHotkeyService>(out var hotkeyService))
				{
					return true;
				}
				DatabaseResult<BulkWriteResult> result = await hotkeyService.PersistOwnedAsync(hotkeys.Items, claims);
				bool written = await BulkWriteReporting.ReportAsync("CharacterSystem", "Hotkey bar save", result);
				return written || !result.IsTransient;
			}
			catch (Exception ex)
			{
				await Log.Error("CharacterSystem", $"Hotkey bar save failed: {ex}");
				return false;
			}
		}
	}
}
