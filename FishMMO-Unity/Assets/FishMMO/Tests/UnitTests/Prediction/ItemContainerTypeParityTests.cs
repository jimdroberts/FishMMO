using System;
using System.Collections.Generic;
using FishMMO.Database.Data;
using FishMMO.Shared;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// <see cref="InventoryType"/> and <see cref="ItemContainerType"/> must stay numerically identical.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Two enums exist for one concept because the database assembly cannot reference the Unity
	/// shared assembly. <c>ItemContainerMapping</c> translates between them with a cast, which is
	/// correct exactly as long as the two agree — and silently wrong the moment they do not.
	/// </para>
	/// <para>
	/// <b>What "silently wrong" means here.</b> The container is a column on every item row and the
	/// discriminator the load path switches on. A one-off between the two enums would file a
	/// character's equipment as bank contents: the items would persist, load, and be placed in the
	/// wrong container, with nothing reporting an error at any point. The failure would first show up
	/// as a player logging in naked with their gear in the bank.
	/// </para>
	/// <para>
	/// This is the check that would have to be remembered otherwise, so it is written down instead.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class ItemContainerTypeParityTests
	{
		[Test]
		public void ContainerEnums_AgreeOnEveryMemberAndValue()
		{
			string[] gameplayNames = Enum.GetNames(typeof(InventoryType));
			string[] persistenceNames = Enum.GetNames(typeof(ItemContainerType));

			LogAssert.AreEqual(gameplayNames.Length, persistenceNames.Length,
				"InventoryType and ItemContainerType must have the same members. A member on one side " +
				"only is a container the other half of the system cannot name.");

			var gameplay = new Dictionary<string, byte>();
			foreach (string name in gameplayNames)
			{
				gameplay[name] = (byte)(InventoryType)Enum.Parse(typeof(InventoryType), name);
			}

			foreach (string name in persistenceNames)
			{
				LogAssert.IsTrue(gameplay.ContainsKey(name),
					$"ItemContainerType.{name} has no counterpart in InventoryType.");

				byte persistenceValue = (byte)(ItemContainerType)Enum.Parse(typeof(ItemContainerType), name);
				LogAssert.AreEqual(gameplay[name], persistenceValue,
					$"'{name}' is {gameplay[name]} as an InventoryType and {persistenceValue} as an " +
					"ItemContainerType. The mapping between them is a cast, so a mismatch files items " +
					"into the wrong container with no error anywhere.");
			}
		}

		/// <summary>
		/// The underlying type is a byte on both sides, because the column is sized for one.
		/// </summary>
		/// <remarks>
		/// Widening either enum past a byte would not fail the value comparison above — the casts
		/// would still line up for the members that exist — but it would let a member be defined
		/// that the <c>smallint</c> column and the <c>(short)</c> projection cannot carry.
		/// </remarks>
		[Test]
		public void ContainerEnums_AreBothByteBacked()
		{
			LogAssert.AreEqual(typeof(byte), Enum.GetUnderlyingType(typeof(InventoryType)),
				"InventoryType must stay byte-backed.");
			LogAssert.AreEqual(typeof(byte), Enum.GetUnderlyingType(typeof(ItemContainerType)),
				"ItemContainerType must stay byte-backed; the column is a smallint and the service " +
				"projects through (short).");
		}
	}
}
