# Running AndroidTreeView In JetBrains Rider

AndroidTreeView does not require administrator privileges by default. The app manifest uses:

```xml
<requestedExecutionLevel level="asInvoker" uiAccess="false" />
```

Run Rider normally unless you are deliberately testing an elevated Windows scenario.

## Open The Solution

Open:

```text
D:\AndroidTreeView\AndroidTreeView.sln
```

Rider should discover the shared `.run/` configurations after the solution loads.

## Run Or Debug

Use either project configuration:

- `AndroidTreeView.App` for the full desktop app.
- `AndroidTreeView.Mini` for the always-on companion app.

Both target `.NET 10` and can be debugged from Rider with normal breakpoints.

If ADB is not found, the app shows the ADB setup flow. Install Android platform-tools, add `adb.exe` to `PATH`, or choose `adb.exe` from the app.

## Run Tests

Use Rider's test explorer or the CLI:

```powershell
dotnet test AndroidTreeView.sln --no-restore
```

The current expected baseline is 265 passing tests.

## CLI Equivalents

```powershell
dotnet restore AndroidTreeView.sln
dotnet build AndroidTreeView.sln
dotnet build src/AndroidTreeView.App/AndroidTreeView.App.csproj --no-restore
dotnet build src/AndroidTreeView.Mini/AndroidTreeView.Mini.csproj --no-restore
dotnet test AndroidTreeView.sln --no-restore
dotnet run --project src/AndroidTreeView.App
dotnet run --project src/AndroidTreeView.Mini
```

Avalonia build tasks may write diagnostic files under the user's local AppData folder. That does not mean the app itself requires administrator privileges.

## Packaging From Rider

Use the terminal inside Rider:

```powershell
./packaging/build-update-zip.ps1 -Product App -Arch x64
./packaging/build-update-zip.ps1 -Product Mini -Arch x64
```

For release packaging, build the x64 App and Mini artifacts. See [packaging.md](./packaging.md).
