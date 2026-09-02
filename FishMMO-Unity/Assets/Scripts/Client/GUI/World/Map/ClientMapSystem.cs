using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishMMO.Logging;

namespace FishMMO.Client
{
	/// <summary>
	/// The state both map panels share: which scene is being mapped, how much of it the character
	/// has explored, what they have pinned to it, and the overhead camera that photographs it.
	/// </summary>
	/// <remarks>
	/// <para><b>Why the panels do not each own this.</b> The minimap and the world map draw the
	/// same world, and almost everything they need is expensive to have twice: a second fog grid
	/// is a second quarter-megabyte that has to be revealed in step with the first or the two maps
	/// disagree about where the player has been; a second overhead camera is a second full scene
	/// render every frame; a second note list is a second writer to the same file. Both panels are
	/// views onto this.</para>
	///
	/// <para><b>Lifetime.</b> Driven entirely from <see cref="SetCharacter"/> and
	/// <see cref="Tick"/>, which the minimap calls — it is the panel that is always present. The
	/// world map reads the same state and works whether or not the minimap is open, but a build
	/// that removes the minimap panel entirely has to drive this from somewhere else.</para>
	/// </remarks>
	public static class ClientMapSystem
	{
		/// <summary>How often the fog is revealed around the character, in seconds.</summary>
		/// <remarks>
		/// Four times a second. The reveal writes a disc of cells whose radius is tens of metres,
		/// which is thousands of byte comparisons; at sixty hertz that is real work to produce a
		/// result no player can perceive, because a character cannot cross a four-metre cell in a
		/// sixtieth of a second.
		/// </remarks>
		private const double RevealInterval = 0.25;

		/// <summary>How often destroyed markers are swept out of the registry, in seconds.</summary>
		private const double PruneInterval = 15.0;

		/// <summary>How long the explored map must be unchanged before it is written, in seconds.</summary>
		/// <remarks>
		/// The same debounce reasoning as <see cref="ClientSettings"/>: walking changes the grid
		/// continuously, and a write is a compress plus a file replace. Coalescing onto a quiet
		/// period means a player crossing a zone writes a handful of times rather than hundreds.
		/// </remarks>
		private const double SaveDebounce = 6.0;

		/// <summary>The character whose map is being shown.</summary>
		public static IPlayerCharacter Character { get; private set; }

		/// <summary>The scene details cache, supplied by whichever panel loads first.</summary>
		public static WorldSceneDetailsCache DetailsCache { get; set; }

		/// <summary>The current scene's map definition, or null when it has none.</summary>
		public static WorldMapDefinition Definition { get; private set; }

		/// <summary>The world rectangle the current scene's map covers.</summary>
		public static Rect MapRect { get; private set; }

		/// <summary>The character's explored map for the current scene, or null before one loads.</summary>
		public static FogOfWarMap Fog { get; private set; }

		/// <summary>The character's notes for the current scene.</summary>
		public static List<MapNote> Notes { get; } = new List<MapNote>();

		/// <summary>The rule deciding which markers may be drawn.</summary>
		public static MapMarkerFilter Filter { get; } = new MapMarkerFilter();

		/// <summary>The overhead camera feeding the minimap.</summary>
		public static MinimapCameraRenderer Renderer { get; } = new MinimapCameraRenderer();

		/// <summary>The baked overhead image for the current scene, or null when there is none.</summary>
		public static Texture2D MapImage { get; private set; }

		/// <summary>The scene the loaded state belongs to.</summary>
		public static string SceneName { get; private set; }

		/// <summary>The scene's player-facing name.</summary>
		public static string SceneDisplayName =>
			Definition != null ? Definition.ResolvedDisplayName : SceneName;

		/// <summary>
		/// Raised when the loaded scene changes, so panels can rebuild everything derived from it.
		/// </summary>
		public static event Action OnSceneMapChanged;

