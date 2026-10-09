using System;
using System.Threading;
using System.Threading.Tasks;
using Test_1.Models;
using Test_1.Utilities;
using TsRemoteLib;

namespace Test_1.Services
{
    public class RobotService
    {
        private TsRemoteS _robot;
        private readonly LoggingService _logger;
        
        public RobotState State { get; private set; }

        public event Action StateChanged;

        public RobotService(LoggingService logger)
        {
            _logger = logger;
            State = new RobotState();
        }

        public bool Connect(string ip = Constants.DefaultRobotIP, int port = Constants.DefaultRobotPort, int srcPort = Constants.DefaultSourcePort)
        {
            try
            {
                if (_robot == null)
                {
                    _robot = new TsRemoteS();
                    _logger.Log("TsRemoteS instance created.");
                }

                _logger.Log($"Setting IP: {ip}, Port: {port}, SrcPort: {srcPort} (openmode=1:Client) ...");
                bool isIpSet = _robot.SetIPaddr(1, ip, port, srcPort);

                if (!isIpSet)
                {
                    _logger.Log("\u2718 SetIPaddr failed. Cannot proceed with connection.");
                    return false;
                }

                _logger.Log("Connecting via TCP/IP (openmode=Client, robot must be E00=1 server) ...");
                bool isConnected = _robot.Connect(1);

                if (isConnected)
                {
                    State.IsConnected = true;
                    _logger.Log("\u2714 Connected successfully to robot controller.");
                    NotifyStateChanged();
                    return true;
                }
                else
                {
                    State.IsConnected = false;
                    _logger.Log("\u2718 Connection failed. Check IP address and controller status.");
                    NotifyStateChanged();
                    return false;
                }
            }
            catch (TsRemoteSException ex)
            {
                _logger.Log($"Connection error (TsRemote): {ex.errorCode} - {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                _logger.Log($"Connection error (General): {ex.GetType().Name} - {ex.Message}");
                return false;
            }
        }

        public void Disconnect()
        {
            if (!State.IsConnected) return;

            try
            {
                _robot?.Disconnect();
                State.IsConnected = false;
                State.IsServoOn = false;
                State.IsWatchdogRunning = false;
                _logger.Log("Disconnected from robot controller.");
                NotifyStateChanged();
            }
            catch (TsRemoteSException ex)
            {
                _logger.Log($"Disconnect error: {ex.errorCode}");
            }
        }

        public void TurnServoOn()
        {
            if (!State.IsConnected || _robot == null)
            {
                _logger.Log("Cannot turn Servo ON: Not connected.");
                return;
            }

            try
            {
                _logger.Log("--- Servo ON Sequence ---");
                _logger.Log("[1] Reading controller status...");
                
                TsStatusAllS status = _robot.GetStatusAll();
                LogControllerStatus(status);

                if (HasActiveAlarms(status))
                {
                    _logger.Log("[2] Resetting alarms...");
                    try
                    {
                        _robot.ResetALARM();
                        _logger.Log("    Alarms reset OK.");
                        Thread.Sleep(500);
                    }
                    catch (TsRemoteSException ex)
                    {
                        _logger.Log($"    Alarm reset error: {ex.errorCode}");
                    }
                }
                else
                {
                    _logger.Log("[2] No active alarms.");
                }

                if (status.EmergencySwitch == 1)
                {
                    _logger.Log("\u2718 EMERGENCY STOP is active! Release E-Stop on controller/teach pendant first.");
                    State.IsEmergencyStopActive = true;
                    NotifyStateChanged();
                    return;
                }
                State.IsEmergencyStopActive = false;

                _logger.Log("[4] Re-reading status after preparation...");
                status = _robot.GetStatusAll();
                LogControllerStatus(status);

                _logger.Log("[5] Sending ServoOn command...");
                _robot.ServoOn();
                Thread.Sleep(500);

                status = _robot.GetStatusAll();
                if (status.ServoStatus == 1)
                {
                    State.IsServoOn = true;
                    _logger.Log("\u2714 \u25B6 Servo is ON! Controller LED should be lit.");
                }
                else
                {
                    _logger.Log("\u26A0 ServoOn command sent but ServoStatus still OFF. Check controller.");
                    State.IsServoOn = false;
                }
                
                NotifyStateChanged();
            }
            catch (TsRemoteSException ex)
            {
                _logger.Log($"Servo ON error: {ex.errorCode} - {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.Log($"Servo ON error (General): {ex.GetType().Name} - {ex.Message}");
            }
        }

        public void TurnServoOff()
        {
            if (!State.IsConnected || _robot == null)
            {
                _logger.Log("Cannot turn Servo OFF: Not connected.");
                return;
            }

            try
            {
                _robot.ServoOff();
                State.IsServoOn = false;
                _logger.Log("\u25A0 Servo turned OFF.");
                NotifyStateChanged();
            }
            catch (TsRemoteSException ex)
            {
                _logger.Log($"Servo OFF error: {ex.errorCode}");
            }
        }

        public void StartWatchdog()
        {
            if (!State.IsConnected || _robot == null)
            {
                _logger.Log("Cannot start Watchdog: Not connected.");
                return;
            }

            if (State.IsWatchdogRunning)
            {
                _logger.Log("Watchdog is already running.");
                return;
            }

            try
            {
                _robot.WatchDogStart(Constants.WatchdogIntervalMs, Constants.WatchdogTimeoutMs, 0, new TsRemoteS.TSStatusEvent(OnWatchdogStatusEvent));
                State.IsWatchdogRunning = true;
                _logger.Log($"\uD83D\uDC41 Watchdog monitoring started (interval: {Constants.WatchdogIntervalMs}ms, timeout: {Constants.WatchdogTimeoutMs}ms).");
                NotifyStateChanged();
            }
            catch (TsRemoteSException ex)
            {
                _logger.Log($"Watchdog error: {ex.errorCode}");
            }
        }

        private void OnWatchdogStatusEvent(TsStatusMonitor para)
        {
            bool stateModified = false;

            bool servoFromWatchdog = (para.ServoStatus == 1);
            if (servoFromWatchdog != State.IsServoOn)
            {
                State.IsServoOn = servoFromWatchdog;
                _logger.Log(State.IsServoOn ? "\u25B6 Servo status changed: ON" : "\u25A0 Servo status changed: OFF");
                stateModified = true;
            }

            if (para.EmergencyStop == 1 && !State.IsEmergencyStopActive)
            {
                _logger.Log("\u26A0 EMERGENCY STOP detected!");
                State.IsEmergencyStopActive = true;
                stateModified = true;
            }
            else if (para.EmergencyStop == 0 && State.IsEmergencyStopActive)
            {
                State.IsEmergencyStopActive = false;
                stateModified = true;
            }

            if (para.SafetyStop == 1)
            {
                _logger.Log("\u26A0 Safety Stop detected!");
            }

            if (para.WatchDogErr == 1)
            {
                _logger.Log("\u26A0 Watchdog communication error!");
                State.IsWatchdogRunning = false;
                stateModified = true;
            }

            if (stateModified)
            {
                NotifyStateChanged();
            }
        }

        public RobotPositionData GetCurrentPosition()
        {
            if (!State.IsConnected || _robot == null) return null;

            try
            {
                return new RobotPositionData
                {
                    JointPosition = _robot.GetPsnFbkJoint(),
                    WorldPosition = _robot.GetPsnFbkWorld()
                };
            }
            catch
            {
                return null;
            }
        }

        public void EnsureWatchdogRunning()
        {
            if (State.IsConnected && !State.IsWatchdogRunning)
            {
                StartWatchdog();
            }
        }

        public void SetSpeed(int speedPercent)
        {
            if (State.IsConnected && _robot != null)
            {
                _robot.MvSpeed = speedPercent;
            }
        }

        public void MoveJointAxis(int axisNo, double targetValue)
        {
            if (!State.IsConnected || _robot == null) return;
            try
            {
                _robot.Movea(axisNo, targetValue);
            }
            catch (Exception ex)
            {
                _logger.Log($"Movea Axis {axisNo} Error: {ex.Message}");
            }
        }

        public void MoveCartesian(double x, double y, double z, double c)
        {
            if (!State.IsConnected || _robot == null) return;
            try
            {
                TsPointS pt = new TsPointS { X = x, Y = y, Z = z, C = c, T = 0 };
                _robot.Move(pt);
            }
            catch (Exception ex)
            {
                _logger.Log($"Move Cartesian Error: {ex.Message}");
            }
        }

        public async Task DrawSquareAsync(double sideLength)
        {
            if (!State.IsConnected || _robot == null) return;
            try
            {
                await MoveToSafeDrawPositionAsync();

                var startPos = GetCurrentPosition()?.WorldPosition;
                if (startPos == null) return;

                double x0 = startPos.X;
                double y0 = startPos.Y;
                double z = startPos.Z;
                double c = startPos.C;

                TsPointS[] points = new TsPointS[]
                {
                    new TsPointS { X = x0 + sideLength, Y = y0, Z = z, C = c, T = 0 },
                    new TsPointS { X = x0 + sideLength, Y = y0 + sideLength, Z = z, C = c, T = 0 },
                    new TsPointS { X = x0, Y = y0 + sideLength, Z = z, C = c, T = 0 },
                    new TsPointS { X = x0, Y = y0, Z = z, C = c, T = 0 }
                };

                _logger.Log($"[Draw] Starting Square (side: {sideLength}mm) from ({x0:F1}, {y0:F1}) at optimized speed...");

                await Task.Run(async () =>
                {
                    // Save current speeds
                    double oldSpeed = _robot.MvSpeed;
                    double oldAcc = _robot.MvAccel;
                    double oldDec = _robot.MvDecel;

                    // Set speed to a higher value but not max
                    _robot.MvSpeed = 40;
                    _robot.MvAccel = 60;
                    _robot.MvDecel = 60;

                    foreach (var pt in points)
                    {
                        try { _robot.Moves(pt); } catch { }
                        await WaitUntilReachAsync(pt.X, pt.Y, 5.0); // Allow blending around corners
                    }

                    // Restore speeds
                    _robot.MvSpeed = oldSpeed;
                    _robot.MvAccel = oldAcc;
                    _robot.MvDecel = oldDec;
                });
                
                _logger.Log("[Draw] Square completed.");
            }
            catch (Exception ex)
            {
                _logger.Log($"Draw Square Error: {ex.Message}");
            }
        }

        public async Task DrawCircleAsync(double radius)
        {
            if (!State.IsConnected || _robot == null) return;
            try
            {
                await MoveToSafeDrawPositionAsync();

                var startPos = GetCurrentPosition()?.WorldPosition;
                if (startPos == null) return;

                double x0 = startPos.X;
                double y0 = startPos.Y;
                double z = startPos.Z;
                double c = startPos.C;

                double cx = x0 + radius;
                double cy = y0;

                _logger.Log($"[Draw] Starting Circle (radius: {radius}mm) from ({x0:F1}, {y0:F1}) at optimized speed...");

                await Task.Run(async () =>
                {
                    double oldSpeed = _robot.MvSpeed;
                    double oldAcc = _robot.MvAccel;
                    double oldDec = _robot.MvDecel;

                    _robot.MvSpeed = 40;  // Increased speed
                    _robot.MvAccel = 60;
                    _robot.MvDecel = 60;

                    int segments = 72; // More segments for a smoother circle
                    for (int i = 1; i <= segments; i++)
                    {
                        double angle = Math.PI - (i * 2 * Math.PI / segments);
                        double px = cx + radius * Math.Cos(angle);
                        double py = cy + radius * Math.Sin(angle);

                        TsPointS pt = new TsPointS { X = px, Y = py, Z = z, C = c, T = 0 };
                        
                        try 
                        {
                            _robot.Moves(pt);
                        } 
                        catch { }
                        
                        // Increase tolerance significantly so the next point is sent well before stopping
                        // This allows the robot controller to blend the motion smoothly.
                        double tolerance = Math.Max(5.0, radius * 0.4); 
                        await WaitUntilReachAsync(pt.X, pt.Y, tolerance);
                    }

                    _robot.MvSpeed = oldSpeed;
                    _robot.MvAccel = oldAcc;
                    _robot.MvDecel = oldDec;
                });
                
                _logger.Log("[Draw] Circle completed.");
            }
            catch (Exception ex)
            {
                _logger.Log($"Draw Circle Error: {ex.Message}");
            }
        }

        private async Task MoveToSafeDrawPositionAsync()
        {
            var pos = GetCurrentPosition()?.WorldPosition;
            if (pos == null) return;
            
            // Safe coordinates for drawing (middle of typical SCARA workspace)
            double safeX = 150.0;
            double safeY = 0.0;
            
            double dx = pos.X - safeX;
            double dy = pos.Y - safeY;
            
            if (Math.Sqrt(dx*dx + dy*dy) > 5.0)
            {
                _logger.Log($"[Draw] Auto-moving to safe start position ({safeX}, {safeY})...");
                TsPointS safePt = new TsPointS { X = safeX, Y = safeY, Z = pos.Z, C = pos.C, T = 0 };
                _robot.Move(safePt); 
                await WaitUntilReachAsync(safeX, safeY, 5.0);
            }
        }

        private async Task WaitUntilReachAsync(double targetX, double targetY, double tolerance = 2.0)
        {
            int timeoutMs = 10000;
            int elapsedMs = 0;
            while (elapsedMs < timeoutMs)
            {
                var pos = GetCurrentPosition()?.WorldPosition;
                if (pos != null)
                {
                    double dx = pos.X - targetX;
                    double dy = pos.Y - targetY;
                    if (Math.Sqrt(dx * dx + dy * dy) <= tolerance)
                    {
                        break;
                    }
                }
                await Task.Delay(50);
                elapsedMs += 50;
            }
        }

        public void StopMotion()
        {
            if (!State.IsConnected || _robot == null) return;
            try
            {
                _robot.ProgramStop();
                _logger.Log("STOP requested.");
            }
            catch (Exception ex)
            {
                _logger.Log($"Stop Error: {ex.Message}");
            }
        }

        private bool HasActiveAlarms(TsStatusAllS status)
        {
            if (status.AlarmNo == null || status.AlarmNo.Length == 0) return false;
            
            bool hasAlarm = false;
            foreach (string alarm in status.AlarmNo)
            {
                if (!string.IsNullOrEmpty(alarm) && alarm != "0" && alarm.Trim() != "")
                {
                    _logger.Log($"    \u26A0 Active Alarm: {alarm}");
                    hasAlarm = true;
                }
            }
            return hasAlarm;
        }

        private void LogControllerStatus(TsStatusAllS status)
        {
            string[] modeNames = { "Teaching", "Internal", "Ext_Sig", "Ext", "Ext232C", "ExtEther" };
            string modeName = (status.MasterMode >= 0 && status.MasterMode < modeNames.Length)
                ? modeNames[status.MasterMode]
                : $"Unknown({status.MasterMode})";

            State.MasterModeName = modeName;

            _logger.Log($"    MasterMode: {modeName} ({status.MasterMode})");
            _logger.Log($"    ServoStatus: {(status.ServoStatus == 1 ? "ON" : "OFF")}");
            _logger.Log($"    EmergencySwitch: {(status.EmergencySwitch == 1 ? "ACTIVE!" : "Normal")}");
            _logger.Log($"    RunStatus: {status.RunStatus}");
        }

        private void NotifyStateChanged()
        {
            StateChanged?.Invoke();
        }
    }
}
