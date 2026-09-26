using FishNet.Connection;
using FishNet.Object;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Auth.Core;
using FishMMO.Shared.Core;
using FishMMO.Logging;
using UnityEngine;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Character saving and session management: periodic saves, character data serialization, async persistence, and session lifecycle (claim/release).
	/// </summary>
	public partial class CharacterSystem
	{
		/// <summary>
		/// Periodic callback for saving all characters to the database.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>The save gate is released on every exit that does not hand it to the async save.</b>
		/// It is taken before the capture loop, and the capture loop used to have no per-character
		/// catch and no <c>finally</c>: one exception from one character — a destroyed transform, a
		/// controller in a bad state — left the gate set, and every later pass returned at it. Saves
		/// stopped for every resident until the process restarted, and after the first error nothing
		/// was logged at all. Now a character that fails to capture is skipped and reported through
		/// <see cref="periodicCaptureFaults"/>, and the gate is released in a <c>finally</c> unless
		/// the save that owns it was enqueued.
		/// </para>
		/// </remarks>
		/// <param name="deltaTime">Seconds since the last pass (unused).</param>
		private void OnPeriodicSave(float deltaTime)
		{
			if (!Initialized || Server == null)
			{
				return;
			}

			if (!Server.DataContainerRegistry.TryGet(out ICharacterMappingData<NetworkConnection> data))
			{
				return;
			}

			// Lingering bodies count: they are not in CharactersByID but they are still
			// resident and still accumulating state worth writing. See
			// AppendLingeringCharacterSnapshots.
			if (data.CharactersByID.Count == 0 && LingeringCharacterCount == 0)
			{
				return;
			}

			Log.Debug("CharacterSystem", "Save" + "[" + DateTime.UtcNow + "]");

			if (!Server.DataContainerRegistry.TryGet<ICharacterSystemRuntimeData>(out var runtimeData))
			{
				return;
			}

			if (!runtimeData.TryBeginSave())
			{
				return;
			}

			bool handedOff = false;
			try
			{
				// Snapshot character data on the main thread, pairing each with the claim this
				// server holds for it so the write can prove ownership.
				var characterDataList = new List<(CharacterData Data, CharacterSessionInfo? Ownership)>(data.CharactersByID.Count);
				var subEntities = new SubEntitySnapshot(data.CharactersByID.Count);
				double now = Time.realtimeSinceStartupAsDouble;
				int failed = 0;
				foreach (var character in data.CharactersByID.Values)
				{
					if (!TryCapturePeriodicSnapshot(data, character, characterDataList, subEntities, now))
					{
						++failed;
					}
				}

				// Combat-logout bodies have no connection and so are absent from the map above.
				failed += AppendLingeringCharacterSnapshots(data, characterDataList, subEntities, now);

				if (failed == 0)
				{
					periodicCaptureFaults.ReportSuccess();
				}

				// Whatever was captured is written, whether or not every character captured.
				EnqueueSubEntitySaves(subEntities);

				if (characterDataList.Count == 0)
				{
					return;
				}

				if (EnqueueAsyncWork(() => SaveAllCharactersAsync(characterDataList)))
				{
					handedOff = true;
				}
				else
				{
					Log.Warning("CharacterSystem", "OnPeriodicSave: Failed to enqueue SaveAllCharactersAsync work item.");
				}
			}
			finally
			{
				// SaveAllCharactersAsync releases the gate when it finishes; nothing else will.
				if (!handedOff)
				{
					runtimeData.EndSave();
				}
			}
		}

		/// <summary>
		/// Failures of the periodic capture, one report for the whole pass: the first in full, then
		/// counted summaries, then a recovery line. Main thread only.
		/// </summary>
		private readonly RepeatingFaultLog periodicCaptureFaults = new RepeatingFaultLog("CharacterSystem", "Periodic save capture");

		/// <summary>
		/// Captures one character's row and sub-entity rows for the periodic save. Main thread only.
		/// </summary>
		/// <remarks>
		/// A character that throws is skipped, and only it: the row is captured first and is kept even
		/// if a sub-entity table then fails, since every row captured is a valid snapshot of its own
		/// table. The next pass tries the character again.
		/// </remarks>
		/// <returns>False when the capture threw.</returns>
		private bool TryCapturePeriodicSnapshot(
			ICharacterMappingData<NetworkConnection> data,
			IPlayerCharacter character,
			List<(CharacterData Data, CharacterSessionInfo? Ownership)> characterDataList,
			SubEntitySnapshot subEntities,
			double now)
		{
			try
			{
				CharacterSessionInfo? ownership = data.SessionTokens.TryGetValue(character.ID, out CharacterSessionInfo held)
					? held
					: (CharacterSessionInfo?)null;
				characterDataList.Add((BuildCharacterData(character), ownership));
				AppendSubEntities(character, subEntities, ownership);
				return true;
			}
			catch (Exception ex)
			{
				// Recorded by the exception, not the character, so one fault across many characters
				// is one line and a count rather than a line each.
				switch (periodicCaptureFaults.Record(ex, now, out int repeats))
				{
					case RepeatingFaultLog.Decision.LogFull:
						Log.Error("CharacterSystem", $"Periodic save capture skipped character {character?.ID} this pass: {ex}");
						break;
					case RepeatingFaultLog.Decision.LogSummary:
						Log.Error("CharacterSystem",
							$"Periodic save capture skipped {repeats} more character(s) with the same exception " +
							$"({periodicCaptureFaults.ConsecutiveFailures} in a row), most recently {character?.ID}: {ex.GetType().Name}: {ex.Message}");
						break;
				}
				return false;
			}
		}

		/// <summary>
		/// Every per-character sub-entity row a save carries, captured together on the main
		/// thread. One character's worth for a despawn or reattach; every resident character's
		/// worth for the periodic save.
		/// </summary>
		/// <remarks>
		/// One type rather than eight parallel lists threaded through five save paths. Each new
		/// sub-entity (achievements, waypoints, factions, archetypes) had to be added to every
		/// one of those paths by hand, and factions were simply missed for the life of the
		/// project — fetched on login, never written back. With the capture and the saves in one
		/// place there is one list to extend and no path that can forget a table.
		/// </remarks>
		private sealed class SubEntitySnapshot
		{
			/* No buffs. A character's buffs are one set that is written with its row — see
			 * CaptureBuffSet and CharacterData.Buffs — so they travel in the CharacterData every one
			 * of these save paths already carries, retry queue included. */
			public readonly List<CharacterAttributeData> Attributes;
			public readonly List<CharacterAbilityData> Abilities;
			public readonly List<PetSnapshot> Pets;
			public readonly List<CharacterAchievementData> Achievements;
			public readonly List<CharacterWaypointData> Waypoints;
			public readonly List<CharacterFactionData> Factions;
			public readonly List<CharacterArchetypeData> Archetypes;
			public readonly List<CharacterKnownAbilityData> KnownAbilities;
			/// <summary>
			/// A departing character's whole hotkey bar, or nothing. Captured only by the departure
			/// paths (<see cref="AppendDepartureSubEntities"/>); a resident's changes are written by
			/// the hotkey system's own pump. See <c>IHotkeySystemRuntimeData.TakeDepartingBar</c>.
			/// </summary>
			public readonly List<CharacterHotkeyData> Hotkeys;
			/// <summary>Each character's knowledge version as captured, keyed by character. See MarkKnowledgePersisted.</summary>
			public readonly Dictionary<long, long> KnowledgeVersions;
			/// <summary>
			/// The session claim each character's rows were captured under, keyed by character. Every
			/// write of these rows quotes it, so a row lands only while that claim is still held — see
			/// <c>CharacterWriteGate</c>. A character is captured only with its claim, so every row here
			/// has one.
			/// </summary>
			public readonly Dictionary<long, CharacterSessionLeaseData> Claims;

			public SubEntitySnapshot(int characterCount = 1)
			{
				int n = Math.Max(1, characterCount);
				Attributes = new List<CharacterAttributeData>(n * 16);
				Abilities = new List<CharacterAbilityData>(n * 8);
				Pets = new List<PetSnapshot>(n);
				Achievements = new List<CharacterAchievementData>(n * 4);
				Waypoints = new List<CharacterWaypointData>(n);
				Factions = new List<CharacterFactionData>(n * 8);
				Archetypes = new List<CharacterArchetypeData>(n);
				KnownAbilities = new List<CharacterKnownAbilityData>(n * 8);
				Hotkeys = new List<CharacterHotkeyData>();
				KnowledgeVersions = new Dictionary<long, long>(n);
				Claims = new Dictionary<long, CharacterSessionLeaseData>(n);
			}

			/// <summary>
			/// A copy of <see cref="Claims"/> for one write, so the write never enumerates a dictionary
			/// that could change under it.
			/// </summary>
			public IReadOnlyCollection<CharacterSessionLeaseData> ClaimList()
			{
				return new List<CharacterSessionLeaseData>(Claims.Values);
			}

			/// <summary>
			/// Appends every row of another character's snapshot, so a flush that speaks for many
			/// characters writes each table once rather than once per character.
			/// </summary>
			/// <remarks>
			/// <b>One claim per character, the first one recorded.</b> The claims are keyed by
			/// character, so appending a character's rows captured under a DIFFERENT claim — an
			/// older session's rows still queued for retry beside the current session's — would
			/// write one session's rows under the other's claim, where an older session's higher
			/// version could beat the newer one's rows. Those rows are left out instead; under their
			/// own claim the gate would only refuse them. The shutdown flush appends resident
			/// characters before the retry queue, so the current session wins.
			/// </remarks>
			/// <param name="other">The snapshot to take the rows of. Not modified.</param>
			public void AddFrom(SubEntitySnapshot other)
			{
				if (other == null)
				{
					return;
				}

				HashSet<long> conflicting = null;
				foreach (KeyValuePair<long, CharacterSessionLeaseData> claim in other.Claims)
				{
					if (Claims.TryGetValue(claim.Key, out CharacterSessionLeaseData held) &&
						(held.OwnerServerID != claim.Value.OwnerServerID || held.OwnerToken != claim.Value.OwnerToken))
					{
						(conflicting ??= new HashSet<long>()).Add(claim.Key);
						continue;
					}
					Claims[claim.Key] = claim.Value;
				}

				if (conflicting == null)
				{
					Attributes.AddRange(other.Attributes);
					Abilities.AddRange(other.Abilities);
					Pets.AddRange(other.Pets);
					Achievements.AddRange(other.Achievements);
					Waypoints.AddRange(other.Waypoints);
					Factions.AddRange(other.Factions);
					Archetypes.AddRange(other.Archetypes);
					KnownAbilities.AddRange(other.KnownAbilities);
					Hotkeys.AddRange(other.Hotkeys);
				}
				else
				{
					Attributes.AddRange(other.Attributes.Where(r => !conflicting.Contains(r.CharacterID)));
					Abilities.AddRange(other.Abilities.Where(r => !conflicting.Contains(r.CharacterID)));
					Pets.AddRange(other.Pets.Where(r => !conflicting.Contains(r.Pet.CharacterID)));
					Achievements.AddRange(other.Achievements.Where(r => !conflicting.Contains(r.CharacterID)));
					Waypoints.AddRange(other.Waypoints.Where(r => !conflicting.Contains(r.CharacterID)));
					Factions.AddRange(other.Factions.Where(r => !conflicting.Contains(r.CharacterID)));
					Archetypes.AddRange(other.Archetypes.Where(r => !conflicting.Contains(r.CharacterID)));
					KnownAbilities.AddRange(other.KnownAbilities.Where(r => !conflicting.Contains(r.CharacterID)));
					Hotkeys.AddRange(other.Hotkeys.Where(r => !conflicting.Contains(r.CharacterID)));
				}

				foreach (KeyValuePair<long, long> knowledge in other.KnowledgeVersions)
				{
					if (conflicting == null || !conflicting.Contains(knowledge.Key))
					{
						KnowledgeVersions[knowledge.Key] = knowledge.Value;
					}
				}
			}
		}

		/// <summary>
		/// Captures every dirty sub-entity row of one character, with the claim they will be written
		/// under. Main thread only; must run after <see cref="BuildCharacterData"/> for the same
		/// character (the pet rows share its version counter).
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>No claim, no capture.</b> Every sub-entity write is ownership-gated
		/// (<c>CharacterWriteGate</c>): a row lands only while the claim it quotes is still held, so
		/// that a write captured by a session released a moment later cannot land over the next
		/// owner's state, an offline debit or a trade's last settlement. A character this server
		/// holds no claim for has nothing it may write, and capturing it anyway would bump its
		/// versions and mark its rows pending for a write that is bound to be refused. Left alone,
		/// its dirty marks stay set. Every resident, lingering or departing character holds one, so
		/// this is an anomaly and says so.
		/// </para>
		/// <para>
		/// The claim is passed rather than looked up because the departure paths take it out of
		/// <c>SessionTokens</c> before they capture.
		/// </para>
		/// </remarks>
		/// <param name="character">The character.</param>
		/// <param name="snapshot">Destination for its rows.</param>
		/// <param name="claim">The claim this server holds for it, or null when it holds none.</param>
		private void AppendSubEntities(IPlayerCharacter character, SubEntitySnapshot snapshot, CharacterSessionInfo? claim)
		{
			if (!claim.HasValue)
			{
				Log.Warning("CharacterSystem",
					$"Character {character.ID} has no session claim on this server; its sub-entity rows were not captured and stay dirty.");
				return;
			}
			snapshot.Claims[character.ID] = new CharacterSessionLeaseData(character.ID, claim.Value.ServerID, claim.Value.Token);

			AppendAttributeData(character, snapshot.Attributes);
			AppendAbilityData(character, snapshot.Abilities);
			AppendPetData(character, snapshot.Pets);
			AppendAchievementData(character, snapshot.Achievements);
			AppendWaypointData(character, snapshot.Waypoints);
			AppendFactionData(character, snapshot.Factions);
			AppendArchetypeData(character, snapshot.Archetypes);
			AppendKnownAbilityData(character, snapshot.KnownAbilities, snapshot.KnowledgeVersions);
		}

		/// <summary>
		/// <see cref="AppendSubEntities"/> for a character that is leaving this server's hands —
		/// logging out, transferring, a combat-logout body ending or being reclaimed, the shutdown —
		/// plus the rows only a departure writes. Main thread only.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>The hotkey bar.</b> A resident's bar is written by the hotkey system's pump, a few
		/// seconds after each change, under the claim it captures then. A departing character's
		/// bar is written here instead, whole and from the live bar, inside the work that ends in
		/// the release — the same move that folded the item flush into the save-and-release. It
		/// used to be flushed from <c>OnDisconnect</c>, a write of its own that only the lane order
		/// kept ahead of the release (not the unkeyed retry queue's, nor the shutdown flush's,
		/// which runs after the hotkey system has already torn down). Every hotkey write is
		/// ownership-gated now, so a write that lands after its release is refused and lost, not
		/// merely late.
		/// </para>
		/// <para>
		/// Only with a claim: <see cref="AppendSubEntities"/> records it first and captures nothing
		/// without one, and the bar follows the same rule.
		/// </para>
		/// </remarks>
		/// <param name="character">The departing character.</param>
		/// <param name="snapshot">Destination for its rows.</param>
		/// <param name="claim">The claim this server holds for it, or null when it holds none.</param>
		private void AppendDepartureSubEntities(IPlayerCharacter character, SubEntitySnapshot snapshot, CharacterSessionInfo? claim)
		{
			AppendSubEntities(character, snapshot, claim);
			if (!claim.HasValue ||
				Server?.DataContainerRegistry == null ||
				!Server.DataContainerRegistry.TryGet(out IHotkeySystemRuntimeData hotkeyData))
			{
				return;
			}

			List<CharacterHotkeyData> bar = hotkeyData.TakeDepartingBar(character.ID, character.Hotkeys);
			if (bar != null)
			{
				snapshot.Hotkeys.AddRange(bar);
			}
		}

		/// <summary>
		/// Hands every non-empty list of a snapshot to the persistence lane, independently.
		/// The periodic and linger paths, where nothing waits on the outcome.
		/// </summary>
		/// <remarks>
		/// The ability rows are grouped by their own character and keyed by it, the one list here
		/// that is. A snapshot can hold abilities belonging to more than one character — a pet's
		/// rows are captured into its owner's snapshot — so the group key is the row's
		/// <c>CharacterID</c> rather than the snapshot's owner.
		/// <para>
		/// Keyed for two reasons, neither of which is the one that keeps a forgotten ability
		/// forgotten. A batch is validated whole: the ability upsert throws when any character it
		/// names is missing or deleted, so an unkeyed batch would let one departed character's pet
		/// rows take the entire snapshot's ability writes down with them. And same-character work on
		/// one lane runs one at a time, in order, so two batches cannot interleave on the same rows.
		/// The item layer keys its flush the same way
		/// (<c>CharacterSystem.CombatLogout</c> passes <c>character.ID</c>).
		/// </para>
		/// <para>
		/// What keeps a forgotten row gone is narrower: the batch carries each ability's real
		/// <c>ID</c>, so the upsert's existing-row half drops any row that is no longer there rather
		/// than re-inserting it. Lane order is not load-bearing for that — a lookup that does not
		/// exist cannot be written back, whatever order the two writes land in.
		/// </para>
		/// </remarks>
		private void EnqueueSubEntitySaves(SubEntitySnapshot s)
		{
			// Every table quotes the claims its rows were captured under. See AppendSubEntities.
			IReadOnlyCollection<CharacterSessionLeaseData> claims = s.ClaimList();
			if (s.Attributes.Count > 0)
			{
				/* One statement for the whole pass. Regeneration and combat mark nearly every
				 * resident's resources dirty, so this used to be one transaction per resident every
				 * pass — BEGIN, SELECT, UNNEST upsert, COMMIT, each — in the same burst as the item
				 * snapshot and the character save.
				 *
				 * It was keyed per character so a character's attribute rows would queue FIFO with its
				 * item batches, which write the same rows: an unkeyed pass captured after an equip
				 * could land before it, supersede the batch's attribute rows, and the item layer
				 * treated that short write as a failure and rolled the equip back. The item layer now
				 * counts a superseded attribute row as written (CharacterInventorySystem
				 * .RequireAttemptedWrite) — the newer row was captured later from the same memory — so
				 * lane order no longer matters. There is no exception left: a trade credits memory at its
				 * apply (TradeCurrencySettlement), and AppendAttributeData leaves an attribute alone while
				 * that trade is settling.
				 *
				 * The service leaves out the rows of a character whose row has gone, or whose claim
				 * this server no longer holds (the ownership gate), rather than failing everyone's with
				 * it; those count as Filtered, and SaveAttributesAsync then keeps every mark for this
				 * pass, so nothing is cleared that was not written. */
				EnqueuePersistence(() => SaveAttributesAsync(s.Attributes, claims));
			}
			if (s.Abilities.Count > 0)
			{
				foreach (var group in s.Abilities.GroupBy(a => a.CharacterID))
				{
					List<CharacterAbilityData> rows = group.ToList();
					EnqueuePersistence(() => SaveAbilitiesAsync(rows, claims), group.Key);
				}
			}
			if (s.Pets.Count > 0) EnqueuePersistence(() => SavePetsAsync(s.Pets, claims));
			if (s.Achievements.Count > 0) EnqueuePersistence(() => SaveAchievementsAsync(s.Achievements, claims));
			if (s.Waypoints.Count > 0) EnqueuePersistence(() => SaveWaypointsAsync(s.Waypoints));
			if (s.Factions.Count > 0) EnqueuePersistence(() => SaveFactionsAsync(s.Factions, claims));
			if (s.Archetypes.Count > 0) EnqueuePersistence(() => SaveArchetypesAsync(s.Archetypes, claims));
			if (s.KnownAbilities.Count > 0) EnqueuePersistence(() => SaveKnownAbilitiesAsync(s.KnownAbilities, s.KnowledgeVersions, claims));
			if (s.Hotkeys.Count > 0)
			{
				// Keyed like the abilities, behind any bar the hotkey pump queued for the character.
				foreach (var group in s.Hotkeys.GroupBy(h => h.CharacterID))
				{
					List<CharacterHotkeyData> rows = group.ToList();
					EnqueuePersistence(() => SaveHotkeysAsync(rows, claims), group.Key);
				}
			}
		}

		/// <summary>
		/// Writes every non-empty list of a snapshot, one after another, on the calling worker.
		/// The despawn and reattach paths, where the next reader of these rows is about to run.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Sequential on purpose: everything the next owner is about to read has to be in the
		/// database before the claim it reads under is available, and a saturated pool could
		/// reorder parallel writes behind the release.
		/// </para>
		/// <para>
		/// <b>Transient failures are retried here, before the release, and nowhere else.</b> These
		/// paths hand the character on: its dirty marks leave with the despawned object, so unlike
		/// the periodic save there is no next pass to carry a failed table. Retrying after the
		/// release is not an option either — every table is ownership-gated now, so a write that
		/// quotes a released claim is refused, and that refusal is the point: the next owner's
		/// versions restart from the rows it loaded, so a late write of ours would otherwise land
		/// over state it has since changed. A bounded retry inside the claim is the only safe place.
		/// </para>
		/// </remarks>
		/// <param name="s">The snapshot to write.</param>
		/// <param name="characterID">The character it belongs to, for the log.</param>
		/// <returns>
		/// True when every table reached a final outcome — written, or refused for a reason another
		/// attempt cannot change; false when at least one was given up on while still worth retrying.
		/// </returns>
		private async Task<bool> SaveSubEntitiesSequentiallyAsync(SubEntitySnapshot s, long characterID)
		{
			// Every table quotes the claims its rows were captured under. See AppendSubEntities.
			IReadOnlyCollection<CharacterSessionLeaseData> claims = s.ClaimList();
			bool done = true;
			if (s.Attributes.Count > 0) done &= await SaveWithRetryAsync(() => SaveAttributesAsync(s.Attributes, claims), "attributes", characterID);
			if (s.Abilities.Count > 0)
			{
				/* Grouped by owning character for the reason EnqueueSubEntitySaves gives: the ability
				 * upsert validates a batch whole, so a pet whose character row is gone would otherwise
				 * take its owner's rows down with it on the very save the next server reads under. */
				foreach (var group in s.Abilities.GroupBy(a => a.CharacterID))
				{
					List<CharacterAbilityData> rows = group.ToList();
					done &= await SaveWithRetryAsync(() => SaveAbilitiesAsync(rows, claims), "abilities", characterID);
				}
			}
			if (s.Pets.Count > 0) done &= await SaveWithRetryAsync(() => SavePetsAsync(s.Pets, claims), "pet", characterID);
			if (s.Achievements.Count > 0) done &= await SaveWithRetryAsync(() => SaveAchievementsAsync(s.Achievements, claims), "achievements", characterID);
			if (s.Waypoints.Count > 0) done &= await SaveWithRetryAsync(() => SaveWaypointsAsync(s.Waypoints), "waypoints", characterID);
			if (s.Factions.Count > 0) done &= await SaveWithRetryAsync(() => SaveFactionsAsync(s.Factions, claims), "factions", characterID);
			if (s.Archetypes.Count > 0) done &= await SaveWithRetryAsync(() => SaveArchetypesAsync(s.Archetypes, claims), "archetype", characterID);
			if (s.KnownAbilities.Count > 0) done &= await SaveWithRetryAsync(() => SaveKnownAbilitiesAsync(s.KnownAbilities, s.KnowledgeVersions, claims), "known abilities", characterID);
			if (s.Hotkeys.Count > 0) done &= await SaveWithRetryAsync(() => SaveHotkeysAsync(s.Hotkeys, claims), "hotkey bar", characterID);
			return done;
		}

		/// <summary>Attempts one sub-entity table gets on the hand-off paths before it is given up on.</summary>
		private const int MaxSubEntityWriteAttempts = 3;

		/// <summary>Backoff between those attempts, multiplied by the attempt number.</summary>
		private const int SubEntityWriteRetryStepMs = 150;

		/// <summary>
		/// Runs one sub-entity write until it is done or the bounded attempts run out.
		/// </summary>
		/// <param name="write">The write. Returns false only for a failure worth another attempt.</param>
		/// <param name="what">The table, for the log.</param>
		/// <param name="characterID">The character, for the log.</param>
		/// <returns>True when the write reached a final outcome; false when it was given up on.</returns>
		private static async Task<bool> SaveWithRetryAsync(Func<Task<bool>> write, string what, long characterID)
		{
			for (int attempt = 1; attempt <= MaxSubEntityWriteAttempts; ++attempt)
			{
				if (await write())
				{
					return true;
				}
				if (attempt < MaxSubEntityWriteAttempts)
				{
					await Task.Delay(SubEntityWriteRetryStepMs * attempt);
				}
			}

			// Each attempt has logged its own failure; this is the line that says what it cost.
			// Zero is the shutdown flush, which writes each table once for every character.
			string whose = characterID > 0 ? $"Character {characterID}" : "Shutdown flush";
			await Log.Error("CharacterSystem",
				$"{whose}: the {what} write failed {MaxSubEntityWriteAttempts} times on hand-off and was given up; the changes since the last successful save are lost.");
			return false;
		}

		/// <summary>
		/// Runs a departing character's item flush until its outcome is final or the bounded
		/// attempts run out.
		/// </summary>
		/// <remarks>
		/// Only <see cref="ItemWriteOutcome.Retry"/> is attempted again; everything else is final.
		/// A <see cref="ItemWriteOutcome.Retry"/> that survives every attempt is returned as it is,
		/// and the caller keeps the claim — see <see cref="SaveAndReleaseCharacterAsync"/>. Running the
		/// flush again is safe: it is a snapshot, and a rerun either restates the same rows or is
		/// refused as superseded by something written after it.
		/// </remarks>
		/// <param name="flush">The flush, as <c>CaptureDespawnFlush</c> returned it.</param>
		/// <param name="characterID">The character, for the log.</param>
		/// <param name="cancellationToken">Stops further attempts; an attempt already running finishes.</param>
		/// <returns>The last attempt's outcome.</returns>
		private static async Task<ItemWriteOutcome> RunItemFlushWithRetryAsync(Func<Task<ItemWriteOutcome>> flush, long characterID, CancellationToken cancellationToken = default)
		{
			ItemWriteOutcome outcome = ItemWriteOutcome.Retry;
			for (int attempt = 1; attempt <= MaxSubEntityWriteAttempts; ++attempt)
			{
				outcome = await RunItemFlushOnceAsync(flush, characterID);
				if (outcome != ItemWriteOutcome.Retry)
				{
					return outcome;
				}
				if (attempt < MaxSubEntityWriteAttempts)
				{
					await Task.Delay(SubEntityWriteRetryStepMs * attempt, cancellationToken);
				}
			}
			return outcome;
		}

		/// <summary>One attempt of a departing character's item flush. Never throws.</summary>
		private static async Task<ItemWriteOutcome> RunItemFlushOnceAsync(Func<Task<ItemWriteOutcome>> flush, long characterID)
		{
			try
			{
				return await flush();
			}
			catch (Exception ex)
			{
				// ApplyItemBatchAsync does not throw; this is for a flush that failed to start.
				await Log.Error("CharacterSystem", $"The item flush for character {characterID} threw: {ex}");
				return ItemWriteOutcome.Retry;
			}
		}

		/// <summary>
		/// Saves the character state, despawns the character from the scene, and fully releases the session.
		/// Always performs a full release (Online → Offline) because the destination
		/// Scene Server's TryClaimAsync requires session_state = Offline (or expired lease).
		/// </summary>
		/// <param name="conn">Network connection of the character.</param>
		/// <param name="character">Player character to save and despawn.</param>
		/// <param name="sessionInfo">Session ownership info to release after save, or null if no session is claimed.</param>
		private void SaveAndDespawnCharacter(NetworkConnection conn, IPlayerCharacter character, CharacterSessionInfo? sessionInfo = null)
		{
			// Combat is transient state — clear it before saving so it never persists to DB.
			// (BuildCharacterData also masks the flag for defense-in-depth.)
			character.DisableFlags(CharacterFlags.IsInCombat);

			// The body is going away, so it is no longer waiting to be reclaimed. Clearing this
			// centrally — rather than at each call site — is what guarantees the flag cannot
			// survive in the database: a stuck IsCombatLogged would make AnyOnlineAsync ignore
			// the character permanently and let the account hold two live sessions at once.
			character.DisableFlags(CharacterFlags.IsCombatLogged);

			// Remove loaded state so when a character is reloaded into a different scene/server, it will properly clamp attributes
			// and prevent actions until fully loaded in the new scene.
			character.DisableFlags(CharacterFlags.IsLoaded);

			// Snapshot character data on the main thread
			CharacterData charData = BuildCharacterData(character);
			var subEntities = new SubEntitySnapshot();
			AppendDepartureSubEntities(character, subEntities, sessionInfo);

			/* The item flush is captured here, on the main thread, while the containers are still
			 * live — and it is AWAITED inside the save-and-release below, before the release.
			 *
			 * It used to be enqueued from the despawn event onto the character's ordered lane
			 * while the save-and-release ran unkeyed on another, so the two raced: the release
			 * could land, the destination scene server could claim and load, and the item
			 * snapshot would then be refused by the ownership assertion — or land too late for
			 * the load that had already read the rows. Everything the player did with items in
			 * the last minute was at the mercy of that race. The lease is passed explicitly for
			 * the same reason the caller took the token out of SessionTokens first. */
			Func<Task<ItemWriteOutcome>> itemFlush = null;
			if (Server.BehaviourRegistry.TryGet(out ICharacterInventorySystem inventorySystem))
			{
				itemFlush = inventorySystem.CaptureDespawnFlush(character, sessionInfo);
			}

			/* Save everything, THEN release the session (Online → Offline).
			 *
			 * All of it in one work item, on purpose. The sub-entity writes used to be enqueued
			 * separately, which put them on a different worker lane than the save-and-release —
			 * so the claim could be handed back while a character's buffs, attributes, abilities
			 * or pet were still unwritten. The destination scene server claims the moment the
			 * release lands and reads the row immediately, and whatever had not been flushed yet
			 * simply was not there: a player zoning could arrive missing the pet they had out,
			 * and the write that finally landed a moment later described a character that had
			 * already moved on.
			 *
			 * The async worker pool is bounded and drops writes when full, so this enqueue can
			 * fail. The session token has already been taken out of SessionTokens by the
			 * caller, which means a dropped work item used to leave nothing anywhere that
			 * could ever release the claim: the character stayed Online until its lease
			 * expired, and every attempt to load it on the destination scene server was
			 * kicked for the whole two minutes. Hand it to the retry queue instead. */
			/* Keyed on the character, like every incremental item batch, so the flush inside
			 * runs on the same lane as the writes it supersedes — the ordering requirement
			 * ICharacterItemService.SaveSnapshotAsync states. Unkeyed, it rode a different lane
			 * and correctness rested entirely on the journal's sequence claim and the row lock. */
			if (!EnqueueAsyncWork(() => SaveAndReleaseCharacterAsync(charData, subEntities, sessionInfo, itemFlush), charData.ID))
			{
				Log.Warning("CharacterSystem", $"SaveAndDespawnCharacter: Failed to enqueue save/release for character {charData.ID} — queued for retry.");
				/* The item flush AND the sub-entity rows travel with the pending release, not on
				 * separate lanes: the retry writes them before it hands the claim back. Both are
				 * ownership-gated, so either one landing after the release would be refused — a
				 * destination server that claims first must not be able to make them fail. The
				 * sub-entity rows used to go out independently, when a late arrival was merely
				 * version-guarded; now it would be refused and lost. */
				QueuePendingFlush(charData.ID, charData, sessionInfo, itemFlush, subEntities);
			}

			// Immediately log out for now.. we could add a timeout later on..?
			if (character.NetworkObject.IsSpawned)
			{
				DispatchCharacterEvent(OnDespawnCharacter, conn, character, nameof(OnDespawnCharacter));

				/* Pooled out of the world scene (PersistentPool), not in place. FishNet leaves a
				 * despawned object wherever it was, so a character that left a dungeon was pooled
				 * inside the instance scene — and the instance unloads when its last occupant leaves,
				 * destroying what the pool still counted. FishNet skips a destroyed entry, so nothing
				 * breaks, but every exit threw away a pooled character and the next login paid for a
				 * fresh instantiate of the whole prefab. Every path that pools a once-spawned
				 * character goes through the one rule. */
				PersistentPool.Despawn(Server.NetworkWrapper.NetworkManager, character.NetworkObject);
			}
		}

		/// <summary>
		/// Builds a CharacterData DTO from an IPlayerCharacter, capturing all fields on the main thread.
		/// Increments character.Version for sequence-based optimistic concurrency.
		/// </summary>
		/// <remarks>
		/// The buff set is captured here, with the row, because it is written with the row — see
		/// <see cref="CaptureBuffSet"/>.
		/// </remarks>
		private CharacterData BuildCharacterData(IPlayerCharacter character)
		{
			character.Version++;
			Vector3 pos = character.Transform.position;
			Quaternion rot = character.Motor != null ? character.Motor.Transform.rotation : character.Transform.rotation;

			/* The transform describes wherever the character currently is, and that is not always
			 * the open world.
			 *
			 * Inside an instance the live transform belongs in the instance columns, and the
			 * open-world columns must keep the position the character will be put back at when
			 * it leaves. Writing the transform to both — which is what happened before — meant
			 * one save inside a dungeon replaced the character's world position with dungeon
			 * coordinates, so anything that returned them to the open world without an explicit
			 * destination (an instance reaped as stale, a cleared instance flag) placed them at
			 * a point that has no relationship to the world scene at all.
			 *
			 * Writing the live transform into the instance columns is also what makes progress
			 * inside a dungeon survive a relog: the entry point was previously re-saved on every
			 * pass, so reconnecting always returned the player to the dungeon's front door. */
			bool inInstance = character.IsInInstance();

			Vector3 worldPos = inInstance ? character.LastWorldPosition : pos;
			Quaternion worldRot = inInstance ? character.LastWorldRotation : rot;
			Vector3 instancePos = inInstance ? pos : character.InstancePosition;
			Quaternion instanceRot = inInstance ? rot : character.InstanceRotation;

			return new CharacterData(
				id: character.ID,
				name: character.CharacterName,
				nameLowercase: character.CharacterNameLower,
				account: character.Account,
				selected: true,
				worldServerID: character.WorldServerID,
				sceneName: character.SceneName,
				sceneHandle: character.SceneHandle,
				bindScene: character.BindScene,
				bindX: character.BindPosition.x,
				bindY: character.BindPosition.y,
				bindZ: character.BindPosition.z,
				instanceID: character.InstanceID,
				instanceX: instancePos.x,
				instanceY: instancePos.y,
				instanceZ: instancePos.z,
				instanceRotX: instanceRot.x,
				instanceRotY: instanceRot.y,
				instanceRotZ: instanceRot.z,
				instanceRotW: instanceRot.w,
				raceID: character.RaceID,
				modelIndex: character.ModelIndex,
				x: worldPos.x,
				y: worldPos.y,
				z: worldPos.z,
				rotX: worldRot.x,
				rotY: worldRot.y,
				rotZ: worldRot.z,
				rotW: worldRot.w,
				accessLevel: (byte)(int)character.AccessLevel,
				online: true,
				/* IsInCombat is transient. IsLoaded is too, and it is load-bearing on the way back
				 * in: the load path installs flags before attributes, and a resource attribute
				 * restored while IsLoaded is set is clamped against a maximum that has no gear or
				 * buffs yet — a character saved at 950/1000 by a periodic save and then recovered
				 * after a crash came back at its bare base maximum. Only the graceful exits ever
				 * cleared the flag; every crash-recovery row carried it. */
				flags: character.Flags & ~(1 << (int)CharacterFlags.IsInCombat) & ~(1 << (int)CharacterFlags.IsLoaded),
				version: character.Version,
				timeCreated: character.TimeCreated,
				lastSaved: DateTime.UtcNow,
				buffs: CaptureBuffSet(character, character.Version)
			);
		}

		/// <summary>
		/// What became of one character-row save.
		/// </summary>
		private enum CharacterSaveOutcome : byte
		{
			/// <summary>The row is written.</summary>
			Saved,
			/// <summary>A transient failure: the same snapshot is worth writing again.</summary>
			Retry,
			/// <summary>Refused for a reason retrying cannot change (stale, missing, invalid).</summary>
			Rejected,
			/// <summary>The claim is gone and another server owns the character.</summary>
			OwnershipLost,
		}

		/// <summary>
		/// Saves a single character asynchronously via the database service.
		/// </summary>
		/// <returns>
		/// <c>true</c> when the row is persisted, or when the write was rejected for a reason
		/// that retrying cannot change; <c>false</c> for transient failures worth retrying.
		/// </returns>
		/// <remarks>
		/// A lost claim reads as <c>true</c> here — there is nothing to retry — which is right for a
		/// caller that only writes the row. A caller that goes on to write the character's other
		/// tables, or to load it, must use <see cref="SaveCharacterOutcomeAsync"/> and stop.
		/// </remarks>
		private async Task<bool> SaveCharacterAsync(CharacterData charData, CharacterSessionInfo? ownership = null)
		{
			return await SaveCharacterOutcomeAsync(charData, ownership) != CharacterSaveOutcome.Retry;
		}

		/// <summary>
		/// Saves a single character and reports exactly what happened to the write.
		/// </summary>
		private async Task<CharacterSaveOutcome> SaveCharacterOutcomeAsync(CharacterData charData, CharacterSessionInfo? ownership = null)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterService>(out var characterService))
				{
					return CharacterSaveOutcome.Retry;
				}

				/* Write through the ownership-gated path whenever this server holds a claim.
				 *
				 * The plain PersistAsync is guarded only by the monotonic Version, which does
				 * not identify the writer. A server whose lease lapsed while it was still
				 * running therefore kept saving characters another server had legitimately
				 * claimed — and won, because its version counter had been climbing for the
				 * whole session while the new owner restarted from the persisted row. The
				 * claim was advisory on the write path; this makes it binding. */
				DatabaseResult result = ownership.HasValue
					? await characterService.PersistOwnedAsync(
						charData,
						new CharacterSessionLeaseData(charData.ID, ownership.Value.ServerID, ownership.Value.Token))
					: await characterService.PersistAsync(charData);

				/* DUPLICATE_REPLAY is this very write, already stored: the service raises it when the row
				 * holds exactly the version being written — a transaction retried after its commit reply
				 * was lost, or a pending-flush retry of a save that did land. It used to fall through to
				 * Retry below, so the retry queue re-sent a stored snapshot until it gave up. */
				if (result.IsSuccess || result.ErrorCode == DatabaseErrorCodes.DuplicateReplay)
				{
					return CharacterSaveOutcome.Saved;
				}

				if (result.ErrorCode == DatabaseErrorCodes.Forbidden)
				{
					/* The claim is gone: another server owns this character now and has been
					 * authoritative since it loaded. Everything accumulated here since the last
					 * successful save is unpersistable — writing it would overwrite the live
					 * session's progress, which is a worse loss than dropping ours. Say exactly
					 * what was dropped, then evict so we stop simulating a character we cannot
					 * save. Retrying is pointless: the claim never comes back. */
					await Log.Error("CharacterSystem",
						$"Character {charData.ID} ('{charData.Name}') is no longer claimed by this server; " +
						$"discarding the unsaved snapshot at version {charData.Version} and evicting it. " +
						"This means the session lease lapsed while the character was still resident — " +
						"check for database outages or async-worker saturation lasting over the lease duration.");
					RequestEviction(charData.ID, "session claim lost");
					return CharacterSaveOutcome.OwnershipLost;
				}

				await Log.Warning("CharacterSystem", $"SaveCharacterAsync DB error for character {charData.ID}: {result.ErrorCode} - {result.ErrorMessage}");

				// A stale or duplicate write means newer state is already persisted; replaying
				// this snapshot would keep losing to the same version guard.
				return result.ErrorCode == DatabaseErrorCodes.StaleState ||
					   result.ErrorCode == DatabaseErrorCodes.NotFound ||
					   result.ErrorCode == DatabaseErrorCodes.ValidationError
					? CharacterSaveOutcome.Rejected
					: CharacterSaveOutcome.Retry;
			}
			catch (Exception ex)
			{
				await Log.Error("CharacterSystem", $"SaveCharacterAsync failed for character {charData.ID}: {ex}");
				return CharacterSaveOutcome.Retry;
			}
		}

		/// <summary>
		/// Saves a character and then releases the session in a single sequential async task.
		/// This ensures the character data is persisted while we still hold the session lock,
		/// preventing another server from claiming the character before the save completes.
		/// </summary>
		private async Task SaveAndReleaseCharacterAsync(
			CharacterData charData,
			SubEntitySnapshot subEntities,
			CharacterSessionInfo? sessionInfo,
			Func<Task<ItemWriteOutcome>> itemFlush = null)
		{
			// Save first — we must persist while still holding the session lock, and prove that
			// ownership in the same statement so a lapsed lease cannot overwrite the new owner.
			CharacterSaveOutcome outcome = await SaveCharacterOutcomeAsync(charData, sessionInfo);

			/* The claim is gone: another server owns this character and has been authoritative
			 * since it loaded. The row write was refused for that reason, and everything below
			 * would only be refused by its own ownership gate (the shutdown flush skips it for the
			 * same reason), so the round trips are saved. There is nothing to release either: the
			 * token in the row is somebody else's. The eviction has already been requested; stop
			 * here. */
			if (outcome == CharacterSaveOutcome.OwnershipLost)
			{
				return;
			}
			bool saved = outcome != CharacterSaveOutcome.Retry;

			/* Items before the release, for the same reason as the sub-entities below. The flush
			 * is a full snapshot of all three containers in one transaction, captured on the main
			 * thread at despawn; it proves ownership with the lease we still hold. */
			ItemWriteOutcome itemOutcome = itemFlush != null
				? await RunItemFlushWithRetryAsync(itemFlush, charData.ID)
				: ItemWriteOutcome.Written;

			if (itemOutcome == ItemWriteOutcome.NotOwned)
			{
				// As for the row above: another server is authoritative, so nothing more is ours to
				// write or to release.
				return;
			}

			/* Sub-entities before the release, not alongside it. Everything the next owner is
			 * about to read has to be in the database before the claim it reads under is
			 * available. Awaited in sequence rather than in parallel so a saturated pool cannot
			 * reorder them behind the release. */
			if (subEntities != null)
			{
				await SaveSubEntitiesSequentiallyAsync(subEntities, charData.ID);
			}

			/* A flush that has still not landed keeps the claim.
			 *
			 * It used to be released regardless, and a failed flush looked like a successful one: the
			 * character's item rows were left as the last snapshot or incremental write had them, and
			 * the repair the failure filed was dropped because the character was no longer resident.
			 * Once the claim is back, the next owner loads those rows and a late flush of ours would
			 * be refused by its ownership check — so the only place the flush can still be delivered
			 * is here, before the release. The retry queue runs it again and releases once it lands;
			 * the player waits a few seconds for the claim rather than losing up to a minute of item
			 * changes. The row save above is handled as before: released regardless, and retried
			 * after, because the row carries its own ownership gate and version guard. */
			if (itemOutcome == ItemWriteOutcome.Retry && sessionInfo.HasValue)
			{
				await Log.Warning("CharacterSystem",
					$"SaveAndReleaseCharacterAsync: the item flush for character {charData.ID} has not landed; keeping its claim and retrying before the release.");
				QueuePendingFlush(charData.ID, saved ? (CharacterData?)null : charData, sessionInfo, itemFlush);
				return;
			}

			// Release regardless of whether the row save landed. Holding the claim back because a
			// row write failed would strand the character far more visibly than losing one save:
			// the destination scene server could not claim it and would kick the player.
			bool released = true;
			if (sessionInfo.HasValue)
			{
				released = await ReleaseCharacterSessionAsync(charData.ID, sessionInfo.Value.ServerID, sessionInfo.Value.Token);
			}

			if (!saved || !released)
			{
				// Retrying the save after the release is safe: the snapshot is the state we
				// held while we owned the character, and the version guard drops it if the
				// next owner has already written something newer.
				QueuePendingFlush(charData.ID, saved ? (CharacterData?)null : charData, released ? null : sessionInfo);
			}
		}

		/// <summary>
		/// Rows per <see cref="ICharacterService.PersistManyAsync"/> call. One transaction each, so this
		/// bounds how long a batch holds its row locks as well as how large one statement grows.
		/// </summary>
		private const int CharacterRowsPerBatch = 500;

		/// <summary>
		/// Saves all characters asynchronously with a processing guard to prevent overlapping saves.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Session leases are deliberately not touched here. They are refreshed on their own
		/// timer by <see cref="OnPeriodicSessionLeaseRefresh"/> so that liveness of a claim
		/// never depends on how long this save takes.
		/// </para>
		/// <para>
		/// <b>One statement per <see cref="CharacterRowsPerBatch"/> characters, not one transaction
		/// each.</b> This used to await the single-row save for every resident in turn — three round
		/// trips a character, in series — so 500 residents cost about 1,500 round trips a pass, and at
		/// a few thousand the pass outlasted the save interval, the next pass was skipped at the gate,
		/// and the effective interval doubled. The batched save makes exactly the checks the single-row
		/// one does, per row, and says per row what happened; a lost claim, a stale row or a deleted
		/// character is handled for that character alone.
		/// </para>
		/// <para>
		/// <b>Every resident is still written every pass, idle or not, and on purpose.</b> The row is
		/// not skipped when unchanged: the lease refresh rewrites each resident's row every
		/// <c>sessionLeaseRefreshRate</c> seconds regardless, so a skip would save no tuple churn, and
		/// the row has writers outside this server's memory (a rename, the world server's scene bind)
		/// whose values the next save is expected to overwrite with the owner's.
		/// </para>
		/// </remarks>
		private async Task SaveAllCharactersAsync(List<(CharacterData Data, CharacterSessionInfo? Ownership)> characterDataList)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterService>(out var characterService))
				{
					return;
				}

				for (int offset = 0; offset < characterDataList.Count; offset += CharacterRowsPerBatch)
				{
					int count = Math.Min(CharacterRowsPerBatch, characterDataList.Count - offset);
					var requests = new List<CharacterPersistRequest>(count);
					for (int i = offset; i < offset + count; ++i)
					{
						requests.Add(ToPersistRequest(characterDataList[i].Data, characterDataList[i].Ownership));
					}

					try
					{
						DatabaseResult<IReadOnlyList<CharacterPersistResult>> result = await characterService.PersistManyAsync(requests);
						if (!result.IsSuccess)
						{
							/* Nothing to retry here: the next pass captures every resident again at a
							 * newer version. Each row's own save paths (logout, despawn) are unaffected. */
							await Log.Warning("CharacterSystem",
								$"SaveAllCharactersAsync: batch of {count} character rows failed: {result.ErrorCode} - {result.ErrorMessage}");
							continue;
						}

						await ReportBatchedSaveAsync(result.Data, "SaveAllCharactersAsync");
					}
					catch (Exception ex)
					{
						await Log.Error("CharacterSystem", $"SaveAllCharactersAsync failed for a batch of {count} character rows: {ex}");
					}
				}
			}
			finally
			{
				if (Server?.DataContainerRegistry.TryGet<ICharacterSystemRuntimeData>(out var runtimeData) == true)
				{
					runtimeData.EndSave();
				}
			}
		}

		/// <summary>The batched-save request for one captured row, gated on the claim when there is one.</summary>
		private static CharacterPersistRequest ToPersistRequest(CharacterData data, CharacterSessionInfo? ownership)
		{
			return new CharacterPersistRequest(
				data,
				ownership.HasValue
					? new CharacterSessionLeaseData(data.ID, ownership.Value.ServerID, ownership.Value.Token)
					: (CharacterSessionLeaseData?)null);
		}

		/// <summary>
		/// Acts on the per-row outcomes of a batched character save: evicts every character whose
		/// claim is gone, and reports the rest the way the single-row save did.
		/// </summary>
		/// <param name="results">The outcomes.</param>
		/// <param name="caller">For the log.</param>
		private async Task ReportBatchedSaveAsync(IReadOnlyList<CharacterPersistResult> results, string caller)
		{
			int stale = 0;
			int missing = 0;
			int invalid = 0;
			for (int i = 0; i < results.Count; ++i)
			{
				CharacterPersistResult row = results[i];
				switch (row.Outcome)
				{
					case CharacterPersistOutcome.OwnershipLost:
						/* The claim is gone: another server owns this character now and has been
						 * authoritative since it loaded. Everything accumulated here since the last
						 * successful save is unpersistable; say so, then evict so we stop simulating a
						 * character we cannot save. */
						await Log.Error("CharacterSystem",
							$"{caller}: character {row.CharacterID} is no longer claimed by this server; discarding its unsaved snapshot and evicting it. " +
							"This means the session lease lapsed while the character was still resident — " +
							"check for database outages or async-worker saturation lasting over the lease duration.");
						RequestEviction(row.CharacterID, "session claim lost");
						break;
					case CharacterPersistOutcome.Stale:
						++stale;
						break;
					case CharacterPersistOutcome.NotFound:
						++missing;
						break;
					case CharacterPersistOutcome.Invalid:
						++invalid;
						break;
				}
			}

			if (stale + missing + invalid > 0)
			{
				// Stale is routine (a logout or despawn overtook the pass); the other two are not.
				await Log.Warning("CharacterSystem",
					$"{caller}: {results.Count} character rows — {stale} stale (a newer write is stored), {missing} not found or deleted, {invalid} malformed.");
			}
		}

		/// <summary>
		/// Removes a character's session token from the mapping data and enqueues an async release.
		/// Returns the extracted CharacterSessionInfo if found, or null if no session was stored.
		/// </summary>
		private CharacterSessionInfo? TryExtractAndReleaseSession(ICharacterMappingData<NetworkConnection> mappingData, long characterID)
		{
			if (mappingData.SessionTokens.TryGetValue(characterID, out CharacterSessionInfo sessionInfo))
			{
				mappingData.SessionTokens.Remove(characterID);
				ReleaseSessionSafely(characterID, sessionInfo.ServerID, sessionInfo.Token);
				return sessionInfo;
			}
			return null;
		}

		/// <summary>
		/// Releases a claimed session, guaranteeing the release is not lost if the async worker
		/// pool is saturated or the database call fails.
		/// </summary>
		/// <remarks>
		/// Every abandoned-load path has to go through here. A claim that is dropped instead of
		/// released leaves the character Online in the database with nothing left in this
		/// process holding its token, so the only thing that can free it is lease expiry —
		/// during which the player cannot be loaded anywhere.
		/// </remarks>
		private void ReleaseSessionSafely(long characterID, long serverID, Guid sessionToken)
		{
			var sessionInfo = new CharacterSessionInfo(sessionToken, serverID);
			/* Keyed on the character, so the release queues behind every write already on its lane
			 * — each quotes this claim and would be refused once it is handed back. See
			 * RunPendingFlushAsync. */
			if (!EnqueueAsyncWork(() => ReleaseSessionWithRetryAsync(characterID, sessionInfo), characterID))
			{
				Log.Warning("CharacterSystem", $"Failed to enqueue session release for character {characterID} — queued for retry.");
				QueuePendingFlush(characterID, null, sessionInfo);
			}
		}

		/// <summary>
		/// Releases a session and queues a retry if the release did not stick.
		/// </summary>
		private async Task ReleaseSessionWithRetryAsync(long characterID, CharacterSessionInfo sessionInfo)
		{
			if (!await ReleaseCharacterSessionAsync(characterID, sessionInfo.ServerID, sessionInfo.Token))
			{
				QueuePendingFlush(characterID, null, sessionInfo);
			}
		}

		#region Pending Flush Retry

		/// <summary>
		/// A save, an item flush and/or a session release that could not be completed on its first
		/// attempt.
		/// </summary>
		/// <remarks>
		/// <b>Every field is read and written under <see cref="Gate"/>.</b> The entry is shared between
		/// the thread that merges new work into it (<see cref="QueuePendingFlush"/>) and the worker
		/// running an attempt (<see cref="RunPendingFlushAsync"/>), and it used to be mutated by the
		/// first while the second was deciding whether it was finished. A merge that landed between
		/// that decision and the removal was removed with the entry — typically a new session's
		/// release, which then waited out its lease. See <see cref="Retired"/>.
		/// </remarks>
		private sealed class PendingCharacterFlush
		{
			/// <summary>Guards every field below.</summary>
			public readonly object Gate = new object();
			/// <summary>Character snapshot still to persist, or null when only a release is outstanding.</summary>
			public CharacterData? CharacterData;
			/// <summary>Session ownership still to release, or null when only a save is outstanding.</summary>
			public CharacterSessionInfo? Session;
			/// <summary>The despawn item flush, run before the release it belongs to. Null once it has landed.</summary>
			public Func<Task<ItemWriteOutcome>> ItemFlush;
			/// <summary>
			/// Sub-entity rows still to write before the release they belong to, or null. Replaced,
			/// never mutated, while queued: an attempt writes the instance it read.
			/// </summary>
			public SubEntitySnapshot SubEntities;
			/// <summary>
			/// Earliest time the next attempt may run, in <see cref="MonotonicClock"/> seconds. The
			/// backoff is a local duration: on the wall clock a host stepped back held every pending
			/// release for the size of the step, with each one's session claim still out.
			/// </summary>
			public double NextAttemptAt;
			/// <summary>Attempts made so far, used for backoff and for giving up.</summary>
			public int Attempts;
			/// <summary>True while an attempt is running.</summary>
			public bool InFlight;
			/// <summary>
			/// Set, under <see cref="Gate"/>, as the entry is removed from the map. A merge that finds
			/// it set has lost the race with the removal and puts its work in a fresh entry instead.
			/// </summary>
			public bool Retired;
		}

		/// <summary>
		/// Outstanding saves/releases awaiting retry, keyed by character ID.
		/// </summary>
		/// <remarks>
		/// Exists so that a full async-worker channel or a transient database error can never
		/// silently strand a claimed character session. A stranded claim is not a local
		/// problem: the character stays Online in the database, so the scene server the player
		/// is transferring to cannot claim it and kicks them, repeatedly, until the lease
		/// expires.
		/// </remarks>
		private readonly ConcurrentDictionary<long, PendingCharacterFlush> pendingFlushes =
			new ConcurrentDictionary<long, PendingCharacterFlush>();

		/// <summary>Interval in seconds between pending-flush retry passes.</summary>
		private const float PendingFlushRetryIntervalSeconds = 3f;

		/// <summary>
		/// Removes <paramref name="pending"/> from the map if it is still the entry there, retiring it
		/// so no later merge can land in it. Call under <c>pending.Gate</c>.
		/// </summary>
		private void RetirePendingFlushLocked(long characterID, PendingCharacterFlush pending)
		{
			pending.Retired = true;
			// Value-matched: a fresh entry that has replaced this one must stay.
			((ICollection<KeyValuePair<long, PendingCharacterFlush>>)pendingFlushes)
				.Remove(new KeyValuePair<long, PendingCharacterFlush>(characterID, pending));
		}

		/// <summary>
		/// Empties the pending-flush queue and returns its contents, for a final synchronous
		/// flush during shutdown.
		/// </summary>
		private List<KeyValuePair<long, PendingCharacterFlush>> DrainPendingFlushes()
		{
			var drained = new List<KeyValuePair<long, PendingCharacterFlush>>(pendingFlushes.Count);
			foreach (var kvp in pendingFlushes)
			{
				lock (kvp.Value.Gate)
				{
					if (kvp.Value.Retired)
					{
						continue;
					}
					RetirePendingFlushLocked(kvp.Key, kvp.Value);
				}
				drained.Add(kvp);
			}
			return drained;
		}

		/// <summary>
		/// Drops any retry queued for a character, without running it. For a character whose claim
		/// belongs to another server now.
		/// </summary>
		private void DropPendingFlush(long characterID)
		{
			if (pendingFlushes.TryGetValue(characterID, out PendingCharacterFlush pending))
			{
				lock (pending.Gate)
				{
					if (!pending.Retired)
					{
						RetirePendingFlushLocked(characterID, pending);
					}
				}
			}
		}

		/// <summary>
		/// Maximum retry attempts before giving up. With the backoff below this spans well over
		/// the session lease duration, after which the claim expires on its own and any further
		/// release would match nothing anyway.
		/// </summary>
		private const int MaxPendingFlushAttempts = 12;

		/// <summary>
		/// Records a save, an item flush and/or a release for retry, merging with any entry already
		/// pending for the same character.
		/// </summary>
		/// <remarks>
		/// New work restarts the entry's attempt count: it is new, and should not inherit the few
		/// attempts an older problem left it.
		/// </remarks>
		/// <param name="characterID">Character the work belongs to.</param>
		/// <param name="charData">Character snapshot to persist, or null if only releasing.</param>
		/// <param name="sessionInfo">Session ownership to release, or null if only saving.</param>
		/// <param name="itemFlush">The departure item flush to land before the release, or null.</param>
		/// <param name="subEntities">
		/// Sub-entity rows to land before the release, or null. Only meaningful with the claim they
		/// were captured under still held, which is why they travel with the release.
		/// </param>
		private void QueuePendingFlush(long characterID, CharacterData? charData, CharacterSessionInfo? sessionInfo, Func<Task<ItemWriteOutcome>> itemFlush = null, SubEntitySnapshot subEntities = null)
		{
			bool hasSubEntities = subEntities != null && subEntities.Claims.Count > 0;
			if (characterID <= 0 || (charData == null && sessionInfo == null && itemFlush == null && !hasSubEntities))
			{
				return;
			}

			while (true)
			{
				PendingCharacterFlush entry = pendingFlushes.GetOrAdd(characterID, _ => new PendingCharacterFlush
				{
					NextAttemptAt = MonotonicClock.NowSeconds,
				});

				lock (entry.Gate)
				{
					if (entry.Retired)
					{
						// Removed while we waited for it; the next lookup finds or makes the live one.
						continue;
					}

					// Keep the newest snapshot; keep whichever session token we have, since a
					// character only ever holds one claim at a time.
					if (charData.HasValue &&
						(!entry.CharacterData.HasValue || charData.Value.Version >= entry.CharacterData.Value.Version))
					{
						entry.CharacterData = charData;
					}
					if (sessionInfo.HasValue)
					{
						entry.Session = sessionInfo;
					}
					if (itemFlush != null)
					{
						entry.ItemFlush = itemFlush;
					}
					if (hasSubEntities)
					{
						if (entry.SubEntities != null && SameClaims(entry.SubEntities, subEntities))
						{
							/* A new instance, never the queued one extended: an attempt may be writing
							 * the instance it read, outside this lock. Rows the older instance carried
							 * are written again, and a repeated row is superseded, not duplicated. */
							var merged = new SubEntitySnapshot();
							merged.AddFrom(entry.SubEntities);
							merged.AddFrom(subEntities);
							entry.SubEntities = merged;
						}
						else
						{
							/* Replaced, never merged across sessions. Rows queued under an older claim
							 * are that session's, and a merged snapshot would write them under the new
							 * claim — where a higher version captured by the old session could beat the
							 * new session's rows. Under their own claim they would only be refused. */
							if (entry.SubEntities != null)
							{
								Log.Warning("CharacterSystem",
									$"Character {characterID}: sub-entity rows queued under an earlier session claim were dropped for a newer session's; the gate would have refused them.");
							}
							entry.SubEntities = subEntities;
						}
					}
					entry.Attempts = 0;
					entry.NextAttemptAt = MonotonicClock.NowSeconds;
					return;
				}
			}
		}

		/// <summary>Whether two snapshots were captured under the same claims.</summary>
		private static bool SameClaims(SubEntitySnapshot a, SubEntitySnapshot b)
		{
			if (a.Claims.Count != b.Claims.Count)
			{
				return false;
			}
			foreach (KeyValuePair<long, CharacterSessionLeaseData> claim in a.Claims)
			{
				if (!b.Claims.TryGetValue(claim.Key, out CharacterSessionLeaseData other) ||
					other.OwnerServerID != claim.Value.OwnerServerID ||
					other.OwnerToken != claim.Value.OwnerToken)
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>
		/// Periodic retry pass over <see cref="pendingFlushes"/>.
		/// </summary>
		private void OnPeriodicPendingFlushRetry(float deltaTime)
		{
			if (Server == null || Server.ServerState != ConnectionState.Started || pendingFlushes.IsEmpty)
			{
				return;
			}

			double now = MonotonicClock.NowSeconds;

			foreach (var kvp in pendingFlushes)
			{
				long characterID = kvp.Key;
				PendingCharacterFlush pending = kvp.Value;

				lock (pending.Gate)
				{
					if (pending.Retired || pending.InFlight || now < pending.NextAttemptAt)
					{
						continue;
					}
					pending.InFlight = true;
				}

				/* Keyed on the character. The release at the end of an attempt hands back the claim that
				 * every write already queued on the character's lane quotes — a quest update, a grant,
				 * a forget, a pet dismissal, a merchant's currency row — and each of those is
				 * ownership-gated: landing after the release it is refused, not merely late. Unkeyed,
				 * an attempt could run ahead of that lane whenever it was backed up, which is exactly
				 * when a save-and-release gets here. On the lane it runs after them, as the
				 * save-and-release it stands in for does. */
				if (!EnqueueAsyncWork(() => RunPendingFlushAsync(characterID, pending), characterID))
				{
					// Still saturated; leave it queued and try again next pass.
					lock (pending.Gate)
					{
						pending.InFlight = false;
					}
				}
			}
		}

		/// <summary>
		/// Executes one retry of a pending save, item flush and release, rescheduling or dropping the
		/// entry according to the outcome.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The work is read from the entry under its lock, done without it, and then cleared from the
		/// entry only where the entry still holds the same work: a merge that landed meanwhile is new
		/// work and stays for the next attempt. The completion test and the removal happen together
		/// under the same lock, so nothing can be merged into an entry that is on its way out.
		/// </para>
		/// <para>
		/// <b>The release waits for the item flush.</b> The flush proves ownership with the lease this
		/// server still holds; a release that landed first would let the destination claim and load
		/// the character without the flush, and then refuse it. A flush that keeps failing therefore
		/// keeps the claim until it lands or the attempts run out, after which the lease expires on
		/// its own. One that fails for good (<see cref="ItemWriteOutcome.Rejected"/>) is abandoned,
		/// with an error naming the loss, and the claim is released: holding it could not deliver it.
		/// </para>
		/// <para>
		/// <b>So does it wait for the sub-entity rows</b>, for the same reason: they are ownership-gated
		/// too, so once the claim is back they would be refused rather than merely arrive late. A table
		/// still failing after its bounded attempts keeps the claim for the next pass.
		/// </para>
		/// </remarks>
		private async Task RunPendingFlushAsync(long characterID, PendingCharacterFlush pending)
		{
			CharacterData? row;
			CharacterSessionInfo? session;
			Func<Task<ItemWriteOutcome>> flush;
			SubEntitySnapshot subEntities;
			lock (pending.Gate)
			{
				pending.Attempts++;
				row = pending.CharacterData;
				session = pending.Session;
				flush = pending.ItemFlush;
				subEntities = pending.SubEntities;
			}

			bool rowDone = !row.HasValue;
			bool flushDone = flush == null;
			bool subEntitiesDone = subEntities == null;
			bool releaseDone = !session.HasValue;
			bool claimLost = false;

			try
			{
				if (row.HasValue)
				{
					/* Gated on the claim when we still hold one. When we do not (the release
					 * already landed and only the save is outstanding) the version guard is the
					 * only protection available, and dropping the write entirely would lose the
					 * state outright — so the ungated path stays for that case, as before. */
					CharacterSaveOutcome saved = await SaveCharacterOutcomeAsync(row.Value, session);
					rowDone = saved != CharacterSaveOutcome.Retry;
					claimLost = saved == CharacterSaveOutcome.OwnershipLost;
				}

				/* Items before the release, as on the normal path. The flush proves ownership
				 * with the lease we still hold; a release that landed first would let the
				 * destination claim and refuse it. */
				if (!claimLost && flush != null)
				{
					ItemWriteOutcome flushed = await RunItemFlushOnceAsync(flush, characterID);
					flushDone = flushed != ItemWriteOutcome.Retry;
					if (flushed == ItemWriteOutcome.NotOwned)
					{
						claimLost = true;
					}
					else if (flushed == ItemWriteOutcome.Rejected)
					{
						await Log.Error("CharacterSystem",
							$"RunPendingFlushAsync: the item flush for character {characterID} was refused for good; its item changes since the last snapshot are lost.");
					}
				}

				/* The sub-entity rows, also before the release and for the same reason: each quotes the
				 * claim it was captured under, so it lands only while that claim is held — and would be
				 * refused, not merely late, after the release below. */
				if (!claimLost && subEntities != null)
				{
					subEntitiesDone = await SaveSubEntitiesSequentiallyAsync(subEntities, characterID);
				}

				if (!claimLost && session.HasValue && flushDone && subEntitiesDone)
				{
					releaseDone = await ReleaseCharacterSessionAsync(characterID, session.Value.ServerID, session.Value.Token);
				}

				if (claimLost)
				{
					// Another server owns the character: nothing here is ours to write or release.
					rowDone = flushDone = subEntitiesDone = releaseDone = true;
				}
			}
			catch (Exception ex)
			{
				await Log.Error("CharacterSystem", $"RunPendingFlushAsync failed for character {characterID}: {ex}");
			}
			finally
			{
				bool gaveUp = false;
				lock (pending.Gate)
				{
					// Clear only the work this attempt finished, and only if nothing newer replaced it.
					if (rowDone && row.HasValue && pending.CharacterData.HasValue &&
						pending.CharacterData.Value.Version == row.Value.Version)
					{
						pending.CharacterData = null;
					}
					if (flushDone && flush != null && ReferenceEquals(pending.ItemFlush, flush))
					{
						pending.ItemFlush = null;
					}
					if (subEntitiesDone && subEntities != null && ReferenceEquals(pending.SubEntities, subEntities))
					{
						pending.SubEntities = null;
					}
					if (releaseDone && session.HasValue && pending.Session.HasValue &&
						pending.Session.Value.Token == session.Value.Token &&
						pending.Session.Value.ServerID == session.Value.ServerID)
					{
						pending.Session = null;
					}

					bool completed = pending.CharacterData == null && pending.Session == null && pending.ItemFlush == null && pending.SubEntities == null;
					if (completed)
					{
						RetirePendingFlushLocked(characterID, pending);
					}
					else if (pending.Attempts >= MaxPendingFlushAttempts)
					{
						RetirePendingFlushLocked(characterID, pending);
						gaveUp = true;
					}
					else
					{
						// Linear backoff: fast enough that a normal scene transition is not held
						// up, slow enough that a database outage is not hammered.
						pending.NextAttemptAt = MonotonicClock.NowSeconds + Math.Min(3 * pending.Attempts, 30);
					}
					pending.InFlight = false;
				}

				if (gaveUp)
				{
					_ = Log.Error("CharacterSystem",
						$"Giving up on pending flush for character {characterID} after {MaxPendingFlushAttempts} attempts" +
						(flush != null && !flushDone ? "; its item flush never landed and those item changes are lost" : string.Empty) +
						(subEntities != null && !subEntitiesDone ? "; some of its sub-entity tables never landed and those changes are lost" : string.Empty) +
						". Any unreleased session will free itself when its lease expires.");
				}
			}
		}

		#endregion

		#region Lost-Claim Eviction

		/// <summary>
		/// Character IDs already scheduled for eviction, so a claim lost during a batch does
		/// not queue one eviction per failing save.
		/// </summary>
		private readonly ConcurrentDictionary<long, byte> evictionRequested = new ConcurrentDictionary<long, byte>();

		/// <summary>
		/// Schedules <paramref name="characterID"/> to be removed from this server because its
		/// session claim is no longer held here.
		/// </summary>
		/// <remarks>
		/// Safe to call from an async worker; the removal itself is marshalled to the main
		/// thread, which is the only place FishNet and the mapping dictionaries may be touched.
		/// </remarks>
		private void RequestEviction(long characterID, string reason)
		{
			if (characterID <= 0 || !evictionRequested.TryAdd(characterID, 0))
			{
				return;
			}

			if (!TryEnqueueMainThread(() => EvictLostCharacter(characterID, reason)))
			{
				// Nothing will run the eviction, so allow a later save failure to try again.
				evictionRequested.TryRemove(characterID, out _);
				Log.Error("CharacterSystem",
					$"Could not schedule eviction of character {characterID} ({reason}); it will keep failing to save until the next attempt succeeds.");
			}
		}

		/// <summary>
		/// Removes a character this server no longer owns, without saving or releasing it.
		/// </summary>
		/// <remarks>
		/// Deliberately does not save: another server has held the claim since it loaded, and
		/// the state here has been diverging ever since. Writing it would overwrite the live
		/// session's progress — the opposite of preserving information. It also does not
		/// release: the row's owner token is somebody else's, so a release would either match
		/// nothing or, worse, would be wrong to perform.
		/// <para>
		/// The connection is disconnected rather than left in place. The client's reconnect
		/// loop takes it back through the world server, which routes it to whichever scene
		/// server actually owns the character, and it reloads the authoritative row — the same
		/// recovery path a dropped scene server already produces.
		/// </para>
		/// </remarks>
		private void EvictLostCharacter(long characterID, string reason)
		{
			evictionRequested.TryRemove(characterID, out _);

			// The snapshot queued for retry belongs to a session we no longer own.
			DropPendingFlush(characterID);

			if (!Server.DataContainerRegistry.TryGet(out ICharacterMappingData<NetworkConnection> data))
			{
				return;
			}

			// Drop the claim bookkeeping without releasing — see the remarks above.
			data.SessionTokens.Remove(characterID);

			/* A lingering body has no connection and is not in CharactersByID, so it is removed
			 * through its own path: balance the scene count the linger took and despawn. That
			 * path is terminal for this character, so return once it runs. */
			if (lingeringCharacters.ContainsKey(characterID))
			{
				Log.Error("CharacterSystem",
					$"Evicting combat-logout body for character {characterID}: {reason}. " +
					"Its unsaved damage is discarded; the owning server's state stands.");
				DropLingeringCharacter(characterID);
				return;
			}

			if (!data.CharactersByID.TryGetValue(characterID, out IPlayerCharacter character))
			{
				// Not resident (already gone, or still in the waiting-scene-load stage keyed by
				// connection). The waiting case is handled by its own handshake timeout.
				Log.Warning("CharacterSystem", $"Evicting character {characterID} ({reason}): not resident; claim bookkeeping dropped.");
				return;
			}

			Log.Error("CharacterSystem",
				$"Evicting {character.CharacterName} ({characterID}) from this scene server: {reason}. " +
				"Its client will reconnect and reload the state held by the server that owns it now.");

			NetworkConnection owner = character.Owner;

			data.CharactersByID.Remove(characterID);
			data.CharactersByLowerCaseName.Remove(character.CharacterNameLower);
			if (data.CharactersByWorld.TryGetValue(character.WorldServerID, out Dictionary<long, IPlayerCharacter> worldChars))
			{
				worldChars.Remove(characterID);
			}
			if (owner != null)
			{
				data.ConnectionCharacters.Remove(owner);
			}

			// Keeps scene population and every social system in step, exactly as a normal
			// disconnect would.
			DispatchCharacterEvent(OnDisconnect, owner, character, nameof(OnDisconnect));

			if (character.NetworkObject != null)
			{
				DispatchCharacterEvent(OnDespawnCharacter, owner, character, nameof(OnDespawnCharacter));

				// Out of the world scene, as every once-spawned character is pooled. See SaveAndDespawnCharacter.
				if (!PersistentPool.Despawn(Server.NetworkWrapper.NetworkManager, character.NetworkObject))
				{
					Server.NetworkWrapper.NetworkManager.StorePooledInstantiated(character.NetworkObject, true);
					PersistentPool.Keep(Server.NetworkWrapper.NetworkManager, character.NetworkObject);
				}
			}

			if (owner != null && owner.IsActive)
			{
				// Non-terminal: the world server will route the reconnect to whichever scene
				// server holds the claim now, which is exactly the recovery this eviction wants.
				DisconnectWithNotice(owner, DisconnectNoticeReason.SessionSuperseded);
			}
		}

		#endregion

		#region Session Lease Refresh

		/// <summary>
		/// Extends the lease on every session this server currently holds, in one statement per
		/// 500 sessions.
		/// </summary>
		/// <remarks>
		/// Runs on its own timer rather than inside the save loop, so a claim's liveness never
		/// depends on how long a save pass takes. A pass stretched by a slow database or a
		/// backed-up worker (or skipped at the save gate) would otherwise leave characters going
		/// longer than the lease duration between refreshes, claimable by another server while
		/// still online.
		/// </remarks>
		private void OnPeriodicSessionLeaseRefresh(float deltaTime)
		{
			if (!Initialized || Server == null || Server.ServerState != ConnectionState.Started)
			{
				return;
			}

			if (!Server.DataContainerRegistry.TryGet(out ICharacterMappingData<NetworkConnection> data) ||
				data.SessionTokens.Count == 0)
			{
				return;
			}

			var leases = new List<CharacterSessionLeaseData>(data.SessionTokens.Count);
			foreach (var kvp in data.SessionTokens)
			{
				leases.Add(new CharacterSessionLeaseData(kvp.Key, kvp.Value.ServerID, kvp.Value.Token));
			}

			if (!EnqueueAsyncWork(() => RefreshSessionLeasesAsync(leases)))
			{
				Log.Warning("CharacterSystem", "OnPeriodicSessionLeaseRefresh: Failed to enqueue lease refresh work item.");
			}
		}

		/// <summary>
		/// Sends a batched session-lease refresh to the database.
		/// </summary>
		private async Task RefreshSessionLeasesAsync(List<CharacterSessionLeaseData> leases)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterService>(out var characterService))
				{
					return;
				}

				DatabaseResult<int> result = await characterService.RefreshSessionLeasesAsync(leases);
				if (!result.IsSuccess)
				{
					await Log.Warning("CharacterSystem", $"RefreshSessionLeasesAsync DB error: {result.ErrorCode} - {result.ErrorMessage}");
					return;
				}

				if (result.Data < leases.Count)
				{
					/* Every entry we send is a session this server believes it owns, so a short
					 * count means at least one claim is gone — released underneath us, or taken
					 * over by another server after our lease lapsed.
					 *
					 * The count alone does not say which, and until it did, nothing could act on
					 * it: this server kept simulating and saving characters it no longer owned,
					 * overwriting the live owner's state on every periodic save. Resolve the
					 * specific characters and evict them. The ownership-gated saves (PersistManyAsync,
					 * SaveCharacterAsync) are the hard guarantee; this is what makes the recovery
					 * prompt (one refresh interval) instead of waiting for a save to be refused. */
					await Log.Warning("CharacterSystem",
						$"Session lease refresh updated {result.Data} of {leases.Count} held sessions; " +
						"identifying the claims this server has lost.");

					DatabaseResult<IReadOnlyList<long>> lostResult = await characterService.FetchUnownedSessionsAsync(leases);
					if (!lostResult.IsSuccess || lostResult.Data == null)
					{
						await Log.Error("CharacterSystem",
							$"Could not identify lost session claims: {lostResult.ErrorCode} - {lostResult.ErrorMessage}. " +
							"Affected characters will be evicted when their next save is refused.");
						return;
					}

					foreach (long characterID in lostResult.Data)
					{
						RequestEviction(characterID, "session lease lapsed and the claim was taken by another server");
					}
				}
			}
			catch (Exception ex)
			{
				await Log.Error("CharacterSystem", $"RefreshSessionLeasesAsync failed: {ex}");
			}
		}

		#endregion

		/// <summary>
		/// Releases a character session from Online → Offline in a single step.
		/// </summary>
		/// <returns>
		/// <c>true</c> when the session is known to be released, or when retrying could not
		/// possibly help; <c>false</c> only for transient failures worth another attempt.
		/// </returns>
		private async Task<bool> ReleaseCharacterSessionAsync(long characterID, long serverID, Guid sessionToken)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterService>(out var characterService))
				{
					await Log.Error("CharacterSystem", $"ReleaseCharacterSessionAsync: ICharacterService not available for character {characterID}.");
					return false;
				}

				if (sessionToken == Guid.Empty || serverID <= 0)
				{
					await Log.Error("CharacterSystem", $"ReleaseCharacterSessionAsync: Invalid session token or server ID for character {characterID}.");
					// Nothing to retry with — a malformed token can never match a row.
					return true;
				}

				DatabaseResult releaseResult = await characterService.ReleaseAsync(characterID, serverID, sessionToken);
				if (releaseResult.IsSuccess)
				{
					return true;
				}

				// The session is no longer ours: either it was already released, or the lease
				// expired and another server claimed it. Retrying would keep matching nothing,
				// and must not be mistaken for a stuck claim.
				if (releaseResult.ErrorCode == DatabaseErrorCodes.InvalidOperation ||
					releaseResult.ErrorCode == DatabaseErrorCodes.NotFound)
				{
					await Log.Warning("CharacterSystem", $"ReleaseCharacterSessionAsync: session for character {characterID} is no longer owned by this server ({releaseResult.ErrorCode}); nothing to release.");
					return true;
				}

				await Log.Error("CharacterSystem", $"ReleaseCharacterSessionAsync: ReleaseAsync failed for character {characterID}: {releaseResult.ErrorCode} - {releaseResult.ErrorMessage}");
				return false;
			}
			catch (Exception ex)
			{
				await Log.Error("CharacterSystem", $"ReleaseCharacterSessionAsync failed for character {characterID}: {ex}");
				return false;
			}
		}

		#region Buff / Attribute / Ability Snapshot + Persistence

		/// <summary>
		/// Captures the character's complete set of active buffs, converting absolute ticks into
		/// remaining seconds. Main-thread only.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>A set, not a list of changes.</b> The result replaces whatever the database holds for
		/// the character, in the same transaction as the character row and only when that row is
		/// written (<see cref="CharacterData.Buffs"/>). An empty list is a real answer — "no buffs" —
		/// and is what deletes the rows of buffs that expired, were dismissed or were stripped on
		/// death. Buffs used to be upserted one row at a time and never deleted, and the load
		/// restores every row it finds, so each of those came back at the next login.
		/// </para>
		/// <para>
		/// Every row carries the character's snapshot version rather than a counter of its own. A
		/// per-buff counter cannot order a set: a buff re-applied after it ended is a new instance
		/// whose counter restarts, and an empty set has no row to carry one. The row version is the
		/// ordering; see <c>CharacterBuffService.ReplaceSetsAsync</c>.
		/// </para>
		/// <para>
		/// Remaining time is frozen while the character is offline, exactly as before: what is left
		/// now is what is left at the next login.
		/// </para>
		/// <para>
		/// Permanent buffs are left out, which deletes any rows they had: they are rebuilt from the
		/// environment after the login. See <see cref="IsPersistedBuff"/>.
		/// </para>
		/// </remarks>
		/// <param name="character">The character whose buffs to capture.</param>
		/// <param name="version">The snapshot version the row is being written at.</param>
		/// <returns>
		/// The set, possibly empty; or null when it could not be read (no buff controller, no time
		/// manager), which leaves the stored buffs untouched rather than deleting them on the strength
		/// of a set nobody built.
		/// </returns>
		private List<CharacterBuffData> CaptureBuffSet(IPlayerCharacter character, long version)
		{
			if (!character.TryGet(out IBuffController buffController))
			{
				return null;
			}

			var timeManager = Server?.NetworkWrapper?.NetworkManager?.TimeManager;
			if (timeManager == null)
			{
				return null;
			}

			var buffs = new List<CharacterBuffData>(buffController.Buffs.Count);
			if (buffController.Buffs.Count == 0)
			{
				return buffs;
			}

			float tickDelta = (float)timeManager.TickDelta;
			uint currentTick = buffController.ResolveAuthoritativeTick(timeManager.LocalTick);

			foreach (var kvp in buffController.Buffs)
			{
				Buff buff = kvp.Value;
				// Permanent buffs are the environment's, not the character's. See IsPersistedBuff.
				if (buff == null || !IsPersistedBuff(buff.Template))
				{
					continue;
				}

				// Convert absolute ticks → remaining seconds. Clamp negatives to 0 (about to expire).
				int remainingTicks = (int)(buff.ExpiryTick - currentTick);
				int nextTickTicks = (int)(buff.NextTickTick - currentTick);
				float remainingTime = remainingTicks > 0 ? remainingTicks * tickDelta : 0f;
				float tickTime = nextTickTicks > 0 ? nextTickTicks * tickDelta : 0f;

				buffs.Add(new CharacterBuffData(
					id: 0,
					version: version,
					characterID: character.ID,
					templateID: buff.Template.ID,
					remainingTime: remainingTime,
					tickTime: tickTime,
					stacks: buff.Stacks,
					tickCount: buff.TickCount
				));
			}
			return buffs;
		}

		/// <summary>
		/// Whether a buff is written to the database with its owner. Pure.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Permanent buffs are not.</b> They are the weather-exposure buffs, and they belong to the
		/// environment rather than the character: <c>WeatherExposureController</c> (and a buff volume)
		/// applies and removes them every tick from where the character is standing, so after a login
		/// they come back on their own within a tick. A saved one could only come back wrong: a
		/// permanent buff has no expiry (<c>ExpiryTick</c> is <c>UNSET_TICK</c>), so it was captured
		/// with no time remaining and restored as a buff that expires on its first tick — an icon that
		/// flickers, an apply and a remove, and an exposure system that briefly thinks it already has
		/// the buff on.
		/// </para>
		/// <para>
		/// Leaving them out of the set is also what deletes the rows saved before this rule: the set
		/// replaces everything the database holds for the character (see <see cref="CaptureBuffSet"/>),
		/// so the first save afterwards drops them. The load skips any it still finds until then.
		/// </para>
		/// </remarks>
		/// <param name="template">The buff's template; null is never persisted.</param>
		/// <returns>True when the buff is saved and restored with its owner.</returns>
		public static bool IsPersistedBuff(BaseBuffTemplate template)
		{
			return template != null && !template.IsPermanent;
		}

		/// <summary>
		/// Appends a snapshot of the character's attributes: the BASE value for every attribute, plus
		/// the current value for resources. Main-thread only; each appended DTO has its Version
		/// bumped on the runtime attribute.
		/// </summary>
		/// <remarks>
		/// <b>The external modifier is deliberately not written, and the doc used to claim it was.</b>
		/// It is the sum of live contributors — equipped items, active buffs, region effects, NPC
		/// scaling — and every one of those is independently persisted or re-derived on load, so the
		/// modifier rebuilds itself. Storing it as well would mean storing a cache of rows the
		/// database already holds, and a stale one would be indistinguishable from a real bonus.
		/// <para>
		/// A resource's CURRENT value is the exception and must be written: it is depletable state,
		/// not a derived one. Nothing reconstructs "logged out on 43 health".
		/// </para>
		/// </remarks>
		private void AppendAttributeData(IPlayerCharacter character, List<CharacterAttributeData> attributes)
		{
			if (!character.TryGet(out ICharacterAttributeController attrController))
			{
				return;
			}

			foreach (var kvp in attrController.Attributes)
			{
				var attr = kvp.Value;
				/* Unchanged since the database last confirmed it, so there is nothing to write.
				 * The row is not merely rejected further down — it is never built, never sent, and
				 * never probed.
				 *
				 * A version of zero is NOT a reason to skip. It used to be ("template-initialized,
				 * never loaded"), which meant any attribute a character had no row for — every
				 * template added to the prefab database after the character was created — was
				 * never written for the life of that character. A dirty zero-version attribute is
				 * an insert: the bump below makes it version 1, and the upsert's version guard
				 * still refuses it if a row somehow exists. */
				if (!attr.PersistenceDirty)
					continue;
				/* A trade has moved this value in memory and its transaction has not answered yet:
				 * that transaction is the row's only writer until it does. Written here, the moved
				 * value could land while the trade is still undecided, and a refusal would then leave
				 * the database holding a payment or a credit for a trade that never happened — and for
				 * a departing character there is no later save to put it right. Left dirty, so the
				 * first pass after the outcome writes whatever memory then holds.
				 * See CharacterAttribute.IsSettling and TradeCurrencySettlement. */
				if (attr.IsSettling)
					continue;
				attr.Version++;
				attr.MarkPersistPending(attr.Version);
				attributes.Add(new CharacterAttributeData(
					id: 0,
					version: attr.Version,
					characterID: character.ID,
					templateID: kvp.Key,
					value: attr.Value,
					currentValue: 0.0f
				));
			}
			foreach (var kvp in attrController.ResourceAttributes)
			{
				var resAttr = kvp.Value;
				// Settling: see above.
				if (!resAttr.PersistenceDirty || resAttr.IsSettling)
					continue;
				resAttr.Version++;
				resAttr.MarkPersistPending(resAttr.Version);
				attributes.Add(new CharacterAttributeData(
					id: 0,
					version: resAttr.Version,
					characterID: character.ID,
					templateID: kvp.Key,
					value: resAttr.Value,
					currentValue: resAttr.CurrentValue
				));
			}
		}

		/// <summary>
		/// Appends a snapshot of the character's crafted abilities (template + event IDs).
		/// Cooldowns are NOT persisted by design — they reset on relog (gameplay-friendly).
		/// Main-thread only. Each appended DTO has its Version bumped on the runtime Ability.
		/// </summary>
		private void AppendAbilityData(IPlayerCharacter character, List<CharacterAbilityData> abilities)
		{
			if (!character.TryGet(out IAbilityController abilityController) || abilityController.KnownAbilities.Count == 0)
			{
				return;
			}

			foreach (var kvp in abilityController.KnownAbilities)
			{
				Ability ability = kvp.Value;
				if (ability == null || ability.Template == null)
				{
					continue;
				}

				/* Unchanged since the database last confirmed it. Skipping ahead of the ToList also
				 * avoids building an event-id list per ability per save for a row nobody will
				 * write. */
				if (!ability.PersistenceDirty)
				{
					continue;
				}

				List<int> eventIds = ability.AbilityEvents.Keys.ToList();

				ability.Version++;
				abilities.Add(new CharacterAbilityData(
					id: ability.ID,
					version: ability.Version,
					characterID: character.ID,
					templateID: ability.Template.ID,
					abilityEvents: eventIds,
					cooldown: 0.0f
				));
			}
		}

		/// <summary>
		/// One character's complete pet state, captured together.
		/// </summary>
		/// <remarks>
		/// The three pet tables are snapshotted and written as a unit because they share a
		/// version stream — see <see cref="AppendPetData"/>. Splitting them into three flat
		/// lists, the way the character's own sub-entities are handled, would lose the grouping
		/// that makes a restored pet's health provably belong to the pet row it came with.
		/// </remarks>
		private readonly struct PetSnapshot
		{
			/// <summary>The pet row: which template, which abilities, and whether it was out.</summary>
			public readonly CharacterPetData Pet;

			/// <summary>The pet's attribute values at the moment of the snapshot.</summary>
			public readonly List<CharacterPetAttributeData> Attributes;

			/// <summary>The pet's active buffs at the moment of the snapshot.</summary>
			public readonly List<CharacterPetBuffData> Buffs;

			public PetSnapshot(CharacterPetData pet, List<CharacterPetAttributeData> attributes, List<CharacterPetBuffData> buffs)
			{
				Pet = pet;
				Attributes = attributes;
				Buffs = buffs;
			}
		}

		/// <summary>
		/// Appends a snapshot of the character's live pet — the pet row, its attributes and its
		/// buffs — so a pet survives a logout or a scene-server handover with the health and
		/// effects it actually had.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Main-thread only, and it must run <em>after</em> <see cref="BuildCharacterData"/> for
		/// the same character: all three pet tables are stamped with the character's own version
		/// counter, which that method increments.
		/// </para>
		/// <para>
		/// Sharing the character's counter is what makes the pet's version stream work without a
		/// read. A pet is pooled and re-summoned freely, so it has no durable identity of its own
		/// to hang a counter on — a freshly summoned pet starting from version 1 would be
		/// rejected as stale against rows left by the pet before it. The character's counter is
		/// monotonic, survives restarts because it is loaded from the character row, and is
		/// already incremented exactly once per snapshot.
		/// </para>
		/// <para>
		/// It also gives the restore path a correctness check it could not otherwise have: rows
		/// written in one pass all carry the same version, so
		/// <c>PetSystem.LoadAndSpawnPetAsync</c> can require an attribute or buff row to match
		/// the pet row's version and thereby ignore leftovers from a previous pet that happened
		/// to know an attribute this one does not.
		/// </para>
		/// </remarks>
		/// <param name="character">The character whose pet to snapshot.</param>
		/// <param name="pets">Destination for the snapshot.</param>
		private void AppendPetData(IPlayerCharacter character, List<PetSnapshot> pets)
		{
			if (!character.TryGet(out IPetController petController))
			{
				return;
			}

			Pet pet = petController.Pet;
			if (pet == null)
			{
				/* No pet out, nothing to say. Whatever the pet row holds is already correct —
				 * it was written to spawned = false by whichever path dismissed or killed the
				 * pet — and writing a row here would only be able to say "no template", which
				 * is not a state the schema can express. */
				return;
			}

			/* Zero is "no template", and nothing else is. Template ids are signed deterministic
			 * hashes, so a negative one is as valid as a positive one — refusing everything below
			 * one skipped the save of roughly half of all pets. (ICharacterPetService applies the
			 * same wrong test on its side, and must be corrected there too before those pets land.) */
			int templateID = pet.PetAbilityTemplate != null ? pet.PetAbilityTemplate.ID : 0;
			if (templateID == 0)
			{
				Log.Warning("CharacterSystem", $"Character {character.ID} has a pet with no resolvable ability template; its state was not persisted.");
				return;
			}

			// BuildCharacterData has already incremented this for the current pass.
			long version = Math.Max(1L, character.Version);

			// Abilities granted at summon time live only on the controller until this runs.
			pet.CaptureKnownAbilities();

			float currentHealth = 0.0f;
			if (pet.TryGet(out ICharacterAttributeController petAttributeController) &&
				petAttributeController.TryGetHealthAttribute(out CharacterResourceAttribute health))
			{
				currentHealth = health.CurrentValue;
			}

			/* A dead owner's pet is not out. Death dismisses the pet (PetSystem.DamageController_OnKilled),
			 * and that handler runs AFTER this system's own kill handler, which subscribed first — so a
			 * combat-logout body killed while its owner was away was finalised, captured here with its
			 * pet still at its side, and despawned, all before the dismissal could run; the dismissal
			 * then found no pet, and the row said the pet was out. The owner got a pet back at their
			 * next login that death had taken from every connected player. Encoded here, where the row
			 * is built, the rule holds whichever handler runs first. */
			CharacterPetData petData = new CharacterPetData(
				id: 0,
				version: version,
				characterID: character.ID,
				templateID: templateID,
				abilities: pet.PetAbilityIDs != null ? new List<int>(pet.PetAbilityIDs) : new List<int>(),
				spawned: IsPetOut(currentHealth, character.IsFlagged(CharacterFlags.IsDead)));

			var attributeData = new List<CharacterPetAttributeData>(16);
			AppendPetAttributeData(character.ID, version, petAttributeController, attributeData);

			var buffData = new List<CharacterPetBuffData>(4);
			AppendPetBuffData(character.ID, version, pet, buffData);

			pets.Add(new PetSnapshot(petData, attributeData, buffData));
		}

		/// <summary>
		/// Whether a captured pet row says the pet is out, restored at the owner's next login. Pure.
		/// </summary>
		/// <remarks>
		/// A pet at zero health is dead and not out. A pet whose owner is dead is not out either:
		/// death dismisses it — see the capture in <see cref="AppendPetData"/> for why the rule has to
		/// be stated here as well as in the dismissal.
		/// </remarks>
		/// <param name="petHealth">The pet's current health.</param>
		/// <param name="ownerDead">Whether the owner is dead.</param>
		/// <returns>True when the pet should come back with its owner.</returns>
		public static bool IsPetOut(float petHealth, bool ownerDead)
		{
			return petHealth > 0.0f && !ownerDead;
		}

		/// <summary>
		/// Appends the pet's attribute values.
		/// </summary>
		/// <remarks>
		/// Unlike the owner's own attributes, nothing is skipped for a zero version: a pet's
		/// attributes are built from its prefab on every spawn and never carry a version loaded
		/// from the database, so the owner's "skip template defaults" rule would skip every row
		/// a pet has. The stamped version comes from the character instead.
		/// </remarks>
		/// <param name="characterID">The owning character.</param>
		/// <param name="version">Version to stamp every row with.</param>
		/// <param name="attributeController">The pet's attribute controller, or null.</param>
		/// <param name="attributes">Destination for the rows.</param>
		private static void AppendPetAttributeData(
			long characterID,
			long version,
			ICharacterAttributeController attributeController,
			List<CharacterPetAttributeData> attributes)
		{
			if (attributeController == null)
			{
				return;
			}

			foreach (var kvp in attributeController.Attributes)
			{
				attributes.Add(new CharacterPetAttributeData(
					id: 0,
					version: version,
					characterID: characterID,
					templateID: kvp.Key,
					value: kvp.Value.Value,
					currentValue: 0.0f));
			}

			foreach (var kvp in attributeController.ResourceAttributes)
			{
				attributes.Add(new CharacterPetAttributeData(
					id: 0,
					version: version,
					characterID: characterID,
					templateID: kvp.Key,
					value: kvp.Value.Value,
					currentValue: kvp.Value.CurrentValue));
			}
		}

		/// <summary>
		/// Appends the pet's active buffs, converting absolute ticks into remaining seconds the
		/// same way the owner's buffs are converted.
		/// </summary>
		/// <param name="characterID">The owning character.</param>
		/// <param name="version">Version to stamp every row with.</param>
		/// <param name="pet">The pet.</param>
		/// <param name="buffs">Destination for the rows.</param>
		private void AppendPetBuffData(long characterID, long version, Pet pet, List<CharacterPetBuffData> buffs)
		{
			if (!pet.TryGet(out IBuffController buffController) || buffController.Buffs.Count == 0)
			{
				return;
			}

			var timeManager = Server?.NetworkWrapper?.NetworkManager?.TimeManager;
			if (timeManager == null)
			{
				return;
			}

			float tickDelta = (float)timeManager.TickDelta;
			uint currentTick = buffController.ResolveAuthoritativeTick(timeManager.LocalTick);

			foreach (var kvp in buffController.Buffs)
			{
				Buff buff = kvp.Value;
				// A pet stands in the same weather its owner does. See IsPersistedBuff.
				if (buff == null || !IsPersistedBuff(buff.Template))
				{
					continue;
				}

				// Absolute ticks → remaining seconds. Clamp negatives to 0 (about to expire).
				int remainingTicks = (int)(buff.ExpiryTick - currentTick);
				int nextTickTicks = (int)(buff.NextTickTick - currentTick);

				buffs.Add(new CharacterPetBuffData(
					id: 0,
					version: version,
					characterID: characterID,
					templateID: buff.Template.ID,
					remainingTime: remainingTicks > 0 ? remainingTicks * tickDelta : 0f,
					tickTime: nextTickTicks > 0 ? nextTickTicks * tickDelta : 0f,
					stacks: buff.Stacks,
					tickCount: buff.TickCount));
			}
		}

		/// <summary>
		/// Persists pet state — row, attributes and buffs — for every snapshotted character.
		/// </summary>
		/// <remarks>
		/// The pet row goes first. If the two dependent writes fail after it, the restore path's
		/// version check simply rejects the older attribute and buff rows and the pet comes back
		/// at its template defaults — which is a far better outcome than the reverse ordering,
		/// where a pet row that failed to write would leave the next login restoring a previous
		/// pet's health onto the wrong creature.
		/// </remarks>
		/// <param name="pets">Snapshots to write.</param>
		/// <param name="claims">The claims the snapshots were captured under. See <see cref="AppendSubEntities"/>.</param>
		/// <returns>False only when a write failed in a way worth another attempt.</returns>
		private async Task<bool> SavePetsAsync(List<PetSnapshot> pets, IReadOnlyCollection<CharacterSessionLeaseData> claims)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null || pets == null || pets.Count == 0)
				{
					return true;
				}

				bool done = true;

				/* Each of the three writes is reported and none aborts the others. A superseded
				 * pet row means a newer write for that character already landed — a dismissal, or
				 * a logout that overtook the periodic pass — and the attribute and buff rows are
				 * then simply ignored on restore by the version match they no longer satisfy.
				 * Abandoning the remaining writes would leave those tables one pass further out
				 * of date for no benefit. */
				if (Server.Database.ServiceRegistry.TryGet<ICharacterPetService>(out var petService))
				{
					/* The pet-row write also prunes the attribute and buff rows its pet can no longer
					 * restore — see CharacterPetService.PruneUnrestorableRowsAsync — which is why it
					 * stays first: it never deletes the rows written just after it. */
					var petRows = pets.Select(p => p.Pet).ToList();
					DatabaseResult<BulkWriteResult> result = await petService.PersistOwnedAsync(petRows, claims);
					done &= await BulkWriteReporting.ReportAsync("CharacterSystem", "Pet save", result) || !result.IsTransient;
				}

				if (Server.Database.ServiceRegistry.TryGet<ICharacterPetAttributeService>(out var petAttributeService))
				{
					var attributeRows = pets.SelectMany(p => p.Attributes).ToList();
					if (attributeRows.Count > 0)
					{
						DatabaseResult<BulkWriteResult> result = await petAttributeService.PersistOwnedAsync(attributeRows, claims);
						done &= await BulkWriteReporting.ReportAsync("CharacterSystem", "Pet attribute save", result) || !result.IsTransient;
					}
				}

				if (Server.Database.ServiceRegistry.TryGet<ICharacterPetBuffService>(out var petBuffService))
				{
					var buffRows = pets.SelectMany(p => p.Buffs).ToList();
					if (buffRows.Count > 0)
					{
						DatabaseResult<BulkWriteResult> result = await petBuffService.PersistOwnedAsync(buffRows, claims);
						done &= await BulkWriteReporting.ReportAsync("CharacterSystem", "Pet buff save", result) || !result.IsTransient;
					}
				}

				return done;
			}
			catch (Exception ex)
			{
				await Log.Error("CharacterSystem", $"SavePetsAsync failed: {ex}");
				return false;
			}
		}

		/// <summary>
		/// Writes a departing character's hotkey bar under the claim it was captured with.
		/// </summary>
		/// <remarks>
		/// No dirty marks to clear: the bar is written whole, from the live bar, and the character is
		/// on its way out. A superseded row is a newer bar already stored — the hotkey pump's, taken
		/// later — and is fine. See <see cref="AppendDepartureSubEntities"/>.
		/// </remarks>
		/// <param name="hotkeys">The rows, one per slot.</param>
		/// <param name="claims">The claims the rows were captured under.</param>
		/// <returns>False only when the write failed in a way worth another attempt.</returns>
		private async Task<bool> SaveHotkeysAsync(List<CharacterHotkeyData> hotkeys, IReadOnlyCollection<CharacterSessionLeaseData> claims)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterHotkeyService>(out var hotkeyService))
				{
					return true;
				}

				DatabaseResult<BulkWriteResult> result = await hotkeyService.PersistOwnedAsync(hotkeys, claims);
				bool written = await BulkWriteReporting.ReportAsync("CharacterSystem", "Hotkey bar save", result);
				return written || !result.IsTransient;
			}
			catch (Exception ex)
			{
				await Log.Error("CharacterSystem", $"SaveHotkeysAsync failed: {ex}");
				return false;
			}
		}

		/// <summary>
		/// Persists a snapshot of attribute state asynchronously.
		/// </summary>
		/// <param name="attributes">The rows.</param>
		/// <param name="claims">The claims the rows were captured under. See <see cref="AppendSubEntities"/>.</param>
		/// <returns>False only when the write failed in a way worth another attempt.</returns>
		private async Task<bool> SaveAttributesAsync(List<CharacterAttributeData> attributes, IReadOnlyCollection<CharacterSessionLeaseData> claims)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterAttributeService>(out var attrService))
				{
					return true;
				}

				DatabaseResult<BulkWriteResult> result = await attrService.PersistOwnedAsync(attributes, claims);
				bool written = await BulkWriteReporting.ReportAsync("CharacterSystem", "Attribute save", result);

				/* Only a write that landed clears the dirty marks, and it clears them back on the
				 * main thread because that is the only thread the attributes are touched from.
				 * A failed write leaves everything marked, so the next pass carries it — which is
				 * the behaviour the unconditional save had by accident and this has to keep on
				 * purpose, since the periodic path has no other retry.
				 *
				 * Filtered rows are why this looks at the outcome and not the boolean alone. A
				 * best-effort report is content with a batch the service declined to attempt in
				 * part — an unresolvable character or template, or a duplicate key it dropped —
				 * and those rows never reached the database. Clearing them would retire a value
				 * that was never stored. Superseded rows are the opposite and safe to clear: they
				 * were refused because the database already holds something newer.
				 *
				 * A row the ownership gate refused is one of the filtered ones (BulkWriteResult
				 * .Unowned), and a batch it refused whole is a failure, so neither clears anything. */
				if (BulkWriteReporting.MayClearDirtyMarks(result))
				{
					TryEnqueueMainThread(() => MarkAttributesPersisted(attributes));
				}

				return written || !result.IsTransient;
			}
			catch (Exception ex)
			{
				await Log.Error("CharacterSystem", $"SaveAttributesAsync failed: {ex}");
				return false;
			}
		}

		/// <summary>
		/// Clears the dirty mark on every attribute a completed write covered.
		/// </summary>
		/// <remarks>
		/// Runs on the main thread, which is the only thread attributes are touched from. Each
		/// attribute decides for itself whether the confirmation still applies — see
		/// <see cref="CharacterAttribute.MarkPersisted"/> — so a value that moved while the write
		/// was in flight stays dirty and a stale confirmation clears nothing. A character that has
		/// since left the server is simply not resolvable and is skipped; its attributes are going
		/// away with it.
		/// </remarks>
		/// <param name="attributes">The snapshot that was written.</param>
		private void MarkAttributesPersisted(List<CharacterAttributeData> attributes)
		{
			if (attributes == null || attributes.Count == 0)
			{
				return;
			}

			for (int i = 0; i < attributes.Count; ++i)
			{
				CharacterAttributeData saved = attributes[i];

				if (!TryGetResidentCharacter(saved.CharacterID, out IPlayerCharacter character) ||
					!character.TryGet(out ICharacterAttributeController attrController))
				{
					continue;
				}

				if (attrController.Attributes.TryGetValue(saved.TemplateID, out CharacterAttribute attribute))
				{
					attribute.MarkPersisted(saved.Version);
				}
				else if (attrController.ResourceAttributes.TryGetValue(saved.TemplateID, out CharacterResourceAttribute resourceAttribute))
				{
					resourceAttribute.MarkPersisted(saved.Version);
				}
			}
		}

		/// <summary>
		/// Resolves a character the server is still holding, connected or lingering.
		/// </summary>
		/// <remarks>
		/// A combat-logout body is not in <c>CharactersByID</c> but is still resident and still
		/// being written by the periodic save — see <see cref="AppendLingeringCharacterSnapshots"/>.
		/// Looking only at the connected map would leave every lingering body's attributes
		/// permanently marked, so they would be rewritten on every pass for as long as the body
		/// stood there.
		/// </remarks>
		/// <param name="characterID">The character to resolve.</param>
		/// <param name="character">Out: the resident character, or null.</param>
		/// <returns>True if the character is still resident.</returns>
		private bool TryGetResidentCharacter(long characterID, out IPlayerCharacter character)
		{
			character = null;

			if (Server?.DataContainerRegistry != null &&
				Server.DataContainerRegistry.TryGet(out ICharacterMappingData<NetworkConnection> data) &&
				data.CharactersByID.TryGetValue(characterID, out character) &&
				character != null)
			{
				return true;
			}

			character = TryGetLingeringCharacter(characterID);
			return character != null;
		}

		/// <summary>
		/// Persists a snapshot of crafted ability state asynchronously.
		/// </summary>
		/// <param name="abilities">The rows.</param>
		/// <param name="claims">The claims the rows were captured under. See <see cref="AppendSubEntities"/>.</param>
		/// <returns>False only when the write failed in a way worth another attempt.</returns>
		private async Task<bool> SaveAbilitiesAsync(List<CharacterAbilityData> abilities, IReadOnlyCollection<CharacterSessionLeaseData> claims)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterAbilityService>(out var abilityService))
				{
					return true;
				}

				DatabaseResult<BulkWriteResult> result = await abilityService.PersistOwnedAsync(abilities, claims);
				bool written = await BulkWriteReporting.ReportAsync("CharacterSystem", "Ability save", result);

				/* Only a write that landed clears the marks, and only on the main thread.
				 *
				 * Filtered rows are the reason this looks at the outcome rather than the boolean
				 * alone. A best-effort report is happy with a batch the service declined to
				 * attempt in part -- an unresolvable character or template, or a duplicate key it
				 * dropped -- and those rows were never offered to the database at all. Clearing
				 * them would retire a change that was never stored, and the periodic save has no
				 * retry of its own to notice. Superseded rows are the opposite and safe to clear:
				 * the database refused them because it already holds something newer. */
				if (BulkWriteReporting.MayClearDirtyMarks(result))
				{
					TryEnqueueMainThread(() => MarkAbilitiesPersisted(abilities));
				}

				return written || !result.IsTransient;
			}
			catch (Exception ex)
			{
				await Log.Error("CharacterSystem", $"SaveAbilitiesAsync failed: {ex}");
				return false;
			}
		}

		/// <summary>
		/// Clears the dirty mark on every ability a completed write covered.
		/// </summary>
		/// <remarks>
		/// Runs on the main thread. Each ability is cleared only if its version still matches the
		/// one written, so an ability changed while the save was in flight stays dirty. A character
		/// that has since logged out is gone from the map and skipped.
		/// </remarks>
		/// <param name="abilities">The snapshot that was written.</param>
		private void MarkAbilitiesPersisted(List<CharacterAbilityData> abilities)
		{
			if (abilities == null)
			{
				return;
			}

			for (int i = 0; i < abilities.Count; ++i)
			{
				CharacterAbilityData saved = abilities[i];

				/* Lingering bodies too, as every other mark does: one is saved every pass but is not
				 * in CharactersByID, so its abilities stayed marked and were rewritten each pass. */
				if (!TryGetResidentCharacter(saved.CharacterID, out IPlayerCharacter character) ||
					!character.TryGet(out IAbilityController abilityController))
				{
					continue;
				}

				if (abilityController.KnownAbilities.TryGetValue(saved.ID, out Ability ability) &&
					ability != null)
				{
					ability.MarkPersisted(saved.Version);
				}
			}
		}

		/// <summary>
		/// Appends a snapshot of every achievement changed since the database last confirmed it.
		/// Main-thread only. Each appended DTO has its Version bumped on the runtime Achievement.
		/// </summary>
		/// <remarks>
		/// Achievements were fetched on login and never written back — progress reset to the
		/// loaded values every session. This is the missing save leg, shaped like the ability
		/// one: <see cref="Achievement.MarkChanged"/> advances the version on mutation, the
		/// snapshot advances it again, and <see cref="MarkAchievementsPersisted"/> clears the
		/// mark only when the version still matches.
		/// </remarks>
		private void AppendAchievementData(IPlayerCharacter character, List<CharacterAchievementData> achievements)
		{
			if (!character.TryGet(out IAchievementController achievementController) ||
				achievementController.Achievements.Count == 0)
			{
				return;
			}

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

		/// <summary>
		/// Persists a snapshot of achievement progress asynchronously.
		/// </summary>
		/// <param name="achievements">The rows.</param>
		/// <param name="claims">The claims the rows were captured under. See <see cref="AppendSubEntities"/>.</param>
		/// <returns>False only when the write failed in a way worth another attempt.</returns>
		private async Task<bool> SaveAchievementsAsync(List<CharacterAchievementData> achievements, IReadOnlyCollection<CharacterSessionLeaseData> claims)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterAchievementService>(out var achievementService))
				{
					return true;
				}

				DatabaseResult<BulkWriteResult> result = await achievementService.PersistOwnedAsync(achievements, claims);
				bool written = await BulkWriteReporting.ReportAsync("CharacterSystem", "Achievement save", result);

				// Same rule as abilities: filtered rows were never offered to the database.
				if (BulkWriteReporting.MayClearDirtyMarks(result))
				{
					TryEnqueueMainThread(() => MarkAchievementsPersisted(achievements));
				}

				return written || !result.IsTransient;
			}
			catch (Exception ex)
			{
				await Log.Error("CharacterSystem", $"SaveAchievementsAsync failed: {ex}");
				return false;
			}
		}

		/// <summary>
		/// Clears the dirty mark on every achievement a completed write covered. Main thread.
		/// </summary>
		private void MarkAchievementsPersisted(List<CharacterAchievementData> achievements)
		{
			if (achievements == null)
			{
				return;
			}

			for (int i = 0; i < achievements.Count; ++i)
			{
				CharacterAchievementData saved = achievements[i];

				if (!TryGetResidentCharacter(saved.CharacterID, out IPlayerCharacter character) ||
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

		/// <summary>Scratch list for collecting dirty waypoint pages on the main thread.</summary>
		private readonly List<WaypointPageSnapshot> waypointPageScratch = new List<WaypointPageSnapshot>();

		/// <summary>
		/// Appends every discovered-waypoint page the database has not yet confirmed.
		/// Main-thread only.
		/// </summary>
		/// <remarks>
		/// The unlock path writes each page the moment it changes; this is the retry for a write
		/// that was lost, and the flush before a session release. A page is dirty until the
		/// merge is confirmed, so a lost write simply shows up here on the next pass.
		/// </remarks>
		private void AppendWaypointData(IPlayerCharacter character, List<CharacterWaypointData> waypoints)
		{
			WaypointPersistence.AppendDirtyPages(character, waypoints, waypointPageScratch);
		}

		/// <summary>
		/// Merges discovered-waypoint pages into the database asynchronously.
		/// </summary>
		/// <returns>
		/// False when the merge did not land. Any failure counts: the merge is an OR, so repeating
		/// one that did land loses nothing.
		/// </returns>
		private async Task<bool> SaveWaypointsAsync(List<CharacterWaypointData> waypoints)
		{
			if (Server?.Database?.ServiceRegistry == null ||
				!Server.Database.ServiceRegistry.TryGet<ICharacterWaypointService>(out var waypointService))
			{
				return true;
			}

			if (await WaypointPersistence.MergeAsync(waypointService, waypoints, "CharacterSystem"))
			{
				TryEnqueueMainThread(() => WaypointPersistence.MarkPersisted(waypoints, ResolveResidentCharacterForWaypoints));
				return true;
			}
			return false;
		}

		/// <summary>Resolves a connected or lingering character for waypoint write confirmation. Main thread.</summary>
		private ICharacter ResolveResidentCharacterForWaypoints(long characterID)
		{
			return TryGetResidentCharacter(characterID, out IPlayerCharacter character) ? character : null;
		}

		/// <summary>
		/// Appends every faction standing changed since the database last confirmed it.
		/// Main-thread only. Each appended DTO has its Version bumped on the runtime Faction.
		/// </summary>
		/// <remarks>
		/// Factions had no save leg at all before 2026-09-07: the only writer was character
		/// creation, so every kill credit and quest reward vanished on logout. Same shape as
		/// achievements — <see cref="Faction.MarkChanged"/> on mutation, a second bump here, and
		/// <see cref="MarkFactionsPersisted"/> clearing only a still-matching version.
		/// </remarks>
		private void AppendFactionData(IPlayerCharacter character, List<CharacterFactionData> factions)
		{
			if (!character.TryGet(out IFactionController factionController) ||
				factionController.Factions.Count == 0)
			{
				return;
			}

			foreach (var kvp in factionController.Factions)
			{
				Faction faction = kvp.Value;
				if (faction == null || !faction.PersistenceDirty)
				{
					continue;
				}

				faction.Version++;
				factions.Add(new CharacterFactionData(
					id: 0,
					version: faction.Version,
					characterID: character.ID,
					templateID: kvp.Key,
					value: faction.Value));
			}
		}

		/// <summary>
		/// Persists a snapshot of faction standings asynchronously.
		/// </summary>
		/// <param name="factions">The rows.</param>
		/// <param name="claims">The claims the rows were captured under. See <see cref="AppendSubEntities"/>.</param>
		/// <returns>False only when the write failed in a way worth another attempt.</returns>
		private async Task<bool> SaveFactionsAsync(List<CharacterFactionData> factions, IReadOnlyCollection<CharacterSessionLeaseData> claims)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterFactionService>(out var factionService))
				{
					return true;
				}

				DatabaseResult<BulkWriteResult> result = await factionService.PersistOwnedAsync(factions, claims);
				bool written = await BulkWriteReporting.ReportAsync("CharacterSystem", "Faction save", result);

				if (BulkWriteReporting.MayClearDirtyMarks(result))
				{
					TryEnqueueMainThread(() => MarkFactionsPersisted(factions));
				}

				return written || !result.IsTransient;
			}
			catch (Exception ex)
			{
				await Log.Error("CharacterSystem", $"SaveFactionsAsync failed: {ex}");
				return false;
			}
		}

		/// <summary>
		/// Captures a character's ability knowledge, when any of it is new since the last write.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Knowledge was the one sub-entity this save never covered. It reached the database only
		/// through the paths that grant it — a merchant purchase, an achievement reward — each
		/// writing its own row as it went. Anything that learned by another route wrote nothing:
		/// a lore object's grant and a scroll's grant lasted exactly as long as the session.
		/// </para>
		/// <para>
		/// Written whole rather than per row because the controller keeps knowledge as two id sets
		/// with nothing to hang a per-entry version on. That is affordable because it is gated on
		/// <see cref="IAbilityKnowledgeController.KnowledgeDirty"/>, which is set only when
		/// something genuinely new is learned — a character that learns nothing writes nothing, and
		/// the rows are versioned upserts, so a re-write of a row the database already has is
		/// filtered rather than applied.
		/// </para>
		/// </remarks>
		private void AppendKnownAbilityData(IPlayerCharacter character, List<CharacterKnownAbilityData> knownAbilities, Dictionary<long, long> knowledgeVersions)
		{
			if (!character.TryGet(out IAbilityController abilityController) ||
				!abilityController.KnowledgeDirty)
			{
				return;
			}

			// What this save covers: see MarkKnowledgePersisted.
			knowledgeVersions[character.ID] = abilityController.KnowledgeVersion;

			/* Base abilities and events share one table, keyed by template id — the same table the
			 * merchant and achievement paths write to, one row at a time. */
			foreach (int templateID in abilityController.KnownBaseAbilities)
			{
				knownAbilities.Add(new CharacterKnownAbilityData(
					id: 0,
					version: 1,
					characterID: character.ID,
					templateID: templateID));
			}
			foreach (int templateID in abilityController.KnownAbilityEvents)
			{
				knownAbilities.Add(new CharacterKnownAbilityData(
					id: 0,
					version: 1,
					characterID: character.ID,
					templateID: templateID));
			}
		}

		/// <summary>
		/// Persists a snapshot of ability knowledge asynchronously.
		/// </summary>
		/// <param name="knownAbilities">The rows.</param>
		/// <param name="knowledgeVersions">Each character's knowledge version as captured.</param>
		/// <param name="claims">The claims the rows were captured under. See <see cref="AppendSubEntities"/>.</param>
		/// <returns>False only when the write failed in a way worth another attempt.</returns>
		private async Task<bool> SaveKnownAbilitiesAsync(List<CharacterKnownAbilityData> knownAbilities, Dictionary<long, long> knowledgeVersions, IReadOnlyCollection<CharacterSessionLeaseData> claims)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterKnownAbilityService>(out var knownAbilityService))
				{
					return true;
				}

				DatabaseResult<BulkWriteResult> result = await knownAbilityService.PersistOwnedAsync(knownAbilities, claims);
				bool written = await BulkWriteReporting.ReportAsync("CharacterSystem", "Known ability save", result);

				/* Gated on Filtered == 0, like the faction and attribute marks: a row the service
				 * declined to attempt was never stored, so the mark must stay and the next pass
				 * writes it again. Superseded rows are the benign case and pass. */
				if (BulkWriteReporting.MayClearDirtyMarks(result))
				{
					TryEnqueueMainThread(() => MarkKnowledgePersisted(knowledgeVersions));
				}

				return written || !result.IsTransient;
			}
			catch (Exception ex)
			{
				await Log.Error("CharacterSystem", $"SaveKnownAbilitiesAsync failed: {ex}");
				return false;
			}
		}

		/// <summary>
		/// Clears the knowledge dirty mark on every character a completed write covered. Main thread.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Called only for a write with no FILTERED rows (see <see cref="SaveKnownAbilitiesAsync"/>).
		/// Every knowledge row is written at version 1, so a row the database already holds comes
		/// back SUPERSEDED, not filtered, and correctly clears the mark.
		/// </para>
		/// <para>
		/// The gate used to be left off because the service filtered every row whose template id
		/// was not positive — and template ids are signed deterministic hashes, so that was roughly
		/// half of all templates, which were never stored at all. The service now refuses only 0
		/// (issue #267), so a filtered row is a real refusal and keeps the character dirty.
		/// </para>
		/// <para>
		/// Only for a character whose knowledge has not moved since the save captured it. The mark
		/// used to be cleared unconditionally, so an ability learned while the write was in flight
		/// was marked written by a save that never carried it — and, with nothing else learned,
		/// never saved at all (issue #267). This is the faction marks' version check, over the
		/// whole set because knowledge keeps no per-entry version.
		/// </para>
		/// </remarks>
		private void MarkKnowledgePersisted(Dictionary<long, long> knowledgeVersions)
		{
			if (knowledgeVersions == null)
			{
				return;
			}

			foreach (KeyValuePair<long, long> captured in knowledgeVersions)
			{
				if (TryGetResidentCharacter(captured.Key, out IPlayerCharacter character) &&
					character.TryGet(out IAbilityController abilityController) &&
					abilityController.KnowledgeVersion == captured.Value)
				{
					abilityController.KnowledgeDirty = false;
				}
			}
		}

		/// <summary>
		/// Clears the dirty mark on every faction a completed write covered. Main thread.
		/// </summary>
		private void MarkFactionsPersisted(List<CharacterFactionData> factions)
		{
			if (factions == null)
			{
				return;
			}

			for (int i = 0; i < factions.Count; ++i)
			{
				CharacterFactionData saved = factions[i];

				if (!TryGetResidentCharacter(saved.CharacterID, out IPlayerCharacter character) ||
					!character.TryGet(out IFactionController factionController))
				{
					continue;
				}

				if (factionController.Factions.TryGetValue(saved.TemplateID, out Faction faction) && faction != null)
				{
					faction.MarkPersisted(saved.Version);
				}
			}
		}

		/// <summary>
		/// Appends the character's archetype row when it has changed since the database last
		/// confirmed it. Main-thread only.
		/// </summary>
		private void AppendArchetypeData(IPlayerCharacter character, List<CharacterArchetypeData> archetypes)
		{
			if (!character.TryGet(out IArchetypeController archetypeController) ||
				!archetypeController.PersistenceDirty ||
				archetypeController.Template == null)
			{
				return;
			}

			archetypeController.Version++;
			archetypes.Add(new CharacterArchetypeData(
				id: 0,
				version: archetypeController.Version,
				characterID: character.ID,
				templateID: archetypeController.Template.ID));
		}

		/// <summary>
		/// Persists archetype rows asynchronously.
		/// </summary>
		/// <param name="archetypes">The rows.</param>
		/// <param name="claims">The claims the rows were captured under. See <see cref="AppendSubEntities"/>.</param>
		/// <returns>False only when the write failed in a way worth another attempt.</returns>
		private async Task<bool> SaveArchetypesAsync(List<CharacterArchetypeData> archetypes, IReadOnlyCollection<CharacterSessionLeaseData> claims)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterArchetypeService>(out var archetypeService))
				{
					return true;
				}

				DatabaseResult<BulkWriteResult> result = await archetypeService.PersistOwnedAsync(archetypes, claims);
				bool written = await BulkWriteReporting.ReportAsync("CharacterSystem", "Archetype save", result);

				if (BulkWriteReporting.MayClearDirtyMarks(result))
				{
					TryEnqueueMainThread(() => MarkArchetypesPersisted(archetypes));
				}

				return written || !result.IsTransient;
			}
			catch (Exception ex)
			{
				await Log.Error("CharacterSystem", $"SaveArchetypesAsync failed: {ex}");
				return false;
			}
		}

		/// <summary>
		/// Clears the dirty mark on every archetype a completed write covered. Main thread.
		/// </summary>
		private void MarkArchetypesPersisted(List<CharacterArchetypeData> archetypes)
		{
			if (archetypes == null)
			{
				return;
			}

			for (int i = 0; i < archetypes.Count; ++i)
			{
				CharacterArchetypeData saved = archetypes[i];
				if (TryGetResidentCharacter(saved.CharacterID, out IPlayerCharacter character) &&
					character.TryGet(out IArchetypeController archetypeController))
				{
					archetypeController.MarkPersisted(saved.Version);
				}
			}
		}

		#endregion
	}
}