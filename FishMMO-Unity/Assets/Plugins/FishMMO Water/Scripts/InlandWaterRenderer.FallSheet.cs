using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using FishMMO.Shared.Biomes;

namespace FishMMO.Water
{
	/// <summary>
	/// A fall's curtain: the water traced down from its lip strip by strip, and the mesh drawn from those paths, a sheet
	/// above the break-up length and ropes of white water below it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Traced, not placed.</b> The curtain used to be laid out along the river's line and pushed off the rock by rules:
	/// pushed out as a block, or slid sideways round what stood in a strip's way, up to four metres. Water falling free
	/// cannot steer; only rock it touches can turn it. Each strip now leaves the brink at its own speed and depth, falls
	/// under gravity, and where it meets rock (the terrain, or a baked prop's collision mesh, <see cref="FallRock"/>) it
	/// loses what drove it into the rock and runs on along the surface. Partings, the throw off a ledge, a horsetail
	/// sheeting down a face and a fall split in two by a buttress all come out of that.
	/// </para>
	/// <para>
	/// <b>Sheet and ropes.</b> Above its break-up length the strips are joined into one slab, glassy and refracting. Below
	/// it the water is ropes of white water (a few per strip, drawn as ribbons facing the eye) and the slab fades out over
	/// the same band, so there is never a slab with holes punched in it.
	/// </para>
	/// </remarks>
	public sealed partial class InlandWaterRenderer
	{
		/// <summary>How far clear of the rock the water's core keeps, metres (on top of half its thickness).</summary>
		private const float SheetClearance = 0.1f;

		/// <summary>How far below the lip the curtain is still taken to be in its channel, clear of rock, metres.</summary>
		private const float BrinkClearMetres = 0.6f;

		/// <summary>How far above the lip the curtain begins, on the river's surface, metres.</summary>
		private const float BrinkRunMetres = 1.6f;

		/// <summary>About how wide each strip of a curtain is, metres: the finest thing the water parts round.</summary>
		private const float StripMetres = 0.7f;
		/// <summary>The most strips a curtain is cut into.</summary>
		private const int MaxStrips = 32;
		/// <summary>Metres between a curtain's rows down its own path.</summary>
		private const float StripRowMetres = 0.6f;
		/// <summary>The most rows down a curtain (a very tall fall takes longer rows).</summary>
		private const int MaxStripRows = 220;
		/// <summary>How far the water behind a gap at the lip spreads into it as it falls, metres per metre fallen.</summary>
		private const float LipSpreadPerMetre = 0.05f;
		/// <summary>Metres below where a stream rode rock that its water stays churned white.</summary>
		private const float RideTrailMetres = 4f;
		/// <summary>Ropes of white water drawn per strip below the break-up length.</summary>
		private const int RopesPerStrip = 3;
		/// <summary>Rows of the traced-path texture the spray reads: the strips' positions at even times from the lip.</summary>
		private const int PathTextureRows = 64;

		/// <summary>One strip of a curtain, traced from the lip.</summary>
		private sealed class Strip
		{
			/// <summary>Its place across the lip from the middle, metres (left of the water's way positive).</summary>
			public float Home;
			/// <summary>Its water per metre of lip, m²/s, and that over the lip's mean.</summary>
			public float Q, Share;
			/// <summary>How far down it stays a sheet, metres.</summary>
			public float Breakup;
			/// <summary>Its path, every row-step metres from the lip: where, how fast, when, and whether it touched rock since the last.</summary>
			public readonly List<Vector3> Points = new List<Vector3>();
			public readonly List<Vector3> Velocities = new List<Vector3>();
			public readonly List<float> Times = new List<float>();
			public readonly List<bool> Touching = new List<bool>();
			public bool Landed;
			/// <summary>It came down on level ground out of the pool and stopped there: not one of the fall's landings.</summary>
			public bool OnRock;
		}

		/// <summary>What building one curtain gives: its mesh, the paths for the spray, and where it lands.</summary>
		private sealed class SheetBuild
		{
			public Mesh Mesh;
			/// <summary>The strips' positions at even times from the lip (RGB, from the lip) and how broken (A; −1 once landed).</summary>
			public Texture2D Paths;
			/// <summary>The time the longest strip takes from the lip to the pool, seconds: the path texture's height.</summary>
			public float PathSeconds;
			public int Strips;
			public float PoolLevel;
			public Vector3 Landing;
			public float ImpactSpeed;
			public readonly List<Part> Parts = new List<Part>();
			public readonly List<Strike> Strikes = new List<Strike>();
		}

		/// <summary>The lip profile at <paramref name="across"/> (0…1), averaged over ±<paramref name="spread"/> of the width.</summary>
		private static float Spread(float[] lip, float across, float spread)
		{
			float sum = 0f;
			for (int k = -2; k <= 2; k++)
			{
				float at = Mathf.Clamp01(across + spread * k * 0.5f) * (lip.Length - 1);
				int k0 = Mathf.FloorToInt(at), k1 = Mathf.Min(lip.Length - 1, k0 + 1);
				sum += Mathf.Lerp(lip[k0], lip[k1], at - k0);
			}
			return sum / 5f;
		}

