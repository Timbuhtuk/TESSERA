$ErrorActionPreference = 'Stop'

function Assert-PlainPath([string] $path) {
    for ($current = [IO.Path]::GetFullPath($path); $current; $current = [IO.Path]::GetDirectoryName($current)) {
        try { $attributes = [IO.File]::GetAttributes($current) }
        catch [IO.FileNotFoundException] { continue }
        catch [IO.DirectoryNotFoundException] { continue }
        if ($attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw ($plan.RedirectedPath + $current)
        }
    }
}

function Clear-OwnedDirectory([string] $path) {
    if (![IO.Directory]::Exists($path)) { return }
    Assert-PlainPath $path
    $prefix = $path.TrimEnd('\') + '\'
    if (!$plan.Executable.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        [IO.Directory]::Delete($path, $true)
        return
    }

    # Leave the executable and its containing directories in place until the cleanup succeeds.
    foreach ($entry in [IO.Directory]::GetFileSystemEntries($path)) {
        if ($plan.Executable.Equals($entry, [StringComparison]::OrdinalIgnoreCase)) { continue }
        if ($plan.Executable.StartsWith($entry + '\', [StringComparison]::OrdinalIgnoreCase)) {
            Clear-OwnedDirectory $entry
        } elseif ([IO.File]::GetAttributes($entry) -band [IO.FileAttributes]::Directory) {
            [IO.Directory]::Delete($entry, $true)
        } else {
            [IO.File]::Delete($entry)
        }
    }
    $remainingDirectories.Add($path)
}

try {
    $plan = ConvertFrom-Json -InputObject $env:TESSERA_UNINSTALL_PLAN
    $owner = Get-Process -Id $plan.OwnerId -ErrorAction SilentlyContinue
    if ($owner -and $owner.StartTime.ToUniversalTime().Ticks -eq [long] $plan.OwnerStarted -and !$owner.WaitForExit(30000)) {
        throw $plan.CloseTimeout
    }

    Assert-PlainPath $plan.Executable
    $exe = [IO.FileInfo]::new($plan.Executable)
    if (!$exe.Exists -or $exe.Length -ne [long] $plan.Length -or $exe.LastWriteTimeUtc.Ticks -ne [long] $plan.LastWrite -or
        [Diagnostics.FileVersionInfo]::GetVersionInfo($plan.Executable).ProductName -ne 'Tessera') {
        throw $plan.ChangedExecutable
    }
    if ($exe.IsReadOnly) { throw $plan.ReadOnlyExecutable }
    $names = @('Tessera', 'Tessera.Desktop', [IO.Path]::GetFileNameWithoutExtension($plan.Executable))
    foreach ($instance in Get-Process -Name $names -ErrorAction SilentlyContinue) {
        $module = $null
        try { $module = $instance.MainModule } catch [ComponentModel.Win32Exception] { }
        if ($module -and ($module.FileName -eq $plan.Executable -or
                ($plan.CleansSharedProfile -and $module.FileVersionInfo.ProductName -eq 'Tessera'))) {
            throw $plan.CloseOtherInstances
        }
    }

    foreach ($path in $plan.Directories + $plan.Files) { Assert-PlainPath $path }
    $remainingDirectories = [Collections.Generic.List[string]]::new()
    foreach ($path in $plan.Directories) { Clear-OwnedDirectory $path }
    foreach ($path in $plan.Files) { [IO.File]::Delete($path) }
    if ($plan.EmptyPortableDirectory -and [IO.Directory]::Exists($plan.EmptyPortableDirectory)) {
        Assert-PlainPath $plan.EmptyPortableDirectory
        if ([IO.Directory]::GetFileSystemEntries($plan.EmptyPortableDirectory).Length -eq 0) {
            [IO.Directory]::Delete($plan.EmptyPortableDirectory)
        }
    }

    Assert-PlainPath $plan.Executable
    [IO.File]::Delete($plan.Executable)
    foreach ($path in $remainingDirectories) {
        if ([IO.Directory]::GetFileSystemEntries($path).Length -eq 0) { [IO.Directory]::Delete($path) }
    }
    exit 0
} catch {
    $message = $plan.ErrorMessage + [Environment]::NewLine + $_.Exception.Message
    [Console]::Error.WriteLine($message)
    if ($plan.ShowErrorDialog) {
        Add-Type -AssemblyName System.Windows.Forms
        [void] [Windows.Forms.MessageBox]::Show($message, $plan.ErrorTitle, 'OK', 'Error')
    }
    exit 1
}
