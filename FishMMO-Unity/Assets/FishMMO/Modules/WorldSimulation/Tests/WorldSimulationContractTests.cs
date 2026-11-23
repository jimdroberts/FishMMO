using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>Split from WorldContractTests: the World Simulation module's half.</summary>
	public class WorldSimulationContractTests
	{
		[Test]
		public void WithoutTheCalendar_TheClockCountsFromTheDefaultEpoch_TheCalendarsOwnDefault()
		{
			LogAssert.AreEqual(WorldEpoch.DefaultUnixSeconds, CalendarProfile.DefaultEpochUnixSeconds, "a world that never changed its calendar reads the same time with the sky module or without it");
			var source = WorldEpoch.Source;
			try
			{
				WorldEpoch.Source = null;
				LogAssert.AreEqual(WorldEpoch.DefaultUnixSeconds, WorldEpoch.UnixSeconds, "no calendar installed");
				WorldEpoch.Source = () => 42L;
				LogAssert.AreEqual(42L, WorldEpoch.UnixSeconds, "an installed calendar's epoch is used");
			}
			finally
			{
				WorldEpoch.Source = source;
			}
		}

		[Test]
		public void TheSkysPreview_IsTheCoreClocksPreview()
		{
			double? hours = WorldClockPreview.Hours;
			try
			{
				WorldDayNightCycle.PreviewHours = 12.5;
				LogAssert.AreEqual(12.5, WorldClockPreview.Hours ?? -1.0, "the sky's preview holds the core clock, which the world's motion follows");
				WorldDayNightCycle.PreviewHours = null;
				LogAssert.IsFalse(WorldClockPreview.Hours.HasValue, "and lets it go");
			}
			finally
			{
				WorldClockPreview.Hours = hours;
			}
		}

		[Test]
		public void WithoutAWorldDesign_ASceneKeepsItsOwnCap_AndItsEntryOverridesIt()
		{
			var lookup = WorldSceneEntries.Lookup;
			var go = new GameObject("WorldContractTests Settings");
			try
			{
				WorldSceneSettings settings = go.AddComponent<WorldSceneSettings>();
				settings.MaxClients = 150;
				WorldSceneEntries.Lookup = null;
				LogAssert.IsTrue(settings.Entry == null, "no world design installed: no entry");
				LogAssert.AreEqual(150, settings.MaxClients, "and the scene keeps its own cap");
				var entry = ScriptableObject.CreateInstance<FishMMO.Shared.Atlas.WorldAtlasScene>();
				entry.MaxClients = 40;
				WorldSceneEntries.Lookup = _ => entry;
				LogAssert.AreEqual(40, settings.MaxClients, "the atlas entry's cap wins, through the core contract");
				LogAssert.AreEqual(0f, settings.Entry.NorthBearingAt(Vector3.zero), "an entry not placed on the globe keeps +Z as north");
				Object.DestroyImmediate(entry);
			}
			finally
			{
				WorldSceneEntries.Lookup = lookup;
				Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void ASceneThatAuthorsItsOwnClimate_CarriesItBesideTheCoreSettings()
		{
			var go = new GameObject("WorldContractTests Climate");
			var climate = ScriptableObject.CreateInstance<FishMMO.Shared.Biomes.ClimateSettings>();
			try
			{
				WorldSceneSettings settings = go.AddComponent<WorldSceneSettings>();
				LogAssert.IsTrue(go.GetComponent<WorldSceneClimate>() == null, "a scene with no climate of its own carries nothing for it");
				settings.SetClimate(null);
				LogAssert.IsTrue(go.GetComponent<WorldSceneClimate>() == null, "clearing what is not there adds nothing");
				settings.SetClimate(climate);
				LogAssert.IsTrue(go.GetComponent<WorldSceneClimate>() != null, "the climate's own component carries it");
				LogAssert.IsTrue(settings.Climate() == climate, "and the climate reads it (no atlas entry here)");
			}
			finally
			{
				Object.DestroyImmediate(go);
				Object.DestroyImmediate(climate);
			}
		}

		[Test]
		public void TheWorldSimBed_KeepsItsAuthoredClimate()
		{
			string scene = ProjectFiles.ReadAllText("Assets/Scenes/Test/WorldSim.unity").Replace("\r\n", "\n");
			string script = UnityEditor.AssetDatabase.AssetPathToGUID(ProjectFiles.Path("Assets/FishMMO/Modules/WorldSimulation/Runtime/Weather/WorldSceneClimate.cs"));
			Match doc = Regex.Match(scene, @"  m_Script: \{fileID: 11500000, guid: " + script + @", type: 3\}\n(?:  .*\n)*");
			LogAssert.IsTrue(doc.Success, "the bed's settings object carries a WorldSceneClimate");
			LogAssert.IsTrue(doc.Value.Contains("Climate: {fileID: 11400000, guid: 706a75aad517420f8dde51790ac44689"), "with the Default Climate it authored before the split");
		}
	}
}
