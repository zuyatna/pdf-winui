param([Parameter(Mandatory)][string]$Executable, [Parameter(Mandatory)][string]$Label, [string]$CollectorPath = 'artifacts/reading-performance/collector/Collector.dll', [string]$Fixture = 'artifacts/optimization/reading-benchmark.pdf')
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
$outputPath = Join-Path $PWD ('artifacts/reading-performance/runs/' + $Label)
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
    [IO.File]::WriteAllText($settingsPath,'{"ReadingTheme":1,"RecentDocuments":[],"BookmarkedDocuments":[]}')
    Mark 'startup' 'Launch existing single-file binary with the synthetic 120-page text PDF; warm palette, default view state.'
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
    Mark 'idle-opened' 'Document ready; no input for 12 seconds.'
    Start-Sleep -Seconds 12
    Capture 'opened'
    Invoke-Element (Find-Id ZoomOptionsButton)
    Start-Sleep -Milliseconds 400
    Invoke-Element (Find-Name 'Actual size (100%)')
    Start-Sleep -Milliseconds 400
    $zoomRect=(Find-Id ZoomOptionsButton).Current.BoundingRectangle
    $buttons=$window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::Button))
    $zoomIn=$null
    foreach($button in $buttons) {
        $rect=$button.Current.BoundingRectangle
        if($rect.Left -ge $zoomRect.Right-1 -and [Math]::Abs($rect.Top-$zoomRect.Top) -lt 12 -and $rect.Left -lt $zoomRect.Right+20) {$zoomIn=$button;break}
    }
    if($null -eq $zoomIn){throw 'Zoom in control missing'}
    Mark 'zoom-150' 'Reset to 100%, then invoke zoom in five times.'
    for($i=0;$i -lt 5;$i++){Invoke-Element $zoomIn;Start-Sleep -Milliseconds 100}
    Start-Sleep -Seconds 5
    Capture 'zoom-150'
    $list=Find-Id PagesListView
    $scroll=$list.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
    function Scroll-To([double]$percent, [int]$dwell) {
        $started=[Diagnostics.Stopwatch]::GetTimestamp()
        $scroll.SetScrollPercent([System.Windows.Automation.ScrollPattern]::NoScroll,$percent)
        $returned=[Diagnostics.Stopwatch]::GetTimestamp()
        $activity.WriteLine(([pscustomobject]@{Kind='scroll';Phase=$script:currentPhase;Ticks=$started;ReturnedTicks=$returned;Target=$percent} | ConvertTo-Json -Compress));$activity.Flush()
        Start-Sleep -Milliseconds $dwell
    }
    Mark 'smooth-scroll' 'Two passes: 0 to 10 percent and back, 0.1 percentage point steps with 20 ms dwell.'
    for($pass=0;$pass -lt 2;$pass++) {
        foreach($step in @((1..100)+(99..0))) {Scroll-To ($step/10.0) 20}
    }
    Mark 'fast-scroll' '0 to 50 percent and back, 2 percentage point steps with 180 ms dwell.'
    foreach($step in @((1..25)+(24..0))) {Scroll-To ($step*2.0) 180}
    Mark 'navigation' 'Page requests with 650 ms minimum dwell.'
    foreach($page in @(5,10,15,20,30,40,50,60,30,10,1)){Go-Page $page}
    Mark 'zoom-response' 'Zoom from 150 to 200 percent, 350 ms dwell between increments.'
    for($i=1;$i -le 5;$i++) {
        $started=[Diagnostics.Stopwatch]::GetTimestamp()
        Invoke-Element $zoomIn
        $returned=[Diagnostics.Stopwatch]::GetTimestamp()
        $activity.WriteLine(([pscustomobject]@{Kind='zoom';Phase=$script:currentPhase;Ticks=$started;ReturnedTicks=$returned;Target=(1.5+$i/10.0)} | ConvertTo-Json -Compress));$activity.Flush()
        Start-Sleep -Milliseconds 350
    }
    Start-Sleep -Seconds 2
    Capture 'zoom-200'
    Mark 'final-idle' 'No input for 10 seconds.'
    Start-Sleep -Seconds 10
    Capture 'final'
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
