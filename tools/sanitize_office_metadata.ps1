param(
    [Parameter(Mandatory = $true)]
    [string[]]$Paths
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

foreach ($path in $Paths) {
    $resolved = (Resolve-Path -LiteralPath $path).Path
    $archive = [IO.Compression.ZipFile]::Open($resolved, [IO.Compression.ZipArchiveMode]::Update)
    try {
        $entry = $archive.GetEntry('docProps/core.xml')
        if ($null -eq $entry) {
            continue
        }

        $reader = [IO.StreamReader]::new($entry.Open(), [Text.Encoding]::UTF8)
        try {
            [xml]$xml = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }

        $manager = [Xml.XmlNamespaceManager]::new($xml.NameTable)
        $manager.AddNamespace('cp', 'http://schemas.openxmlformats.org/package/2006/metadata/core-properties')
        $manager.AddNamespace('dc', 'http://purl.org/dc/elements/1.1/')

        $creator = $xml.SelectSingleNode('/cp:coreProperties/dc:creator', $manager)
        if ($null -eq $creator) {
            $creator = $xml.CreateElement('dc', 'creator', 'http://purl.org/dc/elements/1.1/')
            [void]$xml.DocumentElement.AppendChild($creator)
        }
        $creator.InnerText = 'WriteMirror Project'

        $modifiedBy = $xml.SelectSingleNode('/cp:coreProperties/cp:lastModifiedBy', $manager)
        if ($null -eq $modifiedBy) {
            $modifiedBy = $xml.CreateElement('cp', 'lastModifiedBy', 'http://schemas.openxmlformats.org/package/2006/metadata/core-properties')
            [void]$xml.DocumentElement.AppendChild($modifiedBy)
        }
        $modifiedBy.InnerText = 'WriteMirror Project'

        $stream = $entry.Open()
        try {
            $stream.SetLength(0)
            $writer = [IO.StreamWriter]::new($stream, [Text.UTF8Encoding]::new($false))
            try {
                $xml.Save($writer)
            }
            finally {
                $writer.Dispose()
            }
        }
        finally {
            $stream.Dispose()
        }
    }
    finally {
        $archive.Dispose()
    }
}

# Office themes may embed localized fallback font names even when the visible
# document is Japanese. Replace Chinese and Korean fallback names so that the
# published package contains only Japanese or English text metadata.
foreach ($path in $Paths) {
    $resolved = (Resolve-Path -LiteralPath $path).Path
    $archive = [IO.Compression.ZipFile]::Open($resolved, [IO.Compression.ZipArchiveMode]::Update)
    try {
        foreach ($entry in @($archive.Entries | Where-Object { $_.FullName -like '*/theme/*.xml' })) {
            $reader = [IO.StreamReader]::new($entry.Open(), [Text.Encoding]::UTF8)
            try {
                [xml]$xml = $reader.ReadToEnd()
            }
            finally {
                $reader.Dispose()
            }

            $manager = [Xml.XmlNamespaceManager]::new($xml.NameTable)
            $manager.AddNamespace('a', 'http://schemas.openxmlformats.org/drawingml/2006/main')
            foreach ($font in $xml.SelectNodes('//a:font[@script="Hans" or @script="Hant" or @script="Hang"]', $manager)) {
                $font.SetAttribute('typeface', 'Yu Gothic')
            }

            $stream = $entry.Open()
            try {
                $stream.SetLength(0)
                $writer = [IO.StreamWriter]::new($stream, [Text.UTF8Encoding]::new($false))
                try {
                    $xml.Save($writer)
                }
                finally {
                    $writer.Dispose()
                }
            }
            finally {
                $stream.Dispose()
            }
        }
    }
    finally {
        $archive.Dispose()
    }
}
