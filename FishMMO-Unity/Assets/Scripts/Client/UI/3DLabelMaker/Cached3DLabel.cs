using UnityEngine;
using TMPro;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// A poolable 3D text label rendered via TextMeshPro.
	/// Supports automatic caching after a timed duration or manual caching via <see cref="LabelMaker.Cache"/>.
	/// Visual effects are driven by a <see cref="LabelEffect"/> bit-flag field and processed each frame.
	/// Multiple effects can be combined simultaneously (e.g., FadeOut + FloatUp + ScaleDown).
	/// </summary>
	public sealed class Cached3DLabel : MonoBehaviour, IReference
	{
		// ── Core state ──────────────────────────────────────────────

		/// <summary>
		/// If true, the label must be returned to the pool manually via <see cref="LabelMaker.Cache"/>.
		/// </summary>
		private bool manualCache;

		/// <summary>
		/// Remaining time in seconds before the label is automatically cached.
		/// </summary>
		private float remainingTime;

		/// <summary>
		/// Total lifetime assigned on Initialize (used to compute normalized time t).
		/// </summary>
		private float totalTime;

		/// <summary>
		/// The TextMeshPro component used to render the label text.
		/// </summary>
		[SerializeField]
		private TextMeshPro textMesh;

		/// <summary>
		/// Gets the TextMeshPro component for this label.
		/// </summary>
		public TextMeshPro TMP => textMesh;

		// ── Effect state ────────────────────────────────────────────

		/// <summary>
		/// Bit-flag field storing which <see cref="LabelEffect"/> effects are active.
		/// Manipulated via <see cref="IntBitExtensions"/>.
		/// </summary>
		private int effectFlags;

		/// <summary>
		/// World position recorded at initialization, used as the anchor for movement effects.
		/// </summary>
		private Vector3 originPosition;

		/// <summary>
		/// Base font size recorded at initialization, used as the anchor for scale effects.
		/// </summary>
		private float baseFontSize;

		/// <summary>
		/// Base color recorded at initialization, used as the anchor for fade effects.
		/// </summary>
		private Color baseColor;

		/// <summary>
		/// Running offset accumulated by movement effects (FloatUp, FloatRandom, Bounce, Wave, Shake).
		/// Reset each frame before effects are applied.
		/// </summary>
		private Vector3 moveOffset;

		/// <summary>
		/// Velocity vector used by the FloatRandom effect for physics-style movement.
		/// </summary>
		private Vector3 floatRandomVelocity;

		/// <summary>
		/// Accumulated position delta from FloatRandom (integrated each frame, not reset).
		/// </summary>
		private Vector3 floatRandomOffset;

		// ── Tuning parameters (public so callers can tweak per-label) ──

		/// <summary>Speed in world-units per second for <see cref="LabelEffect.FloatUp"/>.</summary>
		[Header("Effect Parameters")]
		public float FloatUpSpeed = 2.0f;

		/// <summary>Initial speed multiplier for <see cref="LabelEffect.FloatRandom"/>.</summary>
		public float FloatRandomSpeed = 2.0f;

		/// <summary>Gravity (negative = downward) applied each second to <see cref="LabelEffect.FloatRandom"/>.</summary>
		public float FloatRandomGravity = -4.0f;

		/// <summary>Amplitude in world-units for <see cref="LabelEffect.Bounce"/>.</summary>
		public float BounceAmplitude = 0.3f;

		/// <summary>Frequency in cycles per second for <see cref="LabelEffect.Bounce"/>.</summary>
		public float BounceFrequency = 4.0f;

		/// <summary>Amplitude in world-units for <see cref="LabelEffect.Wave"/>.</summary>
		public float WaveAmplitude = 0.15f;

		/// <summary>Frequency in cycles per second for <see cref="LabelEffect.Wave"/>.</summary>
		public float WaveFrequency = 1.5f;

		/// <summary>Min/max scale multiplier for <see cref="LabelEffect.Pulse"/>.</summary>
		public float PulseMin = 0.8f;

		/// <summary>Max scale multiplier for <see cref="LabelEffect.Pulse"/>.</summary>
		public float PulseMax = 1.2f;

		/// <summary>Frequency in cycles per second for <see cref="LabelEffect.Pulse"/>.</summary>
		public float PulseFrequency = 3.0f;

		/// <summary>Maximum offset in world-units per axis for <see cref="LabelEffect.Shake"/>.</summary>
		public float ShakeIntensity = 0.1f;

		// ── Update ──────────────────────────────────────────────────

		/// <summary>
		/// Processes the persist timer and all active visual effects each frame.
		/// </summary>
		void Update()
		{
			if (manualCache) return;

			float dt = Time.deltaTime;
			remainingTime -= dt;
			if (remainingTime <= 0.0f)
			{
				LabelMaker.Cache(this);
				return;
			}

			if (effectFlags == 0) return;

			// Normalized progress 0 → 1 over the label's lifetime.
			float t = 1.0f - (remainingTime / totalTime);
			float fontSize = baseFontSize;
			Color color = baseColor;
			moveOffset = Vector3.zero;

			// ── Fade ────────────────────────────────────────────
			if (effectFlags.IsFlagged(LabelEffect.FadeIn))
			{
				// Fade in over the first half, stay at full alpha after.
				float fadeIn = Mathf.Clamp01(t * 2.0f);
				color.a *= fadeIn;
			}
			if (effectFlags.IsFlagged(LabelEffect.FadeOut))
			{
				// Stay at full alpha for the first half, fade out over the second half.
				float fadeOut = Mathf.Clamp01((1.0f - t) * 2.0f);
				color.a *= fadeOut;
			}

			// ── Scale ───────────────────────────────────────────
			if (effectFlags.IsFlagged(LabelEffect.ScaleUp))
			{
				fontSize *= t;
			}
			if (effectFlags.IsFlagged(LabelEffect.ScaleDown))
			{
				fontSize *= (1.0f - t);
			}
			if (effectFlags.IsFlagged(LabelEffect.Pulse))
			{
				float pulse = Mathf.Lerp(PulseMin, PulseMax, (Mathf.Sin(t * PulseFrequency * Mathf.PI * 2.0f) + 1.0f) * 0.5f);
				fontSize *= pulse;
			}

			// ── Movement ────────────────────────────────────────
			if (effectFlags.IsFlagged(LabelEffect.FloatUp))
			{
				moveOffset.y += FloatUpSpeed * t * totalTime;
			}
			if (effectFlags.IsFlagged(LabelEffect.FloatRandom))
			{
				floatRandomVelocity.y += FloatRandomGravity * dt;
				floatRandomOffset += floatRandomVelocity * dt;
				moveOffset += floatRandomOffset;
			}
			if (effectFlags.IsFlagged(LabelEffect.Bounce))
			{
				moveOffset.y += Mathf.Abs(Mathf.Sin(t * BounceFrequency * Mathf.PI)) * BounceAmplitude;
			}
			if (effectFlags.IsFlagged(LabelEffect.Wave))
			{
				moveOffset.y += Mathf.Sin(t * WaveFrequency * Mathf.PI * 2.0f) * WaveAmplitude;
			}
			if (effectFlags.IsFlagged(LabelEffect.Shake))
			{
				moveOffset.x += Random.Range(-ShakeIntensity, ShakeIntensity);
				moveOffset.y += Random.Range(-ShakeIntensity, ShakeIntensity);
				moveOffset.z += Random.Range(-ShakeIntensity, ShakeIntensity);
			}

			// ── Apply ───────────────────────────────────────────
			transform.position = originPosition + moveOffset;
			textMesh.fontSize = fontSize;
			textMesh.color = color;
		}

		// ── Public API ──────────────────────────────────────────────

		/// <summary>
		/// Initializes the label with the specified display properties, effects, and activates it.
		/// </summary>
		/// <param name="text">Text to display.</param>
		/// <param name="position">World position for the label.</param>
		/// <param name="color">Text color.</param>
		/// <param name="fontSize">Font size in Unity units.</param>
		/// <param name="persistTime">Duration in seconds before automatic caching. Ignored if manualCache is true.</param>
		/// <param name="manualCache">If true, the label must be cached manually via <see cref="LabelMaker.Cache"/>.</param>
		/// <param name="effectFlags">Bit-flag field of <see cref="LabelEffect"/> values to apply. 0 for no effects.</param>
		public void Initialize(string text, Vector3 position, Color color, float fontSize, float persistTime, bool manualCache, int effectFlags = 0)
		{
			textMesh.text = text;
			textMesh.fontSize = fontSize;
			textMesh.color = color;

			textMesh.ForceMeshUpdate();
			position.y += textMesh.textBounds.size.y;
			transform.position = position;

			remainingTime = persistTime;
			totalTime = persistTime > 0.0f ? persistTime : 1.0f;
			this.manualCache = manualCache;

			// Store anchors for effects.
			originPosition = transform.position;
			baseFontSize = fontSize;
			baseColor = color;
			this.effectFlags = effectFlags;

			// Reset per-use effect state.
			moveOffset = Vector3.zero;
			floatRandomOffset = Vector3.zero;

			// Initialize FloatRandom velocity if active.
			if (effectFlags.IsFlagged(LabelEffect.FloatRandom))
			{
				Vector3 randomDir = new Vector3(
					Random.Range(-1f, 1f),
					1f,
					Random.Range(-1f, 1f)
				).normalized;
				floatRandomVelocity = randomDir * FloatRandomSpeed;
			}
			else
			{
				floatRandomVelocity = Vector3.zero;
			}

			// Start invisible if fading in.
			if (effectFlags.IsFlagged(LabelEffect.FadeIn))
			{
				Color startColor = color;
				startColor.a = 0.0f;
				textMesh.color = startColor;
			}

			// Start at zero size if scaling up.
			if (effectFlags.IsFlagged(LabelEffect.ScaleUp))
			{
				textMesh.fontSize = 0.0f;
			}

			gameObject.SetActive(true);
		}

		/// <summary>
		/// Sets the world position of the label and updates the origin anchor for movement effects.
		/// </summary>
		/// <param name="position">The new world position.</param>
		public void SetPosition(Vector3 position)
		{
			transform.position = position;
			originPosition = position;
		}

		/// <summary>
		/// Sets the displayed text content.
		/// </summary>
		/// <param name="text">The new text to display.</param>
		public void SetText(string text)
		{
			if (textMesh == null) return;

			textMesh.text = text;
		}

		/// <summary>
		/// Sets the text color and updates the base color anchor for fade effects.
		/// </summary>
		/// <param name="color">The new color.</param>
		public void SetColor(Color color)
		{
			if (textMesh == null) return;

			textMesh.color = color;
			baseColor = color;
		}

		/// <summary>
		/// Sets the font size and updates the base font size anchor for scale effects.
		/// </summary>
		/// <param name="fontSize">The new font size in Unity units.</param>
		public void SetFontSize(float fontSize)
		{
			if (textMesh == null) return;

			textMesh.fontSize = fontSize;
			baseFontSize = fontSize;
		}
	}
}