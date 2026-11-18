using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Logging;
using FishMMO.Shared.Core;

[assembly: FishMMO.Server.Implementation.World.SceneServer.CharacterPersistence(typeof(FishMMO.Server.Implementation.World.SceneServer.WaypointPersistenceParticipant))]

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

	/// <summary>
	/// Discovered waypoints in the character load and save (Travel module), over the existing <c>character_waypoints</c>
	/// table. Was hard-coded in <c>CharacterSystem.Loading</c>/<c>Saving</c>; removing the module leaves the table and its rows
	/// as they are.
	/// </summary>
	/// <remarks>
	/// The unlock path (<c>WaypointTravelSystem</c>) writes each page the moment it changes; this is the retry for a write
	/// that was lost, and the flush before a session release. A page is dirty until the merge is confirmed, so a lost write
	/// shows up again on the next pass. Restored as persisted, so nothing is dirty after a login.
	/// </remarks>
	public sealed class WaypointPersistenceParticipant : CharacterPersistenceParticipant
	{
		public override string Name => "waypoints";

		private readonly List<WaypointPageSnapshot> scratch = new List<WaypointPageSnapshot>();

		public override CharacterRows CreateRows() => new CharacterRows<CharacterWaypointData>(row => row.CharacterID);

		public override async Task<CharacterFetch> FetchAsync(ICharacterPersistenceHost host, long characterID)
		{
			if (host.Services == null || !host.Services.TryGet<ICharacterWaypointService>(out var waypointService))
			{
				return CharacterFetch.Ok(null);
			}
			var result = await waypointService.FetchAsync(characterID);
			return result.IsSuccess
				? CharacterFetch.Ok(result.Data)
				: CharacterFetch.Failed(result.ErrorCode.ToString(), result.ErrorMessage);
		}

		public override void Apply(ICharacter character, object fetched)
		{
			if (!(fetched is IReadOnlyList<CharacterWaypointData> pages) || pages.Count == 0 ||
				!character.TryGet(out IWaypointController controller))
			{
				return;
			}
			foreach (CharacterWaypointData page in pages)
			{
				controller.Restore(page.SceneName, page.Page, page.Mask);
			}
		}

		public override void Capture(ICharacter character, CharacterRows rows)
		{
			WaypointPersistence.AppendDirtyPages(character, ((CharacterRows<CharacterWaypointData>)rows).Items, scratch);
		}

		public override async Task<bool> SaveAsync(ICharacterPersistenceHost host, CharacterRows rows, IReadOnlyCollection<CharacterSessionLeaseData> claims)
		{
			List<CharacterWaypointData> pages = ((CharacterRows<CharacterWaypointData>)rows).Items;
			if (pages.Count == 0 || host.Services == null || !host.Services.TryGet<ICharacterWaypointService>(out var waypointService))
			{
				return true;
			}
			// The merge is an OR: repeating one that did land loses nothing, so any failure counts as not landed.
			if (await WaypointPersistence.MergeAsync(waypointService, pages, "CharacterSystem"))
			{
				host.TryEnqueueMainThread(() => WaypointPersistence.MarkPersisted(pages, id => host.TryGetResidentCharacter(id, out IPlayerCharacter c) ? c : null));
				return true;
			}
			return false;
		}
	}
}
