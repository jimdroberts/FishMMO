using System;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA action that explores part of the world map without the character having walked it.
	/// Client-only: suppressed on the server and during prediction reconciliation.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This is the hook for map consumables, exploration rewards and discovery triggers — anything
	/// that hands the player ground they have not covered on foot. Put it on an item's use event to
	/// make a map scroll, on a region's enter event to open up the valley the player has just
	/// looked out over, or on a quest completion to fill in the region it sent them through.
	/// </para>
	/// <para>
	/// <b>Exploration is client-side data.</b> It lives in the player's own explored-map files, not
	/// in the database, so this action is the whole delivery mechanism: it raises an event on the
	/// owning client and the map subsystem there applies it. Nothing is sent to the server and
	/// there is nothing for the server to validate — which also means it is only as trustworthy as
	/// the client, and is therefore fine for revealing a map and wrong for anything a player could
	/// gain by lying about.
	/// </para>
	/// <para>
	/// The area is taken from the character's own position, so the same asset works wherever it is
	/// used. Content that needs to name a fixed place instead should call
	/// <c>ClientMapSystem.ExploreArea</c> or <c>ExploreChunk</c> directly from a client script.
	/// </para>
	/// </remarks>
	[Serializable]
	public class ExploreMapAction : BaseAction
	{
		/// <summary>
		/// Raised on the owning client when map exploration should be granted. The payload is the
		/// world-space centre and the radius in metres. Subscribe in a MonoBehaviour to apply.
		/// </summary>
		public static event Action<Vector3, float> OnExploreMap;

		/// <summary>
		/// How far around the character to explore, in world metres.
		/// </summary>
		/// <remarks>
		/// Rounded outwards to whole chunks when it lands, because a chunk is the smallest piece of
		/// ground the map can describe. A radius smaller than one chunk therefore still explores
		/// the chunk the character is standing in, which is what "reveals your surroundings" should
		/// do in the tightest scene as much as the widest.
		/// </remarks>
		[Tooltip("How far around the character to explore, in world metres.")]
		public float Radius = 500.0f;

		/// <summary>
		/// Explores the whole scene instead of a radius.
		/// </summary>
		[Tooltip("Explore the entire scene, ignoring the radius. For a map that hands over a whole zone.")]
		public bool EntireScene;

		/// <inheritdoc />
		public override void Execute(ICharacter initiator, EventData eventData)
		{
#if !UNITY_SERVER
			if (initiator == null || initiator.Transform == null)
			{
				return;
			}

			/* The owning client only. Exploration is written to that player's own files, so running
			 * this for a peer would be writing someone else's map from this machine. */
			if (initiator.NetworkObject == null || !initiator.NetworkObject.IsOwner)
			{
				return;
			}

			/* Not during a reconcile replay. The same replicate is re-run many times over after a
			 * correction, and each pass would grant the reward again — harmless for exploration
			 * itself, since a chunk cannot be explored twice, but it would dirty the file and
			 * rewrite it on every rollback. */
			if (eventData != null &&
				eventData.TryGet(out RegionEventData regionData) &&
				regionData.IsReconciling)
			{
				return;
			}

			// A negative radius is authoring noise, not an instruction to un-explore anything.
			OnExploreMap?.Invoke(initiator.Transform.position, EntireScene ? float.PositiveInfinity : Mathf.Max(0.0f, Radius));
#endif
		}
	}
}
