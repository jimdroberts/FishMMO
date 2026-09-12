using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Client;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// A dungeon entrance puts itself on the minimap and the world map.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Issue #275: entrances were invisible on both maps. Nothing was broken — the entrance simply
	/// had no <see cref="MapMarker"/> and no code that would give it one, and a marker type that is
	/// never registered cannot be drawn no matter how the map renders.
	/// </para>
	/// <para>
	/// Three parts of this are worth pinning and the rest is not. The entrance has to configure the
	/// marker it requires, because that is the whole mechanism and it runs once, unattended, at
	/// awake. The icon has to be read <em>live</em> rather than copied, because
	/// <c>DungeonTemplate</c> resolves its artwork asynchronously and a copy taken at awake is a
	/// copy of nothing. And the discovery rule has to actually gate the draw — an entrance that is
	/// on the map before the player has been anywhere near it is the fog of war leaking, which is
	/// the one thing the map subsystem is built not to do.
	/// </para>
	/// <para>
	/// The last test is a guard rather than a feature. A marker type with no USS rule does not fail
	/// to draw; it draws in the fallback colour, which is an NPC's grey, so the new type looks
	/// exactly like the wrong thing and looks deliberate. That trap is walked into once per type
	/// added to the enum, so it is checked once here for all of them.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class DungeonEntranceMapMarkerTests
	{
		/// <summary>The shared marker sheet both map panels load.</summary>
		private const string MarkerSheetPath = "Assets/Scripts/Client/GUI/World/Map/UIMapShared.uss";

		/// <summary>Objects built by the fixtures, unregistered and destroyed afterwards.</summary>
		private readonly List<GameObject> hosts = new List<GameObject>();

		/// <summary>Sprites and templates built by the fixtures, destroyed afterwards.</summary>
		private readonly List<UnityEngine.Object> temporaryAssets = new List<UnityEngine.Object>();

		/// <summary>
		/// Tears the fixtures down and puts the log filter back.
		/// </summary>
		/// <remarks>
		/// The registry is static and shared with every other fixture in the run, so a marker left
		/// in it is drawn into the next test's results — including tests that assert absence, where
		/// a stray marker is exactly what makes them fail.
		/// </remarks>
		[TearDown]
		public void TearDown()
		{
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;

			for (int i = 0; i < hosts.Count; ++i)
			{
				if (hosts[i] == null)
				{
					continue;
				}

				MapMarker marker = hosts[i].GetComponent<MapMarker>();
				if (marker != null)
				{
					MapMarkerRegistry.Unregister(marker);
				}

				UnityEngine.Object.DestroyImmediate(hosts[i]);
			}
			hosts.Clear();

			for (int i = 0; i < temporaryAssets.Count; ++i)
			{
				if (temporaryAssets[i] != null)
				{
					UnityEngine.Object.DestroyImmediate(temporaryAssets[i]);
				}
			}
			temporaryAssets.Clear();
		}

		/// <summary>
		/// Builds a dungeon entrance the way the client would wake one, and returns it.
		/// </summary>
		/// <param name="dungeonName">The scene name authored on the entrance.</param>
		/// <param name="author">Optional authoring applied to the entrance and its marker first.</param>
		/// <returns>The entrance, with its marker configured.</returns>
		/// <remarks>
		/// <para>
		/// <c>author</c> runs before <c>OnAwake</c>, which is the order the editor produces: the
		/// components are authored in the scene and the callbacks run when it loads. Several tests
		/// here turn on what the entrance does <em>not</em> overwrite, so they have to be able to put
		/// something there to be left alone.
		/// </para>
		/// <para>
		/// Neither <c>Awake</c> nor <c>OnEnable</c> runs in edit mode for a component that is not
		/// <c>[ExecuteAlways]</c>, so both are called by hand. That is not a stand-in for Unity's
		/// dispatch — which is not this project's to verify — but the two moments the component
		/// does its work, and the only way to observe them from a test. The object is deactivated
		/// before the component goes on because a <c>DungeonEntrance</c> drags in
		/// <c>Interactable</c>'s own <c>RequireComponents</c>, a <c>NetworkObject</c> among them,
		/// and standing a networked prefab up in edit mode makes FishNet log an error that has
		/// nothing to do with what is under test.
		/// </para>
		/// </remarks>
		private DungeonEntrance NewEntrance(string dungeonName, Action<DungeonEntrance, MapMarker> author = null)
		{
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;

			GameObject host = new GameObject($"DungeonEntrance_{dungeonName}");
			hosts.Add(host);
			host.SetActive(false);

			DungeonEntrance entrance = host.AddComponent<DungeonEntrance>();
			entrance.DungeonName = dungeonName;

			MapMarker marker = host.GetComponent<MapMarker>();
			LogAssert.IsNotNull(marker, "A DungeonEntrance must arrive with a MapMarker beside it.");

			author?.Invoke(entrance, marker);

			entrance.OnAwake();
			Lifecycle(marker, "OnEnable");

			return entrance;
		}

		/// <summary>
		/// Builds a marker the filter can see, for use as a control.
		/// </summary>
		/// <param name="position">Where it stands.</param>
		/// <returns>A registered marker that is always drawn.</returns>
		/// <remarks>
		/// Registered by hand for the same reason <see cref="NewEntrance"/> calls
		/// <c>OnEnable</c> by hand: <see cref="MapMarkerRegistry"/> is filled from <c>OnEnable</c>,
		/// and <see cref="MapMarkerFilter"/> collects from the registry, so a marker that was never
		/// registered is invisible to it — and a test asserting that something is absent passes
		/// just as happily against a filter that returned nothing at all.
		/// </remarks>
		private MapMarker NewControlMarker(Vector3 position)
		{
			GameObject host = new GameObject("Control");
			hosts.Add(host);

			MapMarker marker = host.AddComponent<MapMarker>();
			marker.Visibility = MapMarkerVisibility.Always;
			host.transform.position = position;

			MapMarkerRegistry.Register(marker);
			return marker;
		}

		/// <summary>Invokes a private Unity callback, since edit mode does not.</summary>
		/// <param name="marker">The component to call on.</param>
		/// <param name="method">The callback's name.</param>
		private static void Lifecycle(MapMarker marker, string method)
		{
			MethodInfo info = typeof(MapMarker).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
			LogAssert.IsNotNull(info, $"MapMarker must still have a {method} for the fixtures to call.");
			info.Invoke(marker, null);
		}

		/// <summary>Builds a throwaway sprite to stand in for dungeon artwork.</summary>
		/// <param name="name">The sprite's name.</param>
		/// <returns>The sprite.</returns>
		private Sprite NewSprite(string name)
		{
			Texture2D texture = new Texture2D(2, 2) { name = $"{name}Texture" };
			temporaryAssets.Add(texture);

			Sprite sprite = Sprite.Create(texture, new Rect(0.0f, 0.0f, 2.0f, 2.0f), new Vector2(0.5f, 0.5f));
			sprite.name = name;
			temporaryAssets.Add(sprite);
			return sprite;
		}

		/// <summary>The snapshot produced for a particular marker.</summary>
		/// <param name="results">The collected snapshots.</param>
		/// <param name="marker">The marker to find.</param>
		/// <returns>Its snapshot.</returns>
		private static MapMarkerSnapshot FindSnapshot(List<MapMarkerSnapshot> results, MapMarker marker)
		{
			for (int i = 0; i < results.Count; ++i)
			{
				if (ReferenceEquals(results[i].Source, marker))
				{
					return results[i];
				}
			}

			LogAssert.Fail($"The filter produced no snapshot for '{marker.name}'.");
			return default;
		}

		/// <summary>Whether a marker reached the map at all.</summary>
		/// <param name="results">The collected snapshots.</param>
		/// <param name="marker">The marker to look for.</param>
		/// <returns>True when it was drawn.</returns>
		private static bool Contains(List<MapMarkerSnapshot> results, MapMarker marker)
		{
			for (int i = 0; i < results.Count; ++i)
			{
				if (ReferenceEquals(results[i].Source, marker))
				{
					return true;
				}
			}

			return false;
		}

		/// <summary>Removes CSS comments, so a selector written about in prose is not a rule.</summary>
		/// <param name="source">The stylesheet text.</param>
		/// <returns>The text with every comment removed.</returns>
		/// <remarks>
		/// The sheet's own header documents the naming scheme, which is the scheme this test
		/// searches for — so scanning the raw text would find the documentation and report the rule
		/// as present in any file that merely described it.
		/// </remarks>
		private static string StripCssComments(string source)
		{
			while (true)
			{
				int open = source.IndexOf("/*", StringComparison.Ordinal);
				if (open < 0)
				{
					return source;
				}

				int close = source.IndexOf("*/", open + 2, StringComparison.Ordinal);
				if (close < 0)
				{
					return source.Substring(0, open);
				}

				source = source.Remove(open, close - open + 2);
			}
		}

		// ── The type ────────────────────────────────────────────────

		[Test]
		public void MarkerType_DungeonEntranceIsAppendedLast_AndClassifiedAsLandmark()
		{
			/* Append-only is not a style preference. The ordinal is draw order, so inserting a type
			 * anywhere above the end silently renumbers everything below it and repaints the map. */
			Assert.AreEqual((byte)MapMarkerType.Waypoint + 1, (byte)MapMarkerType.DungeonEntrance,
				"The ordinal is draw order; append, never insert.");

			/* This is the assertion the type's classification actually rests on. The sweep beside it
			 * in MapSystemTests cannot fail for a forgotten type: Categorize's default arm returns
			 * Landmarks, which is in Categories, so a type nobody classified passes it by accident. */
			LogAssert.AreEqual(MapFilterCategory.Landmarks, MapFilters.Categorize(MapMarkerType.DungeonEntrance),
				"An entrance is a place on the map, so it belongs with the other places.");
		}

		[Test]
		public void EveryMarkerTypeHasAStyleRule()
		{
			LogAssert.IsTrue(File.Exists(MarkerSheetPath), $"the shared marker sheet must be at {MarkerSheetPath}");
			string source = StripCssComments(File.ReadAllText(MarkerSheetPath));

			foreach (MapMarkerType type in Enum.GetValues(typeof(MapMarkerType)))
			{
				/* The class name is generated rather than authored — UITKMapView derives it from the
				 * enum member — so the two only stay in step if something checks. */
				string selector = "." + UITKMapView.MarkerTypeClassPrefix + type.ToString().ToLowerInvariant();

				LogAssert.IsTrue(Regex.IsMatch(source, Regex.Escape(selector) + @"[\s,{]"),
					$"{type} has no rule in {MarkerSheetPath}. A type with no rule draws in the fallback colour, which is an NPC's grey, so a missing style is indistinguishable from the wrong marker.");
			}
		}

		// ── The marker the entrance fills in ────────────────────────

		[Test]
		public void Entrance_FillsInTheMarkerItRequires()
		{
			DungeonEntrance entrance = NewEntrance("Test Dungeon");
			MapMarker marker = entrance.GetComponent<MapMarker>();

			LogAssert.AreEqual(MapMarkerType.DungeonEntrance, marker.Type,
				"The entrance's marker has to say what it is, or it draws as whatever the default is.");

			/* Discovery, not Always. The entrance is a fixed public fixture once found, but it is
			 * not public before that — an entrance drawn from anywhere in the zone would be the fog
			 * of war announcing a place the player has never seen. */
			LogAssert.AreEqual(MapMarkerVisibility.Discovered, marker.Visibility,
				"An entrance appears once its chunk has been explored, and not before.");

			LogAssert.AreEqual("Test Dungeon", marker.Label,
				"The scene name is the only name available when there is no template, and it is better than no name.");
		}

		[Test]
		public void Entrance_LeavesAnAuthoredMarkerAloneApartFromTypeVisibilityAndLabel()
		{
			Sprite artwork = NewSprite("AuthoredIcon");

			DungeonEntrance entrance = NewEntrance("Test Dungeon", (_, marker) =>
			{
				marker.Icon = artwork;
				marker.IconSize = 42.0f;
				marker.Priority = 7;
				marker.ClampToEdge = true;
				marker.ShowOnMinimap = false;
				marker.Label = "The Deep";
			});

			MapMarker marker = entrance.GetComponent<MapMarker>();

			/* What the entrance writes is what it is: its kind, its visibility rule, and a name when
			 * nobody gave it one. Everything else belongs to whoever placed the entrance, and
			 * overwriting it would make the component impossible to author against. */
			LogAssert.AreEqual(MapMarkerType.DungeonEntrance, marker.Type, "Type is the entrance's to set.");
			LogAssert.AreEqual(MapMarkerVisibility.Discovered, marker.Visibility, "So is the visibility rule.");
			LogAssert.AreEqual("The Deep", marker.Label, "An authored label is kept, scene name or not.");
			LogAssert.AreEqual(42.0f, marker.IconSize, "Icon size is the author's.");
			LogAssert.AreEqual(7, marker.Priority, "So is draw priority.");
			LogAssert.IsTrue(marker.ClampToEdge, "And so is edge clamping.");
			LogAssert.IsFalse(marker.ShowOnMinimap, "A marker authored off the minimap must stay off it.");
			LogAssert.AreSame(artwork, marker.ResolvedIcon, "An authored icon wins over the entrance's own artwork.");
		}

		// ── The artwork ─────────────────────────────────────────────

		[Test]
		public void Entrance_SuppliesItsArtworkToTheMarkerAsItArrives()
		{
			Sprite artwork = NewSprite("DungeonArtwork");

			DungeonEntrance entrance = NewEntrance("Test Dungeon");
			MapMarker marker = entrance.GetComponent<MapMarker>();

			LogAssert.IsNull(marker.ResolvedIcon,
				"With no artwork the marker draws its type's shape. A null icon is a normal answer, not a failure.");

			/* Assigned after awake on purpose, which is the real sequence rather than a convenient
			 * one: DungeonTemplate resolves its icon through Addressables, so at awake there is
			 * nothing to copy. A marker that cached the icon once would hold the null forever and
			 * the dungeon's artwork would never appear on any map. */
			entrance.DungeonImage = artwork;

			LogAssert.AreSame(artwork, marker.ResolvedIcon,
				"Artwork that arrives after awake must still reach the map, without anything telling the marker it arrived.");

			LogAssert.AreSame(artwork, ((IMapMarkerIconSource)entrance).MapIcon,
				"With no template, the entrance falls back to the image it was authored with.");
		}

		[Test]
		public void Entrance_LabelPrefersTheTemplatesNameOverTheSceneName()
		{
			DungeonTemplate template = ScriptableObject.CreateInstance<DungeonTemplate>();
			template.DisplayName = "The Sunken Vault";
			temporaryAssets.Add(template);

			DungeonEntrance entrance = NewEntrance("dungeon_sunken_vault",
				(configured, _) => configured.Template = template);

			MapMarker marker = entrance.GetComponent<MapMarker>();

			LogAssert.AreEqual("The Sunken Vault", entrance.ResolvedDungeonName,
				"The template's name is what the dungeon is called; the authored field is a scene name.");

			LogAssert.AreEqual("The Sunken Vault", marker.Label,
				"The map says what the player knows the place by, which is the same name the dungeon finder shows.");
		}

		// ── The discovery rule ──────────────────────────────────────

		[Test]
		public void DiscoveredEntrance_AppearsOnlyAfterItsChunkIsExplored()
		{
			DungeonEntrance entrance = NewEntrance("Test Dungeon");
			MapMarker marker = entrance.GetComponent<MapMarker>();

			marker.transform.position = new Vector3(100.0f, 0.0f, 100.0f);

			FogOfWarMap fog = new FogOfWarMap(new Rect(0.0f, 0.0f, 200.0f, 200.0f), FogOfWarDefaults.ChunkSize);

			/* Prove the fixture is reachable before asserting it is absent. Without something known
			 * to be on the map, "the entrance was not drawn" is also what an empty registry, a
			 * filter that collected nothing and a typo in the type all look like. */
			MapMarker control = NewControlMarker(new Vector3(20.0f, 0.0f, 20.0f));

			List<MapMarkerSnapshot> results = new List<MapMarkerSnapshot>();
			new MapMarkerFilter().Collect(results, null, true, fog);

			FindSnapshot(results, control);

			LogAssert.IsFalse(Contains(results, marker),
				"An entrance standing in a chunk nobody has explored must not be on the map.");

			LogAssert.IsTrue(fog.Reveal(marker.Position),
				"The entrance must be inside the grid the fog covers, or this test proves nothing about visibility.");

			new MapMarkerFilter().Collect(results, null, true, fog);

			MapMarkerSnapshot snapshot = FindSnapshot(results, marker);
			LogAssert.AreEqual("Test Dungeon", snapshot.Label,
				"Once found it is a known place, drawn exactly and named rather than guessed at.");
		}
	}
}
