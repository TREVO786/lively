﻿using ImageMagick;
using livelywpf.Core;
using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace livelywpf.Views
{
    public interface ILibraryPreview
    {
        /// <summary>
        /// Exit and detach wallpaper (will abort if Capture is running.)
        /// </summary>
        public void Exit();
        /// <summary>
        /// Capture thumbnail every 3 seconds.
        /// </summary>
        /// <param name="savePath"></param>
        public void StartThumbnailCaptureLoop(string savePath);
        /// <summary>
        /// Create thumbnail and preview gif 
        /// </summary>
        /// <param name="savePath"></param>
        public void StartCapture(string savePath);
        /// <summary>
        /// Wallpaper is attached to window and ready for capture.
        /// </summary>
        event EventHandler WallpaperAttached;
        /// <summary>
        /// New thumbnail file ready.
        /// </summary>
        event EventHandler<string> ThumbnailUpdated;
        /// <summary>
        /// New preview gif ready.
        /// </summary>
        event EventHandler<string> PreviewUpdated;
        /// <summary>
        /// Progress of operation, from 0 - 100.
        /// </summary>
        event EventHandler<double> CaptureProgress;
    }
    
    /// <summary>
    /// Interaction logic for LibraryPreviewView.xaml
    /// </summary>
    public partial class LibraryPreviewView : Window, ILibraryPreview
    {
        private bool _processing = false;
        private string thumbnailPathTemp;
        private readonly WallpaperType wallpaperType;
        private readonly IntPtr wallpaperHwnd;
        readonly DispatcherTimer thumbnailCaptureTimer = new DispatcherTimer();
        //Good values: 1. 30c,120s 2. 15c, 90s
        readonly int gifAnimationDelay = 1000 * 1 / 30 ; //in milliseconds (1/fps)
        readonly int gifSaveAnimationDelay = 1000 * 1 / 120;
        readonly int gifTotalFrames = 60;
        public event EventHandler<string> ThumbnailUpdated;
        public event EventHandler<string> PreviewUpdated;
        public event EventHandler<double> CaptureProgress;
        public event EventHandler WallpaperAttached;

        public LibraryPreviewView(IWallpaper wp)
        {
            LibraryPreviewViewModel vm = new LibraryPreviewViewModel(this, wp);
            this.DataContext = vm;
            this.Closed += vm.OnWindowClosed;
            wallpaperHwnd = wp.GetHWND();
            wallpaperType = wp.GetWallpaperType();
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            //attach wp hwnd to border ui element.
            WindowOperations.SetProgramToFramework(this, wallpaperHwnd, PreviewBorder);
            WallpaperAttached?.Invoke(this, null);
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_processing)
            {
                e.Cancel = true;
                ModernWpf.Controls.Primitives.FlyoutBase.ShowAttachedFlyout(PreviewBorder);
                return;
            }

            thumbnailCaptureTimer?.Stop();
            try
            {
                //deleting temporary thumbnail file if any..
                File.Delete(tmpThumbCaptureLoopPath);
            }
            catch { }

            //detach wallpaper window from this dialogue.
            WindowOperations.SetParentSafe(wallpaperHwnd, IntPtr.Zero);
        }

        private string tmpThumbCaptureLoopPath;
        private void CaptureLoop(object sender, EventArgs e)
        {
            var currThumbPath = Path.Combine(thumbnailPathTemp, Path.ChangeExtension(Path.GetRandomFileName(), ".jpg"));
            if (File.Exists(tmpThumbCaptureLoopPath))
            {
                if (wallpaperType == WallpaperType.picture)
                    return;

                try
                {
                    File.Delete(tmpThumbCaptureLoopPath);
                }
                catch
                {
                    thumbnailCaptureTimer.Stop();
                }
            }

            Rect previewPanelPos = WindowOperations.GetAbsolutePlacement(PreviewBorder, true);
            Size previewPanelSize = WindowOperations.GetElementPixelSize(PreviewBorder);

            //thumbnail capture
            CaptureScreen.CopyScreen(
                currThumbPath,
               (int)previewPanelPos.Left,
               (int)previewPanelPos.Top,
               (int)previewPanelSize.Width,
               (int)previewPanelSize.Height);

            ThumbnailUpdated?.Invoke(this, currThumbPath);
            tmpThumbCaptureLoopPath = currThumbPath;
        }

        private async void CapturePreview(string saveDirectory)
        {
            _processing = true;
            thumbnailCaptureTimer?.Stop();
            taskbarItemInfo.ProgressState = System.Windows.Shell.TaskbarItemProgressState.Normal;
            Rect previewPanelPos = WindowOperations.GetAbsolutePlacement(PreviewBorder, true);
            Size previewPanelSize = WindowOperations.GetElementPixelSize(PreviewBorder);

            //wait before capturing thumbnail..incase wallpaper is not loaded yet.
            await Task.Delay(100);

            var thumbFilePath = Path.Combine(saveDirectory, Path.ChangeExtension(Path.GetRandomFileName(), ".jpg"));
            //final thumbnail capture..
            CaptureScreen.CopyScreen(
               thumbFilePath,
               (int)previewPanelPos.Left,
               (int)previewPanelPos.Top,
               (int)previewPanelSize.Width,
               (int)previewPanelSize.Height);
            ThumbnailUpdated?.Invoke(this, thumbFilePath);

            //preview clip (animated gif file).
            if (Program.SettingsVM.Settings.GifCapture && wallpaperType != WallpaperType.picture)
            {
                var previewFilePath = Path.Combine(saveDirectory, Path.ChangeExtension(Path.GetRandomFileName(), ".gif"));
                previewPanelPos = WindowOperations.GetAbsolutePlacement(PreviewBorder, true);
                await CaptureScreen.CaptureGif(
                    previewFilePath,
                    (int)previewPanelPos.Left,
                    (int)previewPanelPos.Top,
                    (int)previewPanelPos.Width,
                    (int)previewPanelPos.Height,
                    gifAnimationDelay,
                    gifSaveAnimationDelay,
                    gifTotalFrames,
                    new Progress<int>(percent => CaptureProgress?.Invoke(this, percent - 1)));
                PreviewUpdated?.Invoke(this, previewFilePath);
            }
            _processing = false;
            CaptureProgress?.Invoke(this, 100);
        }

        #region interface methods

        public void Exit()
        {
            this.Close();
        }

        public void StartCapture(string savePath)
        {
            CapturePreview(savePath);
        }

        public void StartThumbnailCaptureLoop(string savePath)
        {
            thumbnailPathTemp = savePath;
            //capture thumbnail every few seconds while user is shown wallpaper metadata preview.
            thumbnailCaptureTimer.Tick += new EventHandler(CaptureLoop);
            thumbnailCaptureTimer.Interval = new TimeSpan(0, 0, 0, 0, 3000);
            thumbnailCaptureTimer.Start();
        }

        #endregion //interface methods

        #region window move/resize lock

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var source = PresentationSource.FromVisual(this) as HwndSource;
            source.AddHook(WndProc);
        }

        //prevent window resize and move during recording.
        //ref: https://stackoverflow.com/questions/3419909/how-do-i-lock-a-wpf-window-so-it-can-not-be-moved-resized-minimized-maximized
        public IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == (int)NativeMethods.WM.WINDOWPOSCHANGING && _processing)
            {
                var wp = Marshal.PtrToStructure<NativeMethods.WINDOWPOS>(lParam);
                wp.flags |= (int)NativeMethods.SetWindowPosFlags.SWP_NOMOVE | (int)NativeMethods.SetWindowPosFlags.SWP_NOSIZE;
                Marshal.StructureToPtr(wp, lParam, false);
            }
            return IntPtr.Zero;
        }

        #endregion //window move/resize lock
    }
}
