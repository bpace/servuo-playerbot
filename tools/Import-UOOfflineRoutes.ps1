[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$SourceRoot,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath,

    [switch]$Enabled
)

$ErrorActionPreference = 'Stop'
$dataRoot = Join-Path $SourceRoot 'playerbots\data'
if (-not (Test-Path -LiteralPath $dataRoot)) { throw "No playerbots data under $SourceRoot" }

function Read-Data([string]$relative) {
    Get-Content -Raw -LiteralPath (Join-Path $dataRoot $relative) | ConvertFrom-Json
}

$waypoints = (Read-Data 'Waypoints\waypoints.json').Waypoints
$destinations = (Read-Data 'Destinations\destinations.json').Destinations
$zones = (Read-Data 'Zones\zones.json').Zones
$pkSpawns = (Read-Data 'CustomSpawns\pk_spawns.json').Spawns
if (-not $waypoints -or -not $destinations) { throw 'The UO Offline source contains no waypoints or destinations.' }

$destinationDirectory = Split-Path -Parent $OutputPath
if ($destinationDirectory) { New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null }
$temporaryPath = "$OutputPath.new"
$settings = [System.Xml.XmlWriterSettings]::new()
$settings.Indent = $true
$settings.Encoding = [System.Text.UTF8Encoding]::new($false)
$writer = [System.Xml.XmlWriter]::Create($temporaryPath, $settings)

function Attribute([string]$name, $value) {
    $script:writer.WriteAttributeString($name, [string]$value)
}

function Write-Connects($connects) {
    $script:writer.WriteStartElement('Connects')
    foreach ($connection in @($connects)) {
        $script:writer.WriteElementString('Name', [string]$connection)
    }
    $script:writer.WriteEndElement()
}

try {
    $writer.WriteStartDocument()
    $writer.WriteStartElement('PlayerBotWorld')
    Attribute 'enabled' $Enabled.IsPresent.ToString().ToLowerInvariant()

    $writer.WriteStartElement('Waypoints')
    foreach ($facet in 'Felucca', 'Trammel') {
        foreach ($point in $waypoints) {
            $writer.WriteStartElement('Waypoint')
            Attribute 'Name' $point.Name; Attribute 'Facet' $facet
            Attribute 'X' $point.X; Attribute 'Y' $point.Y; Attribute 'Z' $point.Z
            Write-Connects $point.Connects
            $writer.WriteEndElement()
        }
    }
    $writer.WriteEndElement()

    $writer.WriteStartElement('Destinations')
    foreach ($facet in 'Felucca', 'Trammel') {
        foreach ($destination in $destinations) {
            $writer.WriteStartElement('Destination')
            Attribute 'Name' $destination.Name; Attribute 'Facet' $facet; Attribute 'Kind' $destination.Type
            Attribute 'X' $destination.X; Attribute 'Y' $destination.Y; Attribute 'Z' $destination.Z
            if ($destination.NearestWaypoint) { Attribute 'NearestWaypoint' $destination.NearestWaypoint }
            $writer.WriteEndElement()
        }
    }
    $writer.WriteEndElement()

    $writer.WriteStartElement('Zones')
    foreach ($facet in 'Felucca', 'Trammel') {
        foreach ($zone in $zones) {
            $writer.WriteStartElement('Zone')
            Attribute 'Name' $zone.Name; Attribute 'Facet' $facet; Attribute 'Kind' $zone.Kind
            $writer.WriteStartElement('Points')
            foreach ($point in $zone.Points) {
                $writer.WriteStartElement('Point'); Attribute 'X' $point[0]; Attribute 'Y' $point[1]; $writer.WriteEndElement()
            }
            $writer.WriteEndElement(); $writer.WriteEndElement()
        }
    }
    $writer.WriteEndElement()

    $writer.WriteStartElement('Spawns')
    foreach ($spawn in $pkSpawns) {
        $writer.WriteStartElement('Spawn')
        Attribute 'Name' $spawn.name; Attribute 'Facet' 'Felucca'; Attribute 'Role' 'PlayerKiller'
        Attribute 'X' $spawn.x; Attribute 'Y' $spawn.y; Attribute 'Z' $spawn.z; Attribute 'Count' $spawn.amount
        $writer.WriteEndElement()
    }
    $writer.WriteEndElement()
    $writer.WriteStartElement('DungeonLinks'); $writer.WriteEndElement()
    $writer.WriteEndElement(); $writer.WriteEndDocument()
}
finally {
    if ($writer) { $writer.Dispose() }
}

Move-Item -LiteralPath $temporaryPath -Destination $OutputPath -Force
Write-Output ("Wrote {0} waypoints per facet, {1} destinations per facet, {2} zones per facet, and {3} Felucca-only PK spawns to {4}." -f $waypoints.Count, $destinations.Count, $zones.Count, $pkSpawns.Count, $OutputPath)
