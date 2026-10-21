#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Furnishes the rooms of a site's buildings (2026-10-11: every building with a door gets an interior): each room
	/// the structure kit records (<see cref="StructureDraft.Room"/>) is laid out by its kind (<see cref="StructureInteriors"/>)
	/// and its furniture added as props — instanced, with collision, and culled with the rest when the walls hide it.
	/// </summary>
	/// <remarks>
	/// Built to the building's scale (its race's stature): a giant's bed is a giant's. Seeded by the site and the room, so a
	/// re-cut furnishes the same.
	/// </remarks>
	[InitializeOnLoad]
	public sealed class PointOfInterestInteriors : IPointOfInterestSiteBuilder
	{
		static PointOfInterestInteriors() => PointOfInterestSiteBuilders.Register(new PointOfInterestInteriors());

		public bool Handles(PointOfInterestRecord record) => true;

		public void Build(PointOfInterestSiteContext context)
		{
			// The shop-houses' trades first: their signs and stall-boards (2026-10-19, here rather than in a site builder of their
			// own, which would move the draws of every builder registered after it).
			PointOfInterestTrades.Lay(context);
			if (context.Rooms.Count == 0)
			{
				return;
			}
			int furnished = 0, items = 0;
			// The trades the site's shop-houses are let to (PointOfInterestTrades, worked out again here: the builders' order is undefined).
			Dictionary<(int, int, int), StructureTrade> trades = null;
			for (int i = 0; i < context.Rooms.Count; i++)
			{
				PointOfInterestSiteContext.PlacedRoom placed = context.Rooms[i];
				AddRoomBox(context, placed);
				int seed = (int)PointOfInterestPlanner.Hash(context.Record != null ? context.Record.Id : 0, i, (int)placed.Room.Kind, 0x1A7E);
				// A shop let to a trade is furnished as that trade's (2026-10-19), its plain shelves its trade's stocked ones.
				StructureRoom room = placed.Room;
				StructureTrade? trade = null;
				if (room.Kind == StructureRoomKind.Shop)
				{
					trades ??= PointOfInterestTrades.TradesOf(context);
					if (trades.TryGetValue(PointOfInterestTrades.PlaceKey(placed.Position), out StructureTrade let))
					{
						trade = let;
						room.Kind = StructurePieces.TradeRoom(let);
					}
				}
				List<InteriorItem> plan = StructureInteriors.Plan(room, placed.Doors ?? System.Array.Empty<StructureDoor>(), seed, trade.HasValue ? TradeFootprint : Footprint);
				foreach (InteriorItem planned in plan)
				{
					InteriorItem item = planned;
					if (trade.HasValue && item.Piece == "Shelf")
					{
						item.Piece = "TradeShelf";
						item.Variant = (int)trade.Value;
					}
					StructurePiece piece = StructurePieces.Find(item.Piece);
					if (piece == null)
					{
						continue;
					}
					GameObject prefab = StructureKit.Prefab(item.Piece, Mathf.Clamp(item.Variant, 0, piece.Variants - 1));
					if (prefab == null)
					{
						continue;
					}
					Vector3 world = placed.Position + placed.Rotation * (item.Position * placed.Scale);
					Quaternion rotation = placed.Rotation * Quaternion.Euler(0f, item.Yaw, 0f);
					context.AddProp(prefab, world, rotation, Vector3.one * placed.Scale);
					items++;
				}
				if (plan.Count > 0)
				{
					furnished++;
					AddRoomLight(context, placed, seed);
				}
			}
			if (items == 0 && context.Rooms.Count > 0)
			{
				PointOfInterestFeatureKit.Note(context, "rooms left bare: the furniture pieces are not generated yet (Biome Tools → Generate biome art, then regenerate).");
			}
		}

		/// <summary>
		/// The room's light (SceneEmitterKind.Indoor): at its middle, a little over head height, reaching its walls and no
		/// further, so the house is lit from within and not through its walls. Not in a tomb or a stable.
		/// </summary>
		private static void AddRoomLight(PointOfInterestSiteContext context, PointOfInterestSiteContext.PlacedRoom placed, int seed)
		{
			StructureRoomKind kind = placed.Room.Kind;
			if (context.Points == null || kind == StructureRoomKind.Tomb || kind == StructureRoomKind.Stable)
			{
				return;
			}
			StructureRoom room = placed.Room;
			float height = Mathf.Min(1.9f, room.Height * 0.7f);
			Vector3 local = room.Centre + Vector3.up * height;
			Vector3 world = placed.Position + placed.Rotation * (local * placed.Scale);
			float halfDiagonal = 0.5f * new Vector2(room.Size.x, room.Size.y).magnitude;
			context.Points.Emitters ??= new List<SceneEmitter>();
			context.Points.Emitters.Add(new SceneEmitter
			{
				Position = world,
				Kind = SceneEmitterKind.Indoor,
				Seed = (uint)seed,
				Reach = Mathf.Clamp(halfDiagonal, 2.5f, 8f) * placed.Scale,
			});
		}

		/// <summary>
		/// The room as a box in the scene's POI asset (<see cref="SceneRoom"/>): inside the walls and under the ceiling, a
		/// hand's breadth in from each, so the walls' outer faces stay outside it and keep the sky's light.
		/// </summary>
		private static void AddRoomBox(PointOfInterestSiteContext context, PointOfInterestSiteContext.PlacedRoom placed)
		{
			if (context.Points == null)
			{
				return;
			}
			StructureRoom room = placed.Room;
			const float inset = 0.1f;
			Vector3 local = room.Centre + Vector3.up * (0.5f * room.Height);
			context.Points.Rooms ??= new List<SceneRoom>();
			context.Points.Rooms.Add(new SceneRoom
			{
				Centre = placed.Position + placed.Rotation * (local * placed.Scale),
				Yaw = placed.Rotation.eulerAngles.y,
				HalfSize = new Vector3(Mathf.Max(0.2f, 0.5f * room.Size.x - inset), Mathf.Max(0.2f, 0.5f * room.Height - inset), Mathf.Max(0.2f, 0.5f * room.Size.y - inset)) * placed.Scale,
			});
		}

		/// <summary>A piece's (x, z) footprint from the kit's table (no art needed), zero for none.</summary>
		private static Vector2 Footprint(string pieceId)
		{
			StructurePiece piece = StructurePieces.Find(pieceId);
			return piece != null ? piece.Footprint : Vector2.zero;
		}

		/// <summary>
		/// A trade's shop's footprints: its plain shelves are laid as its stocked ones (TradeShelf), so they are planned at
		/// that piece's size, the wares that stand proud of the shelf's front included.
		/// </summary>
		private static Vector2 TradeFootprint(string pieceId)
		{
			if (pieceId == "Shelf")
			{
				Vector2 stocked = Footprint("TradeShelf");
				if (stocked != Vector2.zero)
				{
					return stocked;
				}
			}
			return Footprint(pieceId);
		}
	}
}
#endif
