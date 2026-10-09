# Architecture & Debug Routing Map

Modular layout for the **Robot Controller** system. Use the symptom → file table to feed an LLM only the relevant lightweight module instead of the whole codebase.

## Folder tree

```text
Test_1/
├── Core/                          # Main Application logic & UI
│   ├── Models/                    # Data structures & State
│   │   ├── RobotPositionData.cs   # DTO for Joint and World coordinates
│   │   └── RobotState.cs          # Hardware state (IsConnected, ServoOn, Alarms)
│   │
│   ├── Services/                  # Business logic & hardware I/O
│   │   ├── LoggingService.cs      # Centralized Pub/Sub logger
│   │   ├── MotionService.cs       # 7-phase S-Curve trajectory math (pure logic)
│   │   └── RobotService.cs        # Robot TCP comms, Connect/Disconnect, Watchdog loop
│   │
│   ├── UI/                        # Presentation layer (Listen to Services, no direct I/O)
│   │   ├── MainForm.cs            # Event handlers and UI state updates
│   │   ├── MainForm.Designer.cs   # Auto-generated VS designer code
│   │   ├── MainForm.UI.cs         # Programmatic UI setup (InitializeModernUI)
│   │   ├── MainForm.DigitalTwin.cs # Single-flight background feedback polling + lifecycle
│   │   ├── ScaraDigitalTwinView.cs # WPF canvas, THL300 transforms, axis labels and actual WORLD/joint readouts
│   │   ├── RobotCoordinateSystem.cs # Frozen XYZ axes and tick marks in mm
│   │   ├── RobotGroundGrid.cs     # Full viewport open XY grid, projected with the camera
│   │   ├── Thl300CadModel.cs       # Embedded manufacturer CAD meshes/materials
│   │   └── MainForm.resx          # UI resources
│   │
│   └── Utilities/                 # Shared helpers
│       └── Constants.cs           # ALL magic numbers (IP, Port, S-curve params)
│
├── Libs/                          # External runtime dependencies
│   ├── TsRemoteLib.dll            # Proprietary robot communication driver
│   ├── log4net.dll                # Logger dependency
│   ├── Logger.dll                 # Logger dependency
│   └── TsRemoteLib.dll.config     
│
├── Program.cs                     # Entrypoint: Application.Run(new MainForm())
├── Assets/THL300/                 # Original IGES, embedded mesh and provenance
├── Tools/THL300/                  # Reproducible CAD conversion + integrity checks
└── Test_1.csproj                  # MSBuild project config & build targets
```

## Symptom → File Routing Table

| Symptom / Task | Relevant Files to Feed LLM |
| :--- | :--- |
| **Cannot connect, connection drops, or Watchdog timeouts** | `RobotService.cs`, `RobotState.cs` |
| **S-Curve movement is wrong, buggy math, or chart glitch** | `MotionService.cs` |
| **Need to change default IP, Port, or default Speeds** | `Constants.cs` |
| **Buttons don't update color, or UI event not firing** | `MainForm.cs`, `RobotService.cs` |
| **Need to add a new UI control (TextBox, Chart, etc.)** | `MainForm.UI.cs`, `MainForm.cs` |
| **3D twin is stale, frozen, or receives an old connection sample** | `MainForm.DigitalTwin.cs`, `RobotService.cs` |
| **3D geometry, joint direction/zero, camera, or calibration is wrong** | `ScaraDigitalTwinView.cs`, `Thl300CadModel.cs`, `Assets/THL300/THL300.metadata.json` |
| **Log messages are missing or format needs change** | `LoggingService.cs` |
| **Adding a new data field to the robot state (e.g. Temp)** | `RobotState.cs`, `RobotService.cs` |
