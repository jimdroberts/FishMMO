using UnityEngine;
using UnityEngine.InputSystem;
using FishMMO.Client;
using FishMMO.Shared.Weather;
#if UNITY_EDITOR
using FishMMO.Shared;
using FishMMO.Shared.Core;
#endif

namespace FishMMO.TestHarness.World
{
	/// <summary>
	/// The world test bed's camera: right mouse to look, WASD/QE to move, Shift to hurry, scroll to
	/// change the field of view (a narrow view is how you check a moon's real size), R to put it back.
	/// V swaps to walking: a figure with a player's movement put down where the camera is looking, WASD to walk
	/// it, Shift to sprint, Space to jump (held, to rise in water), C to crouch, dive or slide down a ladder, left click to
	/// punch; walking into a ladder or rope climbs it, with the camera following behind and scroll setting how far. In the editor, E while walking
	/// opens or shuts the door ahead (the sites' server-run doors, simulated here: no server) and F lights a torch.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The weather bed had a plainer version of this with no zoom and no floor under it, which meant
	/// the same fly-around behaved differently depending on which scene you happened to be in. This
	/// is the one that could do everything both needed.
	/// </para>
	/// <para>
	/// Walking is for the things flying hides: whether the ground is really standable, how a slope
	/// reads at eye height, where a rock's collision is not where its mesh is. The body is
	/// <see cref="WorldSimWalker"/>; this only reads the keys and places the camera behind it.
	/// </para>
	/// </remarks>
	public sealed class WorldSimCamera : MonoBehaviour
	{
		public float Speed = 10f;
		public float LookSpeed = 0.15f;
		[Tooltip("Metres above the ground under it that the camera never sinks below, so a fly-around cannot end up inside the terrain. From the ground, not from y = 0: y is altitude, so a scene cut from the sea floor has all its ground below zero and one cut from a plateau all of it far above.")]
		public float MinimumHeight = 0.5f;

		private static readonly System.Collections.Generic.List<Terrain> terrains = new System.Collections.Generic.List<Terrain>();
		public float DefaultFieldOfView = 60f;

		[Header("Walking")]
		[Tooltip("How far behind the walker the camera sits to begin with; scroll changes it.")]
		public float FollowDistance = 4f;
		public float MinFollowDistance = 1.2f;
		public float MaxFollowDistance = 30f;
		[Tooltip("Height above the walker's feet the camera orbits: about where its eyes would be.")]
		public float PivotHeight = 1.6f;
		[Tooltip("How far ahead of the camera, in metres, the walker is put down when walking starts. Past that, or looking at sky, it is put on the ground under the camera.")]
		public float PlaceReach = 150f;

		/// <summary>The camera is following the walker rather than flying.</summary>
		public bool Walking => walker != null && walker.isActiveAndEnabled;

		private const float CameraRadius = 0.25f;
		/// <summary>The share of the follow distance one wheel notch takes off or puts on, as a log step: about 15%, so 1.2 m to 30 m is some twenty notches.</summary>
		private const float FollowZoomPerNotch = 0.15f;
		/// <summary>Degrees of field of view one wheel notch narrows or widens.</summary>
		private const float FieldOfViewPerNotch = 2.5f;

		private float yaw;
		private float pitch;
		private WorldSimWalker walker;
		private float followDistance;
		private float shownDistance;
		private int solidMask;

		private void Start()
		{
			Vector3 euler = transform.eulerAngles;
			yaw = euler.y;
			pitch = euler.x > 180f ? euler.x - 360f : euler.x;
			followDistance = FollowDistance;
			int solid = LayerMask.GetMask("Default", "Ground");
			solidMask = solid != 0 ? solid : Physics.DefaultRaycastLayers;
		}

		private void OnDestroy()
		{
			if (walker != null)
			{
				Destroy(walker.gameObject);
			}
		}

		/// <summary>Turns the camera to face <paramref name="direction"/>, level; walking, it swings round behind the walker to look that way.</summary>
		public void Face(Vector3 direction)
		{
			if (direction.sqrMagnitude < 1e-6f)
			{
				return;
			}
			direction.Normalize();
			yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
			pitch = -Mathf.Asin(Mathf.Clamp(direction.y, -1f, 1f)) * Mathf.Rad2Deg;
			if (Walking)
			{
				pitch = Mathf.Clamp(pitch, -80f, 80f);
			}
			transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
		}

