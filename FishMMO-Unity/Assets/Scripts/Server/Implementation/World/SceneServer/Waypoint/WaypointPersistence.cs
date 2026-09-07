using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// The one place a character's discovered-waypoint pages are turned into rows and merged.
	/// Used by the unlock path (write on change) and by the character system's periodic and
	/// despawn saves (retry of anything still dirty).
	/// </summary>
	/// <remarks>
	/// The database merge is an OR, so both callers may write the same page without either
	/// losing bits, and neither needs to know about the other. What each caller does with the
	/// result — confirming the pages on the main thread — is the same too, hence
	/// <see cref="MarkPersisted"/>.
	/// </remarks>
	public static class WaypointPersistence
	{
		/// <summary>Turns a controller page into its row.</summary>
		public static CharacterWaypointData ToData(long characterID, in WaypointPageSnapshot page)
		{
			return new CharacterWaypointData(characterID, page.SceneName, (short)page.Page, page.Mask);
		}

		/// <summary>
		/// Appends every dirty page of a character's record.
		/// </summary>
		/// <returns>The number of pages appended.</returns>
		public static int AppendDirtyPages(ICharacter character, List<CharacterWaypointData> results, List<WaypointPageSnapshot> scratch)
		{
			if (character == null || results == null || !character.TryGet(out IWaypointController controller))
			{
				return 0;
			}

			scratch.Clear();
			controller.CollectDirtyPages(scratch);
			for (int i = 0; i < scratch.Count; ++i)
			{
				results.Add(ToData(character.ID, scratch[i]));
			}
			return scratch.Count;
		}

		/// <summary>
		/// Merges rows into the database.
		/// </summary>
		/// <returns>True when the merge landed.</returns>
		public static async Task<bool> MergeAsync(ICharacterWaypointService service, List<CharacterWaypointData> pages, string tag)
		{
			if (service == null || pages == null || pages.Count == 0)
			{
				return false;
			}

			try
			{
				DatabaseResult result = await service.MergeAsync(pages);
				if (!result.IsSuccess)
				{
					await Log.Warning(tag, $"Waypoint save failed: [{result.ErrorCode}] {result.ErrorMessage}");
					return false;
				}
				return true;
			}
			catch (Exception ex)
			{
				await Log.Error(tag, $"Waypoint save threw: {ex}");
				return false;
			}
		}

		/// <summary>
		/// Confirms written pages on their characters. Main thread only.
		/// </summary>
		/// <param name="pages">The rows that were merged.</param>
		/// <param name="resolve">Finds a resident character by ID, or null.</param>
		public static void MarkPersisted(List<CharacterWaypointData> pages, Func<long, ICharacter> resolve)
		{
			if (pages == null || resolve == null)
			{
				return;
			}

			for (int i = 0; i < pages.Count; ++i)
			{
				CharacterWaypointData page = pages[i];
				ICharacter character = resolve(page.CharacterID);
				if (character != null && character.TryGet(out IWaypointController controller))
				{
					controller.MarkPersisted(page.SceneName, page.Page, page.Mask);
				}
			}
		}
	}
}
