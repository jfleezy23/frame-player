using System;
using System.Windows;
using FramePlayer.Core.Models;

namespace FramePlayer
{
    public partial class CompareSideBySideExportOptionsWindow : Window
    {
        private readonly CompareSideBySideExportDialogSnapshot _snapshot;

        internal CompareSideBySideExportOptionsWindow(CompareSideBySideExportDialogSnapshot snapshot)
        {
            _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));

            InitializeComponent();
            ApplySnapshot();
        }

        internal CompareSideBySideExportDialogSelection Selection { get; private set; }

        private void ContinueButton_Click(object sender, RoutedEventArgs e)
        {
            Selection = new CompareSideBySideExportDialogSelection(
                LoopModeRadioButton.IsChecked == true
                    ? CompareSideBySideExportMode.Loop
                    : CompareSideBySideExportMode.WholeVideo,
                CompareAudioRadioButton.IsChecked == true
                    ? CompareSideBySideExportAudioSource.Compare
                    : CompareSideBySideExportAudioSource.Primary);
            DialogResult = true;
        }

        private void ExportModeRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            UpdateModeBehaviorText();
        }

        private void AudioSourceRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            UpdateAudioBehaviorText();
        }

        private void ApplySnapshot()
        {
            PrimaryFileNameTextBlock.Text = _snapshot.PrimaryFileName;
            PrimaryVideoSummaryTextBlock.Text = _snapshot.PrimaryVideoSummary;
            PrimaryAudioSummaryTextBlock.Text = _snapshot.PrimaryAudioSummary;
            PrimaryPositionSummaryTextBlock.Text = _snapshot.PrimaryPositionSummary;
            PrimaryLoopSummaryTextBlock.Text = _snapshot.PrimaryLoopSummary;

            CompareFileNameTextBlock.Text = _snapshot.CompareFileName;
            CompareVideoSummaryTextBlock.Text = _snapshot.CompareVideoSummary;
            CompareAudioSummaryTextBlock.Text = _snapshot.CompareAudioSummary;
            ComparePositionSummaryTextBlock.Text = _snapshot.ComparePositionSummary;
            CompareLoopSummaryTextBlock.Text = _snapshot.CompareLoopSummary;

            LoopModeRadioButton.IsEnabled = _snapshot.IsLoopModeAvailable;
            LoopAvailabilityTextBlock.Text = _snapshot.IsLoopModeAvailable
                ? "Loop export is available for the current pane-local compare state."
                : _snapshot.LoopModeUnavailableReason;
            LoopAvailabilityTextBlock.Visibility = string.IsNullOrWhiteSpace(LoopAvailabilityTextBlock.Text)
                ? Visibility.Collapsed
                : Visibility.Visible;

            PrimaryAudioRadioButton.Content = _snapshot.PrimaryAudioLabel;
            CompareAudioRadioButton.Content = _snapshot.CompareAudioLabel;

            if (_snapshot.InitialMode == CompareSideBySideExportMode.Loop && _snapshot.IsLoopModeAvailable)
            {
                LoopModeRadioButton.IsChecked = true;
            }
            else
            {
                WholeVideoModeRadioButton.IsChecked = true;
            }

            if (_snapshot.InitialAudioSource == CompareSideBySideExportAudioSource.Compare)
            {
                CompareAudioRadioButton.IsChecked = true;
            }
            else
            {
                PrimaryAudioRadioButton.IsChecked = true;
            }

            UpdateModeBehaviorText();
            UpdateAudioBehaviorText();
        }

        private void UpdateModeBehaviorText()
        {
            if (LoopModeRadioButton.IsChecked == true)
            {
                ModeBehaviorTextBlock.Text = "Loop mode uses each pane's A/B range as its own trim window and extends the shorter pane with black video.";
                return;
            }

            ModeBehaviorTextBlock.Text = "Whole-video mode uses the current compare positions as the sync point and adds black video before the earlier pane when needed.";
        }

        private void UpdateAudioBehaviorText()
        {
            var selectedPaneHasAudio = CompareAudioRadioButton.IsChecked == true
                ? _snapshot.CompareHasAudio
                : _snapshot.PrimaryHasAudio;
            AudioBehaviorTextBlock.Text = selectedPaneHasAudio
                ? "The selected pane's audio will be aligned to the merged export and padded to the output duration."
                : "The selected pane has no audio stream. The merged export will be silent.";
        }
    }

    internal sealed class CompareSideBySideExportDialogSnapshot
    {
        public CompareSideBySideExportDialogSnapshot(
            string primaryFileName,
            string primaryVideoSummary,
            string primaryAudioSummary,
            string primaryPositionSummary,
            string primaryLoopSummary,
            string compareFileName,
            string compareVideoSummary,
            string compareAudioSummary,
            string comparePositionSummary,
            string compareLoopSummary,
            bool primaryHasAudio,
            bool compareHasAudio,
            bool isLoopModeAvailable,
            string loopModeUnavailableReason,
            CompareSideBySideExportMode initialMode,
            CompareSideBySideExportAudioSource initialAudioSource,
            string primaryAudioLabel,
            string compareAudioLabel)
        {
            PrimaryFileName = primaryFileName ?? string.Empty;
            PrimaryVideoSummary = primaryVideoSummary ?? string.Empty;
            PrimaryAudioSummary = primaryAudioSummary ?? string.Empty;
            PrimaryPositionSummary = primaryPositionSummary ?? string.Empty;
            PrimaryLoopSummary = primaryLoopSummary ?? string.Empty;
            CompareFileName = compareFileName ?? string.Empty;
            CompareVideoSummary = compareVideoSummary ?? string.Empty;
            CompareAudioSummary = compareAudioSummary ?? string.Empty;
            ComparePositionSummary = comparePositionSummary ?? string.Empty;
            CompareLoopSummary = compareLoopSummary ?? string.Empty;
            PrimaryHasAudio = primaryHasAudio;
            CompareHasAudio = compareHasAudio;
            IsLoopModeAvailable = isLoopModeAvailable;
            LoopModeUnavailableReason = loopModeUnavailableReason ?? string.Empty;
            InitialMode = initialMode;
            InitialAudioSource = initialAudioSource;
            PrimaryAudioLabel = primaryAudioLabel ?? "Primary pane";
            CompareAudioLabel = compareAudioLabel ?? "Compare pane";
        }

        public string PrimaryFileName { get; }

        public string PrimaryVideoSummary { get; }

        public string PrimaryAudioSummary { get; }

        public string PrimaryPositionSummary { get; }

        public string PrimaryLoopSummary { get; }

        public string CompareFileName { get; }

        public string CompareVideoSummary { get; }

        public string CompareAudioSummary { get; }

        public string ComparePositionSummary { get; }

        public string CompareLoopSummary { get; }

        public bool PrimaryHasAudio { get; }

        public bool CompareHasAudio { get; }

        public bool IsLoopModeAvailable { get; }

        public string LoopModeUnavailableReason { get; }

        public CompareSideBySideExportMode InitialMode { get; }

        public CompareSideBySideExportAudioSource InitialAudioSource { get; }

        public string PrimaryAudioLabel { get; }

        public string CompareAudioLabel { get; }
    }

    internal sealed class CompareSideBySideExportDialogSelection
    {
        public CompareSideBySideExportDialogSelection(
            CompareSideBySideExportMode mode,
            CompareSideBySideExportAudioSource audioSource)
        {
            Mode = mode;
            AudioSource = audioSource;
        }

        public CompareSideBySideExportMode Mode { get; }

        public CompareSideBySideExportAudioSource AudioSource { get; }
    }
}
