#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public sealed partial class PointOfInterestCaveDressing
	{
		private sealed partial class Dresser
		{
			/// <summary>The system's lava (<see cref="CaveLava.Plan"/>), worked out once when first asked.</summary>
			private CaveLavaPlan lavaPlan;
			/// <summary>What the lava pass laid: its pieces, its lava lights and its fumes.</summary>
			public int LavaPieces, LavaLights, LavaFumes;

			private CaveLavaPlan LavaPlan => lavaPlan ??= CaveLava.Plan(system) ?? CaveLavaPlan.Cooled;

			/// <summary>
			/// Whether a prop of the passes before the lava's would stand in molten lava (its foot in a body's footprint, under its
			/// surface): <see cref="Place"/> refuses it. Asked only after the pass has drawn all it draws for the prop, so no stream
			/// moves; never true in a cooled system, whose dressing is exactly what it was.
			/// </summary>
			private bool InLava(Vector3 local) => LavaPlan.Active && CaveLavaDressing.InLava(LavaPlan, local);

			/// <summary>
			/// Whether a glowing crystal of the passes before the lava's gives way to it (2026-10-20: a molten tube "full of glowing
			/// crystals reads oddly"): every one within <see cref="CaveLavaDressing.CrystalClear"/> of the lava, and of the rest all
			/// but <see cref="CaveLavaDressing.CrystalShare"/> by a hash of where it stands. Asked in <see cref="Place"/> after the
			/// pass's draws, as <see cref="InLava"/> is (no stream moves; a crystal not laid gives no light); never in a cooled cave.
			/// </summary>
			private bool LavaOutshines(GameObject prefab, Vector3 local)
			{
				if (!LavaPlan.Active || !CaveLavaDressing.IsCrystalArt(prefab.name))
				{
					return false;
				}
				if (CaveLavaDressing.DistanceToLava(LavaPlan, local, 6f) < CaveLavaDressing.CrystalClear)
				{
					return true;
				}
				uint h = PointOfInterestPlanner.Hash(system.Seed, Mathf.RoundToInt(local.x * 10f), Mathf.RoundToInt(local.z * 10f), 0x3C7);
				return PointOfInterestPlanner.Unit(h) >= CaveLavaDressing.CrystalShare;
			}

			/// <summary>
			/// A lava tube that still runs (2026-10-20, PointOfInterestCaveDressing.Lava.cs): its glowing cracks, seeps, lavacicles
			/// and crust, its lava lights and its fumes (<see cref="CaveLavaDressing"/>). Last of all the passes, on its own stream,
			/// its lights spaced off the glow's, so nothing laid before it moves; nothing at all in a cooled system.
			/// </summary>
			private void Lava()
			{
				CaveLavaPlan plan = LavaPlan;
				if (!plan.Active || plan.Bodies.Count == 0)
				{
					return;
				}
				CaveLavaDressingLayout layout = CaveLavaDressing.Lay(system, plan, (p, margin) => OnFeature(p, margin) || NearMouth(p) || Submerged(p), glowLights);
				foreach (CaveLavaPiece piece in layout.Pieces)
				{
					if (!CaveArt.TryGlow(piece.Kind, piece.Hue, piece.Variant, out CaveGlowArt art))
					{
						Missing.Add("cave lava art");
						continue;
					}
					// Not through Place: a seep's foot and the crust at the lava's edge stand at its margin on purpose.
					context.AddProp(art.Prefab, system.Origin + piece.Position, piece.Rotation, Vector3.one * piece.Scale);
					Placed++;
					LavaPieces++;
				}
				if (context.Points == null)
				{
					return;
				}
				context.Points.Emitters ??= new List<SceneEmitter>();
				foreach (SceneEmitter e in layout.Emitters)
				{
					SceneEmitter world = e;
					world.Position = system.Origin + e.Position;
					context.Points.Emitters.Add(world);
					if (e.Kind == SceneEmitterKind.Magma)
					{
						LavaLights++;
					}
					else
					{
						LavaFumes++;
					}
				}
			}
		}
	}

	/// <summary>One piece of a lava tube's dressing (<see cref="CaveLavaDressing"/>): its glow art, where (system frame), how turned and how large.</summary>
	public struct CaveLavaPiece
	{
		public CaveGlowKind Kind;
		public int Hue, Variant;
		public Vector3 Position;
		public Quaternion Rotation;
		public float Scale;
	}

	/// <summary>What <see cref="CaveLavaDressing.Lay"/> lays round a system's lava: pieces and emitters in the system's frame, and its counts.</summary>
	public sealed class CaveLavaDressingLayout
	{
		public readonly List<CaveLavaPiece> Pieces = new List<CaveLavaPiece>();
		/// <summary>The lava lights (<see cref="SceneEmitterKind.Magma"/>) and the fumes (<see cref="SceneEmitterKind.Fumes"/>), positions in the system's frame.</summary>
		public readonly List<SceneEmitter> Emitters = new List<SceneEmitter>();
		public int FloorCracks, WallCracks, Seeps, Lavacicles, Crusts, Lights, Fumes;
	}

	/// <summary>
	/// What makes a lava tube that still runs look and feel molten round its lava (2026-10-20: "glowing floor/wall cracks,
	/// magma seeping from walls, glowing lavacicles; heat haze, drifting embers/sparks, sulphur fumes, orange light filling the
	/// tube"), laid round the lava the layout planned (<see cref="CaveLava.Plan"/>).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Spreading from the lava.</b> Cracks in the floor glowing in their depths, thickest at the lava's edge and thinning
	/// out over four metres or so; cracks up the walls near it; magma seeping down the walls that come down to it, into it;
	/// lavacicles hung from the roof over it; ropy crust cooled black along its banks. Molten (yellow-orange) near the lava,
	/// banked (deep red) further off.
	/// </para>
	/// <para>
	/// <b>Light and air.</b> Lava lights (<see cref="SceneEmitterKind.Magma"/>) just over the molten surface, along a river every
	/// <see cref="RiverLightMin"/>–<see cref="RiverLightMax"/> m, over each lake and at a fall's lip and foot, none within
	/// <see cref="LightSpacing"/> of another nor <see cref="GlowLightSpacing"/> of the glow's lights, at most <see cref="MaxLights"/>:
	/// as bright, high and far-reaching as their body is wide (SceneMagma.Brightness, RiseFor, ReachFor), they fill the tube with
	/// orange light and win their place in the client's pool before the glow's (SceneMagma.PickLights); the client throws embers
	/// and heat haze off them. The tube's glowing crystals give way to its lava (<see cref="CrystalClear"/>, <see cref="CrystalShare"/>).
	/// Fumes (<see cref="SceneEmitterKind.Fumes"/>) hang under the roof where it stands high enough over the lava, sparser still.
	/// </para>
	/// <para>
	/// <b>Pure and deterministic.</b> A function of the system and its plan (and what the caller refuses), on a stream of its
	/// own: the same cave dresses the same, and the passes before it never see it. Every station draws the same numbers
	/// whether it lays anything or not. A cooled plan lays nothing.
	/// </para>
	/// </remarks>
	public static class CaveLavaDressing
	{
		/// <summary>The Magma hues' indices (ProceduralArtCatalogue.MagmaHues): fresh yellow-orange, banked deep red.</summary>
		public const int MoltenHue = 0, BankedHue = 1;

		/// <summary>The least distance between two lava lights, metres: the glow pass's own spacing.</summary>
		public const float LightSpacing = 9f;
		/// <summary>
		/// The least distance between a lava light and a light already in the system (the glow's), metres: only so two never stand
		/// together. The lava's win a place in the client's pool before the glow's (SceneMagma.PickLights), so it need not keep
		/// the glow's own spacing (2026-10-20, first render: a tube's hundred crystal lights had crowded its lava dark).
		/// </summary>
		public const float GlowLightSpacing = 3f;
		/// <summary>
		/// In a tube that still runs, no glowing crystal stands within this of its lava, metres (its own light outshines theirs),
		/// and of the rest only <see cref="CrystalShare"/> stand (by a hash of their place): a molten tube glows with its magma.
		/// </summary>
		public const float CrystalClear = 8f, CrystalShare = 0.35f;

		/// <summary>Whether a generated prefab's name is a glowing crystal's (the glow art's crystals and giant crystals, the cave crystals).</summary>
		public static bool IsCrystalArt(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				return false;
			}
			string p = ProceduralArtCatalogue.CavePrefix;
			return name.StartsWith(p + CaveGlowKind.Crystal + "_", StringComparison.Ordinal) || name.StartsWith(p + CaveGlowKind.GiantCrystal + "_", StringComparison.Ordinal)
				|| name.StartsWith(p + "Mineral_", StringComparison.Ordinal);
		}

		/// <summary>
		/// How far a point (system frame) lies from any of a plan's lava in plan view, metres: 0 over it, the distance to the nearest
		/// body's edge beside it; bodies whose surface is more than <paramref name="rise"/> above or below the point are not counted
		/// (another level). Infinity for a cooled plan or none in reach.
		/// </summary>
		public static float DistanceToLava(CaveLavaPlan plan, Vector3 p, float rise)
		{
			float best = float.PositiveInfinity;
			if (plan == null || !plan.Active)
			{
				return best;
			}
			foreach (CaveLavaBody body in plan.Bodies)
			{
				Vector3[] path = body.Path;
				if (path == null || path.Length == 0)
				{
					continue;
				}
				if (body.Kind == CaveLavaKind.Lake || path.Length == 1)
				{
					Vector3 c = path[0];
					if (Mathf.Abs(p.y - c.y) <= rise)
					{
						float r = body.Kind == CaveLavaKind.Lake ? body.Width : 0.5f * body.Width;
						best = Mathf.Min(best, Mathf.Max(0f, new Vector2(p.x - c.x, p.z - c.z).magnitude - r));
					}
					continue;
				}
				for (int i = 0; i + 1 < path.Length; i++)
				{
					Vector3 a = path[i], b = path[i + 1];
					var ab = new Vector2(b.x - a.x, b.z - a.z);
					var ap = new Vector2(p.x - a.x, p.z - a.z);
					float t = Mathf.Clamp01(Vector2.Dot(ap, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
					if (Mathf.Abs(p.y - Mathf.Lerp(a.y, b.y, t)) <= rise)
					{
						best = Mathf.Min(best, Mathf.Max(0f, (ap - ab * t).magnitude - 0.5f * body.Width));
					}
				}
			}
			return best;
		}
		/// <summary>Lava lights along a river, metres apart (about).</summary>
		public const float RiverLightMin = 11f, RiverLightMax = 14f;
		/// <summary>The most lava lights a system is given.</summary>
		public const int MaxLights = 40;
		/// <summary>The least distance between two fumes, metres, and the most a system is given.</summary>
		public const float FumeSpacing = 16f;
		public const int MaxFumes = 12;
		/// <summary>The most pieces a system is given.</summary>
		public const int MaxPieces = 1600;
		/// <summary>How far the cracks spread out over a floor from the lava's edge, metres.</summary>
		public const float CrackSpread = 4.5f;
		/// <summary>How high up a wall over the lava its cracks reach, metres.</summary>
		public const float WallCrackRise = 3.2f;
		/// <summary>How far over the molten surface a lava light stands, metres.</summary>
		public const float LightRise = 0.5f;
		/// <summary>The least room between the lava and the roof for lavacicles and for fumes, metres.</summary>
		public const float LavacicleRoom = 2.5f, FumeRoom = 4f;

		/// <summary>
		/// Whether a point (system frame) lies in molten lava: inside a body's footprint (a river's or fall's half-width, a lake's
		/// radius, and <paramref name="margin"/> more) and under its surface there (and <paramref name="above"/> more), down to 30 m
		/// under it (a level below is not the lava's). False for a cooled plan.
		/// </summary>
		public static bool InLava(CaveLavaPlan plan, Vector3 p, float margin = 0.3f, float above = 0.25f)
		{
			if (plan == null || !plan.Active)
			{
				return false;
			}
			foreach (CaveLavaBody body in plan.Bodies)
			{
				Vector3[] path = body.Path;
				if (path == null || path.Length == 0)
				{
					continue;
				}
				if (body.Kind == CaveLavaKind.Lake || path.Length == 1)
				{
					Vector3 c = path[0];
					float r = (body.Kind == CaveLavaKind.Lake ? body.Width : 0.5f * body.Width) + margin;
					if (new Vector2(p.x - c.x, p.z - c.z).sqrMagnitude < r * r && p.y < c.y + above && p.y > c.y - 30f)
					{
						return true;
					}
					continue;
				}
				float reach = 0.5f * body.Width + margin;
				for (int i = 0; i + 1 < path.Length; i++)
				{
					Vector3 a = path[i], b = path[i + 1];
					var ab = new Vector2(b.x - a.x, b.z - a.z);
					var ap = new Vector2(p.x - a.x, p.z - a.z);
					float t = Mathf.Clamp01(Vector2.Dot(ap, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
					float y = Mathf.Lerp(a.y, b.y, t);
					if ((ap - ab * t).sqrMagnitude < reach * reach && p.y < y + above && p.y > y - 30f)
					{
						return true;
					}
				}
			}
			return false;
		}

		/// <summary>
		/// The dressing round a system's lava: nothing for a cooled plan. <paramref name="refused"/> (a point on a floor, a margin)
		/// is true where nothing may be laid (the layout's ways, the mouths' daylight, the water); <paramref name="lights"/> are the
		/// lights already in the system (its frame), which the lava's keep their distance from.
		/// </summary>
		public static CaveLavaDressingLayout Lay(CaveSystem system, CaveLavaPlan plan, Func<Vector3, float, bool> refused = null, IReadOnlyList<Vector3> lights = null)
		{
			var layout = new CaveLavaDressingLayout();
			if (system == null || plan == null || !plan.Active || plan.Bodies.Count == 0)
			{
				return layout;
			}
			new Layer(system, plan, refused, lights, layout).Run();
			return layout;
		}

		private sealed class Layer
		{
			private readonly CaveSystem system;
			private readonly CaveLavaPlan plan;
			private readonly Func<Vector3, float, bool> refused;
			private readonly CaveLavaDressingLayout layout;
			private readonly DeterministicRNG rng;
			/// <summary>The lights already in the system (the glow's), and the lava's own.</summary>
			private readonly List<Vector3> lit = new List<Vector3>(), own = new List<Vector3>();
			private readonly List<Vector3> fumes = new List<Vector3>();

			public Layer(CaveSystem system, CaveLavaPlan plan, Func<Vector3, float, bool> refused, IReadOnlyList<Vector3> lights, CaveLavaDressingLayout layout)
			{
				this.system = system;
				this.plan = plan;
				this.refused = refused;
				this.layout = layout;
				rng = new DeterministicRNG(unchecked(system.Seed * 0x6B43 + 0x2C9D));
				if (lights != null)
				{
					lit.AddRange(lights);
				}
			}

			public void Run()
			{
				foreach (CaveLavaBody body in plan.Bodies)
				{
					if (body.Path == null || body.Path.Length == 0)
					{
						continue;
					}
					switch (body.Kind)
					{
						case CaveLavaKind.Lake:
							Lake(body);
							break;
						case CaveLavaKind.Fall:
							Fall(body);
							break;
						default:
							River(body);
							break;
					}
				}
			}

			// ── Bodies ────────────────────────────────────────────

			/// <summary>
			/// A river: both banks dressed every metre down it, seeps where a wall comes down to it every 5–10 m, lavacicles over it
			/// every 4–7 m, its lights every 11–14 m and its fumes every 18–26 m.
			/// </summary>
			private void River(CaveLavaBody body)
			{
				float half = Mathf.Max(0.3f, 0.5f * body.Width);
				Vector3[] path = body.Path;
				if (path.Length == 1)
				{
					Pool(path[0], half, SceneMagmaBody.River);
					return;
				}
				float length = Length(path);
				float nextLight = rng.Range(1f, 4f), nextFume = rng.Range(4f, 10f), nextDrip = rng.Range(1f, 4f);
				float nextSeepL = rng.Range(2f, 8f), nextSeepR = rng.Range(2f, 8f);
				for (float s = 0.5f; s < length; s += 1f)
				{
					At(path, s, out Vector3 c, out Vector3 along);
					var across = new Vector3(along.z, 0f, -along.x);
					Bank(c, across, along, half);
					Bank(c, -across, along, half);
					if (s >= nextSeepL)
					{
						nextSeepL = s + rng.Range(5f, 10f);
						Seep(c, across, half);
					}
					if (s >= nextSeepR)
					{
						nextSeepR = s + rng.Range(5f, 10f);
						Seep(c, -across, half);
					}
					if (s >= nextDrip)
					{
						nextDrip = s + rng.Range(4f, 7f);
						Drips(c + across * (rng.Range(-0.6f, 0.6f) * half), c.y);
					}
					// Where a light or fumes cannot go (under rock, too near another), tried again a little further down.
					if (s >= nextLight)
					{
						nextLight = Light(c, half, SceneMagmaBody.River) ? s + rng.Range(RiverLightMin, RiverLightMax) : s + 1f;
					}
					if (s >= nextFume)
					{
						nextFume = Fume(c, half) ? s + rng.Range(18f, 26f) : s + 2f;
					}
				}
			}

			/// <summary>A lake: its rim dressed every metre round it, seeps every 6–10 m of it, lavacicles over it, lights and fumes over it.</summary>
			private void Lake(CaveLavaBody body) => Pool(body.Path[0], Mathf.Max(0.5f, body.Width), SceneMagmaBody.Lake);

			private void Pool(Vector3 centre, float radius, SceneMagmaBody kind)
			{
				Light(centre, radius, kind);
				// A great lake: more lights round it, two-thirds of the way out, a dozen metres apart.
				if (radius > 9f)
				{
					float ring = 0.7f * radius;
					int count = Mathf.Max(3, Mathf.RoundToInt(2f * Mathf.PI * ring / 12f));
					float turn = rng.Range(0f, 2f * Mathf.PI);
					for (int i = 0; i < count; i++)
					{
						float a = turn + 2f * Mathf.PI * i / count;
						Light(centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * ring, radius, kind);
					}
				}
				Fume(centre, radius);
				int steps = Mathf.Max(8, Mathf.RoundToInt(2f * Mathf.PI * radius));
				float nextSeep = rng.Range(2f, 6f), round = 0f, start = rng.Range(0f, 2f * Mathf.PI);
				for (int i = 0; i < steps; i++, round += 2f * Mathf.PI * radius / steps)
				{
					float a = start + 2f * Mathf.PI * i / steps;
					var outward = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
					var along = new Vector3(-outward.z, 0f, outward.x);
					Bank(centre, outward, along, radius);
					if (round >= nextSeep)
					{
						nextSeep = round + rng.Range(6f, 10f);
						Seep(centre, outward, radius);
					}
				}
				int drips = Mathf.Clamp(Mathf.RoundToInt(Mathf.PI * radius * radius / 14f), 1, 30);
				for (int i = 0; i < drips; i++)
				{
					float a = rng.Range(0f, 2f * Mathf.PI), r = 0.85f * radius * Mathf.Sqrt(rng.NextFloat());
					Drips(centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r, centre.y);
				}
			}

			/// <summary>
			/// A fall: lights at its lip and at its foot, magma seeping and cracks up the rock either side of it down its height,
			/// cracks round its lip, fumes over its foot. Its plunge pool is a lake of its own.
			/// </summary>
			private void Fall(CaveLavaBody body)
			{
				float half = Mathf.Max(0.3f, 0.5f * body.Width);
				Vector3[] path = body.Path;
				Vector3 lip = path[0], foot = path[path.Length - 1];
				Light(lip, half, SceneMagmaBody.Fall);
				Light(foot, half, SceneMagmaBody.Fall);
				Fume(foot, half);
				Vector3 run = foot - lip;
				var flat = new Vector3(run.x, 0f, run.z);
				Vector3 along = flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward;
				var across = new Vector3(along.z, 0f, -along.x);
				float length = Mathf.Max(0.01f, Length(path));
				for (float s = 0.4f; s < length; s += 0.8f)
				{
					At(path, s, out Vector3 c, out _);
					for (int side = -1; side <= 1; side += 2)
					{
						float roll = rng.NextFloat(), size = rng.NextFloat(), spin = rng.Range(0f, 360f);
						int variant = rng.Range(0, CaveArt.VariantCount(CaveGlowKind.MagmaCrack));
						if (roll < 0.22f)
						{
							// Only where a ledge or the pool is right under it: none hangs out of the face mid-fall.
							Seep(c, across * side, half, grounded: true);
						}
						else if (roll < 0.6f)
						{
							WallCrack(c, across * side, half + 3f, spin, size, variant, MoltenHue);
						}
					}
				}
				// The rock round its lip, cracked and glowing where the lava pours over it.
				for (int k = 0; k < 6; k++)
				{
					float a = rng.Range(0f, 2f * Mathf.PI), d = half + rng.Range(0.2f, 2.5f), turn = rng.Range(-40f, 40f), size = rng.NextFloat();
					int variant = rng.Range(0, CaveArt.VariantCount(CaveGlowKind.MagmaCrack));
					var outward = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
					FloorCrack(lip, outward, d, half, turn, size, variant);
				}
			}

			// ── Banks ─────────────────────────────────────────────

			/// <summary>
			/// One side of the lava at a station: <paramref name="c"/> on its surface, <paramref name="outward"/> away from it across
			/// the bank, <paramref name="edge"/> from <paramref name="c"/> to its edge. A floor crack spreading from it, a skin of crust
			/// at its edge, a crack up the wall over it; each by its own chance, all drawn first.
			/// </summary>
			private void Bank(Vector3 c, Vector3 outward, Vector3 along, float edge)
			{
				float crackRoll = rng.NextFloat(), crackOff = rng.NextFloat(), crackTurn = rng.Range(-40f, 40f), crackSize = rng.NextFloat();
				int crackVariant = rng.Range(0, CaveArt.VariantCount(CaveGlowKind.MagmaCrack));
				float crustRoll = rng.NextFloat(), crustOff = rng.NextFloat(), crustTurn = rng.Range(-25f, 25f), crustSize = rng.NextFloat();
				int crustVariant = rng.Range(0, CaveArt.VariantCount(CaveGlowKind.LavaCrust));
				float wallRoll = rng.NextFloat(), wallUp = rng.NextFloat(), wallSpin = rng.Range(0f, 360f), wallSize = rng.NextFloat();
				int wallVariant = rng.Range(0, CaveArt.VariantCount(CaveGlowKind.MagmaCrack));
				if (crackRoll < 0.42f)
				{
					// Most within a metre or two of the edge, a few out to the spread's end.
					FloorCrack(c, outward, edge + 0.2f + (CrackSpread - 0.2f) * crackOff * crackOff, edge, crackTurn, crackSize, crackVariant);
				}
				if (crustRoll < 0.3f)
				{
					Crust(c, outward, along, edge + 0.05f + 0.8f * crustOff, edge, crustTurn, crustSize, crustVariant);
				}
				if (wallRoll < 0.14f)
				{
					WallCrack(c + Vector3.up * (0.6f + (WallCrackRise - 0.6f) * wallUp), outward, edge + 6f, wallSpin, wallSize, wallVariant, wallUp < 0.25f ? MoltenHue : BankedHue);
				}
			}

			/// <summary>
			/// A crack on the floor <paramref name="d"/> out from <paramref name="c"/> (never so near the edge its inner end hangs over
			/// the lava), running out from it (turned a little), smaller the further out; only where the floor is flat under its length.
			/// </summary>
			private void FloorCrack(Vector3 c, Vector3 outward, float d, float edge, float turn, float size, int variant)
			{
				float out01 = Mathf.Clamp01((d - edge) / CrackSpread);
				float scale = Mathf.Lerp(1.15f, 0.6f, out01) * (0.8f + 0.4f * size);
				float reach = CaveGlowMeshes.MagmaCrackReach[Mathf.Clamp(variant, 0, CaveGlowMeshes.MagmaCrackReach.Length - 1)] * scale;
				d = Mathf.Max(d, edge + 0.1f + reach);
				Vector3 run = Quaternion.Euler(0f, turn, 0f) * outward;
				if (!Floor(c + outward * d, c.y, out Vector3 at) || !Flat(at, run, reach, 0.5f * reach, 0.15f))
				{
					return;
				}
				if (Add(CaveGlowKind.MagmaCrack, d - reach - edge < 1.2f ? MoltenHue : BankedHue, variant, at, Quaternion.FromToRotation(Vector3.right, run), scale))
				{
					layout.FloorCracks++;
				}
			}

			/// <summary>
			/// A skin of ropy crust beside the lava's edge, flowing along it (turned a little): wholly on the bank (none of it over the
			/// lava, where it would hang in the air over a channel's drop), and only where the bank is flat under it.
			/// </summary>
			private void Crust(Vector3 c, Vector3 outward, Vector3 along, float d, float edge, float turn, float size, int variant)
			{
				float scale = 0.6f + 0.6f * size;
				Vector2 extent = 0.5f * scale * CaveGlowMeshes.LavaCrustSizes[Mathf.Clamp(variant, 0, CaveGlowMeshes.LavaCrustSizes.Length - 1)];
				d += 0.1f + extent.y;
				Vector3 flow = Quaternion.Euler(0f, turn, 0f) * along;
				if (!Floor(c + outward * d, c.y, out Vector3 at) || !Flat(at, flow, extent.x, extent.y, 0.15f))
				{
					return;
				}
				if (Add(CaveGlowKind.LavaCrust, d - extent.y - edge < 0.5f ? MoltenHue : BankedHue, variant, at, Quaternion.FromToRotation(Vector3.right, flow), scale))
				{
					layout.Crusts++;
				}
			}

			/// <summary>
			/// Whether the floor under a flat piece at <paramref name="at"/> stays within <paramref name="give"/> of its height out to
			/// <paramref name="length"/> both ways along <paramref name="run"/> and <paramref name="width"/> both ways across it, and is no
			/// lava: so no end of it hangs over a drop or sinks into a rise.
			/// </summary>
			private bool Flat(Vector3 at, Vector3 run, float length, float width, float give)
			{
				var across = new Vector3(run.z, 0f, -run.x);
				foreach (Vector3 offset in new[] { run * length, -run * length, across * width, -across * width })
				{
					Vector3 q = at + offset;
					if (!Ground(new Vector3(q.x, at.y + 0.6f, q.z), 0.6f + give, out float floor) || Mathf.Abs(floor - at.y) > give || InLava(plan, new Vector3(q.x, floor, q.z), 0f, 0.05f))
					{
						return false;
					}
				}
				return true;
			}

			/// <summary>A crack on the wall a ray from <paramref name="from"/> meets, its crust flat on the rock and turned any way about it.</summary>
			private void WallCrack(Vector3 from, Vector3 dir, float reach, float spin, float size, int variant, int hue)
			{
				if (!Wall(from, dir, reach, out Vector3 surface, out Vector3 into) || Refused(new Vector3(surface.x, from.y, surface.z) - into * 0.5f, 0.3f))
				{
					return;
				}
				Quaternion turn = Quaternion.FromToRotation(Vector3.up, -into) * Quaternion.Euler(0f, spin, 0f);
				if (Add(CaveGlowKind.MagmaCrack, hue, variant, surface + into * 0.02f, turn, 0.5f + 0.5f * size))
				{
					layout.WallCracks++;
				}
			}

			/// <summary>
			/// Magma seeping down the wall that comes down to the lava within 2.5 m of its edge, its foot at the wall's foot: its
			/// height fitted under rock (the seep's top must lie on the wall, not stand over a bench into the air). With
			/// <paramref name="grounded"/> (beside a fall), only where a floor is found under it.
			/// </summary>
			private void Seep(Vector3 c, Vector3 outward, float edge, bool grounded = false)
			{
				int variant = rng.Range(0, CaveArt.VariantCount(CaveGlowKind.MagmaSeep));
				float size = rng.NextFloat();
				Vector3 from = c + Vector3.up * 0.35f;
				if (!Wall(from, outward, edge + 2.5f, out Vector3 surface, out Vector3 into))
				{
					return;
				}
				var flatInto = new Vector3(into.x, 0f, into.z);
				if (flatInto.sqrMagnitude < 0.25f)
				{
					return;
				}
				flatInto.Normalize();
				// Its foot on the floor at the wall's foot, or on the lava where the wall goes down into it; none where neither is
				// near (a fall's rock face, high over the pool), or its lobe would stand out of the wall into the air.
				float foot;
				if (Ground(surface - flatInto * 0.3f + Vector3.up * 0.5f, 2.2f, out float floor) && floor >= c.y - 0.3f)
				{
					foot = Mathf.Max(floor, c.y + 0.02f);
				}
				else if (grounded)
				{
					return;
				}
				else
				{
					foot = c.y + 0.02f;
				}
				var at = new Vector3(surface.x, foot, surface.z) + flatInto * 0.02f;
				if (Refused(at - flatInto * 0.5f, 0.3f))
				{
					return;
				}
				float height = CaveGlowMeshes.MagmaSeepHeights[Mathf.Clamp(variant, 0, CaveGlowMeshes.MagmaSeepHeights.Length - 1)];
				// As tall as the wall over it is rock (a few centimetres in), down to half its size, else none.
				for (float scale = 0.8f + 0.4f * size; scale >= 0.5f; scale *= 0.8f)
				{
					if (RockBehind(at, flatInto, height * scale))
					{
						if (Add(CaveGlowKind.MagmaSeep, MoltenHue, variant, at, Quaternion.LookRotation(flatInto, Vector3.up), scale))
						{
							layout.Seeps++;
						}
						return;
					}
				}
			}

			/// <summary>Whether the wall a seep lies on is rock all the way up its height, a hand's breadth in.</summary>
			private bool RockBehind(Vector3 foot, Vector3 into, float height)
			{
				for (float h = 0.25f; h <= height + 1e-3f; h += 0.25f)
				{
					if (system.Void(foot + Vector3.up * h + into * 0.15f) < 0f)
					{
						return false;
					}
				}
				return system.Void(foot + Vector3.up * height + into * 0.15f) >= 0f;
			}

			/// <summary>Lavacicles from the roof over a point of the lava, where it stands high enough over it and no way crosses under them.</summary>
			private void Drips(Vector3 p, float surface)
			{
				int variant = rng.Range(0, CaveArt.VariantCount(CaveGlowKind.Lavacicle));
				float spin = rng.Range(0f, 360f), size = rng.NextFloat(), hot = rng.NextFloat();
				var start = new Vector3(p.x, surface + 0.4f, p.z);
				if (!Roof(start, 30f, out float roof) || roof - surface < LavacicleRoom || Refused(new Vector3(p.x, surface, p.z), 0f))
				{
					return;
				}
				float drop = CaveGlowMeshes.LavacicleDrops[Mathf.Clamp(variant, 0, CaveGlowMeshes.LavacicleDrops.Length - 1)];
				float scale = Mathf.Min((0.7f + 0.6f * size), 0.3f * (roof - surface) / drop);
				if (scale < 0.5f)
				{
					return;
				}
				if (Add(CaveGlowKind.Lavacicle, hot < 0.3f ? MoltenHue : BankedHue, variant, new Vector3(p.x, roof + 0.03f, p.z), Quaternion.Euler(0f, spin, 0f), scale))
				{
					layout.Lavacicles++;
				}
			}

			// ── Light and air ─────────────────────────────────────

			/// <summary>
			/// A lava light over the surface at a point, as high and far-reaching as its body wants (SceneMagma.RiseFor, ReachFor; its
			/// brightness the client's, from the seed), where no other light is within <see cref="LightSpacing"/>.
			/// </summary>
			private bool Light(Vector3 surface, float half, SceneMagmaBody kind)
			{
				Vector3 at = surface + Vector3.up * SceneMagma.RiseFor(kind, half);
				if (system.Void(at) >= 0f)
				{
					// A low roof over it: just over the surface.
					at = surface + Vector3.up * LightRise;
				}
				float reach = SceneMagma.ReachFor(kind, half);
				if (layout.Lights >= MaxLights || system.Void(at) >= 0f || Near(own, at, LightSpacing) || Near(lit, at, GlowLightSpacing))
				{
					return false;
				}
				uint hash = PointOfInterestPlanner.Hash(system.Seed, Mathf.RoundToInt(at.x * 10f), Mathf.RoundToInt(at.z * 10f), 0x1A7);
				layout.Emitters.Add(new SceneEmitter { Position = at, Kind = SceneEmitterKind.Magma, Seed = SceneMagma.Seed(hash, half, kind), Reach = reach });
				own.Add(at);
				layout.Lights++;
				return true;
			}

			/// <summary>Fumes under the roof over a point of the lava, where it stands <see cref="FumeRoom"/> or more over it; spread with the room and the lava's width.</summary>
			private bool Fume(Vector3 surface, float half)
			{
				if (layout.Fumes >= MaxFumes || !Roof(surface + Vector3.up * 0.5f, 40f, out float roof) || roof - surface.y < FumeRoom)
				{
					return false;
				}
				var at = new Vector3(surface.x, roof - 1f, surface.z);
				if (Near(fumes, at, FumeSpacing))
				{
					return false;
				}
				uint hash = PointOfInterestPlanner.Hash(system.Seed, Mathf.RoundToInt(at.x * 10f), Mathf.RoundToInt(at.z * 10f), 0xF5);
				float spread = Mathf.Clamp(3f + half + 0.25f * (roof - surface.y), 4f, 9f);
				layout.Emitters.Add(new SceneEmitter { Position = at, Kind = SceneEmitterKind.Fumes, Seed = hash, Reach = spread });
				fumes.Add(at);
				layout.Fumes++;
				return true;
			}

			// ── Probes ────────────────────────────────────────────

			/// <summary>
			/// The floor under a point beside the lava (system frame): marched down from two metres over it; false where none is
			/// within reach, where it is not a bank (under the molten surface, or more than three metres over it), where it is in
			/// any body's lava, or where the caller refuses it.
			/// </summary>
			private bool Floor(Vector3 p, float surface, out Vector3 at)
			{
				at = p;
				if (!Ground(new Vector3(p.x, surface + 2f, p.z), 5f, out float floor) || floor < surface - 0.3f || floor > surface + 3f)
				{
					return false;
				}
				at = new Vector3(p.x, floor, p.z);
				return !InLava(plan, at, 0f, 0.05f) && !Refused(at, 0f);
			}

			private bool Refused(Vector3 p, float margin) => refused != null && refused(p, margin);

			/// <summary>A piece laid, while the system has room for it.</summary>
			private bool Add(CaveGlowKind kind, int hue, int variant, Vector3 at, Quaternion rotation, float scale)
			{
				if (layout.Pieces.Count >= MaxPieces)
				{
					return false;
				}
				layout.Pieces.Add(new CaveLavaPiece { Kind = kind, Hue = hue, Variant = variant, Position = at, Rotation = rotation, Scale = scale });
				return true;
			}

			private static bool Near(List<Vector3> points, Vector3 p, float distance)
			{
				foreach (Vector3 q in points)
				{
					if ((q - p).sqrMagnitude < distance * distance)
					{
						return true;
					}
				}
				return false;
			}

			/// <summary>The floor under a point in the void, marched down the field and bisected; false where the point is rock or no floor is within reach.</summary>
			private bool Ground(Vector3 p, float reach, out float floor)
			{
				floor = p.y;
				if (system.Void(p) >= 0f)
				{
					return false;
				}
				const float step = 0.2f;
				float y = p.y;
				while (system.Void(new Vector3(p.x, y - step, p.z)) < 0f)
				{
					y -= step;
					if (y < p.y - reach)
					{
						return false;
					}
				}
				float lo = y - step, hi = y;
				for (int k = 0; k < 4; k++)
				{
					float mid = 0.5f * (lo + hi);
					if (system.Void(new Vector3(p.x, mid, p.z)) < 0f)
					{
						hi = mid;
					}
					else
					{
						lo = mid;
					}
				}
				floor = 0.5f * (lo + hi);
				return true;
			}

			/// <summary>The roof over a point in the void, as <see cref="Ground"/> marches the floor.</summary>
			private bool Roof(Vector3 p, float reach, out float roof)
			{
				roof = p.y;
				if (system.Void(p) >= 0f)
				{
					return false;
				}
				const float step = 0.25f;
				float y = p.y;
				while (system.Void(new Vector3(p.x, y + step, p.z)) < 0f)
				{
					y += step;
					if (y > p.y + reach)
					{
						return false;
					}
				}
				float lo = y, hi = y + step;
				for (int k = 0; k < 4; k++)
				{
					float mid = 0.5f * (lo + hi);
					if (system.Void(new Vector3(p.x, mid, p.z)) < 0f)
					{
						lo = mid;
					}
					else
					{
						hi = mid;
					}
				}
				roof = 0.5f * (lo + hi);
				return true;
			}

			/// <summary>
			/// A wall a ray from a point in the void meets: the surface and the way into the rock there (the field's gradient); false
			/// where it meets none within reach, or meets a floor or a roof.
			/// </summary>
			private bool Wall(Vector3 from, Vector3 dir, float reach, out Vector3 surface, out Vector3 into)
			{
				surface = from;
				into = dir;
				if (system.Void(from) >= 0f)
				{
					return false;
				}
				const float step = 0.2f;
				Vector3 last = from;
				bool hit = false;
				for (float d = step; d <= reach; d += step)
				{
					Vector3 p = from + dir * d;
					if (system.Void(p) >= 0f)
					{
						Vector3 lo = last, hi = p;
						for (int k = 0; k < 5; k++)
						{
							Vector3 mid = 0.5f * (lo + hi);
							if (system.Void(mid) < 0f)
							{
								lo = mid;
							}
							else
							{
								hi = mid;
							}
						}
						surface = 0.5f * (lo + hi);
						hit = true;
						break;
					}
					last = p;
				}
				if (!hit)
				{
					return false;
				}
				const float e = 0.15f;
				var g = new Vector3(
					system.Void(surface + Vector3.right * e) - system.Void(surface - Vector3.right * e),
					system.Void(surface + Vector3.up * e) - system.Void(surface - Vector3.up * e),
					system.Void(surface + Vector3.forward * e) - system.Void(surface - Vector3.forward * e));
				if (g.sqrMagnitude < 1e-8f)
				{
					return false;
				}
				into = g.normalized;
				return Mathf.Abs(into.y) < 0.7f;
			}

			/// <summary>A path's length, metres (along it, as it runs).</summary>
			private static float Length(Vector3[] path)
			{
				float length = 0f;
				for (int i = 1; i < path.Length; i++)
				{
					length += Vector3.Distance(path[i - 1], path[i]);
				}
				return length;
			}

			/// <summary>The point <paramref name="s"/> metres along a path, and its way along in plan (forward where it falls straight down).</summary>
			private static void At(Vector3[] path, float s, out Vector3 point, out Vector3 along)
			{
				int i = 1;
				float walked = 0f;
				while (i < path.Length - 1 && walked + Vector3.Distance(path[i - 1], path[i]) < s)
				{
					walked += Vector3.Distance(path[i - 1], path[i]);
					i++;
				}
				Vector3 a = path[i - 1], b = path[i];
				float segment = Vector3.Distance(a, b);
				point = Vector3.Lerp(a, b, segment > 1e-5f ? Mathf.Clamp01((s - walked) / segment) : 0f);
				var flat = new Vector3(b.x - a.x, 0f, b.z - a.z);
				if (flat.sqrMagnitude < 1e-4f)
				{
					flat = new Vector3(path[path.Length - 1].x - path[0].x, 0f, path[path.Length - 1].z - path[0].z);
				}
				along = flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector3.forward;
			}
		}
	}
}
#endif
