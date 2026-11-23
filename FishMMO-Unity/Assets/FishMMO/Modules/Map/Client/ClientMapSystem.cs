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
	/// <para><b>Exploration is the server's.</b> The fog is account-wide and kept by the server
	/// (<c>MapFogSystem</c>), which works it out from where the character walks. This reveals the same way so the map
	/// updates at once, and folds in what the server sends (<see cref="ApplyServerFog"/>); nothing is written locally.</para>
	///
	/// <para><b>Current and viewed scene.</b> The state above is the scene the character stands in: the minimap, exploration
	/// and live markers are about it. The world map may show another scene the account has entered
	/// (<see cref="ViewScene"/>, <see cref="Viewed"/>): its baked map, notes and the server's fog for it, read-only.</para>
	///
	/// <para><b>Lifetime.</b> Driven entirely from <see cref="SetCharacter"/> and
	/// <see cref="Tick"/>, which the minimap calls — it is the panel that is always present. The
	/// world map reads the same state and works whether or not the minimap is open, but a build
	/// that removes the minimap panel entirely has to drive this from somewhere else.</para>
	/// </remarks>
	public static class ClientMapSystem
	{
		/// <summary>How often the fog is revealed around the character, in seconds (the server's own rate).</summary>
		private const double RevealInterval = 0.25;

		/// <summary>How often destroyed markers are swept out of the registry, in seconds.</summary>
		private const double PruneInterval = 15.0;

		/// <summary>The character whose map is being shown.</summary>
		public static IPlayerCharacter Character { get; private set; }

		/// <summary>The scene details cache, supplied by whichever panel loads first.</summary>
		public static WorldSceneDetailsCache DetailsCache { get; set; }

		/// <summary>The current scene's map definition, or null when it has none.</summary>
		public static WorldMapDefinition Definition { get; private set; }

		/// <summary>The current scene's details from the cache, or null when the scene is unknown to it.</summary>
		public static WorldSceneDetails SceneDetails { get; private set; }

		/// <summary>The world rectangle the current scene's map covers.</summary>
		public static Rect MapRect { get; private set; }

		/// <summary>The account's explored map for the current scene, or null before one loads.</summary>
		public static FogOfWarMap Fog { get; private set; }

		/// <summary>The character's notes for the current scene.</summary>
		public static List<MapNote> Notes { get; } = new List<MapNote>();

		/// <summary>The rule deciding which markers may be drawn.</summary>
		public static MapMarkerFilter Filter { get; } = new MapMarkerFilter();

		/// <summary>The overhead camera feeding the minimap.</summary>
		public static MinimapCameraRenderer Renderer { get; } = new MinimapCameraRenderer();

		/// <summary>The baked overhead image for the current scene, or null when there is none.</summary>
		public static Texture2D MapImage { get; private set; }

		/// <summary>The scene the loaded state belongs to: the one the character stands in.</summary>
		public static string SceneName { get; private set; }

		/// <summary>The scene's player-facing name.</summary>
		public static string SceneDisplayName =>
			Definition != null ? Definition.ResolvedDisplayName : SceneName;

		/// <summary>Raised when the loaded scene changes, so panels can rebuild everything derived from it.</summary>
		public static event Action OnSceneMapChanged;

		/// <summary>Raised when a note is added, edited or removed.</summary>
		public static event Action OnNotesChanged;

		/// <summary>Raised when the world map's viewed scene changes, or something it shows (image, fog) arrives.</summary>
		public static event Action OnViewedSceneChanged;

		/// <summary>Raised when the list of scenes the account has entered changes.</summary>
		public static event Action OnVisitedScenesChanged;

		/// <summary>
		/// Sends a fog request for a scene to the server. Installed by the panel that owns the connection (the minimap);
		/// null when nothing is connected.
		/// </summary>
		public static Action<string> RequestFog { get; set; }

		/// <summary>One scene the account has entered at least once, as the server reports it.</summary>
		public readonly struct VisitedScene
		{
			public readonly string SceneName;
			public readonly int ExploredCount;

			public VisitedScene(string sceneName, int exploredCount)
			{
				SceneName = sceneName;
				ExploredCount = exploredCount;
			}
		}

		/// <summary>Every scene the account has entered (the server's list, plus the current scene).</summary>
		public static IReadOnlyList<VisitedScene> VisitedScenes => visitedScenes;

		/// <summary>
		/// What the world map shows: a scene's name, details, map, fog and notes. The current scene's, unless
		/// <see cref="ViewScene"/> chose another.
		/// </summary>
		public sealed class SceneView
		{
			public string SceneName;
			public WorldSceneDetails Details;
			public WorldMapDefinition Definition;
			public Rect MapRect;
			public FogOfWarMap Fog;
			public Texture2D MapImage;
			public IReadOnlyList<MapNote> Notes;

			/// <summary>True when this is the scene the character stands in.</summary>
			public bool IsCurrent;

			/// <summary>The scene's player-facing name.</summary>
			public string DisplayName => Definition != null ? Definition.ResolvedDisplayName : SceneName;
		}

		/// <summary>The scene the world map shows now.</summary>
		public static SceneView Viewed
		{
			get
			{
				if (otherView != null)
				{
					return otherView;
				}
				currentView.SceneName = SceneName;
				currentView.Details = SceneDetails;
				currentView.Definition = Definition;
				currentView.MapRect = MapRect;
				currentView.Fog = Fog;
				currentView.MapImage = MapImage;
				currentView.Notes = Notes;
				currentView.IsCurrent = true;
				return currentView;
			}
		}

		private static readonly SceneView currentView = new SceneView();
		private static SceneView otherView;
		private static AsyncOperationHandle<Texture2D> otherImageHandle;

		/// <summary>The latest fog the server sent, by scene; applied to whichever grid shows that scene.</summary>
		private static readonly Dictionary<string, FogChunkSet> serverFog = new Dictionary<string, FogChunkSet>(StringComparer.Ordinal);

		private static readonly List<VisitedScene> visitedScenes = new List<VisitedScene>();

		/// <summary>Handle for the addressable map image, held so it can be released.</summary>
		private static AsyncOperationHandle<Texture2D> mapImageHandle;

		/// <summary>Whether <see cref="mapImageHandle"/> refers to a live load.</summary>
		private static bool mapImageLoading;

		/// <summary>Time, on the unscaled clock, of the next fog reveal.</summary>
		private static double nextRevealTime;

		/// <summary>Where the last reveal sampled, so the next one explores the way between (a mount or a flight skips nothing).</summary>
		private static Vector3 lastRevealPosition;
		private static bool hasLastRevealPosition;

		/// <summary>Time, on the unscaled clock, of the next sweep of destroyed markers.</summary>
		private static double nextPruneTime;

		/// <summary>The next ID to hand a new note.</summary>
		private static long nextNoteID = 1;

		/// <summary>Whether the "fog cannot advance" warning has already been logged for this scene.</summary>
		private static bool revealRefusalReported;

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

		/// <summary>Points the maps at a character (or at nobody).</summary>
		public static void SetCharacter(IPlayerCharacter character)
		{
			if (ReferenceEquals(Character, character))
			{
				return;
			}

			Character = character;
			Filter.Reset();
			MapRelationshipTracker.Track(character);

			// The server's fog and scene list belong to the account of the character that was showing.
			serverFog.Clear();
			visitedScenes.Clear();
			OnVisitedScenesChanged?.Invoke();

			if (character == null)
			{
				UnloadScene();
				return;
			}

			/* The scene the character is standing in, which inside an instance is the instance
			 * scene and not SceneName. The server keys waypoint unlocks and validates travel
			 * requests by this name. */
			LoadScene(character.CurrentSceneName());
		}

		/// <summary>Per-frame upkeep: follows scene changes, reveals fog, prunes markers.</summary>
		public static void Tick()
		{
			if (Character == null)
			{
				return;
			}

			// A scene change arrives as a field on the character rather than as an event.
			string currentScene = Character.CurrentSceneName();
			if (!string.Equals(currentScene, SceneName, StringComparison.Ordinal))
			{
				LoadScene(currentScene);
			}

			double now = Time.unscaledTimeAsDouble;

			if (Fog != null && now >= nextRevealTime)
			{
				nextRevealTime = now + RevealInterval;

				Transform transform = Character.Transform;
				if (transform == null)
				{
					WarnRevealRefused("the character has no transform");
				}
				else if (!Fog.WorldRect.Contains(new Vector2(transform.position.x, transform.position.z)))
				{
					WarnRevealRefused($"the character is at {transform.position} but scene '{SceneName}' " +
						$"covers {Fog.WorldRect} — every reveal falls outside the map");
					hasLastRevealPosition = false;
				}
				else
				{
					RevealTravelled(transform.position);
				}
			}

			if (now >= nextPruneTime)
			{
				nextPruneTime = now + PruneInterval;
				MapMarkerRegistry.Prune();
			}
		}

		/// <summary>
		/// Explores the chunk under <paramref name="position"/> and the way from the last sample, the rule the server applies
		/// (a sample further than <c>MapFogSystem.MaxTravelPerSampleMetres</c> is a jump: only the arrival).
		/// </summary>
		private static void RevealTravelled(Vector3 position)
		{
			const float maxTravel = 120.0f;
			if (hasLastRevealPosition && (position - lastRevealPosition).sqrMagnitude <= maxTravel * maxTravel)
			{
				float dx = position.x - lastRevealPosition.x, dz = position.z - lastRevealPosition.z;
				float length = Mathf.Sqrt(dx * dx + dz * dz);
				int steps = Mathf.Clamp(Mathf.CeilToInt(length / (Fog.ChunkSize * 0.25f)), 1, 4096);
				for (int i = 0; i <= steps; ++i)
				{
					float t = i / (float)steps;
					Fog.Reveal(new Vector3(lastRevealPosition.x + dx * t, 0.0f, lastRevealPosition.z + dz * t));
				}
			}
			else
			{
				Fog.Reveal(position);
			}
			lastRevealPosition = position;
			hasLastRevealPosition = true;
		}

		/// <summary>
		/// The server's fog for a scene (<c>FogOfWarSceneBroadcast</c>): remembered, and applied to the grid showing that
		/// scene, the current one or the viewed one.
		/// </summary>
		public static void ApplyServerFog(string sceneName, byte[] data)
		{
			if (string.IsNullOrEmpty(sceneName))
			{
				return;
			}
			FogChunkSet set;
			if (data == null || data.Length == 0)
			{
				set = new FogChunkSet();
			}
			else if (!FogCodec.TryDecode(data, out set))
			{
				Log.Warning("ClientMapSystem", $"The server's fog for scene '{sceneName}' could not be read; the map keeps what it has.");
				return;
			}
			serverFog[sceneName] = set;

			// The grid redraws its own texture when chunks change; no scene event (that would reset the world map's view).
			if (Fog != null && string.Equals(sceneName, SceneName, StringComparison.Ordinal))
			{
				Fog.ApplyChunkSet(set);
			}
			if (otherView?.Fog != null && string.Equals(sceneName, otherView.SceneName, StringComparison.Ordinal))
			{
				otherView.Fog.ApplyChunkSet(set);
				OnViewedSceneChanged?.Invoke();
			}
			NoteVisited(sceneName, set.Count);
		}

		/// <summary>The server's list of scenes the account has entered (<c>FogOfWarScenesBroadcast</c>).</summary>
		public static void ApplyServerScenes(string[] sceneNames, int[] exploredCounts)
		{
			visitedScenes.Clear();
			if (sceneNames != null)
			{
				for (int i = 0; i < sceneNames.Length; ++i)
				{
					if (!string.IsNullOrEmpty(sceneNames[i]))
					{
						visitedScenes.Add(new VisitedScene(sceneNames[i], exploredCounts != null && i < exploredCounts.Length ? exploredCounts[i] : 0));
					}
				}
			}
			if (!string.IsNullOrEmpty(SceneName))
			{
				NoteVisited(SceneName, Fog != null ? Fog.ExploredChunkCount : 0, raise: false);
			}
			OnVisitedScenesChanged?.Invoke();
		}

		private static void NoteVisited(string sceneName, int exploredCount, bool raise = true)
		{
			for (int i = 0; i < visitedScenes.Count; ++i)
			{
				if (string.Equals(visitedScenes[i].SceneName, sceneName, StringComparison.Ordinal))
				{
					visitedScenes[i] = new VisitedScene(sceneName, Math.Max(visitedScenes[i].ExploredCount, exploredCount));
					return;
				}
			}
			visitedScenes.Add(new VisitedScene(sceneName, exploredCount));
			if (raise)
			{
				OnVisitedScenesChanged?.Invoke();
			}
		}

		/// <summary>
		/// Shows <paramref name="sceneName"/> on the world map; null or the current scene shows the current scene. Another
		/// scene is read-only: its baked map, this character's notes for it and the account's fog (asked of the server).
		/// </summary>
		public static void ViewScene(string sceneName)
		{
			if (string.IsNullOrEmpty(sceneName) || string.Equals(sceneName, SceneName, StringComparison.Ordinal))
			{
				if (otherView != null)
				{
					ReleaseOtherView();
					OnViewedSceneChanged?.Invoke();
				}
				return;
			}
			if (otherView != null && string.Equals(otherView.SceneName, sceneName, StringComparison.Ordinal))
			{
				return;
			}

			ReleaseOtherView();
			WorldSceneDetails details = null;
			DetailsCache?.Scenes?.TryGetValue(sceneName, out details);
			WorldMapDefinition definition = ResolveDefinition(sceneName, details);
			Rect rect = MapBoundsResolver.Resolve(definition, details);
			SceneView view = new SceneView
			{
				SceneName = sceneName,
				Details = details,
				Definition = definition,
				MapRect = rect,
				Notes = Character != null ? MapNoteStore.Load(Character.ID, sceneName) : new List<MapNote>(),
				IsCurrent = false,
			};
			if (rect.width > 0.0f && rect.height > 0.0f)
			{
				view.Fog = NewFog(definition, rect);
				if (serverFog.TryGetValue(sceneName, out FogChunkSet known))
				{
					view.Fog.ApplyChunkSet(known);
				}
			}
			otherView = view;
			BeginLoadOtherImage(view);

			// Always asked again: the cached copy is from when it was last viewed, and another character may have walked it since.
			RequestFog?.Invoke(sceneName);
			OnViewedSceneChanged?.Invoke();
		}

		private static void ReleaseOtherView()
		{
			if (otherView?.Fog != null)
			{
				otherView.Fog.ReleaseTexture();
			}
			if (otherImageHandle.IsValid())
			{
				Addressables.Release(otherImageHandle);
				otherImageHandle = default;
			}
			otherView = null;
		}

		private static void BeginLoadOtherImage(SceneView view)
		{
			WorldMapDefinition definition = view.Definition;
			if (definition == null || definition.MapImage == null || !definition.MapImage.RuntimeKeyIsValid())
			{
				return;
			}
#if UNITY_EDITOR
			if (UnityEditor.AssetDatabase.GetAssetPath(definition).StartsWith(WorldMapDefinition.BakedDirectory + "/", StringComparison.Ordinal))
			{
				view.MapImage = definition.MapImage.editorAsset as Texture2D;
				return;
			}
#endif
			otherImageHandle = definition.MapImage.LoadAssetAsync<Texture2D>();
			if (!otherImageHandle.IsValid())
			{
				otherImageHandle = default;
				return;
			}
			otherImageHandle.Completed += handle =>
			{
				if (!ReferenceEquals(otherView, view) || handle.Status != AsyncOperationStatus.Succeeded)
				{
					return;
				}
				view.MapImage = handle.Result;
				OnViewedSceneChanged?.Invoke();
			};
		}

		/// <summary>A scene's map definition: the cache's, or in the editor this machine's kept bake.</summary>
		private static WorldMapDefinition ResolveDefinition(string sceneName, WorldSceneDetails details)
		{
			WorldMapDefinition definition = details != null ? details.MapDefinition : null;
#if UNITY_EDITOR
			// The committed cache never references the gitignored bake, but this machine's kept map is right here.
			if (definition == null)
			{
				definition = UnityEditor.AssetDatabase.LoadAssetAtPath<WorldMapDefinition>(WorldMapDefinition.BakedAssetPath(sceneName));
			}
#endif
			return definition;
		}

		/// <summary>An empty grid for a scene, fully revealed when its fog is switched off.</summary>
		private static FogOfWarMap NewFog(WorldMapDefinition definition, Rect rect)
		{
			float chunkSize = definition != null && definition.FogChunkSize > 0.0f
				? definition.FogChunkSize
				: FogOfWarDefaults.ChunkSize;
			FogOfWarMap fog = new FogOfWarMap(rect, chunkSize);
			if (definition != null && !definition.FogOfWarEnabled)
			{
				fog.RevealAll();
				fog.ClearDirty();
			}
			return fog;
		}

		/// <summary>Loads everything the maps show for the scene the character stands in.</summary>
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

			SceneDetails = details;
			Definition = ResolveDefinition(sceneName, details);
			MapRect = MapBoundsResolver.Resolve(Definition, details);

			if (MapRect.width <= 0.0f || MapRect.height <= 0.0f)
			{
				/* Without bounds there is nothing to normalise positions against, so the world map
				 * has no coordinate system and the fog grid has no extent. The minimap still works
				 * — it is a live camera and needs no bounds at all. */
				Log.Warning("ClientMapSystem", $"Scene '{sceneName}' has no map bounds: it has neither a WorldMapDefinition with bounds nor any scene boundary to derive them from. The world map and fog of war are unavailable here; the minimap will still work.");
				OnSceneMapChanged?.Invoke();
				return;
			}

			LoadFog();
			LoadNotes();
			BeginLoadMapImage();
			NoteVisited(sceneName, Fog != null ? Fog.ExploredChunkCount : 0);

			OnSceneMapChanged?.Invoke();
		}

		/// <summary>Drops everything belonging to the previous scene, the viewed one included.</summary>
		private static void UnloadScene()
		{
			ReleaseMapImage();
			bool hadOtherView = otherView != null;
			ReleaseOtherView();

			Fog?.ReleaseTexture();
			Fog = null;
			Definition = null;
			SceneDetails = null;
			MapRect = Rect.zero;
			SceneName = null;
			Notes.Clear();
			nextNoteID = 1;
			nextRevealTime = 0.0;
			hasLastRevealPosition = false;
			revealRefusalReported = false;

			if (hadOtherView)
			{
				OnViewedSceneChanged?.Invoke();
			}
		}

		/// <summary>A fresh grid for the current scene, with whatever the server already sent for it.</summary>
		private static void LoadFog()
		{
			Fog = NewFog(Definition, MapRect);
			if (serverFog.TryGetValue(SceneName, out FogChunkSet known))
			{
				Fog.ApplyChunkSet(known);
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
		/// Passes a reveal's count through. (It scheduled the local save; exploration is kept by the server now.)
		/// </summary>
		/// <param name="revealed">How many chunks were newly explored.</param>
		/// <returns><paramref name="revealed"/>, so callers can return it straight through.</returns>
		private static int NoteExplored(int revealed)
		{
			return revealed;
		}

		#endregion

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

#if UNITY_EDITOR
			// Straight off disk: outside a build the addressable group that would serve a kept map does not exist.
			if (UnityEditor.AssetDatabase.GetAssetPath(Definition).StartsWith(WorldMapDefinition.BakedDirectory + "/", System.StringComparison.Ordinal))
			{
				MapImage = Definition.MapImage.editorAsset as Texture2D;
				return;
			}
#endif

			/* The scene this load belongs to is captured, not read at completion time. A player who
			 * crosses a zone boundary while the image is in flight would otherwise have the old
			 * zone's map assigned over the new one's — Addressables cancels on release, but a load
			 * that had already succeeded still delivers its result. */
			string requestedScene = SceneName;

			mapImageHandle = Definition.MapImage.LoadAssetAsync<Texture2D>();

			/* A handle can come back invalid, and subscribing to one throws rather than reporting
			 * failure through the callback below. Addressables answers this way when it has no
			 * initialised runtime to load through -- which is every edit-mode test that reaches here,
			 * so the exception surfaces as a fixture failure rather than as a map that did not draw.
			 * ReleaseMapImage already tests the same handle before releasing it; this is the matching
			 * test on the way in.
			 *
			 * Treated as a failed load rather than swallowed: the map draws over a plain background
			 * either way, and the reason is worth saying once. */
			if (!mapImageHandle.IsValid())
			{
				mapImageHandle = default;
				Log.Warning("ClientMapSystem",
					$"Addressables returned no load for the baked map image of scene '{requestedScene}'. " +
					"The world map will draw markers over a plain background.");
				return;
			}

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
					Log.Warning("ClientMapSystem", $"Could not load the baked map image for scene '{requestedScene}'. The world map will draw markers over a plain background. Re-run FishMMO Dashboard → World → World Map → Bake Maps, and check the image is in an addressable group.");
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

		/// <summary>Tears everything down: the client is leaving the world.</summary>
		public static void Shutdown()
		{
			UnloadScene();

			Character = null;
			serverFog.Clear();
			visitedScenes.Clear();
			RequestFog = null;
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
		/// Forty metres, which divides a shipped thousand-metre scene into a grid of twenty-eight
		/// chunks — each one an eighth of a percent of the scene.
		/// </para>
		/// <para>
		/// <b>This was 128 metres, and the reversal is worth keeping.</b> The chunk model replaced a
		/// per-cell reveal whose percentage crept by a point per sixty metres walked, and the answer
		/// at the time was a grain coarse enough that entering a chunk was a visible step: a little
		/// over one percent each. That swapped one failure for another. On a nine-chunk grid the
		/// displayed value only ever moves in whole numbers, so a step of a bit over one percent
		/// reads as a fixed increment — the readout looks like a count of blocks rather than a
		/// measurement of ground, which is how it was reported.
		/// </para>
		/// <para>
		/// Both failures have the same root: a whole-number percentage over a coarse grid. So the
		/// grid is fine enough to be worth measuring and the readout shows the fraction —
		/// <c>UITKMap.FormatExplored</c> prints as many decimals as one chunk is worth (tenths here,
		/// hundredths on a generated 4.6 km world). At this size one chunk is 0.13% and a hundred metres of
		/// straight walking crosses two and a half of them, so the number climbs as the player
		/// travels instead of lurching once a block.
		/// </para>
		/// <para>
		/// A scene that wants a different grain sets <c>FogChunkSize</c> on its map definition. A
		/// small interior wants a smaller number still: a two-hundred-metre scene at the default is
		/// five chunks across. Going much below this is self-defeating for the same reason the
		/// 128-metre grain was wrong in the other direction — a scene of ten thousand chunks moves
		/// the tenths digit once per twenty metres, which is a readout that appears to flicker
		/// rather than to measure.
		/// </para>
		/// </remarks>
		public const float ChunkSize = FogChunkSet.DefaultChunkSize;
	}
}
