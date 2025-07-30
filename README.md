# LiveTrains

vibe coded app for real-time train tracking 🚄

## How It Works

**LiveTrains** is a cross-platform .NET MAUI app that visualizes real-time train movement and details using a modern Blazor UI and interactive map powered by Leaflet.js. Here’s an overview of its architecture and main features:

### Architecture

- **.NET MAUI + Blazor Hybrid:**  
  The app uses .NET MAUI for native cross-platform deployment (Android, iOS, Mac Catalyst, Windows) and Blazor for the UI, enabling rich, interactive web-like experiences in a native shell.

- **Component-Based UI:**  
  The UI is built from reusable Razor components. For example, `TrainDetailsBox.razor` displays detailed information about a selected train, including its route, stations, delays, and carrier.

- **Map Integration:**  
  The interactive map is rendered using Leaflet.js, with a custom JavaScript interop layer (`wwwroot/js/leaflet-interop.js`) that communicates with Blazor components. This enables:
  - Real-time train marker updates and smooth animations based on speed.
  - Track drawing with delay-based color coding.
  - Station markers with popups for schedule and delay info.
  - Legends for speed and delay categories.

- **Data Flow:**  
  - **Services:**  
    C# services (e.g., `LiveTrainTrackingService`, `TrainDetailsService`) fetch and manage live train data, which is injected into Blazor components via dependency injection.
  - **Model Classes:**  
    Data is structured using models like `TrainDetails` and `TrainStation`, encapsulating all relevant info (number, type, carrier, stations, delays, etc.).
  - **UI Updates:**  
    When train data updates, Blazor components re-render and call JavaScript interop methods to update the map in real time.

### Data Source: Portal Pasażera Endpoints

- **Unofficial Data Usage:**  
  This app retrieves live train and route data by consuming endpoints from [Portal Pasażera](https://portalpasazera.pl/).  
  **Important:** These endpoints are not official APIs and are not documented.  
  All data integration is based on reverse engineering and observation of network traffic from the Portal Pasażera website.  
  As a result, endpoint structure and data formats may change at any time, and stability is not guaranteed.

### Key Features

- **Live Train Visualization:**  
  Trains are shown as animated markers, with icons and color-coded speed indicators. Selecting a train highlights it and shows its details.

- **Delay & Speed Legends:**  
  The map displays legends explaining the color codes for train speeds and track delays, making it easy to interpret real-time conditions.

- **Detailed Route & Station Info:**  
  The `TrainDetailsBox` component shows the full route, all stations (with expandable/collapsible lists), platform info, scheduled/actual times, and any delays or alerts.

- **Smooth User Experience:**  
  - Map view auto-adjusts to show relevant trains and tracks.
  - Loading indicators and error handling are built in for seamless updates.

### Extensibility

- **Carrier Icons:**  
  Carrier logos are dynamically selected based on train type.
- **Custom Data Sources:**  
  The backend services can be extended to support different APIs or data feeds.
- **UI Customization:**  
  Styles and components can be easily modified for branding or feature expansion.

---

**Contributions welcome!**  
If you have ideas, improvements, or want to help expand the project, feel free to open a pull request. Any help is appreciated!

This app is designed for clarity, performance, and a modern user experience, making real-time train tracking both informative and visually engaging.
