param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$Repository
)

$ErrorActionPreference = 'Stop'
$sourcePath = Join-Path $PSScriptRoot 'release-notes/catalog.json'
$source = Get-Content -LiteralPath $sourcePath -Raw | ConvertFrom-Json
$current = $source.releases | Where-Object version -eq 'next'
if (@($current).Count -ne 1 -or !$current.title.ru -or !$current.title.en -or !$current.notes.ru -or !$current.notes.en) {
    throw 'A bilingual next-release entry is required in release-notes/catalog.json'
}
$history = @{}
foreach ($entry in $source.releases | Where-Object version -ne 'next') { $history[$entry.version] = $entry }

if ($Repository) {
    if ($Repository -notmatch '^[\w.-]+/[\w.-]+$') { throw 'Invalid release repository' }
    $headers = @{ Accept = 'application/vnd.github+json'; 'User-Agent' = 'Tessera-Release-Notes' }
    if ($env:GH_TOKEN) { $headers.Authorization = "Bearer $env:GH_TOKEN" }
    $published = @()
    for ($page = 1; ; $page++) {
        $batch = Invoke-RestMethod "https://api.github.com/repos/$Repository/releases?per_page=100&page=$page" -Headers $headers
        $published += @($batch | Where-Object { !$_.draft -and !$_.prerelease -and $_.tag_name -match '^v\d+\.\d+\.\d+$' })
        if (@($batch).Count -lt 100) { break }
    }
    $published = @($published | Sort-Object { [version]$_.tag_name.Substring(1) } -Descending)
    $latest = $published | Select-Object -First 1
    if ($latest) {
        $archive = $latest.assets | Where-Object name -eq 'ReleaseNotes.json'
        if ($archive) {
            # Keep translated history from the last published binary, including skipped versions.
            $expected = "https://github.com/$Repository/releases/download/$($latest.tag_name)/ReleaseNotes.json"
            if ($archive.browser_download_url -ne $expected -or $archive.size -gt 10000000) { throw 'Invalid release notes archive' }
            $response = Invoke-WebRequest $expected -Headers @{ 'User-Agent' = 'Tessera-Release-Notes' }
            $archiveText = if ($response.Content -is [byte[]]) { [Text.Encoding]::UTF8.GetString($response.Content) } else { [string]$response.Content }
            $previous = $archiveText | ConvertFrom-Json
            if (!$previous.releases -or $previous.releases[0].version -ne $latest.tag_name.Substring(1)) { throw 'Release archive does not match its tag' }
            foreach ($entry in $previous.releases) {
                if ($entry.version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid archived release version' }
                if (!$history.ContainsKey($entry.version)) { $history[$entry.version] = $entry }
            }
        }
        foreach ($release in $published) {
            $oldVersion = $release.tag_name.Substring(1)
            if (!$history.ContainsKey($oldVersion)) { throw "Write user-facing history for $oldVersion in release-notes/catalog.json; raw GitHub descriptions are not patch notes." }
        }

        # Only edited user-facing notes may describe a new release. Never publish commit subjects.
        $null = & git -C $PSScriptRoot rev-parse --verify "$($latest.tag_name)^{commit}" 2>$null
        if ($LASTEXITCODE -ne 0) { throw 'Cannot compare patch notes; fetch full git history and tags.' }
        $oldSource = & git -C $PSScriptRoot show "$($latest.tag_name):release-notes/catalog.json" 2>$null
        $hasPreviousSource = $LASTEXITCODE -eq 0
        if ($hasPreviousSource) {
            $oldEntry = (($oldSource -join "`n") | ConvertFrom-Json).releases | Where-Object version -eq 'next'
            $changed = $false
            foreach ($language in @('ru', 'en')) {
                if ($current.title.$language -cne $oldEntry.title.$language -or $current.notes.$language -cne $oldEntry.notes.$language) { $changed = $true }
            }
            if (!$changed -and $Version -ne $latest.tag_name.Substring(1)) {
                throw 'Write new user-facing patch notes in release-notes/catalog.json: affected tools and their changed behavior. Technical details and commit logs must not be published.'
            }
        }
    }
}

$current.version = $Version
$entries = @($current) + @($history.Values | Where-Object {
    $_.version -ne $Version -and (!$Repository -or [version]$_.version -lt [version]$Version)
} | Sort-Object { [version]$_.version } -Descending)
foreach ($entry in $entries) {
    if (!$entry.title.en -or !$entry.notes.en) { throw "Empty release notes for $($entry.version)" }
}
$output = [IO.Path]::GetFullPath($OutputDirectory, $PSScriptRoot)
[IO.Directory]::CreateDirectory($output) | Out-Null
[IO.File]::WriteAllText((Join-Path $output 'ReleaseNotes.json'), (@{ releases = $entries } | ConvertTo-Json -Depth 8))
$body = @"
# What's new in Tessera v$Version

$($current.title.en)

$($current.notes.en)

"@
[IO.File]::WriteAllText((Join-Path $output 'ReleaseNotes.md'), $body)
Write-Output "Prepared release notes for v$Version ($($entries.Count) versions)."