		/// <summary>
		/// How the water spreads across a fall's lip, from the river's solved flow: per share of the width (0 right bank, 1
		/// left), the speed there over the section's mean (1 average, 0 still water: behind a boulder at the lip, nothing
		/// goes over). Null without a solved flow, when the curtain is even.
		/// </summary>
		private static float[] LipProfile(SceneHydrology.River river, Fall fall, SceneRiverFlow.River solved, int columns)
		{
			if (solved == null || solved.Field == null || !solved.Field.isReadable)
			{
				return null;
			}
			float along = 0f;
			for (int i = 1; i <= fall.Lip && i < river.Points.Length; i++)
			{
				Vector3 step = river.Points[i] - river.Points[i - 1];
				step.y = 0f;
				along += step.magnitude;
			}
			// A little above the lip, where the water still runs as the river: over the edge the field has nothing to say.
			float u = Mathf.Clamp01((along - 1f) / Mathf.Max(1f, solved.Length * solved.AlongMetres));
			var profile = new float[columns];
			for (int k = 0; k < columns; k++)
			{
				float v = (k + 0.5f) / columns;
				Color c = solved.Field.GetPixelBilinear(u, v);
				profile[k] = Mathf.Clamp(c.r, 0f, 2f);
			}
			return profile;
		}

		/// <summary>
		/// Adds triangle (a, b, c) wound to face along the vertices' normals (out of the curtain's slab): the face the
		/// rasteriser draws is the one whose cross product points the normal's way, so a triangle wound the other way is flipped.
		/// </summary>
		private static void AddOutward(List<int> indices, List<Vector3> positions, List<Vector3> normals, int a, int b, int c)
		{
			Vector3 face = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
			Vector3 outward = normals[a] + normals[b] + normals[c];
			if (Vector3.Dot(face, outward) < 0f)
			{
				(b, c) = (c, b);
			}
			indices.Add(a);
			indices.Add(b);
			indices.Add(c);
		}

		/// <summary>Follows one strip from the lip until it lands in the pool (or gives out), meeting the rock on the way.</summary>
		private static void Trace(Strip strip, FallRock rock, Vector3 start, Vector3 velocity, Vector3 lip, Vector3 ahead, float poolLevel,
			float rowStep, float maxLength, List<Strike> strikes)
		{
			Vector3 gravity = new Vector3(0f, -FallHydraulics.Gravity, 0f);
			Vector3 p = start, v = velocity;
			float travelled = 0f, nextRow = rowStep, time = 0f, lastStrike = -10f, groundRun = 0f;
			bool touchedSinceRow = false;
			strip.Points.Add(p);
			strip.Velocities.Add(v);
			strip.Times.Add(0f);
			strip.Touching.Add(false);
			for (int guard = 0; guard < 40000; guard++)
			{
				float speed = v.magnitude;
				float dt = Mathf.Min(0.05f, 0.12f / Mathf.Max(0.5f, speed));
				float fallen = lip.y - p.y;
				Vector3 acceleration = gravity;
				/* Broken water feels the air: below its break-up length its ropes and clumps are slowed toward the speed such
				 * clusters fall at, not on to the pool at the full speed of a dropped stone. Linear in the velocity, so it is
				 * exactly the terminal speed it settles to. */
				float broken = Mathf.Clamp01((fallen - strip.Breakup) / Mathf.Max(1f, 0.5f * strip.Breakup));
				if (broken > 0f)
				{
					acceleration -= v * (broken * FallHydraulics.Gravity / FallHydraulics.BrokenTerminalSpeed);
				}
				v += acceleration * dt;
				Vector3 next = p + v * dt;

				// Still in its channel (on the river, or just over the brink): its banks are the river's, not rock in the way.
				var fromLip = new Vector2(next.x - lip.x, next.z - lip.z);
				bool inChannel = fallen < BrinkClearMetres && fromLip.magnitude < 1.5f;
				if (!inChannel)
				{
					float radius = Mathf.Clamp(0.5f * FallHydraulics.CoreThickness(strip.Q, Mathf.Max(0f, fallen)) + SheetClearance, 0.1f, 1f);
					bool touching = false, onLevelGround = false;
					Vector3 normal = Vector3.zero, contact = next;
					float ground = rock.Ground(next.x, next.z);
					if (next.y - radius < ground)
					{
						// Out of the ground along its normal (a cliff's is nearly level: lifting the water would carry it up the face).
						Vector3 n = rock.GroundNormal(next.x, next.z);
						for (int k = 0; k < 40 && next.y - radius < rock.Ground(next.x, next.z); k++)
						{
							next += n * 0.05f;
						}
						next.y = Mathf.Max(next.y, rock.Ground(next.x, next.z) + radius);
						normal += n;
						contact = next - n * radius;
						touching = true;
						onLevelGround = n.y > 0.6f;
					}
					if (rock.PropContact(next, radius, out Vector3 closest, out Vector3 propNormal))
					{
						next = closest + propNormal * radius;
						normal += propNormal;
						contact = closest;
						touching = true;
					}
					else if (rock.LiveContact(p, next, radius, out Vector3 centre, out Vector3 liveNormal))
					{
						next = centre + liveNormal * 0.01f;
						normal += liveNormal;
						contact = centre - liveNormal * radius;
						touching = true;
					}
					if (touching && normal.sqrMagnitude > 1e-8f)
					{
						normal.Normalize();
						float into = -Vector3.Dot(v, normal);
						if (into > 0f)
						{
							float speedIn = v.magnitude;
							float steep = into / Mathf.Max(1e-4f, speedIn);
							/* Water striking rock loses what drove it into the rock and much of the rest: it breaks into a
							 * film and spray, and the rough face drags at it. It runs on along the surface with what it had
							 * along it, less the harder it struck; only where that leaves it next to nothing does it slide off
							 * downhill, or spill off a flat ledge the fall's way, at a walking pace. Kept at most of its speed
							 * and pushed outward by a share of it, a strip that met a bank's wall was flung sideways off the
							 * fall in a wide arc and on across the ground (Jim, 2026-10-08). */
							Vector3 along = (v + normal * into) * Mathf.Lerp(0.8f, 0.45f, steep);
							if (along.sqrMagnitude < 1f)
							{
								Vector3 downhill = gravity - Vector3.Dot(gravity, normal) * normal;
								Vector3 outward = ahead - Vector3.Dot(ahead, normal) * normal;
								along += downhill.sqrMagnitude > 0.25f ? downhill.normalized : (outward.sqrMagnitude > 1e-6f ? outward.normalized : ahead);
							}
							Vector3 turned = along + normal * (0.05f * into);
							// Rock turns water; it does not throw it: never faster across the ground than it came.
							var acrossIn = new Vector2(v.x, v.z);
							var acrossOut = new Vector2(turned.x, turned.z);
							float most = acrossIn.magnitude + 1.5f;
							if (acrossOut.magnitude > most)
							{
								acrossOut *= most / acrossOut.magnitude;
								turned.x = acrossOut.x;
								turned.z = acrossOut.y;
							}
							v = turned;
							// A hard strike throws spray: not where it brushes rock, nor at the brink.
							if (steep > 0.7f && fallen > 2f && travelled - lastStrike > 1.5f)
							{
								strikes.Add(new Strike { Point = contact, Normal = normal, Speed = speedIn });
								lastStrike = travelled;
							}
						}
						touchedSinceRow = true;
					}
					/* Water that comes down on level ground out of the pool spreads and soaks away where it lands; it does
					 * not skate on across the bank. A ledge's top (a prop) it runs over and spills off. */
					groundRun = onLevelGround && next.y > poolLevel + 0.5f ? groundRun + (next - p).magnitude : 0f;
					if (groundRun > 2f)
					{
						strip.Points.Add(next);
						strip.Velocities.Add(Vector3.zero);
						strip.Times.Add(time + dt);
						strip.Touching.Add(true);
						strip.OnRock = true;
						return;
					}
				}

				float moved = (next - p).magnitude;
				travelled += moved;
				time += dt;
				p = next;
				bool landed = p.y <= poolLevel - 0.3f;
				if (landed)
				{
					p.y = poolLevel - 0.3f;
				}
				while (travelled >= nextRow || landed)
				{
					strip.Points.Add(p);
					strip.Velocities.Add(v);
					strip.Times.Add(time);
					strip.Touching.Add(touchedSinceRow);
					touchedSinceRow = false;
					nextRow += rowStep;
					if (landed)
					{
						break;
					}
				}
				if (landed)
				{
					strip.Landed = true;
					return;
				}
				if (travelled > maxLength || speed < 0.05f && guard > 200)
				{
					return;
				}
			}
		}

