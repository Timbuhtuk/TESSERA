$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$sandbox = Join-Path $taskRoot ('artifacts/version-check-' + [guid]::NewGuid().ToString('N'))
$gitExe = (Get-Command git -CommandType Application | Select-Object -First 1).Source
New-Item -ItemType Directory -Path $sandbox | Out-Null
function Invoke-TestGit {
    & $gitExe -C $sandbox @args | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Version fixture git failed: $args" }
}
function Commit-Fixture {
    [guid]::NewGuid().ToString('N') | Set-Content (Join-Path $sandbox 'marker.txt')
    Invoke-TestGit add .
    Invoke-TestGit commit -m 'Version fixture'
}
function Expect-Version($expected, $scenario) {
    $actual = & (Join-Path $taskRoot 'Get-ReleaseVersion.ps1') -RepositoryRoot $sandbox
    if ($actual -cne $expected) { throw "${scenario}: expected $expected, got $actual" }
    Write-Output "PASS Release numbering: $scenario"
}
Invoke-TestGit init --initial-branch=main
Invoke-TestGit config user.name 'Tessera checks'
Invoke-TestGit config user.email 'checks@example.invalid'
Invoke-TestGit config commit.gpgsign false
Invoke-TestGit config core.autocrlf false
'2.0.0' | Set-Content (Join-Path $sandbox 'release-version.txt')
Commit-Fixture
Invoke-TestGit tag v1.0.57
Expect-Version '2.0.0' 'new major starts at zero and ignores old series'
Invoke-TestGit tag v2.0.0
Expect-Version '2.0.0' 'rerun of a tagged commit keeps its version'
Commit-Fixture
Expect-Version '2.0.1' 'next commit increments the published patch'
Commit-Fixture
Expect-Version '2.0.1' 'unpublished PR commits do not consume patch numbers'
Invoke-TestGit tag v2.0.4
Commit-Fixture
Expect-Version '2.0.5' 'highest merged release determines the next patch'
Invoke-TestGit switch -c unrelated HEAD~1
Commit-Fixture
Invoke-TestGit tag v2.0.99
Invoke-TestGit switch main
Expect-Version '2.0.5' 'unmerged release tags cannot advance the main series'
Invoke-TestGit tag v2.0.12-rc1
Expect-Version '2.0.5' 'prerelease tags are excluded'
'2.1.0' | Set-Content (Join-Path $sandbox 'release-version.txt')
Expect-Version '2.1.0' 'changing the minor starts its new series'
'2.0.10' | Set-Content (Join-Path $sandbox 'release-version.txt')
Expect-Version '2.0.10' 'configured version floor is respected'
'2.0' | Set-Content (Join-Path $sandbox 'release-version.txt')
$rejected = $false
try { $null = & (Join-Path $taskRoot 'Get-ReleaseVersion.ps1') -RepositoryRoot $sandbox }
catch { $rejected = $_.Exception.Message -like '*three-part*' }
if (!$rejected) { throw 'A version incompatible with the updater was accepted' }
Write-Output 'PASS Release numbering: rejects incomplete versions'
