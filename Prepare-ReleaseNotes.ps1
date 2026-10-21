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
            if (!$history.ContainsKey($oldVersion) -and ![string]::IsNullOrWhiteSpace($release.body)) {
                $history[$oldVersion] = [pscustomobject]@{
                    version = $oldVersion
                    title = @{ ru = $release.name; en = $release.name }
                    notes = @{ ru = $release.body; en = $release.body }
                }
            }
        }

        # Reusing the previous release's curated notes would misdescribe the next release.
        # If no new entry was written, derive honest notes from the actual new commits.
        $oldBlob = & git -C $PSScriptRoot rev-parse "$($latest.tag_name):release-notes/catalog.json" 2>$null
        $hasPreviousSource = $LASTEXITCODE -eq 0
        $newBlob = & git -C $PSScriptRoot hash-object $sourcePath
        if ($LASTEXITCODE -ne 0) { throw 'Cannot read release notes source' }
        if ($hasPreviousSource -and $oldBlob -eq $newBlob) {
            $subjects = @(& git -C $PSScriptRoot log "$($latest.tag_name)..HEAD" --no-merges --format=%s)
            if ($LASTEXITCODE -ne 0) { throw 'Cannot calculate release changes; fetch full git history and tags' }
            if ($subjects.Count -eq 0) {
                $subjects = @("Rebuilt the current main revision ($((& git -C $PSScriptRoot rev-parse --short HEAD))) with the release checks.")
            }
            $changes = ($subjects | ForEach-Object { "- $_" }) -join "`n"
            $current.title = [pscustomobject]@{ ru = 'Изменения этой версии'; en = 'Changes in this version' }
            $current.notes = [pscustomobject]@{
                ru = "Изменения из истории коммитов (язык оригинала):`n`n$changes"
                en = "Changes from the commit history:`n`n$changes"
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

## Downloads

Download Tessera.exe or Tessera-win-x64.zip for the desktop app, or Tessera-cli-win-x64.zip for the CLI. No .NET installation is required. SHA256SUMS.txt contains the binary checksums.

Existing users can update in Settings. Keep WorkspaceData beside the executable to preserve the library. Read these notes and earlier releases offline in Settings > Patch notes.
"@
[IO.File]::WriteAllText((Join-Path $output 'ReleaseNotes.md'), $body)
Write-Output "Prepared release notes for v$Version ($($entries.Count) versions)."
