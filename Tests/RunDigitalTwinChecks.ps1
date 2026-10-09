param(
    [Parameter(Mandatory = $true)] [string] $AppPath,
    [string] $CompilerPath = "$env:LOCALAPPDATA/Temp/scara-audit-compiler-4.8.0/tasks/net472/csc.exe",
    [string] $ReferencePath = "$env:LOCALAPPDATA/Temp/scara-audit-framework-4.7.2/build/.NETFramework/v4.7.2",
    [string] $OutputPath = (Join-Path $env:TEMP 'scara-digital-twin-checks')
)

# Runs disconnected, synthetic-feedback UI checks. It never connects to a robot.
$ErrorActionPreference = 'Stop'
$taskApp = (Resolve-Path -LiteralPath $AppPath).Path
$taskCompiler = (Resolve-Path -LiteralPath $CompilerPath).Path
$taskRefs = (Resolve-Path -LiteralPath $ReferencePath).Path
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskOutput = [System.IO.Path]::GetFullPath($OutputPath)
$taskAppFolder = Split-Path -Parent $taskApp
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null

# Keep generated executables and captures outside the source checkout by default.
$taskLocalApp = Join-Path $taskOutput ([System.IO.Path]::GetFileName($taskApp))
if ($taskLocalApp -ne $taskApp) { Copy-Item -LiteralPath $taskApp -Destination $taskLocalApp -Force }
foreach ($taskDependency in @('TsRemoteLib.dll', 'TsRemoteLib.dll.config', 'Logger.dll', 'log4net.dll', 'FPT UNIVERSITY.png', 'DigitalTwinCalibration.xml')) {
    $taskSource = Join-Path $taskAppFolder $taskDependency
    if (!(Test-Path -LiteralPath $taskSource)) { $taskSource = Join-Path (Join-Path $taskRoot 'Libs') $taskDependency }
    if (!(Test-Path -LiteralPath $taskSource)) { $taskSource = Join-Path $taskRoot $taskDependency }
    if ((Test-Path -LiteralPath $taskSource) -and $taskSource -ne (Join-Path $taskOutput $taskDependency)) {
        Copy-Item -LiteralPath $taskSource -Destination $taskOutput -Force
    }
}
$taskAssets = Join-Path $taskAppFolder 'Assets'
if (!(Test-Path -LiteralPath $taskAssets)) { $taskAssets = Join-Path $taskRoot 'Assets' }
if ((Test-Path -LiteralPath $taskAssets) -and $taskAssets -ne (Join-Path $taskOutput 'Assets')) {
    Copy-Item -LiteralPath $taskAssets -Destination $taskOutput -Recurse -Force
}

$taskExe = Join-Path $taskOutput 'DigitalTwinSmokeTests.exe'
$taskArgs = @('/nologo', '/noconfig', '/nostdlib+', '/target:exe', '/platform:x86', '/langversion:7.3', "/out:$taskExe", "/reference:$taskLocalApp", "/reference:$(Join-Path $taskOutput 'TsRemoteLib.dll')")
foreach ($taskAssembly in @('mscorlib', 'System', 'System.Core', 'System.Drawing', 'System.Windows.Forms', 'WindowsBase', 'PresentationCore', 'PresentationFramework', 'System.Xaml', 'WindowsFormsIntegration')) {
    $taskArgs += "/reference:$(Join-Path $taskRefs ($taskAssembly + '.dll'))"
}
$taskArgs += Join-Path $PSScriptRoot 'DigitalTwinSmokeTests.cs'
& $taskCompiler @taskArgs
if ($LASTEXITCODE -ne 0) { throw "Smoke-test compilation failed with exit code $LASTEXITCODE." }

Push-Location $taskOutput
try {
    & $taskExe $taskOutput
    if ($LASTEXITCODE -ne 0) { throw "Digital-twin checks failed with exit code $LASTEXITCODE." }
} finally { Pop-Location }
Write-Output "Digital-twin screenshots: $taskOutput"
