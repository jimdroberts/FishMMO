using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using FishMMO.Client;
using FishMMO.Shared;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Covers the parts of the map system that are pure arithmetic or pure file format, where a
	/// mistake is invisible in a screenshot but wrong in a way that compounds.
	/// </summary>
	/// <remarks>
	/// Three things are worth testing here and the rest is not. The world-to-view mapping decides
	/// where every marker on both maps is drawn, and an error in it is a few pixels at the centre
	/// and metres at the edge — exactly the shape of bug that looks fine until somebody follows a
	/// marker. The fog grid has to be monotonic and has to cover ground that is not a whole number
	/// of cells, or a strip of the world can never be explored. And the explored-map file is signed:
	/// if the signature check does not actually reject an edited file, the check is decoration.
	/// </remarks>
	[TestFixture]
	public class MapSystemTests
	{
		/// <summary>Characters used by the store tests, cleaned up afterwards.</summary>
		private readonly List<long> temporaryCharacters = new List<long>();

		/// <summary>Marker fixtures built by <see cref="NewMarker"/>, torn down afterwards.</summary>
		private readonly List<GameObject> markerHosts = new List<GameObject>();

		/// <summary>
		/// Removes anything the store tests wrote to disk, and the marker fixtures.
		/// </summary>
		/// <remarks>
		/// The stores write under the install directory, which in the editor is the folder above the
		/// Unity project. Leaving files there would make a later run of the same test read the
		/// previous run's data and pass for the wrong reason.
		/// </remarks>
		[TearDown]
		public void TearDown()
		{
			for (int i = 0; i < temporaryCharacters.Count; ++i)
			{
				FogOfWarStore.DeleteAll(temporaryCharacters[i]);
			}
			temporaryCharacters.Clear();

			for (int i = 0; i < markerHosts.Count; ++i)
			{
				if (markerHosts[i] == null)
				{
					continue;
				}

				MapMarker marker = markerHosts[i].GetComponent<MapMarker>();
				if (marker != null)
				{
					MapMarkerRegistry.Unregister(marker);
				}

				Object.DestroyImmediate(markerHosts[i]);
			}
			markerHosts.Clear();
		}

		/// <summary>
		/// Builds a marker fixture that the filter can actually see.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <see cref="MapMarker"/> joins <see cref="MapMarkerRegistry"/> from <c>OnEnable</c>, and
		/// edit mode does not run <c>OnEnable</c> on a component that is not <c>[ExecuteAlways]</c>.
		/// A fixture built with <c>AddComponent</c> alone is therefore not in the registry, and
		/// <see cref="MapMarkerFilter"/> collects from the registry — so the filter returns nothing
		/// for it and every assertion about what it produced is answered by an empty list.
		/// </para>
		/// <para>
		/// That is worth a helper rather than a line in each test, because the failure is not
		/// symmetric: a test asserting a marker is <b>absent</b> passes whether the rule works or
		/// the fixture was simply never registered.
		/// </para>
		/// </remarks>
		/// <param name="name">Name for the host object, used in failure messages.</param>
		/// <returns>A registered marker.</returns>
		private MapMarker NewMarker(string name)
		{
			GameObject host = new GameObject(name);
			markerHosts.Add(host);

			MapMarker marker = host.AddComponent<MapMarker>();
			MapMarkerRegistry.Register(marker);
			return marker;
		}

		/// <summary>
		/// Registers a character ID whose files should be deleted after the test.
		/// </summary>
		/// <param name="characterID">The character ID.</param>
		/// <returns>The same ID, for use inline.</returns>
		private long Temporary(long characterID)
		{
			temporaryCharacters.Add(characterID);
			return characterID;
		}

		// ── MapViewTransform ────────────────────────────────────────

		[Test]
		public void WorldToView_CentreOfView_IsHalfHalf()
		{
			MapViewTransform view = new MapViewTransform(new Vector3(100.0f, 5.0f, -40.0f), 25.0f, 0.0f);

			Vector2 result = view.WorldToView(new Vector3(100.0f, 999.0f, -40.0f));

			Assert.AreEqual(0.5f, result.x, 1e-4f);
			Assert.AreEqual(0.5f, result.y, 1e-4f);
		}

		[Test]
		public void WorldToView_NorthOfCentre_IsAboveCentre()
		{
			MapViewTransform view = new MapViewTransform(Vector3.zero, 50.0f, 0.0f);

			// +Z is north, and an unrotated map puts north at the top, which is view Y of 1.
			Vector2 result = view.WorldToView(new Vector3(0.0f, 0.0f, 50.0f));

			Assert.AreEqual(0.5f, result.x, 1e-4f);
			Assert.AreEqual(1.0f, result.y, 1e-4f);
		}

		[Test]
		public void WorldToView_RotatedNinetyDegrees_PutsEastAtTheTop()
		{
			/* Facing east, a rotating map turns so that what is in front of the character is at the
			 * top. Getting the sign of this wrong produces a map that turns the correct amount in
			 * the wrong direction, which is the single most disorienting thing a minimap can do and
			 * looks perfectly fine in a screenshot. */
			MapViewTransform view = new MapViewTransform(Vector3.zero, 50.0f, 90.0f);

			Vector2 result = view.WorldToView(new Vector3(50.0f, 0.0f, 0.0f));

			Assert.AreEqual(0.5f, result.x, 1e-4f);
			Assert.AreEqual(1.0f, result.y, 1e-4f);
		}

		[Test]
		public void ViewToWorld_RoundTripsThroughWorldToView_AtEveryRotation()
		{
			Vector3[] samples =
			{
				new Vector3(12.0f, 0.0f, -37.0f),
				new Vector3(-140.0f, 0.0f, 88.0f),
				new Vector3(0.0f, 0.0f, 0.0f),
			};

			foreach (float rotation in new[] { 0.0f, 37.0f, 90.0f, 180.0f, 271.5f })
			{
				MapViewTransform view = new MapViewTransform(new Vector3(10.0f, 0.0f, 10.0f), 200.0f, rotation);

				foreach (Vector3 sample in samples)
				{
					Vector3 result = view.ViewToWorld(view.WorldToView(sample));

					Assert.AreEqual(sample.x, result.x, 1e-2f, $"x at rotation {rotation}");
					Assert.AreEqual(sample.z, result.z, 1e-2f, $"z at rotation {rotation}");
				}
			}
		}

		[Test]
		public void MapViewTransform_ZeroRange_IsClampedRatherThanDividingByZero()
		{
			MapViewTransform view = new MapViewTransform(Vector3.zero, 0.0f, 0.0f);

			Vector2 result = view.WorldToView(new Vector3(1.0f, 0.0f, 1.0f));

			Assert.IsFalse(float.IsNaN(result.x), "A zero range must not produce NaN coordinates.");
			Assert.IsFalse(float.IsInfinity(result.y), "A zero range must not produce infinite coordinates.");
		}

		// ── MapBoundsResolver ───────────────────────────────────────

		[Test]
		public void FromSceneBoundaries_UnionsEveryBoundary()
		{
			WorldSceneDetails details = new WorldSceneDetails();
			details.Boundaries.Add("a", new SceneBoundaryDetails()
			{
				BoundaryOrigin = new Vector3(0.0f, 0.0f, 0.0f),
				BoundarySize = new Vector3(100.0f, 50.0f, 100.0f),
			});
			details.Boundaries.Add("b", new SceneBoundaryDetails()
			{
				BoundaryOrigin = new Vector3(200.0f, 0.0f, 0.0f),
				BoundarySize = new Vector3(100.0f, 50.0f, 100.0f),
			});

			Rect result = MapBoundsResolver.FromSceneBoundaries(details);

			Assert.AreEqual(-50.0f, result.xMin, 1e-3f);
			Assert.AreEqual(250.0f, result.xMax, 1e-3f);
			Assert.AreEqual(-50.0f, result.yMin, 1e-3f);
			Assert.AreEqual(50.0f, result.yMax, 1e-3f);
		}

		[Test]
		public void FromSceneBoundaries_NegativeExtent_DescribesTheSameBoxAsItsPositiveTwin()
		{
			/* Unity does not stop anyone typing a negative size, and taking the signed value straight
			 * through produces a rect whose min exceeds its max — after which every containment and
			 * normalisation downstream quietly answers false for the whole scene. */
			WorldSceneDetails details = new WorldSceneDetails();
			details.Boundaries.Add("a", new SceneBoundaryDetails()
			{
				BoundaryOrigin = Vector3.zero,
				BoundarySize = new Vector3(-100.0f, 50.0f, -100.0f),
			});

			Rect result = MapBoundsResolver.FromSceneBoundaries(details);

			Assert.AreEqual(100.0f, result.width, 1e-3f);
			Assert.AreEqual(100.0f, result.height, 1e-3f);
			Assert.Less(result.xMin, result.xMax);
		}

		[Test]
		public void FromSceneBoundaries_NoBoundaries_IsEmpty()
		{
			Assert.AreEqual(Rect.zero, MapBoundsResolver.FromSceneBoundaries(new WorldSceneDetails()));
			Assert.AreEqual(Rect.zero, MapBoundsResolver.FromSceneBoundaries(null));
		}

		[Test]
		public void WorldToNormalized_RoundTrips()
		{
			Rect rect = new Rect(-500.0f, -300.0f, 1000.0f, 600.0f);
			Vector3 sample = new Vector3(123.0f, 0.0f, -77.0f);

			Vector2 normalized = MapBoundsResolver.WorldToNormalized(rect, sample);
			Vector3 result = MapBoundsResolver.NormalizedToWorld(rect, normalized);

			Assert.AreEqual(sample.x, result.x, 1e-2f);
			Assert.AreEqual(sample.z, result.z, 1e-2f);
		}

		// ── Region labels ───────────────────────────────────────────

		[Test]
		public void RegionContains_IgnoresHeight()
		{
			MapRegionLabelDetails region = new MapRegionLabelDetails()
			{
				Name = "Valley",
				Position = Vector3.zero,
				Radius = 50.0f,
			};

			// A player on a tower inside the valley is still in the valley.
			Assert.IsTrue(region.Contains(new Vector3(10.0f, 400.0f, 10.0f)));
			Assert.IsFalse(region.Contains(new Vector3(100.0f, 0.0f, 0.0f)));
		}

		[Test]
		public void FindRegion_PrefersTheSmallestContainingRegion()
		{
			WorldMapDefinition definition = ScriptableObject.CreateInstance<WorldMapDefinition>();
			try
			{
				definition.RegionLabels.Add(new MapRegionLabelDetails() { Name = "Province", Position = Vector3.zero, Radius = 500.0f });
				definition.RegionLabels.Add(new MapRegionLabelDetails() { Name = "City", Position = Vector3.zero, Radius = 60.0f });
				definition.RegionLabels.Add(new MapRegionLabelDetails() { Name = "Elsewhere", Position = new Vector3(2000.0f, 0.0f, 0.0f), Radius = 40.0f });

				Assert.AreEqual("City", definition.FindRegion(new Vector3(10.0f, 0.0f, 10.0f)).Name);
				Assert.AreEqual("Province", definition.FindRegion(new Vector3(300.0f, 0.0f, 0.0f)).Name);
				Assert.IsNull(definition.FindRegion(new Vector3(9000.0f, 0.0f, 0.0f)));
			}
			finally
			{
				Object.DestroyImmediate(definition);
			}
		}

		[Test]
		public void DerivedBounds_AreNotMistakenForAuthoredOnes()
		{
			/* The rebuild writes derived bounds into the same fields the inspector edits. Without the
			 * derived flag the next rebuild would see a non-empty size, conclude somebody had typed
			 * it, and never derive again — freezing every scene's map at whatever its boundaries
			 * happened to be the first time the cache was built. */
			WorldMapDefinition definition = ScriptableObject.CreateInstance<WorldMapDefinition>();
			try
			{
				Assert.IsFalse(definition.HasBounds);

				definition.SetDerivedBounds(new Rect(-100.0f, -100.0f, 200.0f, 200.0f));

				Assert.IsTrue(definition.HasBounds, "Derived bounds are usable at runtime.");
				Assert.IsFalse(definition.HasAuthoredBounds, "Derived bounds must stay re-derivable.");
				Assert.AreEqual(200.0f, definition.MapRect.width, 1e-3f);
			}
			finally
			{
				Object.DestroyImmediate(definition);
			}
		}

		// ── Fog of war ──────────────────────────────────────────────

		[Test]
		public void Fog_StartsCompletelyUnexplored()
		{
			FogOfWarMap fog = new FogOfWarMap(new Rect(0.0f, 0.0f, 100.0f, 100.0f), 50.0f);

			Assert.AreEqual(0.0f, fog.ExploredFraction(), 1e-4f);
			Assert.AreEqual(0, fog.ExploredChunkCount);
			Assert.IsFalse(fog.IsDiscovered(new Vector3(50.0f, 0.0f, 50.0f)));
		}

		[Test]
		public void Fog_GridCoversARectThatIsNotAWholeNumberOfChunks()
		{
			/* Rounding down here leaves a stripe of the zone that no amount of walking can reveal,
			 * because there is no chunk to write it into. */
			FogOfWarMap fog = new FogOfWarMap(new Rect(0.0f, 0.0f, 101.0f, 99.0f), 25.0f);

			Assert.AreEqual(5, fog.ChunksX);
			Assert.AreEqual(4, fog.ChunksZ);
			Assert.GreaterOrEqual(fog.ChunksX * fog.ChunkSize, 101.0f);
			Assert.GreaterOrEqual(fog.ChunksZ * fog.ChunkSize, 99.0f);
		}

		[Test]
		public void Fog_GridRectCoversTheSceneAndIsWhatTheTextureSpans()
		{
			/* The fog texture is one texel per chunk, so whatever rectangle the drawing code says
			 * that texture covers is the rectangle it gets stretched across. Handing it the scene's
			 * rectangle instead of the grid's squeezes nine chunks into the space of 8.6 and walks
			 * the fog out of alignment with the ground — a third of a chunk by the far edge at the
			 * shipped sizes. See UITKMapView.OnGenerateFogContent. */
			Rect scene = new Rect(-540.96f, -540.96f, 1105.92f, 1105.92f);
			FogOfWarMap fog = new FogOfWarMap(scene, 128.0f);

			Assert.AreEqual(9, fog.ChunksX);
			Assert.AreEqual(scene.xMin, fog.GridRect.xMin, 1e-3f, "the grid starts where the scene does");
			Assert.AreEqual(scene.yMin, fog.GridRect.yMin, 1e-3f);
			Assert.AreEqual(fog.ChunksX * fog.ChunkSize, fog.GridRect.width, 1e-3f, "and spans whole chunks");
			Assert.AreEqual(fog.ChunksZ * fog.ChunkSize, fog.GridRect.height, 1e-3f);
			Assert.GreaterOrEqual(fog.GridRect.width, scene.width, "which must cover the scene, never crop it");
			Assert.GreaterOrEqual(fog.GridRect.height, scene.height);

			/* And the two really do differ at the shipped sizes — if they ever stop differing this
			 * test is no longer proving anything and the drawing code's choice stops mattering. */
			Assert.Greater(fog.GridRect.width - scene.width, 1.0f, "the overhang is what makes the distinction matter");
		}

		[Test]
		public void Fog_EnteringAChunkExploresTheWholeOfIt()
		{
			/* The whole point of the chunk model: one step inside the boundary opens the entire
			 * block, so the map gains a readable piece of ground rather than a disc around the
			 * character. */
			FogOfWarMap fog = new FogOfWarMap(new Rect(0.0f, 0.0f, 200.0f, 200.0f), 50.0f);

			Assert.IsTrue(fog.Reveal(new Vector3(51.0f, 0.0f, 51.0f)), "stepping into a new chunk explores it");

			Assert.IsTrue(fog.IsDiscovered(new Vector3(51.0f, 0.0f, 51.0f)));
			Assert.IsTrue(fog.IsDiscovered(new Vector3(99.0f, 0.0f, 99.0f)), "the far corner of the same chunk is explored too");
			Assert.IsFalse(fog.IsDiscovered(new Vector3(101.0f, 0.0f, 101.0f)), "the neighbouring chunk is not");
			Assert.IsTrue(fog.IsDirty);
			Assert.AreEqual(1, fog.ExploredChunkCount);
		}

		[Test]
		public void Fog_ReenteringAnExploredChunkChangesNothing()
		{
			/* Reveal is called four times a second for as long as the client runs. Reporting a
			 * change every time would dirty the file forever and rewrite it on a loop. */
			FogOfWarMap fog = new FogOfWarMap(new Rect(0.0f, 0.0f, 200.0f, 200.0f), 50.0f);
			Vector3 spot = new Vector3(51.0f, 0.0f, 51.0f);

			Assert.IsTrue(fog.Reveal(spot));
			fog.ClearDirty();

			Assert.IsFalse(fog.Reveal(spot), "the same chunk again is not a change");
			Assert.IsFalse(fog.Reveal(spot + new Vector3(10.0f, 0.0f, 10.0f)), "nor is a step within it");
			Assert.IsFalse(fog.IsDirty);
			Assert.AreEqual(1, fog.ExploredChunkCount);
		}

		[Test]
		public void Fog_ExploredFractionIsChunksOverChunks()
		{
			/* The readout's whole reason for changing: with sixteen chunks, entering one is a flat
			 * one sixteenth. Nothing to average, nothing to threshold. */
			FogOfWarMap fog = new FogOfWarMap(new Rect(0.0f, 0.0f, 200.0f, 200.0f), 50.0f);

			Assert.AreEqual(16, fog.ChunkCount);

			fog.Reveal(new Vector3(25.0f, 0.0f, 25.0f));
			Assert.AreEqual(1.0f / 16.0f, fog.ExploredFraction(), 1e-5f);

			fog.Reveal(new Vector3(75.0f, 0.0f, 25.0f));
			Assert.AreEqual(2.0f / 16.0f, fog.ExploredFraction(), 1e-5f);
		}

		[Test]
		public void Fog_RevealAll_ExploresEverything()
		{
			FogOfWarMap fog = new FogOfWarMap(new Rect(0.0f, 0.0f, 200.0f, 200.0f), 50.0f);

			fog.RevealAll();

			Assert.AreEqual(1.0f, fog.ExploredFraction(), 1e-4f);
			Assert.AreEqual(fog.ChunkCount, fog.ExploredChunkCount);
		}

		// ── Exploration API (map items, triggers, quest rewards) ─────

		[Test]
		public void Fog_RevealChunk_TakesGridCoordinates()
		{
			FogOfWarMap fog = new FogOfWarMap(new Rect(-100.0f, -100.0f, 200.0f, 200.0f), 50.0f);

			Assert.IsTrue(fog.RevealChunk(0, 0));
			Assert.IsTrue(fog.IsDiscovered(new Vector3(-99.0f, 0.0f, -99.0f)), "chunk 0,0 is the rect's minimum corner");
			Assert.IsFalse(fog.RevealChunk(0, 0), "already explored");
			Assert.AreEqual(1, fog.ExploredChunkCount);
		}

		[Test]
		public void Fog_RevealChunk_IgnoresCoordinatesOffTheGrid()
		{
			/* Content authored against grid coordinates outlives the bounds it was authored for:
			 * a designer moving a boundary volume resizes the grid under it. Inert beats fatal. */
			FogOfWarMap fog = new FogOfWarMap(new Rect(0.0f, 0.0f, 200.0f, 200.0f), 50.0f);

			Assert.IsFalse(fog.RevealChunk(-1, 0));
			Assert.IsFalse(fog.RevealChunk(0, 99));
			Assert.AreEqual(0, fog.ExploredChunkCount);
			Assert.IsFalse(fog.IsDirty);
		}

		[Test]
		public void Fog_RevealArea_ExploresEveryChunkItTouches()
		{
			FogOfWarMap fog = new FogOfWarMap(new Rect(0.0f, 0.0f, 200.0f, 200.0f), 50.0f);

			/* Clips the corners of four chunks and covers none of them fully. A chunk is the
			 * smallest thing the map can describe, so all four are explored. */
			int revealed = fog.RevealArea(new Rect(45.0f, 45.0f, 10.0f, 10.0f));

			Assert.AreEqual(4, revealed);
			Assert.AreEqual(4, fog.ExploredChunkCount);
			Assert.IsTrue(fog.IsDiscovered(new Vector3(25.0f, 0.0f, 25.0f)));
			Assert.IsTrue(fog.IsDiscovered(new Vector3(75.0f, 0.0f, 75.0f)));
			Assert.IsFalse(fog.IsDiscovered(new Vector3(125.0f, 0.0f, 125.0f)));
		}

		[Test]
		public void Fog_RevealArea_ClampsToTheGridAndCountsOnlyNewChunks()
		{
			FogOfWarMap fog = new FogOfWarMap(new Rect(0.0f, 0.0f, 200.0f, 200.0f), 50.0f);

			Assert.AreEqual(16, fog.RevealArea(new Rect(-5000.0f, -5000.0f, 10000.0f, 10000.0f)),
				"an area larger than the scene explores the scene, not an index out of range");
			Assert.AreEqual(0, fog.RevealArea(new Rect(0.0f, 0.0f, 200.0f, 200.0f)),
				"a second pass explores nothing new, so nothing is written again");
		}

		[Test]
		public void Fog_RevealAround_IsADiscRoundedOutToTheGrid()
		{
			/* The shape a map consumable wants. The chunk diagonally opposite the centre is outside
			 * a sixty-metre circle even though its bounding box is not, which is what separates
			 * this from RevealArea. */
			FogOfWarMap fog = new FogOfWarMap(new Rect(0.0f, 0.0f, 400.0f, 400.0f), 50.0f);

			fog.RevealAround(new Vector3(25.0f, 0.0f, 25.0f), 60.0f);

			Assert.IsTrue(fog.IsDiscovered(new Vector3(25.0f, 0.0f, 25.0f)), "the chunk holding the centre");
			Assert.IsTrue(fog.IsDiscovered(new Vector3(75.0f, 0.0f, 25.0f)), "and the one beside it");
			Assert.IsFalse(fog.IsDiscovered(new Vector3(175.0f, 0.0f, 175.0f)), "but nothing beyond the radius");
			Assert.AreEqual(0, fog.RevealAround(new Vector3(25.0f, 0.0f, 25.0f), 0.0f), "a zero radius explores nothing");
		}

		[Test]
		public void Fog_OffMapPositionsReadAsExplored()
		{
			/* Off-map is not somewhere the player can discover, and reporting it as fogged would hide
			 * every marker just outside a scene's derived bounds — which, since those bounds are a
			 * boundary volume plus padding, includes things placed at the edge on purpose. */
			FogOfWarMap fog = new FogOfWarMap(new Rect(0.0f, 0.0f, 100.0f, 100.0f), 50.0f);

			Assert.IsTrue(fog.IsDiscovered(new Vector3(-500.0f, 0.0f, -500.0f)));
		}

		// ── Explored-map file ───────────────────────────────────────

		[Test]
		public void FogStore_RoundTripsAnExploredMap()
		{
			long characterID = Temporary(918273645);
			Rect rect = new Rect(-100.0f, -100.0f, 400.0f, 400.0f);

			FogOfWarMap written = new FogOfWarMap(rect, 50.0f);
			written.Reveal(new Vector3(0.0f, 0.0f, 0.0f));
			written.Reveal(new Vector3(80.0f, 0.0f, 20.0f));

			Assert.IsTrue(FogOfWarStore.Save(characterID, "TestScene", written));
			Assert.IsFalse(written.IsDirty, "A successful save clears the dirty flag.");

			FogOfWarMap read = FogOfWarStore.Load(characterID, "TestScene", rect, 50.0f);

			Assert.IsNotNull(read);
			CollectionAssert.AreEqual(written.Chunks, read.Chunks);
			Assert.AreEqual(written.ExploredChunkCount, read.ExploredChunkCount);
			Assert.AreEqual(written.ExploredFraction(), read.ExploredFraction(), 1e-5f);
		}

		[Test]
		public void FogStore_RejectsAnEditedFile()
		{
			/* The signature is tamper-EVIDENT, not tamper-proof — the key ships in the client. What
			 * this test protects is that the check is wired up at all: a verification that silently
			 * passes everything is worse than none, because the code reads as though something is
			 * being checked. */
			long characterID = Temporary(918273646);
			Rect rect = new Rect(0.0f, 0.0f, 200.0f, 200.0f);

			FogOfWarMap written = new FogOfWarMap(rect, 50.0f);
			written.Reveal(new Vector3(100.0f, 0.0f, 100.0f));
			FogOfWarStore.Save(characterID, "TestScene", written);

			string path = FogOfWarStore.FilePath(characterID, "TestScene");
			byte[] bytes = File.ReadAllBytes(path);
			bytes[bytes.Length / 2] ^= 0xFF;
			File.WriteAllBytes(path, bytes);

			/* The store logs a warning for a file that fails its signature check, which is the
			 * point of it — but an unexpected log entry fails a Unity test by default. */
			LogAssert.ignoreFailingMessages = true;
			FogOfWarMap read = FogOfWarStore.Load(characterID, "TestScene", rect, 50.0f);
			LogAssert.ignoreFailingMessages = false;

			Assert.IsNull(read, "A file whose contents no longer match its signature must be discarded.");
		}

		[Test]
		public void FogStore_RejectsAnotherCharactersFile()
		{
			long owner = Temporary(918273647);
			long thief = Temporary(918273648);
			Rect rect = new Rect(0.0f, 0.0f, 200.0f, 200.0f);

			FogOfWarMap written = new FogOfWarMap(rect, 50.0f);
			written.RevealAll();
			FogOfWarStore.Save(owner, "TestScene", written);

			// Copy the owner's file into the other character's folder, as a player might.
			string source = FogOfWarStore.FilePath(owner, "TestScene");
			string destination = FogOfWarStore.FilePath(thief, "TestScene");
			Directory.CreateDirectory(Path.GetDirectoryName(destination));
			File.Copy(source, destination, true);

			LogAssert.ignoreFailingMessages = true;
			FogOfWarMap read = FogOfWarStore.Load(thief, "TestScene", rect, 50.0f);
			LogAssert.ignoreFailingMessages = false;

			Assert.IsNull(read, "A map signed for one character must not load for another.");
		}

		[Test]
		public void FogStore_RejectsAMapRecordedAgainstDifferentBounds()
		{
			/* A scene's bounds move when a level designer moves a boundary volume. Reusing a grid
			 * that no longer lines up with the world would put the player's explored ground in the
			 * wrong place — worse than starting again, because it looks plausible. */
			long characterID = Temporary(918273649);

			FogOfWarMap written = new FogOfWarMap(new Rect(0.0f, 0.0f, 200.0f, 200.0f), 50.0f);
			written.RevealAll();
			FogOfWarStore.Save(characterID, "TestScene", written);

			FogOfWarMap read = FogOfWarStore.Load(characterID, "TestScene", new Rect(0.0f, 0.0f, 400.0f, 400.0f), 50.0f);

			Assert.IsNull(read);
		}

		[Test]
		public void FogStore_MissingFileIsNotAnError()
		{
			FogOfWarMap read = FogOfWarStore.Load(Temporary(918273650), "NeverVisited",
				new Rect(0.0f, 0.0f, 100.0f, 100.0f), 50.0f);

			Assert.IsNull(read);
		}

		// ── Notes ───────────────────────────────────────────────────

		[Test]
		public void NoteStore_RoundTripsNotesIncludingAwkwardText()
		{
			long characterID = Temporary(918273651);

			List<MapNote> written = new List<MapNote>();

			MapNote plain = new MapNote() { ID = 1, Position = new Vector3(12.5f, 3.0f, -40.25f), ColorIndex = 2 };
			plain.SetContent("Ore vein", "Three nodes behind the rock.");
			written.Add(plain);

			MapNote awkward = new MapNote() { ID = 2, Position = Vector3.zero, ColorIndex = 0, ShowOnMinimap = false };
			awkward.SetContent("Back \\ slash", "Line one\nLine two");
			written.Add(awkward);

			Assert.IsTrue(MapNoteStore.Save(characterID, "TestScene", written));

			List<MapNote> read = MapNoteStore.Load(characterID, "TestScene");

			Assert.AreEqual(2, read.Count);
			Assert.AreEqual("Ore vein", read[0].Title);
			Assert.AreEqual("Three nodes behind the rock.", read[0].Text);
			Assert.AreEqual(12.5f, read[0].Position.x, 1e-3f);
			Assert.AreEqual(-40.25f, read[0].Position.z, 1e-3f);
			Assert.AreEqual(2, read[0].ColorIndex);
			Assert.IsTrue(read[0].ShowOnMinimap);

			Assert.AreEqual("Back \\ slash", read[1].Title);
			Assert.AreEqual("Line one\nLine two", read[1].Text);
			Assert.IsFalse(read[1].ShowOnMinimap);
		}

		[Test]
		public void NoteStore_SavingNothingRemovesTheFile()
		{
			long characterID = Temporary(918273652);

			List<MapNote> notes = new List<MapNote>();
			MapNote note = new MapNote() { ID = 1 };
			note.SetContent("Temporary", string.Empty);
			notes.Add(note);

			MapNoteStore.Save(characterID, "TestScene", notes);
			Assert.IsTrue(File.Exists(MapNoteStore.FilePath(characterID, "TestScene")));

			MapNoteStore.Save(characterID, "TestScene", new List<MapNote>());

			Assert.IsFalse(File.Exists(MapNoteStore.FilePath(characterID, "TestScene")),
				"An empty note list should leave no file, not an empty one.");
		}

		[Test]
		public void Note_ContentIsClampedToTheLengthsTheStoreAccepts()
		{
			MapNote note = new MapNote();
			note.SetContent(new string('a', MapNote.MaximumTitleLength * 3), new string('b', MapNote.MaximumTextLength * 3));

			Assert.AreEqual(MapNote.MaximumTitleLength, note.Title.Length);
			Assert.AreEqual(MapNote.MaximumTextLength, note.Text.Length);
		}

		// ── Filters and palette ─────────────────────────────────────

		[Test]
		public void Filters_TheLocalPlayerIsNeverFilterable()
		{
			// A map that can hide where you are is a map with a way to break itself.
			foreach (MapFilterCategory category in MapFilters.Categories)
			{
				MapFilters.SetEnabled(category, false);
			}

			Assert.IsTrue(MapFilters.IsEnabled(MapMarkerType.Self));

			foreach (MapFilterCategory category in MapFilters.Categories)
			{
				MapFilters.SetEnabled(category, true);
			}
		}

		[Test]
		public void Filters_EveryMarkerTypeHasACategory()
		{
			/* A type added to the enum and forgotten here would fall into the default arm and be
			 * hidden by an unrelated checkbox, which is a bug nobody would connect to the new type. */
			foreach (MapMarkerType type in System.Enum.GetValues(typeof(MapMarkerType)))
			{
				if (type == MapMarkerType.Self)
				{
					continue;
				}

				MapFilterCategory category = MapFilters.Categorize(type);
				Assert.Contains(category, MapFilters.Categories, $"{type} maps to a category the legend does not list.");
			}
		}

		[Test]
		public void NoteColor_WrapsRatherThanFailingOnAHandEditedIndex()
		{
			// The index comes out of a text file the player can edit, and C# modulo can go negative.
			Assert.AreEqual(MapContent.NoteColors[0], MapContent.NoteColor(0));
			Assert.AreEqual(MapContent.NoteColors[1], MapContent.NoteColor(MapContent.NoteColors.Length + 1));
			Assert.AreEqual(MapContent.NoteColors[MapContent.NoteColors.Length - 1], MapContent.NoteColor(-1));
		}

		// ── Marker positions ────────────────────────────────────────

		[Test]
		public void Filter_ExactMarker_IsMarkedAsTrackingItsTransform()
		{
			/* The map must draw entities where they are on this frame, not where they were when the
			 * snapshot was collected a tenth of a second ago. The view honours that by re-reading the
			 * source transform, but only for markers the filter resolved exactly — so this asserts
			 * the flag that permits it. */
			MapMarker marker = NewMarker("Fixture");
			marker.Visibility = MapMarkerVisibility.Always;
			marker.Type = MapMarkerType.Vendor;
			marker.transform.position = new Vector3(7.0f, 0.0f, 3.0f);

			List<MapMarkerSnapshot> results = new List<MapMarkerSnapshot>();
			new MapMarkerFilter().Collect(results, null, false, null);

			MapMarkerSnapshot snapshot = FindSnapshot(results, marker);
			Assert.IsTrue(snapshot.TracksSource, "A world fixture is resolved exactly and may be re-read live.");
			Assert.AreEqual(7.0f, snapshot.Position.x, 1e-3f);
			Assert.AreEqual(3.0f, snapshot.Position.z, 1e-3f);
		}

		[Test]
		public void Filter_ThrottledMarker_PublishesACoarsePositionAndForbidsLiveReads()
		{
			/* The detection tier's entire value is that the exact position is never published. If
			 * TracksSource leaked true here the view would helpfully refresh the position straight
			 * off the transform every frame and undo the filter from the far side — the throttling
			 * would still be in the code and would no longer be in the picture. */
			MapMarker marker = NewMarker("Stranger");
			marker.Visibility = MapMarkerVisibility.Detection;

			// Inside the detection radius, and deliberately not on the quantisation grid.
			marker.transform.position = new Vector3(6.3f, 0.0f, -2.7f);

			List<MapMarkerSnapshot> results = new List<MapMarkerSnapshot>();
			MapMarkerFilter filter = new MapMarkerFilter();
			filter.Collect(results, null, false, null);

			MapMarkerSnapshot snapshot = FindSnapshot(results, marker);

			Assert.IsFalse(snapshot.TracksSource, "A throttled marker must never be re-read from its transform.");
			Assert.IsNull(snapshot.Label, "A throttled marker is never labelled.");

			float quantum = filter.PositionQuantum;
			Assert.AreEqual(0.0f, Mathf.Repeat(snapshot.Position.x, quantum), 1e-3f, "X is snapped to the grid.");
			Assert.AreEqual(0.0f, Mathf.Repeat(snapshot.Position.z, quantum), 1e-3f, "Z is snapped to the grid.");
			Assert.AreNotEqual(marker.transform.position.x, snapshot.Position.x);
		}

		[Test]
		public void Filter_ThrottledMarker_OutsideDetectionRadius_IsNotDrawnAtAll()
		{
			MapMarker marker = NewMarker("DistantStranger");
			marker.Visibility = MapMarkerVisibility.Detection;

			MapMarkerFilter filter = new MapMarkerFilter();
			marker.transform.position = new Vector3(filter.DetectionRadius * 3.0f, 0.0f, 0.0f);

			/* Prove the fixture is reachable before asserting it is absent. Without this the test
			 * passes just as happily against an empty registry, which is how it passed while the
			 * two beside it were failing for exactly that reason. */
			MapMarker control = NewMarker("Control");
			control.Visibility = MapMarkerVisibility.Always;

			List<MapMarkerSnapshot> results = new List<MapMarkerSnapshot>();
			filter.Collect(results, null, false, null);

			FindSnapshot(results, control);

			foreach (MapMarkerSnapshot snapshot in results)
			{
				Assert.AreNotSame(marker, snapshot.Source,
					"A stranger beyond the detection radius must not reach the map at all.");
			}
		}

		[Test]
		public void Filter_DetectionRadius_IsSmallerThanTheObserverFloor()
		{
			/* The guarantee is that the map is strictly less informative than the network stream it
			 * is drawn from. That only holds if the detection radius stays under the smallest radius
			 * the observer system will shrink a character's streaming range to under load. */
			Assert.Less(new MapMarkerFilter().DetectionRadius, ObserverStreamingPolicy.MinimumRange);
		}

		/// <summary>
		/// The snapshot produced for a particular marker.
		/// </summary>
		/// <param name="results">The collected snapshots.</param>
		/// <param name="marker">The marker to find.</param>
		/// <returns>Its snapshot.</returns>
		/// <remarks>
		/// The registry is static and shared, so a test asserts on its own marker rather than on
		/// whatever happens to be the only entry.
		/// </remarks>
		private static MapMarkerSnapshot FindSnapshot(List<MapMarkerSnapshot> results, MapMarker marker)
		{
			for (int i = 0; i < results.Count; ++i)
			{
				if (ReferenceEquals(results[i].Source, marker))
				{
					return results[i];
				}
			}

			Assert.Fail($"The filter produced no snapshot for '{marker.name}'.");
			return default;
		}

		// ── Cartography seam ────────────────────────────────────────

		[Test]
		public void Cartography_WithNoProvider_GivesTheFullMap()
		{
			/* The profession does not exist yet, so there is nothing to level and no way to earn a
			 * better map. Shipping a crippled one that unlocks against a system nobody can interact
			 * with would be a bug, not a preview. */
			Cartography.SetProvider(null);

			Assert.AreEqual(Cartography.MaximumDetailTier, Cartography.DetailTier);
			Assert.IsTrue(Cartography.ShowsCoordinates);
			Assert.AreEqual(1.0f, Cartography.MaximumWorldMapExtent, 1e-4f);
		}

		[Test]
		public void Cartography_ClampsAProviderThatAnswersOutOfRange()
		{
			Cartography.SetProvider(new StubCartography(-5));
			Assert.AreEqual(0, Cartography.DetailTier);

			Cartography.SetProvider(new StubCartography(Cartography.MaximumDetailTier + 50));
			Assert.AreEqual(Cartography.MaximumDetailTier, Cartography.DetailTier);

			Cartography.SetProvider(null);
		}

		/// <summary>A Cartography provider that answers with whatever it was told.</summary>
		private sealed class StubCartography : ICartographyProvider
		{
			/// <summary>The tier to report.</summary>
			private readonly int tier;

			/// <summary>Builds a provider reporting a fixed tier.</summary>
			/// <param name="value">The tier to report, which may be out of range.</param>
			public StubCartography(int value)
			{
				tier = value;
			}

			/// <inheritdoc />
			public int DetailTier => tier;
		}
	}
}
