using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// A short-lived, flickering arc of lightning between two points, drawn with this object's <see cref="LineRenderer"/>:
	/// what a chain draws from one body it strikes to the next (<see cref="PlayChainArcFXAction"/>).
	/// </summary>
	/// <remarks>
	/// Re-jagged every few frames and faded out over <see cref="Seconds"/>, then destroyed. Presentation only: it is built
	/// on clients and decides nothing.
	/// </remarks>
	[RequireComponent(typeof(LineRenderer))]
	[DisallowMultipleComponent]
	public sealed class LightningArc : MonoBehaviour
	{
		[Tooltip("How long the arc lasts, seconds.")]
		public float Seconds = 0.35f;

		[Tooltip("Points along the arc, ends included.")]
		[Range(3, 32)]
		public int Segments = 12;

		[Tooltip("How far the arc may stray from the straight line, as a share of its length.")]
		public float Jitter = 0.12f;

		[Tooltip("Times a second the arc is re-jagged.")]
		public float FlickerRate = 30f;

		private LineRenderer line;
		private Vector3 from, to;
		private float age, nextFlicker;
		private Color startColour, endColour;
		private System.Random random;

		/// <summary>
		/// Instantiates <paramref name="prefab"/> into <paramref name="scene"/> and draws it from <paramref name="start"/> to
		/// <paramref name="end"/> for <paramref name="seconds"/> (0 keeps the prefab's). A prefab with no arc is destroyed after
		/// half a second.
		/// </summary>
		public static void Spawn(GameObject prefab, Vector3 start, Vector3 end, UnityEngine.SceneManagement.Scene scene, float seconds = 0f)
		{
			if (prefab == null)
			{
				return;
			}
			GameObject instance = Instantiate(prefab, start, Quaternion.identity);
			if (scene.IsValid() && scene.isLoaded && instance.scene != scene)
			{
				UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(instance, scene);
			}
			LightningArc arc = instance.GetComponent<LightningArc>();
			if (arc == null)
			{
				Destroy(instance, 0.5f);
				return;
			}
			if (seconds > 0f)
			{
				arc.Seconds = seconds;
			}
			arc.SetEnds(start, end);
		}

		/// <summary>Draws the arc from <paramref name="start"/> to <paramref name="end"/> (world space).</summary>
		public void SetEnds(Vector3 start, Vector3 end)
		{
			from = start;
			to = end;
			Jag();
		}

		private void Awake()
		{
			line = GetComponent<LineRenderer>();
			line.useWorldSpace = true;
			line.positionCount = Mathf.Max(3, Segments);
			startColour = line.startColor;
			endColour = line.endColor;
			random = new System.Random(unchecked((int)(Time.realtimeSinceStartup * 1000f) ^ (int)transform.position.sqrMagnitude));
		}

		private void Update()
		{
			age += Time.deltaTime;
			if (age >= Seconds)
			{
				Destroy(gameObject);
				return;
			}
			if (age >= nextFlicker)
			{
				Jag();
				nextFlicker = age + 1f / Mathf.Max(1f, FlickerRate);
			}
			float fade = 1f - age / Mathf.Max(0.01f, Seconds);
			Color a = startColour, b = endColour;
			a.a *= fade;
			b.a *= fade;
			line.startColor = a;
			line.endColor = b;
		}

		private void Jag()
		{
			if (line == null)
			{
				return;
			}
			int count = Mathf.Max(3, Segments);
			line.positionCount = count;
			Vector3 span = to - from;
			float length = span.magnitude;
			Vector3 side = Vector3.Cross(span, Vector3.up);
			if (side.sqrMagnitude < 1e-6f)
			{
				side = Vector3.right;
			}
			side.Normalize();
			Vector3 up = Vector3.Cross(side, span).normalized;
			for (int i = 0; i < count; ++i)
			{
				float t = i / (float)(count - 1);
				Vector3 point = from + span * t;
				if (i > 0 && i < count - 1)
				{
					// Strays most in the middle, never at the ends.
					float reach = length * Jitter * Mathf.Sin(t * Mathf.PI);
					point += side * ((float)random.NextDouble() * 2f - 1f) * reach + up * ((float)random.NextDouble() * 2f - 1f) * reach;
				}
				line.SetPosition(i, point);
			}
		}
	}
}
