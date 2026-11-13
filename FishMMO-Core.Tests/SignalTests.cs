using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace FishMMO.Core.Tests
{
	public readonly struct GuildCreated
	{
		public readonly int Character;

		public GuildCreated(int character)
		{
			Character = character;
		}
	}

	public struct Bag
	{
		public List<int> Items;
	}

	public class SignalTests
	{
		private sealed class Listening : GameModule
		{
			private readonly string id;
			private readonly Action<ModuleBuilder> install;

			public Listening(string id, Action<ModuleBuilder> install)
			{
				this.id = id;
				this.install = install;
			}

			public override string Id => id;
			public override void Install(ModuleBuilder builder) => install(builder);
		}

		[Test]
		public void APublishedSignalIsHandledBeforePublishReturns()
		{
			var world = new World();
			var counted = new List<int>();
			ModuleCatalog.Resolve(new GameModule[]
			{
				new Listening("achievements", b => b.OnSignal(SimulationSide.Server, (World w, in GuildCreated g) => counted.Add(g.Character))),
			}).Install(world, new SystemScheduler(world), SimulationSide.Server);

			world.Publish(new GuildCreated(7));
			Assert.That(counted, Is.EqualTo(new[] { 7 }), "no tick in between: the progress is there for a save that follows at once");
		}

		[Test]
		public void SignalsOnTheOtherSideAreNotInstalled()
		{
			var world = new World();
			int heard = 0;
			ModuleCatalog.Resolve(new GameModule[]
			{
				new Listening("achievements", b => b.OnSignal(SimulationSide.Server, (World w, in GuildCreated g) => heard++)),
			}).Install(world, new SystemScheduler(world), SimulationSide.Client);
			world.Publish(new GuildCreated(1));
			Assert.That(heard, Is.Zero);
			Assert.That(world.Resource<Signals>().HasListeners<GuildCreated>(), Is.False);
		}

		[Test]
		public void AThrowingListenerIsReportedAndTheOthersStillRun()
		{
			var world = new World();
			var signals = world.Resource<Signals>();
			var failures = new List<Type>();
			signals.HandlerFailed = (type, ex) => failures.Add(type);
			int second = 0;
			signals.Subscribe((World w, in GuildCreated g) => throw new InvalidOperationException("broken module"));
			signals.Subscribe((World w, in GuildCreated g) => second++);

			Assert.DoesNotThrow(() => world.Publish(new GuildCreated(3)));
			Assert.That(second, Is.EqualTo(1));
			Assert.That(failures, Is.EqualTo(new[] { typeof(GuildCreated) }));
			Assert.That(signals.Failures, Is.EqualTo(1));
		}

		[Test]
		public void PublishingWithNoListenersDoesNothing()
		{
			var world = new World();
			Assert.DoesNotThrow(() => world.Publish(new GuildCreated(1)));
		}

		[Test]
		public void UnsubscribedListenersStopHearing()
		{
			var world = new World();
			int heard = 0;
			SignalHandler<GuildCreated> handler = (World w, in GuildCreated g) => heard++;
			world.Resource<Signals>().Subscribe(handler);
			world.Publish(new GuildCreated(1));
			Assert.That(world.Resource<Signals>().Unsubscribe(handler), Is.True);
			world.Publish(new GuildCreated(2));
			Assert.That(heard, Is.EqualTo(1));
		}
	}

	public class EntityLinkTests
	{
		[Test]
		public void ALinkReadsAndWritesItsEntitysComponents()
		{
			var world = new World();
			Entity e = world.Create();
			world.Add(e, new Position { X = 1 });
			var link = new EntityLink(world, e);
			Assert.That(link.IsValid && link.Has<Position>(), Is.True);
			link.Get<Position>().X = 5;
			Assert.That(world.Get<Position>(e).X, Is.EqualTo(5));
			Assert.That(link.TryGet(out Position copy) && copy.X == 5, Is.True);
			Assert.That(link.TryGet(out Velocity _), Is.False);
		}

		[Test]
		public void ALinkToADestroyedEntityIsInvalid()
		{
			var world = new World();
			Entity e = world.Create();
			var link = new EntityLink(world, e);
			world.Destroy(e);
			Assert.That(link.IsValid, Is.False);
			Assert.That(link.Has<Position>(), Is.False);
			Assert.That(EntityLink.None.IsValid, Is.False);
			Assert.Throws<InvalidOperationException>(() => EntityLink.None.Get<Position>());
		}

		[Test]
		public void LinksAreEqualOnlyForTheSameWorldAndEntity()
		{
			var a = new World();
			var b = new World();
			Entity ea = a.Create();
			Entity eb = b.Create();
			Assert.That(new EntityLink(a, ea), Is.EqualTo(new EntityLink(a, ea)));
			Assert.That(new EntityLink(a, ea), Is.Not.EqualTo(new EntityLink(b, eb)), "same index and generation in two worlds is two entities");
		}
	}

	public class ArchetypeFactoryTests
	{
		private sealed class Bags : GameModule
		{
			public override string Id => "bags";
			public override void Install(ModuleBuilder builder) =>
				builder.Archetype(Archetypes.Player).Add(() => new Bag { Items = new List<int>() });
		}

		[Test]
		public void EachEntityGetsItsOwnComponentFromAFactory()
		{
			var world = new World();
			ModuleCatalog.Resolve(new GameModule[] { new Bags() }).Install(world, new SystemScheduler(world), SimulationSide.Server);
			Entity a = world.Spawn(Archetypes.Player);
			Entity b = world.Spawn(Archetypes.Player);
			world.Get<Bag>(a).Items.Add(1);
			Assert.That(world.Get<Bag>(b).Items, Is.Empty, "one player's list is never another's");
			Assert.That(world.Resource<Archetypes>().PartsOf(Archetypes.Player).Select(p => p.Description), Does.Contain(nameof(Bag)));
		}
	}
}
