using NUnit.Framework;

namespace FishMMO.Core.Tests
{
	public struct Position
	{
		public float X;
	}

	public struct Velocity
	{
		public float X;
	}

	public struct Marker
	{
	}

	public class EntityTests
	{
		[Test]
		public void DefaultEntityIsNullAndNeverAlive()
		{
			var world = new World();
			Assert.That(default(Entity).IsNull, Is.True);
			Assert.That(world.IsAlive(Entity.Null), Is.False);
		}

		[Test]
		public void CreatedEntitiesAreDistinctAndAlive()
		{
			var world = new World();
			Entity a = world.Create();
			Entity b = world.Create();
			Assert.That(a, Is.Not.EqualTo(b));
			Assert.That(world.IsAlive(a) && world.IsAlive(b), Is.True);
			Assert.That(world.EntityCount, Is.EqualTo(2));
		}

		[Test]
		public void ReusedSlotGetsNewGenerationAndOldHandleStaysDead()
		{
			var world = new World();
			Entity old = world.Create();
			world.Destroy(old);
			Entity reused = world.Create();
			Assert.That(reused.Index, Is.EqualTo(old.Index));
			Assert.That(reused.Generation, Is.GreaterThan(old.Generation));
			Assert.That(world.IsAlive(old), Is.False);
			Assert.That(world.IsAlive(reused), Is.True);
			Assert.That(world.Destroy(old), Is.False, "destroying a stale handle must not touch its successor");
			Assert.That(world.IsAlive(reused), Is.True);
		}

		[Test]
		public void DestroyRemovesEveryComponent()
		{
			var world = new World();
			Entity e = world.Create();
			world.Add(e, new Position { X = 1 });
			world.Add(e, new Velocity { X = 2 });
			world.Destroy(e);
			Assert.That(world.Store<Position>().Count, Is.Zero);
			Assert.That(world.Store<Velocity>().Count, Is.Zero);
			Assert.That(world.EntityCount, Is.Zero);
		}

		[Test]
		public void StaleHandleDoesNotSeeSuccessorsComponents()
		{
			var world = new World();
			Entity old = world.Create();
			world.Destroy(old);
			Entity reused = world.Create();
			world.Add(reused, new Position { X = 5 });
			Assert.That(world.Has<Position>(old), Is.False);
			Assert.That(world.Has<Position>(reused), Is.True);
		}

		[Test]
		public void ManyEntitiesGrowTheWorld()
		{
			var world = new World();
			var all = new Entity[1000];
			for (int i = 0; i < all.Length; i++)
			{
				all[i] = world.Create();
				world.Add(all[i], new Position { X = i });
			}
			for (int i = 0; i < all.Length; i++)
			{
				Assert.That(world.Get<Position>(all[i]).X, Is.EqualTo(i));
			}
		}
	}

	public class ComponentStoreTests
	{
		[Test]
		public void SetReplacesAndGetIsByReference()
		{
			var world = new World();
			Entity e = world.Create();
			world.Add(e, new Position { X = 1 });
			world.Add(e, new Position { X = 2 });
			Assert.That(world.Store<Position>().Count, Is.EqualTo(1));
			world.Get<Position>(e).X = 7;
			Assert.That(world.Get<Position>(e).X, Is.EqualTo(7));
		}

		[Test]
		public void RemoveSwapsLastIntoTheHoleAndKeepsLookupsRight()
		{
			var world = new World();
			Entity a = world.Create(), b = world.Create(), c = world.Create();
			world.Add(a, new Position { X = 1 });
			world.Add(b, new Position { X = 2 });
			world.Add(c, new Position { X = 3 });
			Assert.That(world.Remove<Position>(a), Is.True);
			Assert.That(world.Remove<Position>(a), Is.False);
			ComponentStore<Position> store = world.Store<Position>();
			Assert.That(store.Count, Is.EqualTo(2));
			Assert.That(world.Get<Position>(b).X, Is.EqualTo(2));
			Assert.That(world.Get<Position>(c).X, Is.EqualTo(3));
			Assert.That(store.EntityAt(0), Is.EqualTo(c), "the last component moves into the removed one's place");
		}

		[Test]
		public void AddingToADeadEntityThrows()
		{
			var world = new World();
			Entity e = world.Create();
			world.Destroy(e);
			Assert.Throws<System.InvalidOperationException>(() => world.Add(e, new Position()));
		}

		[Test]
		public void EmptyComponentsWork()
		{
			var world = new World();
			Entity e = world.Create();
			world.Add(e, new Marker());
			Assert.That(world.Has<Marker>(e), Is.True);
		}
	}

	public class CommandBufferTests
	{
		[Test]
		public void NothingHappensUntilPlayback()
		{
			var world = new World();
			Entity e = world.Create();
			world.Commands.Add(e, new Position { X = 1 });
			Assert.That(world.Has<Position>(e), Is.False);
			Assert.That(world.Commands.HasPending, Is.True);
			world.Commands.Playback();
			Assert.That(world.Has<Position>(e), Is.True);
			Assert.That(world.Commands.HasPending, Is.False);
		}

		[Test]
		public void AddsThenRemovesThenDestroys()
		{
			var world = new World();
			Entity kept = world.Create();
			Entity removed = world.Create();
			Entity destroyed = world.Create();

			world.Commands.Destroy(destroyed);
			world.Commands.Remove<Position>(removed);
			world.Commands.Add(removed, new Position());
			world.Commands.Add(destroyed, new Position());
			world.Commands.Add(kept, new Position());
			world.Commands.Playback();

			Assert.That(world.Has<Position>(kept), Is.True);
			Assert.That(world.Has<Position>(removed), Is.False, "a removal beats an addition in the same phase");
			Assert.That(world.IsAlive(destroyed), Is.False, "a destruction beats an addition in the same phase");
			Assert.That(world.Store<Position>().Count, Is.EqualTo(1));
		}

		[Test]
		public void ChangesToEntitiesThatDiedFirstAreSkipped()
		{
			var world = new World();
			Entity e = world.Create();
			world.Commands.Add(e, new Position());
			world.Destroy(e);
			Entity successor = world.Create();
			world.Commands.Playback();
			Assert.That(world.Has<Position>(successor), Is.False);
			Assert.That(world.Store<Position>().Count, Is.Zero);
		}

		[Test]
		public void BufferIsReusableAcrossPlaybacks()
		{
			var world = new World();
			Entity e = world.Create();
			for (int i = 0; i < 3; i++)
			{
				world.Commands.Add(e, new Position { X = i });
				world.Commands.Playback();
				Assert.That(world.Get<Position>(e).X, Is.EqualTo(i));
				world.Commands.Remove<Position>(e);
				world.Commands.Playback();
				Assert.That(world.Has<Position>(e), Is.False);
			}
		}
	}

	public class ResourceTests
	{
		private sealed class Settings
		{
			public int Value;
		}

		[Test]
		public void ResourceIsOnePerWorld()
		{
			var a = new World();
			var b = new World();
			a.Resource<Settings>().Value = 3;
			Assert.That(a.Resource<Settings>().Value, Is.EqualTo(3));
			Assert.That(b.Resource<Settings>().Value, Is.Zero);
		}
	}
}
