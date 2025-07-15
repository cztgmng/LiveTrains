using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Diagnostics;

namespace LiveTrains.Services
{
    // This service is for fetching detailed train information like routes, stations, etc.
    public class TrainDetailsService
    {
        private readonly HttpClient _httpClient;
        private string _mapPagePid = string.Empty;

        public TrainDetailsService(HttpClient httpClient)
        {
            _httpClient = httpClient;
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

        // Get the track for a specific train
        public async Task<List<(double lat, double lng)>> GetTrainTrackAsync(long trainId)
        {
            var coords = new List<(double lat, double lng)>();
            
            if (string.IsNullOrEmpty(_mapPagePid))
            {
                await GetMapPagePid();
                if (string.IsNullOrEmpty(_mapPagePid))
                {
                    Debug.WriteLine("Unable to get PID for ShowTrack request");
                    return coords;
                }
            }

            try
            {
                // Prepare request payload
                var payload = new
                {
                    AM = 0,
                    IS = trainId,
                    PID = _mapPagePid
                };
                
                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                var request = new HttpRequestMessage(HttpMethod.Post, "https://mapa.portalpasazera.pl/pl/Mapa/ShowTrack");
                request.Content = content;
                request.Headers.Accept.ParseAdd("*/*");
                request.Headers.Add("Origin", "https://mapa.portalpasazera.pl");
                request.Headers.Referrer = new Uri("https://mapa.portalpasazera.pl/");
                
                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();
                
                var responseBody = await response.Content.ReadAsStringAsync();
                
                using var doc = JsonDocument.Parse(responseBody);
                
                // The track coordinates are in root.a[0].r.s.rt[0] (array of {s, d})
                try 
                {
                    var rt = doc.RootElement
                        .GetProperty("a")[0]
                        .GetProperty("r")
                        .GetProperty("s")
                        .GetProperty("rt")[0];

                    foreach (var point in rt.EnumerateArray())
                    {
                        var lat = point.GetProperty("s").GetDouble();
                        var lng = point.GetProperty("d").GetDouble();
                        coords.Add((lat, lng));
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error extracting track coordinates: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error fetching train track: {ex.Message}");
            }
            
            return coords;
        }
    }
}
