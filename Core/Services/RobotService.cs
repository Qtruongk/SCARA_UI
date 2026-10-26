using System;
using System.Threading;
using System.Threading.Tasks;
using System.Drawing;
using Test_1.Models;
using Test_1.Utilities;
using TsRemoteLib;

namespace Test_1.Services
{
    public class RobotService
    {
        private TsRemoteS _robot;
        private readonly LoggingService _logger;
        // Protect the two feedback reads from a concurrent disconnect/reconnect.
        // Move* and STOP use the driver's own transport lock and remain independent.
        private readonly object _feedbackLifecycle = new object();
        
        public RobotState State { get; private set; }

        public event Action StateChanged;

        public RobotService(LoggingService logger)
        {
            _logger = logger;
            State = new RobotState();
        }

        public bool Connect(string ip = Constants.DefaultRobotIP, int port = Constants.DefaultRobotPort, int srcPort = Constants.DefaultSourcePort)
        {
            lock (_feedbackLifecycle)
            {
                return ConnectCore(ip, port, srcPort);
            }
        }

        private bool ConnectCore(string ip, int port, int srcPort)
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
            lock (_feedbackLifecycle)
            {
                DisconnectCore();
            }
        }

        private void DisconnectCore()
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
            lock (_feedbackLifecycle)
            {
                StartWatchdogCore();
            }
        }

        private void StartWatchdogCore()
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
            lock (_feedbackLifecycle)
            {
                return ReadCurrentPosition();
            }
        }

        private RobotPositionData ReadCurrentPosition()
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

        private async Task WaitUntilReach3DAsync(double targetX, double targetY, double targetZ, double tolerance = 3.0)
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
                    double dz = pos.Z - targetZ;
                    if (Math.Sqrt(dx * dx + dy * dy + dz * dz) <= tolerance)
                    {
                        break;
                    }
                }
                await Task.Delay(50);
                elapsedMs += 50;
            }
        }

        /// <summary>
        /// Điều khiển Robot THL300 cầm bút vẽ chữ X thật sự vào ô vuông trên giấy theo 4 đỉnh góc ranh giới.
        /// </summary>
        public async Task<bool> DrawXInCellAsync(PointF[] cornersRobot, double workZ, double speedPercent = 20.0, double liftHeightMm = 10.0, double marginRatio = 0.20)
        {
            if (!State.IsConnected || _robot == null)
            {
                _logger.Log("[Draw X] Thất bại: Robot chưa kết nối.");
                return false;
            }

            if (!State.IsServoOn)
            {
                _logger.Log("[Draw X] Thất bại: Servo đang TẮT.");
                return false;
            }

            if (cornersRobot == null || cornersRobot.Length < 4)
            {
                _logger.Log("[Draw X] Thất bại: Thiếu dữ liệu 4 góc ranh giới.");
                return false;
            }

            // Kiểm tra an toàn bán kính làm việc R <= 300mm cho cả 4 góc
            for (int i = 0; i < 4; i++)
            {
                double r = Math.Sqrt(cornersRobot[i].X * cornersRobot[i].X + cornersRobot[i].Y * cornersRobot[i].Y);
                if (r > 300.0)
                {
                    _logger.Log($"[Draw X] Hủy lệnh: Góc #{i + 1} có R={r:F1}mm vượt quá bán kính an toàn 300mm!");
                    return false;
                }
            }

            try
            {
                var curPos = GetCurrentPosition()?.WorldPosition;
                double cAngle = curPos?.C ?? 0.0;
                double zSafe = workZ + Math.Abs(liftHeightMm);
                double zWork = workZ;

                PointF tl = cornersRobot[0];
                PointF tr = cornersRobot[1];
                PointF br = cornersRobot[2];
                PointF bl = cornersRobot[3];

                float m = (float)Math.Max(0.05, Math.Min(0.40, marginRatio));

                // Nét 1: Chéo từ TL sang BR
                PointF p1Start = new PointF(tl.X + m * (br.X - tl.X), tl.Y + m * (br.Y - tl.Y));
                PointF p1End = new PointF(br.X - m * (br.X - tl.X), br.Y - m * (br.Y - tl.Y));

                // Nét 2: Chéo từ TR sang BL
                PointF p2Start = new PointF(tr.X + m * (bl.X - tr.X), tr.Y + m * (bl.Y - tr.Y));
                PointF p2End = new PointF(bl.X - m * (bl.X - tr.X), bl.Y - m * (bl.Y - tr.Y));

                _logger.Log($"[Draw X] Bắt đầu vẽ chữ X (Z_work={zWork:F1}mm, Z_safe={zSafe:F1}mm, Tốc độ: {speedPercent}%)...");

                await Task.Run(async () =>
                {
                    double oldSpeed = _robot.MvSpeed;
                    double oldAcc = _robot.MvAccel;
                    double oldDec = _robot.MvDecel;

                    _robot.MvSpeed = Math.Max(5, Math.Min(50, speedPercent));
                    _robot.MvAccel = 50;
                    _robot.MvDecel = 50;

                    try
                    {
                        // 1. Nhấc bút lên độ cao an toàn
                        if (curPos != null)
                        {
                            _robot.Move(new TsPointS { X = curPos.X, Y = curPos.Y, Z = zSafe, C = cAngle, T = 0 });
                            await WaitUntilReach3DAsync(curPos.X, curPos.Y, zSafe, 3.0);
                        }

                        // --- NÉT 1 (TL -> BR) ---
                        // 2. Bay ngang đến điểm bắt đầu nét 1
                        _robot.Move(new TsPointS { X = p1Start.X, Y = p1Start.Y, Z = zSafe, C = cAngle, T = 0 });
                        await WaitUntilReachAsync(p1Start.X, p1Start.Y, 2.0);

                        // 3. Hạ bút xuống mặt giấy
                        _robot.Moves(new TsPointS { X = p1Start.X, Y = p1Start.Y, Z = zWork, C = cAngle, T = 0 });
                        await WaitUntilReach3DAsync(p1Start.X, p1Start.Y, zWork, 2.0);

                        // 4. Kẻ nét 1 đến p1End
                        _robot.Moves(new TsPointS { X = p1End.X, Y = p1End.Y, Z = zWork, C = cAngle, T = 0 });
                        await WaitUntilReachAsync(p1End.X, p1End.Y, 2.0);

                        // 5. Nhấc bút lên Z_safe
                        _robot.Moves(new TsPointS { X = p1End.X, Y = p1End.Y, Z = zSafe, C = cAngle, T = 0 });
                        await WaitUntilReach3DAsync(p1End.X, p1End.Y, zSafe, 2.0);

                        // --- NÉT 2 (TR -> BL) ---
                        // 6. Bay ngang đến điểm bắt đầu nét 2
                        _robot.Move(new TsPointS { X = p2Start.X, Y = p2Start.Y, Z = zSafe, C = cAngle, T = 0 });
                        await WaitUntilReachAsync(p2Start.X, p2Start.Y, 2.0);

                        // 7. Hạ bút xuống mặt giấy
                        _robot.Moves(new TsPointS { X = p2Start.X, Y = p2Start.Y, Z = zWork, C = cAngle, T = 0 });
                        await WaitUntilReach3DAsync(p2Start.X, p2Start.Y, zWork, 2.0);

                        // 8. Kẻ nét 2 đến p2End
                        _robot.Moves(new TsPointS { X = p2End.X, Y = p2End.Y, Z = zWork, C = cAngle, T = 0 });
                        await WaitUntilReachAsync(p2End.X, p2End.Y, 2.0);

                        // 9. Nhấc bút lên Z_safe kết thúc
                        _robot.Moves(new TsPointS { X = p2End.X, Y = p2End.Y, Z = zSafe, C = cAngle, T = 0 });
                        await WaitUntilReach3DAsync(p2End.X, p2End.Y, zSafe, 2.0);
                    }
                    finally
                    {
                        _robot.MvSpeed = oldSpeed;
                        _robot.MvAccel = oldAcc;
                        _robot.MvDecel = oldDec;
                    }
                });

                _logger.Log("[Draw X] ✔ Đã vẽ xong chữ X thành công!");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Log($"[Draw X] Lỗi thực thi: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Điều khiển Robot THL300 cầm bút vẽ chữ O thật sự vào ô vuông trên giấy theo 4 đỉnh góc ranh giới.
        /// </summary>
        public async Task<bool> DrawOInCellAsync(PointF[] cornersRobot, double workZ, double speedPercent = 20.0, double liftHeightMm = 10.0, double radiusRatio = 0.32)
        {
            if (!State.IsConnected || _robot == null)
            {
                _logger.Log("[Draw O] Thất bại: Robot chưa kết nối.");
                return false;
            }

            if (!State.IsServoOn)
            {
                _logger.Log("[Draw O] Thất bại: Servo đang TẮT.");
                return false;
            }

            if (cornersRobot == null || cornersRobot.Length < 4)
            {
                _logger.Log("[Draw O] Thất bại: Thiếu dữ liệu 4 góc ranh giới.");
                return false;
            }

            // Kiểm tra an toàn bán kính làm việc R <= 300mm
            for (int i = 0; i < 4; i++)
            {
                double r = Math.Sqrt(cornersRobot[i].X * cornersRobot[i].X + cornersRobot[i].Y * cornersRobot[i].Y);
                if (r > 300.0)
                {
                    _logger.Log($"[Draw O] Hủy lệnh: Góc #{i + 1} có R={r:F1}mm vượt quá bán kính an toàn 300mm!");
                    return false;
                }
            }

            try
            {
                var curPos = GetCurrentPosition()?.WorldPosition;
                double cAngle = curPos?.C ?? 0.0;
                double zSafe = workZ + Math.Abs(liftHeightMm);
                double zWork = workZ;

                PointF tl = cornersRobot[0];
                PointF tr = cornersRobot[1];
                PointF br = cornersRobot[2];
                PointF bl = cornersRobot[3];

                // Tâm ô
                double cx = (tl.X + tr.X + br.X + bl.X) / 4.0;
                double cy = (tl.Y + tr.Y + br.Y + bl.Y) / 4.0;

                // Kích thước cạnh ô
                double w = Math.Sqrt(Math.Pow(tr.X - tl.X, 2) + Math.Pow(tr.Y - tl.Y, 2));
                double h = Math.Sqrt(Math.Pow(bl.X - tl.X, 2) + Math.Pow(bl.Y - tl.Y, 2));
                double radius = Math.Min(w, h) * Math.Max(0.15, Math.Min(0.45, radiusRatio));

                // Vector trực chuẩn hệ ô
                double ux = (tr.X - tl.X) / (w + 1e-12);
                double uy = (tr.Y - tl.Y) / (w + 1e-12);
                double vx = (bl.X - tl.X) / (h + 1e-12);
                double vy = (bl.Y - tl.Y) / (h + 1e-12);

                int segments = 36;
                var circlePoints = new PointF[segments + 1];
                for (int i = 0; i <= segments; i++)
                {
                    double angle = i * 2.0 * Math.PI / segments;
                    double px = cx + radius * Math.Cos(angle) * ux + radius * Math.Sin(angle) * vx;
                    double py = cy + radius * Math.Cos(angle) * uy + radius * Math.Sin(angle) * vy;
                    circlePoints[i] = new PointF((float)px, (float)py);
                }

                _logger.Log($"[Draw O] Bắt đầu vẽ chữ O (Tâm: ({cx:F1}, {cy:F1}), Bán kính: {radius:F1}mm, Tốc độ: {speedPercent}%)...");

                await Task.Run(async () =>
                {
                    double oldSpeed = _robot.MvSpeed;
                    double oldAcc = _robot.MvAccel;
                    double oldDec = _robot.MvDecel;

                    _robot.MvSpeed = Math.Max(5, Math.Min(40, speedPercent));
                    _robot.MvAccel = 50;
                    _robot.MvDecel = 50;

                    try
                    {
                        // 1. Nhấc bút lên độ cao an toàn
                        if (curPos != null)
                        {
                            _robot.Move(new TsPointS { X = curPos.X, Y = curPos.Y, Z = zSafe, C = cAngle, T = 0 });
                            await WaitUntilReach3DAsync(curPos.X, curPos.Y, zSafe, 3.0);
                        }

                        // 2. Bay ngang đến điểm bắt đầu đường tròn
                        PointF startPt = circlePoints[0];
                        _robot.Move(new TsPointS { X = startPt.X, Y = startPt.Y, Z = zSafe, C = cAngle, T = 0 });
                        await WaitUntilReachAsync(startPt.X, startPt.Y, 2.0);

                        // 3. Hạ bút xuống mặt giấy
                        _robot.Moves(new TsPointS { X = startPt.X, Y = startPt.Y, Z = zWork, C = cAngle, T = 0 });
                        await WaitUntilReach3DAsync(startPt.X, startPt.Y, zWork, 2.0);

                        // 4. Nội suy vẽ liên tục 360 độ
                        for (int i = 1; i <= segments; i++)
                        {
                            PointF pt = circlePoints[i];
                            _robot.Moves(new TsPointS { X = pt.X, Y = pt.Y, Z = zWork, C = cAngle, T = 0 });
                            await WaitUntilReachAsync(pt.X, pt.Y, 2.5);
                        }

                        // 5. Nhấc bút lên Z_safe kết thúc
                        PointF endPt = circlePoints[segments];
                        _robot.Moves(new TsPointS { X = endPt.X, Y = endPt.Y, Z = zSafe, C = cAngle, T = 0 });
                        await WaitUntilReach3DAsync(endPt.X, endPt.Y, zSafe, 2.0);
                    }
                    finally
                    {
                        _robot.MvSpeed = oldSpeed;
                        _robot.MvAccel = oldAcc;
                        _robot.MvDecel = oldDec;
                    }
                });

                _logger.Log("[Draw O] ✔ Đã vẽ xong chữ O thành công!");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Log($"[Draw O] Lỗi thực thi: {ex.Message}");
                return false;
            }
        }

        public void StopMotion()
        {
            if (!State.IsConnected || _robot == null) return;
            try
            {
                _robot.ProgramStop();
                try { _robot.ProgramBreak(); } catch { }
                _logger.Log("STOP requested. Robot motion halted.");
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
