$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore

$iconPath = Join-Path $PSScriptRoot 'app.ico'
$pngPath = Join-Path $PSScriptRoot 'app-icon.png'
$iconStream = [IO.File]::OpenRead($iconPath)
try {
    $decoder = [Windows.Media.Imaging.IconBitmapDecoder]::new(
        $iconStream,
        [Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat,
        [Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
    $largestFrame = $decoder.Frames |
        Sort-Object -Property { $_.PixelWidth * $_.PixelHeight } -Descending |
        Select-Object -First 1
}
finally { $iconStream.Dispose() }

$encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
$encoder.Frames.Add($largestFrame)
$pngStream = [IO.File]::Create($pngPath)
try { $encoder.Save($pngStream) }
finally { $pngStream.Dispose() }
