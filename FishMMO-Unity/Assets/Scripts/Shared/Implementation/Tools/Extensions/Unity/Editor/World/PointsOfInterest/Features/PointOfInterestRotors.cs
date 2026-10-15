#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The turning parts of what a site laid (2026-10-14: mills): after the template's features, every laid piece with a
	/// rotor (<see cref="StructurePieces.RotorsOf"/>: a post mill's sails, a mill's water wheel) gets that part set at its
	/// hub, client-only, spun by <see cref="MillRotor"/>.
	/// </summary>
	/// <remarks>
	/// The pieces themselves stay static instanced props; a turning part cannot be one. Each is three objects: an axle frame
	/// at the hub with its z along the axis (under a <see cref="ClientOnlyObject"/>), the rotor that turns about that z, and
	/// the part's prefab instance under it set back by its own hub. No colliders: a turning blade is not something to stand
	/// on, and the server never sees it.
	/// </remarks>
	[InitializeOnLoad]
	public sealed class PointOfInterestRotors : IPointOfInterestSiteBuilder
	{
		static PointOfInterestRotors() => PointOfInterestSiteBuilders.Register(new PointOfInterestRotors());

		public bool Handles(PointOfInterestRecord record) => record != null;

		public void Build(PointOfInterestSiteContext context)
		{
			if (context.Laid == null)
			{
				return;
			}
			int count = 0;
			for (int i = 0; i < context.Laid.Count; i++)
			{
				PointOfInterestSiteContext.PlacedProp laid = context.Laid[i];
				if (laid.Prefab == null || !StructurePieces.TryParsePrefab(laid.Prefab.name, out StructurePiece piece, out int variant))
				{
					continue;
				}
				foreach (StructureRotor rotor in StructurePieces.RotorsOf(piece, variant))
				{
					GameObject prefab = PartPrefab(rotor, laid.Prefab.name);
					if (prefab == null)
					{
						context.Notes?.Add($"{context.Record.Name}: {piece.Id}'s {rotor.Piece} art is missing (run Generate Biome Art).");
						continue;
					}
					Vector3 hub = laid.Position + laid.Rotation * Vector3.Scale(rotor.Hub, laid.Scale);
					Vector3 axis = (laid.Rotation * rotor.Axis).normalized;
					Vector3 up = Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > 0.95f ? laid.Rotation * Vector3.forward : Vector3.up;
					var frame = new GameObject($"{piece.Id} {rotor.Piece}");
					frame.transform.SetParent(context.Root, false);
					frame.transform.SetPositionAndRotation(hub, Quaternion.LookRotation(axis, up));
					frame.transform.localScale = laid.Scale;
					frame.AddComponent<ClientOnlyObject>();
					var spin = new GameObject("Rotor");
					spin.transform.SetParent(frame.transform, false);
					MillRotor turning = spin.AddComponent<MillRotor>();
					turning.Rpm = rotor.Rpm;
					// Seeded by where it stands: the same every cut, different from the mill next door.
					turning.Phase = Mathf.Repeat(PointOfInterestPlanner.Hash(context.Record.Id, Mathf.RoundToInt(hub.x * 10f), Mathf.RoundToInt(hub.z * 10f), count) % 3600 * 0.1f, 360f);
					GameObject part = Object.Instantiate(prefab, spin.transform, false);
					part.name = prefab.name;
					part.transform.localPosition = -rotor.PieceHub;
					part.transform.localRotation = Quaternion.identity;
					foreach (Collider collider in part.GetComponentsInChildren<Collider>(true))
					{
						Object.DestroyImmediate(collider);
					}
					foreach (Transform t in part.GetComponentsInChildren<Transform>(true))
					{
						GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
					}
					spin.transform.localRotation = Quaternion.AngleAxis(turning.Phase, Vector3.forward);
					count++;
				}
			}
		}

		/// <summary>The turning part's prefab in its owner's finish (a mossy mill's mossy sails), else plain.</summary>
		private static GameObject PartPrefab(StructureRotor rotor, string ownerPrefab)
		{
			foreach (StructureFinish finish in StructurePieces.Finishes)
			{
				if (finish != StructureFinish.None && ownerPrefab.EndsWith("_" + finish, System.StringComparison.Ordinal))
				{
					GameObject finished = AssetDatabase.LoadAssetAtPath<GameObject>(ProceduralArtCatalogue.PrefabPath(StructurePieces.PrefabName(rotor.Piece, rotor.Variant, finish)));
					if (finished != null)
					{
						return finished;
					}
				}
			}
			return AssetDatabase.LoadAssetAtPath<GameObject>(ProceduralArtCatalogue.PrefabPath(StructurePieces.PrefabName(rotor.Piece, rotor.Variant)));
		}
	}
}
#endif
