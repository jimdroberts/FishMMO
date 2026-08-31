using System;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>The shape a <see cref="ShieldVolume"/> occupies.</summary>
	public enum ShieldShape : byte
	{
		/// <summary>No volume. The buff carrying it blocks nothing physically.</summary>
		None = 0,

		/// <summary>A sphere around <see cref="ShieldVolume.LocalCenter"/>.</summary>
		Sphere = 1,

		/// <summary>
		/// A box around <see cref="ShieldVolume.LocalCenter"/>, axis-aligned in the character's local
		/// space — so it turns with the character, which is what a shield does.
		/// </summary>
		Box = 2,

		/// <summary>A capsule around <see cref="ShieldVolume.LocalCenter"/>, standing along local up.</summary>
		Capsule = 3,
	}

	/// <summary>
	/// The dimensions of a character's shield: a real volume standing in front of them, authored on
	/// the buff that raises it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Defined in the character's LOCAL space, and that is the whole trick.</b> A shield described
	/// in world space would have to be moved, turned and — critically — REWOUND alongside the body it
	/// protects, because hits are resolved against where the attacker saw their target. Ability hits
	/// are dispatched after the rewind scope has closed
	/// (<c>AbilityObject.ResolveSweptHits</c>), so a world-space volume read at that moment would sit
	/// where the defender is NOW while the impact point it is being compared against came from where
	/// the defender WAS — metres apart at 200&#160;ms, and a shield that blocks phantom hits and
	/// misses real ones. A local-space volume compared against a local-space impact point captured
	/// inside the scope has no such disagreement to have: the body and its shield moved together, so
	/// the relationship between them is the same in both worlds.
	/// </para>
	/// <para>
	/// <b>It replaces an angle with a shape.</b> A facing cone is measured from the character's
	/// origin, so it says nothing about height or reach: a tower shield and a buckler protect
	/// identical arcs, and a shot at ankle height is "in front" of a shield held at the chest. Real
	/// dimensions make the two different objects, which is what a player expects to be true.
	/// </para>
	/// <para>
	/// <b>Both sides of the same shape.</b> <see cref="Contains"/> answers "did this hit meet the
	/// shield" for an incoming projectile, and the world-space accessors below describe the identical
	/// volume to <c>ShieldInterceptAction</c>'s overlap query. One authored shape, tested from
	/// whichever side is asking, so the thing that stops a projectile and the thing that sweeps one
	/// out of the air can never be different sizes.
	/// </para>
	/// </remarks>
	[Serializable]
	public class ShieldVolume
	{
		/// <summary>The shape this shield occupies. <see cref="ShieldShape.None"/> disables it.</summary>
		[Tooltip("Shape of the shield. None disables the physical volume and leaves only the facing arc.")]
		public ShieldShape Shape = ShieldShape.None;

		/// <summary>
		/// Where the volume sits relative to the character, in the character's own space.
		/// </summary>
		/// <remarks>
		/// +Z is forward, +Y is up. The default stands the shield at chest height, most of a metre in
		/// front — far enough out that a projectile meets it before the body, which is what makes the
		/// impact read as a block rather than as a hit that happened to do nothing.
		/// </remarks>
		[Tooltip("Offset from the character, in the character's own space. +Z forward, +Y up.")]
		public Vector3 LocalCenter = new Vector3(0f, 1.1f, 0.75f);

		/// <summary>Radius, for <see cref="ShieldShape.Sphere"/> and <see cref="ShieldShape.Capsule"/>.</summary>
		[Tooltip("Radius for Sphere and Capsule shapes.")]
		[Min(0f)]
		public float Radius = 0.6f;

		/// <summary>Full extents, for <see cref="ShieldShape.Box"/>.</summary>
		/// <remarks>
		/// Full size rather than half extents, because that is the number a designer measures off the
		/// shield model — the halving happens once, here, rather than in every author's head.
		/// </remarks>
		[Tooltip("Full width, height and depth for the Box shape.")]
		public Vector3 Size = new Vector3(1.2f, 1.4f, 0.2f);

		/// <summary>Total height, for <see cref="ShieldShape.Capsule"/>. Floored at a full diameter.</summary>
		[Tooltip("Total height for the Capsule shape. Values below a full diameter are treated as a sphere.")]
		[Min(0f)]
		public float Height = 1.6f;

		/// <summary>True when this volume can stop anything at all.</summary>
		public bool IsActive => Shape != ShieldShape.None && HasPositiveExtent;

		/// <summary>
		/// False for a shape authored with no size, which would otherwise be a shield that blocks
		/// exactly the points lying on a plane.
		/// </summary>
		private bool HasPositiveExtent
		{
			get
			{
				switch (Shape)
				{
					case ShieldShape.Sphere:
					case ShieldShape.Capsule:
						return Radius > 0f;
					case ShieldShape.Box:
						return Size.x > 0f && Size.y > 0f && Size.z > 0f;
					default:
						return false;
				}
			}
		}

		/// <summary>
		/// True when <paramref name="localPoint"/> — an impact point expressed in the character's own
		/// space — lies inside this shield.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Pure, and deliberately so: no transform is read, no physics is queried, and the answer
		/// depends on nothing but the two arguments. That is what lets the server and the attacker's
		/// own client reach the same verdict on the same hit without either of them having to agree
		/// about where anybody is standing.
		/// </para>
		/// <para>
		/// Uses SQUARED distances throughout. The comparison is against an authored radius, so the
		/// square root buys nothing but rounding.
		/// </para>
		/// </remarks>
		/// <param name="localPoint">Impact point in the character's local space.</param>
		public bool Contains(Vector3 localPoint)
		{
			if (!IsActive)
			{
				return false;
			}

			Vector3 offset = localPoint - LocalCenter;

			switch (Shape)
			{
				case ShieldShape.Sphere:
					return offset.sqrMagnitude <= Radius * Radius;

				case ShieldShape.Box:
					{
						Vector3 half = Size * 0.5f;
						return Mathf.Abs(offset.x) <= half.x &&
							Mathf.Abs(offset.y) <= half.y &&
							Mathf.Abs(offset.z) <= half.z;
					}

				case ShieldShape.Capsule:
					{
						/* Distance to the capsule's SEGMENT, not to its centre. The segment is the
						 * part of the local up axis the two end spheres are centred on; a height at
						 * or below a full diameter collapses it to a point, which is exactly a
						 * sphere and is the right answer for a capsule authored that short. */
						float halfSegment = Mathf.Max(0f, (Height * 0.5f) - Radius);
						float clampedY = Mathf.Clamp(offset.y, -halfSegment, halfSegment);
						Vector3 toAxis = new Vector3(offset.x, offset.y - clampedY, offset.z);
						return toAxis.sqrMagnitude <= Radius * Radius;
					}

				default:
					return false;
			}
		}

		/// <summary>
		/// Where this volume's centre sits in the world, for a character at
		/// <paramref name="characterTransform"/>.
		/// </summary>
		/// <remarks>
		/// For the outward-looking query only — <c>ShieldInterceptAction</c>, which sweeps the volume
		/// for objects in flight. The inward-looking test uses <see cref="Contains"/> and never needs
		/// a world position at all.
		/// </remarks>
		public Vector3 GetWorldCenter(Transform characterTransform)
		{
			return characterTransform == null
				? LocalCenter
				: characterTransform.TransformPoint(LocalCenter);
		}

		/// <summary>
		/// The radius of a sphere that fully contains this volume, in world units.
		/// </summary>
		/// <remarks>
		/// The broadphase bound <c>ShieldInterceptAction</c> queries with before testing candidates
		/// exactly through <see cref="Contains"/>. Querying a sphere and then narrowing means the
		/// authored shape is honoured by ONE implementation — the local-space one that the gate also
		/// uses — instead of the physics overlap approximating it a second way.
		/// </remarks>
		public float GetWorldBoundingRadius(Transform characterTransform)
		{
			float local;
			switch (Shape)
			{
				case ShieldShape.Sphere:
					local = Radius;
					break;
				case ShieldShape.Box:
					local = Size.magnitude * 0.5f;
					break;
				case ShieldShape.Capsule:
					local = Mathf.Max(Radius, Height * 0.5f);
					break;
				default:
					return 0f;
			}

			if (characterTransform == null)
			{
				return local;
			}

			// The largest axis scale, matching how Unity scales a sphere collider.
			Vector3 scale = characterTransform.lossyScale;
			float largest = Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
			return local * largest;
		}

		/// <summary>A short designer-facing description, for tooltips.</summary>
		public string Describe()
		{
			switch (Shape)
			{
				case ShieldShape.Sphere:
					return $"{Radius * 2f:0.#}m sphere";
				case ShieldShape.Box:
					return $"{Size.x:0.#} x {Size.y:0.#}m shield";
				case ShieldShape.Capsule:
					return $"{Radius * 2f:0.#} x {Height:0.#}m shield";
				default:
					return null;
			}
		}
	}
}
