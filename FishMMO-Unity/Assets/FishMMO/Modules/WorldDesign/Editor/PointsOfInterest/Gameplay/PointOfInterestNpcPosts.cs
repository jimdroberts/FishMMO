#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>What kind of place in a site's layout a service NPC stands at (<see cref="NpcPostRule"/>).</summary>
	public enum NpcPostKind : byte
	{
		/// <summary>Out front of a laid building of the named pieces: before its door, else before its front wall.</summary>
		Building = 0,
		/// <summary>Behind the counter of a market stall on the square.</summary>
		Stall = 1,
		/// <summary>Before the door of a shop-house let to one of the named trades (any shop-house when none is named).</summary>
		Shop = 2,
		/// <summary>On the market: round the middle of its stalls, its cross or fountain, or its well.</summary>
		Market = 3,
	}

	/// <summary>One kind of place a service NPC may stand at; a service lists them, most fitting first.</summary>
	public sealed class NpcPostRule
	{
		public NpcPostKind Kind;
		/// <summary>The structure kit pieces (<see cref="StructurePiece.Id"/>) a <see cref="NpcPostKind.Building"/> rule takes.</summary>
		public string[] Pieces = Array.Empty<string>();
		/// <summary>The trades a <see cref="NpcPostKind.Shop"/> rule takes; empty for any shop-house.</summary>
		public StructureTrade[] Trades = Array.Empty<StructureTrade>();

		public static NpcPostRule AtBuilding(params string[] pieces) => new NpcPostRule { Kind = NpcPostKind.Building, Pieces = pieces ?? Array.Empty<string>() };
		public static NpcPostRule AtStall() => new NpcPostRule { Kind = NpcPostKind.Stall };
		public static NpcPostRule AtShop(params StructureTrade[] trades) => new NpcPostRule { Kind = NpcPostKind.Shop, Trades = trades ?? Array.Empty<StructureTrade>() };
		public static NpcPostRule AtMarket() => new NpcPostRule { Kind = NpcPostKind.Market };

		public override string ToString() => Kind switch
		{
			NpcPostKind.Building => $"Building({string.Join(", ", Pieces)})",
			NpcPostKind.Shop => Trades.Length == 0 ? "Shop(any)" : $"Shop({string.Join(", ", Trades)})",
			_ => Kind.ToString(),
		};
	}

	/// <summary>Where one NPC stands: the spot on the ground, the way it faces, and what it stands at ("Smithy", "the market").</summary>
	public struct NpcPost
	{
		public Vector3 Position;
		public Quaternion Facing;
		public string Where;
	}

	/// <summary>
	/// The places a site's layout offers its service NPCs, and which are taken: a smith at the smithy, the stablemaster at the
	/// stables, a merchant behind a market stall.
	/// </summary>
	/// <remarks>
	/// <para><b>Read from what was laid.</b> Every building the site laid is in <see cref="PointOfInterestSiteContext.Laid"/>;
	/// a kit piece's doors and marks are its own (StructurePieces.DoorsOf, MarksOf), placed with the building's position,
	/// rotation and scale, exactly as PointOfInterestSiteContext.AddProp places the server's doors.</para>
	/// <para><b>Out front, not inside.</b> A keeper stands <see cref="DoorStandOff"/> out from the middle of the building's
	/// first door, facing the street (where the Stablemaster always stood). A building with no door of its own (the open-fronted
	/// smithy, the arcaded market hall) is fronted on −z, as every kit building is built (StructurePieces.BuildHouse: its front
	/// wall at −depth/2): the keeper stands <see cref="FrontStandOff"/> before the front of its meshes. Inside would put the NPC
	/// among the furniture PointOfInterestInteriors lays, behind a door that starts shut.</para>
	/// <para><b>Claims.</b> A stall or a shop takes one NPC, a building <see cref="PerBuilding"/> (side by side,
	/// <see cref="Spacing"/> apart: a capital's weaponsmith and armourer at one smithy when it has only one), the market any
	/// number (round its middle, <see cref="MarketRing"/> out). <see cref="TryClaim"/> walks a service's rules in order and,
	/// within each, takes a place nobody holds before sharing one.</para>
	/// <para><b>Not in the water.</b> A place whose spot is wet (context.IsWet) is passed over, except at a site under water.</para>
	/// </remarks>
	public sealed class PointOfInterestNpcPosts
	{
		/// <summary>How far out from the middle of a door a keeper stands, metres (the Stablemaster's).</summary>
		public const float DoorStandOff = 2.5f;
		/// <summary>How far before the front of a building with no door of its own a keeper stands, metres.</summary>
		public const float FrontStandOff = 1.5f;
		/// <summary>How far apart two keepers of one building stand, metres.</summary>
		public const float Spacing = 1.6f;
		/// <summary>How far from the market's middle its NPCs stand, metres.</summary>
		public const float MarketRing = 4f;
		/// <summary>Where behind a market stall's counter its keeper stands, in the stall's frame (its counter toward −z).</summary>
		public static readonly Vector3 StallSpot = new Vector3(0f, 0f, 0.15f);
		/// <summary>How many keepers one building takes.</summary>
		public const int PerBuilding = 2;

		/// <summary>The kit pieces a market is made of: its stalls, the cross or fountain on it, its well.</summary>
		public const string StallPiece = "MarketStall";
		private static readonly string[] MarketPieces = { StallPiece, "MarketCross", "Well" };

		private struct Placed
		{
			public PointOfInterestSiteContext.PlacedProp Laid;
			public StructurePiece Piece;
			public int Variant;
		}

		private readonly PointOfInterestSiteContext context;
		private readonly List<Placed> placed = new List<Placed>();
		private readonly int[] claims;
		private Dictionary<(int, int, int), StructureTrade> trades;
		private bool marketRead;
		private Vector3? market;
		private int marketClaims;

		public PointOfInterestNpcPosts(PointOfInterestSiteContext context)
		{
			this.context = context;
			if (context?.Laid != null)
			{
				foreach (PointOfInterestSiteContext.PlacedProp laid in context.Laid)
				{
					if (laid.Prefab != null && StructurePieces.TryParsePrefab(laid.Prefab.name, out StructurePiece piece, out int variant))
					{
						placed.Add(new Placed { Laid = laid, Piece = piece, Variant = variant });
					}
				}
			}
			claims = new int[placed.Count];
		}

		/// <summary>Whether the site laid any of the pieces.</summary>
		public bool Has(IReadOnlyList<string> pieces)
		{
			if (pieces == null)
			{
				return false;
			}
			foreach (Placed p in placed)
			{
				if (Contains(pieces, p.Piece.Id))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Takes the first free place the rules offer, in their order (within a rule, a place nobody holds before a share of one
		/// held), and claims it. False when none of them offers one.
		/// </summary>
		public bool TryClaim(IReadOnlyList<NpcPostRule> rules, out NpcPost post)
		{
			post = default;
			if (rules == null)
			{
				return false;
			}
			foreach (NpcPostRule rule in rules)
			{
				if (rule == null)
				{
					continue;
				}
				if (rule.Kind == NpcPostKind.Market)
				{
					if (TryMarket(out post))
					{
						return true;
					}
					continue;
				}
				int capacity = rule.Kind == NpcPostKind.Building ? PerBuilding : 1;
				for (int share = 0; share < capacity; share++)
				{
					for (int i = 0; i < placed.Count; i++)
					{
						if (claims[i] != share || !Matches(rule, placed[i]))
						{
							continue;
						}
						if (TryStand(rule, placed[i], share, out post))
						{
							claims[i]++;
							return true;
						}
					}
				}
			}
			return false;
		}

		private bool Matches(NpcPostRule rule, Placed p)
		{
			switch (rule.Kind)
			{
				case NpcPostKind.Building:
					return Contains(rule.Pieces, p.Piece.Id);
				case NpcPostKind.Stall:
					return p.Piece.Id == StallPiece;
				case NpcPostKind.Shop:
					if (!IsShop(p))
					{
						return false;
					}
					if (rule.Trades.Length == 0)
					{
						return true;
					}
					trades ??= PointOfInterestTrades.TradesOf(context);
					return trades.TryGetValue(PointOfInterestTrades.PlaceKey(p.Laid.Position), out StructureTrade trade) && Array.IndexOf(rule.Trades, trade) >= 0;
				default:
					return false;
			}
		}

		private static bool IsShop(Placed p)
		{
			foreach (StructureMark mark in StructurePieces.MarksOf(p.Piece, p.Variant))
			{
				if (mark.Name == PointOfInterestTrades.SignMark || mark.Name == PointOfInterestTrades.StallMark)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>The spot the <paramref name="share"/>th keeper of a laid piece stands on, if it is dry.</summary>
		private bool TryStand(NpcPostRule rule, Placed p, int share, out NpcPost post)
		{
			PointOfInterestSiteContext.PlacedProp laid = p.Laid;
			Vector3 scale = laid.Scale;
			float aside = Aside(share) * Mathf.Abs(scale.x);
			Vector3 at;
			Vector3 outward;
			if (rule.Kind == NpcPostKind.Stall)
			{
				at = laid.Position + laid.Rotation * Vector3.Scale(StallSpot, scale);
				outward = laid.Rotation * Vector3.back;
			}
			else if (TryFirstDoor(p, out Vector3 doorMiddle, out Vector3 across, out Vector3 inward))
			{
				at = doorMiddle - inward * DoorStandOff + across * aside;
				outward = -inward;
			}
			else
			{
				float front = FrontOf(laid.Prefab) * Mathf.Abs(scale.z);
				at = laid.Position + laid.Rotation * new Vector3(aside, 0f, front - FrontStandOff);
				outward = laid.Rotation * Vector3.back;
			}
			return Stand(at, outward, p.Piece.Id, out post);
		}

		/// <summary>The middle of a laid building's first door, and its across and inward ways (flat), in the world.</summary>
		private static bool TryFirstDoor(Placed p, out Vector3 middle, out Vector3 across, out Vector3 inward)
		{
			middle = across = inward = default;
			IReadOnlyList<StructureDoor> doors = StructurePieces.DoorsOf(p.Piece, p.Variant);
			if (doors == null || doors.Count == 0)
			{
				return false;
			}
			StructureDoor door = doors[0];
			PointOfInterestSiteContext.PlacedProp laid = p.Laid;
			Vector3 hinge = laid.Position + laid.Rotation * Vector3.Scale(door.Hinge, laid.Scale);
			across = Flat(laid.Rotation * door.Across, Vector3.right);
			inward = Flat(laid.Rotation * door.Inward, Vector3.forward);
			middle = hinge + across * (door.Width * Mathf.Abs(laid.Scale.x) * 0.5f);
			return true;
		}

		/// <summary>The next place round the market's middle, if the site has a market.</summary>
		private bool TryMarket(out NpcPost post)
		{
			post = default;
			if (!marketRead)
			{
				marketRead = true;
				market = MarketMiddle();
			}
			if (!market.HasValue)
			{
				return false;
			}
			// Round the middle by the golden angle, a little further out each lap, so none stands on another; the first dry one.
			for (int tries = 0; tries < 12; tries++)
			{
				int k = marketClaims++;
				float angle = k * 137.508f * Mathf.Deg2Rad;
				float radius = MarketRing + 1.5f * (k / 6);
				Vector3 outward = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
				if (Stand(market.Value + outward * radius, outward, "the market", out post))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>The middle of the site's market on the ground, if it has one (<see cref="MarketMiddle"/>).</summary>
		public bool TryMarketMiddle(out Vector3 middle)
		{
			if (!marketRead)
			{
				marketRead = true;
				market = MarketMiddle();
			}
			middle = market ?? default;
			if (market.HasValue && context?.GroundAt != null)
			{
				middle.y = context.GroundAt(middle.x, middle.z);
			}
			return market.HasValue;
		}

		/// <summary>The middle of the site's market: of its stalls, else of its cross or fountain, else of its wells.</summary>
		private Vector3? MarketMiddle()
		{
			foreach (string id in MarketPieces)
			{
				Vector3 sum = Vector3.zero;
				int count = 0;
				foreach (Placed p in placed)
				{
					if (p.Piece.Id == id)
					{
						sum += p.Laid.Position;
						count++;
					}
				}
				if (count > 0)
				{
					return sum / count;
				}
			}
			return null;
		}

		/// <summary>A spot on the ground facing out, unless it is in the water.</summary>
		private bool Stand(Vector3 at, Vector3 outward, string where, out NpcPost post)
		{
			post = default;
			bool underwater = context?.Record != null && PointOfInterestKinds.Info(context.Record.Kind).Has(PointOfInterestTraits.Underwater);
			if (context != null && !underwater && context.IsWet(at.x, at.z, 0.05f))
			{
				return false;
			}
			if (context != null && !context.InScene(at.x, at.z))
			{
				return false;
			}
			if (context?.GroundAt != null)
			{
				at.y = context.GroundAt(at.x, at.z);
			}
			outward = Flat(outward, Vector3.back);
			post = new NpcPost { Position = at, Facing = Quaternion.LookRotation(outward, Vector3.up), Where = where };
			return true;
		}

		/// <summary>How far aside the <paramref name="share"/>th keeper of one building stands: 0, then +, −, ++ ….</summary>
		public static float Aside(int share)
		{
			if (share <= 0)
			{
				return 0f;
			}
			int step = (share + 1) / 2;
			return (share % 2 == 1 ? 1f : -1f) * step * Spacing;
		}

		private static Vector3 Flat(Vector3 v, Vector3 fallback)
		{
			v.y = 0f;
			return v.sqrMagnitude > 1e-6f ? v.normalized : fallback;
		}

		private static bool Contains(IReadOnlyList<string> list, string id)
		{
			for (int i = 0; i < list.Count; i++)
			{
				if (string.Equals(list[i], id, StringComparison.Ordinal))
				{
					return true;
				}
			}
			return false;
		}

		// ── A building's front ──────────────────────────────────────────────────────────────

		private static readonly Dictionary<GameObject, float> fronts = new Dictionary<GameObject, float>();

		[InitializeOnLoadMethod]
		private static void ClearOnChange() => EditorApplication.projectChanged += fronts.Clear;

		/// <summary>
		/// The front of a prefab's meshes along its own z (the least z of their bounds, its overhang included), unscaled; −1 m
		/// when it has none. A prefab asset's renderers report no bounds outside a scene, so the meshes' own bounds are placed
		/// under the root (as GatheringFeature measures a footprint).
		/// </summary>
		public static float FrontOf(GameObject prefab)
		{
			if (prefab == null)
			{
				return -1f;
			}
			if (fronts.TryGetValue(prefab, out float cached))
			{
				return cached;
			}
			float front = float.PositiveInfinity;
			Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix;
			foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
			{
				Mesh mesh = filter.sharedMesh;
				if (mesh == null)
				{
					continue;
				}
				Matrix4x4 matrix = toRoot * filter.transform.localToWorldMatrix;
				Vector3 min = mesh.bounds.min, max = mesh.bounds.max;
				for (int corner = 0; corner < 8; ++corner)
				{
					Vector3 c = matrix.MultiplyPoint3x4(new Vector3((corner & 1) != 0 ? max.x : min.x, (corner & 2) != 0 ? max.y : min.y, (corner & 4) != 0 ? max.z : min.z));
					front = Mathf.Min(front, c.z);
				}
			}
			front = float.IsPositiveInfinity(front) ? -1f : front;
			fronts[prefab] = front;
			return front;
		}
	}
}
#endif
