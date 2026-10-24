param([string]$RepositoryRoot = $PSScriptRoot)

$ErrorActionPreference = 'Stop'
$floorText = (Get-Content -LiteralPath (Join-Path $RepositoryRoot 'release-version.txt') -Raw).Trim()
if ($floorText -notmatch '^\d+\.\d+\.\d+$') { throw 'release-version.txt must contain a three-part version' }
$floor = [version]$floorText
$prefix = "v$($floor.Major).$($floor.Minor)."
$pattern = '^' + [regex]::Escape($prefix) + '\d+$'

$tags = @(& git -C $RepositoryRoot tag --merged HEAD --list "$prefix*")
if ($LASTEXITCODE -ne 0) { throw 'Cannot read merged release tags' }
$versions = @($tags | Where-Object { $_ -match $pattern } | ForEach-Object { [version]$_.Substring(1) })
$currentTags = @(& git -C $RepositoryRoot tag --points-at HEAD --list "$prefix*")
if ($LASTEXITCODE -ne 0) { throw 'Cannot read current release tags' }
$current = $currentTags | Where-Object { $_ -match $pattern } | ForEach-Object { [version]$_.Substring(1) } |
    Where-Object { $_ -ge $floor } | Sort-Object -Descending | Select-Object -First 1
if ($current) { return $current.ToString(3) }

$latest = $versions | Sort-Object -Descending | Select-Object -First 1
if (!$latest -or $latest -lt $floor) { return $floor.ToString(3) }
return [version]::new($floor.Major, $floor.Minor, $latest.Build + 1).ToString(3)
