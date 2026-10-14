using FishMMO.Shared.Core;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>What a character carries to see by: nothing, a torch, or a lantern.</summary>
	/// <remarks>Stored as its number in the player's settings, so the values never move.</remarks>
	public enum CarriedLightMode : byte
	{
		/// <summary>Nothing carried.</summary>
		Off = 0,
		/// <summary>A pitch torch: warm, flickering, a room's worth of light.</summary>
		Torch = 1,
		/// <summary>A shuttered lantern: brighter, steadier, a little whiter, and reaching nearly twice as far.</summary>
		Lantern = 2,
	}

	/// <summary>
	/// A light a character carries, held a little ahead of the shoulder so the walls, the floor and the faces in front of
	/// them read in a cave the sky never reaches.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Brighter than the walls' torches.</b> The cut's mine and cave-camp torches are lit by SiteLightPool at about 1.6
	/// with a three-beat flicker and a warm orange, and its buried lamps at 2.2, steady: marks along a way, not light to see
	/// it by. A carried torch uses the same flicker and very nearly the same colour (a shade redder, nearer a pitch flame's
	/// 2200 K), but at the wall torches' strength it lit the rock within an arm's length and left the rest black in the
	/// cave walkthrough render (2026-10-13: "Ensure we can see and use the torch or a stronger light source for visual
	/// clarity"). URP point lights fall off with the square of distance whatever their range, so the strength is what
	/// carries: the torch reads the floor and walls a room's width off, the lantern most of a hall's. Underground the sky's
	/// ambient falls to under a tenth (SkySystem's darkness), so these are most of what lights a cave.
	/// </para>
	/// <para>
	/// <b>Ahead, not at the hand.</b> Parented to what the character's model is drawn under (the smoothed graphical object,
	/// <see cref="HolderFor"/>), not to a hand bone: an arm swinging through a walk would swing every shadow in the cave
	/// with it, and the race model under the mesh root is destroyed and rebuilt whenever it reloads. It sits forward of the
	/// chest at shoulder height, so the body's own shadow falls behind it (away from a camera that follows from behind),
	/// and the front of the character, not the back the camera sees, takes the light's hot near field.
	/// </para>
	/// <para>
	/// <b>One shadow.</b> Soft shadows for at most one carried light at a time (the first lit takes them, and gives them up
	/// when it goes out): a shadowed point light renders six cube faces of every caster within its range, the cost
	/// SiteLightPool already pays for its nearest few indoor lights and no more. Where the quality tier has no
	/// additional-light shadows URP draws the light unshadowed and charges nothing for asking.
	/// </para>
	/// <para>
	/// Presentation only, never simulation: a server never builds one. <see cref="Attach"/> works in edit mode too, so a
	/// render probe can hang the same light on its camera and see what a player sees; flicker only runs in play mode.
	/// </para>
	/// </remarks>
	[DisallowMultipleComponent]
	public sealed class CarriedLight : MonoBehaviour
	{
		/// <summary>The name of the child object the light lives on, so a holder's light can be found again.</summary>
		public const string ObjectName = "Carried Light";

		/// <summary>How far the torch reaches before its falloff is windowed to nothing, metres.</summary>
		public const float TorchRange = 14f;

		/// <summary>The torch's mean strength: enough to see a room's floor and walls by, about four wall torches.</summary>
		public const float TorchIntensity = 6f;

		/// <summary>How far the lantern reaches, metres.</summary>
		public const float LanternRange = 26f;

		/// <summary>The lantern's strength: twice the torch, steady, as a lantern's glass and shutter throw it forward.</summary>
		public const float LanternIntensity = 12f;

		/// <summary>A pitch flame, about 2200 K: a shade redder than the wall torches' (1, 0.66, 0.36).</summary>
		public static readonly Color TorchColour = new Color(1f, 0.62f, 0.31f);

		/// <summary>A lantern's candle behind glass, about 3000 K: a touch whiter than a buried lamp's (1, 0.72, 0.42).</summary>
		public static readonly Color LanternColour = new Color(1f, 0.76f, 0.5f);

		/// <summary>Where a character holds it, as fractions of its height above its feet (and a forward reach, a side step).</summary>
		private const float ShoulderFraction = 0.8f;
		private const float ForwardMetres = 0.5f;
		private const float SideMetres = -0.22f;
		/// <summary>The height a character's offsets are authored for, metres; a taller or shorter one scales them.</summary>
		private const float AuthoredHeight = 1.8f;

		/// <summary>On a camera: low and to the right of the eye, ahead of the near plane, as a torch held in the right hand.</summary>
		private static readonly Vector3 CameraOffset = new Vector3(0.3f, -0.32f, 0.35f);

		/// <summary>The carried light that holds the one shadow, or null when none does.</summary>
		private static CarriedLight shadowHolder;

		private Light source;
		private CarriedLightMode mode;
		private float phase;

		/// <summary>What is carried now.</summary>
		public CarriedLightMode Mode => mode;

		/// <summary>The light itself (disabled while <see cref="Mode"/> is <see cref="CarriedLightMode.Off"/>).</summary>
		public Light Source => source;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			shadowHolder = null;
		}

		/// <summary>The next mode the cycle key steps to: Off, Torch, Lantern, and round again.</summary>
		public static CarriedLightMode Next(CarriedLightMode current)
		{
			switch (current)
			{
				case CarriedLightMode.Off:
					return CarriedLightMode.Torch;
				case CarriedLightMode.Torch:
					return CarriedLightMode.Lantern;
				default:
					return CarriedLightMode.Off;
			}
		}

		/// <summary>
		/// The transform a character's light is parented to: the parent of its mesh root, which is the object FishNet smooths
		/// between ticks, so the light glides with the drawn model rather than stepping with the simulated root.
		/// </summary>
		/// <param name="character">The character; null gives null.</param>
		public static Transform HolderFor(ICharacter character)
		{
			if (character == null)
			{
				return null;
			}
			Transform meshRoot = character.MeshRoot;
			if (meshRoot != null && meshRoot.parent != null)
			{
				return meshRoot.parent;
			}
			return character.Transform;
		}

		/// <summary>
		/// Gives <paramref name="holder"/> a carried light in <paramref name="mode"/>, creating it on first use, and returns
		/// the light (disabled for <see cref="CarriedLightMode.Off"/>).
		/// </summary>
		/// <remarks>
		/// On a camera it is held low and right of the eye; on anything else it is held ahead of the shoulder, measured from
		/// the nearest capsule collider at or above the holder (a character's), or from a 1.8 m figure standing at the
		/// holder when there is none. Usable outside play mode: the light is lit at once at its mean strength, and the
		/// object is never saved with a scene.
		/// </remarks>
		/// <param name="holder">What carries it: a character's <see cref="HolderFor"/>, or a camera.</param>
		/// <param name="mode">What to carry.</param>
		/// <returns>The light, or null when <paramref name="holder"/> is null.</returns>
		public static Light Attach(Transform holder, CarriedLightMode mode)
		{
			if (holder == null)
			{
				return null;
			}
			CarriedLight carried = Find(holder);
			if (carried == null)
			{
				var go = new GameObject(ObjectName) { hideFlags = HideFlags.DontSave };
				go.transform.SetParent(holder, false);
				carried = go.AddComponent<CarriedLight>();
				carried.Build();
			}
			carried.transform.localPosition = HeldOffset(holder);
			carried.transform.localRotation = Quaternion.identity;
			carried.SetMode(mode);
			return carried.source;
		}

		/// <summary>Removes <paramref name="holder"/>'s carried light, if it has one.</summary>
		public static void Detach(Transform holder)
		{
			CarriedLight carried = Find(holder);
			if (carried == null)
			{
				return;
			}
			if (Application.isPlaying)
			{
				Destroy(carried.gameObject);
			}
			else
			{
				DestroyImmediate(carried.gameObject);
			}
		}

		/// <summary>The carried light directly under <paramref name="holder"/>, or null.</summary>
		public static CarriedLight Find(Transform holder)
		{
			if (holder == null)
			{
				return null;
			}
			for (int i = 0; i < holder.childCount; i++)
			{
				if (holder.GetChild(i).TryGetComponent(out CarriedLight carried))
				{
					return carried;
				}
			}
			return null;
		}

		/// <summary>Changes what is carried, lighting or putting out the light at once.</summary>
		public void SetMode(CarriedLightMode value)
		{
			if (source == null)
			{
				Build();
			}
			mode = value;
			bool lit = value != CarriedLightMode.Off && isActiveAndEnabled;
			source.enabled = lit;
			if (!lit)
			{
				ReleaseShadow();
				return;
			}
			source.range = value == CarriedLightMode.Lantern ? LanternRange : TorchRange;
			source.color = value == CarriedLightMode.Lantern ? LanternColour : TorchColour;
			ClaimShadow();
			Apply(Application.isPlaying ? Time.time : 0f);
		}

		private void Build()
		{
			if (!TryGetComponent(out source))
			{
				source = gameObject.AddComponent<Light>();
			}
			source.type = LightType.Point;
			// Always per pixel: the one light the player chose to carry is never the one URP drops for a nearer lamp.
			source.renderMode = LightRenderMode.ForcePixel;
			source.shadows = LightShadows.None;
			source.shadowStrength = 0.9f;
			source.enabled = false;
			// Two carried lights never flicker in step; Unity ids differ per object, and a fixed value in edit mode.
			phase = Application.isPlaying ? (GetInstanceID() & 0xFFFF) * 0.0137f : 0f;
		}

		/// <summary>Takes the one shadow if no other lit carried light holds it.</summary>
		private void ClaimShadow()
		{
			if (shadowHolder == null || shadowHolder == this || !shadowHolder.isActiveAndEnabled || shadowHolder.mode == CarriedLightMode.Off)
			{
				shadowHolder = this;
			}
			source.shadows = shadowHolder == this ? LightShadows.Soft : LightShadows.None;
		}

		private void ReleaseShadow()
		{
			if (shadowHolder == this)
			{
				shadowHolder = null;
			}
			if (source != null)
			{
				source.shadows = LightShadows.None;
			}
		}

		private void OnEnable()
		{
			if (source != null && mode != CarriedLightMode.Off)
			{
				SetMode(mode);
			}
		}

		private void OnDisable()
		{
			ReleaseShadow();
		}

		private void LateUpdate()
		{
			if (mode == CarriedLightMode.Off || source == null)
			{
				return;
			}
			// A light that lost the shadow to one since put out takes it back.
			if (source.shadows == LightShadows.None && (shadowHolder == null || shadowHolder.mode == CarriedLightMode.Off))
			{
				ClaimShadow();
			}
			Apply(Time.time);
		}

		/// <summary>The strength at <paramref name="time"/>: the wall torches' flicker, or a lantern's slow, shallow breath.</summary>
		private void Apply(float time)
		{
			if (mode == CarriedLightMode.Torch)
			{
				// SiteLightPool's torch beat, so a carried torch and a wall torch flicker alike; mean 0.88.
				float flick = 0.88f + 0.08f * Mathf.Sin(time * 9.7f + phase) + 0.04f * Mathf.Sin(time * 15.3f + phase * 1.9f);
				source.intensity = TorchIntensity * flick / 0.88f;
			}
			else
			{
				source.intensity = LanternIntensity * (0.97f + 0.03f * Mathf.Sin(time * 1.7f + phase));
			}
		}

		/// <summary>Where on <paramref name="holder"/> the light is held, in its local space.</summary>
		private static Vector3 HeldOffset(Transform holder)
		{
			if (holder.TryGetComponent(out Camera _))
			{
				return CameraOffset;
			}
			CapsuleCollider capsule = holder.GetComponentInParent<CapsuleCollider>();
			if (capsule == null)
			{
				return new Vector3(SideMetres, AuthoredHeight * ShoulderFraction, ForwardMetres);
			}
			// Measured in the capsule's own space (its height and centre are), then carried into the holder's.
			float height = Mathf.Max(0.5f, capsule.height);
			float scale = height / AuthoredHeight;
			float feet = capsule.center.y - height * 0.5f;
			Vector3 held = new Vector3(capsule.center.x + SideMetres * scale, feet + height * ShoulderFraction, capsule.center.z + ForwardMetres * scale);
			return holder.InverseTransformPoint(capsule.transform.TransformPoint(held));
		}
	}
}
