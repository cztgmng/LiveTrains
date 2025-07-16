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

        
    }
}
