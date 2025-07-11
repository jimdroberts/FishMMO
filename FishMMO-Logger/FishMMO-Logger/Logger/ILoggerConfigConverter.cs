using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq; // Add this using directive for .ToList()

namespace FishMMO.Logging
{
	/// <summary>
	/// Custom JsonConverter for ILoggerConfig to handle polymorphic deserialization.
	/// It relies on external registration of concrete ILoggerConfig types, removing the need for internal reflection.
	/// </summary>
	public class ILoggerConfigConverter : JsonConverter<ILoggerConfig>
	{
		private static readonly Dictionary<string, Type> loggerConfigTypes = new Dictionary<string, Type>();
		private static readonly object registrationLock = new object();

		/// <summary>
		/// Registers a concrete ILoggerConfig type with the converter.
		/// </summary>
		public static void RegisterConfigType(Type configType)
		{
			lock (registrationLock)
			{
				if (configType == null)
				{
					throw new ArgumentNullException(nameof(configType), "Config type cannot be null.");
				}
				if (!typeof(ILoggerConfig).IsAssignableFrom(configType) || configType.IsInterface || configType.IsAbstract)
				{
					throw new ArgumentException($"Type '{configType.Name}' must implement ILoggerConfig and be a concrete class.", nameof(configType));
				}
				if (configType.GetConstructor(Type.EmptyTypes) == null)
				{
					throw new ArgumentException($"Type '{configType.Name}' must have a public parameterless constructor to be registered.", nameof(configType));
				}

				string typeName = configType.Name;
				if (loggerConfigTypes.ContainsKey(typeName))
				{
					// Consider logging a warning here if you have a separate internal logging mechanism
				}
				loggerConfigTypes[typeName] = configType;
			}
		}

		/// <summary>
		/// Returns a read-only dictionary of all currently registered ILoggerConfig types.
		/// </summary>
		public static IReadOnlyDictionary<string, Type> GetAllRegisteredConfigTypes()
		{
			lock (registrationLock)
			{
				return new Dictionary<string, Type>(loggerConfigTypes);
			}
		}

		public override ILoggerConfig Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			if (reader.TokenType != JsonTokenType.StartObject)
			{
				throw new JsonException();
			}

			using (JsonDocument doc = JsonDocument.ParseValue(ref reader))
			{
				if (!doc.RootElement.TryGetProperty("Type", out JsonElement typeElement))
				{
					throw new JsonException("Missing 'Type' discriminator property in ILoggerConfig JSON.");
				}

				string typeName = typeElement.GetString();
				if (string.IsNullOrWhiteSpace(typeName))
				{
					throw new JsonException("The 'Type' discriminator property for ILoggerConfig cannot be null or empty.");
				}

				if (!loggerConfigTypes.TryGetValue(typeName, out Type concreteType))
				{
					throw new JsonException($"Unknown or unregistered ILoggerConfig type '{typeName}'. Ensure it has been registered via ILoggerConfigConverter.RegisterConfigType().");
				}

				var innerOptions = new JsonSerializerOptions(options);

				// Remove this specific converter to prevent infinite recursion during inner deserialization
				foreach (var converter in innerOptions.Converters.ToList()) // .ToList() to safely modify collection during iteration
				{
					if (converter is ILoggerConfigConverter)
					{
						innerOptions.Converters.Remove(converter);
					}
				}

				return (ILoggerConfig)JsonSerializer.Deserialize(doc.RootElement.GetRawText(), concreteType, innerOptions);
			}
		}

		public override void Write(Utf8JsonWriter writer, ILoggerConfig value, JsonSerializerOptions options)
		{
			var innerOptions = new JsonSerializerOptions(options);

			// Remove this specific converter to prevent infinite recursion
			foreach (var converter in innerOptions.Converters.ToList())
			{
				if (converter is ILoggerConfigConverter)
				{
					innerOptions.Converters.Remove(converter);
				}
			}

			// This relies on the concrete type's default serialization.
			JsonSerializer.Serialize(writer, value, value.GetType(), innerOptions);
		}
	}
}