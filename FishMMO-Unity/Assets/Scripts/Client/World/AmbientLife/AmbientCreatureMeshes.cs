using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// The ambient creatures' meshes, built at runtime from a few numbers: one unit long (bill to tail, nose to rump;
	/// a crab one unit wide), nose toward +z, back up +y, standing on the origin (a bat, which never lands, is centred
	/// on it). The vertex layout is FishAmbientLife.hlsl's: colour (linear), TEXCOORD0 x along the body (0 nose .. 1
	/// tail) and y across a wing's chord, TEXCOORD1 which part and how far out along it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Low-poly on purpose: a songbird is under 250 vertices and seen at a few pixels most of the time, so the
	/// silhouette is what counts — the wing's plan (a gull's long blade, a buzzard's broad fingered board, a bat's
	/// scalloped membrane), a rabbit's ears, a squirrel's tail. Wings, ears, tails and legs are single sheets (tails
	/// and legs crossed pairs), drawn from both sides.
	/// </para>
	/// <para>
	/// Parts (TEXCOORD1.x): birds 0 body, 1 left wing, 2 right wing, 3 legs, 4 head and bill, 5 tail; walkers 0 body,
	/// 1-4 legs (front left, front right, hind left, hind right), 5 tail, 6 head and ears; crabs 1-8 legs, 9 claws.
	/// </para>
	/// </remarks>
	public static class AmbientCreatureMeshes
	{
		/// <summary>The shader's motion for a shape (FishAmbientLife.hlsl <c>_Mode</c>).</summary>
		public static float ModeOf(AmbientShape shape)
		{
			switch (shape)
			{
				case AmbientShape.Rat:
				case AmbientShape.Mouse:
				case AmbientShape.Squirrel:
				case AmbientShape.Rabbit:
				case AmbientShape.Frog:
					return 1f;
				case AmbientShape.Lizard:
					return 2f;
				case AmbientShape.Crab:
					return 3f;
				default:
					return 0f;
			}
		}

		/// <summary>The wing's shoulder (x, y, z) for a bird shape, for the shader's flap pivot (<c>_Shoulder</c>).</summary>
		public static Vector3 ShoulderOf(AmbientShape shape)
		{
			BirdPlan plan = Plan(shape);
			return plan.Shoulder;
		}

		/// <summary>The mesh for a kind (its shape and colours), built fresh; uploaded and dropped from the CPU unless <paramref name="keepReadable"/>.</summary>
		public static Mesh Build(AmbientCreatureKind kind, bool keepReadable = false)
		{
			var b = new Builder();
			Color back = kind.Back.linear, belly = kind.Belly.linear, accent = kind.Accent.linear;
			switch (kind.Shape)
			{
				case AmbientShape.Rat:
				case AmbientShape.Mouse:
				case AmbientShape.Squirrel:
				case AmbientShape.Rabbit:
					Walker(b, kind.Shape, back, belly, accent);
					break;
				case AmbientShape.Frog:
					Frog(b, back, belly, accent);
					break;
				case AmbientShape.Lizard:
					Lizard(b, back, belly, accent);
					break;
				case AmbientShape.Crab:
					Crab(b, back, belly, accent);
					break;
				default:
					Bird(b, Plan(kind.Shape), back, belly, accent);
					break;
			}
			Mesh mesh = b.ToMesh(kind.Name);
			// Wings beat and fold, legs swing, rabbits bound: room for all of it.
			Bounds bounds = mesh.bounds;
			bounds.Expand(1.2f);
			mesh.bounds = bounds;
			mesh.UploadMeshData(!keepReadable);
			return mesh;
		}

		// ── Birds and bats ────────────────────────────────────────────

		/// <summary>A bird's proportions, as shares of its length.</summary>
		private struct BirdPlan
		{
			public Vector3 BodyCentre, BodyRadii, HeadCentre, HeadRadii;
			public float BeakLength, BeakRadius;
			public float TailLength, TailRoot, TailTip;
			public Vector3 Shoulder;
			/// <summary>Half span from the shoulder.</summary>
			public float Span;
			public float[] Columns, Lead, Trail;
			public float LegLength;
			/// <summary>Wing tips (outer third) in the accent colour: a gull's black tips, a raptor's dark primaries.</summary>
			public bool DarkTips;
			public bool Bat;
		}

		private static BirdPlan Plan(AmbientShape shape)
		{
			switch (shape)
			{
				case AmbientShape.Crow:
					return new BirdPlan
					{
						BodyCentre = new Vector3(0f, 0.2f, -0.02f), BodyRadii = new Vector3(0.115f, 0.115f, 0.22f),
						HeadCentre = new Vector3(0f, 0.27f, 0.22f), HeadRadii = new Vector3(0.07f, 0.07f, 0.08f), BeakLength = 0.13f, BeakRadius = 0.03f,
						TailLength = 0.3f, TailRoot = 0.06f, TailTip = 0.11f, Shoulder = new Vector3(0.08f, 0.25f, 0.08f), Span = 0.95f,
						Columns = new[] { 0f, 0.4f, 0.75f, 0.92f, 1f }, Lead = new[] { 0.11f, 0.11f, 0.09f, 0.07f, 0.04f }, Trail = new[] { -0.13f, -0.16f, -0.15f, -0.08f, -0.02f },
						LegLength = 0.12f,
					};
				case AmbientShape.Gull:
					return new BirdPlan
					{
						BodyCentre = new Vector3(0f, 0.17f, -0.02f), BodyRadii = new Vector3(0.1f, 0.1f, 0.24f),
						HeadCentre = new Vector3(0f, 0.24f, 0.24f), HeadRadii = new Vector3(0.068f, 0.068f, 0.08f), BeakLength = 0.1f, BeakRadius = 0.022f,
						TailLength = 0.2f, TailRoot = 0.05f, TailTip = 0.08f, Shoulder = new Vector3(0.07f, 0.21f, 0.07f), Span = 1.25f,
						Columns = new[] { 0f, 0.4f, 0.75f, 1f }, Lead = new[] { 0.08f, 0.08f, 0.03f, -0.14f }, Trail = new[] { -0.07f, -0.09f, -0.07f, -0.15f },
						LegLength = 0.1f, DarkTips = true,
					};
				case AmbientShape.Raptor:
				case AmbientShape.Vulture:
					bool vulture = shape == AmbientShape.Vulture;
					return new BirdPlan
					{
						BodyCentre = new Vector3(0f, 0.2f, -0.02f), BodyRadii = new Vector3(0.13f, 0.13f, 0.22f),
						HeadCentre = new Vector3(0f, 0.26f, 0.22f), HeadRadii = vulture ? new Vector3(0.05f, 0.05f, 0.07f) : new Vector3(0.07f, 0.07f, 0.08f),
						BeakLength = 0.07f, BeakRadius = 0.025f,
						TailLength = vulture ? 0.2f : 0.26f, TailRoot = 0.08f, TailTip = 0.14f, Shoulder = new Vector3(0.09f, 0.25f, 0.08f), Span = vulture ? 1.35f : 1.15f,
						// Broad boards ending in spread primaries: the last column is the fingers' ragged edge.
						Columns = new[] { 0f, 0.4f, 0.78f, 0.92f, 1f }, Lead = new[] { 0.12f, 0.13f, 0.11f, 0.09f, 0.05f }, Trail = new[] { -0.16f, -0.2f, -0.18f, -0.1f, -0.02f },
						LegLength = 0.12f, DarkTips = true,
					};
				case AmbientShape.Duck:
					return new BirdPlan
					{
						BodyCentre = new Vector3(0f, 0.07f, -0.03f), BodyRadii = new Vector3(0.15f, 0.12f, 0.28f),
						HeadCentre = new Vector3(0f, 0.25f, 0.25f), HeadRadii = new Vector3(0.065f, 0.07f, 0.08f), BeakLength = 0.11f, BeakRadius = 0.028f,
						TailLength = 0.1f, TailRoot = 0.06f, TailTip = 0.06f, Shoulder = new Vector3(0.1f, 0.13f, 0.06f), Span = 0.8f,
						Columns = new[] { 0f, 0.4f, 0.75f, 1f }, Lead = new[] { 0.09f, 0.08f, 0.03f, -0.08f }, Trail = new[] { -0.08f, -0.1f, -0.08f, -0.09f },
						LegLength = 0f,
					};
				case AmbientShape.Bat:
					return new BirdPlan
					{
						BodyCentre = new Vector3(0f, 0f, -0.05f), BodyRadii = new Vector3(0.1f, 0.1f, 0.24f),
						HeadCentre = new Vector3(0f, 0.02f, 0.24f), HeadRadii = new Vector3(0.08f, 0.075f, 0.09f), BeakLength = 0f, BeakRadius = 0f,
						TailLength = 0f, Shoulder = new Vector3(0.07f, 0.03f, 0.1f), Span = 1.5f,
						// Membrane on long fingers: the trailing edge scallops in between them.
						Columns = new[] { 0f, 0.3f, 0.55f, 0.8f, 1f }, Lead = new[] { 0.12f, 0.17f, 0.13f, 0.08f, 0f }, Trail = new[] { -0.22f, -0.04f, -0.18f, -0.02f, -0.1f },
						LegLength = 0f, Bat = true,
					};
				default:
					return new BirdPlan
					{
						BodyCentre = new Vector3(0f, 0.2f, 0f), BodyRadii = new Vector3(0.11f, 0.11f, 0.22f),
						HeadCentre = new Vector3(0f, 0.27f, 0.23f), HeadRadii = new Vector3(0.075f, 0.075f, 0.08f), BeakLength = 0.09f, BeakRadius = 0.025f,
						TailLength = 0.32f, TailRoot = 0.05f, TailTip = 0.09f, Shoulder = new Vector3(0.08f, 0.25f, 0.08f), Span = 0.75f,
						Columns = new[] { 0f, 0.4f, 0.75f, 1f }, Lead = new[] { 0.1f, 0.08f, 0.03f, -0.05f }, Trail = new[] { -0.12f, -0.14f, -0.12f, -0.06f },
						LegLength = 0.11f,
					};
			}
		}

		private static void Bird(Builder b, BirdPlan plan, Color back, Color belly, Color accent)
		{
			// The body and the head: countershaded ellipsoids, the head turning (part 4) on its own.
			Ellipsoid(b, plan.BodyCentre, plan.BodyRadii, 6, 8, back, belly, 0f, 0f);
			Ellipsoid(b, plan.HeadCentre, plan.HeadRadii, 5, 7, back, Color.Lerp(belly, back, 0.4f), 4f, 0f);
			// Eyes: a dark spot each side of the head.
			for (int side = -1; side <= 1; side += 2)
			{
				var eye = new Vector3(side * plan.HeadRadii.x * 0.92f, plan.HeadCentre.y + plan.HeadRadii.y * 0.25f, plan.HeadCentre.z + plan.HeadRadii.z * 0.35f);
				Disc(b, eye, new Vector3(side, 0f, 0f), plan.HeadRadii.x * 0.3f, new Color(0.01f, 0.01f, 0.012f), 4f);
			}
			if (plan.Bat)
			{
				// Ears: two tall triangles.
				for (int side = -1; side <= 1; side += 2)
				{
					Vector3 root = plan.HeadCentre + new Vector3(side * 0.045f, plan.HeadRadii.y * 0.7f, -0.01f);
					int e0 = b.Add(root + new Vector3(-0.025f * side, 0f, 0.02f), back, new Vector2(0.2f, 0f), new Vector2(4f, 0f));
					int e1 = b.Add(root + new Vector3(0.025f * side, 0f, -0.02f), back, new Vector2(0.25f, 0f), new Vector2(4f, 0f));
					int e2 = b.Add(root + new Vector3(0.02f * side, 0.12f, -0.01f), accent, new Vector2(0.22f, 0f), new Vector2(4f, 1f));
					b.Tri(e0, e1, e2);
				}
			}
			if (plan.BeakLength > 0f)
			{
				// The bill: a short cone from the face (a duck's flattened).
				Vector3 baseCentre = plan.HeadCentre + new Vector3(0f, -plan.HeadRadii.y * 0.15f, plan.HeadRadii.z * 0.8f);
				Vector3 tip = baseCentre + new Vector3(0f, -plan.BeakLength * 0.12f, plan.BeakLength);
				Cone(b, baseCentre, tip, plan.BeakRadius, accent, 4f, 5);
			}
			if (plan.TailLength > 0f)
			{
				// The tail: a flat fan, slightly raised.
				float z0 = plan.BodyCentre.z - plan.BodyRadii.z * 0.7f, z1 = z0 - plan.TailLength;
				float y0 = plan.BodyCentre.y + plan.BodyRadii.y * 0.15f, y1 = y0 + plan.TailLength * 0.08f;
				int t0 = b.Add(new Vector3(-plan.TailRoot, y0, z0), back, new Vector2(0.8f, 0f), new Vector2(5f, 0f));
				int t1 = b.Add(new Vector3(plan.TailRoot, y0, z0), back, new Vector2(0.8f, 0f), new Vector2(5f, 0f));
				int t2 = b.Add(new Vector3(plan.TailTip, y1, z1), back * 0.8f, new Vector2(1f, 0f), new Vector2(5f, 1f));
				int t3 = b.Add(new Vector3(-plan.TailTip, y1, z1), back * 0.8f, new Vector2(1f, 0f), new Vector2(5f, 1f));
				b.Quad(t0, t1, t2, t3);
			}
			if (plan.LegLength > 0f)
			{
				// Legs, a crossed pair of thin strips each, from the belly to the feet at the origin.
				for (int side = -1; side <= 1; side += 2)
				{
					var hip = new Vector3(side * 0.035f, plan.BodyCentre.y - plan.BodyRadii.y * 0.75f, plan.BodyCentre.z + 0.03f);
					var foot = new Vector3(side * 0.035f, 0f, plan.BodyCentre.z + 0.05f);
					Limb(b, new[] { hip, foot, foot + new Vector3(0f, 0f, 0.06f) }, new[] { 0.014f, 0.01f, 0.008f }, accent, 3f);
				}
			}
			// The wings, open: their plan from the columns, spanned out from the shoulders.
			for (int side = -1; side <= 1; side += 2)
			{
				float part = side < 0 ? 1f : 2f;
				int first = b.Count;
				int columns = plan.Columns.Length;
				for (int c = 0; c < columns; c++)
				{
					float s = plan.Columns[c];
					float x = side * (plan.Shoulder.x + s * plan.Span);
					// A little camber: the wing's middle arches up.
					float y = plan.Shoulder.y + 0.012f * Mathf.Sin(s * Mathf.PI);
					Color top = plan.DarkTips && s > 0.7f ? accent : Color.Lerp(back, belly, plan.Bat ? 0f : 0.15f * (1f - s));
					if (plan.Bat)
					{
						top = Color.Lerp(back, accent, 0.6f);
					}
					b.Add(new Vector3(x, y, plan.Shoulder.z + plan.Lead[c] - 0.08f), top, new Vector2(0.4f, 0f), new Vector2(part, s));
					b.Add(new Vector3(x, y - 0.005f, plan.Shoulder.z + plan.Trail[c] - 0.08f), top * 0.92f, new Vector2(0.55f, 1f), new Vector2(part, s));
				}
				for (int c = 0; c < columns - 1; c++)
				{
					int a0 = first + c * 2, a1 = a0 + 1, b0 = a0 + 2, b1 = a0 + 3;
					if (side < 0)
					{
						b.Quad(a0, b0, b1, a1);
					}
					else
					{
						b.Quad(a0, a1, b1, b0);
					}
				}
			}
		}

		// ── Walkers ───────────────────────────────────────────────────

		private static void Walker(Builder b, AmbientShape shape, Color back, Color belly, Color accent)
		{
			bool rabbit = shape == AmbientShape.Rabbit, squirrel = shape == AmbientShape.Squirrel, mouse = shape == AmbientShape.Mouse;
			Vector3 bodyC, bodyR, headC, headR;
			float legFront, legHind, legX, legW, earSize;
			if (rabbit)
			{
				bodyC = new Vector3(0f, 0.2f, -0.08f); bodyR = new Vector3(0.16f, 0.17f, 0.3f);
				headC = new Vector3(0f, 0.3f, 0.26f); headR = new Vector3(0.1f, 0.1f, 0.13f);
				legFront = 0.16f; legHind = -0.22f; legX = 0.09f; legW = 0.05f; earSize = 0f;
			}
			else if (squirrel)
			{
				bodyC = new Vector3(0f, 0.16f, -0.05f); bodyR = new Vector3(0.1f, 0.11f, 0.28f);
				headC = new Vector3(0f, 0.19f, 0.3f); headR = new Vector3(0.08f, 0.08f, 0.1f);
				legFront = 0.16f; legHind = -0.2f; legX = 0.07f; legW = 0.04f; earSize = 0.05f;
			}
			else
			{
				bodyC = new Vector3(0f, 0.12f, -0.05f); bodyR = new Vector3(0.11f, 0.1f, mouse ? 0.28f : 0.32f);
				headC = new Vector3(0f, 0.12f, mouse ? 0.27f : 0.3f); headR = mouse ? new Vector3(0.08f, 0.08f, 0.11f) : new Vector3(0.07f, 0.07f, 0.12f);
				legFront = 0.15f; legHind = -0.22f; legX = 0.07f; legW = 0.035f; earSize = mouse ? 0.085f : 0.05f;
			}
			Ellipsoid(b, bodyC, bodyR, 6, 8, back, belly, 0f, 0f);
			Ellipsoid(b, headC, headR, 5, 7, back, belly, 6f, 0f);
			// The snout's point.
			Cone(b, headC + new Vector3(0f, -headR.y * 0.1f, headR.z * 0.7f), headC + new Vector3(0f, -headR.y * 0.3f, headR.z * 1.25f), headR.x * 0.45f, Color.Lerp(back, accent, 0.4f), 6f, 5);
			for (int side = -1; side <= 1; side += 2)
			{
				var eye = new Vector3(side * headR.x * 0.85f, headC.y + headR.y * 0.3f, headC.z + headR.z * 0.3f);
				Disc(b, eye, new Vector3(side, 0.2f, 0.3f).normalized, headR.x * 0.25f, new Color(0.01f, 0.01f, 0.01f), 6f);
				if (rabbit)
				{
					// Long ears, upright and swept back a little.
					Vector3 root = headC + new Vector3(side * 0.04f, headR.y * 0.8f, -0.03f);
					Limb(b, new[] { root, root + new Vector3(side * 0.02f, 0.14f, -0.05f), root + new Vector3(side * 0.03f, 0.26f, -0.1f) }, new[] { 0.06f, 0.065f, 0.02f }, back, 6f, true);
				}
				else
				{
					Vector3 root = headC + new Vector3(side * headR.x * 0.6f, headR.y * 0.75f, -headR.z * 0.2f);
					Disc(b, root + new Vector3(side * earSize * 0.3f, earSize * 0.6f, 0f), new Vector3(0f, 0f, 1f), earSize, Color.Lerp(back, accent, squirrel ? 0f : 0.6f), 6f);
				}
				// Legs: front and hind, the hind legs heavier (a rabbit's long feet lie along the ground).
				float hindWidth = rabbit ? legW * 1.5f : legW * 1.2f;
				float sideX = side * legX;
				Vector3 frontHip = new Vector3(sideX, bodyC.y - bodyR.y * 0.4f, legFront);
				Limb(b, new[] { frontHip, new Vector3(sideX, 0f, legFront + 0.02f) }, new[] { legW, legW * 0.7f }, Color.Lerp(back, accent, 0.5f), side < 0 ? 1f : 2f);
				Vector3 hindHip = new Vector3(sideX * 1.2f, bodyC.y - bodyR.y * 0.2f, legHind);
				Vector3 hindFoot = new Vector3(sideX * 1.1f, 0f, legHind - 0.02f);
				Vector3[] hind = rabbit ? new[] { hindHip, new Vector3(sideX * 1.15f, 0.04f, legHind - 0.08f), hindFoot + new Vector3(0f, 0f, 0.18f) } : new[] { hindHip, hindFoot, hindFoot + new Vector3(0f, 0f, 0.05f) };
				Limb(b, hind, new[] { hindWidth, hindWidth * 0.7f, hindWidth * 0.6f }, Color.Lerp(back, accent, 0.5f), side < 0 ? 3f : 4f);
			}
			if (rabbit)
			{
				// The scut: a white puff.
				Ellipsoid(b, new Vector3(0f, bodyC.y + 0.04f, bodyC.z - bodyR.z * 0.95f), new Vector3(0.05f, 0.05f, 0.04f), 3, 5, accent, accent, 5f, 0.2f);
			}
			else if (squirrel)
			{
				// The bushy tail: up over the back in an S, wide.
				Limb(b, new[]
				{
					new Vector3(0f, bodyC.y + 0.02f, bodyC.z - bodyR.z * 0.9f), new Vector3(0f, 0.3f, -0.56f), new Vector3(0f, 0.55f, -0.56f), new Vector3(0f, 0.74f, -0.38f),
				}, new[] { 0.1f, 0.2f, 0.22f, 0.12f }, back, 5f);
			}
			else
			{
				// A naked tail as long as the body, drooping to the ground and curving.
				float len = mouse ? 0.75f : 1f;
				Limb(b, new[]
				{
					new Vector3(0f, bodyC.y, bodyC.z - bodyR.z * 0.9f), new Vector3(0f, 0.04f, -0.5f - 0.3f * len), new Vector3(0.06f, 0.012f, -0.45f - 0.7f * len),
					new Vector3(0.14f, 0.01f, -0.4f - 0.95f * len),
				}, new[] { 0.035f, 0.022f, 0.012f, 0.004f }, accent, 5f);
			}
		}

		private static void Frog(Builder b, Color back, Color belly, Color accent)
		{
			// Squat and wide, the head part of the body; eyes up on top.
			Ellipsoid(b, new Vector3(0f, 0.12f, -0.02f), new Vector3(0.24f, 0.12f, 0.32f), 6, 8, back, belly, 0f, 0f);
			Ellipsoid(b, new Vector3(0f, 0.13f, 0.24f), new Vector3(0.2f, 0.1f, 0.17f), 5, 7, back, belly, 6f, 0f);
			for (int side = -1; side <= 1; side += 2)
			{
				Ellipsoid(b, new Vector3(side * 0.11f, 0.21f, 0.29f), new Vector3(0.055f, 0.05f, 0.055f), 3, 5, new Color(0.03f, 0.03f, 0.02f), accent, 6f, 0f);
				// Front legs short and straight; hind legs folded in a Z against the flank, the feet splayed back.
				Limb(b, new[] { new Vector3(side * 0.15f, 0.08f, 0.18f), new Vector3(side * 0.2f, 0f, 0.26f) }, new[] { 0.05f, 0.04f }, back, side < 0 ? 1f : 2f);
				Limb(b, new[] { new Vector3(side * 0.18f, 0.1f, -0.2f), new Vector3(side * 0.32f, 0.05f, -0.02f), new Vector3(side * 0.3f, 0.01f, -0.3f), new Vector3(side * 0.36f, 0f, -0.42f) },
					new[] { 0.09f, 0.07f, 0.05f, 0.07f }, back, side < 0 ? 3f : 4f);
			}
		}

		private static void Lizard(Builder b, Color back, Color belly, Color accent)
		{
			// Low and flat, a tail longer than the body (the loft's continuation), legs sprawled out to the sides.
			Ellipsoid(b, new Vector3(0f, 0.06f, 0f), new Vector3(0.11f, 0.06f, 0.3f), 6, 8, back, belly, 0f, 0f);
			Ellipsoid(b, new Vector3(0f, 0.065f, 0.36f), new Vector3(0.07f, 0.05f, 0.12f), 4, 7, back, belly, 6f, 0f);
			for (int side = -1; side <= 1; side += 2)
			{
				Disc(b, new Vector3(side * 0.055f, 0.08f, 0.4f), new Vector3(side, 0.3f, 0f).normalized, 0.018f, new Color(0.01f, 0.01f, 0.01f), 6f);
				Limb(b, new[] { new Vector3(side * 0.08f, 0.05f, 0.17f), new Vector3(side * 0.2f, 0.06f, 0.22f), new Vector3(side * 0.24f, 0f, 0.28f) },
					new[] { 0.035f, 0.03f, 0.03f }, back, side < 0 ? 1f : 2f);
				Limb(b, new[] { new Vector3(side * 0.08f, 0.05f, -0.2f), new Vector3(side * 0.22f, 0.06f, -0.2f), new Vector3(side * 0.26f, 0f, -0.14f) },
					new[] { 0.04f, 0.035f, 0.03f }, back, side < 0 ? 3f : 4f);
			}
			Limb(b, new[] { new Vector3(0f, 0.05f, -0.28f), new Vector3(0f, 0.035f, -0.65f), new Vector3(0.05f, 0.02f, -1.05f), new Vector3(0.12f, 0.01f, -1.4f) },
				new[] { 0.09f, 0.055f, 0.025f, 0.006f }, Color.Lerp(back, accent, 0.3f), 5f);
		}

		private static void Crab(Builder b, Color back, Color belly, Color accent)
		{
			// The carapace, wider than long and flat; eyes on stalks; eight legs and two claws.
			Ellipsoid(b, new Vector3(0f, 0.13f, 0f), new Vector3(0.5f, 0.15f, 0.36f), 5, 9, back, belly, 0f, 0f);
			for (int side = -1; side <= 1; side += 2)
			{
				Limb(b, new[] { new Vector3(side * 0.09f, 0.2f, 0.3f), new Vector3(side * 0.11f, 0.3f, 0.34f) }, new[] { 0.025f, 0.03f }, new Color(0.02f, 0.02f, 0.02f), 0f);
				for (int leg = 0; leg < 4; leg++)
				{
					float z = Mathf.Lerp(0.18f, -0.22f, leg / 3f);
					float part = 1f + leg * 2f + (side < 0 ? 0f : 1f);
					Limb(b, new[] { new Vector3(side * 0.38f, 0.1f, z), new Vector3(side * 0.62f, 0.24f, z + 0.03f * (1.5f - leg)), new Vector3(side * 0.82f, 0f, z - 0.05f * (leg - 1.5f)) },
						new[] { 0.06f, 0.05f, 0.025f }, Color.Lerp(back, accent, 0.4f), part);
				}
				// The claw: an arm forward and a heavy pincer.
				Limb(b, new[] { new Vector3(side * 0.3f, 0.1f, 0.28f), new Vector3(side * 0.42f, 0.16f, 0.46f) }, new[] { 0.07f, 0.06f }, back, 9f);
				Ellipsoid(b, new Vector3(side * 0.36f, 0.15f, 0.58f), new Vector3(0.11f, 0.08f, 0.15f), 3, 6, back, accent, 9f, 1f);
			}
		}

		// ── Primitives ────────────────────────────────────────────────

		/// <summary>An ellipsoid: back colour above, belly below (countershading), its part and part share on every vertex.</summary>
		private static void Ellipsoid(Builder b, Vector3 centre, Vector3 radii, int rings, int sides, Color back, Color belly, float part, float partShare)
		{
			int first = b.Count;
			for (int r = 0; r <= rings; r++)
			{
				float v = r / (float)rings;
				float lat = (v - 0.5f) * Mathf.PI; // -π/2 tail .. π/2 nose
				float ring = Mathf.Cos(lat), along = Mathf.Sin(lat);
				for (int s = 0; s <= sides; s++)
				{
					float a = s * Mathf.PI * 2f / sides;
					float cos = Mathf.Cos(a), sin = Mathf.Sin(a);
					var p = new Vector3(centre.x + cos * ring * radii.x, centre.y + sin * ring * radii.y, centre.z + along * radii.z);
					Color c = Color.Lerp(belly, back, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(sin * 0.9f + 0.55f)));
					b.Add(p, c, new Vector2(Mathf.Clamp01(0.5f - p.z), 0f), new Vector2(part, partShare));
				}
			}
			for (int r = 0; r < rings; r++)
			{
				for (int s = 0; s < sides; s++)
				{
					int a0 = first + r * (sides + 1) + s;
					int a1 = a0 + sides + 1;
					b.Quad(a0, a1, a1 + 1, a0 + 1);
				}
			}
		}

		/// <summary>A cone from a round base to a point.</summary>
		private static void Cone(Builder b, Vector3 baseCentre, Vector3 tip, float radius, Color colour, float part, int sides)
		{
			Vector3 axis = (tip - baseCentre).normalized;
			Vector3 u = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
			Vector3 w = Vector3.Cross(axis, u);
			int apex = b.Add(tip, colour, new Vector2(Mathf.Clamp01(0.5f - tip.z), 0f), new Vector2(part, 1f));
			int first = b.Count;
			for (int s = 0; s <= sides; s++)
			{
				float a = s * Mathf.PI * 2f / sides;
				Vector3 p = baseCentre + (u * Mathf.Cos(a) + w * Mathf.Sin(a)) * radius;
				b.Add(p, colour, new Vector2(Mathf.Clamp01(0.5f - p.z), 0f), new Vector2(part, 0f));
			}
			for (int s = 0; s < sides; s++)
			{
				b.Tri(apex, first + s + 1, first + s);
			}
		}

		/// <summary>A flat disc facing a direction (eyes, round ears).</summary>
		private static void Disc(Builder b, Vector3 centre, Vector3 normal, float radius, Color colour, float part)
		{
			Vector3 u = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
			Vector3 w = Vector3.Cross(normal, u);
			int middle = b.Add(centre + normal * 0.002f, colour, new Vector2(Mathf.Clamp01(0.5f - centre.z), 0f), new Vector2(part, 0f));
			const int sides = 6;
			int first = b.Count;
			for (int s = 0; s <= sides; s++)
			{
				float a = s * Mathf.PI * 2f / sides;
				Vector3 p = centre + (u * Mathf.Cos(a) + w * Mathf.Sin(a)) * radius;
				b.Add(p, colour, new Vector2(Mathf.Clamp01(0.5f - p.z), 0f), new Vector2(part, 0f));
			}
			for (int s = 0; s < sides; s++)
			{
				b.Tri(middle, first + s, first + s + 1);
			}
		}

		/// <summary>
		/// A limb, tail or ear along a polyline: a strip of quads, crossed with a second at right angles so it reads
		/// from any side (or one strip facing forward, for an ear). TEXCOORD1.y runs 0 at the root to 1 at the tip.
		/// </summary>
		private static void Limb(Builder b, Vector3[] points, float[] widths, Color colour, float part, bool single = false)
		{
			int n = points.Length;
			for (int pass = 0; pass < (single ? 1 : 2); pass++)
			{
				int first = b.Count;
				for (int i = 0; i < n; i++)
				{
					Vector3 along = (i < n - 1 ? points[i + 1] - points[i] : points[i] - points[i - 1]).normalized;
					Vector3 across = pass == 0 ? Vector3.Cross(along, Mathf.Abs(along.y) > 0.9f ? Vector3.forward : Vector3.up) : Vector3.Cross(along, Vector3.right);
					if (across.sqrMagnitude < 1e-6f)
					{
						across = Vector3.right;
					}
					across = across.normalized * (widths[i] * 0.5f);
					float share = n > 1 ? i / (float)(n - 1) : 0f;
					b.Add(points[i] - across, colour, new Vector2(Mathf.Clamp01(0.5f - points[i].z), 0f), new Vector2(part, share));
					b.Add(points[i] + across, colour, new Vector2(Mathf.Clamp01(0.5f - points[i].z), 0f), new Vector2(part, share));
				}
				for (int i = 0; i < n - 1; i++)
				{
					int a0 = first + i * 2;
					b.Quad(a0, a0 + 1, a0 + 3, a0 + 2);
				}
			}
		}

		private sealed class Builder
		{
			private readonly List<Vector3> positions = new List<Vector3>();
			private readonly List<Color> colours = new List<Color>();
			private readonly List<Vector2> uv0 = new List<Vector2>();
			private readonly List<Vector2> uv1 = new List<Vector2>();
			private readonly List<int> triangles = new List<int>();

			public int Count => positions.Count;

			public int Add(Vector3 position, Color colour, Vector2 body, Vector2 part)
			{
				positions.Add(position);
				colours.Add(new Color(colour.r, colour.g, colour.b, 1f));
				uv0.Add(body);
				uv1.Add(part);
				return positions.Count - 1;
			}

			public void Tri(int a, int b, int c)
			{
				triangles.Add(a);
				triangles.Add(b);
				triangles.Add(c);
			}

			public void Quad(int a, int b, int c, int d)
			{
				Tri(a, b, c);
				Tri(a, c, d);
			}

			public Mesh ToMesh(string name)
			{
				var mesh = new Mesh { name = "Ambient life: " + name, hideFlags = HideFlags.DontSave };
				mesh.SetVertices(positions);
				mesh.SetColors(colours);
				mesh.SetUVs(0, uv0);
				mesh.SetUVs(1, uv1);
				mesh.SetTriangles(triangles, 0);
				mesh.RecalculateNormals();
				mesh.RecalculateBounds();
				return mesh;
			}
		}
	}
}
