using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace LiveTrains.Services
{
    public class TrainPosition
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string Number { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Carrier { get; set; } = string.Empty;
        public long TrainId { get; set; } // Added to store the train ID (IS value)
        public bool HasGps { get; set; } // Added to indicate if train has GPS tracking
        public string? GpsTimestamp { get; set; } // Added to store GPS timestamp from 'c' property
        public double AverageSpeedKmh { get; set; } = 0; // Add speed information for movement calculation
        public string SpeedCategory { get; set; } = "Unknown"; // Add speed category for visual indication
        public DateTime LastUpdated { get; set; } = DateTime.Now; // Add timestamp for speed-based movement
    }

    public class FirstNegotiateResponse
    {
        [JsonPropertyName("accessToken")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;
    }

    public class SecondNegotiateResponse
    {
        [JsonPropertyName("connectionId")]
        public string ConnectionId { get; set; } = string.Empty;

        [JsonPropertyName("connectionToken")]
        public string ConnectionToken { get; set; } = string.Empty;
    }

    // Add station information class
    public class TrainStation
    {
        public string Name { get; set; } = string.Empty;
        public string Language { get; set; } = string.Empty;
        public string ScheduledArrival { get; set; } = string.Empty;
        public string ActualArrival { get; set; } = string.Empty;
        public double ArrivalDelay { get; set; } = 0;
        public string ScheduledDeparture { get; set; } = string.Empty;
        public string ActualDeparture { get; set; } = string.Empty;
        public double DepartureDelay { get; set; } = 0;
        public string TransportType { get; set; } = string.Empty;
        public string Platform { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public List<string> Messages { get; set; } = new();
        public List<string> Notices { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public List<string> AdditionalInfo { get; set; } = new();
    }

    // Add this new class to store track coordinates with delay information
    public class TrackCoordinate
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double Delay { get; set; } = 0; // Delay in minutes from "o" property
    }

    // Add speed calculation result class
    public class TrainSpeedInfo
    {
        public double AverageSpeedKmh { get; set; } = 0;
        public double ScheduledAverageSpeedKmh { get; set; } = 0;
        public double ActualAverageSpeedKmh { get; set; } = 0;
        public double TotalDistanceKm { get; set; } = 0;
        public TimeSpan ScheduledTravelTime { get; set; } = TimeSpan.Zero;
        public TimeSpan ActualTravelTime { get; set; } = TimeSpan.Zero;
        public TimeSpan EstimatedTravelTime { get; set; } = TimeSpan.Zero;
        public string SpeedCategory { get; set; } = "Unknown"; // Slow, Moderate, Fast, High-Speed
        public bool IsCalculated { get; set; } = false;
        public string CalculationMethod { get; set; } = "Unknown"; // Route, Stations, Mixed
    }

    // Add this class to return track info including station names and delay info
    public class TrainTrackInfo
    {
        public List<(double lat, double lng)> Coordinates { get; set; } = new();
        public List<TrackCoordinate> CoordinatesWithDelay { get; set; } = new(); // New property for coordinates with delay
        public string StartStationName { get; set; } = string.Empty;
        public string EndStationName { get; set; } = string.Empty;
        public List<TrainStation> Stations { get; set; } = new();
    }

    // Add this class after the TrainTrackInfo class
    public class TrainDetails
    {
        public string Number { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Carrier { get; set; } = string.Empty;
        public string StartStationName { get; set; } = string.Empty;
        public string EndStationName { get; set; } = string.Empty;
        public string RouteName { get; set; } = string.Empty; // a value
        public string RouteNumber { get; set; } = string.Empty; // b value
        public string TrackingUrl { get; set; } = string.Empty; // j value
        public long TrainId { get; set; }
        public List<TrainStation> Stations { get; set; } = new();
        public string StartTime { get; set; } = string.Empty;
        public string EndTime { get; set; } = string.Empty;
        public double StartDelay { get; set; } = 0;
        public double EndDelay { get; set; } = 0;
        public TrainSpeedInfo SpeedInfo { get; set; } = new(); // Add speed information
    }

    public class LiveTrainTrackingService
    {
        private static readonly HttpClient client = new HttpClient();
        private const byte RecordSeparatorByte = 0x1E; // Using byte instead of char for better binary handling
        private CancellationTokenSource? _cts;
        private MemoryStream _messageBuffer = new MemoryStream();
        private string _mapPagePid = string.Empty; // Store PID from the map page
        private Dictionary<string, long> _trainIdMapping = new Dictionary<string, long>(); // Map train numbers to IDs
        private Dictionary<string, TrainPositionHistory> _trainPositionHistory = new Dictionary<string, TrainPositionHistory>(); // Track historical positions
        private DateTime _lastPidFetchTime = DateTime.MinValue;
        private readonly TimeSpan _pidCacheDuration = TimeSpan.FromHours(1); // Cache PID for 1 hour
        
        // GPS filtering properties
        private bool _gpsFilterEnabled = false;
        private List<TrainPosition> _allTrainPositions = new(); // Store all trains
        private List<TrainPosition> _lastFilteredPositions = new(); // Store last filtered result

        public event Action<List<TrainPosition>>? OnTrainPositionsUpdated;
        
        // Property to get/set GPS filter state
        public bool GpsFilterEnabled 
        { 
            get => _gpsFilterEnabled;
            set 
            {
                if (_gpsFilterEnabled != value)
                {
                    _gpsFilterEnabled = value;
                    // Re-filter and notify with current trains
                    var filteredTrains = FilterTrainsByGps(_allTrainPositions);
                    _lastFilteredPositions = filteredTrains;
                    OnTrainPositionsUpdated?.Invoke(filteredTrains);
                }
            }
        }
        
        // Method to filter trains based on GPS availability
        private List<TrainPosition> FilterTrainsByGps(List<TrainPosition> allTrains)
        {
            if (!_gpsFilterEnabled)
            {
                // When GPS filter is disabled, show all trains except GPS-enabled duplicates
                // Group by train number and prefer non-GPS version if both exist
                var trainGroups = allTrains.GroupBy(t => t.Number);
                var result = new List<TrainPosition>();
                
                foreach (var group in trainGroups)
                {
                    var trains = group.ToList();
                    if (trains.Count == 1)
                    {
                        result.Add(trains[0]);
                    }
                    else
                    {
                        // If there are duplicates, prefer the non-GPS version
                        var nonGpsTrain = trains.FirstOrDefault(t => !t.HasGps);
                        if (nonGpsTrain != null)
                        {
                            result.Add(nonGpsTrain);
                            //Debug.WriteLine($"Filtered duplicate train {nonGpsTrain.Number}: chose non-GPS version");
                        }
                        else
                        {
                            // If all have GPS, take the first one
                            result.Add(trains[0]);
                            //Debug.WriteLine($"Filtered duplicate train {trains[0].Number}: all have GPS, chose first");
                        }
                    }
                }
                
                Debug.WriteLine($"GPS filter OFF: {allTrains.Count} total -> {result.Count} filtered (GPS: {result.Count(t => t.HasGps)})");
                return result;
            }
            else
            {
                // When GPS filter is enabled, show GPS-enabled trains + non-GPS trains (no duplicates)
                // Group by train number and prefer GPS version if both exist
                var trainGroups = allTrains.GroupBy(t => t.Number);
                var result = new List<TrainPosition>();
                
                foreach (var group in trainGroups)
                {
                    var trains = group.ToList();
                    if (trains.Count == 1)
                    {
                        result.Add(trains[0]);
                    }
                    else
                    {
                        // If there are duplicates, prefer the GPS version
                        var gpsTrain = trains.FirstOrDefault(t => t.HasGps);
                        if (gpsTrain != null)
                        {
                            result.Add(gpsTrain);
                            //Debug.WriteLine($"Filtered duplicate train {gpsTrain.Number}: chose GPS version");
                        }
                        else
                        {
                            // If none have GPS, take the first one
                            result.Add(trains[0]);
                            //Debug.WriteLine($"Filtered duplicate train {trains[0].Number}: none have GPS, chose first");
                        }
                    }
                }
                
                Debug.WriteLine($"GPS filter ON: {allTrains.Count} total -> {result.Count} filtered (GPS: {result.Count(t => t.HasGps)})");
                return result;
            }
        }

        public async Task StartAsync()
        {
            _cts = new CancellationTokenSource();
            //await GetMapPagePid(); // Fetch the PID at startup
            _ = Task.Run(() => RunAsync(_cts.Token));
        }

        public void Stop()
        {
            _cts?.Cancel();
        }

        private async Task<bool> GetMapPagePid()
        {

            try
            {
                var request = CreateHttpRequest(HttpMethod.Get, "https://mapa.portalpasazera.pl/");
                
                var response = await client.SendAsync(request);
                response.EnsureSuccessStatusCode();
                
                var content = await response.Content.ReadAsStringAsync();
                
                // Extract PID value from the page content
                var pidStart = content.IndexOf("var PID = '") + 10;
                if (pidStart > 10) // Found
                {
                    var end = content.IndexOf('\'', pidStart + 1);
                    _mapPagePid = content.Substring(pidStart + 1, end - pidStart).Replace("\'", "").Replace("\n","").Replace("\r","");
                    Debug.WriteLine($"Extracted PID: {_mapPagePid}");
                    return true;
                }
                else
                {
                    Debug.WriteLine("Could not find PID in the map page");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error fetching map page: {ex.Message}");
                return false;
            }
        }

        private HttpRequestMessage CreateHttpRequest(HttpMethod method, string url, StringContent? content = null)
        {
            var request = new HttpRequestMessage(method, url);
            
            // Common headers for all requests
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:140.0) Gecko/20100101 Firefox/140.0");
            request.Headers.Accept.ParseAdd("*/*");
            
            if (url.Contains("mapa.portalpasazera.pl"))
            {
                request.Headers.Referrer = new Uri("https://mapa.portalpasazera.pl/");
                request.Headers.Add("Origin", "https://mapa.portalpasazera.pl");
            }
            
            // Add content if provided
            if (content != null)
            {
                request.Content = content;
            }
            
            return request;
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            var firstNegotiateResponse = await FirstNegotiateRequest();
            if (firstNegotiateResponse == null) return;

            var secondNegotiateResponse = await SecondNegotiateRequest(firstNegotiateResponse);
            if (secondNegotiateResponse == null) return;

            await ConnectToWebSocket(firstNegotiateResponse, secondNegotiateResponse, cancellationToken);
        }

        private async Task<FirstNegotiateResponse?> FirstNegotiateRequest()
        {
            try
            {
                var requestMessage = CreateHttpRequest(HttpMethod.Post, "https://mapa.portalpasazera.pl/alltrainshub/negotiate?negotiateVersion=1");
                requestMessage.Headers.Host = "mapa.portalpasazera.pl";
                requestMessage.Headers.Add("Cookie", ".AspNetCore.Antiforgery._CeP9oe2XVY=CfDJ8Luexky4l9RKkJn_bkaK49P_ooGCMCjee2l0Wh4x0v6PqhrgwxGBD4UVjePTXjhxwOkOzFqzqYyS8o-N5zov8ywbX_6GXbGO7s93UIuRUzVj6OPm1iAOBTXCorNq4F7XJFbC9PwwOgx523DYfY5wu-A");
                requestMessage.Content = new StringContent("", Encoding.UTF8, "application/json");

                var response = await client.SendAsync(requestMessage);
                response.EnsureSuccessStatusCode();
                var responseBody = await response.Content.ReadAsStringAsync();

                var negotiateResponse = JsonSerializer.Deserialize<FirstNegotiateResponse>(responseBody);
                return negotiateResponse;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in FirstNegotiateRequest: {ex.Message}");
                return null;
            }
        }

        private async Task<SecondNegotiateResponse?> SecondNegotiateRequest(FirstNegotiateResponse firstResponse)
        {
            try
            {
                string secondRequestUrl = firstResponse.Url.Replace("/client/?", "/client/negotiate?");
                var requestMessage = CreateHttpRequest(HttpMethod.Post, secondRequestUrl);
                requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", firstResponse.AccessToken);
                requestMessage.Content = new StringContent("", Encoding.UTF8, "application/json");

                var response = await client.SendAsync(requestMessage);
                response.EnsureSuccessStatusCode();
                var responseBody = await response.Content.ReadAsStringAsync();

                var negotiateResponse = JsonSerializer.Deserialize<SecondNegotiateResponse>(responseBody);
                return negotiateResponse;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in SecondNegotiateRequest: {ex.Message}");
                return null;
            }
        }

        private async Task ConnectToWebSocket(FirstNegotiateResponse firstResponse, SecondNegotiateResponse secondResponse, CancellationToken cancellationToken)
        {
            string wssUrl = firstResponse.Url.Replace("https://", "wss://")
                          + $"&id={secondResponse.ConnectionToken}"
                          + $"&access_token={firstResponse.AccessToken}";

            using (var ws = new ClientWebSocket())
            {
                ws.Options.SetRequestHeader("Origin", "https://mapa.portalpasazera.pl");
                ws.Options.SetRequestHeader("User-Agent", "Mozilla/5.0");

                await ws.ConnectAsync(new Uri(wssUrl), cancellationToken);

                await SendWebSocketMessage(ws, "{\"protocol\":\"json\",\"version\":1}", cancellationToken);
                await ReceiveWebSocketMessage(ws, cancellationToken);

                await SendWebSocketMessage(ws, "{\"arguments\":[\"PL\",6.7,48.35,10.5,55.53,28.3,0,true,\"ATM\",\"\"],\"invocationId\":\"0\",\"target\":\"RegisterParams\",\"type\":1}", cancellationToken);

                while (ws.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    await ReceiveWebSocketMessage(ws, cancellationToken);
                }
                Trace.WriteLine("WebSocket closed.......");
                await RunAsync(cancellationToken);
            }
        }

        private async Task SendWebSocketMessage(ClientWebSocket ws, string message, CancellationToken cancellationToken)
        {
            var bytes = Encoding.UTF8.GetBytes(message);
            var messageBytes = new byte[bytes.Length + 1];
            Array.Copy(bytes, messageBytes, bytes.Length);
            messageBytes[bytes.Length] = RecordSeparatorByte; // Add separator at the end
            
            var segment = new ArraySegment<byte>(messageBytes);
            await ws.SendAsync(segment, WebSocketMessageType.Text, true, cancellationToken);
        }

        private async Task ReceiveWebSocketMessage(ClientWebSocket ws, CancellationToken cancellationToken)
        {
            var buffer = new byte[8192];
            var receiveBuffer = new ArraySegment<byte>(buffer);
            WebSocketReceiveResult result;
            
            try
            {
                do
                {
                    // Continue receiving until we get the full message
                    result = await ws.ReceiveAsync(receiveBuffer, cancellationToken);
                    
                    // Write received bytes directly to memory stream
                    _messageBuffer.Write(buffer, 0, result.Count);
                    
                    Trace.WriteLine($"Received chunk of {result.Count} bytes, EndOfMessage: {result.EndOfMessage}");
                }
                while (!result.EndOfMessage && !cancellationToken.IsCancellationRequested);

                // Only process complete messages when we've reached the end
                if (result.EndOfMessage)
                {
                    Trace.WriteLine($"Complete message received, total buffer length: {_messageBuffer.Length}");
                    ProcessCompleteMessages();
                }
            }
            catch (WebSocketException ex)
            {
                Trace.WriteLine($"WebSocket error: {ex.Message}");
                _messageBuffer.SetLength(0); // Clear buffer on error
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"General error in ReceiveWebSocketMessage: {ex.Message}");
                _messageBuffer.SetLength(0); // Clear buffer on error
            }
        }

        private void ProcessCompleteMessages()
        {
            try
            {
                // Get the full byte array
                var messageBytes = _messageBuffer.ToArray();
                
                // Reset the memory stream for next message
                _messageBuffer.SetLength(0);
                
                if (messageBytes.Length == 0)
                {
                    Debug.WriteLine("ProcessCompleteMessages: Empty message buffer");
                    return;
                }
                
                Debug.WriteLine($"ProcessCompleteMessages: Processing {messageBytes.Length} bytes");
                
                // Find all record separator positions in the byte array
                var separatorPositions = new List<int>();
                for (int i = 0; i < messageBytes.Length; i++)
                {
                    if (messageBytes[i] == RecordSeparatorByte)
                    {
                        separatorPositions.Add(i);
                    }
                }

                Debug.WriteLine($"Found {separatorPositions.Count} record separators");

                // Process each record separator delimited message at the byte level
                int startPos = 0;
                foreach (int separatorPos in separatorPositions)
                {
                    int messageLength = separatorPos - startPos;
                    if (messageLength <= 0)
                    {
                        startPos = separatorPos + 1;
                        continue;
                    }

                    // Extract the message bytes without the separator
                    byte[] messageData = new byte[messageLength];
                    Array.Copy(messageBytes, startPos, messageData, 0, messageLength);
                    
                    // Convert to string and process
                    string messageStr = Encoding.UTF8.GetString(messageData);
                    Debug.WriteLine($"Processing message segment of {messageLength} bytes: {messageStr.Substring(0, Math.Min(100, messageStr.Length))}...");
                    
                    ProcessMessageString(messageStr);

                    startPos = separatorPos + 1;
                }
                
                // Process any remaining data after the last separator
                if (startPos < messageBytes.Length)
                {
                    int remainingLength = messageBytes.Length - startPos;
                    byte[] remainingData = new byte[remainingLength];
                    Array.Copy(messageBytes, startPos, remainingData, 0, remainingLength);
                    
                    string remainingStr = Encoding.UTF8.GetString(remainingData);
                    Debug.WriteLine($"Processing remaining message of {remainingLength} bytes: {remainingStr.Substring(0, Math.Min(100, remainingStr.Length))}...");
                    
                    ProcessMessageString(remainingStr);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in ProcessCompleteMessages: {ex.Message}");
                Debug.WriteLine($"Full exception: {ex}");
            }
        }

        private void ProcessMessageString(string messageStr)
        {
            try
            {
                if (string.IsNullOrEmpty(messageStr.Replace("{", "").Replace("}", "")) || 
                    messageStr.Contains("\"invocationId\":\"0\",\"result\":null"))
                {
                    return;
                }
                
                // Try to parse as a complete JSON first
                if (TryParseAsCompleteJson(messageStr))
                {
                    return;
                }
                
                // If that fails, try to find and extract valid TrainStatus messages
                ExtractTrainStatusMessages(messageStr);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error processing message string: {ex.Message}");
                Debug.WriteLine($"Message preview: {messageStr.Substring(0, Math.Min(200, messageStr.Length))}...");
            }
        }

        private bool TryParseAsCompleteJson(string messageStr)
        {
            try
            {
                Debug.WriteLine($"TryParseAsCompleteJson: Attempting to parse {messageStr.Length} chars as complete JSON");
                
                // Try to parse the entire message as JSON
                using var jsonDoc = JsonDocument.Parse(messageStr);
                var root = jsonDoc.RootElement;
                
                if (root.TryGetProperty("target", out var target) && 
                    target.GetString() == "TrainStatus")
                {
                    Debug.WriteLine("Found TrainStatus in complete JSON");
                    var jsonBytes = Encoding.UTF8.GetBytes(messageStr);
                    var trainPositions = ParseTrainPositionsFromBytes(jsonBytes);
                    if (trainPositions != null && trainPositions.Count > 0)
                    {
                        _allTrainPositions = trainPositions;
                        var filteredTrains = FilterTrainsByGps(trainPositions);
                        _lastFilteredPositions = filteredTrains;
                        OnTrainPositionsUpdated?.Invoke(filteredTrains);
                        
                        Debug.WriteLine($"Successfully parsed complete TrainStatus message with {trainPositions.Count} trains, filtered to {filteredTrains.Count}");
                        return true;
                    }
                    else
                    {
                        Debug.WriteLine("ParseTrainPositionsFromBytes returned empty result for complete JSON");
                    }
                }
                
                // Check for single train position
                if (root.TryGetProperty("t", out _) && 
                    root.TryGetProperty("s", out _) && 
                    root.TryGetProperty("d", out _) && 
                    root.TryGetProperty("n", out _) && 
                    !root.TryGetProperty("target", out _))
                {
                    var singleTrain = ParseSingleTrainPositionFromJsonElement(root);
                    if (singleTrain != null)
                    {
                        UpdateSingleTrainPosition(singleTrain);
                        Debug.WriteLine($"Successfully parsed single train: {singleTrain.Number}");
                        return true;
                    }
                }
                
                Debug.WriteLine("JSON parsed but no TrainStatus or single train found");
                return false;
            }
            catch (JsonException ex)
            {
                Debug.WriteLine($"JSON parsing failed: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in TryParseAsCompleteJson: {ex.Message}");
                return false;
            }
        }

        private void ExtractTrainStatusMessages(string messageStr)
        {
            try
            {
                Debug.WriteLine($"ExtractTrainStatusMessages: Processing message of {messageStr.Length} chars");
                
                // Look for TrainStatus messages in the string
                var trainStatusStart = messageStr.IndexOf("\"target\":\"TrainStatus\"");
                if (trainStatusStart == -1)
                {
                    Debug.WriteLine("No TrainStatus target found in message");
                    return;
                }
                
                Debug.WriteLine($"Found TrainStatus at position {trainStatusStart}");
                
                // Find the start of the JSON object containing TrainStatus
                var jsonStart = messageStr.LastIndexOf('{', trainStatusStart);
                if (jsonStart == -1)
                {
                    Debug.WriteLine("No JSON start found before TrainStatus");
                    return;
                }
                
                Debug.WriteLine($"JSON starts at position {jsonStart}");
                
                // Extract from the JSON start to find a complete JSON object
                var jsonCandidate = messageStr.Substring(jsonStart);
                
                // Try to find the end of this JSON object using better logic
                var braceCount = 0;
                var inString = false;
                var escaped = false;
                var jsonEnd = -1;
                
                for (int i = 0; i < jsonCandidate.Length; i++)
                {
                    char c = jsonCandidate[i];
                    
                    if (escaped)
                    {
                        escaped = false;
                        continue;
                    }
                    
                    if (c == '\\' && inString)
                    {
                        escaped = true;
                        continue;
                    }
                    
                    if (c == '"')
                    {
                        inString = !inString;
                        continue;
                    }
                    
                    if (inString)
                    {
                        continue;
                    }
                    
                    if (c == '{')
                    {
                        braceCount++;
                    }
                    else if (c == '}')
                    {
                        braceCount--;
                        if (braceCount == 0)
                        {
                            jsonEnd = i;
                            break;
                        }
                    }
                }
                
                if (jsonEnd > 0)
                {
                    var completeJson = jsonCandidate.Substring(0, jsonEnd + 1);
                    Debug.WriteLine($"Extracted complete JSON of {completeJson.Length} chars");
                    
                    try
                    {
                        var jsonBytes = Encoding.UTF8.GetBytes(completeJson);
                        var trainPositions = ParseTrainPositionsFromBytes(jsonBytes);
                        if (trainPositions != null && trainPositions.Count > 0)
                        {
                            _allTrainPositions = trainPositions;
                            var filteredTrains = FilterTrainsByGps(trainPositions);
                            _lastFilteredPositions = filteredTrains;
                            OnTrainPositionsUpdated?.Invoke(filteredTrains);
                            
                            Debug.WriteLine($"Successfully extracted and parsed TrainStatus with {trainPositions.Count} trains, filtered to {filteredTrains.Count}");
                        }
                        else
                        {
                            Debug.WriteLine("ParseTrainPositionsFromBytes returned empty or null result");
                        }
                    }
                    catch (JsonException ex)
                    {
                        Debug.WriteLine($"Error parsing extracted JSON: {ex.Message}");
                        Debug.WriteLine($"JSON preview: {completeJson.Substring(0, Math.Min(200, completeJson.Length))}...");
                        
                        // Try to clean up the JSON by removing any trailing garbage
                        var cleanedJson = CleanJsonString(completeJson);
                        if (cleanedJson != completeJson)
                        {
                            Debug.WriteLine("Attempting to parse cleaned JSON");
                            try
                            {
                                var cleanedBytes = Encoding.UTF8.GetBytes(cleanedJson);
                                var trainPositions = ParseTrainPositionsFromBytes(cleanedBytes);
                                if (trainPositions != null && trainPositions.Count > 0)
                                {
                                    _allTrainPositions = trainPositions;
                                    var filteredTrains = FilterTrainsByGps(trainPositions);
                                    _lastFilteredPositions = filteredTrains;
                                    OnTrainPositionsUpdated?.Invoke(filteredTrains);
                                    
                                    Debug.WriteLine($"Successfully parsed cleaned JSON with {trainPositions.Count} trains");
                                }
                            }
                            catch (Exception cleanEx)
                            {
                                Debug.WriteLine($"Error parsing cleaned JSON: {cleanEx.Message}");
                            }
                        }
                    }
                }
                else
                {
                    Debug.WriteLine("Could not find complete JSON object end");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error extracting TrainStatus messages: {ex.Message}");
            }
        }

        private string CleanJsonString(string json)
        {
            try
            {
                // Find the last valid closing brace
                var lastValidBrace = json.LastIndexOf('}');
                if (lastValidBrace > 0 && lastValidBrace < json.Length - 1)
                {
                    // Check if there's garbage after the last brace
                    var afterBrace = json.Substring(lastValidBrace + 1);
                    if (!string.IsNullOrWhiteSpace(afterBrace))
                    {
                        Debug.WriteLine($"Found garbage after JSON: '{afterBrace}'");
                        return json.Substring(0, lastValidBrace + 1);
                    }
                }
                
                return json;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error cleaning JSON: {ex.Message}");
                return json;
            }
        }

        private TrainPosition? ParseSingleTrainPositionFromJsonElement(JsonElement root)
        {
            try
            {
                // Check that all required properties exist
                if (!root.TryGetProperty("s", out var latitude) ||
                    !root.TryGetProperty("d", out var longitude) ||
                    !root.TryGetProperty("n", out var number) ||
                    !root.TryGetProperty("p", out var type))
                {
                    return null;
                }

                // Get carrier code
                string carrier = "";
                if (root.TryGetProperty("pr", out var carrierProp))
                {
                    carrier = carrierProp.GetString() ?? "";
                }

                // Extract train ID
                long trainId = 0;
                if (root.TryGetProperty("t", out var trainIdProp))
                {
                    trainId = trainIdProp.GetInt64();
                }

                // Extract GPS information
                bool hasGps = false;
                string? gpsTimestamp = null;
                if (root.TryGetProperty("c", out var gpsTimestampProp) && 
                    gpsTimestampProp.ValueKind == JsonValueKind.String)
                {
                    gpsTimestamp = gpsTimestampProp.GetString();
                    hasGps = !string.IsNullOrEmpty(gpsTimestamp);
                }

                var trainNumberStr = number.GetString() ?? "";
                var lat = latitude.GetDouble();
                var lng = longitude.GetDouble();
                var currentTime = DateTime.Now;

                // Store train ID for later use
                if (!string.IsNullOrEmpty(trainNumberStr) && trainId > 0)
                {
                    _trainIdMapping[trainNumberStr] = trainId;
                }

                // Update position history and calculate speed
                if (!_trainPositionHistory.ContainsKey(trainNumberStr))
                {
                    _trainPositionHistory[trainNumberStr] = new TrainPositionHistory();
                }

                var history = _trainPositionHistory[trainNumberStr];
                history.AddPosition(lat, lng, currentTime);

                return new TrainPosition
                {
                    Latitude = lat,
                    Longitude = lng,
                    Number = trainNumberStr,
                    Type = type.GetString() ?? "",
                    Carrier = carrier,
                    TrainId = trainId,
                    HasGps = hasGps,
                    GpsTimestamp = gpsTimestamp,
                    AverageSpeedKmh = history.CurrentSpeedKmh,
                    SpeedCategory = history.SpeedCategory,
                    LastUpdated = currentTime
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error parsing single train position from JsonElement: {ex.Message}");
                return null;
            }
        }

        // Add method to parse single train position - simplified version
        private TrainPosition? ParseSingleTrainPosition(string jsonString)
        {
            try
            {
                using var jsonDoc = JsonDocument.Parse(jsonString);
                return ParseSingleTrainPositionFromJsonElement(jsonDoc.RootElement);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error parsing single train position: {ex.Message}");
                return null;
            }
        }

        // Add method to update single train position
        private void UpdateSingleTrainPosition(TrainPosition trainPosition)
        {
            // Find and update the specific train in the all trains list
            var existingTrainIndex = _allTrainPositions.FindIndex(t => t.Number == trainPosition.Number);
            
            if (existingTrainIndex >= 0)
            {
                // Update existing train, but preserve speed information if it's calculated
                var existingTrain = _allTrainPositions[existingTrainIndex];
                
                // Update the basic position data
                existingTrain.Latitude = trainPosition.Latitude;
                existingTrain.Longitude = trainPosition.Longitude;
                existingTrain.HasGps = trainPosition.HasGps;
                existingTrain.GpsTimestamp = trainPosition.GpsTimestamp;
                existingTrain.LastUpdated = trainPosition.LastUpdated;
                
                // Update speed information if available
                if (trainPosition.AverageSpeedKmh > 0)
                {
                    existingTrain.AverageSpeedKmh = trainPosition.AverageSpeedKmh;
                    existingTrain.SpeedCategory = trainPosition.SpeedCategory;
                }
                
                Debug.WriteLine($"Updated train {trainPosition.Number}: Position({trainPosition.Latitude:F6}, {trainPosition.Longitude:F6}), Speed: {existingTrain.AverageSpeedKmh:F1} km/h ({existingTrain.SpeedCategory})");
            }
            else
            {
                // Add new train if it doesn't exist
                _allTrainPositions.Add(trainPosition);
                Debug.WriteLine($"Added new train {trainPosition.Number}: Position({trainPosition.Latitude:F6}, {trainPosition.Longitude:F6}), Speed: {trainPosition.AverageSpeedKmh:F1} km/h ({trainPosition.SpeedCategory})");
            }
            
            // Apply filtering and notify
            var filteredTrains = FilterTrainsByGps(_allTrainPositions);
            _lastFilteredPositions = filteredTrains;
            OnTrainPositionsUpdated?.Invoke(filteredTrains);
        }

        // Add method to categorize speed
        private string CategorizeSpeed(double speedKmh)
        {
            return speedKmh switch
            {
                < 50 => "Slow",
                < 100 => "Moderate",
                < 160 => "Fast",
                >= 160 => "High-Speed",
                _ => "Unknown"
            };
        }

        private List<TrainPosition> ParseTrainPositionsFromBytes(byte[] messageBytes)
        {
            var positions = new List<TrainPosition>();
            try
            {
                var options = new JsonDocumentOptions
                {
                    MaxDepth = 64,
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                };

                using var jsonDoc = JsonDocument.Parse(messageBytes, options);
                var root = jsonDoc.RootElement;

                // Validate the JSON structure before proceeding
                if (!root.TryGetProperty("arguments", out var args) || 
                    args.ValueKind != JsonValueKind.Array || 
                    args.GetArrayLength() <= 1)
                {
                    Debug.WriteLine("Invalid JSON structure: missing arguments or insufficient array length");
                    return positions;
                }

                var trains = args[1];
                var currentTime = DateTime.Now;

                Debug.WriteLine($"Processing {trains.GetArrayLength()} trains from JSON");

                foreach (var train in trains.EnumerateArray())
                {
                    try
                    {
                        // Check that all required properties exist
                        if (!train.TryGetProperty("s", out var latitude) ||
                            !train.TryGetProperty("d", out var longitude) ||
                            !train.TryGetProperty("n", out var number) ||
                            !train.TryGetProperty("p", out var type))
                        {
                            continue; // Skip this train if missing required properties
                        }
                        
                        // Get carrier code - correct property is "pr" not "p"
                        string carrier = "";
                        if (train.TryGetProperty("pr", out var carrierProp))
                        {
                            carrier = carrierProp.GetString() ?? "";
                        }

                        // Extract train ID (IS value) - t property
                        long trainId = 0;
                        if (train.TryGetProperty("t", out var trainIdProp))
                        {
                            trainId = trainIdProp.GetInt64();
                        }
                        
                        // Extract GPS information from 'c' property
                        bool hasGps = false;
                        string? gpsTimestamp = null;
                        if (train.TryGetProperty("c", out var gpsTimestampProp) && 
                            gpsTimestampProp.ValueKind == JsonValueKind.String)
                        {
                            gpsTimestamp = gpsTimestampProp.GetString();
                            hasGps = !string.IsNullOrEmpty(gpsTimestamp);
                        }
                        
                        var trainNumberStr = number.GetString() ?? "";
                        var lat = latitude.GetDouble();
                        var lng = longitude.GetDouble();
                        
                        // Store train ID for later use with the ShowTrack API
                        if (!string.IsNullOrEmpty(trainNumberStr) && trainId > 0)
                        {
                            _trainIdMapping[trainNumberStr] = trainId;
                        }

                        // Update position history and calculate speed
                        if (!_trainPositionHistory.ContainsKey(trainNumberStr))
                        {
                            _trainPositionHistory[trainNumberStr] = new TrainPositionHistory();
                        }

                        var history = _trainPositionHistory[trainNumberStr];
                        history.AddPosition(lat, lng, currentTime);
                        
                        positions.Add(new TrainPosition
                        {
                            Latitude = lat,
                            Longitude = lng,
                            Number = trainNumberStr,
                            Type = type.GetString() ?? "",
                            Carrier = carrier,
                            TrainId = trainId,
                            HasGps = hasGps,
                            GpsTimestamp = gpsTimestamp,
                            AverageSpeedKmh = history.CurrentSpeedKmh,
                            SpeedCategory = history.SpeedCategory,
                            LastUpdated = currentTime
                        });
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error processing train: {ex.Message}");
                        continue;
                    }
                }
                
                Debug.WriteLine($"Successfully parsed {positions.Count} train positions");
                return positions;
            }
            catch (JsonException ex)
            {
                var jsonPreview = Encoding.UTF8.GetString(messageBytes);
                Debug.WriteLine($"JSON parsing error: {ex.Message}");
                Debug.WriteLine($"JSON preview (first 500 chars): {jsonPreview.Substring(0, Math.Min(500, jsonPreview.Length))}...");
                Debug.WriteLine($"JSON preview (last 200 chars): ...{jsonPreview.Substring(Math.Max(0, jsonPreview.Length - 200))}");
                return new List<TrainPosition>();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error parsing train positions: {ex}");
                return new List<TrainPosition>();
            }
        }

        // Add class to track train position history for speed calculation
        public class TrainPositionHistory
        {
            public List<PositionPoint> Positions { get; set; } = new();
            public double CurrentSpeedKmh { get; set; } = 0;
            public string SpeedCategory { get; set; } = "Unknown";
            public DateTime LastCalculated { get; set; } = DateTime.MinValue;
            
            public class PositionPoint
            {
                public double Latitude { get; set; }
                public double Longitude { get; set; }
                public DateTime Timestamp { get; set; }
                public double? CalculatedSpeedKmh { get; set; }
            }
            
            // Calculate speed based on recent positions
            public void CalculateSpeed()
            {
                if (Positions.Count < 2)
                {
                    CurrentSpeedKmh = 0;
                    SpeedCategory = "Unknown";
                    return;
                }
                
                // Clean old positions (keep only last 10 minutes)
                var cutoffTime = DateTime.Now.AddMinutes(-10);
                Positions = Positions.Where(p => p.Timestamp > cutoffTime).ToList();
                
                if (Positions.Count < 2)
                {
                    CurrentSpeedKmh = 0;
                    SpeedCategory = "Unknown";
                    return;
                }
                
                // Calculate speeds for each segment
                var speedSamples = new List<double>();
                for (int i = 1; i < Positions.Count; i++)
                {
                    var prevPos = Positions[i - 1];
                    var currPos = Positions[i];
                    
                    var distance = CalculateDistance(prevPos.Latitude, prevPos.Longitude, currPos.Latitude, currPos.Longitude);
                    var timeSpan = currPos.Timestamp - prevPos.Timestamp;
                    
                    if (timeSpan.TotalSeconds > 0 && distance > 0.001) // Minimum 1 meter movement
                    {
                        var speedKmh = (distance / timeSpan.TotalHours);
                        
                        // Filter out unrealistic speeds (likely GPS errors)
                        if (speedKmh <= 300) // Max 300 km/h for trains
                        {
                            speedSamples.Add(speedKmh);
                            currPos.CalculatedSpeedKmh = speedKmh;
                        }
                    }
                }
                
                if (speedSamples.Count > 0)
                {
                    // Use weighted average with more recent speeds having higher weight
                    var totalWeight = 0.0;
                    var weightedSum = 0.0;
                    
                    for (int i = 0; i < speedSamples.Count; i++)
                    {
                        var weight = Math.Pow(2, i); // Exponential weight for recent samples
                        totalWeight += weight;
                        weightedSum += speedSamples[i] * weight;
                    }
                    
                    CurrentSpeedKmh = weightedSum / totalWeight;
                    SpeedCategory = CategorizeSpeed(CurrentSpeedKmh);
                    LastCalculated = DateTime.Now;
                }
            }
            
            // Add position and calculate speed
            public void AddPosition(double latitude, double longitude, DateTime timestamp)
            {
                // Don't add duplicate positions
                if (Positions.Count > 0)
                {
                    var lastPos = Positions.Last();
                    var distance = CalculateDistance(lastPos.Latitude, lastPos.Longitude, latitude, longitude);
                    var timeDiff = timestamp - lastPos.Timestamp;
                    
                    // Only add if moved at least 10 meters or 30 seconds passed
                    if (distance < 0.01 && timeDiff.TotalSeconds < 30)
                    {
                        return;
                    }
                }
                
                Positions.Add(new PositionPoint
                {
                    Latitude = latitude,
                    Longitude = longitude,
                    Timestamp = timestamp
                });
                
                // Keep only last 20 positions to avoid memory issues
                if (Positions.Count > 20)
                {
                    Positions.RemoveAt(0);
                }
                
                // Calculate speed
                CalculateSpeed();
            }
            
            private static double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
            {
                const double EarthRadiusKm = 6371.0;
                
                var dLat = ToRadians(lat2 - lat1);
                var dLon = ToRadians(lon2 - lon1);
                
                var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                        Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                        Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
                
                var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
                
                return EarthRadiusKm * c;
            }
            
            private static double ToRadians(double degrees)
            {
                return degrees * Math.PI / 180.0;
            }
            
            // Add method to categorize speed
            private string CategorizeSpeed(double speedKmh)
            {
                return speedKmh switch
                {
                    < 50 => "Slow",
                    < 100 => "Moderate",
                    < 160 => "Fast",
                    >= 160 => "High-Speed",
                    _ => "Unknown"
                };
            }
        }

        // Modify the GetTrainTrackAsync method to return station names too
        public async Task<TrainTrackInfo> GetTrainTrackAsync(string trainNumber)
        {
            var trackInfo = new TrainTrackInfo();
            var coords = new List<(double lat, double lng)>();
            var coordsWithDelay = new List<TrackCoordinate>(); // New list for coordinates with delay
            
            // Ensure we have a valid PID
            if (string.IsNullOrEmpty(_mapPagePid))
            {
                bool success = await GetMapPagePid();
                if (!success || string.IsNullOrEmpty(_mapPagePid))
                {
                    Debug.WriteLine("Failed to get PID for ShowTrack request");
                    trackInfo.Coordinates = coords;
                    return trackInfo;
                }
            }

            try
            {
                // Look up train ID from mapping
                if (!_trainIdMapping.TryGetValue(trainNumber, out long trainId))
                {
                    Debug.WriteLine($"Train ID not found for number: {trainNumber}");
                    trackInfo.Coordinates = coords;
                    return trackInfo;
                }
                
                // Prepare request payload with the correct values
                var payload = new
                {
                    AM = 0,
                    IS = trainId,
                    PID = _mapPagePid
                };
                
                var json = JsonSerializer.Serialize(payload);
                Debug.WriteLine($"ShowTrack request payload: {json}");
                
                // Use the common HTTP request creation method
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var request = CreateHttpRequest(HttpMethod.Post, "https://mapa.portalpasazera.pl/pl/Mapa/ShowTrack", content);

                var response = await client.SendAsync(request);
                response.EnsureSuccessStatusCode();
                var responseBody = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"ShowTrack response received, length: {responseBody.Length}");

                using var doc = JsonDocument.Parse(responseBody);
                
                try 
                {
                    // Extract station names from a[0].t.d and a[0].t.f
                    if (doc.RootElement.TryGetProperty("a", out var aElement) && 
                        aElement.GetArrayLength() > 0 &&
                        aElement[0].TryGetProperty("t", out var tElement))
                    {
                        if (tElement.TryGetProperty("d", out var dElement))
                        {
                            var startStationFromT = dElement.GetString();
                            if (!string.IsNullOrEmpty(startStationFromT))
                            {
                                trackInfo.StartStationName = startStationFromT;
                            }
                        }
                        if (tElement.TryGetProperty("f", out var fElement))
                        {
                            var endStationFromT = fElement.GetString();
                            if (!string.IsNullOrEmpty(endStationFromT))
                            {
                                trackInfo.EndStationName = endStationFromT;
                            }
                        }
                    }

                    // Extract station information from a[0].s array
                    if (doc.RootElement.TryGetProperty("a", out var aElement2) && 
                        aElement2.GetArrayLength() > 0 &&
                        aElement2[0].TryGetProperty("s", out var sElement))
                    {
                        foreach (var stationJson in sElement.EnumerateArray())
                        {
                            var station = new TrainStation();
                            
                            if (stationJson.TryGetProperty("a", out var nameElement))
                                station.Name = nameElement.GetString() ?? "";
                            
                            if (stationJson.TryGetProperty("c", out var schedArrElement))
                                station.ScheduledArrival = schedArrElement.GetString() ?? "";
                        
                            if (stationJson.TryGetProperty("d", out var actualArrElement))
                                station.ActualArrival = actualArrElement.GetString() ?? "";
                        
                            if (stationJson.TryGetProperty("e", out var arrDelayElement))
                                station.ArrivalDelay = arrDelayElement.GetDouble();
                        
                            if (stationJson.TryGetProperty("f", out var schedDepElement))
                                station.ScheduledDeparture = schedDepElement.GetString() ?? "";
                        
                            if (stationJson.TryGetProperty("g", out var actualDepElement))
                                station.ActualDeparture = actualDepElement.GetString() ?? "";
                        
                            if (stationJson.TryGetProperty("h", out var depDelayElement))
                                station.DepartureDelay = depDelayElement.GetDouble();
                        
                            if (stationJson.TryGetProperty("i", out var transportElement))
                                station.TransportType = transportElement.GetString() ?? "";
                        
                            if (stationJson.TryGetProperty("j", out var platformElement))
                                station.Platform = platformElement.GetString() ?? "";
                        
                            if (stationJson.TryGetProperty("k", out var latElement))
                                station.Latitude = latElement.GetDouble();
                        
                            if (stationJson.TryGetProperty("l", out var lngElement))
                                station.Longitude = lngElement.GetDouble();
                        
                            // Extract arrays of additional information
                            if (stationJson.TryGetProperty("m", out var messagesElement))
                            {
                                foreach (var msg in messagesElement.EnumerateArray())
                                {
                                    station.Messages.Add(msg.GetString() ?? "");
                                }
                            }
                        
                            if (stationJson.TryGetProperty("n", out var noticesElement))
                            {
                                foreach (var notice in noticesElement.EnumerateArray())
                                {
                                    station.Notices.Add(notice.GetString() ?? "");
                                }
                            }
                        
                            if (stationJson.TryGetProperty("o", out var warningsElement))
                            {
                                foreach (var warning in warningsElement.EnumerateArray())
                                {
                                    station.Warnings.Add(warning.GetString() ?? "");
                                }
                            }
                        
                            if (stationJson.TryGetProperty("p", out var additionalElement))
                            {
                                foreach (var info in additionalElement.EnumerateArray())
                                {
                                    station.AdditionalInfo.Add(info.GetString() ?? "");
                                }
                            }
                        
                            trackInfo.Stations.Add(station);
                        }
                        Debug.WriteLine($"Extracted {trackInfo.Stations.Count} stations");
                        
                        // Fallback: If start station name is empty, use first station from stations list
                        if (string.IsNullOrEmpty(trackInfo.StartStationName) && trackInfo.Stations.Count > 0)
                        {
                            trackInfo.StartStationName = trackInfo.Stations.First().Name;
                            Debug.WriteLine($"Using first station as start station: {trackInfo.StartStationName}");
                        }
                        
                        // Fallback: If end station name is empty, use last station from stations list
                        if (string.IsNullOrEmpty(trackInfo.EndStationName) && trackInfo.Stations.Count > 0)
                        {
                            trackInfo.EndStationName = trackInfo.Stations.Last().Name;
                            Debug.WriteLine($"Using last station as end station: {trackInfo.EndStationName}");
                        }
                    }
                    
                    // Navigate through the JSON structure to find the track coordinates
                    // The track coordinates can be in root.a[0].r.s.rt, ct, rct, or dt (array of {s, d})
                    JsonElement? pointsArray = null;
                    if (doc.RootElement.TryGetProperty("a", out var a) && 
                        a.GetArrayLength() > 0 &&
                        a[0].TryGetProperty("r", out var r) &&
                        r.TryGetProperty("s", out var sElementCoords))
                    {
                        string[] possibleKeys = { "rt", "ct", "rct", "dt" };
                        foreach (var key in possibleKeys)
                        {
                            if (sElementCoords.TryGetProperty(key, out var arr) && arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() > 0)
                            {
                                pointsArray = arr;
                                break;
                            }
                        }
                    }

                    if (pointsArray != null)
                    {
                        int coordsWithDelayCount = 0;
                        foreach (var point in pointsArray.Value.EnumerateArray())
                        {
                            // With this block to handle array of coordinates:
                            if (point.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var coord in point.EnumerateArray())
                                {
                                    var lat = coord.GetProperty("s").GetDouble();
                                    var lng = coord.GetProperty("d").GetDouble();
                                    coords.Add((lat, lng));
                                    
                                    // Extract delay information from "o" property
                                    double delay = 0;
                                    if (coord.TryGetProperty("o", out var delayProp))
                                    {
                                        delay = delayProp.GetDouble();
                                        if (delay > 0) coordsWithDelayCount++;
                                    }
                                    
                                    coordsWithDelay.Add(new TrackCoordinate 
                                    { 
                                        Latitude = lat, 
                                        Longitude = lng, 
                                        Delay = delay 
                                    });
                                }
                            }
                            else
                            {
                                var lat = point.GetProperty("s").GetDouble();
                                var lng = point.GetProperty("d").GetDouble();
                                coords.Add((lat, lng));
                                
                                // Extract delay information from "o" property
                                double delay = 0;
                                if (point.TryGetProperty("o", out var delayProp))
                                {
                                    delay = delayProp.GetDouble();
                                    if (delay > 0) coordsWithDelayCount++;
                                }
                                
                                coordsWithDelay.Add(new TrackCoordinate 
                                { 
                                    Latitude = lat, 
                                    Longitude = lng, 
                                    Delay = delay 
                                });
                            }
                        }
                        Debug.WriteLine($"Extracted {coords.Count} track coordinates, {coordsWithDelayCount} with delays > 0");
                    }
                    else
                    {
                        Debug.WriteLine("No track coordinates found in any of rt, ct, rct, dt");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error extracting track coordinates: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error fetching track: {ex.Message}");
            }
            
            trackInfo.Coordinates = coords;
            trackInfo.CoordinatesWithDelay = coordsWithDelay; // Set the new property
            return trackInfo;
        }

        // Add method to get full train details
        public async Task<TrainDetails> GetTrainDetailsAsync(string trainNumber)
        {
            var trackInfo = await GetTrainTrackAsync(trainNumber);
            
            // Find the train position data from our cache
            var trainPosition = _trainIdMapping.TryGetValue(trainNumber, out var trainId) ? 
                new TrainPosition { Number = trainNumber, TrainId = trainId } : 
                null;
            
            var details = new TrainDetails
            {
                Number = trainNumber,
                StartStationName = trackInfo.StartStationName,
                EndStationName = trackInfo.EndStationName,
                TrainId = trainId,
                Stations = trackInfo.Stations
            };

            // Extract timing information from stations
            if (trackInfo.Stations.Count > 0)
            {
                var firstStation = trackInfo.Stations.First();
                var lastStation = trackInfo.Stations.Last();
                
                details.StartTime = !string.IsNullOrEmpty(firstStation.ScheduledDeparture) ? 
                    firstStation.ScheduledDeparture : firstStation.ScheduledArrival;
                details.StartDelay = firstStation.DepartureDelay != 0 ? 
                    firstStation.DepartureDelay : firstStation.ArrivalDelay;
                
                details.EndTime = !string.IsNullOrEmpty(lastStation.ScheduledArrival) ? 
                    lastStation.ScheduledArrival : lastStation.ScheduledDeparture;
                details.EndDelay = lastStation.ArrivalDelay != 0 ? 
                    lastStation.ArrivalDelay : lastStation.DepartureDelay;
            }
            
            try
            {
                // Ensure we have a valid PID
                if (string.IsNullOrEmpty(_mapPagePid))
                {
                    await GetMapPagePid();
                    if (string.IsNullOrEmpty(_mapPagePid))
                    {
                        Debug.WriteLine("Failed to get PID for train details");
                        return details;
                    }
                }
                
                // Same API call as GetTrainTrackAsync, but extract different data
                var payload = new
                {
                    AM = 0,
                    IS = trainId,
                    PID = _mapPagePid
                };
                
                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var request = CreateHttpRequest(HttpMethod.Post, "https://mapa.portalpasazera.pl/pl/Mapa/ShowTrack", content);
                
                var response = await client.SendAsync(request);
                response.EnsureSuccessStatusCode();
                var responseBody = await response.Content.ReadAsStringAsync();
                
                using var doc = JsonDocument.Parse(responseBody);
                
                // Extract additional train details from a[0].t
                if (doc.RootElement.TryGetProperty("a", out var aElement) &&
                    aElement.GetArrayLength() > 0 &&
                    aElement[0].TryGetProperty("t", out var tElement))
                {
                    // Route name (a)
                    if (tElement.TryGetProperty("a", out var aValue))
                        details.RouteName = aValue.GetString() ?? string.Empty;
                        
                    // Route number (b)
                    if (tElement.TryGetProperty("b", out var bValue))
                        details.RouteNumber = bValue.GetString() ?? string.Empty;
                        
                    // Carrier (c)
                    if (tElement.TryGetProperty("c", out var cValue))
                        details.Carrier = cValue.GetString() ?? string.Empty;
                        
                    // Start station (d) - check if not empty, otherwise keep the one from trackInfo (which has fallback logic)
                    if (tElement.TryGetProperty("d", out var dValue))
                    {
                        var startStationFromT = dValue.GetString();
                        if (!string.IsNullOrEmpty(startStationFromT))
                        {
                            details.StartStationName = startStationFromT;
                        }
                        // If empty, keep the value from trackInfo which already has fallback logic
                    }
                        
                    // End station (f) - check if not empty, otherwise keep the one from trackInfo (which has fallback logic)
                    if (tElement.TryGetProperty("f", out var fValue))
                    {
                        var endStationFromT = fValue.GetString();
                        if (!string.IsNullOrEmpty(endStationFromT))
                        {
                            details.EndStationName = endStationFromT;
                        }
                        // If empty, keep the value from trackInfo which already has fallback logic
                    }
                        
                    // URL to tracking website (j)
                    if (tElement.TryGetProperty("j", out var jValue))
                        details.TrackingUrl = jValue.GetString() ?? string.Empty;
                        
                    // Train type (k)
                    if (tElement.TryGetProperty("k", out var kValue))
                        details.Type = kValue.GetString() ?? string.Empty;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error fetching train details: {ex.Message}");
            }
            
            return details;
        }

        // New combined method that gets both train details and track info in a single API call
        public async Task<(TrainDetails details, TrainTrackInfo trackInfo)> GetTrainDetailsAndTrackAsync(string trainNumber)
        {
            var trackInfo = new TrainTrackInfo();
            var coords = new List<(double lat, double lng)>();
            var coordsWithDelay = new List<TrackCoordinate>();
            
            // Ensure we have a valid PID
            if (string.IsNullOrEmpty(_mapPagePid))
            {
                bool success = await GetMapPagePid();
                if (!success || string.IsNullOrEmpty(_mapPagePid))
                {
                    Debug.WriteLine("Failed to get PID for ShowTrack request");
                    var emptyDetails = new TrainDetails { Number = trainNumber };
                    trackInfo.Coordinates = coords;
                    return (emptyDetails, trackInfo);
                }
            }

            try
            {
                // Look up train ID from mapping
                if (!_trainIdMapping.TryGetValue(trainNumber, out long trainId))
                {
                    Debug.WriteLine($"Train ID not found for number: {trainNumber}");
                    var emptyDetails = new TrainDetails { Number = trainNumber };
                    trackInfo.Coordinates = coords;
                    return (emptyDetails, trackInfo);
                }
                
                // Prepare request payload with the correct values
                var payload = new
                {
                    AM = 0,
                    IS = trainId,
                    PID = _mapPagePid
                };
                
                var json = JsonSerializer.Serialize(payload);
                Debug.WriteLine($"Combined ShowTrack request payload: {json}");
                
                // Use the common HTTP request creation method
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var request = CreateHttpRequest(HttpMethod.Post, "https://mapa.portalpasazera.pl/pl/Mapa/ShowTrack", content);

                var response = await client.SendAsync(request);
                response.EnsureSuccessStatusCode();
                var responseBody = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"Combined ShowTrack response received, length: {responseBody.Length}");

                using var doc = JsonDocument.Parse(responseBody);
                
                // Initialize train details object
                var details = new TrainDetails
                {
                    Number = trainNumber,
                    TrainId = trainId
                };
                
                try 
                {
                    // Extract train details from a[0].t
                    if (doc.RootElement.TryGetProperty("a", out var aElement) &&
                        aElement.GetArrayLength() > 0 &&
                        aElement[0].TryGetProperty("t", out var tElement))
                    {
                        // Route name (a)
                        if (tElement.TryGetProperty("a", out var aValue))
                            details.RouteName = aValue.GetString() ?? string.Empty;
                            
                        // Route number (b)
                        if (tElement.TryGetProperty("b", out var bValue))
                            details.RouteNumber = bValue.GetString() ?? string.Empty;
                            
                        // Carrier (c)
                        if (tElement.TryGetProperty("c", out var cValue))
                            details.Carrier = cValue.GetString() ?? string.Empty;
                        
                        // Start station (d)
                        if (tElement.TryGetProperty("d", out var dValue))
                        {
                            var startStationFromT = dValue.GetString();
                            if (!string.IsNullOrEmpty(startStationFromT))
                            {
                                details.StartStationName = startStationFromT;
                                trackInfo.StartStationName = startStationFromT;
                            }
                        }
                            
                        // End station (f)
                        if (tElement.TryGetProperty("f", out var fValue))
                        {
                            var endStationFromT = fValue.GetString();
                            if (!string.IsNullOrEmpty(endStationFromT))
                            {
                                details.EndStationName = endStationFromT;
                                trackInfo.EndStationName = endStationFromT;
                            }
                        }
                        
                        // URL to tracking website (j)
                        if (tElement.TryGetProperty("j", out var jValue))
                            details.TrackingUrl = jValue.GetString() ?? string.Empty;
                        
                        // Train type (k)
                        if (tElement.TryGetProperty("k", out var kValue))
                            details.Type = kValue.GetString() ?? string.Empty;
                    }

                    // Extract station information from a[0].s array
                    if (doc.RootElement.TryGetProperty("a", out var aElement2) && 
                        aElement2.GetArrayLength() > 0 &&
                        aElement2[0].TryGetProperty("s", out var sElement))
                    {
                        foreach (var stationJson in sElement.EnumerateArray())
                        {
                            var station = new TrainStation();
                            
                            if (stationJson.TryGetProperty("a", out var nameElement))
                                station.Name = nameElement.GetString() ?? "";
                            
                            if (stationJson.TryGetProperty("c", out var schedArrElement))
                                station.ScheduledArrival = schedArrElement.GetString() ?? "";
                        
                            if (stationJson.TryGetProperty("d", out var actualArrElement))
                                station.ActualArrival = actualArrElement.GetString() ?? "";
                        
                            if (stationJson.TryGetProperty("e", out var arrDelayElement))
                                station.ArrivalDelay = arrDelayElement.GetDouble();
                        
                            if (stationJson.TryGetProperty("f", out var schedDepElement))
                                station.ScheduledDeparture = schedDepElement.GetString() ?? "";
                        
                            if (stationJson.TryGetProperty("g", out var actualDepElement))
                                station.ActualDeparture = actualDepElement.GetString() ?? "";
                        
                            if (stationJson.TryGetProperty("h", out var depDelayElement))
                                station.DepartureDelay = depDelayElement.GetDouble();
                        
                            if (stationJson.TryGetProperty("i", out var transportElement))
                                station.TransportType = transportElement.GetString() ?? "";
                        
                            if (stationJson.TryGetProperty("j", out var platformElement))
                                station.Platform = platformElement.GetString() ?? "";
                        
                            if (stationJson.TryGetProperty("k", out var latElement))
                                station.Latitude = latElement.GetDouble();
                        
                            if (stationJson.TryGetProperty("l", out var lngElement))
                                station.Longitude = lngElement.GetDouble();
                        
                            // Extract arrays of additional information
                            if (stationJson.TryGetProperty("m", out var messagesElement))
                            {
                                foreach (var msg in messagesElement.EnumerateArray())
                                {
                                    station.Messages.Add(msg.GetString() ?? "");
                                }
                            }
                        
                            if (stationJson.TryGetProperty("n", out var noticesElement))
                            {
                                foreach (var notice in noticesElement.EnumerateArray())
                                {
                                    station.Notices.Add(notice.GetString() ?? "");
                                }
                            }
                        
                            if (stationJson.TryGetProperty("o", out var warningsElement))
                            {
                                foreach (var warning in warningsElement.EnumerateArray())
                                {
                                    station.Warnings.Add(warning.GetString() ?? "");
                                }
                            }
                        
                            if (stationJson.TryGetProperty("p", out var additionalElement))
                            {
                                foreach (var info in additionalElement.EnumerateArray())
                                {
                                    station.AdditionalInfo.Add(info.GetString() ?? "");
                                }
                            }
                        
                            trackInfo.Stations.Add(station);
                        }
                        
                        details.Stations = trackInfo.Stations;
                        Debug.WriteLine($"Extracted {trackInfo.Stations.Count} stations");
                        
                        // Fallback: If start station name is empty, use first station from stations list
                        if (string.IsNullOrEmpty(trackInfo.StartStationName) && trackInfo.Stations.Count > 0)
                        {
                            trackInfo.StartStationName = trackInfo.Stations.First().Name;
                            details.StartStationName = trackInfo.StartStationName;
                            Debug.WriteLine($"Using first station as start station: {trackInfo.StartStationName}");
                        }
                        
                        // Fallback: If end station name is empty, use last station from stations list
                        if (string.IsNullOrEmpty(trackInfo.EndStationName) && trackInfo.Stations.Count > 0)
                        {
                            trackInfo.EndStationName = trackInfo.Stations.Last().Name;
                            details.EndStationName = trackInfo.EndStationName;
                            Debug.WriteLine($"Using last station as end station: {trackInfo.EndStationName}");
                        }
                    }
                    
                    // Navigate through the JSON structure to find the track coordinates
                    JsonElement? pointsArray = null;
                    if (doc.RootElement.TryGetProperty("a", out var a) && 
                        a.GetArrayLength() > 0 &&
                        a[0].TryGetProperty("r", out var r) &&
                        r.TryGetProperty("s", out var sElementCoords))
                    {
                        string[] possibleKeys = { "rt", "ct", "rct", "dt" };
                        foreach (var key in possibleKeys)
                        {
                            if (sElementCoords.TryGetProperty(key, out var arr) && arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() > 0)
                            {
                                pointsArray = arr;
                                break;
                            }
                        }
                    }

                    if (pointsArray != null)
                    {
                        int coordsWithDelayCount = 0;
                        foreach (var point in pointsArray.Value.EnumerateArray())
                        {
                            if (point.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var coord in point.EnumerateArray())
                                {
                                    var lat = coord.GetProperty("s").GetDouble();
                                    var lng = coord.GetProperty("d").GetDouble();
                                    coords.Add((lat, lng));
                                    
                                    // Extract delay information from "o" property
                                    double delay = 0;
                                    if (coord.TryGetProperty("o", out var delayProp))
                                    {
                                        delay = delayProp.GetDouble();
                                        if (delay > 0) coordsWithDelayCount++;
                                    }
                                    
                                    coordsWithDelay.Add(new TrackCoordinate 
                                    { 
                                        Latitude = lat, 
                                        Longitude = lng, 
                                        Delay = delay 
                                    });
                                }
                            }
                            else
                            {
                                var lat = point.GetProperty("s").GetDouble();
                                var lng = point.GetProperty("d").GetDouble();
                                coords.Add((lat, lng));
                                
                                // Extract delay information from "o" property
                                double delay = 0;
                                if (point.TryGetProperty("o", out var delayProp))
                                {
                                    delay = delayProp.GetDouble();
                                    if (delay > 0) coordsWithDelayCount++;
                                }
                                
                                coordsWithDelay.Add(new TrackCoordinate 
                                { 
                                    Latitude = lat, 
                                    Longitude = lng, 
                                    Delay = delay 
                                });
                            }
                        }
                        Debug.WriteLine($"Extracted {coords.Count} track coordinates, {coordsWithDelayCount} with delays > 0");
                    }
                    else
                    {
                        Debug.WriteLine("No track coordinates found in any of rt, ct, rct, dt");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error extracting track coordinates: {ex.Message}");
                }
                
                trackInfo.Coordinates = coords;
                trackInfo.CoordinatesWithDelay = coordsWithDelay;
                
                // Extract timing information from stations
                if (trackInfo.Stations.Count > 0)
                {
                    var firstStation = trackInfo.Stations.First();
                    var lastStation = trackInfo.Stations.Last();
                    
                    details.StartTime = !string.IsNullOrEmpty(firstStation.ScheduledDeparture) ? 
                        firstStation.ScheduledDeparture : firstStation.ScheduledArrival;
                    details.StartDelay = firstStation.DepartureDelay != 0 ? 
                        firstStation.DepartureDelay : firstStation.ArrivalDelay;
                    
                    details.EndTime = !string.IsNullOrEmpty(lastStation.ScheduledArrival) ? 
                        lastStation.ScheduledArrival : lastStation.ScheduledDeparture;
                    details.EndDelay = lastStation.ArrivalDelay != 0 ? 
                        lastStation.ArrivalDelay : lastStation.DepartureDelay;
                }
                
                return (details, trackInfo);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error fetching combined train data: {ex.Message}");
                var emptyDetails = new TrainDetails { Number = trainNumber };
                trackInfo.Coordinates = coords;
                return (emptyDetails, trackInfo);
            }
        }

        // Add method to pre-warm cache for visible trains
        public async Task PreWarmCacheForVisibleTrains(List<string> trainNumbers)
        {
            // For now, just implement a simple version
            // In a real implementation, this would pre-fetch data for visible trains
            await Task.CompletedTask;
        }

        // Add method to get speed information for a specific train from position history
        public TrainSpeedInfo GetTrainSpeedFromHistory(string trainNumber)
        {
            // For now, return a simple speed info object
            // In a real implementation, this would look up historical speed data
            return new TrainSpeedInfo
            {
                AverageSpeedKmh = 0,
                SpeedCategory = "Unknown",
                IsCalculated = false,
                CalculationMethod = "Not Available"
            };
        }
    }
}
