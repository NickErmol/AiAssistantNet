# AIHelperNET — Phase 1: Solution Skeleton

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` or `superpowers:executing-plans` to implement this plan task-by-task.

**Goal:** Create the full solution structure — projects, references, Directory.Build.props, .gitignore — so the build is green and NetArchTest architecture rules are wired (but not yet passing on missing layers).

**Architecture:** 4-project onion (Domain → Application → Infrastructure → App) + 3 test projects. Domain and Application target `net10.0`; Infrastructure and App target `net10.0-windows`.

**Tech Stack:** .NET 10, WPF, xUnit, NetArchTest.Rules 1.x, FluentAssertions 7.x

---

### Task 1: Git init + .gitignore

**Files:**
- Create: `.gitignore`

- [ ] **Step 1: Init repo and add .gitignore**

```powershell
cd D:\work\AIHelperNET
git init
```

Create `.gitignore` with this content:

```
bin/
obj/
*.user
.vs/
*.suo
.idea/
*.DS_Store
%LOCALAPPDATA%/
```

- [ ] **Step 2: Commit**

```powershell
git add .gitignore
git commit -m "chore: init repo"
```

---

### Task 2: Directory.Build.props

**Files:**
- Create: `Directory.Build.props`

- [ ] **Step 1: Create Directory.Build.props**

```xml
<Project>
  <PropertyGroup>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnableNETAnalyzers>true</EnableNETAnalyzers>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>
</Project>
```

Note: `TargetFramework` is NOT set here — each project sets its own.

- [ ] **Step 2: Commit**

```powershell
git add Directory.Build.props
git commit -m "chore: add Directory.Build.props"
```

---

### Task 3: Create all projects and solution

**Files:**
- Create: `AIHelperNET.sln`
- Create: `src/AIHelperNET.Domain/AIHelperNET.Domain.csproj`
- Create: `src/AIHelperNET.Application/AIHelperNET.Application.csproj`
- Create: `src/AIHelperNET.Infrastructure/AIHelperNET.Infrastructure.csproj`
- Create: `src/AIHelperNET.App/AIHelperNET.App.csproj`
- Create: `tests/AIHelperNET.Domain.Tests/AIHelperNET.Domain.Tests.csproj`
- Create: `tests/AIHelperNET.Application.Tests/AIHelperNET.Application.Tests.csproj`
- Create: `tests/AIHelperNET.Integration.Tests/AIHelperNET.Integration.Tests.csproj`

- [ ] **Step 1: Scaffold the solution**

```powershell
dotnet new sln -n AIHelperNET
```

- [ ] **Step 2: Create src projects**

```powershell
dotnet new classlib -n AIHelperNET.Domain -o src/AIHelperNET.Domain -f net10.0
dotnet new classlib -n AIHelperNET.Application -o src/AIHelperNET.Application -f net10.0
dotnet new classlib -n AIHelperNET.Infrastructure -o src/AIHelperNET.Infrastructure -f net10.0-windows
dotnet new wpf -n AIHelperNET.App -o src/AIHelperNET.App -f net10.0-windows
```

- [ ] **Step 3: Create test projects**

```powershell
dotnet new xunit -n AIHelperNET.Domain.Tests -o tests/AIHelperNET.Domain.Tests -f net10.0
dotnet new xunit -n AIHelperNET.Application.Tests -o tests/AIHelperNET.Application.Tests -f net10.0
dotnet new xunit -n AIHelperNET.Integration.Tests -o tests/AIHelperNET.Integration.Tests -f net10.0-windows
```

- [ ] **Step 4: Add all projects to solution**

```powershell
dotnet sln add src/AIHelperNET.Domain/AIHelperNET.Domain.csproj
dotnet sln add src/AIHelperNET.Application/AIHelperNET.Application.csproj
dotnet sln add src/AIHelperNET.Infrastructure/AIHelperNET.Infrastructure.csproj
dotnet sln add src/AIHelperNET.App/AIHelperNET.App.csproj
dotnet sln add tests/AIHelperNET.Domain.Tests/AIHelperNET.Domain.Tests.csproj
dotnet sln add tests/AIHelperNET.Application.Tests/AIHelperNET.Application.Tests.csproj
dotnet sln add tests/AIHelperNET.Integration.Tests/AIHelperNET.Integration.Tests.csproj
```

- [ ] **Step 5: Wire project references**

```powershell
# Application depends on Domain
dotnet add src/AIHelperNET.Application/AIHelperNET.Application.csproj reference src/AIHelperNET.Domain/AIHelperNET.Domain.csproj

