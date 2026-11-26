using FishMMO.Server.Implementation.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// <see cref="CharacterSystem.IsGone"/>: a destroyed character must read as gone through its interface.
	/// </summary>
	/// <remarks>
	/// The scene server held a reference to a character whose object had been destroyed. <c>character == null</c> on the
	/// interface said it was there, activating it threw, the kick's teardown threw on it before handing back the claim, and
	/// the player could not log in anywhere until the server restarted.
	/// </remarks>
	public class CharacterGoneTests
	{
		[Test]
		public void ADestroyedCharacter_IsGone_ThoughTheInterfaceSaysItIsThere()
		{
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
			var host = new GameObject("GoneProbeCharacter");
			host.SetActive(false);
			IPlayerCharacter character = host.AddComponent<PlayerCharacter>();
			character.ID = 12;

			Assert.IsFalse(CharacterSystem.IsGone(character), "a live character is not gone");

			Object.DestroyImmediate(host);

			Assert.IsFalse(character == null, "the trap: the interface comparison does not see the destroyed object");
			Assert.IsTrue(CharacterSystem.IsGone(character), "IsGone sees it");
			Assert.AreEqual(12, character.ID, "its plain properties still read, which is what lets its claim be released");
		}

		[Test]
		public void NoCharacter_IsGone()
		{
			Assert.IsTrue(CharacterSystem.IsGone(null));
		}
	}
}
