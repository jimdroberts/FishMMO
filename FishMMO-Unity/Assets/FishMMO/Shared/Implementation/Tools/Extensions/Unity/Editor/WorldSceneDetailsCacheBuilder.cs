using UnityEngine;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using System;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.Shared
{
	/// <summary>
	/// Utility for rebuilding and managing the WorldSceneDetailsCache addressable asset in the FishMMO project.
	/// </summary>
	public class WorldSceneDetailsCacheBuilder
	{
		/// <summary>
		/// Dashboard entry point (World → World Scene Details). See <see cref="Rebuild(WorldSystemsMode)"/>.
		/// </summary>
		/// <remarks>
		/// The only caller that asks. Every other rebuild — both of the ones a client build runs —
		/// stays on <see cref="WorldSystemsMode.Report"/>, because a prompt cannot be answered in a
		/// headless build and a scene saved during one would change the scenes being built.
		/// </remarks>
		[DashboardTool(DashboardToolAttribute.WorldSceneDetails, "Rebuild World Scene Details", Section = "Cache", Order = 0, Tooltip = "Re-reads every world scene, rewrites the world scene details cache, and offers to set up any scene that is missing the weather, cloud or sky systems.")]
		public static void RebuildFromDashboard()
		{
			Rebuild(WorldSystemsMode.Prompt);
		}

		/// <summary>Dashboard entry point that only reports on the world systems, changing nothing.</summary>
		[DashboardTool(DashboardToolAttribute.WorldSceneDetails, "Check world systems", Section = "Cache", Order = 1, Tooltip = "Rebuilds the cache and reports which scenes are missing the weather, cloud or sky systems, without changing any of them.")]
		public static void CheckWorldSystemsFromDashboard()
		{
			Rebuild(WorldSystemsMode.Report);
		}

		/// <summary>
		/// Rebuilds the world scene details cache, creating it if it does not exist yet.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Synchronous, and that is the point. This used to load the cache through
		/// <c>Addressables.LoadAssetAsync</c> and do all of its work inside a
		/// <c>handle.Completed</c> callback, so it returned having done nothing and finished later
		/// — if the editor was still alive to finish it. Under <c>-executeMethod</c> it never was:
		/// Unity ran the method, saw it return, and quit before the load completed, so the cache
		/// was silently left stale while the process exited zero. That made the one instruction
		/// <c>WorldMapDefinitionTests</c> gives — bake, then rebuild this cache — impossible to
		/// carry out from a command line, and impossible to carry out at all while the menu item
		/// below was commented out.
		/// </para>
		/// <para>
		/// Loaded through <see cref="AssetDatabase"/> rather than Addressables. This is editor
		/// tooling operating on a project asset, so the asset database is both the direct route and
		/// the honest one: it does not depend on a built catalog, and it cannot report "missing"
		/// for an asset that is sitting on disk. That distinction was not cosmetic — a catalog that
		/// was merely stale sent the old code down its creation path, which called
		/// <c>AssetDatabase.CreateAsset</c> over the existing file and replaced a populated cache
		/// with an empty one.
		/// </para>
		/// </remarks>
		/// <returns><c>true</c> if the cache was rebuilt and saved.</returns>
		public static bool Rebuild()
		{
			return Rebuild(WorldSystemsMode.Off);
		}

		/// <summary>
		/// Rebuilds the cache, and checks every world scene against the weather, cloud, sky and
		/// climate systems on the way past.
		/// </summary>
		/// <param name="worldSystems">
		/// What to do about a scene that is not set up. Every caller but the dashboard passes
		/// <see cref="WorldSystemsMode.Off"/>: an audit finding is not a fault in the rebuild, and
		/// logging one at its own severity from inside a rebuild would fail any test that rebuilds
		/// the cache and blame the rebuild for it.
		/// </param>
		/// <remarks>
		/// The audit rides along rather than running on its own because this rebuild is already the
		/// one operation that opens every world scene; checking them here costs a scan of scenes
		/// that are open anyway instead of opening all of them a second time.
		/// </remarks>
		/// <returns><c>true</c> if the cache was rebuilt and saved.</returns>
		public static bool Rebuild(WorldSystemsMode worldSystems)
		{
			WorldSceneDetailsCache cache =
				AssetDatabase.LoadAssetAtPath<WorldSceneDetailsCache>(WorldSceneDetailsCache.CACHE_FULL_PATH);

			if (cache == null)
			{
				return Create();
			}

			bool rebuilt;
			if (worldSystems == WorldSystemsMode.Off)
			{
				rebuilt = cache.Rebuild();
			}
			else
			{
				WorldSystemsConfigurator.BeginCollecting();
				try
				{
					rebuilt = cache.Rebuild();
				}
				finally
				{
					WorldSystemsConfigurator.EndCollecting();
				}
			}

			EditorUtility.SetDirty(cache);
			AssetDatabase.SaveAssets();

			if (!rebuilt)
			{
				Debug.LogWarning(
					$"[WorldSceneDetailsCacheBuilder] '{WorldSceneDetailsCache.CACHE_FULL_PATH}' was rebuilt, " +
					"but at least one reader reported a failure. The cache may be incomplete.");
			}

			/* After the cache is written, because the audit measures a scene's size from it, and
			 * before anything else reads the cache, because a scene that gains a settings component
			 * changes what the cache should say about it — hence the second rebuild below. */
			if (worldSystems != WorldSystemsMode.Off
				&& WorldSystemsConfigurator.Apply(WorldSystemsConfigurator.Resolve(), worldSystems))
			{
				Debug.Log("[World systems] Scenes changed; rebuilding the cache so it matches them.");
				cache.Rebuild();
				EditorUtility.SetDirty(cache);
				AssetDatabase.SaveAssets();
			}

			return rebuilt;
		}

		/// <summary>
		/// Creates the cache asset and registers it with Addressables.
		/// </summary>
		/// <remarks>
		/// Reached only when the asset genuinely does not exist. It writes to
		/// <see cref="WorldSceneDetailsCache.CACHE_FULL_PATH"/>, so reaching it while a cache is
		/// present would destroy that cache rather than rebuild it.
		/// </remarks>
		/// <returns><c>true</c> if the asset was created and registered.</returns>
		private static bool Create()
		{
			try
			{
				WorldSceneDetailsCache cache = ScriptableObject.CreateInstance<WorldSceneDetailsCache>();
				bool rebuilt = cache.Rebuild();

				EditorUtility.SetDirty(cache);
				AssetDatabase.CreateAsset(cache, WorldSceneDetailsCache.CACHE_FULL_PATH);

				AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
				if (settings == null)
				{
					Debug.LogError("[WorldSceneDetailsCacheBuilder] Addressable Asset Settings not found.");
					return false;
				}

				string guid = AssetDatabase.AssetPathToGUID(WorldSceneDetailsCache.CACHE_FULL_PATH);

				// Registered so the client can load it at runtime; the editor reads it directly.
				if (settings.FindAssetEntry(guid) == null)
				{
					settings.CreateOrMoveEntry(guid, settings.DefaultGroup);
					Debug.Log($"[WorldSceneDetailsCacheBuilder] '{WorldSceneDetailsCache.CACHE_FULL_PATH}' added to Addressables.");
				}

				EditorUtility.SetDirty(settings);
				AssetDatabase.SaveAssets();
				AssetDatabase.Refresh();

				return rebuilt;
			}
			catch (Exception ex)
			{
				Debug.LogError($"[WorldSceneDetailsCacheBuilder] Could not create the cache: {ex.Message}");
				return false;
			}
		}
	}
}