# Infrastructure depends on Application + Domain
dotnet add src/AIHelperNET.Infrastructure/AIHelperNET.Infrastructure.csproj reference src/AIHelperNET.Application/AIHelperNET.Application.csproj
dotnet add src/AIHelperNET.Infrastructure/AIHelperNET.Infrastructure.csproj reference src/AIHelperNET.Domain/AIHelperNET.Domain.csproj

# App depends on Application + Domain + Infrastructure (composition root only)
dotnet add src/AIHelperNET.App/AIHelperNET.App.csproj reference src/AIHelperNET.Application/AIHelperNET.Application.csproj
dotnet add src/AIHelperNET.App/AIHelperNET.App.csproj reference src/AIHelperNET.Domain/AIHelperNET.Domain.csproj
dotnet add src/AIHelperNET.App/AIHelperNET.App.csproj reference src/AIHelperNET.Infrastructure/AIHelperNET.Infrastructure.csproj

# Test references
dotnet add tests/AIHelperNET.Domain.Tests/AIHelperNET.Domain.Tests.csproj reference src/AIHelperNET.Domain/AIHelperNET.Domain.csproj
dotnet add tests/AIHelperNET.Application.Tests/AIHelperNET.Application.Tests.csproj reference src/AIHelperNET.Application/AIHelperNET.Application.csproj
dotnet add tests/AIHelperNET.Application.Tests/AIHelperNET.Application.Tests.csproj reference src/AIHelperNET.Domain/AIHelperNET.Domain.csproj
dotnet add tests/AIHelperNET.Integration.Tests/AIHelperNET.Integration.Tests.csproj reference src/AIHelperNET.Application/AIHelperNET.Application.csproj
dotnet add tests/AIHelperNET.Integration.Tests/AIHelperNET.Integration.Tests.csproj reference src/AIHelperNET.Domain/AIHelperNET.Domain.csproj
dotnet add tests/AIHelperNET.Integration.Tests/AIHelperNET.Integration.Tests.csproj reference src/AIHelperNET.Infrastructure/AIHelperNET.Infrastructure.csproj
```

- [ ] **Step 6: Fix up per-project csproj settings**

Edit `src/AIHelperNET.Domain/AIHelperNET.Domain.csproj` — replace generated content:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
</Project>
```

Edit `src/AIHelperNET.Application/AIHelperNET.Application.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\AIHelperNET.Domain\AIHelperNET.Domain.csproj" />
  </ItemGroup>
</Project>
```

Edit `src/AIHelperNET.Infrastructure/AIHelperNET.Infrastructure.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\AIHelperNET.Application\AIHelperNET.Application.csproj" />
    <ProjectReference Include="..\AIHelperNET.Domain\AIHelperNET.Domain.csproj" />
  </ItemGroup>
</Project>
```

