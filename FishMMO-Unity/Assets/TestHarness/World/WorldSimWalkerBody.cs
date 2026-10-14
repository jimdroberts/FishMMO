using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FishMMO.TestHarness.World
{
	/// <summary>
	/// What the walker looks like: the test character, playing the stock humanoid idle, walk, run and
	/// fall clips at the speed the walker is really moving; or, outside the editor or with the model
	/// missing, a plain capsule the size of its collider.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Only the look. The walker's capsule collider is still what stands, climbs and collides; the model
	/// is a child set down with its soles on the capsule's base and has no collider of its own.
	/// </para>
	/// <para>
	/// <b>A PlayableGraph, not an AnimatorController.</b> The clips are the Standard Assets humanoid ones
	/// beside Ethan, retargeted by Mecanim onto the model's avatar (it is imported Humanoid by
	/// <c>TestHumanImport</c>). A graph built here has no controller asset to keep in step with it.
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

		private const int Idle = 0;
		private const int Walk = 1;
		private const int Run = 2;
		private const int Fall = 3;
		/// <summary>How quickly the blend follows the walker's speed and footing, per second.</summary>
		private const float BlendRate = 10f;
		/// <summary>Seconds off the ground before it falls: a step down a kerb is not a jump.</summary>
		private const float FallDelay = 0.15f;
		/// <summary>The fastest the run is played, as a multiple of its own pace.</summary>
		private const float MaxRunRate = 2f;

		private PlayableGraph graph;
		private AnimationMixerPlayable mixer;
		private AnimationClipPlayable walk;
		private AnimationClipPlayable run;
		private float walkSpeed;
		private float runSpeed;
		private float walkLength;
		private float runLength;
		private float phase;
		private float speed;
		private float airborne;
		private float falling;
		private Material material;

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
		public void Pose(Vector3 velocity, bool grounded, float deltaTime)
		{
			if (!graph.IsValid() || deltaTime <= 0f)
			{
				return;
			}
			float follow = 1f - Mathf.Exp(-BlendRate * deltaTime);
			velocity.y = 0f;
			speed = Mathf.Lerp(speed, velocity.magnitude, follow);
			airborne = grounded ? 0f : airborne + deltaTime;
			falling = Mathf.Lerp(falling, airborne > FallDelay ? 1f : 0f, follow);

			float idle = 0f;
			float walking = 0f;
			float running = 0f;
			float rate = 1f;
			if (speed < walkSpeed)
			{
				walking = speed / walkSpeed;
				idle = 1f - walking;
			}
			else if (speed < runSpeed)
			{
				running = (speed - walkSpeed) / (runSpeed - walkSpeed);
				walking = 1f - running;
			}
			else
			{
				running = 1f;
				rate = Mathf.Min(speed / runSpeed, MaxRunRate);
			}
			// One stride shared by both clips, its length the blend of theirs.
			phase = Mathf.Repeat(phase + deltaTime * rate / Mathf.Lerp(walkLength, runLength, running), 1f);
			walk.SetTime(phase * walkLength);
			run.SetTime(phase * runLength);

			float ground = 1f - falling;
			mixer.SetInputWeight(Idle, idle * ground);
			mixer.SetInputWeight(Walk, walking * ground);
			mixer.SetInputWeight(Run, running * ground);
			mixer.SetInputWeight(Fall, falling);
		}

		/// <summary>Stood still where it was put down, so a jump across the map does not arrive running.</summary>
		public void Settle()
		{
			speed = 0f;
			airborne = 0f;
			falling = 0f;
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
			// The primitive is 2 m by 1 m.
			float round = walker.Radius * 2f;
			shape.transform.localScale = new Vector3(round, walker.Height * 0.5f, round);
		}

#if UNITY_EDITOR
		private bool TryCharacter(WorldSimWalker walker)
		{
			var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
			AnimationClip idle = LoadClip("HumanoidIdle.fbx", "HumanoidIdle");
			AnimationClip walkClip = LoadClip("HumanoidWalk.fbx", "HumanoidWalk");
			AnimationClip runClip = LoadClip("HumanoidRun.fbx", "HumanoidRun");
			AnimationClip fall = LoadClip("HumanoidMidAir.fbx", "HumanoidMidAirLeft");
			if (model == null || idle == null || walkClip == null || runClip == null || fall == null)
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
			Clothe(shape);
			Animate(animator, idle, walkClip, runClip, fall);
			return true;
		}

		/// <summary>
		/// Sized and set down by its bind pose: as tall as the capsule, soles on the capsule's base. The
		/// model is exported at 1.8 m with its origin between its feet, so this normally changes
		/// nothing; it is here so a model of any size or pivot stands right without re-exporting it.
		/// </summary>
		private static void Stand(Transform shape, WorldSimWalker walker)
		{
			Renderer[] renderers = shape.GetComponentsInChildren<Renderer>();
			if (renderers.Length == 0)
			{
				return;
			}
			Bounds bounds = Extent(renderers);
			if (bounds.size.y > 0.01f)
			{
				shape.localScale *= walker.Height / bounds.size.y;
				bounds = Extent(renderers);
			}
			shape.position += Vector3.up * (walker.Feet.y - bounds.min.y);
		}

		private static Bounds Extent(Renderer[] renderers)
		{
			Bounds bounds = renderers[0].bounds;
			for (int i = 1; i < renderers.Length; i++)
			{
				bounds.Encapsulate(renderers[i].bounds);
			}
			return bounds;
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
			foreach (Renderer renderer in shape.GetComponentsInChildren<Renderer>())
			{
				renderer.sharedMaterial = material;
			}
		}

		private static AnimationClip LoadClip(string file, string name)
		{
			foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(ClipFolder + file))
			{
				if (asset is AnimationClip clip && clip.name == name)
				{
					return clip;
				}
			}
			return null;
		}
#endif

		private void Animate(Animator animator, AnimationClip idle, AnimationClip walkClip, AnimationClip runClip, AnimationClip fall)
		{
			// The walker moves itself; the clips' own travel is thrown away.
			animator.applyRootMotion = false;
			animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
			animator.runtimeAnimatorController = null;

			graph = PlayableGraph.Create("World Sim Walker");
			graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
			mixer = AnimationMixerPlayable.Create(graph, 4);
			AnimationPlayableOutput.Create(graph, "Body", animator).SetSourcePlayable(mixer);
			Connect(Idle, idle);
			walk = Connect(Walk, walkClip);
			run = Connect(Run, runClip);
			Connect(Fall, fall);
			// Walk and run are stepped by Pose on their shared phase; idle and fall play on by themselves.
			walk.SetSpeed(0d);
			run.SetSpeed(0d);

			walkSpeed = NaturalSpeed(walkClip, animator.humanScale, 1.5f);
			runSpeed = Mathf.Max(NaturalSpeed(runClip, animator.humanScale, 4f), walkSpeed + 0.5f);
			walkLength = Mathf.Max(walkClip.length, 0.1f);
			runLength = Mathf.Max(runClip.length, 0.1f);
			mixer.SetInputWeight(Idle, 1f);
			graph.Play();
		}

		private AnimationClipPlayable Connect(int input, AnimationClip clip)
		{
			AnimationClipPlayable playable = AnimationClipPlayable.Create(graph, clip);
			graph.Connect(playable, 0, mixer, input);
			mixer.SetInputWeight(input, 0f);
			return playable;
		}

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
