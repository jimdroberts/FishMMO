#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>How much a world-systems finding matters.</summary>
	public enum WorldSystemSeverity
	{
		/// <summary>The system does not run in this scene at all.</summary>
		Error = 0,
		/// <summary>It runs, but visibly wrongly or with a piece missing.</summary>
		Warning = 1,
		/// <summary>Worth knowing. Often the scene is simply too small or too far underground.</summary>
		Info = 2,
	}

	/// <summary>The names of the checks, so a fix and its finding cannot drift apart.</summary>
	public static class WorldSystemCheck
	{
		public const string MissingSettings = "missing-settings";
		public const string MissingDayNight = "missing-day-night";
		public const string DayNightDisabled = "day-night-disabled";
		public const string SceneDirectionalLight = "scene-directional-light";
		public const string MissingAtlasEntry = "missing-atlas-entry";
		public const string AtlasEntryUnplaced = "atlas-entry-unplaced";
		public const string AtlasSizeStale = "atlas-size-stale";
		public const string NoBody = "no-body";
		public const string UnusedClimate = "unused-climate";
		public const string NoSkyProfile = "no-sky-profile";
		public const string NoWeatherArea = "no-weather-area";
		public const string TooSmallForDirector = "too-small-for-director";
		public const string WeatherOff = "weather-off";
		public const string MissingRendererPass = "missing-renderer-pass";
	}

	/// <summary>One thing the audit found about one scene.</summary>
	public sealed class WorldSystemProblem
	{
		/// <summary>One of <see cref="WorldSystemCheck"/>'s names.</summary>
		public string Id;
		public string SceneName;
		public WorldSystemSeverity Severity;
		/// <summary>What is wrong, and what it costs in the running game.</summary>
		public string Message;
		/// <summary>What the fix will do, or — when there is no fix — what a person has to do.</summary>
		public string Remedy;
		/// <summary>Whether the audit can put this right by itself.</summary>
		public bool CanFix;
		/// <summary>Whether fixing it rewrites the scene's <c>.unity</c> file.</summary>
		public bool WritesScene;

		/// <summary>
		/// Applied without asking, because the scene cannot work without it and there is no
		/// judgement to make.
		/// </summary>
		/// <remarks>
		/// Only for what a scene is simply missing — the components, and the atlas entry they read.
		/// Anything that overrules a choice somebody made (a cycle switched off, a light they
		/// placed) still asks, however obviously wrong it looks.
		/// </remarks>
		public bool Automatic;

		/// <summary>
		/// The scene has no place on a celestial body, so the fix uses defaults.
		/// </summary>
		/// <remarks>
		/// Carried on the finding rather than worked out again at fix time so the person is told
		/// once, before the scene is written, what it is being set up as.
		/// </remarks>
		public bool Unattached;

		/// <summary>
		/// The fix for a finding about the project rather than about a scene, or null. Returns
		/// whether the finding is now resolved.
		/// </summary>
		/// <remarks>
		/// A delegate here and a name everywhere else, for one reason: a scene finding is gathered
		/// while its scene is open and applied long after it was closed, so anything it captured is
		/// a destroyed object by the time the fix runs. A project finding captures assets, which
		/// outlive the whole operation.
		/// </remarks>
		public Func<bool> ProjectFix;

		/// <summary>True when this is about the project as a whole, not one scene.</summary>
		public bool IsProjectWide => string.IsNullOrEmpty(SceneName);

		public override string ToString() => $"[{Severity}] {(IsProjectWide ? "project" : SceneName)}: {Message}";
	}

	/// <summary>
	/// What one world scene says about itself, read while it was open.
	/// </summary>
	/// <remarks>
	/// Deliberately a plain record, and deliberately separate from the verdict. The scene has to be
	/// loaded to gather this and is closed again immediately afterwards, so holding on to any
	/// component or GameObject from it would leave the audit pointing at destroyed objects. It also
	/// means <see cref="WorldSystemsAudit.Problems"/> is pure: the rules can be tested without a
	/// scene, an editor, or a solar system.
	/// </remarks>
	public sealed class SceneWorldFacts
	{
		public string SceneName;

		/// <summary>Whether the scene carries a <see cref="WorldSceneSettings"/>. Without one there is no weather.</summary>
		public bool HasSettings;
		/// <summary>Whether it carries a <see cref="WorldDayNightCycle"/>. Without one there is no sky.</summary>
		public bool HasDayNightCycle;
		/// <summary>The cycle's own switch.</summary>
		public bool DayNightEnabled;
		/// <summary>Directional lights authored into the scene. The client's sky makes its own.</summary>
		public int DirectionalLights;
		/// <summary>Whether the scene has a terrain the weather director can measure.</summary>
		public bool HasTerrain;
		/// <summary>Whether the scene (or its atlas entry) names a baked biome map.</summary>
		public bool HasBiomeMap;
		/// <summary>Whether a climate model resolves for the scene — its own, its entry's or its body's.</summary>
		public bool HasClimate;
		/// <summary>
		/// The area the weather director would measure, in square kilometres: the biome map's world
		/// size if there is one, else the union of the scene's terrains. Zero when it has neither.
		/// </summary>
		public float DirectorAreaSquareKm;

		/// <summary>How many Unity terrains the scene has: 1 is a lone terrain, more is a stitched landmass.</summary>
		public int TerrainTiles;

		/// <summary>
		/// What a normalised height of 0 to 1 spans across the whole landmass, in metres.
		/// </summary>
		/// <remarks>
		/// The number the derived lapse rate is built on: a real lapse rate is kelvin per metre, so
		/// how much a scene cools from its lowest ground to its highest depends entirely on how
		/// much ground there is. Measured across every tile, because a stitched landmass is one
		/// slope, not several.
		/// </remarks>
		public float LandmassHeightMetres;
	}

	/// <summary>
	/// Whether a world scene is set up for the weather, cloud, sky and climate systems — and what
	/// is missing when it is not.
	/// </summary>
	/// <remarks>
	/// <para>
	/// These systems are quiet when they are not configured. A scene with no
	/// <see cref="WorldSceneSettings"/> is skipped by <c>WeatherHost</c> without a word, so it has
	/// no weather, no storm cells and no world clock; a scene with no <see cref="WorldDayNightCycle"/>
	/// is left exactly as authored by the client's sky, so it has no sun, no moons and no clouds.
	/// Neither logs an error, because neither is an error — a login screen wants exactly that. The
	/// only way to tell a scene that opted out from one that was never finished is to ask what it
	/// is, which is what this does.
	/// </para>
	/// <para>
	/// <b>The atlas is the input, not the output.</b> Where a scene sits on its planet — latitude,
	/// longitude, body, layer — is a design decision and lives in its <see cref="WorldAtlasScene"/>
	/// entry. Everything derived from that placement (the sun's path, the prevailing wind, the
	/// climate, whether weather exists at all) is already read from the atlas at runtime. So the
	/// automatic configuration here is small on purpose: it adds the components that let a scene
	/// read its atlas entry, and chooses their handful of settings from where the entry says the
	/// scene is. It never invents a placement.
	/// </para>
	/// </remarks>
	public static class WorldSystemsAudit
	{
		/// <summary>
		/// The verdict on one scene. Pure: no assets are loaded, nothing is opened, nothing changes.
		/// </summary>
		/// <param name="facts">What the scene said about itself.</param>
		/// <param name="entry">Its atlas entry, or null when it has none.</param>
		/// <param name="body">The body it stands on, or null.</param>
		/// <param name="isDungeon">Whether a <c>DungeonTemplate</c> names this scene.</param>
		/// <param name="measuredSizeKm">Its size from the scene details cache, or null.</param>
		/// <param name="authoredClimate">
		/// A climate somebody wrote that nothing is using, or null. Only to notice an oversight —
		/// a world with no climate asset at all is fully configured.
		/// </param>
		public static List<WorldSystemProblem> Problems(
			SceneWorldFacts facts,
			WorldAtlasScene entry,
			WorldBody body,
			bool isDungeon,
			Vector2? measuredSizeKm,
			ClimateSettings authoredClimate = null)
		{
			var problems = new List<WorldSystemProblem>();
			if (facts == null)
			{
				return problems;
			}

			bool underground = entry != null && entry.Layer != null && entry.Layer.Underground;
			/* No entry at all, an entry that has not been placed, or no body under it: in every
			 * one of those the components go in at their defaults and read latitude 0, longitude 0,
			 * and whatever sky the client falls back to. */
			bool unattached = entry == null || !entry.Placed || body == null;
			/* What the scene will actually get, by the same rule the server uses. A dungeon gets
			 * none, and so does a scene on an airless rock — neither is a fault, and saying so is
			 * most of this audit's value: it is the difference between "switched off" and "never
			 * finished", which nothing else in the project distinguishes. */
			WeatherSceneMode mode = ResolveMode(entry, body, isDungeon);
			bool wantsWeather = mode != WeatherSceneMode.None;
			bool wantsSky = !underground && !isDungeon;

			if (!facts.HasSettings)
			{
				/* Automatic, and it has to be even though the request only named the day/night
				 * cycle: the cycle resolves its latitude, longitude, heading and body by asking
				 * this component (WorldDayNightCycle.SceneSettings). Added on its own, a cycle
				 * reads latitude 0 on no body and shows the wrong sky convincingly. */
				Add(problems, facts, WorldSystemCheck.MissingSettings, WorldSystemSeverity.Error,
					"has no World Scene Settings, so the weather host skips it entirely: no weather, no storm cells, no climate and no world clock — and a day/night cycle added beside it would have nothing to read its latitude from.",
					"Add a World Scene Settings component. It reads this scene's atlas entry for everything else.",
					fix: true, writesScene: true, automatic: true, unattached: unattached);
			}

			if (!facts.HasDayNightCycle)
			{
				/* Underground is the one place its absence is right, and the reason the severity
				 * moves rather than the check disappearing: a cellar with no sun is correct, and a
				 * surface zone with no sun is a scene nobody has finished. */
				Add(problems, facts, WorldSystemCheck.MissingDayNight,
					wantsSky ? WorldSystemSeverity.Warning : WorldSystemSeverity.Info,
					wantsSky
						? "has no World Day Night Cycle, so the client's sky leaves it exactly as authored: no sun, no moons, no stars, no clouds and no lightning."
						: "has no World Day Night Cycle. That is normal for an underground or dungeon scene, which has no sky to draw.",
					wantsSky
						? unattached
							? "Add a World Day Night Cycle component at its defaults, since the scene has no place on a celestial body to take them from."
							: "Add a World Day Night Cycle component. It takes its time, latitude, heading and sky from the atlas entry, so it follows the scene if it is ever moved on the globe."
						: "Nothing to do unless this scene is meant to show a sky.",
					fix: wantsSky, writesScene: true, automatic: wantsSky, unattached: unattached);
			}
			else if (wantsSky && !facts.DayNightEnabled)
			{
				Add(problems, facts, WorldSystemCheck.DayNightDisabled, WorldSystemSeverity.Warning,
					"has a World Day Night Cycle with the cycle switched off, so its sky is frozen at one time of day.",
					"Switch the cycle on.",
					fix: true, writesScene: true);
			}

			if (facts.DirectionalLights > 0)
			{
				/* The sky makes a directional light per sun and per moon and drives them from the
				 * solar system. One authored into the scene is a second sun that never moves: it
				 * lights the ground from a fixed angle all night, and shadows point two ways. */
				Add(problems, facts, WorldSystemCheck.SceneDirectionalLight, WorldSystemSeverity.Warning,
					$"has {facts.DirectionalLights} directional light(s) of its own. The sky system makes and drives one per sun and moon, so these light the scene from a fixed angle at midnight and cast a second set of shadows.",
					"Switch the scene's directional lights off. They are left in place, not deleted, so a deliberate one can be switched back on.",
					fix: true, writesScene: true);
			}

			if (entry == null)
			{
				Add(problems, facts, WorldSystemCheck.MissingAtlasEntry, WorldSystemSeverity.Error,
					"is not in the world atlas, so it has no body, no latitude and no longitude: its sky, its seasons and its prevailing wind have nothing to derive from.",
					"Create an atlas entry for it. It starts unplaced; put it on the globe in World → World Atlas.",
					fix: true, writesScene: false, automatic: true, unattached: true);
			}
			else
			{
				if (!entry.Placed)
				{
					/* No automatic fix, and this is the one place that restraint matters most.
					 * A latitude is the whole input to the climate, the sun's path and the wind;
					 * a guessed one would quietly decide what the zone is, and look authored. */
					Add(problems, facts, WorldSystemCheck.AtlasEntryUnplaced, WorldSystemSeverity.Warning,
						"has an atlas entry that has not been placed on the globe, so it falls back to latitude 0, longitude 0 — the equator, on the prime meridian.",
						"Place it in World → World Atlas. Where a scene sits is a design decision; the audit will not guess a latitude for you.",
						fix: false, writesScene: false);
				}

				if (measuredSizeKm.HasValue && !Approximately(entry.SizeKm, measuredSizeKm.Value))
				{
					Add(problems, facts, WorldSystemCheck.AtlasSizeStale, WorldSystemSeverity.Info,
						$"is {measuredSizeKm.Value.x:0.###} × {measuredSizeKm.Value.y:0.###} km by its boundaries, but its atlas entry says {entry.SizeKm.x:0.###} × {entry.SizeKm.y:0.###} km.",
						"Refresh the entry's size from the scene's boundaries.",
						fix: true, writesScene: false, automatic: true);
				}
			}

			if (body == null)
			{
				Add(problems, facts, WorldSystemCheck.NoBody, WorldSystemSeverity.Error,
					"stands on no celestial body, so there is no star to light it, no orbit to give it seasons and no atmosphere to decide whether it has weather.",
					"Give the atlas entry a body, or set a home world on the solar system profile.",
					fix: false, writesScene: false);
			}
			else
			{
				/* Having NO climate asset is not a fault: a world's climate is derived.
				 * CelestialMath.ClimateOffsets already turns the body's distance from its star, its
				 * atmosphere, its water and the scene's latitude into offsets on every reading, all
				 * relative to the home world, so a ClimateSettings asset is only the shared
				 * reference model those offsets move — one of it for a whole system, and its field
				 * defaults are the calibrated numbers.
				 *
				 * What IS worth saying is an authored one that nothing uses. Somebody sat and wrote
				 * its variants; a body left on the built-in defaults beside it is an oversight, not
				 * a decision, and the two are indistinguishable from the inspector. */
				if (body.BaseClimate == null && authoredClimate != null
					&& (entry == null || entry.Climate == null) && !facts.HasClimate)
				{
					Add(problems, facts, WorldSystemCheck.UnusedClimate, WorldSystemSeverity.Info,
						$"runs on the built-in climate defaults, while \"{authoredClimate.name}\" sits in the project unused — {body.name} has no base climate.",
						$"Set \"{authoredClimate.name}\" as {body.name}'s base climate, so every scene on it uses the authored model instead of the defaults.",
						fix: true, writesScene: false, automatic: true);
				}

				if (wantsSky && body.Sky == null)
				{
					Add(problems, facts, WorldSystemCheck.NoSkyProfile, WorldSystemSeverity.Warning,
						$"stands on {body.name}, which has no sky profile, so the sky falls back to its built-in defaults.",
						"Run Weather → Weather Tools → Create all weather content, which makes the default sky and assigns it to the home world.",
						fix: false, writesScene: false);
				}
			}

			if (wantsWeather)
			{
				if (facts.DirectorAreaSquareKm <= 0f)
				{
					Add(problems, facts, WorldSystemCheck.NoWeatherArea, WorldSystemSeverity.Warning,
						facts.HasBiomeMap
							? "has a biome map with no world size on it, so the weather director cannot measure the scene and will never start a storm cell here. Background weather still runs."
							: "has neither a baked biome map nor a terrain, so the weather director cannot measure it and will never start a storm cell here. Background weather still runs.",
						facts.HasBiomeMap
							? "Set the biome map's world origin and world size to the ground it covers."
							: "Import a biome map for it (World → Biomes), or give it a terrain.",
						fix: false, writesScene: false);
				}
				else if (facts.DirectorAreaSquareKm < DirectorMinimumSquareKm)
				{
					Add(problems, facts, WorldSystemCheck.TooSmallForDirector, WorldSystemSeverity.Info,
						$"measures {facts.DirectorAreaSquareKm:0.##} km², under the {DirectorMinimumSquareKm:0.##} km² the weather director needs, so no storm cell will ever spawn here. Background weather still runs.",
						"Nothing, unless the scene is meant to be larger. A front is hundreds of metres deep; it has nowhere to cross in a scene this size.",
						fix: false, writesScene: false);
				}
			}
			else
			{
				Add(problems, facts, WorldSystemCheck.WeatherOff, WorldSystemSeverity.Info,
					isDungeon
						? "is a dungeon, so it gets no weather. That is the Auto rule, not a fault."
						: body != null && !body.HasWeather
							? $"stands on {body.name}, which has no atmosphere, so it gets no weather."
							: "is set to have no weather.",
					"Nothing, unless it was meant to have weather — then set the mode on its atlas entry.",
					fix: false, writesScene: false);
			}

			return problems;
		}

		/// <summary>
		/// Checks about the project rather than about one scene, contributed by other editor
		/// assemblies.
		/// </summary>
		/// <remarks>
		/// The cloud, fog and overlay passes live on the URP renderer assets and their types live
		/// in the client assembly, which this one cannot reference — the dependency runs the other
		/// way, so that a tool in another editor assembly needs a reference to this one and not the
		/// reverse. So the client registers its own checks here at editor load, and a server-only
		/// editor, where that assembly is compiled out, simply has none to run.
		/// </remarks>
		public static readonly List<Action<List<WorldSystemProblem>>> ProjectChecks = new List<Action<List<WorldSystemProblem>>>();

		/// <summary>Runs every registered project-wide check.</summary>
		public static List<WorldSystemProblem> ProjectProblems()
		{
			var problems = new List<WorldSystemProblem>();
			foreach (Action<List<WorldSystemProblem>> check in ProjectChecks)
			{
				try
				{
					check?.Invoke(problems);
				}
				catch (Exception ex)
				{
					Debug.LogError($"[World systems] A project check threw: {ex}");
				}
			}
			return problems;
		}

		/// <summary>The area, in square kilometres, below which the weather director never runs.</summary>
		/// <remarks>Mirrors <c>WeatherHost.DirectorMinimumSquareKm</c>, which lives in the server assembly.</remarks>
		public const float DirectorMinimumSquareKm = 2f;

		/// <summary>
		/// The weather mode a scene resolves to, by the same rule the server applies.
		/// </summary>
		/// <remarks>
		/// A copy of <c>WeatherField.ResolveMode</c>'s decision, expressed against the atlas rather
		/// than against a loaded <see cref="WorldSceneSettings"/> — the audit has to answer for
		/// scenes that have no settings component at all, which is the case it exists to find.
		/// </remarks>
		public static WeatherSceneMode ResolveMode(WorldAtlasScene entry, WorldBody body, bool isDungeon)
		{
			WeatherSceneMode mode = entry != null ? entry.EffectiveWeather : WeatherSceneMode.Auto;
			if (mode != WeatherSceneMode.Auto)
			{
				return mode == WeatherSceneMode.Fixed && (entry == null || entry.FixedWeather == null)
					? WeatherSceneMode.None
					: mode;
			}
			if (body != null && !body.HasWeather)
			{
				return WeatherSceneMode.None;
			}
			return isDungeon ? WeatherSceneMode.None : WeatherSceneMode.Own;
		}

		/// <summary>Sizes in km agree to within a metre.</summary>
		private static bool Approximately(Vector2 a, Vector2 b)
		{
			return Mathf.Abs(a.x - b.x) < 0.001f && Mathf.Abs(a.y - b.y) < 0.001f;
		}

		private static void Add(List<WorldSystemProblem> into, SceneWorldFacts facts, string id,
			WorldSystemSeverity severity, string message, string remedy, bool fix, bool writesScene,
			bool automatic = false, bool unattached = false)
		{
			into.Add(new WorldSystemProblem
			{
				Id = id,
				SceneName = facts.SceneName,
				Severity = severity,
				Message = message,
				Remedy = remedy,
				CanFix = fix,
				WritesScene = writesScene,
				Automatic = automatic,
				Unattached = unattached,
			});
		}
	}
}
#endif