		/// <summary>Raised when a note is added, edited or removed.</summary>
		public static event Action OnNotesChanged;

		/// <summary>Handle for the addressable map image, held so it can be released.</summary>
		private static AsyncOperationHandle<Texture2D> mapImageHandle;

		/// <summary>Whether <see cref="mapImageHandle"/> refers to a live load.</summary>
		private static bool mapImageLoading;

		/// <summary>Time, on the unscaled clock, of the next fog reveal.</summary>
		private static double nextRevealTime;

		/// <summary>Time, on the unscaled clock, at which a pending save comes due. Zero for none.</summary>
		private static double saveDueTime;

		/// <summary>Time, on the unscaled clock, of the next sweep of destroyed markers.</summary>
		private static double nextPruneTime;

		/// <summary>The next note identifier for the current scene.</summary>
		private static long nextNoteID = 1;

		/// <summary>
		/// Whether the reason exploration is not advancing has already been reported for this scene.
		/// </summary>
		/// <remarks>
		/// Once per scene load, not once per attempt: the check runs four times a second, and a
		/// condition that holds at all holds for the whole session.
		/// </remarks>
		private static bool revealRefusalReported;

		/// <summary>
		/// Reports, once per scene, that exploration cannot advance and why.
		/// </summary>
		/// <param name="reason">What is preventing the reveal.</param>
		/// <remarks>
		/// A refused reveal is not the same as a reveal that changed nothing. Standing on ground
		/// already explored changes nothing every quarter second and is entirely normal, so
		/// <see cref="FogOfWarMap.Reveal"/> returning false is never reported. Only the two
		/// structural refusals are — no transform to reveal from, and a character outside the
		/// grid — because those never resolve on their own.
		/// </remarks>
		private static void WarnRevealRefused(string reason)
		{
			if (revealRefusalReported)
			{
				return;
			}
			revealRefusalReported = true;

			Log.Warning("ClientMapSystem",
				$"Fog of war cannot advance for scene '{SceneName}': {reason}. Explored territory will " +
				"stay exactly as it is, and the world map's explored percentage will not move.");
		}

		/// <summary>
		/// Points the map subsystem at a character, loading everything for the scene they are in.
		/// </summary>
		/// <param name="character">The local player character, or null to tear down.</param>
		public static void SetCharacter(IPlayerCharacter character)
		{
			if (ReferenceEquals(Character, character))
			{
				return;
			}

			/* Flushed before the switch, not after. The pending write belongs to the OUTGOING
			 * character, and the save path takes its identity from the current one — so a save
			 * that happened after the swap would write one character's exploration into another
			 * character's file. */
			Flush();

			Character = character;
			Filter.Reset();
			MapRelationshipTracker.Track(character);

			if (character == null)
			{
				UnloadScene();
				return;
			}

			LoadScene(character.SceneName);
		}

