using System;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Serializable snapshot of a character's full visual appearance.
	/// Used for save/load, network replication, and character creation.
	///
	/// All values are deterministic — applying the same data produces the same visual result.
	/// </summary>
	[Serializable]
	public struct CharacterAppearanceData
	{
		/// <summary>
		/// The race template ID.
		/// </summary>
		public int RaceId;

		/// <summary>
		/// The character's gender.
		/// </summary>
		public CharacterGender Gender;

		/// <summary>
		/// The model index within the race/gender model set.
		/// </summary>
		public int ModelIndex;

		// ── Bone proportions (1.0 = default for the race) ──────────────

		/// <summary>
		/// Overall height multiplier. Affects spine and leg bones.
		/// </summary>
		public float Height;

		/// <summary>
		/// Arm length multiplier. Affects upper and lower arm bones.
		/// </summary>
		public float ArmLength;

		/// <summary>
		/// Leg length multiplier. Affects upper and lower leg bones.
		/// </summary>
		public float LegLength;

		/// <summary>
		/// Torso length multiplier. Affects spine and chest bones.
		/// </summary>
		public float TorsoLength;

		/// <summary>
		/// Shoulder width multiplier. Affects clavicle/shoulder bone positions.
		/// </summary>
		public float ShoulderWidth;

		/// <summary>
		/// Head scale multiplier. Affects head and neck bones.
		/// </summary>
		public float HeadScale;

		// ── Blend shapes ───────────────────────────────────────────────

		/// <summary>
		/// Body weight blend shape (0-100).
		/// </summary>
		public float Weight;

		/// <summary>
		/// Muscle mass blend shape (0-100).
		/// </summary>
		public float MuscleMass;

		// ── Equipment ──────────────────────────────────────────────────

		/// <summary>
		/// IDs of equipped items, indexed by (byte)ItemSlot.
		/// Length must match the ItemSlot enum count.
		/// </summary>
		public long[] EquippedItemIds;

		/// <summary>
		/// Returns the default appearance for a given race.
		/// </summary>
		public static CharacterAppearanceData Default(int raceId, CharacterGender gender, int modelIndex)
		{
			int slotCount = System.Enum.GetNames(typeof(ItemSlot)).Length;
			long[] emptyEquipment = new long[slotCount];
			for (int i = 0; i < slotCount; i++)
			{
				emptyEquipment[i] = -1;
			}

			return new CharacterAppearanceData
			{
				RaceId = raceId,
				Gender = gender,
				ModelIndex = modelIndex,
				Height = 1f,
				ArmLength = 1f,
				LegLength = 1f,
				TorsoLength = 1f,
				ShoulderWidth = 1f,
				HeadScale = 1f,
				Weight = 0f,
				MuscleMass = 0f,
				EquippedItemIds = emptyEquipment,
			};
		}
	}
}