		/// <summary>Starts or stops walking. Starting puts the walker down where the camera is looking.</summary>
		public void SetWalking(bool walking)
		{
			if (walking == Walking)
			{
				return;
			}
			if (!walking)
			{
				// The camera stays where it was and flies on from there.
				walker.gameObject.SetActive(false);
				return;
			}
			if (walker == null)
			{
				walker = WorldSimWalker.Create(this);
			}
			walker.gameObject.SetActive(true);
			walker.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
			walker.PlaceAt(PlacePoint());
			pitch = Mathf.Clamp(pitch, -80f, 80f);
			followDistance = Mathf.Clamp(followDistance, MinFollowDistance, MaxFollowDistance);
			shownDistance = followDistance;
			Follow(0f);
		}

		private void Update()
		{
			Mouse mouse = Mouse.current;
			Keyboard keyboard = Keyboard.current;
			var camera = GetComponent<Camera>();
			bool walking = Walking;
			if (mouse != null)
			{
				if (mouse.rightButton.isPressed)
				{
					Vector2 delta = mouse.delta.ReadValue();
					yaw += delta.x * LookSpeed;
					float limit = walking ? 80f : 89f;
					pitch = Mathf.Clamp(pitch - delta.y * LookSpeed, -limit, limit);
					transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
				}
				float scroll = mouse.scroll.ReadValue().y;
				// Over the panel the wheel scrolls the panel. It used to do both.
				if (Mathf.Abs(scroll) > 0.01f && !WorldSimPanel.PointerOverPanel)
				{
					// Walking, the wheel is how far behind the camera sits; Ctrl keeps it the zoom. The delta arrives
					// normalised to about 1 a notch (the Input System's default ScrollDeltaBehavior), not the raw 120 these
					// rates were once sized for, which made a notch move the camera a thousandth of the way.
					bool zoom = !walking || (keyboard != null && keyboard.ctrlKey.isPressed);
					if (!zoom)
					{
						followDistance = Mathf.Clamp(followDistance * Mathf.Exp(-scroll * FollowZoomPerNotch), MinFollowDistance, MaxFollowDistance);
					}
					else if (camera != null)
					{
						camera.fieldOfView = Mathf.Clamp(camera.fieldOfView - scroll * FieldOfViewPerNotch, 8f, 90f);
					}
				}
			}
			// While a value is being typed the keys are digits and letters for the box, and R is not
			// a request to reset the zoom.
			bool typing = keyboard == null || WorldSimPanel.TypingInPanel;
			if (!typing && keyboard.vKey.wasPressedThisFrame)
			{
				SetWalking(!walking);
				walking = Walking;
			}
			if (walking)
			{
				Walk(typing ? null : keyboard);
			}
			else if (!typing)
			{
				Fly(keyboard);
			}
			// The camera's own rotation stays level: the sky turns, not the horizon.
			if (!typing && keyboard.rKey.wasPressedThisFrame)
			{
				if (camera != null)
				{
					camera.fieldOfView = DefaultFieldOfView;
				}
				followDistance = FollowDistance;
			}
		}

		private void Fly(Keyboard keyboard)
		{
			var move = Vector3.zero;
			if (keyboard.wKey.isPressed) move += Vector3.forward;
			if (keyboard.sKey.isPressed) move += Vector3.back;
			if (keyboard.aKey.isPressed) move += Vector3.left;
			if (keyboard.dKey.isPressed) move += Vector3.right;
			if (keyboard.eKey.isPressed) move += Vector3.up;
			if (keyboard.qKey.isPressed) move += Vector3.down;
			if (move.sqrMagnitude > 0f)
			{
				float speed = Speed * (keyboard.leftShiftKey.isPressed ? 4f : 1f);
				Vector3 position = transform.position + transform.TransformDirection(move.normalized) * speed * Time.deltaTime;
				position.y = AboveGround(gameObject.scene.GetPhysicsScene(), position, MinimumHeight, solidMask);
				transform.position = position;
			}
		}

