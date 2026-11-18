using FishMMO.Client;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using FishMMO.Shared.WorldDesign;
using FishMMO.Shared;
using NUnit.Framework;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// The sea floor's lurkers (split from BiomeSeaFloorTests, Phase 4, 2026-11-17).
	/// </summary>
	[TestFixture]
	public class SeaLifeLurkTests
	{
		private const string Shaders = "Assets/FishMMO/Modules/AmbientLife/Shaders";

		private static string Read(string path) => ProjectFiles.ReadAllText(path).Replace("\r\n", "\n");

		// ── Sea life: the Eel and the Lurk ───────────────────────────

		[Test]
		public void Lurk_RestsInItsDen_ThenDartsOutAndBack_OnTheSharedClock()
		{
			const double period = 30.0;
			float rested = 0f;
			int samples = 0, outside = 0;
			for (double t = 0.0; t < period; t += 0.05)
			{
				float d = SeaLifePlacement.LurkAt(t, period, 0.37f);
				Assert.That(d, Is.InRange(0f, 1f));
				Assert.AreEqual(d, SeaLifePlacement.LurkAt(t + period * 1000.0, period, 0.37f), 1e-3f, "a pure function of the clock");
				samples++;
				outside += d > 0f ? 1 : 0;
				rested = Mathf.Max(rested, d);
			}
			Assert.AreEqual(SeaLifePlacement.LurkDartShare, outside / (float)samples, 0.02f, "out of its den only for the dart");
			Assert.Greater(rested, 0.99f, "a full dart");
			Assert.IsTrue(SeaLifePlacement.OnFloor(new SeaCreatureKind { Behaviour = SeaCreatureBehaviour.Lurk }));
			Assert.IsFalse(SeaLifePlacement.OnFloor(new SeaCreatureKind { Behaviour = SeaCreatureBehaviour.School, NearFloor = true }), "a near-floor swimmer still swims");
			Assert.AreEqual((int)SeaCreatureShape.Crab + 1, (int)SeaCreatureShape.Eel, "appended");
			Assert.AreEqual((int)SeaCreatureBehaviour.Crawl + 1, (int)SeaCreatureBehaviour.Lurk, "appended");
			StringAssert.Contains("Eel, 6, ", Read(Shaders + "/FishSeaLife.shader")); // 2026-10-25: Mola 7, Nautilus 8 appended after it
			StringAssert.Contains("case SeaCreatureShape.Eel: return 6f;", Read("Assets/FishMMO/Modules/AmbientLife/Client/SeaLife/SeaLifeSystem.cs"));
			StringAssert.Contains("h = h * 31 + (kind.NearFloor ? 1 : 0);", Read("Assets/FishMMO/Modules/AmbientLife/Client/SeaLife/SeaLifeSystem.cs"), "a change re-places the cells");
		}
	}
}