		/// <summary>
		/// Advances the fog, and writes it when it has settled.
		/// </summary>
		/// <remarks>
		/// Called from the minimap's per-frame update whether or not the minimap is visible. That
		/// is deliberate: exploration is something the character does by walking, not something
		/// they do by looking at a map, and tying it to the panel's visibility would mean a player
		/// who closed their minimap stopped revealing the world.
		/// </remarks>
		public static void Tick()
		{
			if (Character == null)
			{
				return;
			}

			// A scene change arrives as a field on the character rather than as an event.
			if (!string.Equals(Character.SceneName, SceneName, StringComparison.Ordinal))
			{
				Flush();
				LoadScene(Character.SceneName);
			}

			double now = Time.unscaledTimeAsDouble;

			if (Fog != null && now >= nextRevealTime)
			{
				nextRevealTime = now + RevealInterval;

				Transform transform = Character.Transform;
				if (transform == null)
				{
					/* Nothing to reveal from. Reported because the failure is otherwise completely
					 * silent: the fog grid still exists, so the map draws fog and the world map's
					 * readout keeps saying "Explored 0%" for the whole session while exploration
					 * never advances a single cell. That is indistinguishable from the readout
					 * itself being broken, which is exactly how it gets reported. */
					WarnRevealRefused("the character has no transform");
				}
				else if (!Fog.WorldRect.Contains(new Vector2(transform.position.x, transform.position.z)))
				{
					/* The character is not standing inside the rectangle the fog grid covers, so
					 * every reveal lands outside the grid and is dropped. Same silence as above,
					 * and the usual cause is a mismatch between the scene the fog was built for and
					 * the scene the character is physically in. */
					WarnRevealRefused($"the character is at {transform.position} but scene '{SceneName}' " +
						$"covers {Fog.WorldRect} — every reveal falls outside the map");
				}
				else if (Fog.Reveal(transform.position))
				{
					saveDueTime = now + SaveDebounce;
				}
			}

			if (saveDueTime > 0.0 && now >= saveDueTime)
			{
				saveDueTime = 0.0;
				SaveFog();
			}

			if (now >= nextPruneTime)
			{
				/* The registry is a static collection keyed by MonoBehaviours. Every marker
				 * unregisters itself when its object is disabled, so in the ordinary case this
				 * finds nothing — but a teardown that destroys objects without disabling them
				 * leaves entries behind, and each one keeps a whole GameObject hierarchy alive for
				 * the rest of the session. Swept on a slow timer because the null comparison is an
				 * engine call per entry and the leak accrues at the rate scenes unload. */
				nextPruneTime = now + PruneInterval;
				MapMarkerRegistry.Prune();
			}
		}

		/// <summary>
		/// Writes anything outstanding immediately.
		/// </summary>
		/// <remarks>
		/// Called on scene change, character change and application quit. The debounce is a
		/// throughput optimisation, not a durability policy — every path that ends the current
		/// map's life has to close it out, or the last stretch of walking before a zone change is
		/// silently lost.
		/// </remarks>
		public static void Flush()
		{
			if (saveDueTime > 0.0)
			{
				saveDueTime = 0.0;
			}
			SaveFog();
		}

		/// <summary>
		/// Loads the definition, explored map and notes for a scene.
		/// </summary>
		/// <param name="sceneName">The scene to load state for.</param>
		private static void LoadScene(string sceneName)
		{
			UnloadScene();

			SceneName = sceneName;
			if (string.IsNullOrEmpty(sceneName))
			{
				OnSceneMapChanged?.Invoke();
				return;
			}

			WorldSceneDetails details = null;
			if (DetailsCache != null && DetailsCache.Scenes != null)
			{
				DetailsCache.Scenes.TryGetValue(sceneName, out details);
			}

			Definition = details != null ? details.MapDefinition : null;
			MapRect = MapBoundsResolver.Resolve(Definition, details);

			if (MapRect.width <= 0.0f || MapRect.height <= 0.0f)
			{
				/* Without bounds there is nothing to normalise positions against, so the world map
				 * has no coordinate system and the fog grid has no extent. The minimap still works
				 * — it is a live camera and needs no bounds at all — which is why this is a
				 * warning rather than a failure. */
				Log.Warning("ClientMapSystem", $"Scene '{sceneName}' has no map bounds: it has neither a WorldMapDefinition with bounds nor any scene boundary to derive them from. The world map and fog of war are unavailable here; the minimap will still work.");
				OnSceneMapChanged?.Invoke();
				return;
			}

			LoadFog();
			LoadNotes();
			BeginLoadMapImage();

			OnSceneMapChanged?.Invoke();
		}

		/// <summary>
		/// Drops everything belonging to the previous scene.
		/// </summary>
		private static void UnloadScene()
		{
			ReleaseMapImage();

			Fog?.ReleaseTexture();
			Fog = null;
			Definition = null;
			MapRect = Rect.zero;
			SceneName = null;
			Notes.Clear();
			nextNoteID = 1;
			saveDueTime = 0.0;
			nextRevealTime = 0.0;
			revealRefusalReported = false;
		}

