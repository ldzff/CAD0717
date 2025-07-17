using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using RobTeach.Models;
using IxMilia.Dxf; // For DxfPoint and DxfVector
using IxMilia.Dxf.Entities; // For DxfLine, DxfArc, DxfCircle etc.
using RobTeach.Utils; // Added for GeometryUtils

namespace RobTeach.Services
{
    public class TrajectoryJsonConverter : JsonConverter<Trajectory>
    {
        public override Trajectory Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("Expected StartObject token");
            }

            using (JsonDocument jsonDocument = JsonDocument.ParseValue(ref reader))
            {
                JsonElement root = jsonDocument.RootElement;
                var trajectory = new Trajectory();

                // Helper function to get string property
                string GetStringProperty(JsonElement element, string propertyName)
                {
                    return element.TryGetProperty(propertyName, out JsonElement property) && property.ValueKind == JsonValueKind.String
                           ? property.GetString() ?? string.Empty // Ensure null is converted to empty string if GetString() returns null
                           : string.Empty;
                }

                // Helper function for boolean property
                bool GetBooleanProperty(JsonElement element, string propertyName, bool defaultValue = false)
                {
                    return element.TryGetProperty(propertyName, out JsonElement property) && (property.ValueKind == JsonValueKind.True || property.ValueKind == JsonValueKind.False)
                           ? property.GetBoolean()
                           : defaultValue;
                }

                // Helper function for int property
                int GetIntProperty(JsonElement element, string propertyName, int defaultValue = 0)
                {
                    return element.TryGetProperty(propertyName, out JsonElement property) && property.ValueKind == JsonValueKind.Number
                           ? property.GetInt32()
                           : defaultValue;
                }

                // Helper function for double property
                double GetDoubleProperty(JsonElement element, string propertyName, double defaultValue = 0.0)
                {
                    return element.TryGetProperty(propertyName, out JsonElement property) && property.ValueKind == JsonValueKind.Number
                           ? property.GetDouble()
                           : defaultValue;
                }

                // Helper function for DxfPoint
                DxfPoint GetDxfPointProperty(JsonElement element, string propertyName, JsonSerializerOptions options)
                {
                    return element.TryGetProperty(propertyName, out JsonElement property)
                           ? JsonSerializer.Deserialize<DxfPoint>(property.GetRawText(), options)
                           : DxfPoint.Origin;
                }

                // Helper function for DxfVector
                DxfVector GetDxfVectorProperty(JsonElement element, string propertyName, JsonSerializerOptions options)
                {
                    return element.TryGetProperty(propertyName, out JsonElement property)
                           ? JsonSerializer.Deserialize<DxfVector>(property.GetRawText(), options)
                           // Note: DxfVector is a struct, so ?? DxfVector.Zero might also be redundant if Deserialize never returns null for structs.
                           // However, for consistency with potential future changes or if it were a class, keeping it for DxfVector for now.
                           // Or, if DxfVectorJsonConverter handles missing properties by returning default, this ?? is also not strictly needed.
                           // For this change, only DxfPoint was specified.
                           // The above comment is now outdated as we are removing the ?? DxfVector.Zero as per instruction for DxfVector as well.
                           : DxfVector.Zero;
                }

                // Deserialize common properties
                trajectory.OriginalEntityHandle = GetStringProperty(root, "OriginalEntityHandle");
                trajectory.EntityType = GetStringProperty(root, "EntityType");
                trajectory.PrimitiveType = GetStringProperty(root, "PrimitiveType");

                trajectory.IsReversed = GetBooleanProperty(root, "IsReversed");
                trajectory.NozzleNumber = GetIntProperty(root, "NozzleNumber");

                trajectory.UpperNozzleEnabled = GetBooleanProperty(root, "UpperNozzleEnabled");
                trajectory.UpperNozzleGasOn = GetBooleanProperty(root, "UpperNozzleGasOn");
                trajectory.UpperNozzleLiquidOn = GetBooleanProperty(root, "UpperNozzleLiquidOn");
                trajectory.LowerNozzleEnabled = GetBooleanProperty(root, "LowerNozzleEnabled");
                trajectory.LowerNozzleGasOn = GetBooleanProperty(root, "LowerNozzleGasOn");
                trajectory.LowerNozzleLiquidOn = GetBooleanProperty(root, "LowerNozzleLiquidOn");

                // Runtime property
                trajectory.Runtime = GetDoubleProperty(root, "Runtime", 0.0); // Default to 0.0 if not present

                // Conditionally deserialize geometric properties
                if (trajectory.PrimitiveType == "Line")
                {
                    trajectory.LineStartPoint = GetDxfPointProperty(root, "LineStartPoint", options);
                    trajectory.LineEndPoint = GetDxfPointProperty(root, "LineEndPoint", options);
                }
                else if (trajectory.PrimitiveType == "Arc")
                {
                    // Properties like ArcCenter, ArcRadius etc. no longer exist on Trajectory object.
                    // These were removed in favor of ArcPoint1, ArcPoint2, ArcPoint3.
                    // Deserialization of these new properties will be handled when the JSON format is updated for them.
                    // For now, to fix compile error, we don't read these old properties onto trajectory.
                    // The OriginalDxfEntity for Arc will also not be created from these non-existent trajectory fields here.
                    // UPDATE: Deserialize ArcPoint1, ArcPoint2, ArcPoint3
                    if (root.TryGetProperty("ArcPoint1", out JsonElement arcPoint1Element))
                    {
                        trajectory.ArcPoint1 = JsonSerializer.Deserialize<TrajectoryPointWithAngles>(arcPoint1Element.GetRawText(), options) ?? new TrajectoryPointWithAngles();
                    }
                    if (root.TryGetProperty("ArcPoint2", out JsonElement arcPoint2Element))
                    {
                        trajectory.ArcPoint2 = JsonSerializer.Deserialize<TrajectoryPointWithAngles>(arcPoint2Element.GetRawText(), options) ?? new TrajectoryPointWithAngles();
                    }
                    if (root.TryGetProperty("ArcPoint3", out JsonElement arcPoint3Element))
                    {
                        trajectory.ArcPoint3 = JsonSerializer.Deserialize<TrajectoryPointWithAngles>(arcPoint3Element.GetRawText(), options) ?? new TrajectoryPointWithAngles();
                    }
                }
                else if (trajectory.PrimitiveType == "Circle")
                {
                    // trajectory.CircleCenter = GetDxfPointProperty(root, "CircleCenter", options); // Old property
                    // trajectory.CircleRadius = GetDoubleProperty(root, "CircleRadius"); // Old property
                    // trajectory.CircleNormal = GetDxfVectorProperty(root, "CircleNormal", options); // Old property
                    if (root.TryGetProperty("CirclePoint1", out JsonElement circlePoint1Element))
                    {
                        trajectory.CirclePoint1 = JsonSerializer.Deserialize<TrajectoryPointWithAngles>(circlePoint1Element.GetRawText(), options) ?? new TrajectoryPointWithAngles();
                    }
                    if (root.TryGetProperty("CirclePoint2", out JsonElement circlePoint2Element))
                    {
                        trajectory.CirclePoint2 = JsonSerializer.Deserialize<TrajectoryPointWithAngles>(circlePoint2Element.GetRawText(), options) ?? new TrajectoryPointWithAngles();
                    }
                    if (root.TryGetProperty("CirclePoint3", out JsonElement circlePoint3Element))
                    {
                        trajectory.CirclePoint3 = JsonSerializer.Deserialize<TrajectoryPointWithAngles>(circlePoint3Element.GetRawText(), options) ?? new TrajectoryPointWithAngles();
                    }
                    // Deserialize original circle parameters
                    trajectory.OriginalCircleCenter = GetDxfPointProperty(root, "OriginalCircleCenter", options);
                    trajectory.OriginalCircleRadius = GetDoubleProperty(root, "OriginalCircleRadius");
                    trajectory.OriginalCircleNormal = GetDxfVectorProperty(root, "OriginalCircleNormal", options);
                }
                else if (trajectory.PrimitiveType == "Polygon")
                {
                    if (root.TryGetProperty("Points", out JsonElement pointsElement))
                    {
                        trajectory.Points = JsonSerializer.Deserialize<System.Collections.Generic.List<Point3D>>(pointsElement.GetRawText(), options) ?? new System.Collections.Generic.List<Point3D>();
                    }
                    trajectory.PolygonZ = GetDoubleProperty(root, "PolygonZ");
                }

                // After all properties of Trajectory are deserialized,
                // create and assign OriginalDxfEntity based on these properties.
                switch (trajectory.PrimitiveType)
                {
                    case "Line":
                        trajectory.OriginalDxfEntity = new DxfLine(trajectory.LineStartPoint, trajectory.LineEndPoint);
                        break;
                    case "Arc":
                        // Cannot construct DxfArc from trajectory.ArcCenter etc. as they were removed.
                        // OriginalDxfEntity for Arc will be null here. It will be populated later if this trajectory
                        // corresponds to a DxfArc selected by the user (in OnCadEntityClicked) which uses the new 3-point model,
                        // or during reconciliation if the JSON is updated to store 3-point data.
                        // For now, this fixes the compile error.
                        // UPDATE: Attempt to reconstruct DxfArc if points are available
                        // This is a simplified reconstruction. A robust one would calculate center, radius, angles from 3 points.
                        // For now, we'll leave OriginalDxfEntity null here, as reconciliation is the primary way to get the live DxfArc.
                        // If the 3 points were always guaranteed to form a valid arc that could be easily converted back to
                        // DxfArc parameters (center, radius, start/end angle), we could do it here.
                        // However, that calculation is non-trivial and might be better handled by specific geometric services if needed
                        // outside of reconciliation with an existing DXF document.
                        // UPDATE: Attempt to reconstruct DxfArc using GeometryUtils
                        if (trajectory.ArcPoint1 != null && trajectory.ArcPoint2 != null && trajectory.ArcPoint3 != null)
                        {
                            var arcParams = GeometryUtils.CalculateArcParametersFromThreePoints(
                                trajectory.ArcPoint1.Coordinates,
                                trajectory.ArcPoint2.Coordinates,
                                trajectory.ArcPoint3.Coordinates);

                            if (arcParams.HasValue)
                            {
                                trajectory.OriginalDxfEntity = new DxfArc(
                                    arcParams.Value.Center,
                                    arcParams.Value.Radius,
                                    arcParams.Value.StartAngle,
                                    arcParams.Value.EndAngle)
                                {
                                    Normal = arcParams.Value.Normal
                                };
                            }
                        }
                        break;
                    case "Circle":
                        // Reconstruct DxfCircle using the deserialized OriginalCircleCenter, OriginalCircleRadius, OriginalCircleNormal
                        // These are considered more authoritative for reconstructing the entity than recalculating from 3 points,
                        // especially to match the entity parsed directly from DXF during reconciliation.
                        if (trajectory.OriginalCircleRadius > 0) // Basic validation
                        {
                            trajectory.OriginalDxfEntity = new DxfCircle(
                                trajectory.OriginalCircleCenter,
                                trajectory.OriginalCircleRadius)
                            {
                                Normal = trajectory.OriginalCircleNormal
                            };
                             System.Diagnostics.Debug.WriteLine($"[TrajectoryJsonConverter] Read: Reconstructed DxfCircle for trajectory {trajectory.OriginalEntityHandle} using Original parameters.");
                        }
                        else
                        {
                            // Fallback or error if original parameters are invalid/missing,
                            // though they should always be present if saved correctly.
                            // One could attempt to use the 3 points here as a fallback, but it might lead to the same reconciliation issues.
                            System.Diagnostics.Debug.WriteLine($"[TrajectoryJsonConverter] Read: Could not reconstruct DxfCircle for trajectory {trajectory.OriginalEntityHandle} as OriginalCircleRadius is invalid or not set. OriginalDxfEntity will be null.");
                        }
                        break;
                    case "LwPolyline": // Assuming LwPolyline data would be deserialized onto Trajectory if supported
                        // This part needs LwPolyline specific properties on Trajectory object if we want to reconstruct it
                        // For now, if PrimitiveType is LwPolyline, OriginalDxfEntity might remain null if not handled here
                        // or if Trajectory class doesn't store LwPolyline vertices directly.
                        // Based on current Trajectory model, it doesn't seem to store LwPolyline vertices.
                        // So, for LwPolyline, OriginalDxfEntity reconstruction from Trajectory fields is not directly possible yet.
                        // This will be a limitation until Trajectory model and this converter are extended for LwPolyline geo data.
                        // For now, we'll leave it potentially null for LwPolyline from config if not handled by specific fields.
                        // The reconciliation logic would then have to be very robust or have a fallback.
                        // However, the goal is to have *some* DxfEntity.
                        // Let's assume for now that if it's an LwPolyline, its specific points/bulges are not on Trajectory object directly.
                        // This part of the plan (serializing LwPolyline specific data) needs to be addressed if LwPolyline highlighting from config is crucial.
                        // For this step, we are focusing on Line, Arc, Circle for which Trajectory has direct fields.
                        if (trajectory.OriginalDxfEntity == null && !string.IsNullOrEmpty(trajectory.EntityType)) // Check EntityType too
                        {
                             // Attempt a placeholder if it's an LwPolyline and we have no specific data on Trajectory model
                             // This won't be geometrically accurate for reconciliation but makes it non-null.
                             if (trajectory.EntityType == typeof(DxfLwPolyline).Name) {
                                 // trajectory.OriginalDxfEntity = new DxfLwPolyline(); // Placeholder, not useful for matching.
                                 // For now, we can't reconstruct LwPolyline from current Trajectory fields.
                             }
                        }
                        break;
                }
                return trajectory;
            }
        }

        public override void Write(Utf8JsonWriter writer, Trajectory value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();

            // Common string properties
            if (!string.IsNullOrEmpty(value.OriginalEntityHandle))
            {
                writer.WriteString("OriginalEntityHandle", value.OriginalEntityHandle);
            }
            if (!string.IsNullOrEmpty(value.EntityType))
            {
                writer.WriteString("EntityType", value.EntityType);
            }
            if (!string.IsNullOrEmpty(value.PrimitiveType))
            {
                writer.WriteString("PrimitiveType", value.PrimitiveType);
            }

            // Boolean properties
            writer.WriteBoolean("IsReversed", value.IsReversed);
            writer.WriteNumber("NozzleNumber", value.NozzleNumber); // Assuming NozzleNumber is int as per typical usage

            // Nozzle control boolean properties
            writer.WriteBoolean("UpperNozzleEnabled", value.UpperNozzleEnabled);
            writer.WriteBoolean("UpperNozzleGasOn", value.UpperNozzleGasOn);
            writer.WriteBoolean("UpperNozzleLiquidOn", value.UpperNozzleLiquidOn);
            writer.WriteBoolean("LowerNozzleEnabled", value.LowerNozzleEnabled);
            writer.WriteBoolean("LowerNozzleGasOn", value.LowerNozzleGasOn);
            writer.WriteBoolean("LowerNozzleLiquidOn", value.LowerNozzleLiquidOn);

            // Runtime property
            writer.WriteNumber("Runtime", value.Runtime);

            // Geometric properties based on PrimitiveType
            switch (value.PrimitiveType)
            {
                case "Line":
                    writer.WritePropertyName("LineStartPoint");
                    JsonSerializer.Serialize(writer, value.LineStartPoint, options);
                    writer.WritePropertyName("LineEndPoint");
                    JsonSerializer.Serialize(writer, value.LineEndPoint, options);
                    break;
                case "Arc":
                    // Old Arc properties (ArcCenter, ArcRadius, etc.) have been removed from Trajectory model.
                    // New 3-point arc properties (ArcPoint1, ArcPoint2, ArcPoint3) will be serialized
                    // in a later stage when this converter is fully updated for the new model.
                    // For now, to fix compile errors, we write no specific geometric data for "Arc" type.
                    // This means Arc geometry won't be persisted correctly in this interim state.
                    // UPDATE: Serialize ArcPoint1, ArcPoint2, ArcPoint3
                    writer.WritePropertyName("ArcPoint1");
                    JsonSerializer.Serialize(writer, value.ArcPoint1, options);
                    writer.WritePropertyName("ArcPoint2");
                    JsonSerializer.Serialize(writer, value.ArcPoint2, options);
                    writer.WritePropertyName("ArcPoint3");
                    JsonSerializer.Serialize(writer, value.ArcPoint3, options);
                    break;
                case "Circle":
                    // writer.WritePropertyName("CircleCenter"); // Old property
                    // JsonSerializer.Serialize(writer, value.CircleCenter, options); // Old property
                    // writer.WriteNumber("CircleRadius", value.CircleRadius); // Old property
                    // writer.WritePropertyName("CircleNormal"); // Old property
                    // JsonSerializer.Serialize(writer, value.CircleNormal, options); // Old property
                    writer.WritePropertyName("CirclePoint1");
                    JsonSerializer.Serialize(writer, value.CirclePoint1, options);
                    writer.WritePropertyName("CirclePoint2");
                    JsonSerializer.Serialize(writer, value.CirclePoint2, options);
                    writer.WritePropertyName("CirclePoint3");
                    JsonSerializer.Serialize(writer, value.CirclePoint3, options);

                    // Serialize original circle parameters
                    writer.WritePropertyName("OriginalCircleCenter");
                    JsonSerializer.Serialize(writer, value.OriginalCircleCenter, options);
                    writer.WriteNumber("OriginalCircleRadius", value.OriginalCircleRadius);
                    writer.WritePropertyName("OriginalCircleNormal");
                    JsonSerializer.Serialize(writer, value.OriginalCircleNormal, options);
                    break;
                case "Polygon":
                    writer.WritePropertyName("Points");
                    JsonSerializer.Serialize(writer, value.Points, options);
                    writer.WriteNumber("PolygonZ", value.PolygonZ);
                    break;
            }

            writer.WriteEndObject();
        }
    }
}
