$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskOutput = Join-Path $taskRoot ('artifacts/notes-script-' + [guid]::NewGuid().ToString('N'))
$global:notesFixtureSource = Get-Content (Join-Path $taskRoot 'release-notes/catalog.json') -Raw
$global:notesFixtureMode = 'curated'

function Invoke-RestMethod {
    param($Uri, $Headers)
    if ($Uri -notlike 'https://api.github.com/repos/Test/Notes/releases?*') { throw "Unexpected API request: $Uri" }
    $items = @(
        [pscustomobject]@{ tag_name = 'v9000.0.100'; name = 'Tessera v9000.0.100'; draft = $false; prerelease = $false; body = 'Internal implementation details must not be imported'; assets = @(
            [pscustomobject]@{ name = 'ReleaseNotes.json'; size = 500; browser_download_url = 'https://github.com/Test/Notes/releases/download/v9000.0.100/ReleaseNotes.json' }
        ) },
        [pscustomobject]@{ tag_name = 'v9000.0.98'; name = 'Older release'; draft = $false; prerelease = $false; body = 'Raw commit and deployment log'; assets = @() },
        [pscustomobject]@{ tag_name = 'v9001.0.0'; draft = $true; prerelease = $false; body = 'Never bundle drafts'; assets = @() }
    )
    Write-Output -NoEnumerate $items
}
function Invoke-WebRequest {
    param($Uri, $Headers)
    if ($Uri -ne 'https://github.com/Test/Notes/releases/download/v9000.0.100/ReleaseNotes.json') { throw 'Unexpected asset URL' }
    $json = @'
{"releases":[{"version":"9000.0.100","title":{"en":"Last release","ru":"Предыдущий релиз"},"notes":{"en":"Color replacement: pick the replacement color from the image.","ru":"Замена цвета: новый цвет можно выбрать на изображении."}},{"version":"9000.0.99","title":{"en":"Earlier tools"},"notes":{"en":"Tiles: choose a neighbor by thumbnail."}},{"version":"9000.0.98","title":{"en":"Older tools"},"notes":{"en":"Resize: keep the image aspect ratio."}}]}
'@
    if ($global:notesFixtureMode -eq 'unknown-history') {
        $archive = $json | ConvertFrom-Json
        $archive.releases = @($archive.releases | Where-Object version -ne '9000.0.98')
        $json = $archive | ConvertTo-Json -Depth 8
    }
    return @{ Content = [Text.Encoding]::UTF8.GetBytes($json) }
}
function git {
    $global:LASTEXITCODE = 0
    if ($args -contains 'rev-parse') { return 'abcdef1' }
    if ($args -contains 'show') {
        $old = $global:notesFixtureSource | ConvertFrom-Json
        if ($global:notesFixtureMode -eq 'curated') { $old.releases[0].notes.en = 'Image board: arrange multiple images.' }
        if ($global:notesFixtureMode -eq 'history-only') { $old.releases[-1].title.en = 'An older title' }
        return $old | ConvertTo-Json -Depth 8
    }
    throw "Patch notes must not read commit logs: $args"
}
function Require($condition, $message) { if (!$condition) { throw $message } }

foreach ($mode in @('curated', 'unchanged', 'history-only', 'unknown-history', 'rerun')) {
    $global:notesFixtureMode = $mode
    $output = Join-Path $taskOutput $mode
    $version = if ($mode -eq 'rerun') { '9000.0.100' } else { '9000.0.101' }
    $errorText = ''
    try { & (Join-Path $taskRoot 'Prepare-ReleaseNotes.ps1') -Version $version -OutputDirectory $output -Repository 'Test/Notes' }
    catch { $errorText = $_.Exception.Message }
    if ($mode -in @('unchanged', 'history-only', 'unknown-history')) {
        Require ($errorText -like '*user-facing*') "Missing curated notes did not fail preparation: $mode / $errorText"
        Require (!(Test-Path (Join-Path $output 'ReleaseNotes.json'))) 'Rejected notes were written as release output'
        Write-Output "PASS Release note preparation: rejects $mode instead of technical or stale notes"
        continue
    }
    Require (!$errorText) "Curated notes were rejected: $errorText"
    $catalog = Get-Content (Join-Path $output 'ReleaseNotes.json') -Raw | ConvertFrom-Json
    $body = Get-Content (Join-Path $output 'ReleaseNotes.md') -Raw
    $current = $catalog.releases[0]
    Require ($current.version -eq $version) 'Current version is not stamped'
    Require (@($catalog.releases | Where-Object version -eq '9000.0.99').Count -eq 1) 'Skipped upgrade history was lost'
    Require (@($catalog.releases | Where-Object version -eq '9000.0.98').Count -eq 1) 'Curated history was lost'
    Require (@($catalog.releases | Where-Object version -eq '9001.0.0').Count -eq 0) 'Draft notes were bundled'
    $expectedBody = "# What's new in Tessera v$version`n`n$($current.title.en)`n`n$($current.notes.en)"
    Require ($body.Replace("`r", '').Trim() -ceq $expectedBody) 'GitHub notes contain extra implementation or download details'
    Require ($current.notes.en -eq (($global:notesFixtureSource | ConvertFrom-Json).releases | Where-Object version -eq 'next').notes.en) 'User-facing notes were replaced by generated text'
    Write-Output "PASS Release note preparation: $mode, curated archive, version and exact user-facing GitHub text"
}
