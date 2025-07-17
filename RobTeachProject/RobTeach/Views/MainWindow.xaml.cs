using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
// Explicitly using System.Windows.Shapes.Shape to avoid ambiguity
// using System.Windows.Shapes; // This line can be removed if all Shape usages are qualified
using RobTeach.Views; // Added for DirectionIndicator
using Microsoft.Win32;
using RobTeach.Services;
using RobTeach.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using IxMilia.Dxf; // Required for DxfFile
using IxMilia.Dxf.Entities;
using System.Diagnostics; // Added for Debug.WriteLine
using System.IO;
using System.Text; // Added for Encoding
using RobTeach.Utils; // Added for GeometryUtils
using IxMilia.Dxf.Blocks; // Added for DxfBlock

// using netDxf.Header; // No longer needed with IxMilia.Dxf
using System.Windows.Threading; // Was for optional Dispatcher.Invoke, now used.
using System.Threading.Tasks; // Added for Task.Delay
// using System.Text.RegularExpressions; // Was for optional IP validation, not currently used.
using MahApps.Metro.Controls; // Added for MetroWindow

namespace RobTeach.Views
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml. This is the main window of the RobTeach application,
    /// handling UI events, displaying CAD data, managing configurations, and initiating Modbus communication.
    /// </summary>
    public partial class MainWindow : MetroWindow // Changed from Window to MetroWindow
    {
        private static readonly List<string> _layersToIgnoreForBoundingBox = new List<string>
        {
            "DEFPOINTS", // Standard non-plotting layer
            "AXES",
            "CONSTRUCTION",
            "0_REF",
            "REFERENCE",
            "DIMENSIONS",
            "TEXT_NOTES",
            "VIEWPORT"
            // Add more common non-geometry layer names if known
        };

        // Services used by the MainWindow
        private readonly CadService _cadService = new CadService();
        private readonly ConfigurationService _configService = new ConfigurationService();
        private readonly ModbusService _modbusService = new ModbusService();

        // Current state variables
        private DxfFile? _currentDxfDocument; // Holds the currently loaded DXF document object.
        private string? _currentDxfFilePath;      // Path to the currently loaded DXF file.
        private string? _currentLoadedConfigPath; // Path to the last successfully loaded configuration file.
        private Models.Configuration _currentConfiguration; // The active configuration, either loaded or built from selections.
        private bool isConfigurationDirty = false;
        private RobTeach.Models.Trajectory? _trajectoryInDetailView; // Made nullable

        // Collections for managing DXF entities and their WPF shape representations
        private readonly List<DxfEntity> _selectedDxfEntities = new List<DxfEntity>(); // Stores original DXF entities selected by the user.
        // Qualified System.Windows.Shapes.Shape for dictionary key
        private readonly Dictionary<System.Windows.Shapes.Shape, DxfEntity> _wpfShapeToDxfEntityMap = new Dictionary<System.Windows.Shapes.Shape, DxfEntity>(); // Changed to DxfEntity
        private readonly Dictionary<string, DxfEntity> _dxfEntityHandleMap = new Dictionary<string, DxfEntity>(); // Maps DXF entity handles to entities for quick lookup when loading configs.
        private readonly List<System.Windows.Shapes.Polyline> _trajectoryPreviewPolylines = new List<System.Windows.Shapes.Polyline>(); // Keeps track of trajectory preview polylines for easy removal.
        private List<DirectionIndicator> _directionIndicators; // Field for the direction indicator arrow
        private List<System.Windows.Controls.TextBlock> _orderNumberLabels = new List<System.Windows.Controls.TextBlock>();

        // Fields for CAD Canvas Zoom/Pan functionality
        private ScaleTransform _scaleTransform;         // Handles scaling (zoom) of the canvas content.
        private TranslateTransform _translateTransform; // Handles translation (pan) of the canvas content.
        private TransformGroup _transformGroup;         // Combines scale and translate transforms.
        private System.Windows.Point _panStartPoint;    // Qualified: Stores the starting point of a mouse pan operation.
        private bool _isPanning;                        // Flag indicating if a pan operation is currently in progress.
        private Rect _dxfBoundingBox = Rect.Empty;      // Stores the calculated bounding box of the entire loaded DXF document.

        // Fields for Marquee Selection
        private System.Windows.Shapes.Rectangle? selectionRectangleUI = null; // The visual rectangle for selection
        private System.Windows.Point selectionStartPoint;                   // Start point of the selection rectangle
        private bool isSelectingWithRect = false;                         // Flag indicating if marquee selection is active

        // Styling constants for visual feedback
        private static readonly Brush DefaultStrokeBrush = Brushes.LightGray; // Default color for CAD shapes.
        private static readonly Brush SelectedStrokeBrush = Brushes.DodgerBlue;   // Color for selected CAD shapes.
        private static readonly Brush CurrentSelectionStrokeBrush = Brushes.Green; // Color for the currently selected trajectory
        private const double DefaultStrokeThickness = 2;                          // Default stroke thickness.
        private const double SelectedStrokeThickness = 3.5;                       // Thickness for selected shapes and trajectories.
        private const string TrajectoryPreviewTag = "TrajectoryPreview";          // Tag for identifying trajectory polylines on canvas (not actively used for removal yet).
        private const double TrajectoryPointResolutionAngle = 15.0; // Default resolution for discretizing arcs/circles.


        /// <summary>
        /// Initializes a new instance of the <see cref="MainWindow"/> class.
        /// Sets up default values, initializes transformation objects for the canvas,
        /// and attaches necessary mouse event handlers for canvas interaction.
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
            AppLogger.Log("Application started."); // Log application start

            // Ensure canvas background color is set
            var canvasBackgroundBrush = (SolidColorBrush)Resources["CanvasBackgroundBrush"];
            CadCanvas.Background = canvasBackgroundBrush;

            _directionIndicators = new List<DirectionIndicator>();

            // Initialize product name with a timestamp to ensure uniqueness for new configurations.
            ProductNameTextBox.Text = $"Product_{DateTime.Now:yyyyMMddHHmmss}";
            _previousProductName = ProductNameTextBox.Text; // Initialize for LostFocus tracking
            _currentConfiguration = new Models.Configuration();
            _currentConfiguration.ProductName = ProductNameTextBox.Text;

            // Setup transformations for the CAD canvas
            _scaleTransform = new ScaleTransform(1, 1);
            _translateTransform = new TranslateTransform(0, 0);
            _transformGroup = new TransformGroup();
            _transformGroup.Children.Add(_scaleTransform);    // Apply scaling first
            _transformGroup.Children.Add(_translateTransform); // Then apply translation
            CadCanvas.RenderTransform = _transformGroup;

            // Attach mouse event handlers for canvas zoom and pan
            CadCanvas.MouseWheel += CadCanvas_MouseWheel;
            CadCanvas.MouseDown += CadCanvas_MouseDown; // For initiating pan
            CadCanvas.MouseMove += CadCanvas_MouseMove; // For active panning
            CadCanvas.MouseUp += CadCanvas_MouseUp;     // For ending pan

            // Attach event handler for canvas resize
            CadCanvas.SizeChanged += CadCanvas_SizeChanged;

            // Attach event handlers for nozzle checkboxes
            // UpperNozzleOnCheckBox.Checked += UpperNozzleOnCheckBox_Changed; // Removed
            // UpperNozzleOnCheckBox.Unchecked += UpperNozzleOnCheckBox_Changed; // Removed
            // LowerNozzleOnCheckBox.Checked += LowerNozzleOnCheckBox_Changed; // Removed
            // LowerNozzleOnCheckBox.Unchecked += LowerNozzleOnCheckBox_Changed; // Removed

            // Set initial state for dependent checkboxes
            // UpperNozzleOnCheckBox_Changed(null, null); // Removed
            // LowerNozzleOnCheckBox_Changed(null, null); // Removed

            // Initialize Spray Pass Management
            _selectedDxfEntities.Clear();
            _wpfShapeToDxfEntityMap.Clear(); // Assuming this map is for temporary DXF display, not persistent selection state

            if (_currentConfiguration.SprayPasses == null || !_currentConfiguration.SprayPasses.Any())
            {
                _currentConfiguration.SprayPasses = new List<SprayPass> { new SprayPass { PassName = "Default Pass 1" } };
                _currentConfiguration.CurrentPassIndex = 0;
            }
            else if (_currentConfiguration.CurrentPassIndex < 0 || _currentConfiguration.CurrentPassIndex >= _currentConfiguration.SprayPasses.Count)
            {
                _currentConfiguration.CurrentPassIndex = 0;
            }

            SprayPassesListBox.ItemsSource = _currentConfiguration.SprayPasses;
            if (_currentConfiguration.CurrentPassIndex >= 0 && _currentConfiguration.CurrentPassIndex < SprayPassesListBox.Items.Count)
            {
                SprayPassesListBox.SelectedIndex = _currentConfiguration.CurrentPassIndex;
            }

            // Attach new event handlers
            AddPassButton.Click += AddPassButton_Click;
            RemovePassButton.Click += RemovePassButton_Click;
            RenamePassButton.Click += RenamePassButton_Click;
            SprayPassesListBox.SelectionChanged += SprayPassesListBox_SelectionChanged;

            CurrentPassTrajectoriesListBox.SelectionChanged += CurrentPassTrajectoriesListBox_SelectionChanged;
            MoveTrajectoryUpButton.Click += MoveTrajectoryUpButton_Click;
            MoveTrajectoryDownButton.Click += MoveTrajectoryDownButton_Click;

            // Event handlers for the new six checkboxes
            TrajectoryUpperNozzleEnabledCheckBox.Checked += TrajectoryUpperNozzleEnabledCheckBox_Changed;
            TrajectoryUpperNozzleEnabledCheckBox.Unchecked += TrajectoryUpperNozzleEnabledCheckBox_Changed;
            TrajectoryUpperNozzleGasOnCheckBox.Checked += TrajectoryUpperNozzleGasOnCheckBox_Changed;
            TrajectoryUpperNozzleGasOnCheckBox.Unchecked += TrajectoryUpperNozzleGasOnCheckBox_Changed;
            TrajectoryUpperNozzleLiquidOnCheckBox.Checked += TrajectoryUpperNozzleLiquidOnCheckBox_Changed;
            TrajectoryUpperNozzleLiquidOnCheckBox.Unchecked += TrajectoryUpperNozzleLiquidOnCheckBox_Changed;

            TrajectoryLowerNozzleEnabledCheckBox.Checked += TrajectoryLowerNozzleEnabledCheckBox_Changed;
            TrajectoryLowerNozzleEnabledCheckBox.Unchecked += TrajectoryLowerNozzleEnabledCheckBox_Changed;
            TrajectoryLowerNozzleGasOnCheckBox.Checked += TrajectoryLowerNozzleGasOnCheckBox_Changed;
            TrajectoryLowerNozzleGasOnCheckBox.Unchecked += TrajectoryLowerNozzleGasOnCheckBox_Changed;
            TrajectoryLowerNozzleLiquidOnCheckBox.Checked += TrajectoryLowerNozzleLiquidOnCheckBox_Changed;
            TrajectoryLowerNozzleLiquidOnCheckBox.Unchecked += TrajectoryLowerNozzleLiquidOnCheckBox_Changed;

            // Event handler for TrajectoryIsReversedCheckBox
            TrajectoryIsReversedCheckBox.Checked += TrajectoryIsReversedCheckBox_Changed;
            TrajectoryIsReversedCheckBox.Unchecked += TrajectoryIsReversedCheckBox_Changed;

            // Event handler for TrajectoryRuntimeTextBox
            TrajectoryRuntimeTextBox.LostFocus += TrajectoryRuntimeTextBox_LostFocus;

            // Event handler for ProductNameTextBox LostFocus (assuming XAML TextChanged might remain for isDirty)
            ProductNameTextBox.LostFocus += ProductNameTextBox_LostFocus;

            // Test Run Button
            StartTestRunButton.IsEnabled = false; // Initial state
            StartTestRunButton.Click += StartTestRunButton_Click; // Wire up the event handler

            RefreshCurrentPassTrajectoriesListBox();
            UpdateSelectedTrajectoryDetailUI(); // Initial call (renamed)
            RefreshCadCanvasHighlights(); // Initial call for canvas highlights
            LanguageComboBox.SelectionChanged += LanguageComboBox_SelectionChanged;
            if (_currentConfiguration != null)
            {
                if (_currentConfiguration.Language == "中文")
                {
                    LanguageComboBox.SelectedIndex = 1;
                }
                else
                {
                    LanguageComboBox.SelectedIndex = 0;
                }
                UpdateUIText(_currentConfiguration.Language);
            }
        }
        private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LanguageComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string language = selectedItem.Content.ToString();
                _currentConfiguration.Language = language;
                UpdateUIText(language);
            }
        }
        private void UpdateUIText(string language)
        {
            if (language == "中文")
            {
                Title = "机器人示教软件";
                LoadDxfButton.Content = "加载DXF";
                SaveConfigButton.Content = "保存配置";
                LoadConfigButton.Content = "加载配置";
                FitToViewButton.Content = "适应视图";
                Show3DViewButton.Content = "显示3D视图";
                SprayPassManagementGroupBox.Header = "喷涂遍数管理";
                AvailablePassesLabel.Content = "可用遍数:";
                AddPassButton.Content = "添加";
                RemovePassButton.Content = "移除";
                RenamePassButton.Content = "重命名";
                TrajectorySequenceGroupBox.Header = "轨迹顺序（当前遍数）";
                ExecutionOrderLabel.Content = "执行顺序:";
                MoveTrajectoryUpButton.Content = "上移";
                MoveTrajectoryDownButton.Content = "下移";
                NozzleSettingsGroupBox.Header = "选定轨迹 - 喷嘴设置";
                UpperNozzleGroupBox.Header = "上喷嘴";
                TrajectoryUpperNozzleGasOnCheckBox.Content = "气体开启";
                TrajectoryUpperNozzleLiquidOnCheckBox.Content = "石墨水开启";
                LowerNozzleGroupBox.Header = "下喷嘴";
                TrajectoryLowerNozzleGasOnCheckBox.Content = "气体开启";
                TrajectoryLowerNozzleLiquidOnCheckBox.Content = "石墨水开启";
                GeometryGroupBox.Header = "选定轨迹 - 几何形状";
                TrajectoryIsReversedCheckBox.Content = "反转方向";
                LineStartZLabel.Content = "Z:";
                ArcCenterZLabel.Content = "圆心Z:";
                CircleCenterZLabel.Content = "圆心Z:";
                PolygonZLabel.Content = "Z:";
                TrajectoryRuntimeLabel.Content = "运行时间 (s):";
                PolygonVerticesGroupBox.Header = "顶点";
                TestRunControlGroupBox.Header = "测试运行控制";
                SpeedModeLabel.Content = "速度模式:";
                SlowSpeedRadioButton.Content = "慢速";
                StandardSpeedRadioButton.Content = "标准速度";
                StartTestRunButton.Content = "开始测试运行";
                ModbusIpLabel.Content = "Modbus IP:";
                ModbusPortLabel.Content = "端口:";
                ModbusConnectButton.Content = "连接";
                ModbusDisconnectButton.Content = "断开";
                SendToRobotButton.Content = "发送到机器人";
                ModbusStatusTextBlock.Text = "已断开";
                StatusTextBlock.Text = "就绪";
            }
            else
            {
                Title = "RobTeach Application";
                LoadDxfButton.Content = "Load DXF";
                SaveConfigButton.Content = "Save Config";
                LoadConfigButton.Content = "Load Config";
                FitToViewButton.Content = "Fit to View";
                Show3DViewButton.Content = "Show 3D View";
                SprayPassManagementGroupBox.Header = "Spray Pass Management";
                AvailablePassesLabel.Content = "Available Passes:";
                AddPassButton.Content = "Add Pass";
                RemovePassButton.Content = "Remove Pass";
                RenamePassButton.Content = "Rename Pass";
                TrajectorySequenceGroupBox.Header = "Trajectory Sequence (Current Pass)";
                ExecutionOrderLabel.Content = "Execution Order:";
                MoveTrajectoryUpButton.Content = "Move Up";
                MoveTrajectoryDownButton.Content = "Move Down";
                NozzleSettingsGroupBox.Header = "Selected Trajectory - Nozzle Settings";
                UpperNozzleGroupBox.Header = "Upper Nozzle";
                TrajectoryUpperNozzleGasOnCheckBox.Content = "Gas On";
                TrajectoryUpperNozzleLiquidOnCheckBox.Content = "Liquid On";
                LowerNozzleGroupBox.Header = "Lower Nozzle";
                TrajectoryLowerNozzleGasOnCheckBox.Content = "Gas On";
                TrajectoryLowerNozzleLiquidOnCheckBox.Content = "Liquid On";
                GeometryGroupBox.Header = "Selected Trajectory - Geometry";
                TrajectoryIsReversedCheckBox.Content = "Reverse Direction";
                LineStartZLabel.Content = "Z:";
                ArcCenterZLabel.Content = "Center Z:";
                CircleCenterZLabel.Content = "Center Z:";
                PolygonZLabel.Content = "Z:";
                TrajectoryRuntimeLabel.Content = "Runtime (s):";
                PolygonVerticesGroupBox.Header = "Vertices";
                TestRunControlGroupBox.Header = "Test Run Control";
                SpeedModeLabel.Content = "Speed Mode:";
                SlowSpeedRadioButton.Content = "Slow Speed";
                StandardSpeedRadioButton.Content = "Standard Speed";
                StartTestRunButton.Content = "Start Test Run";
                ModbusIpLabel.Content = "Modbus IP:";
                ModbusPortLabel.Content = "Port:";
                ModbusConnectButton.Content = "Connect";
                ModbusDisconnectButton.Content = "Disconnect";
                SendToRobotButton.Content = "Send to Robot";
                ModbusStatusTextBlock.Text = "Disconnected";
                StatusTextBlock.Text = "Ready";
            }
        }
        // Removed UpperNozzleOnCheckBox_Changed and LowerNozzleOnCheckBox_Changed

        private void RefreshCadCanvasHighlights()
        {
            if (_currentConfiguration == null || CadCanvas == null) return; // Basic safety check

            // Determine entities in the current pass
            HashSet<DxfEntity> entitiesInCurrentPass = new HashSet<DxfEntity>();
            if (_currentConfiguration.CurrentPassIndex >= 0 &&
                _currentConfiguration.CurrentPassIndex < _currentConfiguration.SprayPasses.Count)
            {
                var currentPass = _currentConfiguration.SprayPasses[_currentConfiguration.CurrentPassIndex];
                if (currentPass != null && currentPass.Trajectories != null)
                {
                    foreach (var trajectory in currentPass.Trajectories)
                    {
                        if (trajectory.OriginalDxfEntity != null) // Assuming Trajectory stores the original DxfEntity
                        {
                            entitiesInCurrentPass.Add(trajectory.OriginalDxfEntity);
                        }
                    }
                }
            }

            var currentSelectedTrajectory = CurrentPassTrajectoriesListBox.SelectedItem as Trajectory;

            // Update all shapes on canvas
            foreach (var wpfShape in _wpfShapeToDxfEntityMap.Keys)
            {
                if (_wpfShapeToDxfEntityMap.TryGetValue(wpfShape, out DxfEntity associatedEntity))
                {
                    if (entitiesInCurrentPass.Contains(associatedEntity))
                    {
                        if (currentSelectedTrajectory != null && currentSelectedTrajectory.OriginalDxfEntity == associatedEntity)
                        {
                            wpfShape.Stroke = CurrentSelectionStrokeBrush;
                        }
                        else
                        {
                            wpfShape.Stroke = SelectedStrokeBrush;
                        }
                        wpfShape.StrokeThickness = SelectedStrokeThickness;
                    }
                    else
                    {
                        wpfShape.Stroke = DefaultStrokeBrush;
                        wpfShape.StrokeThickness = DefaultStrokeThickness;
                    }
                }
                else // Should not happen if map is correct
                {
                    wpfShape.Stroke = DefaultStrokeBrush;
                    wpfShape.StrokeThickness = DefaultStrokeThickness;
                }
            }

            // Also, ensure that trajectory preview polylines are handled if they are separate
            // For now, this method focuses on the shapes mapped from _wpfShapeToDxfEntityMap
        }


        private void AddPassButton_Click(object sender, RoutedEventArgs e)
        {
            int passCount = _currentConfiguration.SprayPasses.Count;
            var newPass = new SprayPass { PassName = $"Pass {passCount + 1}" };
            _currentConfiguration.SprayPasses.Add(newPass);

            // Refresh ListBox - simple way for now
            SprayPassesListBox.ItemsSource = null;
            SprayPassesListBox.ItemsSource = _currentConfiguration.SprayPasses;
            SprayPassesListBox.SelectedItem = newPass;
            AppLogger.Log($"Spray pass added: '{newPass.PassName}'.");
            isConfigurationDirty = true;
            UpdateDirectionIndicator(); // New pass selected, current trajectory selection changes
            UpdateOrderNumberLabels();
        }

        private void RemovePassButton_Click(object sender, RoutedEventArgs e)
        {
            if (SprayPassesListBox.SelectedItem is SprayPass selectedPass)
            {
                if (_currentConfiguration.SprayPasses.Count <= 1)
                {
                    string msg = "Cannot remove the last spray pass.";
                    AppLogger.Log(msg, LogLevel.Warning);
                    MessageBox.Show(msg, "Action Denied", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                _currentConfiguration.SprayPasses.Remove(selectedPass);

                // Refresh ListBox and select a new item
                SprayPassesListBox.ItemsSource = null;
                SprayPassesListBox.ItemsSource = _currentConfiguration.SprayPasses;
                if (_currentConfiguration.SprayPasses.Any())
                {
                    _currentConfiguration.CurrentPassIndex = 0;
                    SprayPassesListBox.SelectedIndex = 0;
                }
                else
                {
                    _currentConfiguration.CurrentPassIndex = -1;
                    // Potentially add a new default pass here if needed
                }
                AppLogger.Log($"Spray pass removed: '{selectedPass.PassName}'. New current pass index: {_currentConfiguration.CurrentPassIndex}");
                RefreshCurrentPassTrajectoriesListBox(); // Update trajectory list for new selected pass
                isConfigurationDirty = true;
                UpdateDirectionIndicator(); // Pass removed, current trajectory selection changes
                UpdateOrderNumberLabels();
            }
        }

        private void UpdateOrderNumberLabels()
        {
            // Clear existing labels
            foreach (var label in _orderNumberLabels)
            {
                if (CadCanvas.Children.Contains(label))
                {
                    CadCanvas.Children.Remove(label);
                }
            }
            _orderNumberLabels.Clear();

            // Check for valid current pass and trajectories
            if (_currentConfiguration == null ||
                _currentConfiguration.CurrentPassIndex < 0 ||
                _currentConfiguration.CurrentPassIndex >= _currentConfiguration.SprayPasses.Count ||
                _currentConfiguration.SprayPasses[_currentConfiguration.CurrentPassIndex].Trajectories == null)
            {
                // Debug.WriteLine("[JULES_DEBUG] UpdateOrderNumberLabels: Current configuration or pass is invalid, returning.");
                return;
            }

            var currentPassTrajectories = _currentConfiguration.SprayPasses[_currentConfiguration.CurrentPassIndex].Trajectories;
            // Debug.WriteLine("[JULES_DEBUG] At the beginning of UpdateOrderNumberLabels. Current pass trajectory order:");
            // for (int i = 0; i < currentPassTrajectories.Count; i++)
            // {
            //     Debug.WriteLine($"[JULES_DEBUG]   UpdateLabels-Entry: Pass[{_currentConfiguration.CurrentPassIndex}]-Trajectory[{i}]: {currentPassTrajectories[i].ToString()}");
            // }

            for (int i = 0; i < currentPassTrajectories.Count; i++)
            {
                var selectedTrajectory = currentPassTrajectories[i];
                // Debug.WriteLine($"[JULES_DEBUG] UpdateOrderNumberLabels: Checking Trajectory - Type: {selectedTrajectory.PrimitiveType}, Points Count: {(selectedTrajectory.Points?.Count ?? -1)}, Index: {i}");

                if (selectedTrajectory.Points == null || !selectedTrajectory.Points.Any())
                {
                    // Debug.WriteLine($"[JULES_DEBUG] UpdateOrderNumberLabels: SKIPPING label for Trajectory - Type: {selectedTrajectory.PrimitiveType}, Index: {i} due to no/empty points.");
                    continue; // Skip if no points to base position on
                }

                TextBlock orderLabel = new TextBlock
                {
                    Text = (i + 1).ToString(),
                    FontSize = 10,
                    Foreground = Brushes.DarkSlateBlue, // Changed to DarkSlateBlue for better contrast potentially
                    Background = new SolidColorBrush(Color.FromArgb(200, 255, 255, 180)), // Semi-transparent light yellow
                    Padding = new Thickness(2, 0, 2, 0),
                    // ToolTip = $"Order: {i + 1}, Entity: {selectedTrajectory.PrimitiveType}" // Optional: add a tooltip
                };

                System.Windows.Point anchorPoint;
                if (selectedTrajectory.PrimitiveType == "Line" && selectedTrajectory.Points.Count >= 2)
                {
                    Point3D p_start_3d = selectedTrajectory.Points[0];
                    Point3D p_end_3d = selectedTrajectory.Points[selectedTrajectory.Points.Count - 1];
                    anchorPoint = new System.Windows.Point((p_start_3d.X + p_end_3d.X) / 2, (p_start_3d.Y + p_end_3d.Y) / 2);
                }
                else // For Arcs, Circles, or Lines with < 2 points (though points.Any() is already checked)
                {
                    int midIndex = selectedTrajectory.Points.Count / 2; // Integer division gives lower midpoint for even counts
                    Point3D p_mid_3d = selectedTrajectory.Points[midIndex];
                    anchorPoint = new Point(p_mid_3d.X, p_mid_3d.Y);
                }

                // Define offsets - these might need tweaking after visual review
                // Keeping previous offsets as a starting point
                double offsetX = 5;  // Offset to the right of the anchor point
                double offsetY = -15; // Offset above the anchor point (FontSize is 10, Padding makes it taller)

                // Debug.WriteLine($"[JULES_DEBUG] UpdateOrderNumberLabels: Creating label for Trajectory - Type: {selectedTrajectory.PrimitiveType}, Index: {i}, Anchor: {anchorPoint}");
                // Apply a scale transform to flip the label vertically and keep it screen-sized
                double scale = 1.0 / _scaleTransform.ScaleX;
                orderLabel.RenderTransform = new ScaleTransform(scale, -scale);
                orderLabel.RenderTransformOrigin = new Point(0.5, 0.5);

                Canvas.SetLeft(orderLabel, anchorPoint.X + (offsetX * scale));
                Canvas.SetTop(orderLabel, anchorPoint.Y + (offsetY * scale));
                Panel.SetZIndex(orderLabel, 100); // Ensure labels are on top

                CadCanvas.Children.Add(orderLabel);
                _orderNumberLabels.Add(orderLabel);
            }
        }

        private void RenamePassButton_Click(object sender, RoutedEventArgs e)
        {
            if (SprayPassesListBox.SelectedItem is SprayPass selectedPass)
            {
                string oldPassName = selectedPass.PassName;
                // Simple rename for now, no input dialog
                string newPassName = selectedPass.PassName + "_Renamed"; // Simulate a rename
                selectedPass.PassName = newPassName;

                AppLogger.Log($"Spray pass renamed from '{oldPassName}' to '{newPassName}'.");

                // Refresh ListBox
                SprayPassesListBox.ItemsSource = null;
                SprayPassesListBox.ItemsSource = _currentConfiguration.SprayPasses;
                SprayPassesListBox.SelectedItem = selectedPass;
                isConfigurationDirty = true;
            }
        }

        private void SprayPassesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SprayPassesListBox.SelectedIndex >= 0)
            {
                _currentConfiguration.CurrentPassIndex = SprayPassesListBox.SelectedIndex;
            }
            else if (!_currentConfiguration.SprayPasses.Any()) // All passes removed
            {
                 _currentConfiguration.CurrentPassIndex = -1;
            }
            // If selection cleared due to item removal, index might be -1 but a pass might still be selected by default.
            // The RemovePassButton_Click should handle setting a valid CurrentPassIndex.

            RefreshCurrentPassTrajectoriesListBox();
            UpdateSelectedTrajectoryDetailUI(); // Renamed
            RefreshCadCanvasHighlights();
            UpdateDirectionIndicator(); // Spray pass selection changed
            UpdateOrderNumberLabels();
        }

        private void RefreshCurrentPassTrajectoriesListBox()
        {
            Debug.WriteLine("[LOG] RefreshCurrentPassTrajectoriesListBox: Called.");
            CurrentPassTrajectoriesListBox.ItemsSource = null; // Clear existing items/binding
            if (_currentConfiguration.CurrentPassIndex >= 0 && _currentConfiguration.CurrentPassIndex < _currentConfiguration.SprayPasses.Count)
            {
                var currentPass = _currentConfiguration.SprayPasses[_currentConfiguration.CurrentPassIndex];
                CurrentPassTrajectoriesListBox.ItemsSource = currentPass.Trajectories;
                Debug.WriteLine($"[LOG] RefreshCurrentPassTrajectoriesListBox: Set ItemsSource to a list with {currentPass.Trajectories.Count} trajectories.");
                // Assuming Trajectory.ToString() is overridden for display or DisplayMemberPath is set in XAML if needed
            }
            // else CurrentPassTrajectoriesListBox remains empty
            UpdateSelectedTrajectoryDetailUI(); // Renamed: Update nozzle UI as selected trajectory might change
        }

        private bool _isSelectionChangeHandled = false;
        private void CurrentPassTrajectoriesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSelectionChangeHandled) return;

            _isSelectionChangeHandled = true;
            Trace.WriteLine("++++ CurrentPassTrajectoriesListBox_SelectionChanged Fired ++++");
            Trace.Flush();
            UpdateSelectedTrajectoryDetailUI(); // Renamed
            UpdateDirectionIndicator(); // Add call to update direction indicator
            RefreshCadCanvasHighlights(); // <-- THIS LINE IS ALREADY HERE
            _isSelectionChangeHandled = false;
        }

        private void UpdateDirectionIndicator()
        {
            // Clear existing indicators
            foreach (var indicator in _directionIndicators)
            {
                if (CadCanvas.Children.Contains(indicator))
                {
                    CadCanvas.Children.Remove(indicator);
                }
            }
            _directionIndicators.Clear();

            // Check for valid current pass
            if (_currentConfiguration == null ||
                _currentConfiguration.CurrentPassIndex < 0 ||
                _currentConfiguration.CurrentPassIndex >= _currentConfiguration.SprayPasses.Count ||
                _currentConfiguration.SprayPasses == null)
            {
                return;
            }

            var currentPass = _currentConfiguration.SprayPasses[_currentConfiguration.CurrentPassIndex];
            if (currentPass == null || currentPass.Trajectories == null)
            {
                return;
            }

            double scale = 1.0 / _scaleTransform.ScaleX;
            const double fixedArrowLineLength = 8.0; // Fixed visual length for the arrow's line segment
            Trajectory? actuallySelectedItem = CurrentPassTrajectoriesListBox.SelectedItem as Trajectory; // Get selected item once

            foreach (var trajectoryInLoop in currentPass.Trajectories) // Renamed loop variable for clarity
            {
                if (trajectoryInLoop.Points == null || !trajectoryInLoop.Points.Any())
                {
                    continue; // Skip if no points
                }

                var newIndicator = new DirectionIndicator
                {
                    Color = (trajectoryInLoop == actuallySelectedItem) ? CurrentSelectionStrokeBrush : SelectedStrokeBrush,
                    ArrowheadSize = 8 * scale,
                    StrokeThickness = 1.5 * scale
                };

                List<Point> points = trajectoryInLoop.Points.Select(p => new Point(p.X, p.Y)).ToList();
                System.Windows.Point arrowStartPoint = new System.Windows.Point();
                System.Windows.Point arrowEndPoint = new System.Windows.Point();
                bool addIndicator = false;

                switch (trajectoryInLoop.PrimitiveType)
                {
                    case "Line":
                        if (points.Count >= 2)
                        {
                            System.Windows.Point p_start = points[0];
                            System.Windows.Point p_end = points[points.Count - 1];
                            System.Windows.Point midPoint = new System.Windows.Point((p_start.X + p_end.X) / 2, (p_start.Y + p_end.Y) / 2);
                            System.Windows.Vector direction = p_end - p_start;

                            if (direction.Length > 0)
                            {
                                direction.Normalize();
                                arrowStartPoint = midPoint - direction * (fixedArrowLineLength * scale / 2.0);
                                arrowEndPoint = midPoint + direction * (fixedArrowLineLength * scale / 2.0);
                                addIndicator = true;
                            }
                        }
                        break;
                    case "Arc":
                        if (points.Count >= 2)
                        {
                            System.Windows.Point p0 = points[0]; // First point on arc
                            System.Windows.Point p1 = points[1]; // Second point to determine initial tangent
                            System.Windows.Vector direction = p1 - p0;

                            if (direction.Length > 0.001) // Check for non-zero length
                            {
                                direction.Normalize();
                                // Center the short arrow around p0
                                arrowStartPoint = p0 - direction * (fixedArrowLineLength * scale / 2.0);
                                arrowEndPoint = p0 + direction * (fixedArrowLineLength * scale / 2.0);
                                addIndicator = true;
                            }
                        }
                        break; // End of case "Arc"
                    case "Circle":
                        if (points.Count >= 2)
                        {
                            System.Windows.Point p0 = points[0]; // First point on circumference
                            System.Windows.Point p1 = points[1]; // Second point to determine initial tangent
                            System.Windows.Vector direction = p1 - p0;

                            if (direction.Length > 0)
                            {
                                direction.Normalize();
                                // Center the short arrow around p0
                                arrowStartPoint = p0 - direction * (fixedArrowLineLength * scale / 2.0);
                                arrowEndPoint = p0 + direction * (fixedArrowLineLength * scale / 2.0);
                                addIndicator = true;
                            }
                        }
                        break;
                    default:
                        // Unknown primitive type, do not show indicator
                        break;
                }

                if (addIndicator && arrowStartPoint != arrowEndPoint)
                {
                    newIndicator.StartPoint = arrowStartPoint;
                    newIndicator.EndPoint = arrowEndPoint;
                    System.Windows.Controls.Panel.SetZIndex(newIndicator, 99); // Set a high Z-index
                    CadCanvas.Children.Add(newIndicator);
                    _directionIndicators.Add(newIndicator);
                }
            }
        }

        private void MoveTrajectoryUpButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentConfiguration.CurrentPassIndex < 0 || _currentConfiguration.CurrentPassIndex >= _currentConfiguration.SprayPasses.Count) return;
            var currentPass = _currentConfiguration.SprayPasses[_currentConfiguration.CurrentPassIndex];
            var selectedIndex = CurrentPassTrajectoriesListBox.SelectedIndex;

            if (selectedIndex > 0 && currentPass.Trajectories.Count > selectedIndex)
            {
                var itemToMove = currentPass.Trajectories[selectedIndex];
                currentPass.Trajectories.RemoveAt(selectedIndex);
                currentPass.Trajectories.Insert(selectedIndex - 1, itemToMove);

                CurrentPassTrajectoriesListBox.ItemsSource = null; // Refresh
                CurrentPassTrajectoriesListBox.ItemsSource = currentPass.Trajectories;
                CurrentPassTrajectoriesListBox.SelectedIndex = selectedIndex - 1;
                AppLogger.Log($"Trajectory moved up in pass '{currentPass.PassName}': '{itemToMove.ToString()}' to index {selectedIndex - 1}.");
                isConfigurationDirty = true;
                RefreshCadCanvasHighlights(); // Visual update after reorder
                UpdateDirectionIndicator(); // Selection might change or visual needs refresh
                UpdateOrderNumberLabels();
            }
        }

        private void MoveTrajectoryDownButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentConfiguration.CurrentPassIndex < 0 || _currentConfiguration.CurrentPassIndex >= _currentConfiguration.SprayPasses.Count) return;
            var currentPass = _currentConfiguration.SprayPasses[_currentConfiguration.CurrentPassIndex];
            var selectedIndex = CurrentPassTrajectoriesListBox.SelectedIndex;

            if (selectedIndex >= 0 && selectedIndex < currentPass.Trajectories.Count - 1)
            {
                var itemToMove = currentPass.Trajectories[selectedIndex];
                currentPass.Trajectories.RemoveAt(selectedIndex);
                currentPass.Trajectories.Insert(selectedIndex + 1, itemToMove);

                CurrentPassTrajectoriesListBox.ItemsSource = null; // Refresh
                CurrentPassTrajectoriesListBox.ItemsSource = currentPass.Trajectories;
                CurrentPassTrajectoriesListBox.SelectedIndex = selectedIndex + 1;
                AppLogger.Log($"Trajectory moved down in pass '{currentPass.PassName}': '{itemToMove.ToString()}' to index {selectedIndex + 1}.");
                isConfigurationDirty = true;
                RefreshCadCanvasHighlights(); // Visual update after reorder
                UpdateDirectionIndicator(); // Selection might change or visual needs refresh
                UpdateOrderNumberLabels();
            }
        }

        private void UpdateSelectedTrajectoryDetailUI() // Renamed method
        {
            // Nozzle settings part
            if (CurrentPassTrajectoriesListBox.SelectedItem is Trajectory selectedTrajectory)
            {
                _trajectoryInDetailView = selectedTrajectory; // Assign to the new field

                // Enable GasOn and LiquidOn checkboxes by default when a trajectory is selected.
                // Specific IsEnabled state for GasOn will be set based on LiquidOn state.
                TrajectoryUpperNozzleGasOnCheckBox.IsEnabled = true;
                TrajectoryUpperNozzleLiquidOnCheckBox.IsEnabled = true;
                TrajectoryLowerNozzleGasOnCheckBox.IsEnabled = true;
                TrajectoryLowerNozzleLiquidOnCheckBox.IsEnabled = true;

                // Set IsChecked status for nozzle settings from selected trajectory
                // TrajectoryUpperNozzleEnabledCheckBox.IsChecked = selectedTrajectory.UpperNozzleEnabled; // Hidden
                TrajectoryUpperNozzleLiquidOnCheckBox.IsChecked = selectedTrajectory.UpperNozzleLiquidOn;
                // Set GasOn AFTER LiquidOn, then apply logic
                TrajectoryUpperNozzleGasOnCheckBox.IsChecked = selectedTrajectory.UpperNozzleGasOn;

                // TrajectoryLowerNozzleEnabledCheckBox.IsChecked = selectedTrajectory.LowerNozzleEnabled; // Hidden
                TrajectoryLowerNozzleLiquidOnCheckBox.IsChecked = selectedTrajectory.LowerNozzleLiquidOn;
                // Set GasOn AFTER LiquidOn, then apply logic
                TrajectoryLowerNozzleGasOnCheckBox.IsChecked = selectedTrajectory.LowerNozzleGasOn;

                // Apply interdependency logic for Upper Nozzle
                if (TrajectoryUpperNozzleLiquidOnCheckBox.IsChecked == true)
                {
                    TrajectoryUpperNozzleGasOnCheckBox.IsChecked = true; // Ensure Gas is on if Liquid is on
                    selectedTrajectory.UpperNozzleGasOn = true; // Sync model if needed due to this rule
                    TrajectoryUpperNozzleGasOnCheckBox.IsEnabled = false;
                }
                else
                {
                    TrajectoryUpperNozzleGasOnCheckBox.IsEnabled = true;
                }

                // Apply interdependency logic for Lower Nozzle
                if (TrajectoryLowerNozzleLiquidOnCheckBox.IsChecked == true)
                {
                    TrajectoryLowerNozzleGasOnCheckBox.IsChecked = true; // Ensure Gas is on if Liquid is on
                    selectedTrajectory.LowerNozzleGasOn = true; // Sync model if needed due to this rule
                    TrajectoryLowerNozzleGasOnCheckBox.IsEnabled = false;
                }
                else
                {
                    TrajectoryLowerNozzleGasOnCheckBox.IsEnabled = true;
                }

                // The old EnabledCheckBox_Changed handlers are no longer needed for this.
                // TrajectoryUpperNozzleEnabledCheckBox_Changed(null, null); // Obsolete
                // TrajectoryLowerNozzleEnabledCheckBox_Changed(null, null); // Obsolete

                // Geometry settings part
                TrajectoryIsReversedCheckBox.IsChecked = selectedTrajectory.IsReversed;

                // Visibility of IsReversed checkbox (only for Line/Arc)
                if (selectedTrajectory.PrimitiveType == "Line" || selectedTrajectory.PrimitiveType == "Arc")
                {
                    TrajectoryIsReversedCheckBox.Visibility = Visibility.Visible;
                }
                else
                {
                    TrajectoryIsReversedCheckBox.Visibility = Visibility.Collapsed;
                }

                // Visibility and content of Z-coordinate panels
                LineHeightControlsPanel.Visibility = selectedTrajectory.PrimitiveType == "Line" ? Visibility.Visible : Visibility.Collapsed;
                ArcHeightControlsPanel.Visibility = selectedTrajectory.PrimitiveType == "Arc" ? Visibility.Visible : Visibility.Collapsed;
                CircleHeightControlsPanel.Visibility = selectedTrajectory.PrimitiveType == "Circle" ? Visibility.Visible : Visibility.Collapsed;
                PolygonHeightControlsPanel.Visibility = selectedTrajectory.PrimitiveType == "Polygon" ? Visibility.Visible : Visibility.Collapsed;

                if (selectedTrajectory.PrimitiveType == "Line")
                {
                    LineStartZTextBox.Text = selectedTrajectory.LineStartPoint.Z.ToString("F3");
                    LineEndZTextBox.Text = selectedTrajectory.LineStartPoint.Z.ToString("F3");
                }
                else if (selectedTrajectory.PrimitiveType == "Arc")
                {
                    // For an arc defined by 3 points, the concept of a single "ArcCenter.Z" is complex
                    // if the arc is tilted. For now, we might display the Z of the first point,
                    // or average Z, or leave it blank. Assuming P1's Z for simplicity if available.
                    if (selectedTrajectory.ArcPoint1 != null)
                    {
                        ArcCenterZTextBox.Text = selectedTrajectory.ArcPoint1.Coordinates.Z.ToString("F3");
                    }
                    else
                    {
                        ArcCenterZTextBox.Text = string.Empty; // Or some default like "N/A"
                    }
                }
                else if (selectedTrajectory.PrimitiveType == "Circle")
                {
                    // Display Z of CirclePoint1. Assume P1, P2, P3 are co-planar for Z adjustment via this UI.
                    CircleCenterZTextBox.Text = selectedTrajectory.CirclePoint1.Coordinates.Z.ToString("F3");
                }
                else if (selectedTrajectory.PrimitiveType == "Polygon" && selectedTrajectory.Points.Count > 0)
                {
                    PolygonZTextBox.Text = selectedTrajectory.PolygonZ.ToString("F3");
                }

                // Set Tags for Z-coordinate TextBoxes
                LineStartZTextBox.Tag = selectedTrajectory;
                LineEndZTextBox.Tag = selectedTrajectory;
                ArcCenterZTextBox.Tag = selectedTrajectory;
                CircleCenterZTextBox.Tag = selectedTrajectory; // Still use CircleCenterZTextBox for the tag
                PolygonZTextBox.Tag = selectedTrajectory;

                // Polygon vertices
                if (selectedTrajectory.PrimitiveType == "Polygon")
                {
                    PolygonVerticesGroupBox.Visibility = Visibility.Visible;
                    PolygonVerticesListBox.ItemsSource = selectedTrajectory.Points.Select(p => new Point(p.X, p.Y)).ToList();
                    PolygonVerticesListBox.Items.Refresh();
                }
                else
                {
                    PolygonVerticesGroupBox.Visibility = Visibility.Collapsed;
                    PolygonVerticesListBox.ItemsSource = null;
                }

                // Runtime TextBox
                TrajectoryRuntimeTextBox.IsEnabled = true;
                TrajectoryRuntimeTextBox.Text = selectedTrajectory.Runtime.ToString("F3"); // Format to 3 decimal places
                TrajectoryRuntimeTextBox.Tag = selectedTrajectory;
            }
            else // No trajectory selected
            {
                _trajectoryInDetailView = null; // Clear the field

                // Disable and uncheck all nozzle checkboxes
                TrajectoryUpperNozzleEnabledCheckBox.IsEnabled = false;
                TrajectoryUpperNozzleGasOnCheckBox.IsEnabled = false;
                TrajectoryUpperNozzleLiquidOnCheckBox.IsEnabled = false;
                TrajectoryLowerNozzleEnabledCheckBox.IsEnabled = false;
                TrajectoryLowerNozzleGasOnCheckBox.IsEnabled = false;
                TrajectoryLowerNozzleLiquidOnCheckBox.IsEnabled = false;

                TrajectoryUpperNozzleEnabledCheckBox.IsChecked = false;
                TrajectoryUpperNozzleGasOnCheckBox.IsChecked = false;
                TrajectoryUpperNozzleLiquidOnCheckBox.IsChecked = false;
                TrajectoryLowerNozzleEnabledCheckBox.IsChecked = false;
                TrajectoryLowerNozzleGasOnCheckBox.IsChecked = false;
                TrajectoryLowerNozzleLiquidOnCheckBox.IsChecked = false;

                // Collapse geometry UI elements
                TrajectoryIsReversedCheckBox.Visibility = Visibility.Collapsed;
                TrajectoryIsReversedCheckBox.IsChecked = false; // Uncheck
                LineHeightControlsPanel.Visibility = Visibility.Collapsed;
                ArcHeightControlsPanel.Visibility = Visibility.Collapsed;
                CircleHeightControlsPanel.Visibility = Visibility.Collapsed;
                PolygonHeightControlsPanel.Visibility = Visibility.Collapsed;
                LineStartZTextBox.Text = string.Empty;
                LineEndZTextBox.Text = string.Empty;
                ArcCenterZTextBox.Text = string.Empty;
                CircleCenterZTextBox.Text = string.Empty;

                // Clear Tags for Z-coordinate TextBoxes
                LineStartZTextBox.Tag = null;
                LineEndZTextBox.Tag = null;
                ArcCenterZTextBox.Tag = null;
                CircleCenterZTextBox.Tag = null;
                PolygonZTextBox.Tag = null;

                // Runtime TextBox
                TrajectoryRuntimeTextBox.IsEnabled = false;
                TrajectoryRuntimeTextBox.Text = string.Empty;
                TrajectoryRuntimeTextBox.Tag = null;
            }
        }

        // Removed CalculateTrajectoryLength - Moved to TrajectoryUtils

        private void TrajectoryRuntimeTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (TrajectoryRuntimeTextBox.Tag is Trajectory selectedTrajectory && selectedTrajectory != null)
            {
                if (double.TryParse(TrajectoryRuntimeTextBox.Text, out double newRuntime))
                {
                    double minRuntime = TrajectoryUtils.CalculateMinRuntime(selectedTrajectory);
                    if (newRuntime >= minRuntime)
                    {
                        if (selectedTrajectory.Runtime != newRuntime)
                        {
                            double oldRuntime = selectedTrajectory.Runtime;
                            selectedTrajectory.Runtime = newRuntime;
                            AppLogger.Log($"Trajectory '{selectedTrajectory.ToString()}' Runtime changed from {oldRuntime:F3}s to {newRuntime:F3}s in pass '{_currentConfiguration.SprayPasses[_currentConfiguration.CurrentPassIndex].PassName}'.");
                            isConfigurationDirty = true;
                            // No need to refresh listbox item as Runtime is not part of its ToString() typically
                        }
                    }
                    else
                    {
                        string msg = $"Runtime cannot be less than the minimum calculated value: {minRuntime:F3} s.";
                        AppLogger.Log(msg, LogLevel.Warning);
                        MessageBox.Show(msg, "Invalid Runtime", MessageBoxButton.OK, MessageBoxImage.Warning);
                        TrajectoryRuntimeTextBox.Text = selectedTrajectory.Runtime.ToString("F3"); // Revert to current or last valid
                    }
                }
                else
                {
                    string msg = "Invalid runtime value. Please enter a valid number.";
                    AppLogger.Log(msg, LogLevel.Error);
                    MessageBox.Show(msg, "Input Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    TrajectoryRuntimeTextBox.Text = selectedTrajectory.Runtime.ToString("F3"); // Revert
                }
            }
        }

        // Removed CalculateMinRuntime - Moved to TrajectoryUtils


        private void TrajectoryIsReversedCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (CurrentPassTrajectoriesListBox.SelectedItem is Trajectory selectedTrajectory)
            {
                bool isReversedNow = TrajectoryIsReversedCheckBox.IsChecked ?? false;
                if (selectedTrajectory.IsReversed != isReversedNow) // Only proceed if state actually changed
                {
                    selectedTrajectory.IsReversed = isReversedNow;
                    AppLogger.Log($"Trajectory '{selectedTrajectory.ToString()}' IsReversed changed to: {isReversedNow} in pass '{_currentConfiguration.SprayPasses[_currentConfiguration.CurrentPassIndex].PassName}'.");
                    isConfigurationDirty = true;

                    if (selectedTrajectory.PrimitiveType == "Line")
                    {
                        var tempPoint = selectedTrajectory.LineStartPoint;
                        selectedTrajectory.LineStartPoint = selectedTrajectory.LineEndPoint;
                        selectedTrajectory.LineEndPoint = tempPoint;
                    }
                    else if (selectedTrajectory.PrimitiveType == "Arc")
                    {
                        var tempArcPoint = selectedTrajectory.ArcPoint1;
                        selectedTrajectory.ArcPoint1 = selectedTrajectory.ArcPoint3;
                        selectedTrajectory.ArcPoint3 = tempArcPoint;
                        // Note: ArcPoint2 (midpoint) remains the same.
                        // The interpretation of P1, P2, P3 to calculate center, start/end angles
                        // in PopulateTrajectoryPoints and WriteSendDataToTempFile must be robust to this swap.
                    }
                    else if (selectedTrajectory.PrimitiveType == "Polygon")
                    {
                        selectedTrajectory.Points.Reverse();
                        // Update the listbox directly since UpdateSelectedTrajectoryDetailUI would re-order it
                        PolygonVerticesListBox.ItemsSource = selectedTrajectory.Points.Select(p => new Point(p.X, p.Y)).ToList();
                        PolygonVerticesListBox.Items.Refresh();
                    }


                    PopulateTrajectoryPoints(selectedTrajectory); // Regenerate points for display
                    CurrentPassTrajectoriesListBox.Items.Refresh(); // Update display
                    RefreshCadCanvasHighlights();
                    UpdateDirectionIndicator();
                }
            }
        }

        private void PopulateTrajectoryPoints(Trajectory trajectory)
        {
            if (trajectory == null) return;

            switch (trajectory.PrimitiveType)
            {
                case "Line":
                    trajectory.Points.Clear();
                    trajectory.Points.AddRange(_cadService.ConvertLineTrajectoryToPoints(trajectory).Select(p => new Point3D(p.X, p.Y, 0)));
                    break;
                case "Arc":
                    trajectory.Points.Clear();
                    if (trajectory.ArcPoint1 != null && trajectory.ArcPoint2 != null && trajectory.ArcPoint3 != null)
                    {
                        var arcParams = GeometryUtils.CalculateArcParametersFromThreePoints(
                            trajectory.ArcPoint1.Coordinates,
                            trajectory.ArcPoint2.Coordinates,
                            trajectory.ArcPoint3.Coordinates);

                        if (arcParams.HasValue)
                        {
                            var (center, radius, startAngle, endAngle, normal, isClockwise) = arcParams.Value;
                            // Create a temporary DxfArc to use with existing CadService method
                            // Note: CadService.ConvertArcToPoints expects angles in degrees.
                            // The DxfArc constructor also expects angles in degrees.
                            DxfArc tempArc = new DxfArc(center, radius, startAngle, endAngle) { Normal = normal };

                            // If the original intention was a CW arc from P1->P2->P3, but DxfArc always assumes CCW,
                            // and our CalculateArcParametersFromThreePoints also re-orders to ensure CCW for DxfArc,
                            // then the points should be generated correctly.
                            // If `IsReversed` on trajectory is true, that will be handled by `_cadService.ConvertArcToPoints` if it has that logic,
                            // or it might need to be handled by swapping start/end angles before calling.
                            // For now, assuming ConvertArcToPoints generates CCW points from startAngle to endAngle.
                            // The IsReversed logic is handled in ConvertArcTrajectoryToPoints in CadService if it needs to be.
                            // Let's assume the current CadService.ConvertArcToPoints can take this tempArc.
                            // However, ConvertArcTrajectoryToPoints takes a Trajectory object.
                            // We need to adapt _cadService.ConvertArcToPoints or use a similar discretization here.

                            // Re-using the logic from CadService.ConvertArcToPoints directly for now for simplicity:
                            List<Point3D> arcPoints = new List<Point3D>();
                            double currentAngleDeg = startAngle;
                            double effectiveEndAngleDeg = endAngle;

                            if (isClockwise) // Should have been handled by swapping start/end in CalculateArcParameters. Re-check.
                            {
                                // If CalculateArcParametersFromThreePoints always returns startAngle < endAngle for CCW DxfArc,
                                // then isClockwise flag here might be redundant or indicate original user intent.
                                // For now, assume parameters are for CCW traversal.
                                // If trajectory.IsReversed is true, then we need to reverse the point generation order or angles.
                                if (trajectory.IsReversed) {
                                    currentAngleDeg = endAngle;
                                    effectiveEndAngleDeg = startAngle;
                                    if (effectiveEndAngleDeg > currentAngleDeg) currentAngleDeg += 360; // Sweep backwards
                                } else {
                                     if (effectiveEndAngleDeg < currentAngleDeg) effectiveEndAngleDeg += 360;
                                }
                            } else { // CCW from P1->P2->P3
                                if (trajectory.IsReversed) {
                                    currentAngleDeg = endAngle;
                                    effectiveEndAngleDeg = startAngle;
                                    if (effectiveEndAngleDeg > currentAngleDeg) currentAngleDeg += 360; // Sweep backwards
                                } else {
                                     if (effectiveEndAngleDeg < currentAngleDeg) effectiveEndAngleDeg += 360;
                                }
                            }

                            bool sweepBackwards = trajectory.IsReversed; // Simplified logic from CadService.ConvertArcTrajectoryToPoints
                            if (sweepBackwards) {
                                currentAngleDeg = endAngle; // Start from original end
                                effectiveEndAngleDeg = startAngle; // Go to original start
                                 // Ensure we sweep correctly backwards
                                double sweep = effectiveEndAngleDeg - currentAngleDeg;
                                if (sweep > 0) sweep -= 360; // Ensure negative or zero sweep for backward
                                effectiveEndAngleDeg = currentAngleDeg + sweep; // Target for loop
                            } else {
                                currentAngleDeg = startAngle;
                                effectiveEndAngleDeg = endAngle;
                                double sweep = effectiveEndAngleDeg - currentAngleDeg;
                                if (sweep < 0) sweep += 360; // Ensure positive or zero sweep for forward
                                effectiveEndAngleDeg = currentAngleDeg + sweep;
                            }


                            double step = sweepBackwards ? -TrajectoryPointResolutionAngle : TrajectoryPointResolutionAngle;
                            if (Math.Abs(step) < 1e-6) step = sweepBackwards ? -1.0 : 1.0; // Avoid zero step

                            if (sweepBackwards) {
                                while (currentAngleDeg >= effectiveEndAngleDeg - Math.Abs(step)/2.0) { // Loop condition for backwards
                                    double radAngle = currentAngleDeg * Math.PI / 180.0;
                                    arcPoints.Add(new Point3D(center.X + radius * Math.Cos(radAngle), center.Y + radius * Math.Sin(radAngle), center.Z));
                                    if (currentAngleDeg <= effectiveEndAngleDeg + 1e-5 && currentAngleDeg >= effectiveEndAngleDeg - 1e-5) break; // Reached end
                                    currentAngleDeg += step;
                                     if (currentAngleDeg < effectiveEndAngleDeg && currentAngleDeg > effectiveEndAngleDeg + step -1e-5 ) currentAngleDeg = effectiveEndAngleDeg; // Ensure last point
                                }
                            } else {
                                while (currentAngleDeg <= effectiveEndAngleDeg + Math.Abs(step)/2.0) { // Loop condition for forwards
                                    double radAngle = currentAngleDeg * Math.PI / 180.0;
                                    arcPoints.Add(new Point3D(center.X + radius * Math.Cos(radAngle), center.Y + radius * Math.Sin(radAngle), center.Z));
                                     if (currentAngleDeg <= effectiveEndAngleDeg + 1e-5 && currentAngleDeg >= effectiveEndAngleDeg - 1e-5) break; // Reached end
                                    currentAngleDeg += step;
                                    if (currentAngleDeg > effectiveEndAngleDeg && currentAngleDeg < effectiveEndAngleDeg + step - 1e-5) currentAngleDeg = effectiveEndAngleDeg; // Ensure last point
                                }
                            }
                            trajectory.Points.AddRange(arcPoints);
                        }
                        else
                        {
                            Debug.WriteLine($"[WARNING] PopulateTrajectoryPoints: Could not calculate arc parameters for trajectory. PrimitiveType: {trajectory.PrimitiveType}. Defaulting to line segments if P1,P2,P3 exist.");
                            // Fallback: add P1, P2, P3 as line segments if arc calculation fails
                            if (trajectory.ArcPoint1 != null) trajectory.Points.Add(new Point3D(trajectory.ArcPoint1.Coordinates.X, trajectory.ArcPoint1.Coordinates.Y, trajectory.ArcPoint1.Coordinates.Z));
                            if (trajectory.ArcPoint2 != null) trajectory.Points.Add(new Point3D(trajectory.ArcPoint2.Coordinates.X, trajectory.ArcPoint2.Coordinates.Y, trajectory.ArcPoint2.Coordinates.Z));
                            if (trajectory.ArcPoint3 != null) trajectory.Points.Add(new Point3D(trajectory.ArcPoint3.Coordinates.X, trajectory.ArcPoint3.Coordinates.Y, trajectory.ArcPoint3.Coordinates.Z));
                        }
                    }
                    else
                    {
                        Debug.WriteLine($"[WARNING] PopulateTrajectoryPoints: Arc trajectory missing ArcPoint1/2/3 data.");
                    }
                    break;
                case "Circle":
                    trajectory.Points.Clear();
                    // Debug.WriteLine($"[JULES_DEBUG] PopulateTrajectoryPoints (Circle): Input P1={trajectory.CirclePoint1.Coordinates}, P2={trajectory.CirclePoint2.Coordinates}, P3={trajectory.CirclePoint3.Coordinates}");
                    var circleParams = GeometryUtils.CalculateCircleCenterRadiusFromThreePoints(
                        trajectory.CirclePoint1.Coordinates,
                        trajectory.CirclePoint2.Coordinates,
                        trajectory.CirclePoint3.Coordinates);

                    if (circleParams.HasValue)
                    {
                        var (center, radius, normal) = circleParams.Value;
                        // Debug.WriteLine($"[JULES_DEBUG] PopulateTrajectoryPoints (Circle): Calculated Params: Center={center}, Radius={radius}, Normal={normal}");
                        List<Point3D> circlePoints = new List<Point3D>();

                        DxfVector localXAxis;
                        double arbThreshold = 1.0 / 64.0;
                        if (Math.Abs(normal.X) < arbThreshold && Math.Abs(normal.Y) < arbThreshold)
                            localXAxis = (new DxfVector(0, 1, 0)).Cross(normal).Normalize();
                        else
                            localXAxis = (DxfVector.ZAxis).Cross(normal).Normalize();
                        DxfVector localYAxis = normal.Cross(localXAxis).Normalize();
                        // Debug.WriteLine($"[JULES_DEBUG] PopulateTrajectoryPoints (Circle): ResolutionAngle={TrajectoryPointResolutionAngle}, LocalX={localXAxis}, LocalY={localYAxis}");

                        for (double angleDeg = 0; angleDeg < 360.0; angleDeg += TrajectoryPointResolutionAngle)
                        {
                            double angleRad = angleDeg * Math.PI / 180.0;
                            double cosAngle = Math.Cos(angleRad);
                            double sinAngle = Math.Sin(angleRad);
                            DxfVector termX = new DxfVector(localXAxis.X * cosAngle, localXAxis.Y * cosAngle, localXAxis.Z * cosAngle);
                            DxfVector termY = new DxfVector(localYAxis.X * sinAngle, localYAxis.Y * sinAngle, localYAxis.Z * sinAngle);
                            DxfVector directionOnPlane_unscaled = termX + termY; // Assuming DxfVector + DxfVector is okay
                            DxfPoint pointOnCircle = center + new DxfVector(directionOnPlane_unscaled.X * radius, directionOnPlane_unscaled.Y * radius, directionOnPlane_unscaled.Z * radius);
                            circlePoints.Add(new Point3D(pointOnCircle.X, pointOnCircle.Y, pointOnCircle.Z));
                            // Debug.WriteLine($"[JULES_DEBUG] PopulateTrajectoryPoints (Circle): angleDeg={angleDeg}, pointOnCircle={pointOnCircle}, Added to circlePoints. Count: {circlePoints.Count}");
                        }

                        if (circlePoints.Count > 0)
                        {
                            DxfPoint firstDxfPoint = center + new DxfVector(localXAxis.X * radius, localXAxis.Y * radius, localXAxis.Z * radius);
                            Point3D firstPoint = new Point3D(firstDxfPoint.X, firstDxfPoint.Y, firstDxfPoint.Z);
                            if (Math.Abs(circlePoints.Last().X - firstPoint.X) > 1e-6 || Math.Abs(circlePoints.Last().Y - firstPoint.Y) > 1e-6 || Math.Abs(circlePoints.Last().Z - firstPoint.Z) > 1e-6)
                            {
                                circlePoints.Add(firstPoint);
                                // Debug.WriteLine($"[JULES_DEBUG] PopulateTrajectoryPoints (Circle): Added closing point. Count: {circlePoints.Count}");
                            }
                        }
                        // Debug.WriteLine($"[JULES_DEBUG] PopulateTrajectoryPoints (Circle): Total points generated before AddRange: {circlePoints.Count}");
                        trajectory.Points.AddRange(circlePoints);
                        Debug.WriteLineIf(circlePoints.Count == 0, $"[JULES_WARNING] PopulateTrajectoryPoints (Circle): Generated 0 points for circle P1={trajectory.CirclePoint1.Coordinates}, P2={trajectory.CirclePoint2.Coordinates}, P3={trajectory.CirclePoint3.Coordinates}");
                    }
                    else
                    {
                        Debug.WriteLine($"[JULES_WARNING] PopulateTrajectoryPoints (Circle): Could not calculate circle parameters. Fallback to 3 points. P1={trajectory.CirclePoint1.Coordinates}, P2={trajectory.CirclePoint2.Coordinates}, P3={trajectory.CirclePoint3.Coordinates}");
                        trajectory.Points.Add(new Point3D(trajectory.CirclePoint1.Coordinates.X, trajectory.CirclePoint1.Coordinates.Y, trajectory.CirclePoint1.Coordinates.Z));
                        // Debug.WriteLine($"[JULES_DEBUG] PopulateTrajectoryPoints (Circle Fallback): Added P1. Points count: {trajectory.Points.Count}");
                        trajectory.Points.Add(new Point3D(trajectory.CirclePoint2.Coordinates.X, trajectory.CirclePoint2.Coordinates.Y, trajectory.CirclePoint2.Coordinates.Z));
                        // Debug.WriteLine($"[JULES_DEBUG] PopulateTrajectoryPoints (Circle Fallback): Added P2. Points count: {trajectory.Points.Count}");
                        trajectory.Points.Add(new Point3D(trajectory.CirclePoint3.Coordinates.X, trajectory.CirclePoint3.Coordinates.Y, trajectory.CirclePoint3.Coordinates.Z));
                        // Debug.WriteLine($"[JULES_DEBUG] PopulateTrajectoryPoints (Circle Fallback): Added P3. Points count: {trajectory.Points.Count}");
                        if(trajectory.Points.Count > 1)
                        {
                           trajectory.Points.Add(new Point3D(trajectory.CirclePoint1.Coordinates.X, trajectory.CirclePoint1.Coordinates.Y, trajectory.CirclePoint1.Coordinates.Z));
                           // Debug.WriteLine($"[JULES_DEBUG] PopulateTrajectoryPoints (Circle Fallback): Closed with P1. Points count: {trajectory.Points.Count}");
                        }
                    }
                    // Debug.WriteLine($"[JULES_DEBUG] PopulateTrajectoryPoints (Circle): Final trajectory.Points.Count = {trajectory.Points.Count}");
                    break;
                case "Polygon":
                    if (trajectory.OriginalDxfEntity is DxfLwPolyline polyline)
                    {
                        trajectory.Points.Clear();
                        var vertices = polyline.Vertices.Select(v => new Point3D(v.X, v.Y, polyline.Elevation)).ToList();
                        if (vertices.Any())
                        {
                            int startIndex = FindBottomLeftVertexIndex(vertices.Select(p => new Point(p.X, p.Y)).ToList());
                            var orderedVertices = new List<Point3D>();
                            for (int i = 0; i < vertices.Count; i++)
                            {
                                orderedVertices.Add(vertices[(startIndex + i) % vertices.Count]);
                            }
                            trajectory.Points.AddRange(orderedVertices);
                        }
                    }
                    break;
                default:
                    // For other types or if PrimitiveType is not set, Points will remain empty or could be populated from OriginalDxfEntity if needed
                    // For now, we rely on the specific Convert<Primitive>TrajectoryToPoints methods.
                    // If OriginalDxfEntity exists and is of a known DxfEntityType, could fall back to old methods:
                    if (trajectory.OriginalDxfEntity != null) {
                        switch (trajectory.OriginalDxfEntity) {
                            case DxfLine line: trajectory.Points.AddRange(_cadService.ConvertLineToPoints(line).Select(p => new Point3D(p.X, p.Y, 0))); break;
                            case DxfArc arc: trajectory.Points.AddRange(_cadService.ConvertArcToPoints(arc, TrajectoryPointResolutionAngle).Select(p => new Point3D(p.X, p.Y, 0))); break;
                            case DxfCircle circle: trajectory.Points.AddRange(_cadService.ConvertCircleToPoints(circle, TrajectoryPointResolutionAngle).Select(p => new Point3D(p.X, p.Y, 0))); break;
                        }
                    }
                    break;
            }
        }

        private void TrajectoryUpperNozzleEnabledCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            // This checkbox is now hidden (Visibility="Collapsed" in XAML).
            // Its logic for enabling/disabling GasOn/LiquidOn checkboxes is superseded by the new interdependency logic.
            // We still need to update the model if this event were somehow triggered,
            // though it shouldn't be if the CheckBox is collapsed.
            // For safety, or if it's decided to use the model property differently:
            if (CurrentPassTrajectoriesListBox.SelectedItem is Trajectory selectedTrajectory)
            {
                // selectedTrajectory.UpperNozzleEnabled = TrajectoryUpperNozzleEnabledCheckBox.IsChecked ?? false;
                // The model's UpperNozzleEnabled might be re-purposed or removed later.
                // For now, we won't automatically uncheck Gas/Liquid here based on this hidden checkbox.
                // isConfigurationDirty = true; // Only if model property is still relevant and changed.
            }
        }

        private void TrajectoryUpperNozzleGasOnCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (CurrentPassTrajectoriesListBox.SelectedItem is Trajectory selectedTrajectory)
            {
                bool newValue = TrajectoryUpperNozzleGasOnCheckBox.IsChecked ?? false;
                if (selectedTrajectory.UpperNozzleGasOn != newValue)
                {
                    selectedTrajectory.UpperNozzleGasOn = newValue;
                    AppLogger.Log($"Trajectory '{selectedTrajectory.ToString()}' UpperNozzleGasOn changed to: {newValue} in pass '{_currentConfiguration.SprayPasses[_currentConfiguration.CurrentPassIndex].PassName}'.");
                    isConfigurationDirty = true;
                }
            }
        }

        private void TrajectoryUpperNozzleLiquidOnCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (CurrentPassTrajectoriesListBox.SelectedItem is Trajectory selectedTrajectory)
            {
                bool isLiquidOn = TrajectoryUpperNozzleLiquidOnCheckBox.IsChecked ?? false;
                bool gasStateChangedByLogic = false;

                if (selectedTrajectory.UpperNozzleLiquidOn != isLiquidOn)
                {
                    selectedTrajectory.UpperNozzleLiquidOn = isLiquidOn;
                    AppLogger.Log($"Trajectory '{selectedTrajectory.ToString()}' UpperNozzleLiquidOn changed to: {isLiquidOn} in pass '{_currentConfiguration.SprayPasses[_currentConfiguration.CurrentPassIndex].PassName}'.");
                    isConfigurationDirty = true;
                }

                if (isLiquidOn)
                {
                    if (!selectedTrajectory.UpperNozzleGasOn) // If gas is not already on
                    {
                        selectedTrajectory.UpperNozzleGasOn = true; // Update model
                        TrajectoryUpperNozzleGasOnCheckBox.IsChecked = true; // Force Gas On CheckBox
                        AppLogger.Log($"Trajectory '{selectedTrajectory.ToString()}' UpperNozzleGasOn automatically set to: true due to LiquidOn in pass '{_currentConfiguration.SprayPasses[_currentConfiguration.CurrentPassIndex].PassName}'.");
                        gasStateChangedByLogic = true; // Indicates a change that might need logging if not covered by its own event
                        isConfigurationDirty = true;
                    }
                    TrajectoryUpperNozzleGasOnCheckBox.IsEnabled = false; // Disable Gas checkbox
                }
                else
                {
                    TrajectoryUpperNozzleGasOnCheckBox.IsEnabled = true; // Enable Gas checkbox
                }
                // No explicit logging for isConfigurationDirty here as it's set if property changes.
            }
        }

        private void TrajectoryLowerNozzleEnabledCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            // This checkbox is now hidden (Visibility="Collapsed" in XAML).
            // Logic is superseded by new interdependency.
            if (CurrentPassTrajectoriesListBox.SelectedItem is Trajectory selectedTrajectory)
            {
                // selectedTrajectory.LowerNozzleEnabled = TrajectoryLowerNozzleEnabledCheckBox.IsChecked ?? false;
                // The model's LowerNozzleEnabled might be re-purposed or removed later.
                // isConfigurationDirty = true; // Only if model property is still relevant and changed.
            }
        }

        private void TrajectoryLowerNozzleGasOnCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (CurrentPassTrajectoriesListBox.SelectedItem is Trajectory selectedTrajectory)
            {
                bool newValue = TrajectoryLowerNozzleGasOnCheckBox.IsChecked ?? false;
                if (selectedTrajectory.LowerNozzleGasOn != newValue)
                {
                    selectedTrajectory.LowerNozzleGasOn = newValue;
                    AppLogger.Log($"Trajectory '{selectedTrajectory.ToString()}' LowerNozzleGasOn changed to: {newValue} in pass '{_currentConfiguration.SprayPasses[_currentConfiguration.CurrentPassIndex].PassName}'.");
                    isConfigurationDirty = true;
                }
            }
        }

        private void TrajectoryLowerNozzleLiquidOnCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (CurrentPassTrajectoriesListBox.SelectedItem is Trajectory selectedTrajectory)
            {
                bool isLiquidOn = TrajectoryLowerNozzleLiquidOnCheckBox.IsChecked ?? false;
                bool gasStateChangedByLogic = false;

                if (selectedTrajectory.LowerNozzleLiquidOn != isLiquidOn)
                {
                    selectedTrajectory.LowerNozzleLiquidOn = isLiquidOn;
                    AppLogger.Log($"Trajectory '{selectedTrajectory.ToString()}' LowerNozzleLiquidOn changed to: {isLiquidOn} in pass '{_currentConfiguration.SprayPasses[_currentConfiguration.CurrentPassIndex].PassName}'.");
                    isConfigurationDirty = true;
                }

                if (isLiquidOn)
                {
                    if (!selectedTrajectory.LowerNozzleGasOn) // If gas is not already on
                    {
                        selectedTrajectory.LowerNozzleGasOn = true; // Update model
                        TrajectoryLowerNozzleGasOnCheckBox.IsChecked = true; // Force Gas On CheckBox
                        AppLogger.Log($"Trajectory '{selectedTrajectory.ToString()}' LowerNozzleGasOn automatically set to: true due to LiquidOn in pass '{_currentConfiguration.SprayPasses[_currentConfiguration.CurrentPassIndex].PassName}'.");
                        gasStateChangedByLogic = true;
                        isConfigurationDirty = true;
                    }
                    TrajectoryLowerNozzleGasOnCheckBox.IsEnabled = false; // Disable Gas checkbox
                }
                else
                {
                    TrajectoryLowerNozzleGasOnCheckBox.IsEnabled = true; // Enable Gas checkbox
                }
            }
        }

        private void ProductNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Check if the control is initialized and loaded to avoid setting dirty flag during setup
            if (this.IsLoaded)
            {
                isConfigurationDirty = true;
                // This event fires on every keystroke, which is too noisy for logging.
                // Logging will be done in a new ProductNameTextBox_LostFocus event handler.
            }
        }

        private string _previousProductName = "";

        private void ProductNameTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (this.IsLoaded && ProductNameTextBox.Text != _previousProductName)
            {
                AppLogger.Log($"Product name changed from '{_previousProductName}' to '{ProductNameTextBox.Text}'.");
                _previousProductName = ProductNameTextBox.Text; // Update for next comparison
                isConfigurationDirty = true; // isConfigurationDirty is likely already true due to TextChanged
            }
        }

    private void UpdateLineStartZFromTextBox()
    {
        if (_trajectoryInDetailView != null && _trajectoryInDetailView.PrimitiveType == "Line")
        {
            if (double.TryParse(LineStartZTextBox.Text, out double newZ))
            {
                if (_trajectoryInDetailView.LineStartPoint.Z != newZ)
                {
                    double oldZ = _trajectoryInDetailView.LineStartPoint.Z;
                    _trajectoryInDetailView.LineStartPoint = new DxfPoint(
                        _trajectoryInDetailView.LineStartPoint.X,
                        _trajectoryInDetailView.LineStartPoint.Y,
                        newZ);
                    _trajectoryInDetailView.LineEndPoint = new DxfPoint(
                        _trajectoryInDetailView.LineEndPoint.X,
                        _trajectoryInDetailView.LineEndPoint.Y,
                        newZ);
                    AppLogger.Log($"Trajectory '{_trajectoryInDetailView.ToString()}' Line Z changed from {oldZ:F3} to {newZ:F3} in pass '{_currentConfiguration.SprayPasses[_currentConfiguration.CurrentPassIndex].PassName}'.");
                    isConfigurationDirty = true;
                    CurrentPassTrajectoriesListBox.Items.Refresh(); // Refresh if Z might be part of ToString()
                }
            }
            else
            {
                string msg = "Invalid Z value. Please enter a valid number.";
                AppLogger.Log(msg, LogLevel.Error);
                MessageBox.Show(msg, "Input Error", MessageBoxButton.OK, MessageBoxImage.Error);
                LineStartZTextBox.Text = _trajectoryInDetailView.LineStartPoint.Z.ToString("F3");
            }
        }
    }

    private void UpdateLineEndZFromTextBox()
    {
        // This method is now empty as the UI for it is hidden.
    }

    private void LineStartZTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateLineStartZFromTextBox();
    }

    private void LineEndZTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateLineEndZFromTextBox();
    }

    private void UpdateArcCenterZFromTextBox()
    {
        if (_trajectoryInDetailView != null && _trajectoryInDetailView.PrimitiveType == "Arc")
        {
            if (double.TryParse(ArcCenterZTextBox.Text, out double newZ))
            {
                // For a 3-point arc, updating a single "Center Z" is complex.
                // Assuming this Z applies to all three points P1, P2, P3 for simplicity,
                // which means the arc is being moved along the Z-axis without tilting.
                bool changed = false;
                if (_trajectoryInDetailView.ArcPoint1 != null && _trajectoryInDetailView.ArcPoint1.Coordinates.Z != newZ)
                {
                    _trajectoryInDetailView.ArcPoint1.Coordinates = new DxfPoint(
                        _trajectoryInDetailView.ArcPoint1.Coordinates.X,
                        _trajectoryInDetailView.ArcPoint1.Coordinates.Y,
                        newZ);
                    changed = true;
                }
                if (_trajectoryInDetailView.ArcPoint2 != null && _trajectoryInDetailView.ArcPoint2.Coordinates.Z != newZ)
                {
                    _trajectoryInDetailView.ArcPoint2.Coordinates = new DxfPoint(
                        _trajectoryInDetailView.ArcPoint2.Coordinates.X,
                        _trajectoryInDetailView.ArcPoint2.Coordinates.Y,
                        newZ);
                    changed = true;
                }
                if (_trajectoryInDetailView.ArcPoint3 != null && _trajectoryInDetailView.ArcPoint3.Coordinates.Z != newZ)
                {
                    _trajectoryInDetailView.ArcPoint3.Coordinates = new DxfPoint(
                        _trajectoryInDetailView.ArcPoint3.Coordinates.X,
                        _trajectoryInDetailView.ArcPoint3.Coordinates.Y,
                        newZ);
                    changed = true;
                }

                if (changed)
                {
                    // To log the old Z, we'd need to capture it before any changes.
                    // For simplicity, we'll log that the Arc Z was set to newZ.
                    // A more detailed log would capture Z of P1, P2, P3 before and after.
                    AppLogger.Log($"Trajectory '{_trajectoryInDetailView.ToString()}' Arc points Z (P1, P2, P3) set to {newZ:F3} in pass '{_currentConfiguration.SprayPasses[_currentConfiguration.CurrentPassIndex].PassName}'.");
                    isConfigurationDirty = true;
                    PopulateTrajectoryPoints(_trajectoryInDetailView); // Regenerate points with new Z
                    CurrentPassTrajectoriesListBox.Items.Refresh();
                }
            }
            else
            {
                string msg = "Invalid Arc Z value. Please enter a valid number.";
                AppLogger.Log(msg, LogLevel.Error);
                MessageBox.Show(msg, "Input Error", MessageBoxButton.OK, MessageBoxImage.Error);
                // Restore to P1's Z or leave as is
                if(_trajectoryInDetailView.ArcPoint1 != null)
                    ArcCenterZTextBox.Text = _trajectoryInDetailView.ArcPoint1.Coordinates.Z.ToString("F3");
                else
                    ArcCenterZTextBox.Text = "0.000"; // Default if P1 is null
            }
        }
    }

    private void ArcCenterZTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateArcCenterZFromTextBox();
    }

    // Removed ArcCenterZTextBox_LostFocus

    private void UpdateCircleCenterZFromTextBox()
    {
        if (_trajectoryInDetailView != null && _trajectoryInDetailView.PrimitiveType == "Circle")
        {
            if (double.TryParse(CircleCenterZTextBox.Text, out double newZ))
            {
                bool changed = false;
                if (_trajectoryInDetailView.CirclePoint1.Coordinates.Z != newZ)
                {
                    _trajectoryInDetailView.CirclePoint1.Coordinates = new DxfPoint(
                        _trajectoryInDetailView.CirclePoint1.Coordinates.X,
                        _trajectoryInDetailView.CirclePoint1.Coordinates.Y, newZ);
                    changed = true;
                }
                if (_trajectoryInDetailView.CirclePoint2.Coordinates.Z != newZ) // Assuming co-planar change
                {
                    _trajectoryInDetailView.CirclePoint2.Coordinates = new DxfPoint(
                        _trajectoryInDetailView.CirclePoint2.Coordinates.X,
                        _trajectoryInDetailView.CirclePoint2.Coordinates.Y, newZ);
                    changed = true;
                }
                if (_trajectoryInDetailView.CirclePoint3.Coordinates.Z != newZ) // Assuming co-planar change
                {
                    _trajectoryInDetailView.CirclePoint3.Coordinates = new DxfPoint(
                        _trajectoryInDetailView.CirclePoint3.Coordinates.X,
                        _trajectoryInDetailView.CirclePoint3.Coordinates.Y, newZ);
                    changed = true;
                }

                if (changed)
                {
                    // Similar to Arc, logging the fact that Circle points Z were set to newZ.
                    AppLogger.Log($"Trajectory '{_trajectoryInDetailView.ToString()}' Circle points Z (P1, P2, P3) set to {newZ:F3} in pass '{_currentConfiguration.SprayPasses[_currentConfiguration.CurrentPassIndex].PassName}'.");
                    isConfigurationDirty = true;
                    PopulateTrajectoryPoints(_trajectoryInDetailView); // Regenerate points with new Z
                    CurrentPassTrajectoriesListBox.Items.Refresh(); // Refresh if Z might be part of ToString() or if Points property affects display
                }
            }
            else
            {
                string msg = "Invalid Circle Z value. Please enter a valid number.";
                AppLogger.Log(msg, LogLevel.Error);
                MessageBox.Show(msg, "Input Error", MessageBoxButton.OK, MessageBoxImage.Error);
                // Restore to P1's Z
                CircleCenterZTextBox.Text = _trajectoryInDetailView.CirclePoint1.Coordinates.Z.ToString("F3");
            }
        }
    }

    private void CircleCenterZTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateCircleCenterZFromTextBox();
    }

    // Removed CircleCenterZTextBox_LostFocus

    private void PolygonZTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (PolygonZTextBox.Tag is Trajectory selectedTrajectory && selectedTrajectory.PrimitiveType == "Polygon")
        {
            if (double.TryParse(PolygonZTextBox.Text, out double newZ))
            {
                if (selectedTrajectory.PolygonZ != newZ)
                {
                    double oldZ = selectedTrajectory.PolygonZ;
                    selectedTrajectory.PolygonZ = newZ;

                    // Update the Z coordinate of all points in the polygon
                    for (int i = 0; i < selectedTrajectory.Points.Count; i++)
                    {
                        selectedTrajectory.Points[i] = new Point3D(selectedTrajectory.Points[i].X, selectedTrajectory.Points[i].Y, newZ);
                    }

                    AppLogger.Log($"Trajectory '{selectedTrajectory.ToString()}' Polygon Z changed from {oldZ:F3} to {newZ:F3} in pass '{_currentConfiguration.SprayPasses[_currentConfiguration.CurrentPassIndex].PassName}'.");
                    isConfigurationDirty = true;
                    CurrentPassTrajectoriesListBox.Items.Refresh();
                    PolygonVerticesListBox.ItemsSource = selectedTrajectory.Points;
                    PolygonVerticesListBox.Items.Refresh();
                }
            }
            else
            {
                string msg = "Invalid Polygon Z value. Please enter a valid number.";
                AppLogger.Log(msg, LogLevel.Error);
                MessageBox.Show(msg, "Input Error", MessageBoxButton.OK, MessageBoxImage.Error);
                PolygonZTextBox.Text = selectedTrajectory.PolygonZ.ToString("F3");
            }
        }
    }

        /// <summary>
        /// Handles the Closing event of the window. Ensures Modbus connection is disconnected.
        /// </summary>
        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            AppLogger.Log("Application closing."); // Log application close
            _modbusService.Disconnect(); // Clean up Modbus connection.
        }

        /// <summary>
        /// Handles the SizeChanged event of the CadCanvas.
        /// Calls PerformFitToView to rescale and center the DXF content when the canvas size changes.
        /// </summary>
        private void CadCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            Debug.WriteLine($"[DEBUG] CadCanvas_SizeChanged: NewSize=({e.NewSize.Width}, {e.NewSize.Height}), DXF Loaded={_currentDxfDocument != null}");
            // Check if the canvas has a valid size and if a DXF document is loaded
            if (e.NewSize.Width > 0 && e.NewSize.Height > 0 && _currentDxfDocument != null)
            {
                PerformFitToView();
            }
        }

        /// <summary>
        /// Handles the Click event of the "Load DXF" button.
        /// Prompts the user to select a DXF file, loads it using <see cref="CadService"/>,
        /// processes its entities for display, and fits the view to the loaded drawing.
        /// </summary>
        private void DrawDxfEntities()
        {
            if (_currentDxfDocument == null)
            {
                AppLogger.Log("DrawDxfEntities: No DXF document loaded.", LogLevel.Warning);
                return;
            }

            AppLogger.Log("=== Drawing DXF Entities ===", LogLevel.Info);
            foreach (var entity in _currentDxfDocument.Entities)
            {
                System.Windows.Shapes.Shape? shape = null;

                if (entity is DxfLine line)
                {
                    var wpfLine = new System.Windows.Shapes.Line
                    {
                        X1 = line.P1.X,
                        Y1 = line.P1.Y,
                        X2 = line.P2.X,
                        Y2 = line.P2.Y,
                        Stroke = DefaultStrokeBrush,
                        StrokeThickness = DefaultStrokeThickness
                    };
                    shape = wpfLine;
                    AppLogger.Log($"Drawing Line: ({line.P1.X:F2}, {line.P1.Y:F2}) to ({line.P2.X:F2}, {line.P2.Y:F2})", LogLevel.Info);
                }
                else if (entity is DxfCircle circle)
                {
                    var wpfEllipse = new System.Windows.Shapes.Ellipse
                    {
                        Width = circle.Radius * 2,
                        Height = circle.Radius * 2,
                        Stroke = DefaultStrokeBrush,
                        StrokeThickness = DefaultStrokeThickness,
                        Fill = null
                    };
                    Canvas.SetLeft(wpfEllipse, circle.Center.X - circle.Radius);
                    Canvas.SetTop(wpfEllipse, circle.Center.Y - circle.Radius);
                    shape = wpfEllipse;
                    AppLogger.Log($"Drawing Circle: Center({circle.Center.X:F2}, {circle.Center.Y:F2}), Radius={circle.Radius:F2}", LogLevel.Info);
                }
                else if (entity is DxfArc arc)
                {
                    var pathGeometry = new PathGeometry();
                    var pathFigure = new PathFigure();
                    
                    // Convert angles to radians
                    double startAngle = arc.StartAngle * Math.PI / 180.0;
                    double endAngle = arc.EndAngle * Math.PI / 180.0;
                    
                    // Calculate start point
                    double startX = arc.Center.X + arc.Radius * Math.Cos(startAngle);
                    double startY = arc.Center.Y + arc.Radius * Math.Sin(startAngle);
                    pathFigure.StartPoint = new Point(startX, startY);

                    // Create the arc segment
                    var arcSegment = new ArcSegment
                    {
                        Point = new Point(
                            arc.Center.X + arc.Radius * Math.Cos(endAngle),
                            arc.Center.Y + arc.Radius * Math.Sin(endAngle)),
                        Size = new Size(arc.Radius, arc.Radius),
                        IsLargeArc = Math.Abs(endAngle - startAngle) > Math.PI,
                        SweepDirection = endAngle > startAngle ? SweepDirection.Clockwise : SweepDirection.Counterclockwise
                    };

                    pathFigure.Segments.Add(arcSegment);
                    pathGeometry.Figures.Add(pathFigure);

                    var path = new System.Windows.Shapes.Path
                    {
                        Data = pathGeometry,
                        Stroke = DefaultStrokeBrush,
                        StrokeThickness = DefaultStrokeThickness
                    };
                    shape = path;
                    AppLogger.Log($"Drawing Arc: Center({arc.Center.X:F2}, {arc.Center.Y:F2}), Radius={arc.Radius:F2}, Angles={arc.StartAngle:F2} to {arc.EndAngle:F2}", LogLevel.Info);
                }
                else if (entity is DxfLwPolyline polyline)
                {
                    var points = new PointCollection();
                    foreach (var vertex in polyline.Vertices)
                    {
                        points.Add(new Point(vertex.X, vertex.Y));
                    }

                    if (polyline.IsClosed && points.Count > 0)
                    {
                        points.Add(points[0]); // Close the polyline by adding the first point again
                    }

                    var wpfPolyline = new System.Windows.Shapes.Polyline
                    {
                        Points = points,
                        Stroke = DefaultStrokeBrush,
                        StrokeThickness = DefaultStrokeThickness
                    };
                    shape = wpfPolyline;
                    AppLogger.Log($"Drawing Polyline: {polyline.Vertices.Count} vertices, Closed={polyline.IsClosed}", LogLevel.Info);
                }

                if (shape != null)
                {
                    // Generate a unique identifier for the entity
                    string entityId = Guid.NewGuid().ToString();
                    shape.Tag = entityId;
                    shape.MouseLeftButtonDown += OnCadEntityClicked;
                    CadCanvas.Children.Add(shape);
                    _wpfShapeToDxfEntityMap[shape] = entity;
                    _dxfEntityHandleMap[entityId] = entity;
                }
            }

            AppLogger.Log($"Total entities drawn: {CadCanvas.Children.Count}", LogLevel.Info);
        }

        private void LoadDxfButton_Click(object sender, RoutedEventArgs e)
        {
            if (!PromptAndTrySaveChanges())
            {
                return; // User cancelled or save failed
            }

            try
            {
                var openFileDialog = new OpenFileDialog
                {
                    Filter = "DXF files (*.dxf)|*.dxf|All files (*.*)|*.*",
                    FilterIndex = 1
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    // Reset all selections and configurations
                    _selectedDxfEntities.Clear();
                    _wpfShapeToDxfEntityMap.Clear();
                    _dxfEntityHandleMap.Clear();
                    _trajectoryPreviewPolylines.Clear();
                    _currentConfiguration = new Models.Configuration();
                    ProductNameTextBox.Text = $"Product_{DateTime.Now:yyyyMMddHHmmss}";
                    _currentConfiguration.SprayPasses.Add(new SprayPass { PassName = "Pass 1" });
                    _currentConfiguration.CurrentPassIndex = 0;
                    SprayPassesListBox.ItemsSource = _currentConfiguration.SprayPasses;
                    SprayPassesListBox.SelectedIndex = 0;
                    CurrentPassTrajectoriesListBox.ItemsSource = null;
                    CadCanvas.Children.Clear();


                    _currentDxfFilePath = openFileDialog.FileName;
                    _currentDxfDocument = DxfFile.Load(_currentDxfFilePath);

                    // Log loaded entities
                    AppLogger.Log("=== Loaded DXF Entities ===", LogLevel.Info);
                    foreach (var entity in _currentDxfDocument.Entities)
                    {
                        if (entity is DxfLine line)
                        {
                            AppLogger.Log($"Line: Start({line.P1.X:F2}, {line.P1.Y:F2}) to End({line.P2.X:F2}, {line.P2.Y:F2})", LogLevel.Info);
                        }
                        else if (entity is DxfCircle circle)
                        {
                            AppLogger.Log($"Circle: Center({circle.Center.X:F2}, {circle.Center.Y:F2}), Radius={circle.Radius:F2}", LogLevel.Info);
                        }
                        else if (entity is DxfLwPolyline polyline)
                        {
                            AppLogger.Log($"Polyline: {polyline.Vertices.Count} vertices:", LogLevel.Info);
                            foreach (var vertex in polyline.Vertices)
                            {
                                AppLogger.Log($"  - Point({vertex.X:F2}, {vertex.Y:F2})", LogLevel.Info);
                            }
                        }
                    }

                    // Calculate and log bounding box
                    _dxfBoundingBox = GetDxfBoundingBox(_currentDxfDocument);
                    AppLogger.Log($"=== DXF Bounding Box ===", LogLevel.Info);
                    AppLogger.Log($"X: {_dxfBoundingBox.X:F2} to {_dxfBoundingBox.X + _dxfBoundingBox.Width:F2}", LogLevel.Info);
                    AppLogger.Log($"Y: {_dxfBoundingBox.Y:F2} to {_dxfBoundingBox.Y + _dxfBoundingBox.Height:F2}", LogLevel.Info);
                    AppLogger.Log($"Width: {_dxfBoundingBox.Width:F2}, Height: {_dxfBoundingBox.Height:F2}", LogLevel.Info);

                    // Clear existing content
                    CadCanvas.Children.Clear();
                    _wpfShapeToDxfEntityMap.Clear();
                    _dxfEntityHandleMap.Clear();

                    // Draw entities and update UI
                    DrawDxfEntities();
                        PerformFitToView();
                    
                    // Log canvas information
                    AppLogger.Log($"=== Canvas Information ===", LogLevel.Info);
                    AppLogger.Log($"Canvas Size: {CadCanvas.ActualWidth:F2} x {CadCanvas.ActualHeight:F2}", LogLevel.Info);
                    AppLogger.Log($"Final Scale: ({_scaleTransform.ScaleX:F2}, {_scaleTransform.ScaleY:F2})", LogLevel.Info);
                    AppLogger.Log($"Final Translation: ({_translateTransform.X:F2}, {_translateTransform.Y:F2})", LogLevel.Info);

                    // Update status
                    StatusTextBlock.Text = $"Loaded: {System.IO.Path.GetFileName(_currentDxfFilePath)}";
                    isConfigurationDirty = true;
                }
            }
            catch (Exception ex)
            {
                HandleError(ex, "loading DXF file");
            }
        }

        private void PerformFitToView()
        {
            if (_dxfBoundingBox == null || CadCanvas == null) return;

            double canvasWidth = CadCanvas.ActualWidth;
            double canvasHeight = CadCanvas.ActualHeight;

            AppLogger.Log("PerformFitToView: Method started.", LogLevel.Info);
            AppLogger.Log($"PerformFitToView: Canvas ActualWidth={canvasWidth:F2}, ActualHeight={canvasHeight:F2}", LogLevel.Debug);
            AppLogger.Log($"PerformFitToView: DXF BoundingBox Input (X,Y,W,H): ({_dxfBoundingBox.X:F2}, {_dxfBoundingBox.Y:F2}, {_dxfBoundingBox.Width:F2}, {_dxfBoundingBox.Height:F2})", LogLevel.Debug);

            double scale;
            // Calculate scale to fit
            if (_dxfBoundingBox.Width <= 1e-6 || _dxfBoundingBox.Height <= 1e-6) // Check for degenerate bounding box
            {
                AppLogger.Log("PerformFitToView: Bounding box width or height is zero or very small. Setting scale to 1.0.", LogLevel.Warning);
                scale = 1.0; // Default scale for degenerate box (e.g. single point)
            }
            else
            {
                double scaleX = canvasWidth / _dxfBoundingBox.Width;
                double scaleY = canvasHeight / _dxfBoundingBox.Height;
                scale = Math.Min(scaleX, scaleY);
                AppLogger.Log($"PerformFitToView: Scale Calculation - scaleX_raw={scaleX:F4}, scaleY_raw={scaleY:F4}, chosen_scale={scale:F4}", LogLevel.Debug);
            }
             // Prevent scale from being excessively small or zero, which can cause issues.
            if (scale < 0.000001)
            {
                AppLogger.Log($"PerformFitToView: Calculated scale {scale:F6} is very small. Clamping to 0.000001.", LogLevel.Warning);
                scale = 0.000001;
            }


            double scaledContentWidth = _dxfBoundingBox.Width * scale;
            double scaledContentHeight = _dxfBoundingBox.Height * scale;
            AppLogger.Log($"PerformFitToView: Scaled DXF content size (W,H): ({scaledContentWidth:F2}, {scaledContentHeight:F2})", LogLevel.Debug);

            // Calculate translation to center
            // _dxfBoundingBox.X is minX_dxf
            // _dxfBoundingBox.Y is minY_dxf
            double translateX = (canvasWidth - scaledContentWidth) / 2.0 - (_dxfBoundingBox.X * scale);
            
            double canvasCenterY = canvasHeight / 2.0;
            // contentCenterY_dxf is the Y-coordinate of the center of the DXF bounding box, in DXF Y-up coordinates.
            double contentCenterY_dxf = _dxfBoundingBox.Y + (_dxfBoundingBox.Height / 2.0);
            // translateY is calculated so that contentCenterY_dxf, when transformed, maps to canvasCenterY.
            // y_screen = y_dxf * (-scale) + translateY
            // canvasCenterY = contentCenterY_dxf * (-scale) + translateY
            // => translateY = canvasCenterY + (contentCenterY_dxf * scale)
            double translateY = canvasCenterY + (contentCenterY_dxf * scale);

            AppLogger.Log($"PerformFitToView: Translation Calculation - canvasCenterY={canvasCenterY:F2}, contentCenterY_dxf={contentCenterY_dxf:F2}", LogLevel.Debug);
            AppLogger.Log($"PerformFitToView: Translation Calculation - translateX={translateX:F2}, translateY={translateY:F2}", LogLevel.Debug);

            // Create new transform objects and assign them to ensure changes are robustly applied.
            // This addresses the issue where direct property modifications on existing _scaleTransform
            // and _translateTransform were not reflected in their logged state.
            _scaleTransform = new ScaleTransform(scale, -scale); // Apply calculated scale; -scale for Y-axis inversion.
            _translateTransform = new TranslateTransform(translateX, translateY); // Apply calculated translation.

            // Recreate the TransformGroup and assign it to the canvas.
            // This ensures the CadCanvas uses these new transform instances,
            // and that _scaleTransform/_translateTransform fields point to these live instances.
            _transformGroup = new TransformGroup();
            _transformGroup.Children.Add(_scaleTransform);    // Add the new scale transform.
            _transformGroup.Children.Add(_translateTransform); // Add the new translate transform.

            CadCanvas.RenderTransform = _transformGroup; // Crucially re-assign the RenderTransform.

            AppLogger.Log($"PerformFitToView: Applied new transforms. Effective Scale=({_scaleTransform.ScaleX:F4}, {_scaleTransform.ScaleY:F4}), Effective Translate=({_translateTransform.X:F2}, {_translateTransform.Y:F2})", LogLevel.Info);

            // Log the state of the actual transform objects now part of CadCanvas.RenderTransform
            AppLogger.Log("=== Final Transform State (Post Re-Assignment and Field Update) ===", LogLevel.Info);
            AppLogger.Log($"Field _scaleTransform State: ({_scaleTransform.ScaleX:F4}, {_scaleTransform.ScaleY:F4})", LogLevel.Info);
            AppLogger.Log($"Field _translateTransform State: ({_translateTransform.X:F2}, {_translateTransform.Y:F2})", LogLevel.Info);

            // Calculate and log viewport bounds in DXF coordinates using the now-updated _scaleTransform and _translateTransform fields
            double absScaleX = Math.Abs(_scaleTransform.ScaleX);
            if (absScaleX < 1e-9) absScaleX = 1e-9;

            double effectiveScaleYForViewport = _scaleTransform.ScaleY;
            if (Math.Abs(effectiveScaleYForViewport) < 1e-9)
            {
                effectiveScaleYForViewport = (effectiveScaleYForViewport < 0) ? -1e-9 : 1e-9; // Preserve sign if it was meant to be negative
            }

            double viewportLeft = -_translateTransform.X / absScaleX;
            double viewportRight = (canvasWidth - _translateTransform.X) / absScaleX;

            double viewportDxfTop = (0 - _translateTransform.Y) / effectiveScaleYForViewport;
            double viewportDxfBottom = (canvasHeight - _translateTransform.Y) / effectiveScaleYForViewport;

            AppLogger.Log("=== Viewport in DXF Coordinates (approximate, based on canvas edges) ===", LogLevel.Info);
            AppLogger.Log($"X range: {Math.Min(viewportLeft, viewportRight):F2} to {Math.Max(viewportLeft, viewportRight):F2}", LogLevel.Info);
            AppLogger.Log($"Y range: {Math.Min(viewportDxfTop, viewportDxfBottom):F2} to {Math.Max(viewportDxfTop, viewportDxfBottom):F2}", LogLevel.Info);

            AppLogger.Log("=== Canvas Information (End of PerformFitToView) ===", LogLevel.Info);
            AppLogger.Log($"Canvas Size: {canvasWidth:F2} x {canvasHeight:F2}", LogLevel.Info);
        }

        /// <summary>
        /// Handles the click event on a CAD entity shape, toggling its selection state.
        /// </summary>
        private void OnCadEntityClicked(object sender, MouseButtonEventArgs e)
        {
            Trace.WriteLine("++++ OnCadEntityClicked Fired ++++");
            Trace.Flush();
            Debug.WriteLine($"[DEBUG] OnCadEntityClicked: Sender is {sender?.GetType().Name}");
            Trajectory trajectoryToSelect = null; // Declare at wider scope

            // Detailed check for the main condition
            if (sender is System.Windows.Shapes.Shape clickedShape && _wpfShapeToDxfEntityMap.TryGetValue(clickedShape, out DxfEntity? dxfEntity))
            {
                // keyExists is implicitly true if TryGetValue succeeds.
                // dxfEntity will be non-null if TryGetValue returns true.
                Trace.WriteLine($"  -- Checking sender type: {sender?.GetType().Name ?? "null"}, IsShape: true, Map contains key: true");
                Trace.Flush();
                Trace.WriteLine("  -- Condition (sender is Shape AND _wpfShapeToDxfEntityMap contains key) MET");
                Trace.Flush();

                // var dxfEntity = _wpfShapeToDxfEntityMap[clickedShape]; // No longer needed due to TryGetValue
                Trace.WriteLine($"  -- Retrieved dxfEntity: {dxfEntity?.GetType().Name ?? "null"}");
                Debug.WriteLine($"[DEBUG] OnCadEntityClicked: Retrieved DxfEntity: {dxfEntity?.GetType().Name}");
                Trace.Flush();

                Point clickPosCanvas = e.GetPosition(CadCanvas);
                Debug.WriteLine($"[DEBUG] OnCadEntityClicked: Click position on Canvas = {clickPosCanvas}");
                Point clickPosDxf = _transformGroup.Inverse.Transform(clickPosCanvas);
                Debug.WriteLine($"[DEBUG] OnCadEntityClicked: Click position transformed to DXF Coords = {clickPosDxf}");

                Rect entityDxfBounds = GetDxfEntityRect(dxfEntity); // Helper method to be added
                Debug.WriteLine($"[DEBUG] OnCadEntityClicked: DXF Entity Bounds = {entityDxfBounds}");
                if (entityDxfBounds != Rect.Empty)
                {
                    Debug.WriteLine($"[DEBUG] OnCadEntityClicked: Does transformed click fall within entity bounds? {entityDxfBounds.Contains(clickPosDxf)}");
                }
                
                // Logic for adding/removing from current spray pass's trajectories
                Trace.WriteLine($"  -- Checking current pass index: {_currentConfiguration.CurrentPassIndex}, SprayPasses count: {_currentConfiguration.SprayPasses?.Count ?? 0}");
                Trace.Flush();
                if (_currentConfiguration.CurrentPassIndex < 0 || _currentConfiguration.CurrentPassIndex >= _currentConfiguration.SprayPasses.Count)
                {
                    Trace.WriteLine("  -- Current pass index invalid, returning.");
                    Trace.Flush();
                    string msg = "Please select or create a spray pass first.";
                    AppLogger.Log(msg, LogLevel.Info); // Info, as it's guidance
                    MessageBox.Show(msg, "No Active Pass", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                var currentPass = _currentConfiguration.SprayPasses[_currentConfiguration.CurrentPassIndex];

                var existingTrajectory = currentPass.Trajectories.FirstOrDefault(t => t.OriginalDxfEntity == dxfEntity);

                if (existingTrajectory != null)
                {
                    // Entity is already part of the current pass. Mark it for selection.
                    Trace.WriteLine("  -- Existing trajectory found. Selecting it.");
                    Trace.Flush();
                    trajectoryToSelect = existingTrajectory;
                    // Do NOT remove it from currentPass.Trajectories.
                    // Do NOT set isConfigurationDirty = true just for re-selecting.
                }
                else
                {
                    // Select - Add to current pass
                    Trace.WriteLine("  -- No existing trajectory. Creating and adding new one.");
                    Trace.Flush();
                    var newTrajectory = new Trajectory
                    {
                        OriginalDxfEntity = dxfEntity,
                        EntityType = dxfEntity.GetType().Name, // General type, can be overridden by PrimitiveType
                        IsReversed = false // Default, can be changed by specific logic below or UI
                    };

                    switch (dxfEntity)
                    {
                        case DxfLwPolyline polyline:
                            trajectoryToSelect = CreatePolygonTrajectoryFromPolyline(polyline);
                            currentPass.Trajectories.Add(trajectoryToSelect);
                            break;
                        case DxfLine line:
                            newTrajectory.PrimitiveType = "Line";
                            double p1DistSq = line.P1.X * line.P1.X + line.P1.Y * line.P1.Y + line.P1.Z * line.P1.Z;
                            double p2DistSq = line.P2.X * line.P2.X + line.P2.Y * line.P2.Y + line.P2.Z * line.P2.Z;
                            if (p1DistSq <= p2DistSq)
                            {
                                newTrajectory.LineStartPoint = line.P1;
                                newTrajectory.LineEndPoint = line.P2;
                            }
                            else
                            {
                                newTrajectory.LineStartPoint = line.P2;
                                newTrajectory.LineEndPoint = line.P1;
                            }
                            currentPass.Trajectories.Add(newTrajectory);
                            trajectoryToSelect = newTrajectory;
                            break;
                        case DxfArc arc:
                            newTrajectory.PrimitiveType = "Arc";
                            // Calculate P1, P2 (mid), P3 for the arc using DxfArc properties
                            double startRad = arc.StartAngle * Math.PI / 180.0;
                            currentPass.Trajectories.Add(newTrajectory);
                            trajectoryToSelect = newTrajectory;
                            double endRad = arc.EndAngle * Math.PI / 180.0;

                            // P1 (Start Point)
                            newTrajectory.ArcPoint1.Coordinates = new DxfPoint(
                                arc.Center.X + arc.Radius * Math.Cos(startRad),
                                arc.Center.Y + arc.Radius * Math.Sin(startRad),
                                arc.Center.Z // Assuming arc is planar, Z is from center
                            );

                            // P3 (End Point)
                            newTrajectory.ArcPoint3.Coordinates = new DxfPoint(
                                arc.Center.X + arc.Radius * Math.Cos(endRad),
                                arc.Center.Y + arc.Radius * Math.Sin(endRad),
                                arc.Center.Z
                            );

                            // P2 (Mid Point Angle)
                            // Ensure angles are handled correctly for sweep (e.g. Start=350, End=10)
                            if (endRad < startRad) // Adjust if end angle is "smaller" due to wrap around
                            {
                                endRad += 2 * Math.PI;
                            }
                            double midRad = (startRad + endRad) / 2.0;
                            newTrajectory.ArcPoint2.Coordinates = new DxfPoint(
                                arc.Center.X + arc.Radius * Math.Cos(midRad),
                                arc.Center.Y + arc.Radius * Math.Sin(midRad),
                                arc.Center.Z
                            );
                            // Rx, Ry, Rz for ArcPoint1, ArcPoint2, ArcPoint3 will default to 0.0
                            break;
                        case DxfCircle circle:
                            newTrajectory.PrimitiveType = "Circle";
                            currentPass.Trajectories.Add(newTrajectory);
                            trajectoryToSelect = newTrajectory;
                            // newTrajectory.CircleCenter = circle.Center; // Replaced by 3 points
                            // newTrajectory.CircleRadius = circle.Radius; // Replaced by 3 points
                            // newTrajectory.CircleNormal = circle.Normal; // Will be derived from 3 points or stored if needed

                            DxfVector normal = circle.Normal.Normalize();
                            DxfPoint center = circle.Center;
                            double radius = circle.Radius;

                            // Final corrected logic: Define local axes based on world coordinates to match user expectation
                            // The local X axis is now always aligned with the world X-Y plane's X direction, unless the circle is vertical.
                            DxfVector localXAxis;
                            if (Math.Abs(normal.Z) > 0.999) // Circle is mostly flat on XY plane
                            {
                                localXAxis = DxfVector.XAxis;
                            }
                            else // Tilted or vertical circle
                            {
                                localXAxis = (DxfVector.ZAxis).Cross(normal).Normalize();
                            }
                            DxfVector localYAxis = normal.Cross(localXAxis).Normalize();

                            // The angle for the bottom-left point is consistently -135 degrees (225 deg) in this local system
                            double angleP1_rad = -135.0 * Math.PI / 180.0;

                            // Calculate the offset vector for the first point
                            DxfVector p1_offset_vec = (localXAxis * (radius * Math.Cos(angleP1_rad))) + (localYAxis * (radius * Math.Sin(angleP1_rad)));
                            newTrajectory.CirclePoint1.Coordinates = center + p1_offset_vec;

                            // Calculate P2 and P3 by rotating from the first point's angle
                            double angleP2_rad = angleP1_rad + (120.0 * Math.PI / 180.0);
                            double angleP3_rad = angleP1_rad + (240.0 * Math.PI / 180.0);

                            DxfVector p2_offset_vec = (localXAxis * (radius * Math.Cos(angleP2_rad))) + (localYAxis * (radius * Math.Sin(angleP2_rad)));
                            DxfVector p3_offset_vec = (localXAxis * (radius * Math.Cos(angleP3_rad))) + (localYAxis * (radius * Math.Sin(angleP3_rad)));

                            newTrajectory.CirclePoint2.Coordinates = center + p2_offset_vec;
                            newTrajectory.CirclePoint3.Coordinates = center + p3_offset_vec;


                            // Store original parameters as well
                            newTrajectory.OriginalCircleCenter = center;
                            newTrajectory.OriginalCircleRadius = radius;
                            newTrajectory.OriginalCircleNormal = normal;

                            // Ensure Z coordinates are consistent if derived from a 2D circle
                            // For a true 3D circle, the Z values from above calculation are correct.
                            // If the original DxfCircle was planar with Z=c.Z, then these points will share that Z.
                            // No explicit Z adjustment needed here if calculations are correct.

                            break;
                        default:
                            newTrajectory.PrimitiveType = dxfEntity.GetType().Name;
                            break;
                    }
                    if (trajectoryToSelect != null)
                    {
                        PopulateTrajectoryPoints(trajectoryToSelect);
                        trajectoryToSelect.Runtime = TrajectoryUtils.CalculateMinRuntime(trajectoryToSelect); // Set default runtime
                        AppLogger.Log($"Trajectory added to pass '{currentPass.PassName}': Type '{trajectoryToSelect.PrimitiveType}', EntityHandle '{trajectoryToSelect.OriginalEntityHandle}'.", LogLevel.Info);
                        isConfigurationDirty = true;
                    }
                }

                RefreshCurrentPassTrajectoriesListBox();
                CurrentPassTrajectoriesListBox.Items.Refresh();

                if (trajectoryToSelect != null)
                {
                    CurrentPassTrajectoriesListBox.SelectedItem = trajectoryToSelect;
                    Trace.WriteLine($"  -- Explicitly set CurrentPassTrajectoriesListBox.SelectedItem to: {trajectoryToSelect.ToString()}");
                }
                else if (existingTrajectory != null) // It was a deselection, and existingTrajectory was the one removed
                {
                    Trace.WriteLine($"  -- Trajectory deselected. CurrentPassTrajectoriesListBox.SelectedItem is now: {CurrentPassTrajectoriesListBox.SelectedItem?.ToString() ?? "null"}");
                }
                Trace.Flush();

                RefreshCadCanvasHighlights();
                StatusTextBlock.Text = $"Selected {currentPass.Trajectories.Count} trajectories in {currentPass.PassName}.";

                Trace.WriteLine("  -- About to call UpdateDirectionIndicator from OnCadEntityClicked");
                Trace.Flush();
                UpdateDirectionIndicator(); // Selection changed by clicking CAD entity
                UpdateOrderNumberLabels();
            }
            else
            {
                Trace.WriteLine("  -- Condition (sender is Shape AND _wpfShapeToDxfEntityMap contains key) FAILED");
                Trace.Flush();
                // It's possible that the click was on the canvas background or another UI element not part of the DXF drawing.
                // No return needed here unless this path should explicitly not do anything further.
            }
        }

        /// <summary>
        /// Updates the trajectory preview by drawing polylines for selected entities.
        /// </summary>
        private void UpdateTrajectoryPreview()
        {
            // Clear existing trajectory previews
            foreach (var polyline in _trajectoryPreviewPolylines)
            {
                CadCanvas.Children.Remove(polyline);
            }
            _trajectoryPreviewPolylines.Clear();

            // Generate preview for selected entities
            foreach (var entity in _selectedDxfEntities)
            {
                List<System.Windows.Point> points = new List<System.Windows.Point>();
                
                switch (entity)
                {
                    case DxfLine line:
                        points = _cadService.ConvertLineToPoints(line);
                        break;
                    case DxfArc arc:
                        points = _cadService.ConvertArcToPoints(arc, TrajectoryPointResolutionAngle);
                        break;
                    case DxfCircle circle:
                        points = _cadService.ConvertCircleToPoints(circle, TrajectoryPointResolutionAngle);
                        break;
                }

                if (points.Count > 1)
                {
                    var polyline = new System.Windows.Shapes.Polyline
                    {
                        Points = new System.Windows.Media.PointCollection(points),
                        Stroke = Brushes.Red,
                        StrokeThickness = SelectedStrokeThickness,
                        StrokeDashArray = new System.Windows.Media.DoubleCollection { 5, 3 },
                        Tag = TrajectoryPreviewTag
                    };
                    
                    _trajectoryPreviewPolylines.Add(polyline);
                    CadCanvas.Children.Add(polyline);
                }
            }
        }

        /// <summary>
        /// Creates a configuration object from the current application state.
        /// </summary>
        private Models.Configuration CreateConfigurationFromCurrentState(bool forSaving = false)
        {
            // This method now primarily ensures the ProductName is up-to-date in _currentConfiguration.
            // The actual SprayPasses and Trajectories are modified directly by UI interactions.
            _currentConfiguration.ProductName = ProductNameTextBox.Text;

            // The old logic of creating a single trajectory from _selectedDxfEntities is removed.
            // That list might be empty or used differently now.
            // If there's a need to explicitly "commit" selections from a temporary list to the current pass,
            // that logic would go here or be part of the selection process itself.

            // For now, we assume _currentConfiguration is the source of truth and is being updated live.
            return _currentConfiguration;
        }
        private void SaveConfigButton_Click(object sender, RoutedEventArgs e)
        {
            bool success = PerformSaveOperation(); // PerformSaveOperation will handle dialogs and actual saving
            if (success)
            {
                isConfigurationDirty = false;
                // StatusTextBlock.Text is likely updated within PerformSaveOperation or can be set here
                // For example: StatusTextBlock.Text = $"Configuration saved to {Path.GetFileName(_currentLoadedConfigPath)}";
                // This depends on how much PerformSaveOperation handles.
                // For now, just focus on calling it and setting the dirty flag.
            }
            else
            {
                // Optional: Update status if save failed or was cancelled within PerformSaveOperation
                // StatusTextBlock.Text = "Save configuration cancelled or failed.";
            }
        }
        private void LoadConfigButton_Click(object sender, RoutedEventArgs e)
        {
            bool canProceed = PromptAndTrySaveChanges();
            if (!canProceed)
            {
                StatusTextBlock.Text = "Load configuration cancelled due to unsaved changes."; // Optional feedback
                return;
            }

            OpenFileDialog openFileDialog = new OpenFileDialog {
                Filter = "Config files (*.json)|*.json|All files (*.*)|*.*",
                Title = "Load Configuration File"
            };
            // Set initial directory (similar to LoadDxfButton_Click for consistency)
            string initialDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "RobTeachProject", "RobTeach", "Configurations"));
            if (!Directory.Exists(initialDir)) initialDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Configurations"); // Fallback
            openFileDialog.InitialDirectory = initialDir;

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    _currentConfiguration = _configService.LoadConfiguration(openFileDialog.FileName);
                    if (_currentConfiguration == null)
                    {
                        string msg = "Failed to load configuration file. The file might be corrupt or not a valid configuration.";
                        AppLogger.Log($"{msg} Path: {openFileDialog.FileName}", LogLevel.Error);
                        MessageBox.Show(msg, "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        StatusTextBlock.Text = "Error: Failed to deserialize configuration.";
                        // Initialize a default empty configuration to prevent null reference issues later
                        _currentConfiguration = new Models.Configuration { ProductName = $"Product_{DateTime.Now:yyyyMMddHHmmss}" };
                        // No further processing if config is null
                    }
                    else // Configuration loaded successfully
                    {
                        // Debug.WriteLine("[JULES_DEBUG] Configuration loaded. Trajectory order after deserialization:");
                        // if (_currentConfiguration.SprayPasses != null && _currentConfiguration.CurrentPassIndex >= 0 && _currentConfiguration.CurrentPassIndex < _currentConfiguration.SprayPasses.Count)
                        // {
                        //     var currentPass = _currentConfiguration.SprayPasses[_currentConfiguration.CurrentPassIndex];
                        //     if (currentPass != null && currentPass.Trajectories != null)
                        //     {
                        //         for (int i = 0; i < currentPass.Trajectories.Count; i++)
                        //         {
                        //             Debug.WriteLine($"[JULES_DEBUG]   Pass[{_currentConfiguration.CurrentPassIndex}]-Trajectory[{i}]: {currentPass.Trajectories[i].ToString()}");
                        //         }
                        //     }
                        //     else
                        //     {
                        //         Debug.WriteLine($"[JULES_DEBUG]   Current pass ({_currentConfiguration.CurrentPassIndex}) or its trajectories are null.");
                        //     }
                        // }
                        // else
                        // {
                        //     Debug.WriteLine("[JULES_DEBUG]   No spray passes or current pass index is invalid.");
                        // }
                    }

                    ProductNameTextBox.Text = _currentConfiguration.ProductName;
                    if (_currentConfiguration.Language == "中文")
                    {
                        LanguageComboBox.SelectedIndex = 1;
                    }
                    else
                    {
                        LanguageComboBox.SelectedIndex = 0;
                    }
                    UpdateUIText(_currentConfiguration.Language);

                    // Restore Modbus Settings
                    ModbusIpAddressTextBox.Text = _currentConfiguration.ModbusIpAddress;
                    ModbusPortTextBox.Text = _currentConfiguration.ModbusPort.ToString();

                    // Restore Canvas View State
                    if (_currentConfiguration.CanvasState != null)
                    {
                        _scaleTransform.ScaleX = _currentConfiguration.CanvasState.ScaleX;
                        _scaleTransform.ScaleY = _currentConfiguration.CanvasState.ScaleY;
                        _translateTransform.X = _currentConfiguration.CanvasState.TranslateX;
                        _translateTransform.Y = _currentConfiguration.CanvasState.TranslateY;
                    }

                    // Load Embedded DXF Content
                    if (!string.IsNullOrEmpty(_currentConfiguration.DxfFileContent))
                    {
                        // Reset current DXF state
                        CadCanvas.Children.Clear();
                        _wpfShapeToDxfEntityMap.Clear();
                        _trajectoryPreviewPolylines.Clear();
                        _selectedDxfEntities.Clear();
                        _dxfEntityHandleMap.Clear();
                        _currentDxfDocument = null;
                        _dxfBoundingBox = Rect.Empty;
                        _currentDxfFilePath = "(Embedded DXF from project file)";

                        try
                        {
                            using (var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(_currentConfiguration.DxfFileContent)))
                            {
                                _currentDxfDocument = DxfFile.Load(memoryStream);
                            }

                            if (_currentDxfDocument != null)
                            {
                                // Debug.WriteLine($"[JULES_DEBUG] Drawing Shapes: Document has {_currentDxfDocument.Entities.Count()} entities.");
                                List<System.Windows.Shapes.Shape> wpfShapes = _cadService.GetWpfShapesFromDxf(_currentDxfDocument);
                                // Debug.WriteLine($"[JULES_DEBUG] Drawing Shapes: CadService.GetWpfShapesFromDxf returned {wpfShapes.Count} shapes.");
                                int shapeIndex = 0;
                                int entityIndex = 0;
                                foreach(var entity in _currentDxfDocument.Entities)
                                {
                                    // string entityIdentifier = $"EntityType: {entity.GetType().Name}, Handle: {entity.Handle.ToString("X")}"; // Removed: Causes compile error
                                    // Debug.WriteLine($"[JULES_DEBUG] Drawing Shapes: Processing DXF Entity at index {entityIndex} (C# type: {entity.GetType().Name})");
                                    if (shapeIndex < wpfShapes.Count)
                                    {
                                        var wpfShape = wpfShapes[shapeIndex];
                                        if (wpfShape != null)
                                        {
                                            // Debug.WriteLine($"[JULES_DEBUG] Drawing Shapes: WPF Shape for Entity at index {entityIndex} is {wpfShape.GetType().Name}. Adding to canvas and map.");
                                            wpfShape.Stroke = DefaultStrokeBrush;
                                            wpfShape.StrokeThickness = DefaultStrokeThickness;
                                            wpfShape.MouseLeftButtonDown += OnCadEntityClicked;
                                            _wpfShapeToDxfEntityMap[wpfShape] = entity;
                                            CadCanvas.Children.Add(wpfShape);
                                        }
                                        else
                                        {
                                            // Debug.WriteLine($"[JULES_DEBUG] Drawing Shapes: WPF Shape for Entity at index {entityIndex} (C# type: {entity.GetType().Name}) is NULL from CadService.");
                                        }
                                    }
                                    else
                                    {
                                        // Debug.WriteLine($"[JULES_DEBUG] Drawing Shapes: No corresponding WPF shape in list for Entity at index {entityIndex} (C# type: {entity.GetType().Name}). Shape list too short.");
                                    }
                                    shapeIndex++;
                                    entityIndex++;
                                }
                                if (wpfShapes.Count != _currentDxfDocument.Entities.Count()) // CadService now returns list with nulls, so counts should match. This log indicates if not.
                                {
                                     // Debug.WriteLine($"[JULES_DEBUG] Drawing Shapes: WARNING - Entity count ({_currentDxfDocument.Entities.Count()}) and WPF shapes list count ({wpfShapes.Count}) do not match. This is unexpected if CadService pads with nulls.");
                                }
                                _dxfBoundingBox = GetDxfBoundingBox(_currentDxfDocument);
                                PerformFitToView();
                                StatusTextBlock.Text = "Loaded embedded DXF and configuration from project file.";
                            }
                            else // Should ideally be caught by DxfFile.Load exception
                            {
                                StatusTextBlock.Text = "Project file loaded, but embedded DXF content was invalid or empty.";
                                _currentDxfDocument = null; // Ensure it's null
                            }
                        }
                        catch (Exception dxfEx)
                        {
                            StatusTextBlock.Text = "Project file loaded, but failed to load embedded DXF content.";
                            string msg = "Failed to load embedded DXF content from the project file. It might be corrupt.";
                            AppLogger.Log(msg, dxfEx, LogLevel.Error);
                            MessageBox.Show($"{msg}\nError: {dxfEx.Message}", "DXF Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            _currentDxfDocument = null; // Ensure it's null
                             // Clear canvas again in case partial loading happened before error, or if an error occurred in GetWpfShapesFromDxf
                            CadCanvas.Children.Clear();
                            _wpfShapeToDxfEntityMap.Clear();
                        }
                    }
                    else
                    {
                        // No embedded DXF content, clear existing DXF from canvas
                        CadCanvas.Children.Clear();
                        _wpfShapeToDxfEntityMap.Clear();
                        _trajectoryPreviewPolylines.Clear();
                        _selectedDxfEntities.Clear();
                        _dxfEntityHandleMap.Clear();
                        _currentDxfDocument = null;
                        _currentDxfFilePath = null; // No active DXF file path
                        _dxfBoundingBox = Rect.Empty;
                        PerformFitToView(); // Reset view if no DXF
                        StatusTextBlock.Text = "Configuration loaded (no embedded DXF).";
                    }

                    // Initialize Spray Passes from loaded configuration
                    if (_currentConfiguration.SprayPasses == null || !_currentConfiguration.SprayPasses.Any())
                    {
                        // If loaded config has no passes, create a default one.
                        _currentConfiguration.SprayPasses = new List<SprayPass> { new SprayPass { PassName = "Default Pass 1" } };
                        _currentConfiguration.CurrentPassIndex = 0;
                    }
                    else if (_currentConfiguration.CurrentPassIndex < 0 || _currentConfiguration.CurrentPassIndex >= _currentConfiguration.SprayPasses.Count)
                    {
                        // If index is invalid, default to first pass.
                        _currentConfiguration.CurrentPassIndex = _currentConfiguration.SprayPasses.Any() ? 0 : -1;
                    }

                    SprayPassesListBox.ItemsSource = null; // Refresh
                    SprayPassesListBox.ItemsSource = _currentConfiguration.SprayPasses;
                    if (_currentConfiguration.CurrentPassIndex >= 0 && _currentConfiguration.CurrentPassIndex < SprayPassesListBox.Items.Count)
                    {
                         SprayPassesListBox.SelectedIndex = _currentConfiguration.CurrentPassIndex;
                    }
                    else if (SprayPassesListBox.Items.Count > 0)
                    {
                        SprayPassesListBox.SelectedIndex = 0; // Fallback to selecting the first if index is out of sync
                        _currentConfiguration.CurrentPassIndex = 0;
                    }


                    // The old global nozzle checkbox updates are removed.
                    // UpperNozzleOnCheckBox_Changed(null, null); // Removed
                    // LowerNozzleOnCheckBox_Changed(null, null); // Removed

                    RefreshCurrentPassTrajectoriesListBox(); // Update trajectory list for the (newly) current pass

                    // Restore selected trajectory index for the current pass
                    if (_currentConfiguration.SelectedTrajectoryIndexInCurrentPass >= 0 &&
                        _currentConfiguration.SelectedTrajectoryIndexInCurrentPass < CurrentPassTrajectoriesListBox.Items.Count)
                    {
                        CurrentPassTrajectoriesListBox.SelectedIndex = _currentConfiguration.SelectedTrajectoryIndexInCurrentPass;
                    }
                    // else, no valid selection or list is empty, ListBox default behavior (no selection or first item)

                    // Populate points for all trajectories in the loaded configuration
                    if (_currentConfiguration != null && _currentConfiguration.SprayPasses != null)
                    {
                        foreach (var pass in _currentConfiguration.SprayPasses)
                        {
                            if (pass.Trajectories != null)
                            {
                                foreach (var trajectory in pass.Trajectories)
                                {
                                    PopulateTrajectoryPoints(trajectory);
                                }
                            }
                        }
                    }

                    // Populate points for all trajectories in the loaded configuration
                    if (_currentConfiguration != null && _currentConfiguration.SprayPasses != null)
                    {
                        foreach (var pass in _currentConfiguration.SprayPasses)
                        {
                            if (pass.Trajectories != null)
                            {
                                foreach (var trajectory in pass.Trajectories)
                                {
                                    PopulateTrajectoryPoints(trajectory);
                                }
                            }
                        }
                    }

                    // Populate points for all trajectories in the loaded configuration
                    if (_currentConfiguration != null && _currentConfiguration.SprayPasses != null)
                    {
                        // Debug.WriteLine("[JULES_DEBUG] Populating points for loaded trajectories.");
                        foreach (var pass in _currentConfiguration.SprayPasses)
                        {
                            if (pass.Trajectories != null)
                            {
                                foreach (var trajectory in pass.Trajectories)
                                {
                                    PopulateTrajectoryPoints(trajectory);
                                    // Debug.WriteLine($"[JULES_DEBUG]   Populated points for: {trajectory.ToString()} - Point Count: {trajectory.Points.Count}");
                                }
                            }
                        }
                    }

                    // Reconcile DxfEntity instances if DXF was loaded from embedded content
                    if (!string.IsNullOrEmpty(_currentConfiguration.DxfFileContent) && _currentDxfDocument != null)
                    {
                        ReconcileTrajectoryEntities(_currentConfiguration, _currentDxfDocument);
                    }

                    UpdateSelectedTrajectoryDetailUI(); // Renamed: Update nozzle UI for potentially selected trajectory
                    RefreshCadCanvasHighlights(); // Update canvas highlights for the loaded pass
                    UpdateDirectionIndicator(); // Config loaded, selection might have changed

                    // Debug.WriteLine("[JULES_DEBUG] Before calling UpdateOrderNumberLabels in LoadConfigButton_Click:");
                    // if (_currentConfiguration != null && _currentConfiguration.SprayPasses != null && _currentConfiguration.CurrentPassIndex >= 0 && _currentConfiguration.CurrentPassIndex < _currentConfiguration.SprayPasses.Count)
                    // {
                    //     var currentPass = _currentConfiguration.SprayPasses[_currentConfiguration.CurrentPassIndex];
                    //     if (currentPass != null && currentPass.Trajectories != null)
                    //     {
                    //         for (int i = 0; i < currentPass.Trajectories.Count; i++)
                    //         {
                    //             Debug.WriteLine($"[JULES_DEBUG]   LoadConfig-PreCall: Pass[{_currentConfiguration.CurrentPassIndex}]-Trajectory[{i}]: {currentPass.Trajectories[i].ToString()}");
                    //         }
                    //     }
                    // }
                    UpdateOrderNumberLabels();

                    // Assuming _cadService.GetWpfShapesFromDxf and entity selection logic
                    // might need to be re-run or updated if the config implies specific CAD entities.
                    // For now, just loading configuration values. Future work might involve
                    // re-selecting entities based on handles stored in config if _currentDxfDocument is still relevant.
                    isConfigurationDirty = false;
                    StatusTextBlock.Text = $"Configuration loaded from {Path.GetFileName(openFileDialog.FileName)}";
                    AppLogger.Log($"Successfully loaded configuration: {Path.GetFileName(openFileDialog.FileName)}");
                    _currentLoadedConfigPath = openFileDialog.FileName;
                    StartTestRunButton.IsEnabled = false; // New config loaded, robot program state is now unknown/stale
                }
                catch (Exception ex)
                {
                    StatusTextBlock.Text = "Error loading configuration.";
                    string msg = $"Failed to load configuration from {openFileDialog.FileName}";
                    AppLogger.Log(msg, ex, LogLevel.Error); // Already logged the exception here.
                    MessageBox.Show($"{msg}: {ex.Message}", "Load Error", MessageBoxButton.OK, MessageBoxImage.Error); // MessageBox is fine.
                    // Reset to a default state if loading fails
                    _currentConfiguration = new Models.Configuration { ProductName = $"Product_{DateTime.Now:yyyyMMddHHmmss}" };
                    ProductNameTextBox.Text = _currentConfiguration.ProductName;
                    // Reset new nozzle CheckBoxes in UI (they are per-trajectory, so this just clears the UI if no trajectory selected)
                    UpdateSelectedTrajectoryDetailUI(); // Renamed: This will clear them if nothing is selected
                    // The old global nozzle checkbox resets are removed.
                    // UpperNozzleOnCheckBox_Changed(null, null); // Removed
                    // LowerNozzleOnCheckBox_Changed(null, null); // Removed
                    isConfigurationDirty = false;
                    UpdateDirectionIndicator(); // Clear indicator if error during load
                    UpdateOrderNumberLabels();
                }
            }
            else
            {
                StatusTextBlock.Text = "Load configuration cancelled.";
                AppLogger.Log("Configuration loading cancelled by user.");
            }
        }
        private void ModbusConnectButton_Click(object sender, RoutedEventArgs e)
        {
            string ipAddress = ModbusIpAddressTextBox.Text;
            string portString = ModbusPortTextBox.Text;

            if (string.IsNullOrEmpty(ipAddress))
            {
                string msg = "IP address cannot be empty.";
                AppLogger.Log(msg, LogLevel.Error);
                MessageBox.Show(msg, "Input Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!int.TryParse(portString, out int port) || port < 1 || port > 65535)
            {
                string msg = "Invalid port number. Please enter a number between 1 and 65535.";
                AppLogger.Log(msg, LogLevel.Error);
                MessageBox.Show(msg, "Input Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            AppLogger.Log($"Attempting Modbus connection to {ipAddress}:{port}.");
            ModbusResponse response = _modbusService.Connect(ipAddress, port);
            ModbusStatusTextBlock.Text = response.Message;

            if (response.Success)
            {
                AppLogger.Log($"Modbus connected successfully to {ipAddress}:{port}. Message: {response.Message}");
                ModbusStatusIndicatorEllipse.Fill = Brushes.Green;
                ModbusConnectButton.IsEnabled = false;
                ModbusDisconnectButton.IsEnabled = true;
                SendToRobotButton.IsEnabled = true;
                // StartTestRunButton remains disabled until data is sent and robot state is confirmed
                StatusTextBlock.Text = "Successfully connected to Modbus server.";
            }
            else
            {
                AppLogger.Log($"Modbus connection failed to {ipAddress}:{port}. Message: {response.Message}", LogLevel.Error);
                ModbusStatusIndicatorEllipse.Fill = Brushes.Red;
                StatusTextBlock.Text = "Failed to connect to Modbus server.";
                StartTestRunButton.IsEnabled = false;
            }
        }

        private void ModbusDisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            _modbusService.Disconnect();
            AppLogger.Log("Modbus disconnected by user.");
            ModbusStatusTextBlock.Text = "Disconnected";
            ModbusStatusIndicatorEllipse.Fill = Brushes.Red;
            ModbusConnectButton.IsEnabled = true;
            ModbusDisconnectButton.IsEnabled = false;
            SendToRobotButton.IsEnabled = false;
            StartTestRunButton.IsEnabled = false; // Disable on disconnect
            StatusTextBlock.Text = "Disconnected from Modbus server.";
        }

        private void SendToRobotButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_modbusService.IsConnected)
            {
                string msg = "Not connected to Modbus server. Please connect first.";
                AppLogger.Log(msg, LogLevel.Warning);
                MessageBox.Show(msg, "Modbus Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Step 1: Read robot status from Modbus address 1000
            // Assuming address 1000 is the correct 0-based register index for the library.
            // If it's 1-based in documentation, it should be 999 here. Using 1000 as per user request.
            ushort robotStatusAddress = 1000;
            ModbusReadInt16Result statusResult = _modbusService.ReadHoldingRegisterInt16(robotStatusAddress);

            if (!statusResult.Success)
            {
                string msg = $"无法读取机械臂状态: {statusResult.Message}"; // "Failed to read robot status"
                AppLogger.Log(msg, LogLevel.Error);
                MessageBox.Show(msg, "Modbus 读取失败", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // --- Validate that all configured spray passes have trajectories ---
            if (_currentConfiguration == null || _currentConfiguration.SprayPasses == null || !_currentConfiguration.SprayPasses.Any())
            {
                // This case is handled further down, but good to be aware of it.
                // The more specific check is for existing passes that are empty.
            }
            else
            {
                foreach (var pass in _currentConfiguration.SprayPasses)
                {
                    if (pass.Trajectories == null || !pass.Trajectories.Any())
                    {
                        string msg = $"Spray pass '{pass.PassName}' contains no primitives. Please add primitives to all configured passes or remove empty ones before sending.";
                        AppLogger.Log(msg, LogLevel.Warning);
                        MessageBox.Show(msg, "Empty Spray Pass", MessageBoxButton.OK, MessageBoxImage.Warning);
                        StatusTextBlock.Text = $"Sending aborted: Spray pass '{pass.PassName}' is empty.";
                        return; // Abort sending
                    }
                }
            }
            // --- End of validation for empty passes ---

            short robotStatus = statusResult.Value;
            // Debug.WriteLine($"[JULES_DEBUG] SendToRobotButton_Click: Robot status read from address {robotStatusAddress} = {robotStatus}");

            // Step 2: Check robot status
            if (robotStatus == 0)
            {
                string msg = "当前机械臂处于工作状态"; // "Robot is currently busy"
                AppLogger.Log(msg, LogLevel.Warning);
                MessageBox.Show(msg, "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            else if (robotStatus == 2)
            {
                string msg = "当前机械臂错误"; // "Robot error"
                AppLogger.Log(msg, LogLevel.Error);
                MessageBox.Show(msg, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            else if (robotStatus != 1) // Only proceed if status is 1
            {
                string msg = $"未知的机械臂状态: {robotStatus}"; // "Unknown robot status"
                AppLogger.Log(msg, LogLevel.Error);
                MessageBox.Show(msg, "状态未知", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // If status is 1, proceed with sending data.
            // Debug.WriteLine($"[JULES_DEBUG] SendToRobotButton_Click: Robot status is 1 (Ready). Proceeding to send configuration.");
            AppLogger.Log("Send to Robot initiated. Robot status is Ready.");



            // Ensure points are populated for the trajectories in the current pass
            if (_currentConfiguration.CurrentPassIndex >= 0 &&
                _currentConfiguration.CurrentPassIndex < _currentConfiguration.SprayPasses.Count)
            {
                SprayPass currentPass = _currentConfiguration.SprayPasses[_currentConfiguration.CurrentPassIndex];
                if (currentPass.Trajectories != null)
                {
                    foreach (var trajectory in currentPass.Trajectories)
                    {
                        PopulateTrajectoryPoints(trajectory); // Assumes PopulateTrajectoryPoints(trajectory) exists and is accessible
                    }
                }
            }
            else if (_currentConfiguration.SprayPasses == null || _currentConfiguration.SprayPasses.Count == 0)
            {
                // No spray passes, so nothing to populate. SendConfiguration will handle this.
                string msg = "No spray passes in the current configuration to send.";
                AppLogger.Log(msg, LogLevel.Warning);
                MessageBox.Show(msg, "Configuration Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            else
            {
                // Invalid CurrentPassIndex, but SprayPasses exist.
                // SendConfiguration will return an error for invalid index.
                // No points to populate here for sending.
                // Optionally, show a MessageBox here too, but ModbusService will also report it.
                string msg = $"Cannot send: Invalid current spray pass index ({_currentConfiguration.CurrentPassIndex}).";
                AppLogger.Log(msg, LogLevel.Warning);
                MessageBox.Show(msg, "Configuration Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return; // Prevent sending if pass index is clearly wrong but passes exist
            }

            _currentConfiguration.ProductName = ProductNameTextBox.Text; // Ensure latest product name
            ModbusResponse response = _modbusService.SendConfiguration(_currentConfiguration);

            // Using StatusTextBlock for general feedback seems more consistent with other operations
            if (response.Success)
            {
                AppLogger.Log($"Configuration successfully sent to robot. Modbus response: {response.Message}");
                StatusTextBlock.Text = "Configuration successfully sent to robot.";
                ModbusStatusTextBlock.Text = response.Message; // Keep specific Modbus status updated too
                StartTestRunButton.IsEnabled = true; // Enable after successful send
            }
            else
            {
                AppLogger.Log($"Failed to send configuration to robot. Modbus response: {response.Message}", LogLevel.Error);
                StatusTextBlock.Text = $"Failed to send configuration: {response.Message}";
                ModbusStatusTextBlock.Text = response.Message; // Update Modbus status as well
                StartTestRunButton.IsEnabled = false;

                if (response.Message.Contains("exceeds the maximum limit"))
                {
                    MessageBox.Show(response.Message, "Data Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// Calculates the overall bounding box of the DXF document, considering header extents and all entity extents.
        /// </summary>
        /// <param name="dxfDoc">The DXF document.</param>
        /// <returns>A Rect representing the bounding box, or Rect.Empty if no valid bounds can be determined.</returns>
        private Rect GetDxfBoundingBox(DxfFile dxfDoc)
        {
            AppLogger.Log("GetDxfBoundingBox: Method started.", LogLevel.Info);
            if (dxfDoc == null)
            {
                AppLogger.Log("GetDxfBoundingBox: dxfDoc is null. Returning Rect.Empty.", LogLevel.Warning);
                return Rect.Empty;
            }
            AppLogger.Log("GetDxfBoundingBox: Method started.", LogLevel.Info); // Moved start log here

            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            bool hasValidBounds = false;

            // Calculate bounds directly from entities
            if (dxfDoc.Entities != null && dxfDoc.Entities.Any())
            {
                AppLogger.Log($"GetDxfBoundingBox: Processing {dxfDoc.Entities.Count()} entities.", LogLevel.Info);
                int entityIndex = 0;
                foreach (var entity in dxfDoc.Entities)
                {
                    if (entity == null)
                    {
                        AppLogger.Log($"GetDxfBoundingBox: Entity at index {entityIndex} is null. Skipping.", LogLevel.Debug);
                        entityIndex++;
                        continue;
                    }
                    AppLogger.Log($"GetDxfBoundingBox: Entity {entityIndex} - Type: {entity.GetType().Name}, Layer: {entity.Layer}, Color: {entity.Color}", LogLevel.Debug);
                    var bounds = CalculateEntityBoundsSimple(entity); // CalculateEntityBoundsSimple already logs entity details
                    if (!bounds.IsEmpty)
                    {
                        AppLogger.Log($"GetDxfBoundingBox: Entity {entityIndex} - Calculated Bounds (X,Y,W,H): ({bounds.X:F2}, {bounds.Y:F2}, {bounds.Width:F2}, {bounds.Height:F2})", LogLevel.Debug);
                        minX = Math.Min(minX, bounds.X);
                        minY = Math.Min(minY, bounds.Y);
                        maxX = Math.Max(maxX, bounds.X + bounds.Width);
                        maxY = Math.Max(maxY, bounds.Y + bounds.Height);
                        hasValidBounds = true;
                        AppLogger.Log($"GetDxfBoundingBox: Entity {entityIndex} - Current Aggregated (minX,minY,maxX,maxY): ({minX:F2}, {minY:F2}, {maxX:F2}, {maxY:F2})", LogLevel.Debug);
                    }
                    else
                    {
                        AppLogger.Log($"GetDxfBoundingBox: Entity {entityIndex} - Returned empty bounds. Not included in aggregation.", LogLevel.Debug);
                    }
                    entityIndex++;
                }
            }

            if (!hasValidBounds)
            {
                AppLogger.Log("GetDxfBoundingBox: No valid entity bounds found after iterating all entities. Returning Rect.Empty.", LogLevel.Warning);
                return Rect.Empty;
            }

            // The following block forcing inclusion of origin (0,0) has been removed
            // as it leads to incorrect bounding boxes for drawings not centered at or spanning the origin.
            // minX = Math.Min(minX, 0);
            // minY = Math.Min(minY, 0);
            // maxX = Math.Max(maxX, 0);
            // maxY = Math.Max(maxY, 0);

            var result = new Rect(minX, minY, maxX - minX, maxY - minY);
            AppLogger.Log($"GetDxfBoundingBox: Final calculated bounding box (minX,minY,width,height): ({result.X:F2}, {result.Y:F2}, {result.Width:F2}, {result.Height:F2})", LogLevel.Info);
            return result;
        }

        private Rect CalculateEntityBoundsSimple(DxfEntity entity)
        {
            // This method is called per entity, so keeping its logging concise or at Debug level.
            // The caller (GetDxfBoundingBox) will log the entity index and type.
            // AppLogger.Log($"CalculateEntityBoundsSimple: Processing entity of type {entity.GetType().Name}", LogLevel.Debug);
            
            if (entity is DxfLine line)
            {
                AppLogger.Log($"CalculateEntityBoundsSimple (Line): P1=({line.P1.X:F2},{line.P1.Y:F2},{line.P1.Z:F2}), P2=({line.P2.X:F2},{line.P2.Y:F2},{line.P2.Z:F2})", LogLevel.Debug);
                double minX = Math.Min(line.P1.X, line.P2.X);
                double minY = Math.Min(line.P1.Y, line.P2.Y);
                double maxX = Math.Max(line.P1.X, line.P2.X);
                double maxY = Math.Max(line.P1.Y, line.P2.Y);
                return new Rect(minX, minY, maxX - minX, maxY - minY);
            }
            else if (entity is DxfCircle circle)
            {
                AppLogger.Log($"CalculateEntityBoundsSimple (Circle): Center=({circle.Center.X:F2},{circle.Center.Y:F2},{circle.Center.Z:F2}), Radius={circle.Radius:F2}, Normal={circle.Normal}", LogLevel.Debug);
                double minX = circle.Center.X - circle.Radius;
                double minY = circle.Center.Y - circle.Radius;
                double width = circle.Radius * 2;
                double height = circle.Radius * 2;
                return new Rect(minX, minY, width, height);
            }
            else if (entity is DxfArc arc)
            {
                AppLogger.Log($"CalculateEntityBoundsSimple (Arc): Center=({arc.Center.X:F2},{arc.Center.Y:F2},{arc.Center.Z:F2}), Radius={arc.Radius:F2}, StartAngle={arc.StartAngle:F2}, EndAngle={arc.EndAngle:F2}, Normal={arc.Normal}", LogLevel.Debug);
                // Calculate start and end points
                var startPoint = new Point(
                    arc.Center.X + arc.Radius * Math.Cos(arc.StartAngle * Math.PI / 180.0),
                    arc.Center.Y + arc.Radius * Math.Sin(arc.StartAngle * Math.PI / 180.0));
                var endPoint = new Point(
                    arc.Center.X + arc.Radius * Math.Cos(arc.EndAngle * Math.PI / 180.0),
                    arc.Center.Y + arc.Radius * Math.Sin(arc.EndAngle * Math.PI / 180.0));

                double minX = Math.Min(startPoint.X, endPoint.X);
                double minY = Math.Min(startPoint.Y, endPoint.Y);
                double maxX = Math.Max(startPoint.X, endPoint.X);
                double maxY = Math.Max(startPoint.Y, endPoint.Y);

                // Check cardinal points (0, 90, 180, 270 degrees) if they fall within the arc
                double startAngle = arc.StartAngle;
                double endAngle = arc.EndAngle;
                if (endAngle < startAngle) endAngle += 360;

                for (int angle = 0; angle < 360; angle += 90)
                {
                    double normalizedAngle = angle;
                    while (normalizedAngle < startAngle) normalizedAngle += 360;
                    if (normalizedAngle >= startAngle && normalizedAngle <= endAngle)
                    {
                        double rad = angle * Math.PI / 180.0;
                        double x = arc.Center.X + arc.Radius * Math.Cos(rad);
                        double y = arc.Center.Y + arc.Radius * Math.Sin(rad);
                        minX = Math.Min(minX, x);
                        minY = Math.Min(minY, y);
                        maxX = Math.Max(maxX, x);
                        maxY = Math.Max(maxY, y);
                    }
                }

                return new Rect(minX, minY, maxX - minX, maxY - minY);
            }
            else if (entity is DxfLwPolyline lwPolyline && lwPolyline.Vertices.Any())
            {
                AppLogger.Log($"CalculateEntityBoundsSimple (LwPolyline): Vertices={lwPolyline.Vertices.Count}, IsClosed={lwPolyline.IsClosed}, Elevation={lwPolyline.Elevation}", LogLevel.Debug);
                // Note: This LwPolyline bounds calculation does not account for bulge (arcs).
                // For accurate bounds, each segment (line or arc from bulge) would need to be calculated.
                // This is a known simplification for now.
                double minX = lwPolyline.Vertices[0].X;
                double minY = lwPolyline.Vertices[0].Y;
                double maxX = minX;
                double maxY = minY;

                foreach (var vertex in lwPolyline.Vertices)
                {
                    minX = Math.Min(minX, vertex.X);
                    minY = Math.Min(minY, vertex.Y);
                    maxX = Math.Max(maxX, vertex.X);
                    maxY = Math.Max(maxY, vertex.Y);
                }
                // Add polyline elevation to Y coordinates for bounding box
                minY += lwPolyline.Elevation;
                maxY += lwPolyline.Elevation;

                return new Rect(minX, minY, maxX - minX, maxY - minY);
            }
            else if (entity is DxfInsert insert)
            {
                AppLogger.Log($"CalculateEntityBoundsSimple (Insert): Name='{insert.Name}', Location=({insert.Location.X:F2},{insert.Location.Y:F2},{insert.Location.Z:F2}), XScale={insert.XScaleFactor:F2}, YScale={insert.YScaleFactor:F2}, Rotation={insert.Rotation:F2}", LogLevel.Debug);
                return GetDxfInsertBounds(insert); // GetDxfInsertBounds has its own logging
            }

            AppLogger.Log($"CalculateEntityBoundsSimple: Unsupported entity type '{entity.GetType().Name}' for bounding box calculation. Returning Empty.", LogLevel.Warning);
            return Rect.Empty;
        }

        private void CadCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (CadCanvas == null || _scaleTransform == null || _translateTransform == null)
            {
                AppLogger.Log("CadCanvas_MouseWheel: Canvas or transforms not initialized, skipping zoom.", LogLevel.Warning);
                return;
            }

            Point mousePos = e.GetPosition(CadCanvas); // Mouse position relative to the CadCanvas

            double zoomFactor = 1.1; // Zoom in by 10%
            double scaleChange;

            if (e.Delta > 0) // Zoom in
            {
                scaleChange = zoomFactor;
            }
            else // Zoom out
            {
                scaleChange = 1.0 / zoomFactor;
            }

            double newScaleX = _scaleTransform.ScaleX * scaleChange;
            double newScaleY = _scaleTransform.ScaleY * scaleChange; // If ScaleY is negative, it will correctly become more negative or less negative

            // Optional: Add min/max zoom limits
            double minScale = 0.05;
            double maxScale = 20.0;
            if (Math.Abs(newScaleX) < minScale || Math.Abs(newScaleX) > maxScale)
            {
                AppLogger.Log($"CadCanvas_MouseWheel: Zoom scale {newScaleX:F4} out of limits [{minScale}, {maxScale}]. No zoom applied.", LogLevel.Debug);
                return; // Zoom limit reached
            }

            AppLogger.Log($"CadCanvas_MouseWheel: MousePos=({mousePos.X:F2},{mousePos.Y:F2}), Delta={e.Delta}, OldScale=({_scaleTransform.ScaleX:F4},{_scaleTransform.ScaleY:F4}), ScaleChange={scaleChange:F4}", LogLevel.Debug);

            // The point under the mouse cursor in world coordinates (DXF coordinates if Y is flipped)
            // To get this, we need the inverse of the current total transform at the mouse point.
            // Current transform: Scale then Translate. Inverse: Un-Translate then Un-Scale.
            // (canvasPoint - Translation) / Scale = worldPoint
            // canvasPoint.X = worldPoint.X * currentScaleX + currentTranslateX
            // worldPoint.X = (canvasPoint.X - currentTranslateX) / currentScaleX
            // worldPoint.Y = (canvasPoint.Y - currentTranslateY) / currentScaleY (where currentScaleY is negative)

            Point worldPointBeforeZoom = new Point(
                (mousePos.X - _translateTransform.X) / _scaleTransform.ScaleX,
                (mousePos.Y - _translateTransform.Y) / _scaleTransform.ScaleY
            );
            AppLogger.Log($"CadCanvas_MouseWheel: WorldPointUnderMouse (BeforeZoom)=({worldPointBeforeZoom.X:F3},{worldPointBeforeZoom.Y:F3})", LogLevel.Debug);


            _scaleTransform.ScaleX = newScaleX;
            _scaleTransform.ScaleY = newScaleY; // Maintain the sign of original ScaleY (should be negative)
            AppLogger.Log($"CadCanvas_MouseWheel: NewScale=({_scaleTransform.ScaleX:F4},{_scaleTransform.ScaleY:F4})", LogLevel.Debug);


            // After scaling, the worldPointBeforeZoom, if rendered with the new scale but old translation,
            // would now appear at a new canvas position:
            // newCanvasPosX = worldPointBeforeZoom.X * newScaleX + oldTranslateX
            // newCanvasPosY = worldPointBeforeZoom.Y * newScaleY + oldTranslateY

            // We want this worldPointBeforeZoom to remain at mousePos.X, mousePos.Y on canvas.
            // So, we need to find newTranslateX, newTranslateY such that:
            // mousePos.X = worldPointBeforeZoom.X * newScaleX + newTranslateX  => newTranslateX = mousePos.X - worldPointBeforeZoom.X * newScaleX
            // mousePos.Y = worldPointBeforeZoom.Y * newScaleY + newTranslateY  => newTranslateY = mousePos.Y - worldPointBeforeZoom.Y * newScaleY

            double newTranslateX = mousePos.X - (worldPointBeforeZoom.X * _scaleTransform.ScaleX);
            double newTranslateY = mousePos.Y - (worldPointBeforeZoom.Y * _scaleTransform.ScaleY);

            _translateTransform.X = newTranslateX;
            _translateTransform.Y = newTranslateY;
            AppLogger.Log($"CadCanvas_MouseWheel: NewTranslate=({_translateTransform.X:F3},{_translateTransform.Y:F3})", LogLevel.Debug);

            StatusTextBlock.Text = $"Zoom: {Math.Abs(_scaleTransform.ScaleX * 100):F1}%";
            isConfigurationDirty = true; // Zoom/pan changes configuration state
            e.Handled = true;
        }

        private void CadCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.MiddleButton == MouseButtonState.Pressed ||
                (e.LeftButton == MouseButtonState.Pressed && Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)) ) // More robust Ctrl check
            {
                _isPanning = true;
                _panStartPoint = e.GetPosition(this); // Pan relative to MainWindow for this example, or CadCanvas.Parent
                CadCanvas.CaptureMouse();
                StatusTextBlock.Text = "Panning...";
                e.Handled = true;
            }
            // Check e.Source to ensure the click originated on the Canvas itself, not on a child like a DxfEntity shape
            else if (e.LeftButton == MouseButtonState.Pressed && e.Source == CadCanvas)
            {
                isSelectingWithRect = true;
                selectionStartPoint = e.GetPosition(CadCanvas);

                if (selectionRectangleUI != null && CadCanvas.Children.Contains(selectionRectangleUI))
                {
                    CadCanvas.Children.Remove(selectionRectangleUI);
                }
                selectionRectangleUI = null; // Ensure it's null before creating a new one

                selectionRectangleUI = new System.Windows.Shapes.Rectangle
                {
                    Stroke = Brushes.DodgerBlue, // Using SelectedStrokeBrush color
                    StrokeThickness = 1,
                    StrokeDashArray = new System.Windows.Media.DoubleCollection { 3, 2 },
                    Fill = new SolidColorBrush(Color.FromArgb(40, 0, 120, 255)) // Light semi-transparent blue
                };

                Canvas.SetLeft(selectionRectangleUI, selectionStartPoint.X);
                Canvas.SetTop(selectionRectangleUI, selectionStartPoint.Y);
                selectionRectangleUI.Width = 0;
                selectionRectangleUI.Height = 0;

                // Add to canvas before trying to set ZIndex, though ZIndex might not be strictly needed here
                // as it's temporary.
                CadCanvas.Children.Add(selectionRectangleUI);
                // Panel.SetZIndex(selectionRectangleUI, int.MaxValue); // Optional: ensure it's on top

                CadCanvas.CaptureMouse();
                StatusTextBlock.Text = "Defining selection area...";
                e.Handled = true;
            }
            // If not handled, and it was a left click, it might be on a DxfEntity shape,
            // which would be handled by OnCadEntityClicked due to event bubbling if that shape has a handler.
        }
        private void CadCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isPanning)
            {
                System.Windows.Point currentPanPoint = e.GetPosition(this); // Or CadCanvas.Parent as IInputElement
                Vector panDelta = currentPanPoint - _panStartPoint;
                _translateTransform.X += panDelta.X;
                _translateTransform.Y += panDelta.Y;
                _panStartPoint = currentPanPoint;
                e.Handled = true;
            }
            else if (isSelectingWithRect && selectionRectangleUI != null)
            {
                System.Windows.Point currentMousePos = e.GetPosition(CadCanvas);

                double x = Math.Min(selectionStartPoint.X, currentMousePos.X);
                double y = Math.Min(selectionStartPoint.Y, currentMousePos.Y);
                double width = Math.Abs(selectionStartPoint.X - currentMousePos.X);
                double height = Math.Abs(selectionStartPoint.Y - currentMousePos.Y);

                Canvas.SetLeft(selectionRectangleUI, x);
                Canvas.SetTop(selectionRectangleUI, y);
                selectionRectangleUI.Width = width;
                selectionRectangleUI.Height = height;

                e.Handled = true;
            }
        }
        private void CadCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isPanning)
            {
                _isPanning = false;
                CadCanvas.ReleaseMouseCapture();
                StatusTextBlock.Text = "Pan complete.";
                e.Handled = true;
            }
            else if (isSelectingWithRect)
            {
                isSelectingWithRect = false;
                CadCanvas.ReleaseMouseCapture();

                if (selectionRectangleUI == null) // Should not happen if isSelectingWithRect was true
                {
                    e.Handled = true;
                    return;
                }

                Rect finalSelectionRect = new Rect(
                    Canvas.GetLeft(selectionRectangleUI),
                    Canvas.GetTop(selectionRectangleUI),
                    selectionRectangleUI.Width,
                    selectionRectangleUI.Height);

                // Remove the visual rectangle from canvas
                CadCanvas.Children.Remove(selectionRectangleUI);
                selectionRectangleUI = null;

                StatusTextBlock.Text = "Selection processed."; // Or clear
                Debug.WriteLine($"[DEBUG] CadCanvas_MouseUp (Marquee): finalSelectionRect (Canvas UI Coords) = {finalSelectionRect}");
                Debug.WriteLine($"[DEBUG] CadCanvas_MouseUp (Marquee): _scaleTransform=({_scaleTransform.ScaleX},{_scaleTransform.ScaleY}), _translateTransform=({_translateTransform.X},{_translateTransform.Y})");

                // Small drag check (if it was more of a click)
                const double clickThreshold = 5.0;
                if (finalSelectionRect.Width < clickThreshold && finalSelectionRect.Height < clickThreshold)
                {
                    e.Handled = true; // Event handled, but no marquee selection performed
                    return;
                }

                // Check for a valid current pass
                if (_currentConfiguration == null || _currentConfiguration.CurrentPassIndex < 0 ||
                    _currentConfiguration.CurrentPassIndex >= _currentConfiguration.SprayPasses.Count ||
                    _currentConfiguration.SprayPasses[_currentConfiguration.CurrentPassIndex].Trajectories == null)
                {
                    string msg = "Please select or create a spray pass first to add entities.";
                    AppLogger.Log(msg, LogLevel.Info); // Info, as it's guidance
                    MessageBox.Show(msg, "No Active Pass", MessageBoxButton.OK, MessageBoxImage.Information);
                    e.Handled = true;
                    return;
                }
                var currentPass = _currentConfiguration.SprayPasses[_currentConfiguration.CurrentPassIndex];
                bool selectionStateChanged = false;
                List<DxfEntity> marqueeHitEntities = new List<DxfEntity>();

                RectangleGeometry selectionGeometry = new RectangleGeometry(finalSelectionRect);
                GeometryHitTestParameters parameters = new GeometryHitTestParameters(selectionGeometry);

                HitTestResultCallback hitTestCallback = (HitTestResult result) =>
                {
                    if (result is GeometryHitTestResult geometryResult)
                    {
                        if (geometryResult.VisualHit is System.Windows.Shapes.Shape hitShape)
                        {
                            if (_wpfShapeToDxfEntityMap.TryGetValue(hitShape, out DxfEntity? hitEntity) && hitEntity != null)
                            {
                                if (!marqueeHitEntities.Contains(hitEntity))
                                {
                                    marqueeHitEntities.Add(hitEntity);
                                    Debug.WriteLine($"[DEBUG] CadCanvas_MouseUp (Marquee HitTest): Hit DxfEntity {hitEntity.GetType().Name}");
                                }
                            }
                        }
                    }
                    return HitTestResultBehavior.Continue;
                };

                // Optional: HitTestFilterCallback to quickly prune visuals not in _wpfShapeToDxfEntityMap.Keys
                // For now, let the result callback handle it.
                VisualTreeHelper.HitTest(CadCanvas, null, hitTestCallback, parameters);

                Debug.WriteLine($"[DEBUG] CadCanvas_MouseUp (Marquee HitTest): Found {marqueeHitEntities.Count} entities in marquee.");

                // Removed outer declaration and population of newPassTrajectories.
                // This logic is now handled within the 'else' block for Replace Mode.

                Trajectory trajectoryToSelect = null;
                bool isShiftPressed = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
                Debug.WriteLine($"[DEBUG] CadCanvas_MouseUp: ShiftPressed={isShiftPressed}, Marquee Hits={marqueeHitEntities.Count}");

                if (isShiftPressed)
                {
                    // Mode: Deselect within marquee (Shift + Marquee)
                    // Remove any currently selected items that are within the marquee.
                    int itemsDeselectedCount = 0;
                    for (int i = currentPass.Trajectories.Count - 1; i >= 0; i--)
                    {
                        Trajectory trajectory = currentPass.Trajectories[i];
                        if (marqueeHitEntities.Contains(trajectory.OriginalDxfEntity))
                        {
                            Debug.WriteLine($"[DEBUG] CadCanvas_MouseUp (Shift+Marquee): Deselecting {trajectory.OriginalDxfEntity.GetType().Name}");
                            currentPass.Trajectories.RemoveAt(i);
                            itemsDeselectedCount++;
                        }
                    }
                    if (itemsDeselectedCount > 0)
                    {
                        AppLogger.Log($"Marquee deselection: {itemsDeselectedCount} trajectories removed from pass '{currentPass.PassName}'.");
                        selectionStateChanged = true;
                    }
                }
                else
                {
                    // Mode: Additive selection (Normal Marquee)
                    // Add any items from the marquee that are not already selected.
                    int itemsAddedCount = 0;
                    List<string> addedTrajectoryInfo = new List<string>(); // For logging details

                    foreach (DxfEntity? hitDxfEntity in marqueeHitEntities) // hitDxfEntity can be null if TryGetValue failed but was added
                    {
                        if (hitDxfEntity == null) continue; // Skip null entities

                        // Use geometric comparison to check if already selected, due to potential instance differences
                        bool alreadySelected = currentPass.Trajectories.Any(t => t.OriginalDxfEntity != null && AreEntitiesGeometricallyEquivalent(t.OriginalDxfEntity, hitDxfEntity));
                        if (!alreadySelected)
                        {
                            Debug.WriteLine($"[DEBUG] CadCanvas_MouseUp (Normal Marquee - Additive): Adding {hitDxfEntity.GetType().Name} as it's not geometrically equivalent to any existing selected trajectory's entity.");
                            var newTrajectory = new Trajectory
                            {
                                OriginalDxfEntity = hitDxfEntity,
                                EntityType = hitDxfEntity.GetType().Name, // Safe due to null check above
                                IsReversed = false // Default
                            };
                            // Populate geometric properties for the new trajectory
                            if (hitDxfEntity is DxfLwPolyline polyline)
                            {
                                var polygonTrajectory = CreatePolygonTrajectoryFromPolyline(polyline);
                                currentPass.Trajectories.Add(polygonTrajectory);
                                itemsAddedCount++;
                                addedTrajectoryInfo.Add($"Type 'Polygon', EntityHandle '{polygonTrajectory.OriginalEntityHandle}'");
                            trajectoryToSelect = polygonTrajectory;
                            continue;
                            }
                            else
                            {
                                switch (hitDxfEntity) // Safe due to null check above
                                {
                                    case DxfLine line:
                                        newTrajectory.PrimitiveType = "Line";
                                        newTrajectory.LineStartPoint = line.P1;
                                        newTrajectory.LineEndPoint = line.P2;
                                        break;
                                    case DxfArc arc:
                                    newTrajectory.PrimitiveType = "Arc";
                                    // Populate ArcPoint1, ArcPoint2, ArcPoint3 from DxfArc
                                    double startRadMarquee = arc.StartAngle * Math.PI / 180.0;
                                    double endRadMarquee = arc.EndAngle * Math.PI / 180.0;
                                    newTrajectory.ArcPoint1.Coordinates = new DxfPoint(
                                        arc.Center.X + arc.Radius * Math.Cos(startRadMarquee),
                                        arc.Center.Y + arc.Radius * Math.Sin(startRadMarquee),
                                        arc.Center.Z);
                                    newTrajectory.ArcPoint3.Coordinates = new DxfPoint(
                                        arc.Center.X + arc.Radius * Math.Cos(endRadMarquee),
                                        arc.Center.Y + arc.Radius * Math.Sin(endRadMarquee),
                                        arc.Center.Z);
                                    if (endRadMarquee < startRadMarquee) endRadMarquee += 2 * Math.PI;
                                    double midRadMarquee = (startRadMarquee + endRadMarquee) / 2.0;
                                    newTrajectory.ArcPoint2.Coordinates = new DxfPoint(
                                        arc.Center.X + arc.Radius * Math.Cos(midRadMarquee),
                                        arc.Center.Y + arc.Radius * Math.Sin(midRadMarquee),
                                        arc.Center.Z);
                                    break;
                                case DxfCircle circle:
                                    newTrajectory.PrimitiveType = "Circle";
                                    // newTrajectory.CircleCenter = circle.Center; // Replaced by 3 points
                                    // newTrajectory.CircleRadius = circle.Radius; // Replaced by 3 points
                                    // newTrajectory.CircleNormal = circle.Normal; // Will be derived

                                    DxfVector marquee_normal = circle.Normal.Normalize();
                                    DxfPoint marquee_center = circle.Center;
                                    double marquee_radius = circle.Radius;

                                    // Re-introducing the corrected logic for marquee_localXAxis
                                    DxfVector marquee_localXAxis;
                                    if (Math.Abs(marquee_normal.Z) > 0.999) // Circle is mostly flat on XY plane
                                    {
                                        marquee_localXAxis = DxfVector.XAxis;
                                    }
                                    else // Tilted or vertical circle
                                    {
                                        marquee_localXAxis = (DxfVector.ZAxis).Cross(marquee_normal).Normalize();
                                    }
                                    DxfVector marquee_localYAxis = marquee_normal.Cross(marquee_localXAxis).Normalize();

                                    double marquee_angleP1_rad = -135.0 * Math.PI / 180.0;

                                    DxfVector marquee_p1_offset_vec = (marquee_localXAxis * (marquee_radius * Math.Cos(marquee_angleP1_rad))) + (marquee_localYAxis * (marquee_radius * Math.Sin(marquee_angleP1_rad)));
                                    newTrajectory.CirclePoint1.Coordinates = marquee_center + marquee_p1_offset_vec;

                                    double marquee_angleP2_rad = marquee_angleP1_rad + (120.0 * Math.PI / 180.0);
                                    double marquee_angleP3_rad = marquee_angleP1_rad + (240.0 * Math.PI / 180.0);

                                    DxfVector marquee_p2_offset_vec = (marquee_localXAxis * (marquee_radius * Math.Cos(marquee_angleP2_rad))) + (marquee_localYAxis * (marquee_radius * Math.Sin(marquee_angleP2_rad)));
                                    DxfVector marquee_p3_offset_vec = (marquee_localXAxis * (marquee_radius * Math.Cos(marquee_angleP3_rad))) + (marquee_localYAxis * (marquee_radius * Math.Sin(marquee_angleP3_rad)));

                                    newTrajectory.CirclePoint2.Coordinates = marquee_center + marquee_p2_offset_vec;
                                    newTrajectory.CirclePoint3.Coordinates = marquee_center + marquee_p3_offset_vec;

                                    // Store original parameters as well
                                    newTrajectory.OriginalCircleCenter = marquee_center;
                                    newTrajectory.OriginalCircleRadius = marquee_radius;
                                    newTrajectory.OriginalCircleNormal = marquee_normal;
                                    break;
                                default:
                                    newTrajectory.PrimitiveType = hitDxfEntity.GetType().Name; // Fallback
                                    break;
                                }
                            }
                            if (hitDxfEntity.GetType() != typeof(DxfLwPolyline))
                            {
                                PopulateTrajectoryPoints(newTrajectory);
                                newTrajectory.Runtime = TrajectoryUtils.CalculateMinRuntime(newTrajectory); // Set default runtime
                                currentPass.Trajectories.Add(newTrajectory);
                                addedTrajectoryInfo.Add($"Type '{newTrajectory.PrimitiveType}', EntityHandle '{newTrajectory.OriginalEntityHandle}'");
                                itemsAddedCount++;
                                trajectoryToSelect = newTrajectory;
                            }
                        }
                    }
                    if (itemsAddedCount > 0)
                    {
                        AppLogger.Log($"Marquee selection: {itemsAddedCount} trajectories added to pass '{currentPass.PassName}'. Details: {string.Join("; ", addedTrajectoryInfo)}");
                        selectionStateChanged = true;
                    }
                }

                if (selectionStateChanged)
                {
                    Debug.WriteLine($"[DEBUG] CadCanvas_MouseUp (Marquee): Selection state changed. Refreshing UI.");
                    isConfigurationDirty = true;
                    RefreshCurrentPassTrajectoriesListBox();
                    RefreshCadCanvasHighlights();
                    UpdateDirectionIndicator();
                    UpdateOrderNumberLabels();
                    StatusTextBlock.Text = $"Selection updated in {currentPass.PassName}. Total: {currentPass.Trajectories.Count}.";
                }
                e.Handled = true;
            }
        }
        /// <summary>
        /// Calculates the bounding rectangle for a given DXF entity.
        /// </summary>
        private bool PerformSaveOperation()
        {
            // Call update methods at the beginning of save operation
            // These methods now use _trajectoryInDetailView, which should reflect the
            // trajectory whose details are currently shown (and potentially edited) in the UI.
            UpdateLineStartZFromTextBox();
            UpdateLineEndZFromTextBox();
            UpdateArcCenterZFromTextBox();
            UpdateCircleCenterZFromTextBox();

            // This logic is largely moved from the original SaveConfigButton_Click
            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                Filter = "Config files (*.json)|*.json|All files (*.*)|*.*",
                Title = "Save Configuration File",
                FileName = $"{ProductNameTextBox.Text}.json" // Suggest filename
            };

            string initialDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "RobTeachProject", "RobTeach", "Configurations"));
            if (!Directory.Exists(initialDir))
            {
                initialDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Configurations"); // Fallback
            }
            if (!Directory.Exists(initialDir))
            {
                try
                {
                    Directory.CreateDirectory(initialDir);
                }
                catch (Exception ex)
                {
                    string msg = "Error creating configurations directory.";
                    AppLogger.Log(msg, ex, LogLevel.Error);
                    StatusTextBlock.Text = $"{msg} {ex.Message}";
                    // Optionally, default to a known accessible path or handle error differently
                    initialDir = AppDomain.CurrentDomain.BaseDirectory;
                }
            }
            saveFileDialog.InitialDirectory = initialDir;

            if (saveFileDialog.ShowDialog() == true)
            {
                // Ensure the _currentConfiguration object has the latest product name from the UI
                _currentConfiguration.ProductName = ProductNameTextBox.Text;

                // Populate DxfFileContent for saving
                string dxfContentForSaving = string.Empty;
                if (!string.IsNullOrEmpty(_currentDxfFilePath) && File.Exists(_currentDxfFilePath))
                {
                    // Case 1: DXF was loaded from a specific file path that still exists
                    try
                    {
                        dxfContentForSaving = File.ReadAllText(_currentDxfFilePath);
                        // Debug.WriteLine($"[JULES_DEBUG] PerformSaveOperation: Saving DXF content from file: {_currentDxfFilePath}");
                    }
                    catch (Exception ex)
                    {
                        string msg = $"Could not read DXF file content from '{_currentDxfFilePath}' for saving.";
                        AppLogger.Log(msg, ex, LogLevel.Warning);
                        MessageBox.Show($"Warning: {msg} {ex.Message}", "File Read Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                        // Keep dxfContentForSaving as string.Empty, or decide if we should use in-memory version if available
                        // For now, if file read fails, we save empty. This could be debated.
                        // If _currentConfiguration.DxfFileContent (in memory) is already populated from a previous successful load (even embedded),
                        // it might be better to use that.
                        // Let's refine: if file read fails, try to use in-memory if it's not the placeholder path.
                        if (_currentDxfFilePath != "(Embedded DXF from project file)" && !string.IsNullOrEmpty(_currentConfiguration.DxfFileContent)) {
                             // Debug.WriteLine($"[JULES_DEBUG] PerformSaveOperation: File read failed, but using non-empty in-memory DxfFileContent as fallback.");
                            dxfContentForSaving = _currentConfiguration.DxfFileContent;
                        } else {
                            dxfContentForSaving = string.Empty;
                        }
                    }
                }
                else if (_currentDxfFilePath == "(Embedded DXF from project file)" && !string.IsNullOrEmpty(_currentConfiguration.DxfFileContent))
                {
                    // Case 2: DXF was loaded from embedded content in a previously loaded configuration.
                    // We should re-save this embedded content.
                    // _currentConfiguration.DxfFileContent already holds this from the load.
                    dxfContentForSaving = _currentConfiguration.DxfFileContent;
                    // Debug.WriteLine("[JULES_DEBUG] PerformSaveOperation: Re-saving existing embedded DxfFileContent.");
                }
                else
                {
                    // Case 3: No valid DXF file path and no current embedded content (e.g., new config without DXF loaded, or path was invalid and no prior embedded data)
                    // Or if _currentDxfFilePath was something else invalid and File.Exists was false.
                    // If _currentConfiguration.DxfFileContent has something (e.g. from a successful load before path became invalid), use it.
                    if (!string.IsNullOrEmpty(_currentConfiguration.DxfFileContent)) {
                        // Debug.WriteLine("[JULES_DEBUG] PerformSaveOperation: Using existing in-memory DxfFileContent as _currentDxfFilePath is invalid or missing for file operations.");
                        dxfContentForSaving = _currentConfiguration.DxfFileContent;
                    } else {
                        // Debug.WriteLine("[JULES_DEBUG] PerformSaveOperation: No valid DXF source found, DxfFileContent will be empty for saving.");
                        dxfContentForSaving = string.Empty;
                    }
                }
                // Assign the determined DXF content to the _currentConfiguration, so it's part of what configToSave copies.
                _currentConfiguration.DxfFileContent = dxfContentForSaving;


                // Populate Modbus Settings
                _currentConfiguration.ModbusIpAddress = ModbusIpAddressTextBox.Text;
                if (int.TryParse(ModbusPortTextBox.Text, out int parsedPort) && parsedPort >= 1 && parsedPort <= 65535)
                {
                    _currentConfiguration.ModbusPort = parsedPort;
                }
                else
                {
                    _currentConfiguration.ModbusPort = 502; // Default port
                }

                // Populate CanvasState
                _currentConfiguration.CanvasState ??= new CanvasViewSettings(); // Ensure not null
                _currentConfiguration.CanvasState.ScaleX = _scaleTransform.ScaleX;
                _currentConfiguration.CanvasState.ScaleY = _scaleTransform.ScaleY;
                _currentConfiguration.CanvasState.TranslateX = _translateTransform.X;
                _currentConfiguration.CanvasState.TranslateY = _translateTransform.Y;

                _currentConfiguration.SelectedTrajectoryIndexInCurrentPass = CurrentPassTrajectoriesListBox.SelectedIndex;
                // This is done before deciding what to filter into configToSave.

                Configuration configToSave = new Configuration
                {
                    ProductName = _currentConfiguration.ProductName,
                    CurrentPassIndex = _currentConfiguration.CurrentPassIndex, // Preserve the selected pass index
                    // NEW: Copy new fields
                    DxfFileContent = _currentConfiguration.DxfFileContent,
                    ModbusIpAddress = _currentConfiguration.ModbusIpAddress,
                    ModbusPort = _currentConfiguration.ModbusPort,
                    CanvasState = _currentConfiguration.CanvasState,
                    SelectedTrajectoryIndexInCurrentPass = _currentConfiguration.SelectedTrajectoryIndexInCurrentPass, // <-- ADD THIS LINE
                    SprayPasses = new List<SprayPass>()
                };

                if (_currentConfiguration.SprayPasses != null) // Ensure SprayPasses list itself is not null
                {
                    foreach (var originalPass in _currentConfiguration.SprayPasses)
                    {
                        // Only include passes that actually contain trajectories
                        if (originalPass.Trajectories != null && originalPass.Trajectories.Any())
                        {
                            SprayPass passToSave = new SprayPass
                            {
                                PassName = originalPass.PassName
                                // If SprayPass has other serializable properties, copy them here
                            };
                            // Copy the list of trajectories for this pass.
                            // These are the trajectories that are actively configured.
                            passToSave.Trajectories = new List<Trajectory>(originalPass.Trajectories);

                            configToSave.SprayPasses.Add(passToSave);
                        }
                    }
                }

                // Now, save the filtered configuration object
                try
                {
                    _configService.SaveConfiguration(configToSave, saveFileDialog.FileName);
                    StatusTextBlock.Text = $"Configuration saved to {Path.GetFileName(saveFileDialog.FileName)}";
                    AppLogger.Log($"Configuration saved to: {saveFileDialog.FileName}");
                    _currentLoadedConfigPath = saveFileDialog.FileName; // Update current loaded path
                    return true; // Save successful
                }
                catch (Exception ex)
                {
                    StatusTextBlock.Text = "Error saving configuration.";
                    string msg = $"Failed to save configuration to {saveFileDialog.FileName}";
                    AppLogger.Log(msg, ex, LogLevel.Error);
                    MessageBox.Show($"{msg}: {ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false; // Save failed
                }
            }
            else
            {
                StatusTextBlock.Text = "Save configuration cancelled.";
                AppLogger.Log("Save configuration cancelled by user.");
                return false; // User cancelled SaveFileDialog
            }
        }
        // Helper method to transform a point (used for DxfInsert)
        private DxfPoint TransformPoint(DxfPoint point, DxfPoint location, double xScale, double yScale, double rotationDegrees)
        {
            // Apply scaling
            double scaledX = point.X * xScale;
            double scaledY = point.Y * yScale;

            // Apply rotation (around block's local origin 0,0)
            double rotationRadians = rotationDegrees * Math.PI / 180.0;
            double cosR = Math.Cos(rotationRadians);
            double sinR = Math.Sin(rotationRadians);

            double rotatedX = scaledX * cosR - scaledY * sinR;
            double rotatedY = scaledX * sinR + scaledY * cosR;

            // Apply translation (to insert.Location)
            // Assuming ZScaleFactor is 1.0 as it's not directly available in DxfInsert in IxMilia.Dxf
            // The Z coordinate of the block entity is added to the insert's Z location.
            return new DxfPoint(rotatedX + location.X, rotatedY + location.Y, point.Z * 1.0 + location.Z);
        }

        // Helper to get bounds of transformed corners of a local bounding box
        private (double minX, double minY, double maxX, double maxY)? GetTransformedBounds(
            (double minX, double minY, double maxX, double maxY) localBounds,
            DxfPoint location, double xScale, double yScale, double rotationDegrees)
        {
            // Define the four corner points of the local bounding box in 2D (Z is ignored for transformation of bounds)
            DxfPoint c1 = new DxfPoint(localBounds.minX, localBounds.minY, 0);
            DxfPoint c2 = new DxfPoint(localBounds.maxX, localBounds.minY, 0);
            DxfPoint c3 = new DxfPoint(localBounds.minX, localBounds.maxY, 0);
            DxfPoint c4 = new DxfPoint(localBounds.maxX, localBounds.maxY, 0);

            // Transform each corner point
            // Note: The Z coordinate of the original local bounds is not used here,
            // as we are transforming a 2D bounding box. The Z of the insert location
            // will be added by TransformPoint, but the resulting bounds are still 2D (minX, minY, maxX, maxY).
            DxfPoint t1 = TransformPoint(c1, location, xScale, yScale, rotationDegrees);
            DxfPoint t2 = TransformPoint(c2, location, xScale, yScale, rotationDegrees);
            DxfPoint t3 = TransformPoint(c3, location, xScale, yScale, rotationDegrees);
            DxfPoint t4 = TransformPoint(c4, location, xScale, yScale, rotationDegrees);

            // Find the min/max of the transformed X and Y coordinates
            double resultMinX = Math.Min(Math.Min(t1.X, t2.X), Math.Min(t3.X, t4.X));
            double resultMinY = Math.Min(Math.Min(t1.Y, t2.Y), Math.Min(t3.Y, t4.Y));
            double resultMaxX = Math.Max(Math.Max(t1.X, t2.X), Math.Max(t3.X, t4.X));
            double resultMaxY = Math.Max(Math.Max(t1.Y, t2.Y), Math.Max(t3.Y, t4.Y));
            return (resultMinX, resultMinY, resultMaxX, resultMaxY);
        }


        private bool PromptAndTrySaveChanges()
        {
            if (!isConfigurationDirty)
            {
                return true; // No unsaved changes, proceed.
            }

            MessageBoxResult result = MessageBox.Show(
                "You have unsaved changes. Would you like to save the current configuration?",
                "Unsaved Changes",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning);

            switch (result)
            {
                case MessageBoxResult.Yes:
                    // PerformSaveOperation() will be implemented in a later step.
                    // Assume it returns true if save was successful, false if user cancelled or error.
                    bool saveSuccess = PerformSaveOperation();
                    if (saveSuccess)
                    {
                        isConfigurationDirty = false; // Reset dirty flag only if save was successful
                        return true; // Proceed with the original action
                    }
                    else
                    {
                        return false; // Save failed or was cancelled, so cancel the original action
                    }
                case MessageBoxResult.No:
                    return true; // User chose not to save, proceed with the original action
                case MessageBoxResult.Cancel:
                    return false; // User cancelled the original action
                default:
                    return false; // Should not happen
            }
        }


        private void HandleError(Exception ex, string action) { /* ... (No change) ... */ }


        private bool PointEquals(DxfPoint p1, DxfPoint p2, double tolerance = 0.001)
        {
            return Math.Abs(p1.X - p2.X) < tolerance &&
                   Math.Abs(p1.Y - p2.Y) < tolerance &&
                   Math.Abs(p1.Z - p2.Z) < tolerance;
        }

        private bool AreEntitiesGeometricallyEquivalent(DxfEntity entity1, DxfEntity entity2, double tolerance = 0.001)
        {
            if (entity1 == null || entity2 == null)
            {
                Debug.WriteLineIf(entity1 == null || entity2 == null, $"[DEBUG] AreEntitiesGeometricallyEquivalent: One or both entities are null. Entity1: {(entity1 == null ? "null" : entity1.GetType().Name)}, Entity2: {(entity2 == null ? "null" : entity2.GetType().Name)}");
                return false;
            }
            if (entity1.GetType() != entity2.GetType())
            {
                Debug.WriteLine($"[DEBUG] AreEntitiesGeometricallyEquivalent: Entity types differ: {entity1.GetType().Name} vs {entity2.GetType().Name}");
                return false;
            }

            Debug.WriteLine($"[DEBUG] AreEntitiesGeometricallyEquivalent: Comparing two {entity1.GetType().Name}");

            switch (entity1)
            {
                case DxfLine line1 when entity2 is DxfLine line2:
                    bool p1p1 = PointEquals(line1.P1, line2.P1, tolerance);
                    bool p2p2 = PointEquals(line1.P2, line2.P2, tolerance);
                    bool p1p2 = PointEquals(line1.P1, line2.P2, tolerance);
                    bool p2p1 = PointEquals(line1.P2, line2.P1, tolerance);
                    Debug.WriteLine($"[DEBUG] LineCompare: L1P1={line1.P1}, L1P2={line1.P2} | L2P1={line2.P1}, L2P2={line2.P2}");
                    Debug.WriteLine($"[DEBUG] LineCompare: (P1s match: {p1p1}, P2s match: {p2p2}) OR (P1-L2P2 match: {p1p2}, P2-L2P1 match: {p2p1})");
                    return (p1p1 && p2p2) || (p1p2 && p2p1);

                case DxfCircle circle1 when entity2 is DxfCircle circle2:
                    bool centerMatch = PointEquals(circle1.Center, circle2.Center, tolerance);
                    bool radiusMatch = Math.Abs(circle1.Radius - circle2.Radius) < tolerance;
                    Debug.WriteLine($"[DEBUG] CircleCompare: C1=({circle1.Center}, R={circle1.Radius}) | C2=({circle2.Center}, R={circle2.Radius})");
                    Debug.WriteLine($"[DEBUG] CircleCompare: CenterMatch={centerMatch}, RadiusMatch={radiusMatch}");
                    return centerMatch && radiusMatch;

                case DxfArc arc1 when entity2 is DxfArc arc2:
                    // TODO: Robust angle comparison (normalize to 0-360, handle wrap-around)
                    // For now, using modulo which is not perfectly robust for all cases like 0 vs 360.
                    // A better way: convert angles to vectors or check if one angle is equivalent to other + k*360.
                    double normalizedStartAngle1 = (arc1.StartAngle % 360 + 360) % 360;
                    double normalizedEndAngle1 = (arc1.EndAngle % 360 + 360) % 360;
                    double normalizedStartAngle2 = (arc2.StartAngle % 360 + 360) % 360;
                    double normalizedEndAngle2 = (arc2.EndAngle % 360 + 360) % 360;

                    bool arcCenterMatch = PointEquals(arc1.Center, arc2.Center, tolerance);
                    bool arcRadiusMatch = Math.Abs(arc1.Radius - arc2.Radius) < tolerance;
                    bool arcStartAngleMatch = Math.Abs(normalizedStartAngle1 - normalizedStartAngle2) < tolerance || Math.Abs(normalizedStartAngle1 - normalizedStartAngle2 - 360) < tolerance || Math.Abs(normalizedStartAngle1 - normalizedStartAngle2 + 360) < tolerance;
                    bool arcEndAngleMatch = Math.Abs(normalizedEndAngle1 - normalizedEndAngle2) < tolerance || Math.Abs(normalizedEndAngle1 - normalizedEndAngle2 - 360) < tolerance || Math.Abs(normalizedEndAngle1 - normalizedEndAngle2 + 360) < tolerance;

                    Debug.WriteLine($"[DEBUG] ArcCompare: A1=C({arc1.Center}),R({arc1.Radius}),SA({arc1.StartAngle}),EA({arc1.EndAngle})");
                    Debug.WriteLine($"[DEBUG] ArcCompare: A2=C({arc2.Center}),R({arc2.Radius}),SA({arc2.StartAngle}),EA({arc2.EndAngle})");
                    Debug.WriteLine($"[DEBUG] ArcCompare: NormA1=SA({normalizedStartAngle1}),EA({normalizedEndAngle1}) | NormA2=SA({normalizedStartAngle2}),EA({normalizedEndAngle2})");
                    Debug.WriteLine($"[DEBUG] ArcCompare: CenterMatch={arcCenterMatch}, RadiusMatch={arcRadiusMatch}, StartAngleMatch={arcStartAngleMatch}, EndAngleMatch={arcEndAngleMatch}");
                    return arcCenterMatch && arcRadiusMatch && arcStartAngleMatch && arcEndAngleMatch;

                case DxfLwPolyline poly1 when entity2 is DxfLwPolyline poly2:
                    Debug.WriteLine($"[DEBUG] LWPolylineCompare: VCount1={poly1.Vertices.Count}, VCount2={poly2.Vertices.Count}, Closed1={poly1.IsClosed}, Closed2={poly2.IsClosed}");
                    if (poly1.Vertices.Count != poly2.Vertices.Count || poly1.IsClosed != poly2.IsClosed) return false;
                    for(int i=0; i < poly1.Vertices.Count; i++)
                    {
                        var v1 = poly1.Vertices[i];
                        var v2 = poly2.Vertices[i];
                        // LwPolyline vertices are DxfLwPolylineVertex, which have X, Y, Bulge. Z is from polyline's Elevation.
                        // Using PointEquals for X,Y comparison by creating temporary DxfPoints.
                        bool xyMatch = PointEquals(new DxfPoint(v1.X, v1.Y, 0), new DxfPoint(v2.X, v2.Y, 0), tolerance);
                        bool bulgeMatch = Math.Abs(v1.Bulge - v2.Bulge) < tolerance;
                        Debug.WriteLine($"[DEBUG] LWPolylineCompare: V{i} P1=({v1.X},{v1.Y},B={v1.Bulge}) | P2=({v2.X},{v2.Y},B={v2.Bulge}) | XYMatch={xyMatch}, BulgeMatch={bulgeMatch}");
                        if (!xyMatch || !bulgeMatch)
                        {
                            return false;
                        }
                    }
                    return true;
                // TODO: Add other entity types as needed
                default:
                    Debug.WriteLine($"[WARNING] AreEntitiesGeometricallyEquivalent: Unhandled entity type {entity1.GetType().Name} for comparison.");
                    return false;
            }
        }

        private void ReconcileTrajectoryEntities(Models.Configuration config, DxfFile? currentDoc)
        {
            if (config == null || currentDoc == null || config.SprayPasses == null || !currentDoc.Entities.Any())
            {
                Debug.WriteLine("[DEBUG] ReconcileTrajectoryEntities: Skipping reconciliation due to null config, doc, passes, or empty document entities.");
                return;
            }

            Debug.WriteLine($"[DEBUG] ReconcileTrajectoryEntities: Starting. Document has {currentDoc.Entities.Count()} entities.");
            // Debug.WriteLine("[JULES_DEBUG] ReconcileTrajectoryEntities: Entering method.");

            // Create a list of available entities from the document to "consume" as they are matched
            // This helps handle cases where multiple identical geometric entities might exist in the DXF,
            // ensuring each trajectory maps to a unique live entity if possible.
            List<DxfEntity> availableDocEntities = new List<DxfEntity>(currentDoc.Entities);

            foreach (var pass in config.SprayPasses)
            {
                if (pass.Trajectories == null)
                {
                    // Debug.WriteLine($"[JULES_DEBUG] ReconcileTrajectoryEntities: Pass '{pass.PassName}' has null trajectories. Skipping.");
                    continue;
                }
                // Debug.WriteLine($"[JULES_DEBUG] ReconcileTrajectoryEntities: Processing pass '{pass.PassName}'. Initial trajectory order:");
                // for(int k=0; k < pass.Trajectories.Count; k++)
                // {
                //     Debug.WriteLine($"[JULES_DEBUG]   Pre-Reconcile: Pass[{pass.PassName}]-Trajectory[{k}]: {pass.Trajectories[k].ToString()}");
                // }

                for (int i = 0; i < pass.Trajectories.Count; i++)
                {
                    var trajectory = pass.Trajectories[i];
                    if (trajectory.OriginalDxfEntity == null && trajectory.PrimitiveType != "Polygon")
                    {
                        Debug.WriteLine($"[DEBUG] ReconcileTrajectoryEntities: Trajectory {i} in pass '{pass.PassName}' has null OriginalDxfEntity and is not a Polygon.");
                        continue;
                    }

                    DxfEntity? matchedEntity = null;
                    int matchedEntityIndexInAvailableList = -1;

                    for (int j = 0; j < availableDocEntities.Count; j++)
                    {
                        if (trajectory.PrimitiveType == "Polygon")
                        {
                            if (availableDocEntities[j] is DxfLwPolyline polyline)
                            {
                                // A simple comparison for polygons could be to check if they have the same number of vertices
                                // and if the first vertex is the same. This is not a robust check, but it's a start.
                                if (polyline.Vertices.Count == trajectory.Points.Count &&
                                    PointEquals(new DxfPoint(polyline.Vertices[0].X, polyline.Vertices[0].Y, polyline.Elevation), new DxfPoint(trajectory.Points[0].X, trajectory.Points[0].Y, trajectory.PolygonZ)))
                                {
                                    matchedEntity = availableDocEntities[j];
                                    matchedEntityIndexInAvailableList = j;
                                    break;
                                }
                            }
                        }
                        else if (AreEntitiesGeometricallyEquivalent(trajectory.OriginalDxfEntity, availableDocEntities[j]))
                        {
                            matchedEntity = availableDocEntities[j];
                            matchedEntityIndexInAvailableList = j;
                            break;
                        }
                    }

                    if (matchedEntity != null)
                    {
                        trajectory.OriginalDxfEntity = matchedEntity; // Update reference to the live entity from the document
                        // availableDocEntities.RemoveAt(matchedEntityIndexInAvailableList); // Allow re-matching for shared entities across passes
                        Debug.WriteLine($"[DEBUG] ReconcileTrajectoryEntities: Reconciled trajectory entity: {matchedEntity.GetType().Name} (Index in availableDocEntities was {matchedEntityIndexInAvailableList}, not removing).");
                    }
                    else
                    {
                        // If no match, the trajectory.OriginalDxfEntity remains the deserialized instance.
                        // Highlighting will likely fail for this specific entity.
                        Debug.WriteLine($"[WARNING] ReconcileTrajectoryEntities: Could not find a matching live entity for deserialized {trajectory.PrimitiveType}.");
                    }
                }
                // Debug.WriteLine($"[JULES_DEBUG] ReconcileTrajectoryEntities: Finished processing pass '{pass.PassName}'. Final trajectory order for this pass:");
                // for(int k=0; k < pass.Trajectories.Count; k++)
                // {
                //     Debug.WriteLine($"[JULES_DEBUG]   Post-Reconcile: Pass[{pass.PassName}]-Trajectory[{k}]: {pass.Trajectories[k].ToString()}");
                // }
            }
            Debug.WriteLine("[DEBUG] ReconcileTrajectoryEntities: Finished.");
            // Debug.WriteLine("[JULES_DEBUG] ReconcileTrajectoryEntities: Exiting method.");
        }


        /// <summary>
        /// Calculates the bounding box of an arc segment defined by two points and a bulge value.
        /// </summary>
        /// <param name="p1">Start point of the arc segment.</param>
        /// <param name="p2">End point of the arc segment.</param>
        /// <param name="bulge">Bulge value. Positive for CCW arc, negative for CW.</param>
        /// <returns>A Rect representing the bounding box of the arc segment, or Rect.Empty if calculation fails.</returns>
        private Rect GetArcSegmentBoundsFromBulge(Point p1, Point p2, double bulge)
        {
            if (Math.Abs(bulge) < 1e-9) // Treat as a straight line segment
            {
                return new Rect(p1, p2);
            }

            double dx = p2.X - p1.X;
            double dy = p2.Y - p1.Y;
            double chordLengthSquared = dx * dx + dy * dy;
            double chordLength = Math.Sqrt(chordLengthSquared);

            if (chordLength < 1e-9) // Points are coincident
            {
                return new Rect(p1, p2);
            }

            // Angle of the chord vector
            double chordAngle = Math.Atan2(dy, dx);

            // Included angle of the arc segment (theta)
            // bulge = tan(theta / 4) ==> theta = 4 * atan(bulge)
            double includedAngle = 4.0 * Math.Atan(bulge);

            // Radius of the arc
            // R = C / (2 * sin(theta / 2))
            double radius = chordLength / (2.0 * Math.Sin(includedAngle / 2.0));
            if (double.IsInfinity(radius) || double.IsNaN(radius)) return new Rect(p1, p2); // Should be caught by bulge or chord checks

            // Arc center calculation
            // Midpoint of the chord
            Point midPointChord = new Point(p1.X + dx / 2.0, p1.Y + dy / 2.0);

            // Distance from chord midpoint to arc center (h)
            // h = R * cos(theta/2) or sqrt(R^2 - (C/2)^2)
            double h = radius * Math.Cos(includedAngle / 2.0);
            // If bulge > 0 (CCW), center is to the "left" of chord vector P1->P2
            // If bulge < 0 (CW), center is to the "right"
            // The sign of bulge also affects the sign of 'h' in some formulations, here h is distance.
            // We need to adjust the perpendicular direction based on bulge.

            double perpDx = -dy / chordLength; // Normalized perpendicular vector component
            double perpDy = dx / chordLength;  // Normalized perpendicular vector component

            // For CCW arc (bulge > 0), center is (midPoint.X - h * perpDx_normalized, midPoint.Y - h * perpDy_normalized)
            // where perp vector is (-dy, dx).
            // If bulge is negative, the center is on the other side.
            // Simplified: sagitta vector direction depends on bulge.
            // Using a common formula: Pc = Pm - (R*cos(theta/2)) * PerpendicularNormalized(V_chord) * sign(bulge)
            // Let's use a more direct center finding:
            // Angle from midpoint of chord to center is chordAngle - PI/2 (for CCW) or + PI/2 (for CW)
            // This 'h' can be thought of as signed based on bulge for offset direction
            double offsetFactor = h * Math.Sign(bulge); // This might be incorrect, h is distance, sign of bulge defines concavity

            Point center = new Point(
                midPointChord.X - offsetFactor * (dy / chordLength), // perp component X scaled by h
                midPointChord.Y + offsetFactor * (dx / chordLength)  // perp component Y scaled by h
            );

            // Start and end angles relative to the arc center
            double startAngle = Math.Atan2(p1.Y - center.Y, p1.X - center.X);
            double endAngle = Math.Atan2(p2.Y - center.Y, p2.Y - center.Y);

            // Normalize angles to be [0, 2*PI)
            // startAngle = (startAngle + 2 * Math.PI) % (2 * Math.PI);
            // endAngle = (endAngle + 2 * Math.PI) % (2 * Math.PI);

            // Determine sweep direction and adjust endAngle for full sweep
            // If bulge < 0, arc is CW. If bulge > 0, arc is CCW.
            // We want to sweep from startAngle to endAngle in the direction of the bulge.

            if (bulge < 0) // Clockwise
            {
                if (endAngle > startAngle)
                    startAngle += 2 * Math.PI; // Ensure sweep is CW
            }
            else // Counter-clockwise
            {
                if (endAngle < startAngle)
                    endAngle += 2 * Math.PI; // Ensure sweep is CCW
            }

            // Initialize bounding box with start and end points
            double minX = Math.Min(p1.X, p2.X);
            double minY = Math.Min(p1.Y, p2.Y);
            double maxX = Math.Max(p1.X, p2.X);
            double maxY = Math.Max(p1.Y, p2.Y);

            // Check cardinal points (0, 90, 180, 270 degrees in circle's coordinate system)
            // if they fall within the arc segment.
            Action<double> checkAngle = (angle) =>
            {
                bool angleInSweep;
                if (bulge > 0) // CCW
                {
                    // Normalize angle to be relative to startAngle for CCW sweep
                    double normalizedAngle = angle;
                    while (normalizedAngle < startAngle) normalizedAngle += 2 * Math.PI;
                    angleInSweep = normalizedAngle >= startAngle && normalizedAngle <= endAngle;
                }
                else // CW
                {
                    // Normalize angle to be relative to startAngle for CW sweep
                    double normalizedAngle = angle;
                    while (normalizedAngle > startAngle) normalizedAngle -= 2 * Math.PI;
                    angleInSweep = normalizedAngle <= startAngle && normalizedAngle >= endAngle;
                }

                if (angleInSweep)
                {
                    double x = center.X + radius * Math.Cos(angle);
                    double y = center.Y + radius * Math.Sin(angle);
                    minX = Math.Min(minX, x);
                    minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x);
                    maxY = Math.Max(maxY, y);
                }
            };

            checkAngle(0);                         // Point at positive X-axis from center
            checkAngle(Math.PI / 2.0);             // Point at positive Y-axis from center
            checkAngle(Math.PI);                   // Point at negative X-axis from center
            checkAngle(3.0 * Math.PI / 2.0);       // Point at negative Y-axis from center

            if (minX > maxX || minY > maxY) return Rect.Empty; // Should not happen with valid inputs

            return new Rect(new Point(minX, minY), new Point(maxX, maxY));
        }

        /// <summary>
        /// Helper method to get the bounding box of a DxfEntity as a Rect.
        /// </summary>
        /// <param name="entity">The DXF entity.</param>
        /// <returns>A Rect representing the entity's bounding box in DXF coordinates, or Rect.Empty if bounds cannot be determined.</returns>
        private Rect GetDxfEntityRect(DxfEntity entity)
        {
            if (entity == null)
                return Rect.Empty;

            var bounds = CalculateEntityBoundsSimple(entity);
            if (!bounds.IsEmpty)
            {
                return bounds;
            }
            return Rect.Empty;
        }


        private void StartTestRunButton_Click(object sender, RoutedEventArgs e)
        {
            // Determine selected speed mode for the confirmation message
            string selectedSpeedModeName = "Slow"; // Default
            if (StandardSpeedRadioButton.IsChecked == true)
            {
                selectedSpeedModeName = "Standard";
            }

            string confirmationMessage = $"The robot will start a test run in '{selectedSpeedModeName} Speed' mode. Please ensure the robot's workspace is clear of any obstructions or personnel.\n\nDo you want to proceed?";
            MessageBoxResult confirmResult = MessageBox.Show(confirmationMessage, "Confirm Test Run", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (confirmResult == MessageBoxResult.No)
            {
                AppLogger.Log("Test run cancelled by user at confirmation dialog.");
                StatusTextBlock.Text = "Test run cancelled by user.";
                return;
            }

            AppLogger.Log($"User confirmed test run initiation ({selectedSpeedModeName} speed).");

            if (!_modbusService.IsConnected)
            {
                string msg = "Not connected to Modbus server. Please connect first to start a test run.";
                AppLogger.Log(msg, LogLevel.Warning);
                MessageBox.Show(msg, "Modbus Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Check Robot Status (Address 1000)
            ushort robotStatusAddress = 1000;
            ModbusReadInt16Result statusResult = _modbusService.ReadHoldingRegisterInt16(robotStatusAddress);

            if (!statusResult.Success)
            {
                string msg = $"Failed to read robot status for test run: {statusResult.Message}";
                AppLogger.Log(msg, LogLevel.Error);
                MessageBox.Show(msg, "Modbus Read Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            short robotStatus = statusResult.Value;
            if (robotStatus != 1) // 1 means stopped/ready
            {
                string msg = $"Cannot start test run: Robot is not in a stopped/ready state (Current status at {robotStatusAddress}: {robotStatus}).";
                AppLogger.Log(msg, LogLevel.Warning);
                MessageBox.Show(msg, "Robot Not Ready", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Determine selected speed mode and set it (Address 1001)
            ushort speedModeAddress = 1001;
            int speedModeValue = 11; // Default to Slow Speed (11)
            string speedModeName = "Slow";

            if (StandardSpeedRadioButton.IsChecked == true)
            {
                speedModeValue = 22; // Standard Speed (22)
                speedModeName = "Standard";
            }

            AppLogger.Log($"Setting speed mode for Test Run to {speedModeName} (Value: {speedModeValue}) at address {speedModeAddress}.");
            ModbusResponse speedSetResponse = _modbusService.WriteSingleShortRegister(speedModeAddress, (short)speedModeValue);

            if (!speedSetResponse.Success)
            {
                string msg = $"Failed to set speed mode on robot for test run: {speedSetResponse.Message}";
                AppLogger.Log(msg, LogLevel.Error);
                MessageBox.Show(msg, "Modbus Write Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Add 50ms delay
            System.Threading.Thread.Sleep(50);

            // Trigger Test Run (Address 1002)
            ushort triggerAddress = 1002;
            short triggerValue = 33;
            AppLogger.Log($"Triggering Test Run (Value: {triggerValue}) at address {triggerAddress}.");
            ModbusResponse triggerResponse = _modbusService.WriteSingleShortRegister(triggerAddress, triggerValue);

            if (!triggerResponse.Success)
            {
                string msg = $"Failed to trigger test run on robot: {triggerResponse.Message}";
                AppLogger.Log(msg, LogLevel.Error);
                MessageBox.Show(msg, "Modbus Write Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            else
            {
                AppLogger.Log($"Test run ({speedModeName} speed) initiated successfully.");
                StatusTextBlock.Text = $"Test run ({speedModeName} speed) initiated.";
                MessageBox.Show($"Test run ({speedModeName} speed) initiated successfully.", "Test Run Started", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private Rect GetTransformedBounds(Rect originalBounds, DxfPoint insertPoint, double scaleX, double scaleY, double rotationDegrees)
        {
            if (originalBounds.IsEmpty)
            {
                return Rect.Empty;
            }

            // Create transformation matrix
            var transform = new Matrix();
            
            // Apply transformations in order: scale, rotate, translate
            transform.Scale(scaleX, scaleY);
            transform.Rotate(rotationDegrees);
            transform.Translate(insertPoint.X, insertPoint.Y);

            // Transform the corners of the original bounds
            var points = new[]
            {
                new System.Windows.Point(originalBounds.Left, originalBounds.Top),
                new System.Windows.Point(originalBounds.Right, originalBounds.Top),
                new System.Windows.Point(originalBounds.Right, originalBounds.Bottom),
                new System.Windows.Point(originalBounds.Left, originalBounds.Bottom)
            };

            // Transform all points
            transform.Transform(points);

            // Calculate new bounds
            double minX = points.Min(p => p.X);
            double minY = points.Min(p => p.Y);
            double maxX = points.Max(p => p.X);
            double maxY = points.Max(p => p.Y);

            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }

        private Trajectory CreatePolygonTrajectoryFromPolyline(DxfLwPolyline polyline)
        {
            var newTrajectory = new Trajectory
            {
                OriginalDxfEntity = polyline,
                EntityType = polyline.GetType().Name,
                IsReversed = false,
                PrimitiveType = "Polygon",
                PolygonZ = polyline.Elevation
            };

            var vertices = polyline.Vertices.Select(v => new Point3D(v.X, v.Y, polyline.Elevation)).ToList();
            int startIndex = FindBottomLeftVertexIndex(vertices.Select(p => new Point(p.X, p.Y)).ToList());
            var orderedVertices = new List<Point3D>();
            for (int i = 0; i < vertices.Count; i++)
            {
                orderedVertices.Add(vertices[(startIndex + i) % vertices.Count]);
            }
            newTrajectory.Points = orderedVertices;

            // No need to call PopulateTrajectoryPoints for polygons as it's handled differently
            newTrajectory.Runtime = TrajectoryUtils.CalculateMinRuntime(newTrajectory);

            return newTrajectory;
        }

        private int FindBottomLeftVertexIndex(List<Point> vertices)
        {
            if (vertices == null || vertices.Count == 0) return -1;

            int bottomLeftIndex = 0;
            double minDistance = double.MaxValue;

            for (int i = 0; i < vertices.Count; i++)
            {
                var p = vertices[i];
                // Simple distance to origin (0,0) - assuming Y is not flipped in this context
                // A better approach would be to transform to screen coordinates if Y is flipped
                double distance = Math.Sqrt(p.X * p.X + p.Y * p.Y);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    bottomLeftIndex = i;
                }
            }
            return bottomLeftIndex;
        }

        private void FitToViewButton_Click(object sender, RoutedEventArgs e)
        {
            AppLogger.Log("[USER ACTION] FitToViewButton_Click called.", LogLevel.Debug);
            PerformFitToView();
        }

        private void Show3DViewButton_Click(object sender, RoutedEventArgs e)
        {
            var robot3DView = new Robot3DView();
            robot3DView.Show();
        }

        private Rect GetDxfInsertBounds(DxfInsert insert)
        {
            AppLogger.Log($"GetDxfInsertBounds: Processing Insert Name='{insert.Name}'.", LogLevel.Debug);
            if (_currentDxfDocument == null)
            {
                AppLogger.Log($"GetDxfInsertBounds: Insert '{insert.Name}' - _currentDxfDocument is null. Cannot resolve block. Bounds based on insertion point only.", LogLevel.Warning);
                return new Rect(insert.Location.X, insert.Location.Y, 0, 0);
            }

            DxfBlock? block = _currentDxfDocument.Blocks.FirstOrDefault(b => b.Name == insert.Name);
            if (block == null)
            {
                AppLogger.Log($"GetDxfInsertBounds: Insert '{insert.Name}' - Block with this name not found in DXF. Bounds based on insertion point only.", LogLevel.Warning);
                return new Rect(insert.Location.X, insert.Location.Y, 0, 0);
            }
            if (!block.Entities.Any())
            {
                 AppLogger.Log($"GetDxfInsertBounds: Insert '{insert.Name}' - Block found but contains no entities. Bounds based on insertion point only.", LogLevel.Debug);
                return new Rect(insert.Location.X, insert.Location.Y, 0, 0);
            }
            AppLogger.Log($"GetDxfInsertBounds: Insert '{insert.Name}' - Block found with {block.Entities.Count()} entities. Calculating block content bounds.", LogLevel.Debug);

            Rect blockBounds = Rect.Empty;
            bool hasValidBounds = false;
            int entityInBlockIndex = 0;
            foreach (DxfEntity entityInBlock in block.Entities)
            {
                if (entityInBlock == null)
                {
                    AppLogger.Log($"GetDxfInsertBounds: Insert '{insert.Name}', Block Entity {entityInBlockIndex}: Null entity. Skipping.", LogLevel.Debug);
                    entityInBlockIndex++;
                    continue;
                }
                // Recursive call to CalculateEntityBoundsSimple for entities within the block
                // Log within CalculateEntityBoundsSimple will detail the block entity
                var entityBounds = CalculateEntityBoundsSimple(entityInBlock);
                if (!entityBounds.IsEmpty)
                {
                     AppLogger.Log($"GetDxfInsertBounds: Insert '{insert.Name}', Block Entity {entityInBlockIndex} ({entityInBlock.GetType().Name}): Bounds ({entityBounds.X:F2},{entityBounds.Y:F2}, {entityBounds.Width:F2},{entityBounds.Height:F2})", LogLevel.Debug);
                    if (!hasValidBounds)
                    {
                        blockBounds = entityBounds;
                        hasValidBounds = true;
                    }
                    else
                    {
                        blockBounds.Union(entityBounds);
                    }
                    AppLogger.Log($"GetDxfInsertBounds: Insert '{insert.Name}', Block Entity {entityInBlockIndex}: Aggregated block bounds now ({blockBounds.X:F2},{blockBounds.Y:F2}, {blockBounds.Width:F2},{blockBounds.Height:F2})", LogLevel.Debug);
                } else {
                    AppLogger.Log($"GetDxfInsertBounds: Insert '{insert.Name}', Block Entity {entityInBlockIndex} ({entityInBlock.GetType().Name}): Returned empty bounds.", LogLevel.Debug);
                }
                entityInBlockIndex++;
            }

            if (!hasValidBounds)
            {
                AppLogger.Log($"GetDxfInsertBounds: Insert '{insert.Name}' - No valid entity bounds found within block content. Bounds based on insertion point only.", LogLevel.Debug);
                return new Rect(insert.Location.X, insert.Location.Y, 0, 0);
            }
            AppLogger.Log($"GetDxfInsertBounds: Insert '{insert.Name}' - Final untransformed block content bounds: ({blockBounds.X:F2},{blockBounds.Y:F2}, {blockBounds.Width:F2},{blockBounds.Height:F2})", LogLevel.Debug);

            // Transform the block bounds according to the insert's properties
            var transformedBounds = GetTransformedBounds(blockBounds, insert.Location, insert.XScaleFactor, insert.YScaleFactor, insert.Rotation);
            AppLogger.Log($"GetDxfInsertBounds: Insert '{insert.Name}' - Final transformed bounds for insert: ({transformedBounds.X:F2},{transformedBounds.Y:F2}, {transformedBounds.Width:F2},{transformedBounds.Height:F2})", LogLevel.Debug);
            return transformedBounds;
        }

        private void UpdateShapesOnCanvas(List<System.Windows.Shapes.Shape?> shapes)
        {
            if (shapes == null)
            {
                AppLogger.Log("[MainWindow] UpdateShapesOnCanvas: shapes list is null.", LogLevel.Warning);
                return;
            }

            AppLogger.Log($"[MainWindow] UpdateShapesOnCanvas: Processing {shapes.Count} shapes.", LogLevel.Debug);
            var nonNullShapes = shapes.Where(s => s != null).Select(s => s!).ToList();
            AppLogger.Log($"[MainWindow] UpdateShapesOnCanvas: Found {nonNullShapes.Count} non-null shapes.", LogLevel.Debug);

            int entityIndex = 0;
            foreach (var shape in nonNullShapes)
            {
                shape.Stroke = DefaultStrokeBrush;
                shape.StrokeThickness = DefaultStrokeThickness;
                if (shape is System.Windows.Shapes.Path path)
                {
                    path.Fill = Brushes.Transparent;
                }

                // Add event handler and map to entity
                if (_currentDxfDocument != null && entityIndex < _currentDxfDocument.Entities.Count())
                {
                    var entity = _currentDxfDocument.Entities.ElementAt(entityIndex);
                    shape.MouseLeftButtonDown += OnCadEntityClicked;
                    _wpfShapeToDxfEntityMap[shape] = entity;
                }
                entityIndex++;
            }

            CadCanvas.Children.Clear();
            foreach (var shape in nonNullShapes)
            {
                CadCanvas.Children.Add(shape);
            }

            // Force layout update
            CadCanvas.UpdateLayout();
            CadCanvas.InvalidateVisual();

            // Update bounding box
            if (nonNullShapes.Any())
            {
                PerformFitToView();
            }
        }
    }
}
