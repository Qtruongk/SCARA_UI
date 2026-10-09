using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Xml;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Forms.Integration;
using Test_1.Models;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace Test_1.UI
{
    /// <summary>Encoder conventions only. Manufacturer CAD dimensions are immutable.</summary>
    public sealed class ScaraDigitalTwinProfile
    {
        public double Link1Length { get { return 125; } }
        public double Link2Length { get { return 175; } }
        public double J1Sign { get; set; } = 1;
        public double J2Sign { get; set; } = 1;
        public double J3Sign { get; set; } = 1;
        public double J4Sign { get; set; } = 1;
        public double J1Zero { get; set; }
        public double J2Zero { get; set; }
        // Supplied CAD has the spindle at its upper stop (joint feedback 160 mm).
        public double J3Zero { get; set; } = 160;
        public double J4Zero { get; set; }
        public bool UseWorldToolYaw { get; set; }
        internal ScaraDigitalTwinProfile Copy() { return (ScaraDigitalTwinProfile)MemberwiseClone(); }

        internal void Validate()
        {
            foreach (double sign in new[] { J1Sign, J2Sign, J3Sign, J4Sign })
                if (Math.Abs(sign) != 1) throw new ArgumentException("Joint direction must be +1 or -1.");
            foreach (double zero in new[] { J1Zero, J2Zero, J3Zero, J4Zero })
                if (double.IsNaN(zero) || double.IsInfinity(zero) || Math.Abs(zero) > 36000)
                    throw new ArgumentException("Invalid joint reference offset.");
        }
    }

    /// <summary>Manufacturer THL300 CAD and actual feedback in a millimetre plotting frame.</summary>
    public sealed class ScaraDigitalTwinView : Forms.UserControl
    {
        private readonly ElementHost _host;
        private readonly Grid _layout;
        private readonly Viewport3D _viewport;
        private readonly OrthographicCamera _camera;
        private readonly RobotGroundGrid _groundGrid;
        private readonly Thl300CadModel _cadModel;
        private readonly Canvas _coordinateLabels;
        private readonly List<CoordinateLabelView> _labelEntries = new List<CoordinateLabelView>();
        private readonly Grid _plot;
        private readonly Border _cameraControls;
        private readonly Dictionary<string, ButtonBase> _cameraButtons = new Dictionary<string, ButtonBase>();
        private readonly Border _readoutPanel;
        private readonly Grid _readoutGroups;
        private readonly Grid _worldReadout, _jointReadout;
        private readonly TextBlock _readoutStatus;
        private readonly Dictionary<string, TextBlock> _readoutValues = new Dictionary<string, TextBlock>();
        private bool _compactReadouts;
        private ModelVisual3D _coordinateVisual;
        private Model3DGroup _robotScene;
        private Model3DGroup _shoulderGroup, _elbowGroup, _slideGroup, _toolGroup;
        private AxisAngleRotation3D _j1Rotation, _j2Rotation, _j4Rotation;
        private TranslateTransform3D _j3Translation;
        private readonly System.Windows.Threading.DispatcherTimer _staleTimer;
        private readonly Stopwatch _feedbackAge = new Stopwatch();
        private ScaraDigitalTwinProfile _profile;
        private bool _connected, _hasFeedback, _orbiting;
        private bool _stale = true, _waitingForFeedback = true;
        private Point _lastPointer;
        private double _azimuth = 55, _elevation = 28, _zoom = 1;
        private readonly double[] _lastJoints = new double[4];
        private readonly double[] _lastWorld = new double[4];

        public string ModelName { get { return "THL300"; } }
        public string SourceCadSha256 { get { return _cadModel.SourceCadSha256; } }
        public int CadPartCount { get { return _cadModel.PartCount; } }
        public int CadTriangleCount { get { return _cadModel.TriangleCount; } }
        /// <summary>Physical tool tip in mm; mounting plane is Z=0, controller origin is Z=-48.</summary>
        public Point3D RenderedToolPosition { get; private set; }
        public double RenderedToolYawDegrees { get; private set; }
        public string FeedbackState
        {
            get { return !_connected ? "OFFLINE" : _hasFeedback && !_stale ? "LIVE" : _waitingForFeedback ? "WAITING" : "STALE"; }
        }

        public static string CalibrationFilePath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SCARA_UI", "thl300-digital-twin.xml"); }
        }

        public ScaraDigitalTwinProfile Calibration
        {
            get { return _profile.Copy(); }
            set
            {
                if (value == null) throw new ArgumentNullException("value");
                value.Validate();
                ScaraDigitalTwinProfile copy = value.Copy();
                OnUi(() => { _profile = copy; if (_hasFeedback) ApplyJointTransforms(); });
            }
        }

        public ScaraDigitalTwinView()
        {
            BackColor = Drawing.Color.FromArgb(245, 246, 250);
            MinimumSize = new Drawing.Size(260, 200);
            _profile = LoadCalibration();
            _cadModel = Thl300CadModel.Load();
            _layout = new Grid
            {
                Background = new SolidColorBrush(Color.FromRgb(250, 252, 254)),
                ClipToBounds = true, UseLayoutRounding = true
            };
            _layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _plot = new Grid { ClipToBounds = true };
            _layout.Children.Add(_plot);
            _groundGrid = new RobotGroundGrid { IsHitTestVisible = false };
            _plot.Children.Add(_groundGrid);
            _viewport = new Viewport3D { ClipToBounds = true };
            _camera = new OrthographicCamera { Width = 1800, NearPlaneDistance = 1, FarPlaneDistance = 1000000 };
            _viewport.Camera = _camera;
            _plot.Children.Add(_viewport);
            _coordinateLabels = new Canvas { IsHitTestVisible = false, ClipToBounds = true };
            _plot.Children.Add(_coordinateLabels);
            CreateCoordinateLabels();
            _cameraControls = CreateCameraControls();
            _plot.Children.Add(_cameraControls);
            _readoutGroups = new Grid();
            _worldReadout = CreateReadoutGroup("TCP · WORLD", new[] { "X", "Y", "Z", "C" }, new[] { "mm", "mm", "mm", "°" });
            _jointReadout = CreateReadoutGroup("JOINTS", new[] { "J1", "J2", "J3", "J4" }, new[] { "°", "°", "mm", "°" });
            _readoutGroups.Children.Add(_worldReadout); _readoutGroups.Children.Add(_jointReadout);
            _readoutStatus = new TextBlock { FontSize = 10, Margin = new Thickness(0, 5, 0, 0) };
            StackPanel readoutContent = new StackPanel();
            readoutContent.Children.Add(_readoutGroups); readoutContent.Children.Add(_readoutStatus);
            _readoutPanel = new Border
            {
                Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(221, 228, 237)),
                BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(12, 7, 12, 7),
                Child = readoutContent
            };
            Grid.SetRow(_readoutPanel, 1); _layout.Children.Add(_readoutPanel);
            ArrangeReadouts(732); UpdateReadouts();
            _layout.SizeChanged += (s, e) => ArrangeReadouts(e.NewSize.Width);
            _host = new ElementHost { Dock = Forms.DockStyle.Fill, Child = _layout };
            Controls.Add(_host);
            _viewport.MouseDown += OnMouseDown;
            _viewport.MouseMove += OnMouseMove;
            _viewport.MouseUp += (s, e) => { _orbiting = false; _viewport.ReleaseMouseCapture(); };
            _viewport.LostMouseCapture += (s, e) => { _orbiting = false; };
            _viewport.MouseWheel += (s, e) =>
            {
                _zoom = Math.Max(0.3, Math.Min(3, _zoom * Math.Pow(0.88, e.Delta / 120.0)));
                UpdateCamera(); e.Handled = true;
            };
            _viewport.SizeChanged += (s, e) => UpdateCamera();
            BuildScene();
            UpdateCamera();
            _staleTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _staleTimer.Tick += (s, e) =>
            {
                if (_connected && _hasFeedback && !_stale && _feedbackAge.ElapsedMilliseconds > 2000)
                { _stale = true; UpdateReadouts(); }
            };
            _staleTimer.Start();
        }

        public void SetConnectionState(bool connected)
        {
            OnUi(() =>
            {
                if (_connected == connected) return;
                _connected = connected; _stale = true; _waitingForFeedback = true;
                UpdateReadouts();
            });
        }

        public void SetFeedbackUnavailable()
        {
            OnUi(() => { _stale = true; _waitingForFeedback = false; UpdateReadouts(); });
        }

        public void ApplyFeedback(RobotPositionData position)
        {
            if (position == null || position.JointPosition == null || position.WorldPosition == null)
            {
                SetFeedbackUnavailable(); return;
            }
            double[] joints = { position.JointPosition.J1, position.JointPosition.J2, position.JointPosition.J3, position.JointPosition.J4 };
            double[] world = { position.WorldPosition.X, position.WorldPosition.Y, position.WorldPosition.Z, position.WorldPosition.C };
            for (int i = 0; i < 4; i++)
            {
                if (!IsFinite(joints[i]) || !IsFinite(world[i]) || Math.Abs(joints[i]) > 1000000 || Math.Abs(world[i]) > 1000000)
                {
                    SetFeedbackUnavailable(); return;
                }
            }
            OnUi(() =>
            {
                if (!_connected) return;
                Array.Copy(joints, _lastJoints, 4); Array.Copy(world, _lastWorld, 4);
                _hasFeedback = true; _stale = false; _waitingForFeedback = false;
                _feedbackAge.Restart(); ApplyJointTransforms(); UpdateReadouts();
            });
        }

        public void ResetCamera()
        {
            OnUi(() => { _azimuth = 55; _elevation = 28; _zoom = 1; UpdateCamera(); });
        }

        private void BuildScene()
        {
            Model3DGroup scene = _robotScene = new Model3DGroup();
            scene.Children.Add(new AmbientLight(Color.FromRgb(72, 78, 90)));
            scene.Children.Add(new DirectionalLight(Color.FromRgb(215, 221, 232), new Vector3D(-0.8, 0.5, -1.2)));
            scene.Children.Add(new DirectionalLight(Color.FromRgb(105, 116, 134), new Vector3D(0.5, -1, -0.5)));
            scene.Children.Add(_cadModel.Base);
            _j1Rotation = new AxisAngleRotation3D(new Vector3D(0, 0, 1), 0);
            _shoulderGroup = new Model3DGroup { Transform = new RotateTransform3D(_j1Rotation, 0, 0, 162.5) };
            _shoulderGroup.Children.Add(_cadModel.Arm1); scene.Children.Add(_shoulderGroup);
            _j2Rotation = new AxisAngleRotation3D(new Vector3D(0, 0, 1), 0);
            _elbowGroup = new Model3DGroup { Transform = new RotateTransform3D(_j2Rotation, 125, 0, 185) };
            _elbowGroup.Children.Add(_cadModel.Arm2); _shoulderGroup.Children.Add(_elbowGroup);
            _j3Translation = new TranslateTransform3D();
            _slideGroup = new Model3DGroup { Transform = _j3Translation };
            _elbowGroup.Children.Add(_slideGroup);
            _j4Rotation = new AxisAngleRotation3D(new Vector3D(0, 0, 1), 0);
            _toolGroup = new Model3DGroup { Transform = new RotateTransform3D(_j4Rotation, 300, 0, 112) };
            _toolGroup.Children.Add(_cadModel.Shaft); _slideGroup.Children.Add(_toolGroup);
            _coordinateVisual = new ModelVisual3D { Content = RobotCoordinateSystem.CreateGeometry() };
            _viewport.Children.Add(_coordinateVisual);
            _viewport.Children.Add(new ModelVisual3D { Content = scene });
            UpdateRenderedToolPose();
        }

        private void ApplyJointTransforms()
        {
            _j1Rotation.Angle = (_profile.J1Sign * (_lastJoints[0] - _profile.J1Zero)) % 360;
            _j2Rotation.Angle = (_profile.J2Sign * (_lastJoints[1] - _profile.J2Zero)) % 360;
            _j3Translation.OffsetZ = _profile.J3Sign * (_lastJoints[2] - _profile.J3Zero);
            _j4Rotation.Angle = (_profile.UseWorldToolYaw
                ? _profile.J4Sign * (_lastWorld[3] - _profile.J4Zero) - _j1Rotation.Angle - _j2Rotation.Angle
                : _profile.J4Sign * (_lastJoints[3] - _profile.J4Zero)) % 360;
            UpdateRenderedToolPose();
            UpdateCoordinateLabels();
        }

        private void UpdateRenderedToolPose()
        {
            Point3D origin = new Point3D(300, 0, 112), direction = new Point3D(301, 0, 112);
            foreach (Model3DGroup group in new[] { _toolGroup, _slideGroup, _elbowGroup, _shoulderGroup })
            {
                origin = group.Transform.Transform(origin); direction = group.Transform.Transform(direction);
            }
            RenderedToolPosition = origin;
            Vector3D forward = direction - origin;
            RenderedToolYawDegrees = Math.Atan2(forward.Y, forward.X) * 180 / Math.PI;
        }

        private void UpdateCamera()
        {
            double width = Math.Max(1, _viewport.ActualWidth), height = Math.Max(1, _viewport.ActualHeight);
            Point3D target = new Point3D(0, 0, 240);
            double azimuth = _azimuth * Math.PI / 180, elevation = _elevation * Math.PI / 180;
            Vector3D outward = new Vector3D(Math.Cos(azimuth) * Math.Cos(elevation),
                Math.Sin(azimuth) * Math.Cos(elevation), Math.Sin(elevation));
            Vector3D forward = -outward;
            Vector3D right = Vector3D.CrossProduct(forward, new Vector3D(0, 0, 1)); right.Normalize();
            Vector3D up = Vector3D.CrossProduct(right, forward);
            double halfWidth = 1, halfHeight = 1;
            // Frame the reachable robot and axis names, independently of the open grid extent.
            foreach (double x in new[] { -350.0, 350.0 })
                foreach (double y in new[] { -350.0, 350.0 })
                    foreach (double z in new[] { -100.0, 580.0 })
                    {
                        Vector3D corner = new Point3D(x, y, z) - target;
                        halfWidth = Math.Max(halfWidth, Math.Abs(Vector3D.DotProduct(corner, right)));
                        halfHeight = Math.Max(halfHeight, Math.Abs(Vector3D.DotProduct(corner, up)));
                    }
            foreach (CoordinateLabelAnchor anchor in RobotCoordinateSystem.Labels)
            {
                if (anchor.Kind != CoordinateLabelKind.AxisLabel) continue;
                Vector3D relative = anchor.Position - target;
                halfWidth = Math.Max(halfWidth, Math.Abs(Vector3D.DotProduct(relative, right)));
                halfHeight = Math.Max(halfHeight, Math.Abs(Vector3D.DotProduct(relative, up)));
            }
            _camera.Width = Math.Max(2 * halfWidth / Math.Max(0.5, 1 - 72 / width),
                2 * halfHeight * width / height / Math.Max(0.5, 1 - 64 / height)) * _zoom;
            Vector3D offset = outward * 4000 * _zoom;
            _camera.Position = target + offset; _camera.LookDirection = -offset; _camera.UpDirection = new Vector3D(0, 0, 1);
            _groundGrid.Update(_camera, width, height);
            UpdateCoordinateLabels();
        }

        private sealed class CoordinateLabelView
        {
            internal CoordinateLabelAnchor Anchor;
            internal TextBlock TextBlock;
        }

        private Border CreateCameraControls()
        {
            Grid buttons = new Grid();
            for (int i = 0; i < 3; i++)
            {
                buttons.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
                buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            }
            AddCameraButton(buttons, "Up", "↑", "Nâng góc nhìn", 0, 1, () => OrbitCamera(0, 8));
            AddCameraButton(buttons, "Left", "←", "Xoay góc nhìn sang trái", 1, 0, () => OrbitCamera(-12, 0));
            AddCameraButton(buttons, "Reset", "⌂", "Về góc nhìn mặc định", 1, 1, ResetCamera);
            AddCameraButton(buttons, "Right", "→", "Xoay góc nhìn sang phải", 1, 2, () => OrbitCamera(12, 0));
            AddCameraButton(buttons, "Down", "↓", "Hạ góc nhìn", 2, 1, () => OrbitCamera(0, -8));
            return new Border
            {
                Width = 100, Height = 100, Padding = new Thickness(4), Margin = new Thickness(10),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Background = new SolidColorBrush(Color.FromArgb(245, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(217, 226, 237)), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8), Child = buttons
            };
        }

        private void AddCameraButton(Grid grid, string key, string symbol, string description,
            int row, int column, Action action)
        {
            ButtonBase button = key == "Reset" ? (ButtonBase)new Button() : new RepeatButton { Delay = 350, Interval = 90 };
            button.Content = symbol; button.ToolTip = description;
            button.Width = 28; button.Height = 28; button.Padding = new Thickness(0);
            button.FontFamily = new FontFamily("Segoe UI"); button.FontSize = 16;
            button.Foreground = new SolidColorBrush(Color.FromRgb(58, 78, 103));
            button.Background = Brushes.White;
            button.BorderBrush = new SolidColorBrush(Color.FromRgb(213, 223, 236));
            button.BorderThickness = new Thickness(1);
            button.Cursor = Cursors.Hand;
            System.Windows.Automation.AutomationProperties.SetName(button, description);
            button.Click += (s, e) => { action(); e.Handled = true; };
            Grid.SetRow(button, row); Grid.SetColumn(button, column);
            _cameraButtons.Add(key, button); grid.Children.Add(button);
        }

        private void OrbitCamera(double horizontal, double vertical)
        {
            OnUi(() =>
            {
                _azimuth = (_azimuth + horizontal) % 360;
                _elevation = Math.Max(5, Math.Min(85, _elevation + vertical));
                UpdateCamera();
            });
        }

        private Grid CreateReadoutGroup(string title, string[] keys, string[] units)
        {
            Grid group = new Grid();
            group.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            group.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            TextBlock heading = new TextBlock
            {
                Text = title, FontSize = 10, FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(76, 91, 111)), Margin = new Thickness(0, 0, 0, 4)
            };
            group.Children.Add(heading);
            Grid metrics = new Grid(); Grid.SetRow(metrics, 1); group.Children.Add(metrics);
            for (int i = 0; i < keys.Length; i++)
            {
                metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                StackPanel metric = new StackPanel { Margin = new Thickness(0, 0, 4, 0) };
                metric.Children.Add(new TextBlock
                {
                    Text = keys[i] + " (" + units[i] + ")", FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(111, 124, 142))
                });
                TextBlock value = new TextBlock
                {
                    Text = "—", FontFamily = new FontFamily("Consolas"), FontSize = 13,
                    FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis,
                    Foreground = new SolidColorBrush(Color.FromRgb(38, 54, 77))
                };
                _readoutValues.Add(keys[i], value); metric.Children.Add(value);
                Grid.SetColumn(metric, i); metrics.Children.Add(metric);
            }
            return group;
        }

        private void ArrangeReadouts(double width)
        {
            bool compact = width < 560;
            if (_readoutGroups.ColumnDefinitions.Count > 0 && compact == _compactReadouts) return;
            _compactReadouts = compact;
            _readoutGroups.ColumnDefinitions.Clear(); _readoutGroups.RowDefinitions.Clear();
            _readoutGroups.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (compact)
            {
                _readoutGroups.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                _readoutGroups.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetColumn(_jointReadout, 0); Grid.SetRow(_jointReadout, 1);
                _worldReadout.Margin = new Thickness(0, 0, 0, 5); _jointReadout.Margin = new Thickness();
            }
            else
            {
                _readoutGroups.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Grid.SetColumn(_jointReadout, 1); Grid.SetRow(_jointReadout, 0);
                _worldReadout.Margin = new Thickness(0, 0, 14, 0); _jointReadout.Margin = new Thickness(14, 0, 0, 0);
            }
            ArrangeReadoutGroup(_worldReadout, compact); ArrangeReadoutGroup(_jointReadout, compact);
        }

        private static void ArrangeReadoutGroup(Grid group, bool compact)
        {
            TextBlock heading = (TextBlock)group.Children[0];
            Grid metrics = (Grid)group.Children[1];
            group.ColumnDefinitions.Clear(); group.RowDefinitions.Clear();
            group.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(heading, 0); Grid.SetColumn(heading, 0);
            if (compact)
            {
                group.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
                group.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Grid.SetRow(metrics, 0); Grid.SetColumn(metrics, 1);
                heading.Margin = new Thickness(0, 0, 8, 0); heading.TextWrapping = TextWrapping.Wrap;
                heading.VerticalAlignment = VerticalAlignment.Center;
            }
            else
            {
                group.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                group.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetRow(metrics, 1); Grid.SetColumn(metrics, 0);
                heading.Margin = new Thickness(0, 0, 0, 4); heading.TextWrapping = TextWrapping.NoWrap;
            }
        }

        private void UpdateReadouts()
        {
            string[] keys = { "X", "Y", "Z", "C", "J1", "J2", "J3", "J4" };
            for (int i = 0; i < keys.Length; i++)
            {
                double value = i < 4 ? _lastWorld[i] : _lastJoints[i - 4];
                _readoutValues[keys[i]].Text = _hasFeedback ? value.ToString("F2", CultureInfo.InvariantCulture) : "—";
                _readoutValues[keys[i]].ToolTip = _hasFeedback ? value.ToString("R", CultureInfo.InvariantCulture) : null;
                _readoutValues[keys[i]].Opacity = _connected && !_stale ? 1 : 0.65;
            }
            string state = FeedbackState;
            string detail = state == "LIVE" ? "Đang cập nhật" : state == "WAITING" ? "Đang chờ phản hồi" :
                state == "STALE" ? (_hasFeedback ? "Dữ liệu cũ · Mất phản hồi" : "Chưa nhận được dữ liệu") :
                (_hasFeedback ? "Mất kết nối · Giữ giá trị cuối" : "Chưa có dữ liệu");
            _readoutStatus.Text = state + " · " + detail;
            _readoutStatus.Foreground = new SolidColorBrush(state == "LIVE" ? Color.FromRgb(38, 132, 90) :
                state == "STALE" ? Color.FromRgb(175, 111, 31) : Color.FromRgb(113, 125, 143));
        }

        private void CreateCoordinateLabels()
        {
            foreach (CoordinateLabelAnchor anchor in RobotCoordinateSystem.Labels)
            {
                bool axis = anchor.Kind == CoordinateLabelKind.AxisLabel;
                TextBlock label = new TextBlock
                {
                    Text = anchor.Text, Foreground = new SolidColorBrush(anchor.Color),
                    FontFamily = new FontFamily("Segoe UI"), FontSize = axis ? 12 : 10,
                    FontWeight = axis ? FontWeights.SemiBold : FontWeights.Normal,
                    Background = new SolidColorBrush(Color.FromArgb(220, 250, 252, 254)),
                    Padding = new Thickness(2, 0, 2, 0), IsHitTestVisible = false
                };
                _labelEntries.Add(new CoordinateLabelView { Anchor = anchor, TextBlock = label });
                _coordinateLabels.Children.Add(label);
            }
        }

        private void UpdateCoordinateLabels()
        {
            double width = _viewport.ActualWidth, height = _viewport.ActualHeight;
            if (width <= 0 || height <= 0) return;
            bool compact = width < 500 || height < 300;
            List<Rect> occupied = new List<Rect>();
            Rect robotBounds = ProjectedRobotBounds();
            // Keep projected tick text clear of the corner camera controls.
            occupied.Add(new Rect(Math.Max(0, width - 110), 10, 100, 100));
            // Reserve the axis names before numeric ticks to keep their units readable.
            foreach (CoordinateLabelKind kind in new[] { CoordinateLabelKind.AxisLabel, CoordinateLabelKind.Tick, CoordinateLabelKind.Origin })
                foreach (CoordinateLabelView entry in _labelEntries)
                {
                    CoordinateLabelAnchor anchor = entry.Anchor;
                    if (anchor.Kind != kind) continue;
                    TextBlock label = entry.TextBlock;
                    label.Visibility = Visibility.Hidden;
                    if (compact && kind == CoordinateLabelKind.Tick && anchor.Value % 200 != 0) continue;
                    Point3D position = anchor.Position;
                    Point point;
                    if (!ProjectPoint(position, out point)) continue;
                    label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    Size size = label.DesiredSize;
                    double x = point.X - size.Width * 0.5, y = point.Y - size.Height * 0.5;
                    if (kind == CoordinateLabelKind.Tick)
                    {
                        if (anchor.Axis == CoordinateAxis.Z) x = point.X - size.Width - 7;
                        else y = point.Y + 4;
                    }
                    else if (kind == CoordinateLabelKind.Origin) { x += 8; y += 6; }
                    Rect rectangle = new Rect(x, y, size.Width, size.Height);
                    if (x < 2 || y < 2 || rectangle.Right > width - 2 || rectangle.Bottom > height - 2) continue;
                    if (kind != CoordinateLabelKind.AxisLabel && robotBounds.IntersectsWith(rectangle)) continue;
                    Rect separation = rectangle; separation.Inflate(3, 2);
                    bool overlaps = false;
                    foreach (Rect other in occupied) if (other.IntersectsWith(separation)) { overlaps = true; break; }
                    if (overlaps) continue;
                    Canvas.SetLeft(label, x); Canvas.SetTop(label, y); label.Visibility = Visibility.Visible;
                    occupied.Add(rectangle);
                }
        }

        private Rect ProjectedRobotBounds()
        {
            if (_robotScene == null) return Rect.Empty;
            Rect3D bounds = _robotScene.Bounds;
            if (bounds.IsEmpty) return Rect.Empty;
            Rect projected = Rect.Empty;
            foreach (double x in new[] { bounds.X, bounds.X + bounds.SizeX })
                foreach (double y in new[] { bounds.Y, bounds.Y + bounds.SizeY })
                    foreach (double z in new[] { bounds.Z, bounds.Z + bounds.SizeZ })
                    {
                        Point point;
                        if (ProjectPoint(new Point3D(x, y, z), out point)) projected.Union(point);
                    }
            if (!projected.IsEmpty) projected.Inflate(8, 6);
            return projected;
        }

        private bool ProjectPoint(Point3D point, out Point projected)
        {
            projected = new Point();
            double width = _viewport.ActualWidth, height = _viewport.ActualHeight;
            if (width <= 0 || height <= 0) return false;
            Vector3D forward = _camera.LookDirection; forward.Normalize();
            Vector3D right = Vector3D.CrossProduct(forward, _camera.UpDirection); right.Normalize();
            Vector3D up = Vector3D.CrossProduct(right, forward);
            Vector3D relative = point - _camera.Position;
            double depth = Vector3D.DotProduct(relative, forward);
            if (depth <= _camera.NearPlaneDistance) return false;
            double scale = width / _camera.Width;
            projected = new Point(width * 0.5 + Vector3D.DotProduct(relative, right) * scale,
                height * 0.5 - Vector3D.DotProduct(relative, up) * scale);
            return IsFinite(projected.X) && IsFinite(projected.Y);
        }

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left && e.ChangedButton != MouseButton.Middle) return;
            if (e.ClickCount == 2) { ResetCamera(); e.Handled = true; return; }
            _orbiting = true; _lastPointer = e.GetPosition(_viewport); _viewport.CaptureMouse(); e.Handled = true;
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (!_orbiting) return;
            Point pointer = e.GetPosition(_viewport);
            _azimuth -= (pointer.X - _lastPointer.X) * 0.45;
            _elevation = Math.Max(5, Math.Min(85, _elevation + (pointer.Y - _lastPointer.Y) * 0.35));
            _lastPointer = pointer; UpdateCamera();
        }

        private void OnUi(Action action)
        {
            if (IsDisposed || Disposing) return;
            if (_layout.Dispatcher.CheckAccess()) action();
            else _layout.Dispatcher.BeginInvoke(new Action(() => { if (!IsDisposed && !Disposing) action(); }));
        }
        private static bool IsFinite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }

        private static ScaraDigitalTwinProfile LoadCalibration()
        {
            ScaraDigitalTwinProfile profile = new ScaraDigitalTwinProfile();
            try
            {
                if (!File.Exists(CalibrationFilePath)) return profile;
                XmlDocument document = new XmlDocument { XmlResolver = null };
                using (XmlReader reader = XmlReader.Create(CalibrationFilePath,
                    new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null })) document.Load(reader);
                if (document.DocumentElement == null || document.DocumentElement.Name != "Thl300DigitalTwin") return profile;
                foreach (System.Reflection.PropertyInfo property in typeof(ScaraDigitalTwinProfile).GetProperties())
                {
                    if (!property.CanWrite) continue;
                    string attribute = document.DocumentElement.GetAttribute(property.Name);
                    double number; bool flag;
                    if (property.PropertyType == typeof(bool) && bool.TryParse(attribute, out flag)) property.SetValue(profile, flag, null);
                    else if (property.PropertyType == typeof(double) && double.TryParse(attribute, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                        property.SetValue(profile, number, null);
                }
                profile.Validate(); return profile;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is XmlException || ex is ArgumentException)
            {
                return new ScaraDigitalTwinProfile();
            }
        }

        public void SaveCalibration()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CalibrationFilePath));
            XmlDocument document = new XmlDocument();
            XmlElement root = document.CreateElement("Thl300DigitalTwin"); root.SetAttribute("Version", "1");
            foreach (System.Reflection.PropertyInfo property in typeof(ScaraDigitalTwinProfile).GetProperties())
            {
                if (!property.CanWrite) continue;
                root.SetAttribute(property.Name, property.PropertyType == typeof(bool)
                    ? ((bool)property.GetValue(_profile, null)).ToString(CultureInfo.InvariantCulture)
                    : ((double)property.GetValue(_profile, null)).ToString("R", CultureInfo.InvariantCulture));
            }
            document.AppendChild(root);
            string temporary = CalibrationFilePath + ".tmp"; document.Save(temporary);
            if (File.Exists(CalibrationFilePath)) File.Replace(temporary, CalibrationFilePath, null);
            else File.Move(temporary, CalibrationFilePath);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _staleTimer != null) _staleTimer.Stop();
            base.Dispose(disposing);
        }
    }
}
