using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// The Duels module's hooks into the core client: "Challenge to Duel" on a player in the world (a
	/// <see cref="CharacterMenuEntries"/> entry, after Trade), and the "Duels" section of the character sheet (a
	/// <see cref="CharacterSheetSections"/> entry: the player's own rating and record, which the server sends on arrival
	/// and after every duel). The duel HUD (<see cref="UITKDuel"/>) sends the challenge and shows everything that follows.
	/// </summary>
	public static class DuelClient
	{
		/// <summary>The menu entry's id.</summary>
		public const string EntryId = "duels.challenge";

		/// <summary>After Trade (120), before Report.</summary>
		public const int Order = 125;

		/// <summary>The character sheet section's id.</summary>
		public const string SheetSectionId = "duels.record";

		/// <summary>The character sheet section's place among module sections.</summary>
		public const int SheetOrder = 100;

		/// <summary>The record last sent for the local character; <see cref="RecordCharacterID"/> 0 until one arrives.</summary>
		private static long RecordCharacterID;
		private static int RecordRating;
		private static int RecordWins;
		private static int RecordLosses;
		private static int RecordDraws;

		/// <summary>Registers the menu entry. Runs before the first scene loads; safe to run again.</summary>
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		public static void Install()
		{
			// World only: a duel is fought where both stand, so a roster row (who may be anywhere) offers none.
			CharacterMenuEntries.Register(EntryId, "Challenge to Duel", Order, target =>
			{
				if (UIManager.TryGetTK(UITKDuel.ControlName, out UITKDuel duel))
				{
					duel.Challenge(target.CharacterID, target.Name);
				}
			}, target => target.Source == CharacterMenuSource.World);

			CharacterSheetSections.Register(SheetSectionId, "Duels", SheetOrder, FillSheet);
		}

		/// <summary>Keeps the local character's record for the sheet, and tells an open sheet it moved.</summary>
		public static void SetRecord(long characterID, DuelRecordBroadcast record)
		{
			RecordCharacterID = characterID;
			RecordRating = record.Rating;
			RecordWins = record.Wins;
			RecordLosses = record.Losses;
			RecordDraws = record.Draws;
			CharacterSheetSections.NotifyChanged();
		}

		/// <summary>Forgets the record (the character left the world).</summary>
		public static void ClearRecord()
		{
			if (RecordCharacterID == 0)
			{
				return;
			}
			RecordCharacterID = 0;
			CharacterSheetSections.NotifyChanged();
		}

		private static bool FillSheet(IPlayerCharacter subject, List<CharacterSheetRow> rows)
		{
			return subject != null && FillSheetFor(subject.ID, rows);
		}

		/// <summary>The sheet's Duels rows: only for the character whose record this client holds, which is its own.</summary>
		public static bool FillSheetFor(long characterID, List<CharacterSheetRow> rows)
		{
			if (RecordCharacterID == 0 || characterID != RecordCharacterID)
			{
				return false;
			}
			rows.Add(new CharacterSheetRow("Rating", RecordRating.ToString(),
				"Duel rating (Elo), moved by every duel. Separate from the arena rating. Starts at 1500; beating a stronger " +
				"duellist gains more, losing to a weaker one costs more. Never below 100."));
			rows.Add(new CharacterSheetRow("Wins", RecordWins.ToString(), "Duels won."));
			rows.Add(new CharacterSheetRow("Losses", RecordLosses.ToString(), "Duels lost: beaten, yielded, stepped off the ground, left the world, or killed by something else."));
			rows.Add(new CharacterSheetRow("Draws", RecordDraws.ToString(), "Duels with no winner: the time ran out, or both stepped off the ground."));
			return true;
		}
	}
}
