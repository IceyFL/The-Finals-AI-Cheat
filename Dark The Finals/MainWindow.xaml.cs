using Paster.Class;
using Paster.UserController;
using PasterAimbot;
using Newtonsoft.Json;
using System;
using InputBindings;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
namespace Paster
{
    public partial class MainWindow : Window
    {
        double sens2;
        bool leftButtonDown = (Control.MouseButtons & MouseButtons.Left) == MouseButtons.Left;
        bool rightButtonDown = (Control.MouseButtons & MouseButtons.Right) == MouseButtons.Right;

        private string lastLoadedModel = "N/A";

        private readonly BrushConverter brushcolor = new();

        private int TimeSinceLastClick = 0;
        private DateTime LastClickTime = DateTime.MinValue;

        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_MOVE = 0x0001; // Movement flag

        private static int ScreenWidth = Screen.PrimaryScreen.Bounds.Width;
        private static int ScreenHeight = Screen.PrimaryScreen.Bounds.Height;   

        private AIModel _onnxModel;
        private InputBindingManager bindingManager;
        private bool IsHolding_Binding = false;
        private CancellationTokenSource cts;
        private enum MenuPosition
        {
            AimMenu,
        }

        // Changed to Dynamic from Double because it was making the Config System hard to rework :/
        public Dictionary<string, dynamic> PasterSettings = new()
        {
            { "FOV_Size", 145 },
            { "Mouse_Sens", 0.65 },
            { "Mouse_SensY", 0.75 },
            { "Y_Offset", 0 },
            { "X_Offset", 0 },
            { "AI_Min_Conf", 25 }
        };

        private Dictionary<string, bool> toggleState = new()
        {
            { "AimbotToggle", false }

        };

        private Thickness WinCenter = new(0, 0, 0, 0);

        public MainWindow()
        {
            InitializeComponent();
            InputBindingManager.init();

            // Check to see if certain items are installed
            RequirementsManager RM = new();
            if (!RM.IsVCRedistInstalled())
            {
                System.Windows.MessageBox.Show("Visual C++ Redistributables x64 are not installed on this device, please install them before using to avoid issues.", "Load Error");
                Process.Start("https://aka.ms/vs/17/release/vc_redist.x64.exe");
                System.Windows.Application.Current.Shutdown();
            }

            // Check for required folders
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            // Setup key/mouse hook
            bindingManager = new InputBindingManager();
            bindingManager.SetupDefault("Right");
            bindingManager.OnBindingPressed += (binding) => { IsHolding_Binding = true; };
            bindingManager.OnBindingReleased += (binding) => { IsHolding_Binding = false; };

            // Load UI
            InitializeMenuPositions();
            ReloadMenu();

            // Start the loop that runs the model
            Task.Run(() => StartModelCaptureLoop());
            Task.Run(() => TitleLoop());
            InitializeModel();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        { }

        #region Mouse Movement / Clicking Handler

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, int dwExtraInfo);

        private static Point CubicBezier(Point start, Point end, Point control1, Point control2, double t)
        {
            double u = 1 - t;
            double tt = t * t;
            double uu = u * u;
            double uuu = uu * u;
            double ttt = tt * t;

            double x = uuu * start.X + 3 * uu * t * control1.X + 3 * u * tt * control2.X + ttt * end.X;
            double y = uuu * start.Y + 3 * uu * t * control1.Y + 3 * u * tt * control2.Y + ttt * end.Y;

            return new Point((int)x, (int)y);
        }

        private void MoveCrosshair(int detectedX, int detectedY)
        {
            double Alpha = PasterSettings["Mouse_Sens"];
            double Beta = PasterSettings["Mouse_SensY"];

            int halfScreenWidth = ScreenWidth / 2;
            int halfScreenHeight = ScreenHeight / 2;

            Random random = new Random();
            int RandomX = random.Next(-5, 5);
            int RandomY = random.Next(-5, 5);

            int targetX = (detectedX - halfScreenWidth) + RandomX;
            int targetY = (detectedY - halfScreenHeight) + RandomY;

            // Aspect ratio correction factor
            double aspectRatioCorrection = (double)ScreenWidth / ScreenHeight;
            targetY = (int)(targetY * aspectRatioCorrection);

            // Define Bezier curve control points
            Point start = new(0, 0); // Current cursor position (locked to center screen)
            Point end = new(targetX, targetY);
            Point control1 = new Point(start.X + (end.X - start.X) / 3, 0);
            Point control2 = new Point(start.X + 2 * (end.X - start.X) / 3, 0);
            Point control3 = new Point(0, start.Y + (end.Y - start.Y) / 3);
            Point control4 = new Point(0, start.Y + 2 * (end.Y - start.Y) / 3);

            // Calculate new position along the Bezier curve
            Point newPosition = CubicBezier(start, end, control1, control2, 1 - Alpha);
            Point newPosition2 = CubicBezier(start, end, control3, control4, 1 - Beta);

            mouse_event(MOUSEEVENTF_MOVE, (uint)newPosition.X, (uint)newPosition2.Y, 0, 0);
        }

