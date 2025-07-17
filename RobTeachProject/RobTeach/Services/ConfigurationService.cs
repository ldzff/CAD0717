using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics; // For Debug.WriteLine
using IxMilia.Dxf; // For DxfPoint, DxfVector
using RobTeach.Models; // For Configuration
using System.Linq; // Added for LINQ methods in temporary test code

namespace RobTeach.Services
{
    // --- Custom JSON Converters for DxfPoint and DxfVector ---

    public class DxfPointJsonConverter : JsonConverter<DxfPoint>
    {
        public override DxfPoint Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("Expected StartObject token for DxfPoint");
            }

            double x = 0, y = 0, z = 0;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    return new DxfPoint(x, y, z);
                }

                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    throw new JsonException("Expected PropertyName token for DxfPoint");
                }

                string propertyName = reader.GetString();
                reader.Read(); // Move to property value

                switch (propertyName?.ToUpperInvariant()) // Case-insensitive matching
                {
                    case "X":
                        x = reader.GetDouble();
                        break;
                    case "Y":
                        y = reader.GetDouble();
                        break;
                    case "Z":
                        z = reader.GetDouble();
                        break;
                    default:
                        // Handle or ignore unknown properties
                        reader.Skip();
                        break;
                }
            }
            // Should be unreachable if logic is correct and reader doesn't throw earlier
            throw new JsonException("Expected EndObject token for DxfPoint, but loop terminated unexpectedly.");
        }

        public override void Write(Utf8JsonWriter writer, DxfPoint value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("X", value.X);
            writer.WriteNumber("Y", value.Y);
            writer.WriteNumber("Z", value.Z);
            writer.WriteEndObject();
        }
    }

    public class DxfVectorJsonConverter : JsonConverter<DxfVector>
    {
        public override DxfVector Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("Expected StartObject token for DxfVector");
            }

            double x = 0, y = 0, z = 0;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    return new DxfVector(x, y, z);
                }

                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    throw new JsonException("Expected PropertyName token for DxfVector");
                }

                string propertyName = reader.GetString();
                reader.Read(); // Move to property value

                switch (propertyName?.ToUpperInvariant()) // Case-insensitive matching
                {
                    case "X":
                        x = reader.GetDouble();
                        break;
                    case "Y":
                        y = reader.GetDouble();
                        break;
                    case "Z":
                        z = reader.GetDouble();
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }
            // Should be unreachable
            throw new JsonException("Expected EndObject token for DxfVector, but loop terminated unexpectedly.");
        }

        public override void Write(Utf8JsonWriter writer, DxfVector value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("X", value.X);
            writer.WriteNumber("Y", value.Y);
            writer.WriteNumber("Z", value.Z);
            writer.WriteEndObject();
        }
    }

    // --- End of Custom JSON Converters ---

    /// <summary>
    /// Provides services for saving and loading application configurations.
    /// Configurations are serialized to and deserialized from JSON format.
    /// </summary>
    public class ConfigurationService
    {
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true, // Good for robustness if config files are manually edited
            // PropertyNamingPolicy = JsonNamingPolicy.CamelCase, // Optional: if you prefer camelCase in JSON
            Converters = {
                new DxfPointJsonConverter(),
                new DxfVectorJsonConverter(),
                new TrajectoryJsonConverter() // Added new converter
            }
        };

        /// <summary>
        /// Saves the provided application <see cref="Configuration"/> to a JSON file.
        /// </summary>
        /// <param name="config">The <see cref="Configuration"/> object to save.</param>
        /// <param name="filePath">The path to the file where the configuration will be saved.</param>
        /// <exception cref="System.ArgumentNullException">Thrown if <paramref name="config"/> or <paramref name="filePath"/> is null.</exception>
        /// <exception cref="System.Exception">Thrown if an error occurs during JSON serialization or file writing.
        /// Specific exceptions can include <see cref="JsonException"/> or <see cref="IOException"/>.</exception>
        public void SaveConfiguration(Configuration config, string filePath)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (string.IsNullOrEmpty(filePath)) throw new ArgumentNullException(nameof(filePath));

            // Serialize the configuration object to a JSON string with indentation for readability.
            string json = JsonSerializer.Serialize(config, _jsonOptions);
            // Write the JSON string to the specified file path.
            // This will overwrite the file if it already exists.
            File.WriteAllText(filePath, json);
        }

        /// <summary>
        /// Loads an application <see cref="Configuration"/> from a JSON file.
        /// </summary>
        /// <param name="filePath">The path to the file from which the configuration will be loaded.</param>
        /// <returns>A <see cref="Configuration"/> object if deserialization is successful;
        /// otherwise, <c>null</c> if the file does not exist or if an error occurs during deserialization.</returns>
        /// <exception cref="System.ArgumentNullException">Thrown if <paramref name="filePath"/> is null or empty.</exception>
        public Configuration? LoadConfiguration(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) throw new ArgumentNullException(nameof(filePath));

            if (!File.Exists(filePath))
            {
                // File not found, return null. UI should handle this.
                Debug.WriteLine($"[ConfigurationService] Configuration file not found: {filePath}");
                return null;
            }

            string json = File.ReadAllText(filePath);

            try
            {
                // Deserialize the JSON string back into a Configuration object.
                return JsonSerializer.Deserialize<Configuration>(json, _jsonOptions);
            }
            catch (JsonException ex) // System.Text.Json.JsonException
            {
                // Log the deserialization error for debugging purposes.
                Debug.WriteLine($"[ConfigurationService] Error deserializing configuration from {filePath}: {ex.Message}");
                // Return null to indicate failure to the caller, which should handle it gracefully.
                return null;
            }
            catch (Exception ex) // Catch other potential errors during file read or deserialization setup
            {
                Debug.WriteLine($"[ConfigurationService] Unexpected error loading configuration from {filePath}: {ex.ToString()}");
                return null;
            }
        }
    }
}
