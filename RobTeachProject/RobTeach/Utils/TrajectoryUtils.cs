using RobTeach.Models;
using System; // For Math

namespace RobTeach.Utils
{
    public static class TrajectoryUtils
    {
        public static double CalculateTrajectoryLength(Trajectory trajectory)
        {
            if (trajectory == null || trajectory.Points == null || trajectory.Points.Count < 2)
            {
                return 0.0;
            }

            double length = 0.0;
            for (int i = 0; i < trajectory.Points.Count - 1; i++)
            {
                var p1 = trajectory.Points[i];
                var p2 = trajectory.Points[i + 1];
                length += Math.Sqrt((p2 - p1).LengthSquared());
            }

            if (trajectory.OriginalDxfEntity is IxMilia.Dxf.Entities.DxfLwPolyline polyline && polyline.IsClosed && trajectory.Points.Count > 2)
            {
                var p1 = trajectory.Points[trajectory.Points.Count - 1];
                var p2 = trajectory.Points[0];
                length += Math.Sqrt((p2 - p1).LengthSquared());
            }

            return length / 1000.0; // Assuming points are in mm, convert to meters
        }

        public static double CalculateMinRuntime(Trajectory trajectory)
        {
            if (trajectory == null) return 0.0;
            double lengthInMeters = CalculateTrajectoryLength(trajectory);
            if (lengthInMeters <= 0) return 0.0; // Avoid division by zero or negative runtime for zero-length trajectories
            // Speed = 2 m/s
            // If length is very small, e.g., less than a precision threshold, consider runtime effectively zero or a very small number.
            // For now, direct calculation:
            if (Math.Abs(lengthInMeters) < 1e-9) return 0.0; // Treat extremely small lengths as zero length for runtime calculation
            return lengthInMeters / 2.0;
        }
    }
}
