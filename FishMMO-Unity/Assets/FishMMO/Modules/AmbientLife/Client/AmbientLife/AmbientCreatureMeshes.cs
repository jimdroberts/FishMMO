using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// The ambient creatures' meshes, built at runtime from a few numbers: one unit long (bill to tail, nose to rump;
	/// a crab one unit wide), nose toward +z, back up +y, standing on the origin (a bat, which never lands, is centred
	/// on it). The vertex layout is FishAmbientLife.hlsl's: colour (linear), TEXCOORD0 x along the body (0 nose .. 1
	/// tail) and y across a wing's chord, TEXCOORD1 which part and how far out along it, TEXCOORD2 the colour's roles; the
	/// colour's alpha is how much of the vertex gives off the creature's own light (its emission mask: <see cref="AmbientGlowParts"/>,
	/// 0 for every vertex of a creature without one), and on a glowing lamp TEXCOORD0.y is that lamp's own twinkle offset.
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
	/// 1-4 legs (front left, front right, hind left, hind right), 5 tail, 6 head and ears; crabs 1-8 legs, 9 claws;
	/// beetles 1-6 legs, 9 feelers; spiders 1-8 legs, 9 palps; crickets 1-6 legs, 9 feelers; millipedes 1-8 legs (round and
	/// round down the body, so they step in waves); snails 1-2 feelers, 9 eye-stalks; fish 1-2 pectoral and 3-4 pelvic fins,
	/// 5 tail fin; jellies 1-8 tentacles; worms 1-4 bristles.
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
				case AmbientShape.Fish:
				case AmbientShape.Worm:
					return 2f;
				case AmbientShape.Crab:
				case AmbientShape.Beetle:
				case AmbientShape.Spider:
				case AmbientShape.Cricket:
				case AmbientShape.Millipede:
				case AmbientShape.Snail:
					return 3f;
				case AmbientShape.Swarm:
					return 4f;
				case AmbientShape.Jelly:
					return 5f;
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
		/// <remarks>
		/// <b>Colour roles</b> (TEXCOORD2, for the biome looks: <see cref="AmbientLooks"/>): every vertex colour is a blend of
		/// the kind's back, belly and accent (lerps and shades of them) plus a few fixed colours (the eyes). Built once more with
		/// pure red, green and blue standing in for the three, each vertex's colour IS its share of each: x of the back, y of
		/// the belly, z of the accent, their sum under one where it is shaded (a wing's underside) and near zero for an eye.
		/// The shader swaps each role's own colour for the look's by those shares, so the body keeps its countershading and
		/// its shading in any coat.
		/// </remarks>
		public static Mesh Build(AmbientCreatureKind kind, bool keepReadable = false)
		{
			var b = new Builder();
			Shape(b, kind, kind.Back.linear, kind.Belly.linear, kind.Accent.linear);
			var roles = new Builder();
			Shape(roles, kind, Color.red, Color.green, Color.blue);
			if (kind.Shape == AmbientShape.Jelly)
			{
				// A jelly's light runs out from its crown: its 'along' is the distance from the crown, not nose to tail.
				b.AlongFromCrown(JellyCrown, 1.3f);
			}
			b.TakeRoles(roles);
			Mesh mesh = b.ToMesh(kind.Name);
			// Wings beat and fold, legs swing, rabbits bound: room for all of it. A swarm's insects fly out to two of its
			// radii (its instance is scaled to one), and a midge column stands taller.
			Bounds bounds = mesh.bounds;
			bounds.Expand(1.2f);
			if (kind.Shape == AmbientShape.Swarm)
			{
				bounds = new Bounds(Vector3.zero, Vector3.one * 4f);
			}
			mesh.bounds = bounds;
			mesh.UploadMeshData(!keepReadable);
			return mesh;
		}

		/// <summary>
		/// A kind's emission mask vertex for vertex (COLOR.a), with its triangle count: what <see cref="Build"/> would give the mesh,
		/// worked out without one (a Mesh is native: this checks a kind's lamps and budget outside the editor).
		/// </summary>
		public static float[] MaskOf(AmbientCreatureKind kind, out int triangles)
		{
			var b = new Builder();
			Shape(b, kind, kind.Back.linear, kind.Belly.linear, kind.Accent.linear);
			triangles = b.Triangles;
			return b.Masks();
		}

		/// <summary>A kind's body into a builder, in the colours given for its back, belly and accent.</summary>
		private static void Shape(Builder b, AmbientCreatureKind kind, Color back, Color belly, Color accent)
		{
			// A creature that glows all over glows from every vertex (its eyes too: none of these have any to speak of).
			AmbientGlowParts glow = kind.Shape == AmbientShape.Swarm ? AmbientGlowParts.None : kind.GlowParts;
			b.Glow = glow == AmbientGlowParts.Body ? 1f : 0f;
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
					Lizard(b, back, belly, accent, glow == AmbientGlowParts.Spots);
					break;
				case AmbientShape.Crab:
					Crab(b, back, belly, accent);
					break;
				case AmbientShape.Beetle:
					Beetle(b, back, belly, accent, glow == AmbientGlowParts.Lanterns);
					break;
				case AmbientShape.Spider:
					Spider(b, back, belly, accent);
					break;
				case AmbientShape.Cricket:
					Cricket(b, back, belly, accent);
					break;
				case AmbientShape.Swarm:
					Swarm(b, kind, back, belly);
					break;
				case AmbientShape.Millipede:
					Millipede(b, back, belly, accent);
					break;
				case AmbientShape.Snail:
					Snail(b, back, belly, accent, glow == AmbientGlowParts.Foot);
					break;
				case AmbientShape.Fish:
					Fish(b, back, belly, accent, glow == AmbientGlowParts.LateralLines);
					break;
				case AmbientShape.Jelly:
					Jelly(b, back, belly, accent, glow == AmbientGlowParts.Bell);
					break;
				case AmbientShape.Worm:
					Worm(b, back, belly, accent);
					break;
				default:
					Bird(b, Plan(kind.Shape), back, belly, accent, glow == AmbientGlowParts.WingEdges);
					break;
			}
			b.Glow = 0f;
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
			/// <summary>A neck between the body and the head (geese, swans, cranes), when its radii are non-zero.</summary>
			public Vector3 NeckCentre, NeckRadii;
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
				case AmbientShape.Owl:
					return new BirdPlan
					{
						// The head is the owl: nearly as wide as the body, the bill a hook barely out of the face.
						BodyCentre = new Vector3(0f, 0.2f, -0.03f), BodyRadii = new Vector3(0.15f, 0.15f, 0.2f),
						HeadCentre = new Vector3(0f, 0.31f, 0.15f), HeadRadii = new Vector3(0.12f, 0.115f, 0.1f), BeakLength = 0.04f, BeakRadius = 0.022f,
						TailLength = 0.13f, TailRoot = 0.07f, TailTip = 0.1f, Shoulder = new Vector3(0.1f, 0.26f, 0.06f), Span = 1f,
						// Broad, rounded, the fingers short: silent flight.
						Columns = new[] { 0f, 0.4f, 0.75f, 0.92f, 1f }, Lead = new[] { 0.14f, 0.15f, 0.13f, 0.09f, 0.02f }, Trail = new[] { -0.17f, -0.2f, -0.18f, -0.12f, -0.04f },
						LegLength = 0.1f,
					};
				case AmbientShape.Goose:
					return new BirdPlan
					{
						// Stretched out in flight: the neck as long as the body, the legs tucked away.
						BodyCentre = new Vector3(0f, 0.12f, -0.08f), BodyRadii = new Vector3(0.12f, 0.11f, 0.23f),
						NeckCentre = new Vector3(0f, 0.14f, 0.2f), NeckRadii = new Vector3(0.035f, 0.035f, 0.15f),
						HeadCentre = new Vector3(0f, 0.155f, 0.38f), HeadRadii = new Vector3(0.045f, 0.045f, 0.065f), BeakLength = 0.08f, BeakRadius = 0.02f,
						TailLength = 0.1f, TailRoot = 0.06f, TailTip = 0.08f, Shoulder = new Vector3(0.08f, 0.15f, 0.02f), Span = 1.2f,
						Columns = new[] { 0f, 0.4f, 0.75f, 1f }, Lead = new[] { 0.09f, 0.09f, 0.05f, -0.08f }, Trail = new[] { -0.1f, -0.12f, -0.09f, -0.1f },
						LegLength = 0f, DarkTips = true,
					};
				case AmbientShape.Butterfly:
					return new BirdPlan
					{
						// The wings are the butterfly: broad and rounded, the hind wing's lobe trailing; the body a slip under them.
						BodyCentre = new Vector3(0f, 0.05f, 0f), BodyRadii = new Vector3(0.05f, 0.05f, 0.3f),
						HeadCentre = new Vector3(0f, 0.06f, 0.33f), HeadRadii = new Vector3(0.06f, 0.06f, 0.06f), BeakLength = 0f, BeakRadius = 0f,
						TailLength = 0f, Shoulder = new Vector3(0.04f, 0.07f, 0.1f), Span = 1.05f,
						Columns = new[] { 0f, 0.35f, 0.7f, 0.9f, 1f }, Lead = new[] { 0.32f, 0.5f, 0.55f, 0.42f, 0.2f }, Trail = new[] { -0.42f, -0.55f, -0.35f, -0.1f, 0.05f },
						LegLength = 0f, DarkTips = true,
					};
				case AmbientShape.Dragonfly:
					return new BirdPlan
					{
						// A thorax with the wings on it and a long abdomen trailing behind.
						BodyCentre = new Vector3(0f, 0.05f, 0.2f), BodyRadii = new Vector3(0.06f, 0.06f, 0.13f),
						NeckCentre = new Vector3(0f, 0.05f, -0.2f), NeckRadii = new Vector3(0.028f, 0.028f, 0.32f),
						HeadCentre = new Vector3(0f, 0.06f, 0.36f), HeadRadii = new Vector3(0.07f, 0.06f, 0.05f), BeakLength = 0f, BeakRadius = 0f,
						TailLength = 0f, Shoulder = new Vector3(0.04f, 0.08f, 0.25f), Span = 0.75f,
						Columns = new[] { 0f, 0.5f, 0.9f, 1f }, Lead = new[] { 0.1f, 0.11f, 0.09f, 0.05f }, Trail = new[] { -0.02f, -0.04f, -0.04f, -0.01f },
						LegLength = 0f,
					};
				case AmbientShape.Ray:
					return new BirdPlan
					{
						// All wing: a flat body whose fins run from the head to the tail's root, broad at the body and
						// tapering to a point, and a whip of a tail trailing behind.
						BodyCentre = new Vector3(0f, 0f, 0.02f), BodyRadii = new Vector3(0.13f, 0.045f, 0.34f),
						HeadCentre = new Vector3(0f, 0.005f, 0.3f), HeadRadii = new Vector3(0.07f, 0.035f, 0.06f), BeakLength = 0f, BeakRadius = 0f,
						TailLength = 0.62f, TailRoot = 0.018f, TailTip = 0.003f, Shoulder = new Vector3(0.11f, 0.01f, 0.08f), Span = 0.55f,
						Columns = new[] { 0f, 0.35f, 0.7f, 1f }, Lead = new[] { 0.3f, 0.2f, 0.08f, -0.06f }, Trail = new[] { -0.36f, -0.24f, -0.12f, -0.08f },
						LegLength = 0f, DarkTips = true,
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

		private static void Bird(Builder b, BirdPlan plan, Color back, Color belly, Color accent, bool glowingEdges = false)
		{
			// The body and the head: countershaded ellipsoids, the head turning (part 4) on its own. A wisp moth's body glows softly.
			b.Glow = glowingEdges ? 0.45f : b.Glow;
			Ellipsoid(b, plan.BodyCentre, plan.BodyRadii, 6, 8, back, belly, 0f, 0f);
			if (plan.NeckRadii.sqrMagnitude > 0f)
			{
				Ellipsoid(b, plan.NeckCentre, plan.NeckRadii, 4, 6, back, Color.Lerp(belly, back, 0.5f), 0f, 0f);
			}
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
					// A wisp moth's light is in its wings' outer edges: the trailing edge brightening out to the tip, the leading edge
					// only at the tip (the light shades across the chord between them).
					float lead = b.Glow, trail = b.Glow;
					if (glowingEdges)
					{
						lead = Mathf.SmoothStep(0.1f, 1f, Mathf.InverseLerp(0.6f, 1f, s));
						trail = Mathf.Lerp(0.3f, 1f, s);
					}
					float keep = b.Glow;
					b.Glow = lead;
					b.Add(new Vector3(x, y, plan.Shoulder.z + plan.Lead[c] - 0.08f), top, new Vector2(0.4f, 0f), new Vector2(part, s));
					b.Glow = trail;
					b.Add(new Vector3(x, y - 0.005f, plan.Shoulder.z + plan.Trail[c] - 0.08f), top * 0.92f, new Vector2(0.55f, 1f), new Vector2(part, s));
					b.Glow = keep;
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

		private static void Lizard(Builder b, Color back, Color belly, Color accent, bool spots = false)
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
			if (spots)
			{
				// A lantern newt's lamps: a double row of round spots down the back (a fire salamander's livery, lit), each with
				// its own twinkle offset, and two more on the tail's root.
				float keep = b.Glow;
				b.Glow = 1f;
				for (int i = 0; i < 6; i++)
				{
					float z = Mathf.Lerp(0.24f, -0.24f, i / 5f);
					for (int side = -1; side <= 1; side += 2)
					{
						float x = side * 0.045f;
						float top = 0.06f + 0.06f * Mathf.Sqrt(Mathf.Max(0f, 1f - (x / 0.11f) * (x / 0.11f) - (z / 0.3f) * (z / 0.3f)));
						float spark = SeaLifePlacement.Unit(SeaLifePlacement.Mix((uint)(i * 2 + (side + 1) / 2) * 0x9E3779B9u ^ 0x5B07u));
						Disc(b, new Vector3(x, top + 0.003f, z), new Vector3(side * 0.35f, 1f, 0f).normalized, 0.022f, accent, 0f, spark);
					}
				}
				for (int i = 0; i < 2; i++)
				{
					float spark = SeaLifePlacement.Unit(SeaLifePlacement.Mix((uint)(20 + i) * 0x9E3779B9u ^ 0x5B07u));
					Disc(b, new Vector3(0f, 0.065f, -0.38f - 0.14f * i), Vector3.up, 0.018f, accent, 5f, spark);
				}
				b.Glow = keep;
			}
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

		private static void Beetle(Builder b, Color back, Color belly, Color accent, bool lanterns = false)
		{
			// A domed shell (the wing cases, a seam down the middle), the shield behind the head, a small head with
			// feelers, and six legs splayed out from under the shell.
			Ellipsoid(b, new Vector3(0f, 0.2f, -0.1f), new Vector3(0.26f, 0.17f, 0.36f), 6, 8, back, belly, 0f, 0f);
			Limb(b, new[] { new Vector3(0f, 0.37f, 0.18f), new Vector3(0f, 0.39f, -0.08f), new Vector3(0f, 0.3f, -0.36f) }, new[] { 0.012f, 0.012f, 0.008f }, accent, 0f, true);
			Ellipsoid(b, new Vector3(0f, 0.19f, 0.3f), new Vector3(0.2f, 0.12f, 0.12f), 4, 7, back, belly, 0f, 0f);
			Ellipsoid(b, new Vector3(0f, 0.15f, 0.43f), new Vector3(0.1f, 0.08f, 0.08f), 4, 6, Color.Lerp(back, accent, 0.5f), belly, 0f, 0f);
			for (int side = -1; side <= 1; side += 2)
			{
				Disc(b, new Vector3(side * 0.085f, 0.18f, 0.47f), new Vector3(side, 0.2f, 0.4f).normalized, 0.025f, new Color(0.01f, 0.01f, 0.012f), 0f);
				for (int leg = 0; leg < 3; leg++)
				{
					float z = Mathf.Lerp(0.3f, -0.1f, leg / 2f);
					float splay = (leg - 1) * 0.12f;
					float part = 1f + leg * 2f + (side < 0 ? 0f : 1f);
					Limb(b, new[] { new Vector3(side * 0.16f, 0.12f, z), new Vector3(side * 0.38f, 0.24f, z + splay * 0.5f), new Vector3(side * 0.5f, 0f, z + splay) },
						new[] { 0.035f, 0.03f, 0.018f }, Color.Lerp(back, accent, 0.6f), part);
				}
				// A feeler: forward and up from the head, swaying (part 9).
				Limb(b, new[] { new Vector3(side * 0.05f, 0.19f, 0.48f), new Vector3(side * 0.14f, 0.3f, 0.62f), new Vector3(side * 0.2f, 0.34f, 0.74f) },
					new[] { 0.016f, 0.012f, 0.008f }, accent, 9f);
			}
			if (lanterns)
			{
				// A click beetle's headlamps: two round lamps on the shield's hind corners, shining up and out.
				float keep = b.Glow;
				b.Glow = 1f;
				for (int side = -1; side <= 1; side += 2)
				{
					Disc(b, new Vector3(side * 0.11f, 0.3f, 0.27f), new Vector3(side * 0.45f, 1f, 0f).normalized, 0.06f, Color.Lerp(belly, accent, 0.3f), 0f, side < 0 ? 0.13f : 0.61f);
				}
				b.Glow = keep;
			}
		}

		private static void Spider(Builder b, Color back, Color belly, Color accent)
		{
			// The head-and-body small and low in front, the abdomen round behind it; eight long legs out of the front part, bent
			// high at the knee and down to the feet well out to the sides, the front pairs reaching forward, the back ones back.
			Ellipsoid(b, new Vector3(0f, 0.16f, 0.12f), new Vector3(0.16f, 0.1f, 0.18f), 4, 7, back, belly, 0f, 0f);
			Ellipsoid(b, new Vector3(0f, 0.22f, -0.22f), new Vector3(0.22f, 0.19f, 0.27f), 5, 8, back, belly, 0f, 0f);
			for (int side = -1; side <= 1; side += 2)
			{
				Disc(b, new Vector3(side * 0.04f, 0.24f, 0.28f), new Vector3(side * 0.3f, 0.6f, 0.75f).normalized, 0.022f, new Color(0.01f, 0.01f, 0.012f), 0f);
				for (int leg = 0; leg < 4; leg++)
				{
					float z = Mathf.Lerp(0.22f, 0.02f, leg / 3f);
					float reach = Mathf.Lerp(0.35f, -0.4f, leg / 3f);
					float part = 1f + leg * 2f + (side < 0 ? 0f : 1f);
					Limb(b, new[] { new Vector3(side * 0.12f, 0.15f, z), new Vector3(side * 0.42f, 0.44f, z + reach * 0.45f), new Vector3(side * 0.78f, 0f, z + reach) },
						new[] { 0.03f, 0.026f, 0.014f }, Color.Lerp(back, accent, 0.5f), part);
				}
				// A palp, short and forward (part 9).
				Limb(b, new[] { new Vector3(side * 0.05f, 0.12f, 0.28f), new Vector3(side * 0.08f, 0.1f, 0.38f) }, new[] { 0.025f, 0.018f }, accent, 9f);
			}
		}

		private static void Cricket(Builder b, Color back, Color belly, Color accent)
		{
			// A humped body in bands, a small head tucked under its front, long spiny hind legs folded high (the knees over the
			// back), short front legs, and feelers twice the body's length sweeping out ahead (part 9).
			Ellipsoid(b, new Vector3(0f, 0.2f, -0.05f), new Vector3(0.16f, 0.17f, 0.42f), 6, 8, back, belly, 0f, 0f);
			Ellipsoid(b, new Vector3(0f, 0.14f, 0.38f), new Vector3(0.1f, 0.11f, 0.1f), 4, 6, Color.Lerp(back, accent, 0.3f), belly, 0f, 0f);
			for (int side = -1; side <= 1; side += 2)
			{
				Disc(b, new Vector3(side * 0.08f, 0.17f, 0.43f), new Vector3(side, 0.2f, 0.4f).normalized, 0.022f, new Color(0.01f, 0.01f, 0.012f), 0f);
				// Front and middle legs.
				for (int leg = 0; leg < 2; leg++)
				{
					float z = leg == 0 ? 0.26f : 0.08f;
					float part = 1f + leg * 2f + (side < 0 ? 0f : 1f);
					Limb(b, new[] { new Vector3(side * 0.1f, 0.1f, z), new Vector3(side * 0.28f, 0.16f, z + 0.08f), new Vector3(side * 0.36f, 0f, z + 0.12f) },
						new[] { 0.026f, 0.022f, 0.014f }, Color.Lerp(back, accent, 0.5f), part);
				}
				// The hind leg: the thigh up and back to a knee over the body, the shin down to the ground far behind.
				Limb(b, new[] { new Vector3(side * 0.12f, 0.14f, -0.12f), new Vector3(side * 0.24f, 0.46f, -0.42f), new Vector3(side * 0.3f, 0f, -0.82f) },
					new[] { 0.06f, 0.035f, 0.016f }, Color.Lerp(back, accent, 0.4f), side < 0 ? 5f : 6f);
				Limb(b, new[] { new Vector3(side * 0.04f, 0.18f, 0.46f), new Vector3(side * 0.3f, 0.42f, 0.95f), new Vector3(side * 0.62f, 0.3f, 1.5f), new Vector3(side * 0.9f, 0.16f, 2f) },
					new[] { 0.014f, 0.01f, 0.007f, 0.004f }, accent, 9f);
			}
		}

		// ── The caves' light-makers (2026-10-20) ─────────────────────

		private static void Millipede(Builder b, Color back, Color belly, Color accent)
		{
			// A string of round segments low to the ground from the head (+z) back, a pair of short legs under each, stepping in
			// waves: the parts go round 1..8 down the body, so the crawler's per-part offset runs a wave along it.
			const int segments = 12;
			for (int i = 0; i < segments; i++)
			{
				float z = Mathf.Lerp(0.4f, -0.44f, i / (float)(segments - 1));
				float r = i == segments - 1 ? 0.036f : 0.046f;
				Ellipsoid(b, new Vector3(0f, 0.045f, z), new Vector3(r, 0.04f, 0.045f), 3, 6, back, belly, 0f, 0f);
				for (int side = -1; side <= 1; side += 2)
				{
					float part = 1f + (i * 2 + (side < 0 ? 0 : 1)) % 8;
					Limb(b, new[] { new Vector3(side * 0.035f, 0.025f, z), new Vector3(side * 0.08f, 0f, z + 0.012f) }, new[] { 0.014f, 0.01f }, Color.Lerp(belly, accent, 0.4f), part, true);
				}
			}
			// The head, a little narrower, with short elbowed feelers.
			Ellipsoid(b, new Vector3(0f, 0.04f, 0.47f), new Vector3(0.04f, 0.034f, 0.04f), 3, 6, Color.Lerp(back, accent, 0.4f), belly, 0f, 0f);
			for (int side = -1; side <= 1; side += 2)
			{
				Limb(b, new[] { new Vector3(side * 0.02f, 0.05f, 0.5f), new Vector3(side * 0.05f, 0.08f, 0.55f), new Vector3(side * 0.07f, 0.05f, 0.6f) },
					new[] { 0.01f, 0.008f, 0.006f }, accent, 9f, true);
			}
		}

		private static void Snail(Builder b, Color back, Color belly, Color accent, bool lamp)
		{
			// The soft foot (one unit long), the head at its front, the shell's coil riding the back: a big whorl and two smaller
			// toward its apex, in the shell's colour (the back) with its darker banding (the accent).
			Ellipsoid(b, new Vector3(0f, 0.045f, 0f), new Vector3(0.13f, 0.055f, 0.5f), 4, 8, belly, belly, 0f, 0f);
			Ellipsoid(b, new Vector3(0f, 0.3f, -0.1f), new Vector3(0.13f, 0.24f, 0.27f), 5, 8, back, Color.Lerp(back, accent, 0.5f), 0f, 0f);
			Ellipsoid(b, new Vector3(0.02f, 0.4f, -0.03f), new Vector3(0.09f, 0.15f, 0.17f), 4, 7, Color.Lerp(back, accent, 0.35f), back, 0f, 0f);
			Ellipsoid(b, new Vector3(0.035f, 0.47f, 0.02f), new Vector3(0.05f, 0.08f, 0.08f), 3, 6, accent, back, 0f, 0f);
			// The light organ under the head (a glowing land snail flashes from the front of its foot).
			float keep = b.Glow;
			b.Glow = lamp ? 1f : keep;
			Ellipsoid(b, new Vector3(0f, 0.07f, 0.4f), new Vector3(0.1f, 0.075f, 0.13f), 4, 7, Color.Lerp(belly, accent, 0.2f), belly, 0f, 0f);
			b.Glow = lamp ? 0.5f : keep;
			for (int side = -1; side <= 1; side += 2)
			{
				// The lower feelers, short and forward (parts 1, 2); the eye-stalks up and out (part 9).
				Limb(b, new[] { new Vector3(side * 0.04f, 0.07f, 0.5f), new Vector3(side * 0.09f, 0.05f, 0.6f) }, new[] { 0.022f, 0.014f }, belly, side < 0 ? 1f : 2f, true);
				Limb(b, new[] { new Vector3(side * 0.04f, 0.12f, 0.46f), new Vector3(side * 0.08f, 0.26f, 0.56f), new Vector3(side * 0.1f, 0.36f, 0.6f) },
					new[] { 0.026f, 0.018f, 0.016f }, belly, 9f);
			}
			b.Glow = keep;
		}

		private static void Fish(Builder b, Color back, Color belly, Color accent, bool lines)
		{
			// A slim fusiform body (nose +z), a forked tail fin (part 5), a low back fin, pectoral (1, 2) and pelvic (3, 4) fins.
			var centre = new Vector3(0f, 0f, 0.04f);
			var radii = new Vector3(0.085f, 0.13f, 0.4f);
			Ellipsoid(b, centre, radii, 6, 8, back, belly, 0f, 0f);
			Color fin = Color.Lerp(belly, accent, 0.5f);
			int r0 = b.Add(new Vector3(0f, 0.05f, -0.3f), fin, new Vector2(0.8f, 0f), new Vector2(5f, 0f));
			int r1 = b.Add(new Vector3(0f, -0.05f, -0.3f), fin, new Vector2(0.8f, 0f), new Vector2(5f, 0f));
			int t0 = b.Add(new Vector3(0f, 0.17f, -0.56f), fin, new Vector2(1f, 0f), new Vector2(5f, 1f));
			int notch = b.Add(new Vector3(0f, 0f, -0.46f), fin, new Vector2(0.96f, 0f), new Vector2(5f, 0.6f));
			int t1 = b.Add(new Vector3(0f, -0.17f, -0.56f), fin, new Vector2(1f, 0f), new Vector2(5f, 1f));
			b.Tri(r0, t0, notch);
			b.Tri(r0, notch, r1);
			b.Tri(r1, notch, t1);
			int d0 = b.Add(new Vector3(0f, 0.12f, 0.06f), fin, new Vector2(0.44f, 0f), new Vector2(0f, 0f));
			int d1 = b.Add(new Vector3(0f, 0.11f, -0.12f), fin, new Vector2(0.62f, 0f), new Vector2(0f, 0f));
			int d2 = b.Add(new Vector3(0f, 0.2f, -0.08f), fin, new Vector2(0.58f, 0f), new Vector2(0f, 0f));
			b.Tri(d0, d1, d2);
			for (int side = -1; side <= 1; side += 2)
			{
				Limb(b, new[] { new Vector3(side * 0.07f, -0.03f, 0.2f), new Vector3(side * 0.17f, -0.07f, 0.1f) }, new[] { 0.07f, 0.04f }, fin, side < 0 ? 1f : 2f, true);
				Limb(b, new[] { new Vector3(side * 0.035f, -0.11f, -0.06f), new Vector3(side * 0.07f, -0.17f, -0.14f) }, new[] { 0.045f, 0.025f }, fin, side < 0 ? 3f : 4f, true);
			}
			if (!lines)
			{
				return;
			}
			// The lateral lines: a narrow band of light down each flank, just proud of the scales, from behind the head to the tail's
			// root; and three lamps under where the eye would be (a blind fish's, lit).
			float keep = b.Glow;
			b.Glow = 1f;
			for (int side = -1; side <= 1; side += 2)
			{
				int first = b.Count;
				const int steps = 7;
				for (int i = 0; i < steps; i++)
				{
					float z = Mathf.Lerp(0.3f, -0.3f, i / (float)(steps - 1));
					float u = (z - centre.z) / radii.z;
					float x = side * (radii.x * Mathf.Sqrt(Mathf.Max(0f, 1f - u * u)) + 0.004f);
					float h = 0.012f * (1f - 0.4f * Mathf.Abs(u));
					b.Add(new Vector3(x, 0.01f + h, z), belly, new Vector2(Mathf.Clamp01(0.5f - z), 0f), new Vector2(0f, 0f));
					b.Add(new Vector3(x, 0.01f - h, z), belly, new Vector2(Mathf.Clamp01(0.5f - z), 0f), new Vector2(0f, 0f));
				}
				for (int i = 0; i < steps - 1; i++)
				{
					int a0 = first + i * 2;
					b.Quad(a0, a0 + 1, a0 + 3, a0 + 2);
				}
				for (int i = 0; i < 3; i++)
				{
					float z = 0.3f + 0.04f * i;
					float u = (z - centre.z) / radii.z;
					float x = side * (radii.x * 0.8f * Mathf.Sqrt(Mathf.Max(0f, 1f - u * u)) + 0.003f);
					Disc(b, new Vector3(x, -0.05f, z), new Vector3(side, -0.4f, 0f).normalized, 0.012f, belly, 0f, 0.3f * i);
				}
			}
			b.Glow = keep;
		}

		/// <summary>The crown of a jelly's bell (its mesh's top), whence its light runs out (the vertices' 'along').</summary>
		private static readonly Vector3 JellyCrown = new Vector3(0f, 0.32f, 0f);

		private static void Jelly(Builder b, Color back, Color belly, Color accent, bool lit)
		{
			// The bell (one unit across) from its crown down past the rim's turn, countershaded crown to rim; TEXCOORD1.y how far
			// down it the vertex is (0 crown .. 1 rim), which the shader's squeeze works on. Lit: the rim and four canals glow,
			// the rest of the bell a little; the tentacles (parts 1-8) hang from the rim, glowing toward their tips.
			const int rings = 5, sides = 10;
			const float turn = 100f * Mathf.Deg2Rad;
			float keep = b.Glow;
			int first = b.Count;
			for (int r = 0; r <= rings; r++)
			{
				float down = r / (float)rings;
				float theta = down * turn;
				for (int s = 0; s <= sides; s++)
				{
					float a = s * Mathf.PI * 2f / sides;
					var p = new Vector3(Mathf.Sin(theta) * 0.5f * Mathf.Cos(a), 0.32f * Mathf.Cos(theta), Mathf.Sin(theta) * 0.5f * Mathf.Sin(a));
					float canal = Mathf.Pow(Mathf.Abs(Mathf.Cos(2f * a)), 8f);
					b.Glow = lit ? Mathf.Max(Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.7f, 1f, down)), Mathf.Max(0.8f * canal * down, 0.2f)) : keep;
					b.Add(p, Color.Lerp(back, belly, down), new Vector2(0f, 0f), new Vector2(0f, down));
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
			// The mouth-arms hanging under the middle.
			b.Glow = lit ? 0.6f : keep;
			Ellipsoid(b, new Vector3(0f, 0.02f, 0f), new Vector3(0.07f, 0.16f, 0.07f), 3, 6, Color.Lerp(belly, accent, 0.5f), belly, 0f, 1f);
			for (int k = 0; k < 8; k++)
			{
				float a = (k + 0.5f) * Mathf.PI * 2f / 8f;
				var rim = new Vector3(0.48f * Mathf.Cos(a), 0.32f * Mathf.Cos(turn), 0.48f * Mathf.Sin(a));
				var points = new Vector3[4];
				for (int i = 0; i < 4; i++)
				{
					float t = i / 3f;
					points[i] = rim + new Vector3(-0.1f * t * Mathf.Cos(a), -0.85f * t, -0.1f * t * Mathf.Sin(a));
				}
				int start = b.Count;
				b.Glow = lit ? 0.5f : keep;
				Limb(b, points, new[] { 0.02f, 0.014f, 0.01f, 0.006f }, Color.Lerp(belly, accent, 0.6f), 1f + k);
				if (lit)
				{
					b.GlowFrom(start, 0.4f, 1f);
				}
			}
			b.Glow = keep;
		}

		private static void Worm(Builder b, Color back, Color belly, Color accent)
		{
			// A slender round body tapering to both ends (nose +z), ringed, a few bristles near the front (parts 1-4).
			const int rings = 12, sides = 6;
			int first = b.Count;
			for (int r = 0; r <= rings; r++)
			{
				float t = r / (float)rings;
				float z = Mathf.Lerp(0.5f, -0.5f, t);
				float radius = 0.008f + 0.034f * Mathf.Sqrt(Mathf.Max(0f, 1f - (2f * t - 1f) * (2f * t - 1f)));
				for (int s = 0; s <= sides; s++)
				{
					float a = s * Mathf.PI * 2f / sides;
					float sin = Mathf.Sin(a);
					var p = new Vector3(Mathf.Cos(a) * radius, 0.04f + sin * radius, z);
					Color c = Color.Lerp(belly, back, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(sin * 0.9f + 0.55f)));
					b.Add(p, c, new Vector2(Mathf.Clamp01(0.5f - z), 0f), new Vector2(r == 0 ? 6f : 0f, 0f));
				}
			}
			for (int r = 0; r < rings; r++)
			{
				for (int s = 0; s < sides; s++)
				{
					int a0 = first + r * (sides + 1) + s;
					int a1 = a0 + sides + 1;
					b.Quad(a0, a0 + 1, a1 + 1, a1);
				}
			}
			for (int i = 0; i < 2; i++)
			{
				for (int side = -1; side <= 1; side += 2)
				{
					float z = 0.32f - 0.12f * i;
					float part = 1f + i * 2f + (side < 0 ? 0f : 1f);
					Limb(b, new[] { new Vector3(side * 0.03f, 0.03f, z), new Vector3(side * 0.06f, 0.01f, z - 0.01f) }, new[] { 0.006f, 0.003f }, accent, part, true);
				}
			}
		}

		// ── Primitives ────────────────────────────────────────────────

		/// <summary>An ellipsoid: back colour above, belly below (countershading), its part and part share on every vertex.</summary>
		/// <summary>
		/// A swarm's insects, as many as the kind's largest swarm: a small quad each, all at the middle, which the shader
		/// flies out to its place and turns to the camera. TEXCOORD0 is the quad's corner (-1..1), TEXCOORD1 the insect's
		/// index and where it falls in the swarm (0..1, so a smaller swarm draws the first share of them). Each a little its
		/// own shade, between the kind's back and belly.
		/// </summary>
		private static void Swarm(Builder b, AmbientCreatureKind kind, Color back, Color belly)
		{
			int count = Mathf.Clamp(kind.GroupSize.y, 1, 200);
			for (int i = 0; i < count; i++)
			{
				uint h = SeaLifePlacement.Mix((uint)i * 0x9E3779B9u ^ 0x5EA5u);
				Color shade = Color.Lerp(back, belly, SeaLifePlacement.Unit(h));
				var part = new Vector2(i, (i + 0.5f) / count);
				// A hair apart, so the quad has a facing; the shader places it.
				int first = b.Add(new Vector3(-0.001f, -0.001f, 0f), shade, new Vector2(-1f, -1f), part);
				b.Add(new Vector3(0.001f, -0.001f, 0f), shade, new Vector2(1f, -1f), part);
				b.Add(new Vector3(0.001f, 0.001f, 0f), shade, new Vector2(1f, 1f), part);
				b.Add(new Vector3(-0.001f, 0.001f, 0f), shade, new Vector2(-1f, 1f), part);
				b.Quad(first, first + 1, first + 2, first + 3);
			}
		}

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
		private static void Disc(Builder b, Vector3 centre, Vector3 normal, float radius, Color colour, float part, float spark = 0f)
		{
			Vector3 u = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
			Vector3 w = Vector3.Cross(normal, u);
			int middle = b.Add(centre + normal * 0.002f, colour, new Vector2(Mathf.Clamp01(0.5f - centre.z), spark), new Vector2(part, 0f));
			const int sides = 6;
			int first = b.Count;
			for (int s = 0; s <= sides; s++)
			{
				float a = s * Mathf.PI * 2f / sides;
				Vector3 p = centre + (u * Mathf.Cos(a) + w * Mathf.Sin(a)) * radius;
				b.Add(p, colour, new Vector2(Mathf.Clamp01(0.5f - p.z), spark), new Vector2(part, 0f));
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
			private readonly List<Vector3> roles = new List<Vector3>();
			private readonly List<int> triangles = new List<int>();

			/// <summary>The emission mask (COLOR.a) every vertex added from now on carries, 0..1.</summary>
			public float Glow;

			public int Count => positions.Count;

			public int Triangles => triangles.Count / 3;

			/// <summary>Every vertex's emission mask (COLOR.a), in order.</summary>
			public float[] Masks()
			{
				var masks = new float[colours.Count];
				for (int i = 0; i < masks.Length; i++)
				{
					masks[i] = colours[i].a;
				}
				return masks;
			}

			/// <summary>Grades the emission mask of the vertices from <paramref name="first"/> on by how far out along their part they are (TEXCOORD1.y).</summary>
			public void GlowFrom(int first, float root, float tip)
			{
				for (int i = first; i < colours.Count; i++)
				{
					Color c = colours[i];
					c.a = Mathf.Lerp(root, tip, uv1[i].y);
					colours[i] = c;
				}
			}

			/// <summary>Rewrites every vertex's 'along' (TEXCOORD0.x) as its distance from a point over a span, 0..1.</summary>
			public void AlongFromCrown(Vector3 crown, float span)
			{
				for (int i = 0; i < positions.Count; i++)
				{
					uv0[i] = new Vector2(Mathf.Clamp01(Vector3.Distance(positions[i], crown) / span), uv0[i].y);
				}
			}

			/// <summary>The colour roles: the same body built in pure red, green and blue for back, belly and accent.</summary>
			public void TakeRoles(Builder basis)
			{
				roles.Clear();
				if (basis.colours.Count != colours.Count)
				{
					Debug.LogError($"[Ambient life] colour roles built {basis.colours.Count} vertices against {colours.Count}: drawn in the kind's own colours only.");
					return;
				}
				foreach (Color c in basis.colours)
				{
					roles.Add(new Vector3(c.r, c.g, c.b));
				}
			}

			public int Add(Vector3 position, Color colour, Vector2 body, Vector2 part)
			{
				positions.Add(position);
				colours.Add(new Color(colour.r, colour.g, colour.b, Mathf.Clamp01(Glow)));
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
				if (roles.Count == positions.Count)
				{
					mesh.SetUVs(2, roles);
				}
				mesh.SetTriangles(triangles, 0);
				mesh.RecalculateNormals();
				mesh.RecalculateBounds();
				return mesh;
			}
		}
	}
}
