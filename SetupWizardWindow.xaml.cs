using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using PlayniteDisplayManager.Displays;
using PlayniteDisplayManager.Hdr;
using PlayniteDisplayManager.Refresh;
using Forms = System.Windows.Forms;

namespace PlayniteDisplayManager
{
    public partial class SetupWizardWindow : Window
    {
        private const int StepCount = 6;
        private const string WindowsPlayDisplayChoiceId = "__windows_primary__";

        private readonly PlayniteDisplayManagerPlugin plugin;
        private readonly SetupWizardDraft draft;
        private readonly int nativeHdrEnabledCount;
        private readonly List<DisplayInfo> connectedDisplays;
        private int step;
        private bool syncingRefreshRadios;
        private bool suppressRecenter;
        private bool userMovedWindow;

        public SetupWizardWindow(
            PlayniteDisplayManagerPlugin sourcePlugin,
            SetupWizardDraft workingCopy,
            int nativeHdrEnabledCount)
        {
            plugin = sourcePlugin ?? throw new ArgumentNullException(nameof(sourcePlugin));
            draft = workingCopy ?? throw new ArgumentNullException(nameof(workingCopy));
            this.nativeHdrEnabledCount = Math.Max(0, nativeHdrEnabledCount);
            connectedDisplays = plugin.Displays
                .GetVisibleDisplays(plugin.Settings?.DisplayAliases)
                .Where(d => d != null && d.IsConnected)
                .ToList();

            InitializeComponent();
            Title = Loc("LOCDisplayManager_SetupWizardTitle");
            LoadLabels();
            LoadDraftIntoControls();
            ShowStep(0);
        }

        public SetupWizardDraft Draft => draft;

        private string Loc(string key) => plugin.Loc(key);

        private void LoadLabels()
        {
            SkipButton.Content = Loc("LOCDisplayManager_SetupWizardSkip");
            BackButton.Content = Loc("LOCDisplayManager_SetupWizardBack");
            WelcomeBody.Text = Loc("LOCDisplayManager_SetupWizardWelcomeBody");
            DisplayTargetLabel.Text = Loc("LOCDisplayManager_TopologyTrialTarget");
            IdentifyDisplayButton.Content = Loc("LOCDisplayManager_IdentifyDisplay");
            IdentifyDisplayHint.Text = Loc("LOCDisplayManager_IdentifyDisplayHint");
            TurnOffOthersCheck.Content = Loc("LOCDisplayManager_TopologyTurnOffOthers");
            TurnOffOthersHelp.Text = Loc("LOCDisplayManager_TopologyTurnOffOthersWarning");
            HdrNoneRadio.Content = Loc("LOCDisplayManager_HdrPolicyNone");
            HdrAllRadio.Content = Loc("LOCDisplayManager_HdrPolicyAllGames");
            HdrMetadataRadio.Content = Loc("LOCDisplayManager_HdrPolicyMetadata");
            HdrHelp.Text = Loc("LOCDisplayManager_HdrPolicyHelp");
            HdrNoSupportTitle.Text = Loc("LOCDisplayManager_HdrPrimaryNoSupportTitle");
            HdrNoSupportBody.Text = Loc("LOCDisplayManager_HdrPrimaryNoSupport");
            RefreshNativeRadio.Content = Loc("LOCDisplayManager_RefreshPolicyNative");
            RefreshHighestRadio.Content = Loc("LOCDisplayManager_RefreshPolicyHighest");
            RefreshHelp.Text = Loc("LOCDisplayManager_RefreshRateHelp");
            ClearNativeFlagsCheck.Content = Loc("LOCDisplayManager_SetupWizardClearNative");
            MigrationHelp.Text = Loc("LOCDisplayManager_SetupWizardMigrationHelp");
            SummaryHelp.Text = Loc("LOCDisplayManager_SetupWizardSummaryHelp");
        }

