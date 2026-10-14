using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Draws a carved cave's rock only where it can be seen: all of it while the camera is in the cave; from outside, the
	/// pieces at its mouths always (they are the opening seen from outside) and the rest only within <see cref="reach"/>
	/// of the camera (at a mouth looking in). Everything deeper is behind the hill.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The pieces are the 32 m cubes the cut meshed the cave in (CaveMesher); their colliders are elsewhere, on the
	/// server-kept cave root, and are never touched. On the cave's client-only visual, so a server never runs it. Checked a
	/// few times a second, staggered between caves, and only the renderers whose state changes are written.
	/// </para>
	/// <para>
	/// No property block on the pieces: one takes a renderer out of the SRP Batcher and the GPU Resident Drawer. The cave
	/// mode of the rock shader's ground contact (<c>_FishContactOn</c> 2, FishGroundColour.hlsl) was set through one, but
	/// the pieces wear FishMMO/Weather Lit, whose forward pass compiles the contact only for the GPU-driven rocks
	/// (FISH_INDIRECT_INSTANCED): the value was read by nothing they draw. A cave contact for the pieces would be a
	/// per-material value (a keyword, or a field of the material's constant buffer), never a block.
	/// </para>
	/// <para>
	/// <b>In the cave</b> is in one of its voids' boxes (its passages', chambers', chasms' and shafts', set by the cut) or near
	/// one of its pieces' boxes. The pieces' boxes alone were not enough once halls grew to 160 m (2026-10-13): a piece is a
	/// 32 m cube of the surface, so the air in the middle of a hall, a few metres over its floor, was in none of them, and the
	/// far walls were switched off by distance while the camera stood inside.
	/// </para>
	/// <para>
	/// The caves alive and their pieces' boxes are open to the client (<see cref="Live"/>), so a full-screen effect can
	/// ask whether the camera is sealed in one (UndergroundView).
	/// </para>
	/// </remarks>
	[DisallowMultipleComponent]
	public sealed class CaveVisibility : MonoBehaviour
	{
		[SerializeField] private Renderer[] pieces = System.Array.Empty<Renderer>();
		[SerializeField] private bool[] mouth = System.Array.Empty<bool>();
		[Tooltip("How near a piece's box the camera must be for it to be drawn, metres.")]
		[SerializeField] private float reach = 72f;
		[Tooltip("The cave's voids, world boxes: inside any of them the camera is in the cave.")]
		[SerializeField] private Bounds[] volumes = System.Array.Empty<Bounds>();

		private const int Interval = 6;
		/// <summary>How near a piece's box the camera counts as in the cave, metres (the boxes are the meshed 32 m cubes).</summary>
		private const float InsideMargin = 2f;
		private Bounds[] boxes;
		private int phase;

		/// <summary>Set by the cut: the pieces and which of them are at a mouth.</summary>
		public void Configure(Renderer[] renderers, bool[] atMouth, float drawReach, Bounds[] voids = null)
		{
			pieces = renderers ?? System.Array.Empty<Renderer>();
			mouth = atMouth ?? System.Array.Empty<bool>();
			reach = drawReach;
			volumes = voids ?? System.Array.Empty<Bounds>();
		}

		private static readonly System.Collections.Generic.List<CaveVisibility> live = new System.Collections.Generic.List<CaveVisibility>();

		/// <summary>The caves enabled now.</summary>
		public static System.Collections.Generic.IReadOnlyList<CaveVisibility> Live => live;

		/// <summary>How many pieces the cave is drawn in.</summary>
		public int PieceCount => boxes != null ? boxes.Length : 0;

		/// <summary>A piece's box in the world, as it was when the cave was enabled.</summary>
		public Bounds BoxOf(int piece) => boxes[piece];

		/// <summary>Whether a piece is at a mouth: the opening seen from outside, and the outside seen from in.</summary>
		public bool AtMouth(int piece) => piece < mouth.Length && mouth[piece];

		/// <summary>Whether a piece is drawn now (a missing renderer is drawn by nothing).</summary>
		public bool Drawn(int piece) => pieces[piece] != null && pieces[piece].enabled;

		/// <summary>Whether a point is inside one of the pieces' boxes or the voids' boxes: in the cave, or in the rock round it.</summary>
		public bool Holds(Vector3 point)
		{
			for (int i = 0; i < boxes.Length; i++)
			{
				if (boxes[i].Contains(point))
				{
					return true;
				}
			}
			return InVoid(point);
		}

		/// <summary>Whether a point is in one of the cave's voids' boxes.</summary>
		private bool InVoid(Vector3 point)
		{
			for (int i = 0; i < volumes.Length; i++)
			{
				if (volumes[i].Contains(point))
				{
					return true;
				}
			}
			return false;
		}

		private void OnEnable()
		{
			boxes = new Bounds[pieces.Length];
			for (int i = 0; i < pieces.Length; i++)
			{
				boxes[i] = pieces[i] != null ? pieces[i].bounds : default;
			}
			phase = (GetInstanceID() & 0x7FFFFFFF) % Interval;
			Apply(true);
			live.Add(this);
		}

		private void OnDisable()
		{
			live.Remove(this);
		}

		private void LateUpdate()
		{
			if ((Time.frameCount + phase) % Interval == 0)
			{
				Apply(false);
			}
		}

		private void Apply(bool force)
		{
			Camera camera = Camera.main;
			if (camera == null)
			{
				return;
			}
			Vector3 eye = camera.transform.position;
			float reach2 = reach * reach;
			// Inside the cave (in any of its pieces' boxes) every piece is drawn: a passage or a cavern is seen far along,
			// and a piece switched off by distance left a hole onto the hillside's underside (2026-10-12).
			bool inside = InVoid(eye);
			for (int i = 0; i < boxes.Length && !inside; i++)
			{
				inside = boxes[i].SqrDistance(eye) <= InsideMargin * InsideMargin;
			}
			for (int i = 0; i < pieces.Length; i++)
			{
				Renderer piece = pieces[i];
				if (piece == null)
				{
					continue;
				}
				bool shown = inside || (i < mouth.Length && mouth[i]) || boxes[i].SqrDistance(eye) <= reach2;
				if (force || piece.enabled != shown)
				{
					piece.enabled = shown;
				}
			}
		}
	}
}
