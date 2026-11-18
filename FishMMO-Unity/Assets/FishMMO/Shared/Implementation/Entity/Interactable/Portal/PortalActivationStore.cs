using System;
using System.Collections.Generic;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// The portal activations a process knows: each character's own (bit sets per scene, the
	/// waypoint shape) and the world's (one <see cref="PortalWorldState"/> per scene and index),
	/// with what the database has and has not confirmed.
	/// </summary>
	/// <remarks>
	/// <para><b>Plain data, no I/O.</b> The scene server's interactable system owns the live store,
	/// fills it from the database (<see cref="RestoreCharacterPage"/>, <see cref="RestoreWorldScene"/>)
	/// and drains what is dirty into it; <see cref="PortalGate"/> reads and writes it through
	/// <see cref="Current"/>. Until a server installs one, <see cref="Current"/> is a store that
	/// treats every record as loaded and keeps them in memory only — so a portal behaves the same
	/// in a harness or a sim, it just forgets on restart.</para>
	/// <para><b>Loaded is tracked separately from empty.</b> A character with no rows and a
	/// character not read yet both have no bits; only the first may be answered "closed" (see
	/// <see cref="PortalActivationRules"/>). Likewise per scene for world rows.</para>
	/// <para><b>Persistence without versions.</b> Character bits reuse <see cref="WaypointUnlockMask"/>:
	/// set-only bits whose database merge is an OR, so dirty is <c>activated &amp; ~persisted</c> and a
	/// bit set while a write is in flight stays dirty on its own. World openings are tracked by
	/// <see cref="PortalWorldState.Covers"/>: a confirmation clears the mark only if what was written
	/// covers what is held now. A released character whose bits are still unconfirmed keeps its
	/// entry, unloaded, until a write confirms them; a reload ORs the database rows back in.</para>
	/// <para><b>Record keys.</b> A per-character record is keyed by a string that is the scene name
	/// for portals, and the scene name plus <see cref="PointOfInterestDiscovery.KeySuffix"/> for the
	/// sites a character has discovered: the same bit pages, persisted by the same rows.</para>
	/// <para>Main thread only.</para>
	/// </remarks>
	public sealed class PortalActivationStore
	{
		private static PortalActivationStore current;

		/// <summary>
		/// The store <see cref="PortalGate"/> uses. A server installs its own on start and clears
		/// it on stop; otherwise a process-local, memory-only store.
		/// </summary>
		public static PortalActivationStore Current
		{
			get => current ??= new PortalActivationStore(assumeLoaded: true);
			set => current = value;
		}

		private sealed class CharacterRecord
		{
			public readonly Dictionary<string, WaypointUnlockMask> Scenes = new Dictionary<string, WaypointUnlockMask>(StringComparer.Ordinal);
			public bool Loaded;
		}

		private readonly Dictionary<long, CharacterRecord> characters = new Dictionary<long, CharacterRecord>();
		private readonly Dictionary<string, Dictionary<int, PortalWorldState>> world = new Dictionary<string, Dictionary<int, PortalWorldState>>(StringComparer.Ordinal);
		private readonly HashSet<string> loadedScenes = new HashSet<string>(StringComparer.Ordinal);
		private readonly Dictionary<(string Scene, int Index), PortalWorldState> unconfirmedWorld = new Dictionary<(string Scene, int Index), PortalWorldState>();

		/// <summary>When true every record counts as loaded: the memory-only store.</summary>
		public readonly bool AssumeLoaded;

		/// <summary>Unix milliseconds now. Replaceable for tests.</summary>
		public Func<long> Clock = () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

		/// <summary>A character activated a per-character portal (newly). Parameters: character ID, scene, index.</summary>
		public event Action<long, string, int> CharacterActivated;

		/// <summary>A world portal's opening grew. Parameters: scene, index, the merged state.</summary>
		public event Action<string, int, PortalWorldState> WorldActivated;

		/// <summary>A decision needed a record that is not loaded. Parameters: character ID (0 for none), scene (null for none).</summary>
		public event Action<long, string> LoadRequested;

		public PortalActivationStore(bool assumeLoaded)
		{
			AssumeLoaded = assumeLoaded;
		}

		/// <summary>The store's notion of now.</summary>
		public long NowUnixMs => Clock != null ? Clock() : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

		#region Character

		/// <summary>Whether a character's activations have been read.</summary>
		public bool IsCharacterLoaded(long characterID)
		{
			return AssumeLoaded || (characters.TryGetValue(characterID, out CharacterRecord record) && record.Loaded);
		}

		/// <summary>Whether a character activated a portal; null when its record is not loaded.</summary>
		public bool? IsCharacterActive(long characterID, string sceneName, int portalIndex)
		{
			if (!IsCharacterLoaded(characterID))
			{
				return null;
			}
			return characters.TryGetValue(characterID, out CharacterRecord record) &&
				!string.IsNullOrEmpty(sceneName) &&
				record.Scenes.TryGetValue(sceneName, out WaypointUnlockMask mask) &&
				mask.Contains(portalIndex);
		}

		/// <summary>
		/// Records a per-character activation and raises <see cref="CharacterActivated"/> when new.
		/// </summary>
		/// <returns>True when newly activated.</returns>
		public bool ActivateForCharacter(long characterID, string sceneName, int portalIndex)
		{
			if (characterID <= 0 || string.IsNullOrEmpty(sceneName) || !WaypointUnlockMask.IsValidIndex(portalIndex))
			{
				return false;
			}
			if (!GetOrCreateMask(characterID, sceneName).Add(portalIndex))
			{
				return false;
			}
			CharacterActivated?.Invoke(characterID, sceneName, portalIndex);
			return true;
		}

		/// <summary>Installs a persisted page: activated and confirmed. The load path.</summary>
		public void RestoreCharacterPage(long characterID, string sceneName, int page, ulong mask)
		{
			if (characterID <= 0 || string.IsNullOrEmpty(sceneName) || mask == 0 || page < 0 || page >= WaypointUnlockMask.MaxPages)
			{
				return;
			}
			GetOrCreateMask(characterID, sceneName).Restore(page, mask);
		}

		/// <summary>Marks a character's record as read, so decisions may use it.</summary>
		public void MarkCharacterLoaded(long characterID)
		{
			if (characterID <= 0)
			{
				return;
			}
			GetOrCreateRecord(characterID).Loaded = true;
		}

		/// <summary>
		/// Forgets a character that left. Its record is kept, unloaded, while any bit is unconfirmed,
		/// so the next flush can still write it.
		/// </summary>
		public void ReleaseCharacter(long characterID)
		{
			if (!characters.TryGetValue(characterID, out CharacterRecord record))
			{
				return;
			}
			record.Loaded = false;
			if (!IsDirty(record))
			{
				characters.Remove(characterID);
			}
		}

		/// <summary>Appends a character's pages with unconfirmed bits.</summary>
		public void CollectDirtyPages(long characterID, List<WaypointPageSnapshot> results)
		{
			if (results == null || !characters.TryGetValue(characterID, out CharacterRecord record))
			{
				return;
			}
			foreach (KeyValuePair<string, WaypointUnlockMask> scene in record.Scenes)
			{
				scene.Value.CollectDirtyPages(scene.Key, results);
			}
		}

		/// <summary>Appends every character with unconfirmed bits, loaded or released.</summary>
		public void CollectDirtyCharacters(List<long> results)
		{
			if (results == null)
			{
				return;
			}
			foreach (KeyValuePair<long, CharacterRecord> entry in characters)
			{
				if (IsDirty(entry.Value))
				{
					results.Add(entry.Key);
				}
			}
		}

		/// <summary>
		/// Records that the database holds these bits. A released character's record goes once it is clean.
		/// </summary>
		public void MarkCharacterPersisted(long characterID, string sceneName, int page, ulong writtenMask)
		{
			if (string.IsNullOrEmpty(sceneName) || !characters.TryGetValue(characterID, out CharacterRecord record))
			{
				return;
			}
			if (record.Scenes.TryGetValue(sceneName, out WaypointUnlockMask mask))
			{
				mask.MarkPersisted(page, writtenMask);
			}
			if (!record.Loaded && !IsDirty(record))
			{
				characters.Remove(characterID);
			}
		}

		/// <summary>Number of character records held, loaded or released. Diagnostics and tests.</summary>
		public int CharacterRecordCount => characters.Count;

		#endregion

		#region World

		/// <summary>Whether a scene's world rows have been read.</summary>
		public bool IsSceneLoaded(string sceneName)
		{
			return AssumeLoaded || (!string.IsNullOrEmpty(sceneName) && loadedScenes.Contains(sceneName));
		}

		/// <summary>A portal's world state; default when none, null when the scene is not loaded.</summary>
		public PortalWorldState? WorldState(string sceneName, int portalIndex)
		{
			if (!IsSceneLoaded(sceneName))
			{
				return null;
			}
			if (sceneName != null && world.TryGetValue(sceneName, out Dictionary<int, PortalWorldState> scene) &&
				scene.TryGetValue(portalIndex, out PortalWorldState state))
			{
				return state;
			}
			return default(PortalWorldState);
		}

		/// <summary>
		/// Merges an opening into a portal's world state, marks it unconfirmed, and raises
		/// <see cref="WorldActivated"/> when the state grew.
		/// </summary>
		/// <returns>True when the state grew.</returns>
		public bool ActivateForWorld(string sceneName, int portalIndex, PortalWorldState opening)
		{
			if (string.IsNullOrEmpty(sceneName) || !WaypointUnlockMask.IsValidIndex(portalIndex))
			{
				return false;
			}
			PortalWorldState before = WorldState(sceneName, portalIndex) ?? default;
			PortalWorldState after = PortalWorldState.Merge(before, opening);
			if (before.Covers(after))
			{
				return false;
			}
			SetWorld(sceneName, portalIndex, after);
			var key = (sceneName, portalIndex);
			unconfirmedWorld.TryGetValue(key, out PortalWorldState pending);
			unconfirmedWorld[key] = PortalWorldState.Merge(pending, after);
			WorldActivated?.Invoke(sceneName, portalIndex, after);
			return true;
		}

		/// <summary>
		/// Installs a scene's rows (merged with anything held) and marks the scene loaded. The
		/// load path, and the periodic refresh that picks up other servers' openings.
		/// </summary>
		/// <returns>The indices whose state grew, so the caller can tell players.</returns>
		public List<int> RestoreWorldScene(string sceneName, IReadOnlyList<(int Index, PortalWorldState State)> rows)
		{
			var grew = new List<int>();
			if (string.IsNullOrEmpty(sceneName))
			{
				return grew;
			}
			if (rows != null)
			{
				for (int i = 0; i < rows.Count; ++i)
				{
					(int index, PortalWorldState state) = rows[i];
					if (!WaypointUnlockMask.IsValidIndex(index))
					{
						continue;
					}
					PortalWorldState before = world.TryGetValue(sceneName, out Dictionary<int, PortalWorldState> scene) &&
						scene.TryGetValue(index, out PortalWorldState held) ? held : default;
					PortalWorldState after = PortalWorldState.Merge(before, state);
					if (!before.Covers(after))
					{
						SetWorld(sceneName, index, after);
						grew.Add(index);
					}
				}
			}
			loadedScenes.Add(sceneName);
			return grew;
		}

		/// <summary>Forgets a scene's rows (its last copy unloaded). Unconfirmed openings are kept for the flush.</summary>
		public void ReleaseScene(string sceneName)
		{
			if (string.IsNullOrEmpty(sceneName))
			{
				return;
			}
			loadedScenes.Remove(sceneName);
			world.Remove(sceneName);
		}

		/// <summary>Appends every world opening the database has not confirmed.</summary>
		public void CollectDirtyWorld(List<(string Scene, int Index, PortalWorldState State)> results)
		{
			if (results == null)
			{
				return;
			}
			foreach (KeyValuePair<(string Scene, int Index), PortalWorldState> entry in unconfirmedWorld)
			{
				results.Add((entry.Key.Scene, entry.Key.Index, entry.Value));
			}
		}

		/// <summary>Records that the database holds an opening; the mark clears only if the write covers what is pending.</summary>
		public void MarkWorldPersisted(string sceneName, int portalIndex, PortalWorldState written)
		{
			var key = (sceneName, portalIndex);
			if (sceneName != null && unconfirmedWorld.TryGetValue(key, out PortalWorldState pending) && written.Covers(pending))
			{
				unconfirmedWorld.Remove(key);
			}
		}

		/// <summary>Number of world openings awaiting confirmation. Diagnostics and tests.</summary>
		public int UnconfirmedWorldCount => unconfirmedWorld.Count;

		#endregion

		/// <summary>Asks the owner to load a record a decision needed. Raised by <see cref="PortalGate"/>.</summary>
		public void RequestLoad(long characterID, string sceneName)
		{
			LoadRequested?.Invoke(characterID, sceneName);
		}

		private void SetWorld(string sceneName, int portalIndex, PortalWorldState state)
		{
			if (!world.TryGetValue(sceneName, out Dictionary<int, PortalWorldState> scene))
			{
				scene = new Dictionary<int, PortalWorldState>();
				world[sceneName] = scene;
			}
			scene[portalIndex] = state;
		}

		private CharacterRecord GetOrCreateRecord(long characterID)
		{
			if (!characters.TryGetValue(characterID, out CharacterRecord record))
			{
				record = new CharacterRecord();
				characters[characterID] = record;
			}
			return record;
		}

		private WaypointUnlockMask GetOrCreateMask(long characterID, string sceneName)
		{
			CharacterRecord record = GetOrCreateRecord(characterID);
			if (!record.Scenes.TryGetValue(sceneName, out WaypointUnlockMask mask))
			{
				mask = new WaypointUnlockMask();
				record.Scenes[sceneName] = mask;
			}
			return mask;
		}

		private static bool IsDirty(CharacterRecord record)
		{
			foreach (WaypointUnlockMask mask in record.Scenes.Values)
			{
				if (mask.IsDirty)
				{
					return true;
				}
			}
			return false;
		}
	}
}
