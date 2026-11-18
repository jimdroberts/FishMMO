using UnityEngine;

namespace FishMMO.TestHarness
{
	/// <summary>
	/// A world-space combat text label: rises, fades, faces the camera, destroys itself.
	/// The sim scenes use it for damage, heal, buff, and death callouts — the server has no
	/// floating-combat-text path of its own (that lives client-side), so the harness draws its
	/// own from the server's authoritative events.
	/// </summary>
	public sealed class FloatingLabel : MonoBehaviour
	{
		private const float Lifetime = 1.4f;
		private const float RiseSpeed = 1.2f;

		private TextMesh text;
		private float age;
		private Color baseColor;

		public static void Spawn(Vector3 position, string message, Color color, float size = 0.35f)
		{
			GameObject go = new GameObject("FloatingLabel");
			go.transform.position = position + new Vector3(Random.Range(-0.3f, 0.3f), 0f, Random.Range(-0.2f, 0.2f));
			FloatingLabel label = go.AddComponent<FloatingLabel>();
			label.text = go.AddComponent<TextMesh>();
			label.text.text = message;
			label.text.characterSize = size;
			label.text.fontSize = 32;
			label.text.anchor = TextAnchor.MiddleCenter;
			label.text.alignment = TextAlignment.Center;
			label.text.color = color;
			label.baseColor = color;
		}

		private void Update()
		{
			age += Time.deltaTime;
			if (age >= Lifetime)
			{
				Destroy(gameObject);
				return;
			}

			transform.position += Vector3.up * (RiseSpeed * Time.deltaTime);

			Camera camera = Camera.main;
			if (camera != null)
			{
				// Billboard: face the camera the way UI text does.
				transform.rotation = Quaternion.LookRotation(transform.position - camera.transform.position);
			}

			float alpha = 1f - Mathf.Clamp01((age - Lifetime * 0.45f) / (Lifetime * 0.55f));
			text.color = new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * alpha);
		}
	}
}
