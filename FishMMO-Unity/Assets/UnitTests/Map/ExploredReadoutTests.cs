using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using FishMMO.Client;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The world map's "Explored %" readout, and the thing that actually moves it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Reported as "the explored percentage never changes". The readout itself is not where that
	/// goes wrong: it recomputes from the live fog four times a second and these tests drive the
	/// real panel to prove it. What produces a frozen percentage is a reveal that is never
	/// accepted, which leaves the fog grid untouched for the whole session while everything
	/// downstream keeps faithfully reporting the number it was given.
	/// </para>
	/// <para>
	/// Two conditions do that and neither used to say anything at all: a character with no
	/// transform, and a character standing outside the rectangle the fog grid covers. Both are
	/// pinned below, warning included, because a silent one is indistinguishable from a broken
	/// readout — which is how it was reported.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class ExploredReadoutTests
	{
		private const string DetailsPath = "Assets/Prefabs/Shared/WorldSceneDetails.asset";
		private const string PanelSettingsPath = "Assets/UI Toolkit/PanelSettings.asset";
		private const string MapUxmlPath = "Assets/Scripts/Client/GUI/World/Map/UIMap.uxml";

		/// <summary>A scene the shipped details cache actually carries bounds for.</summary>
		private const string SceneName = "StartScene A";

		private const long TestCharacterID = 987654321L;

		private GameObject characterHost;
		private GameObject panelHost;

		[TearDown]
		public void TearDown()
		{
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
			ClientMapSystem.SetCharacter(null);
			FogOfWarStore.DeleteAll(TestCharacterID);

			if (characterHost != null)
			{
				Object.DestroyImmediate(characterHost);
			}
			if (panelHost != null)
			{
				Object.DestroyImmediate(panelHost);
			}
		}

		private static WorldSceneDetails Details()
		{
			WorldSceneDetailsCache cache = AssetDatabase.LoadAssetAtPath<WorldSceneDetailsCache>(DetailsPath);
			LogAssert.IsNotNull(cache, $"the shipped details cache must exist at {DetailsPath}");
			ClientMapSystem.DetailsCache = cache;

			cache.Scenes.TryGetValue(SceneName, out WorldSceneDetails details);
			LogAssert.IsNotNull(details, $"the details cache must still carry '{SceneName}'");
			return details;
		}

		private static Rect WorldRect()
		{
			WorldSceneDetails details = Details();
			return MapBoundsResolver.Resolve(details.MapDefinition, details);
		}

		/// <summary>
		/// Stands a character up at a world position and points the map system at it.
		/// </summary>
		/// <remarks>
		/// The cached transform is assigned by hand because <c>BaseCharacter.Awake</c> does not run
		/// on an object created in edit mode, and <c>Transform</c> being null is one of the two
		/// conditions under test.
		/// </remarks>
		private PlayerCharacter ArmCharacter(Vector3 position, bool withTransform = true)
		{
			/* Building a PlayerCharacter by hand drags in its nineteen RequireComponents, and
			 * FishNet logs an error about the duplicate NetworkObject that comes with them. That
			 * is an artefact of standing a networked prefab up in edit mode, not of anything under
			 * test, and an unexpected error log fails a test outright. Expected warnings are still
			 * asserted: ignoreFailingMessages only stops UNEXPECTED messages from failing. */
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;

			characterHost = new GameObject("ExploredReadoutCharacter");
			characterHost.SetActive(false);
			PlayerCharacter character = characterHost.AddComponent<PlayerCharacter>();

			character.ID = TestCharacterID;
			character.SceneName = SceneName;
			characterHost.transform.position = position;

			if (withTransform)
			{
				typeof(BaseCharacter).GetProperty("Transform").SetValue(character, characterHost.transform);
			}

			Details();
			ClientMapSystem.SetCharacter(character);
			return character;
		}

		/// <summary>
		/// Whether the map system has reported that exploration cannot advance.
		/// </summary>
		/// <remarks>
		/// Read off the flag rather than asserted through Unity's log expectations: the message goes
		/// out through FishMMO's own logger, which is not wired to <c>UnityEngine.Debug</c> in an
		/// edit-mode test, so nothing would ever be captured. The flag is what the reporting branch
		/// sets, so testing it tests that the branch was taken.
		/// </remarks>
		private static bool RefusalReported()
		{
			return (bool)typeof(ClientMapSystem)
				.GetField("revealRefusalReported", BindingFlags.Static | BindingFlags.NonPublic)
				.GetValue(null);
		}

		/// <summary>Runs one reveal, bypassing the quarter-second rate limit real time provides.</summary>
		private static void TickOnce()
		{
			typeof(ClientMapSystem)
				.GetField("nextRevealTime", BindingFlags.Static | BindingFlags.NonPublic)
				.SetValue(null, 0.0);
			ClientMapSystem.Tick();
		}

		[Test]
		public void WalkingAcrossAScene_AdvancesTheExploredFraction()
		{
			Rect world = WorldRect();
			ArmCharacter(new Vector3(world.center.x, 30.0f, world.center.y));

			LogAssert.IsNotNull(ClientMapSystem.Fog, "the shipped scene has bounds, so a fog grid must be built");
			LogAssert.IsTrue(ClientMapSystem.Fog.ExploredFraction() <= 0.0f, "a new map starts unexplored");

			for (int i = 0; i < 200; ++i)
			{
				characterHost.transform.position += new Vector3(1.5f, 0.0f, 0.0f);
				TickOnce();
			}

			float fraction = ClientMapSystem.Fog.ExploredFraction();
			LogAssert.IsTrue(ClientMapSystem.Fog.ExploredChunkCount >= 3,
				$"300 metres in a straight line crosses several chunks, but only {ClientMapSystem.Fog.ExploredChunkCount} were explored");
			LogAssert.IsTrue(fraction > 0.02f,
				$"and that must be a visible slice of the scene, but it is {fraction * 100.0f:F3}%");
		}

		[Test]
		public void ACharacterWithNoTransform_ExploresNothing_AndSaysSo()
		{
			ArmCharacter(new Vector3(0.0f, 30.0f, 0.0f), withTransform: false);
			LogAssert.IsNotNull(ClientMapSystem.Fog, "the fog grid is still built; only the reveal is refused");

			for (int i = 0; i < 20; ++i)
			{
				TickOnce();
			}

			LogAssert.IsTrue(RefusalReported(),
				"a character with no transform must be reported, not silently skipped for the whole session");
			LogAssert.IsTrue(ClientMapSystem.Fog.ExploredFraction() <= 0.0f,
				"nothing can be revealed without a transform — the point of the warning");
			LogAssert.IsFalse(ClientMapSystem.Fog.IsDirty, "and nothing is written to disk either");
		}

		[Test]
		public void ACharacterOutsideTheMap_ExploresNothing_AndSaysSo()
		{
			Rect world = WorldRect();

			/* Far outside the scene's bounds. Reveal quietly drops everything in this case, which is
			 * what a character physically standing in one scene while the fog was built for another
			 * looks like from here. */
			ArmCharacter(new Vector3(world.xMax + 5000.0f, 30.0f, world.yMax + 5000.0f));

			for (int i = 0; i < 20; ++i)
			{
				characterHost.transform.position += new Vector3(1.5f, 0.0f, 0.0f);
				TickOnce();
			}

			LogAssert.IsTrue(RefusalReported(),
				"a character outside the fog grid must be reported, not silently skipped for the whole session");
			LogAssert.IsTrue(ClientMapSystem.Fog.ExploredFraction() <= 0.0f,
				"a character outside the grid reveals nothing, however far it walks");
		}

		// ── Granted exploration (map items, triggers, quest rewards) ─

		/// <summary>The pending debounced save, zero when nothing is outstanding.</summary>
		private static double SaveDueTime()
		{
			return (double)typeof(ClientMapSystem)
				.GetField("saveDueTime", BindingFlags.Static | BindingFlags.NonPublic)
				.GetValue(null);
		}

		private static void ClearSaveDueTime()
		{
			typeof(ClientMapSystem)
				.GetField("saveDueTime", BindingFlags.Static | BindingFlags.NonPublic)
				.SetValue(null, 0.0);
		}

		[Test]
		public void ExploreAround_GrantsGroundTheCharacterNeverWalked()
		{
			Rect world = WorldRect();
			Vector3 centre = new Vector3(world.center.x, 30.0f, world.center.y);
			ArmCharacter(centre);

			LogAssert.IsTrue(ClientMapSystem.Fog.ExploredChunkCount == 0, "nothing is explored to begin with");
			ClearSaveDueTime();

			int revealed = ClientMapSystem.ExploreAround(centre, 300.0f);

			LogAssert.IsTrue(revealed > 1, $"a three-hundred-metre map item must cover several chunks, but it covered {revealed}");
			LogAssert.IsTrue(ClientMapSystem.Fog.ExploredChunkCount == revealed, "and the map must hold exactly those");
			LogAssert.IsTrue(SaveDueTime() > 0.0, "granted exploration must be written out like walked exploration");
		}

		[Test]
		public void ReusingAMapItem_OnGroundAlreadyKnown_WritesNothing()
		{
			/* Nothing new explored is not a change, and it must not dirty the file. Otherwise a
			 * player standing on explored ground with a stack of map scrolls rewrites their whole
			 * exploration file once per use for no reason. */
			Rect world = WorldRect();
			Vector3 centre = new Vector3(world.center.x, 30.0f, world.center.y);
			ArmCharacter(centre);

			ClientMapSystem.ExploreAround(centre, 300.0f);
			ClientMapSystem.Fog.ClearDirty();
			ClearSaveDueTime();

			int again = ClientMapSystem.ExploreAround(centre, 300.0f);

			LogAssert.IsTrue(again == 0, $"the same ground again must explore nothing, but it reported {again}");
			LogAssert.IsFalse(ClientMapSystem.Fog.IsDirty, "and must leave the map clean");
			LogAssert.IsTrue(SaveDueTime() == 0.0, "and must not schedule a write");
		}

		[Test]
		public void ExploreChunk_TakesGridCoordinates_AndIgnoresOnesOffTheGrid()
		{
			Rect world = WorldRect();
			ArmCharacter(new Vector3(world.center.x, 30.0f, world.center.y));
			ClearSaveDueTime();

			LogAssert.IsTrue(ClientMapSystem.ExploreChunk(0, 0), "the corner chunk had not been explored");
			LogAssert.IsFalse(ClientMapSystem.ExploreChunk(0, 0), "and cannot be explored twice");
			LogAssert.IsFalse(ClientMapSystem.ExploreChunk(-1, 12345),
				"content naming a chunk outside the grid must be inert, not fatal");
			LogAssert.IsTrue(ClientMapSystem.Fog.ExploredChunkCount == 1, "exactly one chunk was granted");
		}

		[Test]
		public void ExploreEverything_HandsOverTheWholeScene()
		{
			Rect world = WorldRect();
			ArmCharacter(new Vector3(world.center.x, 30.0f, world.center.y));
			ClearSaveDueTime();

			ClientMapSystem.ExploreEverything();

			LogAssert.IsTrue(ClientMapSystem.Fog.ExploredFraction() >= 1.0f, "the whole scene reads as explored");
			LogAssert.IsTrue(SaveDueTime() > 0.0, "and that is worth writing out");
		}

		[Test]
		public void TheExplorationApi_IsSafeBeforeAnySceneIsLoaded()
		{
			/* A map item used during a scene transfer, or on a scene with no bounds, finds no map to
			 * write into. Doing nothing is the answer; throwing would take the item's whole event
			 * chain down with it. */
			ClientMapSystem.SetCharacter(null);

			LogAssert.IsTrue(ClientMapSystem.ExploreAround(Vector3.zero, 500.0f) == 0, "no map, nothing explored");
			LogAssert.IsTrue(ClientMapSystem.ExploreArea(new Rect(0.0f, 0.0f, 100.0f, 100.0f)) == 0, "and no exception");
			LogAssert.IsFalse(ClientMapSystem.ExploreChunk(0, 0), "and none from the grid form either");
			ClientMapSystem.ExploreEverything();
		}

		[Test]
		public void TheReadout_ReportsWhateverTheFogSays()
		{
			Rect world = WorldRect();

			PanelSettings settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
			VisualTreeAsset uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(MapUxmlPath);
			LogAssert.IsNotNull(settings, $"panel settings must exist at {PanelSettingsPath}");
			LogAssert.IsNotNull(uxml, $"the world map UXML must exist at {MapUxmlPath}");

			panelHost = new GameObject("ExploredReadoutPanel");
			UIDocument document = panelHost.AddComponent<UIDocument>();
			document.panelSettings = Object.Instantiate(settings);
			document.visualTreeAsset = uxml;

			UITKMap map = panelHost.AddComponent<UITKMap>();
			map.Document = document;
			map.OnStarting();

			MethodInfo refresh = typeof(UITKMap).GetMethod("RefreshExplored", BindingFlags.Instance | BindingFlags.NonPublic);
			FieldInfo labelField = typeof(UITKMap).GetField("exploredLabel", BindingFlags.Instance | BindingFlags.NonPublic);
			LogAssert.IsNotNull(refresh, "UITKMap must still declare RefreshExplored");
			LogAssert.IsNotNull(labelField, "UITKMap must still hold the explored label");

			PropertyInfo fogProperty = typeof(ClientMapSystem).GetProperty("Fog");

			FogOfWarMap fog = new FogOfWarMap(world, FogOfWarDefaults.ChunkSize);
			fogProperty.SetValue(null, fog);
			refresh.Invoke(map, null);

			Label label = (Label)labelField.GetValue(map);
			LogAssert.IsNotNull(label, "the world map UXML must still carry the explored label");
			LogAssert.IsTrue(label.text == "Explored 0%", $"an unexplored map reads zero, not '{label.text}'");

			Vector3 walk = new Vector3(world.center.x, 0.0f, world.center.y);
			for (int i = 0; i < 200; ++i)
			{
				walk.x += 1.5f;
				fog.Reveal(walk);
			}

			refresh.Invoke(map, null);
			LogAssert.IsTrue(label.text != "Explored 0%",
				$"the readout must follow the fog it is given, but it still reads '{label.text}'");

			fogProperty.SetValue(null, null);
		}
	}
}
