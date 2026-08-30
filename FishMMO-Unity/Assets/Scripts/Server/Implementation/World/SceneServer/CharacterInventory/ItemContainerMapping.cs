using FishMMO.Database.Data;
using FishMMO.Shared;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Translates between the gameplay container enum and the persistence one.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Two enums exist for one concept because the database assembly cannot reference the Unity
	/// shared assembly. They are numerically identical by contract — <c>ItemContainerTypeParityTests</c>
	/// pins that — so the translation is a cast, and this type exists to make every cast go through
	/// one place that says so rather than being scattered as bare <c>(byte)</c> conversions nobody
	/// would think to re-check when either enum gains a member.
	/// </para>
	/// </remarks>
	public static class ItemContainerMapping
	{
		/// <summary>The persistence container for a gameplay container.</summary>
		public static ItemContainerType ToContainerType(this InventoryType inventoryType)
		{
			return (ItemContainerType)(byte)inventoryType;
		}

		/// <summary>The gameplay container for a persistence container.</summary>
		public static InventoryType ToInventoryType(this ItemContainerType container)
		{
			return (InventoryType)(byte)container;
		}
	}
}
