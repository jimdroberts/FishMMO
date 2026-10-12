using System;
using System.Collections.Generic;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;
using FishNet;
using FishNet.Managing;
using FishNet.Managing.Predicting;
using FishNet.Managing.Timing;
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
	/// the deck — and the motor conveys it only to a stably grounded character. Who is aboard is decided spatially, again
	/// from the tick (feet over the deck in the last tick's frame, confirmed by a ray down onto this hull's own collider),
	/// never from a trigger volume or the motor's ground collider, neither of which a reconcile restores.
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
	/// Built by the cut: a harbour's (PointOfInterestHarbour) and the sea lanes' (PointOfInterestSeaTraffic), each on an
	/// object the server keeps, each boat's drawn hull under a ClientOnlyObject.
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
			/// <summary>The hull collider's bounds in the root's space (waterline y 0, bow +x): where a rider can stand.</summary>
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

		/// <summary>Set by the cut.</summary>
		public void Configure(Moored[] boats, Lane[] ways, Sailing[] ships)
		{
			moored = boats ?? Array.Empty<Moored>();
			lanes = ways ?? Array.Empty<Lane>();
			sailing = ships ?? Array.Empty<Sailing>();
		}

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
				for (int i = 0; i < sailing.Length; i++)
				{
					if (sailing[i].Root != null)
					{
						Place(i, PoseAt(i, now, tick));
					}
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
			Bob(seconds, ship.Length, ship.Phase, out float rise, out float roll, out float pitch);
			if (pose.UnderWay)
			{
				roll += SailingHeel;
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
				turn = 180f * Mathf.SmoothStep(0f, 1f, (t - leg) / Mathf.Max(1f, ship.DwellEnd));
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
				turn = 180f * Mathf.SmoothStep(0f, 1f, (t - 2f * leg - ship.DwellEnd) / Mathf.Max(1f, ship.DwellStart));
			}

			Vector3 ahead = PointAt(lane, Mathf.Min(length, along + HeadingReach)) - PointAt(lane, Mathf.Max(0f, along - HeadingReach));
			pose.Position = PointAt(lane, along);
			pose.Yaw = Mathf.Atan2(-ahead.z, ahead.x) * Mathf.Rad2Deg + (backward ? 180f : 0f) + turn;
			pose.Shown = shown;
			pose.UnderWay = underWay;
			return pose;
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
		{
			velocity = Vector3.zero;
			if (!before.Shown || !now.Shown || delta <= 0f || deck.size.x <= 0f || deck.size.z <= 0f)
			{
				return false;
			}
			Vector3 local = before.ToLocal(rider);
			Vector3 min = deck.min, max = deck.max;
			if (local.x < min.x - DeckMargin || local.x > max.x + DeckMargin ||
				local.z < min.z - DeckMargin || local.z > max.z + DeckMargin ||
				local.y < Mathf.Max(RiderAboveWaterline, min.y) || local.y > max.y + DeckHeadroom)
			{
				return false;
			}
			velocity = (now.ToWorld(local) - rider) / delta;
			return true;
		}

		/// <summary>
		/// The carry of whichever ship under way in <paramref name="scene"/> a rider at <paramref name="rider"/> stands on,
		/// at the synced <paramref name="tick"/> it is simulating (a rider's <c>KCCController.WaterTick</c>, the tick the
		/// ships are posed at, live and in replay). Zero, and false, aboard none.
		/// </summary>
		public static bool TryGetCarry(Scene scene, uint tick, double tickDelta, Vector3 rider, float delta, out Vector3 velocity)
		{
			velocity = Vector3.zero;
			List<FloatingBoats> fleets = Fleet.In(scene);
			if (fleets == null)
			{
				return false;
			}
			for (int f = 0; f < fleets.Count; f++)
			{
				FloatingBoats boats = fleets[f];
				if (boats != null && boats.TryCarryAt(tick, tickDelta, rider, delta, out velocity))
				{
					return true;
				}
			}
			velocity = Vector3.zero;
			return false;
		}

		private bool TryCarryAt(uint tick, double tickDelta, Vector3 rider, float delta, out Vector3 velocity)
		{
			velocity = Vector3.zero;
			EnsureStates();
			for (int i = 0; i < sailing.Length; i++)
			{
				Sailing ship = sailing[i];
				if (ship.Root == null)
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
				// Cheap rejection first: a rider nowhere near this hull.
				float reach = Mathf.Max(ship.Length, ship.Deck.extents.magnitude) + 2f;
				if ((now.Position - rider).sqrMagnitude > reach * reach)
				{
					continue;
				}
				if (!TryCarry(before, now, ship.Deck, rider, delta, out Vector3 carry))
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
				changed |= Place(i, now);
			}
			return changed;
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

		/// <summary>The water's surface under a boat at a tick; where nothing is registered (in the editor), the level it was cut against.</summary>
		private float SurfaceAt(Vector3 at, uint tick)
		{
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
