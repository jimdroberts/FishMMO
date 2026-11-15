using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Seats an effect's emitter on bones of the humanoid model it was instantiated under: one copy per bone, so a pair of
	/// feet gets an emitter at each ankle.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Where it runs.</b> A buff's effect is instantiated under the character's <see cref="ICharacter.MeshRoot"/>
	/// (<see cref="BaseBuffTemplate.OnApplyFX"/>), on the owner and every observer alike, and destroyed when the buff
	/// leaves. This component looks for the model's <see cref="Animator"/> under that parent and moves the emitter onto
	/// the bones the avatar maps (<see cref="HumanoidBones"/>, the lookup <see cref="CharacterBody"/> sets the feet on the
	/// ground with), so nothing has to be added to the models for it: every humanoid rig already says where its ankles are.
	/// </para>
	/// <para>
	/// <b>Ownership.</b> The emitters live under the bones, not under this object, so they are destroyed with it
	/// (<see cref="OnDestroy"/>): the buff ending, the controller tearing the effect down, and a model reload that clears the
	/// root all end them. A model reload re-creates the effect through the buff controller, which seats it afresh.
	/// </para>
	/// <para>
	/// <b>Size.</b> Effect prefabs are authored to fit a one-metre cube (<see cref="ParticleAdjuster"/>), so an emitter is
	/// scaled to <see cref="WorldSize"/> metres whatever scale the bone it rides carries (an imported rig's bones are often
	/// scaled by its units).
	/// </para>
	/// <para>
	/// <b>No humanoid rig</b> (an animal, a prop, a model still loading past <see cref="ResolveTimeoutSeconds"/>): the
	/// emitter stays where the prefab put it, at the root's feet, which is still the right place for an effect about the
	/// feet. Nothing is logged for it; it is not a fault.
	/// </para>
	/// </remarks>
	[DisallowMultipleComponent]
	public sealed class FXBoneAttachment : MonoBehaviour
	{
		/// <summary>The bones to seat an emitter on. The first takes <see cref="Emitter"/> itself; the rest take copies.</summary>
		[Tooltip("The bones to seat an emitter on. The first takes the emitter itself; the rest take copies.")]
		public HumanBodyBones[] Bones = { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot };

		/// <summary>The child that is moved onto the bones.</summary>
		[Tooltip("The child that is moved onto the bones.")]
		public Transform Emitter;

		/// <summary>The emitter's size in metres; the effect is authored to a one-metre cube.</summary>
		[Tooltip("The emitter's size in metres. The effect is authored to a one-metre cube (ParticleAdjuster).")]
		public float WorldSize = 0.35f;

		/// <summary>Offset from the bone, in world metres (up is world up).</summary>
		[Tooltip("Offset from the bone, in world metres.")]
		public Vector3 WorldOffset = Vector3.zero;

		/// <summary>
		/// Hold each seated emitter level in the world (no tilt or spin from the bone), for a shape that must lie flat, such
		/// as a ring around the ankle. Its position still follows the bone.
		/// </summary>
		[Tooltip("Hold each seated emitter level in the world (position still follows the bone). For shapes that must lie flat, like a ring around the ankle.")]
		public bool KeepUpright = false;

		/// <summary>How long to keep looking for a rig that has not loaded yet before settling at the root.</summary>
		[Tooltip("How long to keep looking for a rig that has not loaded yet before settling at the root.")]
		public float ResolveTimeoutSeconds = 3f;

		private readonly List<GameObject> seated = new List<GameObject>(2);
		private float waited;
		private bool settled;

		private void Awake()
		{
			if (Emitter != null)
			{
				SetWorldSize(Emitter, WorldSize);
			}
		}

		private void Start()
		{
			TrySeat();
		}

		private void Update()
		{
			if (settled)
			{
				return;
			}
			waited += Time.deltaTime;
			if (!TrySeat() && waited >= ResolveTimeoutSeconds)
			{
				settled = true;
			}
		}

		private void LateUpdate()
		{
			if (!KeepUpright)
			{
				return;
			}
			for (int i = 0; i < seated.Count; ++i)
			{
				GameObject emitter = seated[i];
				if (emitter != null)
				{
					emitter.transform.rotation = Quaternion.identity;
				}
			}
		}

		/// <summary>Seats the emitters if the rig can be found now.</summary>
		/// <returns>True once seated.</returns>
		private bool TrySeat()
		{
			if (settled)
			{
				return true;
			}
			if (Emitter == null || Bones == null || Bones.Length == 0)
			{
				settled = true;
				return true;
			}

			Transform searchFrom = transform.parent != null ? transform.parent : transform;
			Animator animator = FindModelAnimator(searchFrom);
			if (animator == null)
			{
				return false;
			}

			List<Transform> found = new List<Transform>(Bones.Length);
			for (int i = 0; i < Bones.Length; ++i)
			{
				Transform bone = HumanoidBones.Find(animator, animator.transform, Bones[i]);
				if (bone != null)
				{
					found.Add(bone);
				}
			}
			if (found.Count == 0)
			{
				return false;
			}

			for (int i = 0; i < found.Count; ++i)
			{
				Transform emitter = i == 0 ? Emitter : Instantiate(Emitter.gameObject).transform;
				Seat(emitter, found[i]);
				seated.Add(emitter.gameObject);
			}
			settled = true;
			return true;
		}

		private void Seat(Transform emitter, Transform bone)
		{
			emitter.SetParent(bone, false);
			emitter.localRotation = Quaternion.identity;
			if (KeepUpright)
			{
				emitter.rotation = Quaternion.identity;
			}
			emitter.position = bone.position + WorldOffset;
			SetWorldSize(emitter, WorldSize);
		}

		/// <summary>
		/// The model's animator under <paramref name="root"/>, never one belonging to an effect beside this one.
		/// </summary>
		private static Animator FindModelAnimator(Transform root)
		{
			Animator[] animators = root.GetComponentsInChildren<Animator>(true);
			for (int i = 0; i < animators.Length; ++i)
			{
				Animator candidate = animators[i];
				if (candidate != null && candidate.avatar != null && candidate.avatar.isHuman)
				{
					return candidate;
				}
			}
			return null;
		}

		/// <summary>Scales <paramref name="target"/> so its world size is <paramref name="size"/> whatever its parent's scale.</summary>
		private static void SetWorldSize(Transform target, float size)
		{
			if (size <= 0f)
			{
				return;
			}
			Vector3 parentScale = target.parent != null ? target.parent.lossyScale : Vector3.one;
			target.localScale = new Vector3(
				size / NonZero(parentScale.x),
				size / NonZero(parentScale.y),
				size / NonZero(parentScale.z));
		}

		private static float NonZero(float value)
		{
			return Mathf.Abs(value) < 1e-6f ? 1f : Mathf.Abs(value);
		}

		private void OnDestroy()
		{
			for (int i = 0; i < seated.Count; ++i)
			{
				if (seated[i] != null)
				{
					Destroy(seated[i]);
				}
			}
			seated.Clear();
		}
	}
}
