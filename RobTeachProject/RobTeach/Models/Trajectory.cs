using System.Collections.Generic;
using System.Linq;
using System.Windows; // This using statement makes System.Windows.Point available as 'Point'
using IxMilia.Dxf; // Added for DxfPoint, DxfVector
using IxMilia.Dxf.Entities; // For DxfEntity
using System.Text.Json.Serialization; // For JsonIgnore

namespace RobTeach.Models
{
    /// <summary>
    /// Represents a single trajectory derived from a CAD entity, including its points and application parameters.
    /// </summary>
    public class Trajectory
    {
        /// <summary>
        /// Gets or sets the handle of the original CAD entity from which this trajectory was derived.
        /// This helps in linking the trajectory back to its source in the DXF document.
        /// </summary>
        public string OriginalEntityHandle { get; set; } = string.Empty; // May become redundant if OriginalDxfEntity is used primarily

        /// <summary>
        /// Stores the original DXF entity object. This is not serialized to JSON.
        /// </summary>
        [JsonIgnore]
        public DxfEntity? OriginalDxfEntity { get; set; } = null;

        /// <summary>
        /// Gets or sets the type of the original CAD entity (e.g., "Line", "Arc", "LwPolyline").
        /// </summary>
        public string EntityType { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the list of points that define the path of the trajectory.
        /// These points are typically in the coordinate system intended for the robot.
        /// Uses <see cref="System.Windows.Point"/>. This is ignored for JSON serialization
        /// as geometric parameters below should be used for reconstruction.
        /// </summary>
        [JsonIgnore]
        public List<Point3D> Points { get; set; } = new List<Point3D>();
        public double PolygonZ { get; set; } = 0.0;

        /// <summary>
        /// Gets or sets the type of the primitive (e.g., "Line", "Arc", "Circle").
        /// This helps in interpreting the geometric parameters.
        /// </summary>
        public string PrimitiveType { get; set; } = string.Empty;

        // Geometric parameters for Line
        public DxfPoint LineStartPoint { get; set; } = DxfPoint.Origin;
        public DxfPoint LineEndPoint { get; set; } = DxfPoint.Origin;

        // Geometric parameters for Arc (3-point definition with angles)
        public TrajectoryPointWithAngles ArcPoint1 { get; set; }
        public TrajectoryPointWithAngles ArcPoint2 { get; set; } // Midpoint on arc
        public TrajectoryPointWithAngles ArcPoint3 { get; set; }

        // Geometric parameters for Circle (3-point definition)
        public TrajectoryPointWithAngles CirclePoint1 { get; set; }
        public TrajectoryPointWithAngles CirclePoint2 { get; set; }
        public TrajectoryPointWithAngles CirclePoint3 { get; set; }

        // Store original parameters for circles to aid robust reconciliation
        public DxfPoint OriginalCircleCenter { get; set; } = DxfPoint.Origin;
        public double OriginalCircleRadius { get; set; } = 0.0;
        public DxfVector OriginalCircleNormal { get; set; } = DxfVector.ZAxis;

        /// <summary>
        /// Gets or sets a value indicating whether the trajectory's conventional direction should be reversed.
        /// For example, for an arc, this might mean traversing from EndAngle to StartAngle.
        /// </summary>
        public bool IsReversed { get; set; } = false;

        /// <summary>
        /// Gets or sets the nozzle number to be used for this trajectory.
        /// </summary>
        public int NozzleNumber { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether water spray is used for this trajectory.
        /// True indicates water spray; false indicates air spray (or other non-water medium).
        /// </summary>
        // public bool IsWater { get; set; } // Removed as per new nozzle control scheme

        /// <summary>
        /// Gets or sets a value indicating whether the upper nozzle is enabled for this trajectory.
        /// </summary>
        public bool UpperNozzleEnabled { get; set; } = false;

        /// <summary>
        /// Gets or sets a value indicating whether gas is on for the upper nozzle during this trajectory.
        /// </summary>
        public bool UpperNozzleGasOn { get; set; } = false;

        /// <summary>
        /// Gets or sets a value indicating whether liquid is on for the upper nozzle during this trajectory.
        /// </summary>
        public bool UpperNozzleLiquidOn { get; set; } = false;

        /// <summary>
        /// Gets or sets a value indicating whether the lower nozzle is enabled for this trajectory.
        /// </summary>
        public bool LowerNozzleEnabled { get; set; } = false;

        /// <summary>
        /// Gets or sets a value indicating whether gas is on for the lower nozzle during this trajectory.
        /// </summary>
        public bool LowerNozzleGasOn { get; set; } = false;

        /// <summary>
        /// Gets or sets a value indicating whether liquid is on for the lower nozzle during this trajectory.
        /// </summary>
        public bool LowerNozzleLiquidOn { get; set; } = false;

        /// <summary>
        /// Gets or sets the runtime for this trajectory in seconds.
        /// </summary>
        public double Runtime { get; set; } // Default value will be set dynamically

        /// <summary>
        /// Initializes a new instance of the <see cref="Trajectory"/> class.
        /// A parameterless constructor is required for JSON deserialization.
        /// </summary>
        public Trajectory()
        {
            // Default constructor for JSON deserialization and typical instantiation.
            // Points list is initialized by default.
            ArcPoint1 = new TrajectoryPointWithAngles();
            ArcPoint2 = new TrajectoryPointWithAngles();
            ArcPoint3 = new TrajectoryPointWithAngles();

            CirclePoint1 = new TrajectoryPointWithAngles();
            CirclePoint2 = new TrajectoryPointWithAngles();
            CirclePoint3 = new TrajectoryPointWithAngles();
        }

        public override string ToString()
        {
            string details = string.Empty;
            switch (PrimitiveType)
            {
                case "Line":
                    details = $"Line ({LineStartPoint} -> {LineEndPoint})";
                    break;
                case "Arc":
                    // Update ToString to reflect new 3-point arc definition
                    details = $"Arc (P1:{ArcPoint1.Coordinates}, P2:{ArcPoint2.Coordinates}, P3:{ArcPoint3.Coordinates})";
                    // Optionally, could include angles if they are relevant for display here
                    break;
                case "Circle":
                    details = $"Circle (P1:{CirclePoint1.Coordinates}, P2:{CirclePoint2.Coordinates}, P3:{CirclePoint3.Coordinates})";
                    break;
                case "Polygon":
                    details = $"Polygon ({Points.Count} vertices)";
                    break;
                default:
                    // Use EntityType if PrimitiveType is not set or recognized, then fallback to DXF entity type
                    string typeDisplay = string.IsNullOrEmpty(PrimitiveType) ? (string.IsNullOrEmpty(EntityType) ? OriginalDxfEntity?.GetType().Name ?? "Unknown" : EntityType) : PrimitiveType;
                    details = $"{typeDisplay} (Points: {Points.Count})"; // Fallback for non-primitives or if points are primary
                    break;
            }
            string reversedStatus = IsReversed ? " [R]" : "";
            string upperStatus = UpperNozzleEnabled ? $"U(G:{(UpperNozzleGasOn?"On":"Off")},L:{(UpperNozzleLiquidOn?"On":"Off")})" : "U(Off)";
            string lowerStatus = LowerNozzleEnabled ? $"L(G:{(LowerNozzleGasOn?"On":"Off")},L:{(LowerNozzleLiquidOn?"On":"Off")})" : "L(Off)";
            return $"{details}{reversedStatus} - {upperStatus}, {lowerStatus}";
        }
    }
}
