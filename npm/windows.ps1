Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

try {
    $source = $env:AUTORESUME_INSTALL_SOURCE
    $destination = $env:AUTORESUME_INSTALL_DESTINATION
    if ($env:AUTORESUME_INSTALL_ACTION -eq 'download') {
        $uri = [Uri]$source
        if ($uri.Scheme -ne 'https' -or $uri.Host -ne 'github.com') {
            throw 'Only the pinned HTTPS GitHub release is supported.'
        }
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -UseBasicParsing -Uri $uri -OutFile $destination -MaximumRedirection 5 -TimeoutSec 180
    } elseif ($env:AUTORESUME_INSTALL_ACTION -eq 'extract') {
        Add-Type -AssemblyName System.IO.Compression
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $root = [IO.Path]::GetFullPath($destination).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        $zip = [IO.Compression.ZipFile]::OpenRead($source)
        try {
            foreach ($entry in $zip.Entries) {
                if ([IO.Path]::IsPathRooted($entry.FullName) -or $entry.FullName.Contains(':') -or
                    ($entry.FullName -split '[\\/]') -contains '..' -or
                    (($entry.ExternalAttributes -shr 16) -band 0xF000) -eq 0xA000) {
                    throw 'The release ZIP contains an unsafe path or symbolic link.'
                }
                $target = [IO.Path]::GetFullPath([IO.Path]::Combine($root, $entry.FullName))
                if (-not $target.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
                    throw 'A ZIP entry would escape the installation directory.'
                }
            }
        } finally { $zip.Dispose() }
        [IO.Compression.ZipFile]::ExtractToDirectory($source, $destination)
    } else { throw 'Unknown installation action.' }
} catch {
    [Console]::Error.WriteLine('Codex AutoResume install failed: ' + $_.Exception.Message)
    exit 1
}
