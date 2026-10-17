param([Parameter(Mandatory)][string]$Executable, [Parameter(Mandatory)][string]$Label)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
if (-not ('ReaderCapture' -as [type])) {
    Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class ReaderCapture {
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out Rect rect);
    public struct Rect { public int Left, Top, Right, Bottom; }
}
"@
}
if (Get-Process -Name PdfReader.App -ErrorAction SilentlyContinue) { throw 'Close other reader instances before measuring.' }
$fixture = (Resolve-Path artifacts/optimization/reading-benchmark.pdf).Path
$settingsPath = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'PdfReader/settings.json'
$settingsBytes = if (Test-Path -LiteralPath $settingsPath) { [System.IO.File]::ReadAllBytes($settingsPath) } else { $null }
$samples = [System.Collections.Generic.List[object]]::new()
function Find-Id([string]$id) {
    $script:readerWindow.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id))
}
function Sample([string]$phase) {
    $script:process.Refresh()
    $samples.Add([pscustomobject]@{Phase=$phase;PrivateMiB=[math]::Round($script:process.PrivateMemorySize64/1MB,2);WorkingSetMiB=[math]::Round($script:process.WorkingSet64/1MB,2);CpuSeconds=$script:process.TotalProcessorTime.TotalSeconds})
}
function Capture([string]$phase) {
    $rect = [ReaderCapture+Rect]::new()
    [ReaderCapture]::GetWindowRect($script:process.MainWindowHandle,[ref]$rect) | Out-Null
    $bitmap = [System.Drawing.Bitmap]::new($rect.Right-$rect.Left,$rect.Bottom-$rect.Top)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $dc = $graphics.GetHdc()
    try { [ReaderCapture]::PrintWindow($script:process.MainWindowHandle,$dc,2) | Out-Null }
    finally { $graphics.ReleaseHdc($dc) }
    try { $bitmap.Save((Join-Path $PWD "artifacts/optimization/$Label-$phase.png")) }
    finally { $graphics.Dispose(); $bitmap.Dispose() }
}
$clock = [System.Diagnostics.Stopwatch]::StartNew()
$process = Start-Process -FilePath (Resolve-Path -LiteralPath $Executable).Path -ArgumentList ('"'+$fixture+'"') -WindowStyle Hidden -PassThru
try {
    $deadline=(Get-Date).AddSeconds(35)
    do {
        $process.Refresh()
        if ($process.HasExited) { throw "Application exited: $($process.ExitCode)" }
        if ($process.MainWindowHandle -ne 0) {
            $readerWindow=[System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
            $nav = Find-Id NavigationButton
        }
        $ready=$null -ne $nav -and $nav.Current.IsEnabled
        if (-not $ready) { Start-Sleep -Milliseconds 100 }
    } until ($ready -or (Get-Date) -gt $deadline)
    if (-not $ready) { throw 'Startup timed out' }
    $startupMs=$clock.ElapsedMilliseconds
    $readerWindow.GetCurrentPattern([System.Windows.Automation.TransformPattern]::Pattern).Resize(1280,900)
    $firstPageBox=Find-Id GoToPageTextBox
    $firstPageBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('1')
    $firstPageBox.SetFocus()
    [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
    Start-Sleep -Milliseconds 1800
    Sample 'opened'
    # Use 150% zoom consistently, independent of persisted per-file view state.
    (Find-Id ZoomOptionsButton).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    $deadline=(Get-Date).AddSeconds(5)
    do {
        $actual=$readerWindow.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,'Actual size (100%)'))
        if ($null -eq $actual) { Start-Sleep -Milliseconds 100 }
    } until ($null -ne $actual -or (Get-Date) -gt $deadline)
    $actual.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 300
    $buttons=$readerWindow.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::Button))
    $zoomRect=(Find-Id ZoomOptionsButton).Current.BoundingRectangle
    $zoomIn=$null
    foreach ($button in $buttons) {
        $rect=$button.Current.BoundingRectangle
        if ($rect.Left -ge $zoomRect.Right-1 -and [math]::Abs($rect.Top-$zoomRect.Top) -lt 12 -and $rect.Left -lt $zoomRect.Right+20) { $zoomIn=$button; break }
    }
    if ($null -eq $zoomIn) { throw 'Zoom in button not found' }
    for ($index=0;$index -lt 5;$index++) { $zoomIn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 60 }
    Start-Sleep -Milliseconds 1500
    # Inspect the captured zoom percentage: its localized automation name hides the text from UIA.
    Sample 'zoomed'
    Capture 'first-page'
    $pageBox=Find-Id GoToPageTextBox
    foreach ($page in @(5,10,15,20,25,30,35,40,45,50,55,60,55,50,45,40,35,30,25,20,15,10,5,1)) {
        $pageBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($page.ToString())
        $pageBox.SetFocus()
        [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
        Start-Sleep -Milliseconds 650
        Sample "page-$page"
        if ($page -eq 60) { Capture 'middle-page' }
    }
    Start-Sleep -Milliseconds 2000
    Sample 'settled'
    Capture 'returned-page'
    $moduleNames=@($process.Modules | ForEach-Object { $_.ModuleName })
    $report=[pscustomobject]@{Label=$Label;Executable=(Resolve-Path -LiteralPath $Executable).Path;ExeBytes=(Get-Item -LiteralPath $Executable).Length;StartupToDocumentMs=$startupMs;Samples=$samples.ToArray();LoadedMlModules=@($moduleNames | Where-Object { $_ -match 'onnx|DirectML|MachineLearning' });PeakObservedPrivateMiB=($samples | Measure-Object PrivateMiB -Maximum).Maximum;PeakObservedWorkingSetMiB=($samples | Measure-Object WorkingSetMiB -Maximum).Maximum}
    $report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath "artifacts/optimization/$Label.json" -Encoding UTF8
    $report | Select-Object Label,ExeBytes,StartupToDocumentMs,PeakObservedPrivateMiB,PeakObservedWorkingSetMiB,LoadedMlModules | ConvertTo-Json -Compress
} finally {
    $process.Refresh()
    if (-not $process.HasExited) { $process.CloseMainWindow() | Out-Null; if (-not $process.WaitForExit(5000)) { $process.Kill(); $process.WaitForExit() } }
    if ($null -ne $settingsBytes) { [System.IO.File]::WriteAllBytes($settingsPath,$settingsBytes) }
    elseif (Test-Path -LiteralPath $settingsPath) {
        $settings=Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
        $settings.RecentDocuments=@($settings.RecentDocuments | Where-Object { $_.FilePath -ne $fixture })
        $settings | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $settingsPath -Encoding UTF8
    }
}