        private void LoadDraftIntoControls()
        {
            PopulatePlayDisplayBox();
            TurnOffOthersCheck.IsChecked = draft.TurnOffOtherDisplays;

            switch (draft.GlobalHdrPolicy)
            {
                case GlobalHdrPolicy.OnForAllGames:
                    HdrAllRadio.IsChecked = true;
                    break;
                case GlobalHdrPolicy.OnWhenMetadataIndicates:
                    HdrMetadataRadio.IsChecked = true;
                    break;
                default:
                    HdrNoneRadio.IsChecked = true;
                    break;
            }

            RebuildRefreshRadios();

            ClearNativeFlagsCheck.IsChecked = draft.ClearNativeHdrFlags;
            MigrationCountText.Text = string.Format(
                Loc("LOCDisplayManager_SetupWizardMigrationCountFormat"),
                nativeHdrEnabledCount);
        }

        private void PopulatePlayDisplayBox()
        {
            var choices = new List<NamedChoice>
            {
                new NamedChoice
                {
                    Id = WindowsPlayDisplayChoiceId,
                    Name = Loc("LOCDisplayManager_PlayDisplayWindowsDefault")
                }
            };
            choices.AddRange(connectedDisplays.Select(d => new NamedChoice
            {
                Id = d.Id,
                Name = d.EffectiveName
            }));

            PlayDisplayBox.ItemsSource = choices;
            if (string.IsNullOrWhiteSpace(draft.PreferredPlayDisplayId))
            {
                PlayDisplayBox.SelectedValue = WindowsPlayDisplayChoiceId;
            }
            else if (connectedDisplays.Any(d =>
                         string.Equals(d.Id, draft.PreferredPlayDisplayId, StringComparison.OrdinalIgnoreCase)))
            {
                PlayDisplayBox.SelectedValue = draft.PreferredPlayDisplayId;
            }
            else
            {
                PlayDisplayBox.SelectedValue = WindowsPlayDisplayChoiceId;
            }
        }

        private void RebuildRefreshRadios()
        {
            syncingRefreshRadios = true;
            try
            {
                RefreshExactPanel.Children.Clear();
                var primary = ResolvePrimaryForRates();
                var rates = primary != null
                    ? (plugin.RefreshRates?.GetAvailableRates(primary) ?? Array.Empty<double>()).ToList()
                    : new List<double>();

                var exactSelected = draft.GlobalRefreshRatePolicy == RefreshRatePolicy.ExactHz
                    || draft.GlobalRefreshRatePolicy == RefreshRatePolicy.Prefer60
                    || draft.GlobalRefreshRatePolicy == RefreshRatePolicy.Prefer120;
                var preferred = draft.PreferredRefreshRateHz;

                foreach (var rate in rates)
                {
                    var radio = new RadioButton
                    {
                        GroupName = "WizardRefresh",
                        Content = string.Format(
                            CultureInfo.CurrentCulture,
                            Loc("LOCDisplayManager_RefreshPolicyExactFormat"),
                            rate),
                        Tag = rate,
                        Margin = new Thickness(0, 0, 0, 8),
                        IsChecked = exactSelected
                            && preferred.HasValue
                            && Math.Abs(preferred.Value - rate) < 0.05
                    };
                    radio.Checked += RefreshRadio_OnChecked;
                    RefreshExactPanel.Children.Add(radio);
                }

                switch (draft.GlobalRefreshRatePolicy)
                {
                    case RefreshRatePolicy.HighestDetected:
                        RefreshHighestRadio.IsChecked = true;
                        break;
                    case RefreshRatePolicy.ExactHz:
                    case RefreshRatePolicy.Prefer60:
                    case RefreshRatePolicy.Prefer120:
                        if (!RefreshExactPanel.Children.OfType<RadioButton>().Any(r => r.IsChecked == true))
                        {
                            RefreshNativeRadio.IsChecked = true;
                        }
                        break;
                    default:
                        RefreshNativeRadio.IsChecked = true;
                        break;
                }
            }
            finally
            {
                syncingRefreshRadios = false;
            }
        }

        private DisplayInfo ResolvePrimaryForRates()
        {
            return ResolveSelectedPlayDisplay();
        }

