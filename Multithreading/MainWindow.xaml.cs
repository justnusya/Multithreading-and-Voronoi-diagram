using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.IO;

namespace Multithreading
{
    public partial class MainWindow : Window
    {
        private readonly List<Horse> horses = new List<Horse>();
        private readonly Random random = new Random();
        private Barrier raceBarrier;
        private bool isRaceRunning = false;
        private DispatcherTimer renderTimer;
        private Canvas raceTrack;
        private List<Image> horseImages = new List<Image>();
        private List<Image> backgroundTracks = new List<Image>();
        private DataGrid resultsDataGrid;
        private List<BitmapImage> horseAnimationFrames = new List<BitmapImage>();
        private double backgroundOffset = 0;
        private const double BackgroundSpeed = 5;
        private const double TrackImageWidth = 356;
        private const double TrackImageHeight = 348;
        private int balance = 250;
        private int bet = 20;
        private List<string> horseNames = new List<string>() {"1. Lucky","2. Spirit","3. Myshko","4. Flesh"};
        private int horseIndex = 0;
        private bool isBetPlaced = false;
        private int betHorseIndex = -1;
        private int betAmount = 0;
        private int finishedHorsesCount = 0;
        private bool isVzyatka = false;
        private bool isPopavsya = false;

        public MainWindow()
        {
            InitializeComponent();
            SetupUI();
            InitializeRaceTrack();
            LoadAnimationFrames();
        }

