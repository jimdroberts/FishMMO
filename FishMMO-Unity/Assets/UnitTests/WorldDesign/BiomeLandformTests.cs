using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Phase I2g of the biome suite (2026-10-18, decisions 25, 26, 36(e), 38–50): sculpted landforms carved at the cut. Every shape
	/// family is deterministic, bounded and meets the untouched ground at its edge; the carve restores bit for bit; the presets cover
	/// every landform a draft asked for; and with every biome's list empty (the default) nothing changes.
	/// </summary>
	public class BiomeLandformTests
	{
		private const string BiomeFolder = "Assets/Templates/Entity/Biomes";
		private const string World = "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World";
		private const string TemplateSource = "Assets/Scripts/Shared/Implementation/Tools/Biomes/BiomeTemplate.cs";

		private static string Read(string relative) => File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), relative)).Replace("\r\n", "\n");

		/// <summary>Every landform name the drafts ask for (approved sections and section 6), with a few of the drafts' spellings.</summary>
		private static readonly string[] DraftNames =
		{
			"Pingo", "Palsa", "ThawSlump", "RockGlacier", "Cornice", "Amba", "ImpactCrater", "AbyssalHills", "StarfallCrater", "MudVolcano",
			"BrineLake", "FanValley", "SedimentWaves", "SlideScar", "Sarn", "Faru", "BlueHole", "Kandu", "Cryodome", "MoatedMassif",
			"FlowLobeSteps", "DustFill", "GhostCrater", "Kipuka", "BoulderTracks", "StoneRiver", "StarDune", "Seif", "Nebkha", "Guelta",
			"Paleolake", "Foredune", "ParabolicDune", "Blowout", "Rills", "CaprockMesa", "Wash", "CitadelCrag", "Wallow", "Playa",
			"PlatformMound", "ConicalMound", "PillowMound", "SiegeDitch", "Motte", "Kofun", "Tell", "Dibin", "TravertineTerraces", "RuinMound",
			"TerraceScarp", "Dosso", "FontanileHead", "LateralMoraine", "AlluvialFan", "TidalCreeks", "Terp", "Warft", "SeaDike", "SaltPannes",
			"Kunhalom", "MudflowFan", "CrevasseField", "Moulin", "SupraglacialChannels", "MedialMoraine", "TerminalMoraine", "KettlePonds",
			"Ogives", "SnoutPortal", "Tinaja", "FinField", "VolcanicNeck", "Arroyo", "ExplosionCrater", "PaintedHills",
			// The drafts' and the brief's other spellings.
			"Palaeolake", "Hallig mound", "Wierde", "Priele", "Erg", "Barchan", "pingos", "ghost craters", "kipukas", "tidal creeks", "terps",
			"salt pannes", "mesas", "travertine terraces", "impact crater", "sea dike", "flow-lobe steps", "dust-fill", "Stone River",
			"boulder tracks", "kettle ponds", "lateral moraine", "alluvial fans", "rock glaciers", "thaw slumps", "palsas", "pillow mounds",
		};

		// ── Defaults: nothing changes ─────────────────────────────────

		[Test]
		public void Defaults_NoSpecAndNoTemplateCarvesAnything()
		{
			Assert.That(BiomeArtSpec.Entries.Length, Is.EqualTo(83));
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries)
			{
				Assert.That(entry.LandformList(), Is.Empty, $"{entry.Biome} names no landform yet");
			}
			int templates = 0;
			foreach (string path in Directory.GetFiles(BiomeFolder, "*.asset"))
			{
				string text = File.ReadAllText(path);
				if (!Regex.IsMatch(text, "^  MinTemperature:", RegexOptions.Multiline))
				{
					continue;
				}
				templates++;
				Assert.That(Regex.IsMatch(text, @"^  landforms:\s*\n  - ", RegexOptions.Multiline), Is.False, $"{Path.GetFileName(path)} carves a landform");
			}
			Assert.That(templates, Is.EqualTo(83));
			StringAssert.Contains("private List<BiomeLandform> landforms = new List<BiomeLandform>();", Read(TemplateSource));
			Assert.That(BiomeLandforms.Any(BiomeLandforms.None()), Is.False);
			Assert.That(default(BiomeLandform).Carves, Is.False, "an empty entry carves nothing");
			Assert.That(default(BiomeLandform).Paints, Is.False, "an empty entry paints nothing");
		}

		[Test]
		public void Plan_WithNoLandforms_PlacesNothing_AndTheCarveChangesNothing()
		{
			LandformPlanInput input = Input(new BiomeLandform[0]);
			Assert.That(SceneLandforms.Plan(input), Is.Empty);
			float[] metres = Ground(out BiomeLandforms.Grid grid);
			float[] before = (float[])metres.Clone();
			BiomeLandforms.Carving carving = BiomeLandforms.Carve(grid, new List<BiomeLandformSite>(), new BiomeLandforms.Context());
			Assert.That(carving.Count, Is.Zero);
			Assert.That(metres, Is.EqualTo(before));
		}

		[Test]
		public void Forms_AreAppendOnly_AndKnowTheirFamily()
		{
			Assert.That((byte)BiomeLandformForm.Dome, Is.EqualTo(0x00));
			Assert.That((byte)BiomeLandformForm.Star, Is.EqualTo(0x05));
			Assert.That((byte)BiomeLandformForm.Impact, Is.EqualTo(0x10));
			Assert.That((byte)BiomeLandformForm.SpillFill, Is.EqualTo(0x23));
			Assert.That((byte)BiomeLandformForm.Rimstone, Is.EqualTo(0x31));
			Assert.That((byte)BiomeLandformForm.Scarp, Is.EqualTo(0x44));
			Assert.That((byte)BiomeLandformForm.Pass, Is.EqualTo(0x54));
			Assert.That((byte)BiomeLandformForm.FlowSteps, Is.EqualTo(0x63));
			Assert.That((byte)BiomeLandformForm.Parabolic, Is.EqualTo(0x74));
			Assert.That((byte)BiomeLandformForm.Crevasses, Is.EqualTo(0x84));
			foreach (BiomeLandformForm form in (BiomeLandformForm[])Enum.GetValues(typeof(BiomeLandformForm)))
			{
				Assert.That(Enum.IsDefined(typeof(BiomeLandformFamily), BiomeLandforms.FamilyOf(form)), form.ToString());
			}
			Assert.That(BiomeLandforms.FamilyOf(BiomeLandformForm.Tongue), Is.EqualTo(BiomeLandformFamily.Lobe));
			Assert.That(BiomeLandforms.FamilyOf(BiomeLandformForm.Linear), Is.EqualTo(BiomeLandformFamily.Dunes));
		}

		// ── Presets and the spec hook ─────────────────────────────────

		[Test]
		public void Presets_CoverEveryDraftLandform()
		{
			foreach (string name in DraftNames)
			{
				Assert.That(BiomeLandformPresets.TryGet(name, out BiomeLandform form), Is.True, $"no preset for '{name}'");
				Assert.That(form.Carves, Is.True, name);
			}
			var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (BiomeLandformPresets.Preset preset in BiomeLandformPresets.All)
			{
				Assert.That(names.Add(preset.Name), Is.True, $"{preset.Name} twice");
				Assert.That(preset.Form.Kind, Is.EqualTo(preset.Name));
				Assert.That(Enum.IsDefined(typeof(BiomeLandformForm), preset.Form.Form), preset.Name);
				Assert.That(preset.Form.PaintGround < 0 || preset.Form.PaintStrength > 0f, Is.True, preset.Name);
			}
			Assert.That(BiomeLandformPresets.TryGet("Palaeolake", out BiomeLandform palaeo) && palaeo.Kind == "Paleolake", Is.True);
			Assert.That(BiomeLandformPresets.TryGet("Hallig mound", out BiomeLandform hallig) && hallig.Kind == "Warft", Is.True);
			Assert.That(BiomeLandformPresets.Knows("Not A Landform"), Is.False);
			Assert.That(BiomeLandformPresets.Named("Not A Landform").Carves, Is.False, "an unknown name carves nothing");
		}

		[Test]
		public void SpecHook_ByNameAndTuned()
		{
			var entry = new BiomeArtSpec.Entry { Biome = "Test" };
			Assert.That(entry.LandformList(), Is.Empty);
			entry.Landforms("Pingo", "palsas");
			List<BiomeLandform> list = entry.LandformList();
			Assert.That(list.ConvertAll(f => f.Kind), Is.EqualTo(new[] { "Pingo", "Palsa" }));
			entry.Landforms(BiomeLandformPresets.Named("Tell").Painted(3, BiomeLandformPart.Top, 0.5f).AtDensity(2f).Capped(1, 3));
			BiomeLandform tell = entry.LandformList()[0];
			Assert.That(tell.PaintGround, Is.EqualTo(3));
			Assert.That(tell.PaintPart, Is.EqualTo(BiomeLandformPart.Top));
			Assert.That(tell.PaintStrength, Is.EqualTo(0.5f));
			Assert.That(tell.Density, Is.EqualTo(2f));
			Assert.That(tell.PerPatch, Is.EqualTo(1));
			Assert.That(tell.PerScene, Is.EqualTo(3));
			Assert.That(tell.ToString(), Is.Not.EqualTo(BiomeLandformPresets.Named("Tell").ToString()), "a tuned entry prints differently (its mark)");
			Assert.That(BiomeLandformPresets.Named("Tell").ToString(), Is.EqualTo(BiomeLandformPresets.Named("tell").ToString()), "a print is stable");
		}

		// ── The test ground ───────────────────────────────────────────

		private const int Cells = 96;
		private const float CellMetres = 16f;
		private const float Half = Cells * CellMetres * 0.5f;
		private const float Spacing = 4f;
		private const float Sea = -20f;

		/// <summary>A tilted plane (about 6°) with gentle undulations, its west under the sea.</summary>
		private static float GroundAt(float x, float z) => 0.1f * x + 15f * Mathf.Sin(x / 97f) * Mathf.Cos(z / 133f) + 6f * Mathf.Sin(z / 41f + x / 67f);

		private static float[] Ground(out BiomeLandforms.Grid grid)
		{
			int n = Mathf.RoundToInt(2f * Half / Spacing) + 1;
			var metres = new float[n * n];
			for (int z = 0; z < n; z++)
			{
				for (int x = 0; x < n; x++)
				{
					metres[z * n + x] = GroundAt(-Half + x * Spacing, -Half + z * Spacing);
				}
			}
			grid = new BiomeLandforms.Grid(metres, n, n, Spacing, -Half, -Half);
			return metres;
		}

		private static LandformPlanInput Input(IReadOnlyList<BiomeLandform> forms, uint seed = 7u)
		{
			return new LandformPlanInput
			{
				Seed = seed,
				CellsX = Cells,
				CellsZ = Cells,
				CellMetres = CellMetres,
				Origin = new Vector2(-Half, -Half),
				CellBiome = (x, z) => 0,
				Biomes = new List<(int, IReadOnlyList<BiomeLandform>)> { (1001, forms) },
				Ground = GroundAt,
				SeaLevel = Sea,
				WindYaw = 30f,
				SceneBounds = new Vector4(-Half, -Half, Half, Half),
			};
		}

		/// <summary>A biome weight that fades to nothing at the scene's edge, so field landforms meet the ground there.</summary>
		private static BiomeLandforms.Context Context() => new BiomeLandforms.Context
		{
			SeaLevel = Sea,
			BiomeWeight = (id, x, z) => Mathf.Clamp01((Half - 40f - Mathf.Max(Mathf.Abs(x), Mathf.Abs(z))) / 200f),
		};

		/// <summary>A preset opened up so the test ground can hold it: any slope, depth and climate; generous density; no water to be near.</summary>
		private static BiomeLandform Opened(BiomeLandform form)
		{
			form.Slope = Vector2.zero;
			form.Depth = Vector2.zero;
			form.Temperature = Vector2.zero;
			form.Humidity = Vector2.zero;
			if (form.Anchor == BiomeLandformAnchor.NearWater || form.Anchor == BiomeLandformAnchor.SlopeBreak)
			{
				form.Anchor = BiomeLandformAnchor.Anywhere;
			}
			if (form.Placement == BiomeLandformPlacement.Scatter)
			{
				form.Density = Mathf.Max(form.Density, 4f);
			}
			// Keep every footprint inside the 1.5 km test scene.
			form.Size = new Vector2(Mathf.Min(form.Size.x, 300f), Mathf.Min(form.Size.y, 300f));
			form.Length = new Vector2(Mathf.Min(form.Length.x, 500f), Mathf.Min(form.Length.y, 500f));
			form.Width = new Vector2(Mathf.Min(form.Width.x, 200f), Mathf.Min(form.Width.y, 200f));
			return form;
		}

		private static List<BiomeLandformSite> PlanPreset(BiomeLandformPresets.Preset preset)
		{
			var forms = new List<BiomeLandform>();
			if (preset.Form.Form == BiomeLandformForm.Pass)
			{
				forms.Add(Opened(BiomeLandformPresets.Named("Faru")));
			}
			forms.Add(Opened(preset.Form));
			List<BiomeLandformSite> sites = SceneLandforms.Plan(Input(forms));
			return sites.FindAll(s => s.Form.Kind == preset.Name);
		}

		// ── Every family: placed, deterministic, bounded, continuous ──

		[Test]
		public void EveryPreset_IsPlaced_AndThePlanIsDeterministic()
		{
			var unplaced = new List<string>();
			foreach (BiomeLandformPresets.Preset preset in BiomeLandformPresets.All)
			{
				List<BiomeLandformSite> a = PlanPreset(preset), b = PlanPreset(preset);
				if (a.Count == 0)
				{
					unplaced.Add(preset.Name);
					continue;
				}
				Assert.That(b.Count, Is.EqualTo(a.Count), preset.Name);
				for (int i = 0; i < a.Count; i++)
				{
					Assert.That(b[i].Centre, Is.EqualTo(a[i].Centre), preset.Name);
					Assert.That(b[i].Seed, Is.EqualTo(a[i].Seed), preset.Name);
					Assert.That(b[i].Height, Is.EqualTo(a[i].Height), preset.Name);
					Assert.That(b[i].Bounds, Is.EqualTo(a[i].Bounds), preset.Name);
					Assert.That(b[i].Segments?.Count ?? 0, Is.EqualTo(a[i].Segments?.Count ?? 0), preset.Name);
				}
			}
			Assert.That(unplaced, Is.Empty, "presets the test ground could not hold");
			var other = SceneLandforms.Plan(Input(new[] { Opened(BiomeLandformPresets.Named("Pingo")) }, 8u));
			var first = SceneLandforms.Plan(Input(new[] { Opened(BiomeLandformPresets.Named("Pingo")) }, 7u));
			Assert.That(other.Count > 0 && first.Count > 0 && other[0].Centre != first[0].Centre, Is.True, "another scene seed places them elsewhere");
		}

		[Test]
		public void EveryFamily_IsDeterministic_Bounded_AndMeetsTheGroundAtItsEdge()
		{
			BiomeLandforms.Context context = Context();
			foreach (BiomeLandformPresets.Preset preset in BiomeLandformPresets.All)
			{
				foreach (BiomeLandformSite site in PlanPreset(preset))
				{
					var shape = new BiomeLandforms.Shape(site, context);
					var again = new BiomeLandforms.Shape(site, context);
					Vector4 b = shape.Bounds;
					Assert.That(b.z > b.x && b.w > b.y, Is.True, preset.Name);
					var flat = new BiomeLandforms.GroundInfo { Slope = 25f, Downhill = new Vector2(-1f, 0f) };
					float worst = 0f;
					// Inside: the same answer twice, never NaN.
					for (int k = 0; k < 400; k++)
					{
						float x = Mathf.Lerp(b.x, b.z, BiomeLandforms.Unit(BiomeLandforms.Hash(site.Seed, (uint)k, 1u)));
						float z = Mathf.Lerp(b.y, b.w, BiomeLandforms.Unit(BiomeLandforms.Hash(site.Seed, (uint)k, 2u)));
						float g = GroundAt(x, z);
						float h1 = shape.Apply(x, z, g, g, flat), h2 = again.Apply(x, z, g, g, flat);
						Assert.That(float.IsNaN(h1) || float.IsInfinity(h1), Is.False, preset.Name);
						Assert.That(h1, Is.EqualTo(h2), $"{preset.Name} is not deterministic");
						for (BiomeLandformPart part = BiomeLandformPart.Whole; part <= BiomeLandformPart.Crest; part++)
						{
							float m = shape.Mask(part, x, z, g, 10f);
							Assert.That(m >= 0f && m <= 1.0001f, Is.True, $"{preset.Name} {part} mask {m}");
						}
					}
					// Outside its box: untouched exactly. On its box's edge: within a centimetre of the ground.
					for (int k = 0; k < 256; k++)
					{
						float t = k / 256f;
						Vector2[] edge =
						{
							new Vector2(Mathf.Lerp(b.x, b.z, t), b.y + 0.01f), new Vector2(Mathf.Lerp(b.x, b.z, t), b.w - 0.01f),
							new Vector2(b.x + 0.01f, Mathf.Lerp(b.y, b.w, t)), new Vector2(b.z - 0.01f, Mathf.Lerp(b.y, b.w, t)),
						};
						foreach (Vector2 p in edge)
						{
							float g = GroundAt(p.x, p.y);
							worst = Mathf.Max(worst, Mathf.Abs(shape.Apply(p.x, p.y, g, g, flat) - g));
						}
						Vector2 outside = new Vector2(Mathf.Lerp(b.x, b.z, t), b.w + 1f);
						float go = GroundAt(outside.x, outside.y);
						Assert.That(shape.Apply(outside.x, outside.y, go, go, flat), Is.EqualTo(go), $"{preset.Name} reaches past its bounds");
					}
					Assert.That(worst, Is.LessThan(0.01f), $"{preset.Name} ({site.Form.Form}) steps {worst} m at its edge");
				}
			}
		}

		[Test]
		public void Shapes_DoWhatTheirFamilySays()
		{
			var context = new BiomeLandforms.Context { SeaLevel = Sea };
			var flat = new BiomeLandforms.GroundInfo();
			BiomeLandformSite Site(string name, float radius, float height, float level = float.NaN)
			{
				BiomeLandform form = BiomeLandformPresets.Named(name);
				form.Roughness = 0f;
				var s = new BiomeLandformSite { Form = form, Centre = Vector2.zero, Radius = radius, HalfLength = radius, HalfWidth = 2f, Height = height, Level = level, Seed = 5u };
				s.Bounds = BiomeLandforms.BoundsOf(s);
				return s;
			}
			var pingo = new BiomeLandforms.Shape(Site("Pingo", 20f, 10f), context);
			Assert.That(pingo.Apply(8f, 0f, 0f, 0f, flat), Is.GreaterThan(5f), "a pingo stands up");
			Assert.That(pingo.Apply(0f, 0f, 0f, 0f, flat), Is.EqualTo(10f - 1.5f).Within(1e-4f), "with its summit pond sunk into its top");
			var terp = new BiomeLandforms.Shape(Site("Terp", 100f, 5f, 5f), context);
			Assert.That(terp.Apply(0f, 0f, 0f, 0f, flat), Is.EqualTo(5f).Within(1e-4f), "a terp's top is flat at its level");
			Assert.That(terp.Apply(30f, 0f, 1f, 1f, flat), Is.EqualTo(5f).Within(1e-4f), "over sloping ground too");
			var playa = new BiomeLandforms.Shape(Site("Playa", 100f, 1.5f, -1.5f), context);
			Assert.That(playa.Apply(20f, 0f, 0.4f, 0.4f, flat), Is.EqualTo(-1.5f).Within(1e-4f), "a playa's floor is flat");
			var crater = new BiomeLandforms.Shape(Site("StarfallCrater", 40f, 16f), context);
			Assert.That(crater.Apply(0f, 0f, 0f, 0f, flat), Is.LessThan(-10f), "a crater's bowl");
			Assert.That(crater.Apply(40f, 0f, 0f, 0f, flat), Is.GreaterThan(0f), "its raised rim");
		}

		// ── The carve: record and restore ─────────────────────────────

		[Test]
		public void Carve_RestoreThenCarve_IsExactlyOneCarve()
		{
			var forms = new List<BiomeLandform>();
			foreach (string name in new[] { "Pingo", "Terp", "ImpactCrater", "Playa", "CaprockMesa", "Sarn", "TidalCreeks", "AlluvialFan", "Seif", "Polygons", "DustFill", "TravertineTerraces", "LateralMoraine", "Wallow" })
			{
				if (BiomeLandformPresets.TryGet(name, out BiomeLandform form))
				{
					forms.Add(Opened(form));
				}
			}
			forms.Add(Opened(new BiomeLandform { Kind = "TestPolygons", Form = BiomeLandformForm.Polygons, Placement = BiomeLandformPlacement.Field, Width = new Vector2(12f, 12f), Height = new Vector2(0.4f, 0.4f) }));
			List<BiomeLandformSite> sites = SceneLandforms.Plan(Input(forms));
			Assert.That(sites.Count, Is.GreaterThan(5));
			float[] metres = Ground(out BiomeLandforms.Grid grid);
			float[] original = (float[])metres.Clone();
			Func<float, float, float> keep = (x, z) => Mathf.Clamp01(1f - Mathf.Abs(x - 300f) / 20f);
			BiomeLandforms.Carving first = BiomeLandforms.Carve(grid, sites, Context(), keep);
			Assert.That(first.Count, Is.GreaterThan(1000));
			float[] once = (float[])metres.Clone();
			BiomeLandforms.Restore(metres, first);
			for (int i = 0; i < metres.Length; i++)
			{
				Assert.That(BitConverter.SingleToInt32Bits(metres[i]), Is.EqualTo(BitConverter.SingleToInt32Bits(original[i])), $"sample {i} not restored");
			}
			BiomeLandforms.Carving second = BiomeLandforms.Carve(grid, sites, Context(), keep);
			Assert.That(second.Count, Is.EqualTo(first.Count));
			for (int i = 0; i < metres.Length; i++)
			{
				Assert.That(BitConverter.SingleToInt32Bits(metres[i]), Is.EqualTo(BitConverter.SingleToInt32Bits(once[i])), $"sample {i} carved differently");
			}
			// Held ground stays exactly as it was.
			int i300 = Mathf.RoundToInt((300f + Half) / Spacing);
			for (int z = 0; z < grid.Depth; z += 7)
			{
				Assert.That(metres[z * grid.Width + i300], Is.EqualTo(original[z * grid.Width + i300]), "the water's ground is held");
			}
			// CONTROL: carving again without restoring piles a second carve on the first.
			BiomeLandforms.Carve(grid, sites, Context(), keep);
			bool differs = false;
			for (int i = 0; i < metres.Length && !differs; i++)
			{
				differs = metres[i] != once[i];
			}
			Assert.That(differs, Is.True, "a carve without its restore must not look idempotent");
		}

		[Test]
		public void Pads_OfferTopsMirrorsPoolsAndBoulders()
		{
			var context = new BiomeLandforms.Context { SeaLevel = Sea };
			var terp = new BiomeLandformSite { Form = BiomeLandformPresets.Named("Terp"), BiomeID = 3, Centre = new Vector2(10f, 20f), Radius = 100f, HalfLength = 100f, Height = 5f, Level = 9f, Seed = 1u };
			var brine = new BiomeLandformSite { Form = BiomeLandformPresets.Named("BrineLake"), BiomeID = 3, Centre = Vector2.zero, Radius = 50f, HalfLength = 50f, Height = 3f, Level = -103f, Seed = 2u };
			var pingo = new BiomeLandformSite { Form = BiomeLandformPresets.Named("Pingo"), Radius = 20f, HalfLength = 20f, Height = 10f, Seed = 3u };
			List<BiomeLandformPad> pads = SceneLandforms.Pads(new[] { terp, brine, pingo }, context, (x, z) => 9f);
			Assert.That(pads.Count, Is.EqualTo(2), "the pingo offers no pad");
			Assert.That(pads[0].Kind, Is.EqualTo("Terp"));
			Assert.That(pads[0].Radius, Is.EqualTo(55f).Within(1e-3f), "a terp's flat top");
			Assert.That(pads[1].Water, Is.True, "a brine lake's mirror");
			Assert.That(pads[1].Level, Is.InRange(-103f, -100f), "below its rim, never under its floor");
		}

		[Test]
		public void DuneMix_PartitionsThePatch()
		{
			float[] lows = { 0f, 0.3f, 0.7f }, highs = { 0.3f, 0.7f, 1f };
			for (float n = 0f; n < 1f; n += 0.001f)
			{
				float sum = 0f;
				for (int k = 0; k < 3; k++)
				{
					sum += BiomeLandforms.Shape.MixBand(n, lows[k], highs[k]);
				}
				Assert.That(sum, Is.EqualTo(1f).Within(1e-4f), $"at {n}");
			}
		}

		// ── Wiring ────────────────────────────────────────────────────

		[Test]
		public void Wiring_CarvedBeforeTheFinalWater_StoredBeforeThePaint_PaintedAfterTheMargins()
		{
			string ground = Read(World + "/SceneGround.cs");
			int carve = ground.IndexOf("SceneLandforms.Carve(", StringComparison.Ordinal);
			int final = ground.IndexOf("SceneWater final = SceneWater.Build(", StringComparison.Ordinal);
			Assert.That(carve, Is.GreaterThan(0));
			Assert.That(carve, Is.LessThan(final), "landforms are carved before the final water is laid");
			Assert.That(carve, Is.GreaterThan(ground.IndexOf("SceneErosion.Apply(", StringComparison.Ordinal)), "and after the erosion");
			string generator = Read(World + "/SceneGenerator.cs");
			int store = generator.IndexOf("SceneLandforms.Store(scene, landforms)", StringComparison.Ordinal);
			Assert.That(store, Is.GreaterThan(0));
			Assert.That(store, Is.LessThan(generator.IndexOf("if (!PaintBiomes(scene, request", StringComparison.Ordinal)), "stored before the paint reads it");
			int margins = generator.IndexOf("ChannelMarginPaint.Paint(scene, tiles, palette, result.Notes);", StringComparison.Ordinal);
			int paint = generator.IndexOf("LandformPaint.Paint(scene, tiles, palette, field, result.Notes);", StringComparison.Ordinal);
			Assert.That(paint, Is.GreaterThan(margins));
			Assert.That(paint, Is.LessThan(generator.IndexOf("TerrainArraySetup.Apply(scene, tiles, palette", StringComparison.Ordinal)), "before the arrays and the scatter");
			StringAssert.Contains("AuthorLandforms(biome, entry, report, apply);", Read(World + "/ProceduralArt/BiomeArtAuthoring.cs"));
			StringAssert.Contains("public static IReadOnlyList<BiomeLandform> Landforms(BiomeTemplate biome)", Read(World + "/Terrain/BiomeTemplateArt.cs"));
			StringAssert.Contains("public const string Landforms = nameof(landforms);", Read(TemplateSource));
		}
	}
}