        private DisplayInfo ResolveSelectedPlayDisplay()
        {
            if (!string.IsNullOrWhiteSpace(draft.PreferredPlayDisplayId))
            {
                var match = connectedDisplays.FirstOrDefault(d =>
                    string.Equals(d.Id, draft.PreferredPlayDisplayId, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    return match;
                }
            }

            return connectedDisplays.FirstOrDefault(d => d.IsPrimary)
                   ?? connectedDisplays.FirstOrDefault();
        }

        private void IdentifyDisplay_OnClick(object sender, RoutedEventArgs e)
        {
            CommitControlsToDraft();
            DisplayIdentifyOverlay.Show(
                ResolveSelectedPlayDisplay(),
                Loc("LOCDisplayManager_StatusPrimary"));
        }

        private bool ProbeSelectedDisplayHdrSupported()
        {
            var display = ResolveSelectedPlayDisplay();
            if (display == null || plugin.Hdr == null)
            {
                return false;
            }

            try
            {
                var probe = plugin.Hdr.ProbeActiveTargets(new[] { display }).FirstOrDefault();
                return probe != null && probe.HdrSupported;
            }
            catch
            {
                return false;
            }
        }

        private void SyncHdrCapabilityUi()
        {
            var supported = ProbeSelectedDisplayHdrSupported();
            if (HdrNoSupportCallout != null)
            {
                HdrNoSupportCallout.Visibility = supported ? Visibility.Collapsed : Visibility.Visible;
            }

            if (HdrOptionsPanel != null)
            {
                HdrOptionsPanel.Visibility = supported ? Visibility.Visible : Visibility.Collapsed;
            }

            if (!supported)
            {
                draft.GlobalHdrPolicy = GlobalHdrPolicy.DoNotManage;
                HdrNoneRadio.IsChecked = true;
            }
        }

        private void CommitControlsToDraft()
        {
            var selectedDisplay = PlayDisplayBox?.SelectedValue as string;
            draft.PreferredPlayDisplayId =
                string.IsNullOrWhiteSpace(selectedDisplay)
                || string.Equals(selectedDisplay, WindowsPlayDisplayChoiceId, StringComparison.Ordinal)
                    ? null
                    : selectedDisplay;
            draft.TurnOffOtherDisplays = TurnOffOthersCheck.IsChecked == true;

            if (!ProbeSelectedDisplayHdrSupported())
            {
                draft.GlobalHdrPolicy = GlobalHdrPolicy.DoNotManage;
            }
            else if (HdrAllRadio.IsChecked == true)
            {
                draft.GlobalHdrPolicy = GlobalHdrPolicy.OnForAllGames;
            }
            else if (HdrMetadataRadio.IsChecked == true)
            {
                draft.GlobalHdrPolicy = GlobalHdrPolicy.OnWhenMetadataIndicates;
            }
            else
            {
                draft.GlobalHdrPolicy = GlobalHdrPolicy.DoNotManage;
            }

            CommitRefreshToDraft();
            draft.ClearNativeHdrFlags = ClearNativeFlagsCheck.IsChecked == true;
        }

        private void CommitRefreshToDraft()
        {
            if (RefreshHighestRadio.IsChecked == true)
            {
                draft.GlobalRefreshRatePolicy = RefreshRatePolicy.HighestDetected;
                draft.PreferredRefreshRateHz = null;
                return;
            }

            var exact = RefreshExactPanel.Children
                .OfType<RadioButton>()
                .FirstOrDefault(r => r.IsChecked == true);
            if (exact?.Tag is double rate)
            {
                draft.GlobalRefreshRatePolicy = RefreshRatePolicy.ExactHz;
                draft.PreferredRefreshRateHz = rate;
                return;
            }

            draft.GlobalRefreshRatePolicy = RefreshRatePolicy.Native;
            draft.PreferredRefreshRateHz = null;
        }

        private void RefreshRadio_OnChecked(object sender, RoutedEventArgs e)
        {
            if (syncingRefreshRadios)
            {
                return;
            }

            CommitRefreshToDraft();
        }

        private void ShowStep(int index)
        {
            step = Math.Max(0, Math.Min(StepCount - 1, index));
            StepWelcome.Visibility = step == 0 ? Visibility.Visible : Visibility.Collapsed;
            StepDisplay.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
            StepHdr.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
            StepRefresh.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;
            StepMigration.Visibility = step == 4 ? Visibility.Visible : Visibility.Collapsed;
            StepSummary.Visibility = step == 5 ? Visibility.Visible : Visibility.Collapsed;

            StepLabel.Text = string.Format(Loc("LOCDisplayManager_SetupWizardStep"), step + 1, StepCount);
            BackButton.Visibility = step == 0 ? Visibility.Collapsed : Visibility.Visible;
            NextButton.Content = step == StepCount - 1
                ? Loc("LOCDisplayManager_SetupWizardFinish")
                : Loc("LOCDisplayManager_SetupWizardNext");

            switch (step)
            {
                case 0:
                    StepTitle.Text = Loc("LOCDisplayManager_SetupWizardWelcomeTitle");
                    StepHelp.Text = Loc("LOCDisplayManager_SetupWizardWelcomeHelp");
                    break;
                case 1:
                    StepTitle.Text = Loc("LOCDisplayManager_SetupWizardDisplayTitle");
                    StepHelp.Text = Loc("LOCDisplayManager_SetupWizardDisplayHelp");
                    break;
                case 2:
                    CommitControlsToDraft();
                    SyncHdrCapabilityUi();
                    StepTitle.Text = Loc("LOCDisplayManager_SetupWizardHdrTitle");
                    StepHelp.Text = Loc("LOCDisplayManager_SetupWizardHdrHelp");
                    break;
                case 3:
                    CommitControlsToDraft();
                    RebuildRefreshRadios();
                    StepTitle.Text = Loc("LOCDisplayManager_SetupWizardRefreshTitle");
                    StepHelp.Text = Loc("LOCDisplayManager_SetupWizardRefreshHelp");
                    break;
                case 4:
                    StepTitle.Text = Loc("LOCDisplayManager_SetupWizardMigrationTitle");
                    StepHelp.Text = Loc("LOCDisplayManager_SetupWizardMigrationIntro");
                    break;
                default:
                    CommitControlsToDraft();
                    StepTitle.Text = Loc("LOCDisplayManager_SetupWizardSummaryTitle");
                    StepHelp.Text = string.Empty;
                    RebuildSummaryRows();
                    break;
            }
        }

        private void RebuildSummaryRows()
        {
            SummaryRows.Children.Clear();
            AddSummaryRow(
                Loc("LOCDisplayManager_SetupWizardSummaryLabelDisplay"),
                FormatDisplaySummary());
            AddSummaryRow(
                Loc("LOCDisplayManager_SetupWizardSummaryLabelTurnOff"),
                draft.TurnOffOtherDisplays
                    ? Loc("LOCDisplayManager_StatusEnabled")
                    : Loc("LOCDisplayManager_StatusDisabled"));
            AddSummaryRow(
                Loc("LOCDisplayManager_SetupWizardSummaryLabelHdr"),
                FormatHdrSummary());
            AddSummaryRow(
                Loc("LOCDisplayManager_SetupWizardSummaryLabelRefresh"),
                FormatRefreshSummary());
            AddSummaryRow(
                Loc("LOCDisplayManager_SetupWizardSummaryLabelClear"),
                draft.ClearNativeHdrFlags
                    ? string.Format(Loc("LOCDisplayManager_SetupWizardSummaryClearYesFormat"), nativeHdrEnabledCount)
                    : Loc("LOCDisplayManager_SetupWizardSummaryClearNo"));
        }

        private string FormatDisplaySummary()
        {
            if (string.IsNullOrWhiteSpace(draft.PreferredPlayDisplayId))
            {
                return Loc("LOCDisplayManager_PlayDisplayWindowsDefault");
            }

            var match = connectedDisplays.FirstOrDefault(d =>
                string.Equals(d.Id, draft.PreferredPlayDisplayId, StringComparison.OrdinalIgnoreCase));
            return match?.EffectiveName ?? draft.PreferredPlayDisplayId;
        }

        private string FormatHdrSummary()
        {
            switch (draft.GlobalHdrPolicy)
            {
                case GlobalHdrPolicy.OnForAllGames:
                    return Loc("LOCDisplayManager_OverviewActionAlwaysOnShort");
                case GlobalHdrPolicy.OnWhenMetadataIndicates:
                    return Loc("LOCDisplayManager_OverviewActionMetadataShort");
                default:
                    return Loc("LOCDisplayManager_OverviewActionDoNotManageShort");
            }
        }

        private string FormatRefreshSummary()
        {
            switch (draft.GlobalRefreshRatePolicy)
            {
                case RefreshRatePolicy.HighestDetected:
                    return Loc("LOCDisplayManager_RefreshPolicyHighest");
                case RefreshRatePolicy.ExactHz:
                case RefreshRatePolicy.Prefer60:
                case RefreshRatePolicy.Prefer120:
                    if (draft.PreferredRefreshRateHz.HasValue)
                    {
                        return string.Format(
                            CultureInfo.CurrentCulture,
                            Loc("LOCDisplayManager_RefreshPolicyExactFormat"),
                            draft.PreferredRefreshRateHz.Value);
                    }
                    return Loc("LOCDisplayManager_RefreshPolicyNative");
                default:
                    return Loc("LOCDisplayManager_RefreshPolicyNative");
            }
        }

        private void AddSummaryRow(string label, string value)
        {
            var border = new Border { Style = (Style)FindResource("WizardSummaryRow") };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = label,
                Style = (Style)FindResource("WizardSummaryLabel")
            });
            stack.Children.Add(new TextBlock
            {
                Text = value,
                Style = (Style)FindResource("WizardSummaryValue")
            });
            border.Child = stack;
            SummaryRows.Children.Add(border);
        }

