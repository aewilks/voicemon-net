using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using WinRTXamlToolkit.Controls.DataVisualization.Charting;
using Windows.UI.Xaml;
using System.IO;
using System.Linq;
using MathNet.Numerics;
using Windows.Media;
using Windows.Media.Audio;
using System.Runtime.InteropServices;
using Windows.Foundation;
using System.Numerics;

namespace voicemon
{
    [ComImport]
    [Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    unsafe interface IMemoryBufferByteAccess
    {
        void GetBuffer(out byte* buffer, out uint capacity);
    }

    public sealed partial class MainPage : Page
    {
        private ObservableCollection<PitchData> pitchDataCollection;
        private DispatcherTimer timer;
        private AudioFrameOutputNode audioOutputNode;

        public MainPage()
        {
            this.InitializeComponent();
            pitchDataCollection = new ObservableCollection<PitchData>();
            lineSeries.ItemsSource = pitchDataCollection;
            timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds((double)Application.Current.Resources["PitchUpdateInterval"]);
            timer.Tick += Timer_Tick;
            timer.Start();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            var audioFile = (StorageFile)e.Parameter;
            var stream = await audioFile.OpenAsync(FileAccessMode.Read);
            var reader = new BinaryReader(stream.GetInputStreamAt(0).AsStreamForRead());

            var buffer = new byte[(uint)stream.Size];
            int bytesRead = await Task.Run(() => reader.Read(buffer, 0, buffer.Length));

            var frequencies = await CalculatePitchAsync(buffer);
            for (int i = 0; i < frequencies.Length; i++)
            {
                pitchDataCollection.Add(new PitchData() { Time = i, Pitch = frequencies[i] });
            }
        }

        private async Task<double[]> CalculatePitchAsync(byte[] buffer)
        {
            var samplesPerSecond = (double)Application.Current.Resources["SamplesPerSecond"];
            var bytesPerSample = (double)Application.Current.Resources["BytesPerSample"];
            var frameSize = (double)Application.Current.Resources["FrameSize"];
            var minFrequency = (double)Application.Current.Resources["MinimumFrequency"];
            var maxFrequency = (double)Application.Current.Resources["MaximumFrequency"];
            var frequencies = new double[(int)(buffer.Length / (int)frameSize)];


            for (int i = 0; i < buffer.Length - (int)frameSize; i += (int)frameSize)
            {
                var samples = new double[(int)frameSize / (int)bytesPerSample];
                for (int j = 0; j < samples.Length; j++)
                {
                    if (bytesPerSample == 2)
                    {
                        samples[j] = BitConverter.ToInt16(buffer, i + (j * 2)) / 32768.0;
                    }
                    else
                    {
                        // handle error - unsupported bytes per sample
                    }
                }

                var frequency = await CalculateFrequencyAsync(samples, (int)samplesPerSecond, minFrequency, maxFrequency);
                frequencies[i / (int)frameSize] = frequency;
            }

            return frequencies;
        }


        private async Task<double> CalculateFrequencyAsync(double[] samples, int samplesPerSecond, double minFrequency, double maxFrequency)
        {
            var spectrum = new double[samples.Length];
            var frequencyStep = (double)samplesPerSecond / samples.Length;

            // Calculate the frequency spectrum using a Fast Fourier Transform
            var complexSamples = samples.Select(s => new Complex(s, 0)).ToArray();
            MathNet.Numerics.IntegralTransforms.Fourier.Forward(complexSamples, MathNet.Numerics.IntegralTransforms.FourierOptions.NoScaling);

            // Identify the peak frequency within the range of interest
            var maxAmplitude = 0.0;
            var peakFrequency = 0.0;
            for (int i = 0; i < samples.Length / 2; i++)
            {
                var frequency = i * frequencyStep;
                if (frequency >= minFrequency && frequency <= peakFrequency && complexSamples[i].Magnitude > maxAmplitude)
                {
                    maxAmplitude = complexSamples[i].Magnitude;
                    peakFrequency = frequency;
                }
            }

            return peakFrequency;
        }

        private byte[] GetLatestAudioBuffer()
        {
            // Get the latest audio frame
            AudioFrame frame = audioOutputNode.GetFrame();

            // If there is no frame, return an empty buffer
            if (frame == null)
            {
                return new byte[0];
            }

            // Get the audio buffer from the frame
            using (AudioBuffer audioBuffer = frame.LockBuffer(AudioBufferAccessMode.Read))
            using (IMemoryBufferReference reference = audioBuffer.CreateReference())
            {
                // Get the raw audio data from the buffer
                byte[] data;
                unsafe
                {
                    ((IMemoryBufferByteAccess)reference).GetBuffer(out byte* dataInBytes, out uint capacity);

                    // Create a managed byte array to hold the data
                    data = new byte[audioBuffer.Length];

                    // Copy the data to the managed array
                    Marshal.Copy((IntPtr)dataInBytes, data, 0, (int)audioBuffer.Length);
                }

                return data;
            }
        }


        private void Timer_Tick(object sender, object e)
        {
            var lastPitchData = pitchDataCollection.LastOrDefault();
            var lastTime = (lastPitchData != null) ? lastPitchData.Time : 0;
            var newPitchData = new PitchData { Time = lastTime + 1 };

            var audioBuffer = GetLatestAudioBuffer(); // Get latest audio buffer

            // Convert byte[] to double[]
            double[] audioSamples = new double[audioBuffer.Length / 2];
            for (int i = 0; i < audioSamples.Length; i++)
            {
                audioSamples[i] = BitConverter.ToInt16(audioBuffer, i * 2) / 32768.0;
            }

            var frequency = CalculateFrequencyAsync(audioSamples, (int)Application.Current.Resources["SamplesPerSecond"], (double)Application.Current.Resources["MinimumFrequency"], (double)Application.Current.Resources["MaximumFrequency"]).Result;
            newPitchData.Pitch = frequency;

            pitchDataCollection.Add(newPitchData);
            while (pitchDataCollection.Count > (int)Application.Current.Resources["MaximumDataPoints"])
            {
                pitchDataCollection.RemoveAt(0);
            }
        }


    }
}

public class PitchData
{
    public int Time { get; set; }
    public double Pitch { get; set; }
}
