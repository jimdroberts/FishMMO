using UnityEngine;
using UnityEngine.InputSystem;

namespace FishMMO.TestHarness.World
{
	/// <summary>
	/// The world test bed's camera: right mouse to look, WASD/QE to move, Shift to hurry, scroll to
	/// change the field of view (a narrow view is how you check a moon's real size), R to put it back.
	/// V swaps to walking: a capsule put down where the camera is looking, WASD to walk it, Shift to
	/// run, Space to jump, with the camera following behind and scroll setting how far.
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
					// Walking, the wheel is how far behind the camera sits; Ctrl keeps it the zoom.
					bool zoom = !walking || (keyboard != null && keyboard.ctrlKey.isPressed);
					if (!zoom)
					{
						followDistance = Mathf.Clamp(followDistance * Mathf.Exp(-scroll * 0.001f), MinFollowDistance, MaxFollowDistance);
					}
					else if (camera != null)
					{
						camera.fieldOfView = Mathf.Clamp(camera.fieldOfView - scroll * 0.02f, 8f, 90f);
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
			var wish = Vector3.zero;
			bool run = false;
			bool jump = false;
			if (keyboard != null)
			{
				if (keyboard.wKey.isPressed) wish += Vector3.forward;
				if (keyboard.sKey.isPressed) wish += Vector3.back;
				if (keyboard.aKey.isPressed) wish += Vector3.left;
				if (keyboard.dKey.isPressed) wish += Vector3.right;
				run = keyboard.leftShiftKey.isPressed;
				jump = keyboard.spaceKey.wasPressedThisFrame;
			}
			// Relative to where the camera faces, flattened: looking down at your feet still walks forward.
			wish = Quaternion.Euler(0f, yaw, 0f) * wish;
			walker.Move(wish, run, jump, Time.deltaTime);
			Follow(Time.deltaTime);
		}

		/// <summary>
		/// Puts the camera behind the walker. Anything solid between them pulls it in at once; it
		/// eases back out, so passing a trunk does not snap the view in and out.
		/// </summary>
		private void Follow(float deltaTime)
		{
			Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
			Vector3 pivot = walker.Feet + Vector3.up * PivotHeight;
			Vector3 back = rotation * Vector3.back;
			float clear = followDistance;
			if (gameObject.scene.GetPhysicsScene().SphereCast(pivot, CameraRadius, back, out RaycastHit hit, followDistance, solidMask, QueryTriggerInteraction.Ignore))
			{
				clear = Mathf.Max(0.1f, hit.distance);
			}
			shownDistance = clear < shownDistance || deltaTime <= 0f
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
					return IsHole(terrain.terrainData, (position.x - origin.x) / size.x, (position.z - origin.z) / size.z)
						? float.NegativeInfinity
						: origin.y + terrain.SampleHeight(position);
				}
			}
			return float.NegativeInfinity;
		}

		/// <summary>
		/// The height a point is held to, at least <paramref name="clearance"/> above the terrain under it, unless the point is
		/// sheltered (<see cref="Sheltered"/>): then it is left where it is.
		/// </summary>
		private static float AboveGround(PhysicsScene physics, Vector3 position, float clearance, int mask)
		{
			float ground = GroundBelow(position);
			if (float.IsNegativeInfinity(ground) || position.y >= ground + clearance || Sheltered(physics, position, ground, mask))
			{
				return position.y;
			}
			return ground + clearance;
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