		/// <summary>One frame of walking; a null keyboard (typing into the panel) stands still but keeps falling.</summary>
		private void Walk(Keyboard keyboard)
		{
			float forward = 0f;
			float right = 0f;
			bool sprint = false;
			bool jump = false;
			bool jumpHeld = false;
			bool crouch = false;
			if (keyboard != null)
			{
				if (keyboard.wKey.isPressed) forward += 1f;
				if (keyboard.sKey.isPressed) forward -= 1f;
				if (keyboard.aKey.isPressed) right -= 1f;
				if (keyboard.dKey.isPressed) right += 1f;
				sprint = keyboard.leftShiftKey.isPressed;
				jump = keyboard.spaceKey.wasPressedThisFrame;
				jumpHeld = keyboard.spaceKey.isPressed;
				crouch = keyboard.cKey.isPressed;
				if (keyboard.xKey.wasPressedThisFrame)
				{
					walker.ToggleSit();
				}
				// Left click punches; a click on the panel is the panel's.
				Mouse mouse = Mouse.current;
				if (mouse != null && mouse.leftButton.wasPressedThisFrame && !WorldSimPanel.PointerOverPanel)
				{
					walker.Punch();
				}
#if UNITY_EDITOR
				if (keyboard.eKey.wasPressedThisFrame)
				{
					UseDoor();
				}
				if (keyboard.fKey.wasPressedThisFrame)
				{
					ToggleTorch();
				}
#endif
			}
			// Relative to where the camera looks, as a player's input is: flattened on land, and in the water looking
			// down dives.
			walker.Move(forward, right, Quaternion.Euler(pitch, yaw, 0f), jump, jumpHeld, crouch, sprint, Time.deltaTime);
			Follow(Time.deltaTime);
#if UNITY_EDITOR
			FlickerTorch();
#endif
		}

#if UNITY_EDITOR
		/// <summary>The torch the walker carries (F): a warm point light held up beside its head, flickering.</summary>
		private Light torch;

		/// <summary>Lights or puts out the walker's torch, to see by in a cave, a mine or the night. Editor only.</summary>
		private void ToggleTorch()
		{
			if (torch == null)
			{
				var held = new GameObject("World Sim Torch") { hideFlags = HideFlags.DontSave };
				held.transform.SetParent(walker.transform, false);
				// Held up and a little forward of the right shoulder, so it lights the way and the walls beside it.
				// Clear of the body, or the figure's own shadow hides what the torch is for.
				held.transform.localPosition = new Vector3(0.3f, 1.4f, 0.4f);
				torch = held.AddComponent<Light>();
				torch.type = LightType.Point;
				torch.color = new Color(1f, 0.64f, 0.34f);
				torch.range = 11f;
				torch.shadows = LightShadows.Soft;
				torch.enabled = false;
			}
			torch.enabled = !torch.enabled;
		}

		/// <summary>A torch's flicker: three unrelated beats, as the site fires flicker (SiteLightPool).</summary>
		private void FlickerTorch()
		{
			if (torch == null || !torch.enabled)
			{
				return;
			}
			float t = Time.time;
			torch.intensity = 2.2f * (0.84f + 0.09f * Mathf.Sin(t * 11.3f) + 0.05f * Mathf.Sin(t * 17.9f + 1.3f) + 0.03f * Mathf.Sin(t * 5.1f + 2.9f));
		}

		/// <summary>How far round the walker a door is looked for, metres (a drawbridge's switch reaches 8).</summary>
		private const float DoorSearch = 9f;
		private readonly Collider[] doorHits = new Collider[64];

		/// <summary>
		/// Opens or shuts the door ahead of the walker, as a player's "Door Interact" would on the server: the nearest
		/// door, portcullis or drawbridge (an <see cref="ISwitch"/> whose target is the leaf's SwitchTargetMover) within
		/// its own interaction range (the server's rule, <see cref="IInteractable.InRange"/>) and in front rather than behind. Offline there is no tick to time it by, so
		/// the mover swings on frame time and goes solid or not as it would on the server (SwitchTargetMover's offline
		/// path). Editor only: the bed is not a way into the game's doors.
		/// </summary>
		private void UseDoor()
		{
			Vector3 eye = walker.Feet + Vector3.up * 1.6f;
			Vector3 facing = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
			int count = gameObject.scene.GetPhysicsScene().OverlapSphere(eye, DoorSearch, doorHits, ~0, QueryTriggerInteraction.Collide);
			ISwitchTarget best = null;
			float bestScore = float.MaxValue;
			for (int i = 0; i < count; i++)
			{
				Collider hit = doorHits[i];
				// Through the interfaces, not the Switch component itself (a FishNet NetworkBehaviour this bed does not reference).
				ISwitch use = hit != null ? hit.GetComponentInParent<ISwitch>() : null;
				ISwitchTarget target = use?.SwitchTarget;
				if (target == null || !use.InRange(walker.transform))
				{
					continue;
				}
				Vector3 at = hit.ClosestPoint(eye);
				float distance = Vector3.Distance(eye, at);
				Vector3 toward = at - eye;
				toward.y = 0f;
				float ahead = toward.sqrMagnitude < 1e-4f ? 1f : Vector3.Dot(toward.normalized, facing);
				if (ahead < 0.2f)
				{
					continue;
				}
				// Nearer and more squarely ahead wins.
				float score = distance * (2f - ahead);
				if (score < bestScore)
				{
					bestScore = score;
					best = target;
				}
			}
			if (best == null)
			{
				return;
			}
			if (best.IsActivated)
			{
				best.Deactivate(null);
			}
			else
			{
				best.Activate(null);
			}
		}
#endif

