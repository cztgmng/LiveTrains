window.leafletInterop = {
    // The existing code doesn't need to be modified, as it already has the necessary
    // functionality for train markers, popups, and tracks. The TrainDetailsBox component
    // will be displayed in the Razor UI instead of a JavaScript popup.
    map: null,
    
    // Initialize the map with specified coordinates and zoom level
    initializeMap: function (elementId, latitude, longitude, zoomLevel) {
        try {
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
            
            console.log('Map initialized successfully');
            return true;
        } catch (error) {
            console.error('Error initializing map:', error);
            return false;
        }
    },
    
    // Set the view to specified coordinates and zoom level
    setView: function (latitude, longitude, zoomLevel) {
        try {
            if (this.map == null) return false;
            
            this.map.setView([latitude, longitude], zoomLevel);
            return true;
        } catch (error) {
            console.error('Error setting view:', error);
            return false;
        }
    },
    
    // Invalidate the map size (useful when container is resized)
    invalidateSize: function () {
        try {
            if (this.map == null) return false;
            
            this.map.invalidateSize();
            return true;
        } catch (error) {
            console.error('Error invalidating size:', error);
            return false;
        }
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

    // Enhanced speed-based animation function for moving markers
    animateMarkerToPosition: function(marker, newLatLng, trainSpeed = 0, lastUpdateTime = null) {
        if (!marker || !newLatLng) return;
        
        try {
            var startLatLng = marker.getLatLng();
            var startTime = Date.now();
            
            // If the marker is at the same position, don't animate
            var distance = startLatLng.distanceTo(newLatLng);
            if (distance < 1) { // Less than 1 meter difference
                return;
            }
            
            // Calculate animation duration based on train's average speed
            var duration = 1000; // Default 1 second fallback
            
            if (trainSpeed > 0) {
                // Convert speed from km/h to m/s
                var speedMs = trainSpeed * 1000 / 3600;
                
                // Calculate time to travel the distance at the train's speed
                var realTimeMs = (distance / speedMs) * 1000;
                
                // Scale down the real time for visualization (but keep it proportional)
                // Use a scaling factor that keeps faster trains moving faster than slower ones
                var scaleFactor = Math.min(Math.max(realTimeMs / 5000, 0.2), 3.0); // Scale between 0.2x and 3.0x
                duration = Math.max(realTimeMs * scaleFactor, 200); // Minimum 200ms
                duration = Math.min(duration, 5000); // Maximum 5 seconds
                
                console.log(`Train speed: ${trainSpeed} km/h, distance: ${distance.toFixed(1)}m, duration: ${duration.toFixed(0)}ms`);
            } else {
                // Fallback: adjust duration based on distance for trains without speed data
                duration = Math.min(Math.max(distance * 2, 300), 2000);
            }
            
            // Cancel any existing animation for this marker
            if (marker._animationId) {
                cancelAnimationFrame(marker._animationId);
            }
            
            var animate = function() {
                var elapsed = Date.now() - startTime;
                var progress = Math.min(elapsed / duration, 1);
                
                // Use easing function for smooth animation (ease-in-out cubic)
                var easeProgress = progress < 0.5 
                    ? 4 * progress * progress * progress
                    : 1 - Math.pow(-2 * progress + 2, 3) / 2;
                
                var currentLat = startLatLng.lat + (newLatLng.lat - startLatLng.lat) * easeProgress;
                var currentLng = startLatLng.lng + (newLatLng.lng - startLatLng.lng) * easeProgress;
                
                marker.setLatLng([currentLat, currentLng]);
                
                if (progress < 1) {
                    marker._animationId = requestAnimationFrame(animate);
                } else {
                    marker._animationId = null;
                }
            };
            
            marker._animationId = requestAnimationFrame(animate);
        } catch (error) {
            console.error('Error animating marker:', error);
        }
    },

    // Helper function to get speed color based on speed category
    getSpeedColor: function(speedCategory) {
        switch(speedCategory) {
            case 'Slow': return '#ff6b6b';      // Red for slow trains
            case 'Moderate': return '#ffd93d';   // Yellow for moderate speed
            case 'Fast': return '#6bcf7f';       // Green for fast trains
            case 'High-Speed': return '#4dabf7'; // Blue for high-speed trains
            default: return '#868e96';           // Gray for unknown speed
        }
    },

    // Helper function to get station color based on position
    getStationColor: function(station, index, totalStations) {
        if (index === 0) return '#22aa22'; // Green for start station
        if (index === totalStations - 1) return '#ff3300'; // Red for end station
        
        // Color based on delay
        if (station.arrivalDelay > 0 || station.departureDelay > 0) {
            var maxDelay = Math.max(station.arrivalDelay, station.departureDelay);
            if (maxDelay <= 5) return '#ffaa00'; // Orange for small delays
            if (maxDelay <= 15) return '#ff6600'; // Red-orange for medium delays
            return '#ff3300'; // Red for large delays
        }
        
        return '#4dabf7'; // Blue for on-time intermediate stations
    },

    // Helper function to create station popup content
    createStationPopupContent: function(station) {
        var content = `<div class="station-popup">
            <h4>${station.name}</h4>`;
        
        if (station.platform) {
            content += `<p><strong>Platform:</strong> ${station.platform}</p>`;
        }
        
        if (station.scheduledArrival) {
            content += `<p><strong>Scheduled Arrival:</strong> ${station.scheduledArrival}`;
            if (station.arrivalDelay > 0) {
                content += ` <span class="delay">+${station.arrivalDelay} min</span>`;
            }
            content += '</p>';
        }
        
        if (station.scheduledDeparture) {
            content += `<p><strong>Scheduled Departure:</strong> ${station.scheduledDeparture}`;
            if (station.departureDelay > 0) {
                content += ` <span class="delay">+${station.departureDelay} min</span>`;
            }
            content += '</p>';
        }
        
        content += '</div>';
        return content;
    },

    // Enhanced method to smoothly update train positions with speed-based movement
    updateTrainPositionsSmooth: function(trainPositions, selectedTrainNumber, hideOthers, dotNetRef) {
        try {
            if (!this.map || !trainPositions) {
                console.warn('Map or trainPositions not available');
                return;
            }
            
            // Validate dotNetRef
            if (!dotNetRef) {
                console.warn('DotNetObjectReference not provided');
                return;
            }
            
            // Show speed legend when trains are present
            if (trainPositions.length > 0) {
                this.addSpeedLegend();
            }
            
            // Initialize markers map if it doesn't exist
            if (!window._trainMarkersMap) {
                window._trainMarkersMap = new Map();
            }
            
            var markersMap = window._trainMarkersMap;
            var currentTrainNumbers = new Set(trainPositions.map(t => t.number));
            
            // Remove markers for trains that no longer exist
            for (let [trainNumber, marker] of markersMap) {
                if (!currentTrainNumbers.has(trainNumber)) {
                    try {
                        this.map.removeLayer(marker);
                        markersMap.delete(trainNumber);
                    } catch (error) {
                        console.error(`Error removing marker for train ${trainNumber}:`, error);
                    }
                }
            }
            
            // Process each train position
            trainPositions.forEach(train => {
                try {
                    var isSelected = train.number === selectedTrainNumber;
                    var shouldHide = hideOthers && !isSelected;
                    
                    if (shouldHide) {
                        // Hide marker if it exists
                        if (markersMap.has(train.number)) {
                            markersMap.get(train.number).setOpacity(0);
                        }
                        return;
                    }
                    
                    var newLatLng = L.latLng(train.latitude, train.longitude);
                    var iconUrl = this.getCarrierIconUrl(train.type);
                    
                    // Create train title with GPS indicator and speed information
                    var trainTitle = `${train.number} (${train.type})`;
                    if (train.hasGps) {
                        trainTitle += " ???";
                    }
                    if (train.averageSpeedKmh > 0) {
                        trainTitle += ` - ${train.averageSpeedKmh.toFixed(0)} km/h`;
                    }
                    if (train.speedCategory && train.speedCategory !== 'Unknown') {
                        trainTitle += ` (${train.speedCategory})`;
                    }
                    
                    if (markersMap.has(train.number)) {
                        // Update existing marker
                        var marker = markersMap.get(train.number);
                        
                        // Update marker position with speed-based animation
                        this.animateMarkerToPosition(marker, newLatLng, train.averageSpeedKmh, train.lastUpdated);
                        
                        // Update marker appearance if selection state changed
                        var currentIconSize = isSelected ? 48 : 32;
                        var currentAnchor = isSelected ? [24, 24] : [16, 16];
                        var currentZIndex = isSelected ? 1000 : 0;
                        
                        // Only update icon if changed
                        var prevIconUrl = marker.options.icon && marker.options.icon.options && marker.options.icon.options.iconUrl;
                        var prevIconSize = marker.options.icon && marker.options.icon.options && marker.options.icon.options.iconSize;
                        var needIconUpdate = prevIconUrl !== iconUrl || !prevIconSize || prevIconSize[0] !== currentIconSize;
                        if (needIconUpdate) {
                            var newIcon = L.icon({
                                iconUrl: iconUrl,
                                iconSize: [currentIconSize, currentIconSize],
                                iconAnchor: currentAnchor,
                                popupAnchor: [0, -16]
                            });
                            marker.setIcon(newIcon);
                        }
                        // Only update zIndex if changed
                        if (marker.options.zIndexOffset !== currentZIndex) {
                            marker.setZIndexOffset(currentZIndex);
                        }
                        // Only update opacity if hidden
                        if (marker.options.opacity !== 1) {
                            marker.setOpacity(1);
                        }
                        // Only update popup content if changed
                        if (marker.getPopup() && marker.getPopup().getContent() !== trainTitle) {
                            marker.getPopup().setContent(trainTitle);
                        }
                        // Add speed-based visual indicator (colored border) only if color changed
                        if (train.speedCategory && train.speedCategory !== 'Unknown') {
                            var speedColor = this.getSpeedColor(train.speedCategory);
                            var el = marker.getElement();
                            if (el && el._lastSpeedColor !== speedColor) {
                                el.style.filter = `drop-shadow(0 0 2px ${speedColor}) drop-shadow(0 0 12px ${speedColor})`;
                                el._lastSpeedColor = speedColor;
                            }
                        }
                        
                        // Ensure click event is attached (in case it wasn't before)
                        if (!marker._hasClickEvent) {
                            marker.on('click', function () {
                                console.log(`Marker clicked for train: ${train.number}`);
                                try {
                                    dotNetRef.invokeMethodAsync('OnTrainMarkerClicked', train.number);
                                } catch (error) {
                                    console.error(`Error calling OnTrainMarkerClicked for train ${train.number}:`, error);
                                }
                            });
                            marker._hasClickEvent = true;
                        }
                        
                        // Handle selection state
                        if (isSelected && !marker.isPopupOpen()) {
                            marker.openPopup();
                        } else if (!isSelected && marker.isPopupOpen()) {
                            marker.closePopup();
                        }
                        
                    } else {
                        // Create new marker
                        var iconOptions = {
                            iconUrl: iconUrl,
                            iconSize: isSelected ? [48, 48] : [32, 32],
                            iconAnchor: isSelected ? [24, 24] : [16, 16],
                            popupAnchor: [0, -16]
                        };
                        
                        var icon = L.icon(iconOptions);
                        
                        var markerOptions = { 
                            icon: icon,
                            zIndexOffset: isSelected ? 1000 : 0
                        };
                        
                        var marker = L.marker(newLatLng, markerOptions).bindPopup(trainTitle);
                        
                        // Add speed-based visual indicator for new markers
                        if (train.speedCategory && train.speedCategory !== 'Unknown') {
                            var speedColor = this.getSpeedColor(train.speedCategory);
                            // We'll apply the style after the marker is added to the map
                            setTimeout(() => {
                                if (marker.getElement()) {
                                    marker.getElement().style.filter = `drop-shadow(0 0 2px ${speedColor}) drop-shadow(0 0 12px ${speedColor})`;
                                }
                            }, 100);
                        }
                        
                        if (isSelected) {
                            marker.openPopup();
                        }
                        
                        marker.addTo(this.map);
                        
                        // Add click event with proper error handling
                        marker.on('click', function () {
                            console.log(`Marker clicked for train: ${train.number}`);
                            try {
                                dotNetRef.invokeMethodAsync('OnTrainMarkerClicked', train.number);
                            } catch (error) {
                                console.error(`Error calling OnTrainMarkerClicked for train ${train.number}:`, error);
                            }
                        });
                        marker._hasClickEvent = true;
                        
                        // Store train number with marker for easy identification
                        marker.trainNumber = train.number;
                        markersMap.set(train.number, marker);
                    }
                } catch (error) {
                    console.error(`Error processing train ${train.number}:`, error);
                }
            });
        } catch (error) {
            console.error('Error in updateTrainPositionsSmooth:', error);
        }
    },

    // Helper method to get carrier icon URL
    getCarrierIconUrl: function (carrier) {
        switch(carrier) {
            case "IC": return "https://i.imgur.com/PmfA7tz.png";
            case "PR": return "https://i.imgur.com/sNgU6H4.png";
            case "KW": return "https://i.imgur.com/kD31TYF.png";
            case "AR": return "https://i.imgur.com/M7twBR8.png";
            case "KS": return "https://i.imgur.com/xrrZhWb.png";
            case "KD": return "https://i.imgur.com/tepsm70.png";
            case "KM": return "https://i.imgur.com/L7NWcFd.png";
            case "SKM": return "https://i.imgur.com/ID3tqWH.png";
            case "SKMT": return "https://i.imgur.com/DTA4Y0H.png";
            case "ŁKA": return "https://i.imgur.com/E06bGt9.png";
            case "KMŁ": return "https://i.imgur.com/i0T6eWk.png";
            case "ODEG": return "https://i.imgur.com/3HDhpCv.png";
            default: return "https://i.imgur.com/wKEfjIH.png";
        }
    },

    // Add a train marker with a custom icon and click event
    // Added isSelected parameter for highlighting selected train
    addTrainMarkerWithClick: function(lat, lng, label, iconUrl, dotNetRef, trainNumber, isSelected, hideOthers) {
        try {
            if (!this.map) return false;
            
            // Validate dotNetRef
            if (!dotNetRef) {
                console.warn('DotNetObjectReference not provided for train marker');
                return false;
            }
            
            // Store dotNetRef for later use in smooth updates
            if (!window._dotNetRefs) {
                window._dotNetRefs = new Map();
            }
            window._dotNetRefs.set(trainNumber, dotNetRef);
            
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
                try {
                    dotNetRef.invokeMethodAsync('OnTrainMarkerClicked', trainNumber);
                } catch (error) {
                    console.error(`Error calling OnTrainMarkerClicked for train ${trainNumber}:`, error);
                }
            });
            
            // Store train number with marker for easy identification
            marker.trainNumber = trainNumber;
            
            window._markers = window._markers || [];
            window._markers.push(marker);
            
            return true;
        } catch (error) {
            console.error('Error adding train marker:', error);
            return false;
        }
    },

    // Hide all train markers except the selected one (updated for smooth animations)
    hideOtherTrains: function(selectedTrainNumber) {
        try {
            if (window._trainMarkersMap) {
                for (let [trainNumber, marker] of window._trainMarkersMap) {
                    if (trainNumber !== selectedTrainNumber) {
                        marker.setOpacity(0);
                    }
                }
            }
            
            // Fallback for old marker system
            if (window._markers) {
                window._markers.forEach(marker => {
                    if (marker.trainNumber !== selectedTrainNumber) {
                        marker.setOpacity(0);
                    }
                });
            }
        } catch (error) {
            console.error('Error hiding other trains:', error);
        }
    },

    // Show all train markers (updated for smooth animations)
    showAllTrains: function() {
        try {
            if (window._trainMarkersMap) {
                for (let [trainNumber, marker] of window._trainMarkersMap) {
                    marker.setOpacity(1);
                }
            }
            
            // Fallback for old marker system
            if (window._markers) {
                window._markers.forEach(marker => {
                    marker.setOpacity(1);
                });
            }
        } catch (error) {
            console.error('Error showing all trains:', error);
        }
    },

    // Draw a track on the map with stations
    drawTrack: function(latlngs, stationNames, stations) {
        try {
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
        } catch (error) {
            console.error('Error drawing track:', error);
        }
    },

    // Draw a track on the map with stations and delay-based coloring
    drawTrackWithDelay: function(coordinatesWithDelay, stationNames, stations) {
        try {
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
        } catch (error) {
            console.error('Error drawing track with delay:', error);
        }
    },

    // Create segments of consecutive coordinates with the same delay
    createDelaySegments: function(coordinatesWithDelay) {
        try {
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
        } catch (error) {
            console.error('Error creating delay segments:', error);
            return [];
        }
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
        try {
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
        } catch (error) {
            console.error('Error adding delay legend:', error);
        }
    },

    // Add a speed legend showing speed color coding
    addSpeedLegend: function() {
        try {
            if (!this.map) return;
            
            // Remove existing legend if present
            if (window._speedLegend) {
                this.map.removeControl(window._speedLegend);
            }
            
            var SpeedLegend = L.Control.extend({
                options: {
                    position: 'bottomleft'
                },
                onAdd: function() {
                    var container = L.DomUtil.create('div', 'speed-legend');
                    container.innerHTML = `
                        <div style="background: rgba(0,0,0,0.8); color: white; padding: 10px; border-radius: 6px; font-size: 12px; min-width: 140px;">
                            <div style="font-weight: bold; margin-bottom: 8px; font-size: 13px;">Train Speeds</div>
                            <div style="display: flex; align-items: center; margin: 4px 0; gap: 8px;">
                                <div style="width: 12px; height: 12px; background: #ff6b6b; border-radius: 50%; box-shadow: 0 0 6px #ff6b6b;"></div>
                                <span>Slow (&lt;50 km/h)</span>
                            </div>
                            <div style="display: flex; align-items: center; margin: 4px 0; gap: 8px;">
                                <div style="width: 12px; height: 12px; background: #ffd93d; border-radius: 50%; box-shadow: 0 0 6px #ffd93d;"></div>
                                <span>Moderate (50-100 km/h)</span>
                            </div>
                            <div style="display: flex; align-items: center; margin: 4px 0; gap: 8px;">
                                <div style="width: 12px; height: 12px; background: #6bcf7f; border-radius: 50%; box-shadow: 0 0 6px #6bcf7f;"></div>
                                <span>Fast (100-160 km/h)</span>
                            </div>
                            <div style="display: flex; align-items: center; margin: 4px 0; gap: 8px;">
                                <div style="width: 12px; height: 12px; background: #4dabf7; border-radius: 50%; box-shadow: 0 0 6px #4dabf7;"></div>
                                <span>High-Speed (160+ km/h)</span>
                            </div>
                            <div style="display: flex; align-items: center; margin: 4px 0; gap: 8px;">
                                <div style="width: 12px; height: 12px; background: #868e96; border-radius: 50%; box-shadow: 0 0 6px #868e96;"></div>
                                <span>Unknown</span>
                            </div>
                        </div>
                    `;
                    return container;
                }
            });
            
            window._speedLegend = new SpeedLegend();
            this.map.addControl(window._speedLegend);
        } catch (error) {
            console.error('Error adding speed legend:', error);
        }
    },

    // Clear all legends
    clearLegends: function() {
        try {
            // Clear delay legend
            if (window._delayLegend) {
                this.map.removeControl(window._delayLegend);
                window._delayLegend = null;
            }
            
            // Clear speed legend
            if (window._speedLegend) {
                this.map.removeControl(window._speedLegend);
                window._speedLegend = null;
            }
        } catch (error) {
            console.error('Error clearing legends:', error);
        }
    },

    // Clear the track from the map
    clearTrack: function() {
        try {
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
            
            // Clear all legends
            this.clearLegends();
        } catch (error) {
            console.error('Error clearing track:', error);
        }
    },

    // Clear all markers from the map with proper cleanup
    clearMarkers: function() {
        try {
            // Clean up old marker system
            if (window._markers) {
                for (var i = 0; i < window._markers.length; i++) {
                    var marker = window._markers[i];
                    // Cancel any ongoing animations
                    if (marker._animationId) {
                        cancelAnimationFrame(marker._animationId);
                    }
                    this.map.removeLayer(marker);
                }
                window._markers = [];
            }
            
            // Clean up smooth animation marker system
            if (window._trainMarkersMap) {
                for (let [trainNumber, marker] of window._trainMarkersMap) {
                    // Cancel any ongoing animations
                    if (marker._animationId) {
                        cancelAnimationFrame(marker._animationId);
                    }
                    this.map.removeLayer(marker);
                }
                window._trainMarkersMap.clear();
            }
            
            // Clear dotNet references
            if (window._dotNetRefs) {
                window._dotNetRefs.clear();
            }
        } catch (error) {
            console.error('Error clearing markers:', error);
        }
    },
    
    // Show loading indicator on the map
    showLoadingIndicator: function(show) {
        try {
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
                            container.innerHTML = `
                                <div style="background: rgba(0,0,0,0.8); color: white; padding: 15px; border-radius: 8px; 
                                            box-shadow: 0 4px 12px rgba(0,0,0,0.3); display: flex; align-items: center; gap: 10px;
                                            font-size: 14px; font-weight: 500; min-width: 200px;">
                                    <div style="width: 20px; height: 20px; border: 3px solid #ffffff40; 
                                                border-top: 3px solid #ffffff; border-radius: 50%; 
                                                animation: spin 1s linear infinite;"></div>
                                    <span>Loading train details...</span>
                                </div>
                                <style>
                                    @keyframes spin {
                                        0% { transform: rotate(0deg); }
                                        100% { transform: rotate(360deg); }
                                    }
                                </style>
                            `;
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
        } catch (error) {
            console.error('Error showing/hiding loading indicator:', error);
        }
    }
};
