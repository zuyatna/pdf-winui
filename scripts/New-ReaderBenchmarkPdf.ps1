$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path artifacts/optimization -Force | Out-Null
$objects = [System.Collections.Generic.List[string]]::new()
$pageCount = 120
$objects.Add('<< /Type /Catalog /Pages 2 0 R >>')
$kids = (0..($pageCount - 1) | ForEach-Object { "$(4 + $_ * 2) 0 R" }) -join ' '
$objects.Add("<< /Type /Pages /Kids [$kids] /Count $pageCount >>")
$objects.Add('<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>')
for ($page = 0; $page -lt $pageCount; $page++) {
    $contentId = 5 + $page * 2
    $objects.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 3 0 R >> >> /Contents $contentId 0 R >>")
    $content = "0 0 0 rg`nBT /F1 24 Tf 72 720 Td (Memory benchmark page $($page + 1)) Tj ET`n"
    for ($line = 0; $line -lt 35; $line++) { $content += "BT /F1 11 Tf 72 $(680 - $line * 16) Td (PDF Reader text rendering, search, selection and scrolling.) Tj ET`n" }
    $objects.Add("<< /Length $([System.Text.Encoding]::ASCII.GetByteCount($content)) >>`nstream`n$content" + 'endstream')
}
$builder = [System.Text.StringBuilder]::new("%PDF-1.4`n")
$offsets = [System.Collections.Generic.List[int]]::new()
for ($index = 0; $index -lt $objects.Count; $index++) {
    $offsets.Add($builder.Length)
    [void]$builder.Append("$($index + 1) 0 obj`n$($objects[$index])`nendobj`n")
}
$xref = $builder.Length
[void]$builder.Append("xref`n0 $($objects.Count + 1)`n0000000000 65535 f `n")
foreach ($offset in $offsets) { [void]$builder.Append(('{0:D10} 00000 n ' -f $offset) + "`n") }
[void]$builder.Append("trailer`n<< /Size $($objects.Count + 1) /Root 1 0 R >>`nstartxref`n$xref`n%%EOF`n")
[System.IO.File]::WriteAllText((Join-Path $PWD 'artifacts/optimization/reading-benchmark.pdf'), $builder.ToString(), [System.Text.Encoding]::ASCII)