		/// <summary>
		/// Loads the character's explored map, or starts a fresh one.
		/// </summary>
		private static void LoadFog()
		{
			float chunkSize = Definition != null && Definition.FogChunkSize > 0.0f
				? Definition.FogChunkSize
				: FogOfWarDefaults.ChunkSize;

			Fog = FogOfWarStore.Load(Character.ID, SceneName, MapRect, chunkSize)
				?? new FogOfWarMap(MapRect, chunkSize);

			if (Definition != null && !Definition.FogOfWarEnabled)
			{
				/* A scene with fog turned off reads as fully explored rather than being special
				 * cased at every draw site. One branch here beats a null check in the marker
				 * filter, both map views and the discovery rule. */
				Fog.RevealAll();
				Fog.ClearDirty();
			}
		}

		#region Exploration API

		/* Everything below is for content that explores ground the character has not walked: a map
		 * consumable, a quest reward, a trigger volume at a vista, a discovery on reaching a
		 * landmark. Walking is handled by Tick and does not come through here.
		 *
		 * All of it applies to the scene the character is currently in. Exploring part of ANOTHER
		 * scene — a treasure map for a zone you have not visited — is not offered yet, and the
		 * reason is worth stating so it does not get bolted on wrongly: the obvious implementation
		 * loads that scene's file, edits it and saves it, which silently throws away the player's
		 * progress the moment the named scene happens to be the current one, because the live map
		 * in memory is a different object that gets written over the top a few seconds later. Add
		 * it by routing through the live Fog whenever the name matches SceneName, never by loading
		 * a second copy. */

		/// <summary>
		/// Explores every chunk within a radius of a world position.
		/// </summary>
		/// <param name="worldCenter">Centre of the area, in world space.</param>
		/// <param name="radius">Radius in world metres.</param>
		/// <returns>How many chunks this explored that were not explored already.</returns>
		/// <remarks>
		/// The shape a consumable usually wants: "reveals the land within five hundred metres". A
		/// chunk counts when the circle reaches any part of it.
		/// </remarks>
		public static int ExploreAround(Vector3 worldCenter, float radius)
		{
			if (Fog == null)
			{
				return 0;
			}

			return NoteExplored(Fog.RevealAround(worldCenter, radius));
		}

		/// <summary>
		/// Explores every chunk a world-space rectangle touches.
		/// </summary>
		/// <param name="worldArea">The rectangle, on the XZ plane, in world metres.</param>
		/// <returns>How many chunks this explored that were not explored already.</returns>
		public static int ExploreArea(Rect worldArea)
		{
			if (Fog == null)
			{
				return 0;
			}

			return NoteExplored(Fog.RevealArea(worldArea));
		}

		/// <summary>
		/// Explores one chunk by its grid coordinates.
		/// </summary>
		/// <param name="chunkX">The chunk's X index.</param>
		/// <param name="chunkZ">The chunk's Z index.</param>
		/// <returns>True when that chunk had not been explored before.</returns>
		/// <remarks>
		/// For content that names chunks directly. Grid coordinates are only meaningful alongside
		/// the scene's bounds and chunk size, both of which change when a level designer moves a
		/// boundary volume — so anything authored against them should be re-checked when that
		/// happens. Naming a world position through <see cref="ExploreAround"/> does not have that
		/// problem and is the better choice unless the grid is genuinely what is being described.
		/// </remarks>
		public static bool ExploreChunk(int chunkX, int chunkZ)
		{
			if (Fog == null)
			{
				return false;
			}

			return NoteExplored(Fog.RevealChunk(chunkX, chunkZ) ? 1 : 0) > 0;
		}

