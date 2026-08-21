using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// A poolable world-anchored label, drawn by <see cref="UITKWorldLabelLayer"/>.
	/// Supports automatic caching after a timed duration or manual caching via
	/// <see cref="UITKLabelMaker.Cache"/>. Visual effects are driven by a <see cref="LabelEffect"/>
	/// bit-flag field and processed each frame; multiple effects combine
	/// (e.g. FadeOut + FloatUp + ScaleDown).
	/// </summary>
	/// <remarks>
	/// This is the UI Toolkit successor to the TextMeshPro-backed <c>Cached3DLabel</c>. The effect
	/// timeline is carried over unchanged — same flags, same tuning fields, same curves — because
	/// it was never rendering-specific: every effect is arithmetic on a position, a font size or a
	/// colour, and those are exactly the three things <see cref="WorldLabel"/> carries.
	///
	/// The one thing that had to change is where movement lands. The old label moved its own
	/// transform, which is not possible here for a label parented to a character that is itself
	/// moving. Offsets are written to <see cref="WorldLabel.WorldOffset"/> instead, so effects
	/// compose with the anchor rather than replacing it — a damage number can float up off a
	/// target that is running away, which the transform-writing version could not do.
	/// </remarks>
	[DisallowMultipleComponent]
	[RequireComponent(typeof(WorldLabel))]
	public sealed class UITKWorldLabel : MonoBehaviour, IReference
	{
		// ── Core state ──────────────────────────────────────────────

		/// <summary>
		/// If true, the label must be returned to the pool manually via <see cref="UITKLabelMaker.Cache"/>.
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
		/// The label data this component animates.
		/// </summary>
		private WorldLabel label;

		/// <summary>
		/// The underlying label data, for callers that need to read or set text directly.
		/// </summary>
		public WorldLabel Label
		{
			get
			{
				if (label == null)
				{
					label = GetComponent<WorldLabel>();
				}
				return label;
			}
		}

		// ── Effect state ────────────────────────────────────────────

		/// <summary>
		/// Bit-flag field storing which <see cref="LabelEffect"/> effects are active.
		/// Manipulated via <see cref="IntBitExtensions"/>.
		/// </summary>
		private int effectFlags;

		/// <summary>
		/// Base font size recorded at initialization, used as the anchor for scale effects.
		/// </summary>
		private float baseFontSize;

		/// <summary>
		/// Base color recorded at initialization, used as the anchor for fade effects.
		/// </summary>
		private Color baseColor;

		/// <summary>
		/// World offset recorded at initialization, before effects contribute.
		/// </summary>
		private Vector3 baseOffset;

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

		/// <summary>Min scale multiplier for <see cref="LabelEffect.Pulse"/>.</summary>
		public float PulseMin = 0.8f;

		/// <summary>Max scale multiplier for <see cref="LabelEffect.Pulse"/>.</summary>
		public float PulseMax = 1.2f;

		/// <summary>Frequency in cycles per second for <see cref="LabelEffect.Pulse"/>.</summary>
		public float PulseFrequency = 3.0f;

		/// <summary>Maximum offset in world-units per axis for <see cref="LabelEffect.Shake"/>.</summary>
		public float ShakeIntensity = 0.1f;

		private void Awake()
		{
			label = GetComponent<WorldLabel>();
		}

		// ── Update ──────────────────────────────────────────────────

		/// <summary>
		/// Processes the persist timer and all active visual effects each frame.
		/// </summary>
		private void Update()
		{
			if (manualCache)
			{
				return;
			}

			float dt = Time.deltaTime;
			remainingTime -= dt;
			if (remainingTime <= 0.0f)
			{
				UITKLabelMaker.Cache(this);
				return;
			}

			if (effectFlags == 0)
			{
				return;
			}

			WorldLabel target = Label;
			if (target == null)
			{
				return;
			}

			// Normalized progress 0 → 1 over the label's lifetime.
			float t = 1.0f - (remainingTime / totalTime);
			float fontSize = baseFontSize;
			Color color = baseColor;
			Vector3 moveOffset = Vector3.zero;

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
				moveOffset.x += DeterministicRNG.Shared.Range(-ShakeIntensity, ShakeIntensity);
				moveOffset.y += DeterministicRNG.Shared.Range(-ShakeIntensity, ShakeIntensity);
				moveOffset.z += DeterministicRNG.Shared.Range(-ShakeIntensity, ShakeIntensity);
			}

			// ── Apply ───────────────────────────────────────────
			target.WorldOffset = baseOffset + moveOffset;
			target.fontSize = fontSize;
			target.color = color;
		}

		// ── Public API ──────────────────────────────────────────────

		/// <summary>
		/// Initializes the label with the specified display properties, effects, and activates it.
		/// </summary>
		/// <param name="text">Text to display.</param>
		/// <param name="position">World position for the label.</param>
		/// <param name="color">Text color.</param>
		/// <param name="fontSize">Font size in world units, scaled by distance at render time.</param>
		/// <param name="persistTime">Duration in seconds before automatic caching. Ignored if manualCache is true.</param>
		/// <param name="manualCache">If true, the label must be cached manually via <see cref="UITKLabelMaker.Cache"/>.</param>
		/// <param name="effectFlags">Bit-flag field of <see cref="LabelEffect"/> values to apply. 0 for no effects.</param>
		public void Initialize(string text, Vector3 position, Color color, float fontSize, float persistTime, bool manualCache, int effectFlags = 0)
		{
			WorldLabel target = Label;
			if (target == null)
			{
				return;
			}

			transform.position = position;

			/* The TextMeshPro version raised the label by its own measured text height so the
			 * anchor sat at the text's baseline rather than through its middle. There is no mesh
			 * to measure here, and no need for one: the rendered element is translated up by its
			 * own full height in the layer, which is the same correction expressed where the
			 * height is actually known. The offset left here is purely what effects add. */
			baseOffset = Vector3.zero;
			target.WorldOffset = baseOffset;

			target.Set(text, color);
			target.fontSize = fontSize;

			remainingTime = persistTime;
			totalTime = persistTime > 0.0f ? persistTime : 1.0f;
			this.manualCache = manualCache;

			// Store anchors for effects.
			baseFontSize = fontSize;
			baseColor = color;
			this.effectFlags = effectFlags;

			// Reset per-use effect state.
			floatRandomOffset = Vector3.zero;

			// Initialize FloatRandom velocity if active.
			if (effectFlags.IsFlagged(LabelEffect.FloatRandom))
			{
				Vector3 randomDir = new Vector3(
					DeterministicRNG.Shared.Range(-1f, 1f),
					1f,
					DeterministicRNG.Shared.Range(-1f, 1f)
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
				target.color = startColor;
			}

			// Start at zero size if scaling up.
			if (effectFlags.IsFlagged(LabelEffect.ScaleUp))
			{
				target.fontSize = 0.0f;
			}

			gameObject.SetActive(true);
		}

		/// <summary>
		/// Sets the world position of the label and clears any accumulated effect offset.
		/// </summary>
		/// <param name="position">The new world position.</param>
		public void SetPosition(Vector3 position)
		{
			transform.position = position;
			baseOffset = Vector3.zero;
			floatRandomOffset = Vector3.zero;
			if (Label != null)
			{
				Label.WorldOffset = Vector3.zero;
			}
		}

		/// <summary>
		/// Sets the displayed text content.
		/// </summary>
		/// <param name="text">The new text to display.</param>
		public void SetText(string text)
		{
			if (Label == null)
			{
				return;
			}
			Label.text = text;
		}

		/// <summary>
		/// Sets the text color and updates the base color anchor for fade effects.
		/// </summary>
		/// <param name="color">The new color.</param>
		public void SetColor(Color color)
		{
			if (Label == null)
			{
				return;
			}
			Label.color = color;
			baseColor = color;
		}

		/// <summary>
		/// Sets the font size and updates the base font size anchor for scale effects.
		/// </summary>
		/// <param name="fontSize">The new font size in world units.</param>
		public void SetFontSize(float fontSize)
		{
			if (Label == null)
			{
				return;
			}
			Label.fontSize = fontSize;
			baseFontSize = fontSize;
		}
	}
}
