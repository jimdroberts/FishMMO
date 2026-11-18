using UnityEngine;
using FishMMO.Shared;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FishMMO.TestHarness.World
{
	/// <summary>
	/// What the walker looks like: the game's character (the TestHuman prefab every race wears), animated by the game's
	/// own <see cref="CharacterBody"/> from what the walker is doing; or, outside the editor or with the prefab missing, a
	/// plain capsule the size of its collider.
	/// </summary>
	/// <remarks>
	/// Only the look. The walker's capsule collider is still what stands, climbs and collides; the model is a child at its
	/// feet with no collider of its own. Everything the figure does (the gaits, the air, the strokes at and under the
	/// surface, riding the waves, climbing, sitting, punching) is the body's, so the bed shows exactly what a player's
	/// character does in the game.
	/// </remarks>
	public sealed class WorldSimWalkerBody : MonoBehaviour
	{
		/// <summary>The character every race wears (built by CharacterModelMigration).</summary>
		public const string PrefabPath = "Assets/Prefabs/Client/Models/TestHuman/TestHuman.prefab";

		private CharacterBody body;

		/// <summary>The figure's height over the capsule, metres (the swell it rides, the ground it finds).</summary>
		public float Lift => body != null ? body.Lift : 0f;

		/// <summary>Dresses <paramref name="walker"/>: the character in the editor when it is there, else the capsule.</summary>
		public static WorldSimWalkerBody Dress(WorldSimWalker walker)
		{
			var dressed = walker.gameObject.AddComponent<WorldSimWalkerBody>();
#if UNITY_EDITOR
			if (dressed.TryCharacter())
			{
				return dressed;
			}
#endif
			dressed.Capsule(walker);
			return dressed;
		}

		/// <summary>
		/// One frame of the walker's motion: <paramref name="velocity"/> is how far it really went this frame over the
		/// frame's length; <paramref name="water"/> the still water's surface over it while it swims (NaN otherwise).
		/// </summary>
		public void Pose(Vector3 velocity, bool grounded, bool swimming, bool climbing, bool crouching, float water, float deltaTime)
		{
			if (body == null)
			{
				return;
			}
			var motion = new CharacterMotion
			{
				Velocity = velocity,
				Grounded = grounded,
				Swimming = swimming,
				Climbing = climbing,
				Crouching = crouching,
				StillWater = water,
			};
			body.Pose(in motion, deltaTime);
		}

		/// <summary>Throws a punch, the right and the left by turns.</summary>
		public void Punch() => body?.Punch();

		/// <summary>Sits down: on the ground, or on a seat that high whose front edge is that far behind.</summary>
		public void SitDown(bool seat, float height, float back) => body?.SitDown(seat, height, back);

		/// <summary>Gets up.</summary>
		public void StandUp() => body?.StandUp();

		/// <summary>Stood still where it was put down, so a jump across the map does not arrive running.</summary>
		public void Settle() => body?.Settle();

		private void LateUpdate()
		{
			body?.Place(Time.deltaTime);
		}

		private void OnEnable()
		{
			// Hiding the walker stops its graph with it; showing it again starts the graph where it left off.
			body?.SetPlaying(true);
		}

		private void OnDestroy()
		{
			body?.Dispose();
			body = null;
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
		private bool TryCharacter()
		{
			var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
			if (prefab == null || prefab.GetComponent<CharacterModel>() == null)
			{
				Debug.LogWarning($"[World Sim] The character ({PrefabPath}) is missing; walking as a capsule. Run FishMMO.Shared.CharacterModelMigration.Run to build it.");
				return false;
			}
			GameObject shape = Instantiate(prefab, transform, false);
			shape.name = "Body";
			foreach (Transform part in shape.GetComponentsInChildren<Transform>(true))
			{
				part.gameObject.layer = gameObject.layer;
			}
			CharacterModel model = shape.GetComponent<CharacterModel>();
			Animator animator = shape.GetComponent<Animator>();
			animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
			body = CharacterBody.Create(animator, model.Animations, "World Sim walker");
			if (body == null)
			{
				shape.SetActive(false);
				Destroy(shape);
				return false;
			}
			return true;
		}
#endif
	}
}
