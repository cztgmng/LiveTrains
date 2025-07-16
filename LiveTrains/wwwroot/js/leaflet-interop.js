window.leafletInterop = {
    // The existing code doesn't need to be modified, as it already has the necessary
    // functionality for train markers, popups, and tracks. The TrainDetailsBox component
    // will be displayed in the Razor UI instead of a JavaScript popup.
    map: null,
    
    // Initialize the map with specified coordinates and zoom level
    initializeMap: function (elementId, latitude, longitude, zoomLevel) {
        if (this.map != null) {
            this.map.remove();
        }
        
        // Create the map instance
        this.map = L.map(elementId).setView([latitude, longitude], zoomLevel);
        
        // Add the OpenStreetMap Transport tile layer
        L.tileLayer('https://{s}.tile.thunderforest.com/transport-dark/{z}/{x}/{y}.png?apikey=6e5478c8a4f54c779f85573c0e399391', {
            attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors, Tiles &copy; Thunderforest',
            maxZoom: 19
        }).addTo(this.map);

        // Add loading event listeners to set black background
        var mapContainer = document.getElementById(elementId);
        if (mapContainer && this.map) {
            this.map.on('loading', function() {
                mapContainer.classList.add('map-loading-bg');
            });
            this.map.on('load', function() {
                mapContainer.classList.remove('map-loading-bg');
            });
        }
        
        return true;
    },
    
    // Add a marker to the map at the specified coordinates
    //addMarker: function (latitude, longitude, title) {
    //    if (this.map == null) return false;
    //    
    //    const marker = L.marker([latitude, longitude]).addTo(this.map);
    //    if (title) {
    //        marker.bindPopup(title).openPopup();
    //    }
    //    
    //    return true;
    //},
    
    // Set the view to specified coordinates and zoom level
    setView: function (latitude, longitude, zoomLevel) {
        if (this.map == null) return false;
        
        this.map.setView([latitude, longitude], zoomLevel);
        return true;
    },
    
    // Invalidate the map size (useful when container is resized)
    invalidateSize: function () {
        if (this.map == null) return false;
        
        this.map.invalidateSize();
        return true;
    },

    // Update train markers on the map
    updateTrainMarkers: function (trainPositions) {
        return;
        // Remove old train markers if needed
        if (window.trainMarkers) {
            window.trainMarkers.forEach(m => m.remove());
        }
        window.trainMarkers = [];

        if (!this.map) return;

        trainPositions.forEach(tp => {
            const marker = L.marker([tp.latitude, tp.longitude])
                .bindPopup(`Train ${tp.number} (${tp.type})`)
                .addTo(this.map);
            window.trainMarkers.push(marker);
        });
    },

    // Add a train marker with a custom icon
    //addTrainMarker: function(lat, lng, label, iconUrl) {
    //    if (!this.map) return false;
    //
    //    var icon = L.icon({
    //        iconUrl: iconUrl,
    //        iconSize: [32, 32], // Adjust size as needed
    //        iconAnchor: [16, 16],
    //        popupAnchor: [0, -16]
    //    });
    //
    //    var marker = L.marker([lat, lng], { icon: icon }).bindPopup(label);
    //    marker.addTo(this.map);
    //
    //    window._markers = window._markers || [];
    //    window._markers.push(marker);
    //},

    // Add a train marker with a custom icon and click event
    // Added isSelected parameter for highlighting selected train
    addTrainMarkerWithClick: function(lat, lng, label, iconUrl, dotNetRef, trainNumber, isSelected, hideOthers) {
        if (!this.map) return false;
        
        // If hideOthers is true and this train is not selected, don't add the marker
        if (hideOthers && !isSelected) {
            return false;
        }
        
        var iconOptions = {
            iconUrl: iconUrl,
            iconSize: isSelected ? [48, 48] : [32, 32], // Make selected train larger
            iconAnchor: isSelected ? [24, 24] : [16, 16],
            popupAnchor: [0, -16]
        };
        
        var icon = L.icon(iconOptions);
        
        var markerOptions = { 
            icon: icon,
            zIndexOffset: isSelected ? 1000 : 0 // Make selected train appear on top
        };
        
        var marker = L.marker([lat, lng], markerOptions).bindPopup(label);
        
        if (isSelected) {
            // Highlight selected train
            marker.openPopup();
        }
        
        marker.addTo(this.map);
        marker.on('click', function () {
            dotNetRef.invokeMethodAsync('OnTrainMarkerClicked', trainNumber);
        });
        
        // Store train number with marker for easy identification
        marker.trainNumber = trainNumber;
        
        window._markers = window._markers || [];
        window._markers.push(marker);
    },

    // Hide all train markers except the selected one
    hideOtherTrains: function(selectedTrainNumber) {
        if (!window._markers) return;
        
        window._markers.forEach(marker => {
            if (marker.trainNumber !== selectedTrainNumber) {
                marker.setOpacity(0);
            }
        });
    },

    // Show all train markers
    showAllTrains: function() {
        if (!window._markers) return;
        
        window._markers.forEach(marker => {
            marker.setOpacity(1);
        });
    },

    // Draw a track on the map with stations
    drawTrack: function(latlngs, stationNames, stations) {
        if (!this.map) return;
        this.clearTrack();
        
        // Convert to the format Leaflet expects if needed
        let formattedPoints;
        if (latlngs.length > 0 && Array.isArray(latlngs[0])) {
            // Data is already in format [[lat, lng], [lat, lng], ...]
            formattedPoints = latlngs;
        } else {
            // Convert from [{lat, lng}, {lat, lng}, ...] format
            formattedPoints = latlngs.map(point => [point.lat, point.lng]);
        }
        
        // Add track line with markers for stations
        window._trackPolyline = L.polyline(formattedPoints, { 
            color: 'blue', 
            weight: 4,
            opacity: 0.7
        }).addTo(this.map);
        
        // Initialize stations marker array
        window._stationMarkers = window._stationMarkers || [];
        
        // Add station markers if provided
        if (stations && stations.length > 0) {
            stations.forEach((station, index) => {
                // Create station marker
                var stationIcon = L.divIcon({
                    className: 'station-marker',
                    html: `<div class="station-dot" style="background-color: ${this.getStationColor(station, index, stations.length)}; width: 8px; height: 8px; border: 2px solid white; border-radius: 50%; box-shadow: 0 0 3px rgba(0,0,0,0.5);"></div>`,
                    iconSize: [12, 12],
                    iconAnchor: [6, 6]
                });
                
                var marker = L.marker([station.latitude, station.longitude], {
                    icon: stationIcon,
                    zIndexOffset: 100
                }).addTo(this.map);
                
                // Create popup content with timing and delay information
                var popupContent = this.createStationPopupContent(station);
                marker.bindPopup(popupContent);
                
                // Add hover effects
                marker.on('mouseover', function() {
                    this.openPopup();
                });
                
                marker.on('mouseout', function() {
                    setTimeout(() => {
                        if (!this.getPopup().isOpen() || !this.getPopup()._container.matches(':hover')) {
                            this.closePopup();
                        }
                    }, 100);
                });
                
                window._stationMarkers.push(marker);
            });
        }
        
        // Add markers for start and end of track (fallback if no station data)
        if (formattedPoints.length > 0 && (!stations || stations.length === 0)) {
            var startPoint = formattedPoints[0];
            var endPoint = formattedPoints[formattedPoints.length - 1];
            
            // Get station names from the parameter or use default
            var startStationName = stationNames && stationNames.start ? stationNames.start : 'Start Station';
            var endStationName = stationNames && stationNames.end ? stationNames.end : 'End Station';
            
            // Start marker
            window._startMarker = L.marker(startPoint, {
                icon: L.divIcon({
                    className: 'station-marker start-station',
                    html: '<div style="background-color: green; width: 12px; height: 12px; border-radius: 50%;"></div>',
                    iconSize: [12, 12],
                    iconAnchor: [6, 6]
                })
            }).addTo(this.map)
              .bindPopup(startStationName);
            
            // End marker
            window._endMarker = L.marker(endPoint, {
                icon: L.divIcon({
                    className: 'station-marker end-station',
                    html: '<div style="background-color: red; width: 12px; height: 12px; border-radius: 50%;"></div>',
                    iconSize: [12, 12],
                    iconAnchor: [6, 6]
                })
            }).addTo(this.map)
              .bindPopup(endStationName);
        }
        
        // Fit map to track bounds
        if (window._trackPolyline) {
            this.map.fitBounds(window._trackPolyline.getBounds());
        }
    },

    // Draw a track on the map with stations and delay-based coloring
    drawTrackWithDelay: function(coordinatesWithDelay, stationNames, stations) {
        if (!this.map) return;
        this.clearTrack();
        
        if (!coordinatesWithDelay || coordinatesWithDelay.length === 0) {
            console.log("No coordinates with delay provided");
            return;
        }
        
        // Initialize track polylines array to store different colored segments
        window._trackPolylines = window._trackPolylines || [];
        
        // Group consecutive coordinates by delay to create segments with same color
        const segments = this.createDelaySegments(coordinatesWithDelay);
        
        console.log(`Creating ${segments.length} track segments based on delay`);
        
        // Draw each segment with its appropriate color
        segments.forEach(segment => {
            const polyline = L.polyline(segment.coordinates, {
                color: this.getDelayColor(segment.delay),
                weight: 4,
                opacity: 0.8
            }).addTo(this.map);
            
            // Add tooltip to show delay information
            if (segment.delay > 0) {
                polyline.bindTooltip(`Delay: +${segment.delay.toFixed(1)} min`, {
                    permanent: false,
                    direction: 'center'
                });
            } else {
                polyline.bindTooltip('On time', {
                    permanent: false,
                    direction: 'center'
                });
            }
            
            window._trackPolylines.push(polyline);
        });
        
        // Initialize stations marker array
        window._stationMarkers = window._stationMarkers || [];
        
        // Add station markers if provided
        if (stations && stations.length > 0) {
            stations.forEach((station, index) => {
                // Create station marker
                var stationIcon = L.divIcon({
                    className: 'station-marker',
                    html: `<div class="station-dot" style="background-color: ${this.getStationColor(station, index, stations.length)}; width: 8px; height: 8px; border: 2px solid white; border-radius: 50%; box-shadow: 0 0 3px rgba(0,0,0,0.5);"></div>`,
                    iconSize: [12, 12],
                    iconAnchor: [6, 6]
                });
                
                var marker = L.marker([station.latitude, station.longitude], {
                    icon: stationIcon,
                    zIndexOffset: 100
                }).addTo(this.map);
                
                // Create popup content with timing and delay information
                var popupContent = this.createStationPopupContent(station);
                marker.bindPopup(popupContent);
                
                // Add hover effects
                marker.on('mouseover', function() {
                    this.openPopup();
                });
                
                marker.on('mouseout', function() {
                    setTimeout(() => {
                        if (!this.getPopup().isOpen() || !this.getPopup()._container.matches(':hover')) {
                            this.closePopup();
                        }
                    }, 100);
                });
                
                window._stationMarkers.push(marker);
            });
        }
        
        // Add markers for start and end of track (fallback if no station data)
        if (coordinatesWithDelay.length > 0 && (!stations || stations.length === 0)) {
            var startPoint = [coordinatesWithDelay[0].lat, coordinatesWithDelay[0].lng];
            var endPoint = [coordinatesWithDelay[coordinatesWithDelay.length - 1].lat, coordinatesWithDelay[coordinatesWithDelay.length - 1].lng];
            
            // Get station names from the parameter or use default
            var startStationName = stationNames && stationNames.start ? stationNames.start : 'Start Station';
            var endStationName = stationNames && stationNames.end ? stationNames.end : 'End Station';
            
            // Start marker
            window._startMarker = L.marker(startPoint, {
                icon: L.divIcon({
                    className: 'station-marker start-station',
                    html: '<div style="background-color: green; width: 12px; height: 12px; border-radius: 50%;"></div>',
                    iconSize: [12, 12],
                    iconAnchor: [6, 6]
                })
            }).addTo(this.map)
              .bindPopup(startStationName);
            
            // End marker
            window._endMarker = L.marker(endPoint, {
                icon: L.divIcon({
                    className: 'station-marker end-station',
                    html: '<div style="background-color: red; width: 12px; height: 12px; border-radius: 50%;"></div>',
                    iconSize: [12, 12],
                    iconAnchor: [6, 6]
                })
            }).addTo(this.map)
              .bindPopup(endStationName);
        }
        
        // Fit map to track bounds using the first polyline
        if (window._trackPolylines && window._trackPolylines.length > 0) {
            // Create a group of all polylines to get combined bounds
            var group = L.featureGroup(window._trackPolylines);
            this.map.fitBounds(group.getBounds());
        }
        
        // Add delay legend
        this.addDelayLegend();
    },

    // Create segments of consecutive coordinates with the same delay
    createDelaySegments: function(coordinatesWithDelay) {
        if (!coordinatesWithDelay || coordinatesWithDelay.length === 0) {
            return [];
        }
        
        const segments = [];
        let currentSegment = {
            delay: coordinatesWithDelay[0].delay || 0, // Default to 0 if delay is undefined
            coordinates: [[coordinatesWithDelay[0].lat, coordinatesWithDelay[0].lng]]
        };
        
        for (let i = 1; i < coordinatesWithDelay.length; i++) {
            const coord = coordinatesWithDelay[i];
            const coordDelay = coord.delay || 0; // Default to 0 if delay is undefined
            
            // If delay is the same as current segment, add to current segment
            if (coordDelay === currentSegment.delay) {
                currentSegment.coordinates.push([coord.lat, coord.lng]);
            } else {
                // Delay changed, finish current segment and start new one
                if (currentSegment.coordinates.length > 1) {
                    segments.push(currentSegment);
                }
                currentSegment = {
                    delay: coordDelay,
                    coordinates: [
                        // Include the last point from previous segment to ensure continuity
                        currentSegment.coordinates[currentSegment.coordinates.length - 1],
                        [coord.lat, coord.lng]
                    ]
                };
            }
        }
        
        // Add the last segment if it has more than one coordinate
        if (currentSegment.coordinates.length > 1) {
            segments.push(currentSegment);
        }
        
        // If no segments were created (all single points), create one segment with all points
        if (segments.length === 0 && coordinatesWithDelay.length > 1) {
            segments.push({
                delay: coordinatesWithDelay[0].delay || 0,
                coordinates: coordinatesWithDelay.map(coord => [coord.lat, coord.lng])
            });
        }
        
        return segments;
    },

    // Get color based on delay time
    getDelayColor: function(delay) {
        if (delay <= 0) {
            return '#22aa22'; // Green for on time or early
        } else if (delay <= 5) {
            return '#ffaa00'; // Orange for small delays (1-5 minutes)
        } else if (delay <= 15) {
            return '#ff6600'; // Red-orange for medium delays (6-15 minutes)
        } else if (delay <= 30) {
            return '#ff3300'; // Red for significant delays (16-30 minutes)
        } else {
            return '#aa0000'; // Dark red for severe delays (30+ minutes)
        }
    },

    // Add a legend showing delay color coding
    addDelayLegend: function() {
        if (!this.map) return;
        
        // Remove existing legend if present
        if (window._delayLegend) {
            this.map.removeControl(window._delayLegend);
        }
        
        var DelayLegend = L.Control.extend({
            options: {
                position: 'bottomright'
            },
            onAdd: function() {
                var container = L.DomUtil.create('div', 'delay-legend');
                container.innerHTML = `
                    <div style="background: white; padding: 10px; border-radius: 5px; box-shadow: 0 0 5px rgba(0,0,0,0.2); font-size: 12px;">
                        <div style="font-weight: bold; margin-bottom: 5px;">Track Delays</div>
                        <div style="display: flex; align-items: center; margin: 2px 0;">
                            <div style="width: 20px; height: 3px; background: #22aa22; margin-right: 5px;"></div>
                            <span>On time</span>
                        </div>
                        <div style="display: flex; align-items: center; margin: 2px 0;">
                            <div style="width: 20px; height: 3px; background: #ffaa00; margin-right: 5px;"></div>
                            <span>1-5 min</span>
                        </div>
                        <div style="display: flex; align-items: center; margin: 2px 0;">
                            <div style="width: 20px; height: 3px; background: #ff6600; margin-right: 5px;"></div>
                            <span>6-15 min</span>
                        </div>
                        <div style="display: flex; align-items: center; margin: 2px 0;">
                            <div style="width: 20px; height: 3px; background: #ff3300; margin-right: 5px;"></div>
                            <span>16-30 min</span>
                        </div>
                        <div style="display: flex; align-items: center; margin: 2px 0;">
                            <div style="width: 20px; height: 3px; background: #aa0000; margin-right: 5px;"></div>
                            <span>30+ min</span>
                        </div>
                    </div>
                `;
                return container;
            }
        });
        
        window._delayLegend = new DelayLegend();
        this.map.addControl(window._delayLegend);
    },

    // Draw a track on the map with stations (fallback for backward compatibility)
    drawTrack: function(latlngs, stationNames, stations) {
        // Convert old format to new format and call the delay-aware method
        const coordinatesWithDelay = latlngs.map(coord => ({
            lat: Array.isArray(coord) ? coord[0] : coord.lat,
            lng: Array.isArray(coord) ? coord[1] : coord.lng,
            delay: 0 // Default to no delay for backward compatibility
        }));
        
        this.drawTrackWithDelay(coordinatesWithDelay, stationNames, stations);
    },

    // Helper function to get station color based on position and delays
    getStationColor: function(station, index, totalStations) {
        // Color based on delays
        if (station.arrivalDelay > 0 || station.departureDelay > 0) {
            return '#ff4444'; // Red for delays
        }
        
        // Color based on position (start/end/middle)
        if (index === 0) return '#22aa22'; // Green for start
        if (index === totalStations - 1) return '#aa2222'; // Red for end
        return '#4488ff'; // Blue for intermediate stations
    },

    // Helper function to create station popup content
    createStationPopupContent: function(station) {
        var content = `<div class="station-popup">
            <h4 class="station-name">${station.name}</h4>`;
        
        if (station.platform) {
            content += `<div class="station-platform">Platform: ${station.platform}</div>`;
        }
        
        if (station.transportType && station.transportType !== 'TRAIN') {
            content += `<div class="transport-type">${station.transportType}</div>`;
        }
        
        // Arrival information
        if (station.scheduledArrival) {
            content += `<div class="timing-info">
                <div class="arrival-info">
                    <span class="timing-label">Arrival:</span>
                    <span class="scheduled-time">${station.scheduledArrival}</span>`;
            
            if (station.arrivalDelay > 0) {
                content += ` <span class="delay-info" style="color: white;">+${station.arrivalDelay.toFixed(1)}min</span>`;
            }
            
            if (station.actualArrival && station.actualArrival !== station.scheduledArrival) {
                content += ` <span class="actual-time">(${station.actualArrival})</span>`;
            }
            
            content += `</div></div>`;
        }
        
        // Departure information
        if (station.scheduledDeparture) {
            content += `<div class="timing-info">
                <div class="departure-info">
                    <span class="timing-label">Departure:</span>
                    <span class="scheduled-time">${station.scheduledDeparture}</span>`;
            
            if (station.departureDelay > 0) {
                content += ` <span class="delay-info" style="color: white;">+${station.departureDelay.toFixed(1)}min</span>`;
            }
            
            if (station.actualDeparture && station.actualDeparture !== station.scheduledDeparture) {
                content += ` <span class="actual-time">(${station.actualDeparture})</span>`;
            }
            
            content += `</div></div>`;
        }
        
        // Additional information
        if (station.additionalInfo && station.additionalInfo.length > 0) {
            content += `<div class="additional-info">`;
            station.additionalInfo.forEach(info => {
                if (info.trim()) {
                    content += `<div class="info-message">${info}</div>`;
                }
            });
            content += `</div>`;
        }
        
        content += `</div>`;
        
        return content;
    },

    // Clear the track from the map
    clearTrack: function() {
        // Clear multiple polylines if they exist
        if (window._trackPolylines) {
            window._trackPolylines.forEach(polyline => {
                this.map.removeLayer(polyline);
            });
            window._trackPolylines = [];
        }
        
        // Clear single polyline for backward compatibility
        if (window._trackPolyline) {
            this.map.removeLayer(window._trackPolyline);
            window._trackPolyline = null;
        }
        
        if (window._startMarker) {
            this.map.removeLayer(window._startMarker);
            window._startMarker = null;
        }
        
        if (window._endMarker) {
            this.map.removeLayer(window._endMarker);
            window._endMarker = null;
        }
        
        // Clear station markers
        if (window._stationMarkers) {
            window._stationMarkers.forEach(marker => {
                this.map.removeLayer(marker);
            });
            window._stationMarkers = [];
        }
        
        // Clear delay legend
        if (window._delayLegend) {
            this.map.removeControl(window._delayLegend);
            window._delayLegend = null;
        }
    },

    // Clear all markers from the map
    clearMarkers: function() {
        if (!window._markers) return;

        for (var i = 0; i < window._markers.length; i++) {
            this.map.removeLayer(window._markers[i]);
        }

        window._markers = [];
    },
    
    // Show loading indicator on the map
    showLoadingIndicator: function(show) {
        if (!this.map) return;
        
        if (show) {
            if (!window._loadingIndicator) {
                // Create loading indicator
                var LoadingControl = L.Control.extend({
                    options: {
                        position: 'topright'
                    },
                    onAdd: function() {
                        var container = L.DomUtil.create('div', 'loading-indicator');
                        container.innerHTML = '<div style="background: white; padding: 10px; border-radius: 5px; box-shadow: 0 0 5px rgba(0,0,0,0.2);">Loading train track...</div>';
                        return container;
                    }
                });
                
                window._loadingIndicator = new LoadingControl();
                this.map.addControl(window._loadingIndicator);
            }
        } else {
            // Remove loading indicator
            if (window._loadingIndicator) {
                this.map.removeControl(window._loadingIndicator);
                window._loadingIndicator = null;
            }
        }
    }
};
