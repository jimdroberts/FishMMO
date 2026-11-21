using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using FishNet.Serializing;
using FishNet.CodeGenerating;
using UnityEditor;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The knowledge a character owns, through the serializer FishNet actually uses to ship it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The Knowledge tab is fed by broadcasts, not by the spawn payload: <c>ReadPayload</c> CLEARS
	/// <c>KnownBaseAbilities</c> and <c>WritePayload</c> never writes them, so a bought template
	/// reaches the client only through <see cref="KnownAbilityAddBroadcast"/> (the purchase) and
	/// <see cref="KnownAbilityAddMultipleBroadcast"/> (the login sync, built by
	/// <c>SendNonDbCharacterData</c>). Nothing else carries them.
	/// </para>
	/// <para>
	/// A struct marked <c>[UseGlobalCustomSerializer]</c> tells FishNet its wire format is hand
	/// written, and codegen then declines to synthesise one for an ARRAY of that struct. The gap is
	/// silent at build time and surfaces at send time as "Write method not found for X[]" — the
	/// broadcast is never written, so the client is never told. That is issue #265: the templates
	/// are in the database, restored on the server, and never displayed.
	/// </para>
	/// <para>
	/// <see cref="BroadcastArraySerializerTests"/> checks the same pairing by reading source, which
	/// cannot see whether FishNet ever finds the method it demands — the method existing and the
	/// method being wired into <c>GenericWriter&lt;T&gt;</c> are different claims. These tests drive
	/// <c>Writer.Write&lt;T&gt;</c> and <c>Reader.Read&lt;T&gt;</c>, which is the exact pair of calls
	/// the transport makes, over the real merchant's own content.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class KnowledgeSyncWireTests
	{
		private const string MerchantPath =
			"Assets/FishMMO/Modules/Commerce/Templates/NPCs/Human/Merchants/GeneralGoods/GeneralGoods.asset";

		/// <summary>Entries pushed into the static cache by a test, removed again on teardown.</summary>
		private readonly List<ICachedObject> cached = new List<ICachedObject>();

		/// <summary>Registers FishNet's generated serializers (<see cref="FishMMO.UnitTests.Harness.WireRoundTrip"/>).</summary>
		private static void EnsureSerializers() => FishMMO.UnitTests.Harness.WireRoundTrip.EnsureSerializers();

		/// <summary>Why the serializers could not be registered, or null.</summary>
		private static string serializerFailure => FishMMO.UnitTests.Harness.WireRoundTrip.SerializerFailure;

		[TearDown]
		public void TearDown()
		{
			foreach (ICachedObject entry in cached)
			{
				entry.RemoveFromCache();
			}
			cached.Clear();
		}

		/// <summary>The merchant template a player buys ability templates from.</summary>
		private static MerchantTemplate Merchant()
		{
			MerchantTemplate merchant = AssetDatabase.LoadAssetAtPath<MerchantTemplate>(ProjectFiles.Path(MerchantPath));
			LogAssert.IsNotNull(merchant, $"the merchant template must exist at {MerchantPath}");
			return merchant;
		}

		/// <summary>
		/// Registers a template the way the addressables loader does, so its <c>ID</c> is real.
		/// </summary>
		/// <remarks>
		/// The id is what travels the wire and what the client looks the template back up by. An
		/// asset loaded through <c>AssetDatabase</c> reports zero until this runs, and a zero id
		/// would make these tests pass over a value no client ever receives.
		/// </remarks>
		private T Cached<T>(T template) where T : UnityEngine.ScriptableObject, ICachedObject
		{
			template.AddToCache(template.name);
			cached.Add(template);
			return template;
		}

		/// <summary>Writes a broadcast and reads it back through FishNet's own generic entry points.</summary>
		/// <remarks>
		/// Deliberately not the hand-written extension methods: the question these tests ask is
		/// whether <c>GenericWriter&lt;T&gt;.Write</c> was wired up, and calling
		/// <c>WriteKnownAbilityAddBroadcast</c> directly would answer a different one.
		/// </remarks>
		/// <summary>Writes and reads a value through FishNet's generic serializers.</summary>
		public static T RoundTrip<T>(T value) => FishMMO.UnitTests.Harness.WireRoundTrip.RoundTrip(value);

		/// <summary>
		/// The harness itself. If FishNet's serializers are not initialised under the test runner,
		/// every other failure in this fixture says nothing about the game.
		/// </summary>
		[Test]
		public void TheGenericWriterIsInitialisedAtAll()
		{
			/* A control, and the reason this fixture reads the way it does: a failure here means the
			 * runner, not the game. It is also the cheapest possible detector for the whole class of
			 * defect — one value that is known to be serialisable in production. */
			EnsureSerializers();
			LogAssert.IsNull(serializerFailure, serializerFailure);

			Writer writer = new Writer();
			writer.Write<int>(1234567);

			Reader reader = new Reader(writer.GetArraySegment(), null);
			LogAssert.AreEqual(1234567, reader.Read<int>(),
				"a plain int must survive Write<T>/Read<T>, or the generated serializers never registered");
		}

		/// <summary>Guards the fixture: the content these tests round-trip must exist.</summary>
		[Test]
		public void TheMerchantSellsTemplatesAndEventsWithRealIDs()
		{
			MerchantTemplate merchant = Merchant();

			LogAssert.IsNotNull(merchant.Abilities, "the merchant must declare an abilities list");
			LogAssert.IsTrue(merchant.Abilities.Count > 0, "the merchant must actually sell ability templates");

			foreach (AbilityTemplate template in merchant.Abilities)
			{
				LogAssert.IsNotNull(template, "the merchant must not carry an empty offer slot");
				Cached(template);
				LogAssert.AreNotEqual(0, template.ID,
					$"'{template.name}' must receive a deterministic id, or the wire carries zero");
			}

			foreach (AbilityEvent abilityEvent in merchant.AbilityEvents)
			{
				LogAssert.IsNotNull(abilityEvent, "the merchant must not carry an empty event slot");
				Cached(abilityEvent);
				LogAssert.AreNotEqual(0, abilityEvent.ID,
					$"'{abilityEvent.name}' must receive a deterministic id, or the wire carries zero");
			}
		}

		/// <summary>
		/// The login sync: every known template the server has, in one array, arriving as it was sent.
		/// </summary>
		/// <remarks>
		/// This is the payload <c>SendNonDbCharacterData</c> builds the moment a character spawns, and
		/// it is the only thing that puts a previously bought template back into the Knowledge tab.
		/// Built here exactly as the server builds it, from the merchant's own offers.
		/// </remarks>
		[Test]
		public void EveryTemplateTheMerchantSellsSurvivesTheLoginSync()
		{
			MerchantTemplate merchant = Merchant();
			List<AbilityTemplate> sold = new List<AbilityTemplate>(merchant.Abilities);

			List<KnownAbilityAddBroadcast> entries = new List<KnownAbilityAddBroadcast>();
			foreach (AbilityTemplate template in sold)
			{
				Cached(template);
				entries.Add(new KnownAbilityAddBroadcast { TemplateID = template.ID });
			}

			KnownAbilityAddMultipleBroadcast sent = new KnownAbilityAddMultipleBroadcast
			{
				Abilities = entries.ToArray(),
			};

			KnownAbilityAddMultipleBroadcast received = RoundTrip(sent);

			LogAssert.IsNotNull(received.Abilities,
				"the login sync must arrive carrying an array, not null — a null here is an empty Knowledge tab");
			LogAssert.AreEqual(sold.Count, received.Abilities.Length,
				$"all {sold.Count} known templates must survive the login sync");

			for (int i = 0; i < sold.Count; ++i)
			{
				int id = received.Abilities[i].TemplateID;
				LogAssert.AreEqual(sold[i].ID, id,
					$"'{sold[i].name}' must arrive with the id it was sent with");

				/* The client's handler resolves each id through this lookup and drops what it cannot
				 * name — the last place the row can be lost before the panel sees it. */
				LogAssert.AreSame(sold[i], BaseAbilityTemplate.Get<BaseAbilityTemplate>(id),
					$"the abilities panel's lookup must find the '{sold[i].name}' the server sent");
			}
		}

		/// <summary>
		/// The purchase itself: one template, in the broadcast the merchant sends when it is bought.
		/// </summary>
		/// <remarks>
		/// The tab is named "Templates" on the merchant and sells into knowledge rather than into
		/// the craftable set, so this is the message that has to arrive for a purchase to show up
		/// before a relog.
		/// </remarks>
		[Test]
		public void A_TemplateBoughtFromTheMerchantSurvivesThePurchaseBroadcast()
		{
			MerchantTemplate merchant = Merchant();
			AbilityTemplate template = merchant.Abilities[0];
			LogAssert.IsNotNull(template, "the merchant must sell at least one template");
			Cached(template);

			KnownAbilityAddBroadcast received = RoundTrip(new KnownAbilityAddBroadcast
			{
				TemplateID = template.ID,
			});

			LogAssert.AreEqual(template.ID, received.TemplateID,
				$"a bought '{template.name}' must arrive with the id the server granted");
			LogAssert.AreSame(template, BaseAbilityTemplate.Get<BaseAbilityTemplate>(received.TemplateID),
				"the client must be able to name the template it was just told about");
		}

		/// <summary>
		/// The event half of the same sync, which the Knowledge tab's Effects filter reads.
		/// </summary>
		[Test]
		public void EveryAbilityEventTheMerchantSellsSurvivesTheLoginSync()
		{
			MerchantTemplate merchant = Merchant();
			List<AbilityEvent> sold = new List<AbilityEvent>(merchant.AbilityEvents);
			LogAssert.IsTrue(sold.Count > 0, "the merchant must sell at least one ability event");

			List<KnownAbilityEventAddBroadcast> entries = new List<KnownAbilityEventAddBroadcast>();
			foreach (AbilityEvent abilityEvent in sold)
			{
				Cached(abilityEvent);
				entries.Add(new KnownAbilityEventAddBroadcast { TemplateID = abilityEvent.ID });
			}

			KnownAbilityEventAddMultipleBroadcast received = RoundTrip(new KnownAbilityEventAddMultipleBroadcast
			{
				AbilityEvents = entries.ToArray(),
			});

			LogAssert.IsNotNull(received.AbilityEvents,
				"the event sync must arrive carrying an array, not null");
			LogAssert.AreEqual(sold.Count, received.AbilityEvents.Length,
				$"all {sold.Count} known events must survive the login sync");

			for (int i = 0; i < sold.Count; ++i)
			{
				LogAssert.AreEqual(sold[i].ID, received.AbilityEvents[i].TemplateID,
					$"'{sold[i].name}' must arrive with the id it was sent with");
				LogAssert.AreSame(sold[i], AbilityEvent.Get<AbilityEvent>(received.AbilityEvents[i].TemplateID),
					$"the abilities panel must be able to name '{sold[i].name}'");
			}
		}

		/// <summary>
		/// Every broadcast type can be written at all.
		/// </summary>
		/// <remarks>
		/// The floor beneath every feature that talks to a client: a broadcast FishNet holds no
		/// writer for is a message that never leaves the server, and the failure is a log line on a
		/// machine nobody is reading plus a screen that stays empty.
		/// </remarks>
		[Test]
		public void EveryBroadcastTypeCanBeWritten()
		{
			EnsureSerializers();
			LogAssert.IsNull(serializerFailure, serializerFailure);

			List<string> unserializable = new List<string>();
			int checked_ = 0;

			foreach (System.Type type in BroadcastTypes())
			{
				++checked_;
				if (!Reachable(type))
				{
					unserializable.Add(type.Name);
				}
			}

			LogAssert.IsTrue(checked_ > 0, "there must be broadcasts to check");
			LogAssert.IsTrue(unserializable.Count == 0,
				"FishNet has no writer or reader for these broadcast types, so nothing that sends one " +
				"gets through: " + string.Join(", ", unserializable));
		}

		/// <summary>
		/// Every array FishNet is asked to write through codegen has a serializer it actually holds.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The runtime twin of
		/// <see cref="BroadcastArraySerializerTests.EveryCustomSerializedBroadcastSentAsAnArrayHasAnArraySerializer"/>,
		/// and the reason both exist: that one reads source and can only see that a method was
		/// written, while this asks FishNet whether it found one. A method that exists and is never
		/// wired into <c>GenericWriter&lt;T&gt;</c> is the same empty screen as no method at all, and
		/// the send fails with nothing but a log line to say so.
		/// </para>
		/// <para>
		/// That source scan also has a blind spot this does not: it finds array FIELDS by looking for
		/// the text <c>Broadcast[]</c>, so an array of a hand-serialized element that is not itself
		/// named <c>*Broadcast</c> — <c>ArenaMemberEntry[]</c>, <c>ObservedBuffEntry[]</c> — is
		/// invisible to it. This walks the declarations instead of the text, so the element's name
		/// does not matter, and it covers every payload struct in the assembly rather than the
		/// broadcasts alone.
		/// </para>
		/// <para>
		/// <b>Why the container is checked first.</b> A struct carrying the attribute has its wire
		/// format written by hand, and that hand-written method writes its array FIELDS element by
		/// element — <c>writer.WriteArenaMemberEntry(value.Members[i])</c> — so it never asks for an
		/// array serializer at all, and demanding one would be demanding code nobody needs. What such
		/// a struct has to have instead is a live serializer for ITSELF. Only a struct codegen
		/// generates a serializer for reaches for <c>GenericWriter&lt;T[]&gt;</c>, and only there is a
		/// missing array serializer a defect.
		/// </para>
		/// <para>
		/// A struct FishNet holds no serializer for at all is skipped rather than reported: it is
		/// never on the wire, so nothing about it can fail there. <see
		/// cref="EveryBroadcastTypeCanBeWritten"/> is what says a payload did not silently land in
		/// that category.
		/// </para>
		/// </remarks>
		[Test]
		public void EverySerializedArrayOnTheWireHasALiveSerializer()
		{
			EnsureSerializers();
			LogAssert.IsNull(serializerFailure, serializerFailure);

			List<string> missing = new List<string>();
			int arraysChecked = 0;

			foreach (System.Type type in SerializedStructs())
			{
				foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
				{
					System.Type fieldType = field.FieldType;
					if (!fieldType.IsArray)
					{
						continue;
					}

					System.Type element = fieldType.GetElementType();
					if (!element.IsDefined(typeof(UseGlobalCustomSerializerAttribute), false))
					{
						/* Codegen covers the element AND the array, so there is nothing to pair up. */
						continue;
					}

					++arraysChecked;

					if (!Reachable(fieldType))
					{
						missing.Add($"{type.Name}.{field.Name} ({element.Name}[])");
					}
				}
			}

			LogAssert.IsTrue(arraysChecked > 0, "there must be payload array fields to check");
			LogAssert.IsTrue(missing.Count == 0,
				"these fields carry an array of a type with [UseGlobalCustomSerializer], their own " +
				"struct does not, so codegen writes the struct through GenericWriter<T[]> — and " +
				"codegen does not synthesise the array serializer for such an element. The send " +
				"fails at runtime and the field arrives empty: " + string.Join(", ", missing));
		}

		/// <summary>
		/// Every struct in the game assemblies that FishNet writes for codegen to build, minus the
		/// hand-written ones whose own serializer owns their arrays.
		/// </summary>
		private static List<System.Type> SerializedStructs()
		{
			List<System.Type> types = new List<System.Type>();

			foreach (System.Type type in AssemblyTypes(typeof(KnownAbilityAddBroadcast)))
			{
				if (!type.IsValueType)
				{
					continue;
				}

				if (type.IsDefined(typeof(UseGlobalCustomSerializerAttribute), false))
				{
					/* Hand written: its own method walks its array fields element by element. */
					continue;
				}

				if (!Reachable(type))
				{
					/* Nothing serializes it, so nothing about it can fail on the wire. */
					continue;
				}

				types.Add(type);
			}

			return types;
		}

		/// <summary>Every type in the assembly, tolerating types a missing reference hides.</summary>
		private static System.Type[] AssemblyTypes(System.Type anchor)
		{
			try
			{
				return anchor.Assembly.GetTypes();
			}
			catch (ReflectionTypeLoadException ex)
			{
				return System.Array.FindAll(ex.Types, t => t != null);
			}
		}

		/// <summary>True when FishNet holds a writer and a reader for this exact type.</summary>
		/// <remarks>
		/// Both, and separately: a type with a writer and no reader fails in the other direction, on
		/// the receiving peer, which is a harder failure to trace back to the send.
		/// </remarks>
		private static bool Reachable(System.Type type)
		{
			return GenericDelegate(typeof(GenericWriter<>), "Write", type) != null &&
				GenericDelegate(typeof(GenericReader<>), "Read", type) != null;
		}

		/// <summary>The delegate FishNet looks a type's serializer up through, or null.</summary>
		private static object GenericDelegate(System.Type generic, string property, System.Type argument)
		{
			return generic.MakeGenericType(argument)
				.GetProperty(property, BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
				?.GetValue(null);
		}

		/// <summary>Every broadcast struct defined in the assembly the ability broadcasts live in.</summary>
		private static List<System.Type> BroadcastTypes()
		{
			List<System.Type> types = new List<System.Type>();

			foreach (System.Type type in AssemblyTypes(typeof(KnownAbilityAddBroadcast)))
			{
				if (type.IsValueType && typeof(FishNet.Broadcast.IBroadcast).IsAssignableFrom(type))
				{
					types.Add(type);
				}
			}

			return types;
		}
	}
}
