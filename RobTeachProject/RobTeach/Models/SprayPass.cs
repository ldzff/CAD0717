using System.Collections.Generic;
using RobTeach.Models; // Assuming Trajectory is in this namespace

namespace RobTeach.Models
{
    public class SprayPass
    {
        public string PassName { get; set; } = "Default Pass";
        public List<Trajectory> Trajectories { get; set; } = new List<Trajectory>();
        // Constructor or other methods can be added if needed
    }
}
