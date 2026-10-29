using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FishMMO.Shared;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// The biome suite's way and cave lights (2026-10-26, NEW-LIGHTS): the kinds appended to <see cref="BiomeWayLightKind"/>, their
	/// table (placement class, emitter, standing twin), their kit pieces, the beacon's own fire kindled at dusk (ScenePyre), the
	/// beacons laid on lookouts, and that nothing changes where no biome names one.
	/// </summary>
	public class WayLightSuiteTests
	{
		private const int Seed = 12345;

		/// <summary>Every suite light in its number order (13 on): the append-only pin.</summary>
		private static readonly string[] SuiteNames =
		{
			"Beacon", "SignalPyre", "WartFire", "BlubberPyre", "FivePits", "AmberLamp", "CarvedLantern", "StoneLantern", "PaperLantern", "PaperLanternPost",
			"NicheLamp", "NicheLampPost", "Bellglow", "LampPillar", "Fanal", "PiercedJar", "PiercedJarPillar", "FireAltar", "IronCruse", "IronCrusePost",
			"SlabHearth", "Svetets", "IceLantern", "SunLens", "SergeLamp", "KolaSpike", "CrescentStandard", "ClayLamp", "ClayLampPost", "DrownedLamp",
			"TeaTorch", "HearthRing", "Polycandelon", "CastorLamp", "CastorLampPost", "Kriseilu", "WheelLamp", "VolvaMirror", "Plummet", "FrogLamp",
			"HornBole", "HornBolePillar", "Chiragh", "ChiraghPillar", "SaltLamp", "SaltLampCairn", "SoapstoneBowl", "Kokeul", "KokeulPillar", "TrayFire",
			"TuituiTorch", "BarkTorch", "KazanFire", "BrimLamp", "BrimLampPost", "PishaStake", "CopperKandil", "Candelabrum", "NoduleLamp", "Lurelight",
			"NaphthaLamp", "Hearthwarden", "Tenamaztli", "KukuiTorch", "PohoLamp", "Lucerna", "LucernaPost", "VigilLamp", "Shellwick", "Gledpan",
			"WispLantern", "Alminha", "GourdLantern", "LampCairn", "WaysideLantern", "LanterneDesMorts", "CandleNiche",
		};

		private static IEnumerable<BiomeWayLightKind> All => (BiomeWayLightKind[])Enum.GetValues(typeof(BiomeWayLightKind));

		private static IEnumerable<BiomeWayLightKind> Suite => All.Where(k => k >= BiomeWayLights.FirstSuiteLight);

		// ── The table ─────────────────────────────────────────────────

		[Test]
		public void SuiteKinds_AreAppended_InOrder_AndTheTableCoversEveryKind()
		{
			Assert.That((int)BiomeWayLights.FirstSuiteLight, Is.EqualTo(13), "after HangingBracket (12)");
			string[] names = Enum.GetNames(typeof(BiomeWayLightKind));
			Assert.That(names.Skip(13), Is.EqualTo(SuiteNames));
			Assert.That(BiomeWayLights.Count, Is.EqualTo(names.Length), "one table row a kind");
			Assert.That(All.Select(k => (int)k), Is.EqualTo(Enumerable.Range(0, names.Length)), "no gaps");
			Assert.That(names.Length, Is.LessThan(256), "stored as a byte");
			// No two kinds the same ignoring case (decision 97 C4: one NicheLamp, Cave + Coral Reef forms).
			Assert.That(names.Select(n => n.ToLowerInvariant()).Distinct().Count(), Is.EqualTo(names.Length));
			// Decision 97 R1: no real deity's name or epithet (the old-fire figure is an invented hearth-warden).
			Assert.That(names, Has.None.Contains("God").And.None.Contains("Lord"));
		}

		[Test]
		public void OldKinds_KeepTheirMountsAndPosts()
		{
			Assert.That(BiomeWayLights.MountOf(BiomeWayLightKind.WallTorch), Is.EqualTo(BiomeWayLightMount.Wall));
			Assert.That(BiomeWayLights.MountOf(BiomeWayLightKind.WallLantern), Is.EqualTo(BiomeWayLightMount.Wall));
			Assert.That(BiomeWayLights.MountOf(BiomeWayLightKind.HangingBracket), Is.EqualTo(BiomeWayLightMount.Wall));
			Assert.That(BiomeWayLights.MountOf(BiomeWayLightKind.HangingLantern), Is.EqualTo(BiomeWayLightMount.Roof));
			foreach (BiomeWayLightKind k in All.Where(k => k < BiomeWayLights.FirstSuiteLight))
			{
				bool wall = k == BiomeWayLightKind.WallTorch || k == BiomeWayLightKind.WallLantern || k == BiomeWayLightKind.HangingBracket;
				if (!wall && k != BiomeWayLightKind.HangingLantern)
				{
					Assert.That(BiomeWayLights.MountOf(k), Is.EqualTo(BiomeWayLightMount.Ground), k.ToString());
					Assert.That(BiomeWayLights.PostFor(k), Is.EqualTo(k));
				}
				Assert.That(BiomeWayLights.StandsInStead(k), Is.False, $"{k} keeps its old cave handling");
			}
			Assert.That(BiomeWayLights.PostFor(BiomeWayLightKind.HangingLantern), Is.EqualTo(BiomeWayLightKind.LanternPost));
			Assert.That(BiomeWayLights.PostFor(BiomeWayLightKind.WallTorch), Is.EqualTo(BiomeWayLightKind.TorchPost));
			Assert.That(BiomeWayLights.PostFor(BiomeWayLightKind.WallLantern), Is.EqualTo(BiomeWayLightKind.LanternPost));
			Assert.That(BiomeWayLights.PostFor(BiomeWayLightKind.HangingBracket), Is.EqualTo(BiomeWayLightKind.CrookLantern));
			// A number no kind has (a bad asset's) reads as a lantern post.
			Assert.That(BiomeWayLights.ClassOf((BiomeWayLightKind)250), Is.EqualTo(BiomeWayLightClass.Post));
			Assert.That(BiomeWayLights.PostFor((BiomeWayLightKind)250), Is.EqualTo((BiomeWayLightKind)250));
		}

		[Test]
		public void SuiteKinds_MountByClass_AndEveryWallLightHasATwinGivingTheSameLight()
		{
			foreach (BiomeWayLightKind k in Suite)
			{
				BiomeWayLightClass c = BiomeWayLights.ClassOf(k);
				BiomeWayLightMount mount = BiomeWayLights.MountOf(k);
				BiomeWayLightMount expected = c == BiomeWayLightClass.Wall || c == BiomeWayLightClass.Niche ? BiomeWayLightMount.Wall
					: c == BiomeWayLightClass.Hanging ? BiomeWayLightMount.Roof : BiomeWayLightMount.Ground;
				Assert.That(mount, Is.EqualTo(expected), k.ToString());
				BiomeWayLightKind post = BiomeWayLights.PostFor(k);
				if (mount == BiomeWayLightMount.Ground)
				{
					Assert.That(post, Is.EqualTo(k), $"{k} stands as itself");
					Assert.That(BiomeWayLights.StandsInStead(k), Is.False);
					continue;
				}
				Assert.That(BiomeWayLights.MountOf(post), Is.EqualTo(BiomeWayLightMount.Ground), $"{k}'s twin {post} stands");
				Assert.That(BiomeWayLights.EmitterOf(post), Is.EqualTo(BiomeWayLights.EmitterOf(k)), $"{k}'s twin {post} gives the same light");
				Assert.That(BiomeWayLights.StandsInStead(k), Is.True);
			}
			// The drafts' wall-or-post pairs, and the niche lamp's two forms (decision 97 C4: Cave's niche, Coral Reef's carved post).
			Assert.That(BiomeWayLights.PostFor(BiomeWayLightKind.NicheLamp), Is.EqualTo(BiomeWayLightKind.NicheLampPost));
			Assert.That(BiomeWayLights.PostFor(BiomeWayLightKind.Chiragh), Is.EqualTo(BiomeWayLightKind.ChiraghPillar));
			Assert.That(BiomeWayLights.PostFor(BiomeWayLightKind.HornBole), Is.EqualTo(BiomeWayLightKind.HornBolePillar));
			Assert.That(BiomeWayLights.ClassOf(BiomeWayLightKind.Beacon), Is.EqualTo(BiomeWayLightClass.Beacon));
			Assert.That(Suite.Count(k => BiomeWayLights.ClassOf(k) == BiomeWayLightClass.Beacon), Is.EqualTo(5), "the beacon and its four looks");
		}

		[Test]
		public void ColdLights_GlowWithATint_AndTheFireLightsTurnToGlowOffAir()
		{
			var cold = Suite.Where(k => BiomeWayLights.EmitterOf(k) == SceneEmitterKind.Glow).ToArray();
			Assert.That(cold, Is.EquivalentTo(new[]
			{
				BiomeWayLightKind.Bellglow, BiomeWayLightKind.SunLens, BiomeWayLightKind.CrescentStandard, BiomeWayLightKind.DrownedLamp, BiomeWayLightKind.VolvaMirror,
				BiomeWayLightKind.Plummet, BiomeWayLightKind.NoduleLamp, BiomeWayLightKind.Lurelight, BiomeWayLightKind.Gledpan, BiomeWayLightKind.WispLantern,
			}));
			foreach (BiomeWayLightKind k in Suite)
			{
				bool glows = BiomeWayLights.EmitterOf(k) == SceneEmitterKind.Glow;
				Assert.That(BiomeGlowLights.Glows(k), Is.EqualTo(glows), k.ToString());
				Assert.That(StructurePieces.GlowHueOf(BiomeWayLights.PieceId(k)).HasValue, Is.EqualTo(glows), $"{k}: a cold light has its tint, a flame none");
				// Decisions 41/62: a cave light that burns turns to the cold Glow where the air will not burn it; a glow stays.
				foreach (BiomeCaveLightPlace place in (BiomeCaveLightPlace[])Enum.GetValues(typeof(BiomeCaveLightPlace)))
				{
					BiomeCaveLight off = BiomeCaveInteriors.ForAir(new BiomeCaveLight(place, k, 15f), false);
					Assert.That(BiomeGlowLights.Glows(off.Light), Is.True, $"{k} at {place} off air");
					Assert.That(off.Light == k, Is.EqualTo(glows));
					Assert.That(BiomeCaveInteriors.ForAir(new BiomeCaveLight(place, k, 15f), true).Light, Is.EqualTo(k), "on breathable air as named");
				}
			}
			Assert.That(StructurePieces.GlowHueOf(nameof(BiomeWayLightKind.Lurelight)), Is.EqualTo(SceneGlowHue.Blue));
			Assert.That(StructurePieces.GlowHueOf(nameof(BiomeWayLightKind.SunLens)), Is.EqualTo(SceneGlowHue.Amber));
			// The I6g hues are untouched.
			Assert.That(StructurePieces.GlowHueOf("RuneStone"), Is.EqualTo(SceneGlowHue.Rune));
			Assert.That(StructurePieces.GlowHueOf("Glowjar"), Is.EqualTo(SceneGlowHue.Faerie));
			Assert.That(StructurePieces.GlowHueOf(StructurePieces.CrystalPostId), Is.Null);
		}

		[Test]
		public void CaveFloorPlaces_StandAWallLightsTwin_OldKindsAsBefore()
		{
			var lights = new List<BiomeCaveLight>
			{
				new BiomeCaveLight(BiomeCaveLightPlace.Route, BiomeWayLightKind.NicheLamp),
				new BiomeCaveLight(BiomeCaveLightPlace.Camp, BiomeWayLightKind.TeaTorch),
				new BiomeCaveLight(BiomeCaveLightPlace.MineDrift, BiomeWayLightKind.SaltLamp, 10f),
				new BiomeCaveLight(BiomeCaveLightPlace.MineStope, BiomeWayLightKind.Lucerna),
				new BiomeCaveLight(BiomeCaveLightPlace.MineLamp, BiomeWayLightKind.FrogLamp),
			};
			Assert.That(BiomeCaveLights.TryFor(lights, BiomeCaveLightPlace.Route, out BiomeCaveLight route) && route.Light == BiomeWayLightKind.NicheLampPost, Is.True);
			Assert.That(BiomeCaveLights.TryFor(lights, BiomeCaveLightPlace.Camp, out BiomeCaveLight camp) && camp.Light == BiomeWayLightKind.TorchPost, Is.True);
			Assert.That(BiomeCaveLights.TryFor(lights, BiomeCaveLightPlace.MineStope, out BiomeCaveLight stope) && stope.Light == BiomeWayLightKind.LucernaPost, Is.True);
			// Where the dressing hangs lights on the rock, the wall light itself.
			Assert.That(BiomeCaveLights.TryFor(lights, BiomeCaveLightPlace.MineDrift, out BiomeCaveLight drift) && drift.Light == BiomeWayLightKind.SaltLamp && drift.Spacing == 10f, Is.True);
			Assert.That(BiomeCaveLights.TryFor(lights, BiomeCaveLightPlace.MineLamp, out BiomeCaveLight lamp) && lamp.Light == BiomeWayLightKind.FrogLamp, Is.True);
			// The old wall kinds named for a floor place stay as they were (the dressing notes them unfit and lays none).
			var old = new List<BiomeCaveLight> { new BiomeCaveLight(BiomeCaveLightPlace.Route, BiomeWayLightKind.WallTorch), new BiomeCaveLight(BiomeCaveLightPlace.Camp, BiomeWayLightKind.HangingLantern) };
			Assert.That(BiomeCaveLights.TryFor(old, BiomeCaveLightPlace.Route, out BiomeCaveLight r) && r.Light == BiomeWayLightKind.WallTorch, Is.True);
			Assert.That(BiomeCaveLights.TryFor(old, BiomeCaveLightPlace.Camp, out BiomeCaveLight c) && c.Light == BiomeWayLightKind.HangingLantern, Is.True);
			// The default list reads exactly as before.
			foreach (BiomeCaveLight d in BiomeCaveLights.Default())
			{
				Assert.That(BiomeCaveLights.TryFor(BiomeCaveLights.Default(), d.Place, out BiomeCaveLight got) && got.Light == d.Light, Is.True, d.ToString());
			}
		}

		// ── The beacon's fire ─────────────────────────────────────────

		[Test]
		public void Pyre_IsAppended_AndItsSeedCarriesItsLook()
		{
			Assert.That((int)SceneEmitterKind.Pyre, Is.EqualTo((int)SceneEmitterKind.Skylight + 1));
			Assert.That(Enum.GetNames(typeof(SceneEmitterKind)).Last(), Is.EqualTo("Pyre"));
			foreach (uint hash in new uint[] { 0u, 0xFFFFFFFFu, 0x12345678u, 0x00030000u })
			{
				foreach (bool dusk in new[] { false, true })
				{
					foreach (bool blue in new[] { false, true })
					{
						uint seed = ScenePyre.WithLook(hash, dusk, blue);
						Assert.That(ScenePyre.AtDusk(seed), Is.EqualTo(dusk));
						Assert.That(ScenePyre.Blue(seed), Is.EqualTo(blue));
						Assert.That(seed & ~(ScenePyre.DuskBit | ScenePyre.BlueBit), Is.EqualTo(hash & ~(ScenePyre.DuskBit | ScenePyre.BlueBit)), "the rest kept");
						Assert.That(seed & 0xFFFFu, Is.EqualTo(hash & 0xFFFFu), "the flicker's phase kept");
					}
				}
			}
		}

		[Test]
		public void Beacon_IsDarkByDay_KindledAtDusk_EachAtItsOwnMoment()
		{
			var moments = new List<float>();
			for (uint i = 0; i < 64; i++)
			{
				uint seed = ScenePyre.WithLook(i * 0x9E3779B9u, true, false);
				Assert.That(ScenePyre.Kindled(0f, seed), Is.EqualTo(0f), "out by day");
				Assert.That(ScenePyre.Kindled(1f, seed), Is.EqualTo(1f), "burning by night");
				Assert.That(ScenePyre.Light(123.4f, 0f, seed), Is.EqualTo(0f), "no light by day");
				float last = 0f;
				for (int s = 0; s <= 100; s++)
				{
					float k = ScenePyre.Kindled(s / 100f, seed);
					Assert.That(k, Is.GreaterThanOrEqualTo(last - 1e-6f), "catches, never dims, as the dusk deepens");
					Assert.That(k, Is.InRange(0f, 1f));
					last = k;
				}
				float at = ScenePyre.KindleAt(seed);
				Assert.That(at, Is.InRange(ScenePyre.FirstKindled, ScenePyre.LastKindled));
				moments.Add(at);
			}
			Assert.That(moments.Max() - moments.Min(), Is.GreaterThan(0.3f), "a line of beacons is lit one after another");
			// The same seed, the same instant for every player (a pure function of the dusk and the seed).
			uint one = ScenePyre.WithLook(0xABCDEF01u, true, false);
			Assert.That(ScenePyre.Kindled(0.4f, one), Is.EqualTo(ScenePyre.Kindled(0.4f, one)));
			// A fire that is no beacon burns day and night; a brimstone flame is blue.
			uint fire = ScenePyre.WithLook(0x1234u, false, false), blue = ScenePyre.WithLook(0x1234u, false, true);
			Assert.That(ScenePyre.Kindled(0f, fire), Is.EqualTo(1f));
			Assert.That(ScenePyre.Light(10f, 0f, fire), Is.GreaterThan(0.5f));
			Color b = ScenePyre.Colour(blue), o = ScenePyre.Colour(fire);
			Assert.That(b.b, Is.GreaterThan(b.r));
			Assert.That(o.r, Is.GreaterThan(o.b));
			Assert.That(ScenePyre.Range(ScenePyre.WithLook(0u, true, false)), Is.EqualTo(ScenePyre.BeaconRange));
			Assert.That(ScenePyre.Range(blue), Is.EqualTo(ScenePyre.BlueRange));
		}

		[Test]
		public void PyreLook_IsTheBeaconsAndTheBrimstoneLamps()
		{
			foreach (BiomeWayLightKind k in All)
			{
				StructurePieces.PyreLookOf(BiomeWayLights.PieceId(k), out bool dusk, out bool blue);
				Assert.That(dusk, Is.EqualTo(BiomeWayLights.ClassOf(k) == BiomeWayLightClass.Beacon), k.ToString());
				Assert.That(blue, Is.EqualTo(k == BiomeWayLightKind.BrimLamp || k == BiomeWayLightKind.BrimLampPost), k.ToString());
				Assert.That(BiomeWayLights.EmitterOf(k) == SceneEmitterKind.Pyre, Is.EqualTo(dusk || blue), $"{k}: a Pyre has a look");
			}
			foreach (string other in new[] { "Lighthouse", "13", "beacon", "", "CampfireRing" })
			{
				StructurePieces.PyreLookOf(other, out bool dusk, out bool blue);
				Assert.That(dusk || blue, Is.False, other);
			}
		}

		[Test]
		public void ClientReadsThePyre_AndTheLighthouseBeamsDoNot()
		{
			string pool = File.ReadAllText("Assets/Scripts/Client/World/Terrain/SiteLightPool.cs");
			Assert.That(pool, Does.Contain("case SceneEmitterKind.Pyre:").And.Contain("ScenePyre.Light(time, night, e.Seed)"));
			string embers = File.ReadAllText("Assets/Scripts/Client/World/Terrain/SiteEmbers.cs");
			Assert.That(embers, Does.Contain("kind == SceneEmitterKind.Pyre").And.Contain("ScenePyre.Kindled(night, e.Seed)"));
			string beams = File.ReadAllText("Assets/Scripts/Client/World/Terrain/LighthouseBeams.cs");
			Assert.That(beams, Does.Not.Contain("Pyre"), "a beacon's pyre has no mirrors");
			string site = File.ReadAllText("Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World/PointsOfInterest/PointOfInterestSiteContext.cs");
			Assert.That(site, Does.Contain("ScenePyre.WithLook(seed, atDusk, blue)"));
		}

		// ── The kit ───────────────────────────────────────────────────

		[Test]
		public void EveryKind_IsALightPiece_LaidByIdOnly_AndLastInTheKit()
		{
			foreach (BiomeWayLightKind k in Suite)
			{
				string id = BiomeWayLights.PieceId(k);
				StructurePiece piece = StructureKit.Get(id);
				Assert.That(piece, Is.Not.Null, id);
				Assert.That(piece.Tags, Is.EqualTo(new[] { "light" }), $"{id}: laid by id only");
				Assert.That(piece.Variants, Is.EqualTo(2), id);
				Assert.That(piece.Collides, Is.EqualTo(BiomeWayLights.MountOf(k) == BiomeWayLightMount.Ground
					&& k != BiomeWayLightKind.HearthRing && k != BiomeWayLightKind.Tenamaztli && k != BiomeWayLightKind.PohoLamp), $"{id}: walked past on a wall, over on the ground");
				Assert.That(StructureKitPieceSource.Resolves(BiomeWayLights.PieceQuery(k)), Is.True, id);
			}
			// Appended in one run after every piece before them (the cave story's), so no earlier piece moved in the table.
			IReadOnlyList<StructurePiece> all = StructurePieces.All;
			int first = all.Select(p => p.Id).ToList().IndexOf(SuiteNames[0]);
			Assert.That(all.Skip(first).Take(SuiteNames.Length).Select(p => p.Id), Is.EqualTo(SuiteNames));
			Assert.That(all.Take(first).Select(p => p.Id), Does.Contain("Hearth").Or.Contain("CampfireRing"), "after the older pieces");
			string kit = File.ReadAllText("Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World/ProceduralArt/StructurePieces.cs");
			Assert.That(kit.IndexOf("AddWayLights(list);", StringComparison.Ordinal), Is.GreaterThan(kit.IndexOf("AddCaveStory(list);", StringComparison.Ordinal)));
			string payload = File.ReadAllText("Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World/ProceduralArt/ProceduralArtPayload.cs");
			Assert.That(payload, Does.Contain("\"StructurePieces.WayLights.cs\""));
		}

		/// <summary>Builds meshes: Unity or nativerun only.</summary>
		[Test]
		public void EveryPiece_GivesOffItsEmitter_StandsOrHangsAsItsClass_InItsBudget()
		{
			foreach (BiomeWayLightKind k in Suite)
			{
				string id = BiomeWayLights.PieceId(k);
				StructurePiece piece = StructureKit.Get(id);
				for (int v = 0; v < piece.Variants; v++)
				{
					IReadOnlyList<(Vector3 at, SceneEmitterKind kind)> emitters = StructurePieces.EmittersOf(piece, v);
					Assert.That(emitters.Count(e => e.kind == BiomeWayLights.EmitterOf(k)), Is.GreaterThanOrEqualTo(1), $"{id} v{v} gives off its {BiomeWayLights.EmitterOf(k)}");
					Assert.That(emitters.All(e => e.kind == BiomeWayLights.EmitterOf(k)), Is.True, $"{id} v{v} gives off only its own light");
					StructureMesh lod0 = StructurePieces.Build(piece, v, 0, Seed);
					Assert.That(lod0.Mesh.TriangleCount, Is.LessThanOrEqualTo(piece.TriangleBudget), $"{id} v{v}: {lod0.Mesh.TriangleCount} triangles");
					Bounds b = lod0.Mesh.Bounds;
					switch (BiomeWayLights.ClassOf(k))
					{
						case BiomeWayLightClass.Wall:
						case BiomeWayLightClass.Niche:
							Assert.That(b.max.z, Is.InRange(0f, 0.05f), $"{id} v{v}'s back is at z = 0");
							Assert.That(b.min.z, Is.LessThan(-0.2f), $"{id} v{v} stands out along −z");
							Assert.That(Mathf.Abs(b.center.y), Is.LessThan(0.45f), $"{id} v{v} is centred on its fixing");
							break;
						case BiomeWayLightClass.Bowl:
							Assert.That(b.max.y, Is.GreaterThan(0.15f), $"{id} v{v} stands up from the ground");
							break;
						default:
							Assert.That(b.max.y, Is.GreaterThan(0.8f), $"{id} v{v} stands up from the ground");
							break;
					}
					foreach ((Vector3 at, SceneEmitterKind _) in emitters)
					{
						Vector3 near = Vector3.Max(b.min, Vector3.Min(b.max, at));
						Assert.That((near - at).magnitude, Is.LessThan(0.45f), $"{id} v{v}'s light is at the piece ({at})");
					}
				}
			}
		}

		// ── Placement and defaults ────────────────────────────────────

		[Test]
		public void Beacons_AreNamedOnlyByABiomesOwnList()
		{
			Assert.That(PointOfInterestWayBeacons.TryBeacon(null, POIType.Tower, out _), Is.False);
			Assert.That(PointOfInterestWayBeacons.StandsOnGround(BiomeWayLightKind.Beacon), Is.False, "the iron basket wants a tower");
			Assert.That(PointOfInterestWayBeacons.StandsOnGround(BiomeWayLightKind.SignalPyre), Is.True);
			Assert.That(PointOfInterestWayBeacons.StandsOnGround(BiomeWayLightKind.LanternPost), Is.False);
			// The old rule by kind names none, so no unauthored biome lays one.
			foreach (BiomeWayLight light in BiomeWayLights.Default())
			{
				Assert.That(BiomeWayLights.ClassOf(light.Light), Is.Not.EqualTo(BiomeWayLightClass.Beacon));
			}
			Assert.That(PointOfInterestWayBeacons.Lookouts(null), Is.Empty);
			Assert.That(PointOfInterestWayBeacons.Lookouts(new List<PointOfInterestSiteContext.PlacedProp>()), Is.Empty);
		}

		[Test]
		public void Defaults_NoSpecEntryOrTemplateNamesASuiteLight()
		{
			int entries = 0;
			foreach (string path in Directory.GetFiles("Assets", "*.asset", SearchOption.AllDirectories).Where(p => p.Replace('\\', '/').Contains("/Biomes/") || p.Replace('\\', '/').Contains("Templates/World"))
				.Where(p => !BiomeAuthoredRounds.Contains(Path.GetFileNameWithoutExtension(p))))
			{
				string[] lines = File.ReadAllLines(path);
				for (int at = 0; at < lines.Length; at++)
				{
					string head = lines[at].Trim();
					if (head != "wayLights:" && head != "caveLights:")
					{
						continue;
					}
					for (int i = at + 1; i < lines.Length && (lines[i].StartsWith("  - ", StringComparison.Ordinal) || lines[i].StartsWith("    ", StringComparison.Ordinal)); i++)
					{
						string t = lines[i].Trim();
						if (t.StartsWith("Light: ", StringComparison.Ordinal))
						{
							entries++;
							Assert.That(int.Parse(t.Substring(7)), Is.LessThan((int)BiomeWayLights.FirstSuiteLight), $"{Path.GetFileName(path)}: {t}");
						}
					}
				}
			}
			Assert.That(entries, Is.GreaterThan(300), "the templates' way and cave lights were read");
			// The biome spec files name none either until their round authors them (an authored round's are its own, BiomeAuthoredRoundTests).
			foreach (string path in Directory.GetFiles("Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World/ProceduralArt", "BiomeArtSpec*.cs", SearchOption.AllDirectories)
				.Where(p => !BiomeAuthoredRounds.All.Any(b => Path.GetFileName(p) == "BiomeArtSpec." + b.Replace(" ", string.Empty) + ".cs")))
			{
				string text = File.ReadAllText(path);
				foreach (string name in SuiteNames)
				{
					Assert.That(text, Does.Not.Contain("BiomeWayLightKind." + name + ")").And.Not.Contain("BiomeWayLightKind." + name + ","), $"{Path.GetFileName(path)} names {name}");
				}
			}
		}
	}
}
