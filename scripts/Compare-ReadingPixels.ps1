param([string]$BaselinePrefix = 'baseline', [string]$CandidatePrefix = 'cache96',
    [int[]]$Themes = @(0,1,2), [int]$MaximumDifferentPixels = 0, [int]$MaximumChannelDifference = 0,
    [string]$Output = 'artifacts/reading-performance/pixel-comparison.json', [switch]$ReportOnly)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public static class ReaderPixelComparison {
    public static object Compare(string before, string after) {
        using (var a = new Bitmap(before))
        using (var b = new Bitmap(after)) {
            if (a.Size != b.Size) throw new InvalidOperationException("Window sizes differ.");
            var rect = new Rectangle(34, 96, a.Width - 68, a.Height - 128);
            var pa = a.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var pb = b.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try {
                int count = rect.Width * rect.Height;
                var xa = new byte[pa.Stride * rect.Height];
                var xb = new byte[pb.Stride * rect.Height];
                Marshal.Copy(pa.Scan0, xa, 0, xa.Length);
                Marshal.Copy(pb.Scan0, xb, 0, xb.Length);
                int changed = 0, max = 0, minX = rect.Width, minY = rect.Height, maxX = -1, maxY = -1;
                var colorsA = new HashSet<int>();
                var colorsB = new HashSet<int>();
                for (int y = 0; y < rect.Height; y++) {
                    for (int x = 0; x < rect.Width; x++) {
                        int ia = y * pa.Stride + x * 4, ib = y * pb.Stride + x * 4;
                        bool different = false;
                        for (int c = 0; c < 4; c++) {
                            int difference = Math.Abs(xa[ia+c] - xb[ib+c]);
                            different |= difference != 0;
                            max = Math.Max(max, difference);
                        }
                        if (different) { changed++; minX=Math.Min(minX,x); minY=Math.Min(minY,y); maxX=Math.Max(maxX,x); maxY=Math.Max(maxY,y); }
                        colorsA.Add((xa[ia+2] << 16) | (xa[ia+1] << 8) | xa[ia]);
                        colorsB.Add((xb[ib+2] << 16) | (xb[ib+1] << 8) | xb[ib]);
                    }
                }
                return new { Pixels = count, DifferentPixels = changed, MaxChannelDifference = max,
                    BaselineColors = colorsA.Count, CandidateColors = colorsB.Count,
                    DifferenceMinX=minX+rect.Left, DifferenceMaxX=maxX+rect.Left,
                    DifferenceMinY=minY+rect.Top, DifferenceMaxY=maxY+rect.Top };
            } finally { a.UnlockBits(pa); b.UnlockBits(pb); }
        }
    }
}
'@
$results=@()
foreach($theme in $Themes) {
    foreach($page in 1..3) {
        $before=Join-Path $PWD "artifacts/reading-performance/quality/$BaselinePrefix-t$theme/page-$page.png"
        $after=Join-Path $PWD "artifacts/reading-performance/quality/$CandidatePrefix-t$theme/page-$page.png"
        $comparison=[ReaderPixelComparison]::Compare($before,$after)
        $results += [pscustomobject]@{Theme=$theme;Page=$page;Result=$comparison}
    }
}
$results | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $Output -Encoding UTF8
$results | Format-Table Theme,Page,@{Name='DifferentPixels';Expression={$_.Result.DifferentPixels}},@{Name='BaselineColors';Expression={$_.Result.BaselineColors}},@{Name='CandidateColors';Expression={$_.Result.CandidateColors}}
if(-not $ReportOnly -and @($results | Where-Object {$_.Result.DifferentPixels -gt $MaximumDifferentPixels -or $_.Result.MaxChannelDifference -gt $MaximumChannelDifference -or $_.Result.BaselineColors -lt 10 -or $_.Result.CandidateColors -lt 10}).Count) {
    throw 'Pixel regression or insufficient rendered-content signal.'
}
