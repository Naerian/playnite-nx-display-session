using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace PlayniteDisplayManager
{
    public sealed class MessageDialogChange
    {
        public string Before { get; set; }

        public string After { get; set; }
    }

    public partial class MessageDialogWindow : Window
    {
        private bool suppressRecenter;

        public MessageDialogWindow(
            string title,
            string message,
            string okLabel,
            IEnumerable<MessageDialogChange> changes = null)
        {
            InitializeComponent();
            TitleText.Text = title ?? string.Empty;
            BodyText.Text = message ?? string.Empty;
            BodyText.Visibility = string.IsNullOrWhiteSpace(message)
                ? Visibility.Collapsed
                : Visibility.Visible;
            OkButton.Content = string.IsNullOrWhiteSpace(okLabel) ? "OK" : okLabel;
            BuildChanges(changes);
        }

        private void BuildChanges(IEnumerable<MessageDialogChange> changes)
        {
            ChangesPanel.Children.Clear();
            if (changes == null)
            {
                return;
            }

            foreach (var change in changes)
            {
                if (change == null
                    || (string.IsNullOrWhiteSpace(change.Before) && string.IsNullOrWhiteSpace(change.After)))
                {
                    continue;
                }

                var row = new WrapPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 0, 0, 10)
                };

                row.Children.Add(CreateBadge(
                    string.IsNullOrWhiteSpace(change.Before) ? "—" : change.Before,
                    before: true));
                row.Children.Add(new TextBlock
                {
                    Text = "›",
                    Style = TryFindResource("DialogChangeArrow") as Style
                });
                row.Children.Add(CreateBadge(
                    string.IsNullOrWhiteSpace(change.After) ? "—" : change.After,
                    before: false));

                ChangesPanel.Children.Add(row);
            }
        }

        private Border CreateBadge(string text, bool before)
        {
            return new Border
            {
                Style = TryFindResource(before ? "DialogChangeBadgeBefore" : "DialogChangeBadgeAfter") as Style,
                Child = new TextBlock
                {
                    Text = text,
                    Style = TryFindResource("DialogChangeBadgeText") as Style
                }
            };
        }

        private void OkClick(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape || e.Key == Key.Enter)
            {
                DialogResult = true;
                Close();
                e.Handled = true;
            }
        }

        private void OnDragAreaMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                try
                {
                    DragMove();
                }
                catch
                {
                    // Ignore drag failures when the window is not ready.
                }
            }
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            CenterOnPlaynite();
            Dispatcher.BeginInvoke(
                new Action(CenterOnPlaynite),
                System.Windows.Threading.DispatcherPriority.Loaded);
            OkButton.Focus();
        }

        private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!IsLoaded || suppressRecenter)
            {
                return;
            }

            CenterOnPlaynite();
        }

        /// <summary>
        /// Center on Playnite's main window / the monitor where Playnite is visible
        /// (same approach as SetupWizardWindow).
        /// </summary>
        private void CenterOnPlaynite()
        {
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

                if (!double.IsNaN(left) && !double.IsNaN(top)
                    && !double.IsInfinity(left) && !double.IsInfinity(top))
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
                if (main != null
                    && main.IsVisible
                    && main.WindowState != WindowState.Minimized
                    && main.ActualWidth > 0
                    && main.ActualHeight > 0)
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
            if (window == null
                || !window.IsVisible
                || window.WindowState == WindowState.Minimized
                || window.ActualWidth <= 0
                || window.ActualHeight <= 0)
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
            if (source?.CompositionTarget == null)
            {
                return null;
            }

            return source.CompositionTarget.TransformFromDevice;
        }

        private static Matrix? GetTransformToDevice(Window window)
        {
            var source = GetPresentationSource(window);
            if (source?.CompositionTarget == null)
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
    }
}
