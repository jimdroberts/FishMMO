using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Draws a carved cave's rock only where it can be seen: all of it while the camera is in the cave; from outside, the
	/// pieces at its mouths always (they are the opening seen from outside) and the rest only within <see cref="reach"/>
	/// of the camera (at a mouth looking in). Everything deeper is behind the hill.
	/// </summary>
	/// <remarks>
	/// The pieces are the 32 m cubes the cut meshed the cave in (CaveMesher); their colliders are elsewhere, on the
	/// server-kept cave root, and are never touched. On the cave's client-only visual, so a server never runs it. Checked a
	/// few times a second, staggered between caves, and only the renderers whose state changes are written.
	/// </remarks>
	[DisallowMultipleComponent]
	public sealed class CaveVisibility : MonoBehaviour
	{
		[SerializeField] private Renderer[] pieces = System.Array.Empty<Renderer>();
		[SerializeField] private bool[] mouth = System.Array.Empty<bool>();
		[Tooltip("How near a piece's box the camera must be for it to be drawn, metres.")]
		[SerializeField] private float reach = 72f;

		private const int Interval = 6;
		/// <summary>How near a piece's box the camera counts as in the cave, metres (the boxes are the meshed 32 m cubes).</summary>
		private const float InsideMargin = 2f;
		private Bounds[] boxes;
		private int phase;

		/// <summary>Set by the cut: the pieces and which of them are at a mouth.</summary>
		public void Configure(Renderer[] renderers, bool[] atMouth, float drawReach)
		{
			pieces = renderers ?? System.Array.Empty<Renderer>();
			mouth = atMouth ?? System.Array.Empty<bool>();
			reach = drawReach;
		}

		/// <summary>The rock shader's per-draw ground-contact switch (FishGroundColour.hlsl <c>_FishContactOn</c>).</summary>
		private static readonly int ContactOnId = Shader.PropertyToID("_FishContactOn");
		/// <summary>The cave mode: the ground drawn only where the rock comes up through the hillside.</summary>
		private const float CaveContact = 2f;
		private static MaterialPropertyBlock contactBlock;

		private void OnEnable()
		{
			boxes = new Bounds[pieces.Length];
			// Where the cave's rock comes up through the hillside (the collar round its mouth, the lip over it) it is drawn as
			// the ground there, as a scattered rock's foot is, so the mouth reads as earth and turf round an opening rather
			// than a stone plate on the hill (2026-10-12). Its walls and roof under the hill keep their rock.
			contactBlock ??= new MaterialPropertyBlock();
			contactBlock.Clear();
			contactBlock.SetFloat(ContactOnId, CaveContact);
			for (int i = 0; i < pieces.Length; i++)
			{
				boxes[i] = pieces[i] != null ? pieces[i].bounds : default;
				if (pieces[i] != null)
				{
					pieces[i].SetPropertyBlock(contactBlock);
				}
			}
			phase = (GetInstanceID() & 0x7FFFFFFF) % Interval;
			Apply(true);
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
			bool inside = false;
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
