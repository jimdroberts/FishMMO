namespace FishMMO.Shared
{
	/// <summary>
	/// Authoritative bone names for the master humanoid skeleton.
	/// Every character model and equipment mesh must use these exact bone names.
	/// Artists must never rename, add, or remove required bones.
	/// </summary>
	public static class SkeletonBones
	{
		// ── Root ───────────────────────────────────────────────────────

		/// <summary>Root bone of the skeleton hierarchy.</summary>
		public const string Root = "Root";

		/// <summary>Hips/pelvis bone.</summary>
		public const string Hips = "Hips";

		// ── Spine chain ─────────────────────────────────────────────────

		/// <summary>Lower spine bone.</summary>
		public const string Spine = "Spine";

		/// <summary>Upper chest bone.</summary>
		public const string Chest = "Chest";

		/// <summary>Neck bone.</summary>
		public const string Neck = "Neck";

		/// <summary>Head bone.</summary>
		public const string Head = "Head";

		// ── Left arm ────────────────────────────────────────────────────

		/// <summary>Left clavicle/shoulder bone.</summary>
		public const string LeftShoulder = "LeftShoulder";

		/// <summary>Left upper arm bone.</summary>
		public const string LeftUpperArm = "LeftUpperArm";

		/// <summary>Left lower arm / forearm bone.</summary>
		public const string LeftLowerArm = "LeftLowerArm";

		/// <summary>Left hand bone.</summary>
		public const string LeftHand = "LeftHand";

		// ── Right arm ───────────────────────────────────────────────────

		/// <summary>Right clavicle/shoulder bone.</summary>
		public const string RightShoulder = "RightShoulder";

		/// <summary>Right upper arm bone.</summary>
		public const string RightUpperArm = "RightUpperArm";

		/// <summary>Right lower arm / forearm bone.</summary>
		public const string RightLowerArm = "RightLowerArm";

		/// <summary>Right hand bone.</summary>
		public const string RightHand = "RightHand";

		// ── Left leg ────────────────────────────────────────────────────

		/// <summary>Left upper leg / thigh bone.</summary>
		public const string LeftUpperLeg = "LeftUpperLeg";

		/// <summary>Left lower leg / calf bone.</summary>
		public const string LeftLowerLeg = "LeftLowerLeg";

		/// <summary>Left foot bone.</summary>
		public const string LeftFoot = "LeftFoot";

		// ── Right leg ───────────────────────────────────────────────────

		/// <summary>Right upper leg / thigh bone.</summary>
		public const string RightUpperLeg = "RightUpperLeg";

		/// <summary>Right lower leg / calf bone.</summary>
		public const string RightLowerLeg = "RightLowerLeg";

		/// <summary>Right foot bone.</summary>
		public const string RightFoot = "RightFoot";

		// ── Region renderer names ───────────────────────────────────────

		/// <summary>Name of the body head SkinnedMeshRenderer GameObject.</summary>
		public const string BodyHead = "BodyHead";

		/// <summary>Name of the body torso SkinnedMeshRenderer GameObject.</summary>
		public const string BodyTorso = "BodyTorso";

		/// <summary>Name of the body arms SkinnedMeshRenderer GameObject.</summary>
		public const string BodyArms = "BodyArms";

		/// <summary>Name of the body hands SkinnedMeshRenderer GameObject.</summary>
		public const string BodyHands = "BodyHands";

		/// <summary>Name of the body legs SkinnedMeshRenderer GameObject.</summary>
		public const string BodyLegs = "BodyLegs";

		/// <summary>Name of the body feet SkinnedMeshRenderer GameObject.</summary>
		public const string BodyFeet = "BodyFeet";

		// ── Paired bone helpers (for scaling both sides) ─────────────────

		/// <summary>Upper arm bones (left and right), scaled by ArmLength.</summary>
		public static readonly string[] UpperArmBones = { LeftUpperArm, RightUpperArm };

		/// <summary>Lower arm bones (left and right), scaled by ArmLength.</summary>
		public static readonly string[] LowerArmBones = { LeftLowerArm, RightLowerArm };

		/// <summary>Upper leg bones (left and right), scaled by LegLength and Height.</summary>
		public static readonly string[] UpperLegBones = { LeftUpperLeg, RightUpperLeg };

		/// <summary>Lower leg bones (left and right), scaled by LegLength and Height.</summary>
		public static readonly string[] LowerLegBones = { LeftLowerLeg, RightLowerLeg };

		/// <summary>Shoulder/clavicle bones (left and right), scaled by ShoulderWidth.</summary>
		public static readonly string[] ShoulderBones = { LeftShoulder, RightShoulder };

		/// <summary>Hand bones (left and right).</summary>
		public static readonly string[] HandBones = { LeftHand, RightHand };

		/// <summary>Foot bones (left and right).</summary>
		public static readonly string[] FootBones = { LeftFoot, RightFoot };

		/// <summary>
		/// Spine chain bones (Spine and Chest), scaled by Height and TorsoLength.
		/// </summary>
		public static readonly string[] SpineChainBones = { Spine, Chest };

		/// <summary>
		/// Head and neck bones, scaled by HeadScale.
		/// </summary>
		public static readonly string[] HeadChainBones = { Neck, Head };

		/// <summary>
		/// Maps a BodyRegion to its renderer GameObject name.
		/// </summary>
		/// <param name="region">The body region to map.</param>
		/// <returns>The renderer GameObject name for the given region, or null if unknown.</returns>
		public static string GetRegionRendererName(BodyRegion region)
		{
			return region switch
			{
				BodyRegion.Head => BodyHead,
				BodyRegion.Torso => BodyTorso,
				BodyRegion.Arms => BodyArms,
				BodyRegion.Hands => BodyHands,
				BodyRegion.Legs => BodyLegs,
				BodyRegion.Feet => BodyFeet,
				_ => null,
			};
		}
	}
}
