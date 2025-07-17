using IxMilia.Dxf;

namespace RobTeach.Models
{
    public class TrajectoryPointWithAngles
    {
        public DxfPoint Coordinates { get; set; } = DxfPoint.Origin;
        public double Rx { get; set; } = 0.0;
        public double Ry { get; set; } = 0.0;
        public double Rz { get; set; } = 0.0;

        // Default constructor
        public TrajectoryPointWithAngles() { }

        // Constructor with coordinates
        public TrajectoryPointWithAngles(DxfPoint coordinates)
        {
            Coordinates = coordinates;
            // Rx, Ry, Rz will use default 0.0
        }

        // Constructor with coordinates and angles
        public TrajectoryPointWithAngles(DxfPoint coordinates, double rx, double ry, double rz)
        {
            Coordinates = coordinates;
            Rx = rx;
            Ry = ry;
            Rz = rz;
        }
    }
}
