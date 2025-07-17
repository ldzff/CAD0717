namespace RobTeach.Models
{
    /// <summary>
    /// Represents transformation parameters that might be applied to CAD data or trajectories.
    /// This is a placeholder for future coordinate transformation capabilities.
    /// </summary>
    public class Transform
    {
        /// <summary>
        /// Gets or sets the scale factor along the X-axis.
        /// </summary>
        /// <value>Default is 1.0.</value>
        public double ScaleX { get; set; } = 1.0;

        /// <summary>
        /// Gets or sets the scale factor along the Y-axis.
        /// </summary>
        /// <value>Default is 1.0.</value>
        public double ScaleY { get; set; } = 1.0;

        /// <summary>
        /// Gets or sets the offset along the X-axis.
        /// </summary>
        /// <value>Default is 0.0.</value>
        public double OffsetX { get; set; } = 0.0;

        /// <summary>
        /// Gets or sets the offset along the Y-axis.
        /// </summary>
        /// <value>Default is 0.0.</value>
        public double OffsetY { get; set; } = 0.0;

        /// <summary>
        /// Gets or sets the rotation angle in degrees.
        /// </summary>
        /// <value>Default is 0.0.</value>
        public double RotationAngle { get; set; } = 0.0;

        /// <summary>
        /// Initializes a new instance of the <see cref="Transform"/> class with default values.
        /// </summary>
        public Transform()
        {
            // Default constructor
        }
    }
}