		/// <summary>
		/// Puts the camera behind the walker. Anything solid between them pulls it in at once; it
		/// eases back out, so passing a trunk does not snap the view in and out. The wheel's own changes ease both ways.
		/// </summary>
		private void Follow(float deltaTime)
		{
			Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
			// Riding the swell with the figure, not the still water line its capsule floats on.
			Vector3 pivot = walker.Feet + Vector3.up * (PivotHeight + (walker.Swimming ? walker.ShownLift : 0f));
			Vector3 back = rotation * Vector3.back;
			float clear = followDistance;
			bool blocked = false;
			if (gameObject.scene.GetPhysicsScene().SphereCast(pivot, CameraRadius, back, out RaycastHit hit, followDistance, solidMask, QueryTriggerInteraction.Ignore))
			{
				clear = Mathf.Max(0.1f, hit.distance);
				blocked = true;
			}
			shownDistance = (blocked && clear < shownDistance) || deltaTime <= 0f
				? clear
				: Mathf.Lerp(shownDistance, clear, 1f - Mathf.Exp(-6f * deltaTime));
			Vector3 position = pivot + back * shownDistance;
			position.y = AboveGround(gameObject.scene.GetPhysicsScene(), position, MinimumHeight * 0.5f, solidMask);
			transform.SetPositionAndRotation(position, rotation);
		}

		/// <summary>The ground the camera is looking at, if it is near and standable; otherwise the ground under the camera.</summary>
		private Vector3 PlacePoint()
		{
			PhysicsScene physics = gameObject.scene.GetPhysicsScene();
			if (physics.Raycast(transform.position, transform.forward, out RaycastHit seen, PlaceReach, solidMask, QueryTriggerInteraction.Ignore) && seen.normal.y > 0.5f)
			{
				return seen.point;
			}
			if (physics.Raycast(transform.position + Vector3.up, Vector3.down, out RaycastHit under, 20000f, solidMask, QueryTriggerInteraction.Ignore))
			{
				return under.point;
			}
			float ground = GroundBelow(transform.position);
			return new Vector3(transform.position.x, float.IsNegativeInfinity(ground) ? transform.position.y - 1.8f : ground, transform.position.z);
		}

		/// <summary>
		/// World Y of the terrain under a point; negative infinity off the edge of every tile, and over a hole.
		/// </summary>
		/// <remarks>
		/// <c>SampleHeight</c> reads the heightmap, which goes on under a painted hole (a cave mouth, a canyon wall's
		/// footing) although the terrain collider does not: the walker fell into the hole and its seam guard lifted it
		/// straight back onto the surface that is not there, every frame.
		/// </remarks>
		internal static float GroundBelow(Vector3 position)
		{
			return GroundBelow(position, out _);
		}

		/// <summary><see cref="GroundBelow(Vector3)"/>, and the tile it was read from (null where it is negative infinity).</summary>
		private static float GroundBelow(Vector3 position, out Terrain under)
		{
			under = null;
			Terrain.GetActiveTerrains(terrains);
			for (int i = 0; i < terrains.Count; i++)
			{
				Terrain terrain = terrains[i];
				if (terrain == null || terrain.terrainData == null)
				{
					continue;
				}
				Vector3 origin = terrain.GetPosition();
				Vector3 size = terrain.terrainData.size;
				if (position.x >= origin.x && position.x <= origin.x + size.x && position.z >= origin.z && position.z <= origin.z + size.z)
				{
					under = terrain;
					return IsHole(terrain.terrainData, (position.x - origin.x) / size.x, (position.z - origin.z) / size.z)
						? float.NegativeInfinity
						: origin.y + terrain.SampleHeight(position);
				}
			}
			return float.NegativeInfinity;
		}

