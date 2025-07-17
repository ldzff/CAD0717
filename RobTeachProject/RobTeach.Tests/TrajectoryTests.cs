using Microsoft.VisualStudio.TestTools.UnitTesting;
using RobTeach.Models;
using RobTeach.Utils;
using System.Collections.Generic;
using System.Windows;

namespace RobTeach.Tests
{
    [TestClass]
    public class TrajectoryTests
    {
        private const double Tolerance = 0.00001; // Tolerance for double comparisons

        [TestMethod]
        public void CalculateTrajectoryLength_NullTrajectory_ReturnsZero()
        {
            // Arrange
            Trajectory trajectory = null;

            // Act
            double length = TrajectoryUtils.CalculateTrajectoryLength(trajectory);

            // Assert
            Assert.AreEqual(0.0, length, Tolerance);
        }

        [TestMethod]
        public void CalculateTrajectoryLength_NullPoints_ReturnsZero()
        {
            // Arrange
            Trajectory trajectory = new Trajectory();
            trajectory.Points = null;

            // Act
            double length = TrajectoryUtils.CalculateTrajectoryLength(trajectory);

            // Assert
            Assert.AreEqual(0.0, length, Tolerance);
        }

        [TestMethod]
        public void CalculateTrajectoryLength_EmptyPoints_ReturnsZero()
        {
            // Arrange
            Trajectory trajectory = new Trajectory();
            trajectory.Points = new List<Point>();

            // Act
            double length = TrajectoryUtils.CalculateTrajectoryLength(trajectory);

            // Assert
            Assert.AreEqual(0.0, length, Tolerance);
        }

        [TestMethod]
        public void CalculateTrajectoryLength_SinglePoint_ReturnsZero()
        {
            // Arrange
            Trajectory trajectory = new Trajectory();
            trajectory.Points = new List<Point> { new Point(0, 0) };

            // Act
            double length = TrajectoryUtils.CalculateTrajectoryLength(trajectory);

            // Assert
            Assert.AreEqual(0.0, length, Tolerance);
        }

        [TestMethod]
        public void CalculateTrajectoryLength_TwoPoints_ReturnsCorrectLength()
        {
            // Arrange
            Trajectory trajectory = new Trajectory();
            // Points are in mm, result should be in meters
            trajectory.Points = new List<Point> { new Point(0, 0), new Point(3000, 4000) }; // Length 5000 mm

            // Act
            double length = TrajectoryUtils.CalculateTrajectoryLength(trajectory);

            // Assert
            Assert.AreEqual(5.0, length, Tolerance); // 5000mm = 5m
        }

        [TestMethod]
        public void CalculateTrajectoryLength_MultiplePoints_ReturnsCorrectLength()
        {
            // Arrange
            Trajectory trajectory = new Trajectory();
            // 0,0 -> 3000,0 (3m) -> 3000,4000 (4m) = Total 7m
            trajectory.Points = new List<Point> { new Point(0, 0), new Point(3000, 0), new Point(3000, 4000) };

            // Act
            double length = TrajectoryUtils.CalculateTrajectoryLength(trajectory);

            // Assert
            Assert.AreEqual(7.0, length, Tolerance); // 3000mm + 4000mm = 7000mm = 7m
        }

        [TestMethod]
        public void CalculateMinRuntime_NullTrajectory_ReturnsZero()
        {
            // Arrange
            Trajectory trajectory = null;

            // Act
            double runtime = TrajectoryUtils.CalculateMinRuntime(trajectory);

            // Assert
            Assert.AreEqual(0.0, runtime, Tolerance);
        }

        [TestMethod]
        public void CalculateMinRuntime_ZeroLengthTrajectory_ReturnsZero()
        {
            // Arrange
            Trajectory trajectory = new Trajectory();
            trajectory.Points = new List<Point> { new Point(0, 0), new Point(0, 0) }; // Zero length

            // Act
            double runtime = TrajectoryUtils.CalculateMinRuntime(trajectory);

            // Assert
            Assert.AreEqual(0.0, runtime, Tolerance);
        }

        [TestMethod]
        public void CalculateMinRuntime_ValidLength_ReturnsCorrectRuntime()
        {
            // Arrange
            Trajectory trajectory = new Trajectory();
            trajectory.Points = new List<Point> { new Point(0, 0), new Point(4000, 0) }; // Length 4000mm = 4m
                                                                                       // Speed = 2 m/s

            // Act
            double runtime = TrajectoryUtils.CalculateMinRuntime(trajectory);

            // Assert
            Assert.AreEqual(2.0, runtime, Tolerance); // 4m / 2m/s = 2s
        }

        [TestMethod]
        public void CalculateMinRuntime_Length10m_Returns_5s()
        {
            // Arrange
            Trajectory trajectory = new Trajectory();
            trajectory.Points = new List<Point> { new Point(0, 0), new Point(10000, 0) }; // Length 10000mm = 10m

            // Act
            double runtime = TrajectoryUtils.CalculateMinRuntime(trajectory);

            // Assert
            Assert.AreEqual(5.0, runtime, Tolerance); // 10m / 2m/s = 5s
        }


        [TestMethod]
        public void Trajectory_SetRuntime_StoresValue()
        {
            // Arrange
            Trajectory trajectory = new Trajectory();
            double expectedRuntime = 3.14;

            // Act
            trajectory.Runtime = expectedRuntime;

            // Assert
            Assert.AreEqual(expectedRuntime, trajectory.Runtime, Tolerance);
        }

        // Test for default runtime being set requires simulating the UI logic
        // This test conceptually verifies that if UI calls CalculateMinRuntime and sets it, it works.
        [TestMethod]
        public void Trajectory_DefaultRuntimeLogic_CanBeSetToMinRuntime()
        {
            // Arrange
            Trajectory trajectory = new Trajectory();
            trajectory.Points = new List<Point> { new Point(0, 0), new Point(6000, 0) }; // Length 6000mm = 6m
                                                                                       // Min runtime should be 6m / 2m/s = 3s
            // Act
            // Simulate UI logic:
            double minRuntime = TrajectoryUtils.CalculateMinRuntime(trajectory);
            trajectory.Runtime = minRuntime;

            // Assert
            Assert.AreEqual(3.0, trajectory.Runtime, Tolerance);
        }
    }
}