		/// <summary>
		/// A fall's curtain, traced and built. Vertex data for FishMMO/Water/Waterfall: uv0 (0…1 across, metres fallen), uv1
		/// (width m, free of the rock 0…1), uv2 (brink speed m/s, the fall's drop m), uv3 (thickness m — a rope's half width —,
		/// metres above the lip), uv4 (churned by rock, held still by rock), uv5 (metres along the path from the lip, a rope's
		/// side −1/+1 or 0 on the sheet), uv6 (fallen over the strip's break-up length, a rope's seed), colour (r the water's
		/// share over the lip ÷ 2, g how open the sheet's edge is, b 1 on a side face), normal (out of the sheet; a rope's path
		/// direction), tangent (across the curtain: the way it sways).
		/// </summary>
		private static SheetBuild SheetMesh(SceneHydrology.River river, Fall fall, UnityEngine.SceneManagement.Scene scene, SceneRiverFlow.River solved, float turbulence)
		{
			int n = river.Points.Length;
			Vector3 lip = fall.LipPoint;
			Vector3 ahead = river.Points[Mathf.Min(n - 1, fall.Lip + 1)] - river.Points[Mathf.Max(0, fall.Lip - 1)];
			ahead.y = 0f;
			ahead = ahead.sqrMagnitude > 1e-6f ? ahead.normalized : Vector3.forward;
			var left = new Vector3(-ahead.z, 0f, ahead.x);
			float width = fall.Width;
			float poolLevel = river.Points[fall.Plunge].y;
			float drop = Mathf.Max(0.5f, lip.y - poolLevel);
			int strips = Mathf.Clamp(Mathf.RoundToInt(width / StripMetres), 3, MaxStrips);
			float homeWidth = width / strips;

			// ── The rock round the fall ─────────────────────────────────────
			float throw_ = FallHydraulics.BrinkSpeed(fall.UnitDischarge * 2f) * Mathf.Sqrt(2f * drop / FallHydraulics.Gravity);
			Vector3 run = fall.FootPoint - lip;
			run.y = 0f;
			float reach = throw_ + run.magnitude + 10f;
			var bounds = new Bounds(lip, Vector3.zero);
			foreach (Vector3 corner in new[]
			{
				lip - ahead * (BrinkRunMetres + 2f) + left * (0.5f * width + 6f),
				lip - ahead * (BrinkRunMetres + 2f) - left * (0.5f * width + 6f),
				lip + ahead * reach + left * (0.5f * width + 8f),
				lip + ahead * reach - left * (0.5f * width + 8f),
			})
			{
				bounds.Encapsulate(corner + Vector3.up * 3f);
				bounds.Encapsulate(new Vector3(corner.x, poolLevel - 3f, corner.z));
			}
			var rock = new FallRock(scene, bounds);

			// ── The strips ──────────────────────────────────────────────────
			float[] profile = LipProfile(river, fall, solved, 32);
			float profileMean = 1f;
			if (profile != null)
			{
				float sum = 0f;
				foreach (float value in profile)
				{
					sum += value;
				}
				profileMean = Mathf.Max(0.05f, sum / profile.Length);
			}
			float rowStep = Mathf.Max(StripRowMetres, (1.3f * drop + reach) / MaxStripRows);
			var build = new SheetBuild { Strips = strips, PoolLevel = poolLevel };
			var traced = new Strip[strips];
			for (int i = 0; i < strips; i++)
			{
				float home = ((i + 0.5f) / strips - 0.5f) * width;
				float share = profile != null ? Spread(profile, 0.5f + home / width, 0f) / profileMean : 1f;
				var strip = new Strip { Home = home, Share = share, Q = Mathf.Max(1e-3f, fall.UnitDischarge * share) };
				strip.Breakup = FallHydraulics.BreakupLength(strip.Q, turbulence);
				// From the brink: the water's middle half its depth down, leaving at its own speed (a strip behind a boulder, little and slow).
				Vector3 start = lip + left * home - Vector3.up * (0.5f * FallHydraulics.BrinkDepth(strip.Q));
				Vector3 velocity = ahead * Mathf.Max(0.3f, FallHydraulics.BrinkSpeed(strip.Q));
				Trace(strip, rock, start, velocity, lip, ahead, poolLevel, rowStep, 3f * drop + reach + 20f, build.Strikes);
				traced[i] = strip;
			}

			// ── Rows: the brink run on the river, then each strip's path ─────
			var brink = new List<Vector3>();
			var brinkBefore = new List<float>();
			{
				Vector3 upstream = river.Points[Mathf.Max(0, fall.Lip - 1)] - lip;
				float upstreamY = upstream.y;
				upstream.y = 0f;
				float upstreamLength = upstream.magnitude;
				if (upstreamLength > 1e-3f)
				{
					float runIn = Mathf.Min(BrinkRunMetres, upstreamLength * 0.9f);
					for (float back = runIn; back > 0.25f; back -= 0.5f)
					{
						Vector3 at = lip + upstream / upstreamLength * back;
						at.y = lip.y + upstreamY * (back / upstreamLength);
						brink.Add(at);
						brinkBefore.Add(back);
					}
				}
			}
			int brinkRows = brink.Count;
			int pathRows = 0;
			foreach (Strip strip in traced)
			{
				pathRows = Mathf.Max(pathRows, strip.Points.Count);
			}
			int rows = brinkRows + pathRows;
			if (rows < 2)
			{
				return null;
			}
			float lipDepth = river.Depth != null && fall.Lip < river.Depth.Length ? Mathf.Max(0.1f, river.Depth[fall.Lip]) : 0.6f;
			var centres = new Vector3[strips, rows];
			var dirs = new Vector3[strips, rows];
			var fallens = new float[strips, rows];
			var befores = new float[rows];
			var thicks = new float[strips, rows];
			var breaks = new float[strips, rows];
			var ended = new bool[strips, rows];
			var riding = new float[strips, rows];
			var spreadAt = new float[rows];
			var pathMetres = new float[rows];
			for (int r = 0; r < rows; r++)
			{
				bool onBrink = r < brinkRows;
				befores[r] = onBrink ? brinkBefore[r] : 0f;
				pathMetres[r] = onBrink ? -brinkBefore[r] : (r - brinkRows) * rowStep;
			}
			for (int i = 0; i < strips; i++)
			{
				Strip strip = traced[i];
				int last = strip.Points.Count - 1;
				for (int r = 0; r < rows; r++)
				{
					Vector3 at;
					Vector3 dir;
					bool touch = false;
					if (r < brinkRows)
					{
						at = brink[r] + left * strip.Home;
						dir = ahead;
					}
					else
					{
						int j = r - brinkRows;
						ended[i, r] = j > last;
						j = Mathf.Min(j, last);
						at = strip.Points[j];
						dir = strip.Velocities[j].sqrMagnitude > 1e-6f ? strip.Velocities[j].normalized : ahead;
						touch = strip.Touching[j] && !ended[i, r];
					}
					float fallen = Mathf.Max(0f, lip.y - at.y);
					/* Spread by turbulence, the edges most (ξ, Castillo & Carrillo 2016): a few tenths of a metre a side on a 50 m
					 * fall. The strip's share of it by where it stands across the lip, so the middle stays put. */
					float spread = r < brinkRows ? 0f : FallHydraulics.Spread(fall.UnitDischarge, fallen, turbulence);
					spreadAt[r] = Mathf.Max(spreadAt[r], spread);
					centres[i, r] = at + left * (spread * strip.Home / (0.5f * width));
					dirs[i, r] = dir;
					fallens[i, r] = fallen;
					riding[i, r] = touch ? 1f : 0f;
					breaks[i, r] = r < brinkRows ? 0f : fallen / Mathf.Max(0.1f, strip.Breakup);
					// The river's whole depth rolls over the brink; past it, the same water spread along a faster stream, bulked by air and spread.
					thicks[i, r] = r < brinkRows
						? Mathf.Lerp(FallHydraulics.BrinkDepth(strip.Q), 0.67f * lipDepth, befores[r] / BrinkRunMetres)
						: Mathf.Max(0.03f, FallHydraulics.JetThickness(strip.Q, fallen, turbulence));
				}
				// The churn trails down below the contact, fading over RideTrailMetres, and starts half a metre above it.
				float fade = rowStep / RideTrailMetres;
				for (int r = 1; r < rows; r++)
				{
					riding[i, r] = Mathf.Max(riding[i, r], riding[i, r - 1] - fade);
				}
				for (int r = rows - 2; r >= 0; r--)
				{
					riding[i, r] = Mathf.Max(riding[i, r], riding[i, r + 1] - rowStep / 0.5f);
				}
			}

			/* Rows held still by rock: wherever any strip rides rock or has been turned off its line, eased over a few metres.
			 * The shader's sway moves the curtain across as a whole, and swung there it carried each parting off its rock. */
			var pinned = new float[rows];
			for (int r = 0; r < rows; r++)
			{
				for (int i = 0; i < strips; i++)
				{
					float aside = Mathf.Abs(Vector3.Dot(centres[i, r] - lip, left) - traced[i].Home * (1f + spreadAt[r] / (0.5f * width)));
					pinned[r] = Mathf.Max(pinned[r], Mathf.Max(riding[i, r], Mathf.Clamp01((aside - 0.15f) / 0.3f)));
				}
			}
			{
				var eased = new float[rows];
				int span = Mathf.Max(1, Mathf.RoundToInt(3f / rowStep));
				for (int r = 0; r < rows; r++)
				{
					for (int k = Mathf.Max(0, r - span); k <= Mathf.Min(rows - 1, r + span); k++)
					{
						eased[r] = Mathf.Max(eased[r], pinned[k] * (1f - Mathf.Abs(k - r) / (float)(span + 1)));
					}
				}
				pinned = eased;
			}

			// The sheet's frame at each strip and row: along its path, across the curtain, and out of it.
			var tangents = new Vector3[strips, rows];
			var acrosses = new Vector3[strips, rows];
			var facings = new Vector3[strips, rows];
			for (int i = 0; i < strips; i++)
			{
				for (int r = 0; r < rows; r++)
				{
					Vector3 tangent = centres[i, Mathf.Min(rows - 1, r + 1)] - centres[i, Mathf.Max(0, r - 1)];
					tangent = tangent.sqrMagnitude > 1e-8f ? tangent.normalized : dirs[i, r];
					Vector3 across = left - tangent * Vector3.Dot(left, tangent);
					across = across.sqrMagnitude > 1e-6f ? across.normalized : left;
					// Up out of the water lying on the river; out from the rock, the way it is going, once it hangs.
					Vector3 facing = Vector3.Cross(across, tangent).normalized;
					tangents[i, r] = tangent;
					acrosses[i, r] = across;
					facings[i, r] = facing;
				}
			}

			/* How open each side between two strips is, 0 … 1, smoothly: by how far apart they have drawn, how far one
			 * stands out from the other, or one having landed where the other falls on. A yes-or-no flipping from row to
			 * row drew hard steps down the curtain. */
			float Openness(int i, int r)
			{
				if (i < 0 || i + 1 >= strips)
				{
					return 1f;
				}
				if (ended[i, r] != ended[i + 1, r])
				{
					return 1f;
				}
				float stripWidth = (width + 2f * spreadAt[r]) / strips;
				Vector3 gap = centres[i + 1, r] - centres[i, r];
				float apart = Mathf.Clamp01((Vector3.Dot(gap, acrosses[i, r]) - 1.05f * stripWidth) / (0.6f * stripWidth));
				float t = 0.5f * (thicks[i, r] + thicks[i + 1, r]);
				float outward = Mathf.Clamp01((Mathf.Abs(Vector3.Dot(gap, facings[i, r])) - 0.4f * t - 0.15f) / Mathf.Max(0.05f, t));
				return Mathf.Max(apart, outward);
			}

			// ── The slab ────────────────────────────────────────────────────
			var positions = new List<Vector3>();
			var normals = new List<Vector3>();
			var uv0 = new List<Vector2>();
			var uv1 = new List<Vector2>();
			var uv2 = new List<Vector2>();
			var uv3 = new List<Vector2>();
			var uv4 = new List<Vector2>();
			var uv5 = new List<Vector2>();
			var uv6 = new List<Vector2>();
			var meshTangents = new List<Vector4>();
			var colours = new List<Color32>();
			var indices = new List<int>();
			float brinkSpeed = fall.Speed;
			float[] lipShares = profile;
			for (int i = 0; i < strips; i++)
			{
				int first = positions.Count;
				var openHigh = new bool[rows];
				var openLow = new bool[rows];
				for (int r = 0; r < rows; r++)
				{
					Vector3 centre = centres[i, r];
					Vector3 across = acrosses[i, r];
					Vector3 facing = facings[i, r];
					float rowWidth = width + 2f * spreadAt[r];
					float stripWidth = rowWidth / strips;
					float highOpen = Openness(i, r), lowOpen = Openness(i - 1, r);
					openHigh[r] = highOpen > 0.01f;
					openLow[r] = lowOpen > 0.01f;
					/* Each side: from midway to the neighbour (joined, one sheet) to this strip's own edge (cut), by how open
					 * that side is. Both neighbours use the one figure for the side they share, so they meet exactly while it is
					 * shut and draw apart together as it opens. */
					Vector3 high = centre + across * (0.5f * stripWidth);
					if (i + 1 < strips)
					{
						high = Vector3.Lerp(0.5f * (centre + centres[i + 1, r]), high, highOpen);
					}
					Vector3 low = centre - across * (0.5f * stripWidth);
					if (i > 0)
					{
						low = Vector3.Lerp(0.5f * (centre + centres[i - 1, r]), low, lowOpen);
					}
					float halfThick = 0.5f * thicks[i, r];
					// Lying on the river its top face is the river's surface, the body under it; hanging, centred.
					Vector3 sink = -facing * (halfThick * Mathf.Clamp01(facing.y));
					high += sink;
					low += sink;
					Vector3[] corner =
					{
						low + facing * halfThick, high + facing * halfThick, low - facing * halfThick, high - facing * halfThick,
						high + facing * halfThick, high - facing * halfThick, low + facing * halfThick, low - facing * halfThick,
					};
					Vector3 sideHigh = (across * 0.8f + facing * 0.2f).normalized, sideLow = (-across * 0.8f + facing * 0.2f).normalized;
					Vector3[] normal = { facing, facing, -facing, -facing, sideHigh, (across * 0.8f - facing * 0.2f).normalized, sideLow, (-across * 0.8f - facing * 0.2f).normalized };
					float homeHigh = traced[i].Home + 0.5f * homeWidth, homeLow = traced[i].Home - 0.5f * homeWidth;
					float[] home = { homeLow, homeHigh, homeLow, homeHigh, homeHigh, homeHigh, homeLow, homeLow };
					float[] open = { lowOpen, highOpen, lowOpen, highOpen, highOpen, highOpen, lowOpen, lowOpen };
					float airborne = 1f - riding[i, r];
					for (int k = 0; k < 8; k++)
					{
						positions.Add(corner[k]);
						normals.Add(normal[k]);
						// Across the whole curtain by where it left the lip, front and back alike, so the streaks run on from strip to strip.
						uv0.Add(new Vector2(0.5f + home[k] / width, fallens[i, r]));
						uv1.Add(new Vector2(rowWidth, airborne));
						uv2.Add(new Vector2(brinkSpeed, fall.Drop));
						uv3.Add(new Vector2(thicks[i, r], befores[r]));
						uv4.Add(new Vector2(riding[i, r], pinned[r]));
						uv5.Add(new Vector2(pathMetres[r], 0f));
						uv6.Add(new Vector2(breaks[i, r], 0f));
						meshTangents.Add(new Vector4(across.x, across.y, across.z, 1f));
						/* r: how much water goes over the lip at this place, from the river's solved flow (0.5 its mean, 0 none
						 * behind a rock), spreading as it falls, so a gap behind a boulder at the lip fills in lower down.
						 * g: how open the slab's edge is here, where the shader frays it. b: 1 on a side face. */
						float share = lipShares != null
							? Spread(lipShares, 0.5f + home[k] / width, LipSpreadPerMetre * fallens[i, r] / Mathf.Max(0.01f, width)) / profileMean
							: 1f;
						colours.Add(new Color32((byte)Mathf.RoundToInt(255f * Mathf.Clamp01(share * 0.5f)), (byte)Mathf.RoundToInt(255f * open[k]), (byte)(k >= 4 ? 255 : 0), 255));
					}
				}
				for (int r = 0; r + 1 < rows; r++)
				{
					// Nothing past where the strip landed: it is in the pool.
					if (ended[i, r])
					{
						continue;
					}
					int a = first + r * 8, b = a + 8;
					AddOutward(indices, positions, normals, a + 0, b + 0, b + 1);
					AddOutward(indices, positions, normals, a + 0, b + 1, a + 1);
					AddOutward(indices, positions, normals, a + 2, b + 3, b + 2);
					AddOutward(indices, positions, normals, a + 2, a + 3, b + 3);
					if (i == strips - 1 || openHigh[r] || openHigh[r + 1])
					{
						AddOutward(indices, positions, normals, a + 4, b + 4, b + 5);
						AddOutward(indices, positions, normals, a + 4, b + 5, a + 5);
					}
					if (i == 0 || openLow[r] || openLow[r + 1])
					{
						AddOutward(indices, positions, normals, a + 6, b + 7, b + 6);
						AddOutward(indices, positions, normals, a + 6, a + 7, b + 7);
					}
				}
			}

			// ── The ropes ───────────────────────────────────────────────────
			/* Below its break-up length the water falls as ropes of white water: a few to each strip, each its own place
			 * across it and its own thickness, drawn as ribbons the shader turns to face the eye. They begin a little above
			 * the break-up length, where the shader starts fading the sheet into them. */
			var random = new System.Random(fall.River * 7919 + fall.Lip * 104729 + 17);
			float ropeRadiusMax = 0f;
			for (int i = 0; i < strips; i++)
			{
				for (int k = 0; k < RopesPerStrip; k++)
				{
					float offset = ((float)random.NextDouble() - 0.5f) * 0.84f * homeWidth;
					// A rope a hand to two spans across (the ropes of 8–22 cm were lost from any distance), bulking as it falls.
					float thickness = Mathf.Lerp(0.08f, 0.2f, (float)random.NextDouble());
					float seed = (float)random.NextDouble();
					int firstRow = -1;
					for (int r = brinkRows; r < rows; r++)
					{
						if (breaks[i, r] >= 0.55f)
						{
							firstRow = r;
							break;
						}
					}
					if (firstRow < 0)
					{
						continue;
					}
					int firstVertex = positions.Count;
					int lastRow = firstRow;
					for (int r = firstRow; r < rows; r++)
					{
						lastRow = r;
						float beyond = Mathf.Max(0f, fallens[i, r] - traced[i].Breakup);
						// Air bulks a rope as it falls, to a few times its width.
						float half = Mathf.Min(0.8f, thickness * (1f + 0.08f * beyond));
						ropeRadiusMax = Mathf.Max(ropeRadiusMax, half);
						Vector3 at = centres[i, r] + acrosses[i, r] * (offset * (1f + spreadAt[r] / (0.5f * width)));
						for (int side = -1; side <= 1; side += 2)
						{
							positions.Add(at);
							normals.Add(tangents[i, r]);
							uv0.Add(new Vector2(0.5f + (traced[i].Home + offset) / width, fallens[i, r]));
							uv1.Add(new Vector2(width + 2f * spreadAt[r], 1f - riding[i, r]));
							uv2.Add(new Vector2(brinkSpeed, fall.Drop));
							uv3.Add(new Vector2(half, 0f));
							uv4.Add(new Vector2(riding[i, r], pinned[r]));
							uv5.Add(new Vector2(pathMetres[r], side));
							uv6.Add(new Vector2(breaks[i, r], seed));
							meshTangents.Add(new Vector4(acrosses[i, r].x, acrosses[i, r].y, acrosses[i, r].z, 1f));
							colours.Add(new Color32((byte)Mathf.RoundToInt(255f * Mathf.Clamp01(traced[i].Share * 0.5f)), 255, 0, 255));
						}
						if (ended[i, r])
						{
							break;
						}
					}
					/* Wound to face the eye: the shader sets each rope's sides along cross(path, toward the eye), so (side −1,
					 * next +1, next −1) and (side −1, side +1, next +1) are clockwise seen from the eye from every side. */
					for (int r = 0; firstRow + r < lastRow; r++)
					{
						int a = firstVertex + r * 2, b = a + 2;
						indices.Add(a); indices.Add(b + 1); indices.Add(b);
						indices.Add(a); indices.Add(a + 1); indices.Add(b + 1);
					}
				}
			}

			// ── Where it lands, and what the spray reads ────────────────────
			float impactSpeed = 0f;
			int impactCount = 0;
			Vector3 pathLanding = Vector3.zero;
			foreach (Strip strip in traced)
			{
				int last = strip.Points.Count - 1;
				impactSpeed += strip.Velocities[last].magnitude;
				impactCount++;
			}
			build.ImpactSpeed = impactCount > 0 ? impactSpeed / impactCount : fall.ImpactSpeed;
			/* Its parts: neighbouring strips that land together are one; a gap wider than a strip and a half between their
			 * landings (a ledge threw one part out, a buttress split them) makes two, each with its own boil and splash. */
			{
				float stripWidth = (width + 2f * FallHydraulics.Spread(fall.UnitDischarge, drop, turbulence)) / strips;
				// Only the strips that reach the pool: one that came down on the bank is not a landing (no boil, no splash there).
				var landers = new List<Strip>();
				foreach (Strip strip in traced)
				{
					if (!strip.OnRock)
					{
						landers.Add(strip);
					}
				}
				if (landers.Count == 0)
				{
					landers.AddRange(traced);
				}
				int start = 0;
				for (int i = 1; i <= landers.Count; i++)
				{
					bool split = i == landers.Count;
					if (!split)
					{
						Vector3 a = landers[i - 1].Points[landers[i - 1].Points.Count - 1], b = landers[i].Points[landers[i].Points.Count - 1];
						split = new Vector2(a.x - b.x, a.z - b.z).magnitude > 1.6f * stripWidth + 0.5f;
					}
					if (!split)
					{
						continue;
					}
					Vector3 sum = Vector3.zero;
					float q = 0f;
					for (int k = start; k < i; k++)
					{
						Vector3 end = landers[k].Points[landers[k].Points.Count - 1];
						sum += end * landers[k].Q;
						q += landers[k].Q;
					}
					int count = i - start;
					Vector3 landing = q > 0f ? sum / q : landers[start].Points[landers[start].Points.Count - 1];
					landing.y = poolLevel;
					float discharge = q * homeWidth;
					build.Parts.Add(new Part
					{
						Landing = landing,
						Width = count * stripWidth,
						Discharge = discharge,
						ImpactThickness = FallHydraulics.JetThickness(q / Mathf.Max(1, count), drop, turbulence),
					});
					pathLanding += landing * discharge;
					start = i;
				}
				float total = 0f;
				foreach (Part part in build.Parts)
				{
					total += part.Discharge;
				}
				build.Landing = total > 0f ? pathLanding / total : fall.Landing;
				build.Landing.y = poolLevel;
			}
			build.Paths = PathTexture(traced, lip, out build.PathSeconds);

			if (LogSheets)
			{
				int touched = 0, landedCount = 0;
				foreach (Strip strip in traced)
				{
					landedCount += strip.Landed ? 1 : 0;
					foreach (bool t in strip.Touching)
					{
						touched += t ? 1 : 0;
					}
				}
				Debug.Log($"[Fall strips] {fall.River}.{fall.Lip}: {strips} strips, {rows} rows of {rowStep:0.00} m, {landedCount} landed, {touched} rows touching rock, " +
					$"{build.Strikes.Count} strikes, {build.Parts.Count} part(s), {rock.TriangleCount} prop triangles; q {fall.UnitDischarge:0.00} m²/s, brink {fall.Speed:0.00} m/s, " +
					$"break-up {fall.BreakupLength:0.0} m, lands at {build.ImpactSpeed:0.0} m/s");
				for (int i = 0; i < strips; i++)
				{
					Strip strip = traced[i];
					Vector3 end = strip.Points[strip.Points.Count - 1];
					float aside = Vector3.Dot(end - lip, left) - strip.Home;
					Debug.Log($"[Fall strips] strip {i} home {strip.Home:+0.00;-0.00} q {strip.Q:0.00} lands {end} ({aside:+0.00;-0.00} m aside, {Vector3.Dot(end - lip, ahead):0.00} m out), break-up {strip.Breakup:0.0} m");
				}
			}

			var mesh = new Mesh { name = $"Fall {fall.River}.{fall.Lip}", hideFlags = HideFlags.DontSave };
			if (positions.Count > 65000)
			{
				mesh.indexFormat = IndexFormat.UInt32;
			}
			mesh.SetVertices(positions);
			mesh.SetNormals(normals);
			mesh.SetUVs(0, uv0);
			mesh.SetUVs(1, uv1);
			mesh.SetUVs(2, uv2);
			mesh.SetUVs(3, uv3);
			mesh.SetUVs(4, uv4);
			mesh.SetUVs(5, uv5);
			mesh.SetUVs(6, uv6);
			mesh.SetTangents(meshTangents);
			mesh.SetColors(colours);
			mesh.SetTriangles(indices, 0);
			mesh.RecalculateBounds();
			// Room for the sway (FishWaterfall: up to a metre either way across a tall curtain) and the ropes' width.
			Bounds swayBounds = mesh.bounds;
			swayBounds.Expand(new Vector3(2f, 0.5f, 2f) + Vector3.one * (2f * ropeRadiusMax));
			mesh.bounds = swayBounds;
			build.Mesh = mesh;
			return build;
		}

