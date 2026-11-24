using FishMMO.Shared;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The rider's figure on and off the saddle: <see cref="CharacterBody.Place"/> lifts the TestHuman to a mount's saddle, and
	/// brings it back down to the capsule's feet when it gets off (by casting, by pressing Mount again, or by losing the buff),
	/// however far above the feet's ground probe the saddle held it.
	/// </summary>
	[TestFixture]
	public class MountRiderLiftTests
	{
		private const string RiderPrefab = "Assets/Prefabs/Client/Models/TestHuman/TestHuman.prefab";
		private const float Dt = 1f / 60f;

		private GameObject rider;
		private CharacterBody body;

		[SetUp]
		public void SetUp()
		{
			GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RiderPrefab);
			Assert.That(prefab, Is.Not.Null, RiderPrefab);
			var root = new GameObject("MountRiderLiftTests");
			rider = root;
			Object.Instantiate(prefab, root.transform);
			CharacterModel model = root.GetComponentInChildren<CharacterModel>(true);
			Assert.That(model, Is.Not.Null, "the TestHuman carries its CharacterModel");
			body = CharacterBody.Create(model.GetComponent<Animator>(), model.Animations, "MountRiderLiftTests");
			Assert.That(body, Is.Not.Null, "the TestHuman is animated");
		}

		[TearDown]
		public void TearDown()
		{
			body?.Dispose();
			if (rider != null)
			{
				Object.DestroyImmediate(rider);
			}
		}

		private void Frames(float seconds)
		{
			for (float t = 0f; t < seconds; t += Dt)
			{
				body.Place(Dt);
			}
		}

		[TestCase(1.49f, TestName = "Cow saddle")]
		[TestCase(1.60f, TestName = "Horse saddle")]
		[TestCase(1.83f, TestName = "Dragon saddle")]
		public void GettingOff_BringsTheRiderBackToTheGround(float saddleHeight)
		{
			body.SetRiding(true, saddleHeight);
			Frames(1f);
			float seated = saddleHeight - body.KeyedRideHipHeight;
			Assert.That(body.Lift, Is.EqualTo(seated).Within(0.01f), "astride, the hips sit on the saddle");
			Assert.That(seated, Is.GreaterThan(0.6f), "a saddle this high lifts the feet past their ground probe");

			body.SetRiding(false, 0f);
			Frames(1f);
			Assert.That(body.Lift, Is.EqualTo(0f).Within(0.01f), "off the mount, it stands on the capsule's feet again");
		}

		[Test]
		public void Remounting_LiftsItBackUp()
		{
			body.SetRiding(true, 1.6f);
			Frames(1f);
			body.SetRiding(false, 0f);
			Frames(1f);
			body.SetRiding(true, 1.6f);
			Frames(1f);
			Assert.That(body.Lift, Is.EqualTo(1.6f - body.KeyedRideHipHeight).Within(0.01f));
		}
	}
}