        private void LoadAnimationFrames()
        {
            for (int i = 0; i < 12; i++)
            {
                string framePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"WithOutBorder_{i:D4}.png");
                if (File.Exists(framePath))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(framePath);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    horseAnimationFrames.Add(bitmap);
                }
            }
        }

        private void SetupUI()
        {
            var mainGrid = (Grid)this.Content;

            raceTrack = new Canvas
            {
                ClipToBounds = true,
                Margin = new Thickness(10)
            };
            Grid.SetColumn(raceTrack, 1);
            Grid.SetRow(raceTrack, 0);
            mainGrid.Children.Add(raceTrack);

            for (int i = 0; i < 8; i++)
            {
                var trackImage = new Image
                {
                    Source = new BitmapImage(new Uri(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Track.png"))),
                    Width = TrackImageWidth,
                    Height = TrackImageHeight,
                    Stretch = Stretch.UniformToFill //Stretch.Uniform для пропорційного масштабування
                };
                Canvas.SetLeft(trackImage, i * (TrackImageWidth - 14));
                Canvas.SetTop(trackImage, 0);
                raceTrack.Children.Add(trackImage);
                backgroundTracks.Add(trackImage);
            }

            var finishLine = new Rectangle
            {
                Width = 10,
                Height = raceTrack.ActualHeight,
                Fill = Brushes.Red,
                Opacity = 0.7
            };
            Canvas.SetLeft(finishLine, raceTrack.ActualWidth - 60);
            Canvas.SetTop(finishLine, 0);
            raceTrack.Children.Add(finishLine);

            var controlPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 10)
            };

            var startButton = new Button
            {
                Content = "Start Race",
                Width = 100,
                Margin = new Thickness(5)
            };
            startButton.Click += StartRace_Click;

            controlPanel.Children.Add(startButton);

            Grid.SetColumn(controlPanel, 1);
            Grid.SetRow(controlPanel, 0);
            mainGrid.Children.Add(controlPanel);

            resultsDataGrid = mainGrid.Children.OfType<DataGrid>().FirstOrDefault();
        }

        private void InitializeRaceTrack()
        {
            raceTrack.Children.Clear();
            horseImages.Clear();

            foreach (var track in backgroundTracks)
            {
                raceTrack.Children.Add(track);
            }

            var finishLine = new Rectangle
            {
                Width = 10,
                Height = raceTrack.ActualHeight,
                Fill = Brushes.Red,
                Opacity = 0.7
            };
            Canvas.SetLeft(finishLine, raceTrack.ActualWidth - 60);
            Canvas.SetTop(finishLine, 0);
            raceTrack.Children.Add(finishLine);
        }

        private async void StartRace_Click(object sender, RoutedEventArgs e)
        {
            if (isRaceRunning) return;
            if (!isBetPlaced)
            {
                MessageBox.Show("Please place a bet before starting the race.");
                return;
            }

            finishedHorsesCount = 0;
            isRaceRunning = true;
            horses.Clear();
            raceTrack.Children.Clear();
            InitializeRaceTrack();

            //CancellationTokenSource для управління потоками
            var cts = new CancellationTokenSource();

            int horseCount = 4;
            for (int i = 0; i < horseCount; i++)
            {
                var horse = new Horse(horseNames[i], Colors.Transparent);
                horses.Add(horse);

                var horseImage = new Image
                {
                    Width = 70,
                    Height = 50,
                    Source = horseAnimationFrames[0],
                    Tag = i
                };
                Canvas.SetLeft(horseImage, 10);
                Canvas.SetTop(horseImage, i * 50 + 60);
                raceTrack.Children.Add(horseImage);
                horseImages.Add(horseImage);
            }

            UpdateResultsGrid();

            var raceTasks = new List<Task>();
            foreach (var horse in horses)
            {
                raceTasks.Add(Task.Run(() => RunHorse(horse, cts.Token)));
            }

            renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            renderTimer.Tick += (s, args) => RenderFrame(cts.Token);
            renderTimer.Start();

            // Чекаємо завершення гонки з можливістю скасування
            await Task.Run(() =>
            {
                while (finishedHorsesCount < horses.Count && !cts.Token.IsCancellationRequested)
                {
                    Thread.Sleep(100);
                }
            });

            // Завершуємо всі потоки
            cts.Cancel();
            renderTimer.Stop();
            isRaceRunning = false;

            var winner = horses.OrderByDescending(h => h.Position).First();
            int winnerIndex = horses.IndexOf(winner);
            if (isVzyatka)
            {
                Random rand = new Random();
                int number = rand.Next(0, 3);

                if (number == 0)
                {
                    MessageBox.Show("Оце ти попав. Десять год тюрми!");
                    balance = 0;
                    isPopavsya = true;
                }
                else
                {
                    MessageBox.Show($"Forget about horse. YOU WON {balance}$!");
                    balance *= 2;
                }
            }
            else
            {
                if (winnerIndex == betHorseIndex)
                {
                    double coefficient = 1 + (horses.Count - winnerIndex) * 0.2;
                    int winnings = (int)(betAmount * coefficient);
                    balance += winnings;
                    MessageBox.Show($"Your horse won! You won {winnings}$.\nNew balance: {balance}$");
                }
                else
                {
                    balance -= betAmount;
                    MessageBox.Show($"Your horse lost. You lost {betAmount}$.\nNew balance: {balance}$");
                }
            }

            BalanceText.Text = $"Balance: {balance}$";
            isBetPlaced = false;
            isVzyatka = false;
            VzyatkaText.Text = $"Give vzyatka: {balance}$";
        }

        private void RunHorse(Horse horse, CancellationToken token)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            while (horse.Position < raceTrack.ActualWidth - 100 && !token.IsCancellationRequested)
            {
                if (token.IsCancellationRequested)
                    break;

                horse.ChangeAcceleration(random);
                horse.Position += horse.Acceleration;
                Thread.Sleep(random.Next(10, 50));
            }

            stopwatch.Stop();
            horse.Time = stopwatch.Elapsed;
            Interlocked.Increment(ref finishedHorsesCount);
        }

        private void RenderFrame(CancellationToken token)
        {
            if (token.IsCancellationRequested)
                return;

            backgroundOffset += BackgroundSpeed;
            if (backgroundOffset >= TrackImageWidth - 14)
                backgroundOffset = 0;

            for (int i = 0; i < backgroundTracks.Count; i++)
            {
                Canvas.SetLeft(backgroundTracks[i], i * (TrackImageWidth - 14) - backgroundOffset);
            }

            for (int i = 0; i < horses.Count; i++)
            {
                if (horseImages.Count > i)
                {
                    Canvas.SetLeft(horseImages[i], horses[i].Position);
                    int frameIndex = (Environment.TickCount / 100) % 12;
                    horseImages[i].Source = horseAnimationFrames[frameIndex];
                }
            }

            UpdateResultsGrid();
        }

        private void UpdateResultsGrid()
        {
            var sortedHorses = horses
        .OrderByDescending(h => h.Position)
        .ThenBy(h => h.Time)
        .Select((h, index) =>
        {
            double coefficient = 1 + (horses.Count - index) * 0.2;
            double money = bet * coefficient;

            return new
            {
                Position = index + 1,
                Name = h.Name,
                Time = h.Time == TimeSpan.Zero ? "Running..." : h.Time.ToString(@"mm\:ss\.fff"),
                Coefficient = coefficient.ToString("F2"),
                Money = $"{money:F2}$"
            };
        })
        .ToList();

            resultsDataGrid.ItemsSource = sortedHorses;
        }

        private void LessMoneyButton_Click(object sender, RoutedEventArgs e)
        {
            if (bet > 20)
            {
                bet-=20;
                betText.Text = $"{bet}$";
            }
            UpdateResultsGrid();
        }
        private void MoreMoneyButton_Click(object sender, RoutedEventArgs e)
        {
            if (bet <= balance - 20)
            {
                bet += 20;
                betText.Text = $"{bet}$";
            }
            UpdateResultsGrid();
        }
        private void PrevHorseButton_Click(object sender, RoutedEventArgs e)
        {
            horseIndex--;
            if (horseIndex < 0) horseIndex = 3;
            HorseNameText.Text = horseNames[horseIndex];
        }

        private void NextHorseButton_Click(object sender, RoutedEventArgs e)
        {
            horseIndex++;
            if (horseIndex > 3) horseIndex = 0;
            HorseNameText.Text = horseNames[horseIndex];
        }

        private void BetButton_Click(object sender, RoutedEventArgs e)
        {
            if (isRaceRunning)
            {
                MessageBox.Show("Wait till the end");
                return;
            }
            if (isPopavsya)
            {
                MessageBox.Show("ДЕЕЕСЯТЬ ГОД ТЮРМИ!!!");
                return;
            }
            if(balance < 20)
            {
                MessageBox.Show("А по карманах то пусто!");
                return;
            }
            betHorseIndex = horseIndex;
            betAmount = bet;
            isBetPlaced = true;

            MessageBox.Show($"You have bet {betAmount}$ on horse: {horseNames[betHorseIndex]}");
        }

        private void Vzyatka_Click(object sender, RoutedEventArgs e)
        {
            if (isRaceRunning)
            {
                MessageBox.Show("Wait till the end");
                return;
            }
            if (isPopavsya)
            {
                MessageBox.Show("ДЕЕЕСЯТЬ ГОД ТЮРМИ!!!");
                return;
            }
            if (balance < 20)
            {
                MessageBox.Show("А по карманах то пусто!");
                return;
            }
            isBetPlaced = true;
            isVzyatka = true;
            MessageBox.Show($"You give {balance}$ vzyatka");
        }
    }

    public class Horse
    {
        public string Name { get; private set; }
        public Color Color { get; private set; }
        public double Position { get; set; }
        public double Speed { get; private set; }
        public double Acceleration { get; private set; }
        public TimeSpan Time { get; set; }

        public Horse(string name, Color color)
        {
            Name = name;
            Color = color;
            Position = 0;
            var random = new Random();
            Speed = random.Next(5, 10);
            Acceleration = Speed;
        }

        public void ChangeAcceleration(Random random)
        {
            Acceleration = Speed * random.NextDouble() * 0.8 + 0.7;
        }
    }
}