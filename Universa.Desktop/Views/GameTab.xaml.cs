using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Universa.Desktop.Core.Configuration;
using Universa.Desktop.Models;
using Universa.Desktop.Services;
using System.Diagnostics;
using System.Windows.Threading;

namespace Universa.Desktop.Views
{
    public partial class GameTab : UserControl, INotifyPropertyChanged
    {
        private readonly Random _random = new Random();
        private readonly DispatcherTimer _newsTimer = new DispatcherTimer();
        private readonly DispatcherTimer _hourlyTimer = new DispatcherTimer();
        private readonly DispatcherTimer _tickerTimer = new DispatcherTimer();
        private readonly IConfigurationService _configService;
        private ObservableCollection<StockModel> _stocks = new ObservableCollection<StockModel>();
        private ObservableCollection<StockModel> _displayedStocks = new ObservableCollection<StockModel>();
        private ObservableCollection<PortfolioItem> _portfolio = new ObservableCollection<PortfolioItem>();
        private List<string> _newsFeed = new List<string>();
        private int _currentDay = 1;
        private int _currentHour = 9; // Market opens at 9 AM
        private decimal _cash = 10000.00M;
        private string _saveGamePath;
        private bool _marketOpen = true;
        private int _tickerIndex = 0;

        public event PropertyChangedEventHandler PropertyChanged;

