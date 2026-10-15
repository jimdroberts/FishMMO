using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FishMMO.TestHarness.World
{
	/// <summary>
	/// What the walker looks like: the test character, animated by what the walker is doing (standing, walking,
	/// running, falling, crouching, treading water, swimming, climbing) at the speed it is really doing it, with
	/// punches over the top; or, outside the editor or with the model missing, a plain capsule the size of its collider.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Only the look. The walker's capsule collider is still what stands, climbs and collides; the model
	/// is a child set down with its soles on the capsule's base and has no collider of its own.
	/// </para>
	/// <para>
	/// <b>A PlayableGraph, not an AnimatorController.</b> Idle, walk, run, fall and crouch are the Standard Assets
	/// humanoid clips beside Ethan; swim, tread, climb and the punches were keyframed on this model's own skeleton
	/// and ship inside its FBX (TestHumanImport names and loops them). All are humanoid clips, so Mecanim retargets
	/// them onto any humanoid. A graph built here has no controller asset to keep in step with it. One mixer holds
	/// every state's clips, their weights eased toward the state's; the punches are a layer over it that moves the
	/// arms only, so a punch thrown walking keeps the legs walking.
	/// </para>
	/// <para>
	/// <b>Speed-matched, so the feet do not skate.</b> Under the walk clip's own speed idle fades into
	/// walk; between that and the run clip's speed the two are blended with their strides locked to one
	/// phase, as a blend tree locks them, or the two clips' feet disagree mid-step; past the run clip's
	/// speed the run plays faster. The walker's walk (4.5 m/s) is already a jog by the clips' measure.
	/// </para>
	/// </remarks>
	public sealed class WorldSimWalkerBody : MonoBehaviour
	{
		public const string ModelPath = "Assets/TestHarness/World/Character/TestHuman.fbx";
		public const string BaseMapPath = "Assets/TestHarness/World/Character/TestHuman_BaseColor.jpg";
		private const string ClipFolder = "Assets/Prefabs/Client/Models/EthanCharacter/Animation/";

		// The locomotion mixer's inputs.
		private const int Idle = 0;
		private const int Walk = 1;
		private const int Run = 2;
		private const int Fall = 3;
		private const int CrouchIdle = 4;
		private const int CrouchWalk = 5;
		private const int Tread = 6;
		private const int Crawl = 7;
		private const int Climb = 8;
		private const int Inputs = 9;
		/// <summary>Metres climbed per climb cycle (a hand and the opposite foot each way, two rungs).</summary>
		private const float ClimbCycleMetres = 1.2f;
		/// <summary>The swim speed the crawl was keyed for, metres a second (a player swims 2.0, sprints 3.2).</summary>
		private const float CrawlPace = 1.8f;
		/// <summary>Seconds a punch takes to come up from the arms' own pose.</summary>
		private const float PunchIn = 0.06f;
		/// <summary>How quickly the blend follows the walker's speed and footing, per second.</summary>
		private const float BlendRate = 10f;
		/// <summary>Seconds off the ground before it falls: a step down a kerb is not a jump.</summary>
		private const float FallDelay = 0.15f;
		/// <summary>The fastest the run is played, as a multiple of its own pace.</summary>
		private const float MaxRunRate = 2f;

		private PlayableGraph graph;
		private AnimationMixerPlayable mixer;
		private AnimationLayerMixerPlayable layers;
		private readonly AnimationClipPlayable[] clips = new AnimationClipPlayable[Inputs];
		private readonly float[] weights = new float[Inputs];
		private readonly float[] targets = new float[Inputs];
		private AnimationMixerPlayable punchMixer;
		private readonly AnimationClipPlayable[] punches = new AnimationClipPlayable[2];
		private AvatarMask armsMask;
		private bool canPunch;
		private float punchLength;
		/// <summary>Seconds into the punch being thrown; negative when none is.</summary>
		private float punchTime = -1f;
		private int punchSide;
		private float crouchWalkSpeed;
		private float climbPhase;
		private float walkSpeed;
		private float runSpeed;
		private float walkLength;
		private float runLength;
		private float phase;
		private float speed;
		private float airborne;
		private Material material;

		/// <summary>How quickly the feet are eased onto the ground, per second.</summary>
		private const float GroundRate = 12f;
		/// <summary>The most the body is ever raised or lowered to find the ground, metres.</summary>
		private const float MaxLift = 0.5f;
		/// <summary>How far above and below a sole the ground is looked for, metres.</summary>
		private const float GroundReach = 0.6f;
		private Transform shown;
		private Vector3 shownRest;
		private float lift;
		private bool grounded;
		private int groundMask;
		private readonly Transform[] feet = new Transform[2];
		private readonly Transform[] toes = new Transform[2];
		/// <summary>Each foot's and toe's height above its sole in the bind pose.</summary>
		private readonly float[] ankleRest = new float[2];
		private readonly float[] toeRest = new float[2];

		/// <summary>How tall the figure stands, metres: its own height, not the capsule's (a player's is 1.6 m, and a head
		/// stands above it).</summary>
		private const float FigureHeight = 1.8f;
		/// <summary>The height, metres above the soles, the figure turns about to lie along its swim.</summary>
		private const float SwimPivot = 1.1f;
		private float pitch;
		private bool swimming;
		private bool climbing;

		/// <summary>Dresses <paramref name="walker"/>: the character in the editor when it is there, else the capsule.</summary>
		public static WorldSimWalkerBody Dress(WorldSimWalker walker)
		{
			var body = walker.gameObject.AddComponent<WorldSimWalkerBody>();
#if UNITY_EDITOR
			if (body.TryCharacter(walker))
			{
				return body;
			}
#endif
			body.Capsule(walker);
			return body;
		}

		/// <summary>
		/// One frame of the walker's motion: <paramref name="velocity"/> is how far it really went this
		/// frame over the frame's length, not how fast it was asked to go, so walking into a wall stands still.
		/// </summary>
		public void Pose(Vector3 velocity, bool grounded, bool swimming, bool climbing, bool crouching, float deltaTime)
		{
			if (!graph.IsValid() || deltaTime <= 0f)
			{
				return;
			}
			float follow = 1f - Mathf.Exp(-BlendRate * deltaTime);
			this.swimming = swimming;
			this.climbing = climbing;
			float flat = new Vector2(velocity.x, velocity.z).magnitude;
			float measure = climbing ? Mathf.Abs(velocity.y) : swimming ? velocity.magnitude : flat;
			speed = Mathf.Lerp(speed, measure, follow);
			// Along its swim: level swimming lies flat (90°), diving tips past it, rising stands it up; still, it treads water upright.
			float swimPitch = 0f;
			if (swimming)
			{
				float elevation = Mathf.Atan2(velocity.y, Mathf.Max(flat, 1e-3f)) * Mathf.Rad2Deg;
				swimPitch = Mathf.Clamp01(velocity.magnitude / 0.8f) * Mathf.Clamp(90f - elevation, 10f, 170f);
			}
			pitch = Mathf.Lerp(pitch, swimPitch, 1f - Mathf.Exp(-0.5f * BlendRate * deltaTime));
			this.grounded = grounded;
			airborne = grounded || swimming || climbing ? 0f : airborne + deltaTime;

			System.Array.Clear(targets, 0, Inputs);
			if (climbing)
			{
				// Stepped by the height climbed, so the hands keep to the rungs and a climber holding still holds still.
				targets[Climb] = 1f;
				climbPhase = Mathf.Repeat(climbPhase + speed * deltaTime / ClimbCycleMetres, 1f);
				clips[Climb].SetTime(climbPhase * Length(Climb));
			}
			else if (swimming)
			{
				// Treading water, into the crawl as it gets going.
				float stroke = Mathf.Clamp01(speed / 0.8f);
				targets[Tread] = 1f - stroke;
				targets[Crawl] = stroke;
				clips[Crawl].SetSpeed(Mathf.Clamp(speed / CrawlPace, 0.6f, 1.6f));
			}
			else if (airborne > FallDelay)
			{
				targets[Fall] = 1f;
			}
			else if (crouching)
			{
				float share = Mathf.Clamp01(speed / crouchWalkSpeed);
				targets[CrouchIdle] = 1f - share;
				targets[CrouchWalk] = share;
				clips[CrouchWalk].SetSpeed(Mathf.Clamp(speed / crouchWalkSpeed, 0.5f, 2f));
			}
			else
			{
				float rate = 1f;
				float running = 0f;
				if (speed < walkSpeed)
				{
					targets[Walk] = speed / walkSpeed;
					targets[Idle] = 1f - targets[Walk];
				}
				else if (speed < runSpeed)
				{
					running = (speed - walkSpeed) / (runSpeed - walkSpeed);
					targets[Run] = running;
					targets[Walk] = 1f - running;
				}
				else
				{
					running = 1f;
					targets[Run] = 1f;
					rate = Mathf.Min(speed / runSpeed, MaxRunRate);
				}
				// One stride shared by both clips, its length the blend of theirs.
				phase = Mathf.Repeat(phase + deltaTime * rate / Mathf.Lerp(walkLength, runLength, running), 1f);
				clips[Walk].SetTime(phase * walkLength);
				clips[Run].SetTime(phase * runLength);
			}
			// Every weight eases toward its state's, and together they always come to one.
			float sum = 0f;
			for (int i = 0; i < Inputs; i++)
			{
				weights[i] = Mathf.Lerp(weights[i], targets[i], follow);
				sum += weights[i];
			}
			for (int i = 0; i < Inputs; i++)
			{
				mixer.SetInputWeight(i, sum > 1e-5f ? weights[i] / sum : (i == Idle ? 1f : 0f));
			}
			PunchStep(deltaTime);
		}

		/// <summary>
		/// Throws a punch, the right and the left by turns. Not swimming or climbing (the hands are busy), and not while
		/// the last is still going out: the next follows once it is on its way back, so a held rhythm is a combination.
		/// </summary>
		public void Punch()
		{
			if (!canPunch || swimming || climbing || (punchTime >= 0f && punchTime < punchLength * 0.55f))
			{
				return;
			}
			punchSide = 1 - punchSide;
			punchTime = 0f;
		}

		private void PunchStep(float deltaTime)
		{
			if (!canPunch)
			{
				return;
			}
			float weight = 0f;
			if (punchTime >= 0f)
			{
				punchTime += deltaTime;
				if (punchTime >= punchLength || swimming || climbing)
				{
					punchTime = -1f;
				}
				else
				{
					punchMixer.SetInputWeight(punchSide, 1f);
					punchMixer.SetInputWeight(1 - punchSide, 0f);
					punches[punchSide].SetTime(punchTime);
					// Up from the arms' own pose at once, and back into it over the last quarter.
					weight = Mathf.Clamp01(punchTime / PunchIn) * Mathf.Clamp01((punchLength - punchTime) / (punchLength * 0.25f));
				}
			}
			layers.SetInputWeight(1, weight);
		}

		/// <summary>Stood still where it was put down, so a jump across the map does not arrive running.</summary>
		public void Settle()
		{
			speed = 0f;
			airborne = 0f;
			lift = 0f;
			pitch = 0f;
			punchTime = -1f;
			climbPhase = 0f;
			sincePlaced = 0f;
			reported = false;
			System.Array.Clear(weights, 0, Inputs);
			weights[Idle] = 1f;
		}

		/// <summary>
		/// Sets the feet on the ground, after the animation has posed them. Placing the model by its bind pose is not enough:
		/// the idle keeps the hip height of the body it was captured on (another build's legs), the walker's capsule
		/// hovers its skin width above what it stands on, and on a slope the capsule's lowest point is above the ground
		/// under its middle. So each frame the lower of the two soles is found against the ground beneath it and the body
		/// eased up or down to meet it. In the air it holds where it was, so a landing does not pop; swimming or climbing it
		/// eases back to the capsule, which the controller already holds at the water line or on the rungs.
		/// </summary>
		private void LateUpdate()
		{
			float deltaTime = Time.deltaTime;
			if (shown == null || feet[0] == null || feet[1] == null || deltaTime <= 0f)
			{
				return;
			}
			if (grounded)
			{
				PhysicsScene physics = gameObject.scene.GetPhysicsScene();
				float gap = float.MaxValue;
				for (int i = 0; i < 2; i++)
				{
					Vector3 foot = feet[i].position;
					float sole = foot.y - ankleRest[i];
					if (toes[i] != null)
					{
						sole = Mathf.Min(sole, toes[i].position.y - toeRest[i]);
					}
					bool found = physics.Raycast(new Vector3(foot.x, sole + GroundReach, foot.z), Vector3.down, out RaycastHit hit, GroundReach * 2f, groundMask, QueryTriggerInteraction.Ignore);
					if (found)
					{
						gap = Mathf.Min(gap, sole - hit.point.y);
					}
					footNotes[i] = found
						? $"sole {sole:F3}, ground {hit.point.y:F3} on '{hit.collider.name}' (layer {LayerMask.LayerToName(hit.collider.gameObject.layer)}), bone {foot.y:F3}, rest {ankleRest[i]:F3}"
						: $"sole {sole:F3}, no ground within {GroundReach:F1} m, bone {foot.y:F3}, rest {ankleRest[i]:F3}";
				}
				if (gap < float.MaxValue)
				{
					float target = Mathf.Clamp(lift - gap, -MaxLift, MaxLift);
					lift = Mathf.Lerp(lift, target, 1f - Mathf.Exp(-GroundRate * deltaTime));
				}
			}
			else if (swimming || climbing)
			{
				lift = Mathf.Lerp(lift, 0f, 1f - Mathf.Exp(-GroundRate * deltaTime));
			}
			// Turned about the chest, not the soles, so lying along a swim keeps it in the water.
			Quaternion turn = Quaternion.Euler(pitch, 0f, 0f);
			Vector3 pivot = Vector3.up * SwimPivot;
			shown.localRotation = turn;
			shown.localPosition = shownRest + Vector3.up * lift + pivot - turn * pivot;
			Report(deltaTime);
		}

		// ── Footing diagnostics (the figure stood a foot off the ground with no word of why) ──
		private readonly string[] footNotes = { "not measured", "not measured" };
		private float sincePlaced;
		private bool reported;
		private float pinnedFor;

		/// <summary>
		/// One line, two seconds after the walker is put down, with everything its footing rests on: whether it counts as
		/// grounded, where its feet and the ground under them are and what that ground is, and the lift the grounding
		/// settled on. And a warning when the lift sits at its limit, which means the grounding is being asked for more
		/// than it may give.
		/// </summary>
		private void Report(float deltaTime)
		{
			sincePlaced += deltaTime;
			if (!reported && sincePlaced >= 2f)
			{
				reported = true;
				Vector3 at = transform.position;
				bool under = gameObject.scene.GetPhysicsScene().Raycast(at + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 3f, groundMask, QueryTriggerInteraction.Ignore);
				Debug.Log($"[World Sim] Walker footing: grounded {grounded}, swimming {swimming}, climbing {climbing}; feet (capsule base) {at.y:F3}, "
					+ (under ? $"ground under it {hit.point.y:F3} on '{hit.collider.name}'" : "no ground under it within 2.5 m")
					+ $"; lift {lift:F3}, model rest {shownRest.y:F3}; left foot: {footNotes[0]}; right foot: {footNotes[1]}.");
			}
			pinnedFor = Mathf.Abs(lift) >= MaxLift - 1e-3f ? pinnedFor + deltaTime : 0f;
			if (pinnedFor >= 1f && pinnedFor - deltaTime < 1f)
			{
				Debug.LogWarning($"[World Sim] Walker footing: the lift has sat at its limit ({lift:F2} m) for a second; left foot: {footNotes[0]}; right foot: {footNotes[1]}.");
			}
		}

		private void OnEnable()
		{
			// Hiding the walker stops its graph with it; showing it again starts the graph where it left off.
			if (graph.IsValid())
			{
				graph.Play();
			}
		}

		private void OnDestroy()
		{
			if (graph.IsValid())
			{
				graph.Destroy();
			}
			if (material != null)
			{
				Destroy(material);
			}
			if (armsMask != null)
			{
				Destroy(armsMask);
			}
		}

		private void Capsule(WorldSimWalker walker)
		{
			GameObject shape = GameObject.CreatePrimitive(PrimitiveType.Capsule);
			// The walker's own capsule does the colliding.
			Collider own = shape.GetComponent<Collider>();
			own.enabled = false;
			Destroy(own);
			shape.name = "Body";
			shape.layer = gameObject.layer;
			shape.transform.SetParent(transform, false);
			// The primitive is 2 m by 1 m about its middle; the walker's pivot is its feet.
			float round = walker.Radius * 2f;
			shape.transform.localScale = new Vector3(round, walker.Height * 0.5f, round);
			shape.transform.localPosition = Vector3.up * (walker.Height * 0.5f);
		}

#if UNITY_EDITOR
		private bool TryCharacter(WorldSimWalker walker)
		{
			var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
			var set = new ClipSet
			{
				Idle = LoadClip(ClipFolder + "HumanoidIdle.fbx", "HumanoidIdle"),
				Walk = LoadClip(ClipFolder + "HumanoidWalk.fbx", "HumanoidWalk"),
				Run = LoadClip(ClipFolder + "HumanoidRun.fbx", "HumanoidRun"),
				Fall = LoadClip(ClipFolder + "HumanoidMidAir.fbx", "HumanoidMidAirLeft"),
				CrouchIdle = LoadClip(ClipFolder + "HumanoidCrouch.fbx", "HumanoidCrouchIdle"),
				CrouchWalk = LoadClip(ClipFolder + "HumanoidCrouch.fbx", "HumanoidCrouchWalk"),
				// The model's own, keyframed on its skeleton.
				Tread = LoadClip(ModelPath, "SwimTread"),
				Crawl = LoadClip(ModelPath, "SwimCrawl"),
				Climb = LoadClip(ModelPath, "Climb"),
				PunchRight = LoadClip(ModelPath, "PunchRight"),
				PunchLeft = LoadClip(ModelPath, "PunchLeft"),
			};
			if (model == null || set.Idle == null || set.Walk == null || set.Run == null || set.Fall == null)
			{
				Debug.LogWarning($"[World Sim] The walker's character ({ModelPath}) or the humanoid clips in {ClipFolder} are missing; walking as a capsule.");
				return false;
			}

			GameObject shape = Instantiate(model, transform, false);
			shape.name = "Body";
			Animator animator = shape.GetComponent<Animator>();
			if (animator == null || animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)
			{
				Debug.LogWarning($"[World Sim] {ModelPath} has no valid Humanoid avatar; walking as a capsule. Reimport it: TestHumanImport sets it up as a Humanoid.");
				shape.SetActive(false);
				Destroy(shape);
				return false;
			}
			foreach (Transform part in shape.GetComponentsInChildren<Transform>(true))
			{
				part.gameObject.layer = gameObject.layer;
			}
			Stand(shape.transform, walker);
			Footing(shape.transform, animator, walker);
			Clothe(shape);
			if (set.Tread == null || set.Crawl == null || set.Climb == null || set.CrouchIdle == null || set.CrouchWalk == null)
			{
				Debug.LogWarning($"[World Sim] Some of the walker's swim, climb or crouch clips are missing ({ModelPath} not reimported?); it walks for them meanwhile.");
			}
			Animate(animator, set);
			return true;
		}

		/// <summary>
		/// Sized and set down by its bind pose: <see cref="FigureHeight"/> tall, soles on the capsule's base. The
		/// model is exported at 1.8 m with its origin between its feet, so this normally changes
		/// nothing; it is here so a model of any size or pivot stands right without re-exporting it.
		/// </summary>
		/// <remarks>
		/// Measured from the meshes as they were modelled (their rest pose), never from the renderers' bounds: the
		/// importer sizes a skinned renderer's bounds to hold every clip in its file, and this one's carry arms overhead
		/// (crawl, climb) and pointed toes (the kick). By those the figure was shrunk to fit and stood a foot up on toes
		/// it does not have at rest, and the grounding, measured from the same false sole, held it there.
		/// </remarks>
		private static void Stand(Transform shape, WorldSimWalker walker)
		{
			if (!RestExtent(shape, out float bottom, out float top))
			{
				return;
			}
			if (top - bottom > 0.01f)
			{
				shape.localScale *= FigureHeight / (top - bottom);
				RestExtent(shape, out bottom, out top);
			}
			shape.position += Vector3.up * (walker.Feet.y - bottom);
		}

		/// <summary>The feet LateUpdate grounds, and how high each bone stands above its sole in the bind pose (stood on the capsule's base).</summary>
		private void Footing(Transform shape, Animator animator, WorldSimWalker walker)
		{
			shown = shape;
			shownRest = shape.localPosition;
			int solid = LayerMask.GetMask("Default", "Ground");
			groundMask = (solid != 0 ? solid : Physics.DefaultRaycastLayers) & ~(1 << gameObject.layer);
			feet[0] = Bone(animator, shape, HumanBodyBones.LeftFoot);
			feet[1] = Bone(animator, shape, HumanBodyBones.RightFoot);
			toes[0] = Bone(animator, shape, HumanBodyBones.LeftToes);
			toes[1] = Bone(animator, shape, HumanBodyBones.RightToes);
			if (feet[0] == null || feet[1] == null)
			{
				Debug.LogWarning($"[World Sim] The walker's feet were not found in {ModelPath}'s avatar; its soles are not set on the ground.");
			}
			float sole = walker.Feet.y;
			for (int i = 0; i < 2; i++)
			{
				ankleRest[i] = feet[i] != null ? feet[i].position.y - sole : 0f;
				toeRest[i] = toes[i] != null ? toes[i].position.y - sole : 0f;
			}
			/* At rest the two feet stand alike. When they do not, the model came in posed: Unity takes a model's default
			 * pose from its file's first take, and builds its Humanoid avatar from that pose too, so every clip is
			 * retargeted through it. A climb's first frame did that once (a knee up): the right foot never reached the
			 * ground, and this grounding, measured from it, held the figure a quarter metre up. */
			if (feet[0] != null && feet[1] != null && Mathf.Abs(ankleRest[0] - ankleRest[1]) > 0.03f)
			{
				Debug.LogWarning($"[World Sim] {ModelPath} is not at rest as imported (its feet stand {ankleRest[0]:F2} and {ankleRest[1]:F2} m up): its first take must be its rest pose, or the avatar and the grounding are built from a posed figure.");
			}
		}

		/// <summary>
		/// A bone by the avatar's own map of it (its human description), not by <see cref="Animator.GetBoneTransform"/>:
		/// that answers null until the Animator is initialised, which one with no controller and no graph yet is not.
		/// Asked straight after the model was made, it gave no feet, and the grounding never ran: the figure stood as high
		/// as the idle held its hips, a hand's breadth over the ground.
		/// </summary>
		private static Transform Bone(Animator animator, Transform root, HumanBodyBones bone)
		{
			string human = HumanTrait.BoneName[(int)bone];
			foreach (HumanBone mapped in animator.avatar.humanDescription.human)
			{
				if (mapped.humanName == human)
				{
					Transform found = Named(root, mapped.boneName);
					if (found != null)
					{
						return found;
					}
				}
			}
			return animator.isInitialized ? animator.GetBoneTransform(bone) : null;
		}

		private static Transform Named(Transform parent, string name)
		{
			if (parent.name == name)
			{
				return parent;
			}
			for (int i = 0; i < parent.childCount; i++)
			{
				Transform found = Named(parent.GetChild(i), name);
				if (found != null)
				{
					return found;
				}
			}
			return null;
		}

		/// <summary>
		/// The lowest and highest world height of the figure at rest: each mesh's own bounds (its vertices as modelled),
		/// placed by its renderer's transform, which is where a skinned mesh draws while its bones stand in their bind pose.
		/// </summary>
		private static bool RestExtent(Transform shape, out float bottom, out float top)
		{
			bottom = float.MaxValue;
			top = float.MinValue;
			foreach (Renderer renderer in shape.GetComponentsInChildren<Renderer>())
			{
				Mesh mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh
					: renderer.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
				if (mesh == null)
				{
					continue;
				}
				Bounds local = mesh.bounds;
				Matrix4x4 toWorld = renderer.transform.localToWorldMatrix;
				for (int corner = 0; corner < 8; corner++)
				{
					Vector3 point = local.center + Vector3.Scale(local.extents,
						new Vector3((corner & 1) != 0 ? 1f : -1f, (corner & 2) != 0 ? 1f : -1f, (corner & 4) != 0 ? 1f : -1f));
					float y = toWorld.MultiplyPoint3x4(point).y;
					bottom = Mathf.Min(bottom, y);
					top = Mathf.Max(top, y);
				}
			}
			return bottom < top;
		}

		/// <summary>The weather-lit surface the rest of the bed uses, so the character wets in rain as the ground does.</summary>
		private void Clothe(GameObject shape)
		{
			Shader shader = Shader.Find("FishMMO/Weather Lit");
			if (shader == null)
			{
				shader = Shader.Find("Universal Render Pipeline/Lit");
			}
			if (shader == null)
			{
				return;
			}
			material = new Material(shader) { name = "Test Human" };
			material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(BaseMapPath));
			material.SetFloat("_Smoothness", 0.3f);
			/* Its sky light as the ground's and the rocks' is: from the light probes where the scene has some, and from the
			 * scene's ambient where it has none, as the terrain's own renderers choose (TerrainDetailInstancing). A skinned
			 * mesh blends probes by default, and the world scenes carry none: its shaded side took no sky at all, and with the
			 * sun behind it the figure stood black against lit ground. */
			LightProbes probes = LightmapSettings.lightProbes;
			LightProbeUsage usage = probes != null && probes.count > 0 ? LightProbeUsage.BlendProbes : LightProbeUsage.Off;
			foreach (Renderer renderer in shape.GetComponentsInChildren<Renderer>())
			{
				renderer.sharedMaterial = material;
				renderer.lightProbeUsage = usage;
			}
		}

		private static AnimationClip LoadClip(string path, string name)
		{
			foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
			{
				if (asset is AnimationClip clip && clip.name == name)
				{
					return clip;
				}
			}
			return null;
		}
#endif

		/// <summary>The clips the body plays; any but idle, walk, run and fall may be missing.</summary>
		private sealed class ClipSet
		{
			public AnimationClip Idle, Walk, Run, Fall, CrouchIdle, CrouchWalk, Tread, Crawl, Climb, PunchRight, PunchLeft;
		}

		private void Animate(Animator animator, ClipSet set)
		{
			// The walker moves itself; the clips' own travel is thrown away.
			animator.applyRootMotion = false;
			animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
			animator.runtimeAnimatorController = null;

			graph = PlayableGraph.Create("World Sim Walker");
			graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
			mixer = AnimationMixerPlayable.Create(graph, Inputs);
			canPunch = set.PunchRight != null && set.PunchLeft != null;
			layers = AnimationLayerMixerPlayable.Create(graph, canPunch ? 2 : 1);
			graph.Connect(mixer, 0, layers, 0);
			layers.SetInputWeight(0, 1f);
			AnimationPlayableOutput.Create(graph, "Body", animator).SetSourcePlayable(layers);

			// A missing clip is stood in for by the nearest there is: a crouch by standing, a swim and a climb by walking.
			Connect(Idle, set.Idle);
			Connect(Walk, set.Walk);
			Connect(Run, set.Run);
			Connect(Fall, set.Fall);
			Connect(CrouchIdle, set.CrouchIdle != null ? set.CrouchIdle : set.Idle);
			Connect(CrouchWalk, set.CrouchWalk != null ? set.CrouchWalk : set.Walk);
			Connect(Tread, set.Tread != null ? set.Tread : set.Idle);
			Connect(Crawl, set.Crawl != null ? set.Crawl : set.Walk);
			Connect(Climb, set.Climb != null ? set.Climb : set.Walk);
			// Walk, run and climb are stepped by Pose (a shared stride; the height climbed); the rest play on by themselves.
			clips[Walk].SetSpeed(0d);
			clips[Run].SetSpeed(0d);
			clips[Climb].SetSpeed(0d);

			if (canPunch)
			{
				punchMixer = AnimationMixerPlayable.Create(graph, 2);
				punches[0] = AnimationClipPlayable.Create(graph, set.PunchRight);
				punches[1] = AnimationClipPlayable.Create(graph, set.PunchLeft);
				for (int i = 0; i < 2; i++)
				{
					punches[i].SetSpeed(0d);
					graph.Connect(punches[i], 0, punchMixer, i);
				}
				graph.Connect(punchMixer, 0, layers, 1);
				// The arms only: the legs and the body go on with whatever they were doing.
				armsMask = new AvatarMask { name = "World Sim Walker arms" };
				for (AvatarMaskBodyPart part = 0; part < AvatarMaskBodyPart.LastBodyPart; part++)
				{
					armsMask.SetHumanoidBodyPartActive(part, part == AvatarMaskBodyPart.LeftArm || part == AvatarMaskBodyPart.RightArm
						|| part == AvatarMaskBodyPart.LeftFingers || part == AvatarMaskBodyPart.RightFingers);
				}
				layers.SetLayerMaskFromAvatarMask(1, armsMask);
				layers.SetInputWeight(1, 0f);
				punchLength = Mathf.Max(0.1f, Mathf.Min(set.PunchRight.length, set.PunchLeft.length));
			}

			walkSpeed = NaturalSpeed(set.Walk, animator.humanScale, 1.5f);
			runSpeed = Mathf.Max(NaturalSpeed(set.Run, animator.humanScale, 4f), walkSpeed + 0.5f);
			crouchWalkSpeed = NaturalSpeed(set.CrouchWalk != null ? set.CrouchWalk : set.Walk, animator.humanScale, 1.2f);
			walkLength = Mathf.Max(set.Walk.length, 0.1f);
			runLength = Mathf.Max(set.Run.length, 0.1f);
			weights[Idle] = 1f;
			mixer.SetInputWeight(Idle, 1f);
			graph.Play();
		}

		private void Connect(int input, AnimationClip clip)
		{
			AnimationClipPlayable playable = AnimationClipPlayable.Create(graph, clip);
			graph.Connect(playable, 0, mixer, input);
			mixer.SetInputWeight(input, 0f);
			clips[input] = playable;
		}

		private float Length(int input) => Mathf.Max(0.1f, clips[input].GetAnimationClip().length);

		/// <summary>
		/// The ground speed a clip was captured at, in metres per second on this body. A humanoid clip's
		/// root travel is stored for a body of human scale one, so it is scaled by this one's.
		/// </summary>
		private static float NaturalSpeed(AnimationClip clip, float humanScale, float fallback)
		{
			Vector3 average = clip.averageSpeed;
			float flat = new Vector2(average.x, average.z).magnitude * humanScale;
			return flat > 0.1f ? flat : fallback;
		}
	}
}
