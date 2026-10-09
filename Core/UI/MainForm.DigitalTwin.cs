using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Forms;
using Test_1.Utilities;

namespace Test_1.UI
{
    public partial class MainForm
    {
        private ScaraDigitalTwinView _digitalTwin;
        private Timer _feedbackTimer;
        private readonly Stopwatch _feedbackAge = new Stopwatch();
        private bool _feedbackReadPending;
        private bool _twinConnected;
        private bool _twinStopped;
        private int _feedbackSession;

        private Control InitializeDigitalTwin()
        {
            _digitalTwin = new ScaraDigitalTwinView { Dock = DockStyle.Fill, Name = "scaraDigitalTwin" };
            _digitalTwin.SetConnectionState(false);
            _feedbackTimer = new Timer(components) { Interval = Constants.DigitalTwinRefreshMs };
            _feedbackTimer.Tick += PollRobotFeedback;
            _feedbackTimer.Start();
            return _digitalTwin;
        }

        private void UpdateDigitalTwinConnection(bool connected)
        {
            if (_digitalTwin == null || _twinStopped || connected == _twinConnected) return;
            _twinConnected = connected;
            // Ignore an outstanding reply if its connection has since been replaced.
            _feedbackSession++;
            _feedbackAge.Reset();
            _digitalTwin.SetConnectionState(connected);
        }

        private async void PollRobotFeedback(object sender, EventArgs e)
        {
            if (_twinStopped || IsDisposed || Disposing || !_twinConnected) return;
            if (_feedbackReadPending)
            {
                if (_feedbackAge.IsRunning && _feedbackAge.ElapsedMilliseconds >= Constants.DigitalTwinStaleMs)
                    _digitalTwin.SetFeedbackUnavailable();
                return;
            }

            _feedbackReadPending = true;
            int session = _feedbackSession;
            if (!_feedbackAge.IsRunning) _feedbackAge.Start();
            try
            {
                // Socket I/O runs off the UI thread; at most one feedback request is in flight.
                var position = await Task.Run(() => _robotService.GetCurrentPosition());
                if (_twinStopped || IsDisposed || Disposing || session != _feedbackSession || !_twinConnected) return;
                if (position == null || position.JointPosition == null)
                {
                    _digitalTwin.SetFeedbackUnavailable();
                    return;
                }
                _digitalTwin.ApplyFeedback(position);
                _feedbackAge.Restart();
            }
            catch (Exception)
            {
                if (!_twinStopped && !IsDisposed && !Disposing && session == _feedbackSession && _twinConnected)
                    _digitalTwin.SetFeedbackUnavailable();
            }
            finally
            {
                _feedbackReadPending = false;
            }
        }

        private void ShutdownDigitalTwin()
        {
            if (_twinStopped) return;
            _twinStopped = true;
            _feedbackSession++;
            if (_feedbackTimer != null) _feedbackTimer.Stop();
            if (_robotService != null) _robotService.StateChanged -= UpdateButtonStates;
            if (_logger != null) _logger.OnLogMessage -= LogMessage;
            if (_motionService != null)
            {
                _motionService.OnDemoStep -= MotionService_OnDemoStep;
                _motionService.OnDemoFinished -= MotionService_OnDemoFinished;
            }
        }
    }
}