		/// <summary>
		/// The strips' paths for the spray: a strip a column, rows at even times from the lip to the longest strip's
		/// landing, RGB its position from the lip, A how broken it is there (fallen over its break-up length), −1 once
		/// it has landed. Strands of water read it to fall where the curtain falls.
		/// </summary>
		private static Texture2D PathTexture(Strip[] strips, Vector3 lip, out float seconds)
		{
			seconds = 0.1f;
			foreach (Strip strip in strips)
			{
				seconds = Mathf.Max(seconds, strip.Times[strip.Times.Count - 1]);
			}
			var texture = new Texture2D(strips.Length, PathTextureRows, TextureFormat.RGBAHalf, false, true)
			{
				name = "Fall paths",
				hideFlags = HideFlags.DontSave,
				wrapMode = TextureWrapMode.Clamp,
				filterMode = FilterMode.Bilinear,
			};
			var pixels = new Color[strips.Length * PathTextureRows];
			for (int i = 0; i < strips.Length; i++)
			{
				Strip strip = strips[i];
				int j = 0;
				for (int k = 0; k < PathTextureRows; k++)
				{
					float time = seconds * k / (PathTextureRows - 1);
					while (j + 1 < strip.Times.Count && strip.Times[j + 1] < time)
					{
						j++;
					}
					Vector3 at;
					float broken;
					if (j + 1 >= strip.Times.Count || time > strip.Times[strip.Times.Count - 1])
					{
						at = strip.Points[strip.Points.Count - 1];
						broken = -1f;
					}
					else
					{
						float t0 = strip.Times[j], t1 = strip.Times[j + 1];
						float t = t1 > t0 ? Mathf.Clamp01((time - t0) / (t1 - t0)) : 0f;
						at = Vector3.Lerp(strip.Points[j], strip.Points[j + 1], t);
						broken = Mathf.Max(0f, lip.y - at.y) / Mathf.Max(0.1f, strip.Breakup);
					}
					Vector3 from = at - lip;
					pixels[k * strips.Length + i] = new Color(from.x, from.y, from.z, broken);
				}
			}
			texture.SetPixels(pixels);
			texture.Apply(false, true);
			return texture;
		}
	}
}
