using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared;
using FishMMO.Shared.WorldDesign;
using FishMMO.Water;
using Object = UnityEngine.Object;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The ice placer: where floating ice occurs (pure functions of temperature), and the scene it
	/// writes — never through the sea bed, clear of the shore, the edge and each other, the same
	/// every time from the same seed, replacing only its own root, and skipping prefabs that were
	/// never generated.
	/// </summary>
	[TestFixture]
	public class IcePlacerTests
	{
		private Scene scene;

		[SetUp]
		public void OpenScene()
		{
			scene = EditorSceneManager.NewPreviewScene();
		}

		[TearDown]
		public void CloseScene()
		{
			if (scene.IsValid())
			{
				EditorSceneManager.ClosePreviewScene(scene);
			}
		}

		// ── Occurrence ────────────────────────────────────────────────

		private static float[] Year(float c)
		{
			var seasons = new float[IceOccurrence.Seasons];
			for (int i = 0; i < seasons.Length; i++) seasons[i] = c;
			return seasons;
		}

		[Test]
		public void SeaSurface_IsHeldAtSeaWatersFreezingPoint()
		{
			Assert.That(IceOccurrence.SeaSurfaceC(-20f), Is.EqualTo(-1.8f));
			Assert.That(IceOccurrence.SeaSurfaceC(3f), Is.EqualTo(3f));
		}

		[Test]
		public void SeaIce_NoneAboveSeaWatersFreezingPoint_ThenRampsToAClosedPack()
		{
			Assert.That(IceOccurrence.SeaIceConcentration(0f), Is.EqualTo(0f), "fresh water's 0 °C does not freeze the sea");
			Assert.That(IceOccurrence.SeaIceConcentration(-1.8f), Is.EqualTo(0f).Within(1e-5f));
			Assert.That(IceOccurrence.SeaIceConcentration(-1.8f - IceOccurrence.PackRampKelvin * 0.5f), Is.EqualTo(0.5f).Within(1e-4f));
			Assert.That(IceOccurrence.SeaIceConcentration(-30f), Is.EqualTo(1f));
			float last = 0f;
			for (float t = 5f; t >= -20f; t -= 0.5f)
			{
				float c = IceOccurrence.SeaIceConcentration(t);
				Assert.That(c, Is.GreaterThanOrEqualTo(last), "colder water never has less ice");
				last = c;
			}
			// Half the year in a closed pack, half open: the year's average.
			var seasons = Year(-20f);
			for (int i = 0; i < seasons.Length / 2; i++) seasons[i] = 5f;
			Assert.That(IceOccurrence.SeaIceConcentration(seasons), Is.EqualTo(0.5f).Within(1e-5f));
			Assert.That(IceOccurrence.PancakeShare(0.1f), Is.GreaterThan(IceOccurrence.PancakeShare(0.9f)), "pancakes at the ice edge");
		}

		[Test]
		public void Bergs_FewerAndSmallerAsTheSeaWarms_NoneAboveFiveDegrees()
		{
			float lastSupply = float.MaxValue;
			int lastSize = int.MaxValue;
			for (float sst = -1.8f; sst <= 5f; sst += 0.1f)
			{
				float supply = IceOccurrence.BergSupply(sst, true, 0f);
				IcebergSize? size = IceOccurrence.LargestBergSize(sst, 0f);
				Assert.That(supply, Is.LessThanOrEqualTo(lastSupply), $"supply at {sst:0.0} °C");
				Assert.That(size.HasValue, Is.True, $"something survives at {sst:0.0} °C");
				Assert.That((int)size.Value, Is.LessThanOrEqualTo(lastSize), $"largest size at {sst:0.0} °C");
				lastSupply = supply;
				lastSize = (int)size.Value;
			}
			Assert.That(IceOccurrence.LargestBergSize(-1f, 0f), Is.EqualTo(IcebergSize.Large));
			Assert.That(IceOccurrence.LargestBergSize(4.9f, 0f), Is.EqualTo(IcebergSize.Growler));
			Assert.That(IceOccurrence.LargestBergSize(5.5f, 0f), Is.Null);
			Assert.That(IceOccurrence.BergSupply(5.5f, true, 0f), Is.EqualTo(0f));
			Assert.That(IceOccurrence.BergSupply(-1.8f, false, 0f), Is.EqualTo(0f), "no calving coast, no bergs");
			Assert.That(IceOccurrence.BergSupply(-1.8f, true, 20f), Is.LessThan(IceOccurrence.BergSupply(-1.8f, true, 0f)), "drift costs supply");
			Assert.That(IceOccurrence.LargestBergSize(-1f, 25f), Is.EqualTo(IcebergSize.Small), "two classes lost over 25° of drift");
		}

		[Test]
		public void Tabular_OnlyFromAnIceShelfCoast()
		{
			Assert.That(IceOccurrence.ClassWeight(IcebergClass.Tabular, -15f, -1.8f, 0f), Is.GreaterThan(0f));
			Assert.That(IceOccurrence.ClassWeight(IcebergClass.Tabular, -2f, -1.8f, 0f), Is.EqualTo(0f));
			Assert.That(IceOccurrence.ClassWeight(IcebergClass.Drydock, -15f, 4f, 10f),
				Is.GreaterThan(IceOccurrence.ClassWeight(IcebergClass.Drydock, -15f, -1.8f, 0f)), "erosion carves drydocks");
		}

		[Test]
		public void Decide_AWarmSea_HasNoIce_AndSaysWhy()
		{
			IceClimate warm = IceOccurrence.Decide(Year(12f), true, 0f, -10f);
			Assert.That(warm.AnyIce, Is.False);
			StringAssert.Contains("none", warm.NoIceReason);

			IceClimate polar = IceOccurrence.Decide(Year(-12f), true, 0f, -15f);
			Assert.That(polar.HasSeaIce && polar.HasBergs, Is.True);
			Assert.That(polar.LargestBerg, Is.EqualTo(IcebergSize.Large));

			IceClimate noSource = IceOccurrence.Decide(Year(-12f), false, 0f, 0f);
			Assert.That(noSource.HasSeaIce, Is.True);
			Assert.That(noSource.HasBergs, Is.False, "sea ice forms in place; bergs need a glacier");
		}

		[Test]
		public void Footing_FloatsGroundsOrRefuses()
		{
			var o = new IcePlacerOptions();
			Assert.That(IcePlacer.Footing(10f, 50f, 3f, o, out float rise), Is.EqualTo(IceFooting.Floats));
			Assert.That(rise, Is.EqualTo(0f));
			Assert.That(IcePlacer.Footing(1f, 50f, 3f, o, out rise), Is.EqualTo(IceFooting.Grounded), "touches at low tide");
			Assert.That(rise, Is.EqualTo(0f));
			Assert.That(IcePlacer.Footing(-4f, 50f, 3f, o, out rise), Is.EqualTo(IceFooting.Grounded));
			Assert.That(rise, Is.EqualTo(4f), "sits on the bed, 4 m above its float line");
			Assert.That(IcePlacer.Footing(-6f, 50f, 3f, o, out _), Is.EqualTo(IceFooting.Rejected), "would stand more than a tenth of its draught out of the water");
		}

		// ── The scene ─────────────────────────────────────────────────

		private const float LowTide = 3f;

		/// <summary>A synthetic shore field: land west of x = −600, the bed falling 0.5 m per metre east of it to 400 m.</summary>
		private static WaterShoreField.Snapshot Coast(Func<float, float> depthOfX = null)
		{
			depthOfX ??= x => Mathf.Clamp((x + 600f) * 0.5f, -50f, 400f);
			const int resolution = 220;
			var area = new Rect(-1100f, -1100f, 2200f, 2200f);
			float texel = area.width / resolution;
			var halves = new ushort[resolution * resolution * 2];
			for (int y = 0; y < resolution; y++)
			{
				for (int x = 0; x < resolution; x++)
				{
					float wx = area.xMin + (x + 0.5f) * texel;
					halves[(y * resolution + x) * 2] = Mathf.FloatToHalf(depthOfX(wx));
					halves[(y * resolution + x) * 2 + 1] = Mathf.FloatToHalf(Mathf.Clamp(wx + 600f, -2000f, 2000f));
				}
			}
			return new WaterShoreField.Snapshot(halves, resolution, area, texel, 1);
		}

		private static IceSite Site => new IceSite { Area = new Rect(-1000f, -1000f, 2000f, 2000f), SeaLevelY = 0f, LowTideMetres = LowTide };

		private static IcePlacerOptions Options => new IcePlacerOptions { MaxSeaIcePieces = 80 };

		private static IceClimate Polar => IceOccurrence.Decide(Year(-12f), true, 0f, -15f);

		/// <summary>Prefabs measured from the catalogue's shapes, as the art generator would build them.</summary>
		private sealed class FakePrefabs : IIcePrefabSource
		{
			public readonly Dictionary<string, Bounds> Measured = new Dictionary<string, Bounds>();
			public readonly HashSet<string> Missing = new HashSet<string>();
			public int Instantiated;

			public FakePrefabs()
			{
				foreach (IcebergShape s in IceMeshes.Icebergs)
				{
					float draught = s.Height * (s.Size <= IcebergSize.BergyBit ? 5f : 3f);
					Measured[IcePlacer.IcebergPrefab(s)] = new Bounds(new Vector3(0f, (s.Height - draught) * 0.5f, 0f),
						new Vector3(s.Length * 1.2f, s.Height + draught, s.Width * 1.2f));
				}
				foreach (SeaIceShape s in IceMeshes.SeaIce)
				{
					float draught = s.Thickness * 0.9f;
					Measured[IcePlacer.SeaIcePrefab(s)] = new Bounds(new Vector3(0f, (s.Thickness - 2f * draught) * 0.5f + s.RimHeight * 0.5f, 0f),
						new Vector3(s.Length, s.Thickness + s.RimHeight, s.Width));
				}
			}

			public bool TryMeasure(string prefabName, out Bounds localBounds)
			{
				localBounds = default;
				return !Missing.Contains(prefabName) && Measured.TryGetValue(prefabName, out localBounds);
			}

			public GameObject Instantiate(string prefabName, Transform parent)
			{
				var go = new GameObject(prefabName);
				go.transform.SetParent(parent, false);
				go.AddComponent<WaterFloater>();
				Instantiated++;
				return go;
			}
		}

		private static string PrefabOf(GameObject piece) => piece.name.Substring(0, piece.name.LastIndexOf(' '));

		private List<GameObject> Pieces()
		{
			var pieces = new List<GameObject>();
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				if (root.GetComponent<GeneratedSeaIce>() != null)
				{
					foreach (Transform child in root.transform) pieces.Add(child.gameObject);
				}
			}
			return pieces;
		}

		[Test]
		public void Place_OnAColdCoast_PutsEveryPieceClearOfTheBed_TheShore_TheEdge_AndEachOther()
		{
			var prefabs = new FakePrefabs();
			WaterShoreField.Snapshot field = Coast();
			IcePlacerOptions options = Options;
			IcePlacerReport report = IcePlacer.Place(scene, Site, Polar, field, 1234u, prefabs, options);
			TestContext.WriteLine(report.ToString());
			foreach (string note in report.Notes) TestContext.WriteLine(note);

			Assert.That(report.Bergs, Is.GreaterThan(0));
			Assert.That(report.SeaIce, Is.GreaterThan(0));
			List<GameObject> pieces = Pieces();
			Assert.That(pieces.Count, Is.EqualTo(report.Bergs + report.SeaIce));

			var discs = new List<(Vector2 c, float r, bool berg)>();
			foreach (GameObject piece in pieces)
			{
				string prefab = PrefabOf(piece);
				Bounds b = prefabs.Measured[prefab];
				bool berg = prefab.StartsWith("Iceberg_", StringComparison.Ordinal);
				Vector3 p = piece.transform.position;
				var c = new Vector2(p.x, p.z);
				Assert.That(IcePlacer.Footprint(field, b, c, piece.transform.eulerAngles.y, options, out float clearance, out float edge), Is.True);
				Assert.That(edge, Is.GreaterThanOrEqualTo((berg ? options.BergShoreGapMetres : options.SeaIceShoreGapMetres) - 0.5f),$"{piece.name} by the shore");
				WaterFloater floater = piece.GetComponent<WaterFloater>();
				float draught = -b.min.y;
				if (floater.Response > 0f)
				{
					Assert.That(clearance, Is.GreaterThanOrEqualTo(LowTide + options.HeaveAllowanceMetres + options.KeelMarginMetres - 0.01f),
						$"{piece.name} floats through the sea bed");
					Assert.That(p.y, Is.EqualTo(0f));
				}
				else
				{
					Assert.That(p.y, Is.EqualTo(Mathf.Max(0f, -clearance)).Within(0.01f), $"{piece.name} sits on the bed");
					Assert.That(p.y, Is.LessThanOrEqualTo(options.MaxGroundedRise * draught + 0.01f));
				}
				float r = new Vector2(Mathf.Max(-b.min.x, b.max.x), Mathf.Max(-b.min.z, b.max.z)).magnitude;
				Assert.That(Site.Area.xMin + options.EdgeMarginMetres + r, Is.LessThanOrEqualTo(c.x + 0.01f));
				Assert.That(Site.Area.xMax - options.EdgeMarginMetres - r, Is.GreaterThanOrEqualTo(c.x - 0.01f));
				Assert.That(Site.Area.yMin + options.EdgeMarginMetres + r, Is.LessThanOrEqualTo(c.y + 0.01f));
				Assert.That(Site.Area.yMax - options.EdgeMarginMetres - r, Is.GreaterThanOrEqualTo(c.y - 0.01f));
				foreach (var d in discs)
				{
					float gap = berg || d.berg ? options.BergGapMetres : options.SeaIceGapMetres;
					Assert.That(Vector2.Distance(c, d.c), Is.GreaterThanOrEqualTo(r + d.r + gap - 0.01f), $"{piece.name} crowds another piece");
				}
				discs.Add((c, r, berg));
			}
		}

		[Test]
		public void Place_OnAShallowShelf_GroundsOrRefusesDeepKeels_AndStillsWhatIsAground()
		{
			var prefabs = new FakePrefabs();
			// 20 m everywhere, no shore: a medium berg's keel is far deeper.
			WaterShoreField.Snapshot field = Coast(x => 20f);
			IcePlacerReport report = IcePlacer.Place(scene, Site, Polar, field, 77u, prefabs, Options);
			foreach (GameObject piece in Pieces())
			{
				Bounds b = prefabs.Measured[PrefabOf(piece)];
				float draught = -b.min.y;
				WaterFloater floater = piece.GetComponent<WaterFloater>();
				// Its keel never below the bed: a deeper keel than the water is lifted onto it.
				Assert.That(piece.transform.position.y - draught, Is.GreaterThanOrEqualTo(-20f - 0.01f), $"{piece.name}'s keel is in the sea bed");
				if (draught > 20f - LowTide - 1.5f)
				{
					Assert.That(floater.Response, Is.EqualTo(0f), $"{piece.name} touches at low tide, so it is aground");
				}
			}
			Assert.That(report.Placed.ContainsKey("Iceberg_TabularMedium"), Is.False);
		}

		private List<string> Signature()
		{
			var lines = new List<string>();
			foreach (GameObject piece in Pieces())
			{
				Transform t = piece.transform;
				lines.Add($"{piece.name} {t.position.x:0.000} {t.position.y:0.000} {t.position.z:0.000} {t.eulerAngles.y:0.000} {piece.GetComponent<WaterFloater>().Response}");
			}
			return lines;
		}

		[Test]
		public void Place_IsDeterministic_FromTheSeed()
		{
			WaterShoreField.Snapshot field = Coast();
			IcePlacer.Place(scene, Site, Polar, field, 99u, new FakePrefabs(), Options);
			List<string> first = Signature();
			IcePlacer.Place(scene, Site, Polar, field, 99u, new FakePrefabs(), Options);
			List<string> second = Signature();
			CollectionAssert.AreEqual(first, second);
			Assert.That(first.Count, Is.GreaterThan(0));

			IcePlacer.Place(scene, Site, Polar, field, 100u, new FakePrefabs(), Options);
			CollectionAssert.AreNotEqual(first, Signature(), "another scene's seed places other ice");
		}

		[Test]
		public void Place_ReplacesOnlyItsOwnRoot()
		{
			var stranger = new GameObject(IcePlacer.RootName);
			SceneManager.MoveGameObjectToScene(stranger, scene);
			WaterShoreField.Snapshot field = Coast();
			IcePlacerReport a = IcePlacer.Place(scene, Site, Polar, field, 5u, new FakePrefabs(), Options);
			IcePlacerReport b = IcePlacer.Place(scene, Site, Polar, field, 5u, new FakePrefabs(), Options);
			int marked = 0, unmarked = 0;
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				if (root.name != IcePlacer.RootName) continue;
				if (root.GetComponent<GeneratedSeaIce>() != null) marked++; else unmarked++;
			}
			Assert.That(marked, Is.EqualTo(1));
			Assert.That(unmarked, Is.EqualTo(1), "an unmarked object of the same name is not the placer's");
			Assert.That(b.Bergs + b.SeaIce, Is.EqualTo(a.Bergs + a.SeaIce));
			Assert.That(Pieces().Count, Is.EqualTo(b.Bergs + b.SeaIce));

			// A warm re-run clears it and leaves nothing.
			IcePlacer.Place(scene, Site, IceOccurrence.Decide(Year(15f), true, 0f, -10f), field, 5u, new FakePrefabs(), Options);
			Assert.That(Pieces().Count, Is.EqualTo(0));
			Assert.That(stranger != null, Is.True);
		}

		[Test]
		public void Place_SkipsAndNotesPrefabsThatWereNeverGenerated()
		{
			var prefabs = new FakePrefabs();
			foreach (IcebergShape s in IceMeshes.Icebergs) prefabs.Missing.Add(IcePlacer.IcebergPrefab(s));
			IcePlacerReport report = IcePlacer.Place(scene, Site, Polar, Coast(), 1234u, prefabs, Options);
			Assert.That(report.Bergs, Is.EqualTo(0));
			Assert.That(report.MissingAssets, Is.GreaterThan(0));
			Assert.That(report.SeaIce, Is.GreaterThan(0), "the sea ice that exists is still placed");
			foreach (GameObject piece in Pieces())
			{
				StringAssert.StartsWith("SeaIce_", piece.name);
			}
			Assert.That(report.Notes.Exists(n => n.Contains("not generated")), Is.True);
			Assert.That(prefabs.Instantiated, Is.EqualTo(report.SeaIce), "nothing is made for a missing prefab");
		}

		private GameObject MarkedRoot()
		{
			var root = new GameObject(IcePlacer.RootName);
			root.AddComponent<GeneratedSeaIce>();
			SceneManager.MoveGameObjectToScene(root, scene);
			return root;
		}

		private static SceneGenerationRequest Request => new SceneGenerationRequest { SceneName = "Ice Test", SizeKm = new Vector2(2f, 2f) };

		[Test]
		public void PlaceInOpenScene_WithNoSea_SkipsAndTouchesNothing()
		{
			GameObject old = MarkedRoot();
			IcePlacerReport report = IcePlacer.PlaceInOpenScene(scene, Request, 1u);
			Assert.That(report.Root, Is.Null);
			Assert.That(old != null, Is.True, "a skip leaves the scene as it was");
			Assert.That(report.Notes.Exists(n => n.Contains("no sea")), Is.True);
		}

		[Test]
		public void PlaceInOpenScene_OnLava_SkipsAndTouchesNothing()
		{
			GameObject old = MarkedRoot();
			var host = new GameObject("Lava");
			SceneManager.MoveGameObjectToScene(host, scene);
			host.SetActive(false); // no sea systems running in the test
			host.AddComponent<MeshFilter>();
			host.AddComponent<MeshRenderer>();
			host.AddComponent<WaterSurface>().Liquid = WaterLiquid.Lava;
			IcePlacerReport report = IcePlacer.PlaceInOpenScene(scene, Request, 1u);
			Assert.That(report.Root, Is.Null);
			Assert.That(old != null, Is.True);
			Assert.That(report.Notes.Exists(n => n.Contains("lava")), Is.True);
		}

		[Test]
		public void SceneSeed_IsTheGenerators()
		{
			var request = Request;
			System.Reflection.MethodInfo generator = typeof(SceneGenerator).GetMethod("SceneSeed",
				System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
			Assert.That(generator, Is.Not.Null, "SceneGenerator.SceneSeed moved; IcePlacer.SceneSeed must follow it");
			Assert.That(IcePlacer.SceneSeed(request), Is.EqualTo((uint)generator.Invoke(null, new object[] { request })));
		}

		[Test]
		public void PrefabNames_FollowTheArtGenerator()
		{
			Assert.That(IcePlacer.IcebergPrefab(IceMeshes.Icebergs[0]), Is.EqualTo("Iceberg_IrregularGrowler"));
			Assert.That(IcePlacer.SeaIcePrefab(IceMeshes.SeaIce[0]), Is.EqualTo("SeaIce_Pancake"));
		}
	}
}
