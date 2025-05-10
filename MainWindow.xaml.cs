using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Threading.Tasks;
using System.Timers;
using System.ComponentModel;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Auth;
using Microsoft.Extensions.Configuration;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Forms;
using System.Drawing;

namespace MusicZeroV2
{
    public partial class MainWindow : Window
    {
        private SpotifyClient? _spotify;
        private EmbedIOAuthServer? _server;
        private System.Timers.Timer? _updateTimer;
        public required string _clientId;
        public required string _clientSecret;
        private const string REDIRECT_URI = "http://127.0.0.1:5000/callback";
        private DispatcherTimer hideTimer = new DispatcherTimer();
        private bool isMouseOver = false;
        private bool isAnimating = false;
        private NotifyIcon? _notifyIcon;
        private bool isDragging = false;
        private System.Windows.Point dragStartPoint;
        private double initialHorizontalOffset;
        private double initialVerticalOffset;
        private System.Windows.Point lastMousePosition;

        // Win32 API imports for window styles
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        public MainWindow()
        {
            InitializeComponent();
            
            // Position the window at the top of the screen
            Left = 0;
            Top = 0;
            
            // Show the content panel by default
            ContentPanel.Visibility = Visibility.Visible;
            ContentPanel.Opacity = 1;
            ContentPanel.Margin = new Thickness(0, 0, 0, 0);
            
            // Ensure the window is visible
            Show();
            Activate();

            LoadConfiguration();
            InitializeSpotify();
            InitializeHideTimer();
            InitializeNotifyIcon();
            Loaded += Window_Loaded;
        }

        private void LoadConfiguration()
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            _clientId = configuration["Spotify:ClientId"] ?? throw new InvalidOperationException("Spotify ClientId not found in configuration");
            _clientSecret = configuration["Spotify:ClientSecret"] ?? throw new InvalidOperationException("Spotify ClientSecret not found in configuration");
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Hide from Alt+Tab
            var helper = new WindowInteropHelper(this);
            var exStyle = GetWindowLong(helper.Handle, GWL_EXSTYLE);
            exStyle |= WS_EX_TOOLWINDOW;
            SetWindowLong(helper.Handle, GWL_EXSTYLE, exStyle);
        }

