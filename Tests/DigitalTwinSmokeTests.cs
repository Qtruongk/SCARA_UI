using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Forms.Integration;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using Test_1.Models;
using Test_1.UI;
using TsRemoteLib;
using Forms = System.Windows.Forms;

// STA integration checks for the real Home control using synthetic feedback only.
// No driver, network, servo or movement commands are called by this executable.
internal static class DigitalTwinSmokeTests
{
    private static int _checks;
    private static string _output;
    private const double SourceCadToolZ = 112;
    private const double FeedbackZeroToolZ = -48;

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            _output = args.Length > 0 ? args[0] : AppDomain.CurrentDomain.BaseDirectory;
            Directory.CreateDirectory(_output);
            RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
            Forms.Application.EnableVisualStyles();
            Forms.Application.SetCompatibleTextRenderingDefault(false);
            using (var form = new MainForm())
            {
                form.StartPosition = Forms.FormStartPosition.Manual;
                form.Location = new System.Drawing.Point(-20000, -20000);
                form.ShowInTaskbar = false;
                form.Size = new System.Drawing.Size(1280, 800);
                form.Show();
                DrainUi();
                var twin = (Forms.Control)Field(form, "_digitalTwin");
                Check(twin != null && twin.Visible, "Home contains a visible digital twin");
                ((ScaraDigitalTwinView)twin).Calibration = new ScaraDigitalTwinProfile();
                VerifyDriverUncreated(form);
                VerifyLayout(form, twin, 1280, 800);
                VerifyCadGeometry(twin);
                VerifyCoordinateSystem(twin);
                VerifyInitialReadouts(twin);
                VerifyToolPose(twin, 300, 0, SourceCadToolZ, 0, "Manufacturer CAD reference pose");
                Capture(form, twin, "home-cad-reference-1280x800");
                VerifyFeedback(twin);
                Capture(form, twin, "home-live-1280x800");
                Call(twin, "ApplyFeedback", Pose(180, 0, 0, 0));
                DrainUi();
                Capture(form, twin, "home-negative-x-1280x800");
                Call(twin, "ApplyFeedback", Pose(35, -65, 75, 45));
                Call(twin, "SetConnectionState", false);
                DrainUi();
                Capture(form, twin, "home-disconnected-1280x800");
                Call(twin, "SetConnectionState", true);
                Call(twin, "ApplyFeedback", Pose(35, -65, 75, 45));

                var guide = Descendants(form).OfType<Forms.Button>().First(b => b.Text.Contains("Guide key"));
                var home = Descendants(form).OfType<Forms.Button>().First(b => b.Text.Contains("Home"));
                guide.PerformClick();
                DrainUi();
                Check(!twin.Visible, "Guide navigation hides Home renderer");
                home.PerformClick();
                DrainUi();
                Check(twin.Visible, "Returning Home restores renderer");

                form.Size = new System.Drawing.Size(900, 600);
                DrainUi();
                Call(twin, "ApplyFeedback", Pose(35, -65, 75, 45));
                Capture(form, twin, "home-live-900x600");
                VerifyLayout(form, twin, 900, 600);
                VerifyDriverUncreated(form);
                VerifyPollingLifecycle(form, twin);
                VerifyCloseDuringBlockedRead(form);
                Check((bool)Field(form, "_twinStopped"), "Closing form stops feedback lifecycle");
            }
            Console.WriteLine("PASS: " + _checks + " offline digital-twin assertions.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    private static void VerifyFeedback(Forms.Control twin)
    {
        var initial = GeometrySignature(twin);
        var coordinateInitial = CoordinateGeometrySignature(twin);
        var groundInitial = GroundGridSignature(twin);
        var labelInitial = VisibleCoordinateLabelPositions(twin);
        Call(twin, "ApplyFeedback", Pose(20, 30, 60, 40));
        DrainUi();
        Check(GeometrySignature(twin) == initial, "Disconnected view ignores unsolicited feedback");
        Check(ReadoutValues(twin).Values.All(v => v.Text == "—"), "Disconnected unsolicited feedback does not invent coordinate or joint readings");
        Call(twin, "SetConnectionState", true);
        Check(Status(twin).Contains("WAIT"), "Connecting waits for actual feedback");
        VerifyReadoutStatus(twin, "WAITING");
        Call(twin, "SetFeedbackUnavailable");
        Check(Status(twin).Contains("STALE"), "Failed first read sets stale feedback state");
        VerifyReadoutStatus(twin, "STALE");
        Check(ReadoutValues(twin).Values.All(v => v.Text == "—"), "Failed first read displays unavailable values instead of fake zeros");
        Check(GeometrySignature(twin) == initial, "Failed first read preserves reference geometry");
        var zeroPose = Pose(0, 0, 0, 0);
        Call(twin, "ApplyFeedback", zeroPose);
        DrainUi();
        Check(Status(twin).Contains("LIVE"), "Valid feedback marks view live");
        VerifyReadouts(twin, zeroPose, "First actual feedback");
        VerifyReadoutStatus(twin, "LIVE");
        VerifyToolPose(twin, 300, 0, FeedbackZeroToolZ, 0, "THL300 feedback-zero pose");
        VerifyJointGeometry(twin);
        Check(CoordinateGeometrySignature(twin) == coordinateInitial, "Open coordinate axes remain fixed while all four robot joints move");
        Check(GroundGridSignature(twin) == groundInitial, "Full-canvas ground grid remains fixed while the measured robot moves");
        var labelAfter = VisibleCoordinateLabelPositions(twin);
        var sharedLabels = labelInitial.Keys.Where(labelAfter.ContainsKey).ToList();
        Check(sharedLabels.Count >= 3 && sharedLabels.All(l => (labelAfter[l] - labelInitial[l]).Length < 0.00001),
            "Visible coordinate labels stay world anchored during robot movement while occluded ticks may hide");
        VerifyControllerReadoutSource(twin);

        string lastValid = GeometrySignature(twin);
        string lastReadout = ReadoutSignature(twin);
        Call(twin, "ApplyFeedback", Pose(double.NaN, 5, 20, 10));
        DrainUi();
        Check(GeometrySignature(twin) == lastValid, "Non-finite joint feedback cannot distort geometry");
        Check(Status(twin).Contains("STALE"), "Invalid feedback marks view stale");
        VerifyReadoutStatus(twin, "STALE");
        Check(ReadoutSignature(twin) == lastReadout, "Invalid joints preserve last actual coordinate and joint values");
        Call(twin, "ApplyFeedback", new object[] { null });
        Check(GeometrySignature(twin) == lastValid, "Null feedback preserves last valid pose");
        Call(twin, "ApplyFeedback", new RobotPositionData { JointPosition = null });
        Check(GeometrySignature(twin) == lastValid, "Missing joint feedback preserves last valid pose");
        var invalidWorld = Pose(10, 20, 30, 40);
        invalidWorld.WorldPosition.X = double.PositiveInfinity;
        Call(twin, "ApplyFeedback", invalidWorld);
        Check(GeometrySignature(twin) == lastValid, "Invalid world feedback preserves last valid pose");
        Check(ReadoutSignature(twin) == lastReadout, "Invalid world coordinates cannot partially replace readout values");
        Call(twin, "ApplyFeedback", Pose(15, -35, 55, 80));
        DrainUi();
        Check(Status(twin).Contains("LIVE"), "Fresh valid feedback recovers from stale state");
        lastValid = GeometrySignature(twin);
        lastReadout = ReadoutSignature(twin);
        Call(twin, "SetFeedbackUnavailable");
        DrainUi();
        Check(GeometrySignature(twin) == lastValid, "Unavailable feedback freezes last measured pose");
        Check(Status(twin).Contains("STALE"), "Unavailable feedback sets stale state");
        VerifyReadoutStatus(twin, "STALE");
        Check(ReadoutSignature(twin) == lastReadout, "Failed reads retain last measurements in the readout panel");

        Call(twin, "SetConnectionState", false);
        Call(twin, "ApplyFeedback", Pose(170, 140, 140, 150));
        DrainUi();
        Check(GeometrySignature(twin) == lastValid, "Disconnect ignores a late feedback reply");
        Check(Status(twin).Contains("OFFLINE") || Status(twin).Contains("DISCONNECT"), "Disconnect sets offline state");
        VerifyReadoutStatus(twin, "OFFLINE");
        Check(ReadoutSignature(twin) == lastReadout, "Disconnect and late replies preserve the last actual readout sample");
        Call(twin, "SetConnectionState", true);
        Check(Status(twin).Contains("WAIT"), "Reconnect waits for a new measurement");
        Check(GeometrySignature(twin) == lastValid, "Reconnect does not invent a robot pose");
        VerifyReadoutStatus(twin, "WAITING");
        Check(ReadoutSignature(twin) == lastReadout, "Reconnect retains previous values while waiting for a fresh measurement");
        Call(twin, "ApplyFeedback", Pose(35, -65, 75, 45));
        DrainUi();
        Check(GeometrySignature(twin) != lastValid, "Next measurement after reconnect updates pose");
        VerifyCameraActions(twin);
        VerifyCalibration(twin);
        Call(twin, "ApplyFeedback", Pose(35, -65, 75, 45));
        DrainUi();
        lastValid = GeometrySignature(twin);
        lastReadout = ReadoutSignature(twin);
        for (int i = 0; i < 28; i++) { Thread.Sleep(100); DrainUi(); }
        Check(Status(twin).Contains("STALE"), "A silent feedback stream expires to stale state");
        VerifyReadoutStatus(twin, "STALE");
        Check(GeometrySignature(twin) == lastValid, "Feedback expiry freezes measured geometry");
        Check(ReadoutSignature(twin) == lastReadout, "Feedback expiry retains the last actual numeric readings");
        Call(twin, "ApplyFeedback", Pose(35, -65, 75, 45));
        DrainUi();
    }

    private static Dictionary<string, TextBlock> ReadoutValues(Forms.Control twin)
    {
        return (Dictionary<string, TextBlock>)Field(twin, "_readoutValues");
    }

    private static string ReadoutSignature(Forms.Control twin)
    {
        return string.Join("|", ReadoutValues(twin).OrderBy(p => p.Key).Select(p => p.Key + ":" + p.Value.Text));
    }

    private static void VerifyReadoutStatus(Forms.Control twin, string expected)
    {
        var status = (TextBlock)Field(twin, "_readoutStatus");
        Check(status.IsVisible && status.Text.StartsWith(expected, StringComparison.Ordinal), "Readout status visibly reports " + expected);
    }

    private static void VerifyInitialReadouts(Forms.Control twin)
    {
        var values = ReadoutValues(twin);
        Check(values.Count == 8 && new[] { "X", "Y", "Z", "C", "J1", "J2", "J3", "J4" }.All(values.ContainsKey),
            "Home contains all actual world-coordinate and four joint readings");
        Check(values.Values.All(v => v.IsVisible && v.Text == "—"), "Before feedback every coordinate and joint reading is unavailable");
        VerifyReadoutStatus(twin, "OFFLINE");
        var panel = (Border)Field(twin, "_readoutPanel");
        var labels = WpfDescendants(panel).OfType<TextBlock>().Where(t => !values.Values.Contains(t)).Select(t => Regex.Replace(t.Text, @"\s+", "")).ToList();
        foreach (var key in values.Keys)
        {
            string unit = key == "X" || key == "Y" || key == "Z" || key == "J3" ? "mm" : "°";
            Check(labels.Contains(key + "(" + unit + ")"), key + " readout specifies " + unit + " units");
        }
        Check(labels.Any(t => t.Contains("WORLD")) && labels.Any(t => t.Contains("JOINTS")), "Readout groups distinguish controller WORLD coordinates from joint measurements");
    }

    private static void VerifyReadouts(Forms.Control twin, RobotPositionData sample, string name)
    {
        var expected = new Dictionary<string, double>
        {
            { "X", sample.WorldPosition.X }, { "Y", sample.WorldPosition.Y }, { "Z", sample.WorldPosition.Z }, { "C", sample.WorldPosition.C },
            { "J1", sample.JointPosition.J1 }, { "J2", sample.JointPosition.J2 }, { "J3", sample.JointPosition.J3 }, { "J4", sample.JointPosition.J4 }
        };
        var actual = ReadoutValues(twin);
        foreach (var pair in expected)
        {
            double numeric;
            string text = actual[pair.Key].Text;
            Check(Regex.IsMatch(text, @"^-?\d+\.\d{2}$") && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out numeric) && Math.Abs(numeric - pair.Value) <= 0.00501,
                name + " displays actual " + pair.Key + " = " + pair.Value.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static void VerifyControllerReadoutSource(Forms.Control twin)
    {
        var sample = Pose(12.125, -34.375, 56.625, 78.875);
        sample.WorldPosition.X = 281.375; sample.WorldPosition.Y = -35.625;
        sample.WorldPosition.Z = 62.875; sample.WorldPosition.C = -123.375;
        Call(twin, "ApplyFeedback", sample);
        DrainUi();
        VerifyReadouts(twin, sample, "Independent controller WORLD sample");
        var tip = ((ScaraDigitalTwinView)twin).RenderedToolPosition;
        Check(Math.Abs(tip.X - sample.WorldPosition.X) > 1 && Math.Abs(tip.Z - sample.WorldPosition.Z) > 1,
            "WORLD readouts use actual controller data even when CAD tool coordinates differ");
        var signature = ReadoutSignature(twin);
        sample.WorldPosition.X = 999; sample.JointPosition.J1 = 999;
        DrainUi();
        Check(ReadoutSignature(twin) == signature, "Readout retains a copied feedback sample when source objects are later mutated");
    }

    private static void VerifyJointGeometry(Forms.Control twin)
    {
        // Verify actual CAD model transforms, including each articulated joint.
        int joint = 1;
        var samples = new[] { Pose(90, 0, 0, 0), Pose(90, 90, 0, 0), Pose(90, 90, 80, 0), Pose(90, 90, 80, 70) };
        var expected = new[] { new[] { 0d, 300d, FeedbackZeroToolZ, 90d }, new[] { -175d, 125d, FeedbackZeroToolZ, 180d }, new[] { -175d, 125d, FeedbackZeroToolZ + 80, 180d }, new[] { -175d, 125d, FeedbackZeroToolZ + 80, 250d } };
        foreach (var sample in samples)
        {
            var before = GeometrySignature(twin);
            Call(twin, "ApplyFeedback", sample);
            DrainUi();
            VerifyReadouts(twin, sample, "J" + joint + " feedback");
            Check(GeometrySignature(twin) != before, "J" + joint + " alters articulated 3D geometry");
            var pose = expected[joint - 1];
            VerifyToolPose(twin, pose[0], pose[1], pose[2], pose[3], "J" + joint);
            joint++;
        }
    }

    private static void VerifyToolPose(Forms.Control twin, double x, double y, double z, double yaw, string name)
    {
        var view = (ScaraDigitalTwinView)twin;
        var p = view.RenderedToolPosition;
        Check(Math.Abs(p.X - x) < 0.00001 && Math.Abs(p.Y - y) < 0.00001 && Math.Abs(p.Z - z) < 0.00001,
            name + " places articulated tool at expected XYZ (" + x + ", " + y + ", " + z + ")");
        double angleDelta = (view.RenderedToolYawDegrees - yaw + 540) % 360 - 180;
        Check(Math.Abs(angleDelta) < 0.00001, name + " sets expected tool orientation " + yaw + " degrees");
    }

    private static void VerifyCameraActions(Forms.Control twin)
    {
        // Isolate camera actions from elapsed-time expiry while exercising the
        // slower software-rendered floor checks. Expiry is checked separately.
        var staleTimer = (DispatcherTimer)Field(twin, "_staleTimer");
        staleTimer.Stop();
        var host = Descendants(twin).OfType<ElementHost>().Single();
        var viewport = FindViewport(host.Child);
        var camera = (OrthographicCamera)viewport.Camera;
        var position = camera.Position;
        double cameraWidth = camera.Width;
        var geometry = GeometrySignature(twin);
        var labels = CoordinateLabelSignature(twin);
        var readouts = ReadoutSignature(twin);
        var feedbackState = Status(twin);
        var buttons = (Dictionary<string, ButtonBase>)Field(twin, "_cameraButtons");
        Action<string> click = key => buttons[key].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        viewport.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, 120) { RoutedEvent = Mouse.MouseWheelEvent });
        DrainUi();
        Check(Math.Abs(camera.Width - cameraWidth) > 0.00001, "Mouse-wheel zoom changes the orthographic model scale");
        Check(CoordinateLabelSignature(twin) != labels, "Coordinate labels reproject when zoom changes");
        click("Reset");
        DrainUi();
        Check((camera.Position - position).Length < 0.00001 && Math.Abs(camera.Width - cameraWidth) < 0.00001 && Math.Abs((double)Field(twin, "_zoom") - 1) < 0.00001,
            "Corner reset button restores the initial view and zoom");
        Check(CoordinateLabelSignature(twin) == labels, "Corner reset restores coordinate label projection");
        double azimuth = (double)Field(twin, "_azimuth");
        double elevation = (double)Field(twin, "_elevation");
        click("Left");
        Check(Math.Abs((double)Field(twin, "_azimuth") - azimuth) == 12 && camera.Position != position && CoordinateLabelSignature(twin) != labels,
            "Left corner button orbits the camera by 12 degrees and reprojects coordinate labels");
        click("Right");
        Check(Math.Abs((double)Field(twin, "_azimuth") - azimuth) < 0.00001, "Right corner button reverses horizontal orbit");
        click("Up");
        Check(Math.Abs((double)Field(twin, "_elevation") - elevation - 8) < 0.00001 && camera.Position != position && CoordinateLabelSignature(twin) != labels,
            "Up corner button raises the camera by 8 degrees and reprojects coordinate labels");
        click("Down");
        Check(Math.Abs((double)Field(twin, "_elevation") - elevation) < 0.00001, "Down corner button reverses vertical orbit");
        var elevationField = twin.GetType().GetField("_elevation", BindingFlags.Instance | BindingFlags.NonPublic);
        elevationField.SetValue(twin, 84d);
        Call(twin, "UpdateCamera");
        click("Up");
        bool upperClamp = (double)Field(twin, "_elevation") == 85;
        DrainUi();
        VerifyGroundGrid(twin, "85 degree elevation");
        elevationField.SetValue(twin, 6d);
        Call(twin, "UpdateCamera");
        click("Down");
        Check(upperClamp && (double)Field(twin, "_elevation") == 5, "Corner vertical orbit stays within the usable 5..85 degree range");
        DrainUi();
        VerifyGroundGrid(twin, "5 degree elevation");
        Capture((MainForm)twin.FindForm(), twin, "home-open-floor-low-elevation-1280x800");
        var azimuthField = twin.GetType().GetField("_azimuth", BindingFlags.Instance | BindingFlags.NonPublic);
        var zoomField = twin.GetType().GetField("_zoom", BindingFlags.Instance | BindingFlags.NonPublic);
        foreach (double zoom in new[] { 0.3, 3.0 })
        {
            azimuthField.SetValue(twin, azimuth + 180);
            zoomField.SetValue(twin, zoom);
            Call(twin, "UpdateCamera");
            DrainUi();
            VerifyGroundGrid(twin, "reverse orbit and zoom " + zoom.ToString(CultureInfo.InvariantCulture));
        }
        click("Reset");
        click("Left");
        click("Up");
        Check(GeometrySignature(twin) == geometry && ReadoutSignature(twin) == readouts && Status(twin) == feedbackState,
            "All corner camera actions preserve measured robot pose, readouts and feedback state");
        Capture((MainForm)twin.FindForm(), twin, "home-orbit-1280x800");
        click("Reset");
        DrainUi();
        staleTimer.Start();
    }

    private static void VerifyCalibration(Forms.Control twin)
    {
        var view = (ScaraDigitalTwinView)twin;
        Check(view.Calibration.Link1Length == 125 && view.Calibration.Link2Length == 175, "THL300 CAD link lengths remain 125 and 175 mm");
        Check(typeof(ScaraDigitalTwinProfile).GetProperty("Link1Length").GetSetMethod() == null && typeof(ScaraDigitalTwinProfile).GetProperty("Link2Length").GetSetMethod() == null, "Calibration cannot replace manufacturer CAD link dimensions");
        view.Calibration = new ScaraDigitalTwinProfile
        {
            J1Sign = -1, J2Sign = -1, J3Sign = -1, J4Sign = -1,
            J1Zero = 10, J2Zero = 20, J3Zero = 30, J4Zero = 40
        };
        var calibratedSample = Pose(100, 110, 80, 100);
        Call(twin, "ApplyFeedback", calibratedSample);
        DrainUi();
        VerifyToolPose(twin, -175, -125, SourceCadToolZ - 50, 120, "THL300 calibrated joint signs and zeros");
        VerifyReadouts(twin, calibratedSample, "Calibrated renderer preserves raw feedback");
        view.Calibration = new ScaraDigitalTwinProfile { UseWorldToolYaw = true };
        var worldYawSample = Pose(60, -25, 30, 150);
        Call(twin, "ApplyFeedback", worldYawSample);
        DrainUi();
        Check(Math.Abs(view.RenderedToolYawDegrees - 150) < 0.00001, "Compensated J4 mode uses actual world C orientation");
        VerifyReadouts(twin, worldYawSample, "World-yaw rendering preserves actual joint readings");
        view.Calibration = new ScaraDigitalTwinProfile();
    }

    private static RobotPositionData Pose(double j1, double j2, double j3, double j4)
    {
        return new RobotPositionData
        {
            JointPosition = new TsJointS { J1 = j1, J2 = j2, J3 = j3, J4 = j4 },
            WorldPosition = new TsPointS { X = 280.5, Y = -35.25, Z = 60, C = j4 }
        };
    }

    private static void VerifyLayout(MainForm form, Forms.Control twin, int width, int height)
    {
        Check(form.Width == width && form.Height == height, "Form supports " + width + "x" + height);
        Check(twin.Parent != null && twin.Parent.Name == "workspaceHome", "Digital twin occupies Home workspace");
        Check(twin.Width >= 300 && twin.Height >= 200, "Twin panel remains usable at " + width + "x" + height);
        var host = Descendants(twin).OfType<ElementHost>().Single();
        var viewport = FindViewport(host.Child);
        Check(viewport != null && viewport.ActualWidth >= 300 && viewport.ActualHeight >= 200, "CAD viewport retains usable dimensions");
        var descendants = WpfDescendants(host.Child).ToList();
        var cameraButtons = (Dictionary<string, ButtonBase>)Field(twin, "_cameraButtons");
        Check(cameraButtons.Count == 5 && new[] { "Left", "Right", "Up", "Down", "Reset" }.All(cameraButtons.ContainsKey) &&
            descendants.OfType<ButtonBase>().Count() == 5 && descendants.OfType<ButtonBase>().All(cameraButtons.Values.Contains) &&
            !descendants.Any(n => n is TextBox || n is Slider), "Home only offers the five camera buttons without robot command controls");
        Check(cameraButtons.Where(p => p.Key != "Reset").All(p => p.Value is RepeatButton) && cameraButtons["Reset"] is System.Windows.Controls.Button,
            "Directional camera buttons support hold-to-orbit and reset is a single action");
        var coordinateCanvas = (Canvas)Field(twin, "_coordinateLabels");
        Check(Math.Abs(coordinateCanvas.ActualWidth - viewport.ActualWidth) < 1 && Math.Abs(coordinateCanvas.ActualHeight - viewport.ActualHeight) < 1, "Coordinate label layer fills the plot area");
        var labels = WpfDescendants(coordinateCanvas).OfType<TextBlock>().ToList();
        Check(labels.Count >= 3 && labels.All(l => Regex.IsMatch(l.Text.Trim(), @"^(?:[XYZ](?:\s*[\(\[]?mm[\)\]]?)?|O|mm|[-+]?\d+(?:\.\d+)?)$")), "Plot text contains coordinate axes, origin, units and numeric ticks");
        foreach (var axis in new[] { "X", "Y", "Z" })
            Check(labels.Any(l => l.IsVisible && l.Text.Trim().StartsWith(axis)), axis + " coordinate label remains visible at " + width + "x" + height);
        var panel = new Rect(new System.Windows.Point(), coordinateCanvas.RenderSize);
        panel.Inflate(1, 1);
        Check(labels.Where(l => l.IsVisible).All(l => panel.Contains(l.TransformToAncestor(coordinateCanvas).TransformBounds(new Rect(new System.Windows.Point(), l.RenderSize)))), "Projected coordinate labels fit the plot area at " + width + "x" + height);
        var readoutPanel = (Border)Field(twin, "_readoutPanel");
        var rootBounds = new Rect(new System.Windows.Point(), host.Child.RenderSize);
        rootBounds.Inflate(1, 1);
        var readoutBounds = readoutPanel.TransformToAncestor(host.Child).TransformBounds(new Rect(new System.Windows.Point(), readoutPanel.RenderSize));
        var plotBounds = viewport.TransformToAncestor(host.Child).TransformBounds(new Rect(new System.Windows.Point(), viewport.RenderSize));
        var cameraControls = (Border)Field(twin, "_cameraControls");
        var cameraBounds = cameraControls.TransformToAncestor(host.Child).TransformBounds(new Rect(new System.Windows.Point(), cameraControls.RenderSize));
        Check(cameraControls.IsVisible && plotBounds.Contains(cameraBounds) && cameraBounds.Right >= plotBounds.Right - 24 && cameraBounds.Top <= plotBounds.Top + 24 &&
            cameraButtons.Values.All(b => b.IsVisible && b.ActualWidth >= 24 && b.ActualHeight >= 24),
            "Usable camera buttons fit in the plot's upper-right corner at " + width + "x" + height);
        Check(readoutPanel.IsVisible && rootBounds.Contains(readoutBounds), "Readout panel remains visible within Home at " + width + "x" + height);
        Check(readoutBounds.Top >= plotBounds.Bottom - 1, "Readout panel sits below the plot without covering the robot at " + width + "x" + height);
        Check(ReadoutValues(twin).Values.All(v => v.IsVisible && v.ActualHeight >= 12 && v.ActualWidth >= v.DesiredSize.Width - 1 &&
            rootBounds.Contains(v.TransformToAncestor(host.Child).TransformBounds(new Rect(new System.Windows.Point(), v.RenderSize)))),
            "All eight numeric readouts fit without clipping at " + width + "x" + height);
        VerifyGroundGrid(twin, width + "x" + height);
    }

    private static void VerifyCadGeometry(Forms.Control twin)
    {
        Check(Property(twin, "ModelName").ToString() == "THL300", "Renderer loads manufacturer THL300 model");
        Check(Property(twin, "SourceCadSha256").ToString().ToUpperInvariant() == "5852A524E1CB6E865EBEA1F0173DA6BAA4B85183638F7C2BD4028A439C001946", "Model provenance matches original manufacturer THL300 IGES");
        Check(Convert.ToInt32(Property(twin, "CadPartCount")) == 4, "Manufacturer base, arm 1, arm 2 and shaft are loaded separately");
        int cadTriangles = Convert.ToInt32(Property(twin, "CadTriangleCount"));
        Check(cadTriangles == 191360, "All 191360 manufacturer CAD triangles are loaded instead of a procedural mock");
        var host = Descendants(twin).OfType<ElementHost>().Single();
        var viewport = FindViewport(host.Child);
        var cad = Field(twin, "_cadModel");
        var meshes = new List<MeshGeometry3D>();
        foreach (var part in new[] { "Base", "Arm1", "Arm2", "Shaft" }) CollectMeshes((Model3DGroup)Property(cad, part), meshes);
        Check(meshes.Count == 16, "Scene renders all 16 actual CAD color sections");
        Check(meshes.Sum(m => m.TriangleIndices.Count / 3) == cadTriangles, "Actual rendered CAD triangle count matches loaded asset metadata");
        Check(meshes.All(m => m.Positions.Count > 0 && m.TriangleIndices.Count > 0 && m.TriangleIndices.Count % 3 == 0), "Every CAD surface section contains valid triangle geometry");
        Check(meshes.All(m => m.TriangleIndices.All(i => i >= 0 && i < m.Positions.Count)), "All CAD triangle indices reference existing vertices");
        var parts = new[] { "Base", "Arm1", "Arm2", "Shaft" };
        var expected = new[] { 65820, 2588, 121096, 1856 };
        var solids = new Model3DGroup[4];
        for (int i = 0; i < parts.Length; i++)
        {
            solids[i] = (Model3DGroup)Property(cad, parts[i]);
            var surfaces = new List<MeshGeometry3D>();
            CollectMeshes(solids[i], surfaces);
            Check(surfaces.Sum(m => m.TriangleIndices.Count / 3) == expected[i], parts[i] + " uses the complete manufacturer CAD solid");
        }
        var scene = viewport.Children.OfType<ModelVisual3D>().Select(v => v.Content).OfType<Model3DGroup>().Single(g => g.Children.Contains(solids[0]));
        var shoulder = (Model3DGroup)Field(twin, "_shoulderGroup");
        var elbow = (Model3DGroup)Field(twin, "_elbowGroup");
        var slide = (Model3DGroup)Field(twin, "_slideGroup");
        var tool = (Model3DGroup)Field(twin, "_toolGroup");
        Check(scene.Children.Contains(solids[0]), "Manufacturer base remains fixed outside all joint transforms");
        Check(shoulder.Children.Contains(solids[1]) && shoulder.Children.Contains(elbow) && elbow.Children.Contains(solids[2]), "Actual CAD arms articulate under J1 and nested J2");
        Check(elbow.Children.Contains(slide) && slide.Children.Contains(tool) && tool.Children.Contains(solids[3]), "Actual CAD shaft follows nested J3 translation and J4 rotation");
    }

    private static void CollectMeshes(Model3D model, IList<MeshGeometry3D> meshes)
    {
        var geometry = model as GeometryModel3D;
        if (geometry != null && geometry.Geometry is MeshGeometry3D) meshes.Add((MeshGeometry3D)geometry.Geometry);
        var group = model as Model3DGroup;
        if (group != null) foreach (var child in group.Children) CollectMeshes(child, meshes);
    }

    private static void VerifyCoordinateSystem(Forms.Control twin)
    {
        var type = twin.GetType().Assembly.GetType("Test_1.UI.RobotCoordinateSystem", true);
        Func<string, int> constant = name => Convert.ToInt32(type.GetField(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).GetValue(null));
        Check(constant("MinXY") == -600 && constant("MaxXY") == 600 && constant("MinZ") == -100 && constant("MaxZ") == 800,
            "Coordinate scales cover X/Y -600..600 and Z -100..800 mm");
        Check(constant("MajorStep") == 100, "Coordinate grid uses 100 mm major intervals");
        var host = Descendants(twin).OfType<ElementHost>().Single();
        var viewport = FindViewport(host.Child);
        var coordinate = (ModelVisual3D)Field(twin, "_coordinateVisual");
        var robot = (Model3DGroup)Field(twin, "_robotScene");
        Check(viewport.Children.Contains(coordinate) && !robot.Children.Contains(coordinate.Content), "Open coordinate axes are a separate world-space scene outside robot joints");
        Check(coordinate.Content.IsFrozen && coordinate.Transform.Value.IsIdentity && coordinate.Content.Transform.Value.IsIdentity,
            "Coordinate geometry remains immutable in the physical world frame");
        var bounds = coordinate.Content.Bounds;
        Check(bounds.X <= -600 && bounds.X + bounds.SizeX >= 600 && bounds.Y <= -600 && bounds.Y + bounds.SizeY >= 600 && bounds.Z <= -100 && bounds.Z + bounds.SizeZ >= 800,
            "Actual rendered XYZ axes span their calibrated coordinate scales");
        var coordinateMeshes = new List<MeshGeometry3D>();
        CollectMeshes(coordinate.Content, coordinateMeshes);
        var coordinateVertices = coordinateMeshes.SelectMany(m => m.Positions).ToList();
        Check(coordinateVertices.All(p => Math.Abs(p.X) <= 310 || Math.Abs(p.Y) <= 310),
            "Coordinate geometry has no outer corner rods or enclosing box");
        Check(coordinateVertices.Where(p => p.Z > 40).All(p => Math.Abs(p.X) < 20 && Math.Abs(p.Y) < 20),
            "Only the central Z axis rises above the open floor without vertical grid walls");
        var entries = ((System.Collections.IEnumerable)Field(twin, "_labelEntries")).Cast<object>().ToList();
        var anchors = entries.Select(e => Field(e, "Anchor")).ToList();
        var origin = anchors.Single(a => Property(a, "Kind").ToString() == "Origin");
        Check((Point3D)Property(origin, "Position") == new Point3D(0, 0, 0) && Math.Abs(((Model3DGroup)Property(Field(twin, "_cadModel"), "Base")).Bounds.Z) < 0.00001,
            "Coordinate origin matches manufacturer CAD mounting plane Z=0");
        var ticks = anchors.Where(a => Property(a, "Kind").ToString() == "Tick").ToList();
        Check(ticks.Count >= 20 && ticks.All(a => Convert.ToInt32(Property(a, "Value")) % 100 == 0), "Numeric coordinate ticks represent 100 mm world intervals");
        foreach (var axis in new[] { "X", "Y", "Z" })
        {
            var values = ticks.Where(a => Property(a, "Axis").ToString() == axis).Select(a => Convert.ToInt32(Property(a, "Value"))).OrderBy(v => v).ToArray();
            int min = axis == "Z" ? -100 : -600, max = axis == "Z" ? 800 : 600;
            var expectedValues = Enumerable.Range(0, (max - min) / 100 + 1).Select(i => min + i * 100).Where(v => v != 0).ToArray();
            Check(values.SequenceEqual(expectedValues), axis + " tick anchors cover the entire calibrated coordinate range with the origin marking zero");
            var label = anchors.Single(a => Property(a, "Kind").ToString() == "AxisLabel" && Property(a, "Axis").ToString() == axis);
            var color = (System.Windows.Media.Color)Property(label, "Color");
            Check(axis == "X" ? color.R > color.G && color.R > color.B : axis == "Y" ? color.G > color.R && color.G > color.B : color.B > color.R && color.B > color.G,
                axis + " axis has its corresponding red, green or blue coordinate color");
        }
    }

    private static string CoordinateGeometrySignature(Forms.Control twin)
    {
        var signature = new StringBuilder();
        AppendVisual(signature, (ModelVisual3D)Field(twin, "_coordinateVisual"), Matrix3D.Identity);
        return signature.ToString();
    }

    private static RenderTargetBitmap RenderGroundGrid(Forms.Control twin)
    {
        var ground = (FrameworkElement)Field(twin, "_groundGrid");
        ground.UpdateLayout();
        var rendered = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(ground.ActualWidth)),
            Math.Max(1, (int)Math.Ceiling(ground.ActualHeight)), 96, 96, PixelFormats.Pbgra32);
        rendered.Render(ground);
        return rendered;
    }

    private static string GroundGridSignature(Forms.Control twin)
    {
        var rendered = RenderGroundGrid(twin);
        int stride = rendered.PixelWidth * 4;
        var pixels = new byte[stride * rendered.PixelHeight];
        rendered.CopyPixels(pixels, stride, 0);
        uint hash = 2166136261;
        unchecked { foreach (byte value in pixels) hash = (hash ^ value) * 16777619; }
        return hash.ToString("X8", CultureInfo.InvariantCulture);
    }

    private static void VerifyGroundGrid(Forms.Control twin, string context)
    {
        var host = Descendants(twin).OfType<ElementHost>().Single();
        var viewport = FindViewport(host.Child);
        var ground = (FrameworkElement)Field(twin, "_groundGrid");
        var camera = viewport.Camera as OrthographicCamera;
        Check(camera != null && camera.Width > 0 && !double.IsInfinity(camera.Width),
            "Open floor uses a finite orthographic camera at " + context);
        Check(!ground.IsHitTestVisible && Math.Abs(ground.ActualWidth - viewport.ActualWidth) < 1 &&
            Math.Abs(ground.ActualHeight - viewport.ActualHeight) < 1,
            "Ground grid fills the viewport without intercepting orbit controls at " + context);

        // Independently intersect each orthographic corner ray with the physical
        // XY floor. The renderer's grid bounds must cover all four intersections.
        var worldBounds = (Rect)Property(ground, "RenderedWorldBounds");
        Check(!worldBounds.IsEmpty && worldBounds.Width > 0 && worldBounds.Height > 0,
            "Open floor has nonempty finite world coverage at " + context);
        Vector3D forward = camera.LookDirection; forward.Normalize();
        Vector3D right = Vector3D.CrossProduct(forward, camera.UpDirection); right.Normalize();
        Vector3D up = Vector3D.CrossProduct(right, forward);
        double halfWidth = camera.Width / 2;
        double halfHeight = halfWidth * viewport.ActualHeight / viewport.ActualWidth;
        foreach (int sx in new[] { -1, 1 })
            foreach (int sy in new[] { -1, 1 })
            {
                Point3D rayOrigin = camera.Position + sx * halfWidth * right + sy * halfHeight * up;
                Point3D floorPoint = rayOrigin + forward * ((-100 - rayOrigin.Z) / forward.Z);
                Check(worldBounds.Contains(new System.Windows.Point(floorPoint.X, floorPoint.Y)),
                    "Ground grid reaches viewport corner (" + sx + ", " + sy + ") at " + context);
            }

        // Render just the transparent grid layer, so the CAD, coordinate labels,
        // white background and camera buttons cannot create a false coverage pass.
        var rendered = RenderGroundGrid(twin);
        int width = rendered.PixelWidth, height = rendered.PixelHeight, stride = width * 4;
        var pixels = new byte[stride * height];
        rendered.CopyPixels(pixels, stride, 0);
        int tile = Math.Min(96, Math.Min(width, height) / 3);
        var regions = new[]
        {
            new System.Drawing.Point(0, 0), new System.Drawing.Point(width - tile, 0),
            new System.Drawing.Point(0, height - tile), new System.Drawing.Point(width - tile, height - tile),
            new System.Drawing.Point((width - tile) / 2, (height - tile) / 2)
        };
        for (int region = 0; region < regions.Length; region++)
        {
            int gridPixels = 0;
            var start = regions[region];
            for (int y = start.Y; y < start.Y + tile; y++)
                for (int x = start.X; x < start.X + tile; x++)
                {
                    int index = y * stride + x * 4;
                    // Pbgra32 channels are premultiplied; composite onto white.
                    int brightness = (pixels[index] + pixels[index + 1] + pixels[index + 2]) / 3 + 255 - pixels[index + 3];
                    if (brightness < 250) gridPixels++;
                }
            Check(gridPixels > 10, "Actual ground-grid strokes appear in " +
                (region == 4 ? "the viewport center" : "viewport corner " + (region + 1)) + " at " + context);
        }
    }

    private static string CoordinateLabelSignature(Forms.Control twin)
    {
        var signature = new StringBuilder();
        var coordinateCanvas = (Canvas)Field(twin, "_coordinateLabels");
        foreach (var label in WpfDescendants(coordinateCanvas).OfType<TextBlock>())
        {
            signature.Append(label.Text).Append(':').Append(label.Visibility).Append(':');
            if (label.IsVisible) signature.Append(label.TransformToAncestor(coordinateCanvas).Transform(new System.Windows.Point()).ToString(CultureInfo.InvariantCulture));
            signature.Append('|');
        }
        return signature.ToString();
    }

    private static Dictionary<TextBlock, System.Windows.Point> VisibleCoordinateLabelPositions(Forms.Control twin)
    {
        var canvas = (Canvas)Field(twin, "_coordinateLabels");
        return WpfDescendants(canvas).OfType<TextBlock>().Where(l => l.IsVisible)
            .ToDictionary(l => l, l => l.TransformToAncestor(canvas).Transform(new System.Windows.Point()));
    }

    private static void VerifyDriverUncreated(MainForm form)
    {
        object service = Field(form, "_robotService");
        Check(Field(service, "_robot") == null, "Robot driver remains uncreated: no connection or commands");
    }

    private static void VerifyPollingLifecycle(MainForm form, Forms.Control twin)
    {
        ((Forms.Timer)Field(form, "_feedbackTimer")).Stop();
        // Only the twin's synthetic connection changes. The actual service remains
        // disconnected, so releasing its read gate can only produce a null sample.
        Call(twin, "SetConnectionState", false);
        Call(form, "UpdateDigitalTwinConnection", true);
        var service = Field(form, "_robotService");
        var gate = Field(service, "_feedbackLifecycle");
        using (var held = new ManualResetEventSlim())
        using (var release = new ManualResetEventSlim())
        {
            var holder = new Thread(() => { lock (gate) { held.Set(); release.Wait(); } });
            holder.IsBackground = true;
            holder.Start();
            try
            {
                Check(held.Wait(5000), "Offline service gate is held for a delayed-feedback test");
                Call(form, "PollRobotFeedback", null, EventArgs.Empty);
                Check((bool)Field(form, "_feedbackReadPending"), "Polling marks an in-flight feedback read");
                Call(form, "PollRobotFeedback", null, EventArgs.Empty);
                Check((bool)Field(form, "_feedbackReadPending"), "Second timer tick keeps the existing read pending");
                bool heartbeat = false;
                form.BeginInvoke(new Action(() => heartbeat = true));
                DrainUi();
                Check(heartbeat, "A blocked feedback read leaves the UI responsive");
                int session = (int)Field(form, "_feedbackSession");
                Call(form, "UpdateDigitalTwinConnection", false);
                Call(form, "UpdateDigitalTwinConnection", true);
                Check((int)Field(form, "_feedbackSession") == session + 2, "Disconnect and reconnect invalidate the old feedback session");
                Check(Status(twin).Contains("WAIT"), "New session waits while old reply is still blocked");
                VerifyReadoutStatus(twin, "WAITING");
            }
            finally { release.Set(); holder.Join(5000); }
            WaitForReadCompletion(form);
            Check(Status(twin).Contains("WAIT"), "Old null feedback cannot mark the new connection stale");
            VerifyReadoutStatus(twin, "WAITING");
        }
        Call(form, "UpdateDigitalTwinConnection", false);
        VerifyDriverUncreated(form);
    }

    private static void VerifyCloseDuringBlockedRead(MainForm form)
    {
        var gate = Field(Field(form, "_robotService"), "_feedbackLifecycle");
        using (var held = new ManualResetEventSlim())
        using (var release = new ManualResetEventSlim())
        {
            var holder = new Thread(() => { lock (gate) { held.Set(); release.Wait(); } });
            holder.IsBackground = true;
            holder.Start();
            try
            {
                Check(held.Wait(5000), "Offline service gate is held during form close");
                Call(form, "UpdateDigitalTwinConnection", true);
                Call(form, "PollRobotFeedback", null, EventArgs.Empty);
                Check((bool)Field(form, "_feedbackReadPending"), "Closing test starts with a pending read");
                form.Close();
                Check(form.IsDisposed && (bool)Field(form, "_twinStopped"), "Form closes promptly while feedback is blocked");
            }
            finally { release.Set(); holder.Join(5000); }
            WaitForReadCompletion(form);
            Check(!(bool)Field(form, "_feedbackReadPending"), "Late feedback after disposal completes safely");
        }
    }

    private static void WaitForReadCompletion(MainForm form)
    {
        var wait = System.Diagnostics.Stopwatch.StartNew();
        while ((bool)Field(form, "_feedbackReadPending") && wait.ElapsedMilliseconds < 5000) DrainUi();
        Check(!(bool)Field(form, "_feedbackReadPending"), "Delayed background read finishes without a stuck poll");
    }

    private static void Capture(MainForm form, Forms.Control twin, string name)
    {
        Console.WriteLine("RENDERING: " + name);
        DrainUi();
        var host = Descendants(twin).OfType<ElementHost>().Single();
        var child = host.Child;
        child.UpdateLayout();
        int renderWidth = Math.Max(1, (int)Math.Ceiling(child.RenderSize.Width));
        int renderHeight = Math.Max(1, (int)Math.Ceiling(child.RenderSize.Height));
        var rendered = new RenderTargetBitmap(renderWidth, renderHeight, 96, 96, PixelFormats.Pbgra32);
        rendered.Render(child);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rendered));
        string viewPath = Path.Combine(_output, name + "-viewport.png");
        using (var stream = File.Create(viewPath)) encoder.Save(stream);

        // ElementHost uses a separate WPF surface. Composite its native rendered
        // bitmap into the WinForms capture instead of taking a desktop screenshot.
        using (var bitmap = new Bitmap(form.Width, form.Height))
        {
            bool visible = host.Visible;
            try
            {
                host.Visible = false;
                form.DrawToBitmap(bitmap, new Rectangle(System.Drawing.Point.Empty, form.Size));
            }
            finally { host.Visible = visible; }
            using (var graphics = Graphics.FromImage(bitmap))
            using (var wpfBitmap = new Bitmap(viewPath))
            {
                var location = form.PointToClient(host.PointToScreen(System.Drawing.Point.Empty));
                var clientOrigin = form.PointToScreen(System.Drawing.Point.Empty);
                location.Offset(clientOrigin.X - form.Left, clientOrigin.Y - form.Top);
                graphics.DrawImage(wpfBitmap, new Rectangle(location, host.Size));
            }
            bitmap.Save(Path.Combine(_output, name + ".png"), ImageFormat.Png);
        }
        Console.WriteLine("CAPTURE: " + Path.Combine(_output, name + ".png"));
    }

    private static string GeometrySignature(Forms.Control twin)
    {
        var host = Descendants(twin).OfType<ElementHost>().Single();
        var viewport = FindViewport(host.Child);
        var signature = new StringBuilder();
        foreach (var model in viewport.Children.OfType<ModelVisual3D>())
            AppendVisual(signature, model, Matrix3D.Identity);
        return signature.ToString();
    }

    private static void AppendVisual(StringBuilder signature, ModelVisual3D visual, Matrix3D parent)
    {
        Matrix3D world = visual.Transform.Value;
        world.Append(parent);
        if (visual.Content != null) AppendModel(signature, visual.Content, world);
        foreach (var child in visual.Children.OfType<ModelVisual3D>()) AppendVisual(signature, child, world);
    }

    private static void AppendModel(StringBuilder signature, Model3D model, Matrix3D parent)
    {
        Matrix3D world = model.Transform.Value;
        world.Append(parent);
        var geometry = model as GeometryModel3D;
        if (geometry != null)
        {
            signature.Append(world.ToString(CultureInfo.InvariantCulture));
            signature.Append('|');
            var mesh = geometry.Geometry as MeshGeometry3D;
            if (mesh != null && mesh.Positions.Count > 0)
                signature.Append(world.Transform(mesh.Positions[0]).ToString(CultureInfo.InvariantCulture));
        }
        var group = model as Model3DGroup;
        if (group != null) foreach (var child in group.Children) AppendModel(signature, child, world);
    }

    private static Viewport3D FindViewport(DependencyObject node)
    {
        if (node is Viewport3D) return (Viewport3D)node;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var found = FindViewport(VisualTreeHelper.GetChild(node, i));
            if (found != null) return found;
        }
        return null;
    }

    private static string Status(Forms.Control twin)
    {
        return twin.GetType().GetProperty("FeedbackState").GetValue(twin, null).ToString().ToUpperInvariant();
    }

    private static IEnumerable<DependencyObject> WpfDescendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var nested in WpfDescendants(child)) yield return nested;
        }
    }

    private static IEnumerable<Forms.Control> Descendants(Forms.Control parent)
    {
        foreach (Forms.Control child in parent.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    private static object Field(object target, string name)
    {
        return target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(target);
    }

    private static object Property(object target, string name)
    {
        return target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(target, null);
    }

    private static void Call(object target, string method, params object[] values)
    {
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(target, values);
    }

    private static void DrainUi()
    {
        for (int i = 0; i < 3; i++)
        {
            Forms.Application.DoEvents();
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Render, new Action(delegate { }));
            Thread.Sleep(25);
        }
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException("FAILED: " + description);
        _checks++;
        Console.WriteLine("OK: " + description);
    }
}
