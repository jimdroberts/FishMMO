using System;
using System.IO;
using FishMMO.Shared;
using NUnit.Framework;
using UnityEditor;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs that rebuilding the world scene details cache finishes before it returns.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The rebuild used to load the cache through Addressables and do its work in a
	/// <c>handle.Completed</c> callback, so it returned having done nothing. Interactively the
	/// callback arrived a moment later and the asset was written, which is why this went unnoticed.
	/// Under <c>-executeMethod</c> the editor quits as soon as the method returns, so the callback
	/// never ran: the process exited zero, printed nothing, and left the cache stale.
	/// </para>
	/// <para>
	/// That mattered beyond convenience. <c>WorldMapDefinitionTests</c> tells whoever reads its
	/// failure to bake the maps and then rebuild this cache -- an instruction that could not be
	/// carried out from a command line at all, and could not be carried out from the editor either
	/// while the menu item was commented out.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class WorldSceneDetailsCacheBuilderTests
	{
		private static WorldSceneDetailsCache LoadCache()
		{
			return AssetDatabase.LoadAssetAtPath<WorldSceneDetailsCache>(
				WorldSceneDetailsCache.CACHE_FULL_PATH);
		}

		[Test]
		public void TheCacheIsPopulatedBeforeRebuildReturns()
		{
			/* The whole defect, stated as behaviour. A rebuild that schedules its work and returns
			 * passes nothing here, because by the time the assertion runs the caller has already
			 * been handed control back -- which is exactly the moment the editor quits under
			 * -executeMethod. */
			WorldSceneDetailsCache before = LoadCache();
			LogAssert.IsNotNull(before,
				$"'{WorldSceneDetailsCache.CACHE_FULL_PATH}' must exist for this fixture.");

			int sceneCount = before.Scenes.Count;
			LogAssert.IsTrue(sceneCount > 0,
				"the cache must already hold scenes, or this fixture cannot tell a working rebuild " +
				"from one that quietly emptied it");

			bool rebuilt = WorldSceneDetailsCacheBuilder.Rebuild();

			LogAssert.IsTrue(rebuilt,
				"Rebuild must report success synchronously, not schedule the work and return");

			LogAssert.AreEqual(sceneCount, LoadCache().Scenes.Count,
				"the cache must hold the same scenes immediately after the call returns");
		}

		[Test]
		public void RebuildingTwiceLeavesTheSameCache()
		{
			/* Rebuilding is how the cache is kept honest after a scene changes, so it has to be
			 * safe to run at any time. If a second run disagreed with the first, nobody could run
			 * it without checking what it did to their project. */
			WorldSceneDetailsCacheBuilder.Rebuild();
			int first = LoadCache().Scenes.Count;

			WorldSceneDetailsCacheBuilder.Rebuild();
			int second = LoadCache().Scenes.Count;

			LogAssert.AreEqual(first, second, "a second rebuild must agree with the first");
		}

		[Test]
		public void TheRebuildIsReachableFromTheEditorMenu()
		{
			/* The menu item was commented out, so the only way to run this was to know the type
			 * name and call it by hand. A maintenance action nobody can find is one nobody runs. */
			string source = File.ReadAllText(Path.Combine(
				Directory.GetCurrentDirectory(),
				"Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/WorldSceneDetailsCacheBuilder.cs"));

			int menu = source.IndexOf("[MenuItem(\"FishMMO/Rebuild World Scene Details", StringComparison.Ordinal);
			LogAssert.IsTrue(menu >= 0, "the rebuild must be on the FishMMO menu");

			// A commented-out attribute still contains the text, so the line has to be checked.
			int lineStart = source.LastIndexOf('\n', menu) + 1;
			string line = source.Substring(lineStart, menu - lineStart);

			LogAssert.IsFalse(line.Contains("//"),
				"the menu item must not be commented out");
		}

		[Test]
		public void TheRebuildDoesNotDependOnTheAddressablesCatalog()
		{
			/* Pinned in source because the failure it prevents is destructive and cannot be
			 * provoked safely from a test.
			 *
			 * Loading through Addressables reports "missing" whenever the catalog is stale, not
			 * only when the asset is absent -- and the missing branch creates the asset, writing
			 * over the very file it failed to read. A stale catalog therefore replaced a populated
			 * cache with an empty one. The asset database cannot make that mistake: it answers for
			 * what is on disk. */
			string source = File.ReadAllText(Path.Combine(
				Directory.GetCurrentDirectory(),
				"Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/WorldSceneDetailsCacheBuilder.cs"));

			int rebuild = source.IndexOf("public static bool Rebuild()", StringComparison.Ordinal);
			LogAssert.IsTrue(rebuild >= 0, "Rebuild must still exist and report its outcome");

			int end = source.IndexOf("private static bool Create()", rebuild, StringComparison.Ordinal);
			LogAssert.IsTrue(end > rebuild, "the end of Rebuild must be locatable");

			string body = source.Substring(rebuild, end - rebuild);

			LogAssert.IsFalse(body.Contains("LoadAssetAsync"),
				"the cache must be read from the asset database, not through an async catalog load");
		}
	}
}