        #endregion Mouse Movement / Clicking Handler

        #region Aim Aligner Main and Loop

        public async Task ModelCapture()
        {
            var closestPrediction = await _onnxModel.GetClosestPredictionToCenterAsync();
            if (closestPrediction == null)
            {
                return;
            }
            float scaleX = (float)ScreenWidth / 640f;
            float scaleY = (float)ScreenHeight / 640f;

            double YOffset = PasterSettings["Y_Offset"];
            double XOffset = PasterSettings["X_Offset"];
            int detectedX = (int)((closestPrediction.Rectangle.X + closestPrediction.Rectangle.Width / 2) * scaleX + XOffset);
            int detectedY = (int)((closestPrediction.Rectangle.Y + closestPrediction.Rectangle.Height / 2) * scaleY + YOffset);

            if (IsHolding_Binding)
                MoveCrosshair(detectedX, detectedY);
        }

        private async Task TitleLoop()
        {
            cts = new CancellationTokenSource();

            while (!cts.Token.IsCancellationRequested)
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Random random = new Random();
                    int randomNumber = random.Next(1, 10000);
                    string title = randomNumber.ToString("D4");
                    this.Title = title;
                });
                await Task.Delay(2000);
                }
            }

        private async Task StartModelCaptureLoop()
        {
            // Create a new CancellationTokenSource
            cts = new CancellationTokenSource();

            while (!cts.Token.IsCancellationRequested)
            {
                if (toggleState["AimbotToggle"] && IsHolding_Binding)
                {
                    await ModelCapture();
                }
                await Task.Delay(1);
            }
        }

        #endregion Aim Aligner Main and Loop


        #region Menu Initialization and Setup

        private void InitializeMenuPositions()
        {
            AimMenu.Margin = new Thickness(0, 0, 0, 0);
        }

        private void SetupToggle(AToggle toggle, Action<bool> action, bool initialState)
        {
            toggle.Reader.Tag = initialState;
            (initialState ? (Action)(() => toggle.EnableSwitch()) : () => toggle.DisableSwitch())();

            toggle.Reader.Click += (s, x) =>
            {
                bool currentState = (bool)toggle.Reader.Tag;
                toggle.Reader.Tag = !currentState;
                action.Invoke(!currentState);
                SetToggleState(toggle);
            };
        }

        private void SetToggleState(AToggle toggle)
        {
            bool state = (bool)toggle.Reader.Tag;

            // Stop them from turning on anything until the model has been selected.
            if ((toggle.Reader.Name == "AimbotToggle" || toggle.Reader.Name == "ConstantAimbot") && lastLoadedModel == "N/A")
            {
                SetToggleStatesOnModelNotSelected();
                System.Windows.MessageBox.Show("Please restart the cheat.", "Toggle Error");
                return;
            }

            (state ? (Action)toggle.EnableSwitch : (Action)toggle.DisableSwitch)();

            toggleState[toggle.Reader.Name] = state;
        }

        private void SetToggleStatesOnModelNotSelected()
        {
            Bools.AIAimAligner = false;
        }

        #endregion Menu Initialization and Setup

        #region Menu Controls

        private async void Selection_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button clickedButton)
            {
                MenuPosition position = (MenuPosition)Enum.Parse(typeof(MenuPosition), clickedButton.Tag.ToString());
                ResetMenuColors();
                clickedButton.Foreground = (Brush)brushcolor.ConvertFromString("#8b0000");
                ApplyMenuAnimations(position);
                UpdateMenuVisibility(position);
            }
        }

        private void ResetMenuColors()
        {
            Selection1.Foreground = 
                (Brush)brushcolor.ConvertFromString("#8b0000");
        }

        private void ApplyMenuAnimations(MenuPosition position)
        {
            Thickness highlighterMargin = new(0, 30, 414, 0);
            switch (position)
            {
                case MenuPosition.AimMenu:
                    highlighterMargin = new Thickness(10, 30, 414, 0);
                    Animator.ObjectShift(TimeSpan.FromMilliseconds(500), MenuHighlighter, MenuHighlighter.Margin, highlighterMargin);

                    Animator.ObjectShift(TimeSpan.FromMilliseconds(500), AimMenu, AimMenu.Margin, WinCenter);
                    break;
            }
        }

        private void UpdateMenuVisibility(MenuPosition position)
        {
            AimMenu.Visibility = (position == MenuPosition.AimMenu) ? Visibility.Visible : Visibility.Collapsed;
        }

        #endregion Menu Controls


        private void SetMenuState(bool state)
        {
            AimMenu.IsEnabled = state;
        }

        private void LoadAimMenu()
        {
            AToggle Enable_AIAimAligner = new(this, "Aimbot",
                "This will enable the AI's ability to align the aim.");
            Enable_AIAimAligner.Reader.Name = "AimbotToggle";
            SetupToggle(Enable_AIAimAligner, state => Bools.AIAimAligner = state, Bools.AIAimAligner);
            AimScroller.Children.Add(Enable_AIAimAligner);

            AKeyChanger Change_KeyPress = new("Aim key", "Right");
            Change_KeyPress.Reader.Click += (s, x) =>
            {
                Change_KeyPress.KeyNotifier.Content = "Listening..";
                bindingManager.StartListeningForBinding();
            };

            bindingManager.OnBindingSet += (binding) =>
            {
                Change_KeyPress.KeyNotifier.Content = binding;
            };

            AimScroller.Children.Add(Change_KeyPress);

            #region Aiming Configuration

            ASlider MouseSensitivtyX = new ASlider(this, "Smoothing X", "",
                "",
                0.01);

            MouseSensitivtyX.Slider.Minimum = 0.01;
            MouseSensitivtyX.Slider.Maximum = 1;
            MouseSensitivtyX.Slider.Value = PasterSettings["Mouse_Sens"];
            MouseSensitivtyX.Slider.TickFrequency = 0.01;
            MouseSensitivtyX.Slider.ValueChanged += (s, x) =>
            {
                PasterSettings["Mouse_Sens"] = MouseSensitivtyX.Slider.Value;
            };

            AimScroller.Children.Add(MouseSensitivtyX);

            ASlider MouseSensitivtyY = new ASlider(this, "Smoothing Y", "",
                "",
                0.01);

            MouseSensitivtyY.Slider.Minimum = 0.01;
            MouseSensitivtyY.Slider.Maximum = 1;
            MouseSensitivtyY.Slider.Value = PasterSettings["Mouse_SensY"];
            MouseSensitivtyY.Slider.TickFrequency = 0.01;
            MouseSensitivtyY.Slider.ValueChanged += (s, x) =>
            {
                PasterSettings["Mouse_SensY"] = MouseSensitivtyY.Slider.Value;
            };

            AimScroller.Children.Add(MouseSensitivtyY);


            ASlider YOffset = new ASlider(this, "Aim Height", "",
                "",
                1);

            YOffset.Slider.Minimum = 1;
            YOffset.Slider.Maximum = 100;
            YOffset.Slider.Value = PasterSettings["Y_Offset"];
            YOffset.Slider.TickFrequency = 1;
            YOffset.Slider.ValueChanged += (s, x) =>
            {
                PasterSettings["Y_Offset"] = YOffset.Slider.Value;
            };

            AimScroller.Children.Add(YOffset);

            ASlider FovSlider = new(this, "FOV", "",
            "",
            1);

            FovSlider.Slider.Minimum = 10;
            FovSlider.Slider.Maximum = 640;
            FovSlider.Slider.Value = PasterSettings["FOV_Size"];
            FovSlider.Slider.TickFrequency = 1;
            FovSlider.Slider.ValueChanged += (s, x) =>
            {
                double FovSize = FovSlider.Slider.Value;
                PasterSettings["FOV_Size"] = FovSize;
                if (_onnxModel != null)
                {
                    _onnxModel.FovSize = (int)FovSize;
                }
                AwfulPropertyChanger.PostNewFOVSize();
            };

            AimScroller.Children.Add(FovSlider);

            #endregion Aiming Configuration
        }

        private bool ModelLoadDebounce = false;
        private async void InitializeModel()
        {
            try
            {
                if (!ModelLoadDebounce)
                {
                    if (!(Bools.AIAimAligner))
                    {
                        ModelLoadDebounce = true;

                        // Save the embedded resource to a temporary file
                        string tempFilePath = Path.Combine(Path.GetTempPath(), "load.onnx");
                        SaveEmbeddedResourceToFile("Spotify.load.onnx", tempFilePath);

                        // Load the model from the temporary file
                        _onnxModel?.Dispose();
                        _onnxModel = new AIModel(tempFilePath)
                        {
                            ConfidenceThreshold = (float)(PasterSettings["AI_Min_Conf"] / 100.0f),
                            FovSize = (int)PasterSettings["FOV_Size"]
                        };

                        // Load the model from the temporary file
                        lastLoadedModel = "sup";
                        ModelLoadDebounce = false;
                        File.Delete(tempFilePath);
                    }
                }
            }
            catch (Exception)
            {
                System.Windows.MessageBox.Show("Installation is corrupt. Please create a Ticket on discord.", "Load Error");
            }
        }

        private void SaveEmbeddedResourceToFile(string resourceName, string filePath)
        {
            using (Stream resourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                if (resourceStream != null)
                {
                    using (FileStream fileStream = File.Create(filePath))
                    {
                        resourceStream.CopyTo(fileStream);
                    }
                }
                else
                {
                    throw new FileNotFoundException($"Embedded resource '{resourceName}' not found.");
                }
            }
        }

        private void ReloadMenu()
        {
            AimScroller.Children.Clear();

            LoadAimMenu();
        }

        #region Window Controls

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Application.Current.Shutdown();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        private static bool SavedData = false;

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            SavedData = true;
            // Unhook keybind hooker
            bindingManager.StopListening();

            // Dispose (best practice)
            cts.Dispose();

            // Close
            System.Windows.Application.Current.Shutdown();
        }

        #endregion Window Controls
    }
}
