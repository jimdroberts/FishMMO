using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Event data for a hit, carrying where in the world the impact happened.
	/// </summary>
	/// <remarks>
	/// This used to carry Unity's <c>Collision</c>. Nothing produces one any more: an ability object
	/// resolves its hits with an explicit swept query (<see cref="AbilityObjectSweep"/>) so they can
	/// be lag compensated, and an area effect never had a contact to report in the first place — so
	/// the field was null on every path that survived and <see cref="PlayFXAction"/> dereferenced it
	/// regardless. A point and a normal are all any consumer read out of it.
	/// </remarks>
	public class CollisionEventData : EventData
	{
		/// <summary>World point of impact. Only meaningful when <see cref="HasHitPoint"/> is true.</summary>
		public Vector3 HitPoint { get; }

		/// <summary>Surface normal at <see cref="HitPoint"/>. Only meaningful when <see cref="HasHitPoint"/> is true.</summary>
		public Vector3 HitNormal { get; }

		/// <summary>
		/// True when this event knows where the impact was.
		/// </summary>
		/// <remarks>
		/// False for a hit that has no single point — an area effect resolves a whole overlap at once
		/// — so a consumer can fall back rather than place an effect at the world origin.
		/// </remarks>
		public bool HasHitPoint { get; }

		/// <summary>Creates event data for a hit with no single impact point.</summary>
		/// <param name="initiator">The character initiating the event.</param>
		public CollisionEventData(ICharacter initiator)
			: base(initiator)
		{
		}

		/// <summary>Creates event data for a hit at a known point.</summary>
		/// <param name="initiator">The character initiating the event.</param>
		/// <param name="hitPoint">World point of impact.</param>
		/// <param name="hitNormal">Surface normal at the impact.</param>
		public CollisionEventData(ICharacter initiator, Vector3 hitPoint, Vector3 hitNormal)
			: base(initiator)
		{
			HitPoint = hitPoint;
			HitNormal = hitNormal;
			HasHitPoint = true;
		}
	}
}