		/// <summary>
		/// Explores the whole of the current scene.
		/// </summary>
		/// <remarks>
		/// For the map that hands a player an entire zone. A scene whose definition disables fog is
		/// already fully explored and has nothing to save, so this does nothing there.
		/// </remarks>
		public static void ExploreEverything()
		{
			if (Fog == null)
			{
				return;
			}

			int before = Fog.ExploredChunkCount;
			Fog.RevealAll();
			NoteExplored(Fog.ExploredChunkCount - before);
		}

		/// <summary>
		/// Schedules a save when exploration actually changed something.
		/// </summary>
		/// <param name="revealed">How many chunks were newly explored.</param>
		/// <returns><paramref name="revealed"/>, so callers can return it straight through.</returns>
		/// <remarks>
		/// The same debounce the walking path uses. Nothing new explored means nothing to write —
		/// re-using a map item on ground already covered must not dirty the file.
		/// </remarks>
		private static int NoteExplored(int revealed)
		{
			if (revealed > 0)
			{
				saveDueTime = Time.unscaledTimeAsDouble + SaveDebounce;
			}

			return revealed;
		}

		#endregion

		/// <summary>
		/// Writes the explored map if it has changed.
		/// </summary>
		private static void SaveFog()
		{
			if (Character == null || Fog == null || !Fog.IsDirty || string.IsNullOrEmpty(SceneName))
			{
				return;
			}

			// A scene with fog disabled is fully revealed by construction; there is nothing to keep.
			if (Definition != null && !Definition.FogOfWarEnabled)
			{
				Fog.ClearDirty();
				return;
			}

			FogOfWarStore.Save(Character.ID, SceneName, Fog);
		}

		/// <summary>
		/// Loads the character's notes for the current scene.
		/// </summary>
		private static void LoadNotes()
		{
			Notes.Clear();
			Notes.AddRange(MapNoteStore.Load(Character.ID, SceneName));

			nextNoteID = 1;
			for (int i = 0; i < Notes.Count; ++i)
			{
				if (Notes[i].ID >= nextNoteID)
				{
					nextNoteID = Notes[i].ID + 1;
				}
			}
		}

		/// <summary>
		/// Adds a note at a world position.
		/// </summary>
		/// <param name="position">Where to pin it.</param>
		/// <param name="title">The note's title.</param>
		/// <param name="text">The note's body.</param>
		/// <param name="colorIndex">Which palette colour to draw the pin in.</param>
		/// <returns>The note, or null when the character's note allowance is full.</returns>
		public static MapNote AddNote(Vector3 position, string title, string text, int colorIndex)
		{
			if (Character == null || string.IsNullOrEmpty(SceneName))
			{
				return null;
			}

			if (Notes.Count >= Cartography.NoteCapacity)
			{
				return null;
			}

			MapNote note = new MapNote()
			{
				ID = nextNoteID++,
				Position = position,
				ColorIndex = colorIndex,
			};
			note.SetContent(title, text);

			Notes.Add(note);
			SaveNotes();
			return note;
		}