Edit `src/AIHelperNET.App/AIHelperNET.App.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <OutputType>WinExe</OutputType>
    <RootNamespace>AIHelperNET.App</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\AIHelperNET.Application\AIHelperNET.Application.csproj" />
    <ProjectReference Include="..\AIHelperNET.Domain\AIHelperNET.Domain.csproj" />
    <ProjectReference Include="..\AIHelperNET.Infrastructure\AIHelperNET.Infrastructure.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 7: Delete generated boilerplate stubs**

```powershell
Remove-Item src/AIHelperNET.Domain/Class1.cs -ErrorAction SilentlyContinue
Remove-Item src/AIHelperNET.Application/Class1.cs -ErrorAction SilentlyContinue
Remove-Item src/AIHelperNET.Infrastructure/Class1.cs -ErrorAction SilentlyContinue
Remove-Item tests/AIHelperNET.Domain.Tests/UnitTest1.cs -ErrorAction SilentlyContinue
Remove-Item tests/AIHelperNET.Application.Tests/UnitTest1.cs -ErrorAction SilentlyContinue
Remove-Item tests/AIHelperNET.Integration.Tests/UnitTest1.cs -ErrorAction SilentlyContinue
```

- [ ] **Step 8: Verify build is green**

```powershell
dotnet build AIHelperNET.sln
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 9: Commit**

```powershell
git add -A
git commit -m "chore: scaffold solution with all projects and references"
```

---

### Task 4: Add NuGet packages

**Files:** All .csproj files

- [ ] **Step 1: Application packages**

```powershell
dotnet add src/AIHelperNET.Application/AIHelperNET.Application.csproj package Mediator.SourceGenerator --version 3.*
dotnet add src/AIHelperNET.Application/AIHelperNET.Application.csproj package Mediator.Abstractions --version 3.*
dotnet add src/AIHelperNET.Application/AIHelperNET.Application.csproj package FluentResults --version 3.*
dotnet add src/AIHelperNET.Application/AIHelperNET.Application.csproj package FluentValidation --version 12.*
dotnet add src/AIHelperNET.Application/AIHelperNET.Application.csproj package Riok.Mapperly --version 4.*
dotnet add src/AIHelperNET.Application/AIHelperNET.Application.csproj package Microsoft.Extensions.Logging.Abstractions
dotnet add src/AIHelperNET.Application/AIHelperNET.Application.csproj package Microsoft.Extensions.DependencyInjection.Abstractions
```

- [ ] **Step 2: Infrastructure packages**

```powershell
dotnet add src/AIHelperNET.Infrastructure/AIHelperNET.Infrastructure.csproj package FluentResults --version 3.*
dotnet add src/AIHelperNET.Infrastructure/AIHelperNET.Infrastructure.csproj package NAudio --version 2.*
dotnet add src/AIHelperNET.Infrastructure/AIHelperNET.Infrastructure.csproj package Whisper.net --version 1.*
dotnet add src/AIHelperNET.Infrastructure/AIHelperNET.Infrastructure.csproj package Whisper.net.Runtime.Cuda --version 1.*
dotnet add src/AIHelperNET.Infrastructure/AIHelperNET.Infrastructure.csproj package OllamaSharp
dotnet add src/AIHelperNET.Infrastructure/AIHelperNET.Infrastructure.csproj package Microsoft.EntityFrameworkCore.Sqlite --version 10.*
dotnet add src/AIHelperNET.Infrastructure/AIHelperNET.Infrastructure.csproj package Microsoft.EntityFrameworkCore.Design --version 10.*
dotnet add src/AIHelperNET.Infrastructure/AIHelperNET.Infrastructure.csproj package AdysTech.CredentialManager
dotnet add src/AIHelperNET.Infrastructure/AIHelperNET.Infrastructure.csproj package Microsoft.Extensions.Options
dotnet add src/AIHelperNET.Infrastructure/AIHelperNET.Infrastructure.csproj package Microsoft.Extensions.Http
dotnet add src/AIHelperNET.Infrastructure/AIHelperNET.Infrastructure.csproj package Microsoft.Extensions.DependencyInjection.Abstractions
```

- [ ] **Step 3: App packages**