		/// <summary>
		/// The height a point is held to, at least <paramref name="clearance"/> above the terrain under it as it is drawn (the
		/// snow lying on it included, <see cref="SnowLift"/>), unless the point is sheltered (<see cref="Sheltered"/>): then it
		/// is left where it is.
		/// </summary>
		/// <remarks>
		/// The floor was the heightmap alone, but the terrain shader lifts the ground under snow by its blanket and the deep
		/// snow past it, a metre and more: the camera went down through the white surface and looked up at it from inside.
		/// </remarks>
		private static float AboveGround(PhysicsScene physics, Vector3 position, float clearance, int mask)
		{
			float ground = GroundBelow(position, out Terrain under);
			if (float.IsNegativeInfinity(ground))
			{
				return position.y;
			}
			float surface = ground + SnowLift(under, new Vector3(position.x, ground, position.z));
			if (position.y >= surface + clearance || Sheltered(physics, position, ground, mask))
			{
				return position.y;
			}
			return surface + clearance;
		}

		private static readonly int SnowDepthProperty = Shader.PropertyToID("_FishSnowDepth");

		/// <summary>
		/// How high the drawn snow stands over the terrain at a point on it, metres, as the terrain shader lifts it
		/// (FishSnowLift / FishSnowLiftMetres): the tile's blanket where the cover is white, the deep snow past it, none on
		/// ground too steep to hold it, and none unless this quality tier lifts the ground and there is snow in the scene.
		/// Read from the cover map's own copy of what it publishes. Footprints and the sky's shelter are left out: both only
		/// lower the snow, so the camera errs above the surface, never under it.
		/// </summary>
		private static float SnowLift(Terrain terrain, Vector3 ground)
		{
			Material material = terrain != null ? terrain.materialTemplate : null;
			float blanket = material != null && material.HasFloat(SnowDepthProperty) ? material.GetFloat(SnowDepthProperty) : 0f;
			if (blanket <= 0f || Shader.GetGlobalVector(WeatherShaderGlobals.Tier).x <= 0f)
			{
				return 0f;
			}
			Vector4 everywhere = Shader.GetGlobalVector(WeatherShaderGlobals.Cover);
			if (everywhere.x <= 0f && Shader.GetGlobalVector(WeatherCoverMap.SnowLineId).z <= 0.5f)
			{
				return 0f;
			}
			WeatherCoverMap map = WeatherPresentation.Instance != null ? WeatherPresentation.Instance.CoverMap : null;
			float cover = map != null ? map.Sample(ground, new WeatherCover { Snow = everywhere.x }).Snow : everywhere.x;
			float deep = map != null ? map.SampleDeepSnow(ground) : 0f;
			float lift = Mathf.Clamp01(cover) * blanket + deep;
			if (lift <= 0f)
			{
				return 0f;
			}
			// FishCoverFacing at a slope bias of 0.5: all of it on the level, none past 60°.
			Vector3 size = terrain.terrainData.size;
			Vector3 origin = terrain.GetPosition();
			float up = terrain.terrainData.GetInterpolatedNormal((ground.x - origin.x) / size.x, (ground.z - origin.z) / size.z).y;
			float facing = Mathf.Clamp01((up - 0.5f) / 0.5f);
			return lift * facing * facing;
		}

		/// <summary>
		/// Whether a point under the terrain's surface is inside something rather than lost beneath the ground: a cave, the
		/// space under an overhang. True when the first thing above it, before the surface, is not the terrain.
		/// </summary>
		/// <remarks>
		/// The terrain's heightmap runs on over a cave's tunnel (only its mouth is holed), so "below <see cref="GroundBelow"/>"
		/// alone read every step into a cave as a fall through the ground: the walker's seam guard and the camera's floor
		/// both lifted it out onto the hillside above. A cave's shell faces inward, so a ray up from inside meets its roof.
		/// </remarks>
		internal static bool Sheltered(PhysicsScene physics, Vector3 position, float ground, int mask)
		{
			if (float.IsNegativeInfinity(ground) || position.y >= ground)
			{
				return false;
			}
			return physics.Raycast(position, Vector3.up, out RaycastHit roof, ground - position.y + 1f, mask, QueryTriggerInteraction.Ignore)
				&& !(roof.collider is TerrainCollider);
		}

		/// <summary>Whether the hole map has a hole at a point given as 0..1 across the tile (the hole texel it falls in).</summary>
		private static bool IsHole(TerrainData data, float u, float v)
		{
			int resolution = data.holesResolution;
			if (resolution <= 0)
			{
				return false;
			}
			int x = Mathf.Clamp(Mathf.FloorToInt(u * resolution), 0, resolution - 1);
			int z = Mathf.Clamp(Mathf.FloorToInt(v * resolution), 0, resolution - 1);
			return data.IsHole(x, z);
		}
	}
}
