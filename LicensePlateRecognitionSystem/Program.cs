// Program.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using OpenCvSharp;
using Tesseract;

namespace PlateRecognitionSystem
{
    // Ana Program
    class Program
    {
        [STAThread]
        public static void Main(string[] args) => BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace();
    }

    // App
    public class App : Application
    {
        public override void Initialize()
        {
            // XAML kullanmıyoruz, boş bırak
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow();
            }

            base.OnFrameworkInitializationCompleted();
        }
    }

    // MainWindow
    public class MainWindow : Avalonia.Controls.Window
    {
        // UI Controls
        private Avalonia.Controls.Image cameraImage;
        private TextBlock plateTextBlock;
        private TextBlock statusTextBlock;
        private ListBox logListBox;
        private ListBox platesListBox;
        private TextBox newPlateTextBox;
        private Button startButton;
        private Button stopButton;
        private Button addPlateButton;
        private Button deleteButton;

        // System variables
        private VideoCapture? capture;
        private TesseractEngine? ocrEngine;
        private List<string> authorizedPlates = new();
        private DateTime lastDetectionTime = DateTime.MinValue;
        private bool isRunning = false;
        private CancellationTokenSource? cancellationTokenSource;
        
        private const int DETECTION_COOLDOWN_SECONDS = 5;
        private const string JSON_FILE_PATH = "authorized_plates.json";

        public MainWindow()
        {
            InitializeComponent();
            InitializeOCR();
            LoadAuthorizedPlates();
        }

        private void InitializeComponent()
        {
            Width = 1200;
            Height = 800;
            Title = "Otopark Plaka Tanıma Sistemi - .NET 9";
            CanResize = false;

            var mainGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("800,*"),
                Margin = new Thickness(10)
            };

            // Sol panel
            var leftPanel = CreateLeftPanel();
            Grid.SetColumn(leftPanel, 0);
            mainGrid.Children.Add(leftPanel);

            // Sağ panel
            var rightPanel = CreateRightPanel();
            Grid.SetColumn(rightPanel, 1);
            mainGrid.Children.Add(rightPanel);

            Content = mainGrid;
        }

        private StackPanel CreateLeftPanel()
        {
            var panel = new StackPanel { Spacing = 10 };

            // Başlık
            var titleLabel = new TextBlock
            {
                Text = "Kamera Görüntüsü",
                FontSize = 18,
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
            };
            panel.Children.Add(titleLabel);

            // Kamera görseli
            cameraImage = new Avalonia.Controls.Image
            {
                Width = 780,
                Height = 585,
                Stretch = Stretch.Fill
            };
            var cameraBorder = new Border
            {
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(2),
                Child = cameraImage,
                Background = Brushes.Black
            };
            panel.Children.Add(cameraBorder);

            // Durum
            statusTextBlock = new TextBlock
            {
                Text = "Durum: Hazır",
                FontSize = 16,
                FontWeight = FontWeight.Bold,
                Foreground = Brushes.Green,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                Background = Brushes.LightGray,
                Padding = new Thickness(10),
                Width = 780
            };
            panel.Children.Add(statusTextBlock);

            return panel;
        }

        private StackPanel CreateRightPanel()
        {
            var panel = new StackPanel { Spacing = 15, Width = 360 };

            // Okunan Plaka
            panel.Children.Add(CreatePlateSection());

            // Kontrol Butonları
            panel.Children.Add(CreateControlButtons());

            // Log
            panel.Children.Add(CreateLogSection());

            // Yetkili Plakalar
            panel.Children.Add(CreatePlatesSection());

            return panel;
        }

        private StackPanel CreatePlateSection()
        {
            var section = new StackPanel { Spacing = 5 };

            var label = new TextBlock
            {
                Text = "Okunan Plaka:",
                FontSize = 14,
                FontWeight = FontWeight.Bold
            };
            section.Children.Add(label);

            plateTextBlock = new TextBlock
            {
                FontSize = 32,
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                Background = Brushes.White,
                Padding = new Thickness(10),
                Width = 360,
                Height = 70
            };
            var plateBorder = new Border
            {
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(2),
                Child = plateTextBlock
            };
            section.Children.Add(plateBorder);

            return section;
        }

        private StackPanel CreateControlButtons()
        {
            var panel = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 10
            };

            startButton = new Button
            {
                Content = "Başlat",
                Width = 175,
                Height = 45,
                FontSize = 14,
                FontWeight = FontWeight.Bold,
                Background = Brushes.LightGreen
            };
            startButton.Click += StartButton_Click;
            panel.Children.Add(startButton);

            stopButton = new Button
            {
                Content = "Durdur",
                Width = 175,
                Height = 45,
                FontSize = 14,
                FontWeight = FontWeight.Bold,
                Background = Brushes.LightCoral,
                IsEnabled = false
            };
            stopButton.Click += StopButton_Click;
            panel.Children.Add(stopButton);

            return panel;
        }

        private StackPanel CreateLogSection()
        {
            var section = new StackPanel { Spacing = 5 };

            var label = new TextBlock
            {
                Text = "Sistem Kayıtları:",
                FontSize = 14,
                FontWeight = FontWeight.Bold
            };
            section.Children.Add(label);

            logListBox = new ListBox
            {
                Height = 150,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 10
            };
            var logBorder = new Border
            {
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                Child = logListBox
            };
            section.Children.Add(logBorder);

            return section;
        }

        private StackPanel CreatePlatesSection()
        {
            var section = new StackPanel { Spacing = 5 };

            var label = new TextBlock
            {
                Text = "Yetkili Plakalar:",
                FontSize = 14,
                FontWeight = FontWeight.Bold
            };
            section.Children.Add(label);

            platesListBox = new ListBox
            {
                Height = 120,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12
            };
            var platesBorder = new Border
            {
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                Child = platesListBox
            };
            section.Children.Add(platesBorder);

            // Ekleme paneli
            var addPanel = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 5
            };

            newPlateTextBox = new TextBox
            {
                Width = 150,
                Watermark = "34ABC123",
                MaxLength = 8
            };
            addPanel.Children.Add(newPlateTextBox);

            addPlateButton = new Button
            {
                Content = "Ekle",
                Width = 100
            };
            addPlateButton.Click += AddPlateButton_Click;
            addPanel.Children.Add(addPlateButton);

            deleteButton = new Button
            {
                Content = "Sil",
                Width = 100
            };
            deleteButton.Click += DeleteButton_Click;
            addPanel.Children.Add(deleteButton);

            section.Children.Add(addPanel);

            return section;
        }

        private void InitializeOCR()
        {
            try
            {
                // Mac için Tesseract yolu
                string tessdataPath = "./tessdata";
                
                if (!Directory.Exists(tessdataPath))
                {
                    Directory.CreateDirectory(tessdataPath);
                }

                ocrEngine = new TesseractEngine(tessdataPath, "eng", EngineMode.Default);
                ocrEngine.SetVariable("tessedit_char_whitelist", "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789");
                AddLog("OCR motoru başlatıldı.");
            }
            catch (Exception ex)
            {
                AddLog($"OCR hatası: {ex.Message}");
            }
        }

        private void LoadAuthorizedPlates()
        {
            try
            {
                if (File.Exists(JSON_FILE_PATH))
                {
                    string json = File.ReadAllText(JSON_FILE_PATH);
                    var data = JsonSerializer.Deserialize<PlateData>(json);
                    authorizedPlates = data?.AuthorizedPlates ?? new List<string>();
                }
                else
                {
                    authorizedPlates = new List<string> { "34ABC123", "06XYZ789", "35TEST01" };
                    SaveAuthorizedPlates();
                }

                RefreshPlatesList();
                AddLog($"{authorizedPlates.Count} yetkili plaka yüklendi.");
            }
            catch (Exception ex)
            {
                AddLog($"Yükleme hatası: {ex.Message}");
            }
        }

        private void SaveAuthorizedPlates()
        {
            try
            {
                var data = new PlateData { AuthorizedPlates = authorizedPlates };
                string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(JSON_FILE_PATH, json);
            }
            catch (Exception ex)
            {
                AddLog($"Kaydetme hatası: {ex.Message}");
            }
        }

        private void RefreshPlatesList()
        {
            Dispatcher.UIThread.Post(() =>
            {
                platesListBox.Items.Clear();
                foreach (var plate in authorizedPlates)
                {
                    platesListBox.Items.Add(plate);
                }
            });
        }

        private async void StartButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            try
            {
                capture = new VideoCapture(0);
                
                if (!capture.IsOpened())
                {
                    AddLog("Kamera açılamadı!");
                    return;
                }

                capture.Set(VideoCaptureProperties.FrameWidth, 640);
                capture.Set(VideoCaptureProperties.FrameHeight, 480);

                isRunning = true;
                startButton.IsEnabled = false;
                stopButton.IsEnabled = true;
                statusTextBlock.Text = "Durum: Çalışıyor";
                statusTextBlock.Foreground = Brushes.Green;
                AddLog("Sistem başlatıldı.");

                cancellationTokenSource = new CancellationTokenSource();
                await Task.Run(() => CameraLoop(cancellationTokenSource.Token));
            }
            catch (Exception ex)
            {
                AddLog($"Başlatma hatası: {ex.Message}");
            }
        }

        private void StopButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            StopSystem();
        }

        private void StopSystem()
        {
            isRunning = false;
            cancellationTokenSource?.Cancel();

            capture?.Release();
            capture?.Dispose();
            capture = null;

            Dispatcher.UIThread.Post(() =>
            {
                startButton.IsEnabled = true;
                stopButton.IsEnabled = false;
                statusTextBlock.Text = "Durum: Durduruldu";
                statusTextBlock.Foreground = Brushes.Red;
                AddLog("Sistem durduruldu.");
            });
        }

        private async Task CameraLoop(CancellationToken token)
        {
            using var frame = new Mat();

            while (isRunning && !token.IsCancellationRequested)
            {
                try
                {
                    capture?.Read(frame);

                    if (!frame.Empty())
                    {
                        // Frame'i göster
                        await DisplayFrame(frame);

                        // Plaka tanıma
                        if ((DateTime.Now - lastDetectionTime).TotalSeconds >= DETECTION_COOLDOWN_SECONDS)
                        {
                            string? plate = RecognizePlate(frame);
                            if (!string.IsNullOrEmpty(plate))
                            {
                                await Dispatcher.UIThread.InvokeAsync(() =>
                                {
                                    plateTextBlock.Text = plate;
                                    plateTextBlock.Background = Brushes.LightYellow;
                                });

                                CheckAndOpenBarrier(plate);
                                lastDetectionTime = DateTime.Now;
                            }
                        }
                    }

                    await Task.Delay(33, token); // ~30 FPS
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    AddLog($"Kamera hatası: {ex.Message}");
                }
            }
        }

        private async Task DisplayFrame(Mat frame)
        {
            try
            {
                using var bitmap = OpenCvSharp.Extensions.BitmapConverter.ToBitmap(frame);
                using var memoryStream = new MemoryStream();
                
                bitmap.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Png);
                memoryStream.Position = 0;

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    cameraImage.Source = new Bitmap(memoryStream);
                });
            }
            catch (Exception ex)
            {
                AddLog($"Görüntü hatası: {ex.Message}");
            }
        }

        private string? RecognizePlate(Mat frame)
        {
            try
            {
                if (ocrEngine == null) return null;

                // Gri tonlama
                using var gray = new Mat();
                Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);

                // Kontrast artırma
                Cv2.EqualizeHist(gray, gray);

                // Bulanıklaştırma
                Cv2.GaussianBlur(gray, gray, new OpenCvSharp.Size(5, 5), 0);

                // Threshold
                using var thresh = new Mat();
                Cv2.Threshold(gray, thresh, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);

                // Mat'i byte array'e çevir
                byte[] imageBytes = thresh.ToBytes(".png");

                // Byte array'den Pix oluştur
                using var pix = Pix.LoadFromMemory(imageBytes);
                using var page = ocrEngine.Process(pix);
                string text = page.GetText().Trim();

                // Temizle ve doğrula
                string plate = CleanPlateText(text);
                
                if (IsValidTurkishPlate(plate))
                {
                    return plate;
                }
            }
            catch (Exception ex)
            {
                AddLog($"OCR hatası: {ex.Message}");
            }

            return null;
        }

        private string CleanPlateText(string text)
        {
            return Regex.Replace(text, @"[^A-Z0-9]", "").ToUpper();
        }

        private bool IsValidTurkishPlate(string plate)
        {
            if (string.IsNullOrEmpty(plate)) return false;
            if (plate.Length < 7 || plate.Length > 8) return false;
            if (!char.IsDigit(plate[0]) || !char.IsDigit(plate[1])) return false;
            return true;
        }

        private void CheckAndOpenBarrier(string plate)
        {
            if (authorizedPlates.Contains(plate))
            {
                AddLog($"✓ YETKİLİ: {plate} - Bariyer açılıyor...");
                Dispatcher.UIThread.Post(() => plateTextBlock.Background = Brushes.LightGreen);
                OpenBarrier();
            }
            else
            {
                AddLog($"✗ YETKİSİZ: {plate} - Giriş reddedildi!");
                Dispatcher.UIThread.Post(() => plateTextBlock.Background = Brushes.LightCoral);
            }

            // 3 saniye sonra rengi sıfırla
            Task.Delay(3000).ContinueWith(_ =>
            {
                Dispatcher.UIThread.Post(() => plateTextBlock.Background = Brushes.White);
            });
        }

        private void OpenBarrier()
        {
            // Buraya Arduino/ESP32 entegrasyonu eklenecek
            AddLog("→ Bariyer açma komutu gönderildi (simülasyon)");
        }

        private void AddPlateButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            string newPlate = newPlateTextBox.Text?.Trim().ToUpper() ?? "";

            if (string.IsNullOrEmpty(newPlate))
            {
                AddLog("Plaka boş olamaz!");
                return;
            }

            if (!IsValidTurkishPlate(newPlate))
            {
                AddLog("Geçersiz plaka formatı! Örnek: 34ABC123");
                return;
            }

            if (authorizedPlates.Contains(newPlate))
            {
                AddLog("Bu plaka zaten kayıtlı!");
                return;
            }

            authorizedPlates.Add(newPlate);
            SaveAuthorizedPlates();
            RefreshPlatesList();
            newPlateTextBox.Text = "";
            AddLog($"Yeni plaka eklendi: {newPlate}");
        }

        private void DeleteButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (platesListBox.SelectedItem is string selectedPlate)
            {
                authorizedPlates.Remove(selectedPlate);
                SaveAuthorizedPlates();
                RefreshPlatesList();
                AddLog($"Plaka silindi: {selectedPlate}");
            }
        }

        private void AddLog(string message)
        {
            Dispatcher.UIThread.Post(() =>
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss");
                logListBox.Items.Insert(0, $"[{timestamp}] {message}");

                if (logListBox.Items.Count > 100)
                {
                    logListBox.Items.RemoveAt(logListBox.Items.Count - 1);
                }
            });
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            StopSystem();
            ocrEngine?.Dispose();
            base.OnClosing(e);
        }
    }

    public class PlateData
    {
        public List<string> AuthorizedPlates { get; set; } = new();
    }
}