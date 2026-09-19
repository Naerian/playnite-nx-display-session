using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PlayniteDisplayManager
{
    public partial class SetupWizardWindow : Window
    {
        private const int StepCount = 3;
        private readonly PlayniteDisplayManagerPlugin plugin;
        private readonly SetupWizardDraft draft;
        private readonly int nativeHdrEnabledCount;
        private int step;

        public SetupWizardWindow(
            PlayniteDisplayManagerPlugin sourcePlugin,
            SetupWizardDraft workingCopy,
            int nativeHdrEnabledCount)
        {
            plugin = sourcePlugin ?? throw new ArgumentNullException(nameof(sourcePlugin));
            draft = workingCopy ?? throw new ArgumentNullException(nameof(workingCopy));
            this.nativeHdrEnabledCount = Math.Max(0, nativeHdrEnabledCount);
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
            ClearNativeFlagsCheck.Content = Loc("LOCDisplayManager_SetupWizardClearNative");
            MigrationHelp.Text = Loc("LOCDisplayManager_SetupWizardMigrationHelp");
            SummaryHelp.Text = Loc("LOCDisplayManager_SetupWizardSummaryHelp");
        }

        private void LoadDraftIntoControls()
        {
            ClearNativeFlagsCheck.IsChecked = draft.ClearNativeHdrFlags;
            MigrationCountText.Text = string.Format(
                Loc("LOCDisplayManager_SetupWizardMigrationCountFormat"),
                nativeHdrEnabledCount);
        }

        private void CommitControlsToDraft()
        {
            draft.ClearNativeHdrFlags = ClearNativeFlagsCheck.IsChecked == true;
        }

        private void ShowStep(int index)
        {
            step = Math.Max(0, Math.Min(StepCount - 1, index));
            StepWelcome.Visibility = step == 0 ? Visibility.Visible : Visibility.Collapsed;
            StepMigration.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
            StepSummary.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;

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
                Loc("LOCDisplayManager_SetupWizardSummaryLabelClear"),
                draft.ClearNativeHdrFlags
                    ? string.Format(Loc("LOCDisplayManager_SetupWizardSummaryClearYesFormat"), nativeHdrEnabledCount)
                    : Loc("LOCDisplayManager_SetupWizardSummaryClearNo"));
            AddSummaryRow(
                Loc("LOCDisplayManager_SetupWizardSummaryLabelOwner"),
                Loc("LOCDisplayManager_SetupWizardSummaryOwnerValue"));
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
                if (step == 1)
                {
                    CommitControlsToDraft();
                }

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
                try { DragMove(); } catch { /* ignore */ }
            }
        }
    }
}