        private async void InitializeSpotify()
        {
            _server = new EmbedIOAuthServer(new Uri(REDIRECT_URI), 5000);
            await _server.Start();

            _server.AuthorizationCodeReceived += async (sender, response) =>
            {
                await _server.Stop();
                var config = SpotifyClientConfig.CreateDefault();
                var tokenResponse = await new OAuthClient().RequestToken(
                    new AuthorizationCodeTokenRequest(
                        _clientId, _clientSecret, response.Code, new Uri(REDIRECT_URI)
                    )
                );

                _spotify = new SpotifyClient(tokenResponse.AccessToken);
                StartUpdateTimer();
            };

            var loginRequest = new LoginRequest(_server.BaseUri, _clientId, LoginRequest.ResponseType.Code)
            {
                Scope = new[] { 
                    Scopes.UserReadPlaybackState, 
                    Scopes.UserModifyPlaybackState,
                    Scopes.UserReadCurrentlyPlaying,
                    Scopes.UserReadPrivate,
                    Scopes.PlaylistReadPrivate,
                    Scopes.PlaylistReadCollaborative
                }
            };

            var uri = loginRequest.ToUri();
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = uri.ToString(),
                UseShellExecute = true
            });
        }

        private void StartUpdateTimer()
        {
            _updateTimer = new System.Timers.Timer(1000);
            _updateTimer.Elapsed += async (sender, e) => await UpdatePlaybackInfo();
            _updateTimer.Start();
        }

        private async Task UpdatePlaybackInfo()
        {
            if (_spotify == null) return;

            try
            {
                var playback = await _spotify.Player.GetCurrentPlayback();
                if (playback?.Item is FullTrack track)
                {
                    Dispatcher.Invoke(() =>
                    {
                        TitleText.Text = track.Name;
                        ArtistText.Text = string.Join(", ", track.Artists.Select(a => a.Name));
                        PlayPauseIcon.Data = playback.IsPlaying 
                            ? Geometry.Parse("M6 19h4V5H6v14zm8-14v14h4V5h-4z")  // Pause icon
                            : Geometry.Parse("M8 5v14l11-7z");  // Play icon

                        // Update popup controls
                        if (SongInfoPopup.IsOpen)
                        {
                            PopupPlayPauseIcon.Data = playback.IsPlaying
                                ? Geometry.Parse("M6 19h4V5H6v14zm8-14v14h4V5h-4z")  // Pause icon
                                : Geometry.Parse("M8 5v14l11-7z");  // Play icon
                        }

                        // Update progress
                        ProgressBar.Maximum = track.DurationMs;
                        ProgressBar.Value = playback.ProgressMs;

                        // Update popup progress if it's open
                        if (SongInfoPopup.IsOpen)
                        {
                            PopupProgressBar.Maximum = track.DurationMs;
                            PopupProgressBar.Value = playback.ProgressMs;
                        }

                        // Get up next track
                        UpdateUpNext();
                    });
                }
            }
            catch (Exception)
            {
                // Handle any errors silently
            }
        }

        private async void UpdateUpNext()
        {
            if (_spotify == null) return;

            try
            {
                var queue = await _spotify.Player.GetQueue();
                if (queue?.Queue?.Count > 0)
                {
                    // Get the first track that isn't the current one
                    var nextTrack = queue.Queue.FirstOrDefault(t => t is FullTrack);
                    if (nextTrack is FullTrack track)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            UpNextText.Text = $"{track.Name} - {string.Join(", ", track.Artists.Select(a => a.Name))}";
                        });
                    }
                    else
                    {
                        Dispatcher.Invoke(() =>
                        {
                            UpNextText.Text = "Nothing";
                        });
                    }
                }
                else
                {
                    Dispatcher.Invoke(() =>
                    {
                        UpNextText.Text = "Nothing";
                    });
                }
            }
            catch
            {
                Dispatcher.Invoke(() =>
                {
                    UpNextText.Text = "Nothing";
                });
            }
        }

        private async void PlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            if (_spotify == null) return;

            try
            {
                var playback = await _spotify.Player.GetCurrentPlayback();
                if (playback?.IsPlaying == true)
                {
                    await _spotify.Player.PausePlayback();
                }
                else
                {
                    await _spotify.Player.ResumePlayback();
                }
            }
            catch (Exception)
            {
                // Handle any errors silently
            }
        }

        private async void NextButton_Click(object sender, RoutedEventArgs e)
        {
            if (_spotify == null) return;

            try
            {
                await _spotify.Player.SkipNext();
            }
            catch (Exception)
            {
                // Handle any errors silently
            }
        }

        private async void PreviousButton_Click(object sender, RoutedEventArgs e)
        {
            if (_spotify == null) return;

            try
            {
                await _spotify.Player.SkipPrevious();
            }
            catch (Exception)
            {
                // Handle any errors silently
            }
        }

        private void Window_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            isMouseOver = true;
            hideTimer.Stop();

            if (ContentPanel.Visibility == Visibility.Collapsed)
            {
                ContentPanel.Visibility = Visibility.Visible;
                var storyboard = (Storyboard)FindResource("ExpandAnimation");
                storyboard.Begin();
            }
        }

        private void Window_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            isMouseOver = false;
            hideTimer.Start();
        }

        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                CloseApp();
            }
        }

        private void InitializeNotifyIcon()
        {
            _notifyIcon = new NotifyIcon();
            _notifyIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location);
            _notifyIcon.Visible = true;
            _notifyIcon.DoubleClick += (s, e) => ShowWindow();
            _notifyIcon.ContextMenuStrip = new ContextMenuStrip();
            _notifyIcon.ContextMenuStrip.Items.Add("Show", null, (s, e) => ShowWindow());
            _notifyIcon.ContextMenuStrip.Items.Add("Exit", null, (s, e) => CloseApp());
        }

        private void ShowWindow()
        {
            Show();
            Activate();
            WindowState = WindowState.Normal;
        }

        private void CloseApp()
        {
            _notifyIcon?.Dispose();
            System.Windows.Application.Current.Shutdown();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            _updateTimer?.Stop();
            _updateTimer?.Dispose();
            _server?.Stop();
        }

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);
            if (WindowState == WindowState.Minimized)
            {
                Hide();
            }
        }

        private void InitializeHideTimer()
        {
            hideTimer.Interval = TimeSpan.FromSeconds(3);
            hideTimer.Tick += (s, e) =>
            {
                if (!isMouseOver)
                {
                    var storyboard = (Storyboard)FindResource("CollapseAnimation");
                    storyboard.Completed += (sender, args) =>
                    {
                        ContentPanel.Visibility = Visibility.Collapsed;
                        isAnimating = false;
                    };
                    storyboard.Begin();
                }
            };
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                // Update popup content
                PopupTitleText.Text = TitleText.Text;
                PopupArtistText.Text = ArtistText.Text;
                PopupUpNextText.Text = UpNextText.Text;
                PopupProgressBar.Maximum = ProgressBar.Maximum;
                PopupProgressBar.Value = ProgressBar.Value;

                // Show popup
                SongInfoPopup.IsOpen = true;
            }
        }

        private void ClosePopup_Click(object sender, RoutedEventArgs e)
        {
            SongInfoPopup.IsOpen = false;
        }

        private void Border_MouseUp(object sender, MouseButtonEventArgs e)
        {
            // Get the position of the mouse relative to the popup content
            var border = (Border)sender;
            var mousePos = e.GetPosition(border);
            
            // Only close if the click is outside the content area
            if (mousePos.X < 0 || mousePos.X > border.ActualWidth ||
                mousePos.Y < 0 || mousePos.Y > border.ActualHeight)
            {
                SongInfoPopup.IsOpen = false;
            }
        }

        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var border = (Border)sender;
            dragStartPoint = e.GetPosition(border);
            lastMousePosition = e.GetPosition(null);
            initialHorizontalOffset = SongInfoPopup.HorizontalOffset;
            initialVerticalOffset = SongInfoPopup.VerticalOffset;
            isDragging = true;
            border.CaptureMouse();
        }

        private void Border_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (isDragging)
            {
                var currentPosition = e.GetPosition(null);
                var delta = currentPosition - lastMousePosition;
                
                SongInfoPopup.HorizontalOffset += delta.X;
                SongInfoPopup.VerticalOffset += delta.Y;
                
                lastMousePosition = currentPosition;
            }
        }

        private void Border_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            isDragging = false;
            var border = (Border)sender;
            border.ReleaseMouseCapture();
        }

        private async void PopupPreviousButton_Click(object sender, RoutedEventArgs e)
        {
            if (_spotify == null) return;

            try
            {
                await _spotify.Player.SkipPrevious();
            }
            catch (Exception)
            {
                // Handle any errors silently
            }
        }

        private async void PopupPlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            if (_spotify == null) return;

            try
            {
                var playback = await _spotify.Player.GetCurrentPlayback();
                if (playback?.IsPlaying == true)
                {
                    await _spotify.Player.PausePlayback();
                }
                else
                {
                    await _spotify.Player.ResumePlayback();
                }
            }
            catch (Exception)
            {
                // Handle any errors silently
            }
        }

        private async void PopupNextButton_Click(object sender, RoutedEventArgs e)
        {
            if (_spotify == null) return;

            try
            {
                await _spotify.Player.SkipNext();
            }
            catch (Exception)
            {
                // Handle any errors silently
            }
        }
    }
} 