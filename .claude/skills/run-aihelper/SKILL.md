---
description: Launch the AIHelperNET WPF overlay app (stop any running instance, build, run)
---

# Run AIHelperNET

Follow these steps exactly.

## 1 — Stop any running instance

```powershell
Get-Process -Name "AIHelperNET.App" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 800
```

## 2 — Build

```powershell
cd D:\work\AIHelperNET
dotnet build src/AIHelperNET.App/AIHelperNET.App.csproj -c Debug
```

If the build fails, report the errors and stop — do not attempt to launch.

## 3 — Launch

```powershell
Start-Process "D:\work\AIHelperNET\src\AIHelperNET.App\bin\Debug\net10.0-windows10.0.17763.0\AIHelperNET.App.exe"
```

## 4 — Report

Tell the user:
- Build succeeded / failed (with errors if failed)
- App launched — they should see the overlay window on screen
- Remind them: click 👁/🎥 to toggle stealth if the window looks blank in a screen recording
