using System;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Devices.Enumeration;
using Windows.Media.Capture;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using System.Threading.Tasks;
using Windows.Media.MediaProperties;
using Windows.ApplicationModel.Background;
using Windows.Devices.Background;

namespace voicemon
{
    public sealed partial class App : Application
    {
        private MediaCapture mediaCapture;
        private StorageFile audioFile;

        public App()
        {
            this.InitializeComponent();
            this.Suspending += OnSuspending;
            this.RequestedTheme = ApplicationTheme.Light;
        }

        protected override async void OnLaunched(LaunchActivatedEventArgs e)
        {
            Frame rootFrame = Window.Current.Content as Frame;

            if (rootFrame == null)
            {
                rootFrame = new Frame();
                rootFrame.NavigationFailed += OnNavigationFailed;

                Window.Current.Content = rootFrame;
            }

            if (e.PrelaunchActivated == false)
            {
                if (rootFrame.Content == null)
                {
                    rootFrame.Navigate(typeof(MainPage), e.Arguments);
                }
                Window.Current.Activate();

                await InitializeAudioCaptureAsync();
                await StartPitchDetectionBackgroundTaskAsync();
            }
        }

        private async Task InitializeAudioCaptureAsync()
        {
            var devices = await DeviceInformation.FindAllAsync(DeviceClass.AudioCapture);

            if (devices.Count == 0)
            {
                // handle error - no audio capture devices available
                return;
            }

            var settings = new MediaCaptureInitializationSettings()
            {
                StreamingCaptureMode = StreamingCaptureMode.Audio,
                AudioProcessing = Windows.Media.AudioProcessing.Default,
                MediaCategory = MediaCategory.Speech,
                AudioDeviceId = devices[0].Id,
            };

            mediaCapture = new MediaCapture();

            try
            {
                await mediaCapture.InitializeAsync(settings);
            }
            catch (Exception ex)
            {
                // handle error - failed to initialize audio capture device
                return;
            }

            audioFile = await ApplicationData.Current.LocalFolder.CreateFileAsync("audio.wav", CreationCollisionOption.GenerateUniqueName);

            var profile = MediaEncodingProfile.CreateWav(AudioEncodingQuality.High);
            await mediaCapture.StartRecordToStorageFileAsync(profile, audioFile);
        }

        private async Task StartPitchDetectionBackgroundTaskAsync()
        {
            var accessStatus = await BackgroundExecutionManager.RequestAccessAsync();

            if (accessStatus != BackgroundAccessStatus.AlwaysAllowed && accessStatus != BackgroundAccessStatus.AllowedSubjectToSystemPolicy)
            {
                // handle error - background task access not granted
                return;
            }

            var taskBuilder = new BackgroundTaskBuilder();
            taskBuilder.Name = "PitchDetectorBackgroundTask";
            taskBuilder.SetTrigger(new TimeTrigger(15, false));
            taskBuilder.AddCondition(new SystemCondition(SystemConditionType.InternetAvailable));
            taskBuilder.TaskEntryPoint = typeof(PitchDetectorBackgroundTask).FullName;
            var registration = taskBuilder.Register();
        }

        private void OnSuspending(object sender, SuspendingEventArgs e)
        {
            var deferral = e.SuspendingOperation.GetDeferral();

            mediaCapture.StopRecordAsync();
            audioFile = null;

            deferral.Complete();
        }

        private void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
        }
    }

    internal class PitchDetectorBackgroundTask
    {
    }
}
