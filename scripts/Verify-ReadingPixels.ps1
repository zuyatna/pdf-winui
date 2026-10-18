param([Parameter(Mandatory)][string]$Executable, [Parameter(Mandatory)][string]$Label, [string]$CollectorPath = 'artifacts/reading-performance/collector/Collector.dll', [string]$Fixture = 'artifacts/reading-performance/rendering-quality.pdf', [ValidateRange(0,2)][int]$Theme = 1)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ProfileCapture {
 [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr window);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out Rect rect);
 public struct Rect { public int Left, Top, Right, Bottom; }
}
'@
$outputPath = Join-Path $PWD ('artifacts/reading-performance/quality/' + $Label)
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
if (Get-ChildItem -LiteralPath $outputPath -Force) { throw 'Use a new run label: the output directory must be empty to preserve measurements and settings backups.' }
$executable = (Resolve-Path -LiteralPath $Executable).Path
$fixture = (Resolve-Path -LiteralPath $Fixture).Path
$collectorDll = (Resolve-Path -LiteralPath $CollectorPath).Path
if (Get-Process -Name PdfReader.App -ErrorAction SilentlyContinue) { throw 'A reader instance is already running; refusing to change its settings.' }
$settingsPath = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'PdfReader/settings.json'
$originalBytes = if (Test-Path -LiteralPath $settingsPath) { [IO.File]::ReadAllBytes($settingsPath) } else { $null }
if ($null -ne $originalBytes) { [IO.File]::WriteAllBytes((Join-Path $outputPath 'settings.original.json'),$originalBytes) }
else { [IO.File]::WriteAllText((Join-Path $outputPath 'settings.originally-absent'),'absent') }
New-Item -ItemType Directory -Path (Split-Path $settingsPath) -Force | Out-Null
$activity = [IO.StreamWriter]::new((Join-Path $outputPath 'activity.jsonl'))
$process = $null
$collector = $null
function Mark([string]$phase, [string]$detail) {
    $script:currentPhase=$phase
    $activity.WriteLine(([pscustomobject]@{Ticks=[Diagnostics.Stopwatch]::GetTimestamp();Utc=[DateTime]::UtcNow.ToString('o');Phase=$phase;Detail=$detail} | ConvertTo-Json -Compress))
    $activity.Flush()
    Write-Output "$phase : $detail"
}
function Find-Id([string]$id) {
    $script:window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,$id))
}
function Find-Name([string]$name) {
    $script:window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,$name))
}
function Invoke-Element($element) { $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
function Go-Page([int]$number) {
    $box = Find-Id GoToPageTextBox
    $box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($number.ToString())
    $box.SetFocus()
    $started=[Diagnostics.Stopwatch]::GetTimestamp()
    [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
    $returned=[Diagnostics.Stopwatch]::GetTimestamp()
    $activity.WriteLine(([pscustomobject]@{Kind='navigate';Phase=$script:currentPhase;Ticks=$started;ReturnedTicks=$returned;Target=$number} | ConvertTo-Json -Compress));$activity.Flush()
    Start-Sleep -Milliseconds 650
    $actual=$box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
    if ($actual -ne $number.ToString()) { throw "Navigation requested $number, but page box reports $actual" }
}
function Capture([string]$name) {
    $rect = [ProfileCapture+Rect]::new()
    [ProfileCapture]::GetWindowRect($script:process.MainWindowHandle,[ref]$rect) | Out-Null
    $bitmap = [Drawing.Bitmap]::new($rect.Right-$rect.Left,$rect.Bottom-$rect.Top)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $dc = $graphics.GetHdc()
    try { $ok=[ProfileCapture]::PrintWindow($script:process.MainWindowHandle,$dc,2) }
    finally { $graphics.ReleaseHdc($dc) }
    try { if (-not $ok) { throw 'PrintWindow failed' }; $bitmap.Save((Join-Path $outputPath "$name.png")) }
    finally { $graphics.Dispose(); $bitmap.Dispose() }
}
try {
    [IO.File]::WriteAllText($settingsPath,('{"ReadingTheme":'+$Theme+',"RecentDocuments":[],"BookmarkedDocuments":[]}'))
    Mark 'startup' "Launch the three-page quality fixture with reading theme $Theme."
    $previousProbe=$env:PDFREADER_PERF_OUTPUT
    $env:PDFREADER_PERF_OUTPUT=$outputPath
    try { $process=Start-Process -FilePath $executable -ArgumentList ('"'+$fixture+'"') -WindowStyle Hidden -PassThru } finally { $env:PDFREADER_PERF_OUTPUT=$previousProbe }
    $collector=Start-Process -FilePath 'dotnet' -ArgumentList @(('"'+$collectorDll+'"'),$process.Id,('"'+$outputPath+'"')) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $outputPath 'collector-out.txt') -RedirectStandardError (Join-Path $outputPath 'collector-error.txt')
    $deadline=(Get-Date).AddSeconds(45)
    do {
        $process.Refresh()
        if ($process.HasExited) { throw "Reader exited: $($process.ExitCode)" }
        if ($process.MainWindowHandle -ne 0) {
            $window=[System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
            $nav=Find-Id NavigationButton
        }
        $ready=$null -ne $nav -and $nav.Current.IsEnabled
        if (-not $ready) { Start-Sleep -Milliseconds 100 }
    } until ($ready -or (Get-Date) -gt $deadline)
    if (-not $ready) { throw 'Startup timeout' }
    $window.GetCurrentPattern([System.Windows.Automation.TransformPattern]::Pattern).Resize(1280,900)
    Go-Page 1
    Start-Sleep -Seconds 2
    Invoke-Element (Find-Id ZoomOptionsButton)
    Start-Sleep -Milliseconds 400
    Invoke-Element (Find-Name 'Fit page')
    Start-Sleep -Seconds 2
    foreach($number in 1..3) {
        Go-Page $number
        Start-Sleep -Seconds 1
        Mark "quality-page-$number" "Capture page $number at fit-page zoom with reading theme $Theme."
        Capture "page-$number"
    }
    $process.Refresh()
    $modules=@($process.Modules | ForEach-Object { [pscustomobject]@{Name=$_.ModuleName;Bytes=$_.ModuleMemorySize;Version=$_.FileVersionInfo.FileVersion} })
    $windowRect = [ProfileCapture+Rect]::new()
    if (-not [ProfileCapture]::GetWindowRect($process.MainWindowHandle,[ref]$windowRect)) { throw 'GetWindowRect failed' }
    [pscustomobject]@{Label=$Label;WindowWidth=($windowRect.Right-$windowRect.Left);WindowHeight=($windowRect.Bottom-$windowRect.Top);StopwatchFrequency=[Diagnostics.Stopwatch]::Frequency;Pid=$process.Id;Executable=$executable;Sha256=(Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash;FileBytes=(Get-Item $executable).Length;Dpi=[ProfileCapture]::GetDpiForWindow($process.MainWindowHandle);Fixture=$fixture;FixtureSha256=(Get-FileHash $fixture).Hash;Modules=$modules;Threads=@($process.Threads | ForEach-Object {[pscustomobject]@{Id=$_.Id;CpuSeconds=$_.TotalProcessorTime.TotalSeconds;State=$_.ThreadState.ToString()}})} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $outputPath 'metadata.json') -Encoding UTF8
    Mark 'complete' 'Workload completed; stop collection and close the instrumented test instance.'
} catch {
    Mark 'failed' $_.Exception.Message
    $_ | Out-String | Set-Content -LiteralPath (Join-Path $outputPath 'workload-error.txt')
    throw
} finally {
    try {
        try {
            [IO.File]::WriteAllText((Join-Path $outputPath 'stop'),'stop')
            if ($null -ne $collector -and -not $collector.HasExited) {
                if (-not $collector.WaitForExit(20000)) { $collector.Kill() }
            }
        } finally {
            if ($null -ne $process) {
                $process.Refresh()
                if (-not $process.HasExited) {
                    $process.CloseMainWindow() | Out-Null
                    if (-not $process.WaitForExit(5000)) { $process.Kill(); $process.WaitForExit() }
                }
            }
        }
    } finally {
        try {
            if ($null -ne $originalBytes) { [IO.File]::WriteAllBytes($settingsPath,$originalBytes) }
            elseif (Test-Path -LiteralPath $settingsPath) { Remove-Item -LiteralPath $settingsPath }
            [IO.File]::WriteAllText((Join-Path $outputPath 'settings-restored.txt'),'restored')
        } finally { $activity.Dispose() }
    }
}
