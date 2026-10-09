using System;

namespace Test_1.Models
{
    public class RobotState
    {
        public bool IsConnected { get; set; }
        public bool IsServoOn { get; set; }
        public bool IsWatchdogRunning { get; set; }
        public bool HasAlarm { get; set; }
        public bool IsEmergencyStopActive { get; set; }
        public string MasterModeName { get; set; }
        
        public RobotState()
        {
            IsConnected = false;
            IsServoOn = false;
            IsWatchdogRunning = false;
            HasAlarm = false;
            IsEmergencyStopActive = false;
            MasterModeName = "Unknown";
        }
    }
}
