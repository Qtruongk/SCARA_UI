using System;
using TsRemoteLib;

namespace Test_1.Models
{
    public class RobotPositionData
    {
        public TsJointS JointPosition { get; set; }
        public TsPointS WorldPosition { get; set; }
    }
}
