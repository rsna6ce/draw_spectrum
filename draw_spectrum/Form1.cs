using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

using System.Numerics;
using System.IO;
using System.Diagnostics;

namespace draw_spectrum
{
    public partial class Form1 : Form
    {
        private PictureBox pictureBox;
        private Button buttonLoad;
        private Button buttonStartStop;
        private Label currentTimeLabel;
        private Label totalTimeLabel;
        private TextBox fftSizeInput;
        private TextBox maxSpectrumSizeInput;
        private CheckBox saveImagesCheckBox;
        private Timer updateTimer;
        private float[] audioData;
        private int sampleRate;
        private int currentSampleIndex;
        private bool isPlaying;
        private int fftSize = 1024;
        private int frameCount;
        private string inputAudioFile;
        private int maxSpectrumSize = 256;

        private readonly double encodeFrameRate = 10.0; // エンコードフレームレート（fps）
        private readonly double drawFrameRate = 60.0;   // 描画フレームレート（fps、デフォルト60）
        private readonly int gapParameter = 1;          // スキマパラメータ（デフォルト1）

        public Form1()
        {
            InitializeComponent();
            SetupControls();
        }

        private void SetupControls()
        {
            pictureBox = new PictureBox
            {
                Size = new Size(800, 400),
                Location = new Point(10, 50),
                BorderStyle = BorderStyle.FixedSingle
            };
            this.Controls.Add(pictureBox);

            buttonLoad = new Button
            {
                Text = "Load Audio File",
                Size = new Size(100, 30),
                Location = new Point(10, 10)
            };
            buttonLoad.Click += ButtonLoad_Click;
            this.Controls.Add(buttonLoad);

            buttonStartStop = new Button
            {
                Text = "Start",
                Size = new Size(60, 30),
                Location = new Point(120, 10)
            };
            buttonStartStop.Click += ButtonStartStop_Click;
            this.Controls.Add(buttonStartStop);

            Label fftSizeLabel = new Label
            {
                Text = "FFT Size:",
                Size = new Size(60, 30),
                Location = new Point(190, 10)
            };
            this.Controls.Add(fftSizeLabel);

            fftSizeInput = new TextBox
            {
                Text = fftSize.ToString(),
                Size = new Size(50, 30),
                Location = new Point(250, 10)
            };
            this.Controls.Add(fftSizeInput);

            Label maxSpectrumSizeLabel = new Label
            {
                Text = "Max Spectrum Size:",
                Size = new Size(100, 30),
                Location = new Point(310, 10)
            };
            this.Controls.Add(maxSpectrumSizeLabel);

            maxSpectrumSizeInput = new TextBox
            {
                Text = maxSpectrumSize.ToString(),
                Size = new Size(50, 30),
                Location = new Point(410, 10)
            };
            this.Controls.Add(maxSpectrumSizeInput);

            currentTimeLabel = new Label
            {
                Text = "Current: 0.00 sec",
                Size = new Size(100, 30),
                Location = new Point(470, 10)
            };
            this.Controls.Add(currentTimeLabel);

            totalTimeLabel = new Label
            {
                Text = "Total: 0.00 sec",
                Size = new Size(100, 30),
                Location = new Point(580, 10)
            };
            this.Controls.Add(totalTimeLabel);

            saveImagesCheckBox = new CheckBox
            {
                Text = "Save Images",
                Size = new Size(100, 30),
                Location = new Point(690, 10),
                Checked = true
            };
            this.Controls.Add(saveImagesCheckBox);

            updateTimer = new Timer
            {
                Interval = (int)(1000.0 / drawFrameRate) // 60fps -> 16ms
            };
            updateTimer.Tick += UpdateTimer_Tick;
        }

