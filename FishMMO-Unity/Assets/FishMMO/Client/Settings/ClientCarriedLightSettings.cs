using FishMMO.Shared;
using FishMMO.Shared.Core;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// The torch or lantern the player's own character carries (<see cref="CarriedLight"/>): which one, remembered between
	/// sessions, and stepped by the carried-light key (Off, Torch, Lantern).
	/// </summary>
	/// <remarks>
	/// <para>
	/// Kept in the settings store rather than on the character because it is how this player likes to see, not something
	/// the character owns: the same choice comes back on every character and every login, as the look sensitivity does.
	/// </para>
	/// <para>
	/// <b>Seen by this client alone.</b> Nothing replicated says what a character carries: <see cref="CharacterFlags"/>
	/// are the server's to set, and the movement replicate's flags are input the simulation reads. A light is neither, so
	/// other players do not see it, rather than borrowing a channel meant for something else.
	/// </para>
	/// </remarks>
	public static class ClientCarriedLightSettings
	{
		/// <summary>Where the local character's light hangs, or null while there is no local character.</summary>
		private static Transform holder;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			holder = null;
		}

		/// <summary>What the player last chose to carry; nothing on a fresh install.</summary>
		public static CarriedLightMode Mode
		{
			get
			{
				int value = ClientSettings.GetInt(ClientSettings.CarriedLightModeKey, (int)CarriedLightMode.Off);
				return value >= (int)CarriedLightMode.Off && value <= (int)CarriedLightMode.Lantern ? (CarriedLightMode)value : CarriedLightMode.Off;
			}
		}

		/// <summary>Hangs the saved choice on the local character as it enters the world.</summary>
		public static void Begin(ICharacter character)
		{
			holder = CarriedLight.HolderFor(character);
			CarriedLight.Attach(holder, Mode);
		}

		/// <summary>Forgets the local character as it leaves; its light goes with its object.</summary>
		public static void End()
		{
			holder = null;
		}

		/// <summary>Steps to the next mode, shows it at once, and remembers it.</summary>
		public static void Cycle()
		{
			// From what is lit, when there is one: it is what the player sees, even with no store to read back from.
			CarriedLight carried = CarriedLight.Find(holder);
			SetMode(CarriedLight.Next(carried != null ? carried.Mode : Mode));
		}

		/// <summary>Carries <paramref name="mode"/> and remembers it.</summary>
		public static void SetMode(CarriedLightMode mode)
		{
			ClientSettings.Set(ClientSettings.CarriedLightModeKey, (int)mode);
			if (holder != null)
			{
				CarriedLight.Attach(holder, mode);
			}
		}
	}
}
