param(
    [string]$Version = "v3.1.1"
)

$ErrorActionPreference = "Stop"

$workspaceRoot = $PSScriptRoot
$publishDir = Join-Path $workspaceRoot "publish\$Version"

Write-Host "Starting build & packaging for version $Version..."
if (Test-Path $publishDir) {
    Remove-Item -Path $publishDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

$rids = @("win-x64", "win-x86", "win-arm64")

foreach ($rid in $rids) {
    Write-Host "`n>>> Publishing $rid..."
    $outDir = Join-Path $publishDir $rid
    
    dotnet publish "$workspaceRoot\HorizonRadioOverlay.csproj" `
        -c Release `
        -r $rid `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugSymbols=false `
        -p:DebugType=None `
        -o $outDir

    $singleExe = Join-Path $outDir "HorizonRadioOverlay.exe"
    $targetExeName = "HorizonRadioOverlay_${Version}_${rid}.exe"
    $targetExePath = Join-Path $publishDir $targetExeName
    Copy-Item $singleExe $targetExePath -Force
    Write-Host "Created single-file: $targetExeName"

    $zipName = "HorizonRadioOverlay_${Version}_${rid}.zip"
    $zipPath = Join-Path $publishDir $zipName
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path "$outDir\*" -DestinationPath $zipPath -Force
    Write-Host "Created zip package: $zipName"
}

# 复制 Release Notes
$notesFile = Join-Path $workspaceRoot "RELEASE_NOTES_${Version}.md"
if (Test-Path $notesFile) {
    Copy-Item $notesFile (Join-Path $publishDir "RELEASE_NOTES_${Version}.md") -Force
}

# 计算 SHA256 哈希
Write-Host "`nGenerating SHA256 checksums..."
$shaFile = Join-Path $publishDir "SHA256SUMS.txt"
$filesToHash = Get-ChildItem -Path $publishDir -File -Filter "HorizonRadioOverlay_*" | Sort-Object Name
$hashEntries = foreach ($file in $filesToHash) {
    $hash = (Get-FileHash -Path $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $($file.Name)"
}
$hashEntries | Set-Content -Path $shaFile -Encoding utf8
Write-Host "SHA256SUMS.txt generated successfully."

Write-Host "`nAll packages built successfully in: $publishDir"
Get-ChildItem -Path $publishDir -File | Format-Table Name, Length, LastWriteTime -AutoSize
