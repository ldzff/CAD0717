using System.Collections.Generic;

namespace RobTeach.Models
{
    /// <summary>
    /// Represents a complete configuration for a product, including its name,
    /// a list of trajectories, and transformation parameters.
    /// </summary>
    public class Configuration
    {
        /// <summary>
        /// Gets or sets the name of the product associated with this configuration.
        /// </summary>
        public string ProductName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the raw content of the DXF file associated with this configuration.
        /// </summary>
        public string DxfFileContent { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the Modbus IP Address for robot communication.
        /// </summary>
        public string ModbusIpAddress { get; set; } = "127.0.0.1"; // Default IP

        /// <summary>
        /// Gets or sets the Modbus Port for robot communication.
        /// </summary>
        public int ModbusPort { get; set; } = 502; // Default Port

        /// <summary>
        /// Gets or sets the settings related to the CAD canvas view state.
        /// </summary>
        public CanvasViewSettings CanvasState { get; set; } = new CanvasViewSettings();

        /// <summary>
        /// Gets or sets the list of trajectories defined for this product configuration.
        /// This might be deprecated in favor of SprayPasses.
        /// </summary>
        public List<Trajectory> Trajectories { get; set; } = new List<Trajectory>();

        /// <summary>
        /// Gets or sets the list of spray passes for this configuration. Each pass can contain multiple trajectories.
        /// </summary>
        public List<SprayPass> SprayPasses { get; set; } = new List<SprayPass>();

        /// <summary>
        /// Gets or sets the index of the currently active or selected spray pass.
        /// -1 could indicate no pass is selected, or 0 for the first pass by default.
        /// </summary>
        public int CurrentPassIndex { get; set; } = -1;

        /// <summary>
        /// Gets or sets the index of the selected trajectory within the current spray pass (identified by CurrentPassIndex).
        /// A value of -1 indicates no trajectory is selected.
        /// </summary>
        public int SelectedTrajectoryIndexInCurrentPass { get; set; } = -1;

        /// <summary>
        /// Gets or sets the transformation parameters associated with this configuration.
        /// This is a placeholder for future functionality like scaling, offsetting, or rotating
        /// the entire set of trajectories.
        /// </summary>
        public Transform TransformParameters { get; set; } = new Transform();
        public string Language { get; set; } = "中文";

        /// <summary>
        /// Initializes a new instance of the <see cref="Configuration"/> class.
        /// A parameterless constructor is provided for JSON deserialization and typical instantiation.
        /// Trajectories list and TransformParameters are initialized to default instances.
        /// </summary>
        public Configuration()
        {
            // Default constructor.
        }
    }
}
