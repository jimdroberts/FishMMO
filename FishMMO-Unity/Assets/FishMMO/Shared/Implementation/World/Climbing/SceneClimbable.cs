using System;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>What a climbable is, which sets how fast it is climbed and whether a climber can shuffle across it.</summary>
	/// <remarks>Append only; the ordinal is stored in scene assets.</remarks>
	public enum SceneClimbableKind : byte
	{
		/// <summary>A rigid ladder of rails and rungs: the quickest climb, and wide enough to shuffle across.</summary>
		Ladder = 0,
		/// <summary>A ladder of rope and wooden rungs: a little slower, as it gives under the feet.</summary>
		RopeLadder = 1,
		/// <summary>A knotted rope: climbed hand over hand, slowest of the three, held from whichever side it was reached.</summary>
		Rope = 2,
	}

	/// <summary>
	/// One ladder, rope ladder or rope a character can climb, baked by the cut (a cave's shafts and ledges, a mine's
	/// levels): the climbing movement mode, the server's NavMesh links and the NPCs' traversal of them all read these,
	/// nothing else. The pieces drawn for it are baked props, data like this, so nothing here refers to them.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The climb is the segment <see cref="Bottom"/> to <see cref="Top"/>,</b> its centre line. A climber's feet hang
	/// <see cref="Out"/> of it by the body's radius and a little more, and are kept on that line's height: it may lean
	/// (a ladder against a ledge, up to about 15 degrees) but never far from upright.
	/// </para>
	/// <para>
	/// Identified at run time by its index in <see cref="ScenePointsOfInterest.Climbables"/> within the character's own
	/// scene: the server and every client load the same asset, so the index is the same on both and is what the
	/// reconcile carries.
	/// </para>
	/// </remarks>
	[Serializable]
	public struct SceneClimbable
	{
		public SceneClimbableKind Kind;

		/// <summary>World metres: where the climb's foot stands on the lower floor, on its centre line.</summary>
		public Vector3 Bottom;

		/// <summary>World metres: the top of the climb, level with the upper floor a climber steps off onto.</summary>
		public Vector3 Top;

		/// <summary>
		/// Horizontal unit vector from the wall (or the ladder) toward where the climber hangs: the climber faces −Out.
		/// Zero for a free-hanging rope, which is held from the side the climber came at.
		/// </summary>
		public Vector3 Out;

		/// <summary>
		/// Horizontal unit vector: the way a climber steps off at the top onto the upper floor. For a ladder against a
		/// ledge or a shaft's wall it is −Out.
		/// </summary>
		public Vector3 TopExit;

		/// <summary>Half the width it can be held across, metres: a ladder about 0.3, a rope about 0.1.</summary>
		public float HalfWidth;
	}
}
