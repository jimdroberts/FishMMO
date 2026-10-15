using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.Weather;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// A cloud world's sky (SceneCloudWorld): the deck the march draws under its islands (CloudClimate.Deck,
	/// FISH_CLOUD_DECK), and the sky's pictures of the ground, which must not take the islands' hole-filled
	/// heightmaps for ground or let the ground bend the clouds.
	/// </summary>
	[TestFixture]
	public class CloudWorldSkyTests
	{
		private const string Volume = "Assets/Prefabs/Client/Weather/Shaders/FishCloudVolume.hlsl";
		private const string Sky = "Assets/Scripts/Client/World/Weather/Sky/SkySystem.cs";
		private const string Terrain = "Assets/Scripts/Client/World/Weather/Sky/CloudTerrainMap.cs";

		private static CloudClimate.Scales Earthlike => CloudClimate.ScalesFor(PlanetAir.Earthlike, 45f, 238u);

		[Test]
		public void TheDeckStandsWhereTheSceneSaysAndIsSolidUnderItsBillows()
		{
			CloudBand deck = CloudClimate.Deck(Earthlike, 0f, 400f);
			LogAssert.AreEqual(CloudRegime.Deck, deck.Regime, "the deck's own regime");
			LogAssert.IsTrue(deck.Present && !deck.Column && !deck.CarriesRain && !deck.GrowsStorms, "a deck, not the weather's column");
			LogAssert.AreEqual(-400f, deck.Bottom, "drawn down to its depth under the top");
			LogAssert.AreEqual(CloudClimate.DeckCrest, deck.Top, "its highest cells over its mean top");
			Assert.That(deck.TopSoftness * (deck.Top - deck.Bottom), Is.EqualTo(CloudClimate.DeckBillowDepth).Within(0.01f),
				"the top's softness is the billows' depth, which the shader fills in solid under");
			float tau = deck.Extinction * (deck.Top - deck.Bottom);
			Assert.That(tau, Is.GreaterThan(10f), $"nothing is seen through it to the nothing under it: τ {tau:0}");
		}

		[Test]
		public void TheDecksShellReachesAsHighAsItsCloudCan()
		{
			// The march skips everything over the deck's shell (_FishCloudLayerD.z), so it must hold the highest cloud
			// the deck can make: where the noise's highest value (0.759, CloudStats.FillForThreshold) times the profile
			// still clears the deck's cut, less what its mixing shell's water reaches past the cut (about 0.08).
			float cover = CloudClimate.DeckCover;
			float cut = Mathf.Lerp(FishMMO.Client.SkySystem.CloudCutClear, FishMMO.Client.SkySystem.CloudCutFull, cover)
				- FishMMO.Client.SkySystem.CloudCutBend * cover * cover * cover * cover;
			float profile = (cut - 0.08f) / 0.759f;
			float highestCloud = CloudClimate.DeckCrest - profile * CloudClimate.DeckBillowDepth;
			float shell = CloudClimate.DeckHighest(0f);
			Assert.That(shell, Is.GreaterThanOrEqualTo(highestCloud), $"no deck cloud over its shell: shell {shell:0} m, cloud to {highestCloud:0} m");
			Assert.That(shell, Is.LessThan(CloudClimate.DeckCrest), "and lower than its band's top, so a low island's camera stands over it");
		}

		[Test]
		public void TheDeckIsTheSameInAnyWeather()
		{
			CloudBand fair = CloudClimate.Deck(Earthlike, 0f, 400f);
			// No world air at all (the scales a scene without weather of its own has).
			CloudBand none = CloudClimate.Deck(default, 0f, 400f);
			LogAssert.AreEqual(fair.Coverage, none.Coverage, "its cover is its own");
			LogAssert.AreEqual(fair.MaxCoverage, none.MaxCoverage, "and the most it has anywhere");
			LogAssert.AreEqual(fair.Extinction, none.Extinction, "its opacity too");
			Assert.That(none.NoiseScale, Is.InRange(2500f, 6000f), "cells of a few hundred metres without a world's scales");
			Assert.That(none.DetailScale, Is.InRange(150f, 400f));
			LogAssert.IsTrue(fair.MaxCoverage > 0.002f && fair.Extinction > 0f, "never a band the march skips as empty");
		}

		[Test]
		public void ADeckShallowerThanItsBillowsIsDrawnAsDeepAsThem()
		{
			CloudBand deck = CloudClimate.Deck(Earthlike, 50f, 20f);
			LogAssert.AreEqual(50f - CloudClimate.DeckBillowDepth, deck.Bottom, "at least the billows' depth under its top");
			LogAssert.IsTrue(deck.TopSoftness > 0f && deck.TopSoftness <= 1f, $"a softness within the band: {deck.TopSoftness}");
		}

		[Test]
		public void TheShadersDeckIsTheSameRegime()
		{
			string volume = SourceScanPins.ReadCode(Volume);
			LogAssert.IsTrue(volume.Contains("#define FISH_CLOUD_DECK " + (int)CloudRegime.Deck + "\n"),
				"FISH_CLOUD_DECK is CloudRegime.Deck's number: the shader knows the deck by _FishCloudLayerF.x");
		}

		[Test]
		public void TheGroundBendsNoCloudOverACloudWorld()
		{
			string volume = SourceScanPins.ReadCode(Volume);
			SourceScanPins.HoldsAndFires("terrain in the field", volume,
				code =>
				{
					string field = SourceScanPins.Body(code, "FishCloudField FishCloudFieldAt(float2 xz)");
					if (field == null)
					{
						return "FishCloudFieldAt is gone";
					}
					return field.Contains("if (FishCloudOverTerrain() && (skip & 16u) == 0u)") ? null : "the field reads the terrain over a cloud world";
				},
				SourceScanPins.Replace("if (FishCloudOverTerrain() && (skip & 16u) == 0u)", "if (_FishCloudTerrainRect.w > 0.5 && (skip & 16u) == 0u)"),
				"the terrain read whatever the scene");
			SourceScanPins.HoldsAndFires("rock clearance", volume,
				code => code.Contains("_FishCloudFlowRect.w < 0.5 || _FishCloudDeck.z > 0.5 ||") ? null : "the clouds are cleared off the islands' hole-filled rock",
				SourceScanPins.Replace("_FishCloudFlowRect.w < 0.5 || _FishCloudDeck.z > 0.5 ||", "_FishCloudFlowRect.w < 0.5 ||"),
				"clearance over a cloud world");
		}

		[Test]
		public void TheDeckCastsNoShadow()
		{
			string volume = SourceScanPins.ReadCode(Volume);
			const string skip = "if (a.z <= 0.002 || a.w <= 0.0 || (int)(_FishCloudLayerF[i].x + 0.5) == FISH_CLOUD_DECK)";
			SourceScanPins.HoldsAndFires("shadow walk", volume,
				code =>
				{
					string walk = SourceScanPins.Body(code, "float FishCloudShadowDepth(");
					return walk == null ? "FishCloudShadowDepth is gone" : walk.Contains(skip) ? null : "the deck under every island shadows it";
				},
				SourceScanPins.Replace(skip, "if (a.z <= 0.002 || a.w <= 0.0)"),
				"the deck walked toward the sun");
		}

		[Test]
		public void TheMarchsFloorGoesDownToTheDeck()
		{
			string sky = SourceScanPins.ReadCode(Sky);
			SourceScanPins.HoldsAndFires("shell floor", sky,
				code => code.Contains("CloudShellBottom = Mathf.Min(CloudStackFloor(") && code.Contains(", deckFloor);") ? null : "the stack's floor stops at sea level, over the deck",
				SourceScanPins.Replace(", deckFloor);", ";"),
				"the floor clamped at sea level");
			// And never below it anywhere else.
			LogAssert.AreEqual(0f, SkyFloorNoRainNoFog(-300f), "an Earth-like stack's floor is never below sea level");
		}

		[Test]
		public void AnIslandsHolesAreNotGround()
		{
			string terrain = SourceScanPins.ReadCode(Terrain);
			SourceScanPins.HoldsAndFires("holes", terrain,
				code =>
				{
					string height = SourceScanPins.Body(code, "public bool TryHeight(float x, float z, out float height)");
					return height == null ? "TryHeight is gone"
						: height.Contains("holes != null && k < holes.Length && holes[k] != null") && height.Contains("holes[k].HoleAt(") ? null
						: "a hole's filled-in height is read as ground";
				},
				SourceScanPins.Replace("holes != null && k < holes.Length && holes[k] != null", "false"),
				"holes read as ground");
		}

		private static float SkyFloorNoRainNoFog(float lowest)
		{
			return FishMMO.Client.SkySystem.CloudStackFloor(lowest, false, false, -2f);
		}
	}
}
