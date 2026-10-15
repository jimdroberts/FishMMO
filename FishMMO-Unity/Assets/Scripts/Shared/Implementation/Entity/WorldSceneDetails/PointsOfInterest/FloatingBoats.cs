using System;
using System.Collections.Generic;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;
using FishNet;
using FishNet.Managing;
using FishNet.Managing.Predicting;
using FishNet.Managing.Timing;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared
{
	/// <summary>
	/// Boats on the water: moored ones riding the tide at their harbour (and settling on the mud, heeled over, when it goes
	/// out from under them), and ships under way along the scene's sea lanes, solid, with a deck a player can stand on and be
	/// carried by (2026-10-12: "We need docks…, accurate ships and boats"; "We should have ships floating out in the
	/// ocean too?"; "Solid sailing ships needs to be done.").
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why not props.</b> The POI props are static and instanced; the sea is not (<see cref="SeaTide"/> moves it up to a
	/// few metres), so a hull fixed at mean sea level would stand clear of the water at low tide and be swamped at high.
	/// </para>
	/// <para>
	/// <b>A moored boat collides</b> (2026-10-12: "Add collision to floating boats so we don't swim through them"). Its
	/// root, which the server keeps, carries its hull's collider and moves only with the tide: its level is worked out from
	/// <see cref="WaterQuery"/> at the synced tick, on the server and every client alike and to the millimetre, so a
	/// prediction meets the hull where the server does (and the tide moves it a tenth of a millimetre a second). Its bob and
	/// roll on the swell are its client-only visual child's alone.
	/// </para>
	/// <para>
	/// <b>A ship under way collides too, and its pose is a pure function of the synced tick.</b> Each ship's root (kept by
	/// the server, its hull's collider on it, no NetworkObject) is placed on its lane by <see cref="CourseAt"/> from
	/// <c>tick × TickDelta</c> and on the water by <see cref="WaterQuery"/> at the same tick. Nothing about it is ever sent:
	/// the server and every client already agree on the tick, so they agree on the hull. That is the whole reason not to
	/// make it a <see cref="KCCPlatform"/>: a platform is STEPPED (its next pose depends on its last), so FishNet has to
	/// reconcile it to every observer every tick and roll it back under a replaying rider, which costs a forwarded
	/// NetworkObject, a reconcile per observer per tick, and an observer range — and a ship is seen from kilometres off,
	/// sails for an hour, and twelve of them share a scene. A pose that is a function of the tick needs none of it: to
	/// "roll the hull back" for a replayed tick is simply to evaluate the function at that tick.
	/// </para>
	/// <para>
	/// <b>When it is posed.</b> On every peer, before anything simulates a tick: in <c>TimeManager.OnPreTick</c> for the live
	/// tick, in <c>PredictionManager.OnPreReplicateReplay</c> for each tick a client replays (so a replayed ground probe meets
	/// the deck where it stood on that tick, as a forwarded platform's would), and again in <c>OnPostReconcile</c> to stand it
	/// back at the live tick before <c>OnTick</c> runs; then <c>Physics.SyncTransforms</c>, because the project runs with
	/// automatic transform sync off and a moved collider is otherwise invisible to queries until the next simulation. The
	/// tick it is posed at is the SYNCED tick the server runs that input on — the server's own <c>Tick</c>, a client's
	/// <c>LocalTick</c> mapped through the last reconcile's pairing (<see cref="WeatherExposureTick"/>) — which is the same
	/// tick a rider's replicate reads (<c>KCCController.WaterTick</c>), live and in replay.
	/// </para>
	/// <para>
	/// <b>Riders</b> are carried through the same seam as a platform's (<c>KinematicCharacterMotor.SetPlatformVelocity</c>,
	/// the attached-rigidbody carry): <see cref="TryGetCarry"/> gives the velocity that keeps a deck point under the rider's
	/// feet from the last tick's pose to this one's — a rigid motion, so the 180° swing at anchor carries a rider round with
	/// the deck — and the yaw the hull turned through between the two, which the motor puts on the rider's facing as KCC
	/// does an attached PhysicsMover's rotation; both only to a stably grounded character. Who is aboard is decided
	/// spatially, again from the tick (feet over the deck in the last tick's frame, confirmed by a ray down onto this hull's
	/// own collider), never from a trigger volume or the motor's ground collider, neither of which a reconcile restores.
	/// </para>
	/// <para>
	/// <b>A rider's camera turns with the ship</b> (the owner's only, a presentation of the drawn hull: see
	/// <see cref="TryGetDrawnYaw"/>), as KCC's camera turns with a PhysicsMover. It has to: a character facing the camera
	/// (the shipped orientation) takes its facing from the camera's rotation in its input every tick, so a camera fixed in
	/// the world would hold the rider's facing fixed in the world whatever the motor did. Being input, the turned camera
	/// reaches the server and every replay as it is, so nothing about it can diverge.
	/// </para>
	/// <para>
	/// <b>Drawn</b> on the client from the same function at a fractional tick, <see cref="VisualInterpolationTicks"/> behind
	/// the simulated one: the lag the owner's own character is drawn at (its NetworkObject smoother), so a rider and the deck
	/// under them are drawn at the same moment and do not slide against each other. Bob, roll and the wind's heel are the
	/// drawn hull's alone; the collider never bobs.
	/// </para>
	/// <para>
	/// A ship beyond the scene's edge (waiting at a lane end that is open sea) is hidden and its collider and obstacle are
	/// switched off: nothing can run into a ship nobody can see.
	/// </para>
	/// <para>
	/// <b>Airships</b> (a cloud world's, 2026-10-14: "flying ships") are the same ships on lanes through the air
	/// (<see cref="Airborne"/>): a lane point's y is the ship's own height, climbing and descending between landing
	/// stages, and the root's frame has the deck at y 0 instead of the waterline. At a port end such a lane holds the ship
	/// still alongside its stage and swings it only at the end of its wait (<see cref="Lane.BerthSwing"/>). Everything
	/// else (the pose from the tick, the carry, the solid hull) is the ships' own.
	/// </para>
	/// <para>
	/// Built by the cut: a harbour's (PointOfInterestHarbour), the sea lanes' (PointOfInterestSeaTraffic) and the sky's
	/// (PointOfInterestSkyTraffic), each on an object the server keeps, each boat's drawn hull under a ClientOnlyObject.
	/// </para>
	/// </remarks>
	[DisallowMultipleComponent]
	public sealed class FloatingBoats : MonoBehaviour
	{
		/// <summary>A boat at its moorings.</summary>
		[Serializable]
		public struct Moored
		{
			/// <summary>The boat's root (its collider; the server keeps it), moved with the tide.</summary>
			public Transform Boat;
			/// <summary>Its drawn hull under the root (client-only: null on a server), bobbed and rolled on the swell.</summary>
			public Transform Visual;
			/// <summary>Where it lies (y the water level it was cut against) and its heading, degrees.</summary>
			public Vector3 Position;
			public float Yaw;
			/// <summary>The lowest its waterline sits: on the bottom, when the tide is out (negative infinity on water with no tide).</summary>
			public float Floor;
			/// <summary>The list it takes when aground, degrees (signed: the side it falls to).</summary>
			public float Heel;
			/// <summary>Its length, metres: a small boat bobs quicker and rolls further.</summary>
			public float Length;
			public float Phase;
		}

		/// <summary>A way over deep water, through its points (y the sea level), its two ends a port or the scene's edge.</summary>
		[Serializable]
		public sealed class Lane
		{
			public Vector3[] Points = Array.Empty<Vector3>();
			/// <summary>The distance along the lane to each point, metres.</summary>
			public float[] Along = Array.Empty<float>();
			public bool PortAtStart, PortAtEnd;
			/// <summary>
			/// How long a ship takes over its swing at a port end, seconds, at the END of its wait there; it lies still at its
			/// berth until then. Zero (a sea lane's): it swings to its cable through the whole wait, as a ship at anchor does.
			/// An airship at a landing stage holds still alongside it so its passengers can step aboard, and turns only once it
			/// has cast off (<see cref="SwingShare"/>).
			/// </summary>
			public float BerthSwing;
			public float Length => Along.Length > 0 ? Along[Along.Length - 1] : 0f;
		}

		/// <summary>A ship plying a lane.</summary>
		[Serializable]
		public struct Sailing
		{
			/// <summary>
			/// The ship's own object, which the server keeps: its hull's collider (and an obstacle for swimming NPCs' agents),
			/// posed from the synced tick and never bobbed. Null in a scene cut before ships were solid: then it is drawn only.
			/// </summary>
			public Transform Root;
			/// <summary>
			/// The ship under sail, and (optional) the same ship with its sail furled, shown while it lies in port: drawn hulls,
			/// client-only (null on a server), under <see cref="Root"/>.
			/// </summary>
			public Transform Ship;
			public Transform Furled;
			/// <summary>
			/// The hull collider's bounds in the root's space (bow +x; y 0 a ship's waterline, an airship's deck): where a rider
			/// can stand.
			/// </summary>
			public Bounds Deck;
			public int Lane;
			/// <summary>Its speed through the water, metres a second.</summary>
			public float Speed;
			/// <summary>Where in its round it is at second 0, seconds.</summary>
			public float Offset;
			/// <summary>How long it waits at the lane's start and its end, seconds.</summary>
			public float DwellStart, DwellEnd;
			public float Length;
			public float Phase;
		}

		/// <summary>
		/// Where a ship's hull stands at one moment: on its lane (y the water's level), its heading, and whether it is in the
		/// scene at all. Yaw only: the collider neither heels nor bobs. Its frame is the root's (waterline y 0, bow +x).
		/// </summary>
		public struct ShipPose
		{
			public Vector3 Position;
			/// <summary>Degrees about +y, as <c>Quaternion.Euler(0, Yaw, 0)</c>.</summary>
			public float Yaw;
			/// <summary>In the scene: false while it waits beyond the scene's edge, when it neither shows nor collides.</summary>
			public bool Shown;
			/// <summary>Sailing (sail set) rather than lying at anchor.</summary>
			public bool UnderWay;

			/// <summary>A world point in the hull's frame.</summary>
			public Vector3 ToLocal(Vector3 world)
			{
				Vector3 d = world - Position;
				float r = Yaw * Mathf.Deg2Rad;
				float c = Mathf.Cos(r), s = Mathf.Sin(r);
				return new Vector3(d.x * c - d.z * s, d.y, d.x * s + d.z * c);
			}

			/// <summary>A point in the hull's frame, in the world (Unity's yaw: +x turns towards −z).</summary>
			public Vector3 ToWorld(Vector3 local)
			{
				float r = Yaw * Mathf.Deg2Rad;
				float c = Mathf.Cos(r), s = Mathf.Sin(r);
				return Position + new Vector3(local.x * c + local.z * s, local.y, -local.x * s + local.z * c);
			}
		}

		[SerializeField] private Moored[] moored = Array.Empty<Moored>();
		[SerializeField] private Lane[] lanes = Array.Empty<Lane>();
		[SerializeField] private Sailing[] sailing = Array.Empty<Sailing>();
		/// <summary>
		/// Airships (a cloud world's sky traffic, PointOfInterestSkyTraffic): no water under them, so their lanes' heights are
		/// their own (a lake on an island they pass over is never their level), they sway on the air rather than bob on a
		/// swell, wear no shore mark, and a rider is aboard from the deck up (there is no waterline to be under).
		/// </summary>
		[SerializeField] private bool airborne;

		/// <summary>The share of a leg a ship spends gathering way out of port, or losing it coming in.</summary>
		private const float PortEase = 0.12f;
		/// <summary>How far ahead and behind the heading is taken along the lane, metres: a ship turns through a bend, it does not pivot at a point.</summary>
		private const float HeadingReach = 9f;
		/// <summary>Moored boats further than this from the camera are left as they were, metres.</summary>
		private const float MooredReach = 1500f;
		/// <summary>The wind's list on a ship under sail, degrees.</summary>
		private const float SailingHeel = 3.5f;

		/// <summary>
		/// How many ticks behind the simulated hull the drawn one is: the owner's own character's interpolation
		/// (<c>_ownerInterpolation</c> on the playable prefabs, and a platform deck's smoother), so a rider and the deck under
		/// them are drawn at the same moment. Pinned to <c>PlatformRiderSmoothingTests.InterpolationTicks</c>; never give the
		/// deck a different lag from the rider (see the rider-smoothing notes on issue #228).
		/// </summary>
		public const int VisualInterpolationTicks = 2;

		/// <summary>The lowest a rider's feet stand above the waterline in the hull's frame, metres: a swimmer alongside is never aboard.</summary>
		public const float RiderAboveWaterline = 0.2f;
		/// <summary>How far past the hull's bounds a rider's feet may stand and still be aboard, metres (the gunwale's width).</summary>
		public const float DeckMargin = 0.3f;
		/// <summary>How far over the hull's highest point a rider's feet may be and still be aboard, metres.</summary>
		public const float DeckHeadroom = 0.5f;

		/// <summary>
		/// How far past its own length a ship under way keeps its collider placed for a character, metres (<see cref="PoseShips"/>):
		/// on the server any character in the scene, on a client the character it predicts and its camera. Beyond that
		/// nothing can touch the hull before the next tick poses it again, so its collider is switched off and its root
		/// left where it was, rather than moved and re-synced every tick for nobody. Ample against a tick's travel (under a
		/// metre) and against a client's camera, its rain shelter map and its replays.
		/// </summary>
		public const float SolidReach = 64f;

		/// <summary>How long a rider's position counts as near, seconds: it asks for a carry every tick it simulates.</summary>
		private const float RiderMemorySeconds = 1f;

		private static readonly ProfilerMarker PoseAllMarker = new ProfilerMarker("FloatingBoats.PoseAll");
		private static readonly ProfilerMarker CarryMarker = new ProfilerMarker("FloatingBoats.TryGetCarry");

		/// <summary>
		/// One ship under way, as the carry found a rider aboard it: what a rider's camera follows. Default (no fleet) when
		/// aboard none.
		/// </summary>
		public readonly struct ShipRef : IEquatable<ShipRef>
		{
			public readonly FloatingBoats Fleet;
			public readonly int Index;

			public ShipRef(FloatingBoats fleet, int index)
			{
				Fleet = fleet;
				Index = index;
			}

			public bool IsValid => Fleet != null && Index >= 0 && Index < Fleet.sailing.Length;

			public bool Equals(ShipRef other) => ReferenceEquals(Fleet, other.Fleet) && Index == other.Index;
			public override bool Equals(object obj) => obj is ShipRef other && Equals(other);
			public override int GetHashCode() => (ReferenceEquals(Fleet, null) ? 0 : Fleet.GetInstanceID()) * 397 ^ Index;
		}

		/// <summary>Per ship, what the tick driver last did with it. Runtime only.</summary>
		private struct ShipState
		{
			public bool Posed;
			public uint Tick;
			public ShipPose Now, Before;
			public Collider[] Colliders;
			public Behaviour Obstacle;
			public bool Solid, SolidApplied;
		}

		[NonSerialized] private ShipState[] states;
		/// <summary>Each lane's points' extent across the ground (x and z), for <see cref="TryCarryAt"/>'s first rejection. Runtime only.</summary>
		[NonSerialized] private Rect[] laneAreas;
		[NonSerialized] private Lane[] laneAreasOf;

		/// <summary>Set by the cut. <paramref name="inAir"/> for airships (<see cref="Airborne"/>).</summary>
		public void Configure(Moored[] boats, Lane[] ways, Sailing[] ships, bool inAir = false)
		{
			moored = boats ?? Array.Empty<Moored>();
			lanes = ways ?? Array.Empty<Lane>();
			sailing = ships ?? Array.Empty<Sailing>();
			airborne = inAir;
		}

		/// <summary>True for airships: see the field's notes.</summary>
		public bool Airborne => airborne;

		public int MooredCount => moored.Length;
		public int SailingCount => sailing.Length;

		/// <summary>
		/// True when <paramref name="t"/> is (under) a ship under way: it moves, so the NavMesh bake must never take its collider
		/// as ground or wall where the cut happened to leave it.
		/// </summary>
		public bool IsUnderWay(Transform t)
		{
			if (t == null)
			{
				return false;
			}
			foreach (Sailing ship in sailing)
			{
				if (ship.Root != null && t.IsChildOf(ship.Root))
				{
					return true;
				}
			}
			return false;
		}

		private void OnEnable()
		{
			MarkHulls();
			if (sailing.Length > 0)
			{
				Fleet.Add(this);
			}
		}

		private void OnDisable()
		{
			Fleet.Remove(this);
		}

		private void LateUpdate()
		{
			Camera camera = Camera.main;
			Vector3 eye = camera != null ? camera.transform.position : Vector3.zero;
			double now = WorldMotion.Seconds;
			uint tick = WeatherQuery.CurrentTick;
			for (int i = 0; i < moored.Length; i++)
			{
				Float(ref moored[i], camera, eye, now, tick);
			}
			if (sailing.Length == 0)
			{
				return;
			}

			// The drawn ships: from the tick, a little behind it, when there is one; offline (an editor scene played on its
			// own) there is no tick to share, so the presentation clock moves the roots as well.
			double seconds;
			uint waterTick;
			if (Fleet.TryVisualTick(out double visualTick, out double tickDelta))
			{
				seconds = visualTick * tickDelta;
				waterTick = visualTick <= 0.0 ? 0u : (uint)Math.Floor(visualTick);
			}
			else
			{
				seconds = now;
				waterTick = tick;
				bool moved = false;
				for (int i = 0; i < sailing.Length; i++)
				{
					if (sailing[i].Root != null)
					{
						moved |= Place(i, PoseAt(i, now, tick));
					}
				}
				// Automatic transform sync is off, as the networked path says: without this a hull's collider stayed where it
				// was last synced while the hull was drawn sailing on, and anything walking onto the deck fell through it.
				if (moved)
				{
					Physics.SyncTransforms();
				}
			}
			for (int i = 0; i < sailing.Length; i++)
			{
				Draw(ref sailing[i], seconds, waterTick);
			}
		}

		/// <summary>
		/// A moored boat: its root (and collider) to the tide's level at the tick, settled and heeled on the bottom when the
		/// tide is out from under it; its drawn hull bobbing on the swell over that, near enough to the camera to be seen.
		/// </summary>
		private void Float(ref Moored boat, Camera camera, Vector3 eye, double now, uint tick)
		{
			if (boat.Boat == null)
			{
				return;
			}
			float surface = SurfaceAt(boat.Position, tick);
			float aground = float.IsNegativeInfinity(boat.Floor) ? 0f : Mathf.Clamp01((boat.Floor - surface) / 0.6f);
			float y = Mathf.Max(surface, boat.Floor);
			// The collider's pose depends on the tick alone. Moved only when it has moved: a static collider re-placed every
			// frame costs the physics a re-insert for nothing.
			var position = new Vector3(boat.Position.x, y, boat.Position.z);
			Quaternion pose = Quaternion.Euler(0f, boat.Yaw, 0f) * Quaternion.Euler(boat.Heel * aground, 0f, 0f);
			if ((boat.Boat.position - position).sqrMagnitude > 1e-6f || Quaternion.Angle(boat.Boat.rotation, pose) > 0.05f)
			{
				boat.Boat.SetPositionAndRotation(position, pose);
			}
			if (boat.Visual == null || (camera != null && (boat.Position - eye).sqrMagnitude > MooredReach * MooredReach))
			{
				return;
			}
			// Afloat it rides the swell; aground it lies still on its bilge.
			Bob(now, boat.Length, boat.Phase, out float rise, out float roll, out float pitch);
			float afloat = 1f - aground;
			boat.Visual.localPosition = new Vector3(0f, rise * afloat, 0f);
			boat.Visual.localRotation = Quaternion.Euler(roll * afloat, 0f, pitch * afloat);
		}

		/// <summary>A drawn ship at <paramref name="seconds"/>: the hull's course, with the swell's bob and the wind's heel on top.</summary>
		private void Draw(ref Sailing ship, double seconds, uint waterTick)
		{
			if (ship.Ship == null || ship.Lane < 0 || ship.Lane >= lanes.Length)
			{
				return;
			}
			ShipPose pose = CourseAt(lanes[ship.Lane], ship, seconds);
			Transform sails = ship.Ship, furled = ship.Furled != null ? ship.Furled : ship.Ship;
			Transform shownShip = pose.UnderWay ? sails : furled;
			Show(sails, pose.Shown && shownShip == sails);
			if (furled != sails)
			{
				Show(furled, pose.Shown && shownShip == furled);
			}
			if (!pose.Shown)
			{
				return;
			}
			float surface = SurfaceAt(pose.Position, waterTick);
			float rise, roll, pitch;
			if (airborne)
			{
				Sway(seconds, ship.Length, ship.Phase, out rise, out roll, out pitch);
			}
			else
			{
				Bob(seconds, ship.Length, ship.Phase, out rise, out roll, out pitch);
			}
			if (pose.UnderWay)
			{
				roll += airborne ? AirborneHeel : SailingHeel;
			}
			shownShip.SetPositionAndRotation(new Vector3(pose.Position.x, surface + rise, pose.Position.z),
				Quaternion.Euler(0f, pose.Yaw, 0f) * Quaternion.Euler(roll, 0f, pitch));
		}

		/// <summary>
		/// Where a ship is on its lane at <paramref name="seconds"/> of the shared clock (y the lane's sea level, which the
		/// caller replaces with the water's at the tick): the out-leg with its port easing, the wait at the far end (in port,
		/// swinging 180° to its cable; at the scene's edge, out of sight), the leg back, and the wait at the start. Pure: the
		/// same seconds give the same pose on every peer.
		/// </summary>
		public static ShipPose CourseAt(Lane lane, in Sailing ship, double seconds)
		{
			var pose = new ShipPose();
			if (lane == null || lane.Points == null || lane.Points.Length == 0)
			{
				return pose;
			}
			float length = lane.Length;
			if (length <= 1f || ship.Speed <= 0f)
			{
				pose.Position = lane.Points[0];
				return pose;
			}
			float easeStart = lane.PortAtStart ? PortEase : 0f, easeEnd = lane.PortAtEnd ? PortEase : 0f;
			float share = 1f - 0.5f * (easeStart + easeEnd);
			float leg = length / ship.Speed / share;
			double period = 2.0 * leg + ship.DwellStart + ship.DwellEnd;
			float t = (float)Repeat(seconds + ship.Offset, period);

			float along, turn = 0f;
			bool shown = true, underWay = true, backward = false;
			if (t < leg)
			{
				along = length * Covered(t / leg, easeStart, easeEnd) / share;
			}
			else if (t < leg + ship.DwellEnd)
			{
				along = length;
				shown = lane.PortAtEnd;
				underWay = false;
				// Swinging round to its cable, to sail back the way it came.
				turn = 180f * SwingShare(t - leg, ship.DwellEnd, lane.BerthSwing);
			}
			else if (t < 2f * leg + ship.DwellEnd)
			{
				along = length - length * Covered((t - leg - ship.DwellEnd) / leg, easeEnd, easeStart) / share;
				backward = true;
			}
			else
			{
				along = 0f;
				shown = lane.PortAtStart;
				underWay = false;
				backward = true;
				turn = 180f * SwingShare(t - 2f * leg - ship.DwellEnd, ship.DwellStart, lane.BerthSwing);
			}

			Vector3 ahead = PointAt(lane, Mathf.Min(length, along + HeadingReach)) - PointAt(lane, Mathf.Max(0f, along - HeadingReach));
			pose.Position = PointAt(lane, along);
			pose.Yaw = Mathf.Atan2(-ahead.z, ahead.x) * Mathf.Rad2Deg + (backward ? 180f : 0f) + turn;
			pose.Shown = shown;
			pose.UnderWay = underWay;
			return pose;
		}

		/// <summary>
		/// How long one whole round of <paramref name="ship"/> on <paramref name="lane"/> takes, seconds, as
		/// <see cref="CourseAt"/> sails it: out, the wait at the far end, back, the wait at the start. Zero for a lane it
		/// cannot sail.
		/// </summary>
		public static double RoundSeconds(Lane lane, in Sailing ship)
		{
			if (lane == null || lane.Points == null || lane.Points.Length == 0 || lane.Length <= 1f || ship.Speed <= 0f)
			{
				return 0.0;
			}
			float easeStart = lane.PortAtStart ? PortEase : 0f, easeEnd = lane.PortAtEnd ? PortEase : 0f;
			float share = 1f - 0.5f * (easeStart + easeEnd);
			float leg = lane.Length / ship.Speed / share;
			return 2.0 * leg + ship.DwellStart + ship.DwellEnd;
		}

		/// <summary>
		/// How far through its 180° swing a ship waiting <paramref name="elapsed"/> seconds of a <paramref name="dwell"/> is,
		/// 0..1. With no <paramref name="berthSwing"/> (a sea lane) it swings through the whole wait; with one it lies still
		/// until the last <paramref name="berthSwing"/> seconds (at most the whole wait) and swings then.
		/// </summary>
		public static float SwingShare(float elapsed, float dwell, float berthSwing)
		{
			if (!(berthSwing > 0f))
			{
				return Mathf.SmoothStep(0f, 1f, elapsed / Mathf.Max(1f, dwell));
			}
			float swing = Mathf.Min(berthSwing, dwell);
			return Mathf.SmoothStep(0f, 1f, (elapsed - (dwell - swing)) / Mathf.Max(1f, swing));
		}

		/// <summary>The shared clock's seconds at a synced tick: the one time base every peer agrees on, with no timeline to wait for.</summary>
		public static double SecondsAt(double tick, double tickDelta) => tick * tickDelta;

		/// <summary>
		/// The velocity that carries a rider standing at <paramref name="rider"/> (its feet, where the last tick left it) with
		/// a hull that moved from <paramref name="before"/> to <paramref name="now"/> over <paramref name="delta"/> seconds:
		/// the deck point under its feet, moved rigidly. False (and zero) when the rider is not over this deck — outside its
		/// bounds in the last tick's frame, or below the waterline, as a swimmer alongside is — or the ship is hidden.
		/// </summary>
		public static bool TryCarry(in ShipPose before, in ShipPose now, Bounds deck, Vector3 rider, float delta, out Vector3 velocity)
			=> TryCarry(before, now, deck, rider, delta, out velocity, out _);

		/// <summary>
		/// <see cref="TryCarry(in ShipPose, in ShipPose, Bounds, Vector3, float, out Vector3)"/>, and the yaw the hull turned
		/// through, degrees about +y (<see cref="Turn"/>): what the rider's facing turns by, so it keeps its bearing on the deck
		/// as its feet keep their place. Zero when not carried.
		/// </summary>
		public static bool TryCarry(in ShipPose before, in ShipPose now, Bounds deck, Vector3 rider, float delta, out Vector3 velocity, out float turn)
			=> TryCarry(before, now, deck, rider, delta, RiderAboveWaterline, out velocity, out turn);

		/// <summary>
		/// <see cref="TryCarry(in ShipPose, in ShipPose, Bounds, Vector3, float, out Vector3, out float)"/> with the lowest a
		/// rider's feet may stand in the hull's frame and still be aboard: <see cref="RiderAboveWaterline"/> on the water,
		/// negative infinity for an airship (nothing but its own hull's bounds, as nobody swims alongside it).
		/// </summary>
		public static bool TryCarry(in ShipPose before, in ShipPose now, Bounds deck, Vector3 rider, float delta, float lowest, out Vector3 velocity, out float turn)
		{
			velocity = Vector3.zero;
			turn = 0f;
			if (!before.Shown || !now.Shown || delta <= 0f || deck.size.x <= 0f || deck.size.z <= 0f)
			{
				return false;
			}
			Vector3 local = before.ToLocal(rider);
			Vector3 min = deck.min, max = deck.max;
			if (local.x < min.x - DeckMargin || local.x > max.x + DeckMargin ||
				local.z < min.z - DeckMargin || local.z > max.z + DeckMargin ||
				local.y < Mathf.Max(lowest, min.y) || local.y > max.y + DeckHeadroom)
			{
				return false;
			}
			velocity = (now.ToWorld(local) - rider) / delta;
			turn = Turn(before, now);
			return true;
		}

		/// <summary>
		/// The yaw a hull turned through from <paramref name="before"/> to <paramref name="now"/>, degrees about +y, the short
		/// way round: the heading comes from an arctangent that wraps at ±180°, and a round's two swings add a whole turn, so
		/// the plain difference can be a full turn out.
		/// </summary>
		public static float Turn(in ShipPose before, in ShipPose now) => Mathf.DeltaAngle(before.Yaw, now.Yaw);

		/// <summary>
		/// A direction turned by <paramref name="turn"/> degrees about +y in Unity's sense (<c>Quaternion.Euler(0, turn, 0)</c>),
		/// as a deck turns everything standing on it: the rotation <see cref="ShipPose.ToWorld"/> puts between two poses.
		/// </summary>
		public static Vector3 Turned(Vector3 direction, float turn)
		{
			float r = turn * Mathf.Deg2Rad;
			float c = Mathf.Cos(r), s = Mathf.Sin(r);
			return new Vector3(direction.x * c + direction.z * s, direction.y, -direction.x * s + direction.z * c);
		}

		/// <summary>
		/// The carry of whichever ship under way in <paramref name="scene"/> a rider at <paramref name="rider"/> stands on,
		/// at the synced <paramref name="tick"/> it is simulating (a rider's <c>KCCController.WaterTick</c>, the tick the
		/// ships are posed at, live and in replay): the velocity, the yaw the hull turned through this tick, degrees, and
		/// which ship it is. Zero, default and false, aboard none.
		/// </summary>
		public static bool TryGetCarry(Scene scene, uint tick, double tickDelta, Vector3 rider, float delta, out Vector3 velocity, out float turn, out ShipRef ship)
		{
			using (CarryMarker.Auto())
			{
				return TryGetCarryIn(scene, tick, tickDelta, rider, delta, out velocity, out turn, out ship);
			}
		}

		private static bool TryGetCarryIn(Scene scene, uint tick, double tickDelta, Vector3 rider, float delta, out Vector3 velocity, out float turn, out ShipRef ship)
		{
			velocity = Vector3.zero;
			turn = 0f;
			ship = default;
			List<FloatingBoats> fleets = Fleet.In(scene);
			if (fleets == null)
			{
				return false;
			}
			// A character simulating here: the hulls near it must stand solid for it from the next tick on (PoseShips).
			Fleet.NoteRider(scene, rider);
			for (int f = 0; f < fleets.Count; f++)
			{
				FloatingBoats boats = fleets[f];
				if (boats != null && boats.TryCarryAt(tick, tickDelta, rider, delta, out velocity, out turn, out int index))
				{
					ship = new ShipRef(boats, index);
					return true;
				}
			}
			velocity = Vector3.zero;
			turn = 0f;
			return false;
		}

		/// <summary>
		/// The yaw <paramref name="ship"/>'s drawn hull shows this frame, degrees: worked out at the same fractional tick
		/// <see cref="LateUpdate"/> draws it at. What a rider's camera turns by from one frame to the next, so the view swings
		/// with the deck the rider sees (drawn at the rider's own lag) rather than with the collider a few ticks ahead of it.
		/// False for no ship, or one out of sight.
		/// </summary>
		public static bool TryGetDrawnYaw(in ShipRef ship, out float yaw)
		{
			yaw = 0f;
			if (!ship.IsValid)
			{
				return false;
			}
			FloatingBoats boats = ship.Fleet;
			Sailing sailing = boats.sailing[ship.Index];
			if (sailing.Lane < 0 || sailing.Lane >= boats.lanes.Length)
			{
				return false;
			}
			double seconds = Fleet.TryVisualTick(out double visualTick, out double tickDelta) ? visualTick * tickDelta : WorldMotion.Seconds;
			ShipPose pose = CourseAt(boats.lanes[sailing.Lane], sailing, seconds);
			yaw = pose.Yaw;
			return pose.Shown;
		}

		private bool TryCarryAt(uint tick, double tickDelta, Vector3 rider, float delta, out Vector3 velocity, out float turn, out int index)
		{
			velocity = Vector3.zero;
			turn = 0f;
			index = -1;
			EnsureStates();
			for (int i = 0; i < sailing.Length; i++)
			{
				Sailing ship = sailing[i];
				if (ship.Root == null)
				{
					continue;
				}
				// Cheap rejection first: a rider nowhere near this hull.
				float reach = Mathf.Max(ship.Length, ship.Deck.extents.magnitude) + 2f;
				/* Before any pose: a hull is always on its lane, so a rider further than the reach from the whole lane's
				 * extent is further than it from the hull, whatever the tick (and an unusable lane carries nobody). */
				if (ship.Lane < 0 || ship.Lane >= laneAreas.Length || !MayReach(laneAreas[ship.Lane], reach, rider))
				{
					continue;
				}
				ShipPose now, before;
				bool posedHere = states[i].Posed && states[i].Tick == tick;
				if (posedHere)
				{
					now = states[i].Now;
					before = states[i].Before;
				}
				else
				{
					now = PoseAt(i, SecondsAt(tick, tickDelta), tick);
					before = PoseAt(i, SecondsAt(tick - 1.0, tickDelta), tick - 1u);
				}
				if ((now.Position - rider).sqrMagnitude > reach * reach)
				{
					continue;
				}
				if (!TryCarry(before, now, ship.Deck, rider, delta, airborne ? float.NegativeInfinity : RiderAboveWaterline, out Vector3 carry, out float turned))
				{
					continue;
				}
				// Confirmed on the hull itself where the physics has it at this tick: feet over its deck, not over a quay or a
				// moored boat that happens to lie inside the hull's bounding box.
				if (posedHere && !OverDeck(states[i].Colliders, now.ToWorld(before.ToLocal(rider))))
				{
					continue;
				}
				velocity = carry;
				turn = turned;
				index = i;
				return true;
			}
			return false;
		}

		/// <summary>Whether a short ray down from just over a point meets one of the hull's colliders.</summary>
		private static bool OverDeck(Collider[] colliders, Vector3 feet)
		{
			if (colliders == null)
			{
				return false;
			}
			var ray = new Ray(feet + Vector3.up * 0.75f, Vector3.down);
			for (int c = 0; c < colliders.Length; c++)
			{
				Collider collider = colliders[c];
				if (collider != null && collider.enabled && !collider.isTrigger && collider.Raycast(ray, out RaycastHit _, 1.5f))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>Ship <paramref name="i"/>'s hull at a moment, on the water at <paramref name="waterTick"/>.</summary>
		private ShipPose PoseAt(int i, double seconds, uint waterTick)
		{
			Sailing ship = sailing[i];
			if (ship.Lane < 0 || ship.Lane >= lanes.Length)
			{
				return new ShipPose();
			}
			ShipPose pose = CourseAt(lanes[ship.Lane], ship, seconds);
			pose.Position.y = SurfaceAt(pose.Position, waterTick);
			return pose;
		}

		/// <summary>
		/// The tick driver's work: every ship's hull to where it stands at the synced <paramref name="tick"/>, its collider on
		/// or off. True when any collider moved or changed, so the caller syncs the physics.
		/// </summary>
		private bool PoseShips(uint tick, double tickDelta)
		{
			EnsureStates();
			bool changed = false;
			Scene scene = gameObject.scene;
			for (int i = 0; i < sailing.Length; i++)
			{
				if (sailing[i].Root == null)
				{
					continue;
				}
				ref ShipState state = ref states[i];
				ShipPose before = state.Posed && state.Tick == tick - 1u ? state.Now : PoseAt(i, SecondsAt(tick - 1.0, tickDelta), tick - 1u);
				ShipPose now = state.Posed && state.Tick == tick ? state.Now : PoseAt(i, SecondsAt(tick, tickDelta), tick);
				state.Before = before;
				state.Now = now;
				state.Tick = tick;
				state.Posed = true;
				/* Placed only where a character could meet it (SolidReach). Elsewhere it is switched off, not left standing
				 * where it last was, so a character arriving later never meets a hull the ship has sailed out of; it is
				 * placed again, at this tick's pose, the tick one comes within reach. The pose itself (and so any carry)
				 * is the tick's either way. */
				if (now.Shown && !Fleet.AnyoneNear(scene, now.Position, SolidReach + Mathf.Max(sailing[i].Length, sailing[i].Deck.extents.magnitude)))
				{
					changed |= Solid(ref state, false);
					continue;
				}
				changed |= Place(i, now);
			}
			return changed;
		}

		/// <summary>
		/// Whether a rider at <paramref name="rider"/> can be within <paramref name="reach"/> of a point somewhere in
		/// <paramref name="area"/> (a lane's extent across the ground): false only when it is further than that from all of it.
		/// </summary>
		public static bool MayReach(Rect area, float reach, Vector3 rider)
			=> rider.x >= area.xMin - reach && rider.x <= area.xMax + reach && rider.z >= area.yMin - reach && rider.z <= area.yMax + reach;

		/// <summary>A lane's points' extent across the ground (x, z as the rect's x, y); empty at the origin for a lane with none.</summary>
		public static Rect LaneArea(Lane lane)
		{
			if (lane == null || lane.Points == null || lane.Points.Length == 0)
			{
				return new Rect(0f, 0f, 0f, 0f);
			}
			float minX = float.PositiveInfinity, minZ = float.PositiveInfinity, maxX = float.NegativeInfinity, maxZ = float.NegativeInfinity;
			foreach (Vector3 p in lane.Points)
			{
				minX = Mathf.Min(minX, p.x);
				maxX = Mathf.Max(maxX, p.x);
				minZ = Mathf.Min(minZ, p.z);
				maxZ = Mathf.Max(maxZ, p.z);
			}
			return Rect.MinMaxRect(minX, minZ, maxX, maxZ);
		}

		/// <summary>Stands ship <paramref name="i"/>'s root at a pose; hidden, its collider off and the root left where it was.</summary>
		private bool Place(int i, ShipPose pose)
		{
			EnsureStates();
			Transform root = sailing[i].Root;
			bool changed = Solid(ref states[i], pose.Shown);
			if (!pose.Shown)
			{
				return changed;
			}
			Quaternion rotation = Quaternion.Euler(0f, pose.Yaw, 0f);
			if ((root.position - pose.Position).sqrMagnitude > 1e-8f || Quaternion.Angle(root.rotation, rotation) > 1e-3f)
			{
				root.SetPositionAndRotation(pose.Position, rotation);
				changed = true;
			}
			return changed;
		}

		/// <summary>Turns a ship's colliders and obstacle on or off, only when that changes.</summary>
		private static bool Solid(ref ShipState state, bool solid)
		{
			if (state.SolidApplied && state.Solid == solid)
			{
				return false;
			}
			state.Solid = solid;
			state.SolidApplied = true;
			if (state.Colliders != null)
			{
				foreach (Collider collider in state.Colliders)
				{
					if (collider != null)
					{
						collider.enabled = solid;
					}
				}
			}
			if (state.Obstacle != null)
			{
				state.Obstacle.enabled = solid;
			}
			return true;
		}

		private void EnsureStates()
		{
			if (laneAreas == null || !ReferenceEquals(laneAreasOf, lanes) || laneAreas.Length != lanes.Length)
			{
				laneAreas = new Rect[lanes.Length];
				for (int l = 0; l < lanes.Length; l++)
				{
					laneAreas[l] = LaneArea(lanes[l]);
				}
				laneAreasOf = lanes;
			}
			if (states != null && states.Length == sailing.Length)
			{
				return;
			}
			states = new ShipState[sailing.Length];
			for (int i = 0; i < sailing.Length; i++)
			{
				Transform root = sailing[i].Root;
				if (root == null)
				{
					continue;
				}
				// The root's own colliders only: the drawn hulls under it have none (the cut strips them).
				states[i].Colliders = root.GetComponents<Collider>();
				states[i].Obstacle = root.GetComponent<UnityEngine.AI.NavMeshObstacle>();
			}
		}

		/// <summary>
		/// The share of a leg's distance covered at <paramref name="u"/> of its time, at full speed but for a ramp up over
		/// the first <paramref name="easeStart"/> of the time and down over the last <paramref name="easeEnd"/>: out of
		/// 1 − (easeStart + easeEnd) / 2 at the leg's end.
		/// </summary>
		public static float Covered(float u, float easeStart, float easeEnd)
		{
			u = Mathf.Clamp01(u);
			float s = easeStart > 0f && u < easeStart ? u * u / (2f * easeStart) : easeStart * 0.5f + (u - easeStart);
			if (easeEnd > 0f && u > 1f - easeEnd)
			{
				float over = u - (1f - easeEnd);
				s -= over * over / (2f * easeEnd);
			}
			return s;
		}

		/// <summary>The lane's point <paramref name="along"/> metres from its start.</summary>
		public static Vector3 PointAt(Lane lane, float along)
		{
			Vector3[] points = lane.Points;
			float[] distance = lane.Along;
			int n = Mathf.Min(points.Length, distance.Length);
			if (n == 0)
			{
				return Vector3.zero;
			}
			if (along <= 0f || n == 1)
			{
				return points[0];
			}
			if (along >= distance[n - 1])
			{
				return points[n - 1];
			}
			int lo = 0, hi = n - 1;
			while (hi - lo > 1)
			{
				int mid = (lo + hi) >> 1;
				if (distance[mid] <= along)
				{
					lo = mid;
				}
				else
				{
					hi = mid;
				}
			}
			float span = distance[hi] - distance[lo];
			return Vector3.Lerp(points[lo], points[hi], span > 1e-4f ? (along - distance[lo]) / span : 0f);
		}

		/// <summary>The remainder of <paramref name="value"/> over a positive <paramref name="period"/>, in [0, period).</summary>
		private static double Repeat(double value, double period)
		{
			if (!(period > 0.0))
			{
				return 0.0;
			}
			double r = value - Math.Floor(value / period) * period;
			return r < 0.0 ? 0.0 : r >= period ? 0.0 : r;
		}

		/// <summary>The list an airship under way takes from the wind on its gasbag, degrees: less than a ship's under sail.</summary>
		private const float AirborneHeel = 1.2f;

		/// <summary>
		/// An airship's motion on the air: a slow lift and a gentle roll, slower and smaller for a big ship. Kept to a few
		/// centimetres at the deck's edge, as a ship's bob is: the collider a rider stands on does not move with it.
		/// </summary>
		private static void Sway(double now, float length, float phase, out float rise, out float roll, out float pitch)
		{
			float small = Mathf.Clamp01((26f - length) / 12f);
			float period = Mathf.Lerp(13f, 9f, small);
			float a = (float)WorldMotion.Repeat(now / period + phase, 1.0) * Mathf.PI * 2f;
			float b = (float)WorldMotion.Repeat(now / (period * 1.61) + phase * 0.73, 1.0) * Mathf.PI * 2f;
			rise = Mathf.Lerp(0.1f, 0.08f, small) * Mathf.Sin(a);
			roll = Mathf.Lerp(0.7f, 1.3f, small) * Mathf.Sin(b);
			pitch = Mathf.Lerp(0.3f, 0.6f, small) * Mathf.Sin(a + 1.3f);
		}

		/// <summary>The boat's motion on the swell: a small boat bobs quicker and rolls further than a ship.</summary>
		private static void Bob(double now, float length, float phase, out float rise, out float roll, out float pitch)
		{
			float small = Mathf.Clamp01((12f - length) / 8f);
			float period = Mathf.Lerp(7f, 3.6f, small);
			float a = (float)WorldMotion.Repeat(now / period + phase, 1.0) * Mathf.PI * 2f;
			float b = (float)WorldMotion.Repeat(now / (period * 1.37) + phase * 0.61, 1.0) * Mathf.PI * 2f;
			rise = Mathf.Lerp(0.07f, 0.05f, small) * Mathf.Sin(a);
			roll = Mathf.Lerp(1.2f, 3.5f, small) * Mathf.Sin(b);
			pitch = Mathf.Lerp(0.5f, 1.6f, small) * Mathf.Sin(a + 1.1f);
		}

		/// <summary>
		/// The water's surface under a boat at a tick; where nothing is registered (in the editor), the level it was cut
		/// against. An airship's level is always its lane's: whatever water lies under it is far below.
		/// </summary>
		private float SurfaceAt(Vector3 at, uint tick)
		{
			if (airborne)
			{
				return at.y;
			}
			WaterSample sample = WaterQuery.Sample(gameObject.scene, at.x, at.z, tick);
			return sample.Present ? sample.Surface : at.y;
		}

		private static void Show(Transform boat, bool shown)
		{
			if (boat != null && boat.gameObject.activeSelf != shown)
			{
				boat.gameObject.SetActive(shown);
			}
		}

		/// <summary>
		/// The FishMMO Water plugin's hull mark (FishHullShoreMark.shader): drawn over a hull's own pixels, it tells the sea's
		/// shore pass to leave them alone. Found by name, as this assembly does not reference the plugin; the scene's
		/// WaterShore references the shader so a build includes it.
		/// </summary>
		public const string ShoreMarkShaderName = "FishMMO/Water/Hull Shore Mark";

		/// <summary>The object each hull mark is drawn from, a child of the hull renderer it copies.</summary>
		public const string ShoreMarkName = "Shore Mark";

		/// <summary>One material for every mark: it has no properties.</summary>
		private static Material shoreMark;

		[NonSerialized] private bool hullsMarked;

		/// <summary>
		/// Marks every drawn hull for the sea's shore pass to skip (FishHullShoreMark.shader). That pass draws the swash, its
		/// foam and the wet sand on whatever the depth buffer holds at the sea's edge, by design (terrain, rocks, pier piles),
		/// and a boat at its waterline drew a beach up its side. Once per component, on the client only (a server has no
		/// drawn hulls); in play only, as nothing of it may be saved into the scene. Presentation alone: no pose, collider
		/// or carry is touched.
		/// </summary>
		private void MarkHulls()
		{
			if (hullsMarked || !Application.isPlaying)
			{
				return;
			}
			hullsMarked = true;
			// An airship never meets the sea's edge.
			if (airborne || !HasDrawnHull())
			{
				return;
			}
			Material mark = ShoreMark();
			if (mark == null)
			{
				return;
			}
			var marks = new Dictionary<Renderer, Renderer>();
			for (int i = 0; i < moored.Length; i++)
			{
				MarkHull(moored[i].Visual, mark, marks);
			}
			for (int i = 0; i < sailing.Length; i++)
			{
				MarkHull(sailing[i].Ship, mark, marks);
				if (sailing[i].Furled != sailing[i].Ship)
				{
					MarkHull(sailing[i].Furled, mark, marks);
				}
			}
		}

		private bool HasDrawnHull()
		{
			for (int i = 0; i < moored.Length; i++)
			{
				if (moored[i].Visual != null)
				{
					return true;
				}
			}
			for (int i = 0; i < sailing.Length; i++)
			{
				if (sailing[i].Ship != null || sailing[i].Furled != null)
				{
					return true;
				}
			}
			return false;
		}

		private static Material ShoreMark()
		{
			if (shoreMark != null)
			{
				return shoreMark;
			}
			Shader shader = Shader.Find(ShoreMarkShaderName);
			if (shader == null || !shader.isSupported)
			{
				return null;
			}
			return shoreMark = new Material(shader) { name = "Hull shore mark", hideFlags = HideFlags.HideAndDontSave, enableInstancing = true };
		}

		/// <summary>
		/// One drawn hull: a mark beside each of its opaque mesh renderers, and each mark put into its renderer's LOD (a
		/// mark outside the group would draw the near mesh at every distance, against a far mesh's depth).
		/// </summary>
		private static void MarkHull(Transform hull, Material mark, Dictionary<Renderer, Renderer> marks)
		{
			if (hull == null)
			{
				return;
			}
			marks.Clear();
			foreach (MeshRenderer renderer in hull.GetComponentsInChildren<MeshRenderer>(true))
			{
				Renderer marked = MarkRenderer(renderer, mark);
				if (marked != null)
				{
					marks[renderer] = marked;
				}
			}
			if (marks.Count == 0)
			{
				return;
			}
			foreach (LODGroup group in hull.GetComponentsInChildren<LODGroup>(true))
			{
				LOD[] lods = group.GetLODs();
				bool changed = false;
				for (int l = 0; l < lods.Length; l++)
				{
					Renderer[] renderers = lods[l].renderers;
					if (renderers == null)
					{
						continue;
					}
					var withMarks = new List<Renderer>(renderers.Length * 2);
					foreach (Renderer renderer in renderers)
					{
						withMarks.Add(renderer);
						if (renderer != null && marks.TryGetValue(renderer, out Renderer marked))
						{
							withMarks.Add(marked);
							changed = true;
						}
					}
					lods[l].renderers = withMarks.ToArray();
				}
				if (changed)
				{
					group.SetLODs(lods);
				}
			}
		}

		/// <summary>
		/// The mark for one hull renderer: its mesh again, every submesh it draws, with the mark's material. None for a
		/// renderer with anything but opaque materials — a transparent surface (the water lid among them) writes no depth,
		/// so a mark there would stand for whatever lies behind it.
		/// </summary>
		private static Renderer MarkRenderer(MeshRenderer renderer, Material mark)
		{
			if (renderer.gameObject.name == ShoreMarkName)
			{
				return null;
			}
			MeshFilter filter = renderer.GetComponent<MeshFilter>();
			Mesh mesh = filter != null ? filter.sharedMesh : null;
			Material[] materials = renderer.sharedMaterials;
			if (mesh == null || materials.Length == 0)
			{
				return null;
			}
			foreach (Material material in materials)
			{
				if (material == null || material.renderQueue > (int)UnityEngine.Rendering.RenderQueue.GeometryLast)
				{
					return null;
				}
			}
			var marks = new Material[Mathf.Min(mesh.subMeshCount, materials.Length)];
			for (int i = 0; i < marks.Length; i++)
			{
				marks[i] = mark;
			}

			var holder = new GameObject(ShoreMarkName) { layer = renderer.gameObject.layer };
			holder.transform.SetParent(renderer.transform, false);
			holder.AddComponent<MeshFilter>().sharedMesh = mesh;
			var marked = holder.AddComponent<MeshRenderer>();
			marked.sharedMaterials = marks;
			marked.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			marked.receiveShadows = false;
			marked.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
			marked.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
			marked.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
			marked.renderingLayerMask = renderer.renderingLayerMask;
			marked.allowOcclusionWhenDynamic = renderer.allowOcclusionWhenDynamic;
			marked.enabled = renderer.enabled;
			return marked;
		}

		/// <summary>
		/// The synced tick a peer simulates its <paramref name="localTick"/> at: the server's own tick on the server; on a
		/// client, its local tick mapped through the last reconcile's (client, server) pairing — the same mapping
		/// (<see cref="WeatherExposureTick.Resolve(bool, uint, uint, uint, uint, uint)"/>) a rider's replicate uses for the
		/// water, so the ships are posed at exactly the tick the rider reads them at, live and in replay.
		/// </summary>
		public static uint SyncTick(bool isServer, uint serverTick, uint clientSyncTick, uint clientStateTick, uint serverStateTick, uint localTick)
			=> WeatherExposureTick.Resolve(isServer, serverTick, clientSyncTick, clientStateTick, serverStateTick, localTick);

		/// <summary>
		/// Poses every ship under way from FishNet's tick, on whichever peer this is, and remembers where the drawn ships
		/// should be. One subscription per process however many scenes (a scene server hosts several).
		/// </summary>
		private static class Fleet
		{
			private static readonly Dictionary<int, List<FloatingBoats>> byScene = new Dictionary<int, List<FloatingBoats>>();
			private static readonly List<FloatingBoats> all = new List<FloatingBoats>();
			private static NetworkManager network;
			private static TimeManager time;
			private static PredictionManager prediction;
			private static bool attached;

			private static int visualFrame = -1;
			private static double visualTick, visualOffset;
			private static bool visualOffsetSet;

			/// <summary>The latest riders' positions (whoever asked for a carry), a ring: on a client, the character it predicts.</summary>
			private const int RiderSlots = 32;
			private static readonly Vector3[] riderAt = new Vector3[RiderSlots];
			private static readonly int[] riderScene = new int[RiderSlots];
			private static readonly float[] riderTime = new float[RiderSlots];
			private static int riderNext, riderCount;

			public static void NoteRider(Scene scene, Vector3 position)
			{
				riderAt[riderNext] = position;
				riderScene[riderNext] = scene.handle;
				riderTime[riderNext] = Time.unscaledTime;
				riderNext = (riderNext + 1) % RiderSlots;
				riderCount = Mathf.Min(RiderSlots, riderCount + 1);
			}

			/// <summary>
			/// Whether anything that collides with a hull may be within <paramref name="reach"/> of <paramref name="point"/>
			/// (across the ground): a rider of the last second; on the server any character of the scene; on a client its
			/// camera, which its shelter map and its own collisions work round.
			/// </summary>
			public static bool AnyoneNear(Scene scene, Vector3 point, float reach)
			{
				float reachSquared = reach * reach;
				float now = Time.unscaledTime;
				int handle = scene.handle;
				for (int r = 0; r < riderCount; r++)
				{
					if (riderScene[r] == handle && now - riderTime[r] <= RiderMemorySeconds && Near(riderAt[r], point, reachSquared))
					{
						return true;
					}
				}
				if (network != null && network.IsServerStarted && LagCompensationRegistry.AnyWithin(scene, point, reach))
				{
					return true;
				}
				if (network == null || network.IsClientStarted)
				{
					Camera camera = Camera.main;
					if (camera != null && Near(camera.transform.position, point, reachSquared))
					{
						return true;
					}
				}
				return false;
			}

			private static bool Near(Vector3 a, Vector3 b, float reachSquared)
			{
				float dx = a.x - b.x, dz = a.z - b.z;
				return dx * dx + dz * dz <= reachSquared;
			}

			public static void Add(FloatingBoats boats)
			{
				if (all.Contains(boats))
				{
					return;
				}
				all.Add(boats);
				int handle = boats.gameObject.scene.handle;
				if (!byScene.TryGetValue(handle, out List<FloatingBoats> list))
				{
					list = new List<FloatingBoats>();
					byScene.Add(handle, list);
				}
				list.Add(boats);
				Attach();
			}

			public static void Remove(FloatingBoats boats)
			{
				if (!all.Remove(boats))
				{
					return;
				}
				foreach (KeyValuePair<int, List<FloatingBoats>> pair in byScene)
				{
					if (pair.Value.Remove(boats) && pair.Value.Count == 0)
					{
						byScene.Remove(pair.Key);
						break;
					}
				}
				if (all.Count == 0)
				{
					Detach();
				}
			}

			public static List<FloatingBoats> In(Scene scene) => byScene.TryGetValue(scene.handle, out List<FloatingBoats> list) ? list : null;

			/// <summary>
			/// Subscribes to the running NetworkManager's clock, or to a new one after the last was destroyed. Asked of the
			/// instances first: InstanceFinder logs "NetworkManager not found" every time it finds none.
			/// </summary>
			private static void Attach()
			{
				if (attached && network != null)
				{
					return;
				}
				Detach();
				if (all.Count == 0 || NetworkManager.Instances.Count == 0)
				{
					return;
				}
				NetworkManager nm = InstanceFinder.NetworkManager;
				// The NetworkManager's own PredictionManager is internal to FishNet; InstanceFinder hands out the same one.
				PredictionManager pm = nm != null ? InstanceFinder.PredictionManager : null;
				if (nm == null || nm.TimeManager == null || pm == null)
				{
					return;
				}
				network = nm;
				time = nm.TimeManager;
				prediction = pm;
				time.OnPreTick += OnPreTick;
				prediction.OnPreReplicateReplay += OnPreReplicateReplay;
				prediction.OnPostReconcile += OnPostReconcile;
				attached = true;
			}

			private static void Detach()
			{
				// Plain C# events: safe to unsubscribe from a destroyed manager.
				if (!ReferenceEquals(time, null))
				{
					time.OnPreTick -= OnPreTick;
				}
				if (!ReferenceEquals(prediction, null))
				{
					prediction.OnPreReplicateReplay -= OnPreReplicateReplay;
					prediction.OnPostReconcile -= OnPostReconcile;
				}
				network = null;
				time = null;
				prediction = null;
				attached = false;
				visualOffsetSet = false;
			}

			/// <summary>The synced tick this peer simulates <paramref name="localTick"/> at.</summary>
			private static uint SyncTickOf(uint localTick)
			{
				if (network.IsServerStarted)
				{
					return time.Tick;
				}
				WeatherExposureTick.ReconcilePairing.Get(prediction, network.ClientManager, out uint clientStateTick, out uint serverStateTick);
				return SyncTick(false, WeatherExposureTick.Unset, time.Tick, clientStateTick, serverStateTick, localTick);
			}

			/// <summary>The live tick, before anything simulates it (and before a client's reconcile replays).</summary>
			private static void OnPreTick() => PoseAll(SyncTickOf(time.LocalTick));

			/// <summary>Each replayed tick, before its replicates run: the hull where it stood on that tick.</summary>
			private static void OnPreReplicateReplay(uint clientTick, uint serverTick) => PoseAll(SyncTickOf(clientTick));

			/// <summary>After a replay, back to the live tick (whose OnTick follows), mapped through the reconcile's own pairing.</summary>
			private static void OnPostReconcile(uint clientTick, uint serverTick) => PoseAll(SyncTickOf(time.LocalTick));

			private static void PoseAll(uint tick)
			{
				using (PoseAllMarker.Auto())
				{
					PoseAllAt(tick);
				}
			}

			private static void PoseAllAt(uint tick)
			{
				if (network == null)
				{
					attached = false;
					return;
				}
				double tickDelta = time.TickDelta;
				bool changed = false;
				for (int i = all.Count - 1; i >= 0; i--)
				{
					FloatingBoats boats = all[i];
					if (boats == null)
					{
						all.RemoveAt(i);
						continue;
					}
					changed |= boats.PoseShips(tick, tickDelta);
				}
				// Automatic transform sync is off: without this the character controller would probe last tick's hulls.
				if (changed)
				{
					Physics.SyncTransforms();
				}
			}

			/// <summary>
			/// The fractional synced tick the drawn ships show this frame: the last simulated tick, mapped, less
			/// <see cref="VisualInterpolationTicks"/>, plus how far the clock is into the next — where the owner's smoothed
			/// character is drawn. The mapping's offset is eased (it can step a tick when a reconcile re-pairs) so a drawn hull
			/// never jumps. False with no running clock.
			/// </summary>
			public static bool TryVisualTick(out double tick, out double tickDelta)
			{
				tick = 0.0;
				tickDelta = 0.0;
				Attach();
				if (!attached || network == null || time == null || time.TickDelta <= 0.0 || time.LocalTick == 0u)
				{
					return false;
				}
				tickDelta = time.TickDelta;
				if (Time.frameCount != visualFrame)
				{
					visualFrame = Time.frameCount;
					uint last = time.LocalTick - 1u;
					double offset = (double)SyncTickOf(last) - last;
					if (!visualOffsetSet || Math.Abs(offset - visualOffset) > 8.0)
					{
						visualOffset = offset;
						visualOffsetSet = true;
					}
					else
					{
						double step = Time.unscaledDeltaTime / tickDelta * 0.25;
						visualOffset = offset > visualOffset ? Math.Min(offset, visualOffset + step) : Math.Max(offset, visualOffset - step);
					}
					visualTick = last + visualOffset - VisualInterpolationTicks + time.GetTickPercentAsDouble();
				}
				tick = visualTick;
				return true;
			}
		}
	}
}