```powershell
dotnet add src/AIHelperNET.App/AIHelperNET.App.csproj package CommunityToolkit.Mvvm --version 8.*
dotnet add src/AIHelperNET.App/AIHelperNET.App.csproj package Microsoft.Extensions.Hosting
dotnet add src/AIHelperNET.App/AIHelperNET.App.csproj package Serilog.Extensions.Hosting --version 9.*
dotnet add src/AIHelperNET.App/AIHelperNET.App.csproj package Serilog.Sinks.File --version 6.*
dotnet add src/AIHelperNET.App/AIHelperNET.App.csproj package FluentResults --version 3.*
```

- [ ] **Step 4: Test packages**

```powershell
# Domain.Tests
dotnet add tests/AIHelperNET.Domain.Tests/AIHelperNET.Domain.Tests.csproj package FluentAssertions --version 7.*
dotnet add tests/AIHelperNET.Domain.Tests/AIHelperNET.Domain.Tests.csproj package xunit --version 2.*

# Application.Tests
dotnet add tests/AIHelperNET.Application.Tests/AIHelperNET.Application.Tests.csproj package FluentAssertions --version 7.*
dotnet add tests/AIHelperNET.Application.Tests/AIHelperNET.Application.Tests.csproj package NSubstitute --version 5.*
dotnet add tests/AIHelperNET.Application.Tests/AIHelperNET.Application.Tests.csproj package FluentResults --version 3.*
dotnet add tests/AIHelperNET.Application.Tests/AIHelperNET.Application.Tests.csproj package Microsoft.Extensions.Time.Testing

# Integration.Tests
dotnet add tests/AIHelperNET.Integration.Tests/AIHelperNET.Integration.Tests.csproj package FluentAssertions --version 7.*
dotnet add tests/AIHelperNET.Integration.Tests/AIHelperNET.Integration.Tests.csproj package NetArchTest.Rules --version 1.*
dotnet add tests/AIHelperNET.Integration.Tests/AIHelperNET.Integration.Tests.csproj package Microsoft.EntityFrameworkCore.Sqlite --version 10.*
dotnet add tests/AIHelperNET.Integration.Tests/AIHelperNET.Integration.Tests.csproj package FluentResults --version 3.*
```

- [ ] **Step 5: Verify build still green**

```powershell
dotnet build AIHelperNET.sln
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Commit**

```powershell
git add -A
git commit -m "chore: add NuGet packages to all projects"
```

---

### Task 5: Wire NetArchTest architecture guard (red — no types yet)

**Files:**
- Create: `tests/AIHelperNET.Integration.Tests/Architecture/ArchitectureTests.cs`

- [ ] **Step 1: Create architecture test file**

```csharp
// tests/AIHelperNET.Integration.Tests/Architecture/ArchitectureTests.cs
using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace AIHelperNET.Integration.Tests.Architecture;

public class ArchitectureTests
{
    // These will be populated once Domain/Application types exist.
    // For now they compile and serve as living documentation of the rules.

    [Fact]
    public void Domain_ShouldNotDependOnApplication()
    {
        // Skipped until Domain assembly has types.
        // Replace `Skip` with real assertion in Phase 2.
        Assert.True(true, "Placeholder — implement after Domain types exist.");
    }

    [Fact]
    public void Domain_ShouldNotDependOnInfrastructure()
    {
        Assert.True(true, "Placeholder — implement after Domain types exist.");
    }

    [Fact]
    public void Application_ShouldNotDependOnInfrastructure()
    {
        Assert.True(true, "Placeholder — implement after Application types exist.");
    }
}
```

- [ ] **Step 2: Run tests — should pass (placeholders)**

```powershell
dotnet test tests/AIHelperNET.Integration.Tests/AIHelperNET.Integration.Tests.csproj --logger "console;verbosity=normal"
```

Expected: 3 tests pass.

- [ ] **Step 3: Commit**

```powershell
git add tests/AIHelperNET.Integration.Tests/Architecture/ArchitectureTests.cs
git commit -m "test: add architecture guard placeholders (NetArchTest)"
```

---

**Phase 1 complete.** Continue with `2026-06-02-aihelper-phase2-domain.md`.
