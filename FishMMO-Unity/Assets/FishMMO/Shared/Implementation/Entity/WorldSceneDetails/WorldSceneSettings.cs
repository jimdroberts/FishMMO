using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

namespace FishMMO.Shared
{
	/// <summary>
	/// MonoBehaviour holding per-scene server-facing configuration and the link to the scene's
	/// <see cref="WorldMapDefinition"/>.
	/// </summary>
	/// <remarks>
	/// Core (Phase 4 of the core and modules plan, 2026-11-17): only what the game itself needs of a scene. What the world
	/// modules know of it — where it lies on its planet, its time of day, its climate, biomes and weather — they read through
	/// their own extensions of this component (<c>WorldSceneAtlas</c>, <c>WorldSceneClimates</c>), from the scene's atlas
	/// entry and, for a scene that authors its own climate, a <c>WorldSceneClimate</c> beside it. The day and night cycle
	/// is its own component too.
	/// </remarks>
	public class WorldSceneSettings : MonoBehaviour
	{
		/// <summary>
		/// The hard ceiling on clients in any one scene, anywhere in the project.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The single source of truth for the cap. The world server (routing and instance
		/// spin-up) and the scene server (channel admission) both clamp to this, so a scene
		/// authored above it is reduced rather than honoured — a designer cannot raise a scene's
		/// population past what the bandwidth and observer budgets were sized for.
		/// </para>
		/// <para>
		/// 200 is a deliberate ceiling, not a guess: per-client cost is bounded by the observer
		/// visibility budget rather than by scene population, so scaling past this is done by
		/// adding scene instances (and scene servers), never by widening one scene. See
		/// <c>SceneServerPlacementPolicy</c>.
		/// </para>
		/// </remarks>
		public const int MaximumClientsPerScene = 200;

		/// <summary>
		/// The maximum number of clients allowed in this scene.
		/// </summary>
		/// <remarks>
		/// The <see cref="RangeAttribute"/> constrains the inspector; it does not constrain a value
		/// edited into the scene YAML by hand or arriving from an older asset, so every consumer
		/// clamps to <see cref="MaximumClientsPerScene"/> on read as well.
		/// </remarks>
		[Tooltip("The maximum number of clients allowed in this scene. Clamped to 200. The scene's world atlas entry may override it.")]
		[Range(1, MaximumClientsPerScene)]
		[SerializeField, FormerlySerializedAs("MaxClients")]
		private int maxClients = MaximumClientsPerScene;

		/// <summary>The client cap: the atlas entry's, else the scene's own.</summary>
		public int MaxClients
		{
			get
			{
				IWorldSceneEntry entry = Entry;
				return entry != null && entry.MaxClients > 0 ? entry.MaxClients : maxClients;
			}
			set => maxClients = value;
		}

		/// <summary>
		/// Optional hand-made map definition, for a set of scenes that share one map (a dungeon and
		/// its instanced twin). Normally empty: a client build bakes a definition per scene into
		/// <see cref="WorldMapDefinition.BakedDirectory"/>, points the world scene details cache at
		/// it, and removes it after the build. A definition assigned here is filled in by that same
		/// bake instead.
		/// </summary>
		[Tooltip("Optional. Leave empty and the client build bakes this scene's map; assign one only to share a map between scenes.")]
		public WorldMapDefinition MapDefinition;

		/// <summary>
		/// The scene's loading image. Authored here; the map bake copies it into the scene's
		/// definition so the client finds the whole presentation in one place.
		/// </summary>
		[Tooltip("The image shown while this scene loads. Copied into the baked map definition.")]
		public Sprite SceneTransitionImage;

		/// <summary>
		/// A world only the client draws, never a place players go (the login screen's backdrop). It is cut, baked and
		/// shipped like any world scene, so its ground, sky and weather work, but the world scene details cache leaves
		/// it out, so no server hosts it and nothing can send a character there.
		/// </summary>
		[Tooltip("A client-only backdrop (the login screen's world): built like a world scene, never hosted or travelled to.")]
		public bool ClientBackdrop;

		// ── The scene's entry in the world's design ───────────────────

		/// <summary>
		/// This scene's entry in the installed world design (the world atlas), as the core sees it, or null: no world design
		/// installed, or the scene not in it. The world modules read their own entry (<c>settings.AtlasEntry()</c>).
		/// </summary>
		public IWorldSceneEntry Entry => WorldSceneEntries.Find(SceneName);

		/// <summary>
		/// The scene this component belongs to, resolved once rather than on every read.
		/// </summary>
		/// <remarks>
		/// <para>The biome sampler asked <c>gameObject.scene</c> for every sample, and every property
		/// here that consults the atlas asked it for the scene's name — two engine calls and a fresh
		/// string each time, about ten times per biome sample, and weather exposure samples every
		/// character every second on the server and the owning client alike. The answer cannot
		/// change at runtime: nothing moves a settings object between scenes, and it goes with its
		/// scene when that unloads. So it is read on first use and read again whenever the
		/// component is enabled.</para>
		///
		/// <para>Outside play mode it is read live every time, exactly as before. A designer can drag
		/// the object into another open scene, and edit-mode tools can hold a component whose
		/// <c>OnEnable</c> never ran.</para>
		/// </remarks>
		public Scene OwnScene
		{
			get
			{
#if UNITY_EDITOR
				if (!Application.isPlaying)
				{
					return gameObject.scene;
				}
#endif
				if (ownScene.handle == SceneHandle.None)
				{
					ResolveOwnScene();
				}
				return ownScene;
			}
		}

		/// <summary>The name of <see cref="OwnScene"/>, resolved with it.</summary>
		public string SceneName
		{
			get
			{
#if UNITY_EDITOR
				if (!Application.isPlaying)
				{
					return gameObject.scene.name;
				}
#endif
				if (ownScene.handle == SceneHandle.None)
				{
					ResolveOwnScene();
				}
				return ownSceneName;
			}
		}

		/// <summary>
		/// The resolved scene. A handle of 0 is no scene at all, and means "not resolved yet" — so a
		/// value the editor's script reload wiped simply resolves again.
		/// </summary>
		private Scene ownScene;
		private string ownSceneName;

		/// <summary>Reads the scene this component is in, and its name.</summary>
		private void ResolveOwnScene()
		{
			ownScene = gameObject.scene;
			ownSceneName = ownScene.name;
		}

		private static readonly Dictionary<SceneHandle, WorldSceneSettings> byScene = new Dictionary<SceneHandle, WorldSceneSettings>();

		/// <summary>The settings component of a loaded scene, if it has one. Scenes load additively on a scene server, so this is keyed by scene.</summary>
		public static bool TryGetForScene(Scene scene, out WorldSceneSettings settings)
		{
			return byScene.TryGetValue(scene.handle, out settings) && settings != null;
		}

		private void OnEnable()
		{
			ResolveOwnScene();
			byScene[ownScene.handle] = this;
		}

		private void OnDisable()
		{
			if (byScene.TryGetValue(gameObject.scene.handle, out WorldSceneSettings current) && current == this)
			{
				byScene.Remove(gameObject.scene.handle);
			}
		}
	}
}