        private void ButtonLoad_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = "Audio files (*.wav;*.mp3)|*.wav;*.mp3";
                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    inputAudioFile = openFileDialog.FileName;
                    if (Path.GetExtension(inputAudioFile).ToLower() == ".mp3")
                    {
                        ConvertMp3ToWav(inputAudioFile);
                        LoadWavFile(Path.Combine(Path.GetTempPath(), "temp.wav"));
                    }
                    else
                    {
                        LoadWavFile(inputAudioFile);
                    }
                }
            }
        }

        private void ConvertMp3ToWav(string mp3FilePath)
        {
            try
            {
                string tempWavPath = Path.Combine(Path.GetTempPath(), "temp.wav");
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    /*Arguments = $"-i \"{mp3FilePath}\" -f wav \"{tempWavPath}\" -y",*/
                    Arguments = string.Format("-loglevel warning " +
                                             "-y " +
                                             "-i \"{0}\" " +
                                             "-vn -ac 1 -ar 24000 " +
                                             "-vcodec libx264 " +
                                             "-acodec pcm_s16le " +
                                             "-f wav " +
                                             "\"{1}\"",
                                             mp3FilePath, tempWavPath),


                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (Process process = Process.Start(psi))
                {
                    process.WaitForExit();
                    if (process.ExitCode != 0)
                    {
                        string error = process.StandardError.ReadToEnd();
                        throw new Exception($"FFmpeg error: {error}");
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error converting MP3 to WAV: {ex.Message}");
            }
        }

        private void ButtonStartStop_Click(object sender, EventArgs e)
        {
            if (audioData == null || audioData.Length == 0)
            {
                MessageBox.Show("No audio data loaded.");
                return;
            }

            if (!int.TryParse(fftSizeInput.Text, out int newFftSize) || newFftSize <= 0 || (newFftSize & (newFftSize - 1)) != 0)
            {
                MessageBox.Show("FFT size must be a power of 2 (e.g., 512, 1024). Using default value.");
                fftSize = 1024;
                fftSizeInput.Text = fftSize.ToString();
            }
            else
            {
                fftSize = newFftSize;
            }

            if (!int.TryParse(maxSpectrumSizeInput.Text, out int newMaxSpectrumSize) || newMaxSpectrumSize <= 0)
            {
                MessageBox.Show("Max Spectrum Size must be a positive integer. Using default value.");
                maxSpectrumSize = 256;
                maxSpectrumSizeInput.Text = maxSpectrumSize.ToString();
            }
            else
            {
                maxSpectrumSize = newMaxSpectrumSize;
            }

            isPlaying = !isPlaying;
            if (isPlaying)
            {
                buttonStartStop.Text = "Stop";
                frameCount = 0;
                currentSampleIndex = 0;
                updateTimer.Start();

                if (saveImagesCheckBox.Checked)
                {
                    try
                    {
                        string exePath = AppDomain.CurrentDomain.BaseDirectory;
                        string outputFolder = Path.Combine(exePath, "output");
                        if (!Directory.Exists(outputFolder))
                        {
                            Directory.CreateDirectory(outputFolder);
                        }

                        string[] existingFiles = Directory.GetFiles(outputFolder, "*.png");
                        foreach (string file in existingFiles)
                        {
                            try
                            {
                                File.Delete(file);
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show($"Error deleting file {file}: {ex.Message}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error preparing output folder: {ex.Message}");
                    }
                }
            }
            else
            {
                buttonStartStop.Text = "Start";
                updateTimer.Stop();
            }
        }

        private void UpdateTimer_Tick(object sender, EventArgs e)
        {
            if (audioData == null || audioData.Length == 0 || !isPlaying) return;

            updateTimer.Stop();

            frameCount++;
            double timerIntervalSec = 1.0 / encodeFrameRate;
            currentSampleIndex = (int)(frameCount * timerIntervalSec * sampleRate);

            if (currentSampleIndex + fftSize > audioData.Length)
            {
                isPlaying = false;
                buttonStartStop.Text = "Start";

                currentSampleIndex = audioData.Length - fftSize;
                if (currentSampleIndex < 0) currentSampleIndex = 0;

                float currentTimeSec = (float)currentSampleIndex / sampleRate;
                //DrawSpectrum2(currentTimeSec);

                if (saveImagesCheckBox.Checked)
                {
                    EncodeVideoWithFfmpeg();
                }
                return;
            }

            float currentTimeSec2 = (float)currentSampleIndex / sampleRate;
            currentTimeLabel.Text = $"Current: {currentTimeSec2:F2} sec";

            DrawSpectrum2(currentTimeSec2);

            updateTimer.Start();
        }

        private void EncodeVideoWithFfmpeg()
        {
            try
            {
                string exePath = AppDomain.CurrentDomain.BaseDirectory;
                string outputFolder = Path.Combine(exePath, "output");
                string tempVideoPath = Path.Combine(exePath, "temp_video.mp4");
                string finalOutputPath = Path.Combine(exePath, "final_output.mp4");

                string[] pngFiles = Directory.GetFiles(outputFolder, "*.png");
                if (pngFiles.Length == 0)
                {
                    throw new Exception("No PNG files found in the output folder.");
                }

                ProcessStartInfo psiVideo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = string.Format("-loglevel warning " +
                                             "-y " +
                                             "-framerate {0} " +
                                             "-i \"{1}/%08d.png\" " +
                                             "-vframes {2} " +
                                             "-vf \"scale={3}:{4},format=yuv420p\" " +
                                             "-vcodec libx264 " +
                                             "-r {0} " +
                                             "\"{5}\"",
                                             encodeFrameRate, outputFolder, frameCount, pictureBox.Width, pictureBox.Height, tempVideoPath),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (Process processVideo = Process.Start(psiVideo))
                {
                    processVideo.WaitForExit();
                    if (processVideo.ExitCode != 0)
                    {
                        string error = processVideo.StandardError.ReadToEnd();
                        throw new Exception($"FFmpeg video encoding error: {error}");
                    }
                }

                string audioInput = Path.GetExtension(inputAudioFile).ToLower() == ".mp3" ? Path.Combine(Path.GetTempPath(), "temp.wav") : inputAudioFile;
                ProcessStartInfo psiMux = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = string.Format("-loglevel warning " +
                                             "-y " +
                                             "-i \"{0}\" " +
                                             "-i \"{1}\" " +
                                             "-c:v copy " +
                                             "-c:a aac " +
                                             "-map 0:v:0 " +
                                             "-map 1:a:0 " +
                                             "\"{2}\"",
                                             tempVideoPath, audioInput, finalOutputPath),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (Process processMux = Process.Start(psiMux))
                {
                    processMux.WaitForExit();
                    if (processMux.ExitCode != 0)
                    {
                        string error = processMux.StandardError.ReadToEnd();
                        throw new Exception($"FFmpeg muxing error: {error}");
                    }
                }

                if (File.Exists(tempVideoPath)) File.Delete(tempVideoPath);
                // temp.wavの削除は廃止
                MessageBox.Show($"動画が生成されました: {finalOutputPath}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error encoding video: {ex.Message}");
            }
        }

        private void LoadWavFile(string filePath)
        {
            try
            {
                using (BinaryReader reader = new BinaryReader(File.Open(filePath, FileMode.Open)))
                {
                    string riff = new string(reader.ReadChars(4));
                    if (riff != "RIFF") throw new Exception("Not a valid WAV file");
                    reader.ReadInt32();
                    string wave = new string(reader.ReadChars(4));
                    if (wave != "WAVE") throw new Exception("Not a valid WAV file");

                    string chunkID = new string(reader.ReadChars(4));
                    while (chunkID != "fmt ")
                    {
                        int chunkSize = reader.ReadInt32();
                        reader.BaseStream.Seek(chunkSize, SeekOrigin.Current);
                        chunkID = new string(reader.ReadChars(4));
                    }

                    int fmtChunkSize = reader.ReadInt32();
                    short audioFormat = reader.ReadInt16();
                    if (audioFormat != 1) throw new Exception("Only PCM format is supported");
                    short channels = reader.ReadInt16();
                    sampleRate = reader.ReadInt32();
                    reader.ReadInt32();
                    short blockAlign = reader.ReadInt16();
                    short bitsPerSample = reader.ReadInt16();
                    if (bitsPerSample != 16) throw new Exception("Only 16-bit WAV is supported");

                    if (fmtChunkSize > 16)
                        reader.BaseStream.Seek(fmtChunkSize - 16, SeekOrigin.Current);

                    chunkID = new string(reader.ReadChars(4));
                    while (chunkID != "data")
                    {
                        int chunkSize = reader.ReadInt32();
                        reader.BaseStream.Seek(chunkSize, SeekOrigin.Current);
                        chunkID = new string(reader.ReadChars(4));
                    }

                    int dataSize = reader.ReadInt32();
                    int sampleCount = dataSize / (bitsPerSample / 8) / channels;

                    audioData = new float[sampleCount];
                    for (int i = 0; i < sampleCount; i++)
                    {
                        short sample = reader.ReadInt16();
                        if (channels == 2) reader.ReadInt16();
                        audioData[i] = sample / 32768f;
                    }

                    float totalTimeSec = (float)sampleCount / sampleRate;
                    totalTimeLabel.Text = $"Total: {totalTimeSec:F2} sec";

                    currentSampleIndex = 0;
                    frameCount = 0;
                    currentTimeLabel.Text = "Current: 0.00 sec";
                    isPlaying = false;
                    buttonStartStop.Text = "Start";
                    updateTimer.Stop();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading WAV file: {ex.Message}");
                audioData = null;
            }
        }

        private void DrawSpectrum(float currentTimeSec)
        {
            if (audioData == null || audioData.Length == 0) return;

            Complex[] fftBuffer = new Complex[fftSize];
            for (int i = 0; i < fftSize; i++)
            {
                int sampleIndex = currentSampleIndex + i;
                if (sampleIndex < audioData.Length)
                    fftBuffer[i] = new Complex(audioData[sampleIndex], 0);
                else
                    fftBuffer[i] = new Complex(0, 0);
            }

            FastFourierTransform(fftBuffer);

            int fullSpectrumSize = fftSize / 2;
            int spectrumSize = Math.Min(fullSpectrumSize, maxSpectrumSize);
            float[] spectrum = new float[spectrumSize];
            for (int i = 0; i < spectrumSize; i++)
            {
                spectrum[i] = (float)Math.Sqrt(fftBuffer[i].Real * fftBuffer[i].Real + fftBuffer[i].Imaginary * fftBuffer[i].Imaginary);
            }

            using (Bitmap bmp = new Bitmap(pictureBox.Width, pictureBox.Height))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Black);

                int centerY = pictureBox.Height / 2;

                using (Pen centerPen = new Pen(Color.Green, 1)) // 中央線を緑に変更
                {
                    g.DrawLine(centerPen, 0, centerY, pictureBox.Width, centerY);
                }

                float maxAmplitude = spectrum.Max();
                if (maxAmplitude == 0) maxAmplitude = 1;

                float barWidth = (float)pictureBox.Width / spectrumSize;

                for (int i = 0; i < spectrumSize; i++)
                {
                    float scaledValue = (spectrum[i] / maxAmplitude) * (pictureBox.Height / 2);
                    scaledValue = Math.Max(0, scaledValue);

                    int x = (int)(i * barWidth);
                    int actualBarWidth = Math.Max(1, (int)barWidth - gapParameter); // スキマパラメータを適用

                    int yTop = centerY - (int)scaledValue;
                    g.FillRectangle(Brushes.Green, x, yTop, actualBarWidth, (int)scaledValue);

                    int yBottom = centerY;
                    g.FillRectangle(Brushes.Green, x, yBottom, actualBarWidth, (int)scaledValue);
                }

                if (saveImagesCheckBox.Checked)
                {
                    try
                    {
                        string exePath = AppDomain.CurrentDomain.BaseDirectory;
                        string outputFolder = Path.Combine(exePath, "output");

                        string fileName = frameCount.ToString("D8") + ".png";
                        string filePath = Path.Combine(outputFolder, fileName);

                        bmp.Save(filePath, System.Drawing.Imaging.ImageFormat.Png);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error saving image: {ex.Message}");
                    }
                }

                pictureBox.Image?.Dispose();
                pictureBox.Image = (Bitmap)bmp.Clone();
            }
        }
        private void DrawSpectrum2(float currentTimeSec)
        {
            if (audioData == null || audioData.Length == 0) return;

            Complex[] fftBuffer = new Complex[fftSize];
            // ハニング窓を適用
            for (int i = 0; i < fftSize; i++)
            {
                int sampleIndex = currentSampleIndex + i;
                float windowValue = 0.5f * (1.0f - (float)Math.Cos(2.0 * Math.PI * i / (fftSize - 1)));
                if (sampleIndex < audioData.Length)
                    fftBuffer[i] = new Complex(audioData[sampleIndex] * windowValue, 0);
                else
                    fftBuffer[i] = new Complex(0, 0);
            }

            FastFourierTransform(fftBuffer);

            int fullSpectrumSize = fftSize / 2;
            int spectrumSize = Math.Min(fullSpectrumSize, maxSpectrumSize);
            float[] spectrum = new float[spectrumSize];
            for (int i = 0; i < spectrumSize; i++)
            {
                spectrum[i] = (float)Math.Sqrt(fftBuffer[i].Real * fftBuffer[i].Real + fftBuffer[i].Imaginary * fftBuffer[i].Imaginary);
            }

            using (Bitmap bmp = new Bitmap(pictureBox.Width, pictureBox.Height))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Black);

                int centerY = pictureBox.Height / 2;

                using (Pen centerPen = new Pen(Color.Green, 1))
                {
                    g.DrawLine(centerPen, 0, centerY, pictureBox.Width, centerY);
                }

                float maxAmplitude = spectrum.Max();
                if (maxAmplitude == 0) maxAmplitude = 1;

                int startIndex = 5; // 低周波スキップ（オプション）
                int effectiveSpectrumSize = spectrumSize - startIndex;
                if (effectiveSpectrumSize <= 0) effectiveSpectrumSize = 1;

                float barWidth = (float)pictureBox.Width / effectiveSpectrumSize;

                for (int i = startIndex; i < spectrumSize; i++)
                {
                    float scaledValue = (spectrum[i] / maxAmplitude) * (pictureBox.Height / 2);
                    scaledValue = Math.Max(0, scaledValue);

                    int x = (int)((i - startIndex) * barWidth);
                    int actualBarWidth = Math.Max(1, (int)barWidth - gapParameter);

                    int yTop = centerY - (int)scaledValue;
                    g.FillRectangle(Brushes.Green, x, yTop, actualBarWidth, (int)scaledValue);

                    int yBottom = centerY;
                    g.FillRectangle(Brushes.Green, x, yBottom, actualBarWidth, (int)scaledValue);
                }

                if (saveImagesCheckBox.Checked)
                {
                    try
                    {
                        string exePath = AppDomain.CurrentDomain.BaseDirectory;
                        string outputFolder = Path.Combine(exePath, "output");

                        string fileName = frameCount.ToString("D8") + ".png";
                        string filePath = Path.Combine(outputFolder, fileName);

                        bmp.Save(filePath, System.Drawing.Imaging.ImageFormat.Png);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error saving image: {ex.Message}");
                    }
                }

                pictureBox.Image?.Dispose();
                pictureBox.Image = (Bitmap)bmp.Clone();
            }
        }


        private void FastFourierTransform(Complex[] data)
        {
            int n = data.Length;
            if (n <= 1) return;

            Complex[] even = new Complex[n / 2];
            Complex[] odd = new Complex[n / 2];
            for (int i = 0; i < n / 2; i++)
            {
                even[i] = data[2 * i];
                odd[i] = data[2 * i + 1];
            }

            FastFourierTransform(even);
            FastFourierTransform(odd);

            for (int k = 0; k < n / 2; k++)
            {
                Complex t = Complex.FromPolarCoordinates(1.0, -2 * Math.PI * k / n) * odd[k];
                data[k] = even[k] + t;
                data[k + n / 2] = even[k] - t;
            }
        }




    }
}
