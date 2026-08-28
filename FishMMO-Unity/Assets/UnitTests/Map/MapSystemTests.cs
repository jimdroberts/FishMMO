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

		/// <summary>
		/// Removes anything the store tests wrote to disk.
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
			FogOfWarMap fog = new FogOfWarMap(new Rect(0.0f, 0.0f, 100.0f, 100.0f), 4.0f);

			Assert.AreEqual(0.0f, fog.ExploredFraction(), 1e-4f);
			Assert.IsFalse(fog.IsDiscovered(new Vector3(50.0f, 0.0f, 50.0f)));
		}

		[Test]
		public void Fog_GridCoversARectThatIsNotAWholeNumberOfCells()
		{
			/* Rounding down here leaves a stripe of the zone that no amount of walking can reveal,
			 * because there is no cell to write it into. */
			FogOfWarMap fog = new FogOfWarMap(new Rect(0.0f, 0.0f, 101.0f, 99.0f), 4.0f);

			Assert.AreEqual(26, fog.CellsX);
			Assert.AreEqual(25, fog.CellsZ);
			Assert.GreaterOrEqual(fog.CellsX * fog.CellSize, 101.0f);
			Assert.GreaterOrEqual(fog.CellsZ * fog.CellSize, 99.0f);
		}

		[Test]
		public void Fog_RevealClearsTheGroundUnderTheCharacter()
		{
			FogOfWarMap fog = new FogOfWarMap(new Rect(0.0f, 0.0f, 200.0f, 200.0f), 4.0f);

			Assert.IsTrue(fog.Reveal(new Vector3(100.0f, 0.0f, 100.0f), 20.0f));

			Assert.IsTrue(fog.IsDiscovered(new Vector3(100.0f, 0.0f, 100.0f)));
			Assert.IsFalse(fog.IsDiscovered(new Vector3(180.0f, 0.0f, 180.0f)), "Ground well outside the radius stays fogged.");
			Assert.IsTrue(fog.IsDirty);
		}

		[Test]
		public void Fog_RevealNeverPutsFogBack()
		{
			/* Monotonic reveal is what lets overlapping reveals be applied in any order. A smaller
			 * reveal that overwrote a larger one would un-explore ground the player had walked, and
			 * the failure would only show up after a specific path. */
			FogOfWarMap fog = new FogOfWarMap(new Rect(0.0f, 0.0f, 200.0f, 200.0f), 4.0f);
			Vector3 centre = new Vector3(100.0f, 0.0f, 100.0f);

			fog.Reveal(centre, 40.0f);
			float wide = fog.ExploredFraction();

			fog.Reveal(centre, 5.0f);

			Assert.AreEqual(wide, fog.ExploredFraction(), 1e-5f);
			Assert.IsTrue(fog.IsDiscovered(new Vector3(120.0f, 0.0f, 100.0f)));
		}

		[Test]
		public void Fog_RevealAll_ExploresEverything()
		{
			FogOfWarMap fog = new FogOfWarMap(new Rect(0.0f, 0.0f, 200.0f, 200.0f), 4.0f);

			fog.RevealAll();

			Assert.AreEqual(1.0f, fog.ExploredFraction(), 1e-4f);
		}

		[Test]
		public void Fog_OffMapPositionsReadAsExplored()
		{
			/* Off-map is not somewhere the player can discover, and reporting it as fogged would hide
			 * every marker just outside a scene's derived bounds — which, since those bounds are a
			 * boundary volume plus padding, includes things placed at the edge on purpose. */
			FogOfWarMap fog = new FogOfWarMap(new Rect(0.0f, 0.0f, 100.0f, 100.0f), 4.0f);

			Assert.IsTrue(fog.IsDiscovered(new Vector3(-500.0f, 0.0f, -500.0f)));
		}

		// ── Explored-map file ───────────────────────────────────────

		[Test]
		public void FogStore_RoundTripsAnExploredMap()
		{
			long characterID = Temporary(918273645);
			Rect rect = new Rect(-100.0f, -100.0f, 400.0f, 400.0f);

			FogOfWarMap written = new FogOfWarMap(rect, 4.0f);
			written.Reveal(new Vector3(0.0f, 0.0f, 0.0f), 30.0f);
			written.Reveal(new Vector3(80.0f, 0.0f, 20.0f), 25.0f);

			Assert.IsTrue(FogOfWarStore.Save(characterID, "TestScene", written));
			Assert.IsFalse(written.IsDirty, "A successful save clears the dirty flag.");

			FogOfWarMap read = FogOfWarStore.Load(characterID, "TestScene", rect, 4.0f);

			Assert.IsNotNull(read);
			CollectionAssert.AreEqual(written.Cells, read.Cells);
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

			FogOfWarMap written = new FogOfWarMap(rect, 4.0f);
			written.Reveal(new Vector3(100.0f, 0.0f, 100.0f), 20.0f);
			FogOfWarStore.Save(characterID, "TestScene", written);

			string path = FogOfWarStore.FilePath(characterID, "TestScene");
			byte[] bytes = File.ReadAllBytes(path);
			bytes[bytes.Length / 2] ^= 0xFF;
			File.WriteAllBytes(path, bytes);

			/* The store logs a warning for a file that fails its signature check, which is the
			 * point of it — but an unexpected log entry fails a Unity test by default. */
			LogAssert.ignoreFailingMessages = true;
			FogOfWarMap read = FogOfWarStore.Load(characterID, "TestScene", rect, 4.0f);
			LogAssert.ignoreFailingMessages = false;

			Assert.IsNull(read, "A file whose contents no longer match its signature must be discarded.");
		}

		[Test]
		public void FogStore_RejectsAnotherCharactersFile()
		{
			long owner = Temporary(918273647);
			long thief = Temporary(918273648);
			Rect rect = new Rect(0.0f, 0.0f, 200.0f, 200.0f);

			FogOfWarMap written = new FogOfWarMap(rect, 4.0f);
			written.RevealAll();
			FogOfWarStore.Save(owner, "TestScene", written);

			// Copy the owner's file into the other character's folder, as a player might.
			string source = FogOfWarStore.FilePath(owner, "TestScene");
			string destination = FogOfWarStore.FilePath(thief, "TestScene");
			Directory.CreateDirectory(Path.GetDirectoryName(destination));
			File.Copy(source, destination, true);

			LogAssert.ignoreFailingMessages = true;
			FogOfWarMap read = FogOfWarStore.Load(thief, "TestScene", rect, 4.0f);
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

			FogOfWarMap written = new FogOfWarMap(new Rect(0.0f, 0.0f, 200.0f, 200.0f), 4.0f);
			written.RevealAll();
			FogOfWarStore.Save(characterID, "TestScene", written);

			FogOfWarMap read = FogOfWarStore.Load(characterID, "TestScene", new Rect(0.0f, 0.0f, 400.0f, 400.0f), 4.0f);

			Assert.IsNull(read);
		}

		[Test]
		public void FogStore_MissingFileIsNotAnError()
		{
			FogOfWarMap read = FogOfWarStore.Load(Temporary(918273650), "NeverVisited",
				new Rect(0.0f, 0.0f, 100.0f, 100.0f), 4.0f);

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

		[Test]
		public void Cartography_RevealRadiusStaysInsideTheMinimapView()
		{
			/* A reveal radius larger than the minimap's own view would make the mechanic invisible:
			 * the player would never see unexplored ground on the map they are looking at. */
			for (int tier = 0; tier <= Cartography.MaximumDetailTier; ++tier)
			{
				Cartography.SetProvider(new StubCartography(tier));
				Assert.Less(Cartography.RevealRadius, 60.0f, $"tier {tier}");
				Assert.Greater(Cartography.RevealRadius, 0.0f, $"tier {tier}");
			}

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