		/// <summary>
		/// Removes a note.
		/// </summary>
		/// <param name="noteID">The note's identifier.</param>
		/// <returns>True when a note was removed.</returns>
		public static bool RemoveNote(long noteID)
		{
			for (int i = 0; i < Notes.Count; ++i)
			{
				if (Notes[i].ID == noteID)
				{
					Notes.RemoveAt(i);
					SaveNotes();
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Replaces a note's text.
		/// </summary>
		/// <param name="noteID">The note's identifier.</param>
		/// <param name="title">The new title.</param>
		/// <param name="text">The new body.</param>
		/// <returns>True when a note was found and updated.</returns>
		public static bool UpdateNote(long noteID, string title, string text)
		{
			for (int i = 0; i < Notes.Count; ++i)
			{
				if (Notes[i].ID == noteID)
				{
					Notes[i].SetContent(title, text);
					SaveNotes();
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Writes the notes and tells the panels.
		/// </summary>
		/// <remarks>
		/// Written immediately rather than debounced, unlike the fog. Notes change when a player
		/// deliberately types one, which happens a handful of times an hour; the fog changes every
		/// time they take a step.
		/// </remarks>
		private static void SaveNotes()
		{
			if (Character != null && !string.IsNullOrEmpty(SceneName))
			{
				MapNoteStore.Save(Character.ID, SceneName, Notes);
			}
			OnNotesChanged?.Invoke();
		}

		/// <summary>
		/// Starts loading the scene's baked map image.
		/// </summary>
		private static void BeginLoadMapImage()
		{
			if (Definition == null || Definition.MapImage == null || !Definition.MapImage.RuntimeKeyIsValid())
			{
				return;
			}

			/* The scene this load belongs to is captured, not read at completion time. A player who
			 * crosses a zone boundary while the image is in flight would otherwise have the old
			 * zone's map assigned over the new one's — Addressables cancels on release, but a load
			 * that had already succeeded still delivers its result. */
			string requestedScene = SceneName;

			mapImageHandle = Definition.MapImage.LoadAssetAsync<Texture2D>();
			mapImageLoading = true;
			mapImageHandle.Completed += handle =>
			{
				mapImageLoading = false;

				if (!string.Equals(requestedScene, SceneName, StringComparison.Ordinal))
				{
					return;
				}

				if (handle.Status != AsyncOperationStatus.Succeeded)
				{
					Log.Warning("ClientMapSystem", $"Could not load the baked map image for scene '{requestedScene}'. The world map will draw markers over a plain background. Re-run FishMMO/World Map/Bake Maps, and check the image is in an addressable group.");
					return;
				}

				MapImage = handle.Result;
				OnSceneMapChanged?.Invoke();
			};
		}

		/// <summary>
		/// Releases the baked map image.
		/// </summary>
		private static void ReleaseMapImage()
		{
			MapImage = null;

			if (mapImageHandle.IsValid())
			{
				/* Released even while it is still loading. Addressables cancels an in-flight load
				 * on release, and a zone change during a load would otherwise leave the completion
				 * callback assigning the previous scene's map over the new one's. */
				Addressables.Release(mapImageHandle);
				mapImageHandle = default;
			}

			mapImageLoading = false;
		}

		/// <summary>Whether the baked map image is still being loaded.</summary>
		public static bool IsMapImageLoading => mapImageLoading;

		/// <summary>
		/// Tears everything down. Called on quit to login.
		/// </summary>
		public static void Shutdown()
		{
			Flush();
			UnloadScene();

			Character = null;
			Filter.Reset();
			MapRelationshipTracker.Untrack();
			MapMarkerRegistry.Clear();
			Renderer.Dispose();
		}
	}

	/// <summary>
	/// Defaults for the exploration grid, in one place so the store, the map and the definition's
	/// override all agree.
	/// </summary>
	public static class FogOfWarDefaults
	{
		/// <summary>
		/// Length of one exploration chunk's side, in world metres.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A hundred and twenty-eight metres, which makes the shipped thousand-metre scenes nine
		/// chunks square. That is the number chosen to make the percentage worth showing: each
		/// chunk entered is a little over one percent, so the readout moves a visible step every
		/// time the player reaches new ground, rather than creeping by a point per sixty metres
		/// walked as the per-cell version did.
		/// </para>
		/// <para>
		/// It is also about the area the old reveal disc covered, so a scene opens up at roughly
		/// the rate it used to — in blocks with edges rather than as a spreading smudge.
		/// </para>
		/// <para>
		/// A scene that wants a different grain sets <c>FogChunkSize</c> on its map definition. A
		/// small interior wants a smaller number: at this size a two-hundred-metre scene is four
		/// chunks, and each one is a quarter of the map.
		/// </para>
		/// </remarks>
		public const float ChunkSize = 128.0f;
	}
}
