using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Linq;

namespace LiveTrains.Services
{
    // Enhanced TrainDetailsService with speed calculation capabilities
    public class TrainDetailsService
    {
        private readonly HttpClient _httpClient;
        private readonly LiveTrainTrackingService _trackingService;
        private string _mapPagePid = string.Empty;

        public TrainDetailsService(HttpClient httpClient, LiveTrainTrackingService trackingService)
        {
            _httpClient = httpClient;
            _trackingService = trackingService;
        }

        public async Task Initialize()
        {
            await GetMapPagePid();
        }

        private async Task GetMapPagePid()
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, "https://portalpasazera.pl/MapaPociagow");
                request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:140.0) Gecko/20100101 Firefox/140.0");
                
                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();
                
                var content = await response.Content.ReadAsStringAsync();
                
                // Extract PID value from the page content
                var pidStart = content.IndexOf("var PID = '") + 10;
                if (pidStart > 10) // Found
                {
                    var pidEnd = content.IndexOf("'", pidStart);
                    _mapPagePid = content.Substring(pidStart, pidEnd - pidStart);
                    Debug.WriteLine($"Extracted PID: {_mapPagePid}");
                }
                else
                {
                    Debug.WriteLine("Could not find PID in the map page");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error fetching map page: {ex.Message}");
            }
        }

        // Add method to get comprehensive train details with speed information
        public async Task<TrainDetails> GetTrainDetailsWithSpeedAsync(string trainNumber)
        {
            try
            {
                var trainDetails = await _trackingService.GetTrainDetailsAsync(trainNumber);
                
                if (trainDetails != null && trainDetails.SpeedInfo.IsCalculated)
                {
                    Debug.WriteLine($"Train {trainNumber} speed details:");
                    Debug.WriteLine($"  - Total distance: {trainDetails.SpeedInfo.TotalDistanceKm:F2} km");
                    Debug.WriteLine($"  - Average speed: {trainDetails.SpeedInfo.AverageSpeedKmh:F1} km/h");
                    Debug.WriteLine($"  - Scheduled speed: {trainDetails.SpeedInfo.ScheduledAverageSpeedKmh:F1} km/h");
                    Debug.WriteLine($"  - Actual speed: {trainDetails.SpeedInfo.ActualAverageSpeedKmh:F1} km/h");
                    Debug.WriteLine($"  - Speed category: {trainDetails.SpeedInfo.SpeedCategory}");
                    Debug.WriteLine($"  - Travel time: {trainDetails.SpeedInfo.ScheduledTravelTime:hh\\:mm}");
                }
                
                return trainDetails;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting train details with speed: {ex.Message}");
                return new TrainDetails { Number = trainNumber };
            }
        }

        // Add method to get speed information for multiple trains using position history
        public List<TrainSpeedInfo> GetMultipleTrainSpeedsFromHistory(List<string> trainNumbers)
        {
            var speedInfos = new List<TrainSpeedInfo>();
            
            foreach (var trainNumber in trainNumbers)
            {
                try
                {
                    var speedInfo = _trackingService.GetTrainSpeedFromHistory(trainNumber);
                    speedInfos.Add(speedInfo);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error getting speed for train {trainNumber}: {ex.Message}");
                    speedInfos.Add(new TrainSpeedInfo { IsCalculated = false });
                }
            }
            
            return speedInfos;
        }

        // Add method to categorize trains by speed using position history
        public Dictionary<string, List<string>> CategorizeTrainsBySpeedFromHistory(List<string> trainNumbers)
        {
            var categorizedTrains = new Dictionary<string, List<string>>
            {
                { "Slow", new List<string>() },
                { "Moderate", new List<string>() },
                { "Fast", new List<string>() },
                { "High-Speed", new List<string>() },
                { "Unknown", new List<string>() }
            };
            
            foreach (var trainNumber in trainNumbers)
            {
                try
                {
                    var speedInfo = _trackingService.GetTrainSpeedFromHistory(trainNumber);
                    if (speedInfo.IsCalculated)
                    {
                        categorizedTrains[speedInfo.SpeedCategory].Add(trainNumber);
                    }
                    else
                    {
                        categorizedTrains["Unknown"].Add(trainNumber);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error categorizing train {trainNumber}: {ex.Message}");
                    categorizedTrains["Unknown"].Add(trainNumber);
                }
            }
            
            return categorizedTrains;
        }

        // Add method to get speed statistics for a collection of trains using position history
        public TrainSpeedStatistics GetSpeedStatisticsFromHistory(List<string> trainNumbers)
        {
            var statistics = new TrainSpeedStatistics();
            var validSpeeds = new List<double>();
            
            foreach (var trainNumber in trainNumbers)
            {
                try
                {
                    var speedInfo = _trackingService.GetTrainSpeedFromHistory(trainNumber);
                    if (speedInfo.IsCalculated && speedInfo.AverageSpeedKmh > 0)
                    {
                        validSpeeds.Add(speedInfo.AverageSpeedKmh);
                        statistics.TotalDistance += speedInfo.TotalDistanceKm;
                        statistics.TotalTrains++;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error getting speed statistics for train {trainNumber}: {ex.Message}");
                }
            }
            
            if (validSpeeds.Any())
            {
                statistics.AverageSpeed = validSpeeds.Average();
                statistics.MinSpeed = validSpeeds.Min();
                statistics.MaxSpeed = validSpeeds.Max();
                statistics.MedianSpeed = validSpeeds.OrderBy(s => s).Skip(validSpeeds.Count / 2).FirstOrDefault();
            }
            
            return statistics;
        }
    }

    // Add speed statistics class
    public class TrainSpeedStatistics
    {
        public double AverageSpeed { get; set; } = 0;
        public double MinSpeed { get; set; } = 0;
        public double MaxSpeed { get; set; } = 0;
        public double MedianSpeed { get; set; } = 0;
        public double TotalDistance { get; set; } = 0;
        public int TotalTrains { get; set; } = 0;
    }
}
