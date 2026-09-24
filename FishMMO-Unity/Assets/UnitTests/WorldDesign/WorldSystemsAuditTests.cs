using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;
using FishMMO.Shared.WorldDesign;
using Object = UnityEngine.Object;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Whether a world scene is set up for the weather, cloud and sky systems — and, just as much,
	/// whether the audit knows the difference between a scene that opted out and one nobody
	/// finished.
	/// </summary>
	/// <remarks>
	/// The verdict is pure: facts in, findings out. It has to be, because it answers for scenes
	/// that have no settings component at all — which is the case it exists to find, and the one
	/// case where asking the running system is impossible.
	/// </remarks>
	[TestFixture]
	public class WorldSystemsAuditTests
	{
		private readonly List<Object> created = new List<Object>();

		[TearDown]
		public void TearDown()
		{
			foreach (Object o in created)
			{
				Object.DestroyImmediate(o);
			}
			created.Clear();
		}

		private T Make<T>(string name) where T : ScriptableObject
		{
			T asset = ScriptableObject.CreateInstance<T>();
			asset.name = name;
			created.Add(asset);
			return asset;
		}

		/// <summary>A scene that is set up: both components, a climate, a terrain big enough to storm over.</summary>
		private static SceneWorldFacts Finished(string name = "Meadow")
		{
			return new SceneWorldFacts
			{
				SceneName = name,
				HasSettings = true,
				HasDayNightCycle = true,
				DayNightEnabled = true,
				HasClimate = true,
				HasTerrain = true,
				DirectorAreaSquareKm = 16f,
			};
		}

		private WorldBody Earthlike(string name = "Home")
		{
			WorldBody body = Make<WorldBody>(name);
			body.Atmosphere = AtmosphereKind.Thick;
			body.Sky = Make<SkyProfile>(name + " Sky");
			body.BaseClimate = Make<FishMMO.Shared.Biomes.ClimateSettings>(name + " Climate");
			return body;
		}

		private WorldAtlasScene Placed(string sceneName, WorldBody body, bool underground = false)
		{
			WorldAtlasScene entry = Make<WorldAtlasScene>(sceneName);
			entry.SceneName = sceneName;
			entry.Placed = true;
			entry.Body = body;
			entry.Latitude = 12.0;
			entry.Longitude = 40.0;
			entry.SizeKm = new Vector2(4f, 4f);
			WorldAtlasLayer layer = Make<WorldAtlasLayer>(underground ? "Underworld" : "Overworld");
			layer.Underground = underground;
			layer.DefaultWeather = underground ? WeatherSceneMode.None : WeatherSceneMode.Auto;
			entry.Layer = layer;
			return entry;
		}

		private static WorldSystemProblem Find(List<WorldSystemProblem> problems, string id)
		{
			foreach (WorldSystemProblem problem in problems)
			{
				if (problem.Id == id)
				{
					return problem;
				}
			}
			return null;
		}

		private static int CountAtLeast(List<WorldSystemProblem> problems, WorldSystemSeverity severity)
		{
			int count = 0;
			foreach (WorldSystemProblem problem in problems)
			{
				if (problem.Severity <= severity)
				{
					count++;
				}
			}
			return count;
		}

		[Test]
		public void AFinishedSurfaceSceneHasNothingWrongWithIt()
		{
			/* The baseline, and the one that keeps the audit honest. A check that fires on a scene
			 * that is genuinely finished trains everyone to ignore the whole report. */
			WorldBody body = Earthlike();
			List<WorldSystemProblem> problems = WorldSystemsAudit.Problems(
				Finished(), Placed("Meadow", body), body, isDungeon: false, new Vector2(4f, 4f));

			Assert.That(CountAtLeast(problems, WorldSystemSeverity.Warning), Is.Zero,
				"a finished scene must raise no errors and no warnings: " + string.Join(" | ", problems));
		}

		[Test]
		public void ASceneWithNoSettingsIsAnErrorBecauseItGetsNoWeatherAtAll()
		{
			/* WeatherHost.TryAddScene returns immediately when a scene has no WorldSceneSettings —
			 * no log, no error, because a login screen wants exactly that. So the scene has no
			 * weather, no storm cells and no world clock, and nothing anywhere says so. */
			WorldBody body = Earthlike();
			SceneWorldFacts facts = Finished();
			facts.HasSettings = false;

			WorldSystemProblem problem = Find(
				WorldSystemsAudit.Problems(facts, Placed("Meadow", body), body, false, null),
				WorldSystemCheck.MissingSettings);

			Assert.That(problem, Is.Not.Null, "a scene with no settings must be reported");
			Assert.That(problem.Severity, Is.EqualTo(WorldSystemSeverity.Error));
			Assert.That(problem.CanFix, Is.True, "adding the component is exactly what the audit is for");
			Assert.That(problem.WritesScene, Is.True, "and it cannot be done without saving the scene");
		}

		[Test]
		public void ASurfaceSceneWithNoDayNightCycleLosesItsSkyAndItsClouds()
		{
			WorldBody body = Earthlike();
			SceneWorldFacts facts = Finished();
			facts.HasDayNightCycle = false;

			WorldSystemProblem problem = Find(
				WorldSystemsAudit.Problems(facts, Placed("Meadow", body), body, false, null),
				WorldSystemCheck.MissingDayNight);

			Assert.That(problem.Severity, Is.EqualTo(WorldSystemSeverity.Warning));
			Assert.That(problem.CanFix, Is.True);
			Assert.That(problem.Automatic, Is.True, "a missing part is not an opinion; it goes in");
		}

		[Test]
		public void AMissingCycleAndItsSettingsBothGoInBecauseTheCycleReadsTheSettings()
		{
			/* WorldDayNightCycle.SceneSettings resolves the scene's latitude, longitude, heading
			 * and body by asking WorldSceneSettings. A cycle added on its own reads latitude 0 on
			 * no body and shows the wrong sky convincingly, so the pair is the unit. */
			WorldBody body = Earthlike();
			SceneWorldFacts facts = Finished();
			facts.HasSettings = false;
			facts.HasDayNightCycle = false;

			List<WorldSystemProblem> problems = WorldSystemsAudit.Problems(
				facts, Placed("Meadow", body), body, false, null);

			Assert.That(Find(problems, WorldSystemCheck.MissingSettings).Automatic, Is.True);
			Assert.That(Find(problems, WorldSystemCheck.MissingDayNight).Automatic, Is.True);
		}

		[Test]
		public void AnUndergroundSceneNeverHasACycleAddedAutomatically()
		{
			WorldBody body = Earthlike();
			SceneWorldFacts facts = Finished("Cellar");
			facts.HasDayNightCycle = false;

			WorldSystemProblem problem = Find(
				WorldSystemsAudit.Problems(facts, Placed("Cellar", body, underground: true), body, false, null),
				WorldSystemCheck.MissingDayNight);

			Assert.That(problem.Automatic, Is.False, "there is no sky down there to add one for");
		}

		[Test]
		public void ASceneWithNowhereToStandIsMarkedUnattachedSoItCanBeSaidSoFirst()
		{
			/* The components still go in — a scene with none is broken either way — but at their
			 * defaults, reading latitude 0 and the client's fallback sky. That is worth being told
			 * before the scene is written, not discovered later by its sky being wrong. */
			SceneWorldFacts facts = Finished();
			facts.HasSettings = false;
			facts.HasDayNightCycle = false;

			List<WorldSystemProblem> problems = WorldSystemsAudit.Problems(facts, null, null, false, null);

			Assert.That(Find(problems, WorldSystemCheck.MissingDayNight).Unattached, Is.True);
			Assert.That(Find(problems, WorldSystemCheck.MissingSettings).Unattached, Is.True);
		}

		[Test]
		public void APlacedSceneIsNotMarkedUnattached()
		{
			WorldBody body = Earthlike();
			SceneWorldFacts facts = Finished();
			facts.HasDayNightCycle = false;

			Assert.That(Find(WorldSystemsAudit.Problems(facts, Placed("Meadow", body), body, false, null),
				WorldSystemCheck.MissingDayNight).Unattached, Is.False);
		}

		[Test]
		public void AChoiceSomebodyMadeIsNeverOverruledWithoutAsking()
		{
			/* The line between the two halves. A cycle switched off and a light somebody placed are
			 * both probably wrong, and both are still theirs — so they ask, however obvious the
			 * answer looks. Only what a scene is simply missing goes in on its own. */
			WorldBody body = Earthlike();
			SceneWorldFacts facts = Finished();
			facts.DayNightEnabled = false;
			facts.DirectionalLights = 1;

			List<WorldSystemProblem> problems = WorldSystemsAudit.Problems(
				facts, Placed("Meadow", body), body, false, new Vector2(4f, 4f));

			Assert.That(Find(problems, WorldSystemCheck.DayNightDisabled).Automatic, Is.False);
			Assert.That(Find(problems, WorldSystemCheck.SceneDirectionalLight).Automatic, Is.False);
		}

		[Test]
		public void AnUndergroundSceneWithNoDayNightCycleIsCorrect()
		{
			/* The distinction the whole audit turns on. A cellar with no sun is finished; a
			 * meadow with no sun is not. Reporting both the same way would make the report
			 * useless for the only question anyone asks of it. */
			WorldBody body = Earthlike();
			SceneWorldFacts facts = Finished("Cellar");
			facts.HasDayNightCycle = false;

			WorldSystemProblem problem = Find(
				WorldSystemsAudit.Problems(facts, Placed("Cellar", body, underground: true), body, false, null),
				WorldSystemCheck.MissingDayNight);

			Assert.That(problem.Severity, Is.EqualTo(WorldSystemSeverity.Info), "not a fault underground");
			Assert.That(problem.CanFix, Is.False, "and emphatically not something to add");
		}

		[Test]
		public void AnUnplacedAtlasEntryIsReportedButNeverGuessedAt()
		{
			/* Latitude is the input to the climate, the sun's path and the prevailing wind. A
			 * guessed one would decide what the zone is and look authored while doing it, so this
			 * is the one finding that must stay unfixable however convenient a default would be. */
			WorldBody body = Earthlike();
			WorldAtlasScene entry = Placed("Meadow", body);
			entry.Placed = false;

			WorldSystemProblem problem = Find(
				WorldSystemsAudit.Problems(Finished(), entry, body, false, null),
				WorldSystemCheck.AtlasEntryUnplaced);

			Assert.That(problem, Is.Not.Null);
			Assert.That(problem.CanFix, Is.False, "the audit must never invent a placement");
		}

		[Test]
		public void ASceneWithNoAtlasEntryHasNothingToDeriveFrom()
		{
			WorldBody body = Earthlike();
			List<WorldSystemProblem> problems = WorldSystemsAudit.Problems(Finished(), null, body, false, null);

			WorldSystemProblem problem = Find(problems, WorldSystemCheck.MissingAtlasEntry);
			Assert.That(problem, Is.Not.Null);
			Assert.That(problem.Severity, Is.EqualTo(WorldSystemSeverity.Error));
			Assert.That(problem.CanFix, Is.True, "creating the entry is safe; placing it is not");
			Assert.That(problem.WritesScene, Is.False, "an atlas entry is an asset, not a scene");
		}

		[Test]
		public void AWorldWithNoClimateAssetIsNotAFaultBecauseClimateIsDerived()
		{
			/* CelestialMath.ClimateOffsets already turns a body's distance from its star, its
			 * atmosphere, its water and the scene's latitude into offsets on every reading, all
			 * relative to the home world. A ClimateSettings asset is only the shared reference
			 * model those offsets move — one of it for a whole solar system — and its field
			 * defaults are the calibrated numbers. So having none is the normal case. */
			WorldBody body = Earthlike();
			body.BaseClimate = null;
			SceneWorldFacts facts = Finished();
			facts.HasClimate = false;

			List<WorldSystemProblem> problems = WorldSystemsAudit.Problems(
				facts, Placed("Meadow", body), body, false, new Vector2(4f, 4f));

			Assert.That(CountAtLeast(problems, WorldSystemSeverity.Warning), Is.Zero,
				"a world with no climate asset is fully configured: " + string.Join(" | ", problems));
		}

		[Test]
		public void TheDerivedClimateIsTheCalibratedModelAndNotTheOldGuess()
		{
			/* The fallback this replaced read Temperature = -height01 * 0.8, which is -0.4 at mid
			 * elevation — below freezing over most of a scene — and gave no latitude gradient at
			 * all. Since no ClimateSettings asset existed anywhere in the project, that guess and
			 * not the model was deciding every biome and every rain-or-snow call. */
			FishMMO.Shared.Biomes.ClimateSettings derived = FishMMO.Shared.Biomes.ClimateSettings.Default;

			Assert.That(derived, Is.Not.Null);
			FishMMO.Shared.Biomes.ClimateSample midway = derived.Evaluate(0.5f, 0.5f);
			Assert.That(midway.Temperature, Is.GreaterThan(0f),
				"mid elevation on the sub-solar equator must be above freezing, not the old -0.4");
			Assert.That(derived.MapLatitudeSpanDegrees, Is.GreaterThan(0f),
				"a scene's map must span some latitude, or one map can never run from forest to tundra");
		}

		[Test]
		public void TheDerivedClimateIsNeverWrittenToDisk()
		{
			/* It is the model, not an asset: one instance in memory answering for every scene that
			 * authors none. Registering it in the cached-object table would give it the ID 0 slot
			 * that a template referenced only by a prefab already collides on. */
			FishMMO.Shared.Biomes.ClimateSettings derived = FishMMO.Shared.Biomes.ClimateSettings.Default;

			Assert.That(derived.hideFlags & HideFlags.DontSave, Is.EqualTo(HideFlags.DontSave));
			Assert.That(FishMMO.Shared.Biomes.ClimateSettings.Default, Is.SameAs(derived), "one instance, not one per call");
		}

		[Test]
		public void AnAuthoredDirectionalLightIsASecondSunThatNeverMoves()
		{
			WorldBody body = Earthlike();
			SceneWorldFacts facts = Finished();
			facts.DirectionalLights = 2;

			WorldSystemProblem problem = Find(
				WorldSystemsAudit.Problems(facts, Placed("Meadow", body), body, false, null),
				WorldSystemCheck.SceneDirectionalLight);

			Assert.That(problem, Is.Not.Null, "the sky system makes and drives its own");
			Assert.That(problem.CanFix, Is.True);
		}

		[Test]
		public void ASceneTooSmallToStormOverSaysSoRatherThanLookingBroken()
		{
			/* A front is hundreds of metres deep. Under the director's minimum no cell ever
			 * spawns, which looks exactly like a director that is switched off — so the audit says
			 * which it is, at Info, because there is nothing to fix. */
			WorldBody body = Earthlike();
			SceneWorldFacts facts = Finished();
			facts.DirectorAreaSquareKm = WorldSystemsAudit.DirectorMinimumSquareKm * 0.5f;

			WorldSystemProblem problem = Find(
				WorldSystemsAudit.Problems(facts, Placed("Meadow", body), body, false, null),
				WorldSystemCheck.TooSmallForDirector);

			Assert.That(problem, Is.Not.Null);
			Assert.That(problem.Severity, Is.EqualTo(WorldSystemSeverity.Info));
			Assert.That(problem.CanFix, Is.False);
		}

		[Test]
		public void ASceneTheDirectorCannotMeasureAtAllIsAWarning()
		{
			/* Different from being small: with neither a biome map nor a terrain, TryGetArea
			 * returns false and the director never runs whatever the scene's size. That is
			 * something a person can put right, so it is a warning and not an aside. */
			WorldBody body = Earthlike();
			SceneWorldFacts facts = Finished();
			facts.HasTerrain = false;
			facts.HasBiomeMap = false;
			facts.DirectorAreaSquareKm = 0f;

			List<WorldSystemProblem> problems = WorldSystemsAudit.Problems(facts, Placed("Meadow", body), body, false, null);

			Assert.That(Find(problems, WorldSystemCheck.NoWeatherArea), Is.Not.Null);
			Assert.That(Find(problems, WorldSystemCheck.TooSmallForDirector), Is.Null,
				"unmeasurable and small are different faults and must not both fire");
		}

		[Test]
		public void ADungeonGetsNoWeatherAndIsNotToldOffForIt()
		{
			WorldBody body = Earthlike();
			SceneWorldFacts facts = Finished("Crypt");
			facts.DirectorAreaSquareKm = 0f;

			List<WorldSystemProblem> problems = WorldSystemsAudit.Problems(
				facts, Placed("Crypt", body), body, isDungeon: true, null);

			Assert.That(Find(problems, WorldSystemCheck.WeatherOff), Is.Not.Null, "it should say why");
			Assert.That(Find(problems, WorldSystemCheck.NoWeatherArea), Is.Null,
				"a dungeon has no weather to find an area for");
		}

		[Test]
		public void AWorldWithNoAtmosphereHasNoWeatherWhateverTheSceneSays()
		{
			WorldBody airless = Make<WorldBody>("Rock");
			airless.Atmosphere = AtmosphereKind.None;

			Assert.That(WorldSystemsAudit.ResolveMode(Placed("Crater", airless), airless, false),
				Is.EqualTo(WeatherSceneMode.None));
		}

		[Test]
		public void AFixedPresetWithNoPresetIsNoWeatherRatherThanBrokenWeather()
		{
			/* The same correction WeatherField.ResolveMode makes on the server: a scene set to
			 * Fixed with nothing to fix it to would otherwise hold a mode it cannot satisfy. */
			WorldBody body = Earthlike();
			WorldAtlasScene entry = Placed("Meadow", body);
			entry.Weather = WeatherSceneMode.Fixed;
			entry.FixedWeather = null;

			Assert.That(WorldSystemsAudit.ResolveMode(entry, body, false), Is.EqualTo(WeatherSceneMode.None));
		}

		[Test]
		public void TheServerAgreesThatASceneWithNoSettingsHasNoWeather()
		{
			/* The audit copies the server's rule rather than calling it, because it has to answer
			 * for scenes with no settings component — which is precisely the case WeatherField
			 * answers None for. Pinned so the two cannot drift apart unnoticed. */
			Assert.That(WeatherField.ResolveMode(null, "Meadow"), Is.EqualTo(WeatherSceneMode.None));
		}

		[Test]
		public void TheBuildNeverPromptsAndNeverSavesAScene()
		{
			/* A client build rebuilds this cache twice, between baking the world maps and building
			 * them. A dialog there stalls a headless build; a scene saved there changes the very
			 * scenes being built. Both are avoided by the build calling the parameterless Rebuild,
			 * which does not audit at all — pinned in source because the failure cannot be provoked
			 * from a test without running a build. */
			string source = File.ReadAllText(Path.Combine(
				Directory.GetCurrentDirectory(),
				"Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/FishMMO Dashboard/CustomBuildTool/Core/CustomBuildTool.cs"));

			Assert.That(source, Does.Contain("WorldSceneDetailsCacheBuilder.Rebuild();"),
				"the build must call the plain rebuild");
			Assert.That(source, Does.Not.Contain("WorldSystemsMode"),
				"the build must never choose a mode that prompts or writes scenes");
		}

		[Test]
		public void APlainRebuildDoesNotAudit()
		{
			/* The audit logs at the severity it found, so an Error finding would turn every test
			 * that rebuilds the cache red and blame the rebuild for a scene's missing component.
			 * Only the dashboard buttons ask for it. */
			Assert.That(WorldSystemsMode.Off, Is.EqualTo(default(WorldSystemsMode)),
				"not auditing must be the default, so a new caller cannot opt in by forgetting to choose");

			string source = File.ReadAllText(Path.Combine(
				Directory.GetCurrentDirectory(),
				"Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/WorldSceneDetailsCacheBuilder.cs"));

			Assert.That(source, Does.Contain("return Rebuild(WorldSystemsMode.Off);"),
				"the parameterless rebuild must stay the un-audited one");
			Assert.That(source, Does.Contain("Rebuild(WorldSystemsMode.Prompt)"),
				"the dashboard button is what asks");
		}
	}
}
