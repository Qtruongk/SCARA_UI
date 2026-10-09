using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Test_1.Models;
using Test_1.Services;
using Test_1.Utilities;

namespace Test_1.UI
{
    public partial class MainForm : Form
    {
        private readonly RobotService _robotService;
        private readonly LoggingService _logger;
        private readonly MotionService _motionService;
        private bool _connectionChangePending;
        private bool _statusCommandPending;

        public MainForm()
        {
            InitializeComponent();
            
            // Initialize Services
            _logger = new LoggingService();
            _logger.OnLogMessage += LogMessage;

            _robotService = new RobotService(_logger);
            _robotService.StateChanged += UpdateButtonStates;

            _motionService = new MotionService();
            _motionService.OnDemoStep += MotionService_OnDemoStep;
            _motionService.OnDemoFinished += MotionService_OnDemoFinished;

            InitializeModernUI();
            
            _logger.Log("System initialized. Ready to connect.");
        }

        private void LogMessage(string message)
        {
            if (IsDisposed || Disposing || txtLog.IsDisposed) return;
            if (txtLog.InvokeRequired)
            {
                try { txtLog.BeginInvoke(new Action(() => LogMessage(message))); }
                catch (InvalidOperationException) { /* The window closed while a worker was logging. */ }
            }
            else
            {
                txtLog.AppendText(message);
            }
        }

        private void UpdateButtonStates()
        {
            if (IsDisposed || Disposing) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(UpdateButtonStates)); }
                catch (InvalidOperationException) { /* The window closed while a driver event was queued. */ }
                return;
            }

            var state = _robotService.State;
            UpdateDigitalTwinConnection(state.IsConnected && !_connectionChangePending);
            btnConnect.Enabled = !_connectionChangePending && !_statusCommandPending;

            Color brandOrange = Color.FromArgb(232, 93, 4);
            Color dangerRed = Color.FromArgb(220, 53, 69);
            Color successGreen = Color.FromArgb(40, 167, 69);
            Color lightGrayBg = Color.FromArgb(240, 240, 240);
            Color textDark = Color.FromArgb(40, 40, 40);
            Color textMuted = Color.FromArgb(120, 120, 120);

            if (state.IsConnected)
            {
                btnConnect.Text = "\u2716 Disconnect";
                btnConnect.BackColor = dangerRed;
                btnConnect.ForeColor = Color.White;

                btnServoOn.Enabled = !_connectionChangePending && !_statusCommandPending;
                btnServoOff.Enabled = !_connectionChangePending && !_statusCommandPending;
                btnWatchdog.Enabled = !_connectionChangePending && !_statusCommandPending;

                btnServoOn.ForeColor = state.IsServoOn ? Color.White : textDark;
                btnServoOn.BackColor = state.IsServoOn ? successGreen : lightGrayBg;

                btnServoOff.ForeColor = !state.IsServoOn ? Color.White : textDark;
                btnServoOff.BackColor = !state.IsServoOn ? dangerRed : lightGrayBg;

                btnWatchdog.ForeColor = state.IsWatchdogRunning ? Color.White : textDark;
                btnWatchdog.BackColor = state.IsWatchdogRunning ? Color.FromArgb(0, 123, 255) : lightGrayBg;
                btnWatchdog.Text = state.IsWatchdogRunning ? "\uD83D\uDC41 Watchdog ON" : "\uD83D\uDC41 Watchdog";
            }
            else
            {
                btnConnect.Text = "\u26A1 Connect";
                btnConnect.BackColor = brandOrange;
                btnConnect.ForeColor = Color.White;

                btnServoOn.Enabled = false;
                btnServoOff.Enabled = false;
                btnWatchdog.Enabled = false;
                
                btnServoOn.BackColor = lightGrayBg;
                btnServoOff.BackColor = lightGrayBg;
                btnWatchdog.BackColor = lightGrayBg;
            }

            lblConnectionStatus.Text = state.IsConnected ? "\u25CF Connected" : "\u25CB Disconnected";
            lblConnectionStatus.ForeColor = state.IsConnected ? successGreen : dangerRed;

            lblServoStatus.Text = state.IsConnected ? (state.IsServoOn ? "\u25CF Servo: ON" : "\u25CB Servo: OFF") : "\u25CB Servo: N/A";
            lblServoStatus.ForeColor = state.IsConnected ? (state.IsServoOn ? successGreen : brandOrange) : textMuted;

            lblWatchdogStatus.Text = state.IsWatchdogRunning ? "\u25CF Watchdog: Active" : "\u25CB Watchdog: Off";
            lblWatchdogStatus.ForeColor = state.IsWatchdogRunning ? Color.FromArgb(0, 123, 255) : textMuted;
        }

        private async void btnConnect_Click(object sender, EventArgs e)
        {
            if (_connectionChangePending || _statusCommandPending) return;
            _connectionChangePending = true;
            UpdateButtonStates();
            try
            {
                if (_robotService.State.IsConnected)
                    await Task.Run(() => _robotService.Disconnect());
                else
                    await Task.Run(() => _robotService.Connect());
            }
            catch (Exception ex)
            {
                _logger.Log("Connection change error: " + ex.Message);
            }
            finally
            {
                _connectionChangePending = false;
                if (!IsDisposed && !Disposing) UpdateButtonStates();
            }
        }

        private async void btnServoOn_Click(object sender, EventArgs e)
        {
            await RunRobotStatusCommand(_robotService.TurnServoOn);
        }

        private async void btnServoOff_Click(object sender, EventArgs e)
        {
            await RunRobotStatusCommand(_robotService.TurnServoOff);
        }

        private async void btnWatchdog_Click(object sender, EventArgs e)
        {
            await RunRobotStatusCommand(_robotService.StartWatchdog);
        }

        private async Task RunRobotStatusCommand(Action command)
        {
            if (_connectionChangePending || _statusCommandPending) return;
            _statusCommandPending = true;
            UpdateButtonStates();
            try { await Task.Run(command); }
            catch (Exception ex) { _logger.Log("Controller command error: " + ex.Message); }
            finally
            {
                _statusCommandPending = false;
                if (!IsDisposed && !Disposing) UpdateButtonStates();
            }
        }

        private void MotionService_OnDemoStep(double t, double p, double v, double a)
        {
            if (IsDisposed || Disposing) return;
            if (InvokeRequired)
            {
                Invoke(new Action(() => MotionService_OnDemoStep(t, p, v, a)));
                return;
            }
            
            if (sPos != null && sVel != null && sAcc != null)
            {
                sPos.Points.AddXY(t, p);
                sVel.Points.AddXY(t, v);
                sAcc.Points.AddXY(t, a);
                motionChart?.Update();
            }
        }

        private void MotionService_OnDemoFinished()
        {
            if (IsDisposed || Disposing) return;
            if (InvokeRequired)
            {
                Invoke(new Action(MotionService_OnDemoFinished));
                return;
            }

            if (btnStartDemo != null)
            {
                btnStartDemo.Enabled = true;
            }
            if (btnStartTrap != null)
            {
                btnStartTrap.Enabled = true;
            }
        }
    }
}
