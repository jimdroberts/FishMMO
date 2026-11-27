using System;
using System.Collections.Generic;
using FishNet.Managing.Timing;
using FishMMO.Database.Data;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Between a player's mercenary as the server holds it (<see cref="MercenaryController"/>, the live
	/// <see cref="Mercenary"/>) and its database row (<see cref="CharacterMercenaryData"/>).
	/// </summary>
	public static class MercenaryRecords
	{
		/// <summary>
		/// The row for the player's mercenary now. A live one's resources and buffs are read off it; a resting (or not yet
		/// spawned) one's are the values it was saved with. Null when the player has no mercenary. Main thread.
		/// </summary>
		public static CharacterMercenaryData Capture(long characterID, MercenaryController controller, Mercenary live, TimeManager timeManager)
		{
			if (controller == null || !controller.HasMercenary || characterID <= 0)
			{
				return null;
			}

			List<MercenaryPersistedResource> resources = controller.SavedResources;
			List<MercenaryPersistedBuff> buffs = controller.SavedBuffs;
			if (live != null)
			{
				CaptureLive(live, timeManager, out resources, out buffs);
			}

			SceneObjectNamer.NameState name = controller.NameState;
			CharacterMercenaryData data = new CharacterMercenaryData()
			{
				CharacterID = characterID,
				KitID = controller.KitID,
				Name = MercenaryRules.CleanName(controller.MercenaryName),
				Resting = controller.Resting,
				Stance = (byte)controller.Stance,
				AppearanceSeed = controller.AppearanceSeed,
				NameSeed = name.Seed,
				NameGender = (byte)name.Gender,
				NameBiomeID = name.BiomeID,
				NameVariant = name.Variant,
				RaceTemplateID = controller.RaceTemplateID,
			};
			WriteResources(data, resources);
			WriteBuffs(data, buffs);
			return data;
		}

		/// <summary>Reads a live mercenary's resource values and its timed buffs (permanent ones are its kit's, never saved).</summary>
		public static void CaptureLive(Mercenary live, TimeManager timeManager, out List<MercenaryPersistedResource> resources, out List<MercenaryPersistedBuff> buffs)
		{
			resources = new List<MercenaryPersistedResource>(3);
			buffs = new List<MercenaryPersistedBuff>(4);
			if (live == null)
			{
				return;
			}
			if (live.TryGet(out ICharacterAttributeController attributes))
			{
				foreach (KeyValuePair<int, CharacterResourceAttribute> pair in attributes.ResourceAttributes)
				{
					if (pair.Value != null)
					{
						resources.Add(new MercenaryPersistedResource() { TemplateID = pair.Key, CurrentValue = pair.Value.CurrentValue });
					}
				}
			}
			if (timeManager != null && live.TryGet(out IBuffPersistence buffPersistence))
			{
				List<MercenaryPersistedBuff> into = buffs;
				buffPersistence.CaptureBuffs(timeManager.LocalTick, (float)timeManager.TickDelta,
					(templateID, remainingTime, tickTime, stacks, tickCount) => into.Add(new MercenaryPersistedBuff()
					{
						TemplateID = templateID,
						RemainingTime = remainingTime,
						TickTime = tickTime,
						Stacks = stacks,
						TickCount = tickCount,
					}));
			}
		}

		/// <summary>Puts a fetched row on the controller, as saved (nothing dirty). Pure over its arguments.</summary>
		public static void Apply(CharacterMercenaryData data, MercenaryController controller)
		{
			if (data == null || controller == null || data.KitID == 0)
			{
				return;
			}
			controller.AppearanceSeed = data.AppearanceSeed;
			controller.NameState = new SceneObjectNamer.NameState()
			{
				Seed = data.NameSeed,
				Gender = (CharacterGender)data.NameGender,
				BiomeID = data.NameBiomeID,
				Variant = data.NameVariant,
			};
			controller.RaceTemplateID = data.RaceTemplateID;
			controller.SavedResources = ReadResources(data);
			controller.SavedBuffs = ReadBuffs(data);
			PetStance stance = Enum.IsDefined(typeof(PetStance), data.Stance) ? (PetStance)data.Stance : PetStance.Defensive;
			controller.SetState(data.KitID, data.Name, data.Resting, stance);
			controller.PersistenceDirty = false;
		}

		/// <summary>The resources in a row; null when it has none.</summary>
		public static List<MercenaryPersistedResource> ReadResources(CharacterMercenaryData data)
		{
			int count = Math.Min(data.ResourceTemplateIDs?.Length ?? 0, data.ResourceValues?.Length ?? 0);
			if (count == 0)
			{
				return null;
			}
			var resources = new List<MercenaryPersistedResource>(count);
			for (int i = 0; i < count; ++i)
			{
				resources.Add(new MercenaryPersistedResource() { TemplateID = data.ResourceTemplateIDs[i], CurrentValue = data.ResourceValues[i] });
			}
			return resources;
		}

		/// <summary>The buffs in a row; null when it has none.</summary>
		public static List<MercenaryPersistedBuff> ReadBuffs(CharacterMercenaryData data)
		{
			int count = data.BuffTemplateIDs?.Length ?? 0;
			if (count == 0 || data.BuffRemaining?.Length != count || data.BuffTickTime?.Length != count ||
				data.BuffStacks?.Length != count || data.BuffTickCount?.Length != count)
			{
				return null;
			}
			var buffs = new List<MercenaryPersistedBuff>(count);
			for (int i = 0; i < count; ++i)
			{
				buffs.Add(new MercenaryPersistedBuff()
				{
					TemplateID = data.BuffTemplateIDs[i],
					RemainingTime = data.BuffRemaining[i],
					TickTime = data.BuffTickTime[i],
					Stacks = data.BuffStacks[i],
					TickCount = data.BuffTickCount[i],
				});
			}
			return buffs;
		}

		private static void WriteResources(CharacterMercenaryData data, List<MercenaryPersistedResource> resources)
		{
			int count = resources == null ? 0 : Math.Min(resources.Count, CharacterMercenaryServiceLimits.MaxEntries);
			data.ResourceTemplateIDs = new int[count];
			data.ResourceValues = new float[count];
			for (int i = 0; i < count; ++i)
			{
				data.ResourceTemplateIDs[i] = resources[i].TemplateID;
				float value = resources[i].CurrentValue;
				data.ResourceValues[i] = float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
			}
		}

		private static void WriteBuffs(CharacterMercenaryData data, List<MercenaryPersistedBuff> buffs)
		{
			int count = buffs == null ? 0 : Math.Min(buffs.Count, CharacterMercenaryServiceLimits.MaxEntries);
			data.BuffTemplateIDs = new int[count];
			data.BuffRemaining = new float[count];
			data.BuffTickTime = new float[count];
			data.BuffStacks = new int[count];
			data.BuffTickCount = new int[count];
			for (int i = 0; i < count; ++i)
			{
				data.BuffTemplateIDs[i] = buffs[i].TemplateID;
				data.BuffRemaining[i] = Finite(buffs[i].RemainingTime);
				data.BuffTickTime[i] = Finite(buffs[i].TickTime);
				data.BuffStacks[i] = buffs[i].Stacks;
				data.BuffTickCount[i] = buffs[i].TickCount;
			}
		}

		private static float Finite(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
	}

	/// <summary>The row limits the database service enforces, mirrored so a capture never writes a row it refuses.</summary>
	public static class CharacterMercenaryServiceLimits
	{
		/// <summary>The most resources or buffs one row keeps (<c>CharacterMercenaryService.MaxEntries</c>).</summary>
		public const int MaxEntries = 64;
	}
}