        public GameTab()
        {
            InitializeComponent();
            
            try
            {
                _configService = ServiceLocator.Instance.GetRequiredService<IConfigurationService>();
                _saveGamePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Universa", "StockGame");
                
                // Ensure save directory exists
                if (!Directory.Exists(_saveGamePath))
                {
                    Directory.CreateDirectory(_saveGamePath);
                }
                
                // Set data sources
                StockListView.ItemsSource = _displayedStocks;
                PortfolioListView.ItemsSource = _portfolio;
                
                // Initialize news timer
                _newsTimer.Interval = TimeSpan.FromSeconds(30);
                _newsTimer.Tick += (s, e) => GenerateRandomNews();
                _newsTimer.Start();
                
                // Initialize hourly timer
                _hourlyTimer.Interval = TimeSpan.FromSeconds(10); // 10 seconds = 1 hour in game time
                _hourlyTimer.Tick += HourlyTimer_Tick;
                
                // Initialize ticker timer
                _tickerTimer.Interval = TimeSpan.FromSeconds(2);
                _tickerTimer.Tick += TickerTimer_Tick;
                _tickerTimer.Start();
                
                // Initialize game
                InitializeGame();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error initializing game tab: {ex.Message}");
                MessageBox.Show($"Error initializing game: {ex.Message}", "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void HourlyTimer_Tick(object sender, EventArgs e)
        {
            // Advance the hour
            _currentHour++;
            
            // Check if day is over (market closes at 5 PM)
            if (_currentHour > 17)
            {
                _currentHour = 9; // Reset to 9 AM
                _currentDay++;    // Advance to next day
                
                // Generate end of day summary
                GenerateMarketSummary();
            }
            
            // Update display
            UpdateDisplay();
            
            // Simulate hourly market changes
            SimulateHourlyMarket();
            
            // Random events (10% chance each hour)
            if (_random.NextDouble() < 0.1)
            {
                GenerateRandomEvent();
            }
        }

        private void TickerTimer_Tick(object sender, EventArgs e)
        {
            if (_stocks.Count == 0) return;
            
            // Store the currently selected stock symbol before updating
            string selectedSymbol = null;
            if (StockListView.SelectedItem is StockModel selectedStock)
            {
                selectedSymbol = selectedStock.Symbol;
            }
            
            // Clear the displayed stocks
            _displayedStocks.Clear();
            
            // Calculate how many stocks to display (based on available space)
            int visibleItems = 10; // Approximate number of visible items in the ListView
            
            // Add the next batch of stocks to display
            for (int i = 0; i < visibleItems; i++)
            {
                int index = (_tickerIndex + i) % _stocks.Count;
                _displayedStocks.Add(_stocks[index]);
            }
            
            // Increment the ticker index
            _tickerIndex = (_tickerIndex + 1) % _stocks.Count;
            
            // Restore selection if the stock is still visible
            if (!string.IsNullOrEmpty(selectedSymbol))
            {
                foreach (StockModel stock in _displayedStocks)
                {
                    if (stock.Symbol == selectedSymbol)
                    {
                        StockListView.SelectedItem = stock;
                        break;
                    }
                }
            }
        }

        private void SimulateHourlyMarket()
        {
            // Update hour
            _currentHour++;
            
            // Check if market day is over
            if (_currentHour > 17)
            {
                _currentHour = 9;
                _currentDay++;
                GenerateMarketSummary();
            }
            
            // Update market open status
            _marketOpen = (_currentHour >= 9 && _currentHour <= 17);
            
            // Only simulate price changes when market is open
            if (_marketOpen)
            {
                // Update each stock price
                foreach (var stock in _stocks)
                {
                    // Store previous price
                    stock.PreviousPrice = stock.CurrentPrice;
                    
                    // Calculate random price change based on volatility
                    decimal changePercent = (decimal)(_random.NextDouble() * 2 - 1) * stock.Volatility;
                    
                    // Apply special characteristics
                    if (stock.IsCyclical)
                    {
                        // Cyclical stocks are more affected by market trends
                        if (_random.NextDouble() < 0.7) // 70% chance to follow market trend
                        {
                            bool marketUp = _stocks.Average(s => s.PriceChange) > 0;
                            if (marketUp)
                            {
                                changePercent = Math.Abs(changePercent) * 1.2M; // Amplify positive change
                            }
                            else
                            {
                                changePercent = -Math.Abs(changePercent) * 1.2M; // Amplify negative change
                            }
                        }
                    }
                    else if (stock.IsDefensive)
                    {
                        // Defensive stocks are more stable and often move opposite to market
                        if (_random.NextDouble() < 0.6) // 60% chance to resist market trend
                        {
                            bool marketUp = _stocks.Average(s => s.PriceChange) > 0;
                            if (marketUp)
                            {
                                changePercent = changePercent * 0.7M; // Dampen change in bull market
                            }
                            else
                            {
                                changePercent = changePercent * 0.5M; // Dampen change in bear market
                                if (_random.NextDouble() < 0.4) // 40% chance to move opposite
                                {
                                    changePercent = Math.Abs(changePercent) * 0.5M; // Small positive change in bear market
                                }
                            }
                        }
                    }
                    else if (stock.YieldsFocus)
                    {
                        // Yield-focused stocks are less volatile but more sensitive to market sentiment
                        changePercent = changePercent * 0.8M; // Generally less volatile
                        
                        // Simulate dividend effect occasionally
                        if (_random.NextDouble() < 0.05) // 5% chance
                        {
                            if (_random.NextDouble() < 0.7) // 70% chance of positive dividend news
                            {
                                changePercent = Math.Abs(changePercent) + 0.01M;
                                AddNews($"Dividend increase expected for {stock.Name} ({stock.Symbol}). Investors respond positively.");
                            }
                            else
                            {
                                changePercent = -Math.Abs(changePercent) - 0.01M;
                                AddNews($"Dividend cut concerns for {stock.Name} ({stock.Symbol}). Stock under pressure.");
                            }
                        }
                    }
                    
                    // Apply change
                    stock.CurrentPrice = Math.Max(1.00M, stock.CurrentPrice * (1 + changePercent));
                    stock.CalculateChanges();
                    
                    // Update portfolio if needed
                    var portfolioItem = _portfolio.FirstOrDefault(p => p.Symbol == stock.Symbol);
                    if (portfolioItem != null)
                    {
                        portfolioItem.CurrentPrice = stock.CurrentPrice;
                        portfolioItem.CalculateValues();
                    }
                }
                
                // 10% chance of a random event each hour
                if (_random.NextDouble() < 0.1)
                {
                    GenerateRandomEvent();
                }
            }
            
            // Update display
            UpdateDisplay();
        }

        private void GenerateRandomEvent()
        {
            // Determine if this will be a sector-wide event (30% chance) or individual stock event (70% chance)
            bool isSectorEvent = _random.NextDouble() < 0.3;
            
            if (isSectorEvent)
            {
                GenerateSectorEvent();
            }
            else
            {
                GenerateIndividualStockEvent();
            }
        }
        
        private void GenerateSectorEvent()
        {
            // Get all available sectors
            var sectors = _stocks.Select(s => s.Sector).Distinct().ToList();
            
            // Select a random sector
            string sector = sectors[_random.Next(sectors.Count)];
            
            // Define possible sector events
            string[] sectorEvents = new string[]
            {
                // Positive sector events
                $"New government regulations favor {sector} sector. All {sector} stocks trending upward.",
                $"Breakthrough innovation in {sector} technology. Companies in this sector expected to benefit.",
                $"Strong quarterly earnings reported across {sector} sector. Analysts upgrade outlook.",
                $"International trade agreement benefits {sector} companies. Stocks rally.",
                $"Increased consumer demand for {sector} products. Sector outperforming the broader market.",
                
                // Negative sector events
                $"Regulatory challenges hit {sector} sector. Companies facing compliance costs.",
                $"Supply chain disruptions affecting entire {sector} industry. Stocks tumble.",
                $"Economic slowdown impacts {sector} sector disproportionately. Investors cautious.",
                $"New competitive threats emerge for {sector} companies. Market share concerns.",
                $"Labor disputes across {sector} sector. Production delays expected."
            };
            
            // Add special events for AI/SaaS sector
            if (sector == "AI/SaaS")
            {
                sectorEvents = new string[]
                {
                    // Positive AI/SaaS events
                    "Venture capital funding surges for AI/SaaS startups. Investors bullish on growth potential.",
                    "Major enterprise clients adopting AI/SaaS solutions at record pace. Sector stocks climb.",
                    "New AI breakthrough announced at tech conference. AI/SaaS companies expected to leverage technology.",
                    "Cloud infrastructure costs dropping, benefiting AI/SaaS companies' profit margins.",
                    "Talent migration to AI/SaaS startups accelerates. Innovation expected to increase.",
                    
                    // Negative AI/SaaS events
                    "Data privacy concerns emerge for AI/SaaS companies. Regulatory scrutiny increases.",
                    "AI/SaaS sector valuations called into question by major analysts. Correction possible.",
                    "Enterprise spending on AI/SaaS solutions slows amid economic uncertainty.",
                    "Cybersecurity vulnerabilities discovered in common AI frameworks. Companies scrambling.",
                    "Talent shortage in AI development creating bottlenecks for AI/SaaS companies."
                };
            }
            
            // Select a random event
            int eventIndex = _random.Next(sectorEvents.Length);
            string eventText = sectorEvents[eventIndex];
            
            // Determine impact (positive for first half of events, negative for second half)
            bool isPositive = eventIndex < sectorEvents.Length / 2;
            
            // Apply effect to all stocks in the sector
            foreach (var stock in _stocks.Where(s => s.Sector.Contains(sector)))
            {
                // Store previous price
                stock.PreviousPrice = stock.CurrentPrice;
                
                // Calculate impact factor (1-4% for sector-wide events)
                // For ClearFracture AI, amplify the effect due to being a small startup
                decimal impactFactor;
                if (stock.Symbol == "CLFR")
                {
                    // Small startups are more volatile - 2-8% for sector events
                    impactFactor = isPositive 
                        ? 0.02M + (decimal)(_random.NextDouble() * 0.06) 
                        : -0.02M - (decimal)(_random.NextDouble() * 0.06);
                }
                else
                {
                    impactFactor = isPositive 
                        ? 0.01M + (decimal)(_random.NextDouble() * 0.03) 
                        : -0.01M - (decimal)(_random.NextDouble() * 0.03);
                }
                
                // Apply impact
                stock.CurrentPrice = Math.Max(1.00M, stock.CurrentPrice * (1 + impactFactor));
                stock.CalculateChanges();
                
                // Update portfolio if needed
                var portfolioItem = _portfolio.FirstOrDefault(p => p.Symbol == stock.Symbol);
                if (portfolioItem != null)
                {
                    portfolioItem.CurrentPrice = stock.CurrentPrice;
                    portfolioItem.CalculateValues();
                }
            }
            
            // Add news
            AddNews(eventText);
        }
        
        private void GenerateIndividualStockEvent()
        {
            // Select a random stock
            var stock = _stocks[_random.Next(_stocks.Count)];
            
            // Define possible events
            string[] events;
            
            // Special events for ClearFracture AI
            if (stock.Symbol == "CLFR")
            {
                events = new string[]
                {
                    // Positive ClearFracture AI events
                    $"Breaking: {stock.Name} ({stock.Symbol}) secures major funding round! Stock surges as investors pile in.",
                    $"{stock.Name} ({stock.Symbol}) announces revolutionary new AI algorithm. Tech community buzzing.",
                    $"Major enterprise client signs multi-year contract with {stock.Name} ({stock.Symbol}). Revenue visibility improves.",
                    $"{stock.Name} ({stock.Symbol}) featured in prominent tech publication as 'startup to watch'. Stock climbs.",
                    $"Talent acquisition: {stock.Name} ({stock.Symbol}) hires renowned AI researcher from competitor.",
                    $"{stock.Name} ({stock.Symbol}) announces partnership with major cloud provider. Integration expected to accelerate growth.",
                    $"Beta testing results for {stock.Name}'s ({stock.Symbol}) new platform exceed expectations. Stock rallies.",
                    $"Rumors of acquisition interest in {stock.Name} ({stock.Symbol}) from larger tech companies. Shares jump.",
                    
                    // Negative ClearFracture AI events
                    $"Cash burn concerns at {stock.Name} ({stock.Symbol}). Analysts question runway length.",
                    $"Key technical founder leaves {stock.Name} ({stock.Symbol}) for competitor. Leadership concerns emerge.",
                    $"Product launch delayed at {stock.Name} ({stock.Symbol}). Development challenges cited.",
                    $"Customer acquisition costs rising for {stock.Name} ({stock.Symbol}). Profitability timeline extended.",
                    $"Patent dispute filed against {stock.Name} ({stock.Symbol}). Legal uncertainty weighs on stock.",
                    $"Security vulnerability discovered in {stock.Name}'s ({stock.Symbol}) platform. Fix in progress.",
                    $"Quarterly results show slower growth than expected for {stock.Name} ({stock.Symbol}). Stock drops.",
                    $"Larger competitor announces similar product to {stock.Name}'s ({stock.Symbol}) main offering. Market share concerns."
                };
            }
            else
            {
                events = new string[]
                {
                    // Positive individual stock events
                    $"Breaking: {stock.Name} ({stock.Symbol}) announces surprise acquisition! Stock jumps significantly.",
                    $"{stock.Name} ({stock.Symbol}) reports record quarterly profits! Stock soars.",
                    $"New breakthrough technology announced by {stock.Name} ({stock.Symbol}). Investors excited.",
                    $"Activist investor takes large position in {stock.Name} ({stock.Symbol}). Stock rises on speculation.",
                    $"{stock.Name} ({stock.Symbol}) announces major expansion plans. Market reacts positively.",
                    $"Celebrity endorsement boosts {stock.Name} ({stock.Symbol}) brand visibility. Stock trending up.",
                    $"{stock.Name} ({stock.Symbol}) awarded major government contract. Revenue expectations increase.",
                    $"Analyst upgrade for {stock.Name} ({stock.Symbol}) citing strong growth potential.",
                    
                    // Negative individual stock events
                    $"Regulatory investigation launched into {stock.Name} ({stock.Symbol}). Investors concerned.",
                    $"CEO of {stock.Name} ({stock.Symbol}) unexpectedly resigns. Market reacts negatively.",
                    $"Major product recall at {stock.Name} ({stock.Symbol}). Stock tumbles.",
                    $"Dividend cut announced by {stock.Name} ({stock.Symbol}). Shareholders disappointed.",
                    $"Merger talks between {stock.Name} and competitor fall through. {stock.Symbol} drops.",
                    $"Cybersecurity breach reported at {stock.Name} ({stock.Symbol}). Stock falls on news.",
                    $"Patent litigation against {stock.Name} ({stock.Symbol}) raises legal cost concerns.",
                    $"Earnings miss for {stock.Name} ({stock.Symbol}). Guidance lowered for next quarter."
                };
            }
            
            // Select a random event
            int eventIndex = _random.Next(events.Length);
            string eventText = events[eventIndex];
            
            // Apply effect to stock price
            decimal impactFactor;
            
            // Events in first half are positive, second half are negative
            if (eventIndex < events.Length / 2)
            {
                // For ClearFracture AI, amplify the effect due to being a small startup
                if (stock.Symbol == "CLFR")
                {
                    // Positive event (5-15% increase for small startup)
                    impactFactor = 0.05M + (decimal)(_random.NextDouble() * 0.10);
                }
                else
                {
                    // Positive event (3-10% increase)
                    impactFactor = 0.03M + (decimal)(_random.NextDouble() * 0.07);
                }
            }
            else
            {
                // For ClearFracture AI, amplify the effect due to being a small startup
                if (stock.Symbol == "CLFR")
                {
                    // Negative event (5-15% decrease for small startup)
                    impactFactor = -0.05M - (decimal)(_random.NextDouble() * 0.10);
                }
                else
                {
                    // Negative event (3-10% decrease)
                    impactFactor = -0.03M - (decimal)(_random.NextDouble() * 0.07);
                }
            }
            
            // Apply impact
            stock.PreviousPrice = stock.CurrentPrice;
            stock.CurrentPrice = Math.Max(1.00M, stock.CurrentPrice * (1 + impactFactor));
            stock.CalculateChanges();
            
            // Update portfolio if needed
            var portfolioItem = _portfolio.FirstOrDefault(p => p.Symbol == stock.Symbol);
            if (portfolioItem != null)
            {
                portfolioItem.CurrentPrice = stock.CurrentPrice;
                portfolioItem.CalculateValues();
            }
            
            // Add news
            AddNews(eventText);
        }

        private void InitializeGame()
        {
            // Clear existing data
            _stocks.Clear();
            _portfolio.Clear();
            _newsFeed.Clear();
            
            // Reset game state
            _currentDay = 1;
            _currentHour = 9;
            _cash = 10000.00M;
            UpdateDisplay();
            
            // Create initial stocks
            CreateInitialStocks();
            
            // Add initial news
            AddNews("Welcome to Stock Trader! Buy low, sell high, and try to build your fortune.");
            AddNews("Market opens with mixed signals. Analysts predict volatility ahead.");
            
            // Start hourly timer
            _hourlyTimer.Start();
            _marketOpen = true;
        }

        private void CreateInitialStocks()
        {
            // Technology sector
            _stocks.Add(new StockModel { Symbol = "APPL", Name = "Apple Technologies", CurrentPrice = 150.00M, PreviousPrice = 148.50M, Volatility = 0.03M, Sector = "Technology" });
            _stocks.Add(new StockModel { Symbol = "MCSF", Name = "Microcore Systems", CurrentPrice = 280.75M, PreviousPrice = 275.25M, Volatility = 0.025M, Sector = "Technology" });
            _stocks.Add(new StockModel { Symbol = "GOOG", Name = "Global Search Inc.", CurrentPrice = 2500.00M, PreviousPrice = 2450.00M, Volatility = 0.035M, Sector = "Technology" });
            _stocks.Add(new StockModel { Symbol = "CSCO", Name = "Connect Networks", CurrentPrice = 45.25M, PreviousPrice = 44.80M, Volatility = 0.02M, Sector = "Technology" });
            _stocks.Add(new StockModel { Symbol = "ORCL", Name = "Data Systems Inc.", CurrentPrice = 65.75M, PreviousPrice = 64.90M, Volatility = 0.025M, Sector = "Technology" });
            
            // AI sector
            _stocks.Add(new StockModel { Symbol = "NVDA", Name = "Nexus Visual AI", CurrentPrice = 420.50M, PreviousPrice = 410.25M, Volatility = 0.045M, Sector = "AI" });
            _stocks.Add(new StockModel { Symbol = "AIBR", Name = "AI Brain Corp", CurrentPrice = 180.25M, PreviousPrice = 175.50M, Volatility = 0.05M, Sector = "AI" });
            _stocks.Add(new StockModel { Symbol = "CLFR", Name = "ClearFracture AI", CurrentPrice = 28.75M, PreviousPrice = 26.50M, Volatility = 0.08M, Sector = "AI/SaaS" });
            _stocks.Add(new StockModel { Symbol = "DPLR", Name = "Deep Learning Research", CurrentPrice = 75.50M, PreviousPrice = 72.25M, Volatility = 0.06M, Sector = "AI" });
            _stocks.Add(new StockModel { Symbol = "CGNX", Name = "Cognitive Nexus", CurrentPrice = 42.25M, PreviousPrice = 40.75M, Volatility = 0.055M, Sector = "AI" });
            
            // Finance sector
            _stocks.Add(new StockModel { Symbol = "BNKR", Name = "First National Bank", CurrentPrice = 85.50M, PreviousPrice = 86.25M, Volatility = 0.02M, Sector = "Finance" });
            _stocks.Add(new StockModel { Symbol = "PYMT", Name = "Digital Payments Corp", CurrentPrice = 210.25M, PreviousPrice = 205.75M, Volatility = 0.04M, Sector = "Finance" });
            _stocks.Add(new StockModel { Symbol = "VISA", Name = "Global Transactions", CurrentPrice = 225.50M, PreviousPrice = 223.75M, Volatility = 0.025M, Sector = "Finance" });
            _stocks.Add(new StockModel { Symbol = "INSR", Name = "Shield Insurance", CurrentPrice = 65.25M, PreviousPrice = 64.50M, Volatility = 0.02M, Sector = "Finance", IsDefensive = true });
            _stocks.Add(new StockModel { Symbol = "FINT", Name = "Future Fintech", CurrentPrice = 45.75M, PreviousPrice = 43.25M, Volatility = 0.05M, Sector = "Finance/Technology" });
            
            // Energy sector
            _stocks.Add(new StockModel { Symbol = "SOLR", Name = "Solar Energy Systems", CurrentPrice = 45.75M, PreviousPrice = 42.50M, Volatility = 0.05M, Sector = "Energy" });
            _stocks.Add(new StockModel { Symbol = "OILX", Name = "Global Oil Exploration", CurrentPrice = 65.25M, PreviousPrice = 67.50M, Volatility = 0.035M, Sector = "Energy", IsCyclical = true });
            _stocks.Add(new StockModel { Symbol = "GREN", Name = "Green Power Inc", CurrentPrice = 78.50M, PreviousPrice = 76.25M, Volatility = 0.04M, Sector = "Energy" });
            _stocks.Add(new StockModel { Symbol = "WIND", Name = "Windforce Generation", CurrentPrice = 35.25M, PreviousPrice = 34.50M, Volatility = 0.045M, Sector = "Energy" });
            _stocks.Add(new StockModel { Symbol = "NUKE", Name = "Advanced Nuclear", CurrentPrice = 55.75M, PreviousPrice = 54.25M, Volatility = 0.03M, Sector = "Energy", IsDefensive = true });
            
            // Healthcare sector
            _stocks.Add(new StockModel { Symbol = "PHRM", Name = "Advanced Pharmaceuticals", CurrentPrice = 120.50M, PreviousPrice = 118.75M, Volatility = 0.03M, Sector = "Healthcare" });
            _stocks.Add(new StockModel { Symbol = "MEDX", Name = "Medical Devices Inc.", CurrentPrice = 75.25M, PreviousPrice = 73.50M, Volatility = 0.025M, Sector = "Healthcare" });
            _stocks.Add(new StockModel { Symbol = "GNOM", Name = "Genomic Therapies", CurrentPrice = 95.75M, PreviousPrice = 92.50M, Volatility = 0.045M, Sector = "Healthcare" });
            _stocks.Add(new StockModel { Symbol = "HOSP", Name = "Healthcare Networks", CurrentPrice = 110.25M, PreviousPrice = 109.50M, Volatility = 0.02M, Sector = "Healthcare", IsDefensive = true });
            _stocks.Add(new StockModel { Symbol = "BIOT", Name = "Biotech Innovations", CurrentPrice = 65.50M, PreviousPrice = 62.75M, Volatility = 0.06M, Sector = "Healthcare" });
            
            // Retail sector
            _stocks.Add(new StockModel { Symbol = "AMZN", Name = "Global Marketplace", CurrentPrice = 3200.00M, PreviousPrice = 3150.00M, Volatility = 0.03M, Sector = "Retail" });
            _stocks.Add(new StockModel { Symbol = "SHOP", Name = "Digital Storefronts", CurrentPrice = 110.25M, PreviousPrice = 108.50M, Volatility = 0.035M, Sector = "Retail" });
            _stocks.Add(new StockModel { Symbol = "GROC", Name = "Fresh Markets", CurrentPrice = 45.75M, PreviousPrice = 45.25M, Volatility = 0.02M, Sector = "Retail", IsDefensive = true });
            _stocks.Add(new StockModel { Symbol = "LUXE", Name = "Luxury Brands Group", CurrentPrice = 85.50M, PreviousPrice = 83.25M, Volatility = 0.04M, Sector = "Retail", IsCyclical = true });
            _stocks.Add(new StockModel { Symbol = "FASH", Name = "Fashion Forward", CurrentPrice = 35.25M, PreviousPrice = 34.50M, Volatility = 0.045M, Sector = "Retail", IsCyclical = true });
            
            // Manufacturing sector
            _stocks.Add(new StockModel { Symbol = "MANF", Name = "Global Manufacturing", CurrentPrice = 75.25M, PreviousPrice = 74.50M, Volatility = 0.025M, Sector = "Manufacturing", IsCyclical = true });
            _stocks.Add(new StockModel { Symbol = "AUTO", Name = "Autonomous Vehicles", CurrentPrice = 95.75M, PreviousPrice = 92.50M, Volatility = 0.045M, Sector = "Manufacturing/Technology" });
            _stocks.Add(new StockModel { Symbol = "AERO", Name = "Aerospace Systems", CurrentPrice = 125.50M, PreviousPrice = 123.75M, Volatility = 0.035M, Sector = "Manufacturing", IsDefensive = true });
            _stocks.Add(new StockModel { Symbol = "TOOL", Name = "Precision Tools", CurrentPrice = 55.25M, PreviousPrice = 54.50M, Volatility = 0.03M, Sector = "Manufacturing" });
            _stocks.Add(new StockModel { Symbol = "ROBO", Name = "Robotics Automation", CurrentPrice = 85.75M, PreviousPrice = 83.25M, Volatility = 0.04M, Sector = "Manufacturing/Technology" });
            
            // Entertainment sector
            _stocks.Add(new StockModel { Symbol = "NFLX", Name = "Stream Media", CurrentPrice = 450.25M, PreviousPrice = 445.50M, Volatility = 0.04M, Sector = "Entertainment" });
            _stocks.Add(new StockModel { Symbol = "GAME", Name = "Interactive Gaming", CurrentPrice = 75.50M, PreviousPrice = 73.25M, Volatility = 0.05M, Sector = "Entertainment" });
            _stocks.Add(new StockModel { Symbol = "FILM", Name = "Global Studios", CurrentPrice = 95.25M, PreviousPrice = 93.50M, Volatility = 0.035M, Sector = "Entertainment" });
            _stocks.Add(new StockModel { Symbol = "MUSC", Name = "Harmony Music", CurrentPrice = 45.75M, PreviousPrice = 44.25M, Volatility = 0.04M, Sector = "Entertainment" });
            _stocks.Add(new StockModel { Symbol = "VRXR", Name = "Virtual Reality Experiences", CurrentPrice = 65.25M, PreviousPrice = 62.50M, Volatility = 0.06M, Sector = "Entertainment/Technology" });
            
            // Real Estate sector
            _stocks.Add(new StockModel { Symbol = "REIT", Name = "Diversified Properties", CurrentPrice = 85.50M, PreviousPrice = 84.75M, Volatility = 0.025M, Sector = "Real Estate", IsDefensive = true, YieldsFocus = true });
            _stocks.Add(new StockModel { Symbol = "COMM", Name = "Commercial Spaces", CurrentPrice = 65.25M, PreviousPrice = 64.50M, Volatility = 0.03M, Sector = "Real Estate", YieldsFocus = true });
            _stocks.Add(new StockModel { Symbol = "HOME", Name = "Residential Developments", CurrentPrice = 45.75M, PreviousPrice = 45.25M, Volatility = 0.035M, Sector = "Real Estate", IsCyclical = true });
            _stocks.Add(new StockModel { Symbol = "STOR", Name = "Storage Solutions", CurrentPrice = 35.25M, PreviousPrice = 34.75M, Volatility = 0.02M, Sector = "Real Estate", IsDefensive = true, YieldsFocus = true });
            _stocks.Add(new StockModel { Symbol = "LAND", Name = "Agricultural Properties", CurrentPrice = 55.50M, PreviousPrice = 54.75M, Volatility = 0.025M, Sector = "Real Estate", IsDefensive = true, YieldsFocus = true });
            
            // Calculate initial changes
            foreach (var stock in _stocks)
            {
                stock.CalculateChanges();
            }
            
            // Initialize displayed stocks
            for (int i = 0; i < Math.Min(10, _stocks.Count); i++)
            {
                _displayedStocks.Add(_stocks[i]);
            }
        }

        private void UpdateDisplay()
        {
            DayCounter.Text = $"{_currentDay} - {_currentHour}:00";
            CashDisplay.Text = _cash.ToString("N2");
            
            // Update market status
            if (_currentHour >= 9 && _currentHour <= 17)
            {
                MarketStatusText.Text = "OPEN";
                MarketStatusText.Foreground = Application.Current.Resources["PositiveChangeBrush"] as SolidColorBrush;
                _marketOpen = true;
            }
            else
            {
                MarketStatusText.Text = "CLOSED";
                MarketStatusText.Foreground = Application.Current.Resources["NegativeChangeBrush"] as SolidColorBrush;
                _marketOpen = false;
            }
        }

        private void AddNews(string news)
        {
            string timestamp = $"[Day {_currentDay} - {_currentHour}:00] ";
            _newsFeed.Insert(0, timestamp + news);
            
            // Limit news feed to last 20 items
            if (_newsFeed.Count > 20)
            {
                _newsFeed.RemoveAt(_newsFeed.Count - 1);
            }
            
            // Update news display
            NewsTextBlock.Text = string.Join("\n\n", _newsFeed);
        }

        private void GenerateRandomNews()
        {
            if (_stocks.Count == 0) return;
            
            // Select a random stock
            var stock = _stocks[_random.Next(_stocks.Count)];
            
            // Generate random news
            string[] newsTemplates = new string[]
            {
                $"Analysts upgrade {stock.Name} ({stock.Symbol}) citing strong growth potential.",
                $"Investors express concern over {stock.Name}'s ({stock.Symbol}) recent performance.",
                $"Quarterly earnings for {stock.Name} ({stock.Symbol}) exceed expectations.",
                $"Rumors of management changes at {stock.Name} ({stock.Symbol}) affect market confidence.",
                $"New product announcement from {stock.Name} ({stock.Symbol}) generates market buzz.",
                $"{stock.Name} ({stock.Symbol}) announces expansion into international markets.",
                $"Regulatory concerns impact {stock.Name} ({stock.Symbol}) stock performance.",
                $"Industry analysts predict strong quarter for {stock.Sector} sector, including {stock.Symbol}.",
                $"Market volatility affects {stock.Sector} stocks like {stock.Symbol}.",
                $"Investors remain cautious about {stock.Sector} sector performance, watching {stock.Symbol} closely."
            };
            
            string news = newsTemplates[_random.Next(newsTemplates.Length)];
            AddNews(news);
        }

        private void SimulateMarketDay()
        {
            // Simulate the entire day at once
            for (int hour = _currentHour; hour <= 17; hour++)
            {
                _currentHour = hour;
                UpdateDisplay();
                SimulateHourlyMarket();
                
                // Random events (10% chance each hour)
                if (_random.NextDouble() < 0.1)
                {
                    GenerateRandomEvent();
                }
            }
            
            // Reset to next day
            _currentHour = 9;
            _currentDay++;
            
            // Update display
            UpdateDisplay();
            
            // Generate market summary news
            GenerateMarketSummary();
        }

        private void GenerateMarketSummary()
        {
            // Count stocks that went up or down
            int upCount = _stocks.Count(s => s.PriceChange > 0);
            int downCount = _stocks.Count(s => s.PriceChange < 0);
            
            // Find biggest gainer and loser
            var biggestGainer = _stocks.OrderByDescending(s => s.PercentChange).FirstOrDefault();
            var biggestLoser = _stocks.OrderBy(s => s.PercentChange).FirstOrDefault();
            
            // Generate summary
            string summary = $"Market Summary: {upCount} stocks up, {downCount} stocks down. ";
            
            if (biggestGainer != null)
            {
                summary += $"Biggest gainer: {biggestGainer.Symbol} (+{biggestGainer.PercentChange:F2}%). ";
            }
            
            if (biggestLoser != null)
            {
                summary += $"Biggest loser: {biggestLoser.Symbol} ({biggestLoser.PercentChange:F2}%).";
            }
            
            AddNews(summary);
        }

        private void BuyStock(StockModel stock, int shares)
        {
            if (stock == null || shares <= 0) return;
            
            // Calculate total cost
            decimal totalCost = stock.CurrentPrice * shares;
            
            // Check if player has enough cash
            if (totalCost > _cash)
            {
                MessageBox.Show($"Not enough cash to buy {shares} shares of {stock.Symbol}.", "Insufficient Funds", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            // Find if stock already exists in portfolio
            var portfolioItem = _portfolio.FirstOrDefault(p => p.Symbol == stock.Symbol);
            
            if (portfolioItem != null)
            {
                // Update existing position
                decimal totalValue = (portfolioItem.Shares * portfolioItem.AverageCost) + totalCost;
                int totalShares = portfolioItem.Shares + shares;
                portfolioItem.AverageCost = totalValue / totalShares;
                portfolioItem.Shares = totalShares;
                portfolioItem.CalculateValues();
            }
            else
            {
                // Add new position
                _portfolio.Add(new PortfolioItem
                {
                    Symbol = stock.Symbol,
                    Name = stock.Name,
                    Shares = shares,
                    AverageCost = stock.CurrentPrice,
                    CurrentPrice = stock.CurrentPrice
                });
            }
            
            // Deduct cash
            _cash -= totalCost;
            UpdateDisplay();
            
            // Add news
            AddNews($"Purchased {shares} shares of {stock.Symbol} at ${stock.CurrentPrice:F2} per share.");
        }

        private void SellStock(PortfolioItem item, int shares)
        {
            if (item == null || shares <= 0) return;
            
            // Check if player has enough shares
            if (shares > item.Shares)
            {
                MessageBox.Show($"You only have {item.Shares} shares of {item.Symbol} to sell.", "Insufficient Shares", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            // Find current stock price
            var stock = _stocks.FirstOrDefault(s => s.Symbol == item.Symbol);
            if (stock == null) return;
            
            // Calculate sale proceeds
            decimal proceeds = stock.CurrentPrice * shares;
            
            // Update portfolio
            if (shares == item.Shares)
            {
                // Remove item if selling all shares
                _portfolio.Remove(item);
            }
            else
            {
                // Update shares count
                item.Shares -= shares;
                item.CalculateValues();
            }
            
            // Add cash
            _cash += proceeds;
            UpdateDisplay();
            
            // Add news
            AddNews($"Sold {shares} shares of {stock.Symbol} at ${stock.CurrentPrice:F2} per share.");
        }

        private void SaveGame()
        {
            try
            {
                var gameState = new GameState
                {
                    Day = _currentDay,
                    Hour = _currentHour,
                    Cash = _cash,
                    Stocks = _stocks.ToList(),
                    Portfolio = _portfolio.ToList(),
                    NewsFeed = _newsFeed
                };
                
                string filePath = Path.Combine(_saveGamePath, "savegame.json");
                string json = JsonSerializer.Serialize(gameState, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(filePath, json);
                
                MessageBox.Show("Game saved successfully.", "Save Game", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving game: {ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadGame()
        {
            try
            {
                string filePath = Path.Combine(_saveGamePath, "savegame.json");
                
                if (!File.Exists(filePath))
                {
                    MessageBox.Show("No saved game found.", "Load Game", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                
                string json = File.ReadAllText(filePath);
                var gameState = JsonSerializer.Deserialize<GameState>(json);
                
                if (gameState != null)
                {
                    // Clear existing data
                    _stocks.Clear();
                    _portfolio.Clear();
                    _newsFeed.Clear();
                    
                    // Restore game state
                    _currentDay = gameState.Day;
                    _currentHour = gameState.Hour;
                    _cash = gameState.Cash;
                    
                    // Restore stocks
                    foreach (var stock in gameState.Stocks)
                    {
                        _stocks.Add(stock);
                    }
                    
                    // Restore portfolio
                    foreach (var item in gameState.Portfolio)
                    {
                        _portfolio.Add(item);
                    }
                    
                    // Restore news feed
                    _newsFeed.AddRange(gameState.NewsFeed);
                    NewsTextBlock.Text = string.Join("\n\n", _newsFeed);
                    
                    // Update display
                    UpdateDisplay();
                    
                    // Start hourly timer
                    _hourlyTimer.Start();
                    
                    MessageBox.Show("Game loaded successfully.", "Load Game", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading game: {ex.Message}", "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #region Event Handlers
        private void NewGameButton_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Start a new game? This will erase your current progress.", "New Game", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                // Stop timer before resetting
                _hourlyTimer.Stop();
                InitializeGame();
            }
        }

        private void SaveGameButton_Click(object sender, RoutedEventArgs e)
        {
            SaveGame();
        }

        private void LoadGameButton_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Load saved game? This will erase your current progress.", "Load Game", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                // Stop timer before loading
                _hourlyTimer.Stop();
                LoadGame();
            }
        }

        private void BuyButton_Click(object sender, RoutedEventArgs e)
        {
            // Check if market is open
            if (!_marketOpen)
            {
                MessageBox.Show("Market is closed. Trading is only available between 9:00 and 17:00.", "Market Closed", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            // Get stock by symbol or selection
            StockModel stockToBuy = null;
            
            // First check if a symbol was entered
            string symbol = SymbolTextBox.Text.Trim().ToUpper();
            if (!string.IsNullOrEmpty(symbol))
            {
                stockToBuy = _stocks.FirstOrDefault(s => s.Symbol == symbol);
                if (stockToBuy == null)
                {
                    MessageBox.Show($"Stock with symbol '{symbol}' not found.", "Invalid Symbol", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            else
            {
                // Fall back to selected stock
                stockToBuy = StockListView.SelectedItem as StockModel;
                if (stockToBuy == null)
                {
                    MessageBox.Show("Please select a stock or enter a valid symbol to buy.", "Buy Stock", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
            }
            
            // Get shares amount
            if (!int.TryParse(SharesTextBox.Text, out int shares) || shares <= 0)
            {
                MessageBox.Show("Please enter a valid number of shares.", "Buy Stock", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            // Execute buy
            BuyStock(stockToBuy, shares);
            
            // Clear the symbol text box after successful purchase
            SymbolTextBox.Text = string.Empty;
        }

        private void SellButton_Click(object sender, RoutedEventArgs e)
        {
            // Check if market is open
            if (!_marketOpen)
            {
                MessageBox.Show("Market is closed. Trading is only available between 9:00 and 17:00.", "Market Closed", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            // Get portfolio item by symbol or selection
            PortfolioItem itemToSell = null;
            
            // First check if a symbol was entered
            string symbol = SymbolTextBox.Text.Trim().ToUpper();
            if (!string.IsNullOrEmpty(symbol))
            {
                itemToSell = _portfolio.FirstOrDefault(p => p.Symbol == symbol);
                if (itemToSell == null)
                {
                    MessageBox.Show($"You don't own any shares of '{symbol}'.", "Invalid Symbol", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            else
            {
                // Fall back to selected portfolio item
                itemToSell = PortfolioListView.SelectedItem as PortfolioItem;
                if (itemToSell == null)
                {
                    MessageBox.Show("Please select a stock from your portfolio or enter a valid symbol to sell.", "Sell Stock", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
            }
            
            // Get shares amount
            if (!int.TryParse(SharesTextBox.Text, out int shares) || shares <= 0)
            {
                MessageBox.Show("Please enter a valid number of shares.", "Sell Stock", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            // Execute sell
            SellStock(itemToSell, shares);
            
            // Clear the symbol text box after successful sale
            SymbolTextBox.Text = string.Empty;
        }

        private void NextDayButton_Click(object sender, RoutedEventArgs e)
        {
            SimulateMarketDay();
        }

        private void PauseResumeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_hourlyTimer.IsEnabled)
            {
                // Pause simulation
                _hourlyTimer.Stop();
                _tickerTimer.Stop();
                PauseResumeButton.Content = "Resume";
                AddNews("Market simulation paused.");
            }
            else
            {
                // Resume simulation
                _hourlyTimer.Start();
                _tickerTimer.Start();
                PauseResumeButton.Content = "Pause";
                AddNews("Market simulation resumed.");
            }
        }
        #endregion
    }

    #region Models
    public class StockModel : INotifyPropertyChanged
    {
        private string _symbol;
        private string _name;
        private decimal _currentPrice;
        private decimal _previousPrice;
        private decimal _priceChange;
        private decimal _percentChange;
        private decimal _volatility;
        private string _sector;
        private bool _isCyclical;
        private bool _isDefensive;
        private bool _yieldsFocus;

        public string Symbol
        {
            get => _symbol;
            set
            {
                _symbol = value;
                OnPropertyChanged(nameof(Symbol));
            }
        }

        public string Name
        {
            get => _name;
            set
            {
                _name = value;
                OnPropertyChanged(nameof(Name));
            }
        }

        public decimal CurrentPrice
        {
            get => _currentPrice;
            set
            {
                _currentPrice = value;
                OnPropertyChanged(nameof(CurrentPrice));
            }
        }

        public decimal PreviousPrice
        {
            get => _previousPrice;
            set
            {
                _previousPrice = value;
                OnPropertyChanged(nameof(PreviousPrice));
            }
        }

        public decimal PriceChange
        {
            get => _priceChange;
            set
            {
                _priceChange = value;
                OnPropertyChanged(nameof(PriceChange));
            }
        }

        public decimal PercentChange
        {
            get => _percentChange;
            set
            {
                _percentChange = value;
                OnPropertyChanged(nameof(PercentChange));
            }
        }

        public decimal Volatility
        {
            get => _volatility;
            set
            {
                _volatility = value;
                OnPropertyChanged(nameof(Volatility));
            }
        }

        public string Sector
        {
            get => _sector;
            set
            {
                _sector = value;
                OnPropertyChanged(nameof(Sector));
            }
        }

        public bool IsCyclical
        {
            get => _isCyclical;
            set
            {
                _isCyclical = value;
                OnPropertyChanged(nameof(IsCyclical));
            }
        }

        public bool IsDefensive
        {
            get => _isDefensive;
            set
            {
                _isDefensive = value;
                OnPropertyChanged(nameof(IsDefensive));
            }
        }

        public bool YieldsFocus
        {
            get => _yieldsFocus;
            set
            {
                _yieldsFocus = value;
                OnPropertyChanged(nameof(YieldsFocus));
            }
        }

        public void CalculateChanges()
        {
            PriceChange = CurrentPrice - PreviousPrice;
            PercentChange = PreviousPrice != 0 ? (PriceChange / PreviousPrice) * 100 : 0;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class PortfolioItem : INotifyPropertyChanged
    {
        private string _symbol;
        private string _name;
        private int _shares;
        private decimal _averageCost;
        private decimal _currentPrice;
        private decimal _currentValue;
        private decimal _totalGainLoss;
        private decimal _percentGainLoss;

        public string Symbol
        {
            get => _symbol;
            set
            {
                _symbol = value;
                OnPropertyChanged(nameof(Symbol));
            }
        }

        public string Name
        {
            get => _name;
            set
            {
                _name = value;
                OnPropertyChanged(nameof(Name));
            }
        }

        public int Shares
        {
            get => _shares;
            set
            {
                _shares = value;
                OnPropertyChanged(nameof(Shares));
                CalculateValues();
            }
        }

        public decimal AverageCost
        {
            get => _averageCost;
            set
            {
                _averageCost = value;
                OnPropertyChanged(nameof(AverageCost));
                CalculateValues();
            }
        }

        public decimal CurrentPrice
        {
            get => _currentPrice;
            set
            {
                _currentPrice = value;
                OnPropertyChanged(nameof(CurrentPrice));
                CalculateValues();
            }
        }

        public decimal CurrentValue
        {
            get => _currentValue;
            set
            {
                _currentValue = value;
                OnPropertyChanged(nameof(CurrentValue));
            }
        }

        public decimal TotalGainLoss
        {
            get => _totalGainLoss;
            set
            {
                _totalGainLoss = value;
                OnPropertyChanged(nameof(TotalGainLoss));
            }
        }

        public decimal PercentGainLoss
        {
            get => _percentGainLoss;
            set
            {
                _percentGainLoss = value;
                OnPropertyChanged(nameof(PercentGainLoss));
            }
        }

        public void CalculateValues()
        {
            CurrentValue = Shares * CurrentPrice;
            TotalGainLoss = CurrentValue - (Shares * AverageCost);
            PercentGainLoss = AverageCost != 0 ? ((CurrentPrice - AverageCost) / AverageCost) * 100 : 0;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class GameState
    {
        public int Day { get; set; }
        public int Hour { get; set; }
        public decimal Cash { get; set; }
        public List<StockModel> Stocks { get; set; }
        public List<PortfolioItem> Portfolio { get; set; }
        public List<string> NewsFeed { get; set; }
    }
    #endregion
} 