using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace ShareIsCare
{
    public partial class MainWindow : Window
    {
        private Process? _currentProcess;
        private string _ytdlpPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "yt-dlp.exe");
        private bool _isDownloading = false;

        public MainWindow()
        {
            InitializeComponent();
            InitializeUI();
        }

        private void InitializeUI()
        {
            // Set default output path
            OutputPathTextBox.Text = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads"
            );

            // Check if yt-dlp exists
            CheckYtDlpExists();
        }

        private void CheckYtDlpExists()
        {
            // Check multiple possible locations
            string[] possiblePaths = {
                "yt-dlp.exe",
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "yt-dlp.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "yt-dlp.exe")
            };

            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    _ytdlpPath = path;
                    LogOutput($"Found yt-dlp at: {path}\n");
                    return;
                }
            }

            // Not found, prompt user
            var result = MessageBox.Show(
                "yt-dlp.exe not found!\n\n" +
                "Please download it from: https://github.com/yt-dlp/yt-dlp/releases\n\n" +
                "Would you like to specify its location now?",
                "yt-dlp Not Found",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );

            if (result == MessageBoxResult.Yes)
            {
                SelectYtDlpPath();
            }
        }

        private void SelectYtDlpPath()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
                Title = "Select yt-dlp.exe",
                FileName = "yt-dlp.exe"
            };

            if (dialog.ShowDialog() == true)
            {
                _ytdlpPath = dialog.FileName;
                LogOutput($"yt-dlp path set to: {_ytdlpPath}\n");
            }
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Select output folder for downloads",
                InitialDirectory = OutputPathTextBox.Text
            };

            if (dialog.ShowDialog() == true)
            {
                OutputPathTextBox.Text = dialog.FolderName;
            }
        }

        private void DownloadTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CustomFormatTextBox == null || FormatLabel == null) return;

            bool isCustom = DownloadTypeCombo.SelectedIndex == 3;
            FormatLabel.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
            CustomFormatTextBox.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
        }

        private void LimitRateCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (RateLimitTextBox != null)
                RateLimitTextBox.IsEnabled = true;
        }

        private void LimitRateCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (RateLimitTextBox != null)
                RateLimitTextBox.IsEnabled = false;
        }

        private async void GetInfoButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateUrl()) return;

            GetInfoButton.IsEnabled = false;
            LogOutput("=== Fetching video information ===\n");

            try
            {
                var args = $"--dump-json --no-warnings \"{UrlTextBox.Text}\"";
                await RunYtDlpAsync(args, isDownload: false);
            }
            finally
            {
                GetInfoButton.IsEnabled = true;
            }
        }

        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateUrl() || !ValidateOutputPath()) return;

            if (_isDownloading)
            {
                MessageBox.Show("A download is already in progress!", "Info",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var args = BuildCommandLineArgs();
            LogOutput("=== Starting Download ===\n");
            LogOutput($"Command: yt-dlp {args}\n\n");

            SetDownloadingState(true);

            try
            {
                await RunYtDlpAsync(args, isDownload: true);
            }
            finally
            {
                SetDownloadingState(false);
            }
        }

        private bool ValidateUrl()
        {
            if (string.IsNullOrWhiteSpace(UrlTextBox.Text))
            {
                MessageBox.Show("Please enter a video URL!", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                UrlTextBox.Focus();
                return false;
            }
            return true;
        }

        private bool ValidateOutputPath()
        {
            if (string.IsNullOrWhiteSpace(OutputPathTextBox.Text))
            {
                MessageBox.Show("Please select an output folder!", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!Directory.Exists(OutputPathTextBox.Text))
            {
                var result = MessageBox.Show(
                    $"Directory does not exist:\n{OutputPathTextBox.Text}\n\nCreate it?",
                    "Directory Not Found",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question
                );

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        Directory.CreateDirectory(OutputPathTextBox.Text);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to create directory:\n{ex.Message}",
                            "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return false;
                    }
                }
                else
                {
                    return false;
                }
            }

            return true;
        }

        private string BuildCommandLineArgs()
        {
            var args = new StringBuilder();

            // Output template
            var outputTemplate = Path.Combine(
                OutputPathTextBox.Text,
                "%(title)s.%(ext)s"
            );
            args.Append($"-o \"{outputTemplate}\"");

            // Format selection
            var format = GetFormatString();
            if (!string.IsNullOrEmpty(format))
            {
                args.Append($" -f \"{format}\"");
            }

            // Audio extraction
            if (DownloadTypeCombo.SelectedIndex == 1) // Audio Only
            {
                args.Append(" -x");

                var audioFormat = ((ComboBoxItem)AudioFormatCombo.SelectedItem).Content.ToString();
                args.Append($" --audio-format {audioFormat}");

                if (AudioQualityCombo.SelectedIndex > 0)
                {
                    var quality = ((ComboBoxItem)AudioQualityCombo.SelectedItem).Content.ToString();
                    args.Append($" --audio-quality {quality}");
                }
            }

            // Playlist handling
            args.Append(PlaylistCheckBox.IsChecked == true
                ? " --yes-playlist"
                : " --no-playlist");

            // Subtitles
            if (SubtitlesCheckBox.IsChecked == true)
            {
                args.Append(" --write-subs --write-auto-subs --sub-lang en --embed-subs");
            }

            // Thumbnail
            if (ThumbnailCheckBox.IsChecked == true)
            {
                args.Append(" --embed-thumbnail");
            }

            // Metadata
            if (MetadataCheckBox.IsChecked == true)
            {
                args.Append(" --embed-metadata");
            }

            // Rate limit
            if (LimitRateCheckBox.IsChecked == true &&
                !string.IsNullOrWhiteSpace(RateLimitTextBox.Text))
            {
                if (int.TryParse(RateLimitTextBox.Text, out int rate) && rate > 0)
                {
                    args.Append($" --limit-rate {rate}K");
                }
            }

            // Progress options
            args.Append(" --newline --no-colors --progress");

            // Additional arguments
            if (!string.IsNullOrWhiteSpace(AdditionalArgsTextBox.Text))
            {
                args.Append($" {AdditionalArgsTextBox.Text.Trim()}");
            }

            // URL (at the end)
            args.Append($" \"{UrlTextBox.Text.Trim()}\"");

            return args.ToString();
        }

        private string GetFormatString()
        {
            int type = DownloadTypeCombo.SelectedIndex;
            int quality = QualityCombo.SelectedIndex;

            return type switch
            {
                1 => "bestaudio/best", // Audio only
                2 => GetVideoOnlyFormat(quality), // Video only
                3 => CustomFormatTextBox.Text.Trim(), // Custom
                _ => GetVideoAudioFormat(quality) // Video + Audio (default)
            };
        }

        private string GetVideoAudioFormat(int qualityIndex)
        {
            if (qualityIndex == 0) return "bestvideo+bestaudio/best";

            var height = qualityIndex switch
            {
                1 => 2160,
                2 => 1440,
                3 => 1080,
                4 => 720,
                5 => 480,
                6 => 360,
                _ => 9999
            };

            return $"bestvideo[height<={height}]+bestaudio/best[height<={height}]";
        }

        private string GetVideoOnlyFormat(int qualityIndex)
        {
            if (qualityIndex == 0) return "bestvideo";

            var height = qualityIndex switch
            {
                1 => 2160,
                2 => 1440,
                3 => 1080,
                4 => 720,
                5 => 480,
                6 => 360,
                _ => 9999
            };

            return $"bestvideo[height<={height}]";
        }

        private async Task RunYtDlpAsync(string arguments, bool isDownload)
        {
            if (!File.Exists(_ytdlpPath))
            {
                MessageBox.Show(
                    $"yt-dlp.exe not found at:\n{_ytdlpPath}\n\nPlease select the correct path.",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
                SelectYtDlpPath();
                return;
            }

            try
            {
                _currentProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = _ytdlpPath,
                        Arguments = arguments,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8
                    }
                };

                _currentProcess.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Dispatcher.Invoke(() =>
                        {
                            LogOutput(e.Data + "\n");
                            if (isDownload) ParseProgress(e.Data);
                        });
                    }
                };

                _currentProcess.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Dispatcher.Invoke(() => LogOutput($"[ERROR] {e.Data}\n"));
                    }
                };

                _currentProcess.Start();
                _currentProcess.BeginOutputReadLine();
                _currentProcess.BeginErrorReadLine();

                await _currentProcess.WaitForExitAsync();

                Dispatcher.Invoke(() =>
                {
                    if (_currentProcess.ExitCode == 0)
                    {
                        LogOutput("\n✓ Process completed successfully!\n");
                        ResetProgress();
                    }
                    else
                    {
                        LogOutput($"\n✗ Process exited with code: {_currentProcess.ExitCode}\n");
                        ResetProgress();
                    }
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    LogOutput($"\n[EXCEPTION] {ex.Message}\n");
                    MessageBox.Show($"Error running yt-dlp:\n{ex.Message}",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    ResetProgress();
                });
            }
        }

        private void ParseProgress(string output)
        {
            // Match: [download]  45.0% of 10.50MiB at 1.20MiB/s ETA 00:04
            var match = Regex.Match(output, @"\[download\]\s+(\d+\.?\d*)%");
            if (match.Success && double.TryParse(match.Groups[1].Value, out double progress))
            {
                DownloadProgressBar.IsIndeterminate = false;
                DownloadProgressBar.Value = progress;
            }
            else if (output.Contains("[download] Destination:"))
            {
                DownloadProgressBar.IsIndeterminate = true;
            }
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentProcess != null && !_currentProcess.HasExited)
            {
                try
                {
                    _currentProcess.Kill(entireProcessTree: true);
                    LogOutput("\n✗ Download stopped by user\n");
                }
                catch (Exception ex)
                {
                    LogOutput($"\n[ERROR] Failed to stop process: {ex.Message}\n");
                }
            }

            SetDownloadingState(false);
            ResetProgress();
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            OutputTextBox.Clear();
            ResetProgress();
        }

        private void SetDownloadingState(bool isDownloading)
        {
            _isDownloading = isDownloading;
            DownloadButton.IsEnabled = !isDownloading;
            StopButton.IsEnabled = isDownloading;
            GetInfoButton.IsEnabled = !isDownloading;

            if (isDownloading)
            {
                DownloadProgressBar.IsIndeterminate = true;
            }
        }

        private void ResetProgress()
        {
            DownloadProgressBar.IsIndeterminate = false;
            DownloadProgressBar.Value = 0;
        }

        private void LogOutput(string text)
        {
            OutputTextBox.AppendText(text);
            OutputTextBox.ScrollToEnd();
        }

        protected override void OnClosed(EventArgs e)
        {
            // Clean up process on window close
            if (_currentProcess != null && !_currentProcess.HasExited)
            {
                try
                {
                    _currentProcess.Kill(entireProcessTree: true);
                }
                catch { /* Ignore cleanup errors */ }
            }

            base.OnClosed(e);
        }
    }
}