        private void NextClick(object sender, RoutedEventArgs args)
        {
            if (step < StepCount - 1)
            {
                CommitControlsToDraft();
                ShowStep(step + 1);
                return;
            }

            CommitControlsToDraft();
            draft.SetupWizardCompleted = true;
            DialogResult = true;
        }

        private void BackClick(object sender, RoutedEventArgs args)
        {
            if (step > 0)
            {
                CommitControlsToDraft();
                ShowStep(step - 1);
            }
        }

        private void SkipClick(object sender, RoutedEventArgs args)
        {
            draft.SetupWizardCompleted = true;
            DialogResult = false;
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs args)
        {
            if (args.Key == Key.Escape)
            {
                args.Handled = true;
                SkipClick(sender, args);
            }
        }

        private void OnDragAreaMouseLeftButtonDown(object sender, MouseButtonEventArgs args)
        {
            if (args.ChangedButton == MouseButton.Left)
            {
                try
                {
                    userMovedWindow = true;
                    DragMove();
                }
                catch
                {
                    // ignore
                }
            }
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs args)
        {
            CenterInOwnerOrScreen();
            Dispatcher.BeginInvoke(
                new Action(CenterInOwnerOrScreen),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        private void OnWindowSizeChanged(object sender, SizeChangedEventArgs args)
        {
            if (suppressRecenter || userMovedWindow || !IsLoaded)
            {
                return;
            }

            if (!args.HeightChanged && !args.WidthChanged)
            {
                return;
            }

            CenterInOwnerOrScreen();
        }

        private void CenterInOwnerOrScreen()
        {
            if (userMovedWindow)
            {
                return;
            }

            suppressRecenter = true;
            try
            {
                UpdateLayout();
                var width = ActualWidth;
                var height = ActualHeight;
                if (width < 100 || height < 100 || double.IsNaN(width) || double.IsNaN(height))
                {
                    return;
                }

                var anchor = GetCenteringAnchor();
                Point? centerDip = null;
                if (anchor == null || anchor.WindowState != WindowState.Maximized)
                {
                    centerDip = TryGetWindowCenterDip(anchor);
                }

                double left;
                double top;

                if (centerDip.HasValue)
                {
                    left = centerDip.Value.X - (width / 2.0);
                    top = centerDip.Value.Y - (height / 2.0);
                }
                else
                {
                    var workArea = GetWorkAreaDip(anchor);
                    left = workArea.Left + ((workArea.Width - width) / 2.0);
                    top = workArea.Top + ((workArea.Height - height) / 2.0);
                }

                var clampArea = GetWorkAreaDip(anchor);
                if (width <= clampArea.Width)
                {
                    left = Math.Min(Math.Max(left, clampArea.Left), clampArea.Right - width);
                }
                else
                {
                    left = clampArea.Left;
                }

                if (height <= clampArea.Height)
                {
                    top = Math.Min(Math.Max(top, clampArea.Top), clampArea.Bottom - height);
                }
                else
                {
                    top = clampArea.Top;
                }

                if (!double.IsNaN(left) && !double.IsNaN(top) &&
                    !double.IsInfinity(left) && !double.IsInfinity(top))
                {
                    Left = left;
                    Top = top;
                }
            }
            finally
            {
                suppressRecenter = false;
            }
        }

        private Window GetCenteringAnchor()
        {
            try
            {
                var main = Application.Current != null ? Application.Current.MainWindow : null;
                if (main != null &&
                    main.IsVisible &&
                    main.WindowState != WindowState.Minimized &&
                    main.ActualWidth > 0 &&
                    main.ActualHeight > 0)
                {
                    return main;
                }
            }
            catch
            {
            }

            return Owner;
        }

        private Point? TryGetWindowCenterDip(Window window)
        {
            if (window == null ||
                !window.IsVisible ||
                window.WindowState == WindowState.Minimized ||
                window.ActualWidth <= 0 ||
                window.ActualHeight <= 0)
            {
                return null;
            }

            try
            {
                var centerPx = window.PointToScreen(new Point(
                    window.ActualWidth / 2.0,
                    window.ActualHeight / 2.0));
                var fromDevice = GetTransformFromDevice(this) ?? GetTransformFromDevice(window);
                if (fromDevice == null)
                {
                    return new Point(centerPx.X, centerPx.Y);
                }

                return fromDevice.Value.Transform(new Point(centerPx.X, centerPx.Y));
            }
            catch
            {
                return null;
            }
        }

        private Rect GetWorkAreaDip(Window anchor)
        {
            try
            {
                var screen = GetScreenForWindow(anchor) ?? GetScreenForWindow(this) ?? Forms.Screen.PrimaryScreen;
                if (screen == null)
                {
                    return SystemParameters.WorkArea;
                }

                var pixel = screen.WorkingArea;
                var fromDevice = GetTransformFromDevice(anchor) ?? GetTransformFromDevice(this);
                if (fromDevice == null)
                {
                    return new Rect(pixel.Left, pixel.Top, pixel.Width, pixel.Height);
                }

                var topLeft = fromDevice.Value.Transform(new Point(pixel.Left, pixel.Top));
                var bottomRight = fromDevice.Value.Transform(new Point(pixel.Right, pixel.Bottom));
                return new Rect(topLeft, bottomRight);
            }
            catch
            {
                return SystemParameters.WorkArea;
            }
        }

        private static Forms.Screen GetScreenForWindow(Window window)
        {
            if (window == null)
            {
                return null;
            }

            try
            {
                var handle = new WindowInteropHelper(window).Handle;
                if (handle != IntPtr.Zero)
                {
                    return Forms.Screen.FromHandle(handle);
                }

                if (!double.IsNaN(window.Left) && !double.IsNaN(window.Top))
                {
                    var px = GetTransformToDevice(window);
                    if (px != null)
                    {
                        var point = px.Value.Transform(new Point(window.Left + 8, window.Top + 8));
                        return Forms.Screen.FromPoint(new System.Drawing.Point(
                            (int)Math.Round(point.X),
                            (int)Math.Round(point.Y)));
                    }

                    return Forms.Screen.FromPoint(new System.Drawing.Point(
                        (int)Math.Round(window.Left + 8),
                        (int)Math.Round(window.Top + 8)));
                }
            }
            catch
            {
            }

            return null;
        }

        private static Matrix? GetTransformFromDevice(Window window)
        {
            var source = GetPresentationSource(window);
            if (source == null || source.CompositionTarget == null)
            {
                return null;
            }

            return source.CompositionTarget.TransformFromDevice;
        }

        private static Matrix? GetTransformToDevice(Window window)
        {
            var source = GetPresentationSource(window);
            if (source == null || source.CompositionTarget == null)
            {
                return null;
            }

            return source.CompositionTarget.TransformToDevice;
        }

        private static PresentationSource GetPresentationSource(Window window)
        {
            if (window == null)
            {
                return null;
            }

            var source = PresentationSource.FromVisual(window);
            if (source != null)
            {
                return source;
            }

            try
            {
                var handle = new WindowInteropHelper(window).Handle;
                if (handle != IntPtr.Zero)
                {
                    return HwndSource.FromHwnd(handle);
                }
            }
            catch
            {
            }

            return null;
        }

        private sealed class NamedChoice
        {
            public string Id { get; set; }
            public string Name { get; set; }
        }
    }
}
