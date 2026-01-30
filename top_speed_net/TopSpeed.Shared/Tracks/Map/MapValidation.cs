using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using TopSpeed.Data;
using TopSpeed.Tracks.Materials;
using TopSpeed.Tracks.Rooms;
using TopSpeed.Tracks.Areas;
using TopSpeed.Tracks.Beacons;
using TopSpeed.Tracks.Guidance;
using TopSpeed.Tracks.Markers;
using TopSpeed.Tracks.Sectors;
using TopSpeed.Tracks.Topology;
using TopSpeed.Tracks.Walls;

namespace TopSpeed.Tracks.Map
{
    public enum TrackMapIssueSeverity
    {
        Warning = 0,
        Error = 1
    }

    public sealed class TrackMapIssue
    {
        public TrackMapIssue(TrackMapIssueSeverity severity, string message, int? lineNumber = null, int? cellX = null, int? cellZ = null)
        {
            Severity = severity;
            Message = message;
            LineNumber = lineNumber;
            CellX = cellX;
            CellZ = cellZ;
        }

        public TrackMapIssueSeverity Severity { get; }
        public string Message { get; }
        public int? LineNumber { get; }
        public int? CellX { get; }
        public int? CellZ { get; }

        public override string ToString()
        {
            var location = LineNumber.HasValue ? $"line {LineNumber}" : null;
            if (CellX.HasValue && CellZ.HasValue)
            {
                var cell = $"cell ({CellX},{CellZ})";
                location = string.IsNullOrWhiteSpace(location) ? cell : $"{location}, {cell}";
            }
            return string.IsNullOrWhiteSpace(location)
                ? $"{Severity}: {Message}"
                : $"{Severity}: {Message} ({location})";
        }
    }

    public sealed class TrackMapValidationOptions
    {
        public bool RequireSafeZones { get; set; }
        public bool RequireIntersections { get; set; }
        public bool TreatUnreachableCellsAsErrors { get; set; }
    }

    public sealed class TrackMapValidationResult
    {
        public TrackMapValidationResult(IReadOnlyList<TrackMapIssue> issues)
        {
            Issues = issues ?? Array.Empty<TrackMapIssue>();
        }

        public IReadOnlyList<TrackMapIssue> Issues { get; }

        public bool IsValid => Issues.All(issue => issue.Severity != TrackMapIssueSeverity.Error);
    }

    public sealed class TrackMapMetadata
    {
        public string Name { get; set; } = "Track";
        public float CellSizeMeters { get; set; } = 1f;
        public TrackWeather Weather { get; set; } = TrackWeather.Sunny;
        public TrackAmbience Ambience { get; set; } = TrackAmbience.NoAmbience;
        public string DefaultMaterialId { get; set; } = "asphalt";
        public bool DefaultMaterialDefined { get; set; }
        public TrackNoise DefaultNoise { get; set; } = TrackNoise.NoNoise;
        public float DefaultWidthMeters { get; set; } = 12f;
        public float? BaseHeightMeters { get; set; }
        public float? DefaultAreaHeightMeters { get; set; }
        public float? DefaultCeilingHeightMeters { get; set; }
        public float StartX { get; set; }
        public float StartZ { get; set; }
        public float StartHeadingDegrees { get; set; }
        public MapDirection StartHeading { get; set; } = MapDirection.North;
        public float SafeZoneRingMeters { get; set; }
        public string SafeZoneMaterialId { get; set; } = "gravel";
        public TrackNoise SafeZoneNoise { get; set; } = TrackNoise.NoNoise;
        public string? SafeZoneName { get; set; }
        public float OuterRingMeters { get; set; }
        public string OuterRingMaterialId { get; set; } = "gravel";
        public TrackNoise OuterRingNoise { get; set; } = TrackNoise.NoNoise;
        public string? OuterRingName { get; set; }
        public TrackAreaType OuterRingType { get; set; } = TrackAreaType.Boundary;
        public TrackAreaFlags OuterRingFlags { get; set; } = TrackAreaFlags.None;
    }

    public sealed class TrackMapDefinition
    {
        public TrackMapDefinition(TrackMapMetadata metadata)
        {
            Metadata = metadata;
            Sectors = Array.Empty<TrackSectorDefinition>();
            Areas = Array.Empty<TrackAreaDefinition>();
            Shapes = Array.Empty<ShapeDefinition>();
            Portals = Array.Empty<PortalDefinition>();
            Links = Array.Empty<LinkDefinition>();
            Beacons = Array.Empty<TrackBeaconDefinition>();
            Markers = Array.Empty<TrackMarkerDefinition>();
            Approaches = Array.Empty<TrackApproachDefinition>();
            Branches = Array.Empty<TrackBranchDefinition>();
            Walls = Array.Empty<TrackWallDefinition>();
            Materials = Array.Empty<TrackMaterialDefinition>();
            Rooms = Array.Empty<TrackRoomDefinition>();
        }

        public TrackMapDefinition(
            TrackMapMetadata metadata,
            List<TrackSectorDefinition> sectors,
            List<TrackAreaDefinition> areas,
            List<ShapeDefinition> shapes,
            List<PortalDefinition> portals,
            List<LinkDefinition> links,
            List<TrackBeaconDefinition> beacons,
            List<TrackMarkerDefinition> markers,
            List<TrackApproachDefinition> approaches,
            List<TrackBranchDefinition> branches,
            List<TrackWallDefinition> walls,
            List<TrackMaterialDefinition> materials,
            List<TrackRoomDefinition> rooms)
        {
            Metadata = metadata;
            Sectors = sectors ?? new List<TrackSectorDefinition>();
            Areas = areas ?? new List<TrackAreaDefinition>();
            Shapes = shapes ?? new List<ShapeDefinition>();
            Portals = portals ?? new List<PortalDefinition>();
            Links = links ?? new List<LinkDefinition>();
            Beacons = beacons ?? new List<TrackBeaconDefinition>();
            Markers = markers ?? new List<TrackMarkerDefinition>();
            Approaches = approaches ?? new List<TrackApproachDefinition>();
            Branches = branches ?? new List<TrackBranchDefinition>();
            Walls = walls ?? new List<TrackWallDefinition>();
            Materials = materials ?? new List<TrackMaterialDefinition>();
            Rooms = rooms ?? new List<TrackRoomDefinition>();
        }

        public TrackMapMetadata Metadata { get; }
        public IReadOnlyList<TrackSectorDefinition> Sectors { get; }
        public IReadOnlyList<TrackAreaDefinition> Areas { get; }
        public IReadOnlyList<ShapeDefinition> Shapes { get; }
        public IReadOnlyList<PortalDefinition> Portals { get; }
        public IReadOnlyList<LinkDefinition> Links { get; }
        public IReadOnlyList<TrackBeaconDefinition> Beacons { get; }
        public IReadOnlyList<TrackMarkerDefinition> Markers { get; }
        public IReadOnlyList<TrackApproachDefinition> Approaches { get; }
        public IReadOnlyList<TrackBranchDefinition> Branches { get; }
        public IReadOnlyList<TrackWallDefinition> Walls { get; }
        public IReadOnlyList<TrackMaterialDefinition> Materials { get; }
        public IReadOnlyList<TrackRoomDefinition> Rooms { get; }
    }

    public static class TrackMapFormat
    {
        private const string MapExtension = ".tsm";
        private static readonly HashSet<string> SectorKnownKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "id",
            "type",
            "name",
            "code",
            "area",
            "shape",
            "material",
            "noise",
            "flags",
            "flag",
            "caps",
            "capabilities"
        };
        private static readonly HashSet<string> AreaKnownKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "id",
            "type",
            "name",
            "shape",
            "material",
            "noise",
            "width",
            "elevation",
            "height",
            "ceiling",
            "ceiling_height",
            "room",
            "reverb_time",
            "reverb_gain",
            "hf_decay_ratio",
            "early_reflections_gain",
            "late_reverb_gain",
            "diffusion",
            "air_absorption",
            "occlusion_scale",
            "transmission_scale",
            "flags",
            "flag",
            "caps",
            "capabilities"
        };
        private static readonly HashSet<string> BeaconKnownKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "id",
            "type",
            "role",
            "name",
            "name2",
            "secondary",
            "sector",
            "object",
            "shape",
            "x",
            "z",
            "heading",
            "orientation",
            "radius",
            "activation_radius"
        };
        private static readonly HashSet<string> MarkerKnownKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "id",
            "type",
            "name",
            "shape",
            "x",
            "z",
            "heading",
            "orientation"
        };
        private static readonly HashSet<string> ApproachKnownKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "id",
            "sector",
            "name",
            "entry",
            "exit",
            "entry_portal",
            "exit_portal",
            "runway_entry",
            "runway_exit",
            "taxi_entry",
            "taxi_exit",
            "gate_entry",
            "gate_exit",
            "entry_heading",
            "entry_dir",
            "entry_direction",
            "exit_heading",
            "exit_dir",
            "exit_direction",
            "approach_heading",
            "threshold_heading",
            "width",
            "lane_width",
            "approach_width",
            "length",
            "approach_length",
            "tolerance",
            "alignment_tolerance",
            "align_tol"
        };
        private static readonly HashSet<string> WallKnownKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "id",
            "name",
            "shape",
            "width",
            "wall_width",
            "height",
            "wall_height",
            "material",
            "collision",
            "collision_mode",
            "behavior",
            "mode"
        };
        private static readonly HashSet<string> MaterialKnownKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "id",
            "name",
            "preset",
            "absorption",
            "absorption_low",
            "absorption_mid",
            "absorption_high",
            "scattering",
            "transmission",
            "transmission_low",
            "transmission_mid",
            "transmission_high",
            "collision",
            "collision_material"
        };
        private static readonly HashSet<string> RoomKnownKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "id",
            "name",
            "preset",
            "reverb_time",
            "reverb_gain",
            "hf_decay_ratio",
            "early_reflections_gain",
            "late_reverb_gain",
            "diffusion",
            "air_absorption",
            "occlusion_scale",
            "transmission_scale"
        };

        public static bool TryResolvePath(string nameOrPath, out string path)
        {
            path = string.Empty;
            if (string.IsNullOrWhiteSpace(nameOrPath))
                return false;

            if (nameOrPath.IndexOfAny(new[] { '\\', '/' }) >= 0)
            {
                path = nameOrPath;
                return File.Exists(path) && LooksLikeMap(path);
            }

            if (!Path.HasExtension(nameOrPath))
            {
                path = Path.Combine(AppContext.BaseDirectory, "Tracks", nameOrPath + MapExtension);
                return File.Exists(path);
            }

            path = Path.Combine(AppContext.BaseDirectory, "Tracks", nameOrPath);
            return File.Exists(path) && LooksLikeMap(path);
        }

        public static bool TryParse(string nameOrPath, out TrackMapDefinition? map, out List<TrackMapIssue> issues)
        {
            issues = new List<TrackMapIssue>();
            map = null;

            if (!TryResolvePath(nameOrPath, out var path))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Track map not found."));
                return false;
            }

            var metadata = new TrackMapMetadata();
            var sectors = new List<TrackSectorDefinition>();
            var areas = new List<TrackAreaDefinition>();
            var shapes = new List<ShapeDefinition>();
            var portals = new List<PortalDefinition>();
            var links = new List<LinkDefinition>();
            var beacons = new List<TrackBeaconDefinition>();
            var markers = new List<TrackMarkerDefinition>();
            var approaches = new List<TrackApproachDefinition>();
            var walls = new List<TrackWallDefinition>();
            var branches = new List<TrackBranchDefinition>();
            var materials = new List<TrackMaterialDefinition>();
            var rooms = new List<TrackRoomDefinition>();
            var guideBlocks = new List<SectionBlock>();
            var branchBlocks = new List<SectionBlock>();

            var blocks = ReadBlocks(path, issues);
            foreach (var block in blocks)
            {
                switch (block.Name)
                {
                    case "meta":
                        ApplyMeta(metadata, block, issues);
                        break;
                    case "cell":
                    case "line":
                    case "rect":
                        issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Grid cell sections are no longer supported. Use shapes and areas instead.", block.StartLine));
                        break;
                    case "sector":
                        ApplySector(sectors, block, issues);
                        break;
                    case "junction":
                        ApplyJunction(sectors, block, issues);
                        break;
                    case "area":
                        ApplyArea(metadata, areas, block, issues);
                        break;
                    case "shape":
                        ApplyShape(shapes, block, issues);
                        break;
                    case "portal":
                        ApplyPortal(portals, block, issues);
                        break;
                    case "link":
                        ApplyLink(links, block, issues);
                        break;
                    case "path":
                        issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Path sections are not supported. Use areas with shapes instead.", block.StartLine));
                        break;
                    case "lane":
                        issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Lane sections are not supported. Use area metadata for optional lane guidance.", block.StartLine));
                        break;
                    case "beacon":
                        ApplyBeacon(beacons, block, issues);
                        break;
                    case "marker":
                        ApplyMarker(markers, block, issues);
                        break;
                    case "approach":
                        ApplyApproach(approaches, block, issues);
                        break;
                    case "guide":
                    case "guidance":
                        guideBlocks.Add(block);
                        break;
                    case "branch":
                        branchBlocks.Add(block);
                        break;
                    case "wall":
                        ApplyWall(metadata, walls, block, issues);
                        break;
                    case "material":
                        ApplyMaterial(materials, block, issues);
                        break;
                    case "acoustic_material":
                        issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "acoustic_material sections are not supported. Use [material] instead.", block.StartLine));
                        break;
                    case "room":
                        ApplyRoom(rooms, block, issues);
                        break;
                    case "turn":
                        ApplyTurn(metadata, sectors, areas, shapes, portals, approaches, block, issues);
                        break;
                    case "curve":
                        issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Curve sections are not supported. Use areas, portals, and approach metadata instead.", block.StartLine));
                        break;
                }
            }

            if (guideBlocks.Count > 0)
                ApplyGuides(guideBlocks, sectors, areas, shapes, portals, approaches, issues);
            if (branchBlocks.Count > 0)
                ApplyBranches(branchBlocks, sectors, areas, portals, branches, issues);

            map = new TrackMapDefinition(metadata, sectors, areas, shapes, portals, links, beacons, markers, approaches, branches, walls, materials, rooms);
            return issues.All(issue => issue.Severity != TrackMapIssueSeverity.Error);
        }

        public static TrackMapDefinition Parse(string nameOrPath)
        {
            if (!TryParse(nameOrPath, out var map, out var issues) || map == null)
            {
                var message = issues.Count > 0 ? issues[0].Message : "Failed to parse track map.";
                throw new InvalidDataException(message);
            }

            return map;
        }

        private static bool LooksLikeMap(string nameOrPath)
        {
            return string.Equals(Path.GetExtension(nameOrPath), MapExtension, StringComparison.OrdinalIgnoreCase);
        }

        private static string StripComments(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return string.Empty;
            var trimmed = line.Trim();
            if (trimmed.StartsWith("#", StringComparison.Ordinal) ||
                trimmed.StartsWith(";", StringComparison.Ordinal))
                return string.Empty;
            var hash = trimmed.IndexOf('#');
            if (hash >= 0)
                trimmed = trimmed.Substring(0, hash);
            var semi = trimmed.IndexOf(';');
            if (semi >= 0)
                trimmed = trimmed.Substring(0, semi);
            return trimmed.Trim();
        }

        private sealed class SectionBlock
        {
            public SectionBlock(string name, string? argument, int startLine)
            {
                Name = name;
                Argument = string.IsNullOrWhiteSpace(argument) ? null : argument;
                StartLine = startLine;
                Values = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            }

            public string Name { get; }
            public string? Argument { get; }
            public int StartLine { get; }
            public Dictionary<string, List<string>> Values { get; }

            public void AddValue(string key, string value)
            {
                if (!Values.TryGetValue(key, out var list))
                {
                    list = new List<string>();
                    Values[key] = list;
                }
                list.Add(value);
            }
        }

        private static List<SectionBlock> ReadBlocks(string path, List<TrackMapIssue> issues)
        {
            var blocks = new List<SectionBlock>();
            SectionBlock? current = null;
            var lineNumber = 0;

            foreach (var rawLine in File.ReadLines(path))
            {
                lineNumber++;
                var line = StripComments(rawLine);
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                if (TryReadSectionHeader(line, out var name, out var argument))
                {
                    current = new SectionBlock(name, argument, lineNumber);
                    blocks.Add(current);
                    continue;
                }

                if (current == null)
                {
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Warning, "Value outside of a section.", lineNumber));
                    continue;
                }

                if (!TryParseKeyValue(line, out var key, out var value))
                {
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Warning, "Invalid key/value line.", lineNumber));
                    continue;
                }

                current.AddValue(key, value);
            }

            return blocks;
        }

        private static bool TryReadSectionHeader(string line, out string name, out string? argument)
        {
            name = string.Empty;
            argument = null;
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("[", StringComparison.Ordinal) ||
                !trimmed.EndsWith("]", StringComparison.Ordinal))
                return false;

            var content = trimmed.Substring(1, trimmed.Length - 2).Trim();
            if (string.IsNullOrWhiteSpace(content))
                return false;

            var separatorIndex = content.IndexOf(':');
            if (separatorIndex < 0)
                separatorIndex = content.IndexOf(' ');

            if (separatorIndex >= 0)
            {
                name = content.Substring(0, separatorIndex).Trim().ToLowerInvariant();
                argument = content.Substring(separatorIndex + 1).Trim().Trim('"');
            }
            else
            {
                name = content.Trim().ToLowerInvariant();
            }

            return !string.IsNullOrWhiteSpace(name);
        }

        private static bool TryParseKeyValue(string line, out string key, out string value)
        {
            key = string.Empty;
            value = string.Empty;
            var idx = line.IndexOf('=');
            if (idx <= 0)
                return false;
            key = line.Substring(0, idx).Trim().ToLowerInvariant();
            value = line.Substring(idx + 1).Trim().Trim('"');
            return !string.IsNullOrWhiteSpace(key);
        }

        private static void ApplyMeta(TrackMapMetadata metadata, SectionBlock block, List<TrackMapIssue> issues)
        {
            if (block.Values.ContainsKey("default_surface") ||
                block.Values.ContainsKey("safe_zone_surface") ||
                block.Values.ContainsKey("outer_ring_surface"))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Surface keys are not supported in meta. Use default_material, safe_zone_material, or outer_ring_material instead.", block.StartLine));
                return;
            }

            if (TryGetValue(block, "name", out var name) && !string.IsNullOrWhiteSpace(name))
                metadata.Name = name.Trim().Trim('"');

            if (TryGetValue(block, "cell_size", out var cellSizeRaw) || TryGetValue(block, "cellsize", out cellSizeRaw))
            {
                if (TryFloat(cellSizeRaw, out var cellSize))
                    metadata.CellSizeMeters = Math.Max(0.1f, cellSize);
            }

            if (TryGetValue(block, "default_material", out var defaultMaterial) &&
                !string.IsNullOrWhiteSpace(defaultMaterial))
            {
                metadata.DefaultMaterialId = defaultMaterial.Trim();
                metadata.DefaultMaterialDefined = true;
            }
            else if (block.Values.ContainsKey("default_material"))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "default_material cannot be empty.", block.StartLine));
                return;
            }

            if (TryGetValue(block, "default_noise", out var defaultNoise) &&
                Enum.TryParse(defaultNoise, true, out TrackNoise noise))
                metadata.DefaultNoise = noise;

            if (TryGetValue(block, "default_width", out var defaultWidth) && TryFloat(defaultWidth, out var width))
                metadata.DefaultWidthMeters = Math.Max(0.5f, width);

            if (TryGetValue(block, "base_height", out var baseHeightRaw) && TryFloat(baseHeightRaw, out var baseHeight))
                metadata.BaseHeightMeters = baseHeight;

            if (TryGetValue(block, "default_area_height", out var defaultAreaHeightRaw) &&
                TryFloat(defaultAreaHeightRaw, out var defaultAreaHeight))
                metadata.DefaultAreaHeightMeters = defaultAreaHeight;

            if (TryGetValue(block, "default_ceiling_height", out var defaultCeilingRaw) &&
                TryFloat(defaultCeilingRaw, out var defaultCeilingHeight))
                metadata.DefaultCeilingHeightMeters = defaultCeilingHeight;

            if (TryGetValue(block, "weather", out var weatherRaw) &&
                Enum.TryParse(weatherRaw, true, out TrackWeather weather))
                metadata.Weather = weather;

            if (TryGetValue(block, "ambience", out var ambienceRaw) &&
                Enum.TryParse(ambienceRaw, true, out TrackAmbience ambience))
                metadata.Ambience = ambience;

            if (TryGetValue(block, "start_x", out var startXRaw) && TryFloat(startXRaw, out var startX))
                metadata.StartX = startX;

            if (TryGetValue(block, "start_z", out var startZRaw) && TryFloat(startZRaw, out var startZ))
                metadata.StartZ = startZ;

            if (TryReadHeading(block, "start", out var headingDegrees))
            {
                metadata.StartHeadingDegrees = NormalizeDegrees(headingDegrees);
                if (TryDirectionFromDegrees(metadata.StartHeadingDegrees, out var headingDir))
                    metadata.StartHeading = headingDir;
            }

            if (TryGetValue(block, "safe_zone_ring", out var ringRaw) ||
                TryGetValue(block, "safe_zone_ring_meters", out ringRaw) ||
                TryGetValue(block, "safe_zone_band", out ringRaw))
            {
                if (TryFloat(ringRaw, out var ringMeters))
                    metadata.SafeZoneRingMeters = Math.Max(0f, ringMeters);
            }

            if (TryGetValue(block, "safe_zone_material", out var safeMaterial) &&
                !string.IsNullOrWhiteSpace(safeMaterial))
                metadata.SafeZoneMaterialId = safeMaterial.Trim();

            if (TryGetValue(block, "safe_zone_noise", out var safeNoise) &&
                Enum.TryParse(safeNoise, true, out TrackNoise safeNoiseValue))
                metadata.SafeZoneNoise = safeNoiseValue;

            if (TryGetValue(block, "safe_zone_name", out var safeName))
            {
                var trimmedName = safeName?.Trim();
                metadata.SafeZoneName = string.IsNullOrWhiteSpace(trimmedName) ? null : trimmedName;
            }

            if (TryGetValue(block, "outer_ring", out var outerRingRaw) ||
                TryGetValue(block, "outer_ring_meters", out outerRingRaw) ||
                TryGetValue(block, "outer_ring_band", out outerRingRaw))
            {
                if (TryFloat(outerRingRaw, out var ringMeters))
                    metadata.OuterRingMeters = Math.Max(0f, ringMeters);
            }

            if (TryGetValue(block, "outer_ring_material", out var outerMaterial) &&
                !string.IsNullOrWhiteSpace(outerMaterial))
                metadata.OuterRingMaterialId = outerMaterial.Trim();

            if (TryGetValue(block, "outer_ring_noise", out var outerNoise) &&
                Enum.TryParse(outerNoise, true, out TrackNoise outerNoiseValue))
                metadata.OuterRingNoise = outerNoiseValue;

            if (TryGetValue(block, "outer_ring_name", out var outerName))
            {
                var trimmedName = outerName?.Trim();
                metadata.OuterRingName = string.IsNullOrWhiteSpace(trimmedName) ? null : trimmedName;
            }

            if (TryGetValue(block, "outer_ring_type", out var outerTypeRaw) &&
                Enum.TryParse(outerTypeRaw, true, out TrackAreaType outerType))
                metadata.OuterRingType = outerType;

            if (TryGetValue(block, "outer_ring_flags", out var outerFlagsRaw) &&
                TryParseAreaFlags(outerFlagsRaw, out var outerFlags))
                metadata.OuterRingFlags = outerFlags;

            if (metadata.DefaultAreaHeightMeters.HasValue && metadata.DefaultAreaHeightMeters.Value <= 0f)
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "default_area_height must be greater than zero.", block.StartLine));

            if (metadata.DefaultCeilingHeightMeters.HasValue &&
                metadata.BaseHeightMeters.HasValue &&
                metadata.DefaultCeilingHeightMeters.Value <= metadata.BaseHeightMeters.Value)
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "default_ceiling_height must be above base_height.", block.StartLine));
        }

        private static void ApplyShape(
            List<ShapeDefinition> shapes,
            SectionBlock block,
            List<TrackMapIssue> issues)
        {
            if (!TryReadId(block, out var id))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Shape requires an id.", block.StartLine));
                return;
            }

            if (shapes.Any(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase)))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Duplicate shape id '{id}'.", block.StartLine));
                return;
            }

            if (!TryGetValue(block, "type", out var rawType) ||
                string.IsNullOrWhiteSpace(rawType) ||
                !TryShapeType(rawType, out var shapeType))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Shape requires a valid type.", block.StartLine));
                return;
            }

            switch (shapeType)
            {
                case ShapeType.Rectangle:
                    if (!TryFloat(block, "x", out var rectX) ||
                        !TryFloat(block, "z", out var rectZ) ||
                        !TryFloat(block, "width", out var rectWidth) ||
                        !TryFloat(block, "height", out var rectHeight) ||
                        rectWidth <= 0f || rectHeight <= 0f)
                    {
                        issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Rectangle requires x, z, width, height.", block.StartLine));
                        return;
                    }
                    shapes.Add(new ShapeDefinition(id, shapeType, rectX, rectZ, rectWidth, rectHeight));
                    break;
                case ShapeType.Circle:
                    if (!TryFloat(block, "x", out var circleX) ||
                        !TryFloat(block, "z", out var circleZ) ||
                        !TryFloat(block, "radius", out var circleRadius) ||
                        circleRadius <= 0f)
                    {
                        issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Circle requires x, z, radius.", block.StartLine));
                        return;
                    }
                    shapes.Add(new ShapeDefinition(id, shapeType, circleX, circleZ, radius: circleRadius));
                    break;
                case ShapeType.Ring:
                    if (TryFloat(block, "radius", out var ringRadius))
                    {
                        if (!TryFloat(block, "x", out var ringX) ||
                            !TryFloat(block, "z", out var ringZ) ||
                            !TryFloat(block, "ring_width", out var ringWidth) ||
                            ringRadius <= 0f || ringWidth <= 0f)
                        {
                            issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Ring circle requires x, z, radius, ring_width.", block.StartLine));
                            return;
                        }
                        shapes.Add(new ShapeDefinition(id, shapeType, ringX, ringZ, radius: ringRadius, ringWidth: ringWidth));
                    }
                    else
                    {
                        if (!TryFloat(block, "x", out var ringRectX) ||
                            !TryFloat(block, "z", out var ringRectZ) ||
                            !TryFloat(block, "width", out var ringRectWidth) ||
                            !TryFloat(block, "height", out var ringRectHeight) ||
                            !TryFloat(block, "ring_width", out var ringRectRing) ||
                            ringRectWidth <= 0f || ringRectHeight <= 0f || ringRectRing <= 0f)
                        {
                            issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Ring rectangle requires x, z, width, height, ring_width.", block.StartLine));
                            return;
                        }
                        shapes.Add(new ShapeDefinition(id, shapeType, ringRectX, ringRectZ, ringRectWidth, ringRectHeight, ringWidth: ringRectRing));
                    }
                    break;
                case ShapeType.Polygon:
                case ShapeType.Polyline:
                    if (!TryParsePoints(block, out var points))
                    {
                        issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Shape requires points.", block.StartLine));
                        return;
                    }
                    if (shapeType == ShapeType.Polygon && points.Count < 3)
                    {
                        issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Polygon requires at least 3 points.", block.StartLine));
                        return;
                    }
                    if (shapeType == ShapeType.Polyline && points.Count < 2)
                    {
                        issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Polyline requires at least 2 points.", block.StartLine));
                        return;
                    }
                    shapes.Add(new ShapeDefinition(id, shapeType, points: points));
                    break;
                default:
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Shape '{id}' has unsupported type '{rawType}'.", block.StartLine));
                    break;
            }
        }

        private static void ApplySector(
            List<TrackSectorDefinition> sectors,
            SectionBlock block,
            List<TrackMapIssue> issues)
        {
            if (block.Values.ContainsKey("surface"))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Sector surface is not supported. Use material instead.", block.StartLine));
                return;
            }

            if (!TryReadId(block, out var id))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Sector requires an id.", block.StartLine));
                return;
            }

            if (!TryGetValue(block, "type", out var rawType) ||
                string.IsNullOrWhiteSpace(rawType) ||
                !Enum.TryParse(rawType, true, out TrackSectorType sectorType))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Sector requires a valid type.", block.StartLine));
                return;
            }

            if (sectors.Any(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase)))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Duplicate sector id '{id}'.", block.StartLine));
                return;
            }

            var name = TryGetValue(block, "name", out var nameValue) ? nameValue : null;
            var code = TryGetValue(block, "code", out var codeValue) ? codeValue : null;
            var areaId = TryGetValue(block, "area", out var areaValue) ? areaValue :
                (TryGetValue(block, "shape", out areaValue) ? areaValue : null);
            var materialId = TryMaterialId(block, "material", out var materialValue) ? materialValue : null;
            var noise = TryNoise(block, "noise", out var noiseValue) ? noiseValue : (TrackNoise?)null;
            var flags = TrySectorFlags(block, out var sectorFlags) ? sectorFlags : TrackSectorFlags.None;
            var metadata = CollectSectorMetadata(block);

            if (HasGuideKeys(block))
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Sector '{id}' contains guide keys. Use a [guide] section instead.", block.StartLine));
            if (HasBranchKeys(block))
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Sector '{id}' contains branch keys. Use a [branch] section instead.", block.StartLine));

            sectors.Add(new TrackSectorDefinition(id, sectorType, name, areaId, code, materialId, noise, flags, metadata));
        }

        private static void ApplyJunction(
            List<TrackSectorDefinition> sectors,
            SectionBlock block,
            List<TrackMapIssue> issues)
        {
            if (!block.Values.ContainsKey("type"))
                block.AddValue("type", "intersection");
            ApplySector(sectors, block, issues);
        }

        private static void ApplyArea(
            TrackMapMetadata metadata,
            List<TrackAreaDefinition> areas,
            SectionBlock block,
            List<TrackMapIssue> issues)
        {
            if (block.Values.ContainsKey("surface") ||
                block.Values.ContainsKey("acoustic_material") ||
                block.Values.ContainsKey("audio_material") ||
                block.Values.ContainsKey("sound_material") ||
                block.Values.ContainsKey("wall_acoustic_material") ||
                block.Values.ContainsKey("wall_audio_material") ||
                block.Values.ContainsKey("wall_sound_material"))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Area surface/acoustic_material is not supported. Use material instead.", block.StartLine));
                return;
            }

            if (!TryReadId(block, out var id))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Area requires an id.", block.StartLine));
                return;
            }

            if (!TryGetValue(block, "type", out var rawType) ||
                string.IsNullOrWhiteSpace(rawType) ||
                !Enum.TryParse(rawType, true, out TrackAreaType areaType))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Area requires a valid type.", block.StartLine));
                return;
            }

            if (areas.Any(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase)))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Duplicate area id '{id}'.", block.StartLine));
                return;
            }

            if (!TryGetValue(block, "shape", out var shapeId) || string.IsNullOrWhiteSpace(shapeId))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Area requires a shape id.", block.StartLine));
                return;
            }

            if (TryGetValue(block, "invert", out _) ||
                TryGetValue(block, "outside", out _) ||
                TryGetValue(block, "outside_of", out _))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Area invert/outside is not supported. Use a ring shape with ring_width.", block.StartLine));
                return;
            }

            var name = TryGetValue(block, "name", out var nameValue) ? nameValue : null;
            var roomId = TryGetValue(block, "room", out var roomValue) ? roomValue?.Trim() : null;
            if (string.IsNullOrWhiteSpace(roomId))
                roomId = null;
            var roomOverrides = ReadRoomOverrides(block, out var hasRoomOverrides);
            var materialId = TryMaterialId(block, "material", out var materialValue) ? materialValue : null;
            if (string.IsNullOrWhiteSpace(materialId))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Area '{id}' requires material.", block.StartLine));
                return;
            }
            var noise = TryNoise(block, "noise", out var noiseValue) ? noiseValue : (TrackNoise?)null;
            var width = TryFloat(block, "width", out var widthValue) ? Math.Max(0.1f, widthValue) : (float?)null;
            var flags = TryAreaFlags(block, out var areaFlags) ? areaFlags : TrackAreaFlags.None;
            var areaMetadata = CollectAreaMetadata(block);

            var hasElevation = TryFloatAny(block, out var elevationValue, "elevation");
            if (!hasElevation && !metadata.BaseHeightMeters.HasValue)
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Area '{id}' requires elevation or meta base_height.", block.StartLine));
                return;
            }

            var elevationMeters = hasElevation ? elevationValue : metadata.BaseHeightMeters!.Value;

            var hasHeight = TryFloatAny(block, out var heightValue, "height");
            if (!hasHeight && !metadata.DefaultAreaHeightMeters.HasValue)
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Area '{id}' requires height or meta default_area_height.", block.StartLine));
                return;
            }

            var heightMeters = hasHeight ? heightValue : metadata.DefaultAreaHeightMeters!.Value;
            if (heightMeters <= 0f)
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Area '{id}' height must be greater than zero.", block.StartLine));
                return;
            }

            var ceilingMeters = (float?)null;
            if (TryFloatAny(block, out var ceilingValue, "ceiling_height", "ceiling"))
                ceilingMeters = ceilingValue;
            else if (metadata.DefaultCeilingHeightMeters.HasValue)
                ceilingMeters = metadata.DefaultCeilingHeightMeters.Value;

            if (ceilingMeters.HasValue && ceilingMeters.Value <= elevationMeters)
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Area '{id}' ceiling_height must be above elevation.", block.StartLine));
                return;
            }

            if (hasRoomOverrides && string.IsNullOrWhiteSpace(roomId))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Area '{id}' has room overrides but no room id.", block.StartLine));
                return;
            }

            if (ceilingMeters.HasValue &&
                TryFloatAny(block, out var wallHeightValue, "wall_height", "wall_height_m"))
            {
                var expectedWallHeight = ceilingMeters.Value - elevationMeters;
                if (Math.Abs(wallHeightValue - expectedWallHeight) > 0.01f)
                {
                    issues.Add(new TrackMapIssue(
                        TrackMapIssueSeverity.Error,
                        $"Area '{id}' has ceiling_height; wall_height must match {expectedWallHeight.ToString(CultureInfo.InvariantCulture)}.",
                        block.StartLine));
                    return;
                }
            }

            if (HasGuideKeys(block))
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Area '{id}' contains guide keys. Use a [guide] section instead.", block.StartLine));
            if (HasBranchKeys(block))
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Area '{id}' contains branch keys. Use a [branch] section instead.", block.StartLine));

            areas.Add(new TrackAreaDefinition(id, areaType, shapeId, elevationMeters, heightMeters, ceilingMeters, roomId, roomOverrides, name, materialId, noise, width, flags, areaMetadata));
        }

        private static bool HasGuideKeys(SectionBlock block)
        {
            foreach (var key in block.Values.Keys)
            {
                if (IsGuideKey(key))
                    return true;
            }
            return false;
        }

        private static bool HasBranchKeys(SectionBlock block)
        {
            foreach (var key in block.Values.Keys)
            {
                if (IsBranchKey(key))
                    return true;
            }
            return false;
        }

        private static bool IsGuideKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return false;

            switch (key.Trim().ToLowerInvariant())
            {
                case "entry_portal":
                case "exit_portal":
                case "entries":
                case "entry_portals":
                case "entry_list":
                case "entry_headings":
                case "exits":
                case "exit_portals":
                case "exit_list":
                case "exit_headings":
                case "exit_names":
                case "pairing":
                case "entry_heading":
                case "exit_heading":
                case "side":
                case "sides":
                case "range":
                case "beacon_range":
                case "beacon_mode":
                case "beacon_shape":
                case "turn_range":
                case "turn_shape":
                case "centerline_shape":
                case "entry_enabled":
                case "exit_enabled":
                case "beacon_enabled":
                case "turn_enabled":
                case "enabled":
                    return true;
            }

            return false;
        }

        private static bool IsBranchKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return false;

            switch (key.Trim().ToLowerInvariant())
            {
                case "role":
                case "entry_portal":
                case "entry_heading":
                case "entries":
                case "entry_portals":
                case "entry_list":
                case "entry_headings":
                case "exits":
                case "exit_portals":
                case "exit_list":
                case "exit_headings":
                case "exit_names":
                case "preferred_exit":
                    return true;
            }

            return false;
        }

        private static void ApplyGuides(
            List<SectionBlock> blocks,
            List<TrackSectorDefinition> sectors,
            List<TrackAreaDefinition> areas,
            List<ShapeDefinition> shapes,
            List<PortalDefinition> portals,
            List<TrackApproachDefinition> approaches,
            List<TrackMapIssue> issues)
        {
            if (blocks == null || blocks.Count == 0)
                return;

            var sectorsById = new Dictionary<string, TrackSectorDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (var sector in sectors)
            {
                if (sector == null)
                    continue;
                sectorsById[sector.Id] = sector;
            }

            var portalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var portal in portals)
            {
                if (portal == null || string.IsNullOrWhiteSpace(portal.Id))
                    continue;
                portalIds.Add(portal.Id);
            }

            var sectorsByArea = new Dictionary<string, List<TrackSectorDefinition>>(StringComparer.OrdinalIgnoreCase);
            foreach (var sector in sectors)
            {
                if (sector == null || string.IsNullOrWhiteSpace(sector.AreaId))
                    continue;
                if (!sectorsByArea.TryGetValue(sector.AreaId!, out var list))
                {
                    list = new List<TrackSectorDefinition>();
                    sectorsByArea[sector.AreaId!] = list;
                }
                list.Add(sector);
            }

            var shapeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var shape in shapes)
            {
                if (shape == null || string.IsNullOrWhiteSpace(shape.Id))
                    continue;
                shapeIds.Add(shape.Id);
            }

            var guideIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var block in blocks)
            {
                var id = TryReadId(block, out var idValue) ? idValue : "(unnamed)";
                if (!string.IsNullOrWhiteSpace(idValue))
                {
                    if (!guideIds.Add(idValue))
                    {
                        issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Duplicate guide id '{idValue}'.", block.StartLine));
                        continue;
                    }
                }
                var areaId = TryGetValue(block, "area", out var areaValue) ? areaValue?.Trim() : null;
                var sectorId = TryGetValue(block, "sector", out var sectorValue) ? sectorValue?.Trim() : null;

                List<TrackSectorDefinition> targets = new();
                if (!string.IsNullOrWhiteSpace(sectorId))
                {
                    if (!sectorsById.TryGetValue(sectorId!, out var sector))
                    {
                        issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Guide '{id}' references unknown sector '{sectorId}'.", block.StartLine));
                        continue;
                    }
                    if (!string.IsNullOrWhiteSpace(areaId) &&
                        !string.Equals(sector.AreaId, areaId, StringComparison.OrdinalIgnoreCase))
                    {
                        issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Guide '{id}' area '{areaId}' does not match sector '{sector.Id}'.", block.StartLine));
                        continue;
                    }
                    targets.Add(sector);
                }
                else if (!string.IsNullOrWhiteSpace(areaId))
                {
                    if (!sectorsByArea.TryGetValue(areaId!, out var list) || list.Count == 0)
                    {
                        issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Guide '{id}' references area '{areaId}' with no sectors.", block.StartLine));
                        continue;
                    }
                    if (list.Count > 1)
                    {
                        issues.Add(new TrackMapIssue(
                            TrackMapIssueSeverity.Error,
                            $"Guide '{id}' references area '{areaId}' used by multiple sectors. Specify sector.",
                            block.StartLine));
                        continue;
                    }
                    targets.AddRange(list);
                }
                else
                {
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Guide '{id}' requires area or sector.", block.StartLine));
                    continue;
                }

                var name = TryGetValue(block, "name", out var nameValue) ? nameValue : null;
                var entryPortal = TryGetValue(block, "entry_portal", out var entryPortalValue) ? entryPortalValue : null;
                var exitPortal = TryGetValue(block, "exit_portal", out var exitPortalValue) ? exitPortalValue : null;
                var entryHeading = TryReadHeading(block, "entry_heading", out var entryHeadingValue) ? entryHeadingValue : (float?)null;
                var exitHeading = TryReadHeading(block, "exit_heading", out var exitHeadingValue) ? exitHeadingValue : (float?)null;
                var width = TryFloat(block, "width", out var widthValue) ? Math.Max(0.1f, widthValue) : (float?)null;
                var length = TryFloat(block, "length", out var lengthValue) ? Math.Max(0.1f, lengthValue) : (float?)null;
                var tolerance = TryFloat(block, "tolerance", out var toleranceValue) ? Math.Max(0f, toleranceValue) : (float?)null;

                var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (TryGetValue(block, "side", out var side))
                    metadata["approach_side"] = side.Trim();
                if (TryGetValue(block, "sides", out var sides))
                    metadata["approach_sides"] = sides.Trim();
                if (TryGetValue(block, "range", out var range))
                    metadata["approach_range"] = range.Trim();
                if (TryGetValue(block, "beacon_range", out var beaconRange))
                    metadata["beacon_range"] = beaconRange.Trim();
                if (TryGetValue(block, "beacon_mode", out var beaconMode))
                    metadata["beacon_mode"] = beaconMode.Trim();
                if (TryGetValue(block, "beacon_shape", out var beaconShape))
                    metadata["beacon_shape"] = beaconShape.Trim();
                if (TryGetValue(block, "turn_range", out var turnRange))
                    metadata["turn_range"] = turnRange.Trim();
                if (TryGetValue(block, "turn_shape", out var turnShape))
                    metadata["turn_shape"] = turnShape.Trim();
                if (TryGetValue(block, "centerline_shape", out var centerlineShape))
                    metadata["centerline_shape"] = centerlineShape.Trim();
                if (TryGetValue(block, "enabled", out var enabled))
                    metadata["enabled"] = enabled.Trim();
                if (TryGetValue(block, "entry_enabled", out var entryEnabled))
                    metadata["approach_entry"] = entryEnabled.Trim();
                if (TryGetValue(block, "exit_enabled", out var exitEnabled))
                    metadata["approach_exit"] = exitEnabled.Trim();
                if (TryGetValue(block, "beacon_enabled", out var beaconEnabled))
                    metadata["beacon_enabled"] = beaconEnabled.Trim();
                if (TryGetValue(block, "turn_enabled", out var turnEnabled))
                    metadata["turn_enabled"] = turnEnabled.Trim();

                ValidateGuideShape(metadata, shapeIds, "beacon_shape", id, issues, block.StartLine);
                ValidateGuideShape(metadata, shapeIds, "turn_shape", id, issues, block.StartLine);
                ValidateGuideShape(metadata, shapeIds, "centerline_shape", id, issues, block.StartLine);

                var entries = ParsePortalRefs(block, "entries", "entry_portals", "entry_list");
                var exits = ParsePortalRefs(block, "exits", "exit_portals", "exit_list");

                if (entries.Count == 0 && !string.IsNullOrWhiteSpace(entryPortal))
                    entries.Add(new PortalRef(entryPortal!, entryHeading));
                if (exits.Count == 0 && !string.IsNullOrWhiteSpace(exitPortal))
                    exits.Add(new PortalRef(exitPortal!, exitHeading));

                ApplyHeadingsList(entries, block, "entry_headings");
                ApplyHeadingsList(exits, block, "exit_headings");

                foreach (var entry in entries)
                {
                    if (!portalIds.Contains(entry.PortalId))
                        issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Guide '{id}' references missing entry portal '{entry.PortalId}'.", block.StartLine));
                }
                foreach (var exit in exits)
                {
                    if (!portalIds.Contains(exit.PortalId))
                        issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Guide '{id}' references missing exit portal '{exit.PortalId}'.", block.StartLine));
                }

                var pairing = "all";
                if (TryGetValue(block, "pairing", out var pairingValue) && !string.IsNullOrWhiteSpace(pairingValue))
                    pairing = pairingValue.Trim().ToLowerInvariant();

                foreach (var sector in targets)
                {
                    BuildGuideApproaches(
                        approaches,
                        sector.Id,
                        name,
                        entries,
                        exits,
                        pairing,
                        width,
                        length,
                        tolerance,
                        metadata,
                        id,
                        issues,
                        block.StartLine);
                }
            }
        }

        private static void ValidateGuideShape(
            IReadOnlyDictionary<string, string> metadata,
            HashSet<string> shapeIds,
            string key,
            string guideId,
            List<TrackMapIssue> issues,
            int? line)
        {
            if (!metadata.TryGetValue(key, out var value))
                return;
            if (string.IsNullOrWhiteSpace(value))
                return;
            if (!shapeIds.Contains(value.Trim()))
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Guide '{guideId}' references missing shape '{value}' for {key}.", line));
        }

        private readonly struct PortalRef
        {
            public PortalRef(string portalId, float? heading)
            {
                PortalId = portalId;
                Heading = heading;
            }

            public string PortalId { get; }
            public float? Heading { get; }
        }

        private static List<PortalRef> ParsePortalRefs(SectionBlock block, params string[] keys)
        {
            var list = new List<PortalRef>();
            if (!TryGetValue(block, keys[0], out var raw) || string.IsNullOrWhiteSpace(raw))
            {
                for (var i = 1; i < keys.Length; i++)
                {
                    if (TryGetValue(block, keys[i], out raw) && !string.IsNullOrWhiteSpace(raw))
                        break;
                    raw = string.Empty;
                }
            }

            if (string.IsNullOrWhiteSpace(raw))
                return list;

            foreach (var token in SplitTokens(raw))
            {
                if (TryParsePortalToken(token, out var portalId, out var heading))
                    list.Add(new PortalRef(portalId, heading));
            }

            return list;
        }

        private static bool TryParsePortalToken(string token, out string portalId, out float? heading)
        {
            portalId = string.Empty;
            heading = null;
            if (string.IsNullOrWhiteSpace(token))
                return false;

            var trimmed = token.Trim();
            var separator = trimmed.IndexOfAny(new[] { ':', '@' });
            if (separator > 0)
            {
                portalId = trimmed.Substring(0, separator).Trim();
                var headingRaw = trimmed.Substring(separator + 1).Trim();
                if (TryDirection(headingRaw, out var direction))
                    heading = DirectionToDegrees(direction);
                else if (TryFloat(headingRaw, out var headingValue))
                    heading = headingValue;
            }
            else
            {
                portalId = trimmed;
            }

            return !string.IsNullOrWhiteSpace(portalId);
        }

        private static void ApplyHeadingsList(List<PortalRef> list, SectionBlock block, string key)
        {
            if (list.Count == 0)
                return;
            if (!TryGetValue(block, key, out var raw) || string.IsNullOrWhiteSpace(raw))
                return;

            var headings = new List<float>();
            foreach (var token in SplitTokens(raw))
            {
                if (TryDirection(token, out var direction))
                    headings.Add(DirectionToDegrees(direction));
                else if (TryFloat(token, out var value))
                    headings.Add(value);
            }

            if (headings.Count == 0)
                return;

            var count = Math.Min(list.Count, headings.Count);
            for (var i = 0; i < count; i++)
            {
                if (list[i].Heading.HasValue)
                    continue;
                list[i] = new PortalRef(list[i].PortalId, headings[i]);
            }
        }

        private static void BuildGuideApproaches(
            List<TrackApproachDefinition> approaches,
            string sectorId,
            string? name,
            List<PortalRef> entries,
            List<PortalRef> exits,
            string pairing,
            float? width,
            float? length,
            float? tolerance,
            IReadOnlyDictionary<string, string> metadata,
            string guideId,
            List<TrackMapIssue> issues,
            int? line)
        {
            if (entries.Count == 0 && exits.Count == 0)
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Guide '{guideId}' defines no entries or exits.", line));
                return;
            }

            if (entries.Count > 0 && exits.Count > 0)
            {
                if (pairing == "by_index" || pairing == "zip")
                {
                    if (entries.Count != exits.Count)
                    {
                        issues.Add(new TrackMapIssue(
                            TrackMapIssueSeverity.Error,
                            $"Guide '{guideId}' pairing=by_index requires equal entry/exit counts.",
                            line));
                        return;
                    }

                    for (var i = 0; i < entries.Count; i++)
                    {
                        AddGuideApproach(approaches, sectorId, name, entries[i], exits[i], width, length, tolerance, metadata);
                    }
                    return;
                }

                foreach (var entry in entries)
                {
                    foreach (var exit in exits)
                        AddGuideApproach(approaches, sectorId, name, entry, exit, width, length, tolerance, metadata);
                }

                return;
            }

            if (entries.Count > 0)
            {
                foreach (var entry in entries)
                    AddGuideApproach(approaches, sectorId, name, entry, null, width, length, tolerance, metadata);
                return;
            }

            foreach (var exit in exits)
                AddGuideApproach(approaches, sectorId, name, null, exit, width, length, tolerance, metadata);
        }

        private static void AddGuideApproach(
            List<TrackApproachDefinition> approaches,
            string sectorId,
            string? name,
            PortalRef? entry,
            PortalRef? exit,
            float? width,
            float? length,
            float? tolerance,
            IReadOnlyDictionary<string, string> metadata)
        {
            string? entryPortalId = entry?.PortalId;
            float? entryHeading = entry?.Heading;
            string? exitPortalId = exit?.PortalId;
            float? exitHeading = exit?.Heading;

            approaches.Add(new TrackApproachDefinition(
                sectorId,
                name,
                entryPortalId,
                exitPortalId,
                entryHeading,
                exitHeading,
                width,
                length,
                tolerance,
                metadata));
        }

        private static void ApplyBranches(
            List<SectionBlock> blocks,
            List<TrackSectorDefinition> sectors,
            List<TrackAreaDefinition> areas,
            List<PortalDefinition> portals,
            List<TrackBranchDefinition> branches,
            List<TrackMapIssue> issues)
        {
            if (blocks == null || blocks.Count == 0)
                return;

            var sectorsById = new Dictionary<string, TrackSectorDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (var sector in sectors)
            {
                if (sector == null)
                    continue;
                sectorsById[sector.Id] = sector;
            }

            var portalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var portal in portals)
            {
                if (portal == null || string.IsNullOrWhiteSpace(portal.Id))
                    continue;
                portalIds.Add(portal.Id);
            }

            var branchIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var sectorsByArea = new Dictionary<string, List<TrackSectorDefinition>>(StringComparer.OrdinalIgnoreCase);
            foreach (var sector in sectors)
            {
                if (sector == null || string.IsNullOrWhiteSpace(sector.AreaId))
                    continue;
                if (!sectorsByArea.TryGetValue(sector.AreaId!, out var list))
                {
                    list = new List<TrackSectorDefinition>();
                    sectorsByArea[sector.AreaId!] = list;
                }
                list.Add(sector);
            }

            foreach (var block in blocks)
            {
                if (!TryReadId(block, out var branchId))
                {
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Branch requires an id.", block.StartLine));
                    continue;
                }

                var areaId = TryGetValue(block, "area", out var areaValue) ? areaValue?.Trim() : null;
                var sectorId = TryGetValue(block, "sector", out var sectorValue) ? sectorValue?.Trim() : null;

                TrackSectorDefinition? sector = null;
                if (!string.IsNullOrWhiteSpace(sectorId))
                {
                    if (!sectorsById.TryGetValue(sectorId!, out sector))
                    {
                        issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Branch '{branchId}' references unknown sector '{sectorId}'.", block.StartLine));
                        continue;
                    }
                    if (!string.IsNullOrWhiteSpace(areaId) &&
                        !string.Equals(sector.AreaId, areaId, StringComparison.OrdinalIgnoreCase))
                    {
                        issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Branch '{branchId}' area '{areaId}' does not match sector '{sector.Id}'.", block.StartLine));
                        continue;
                    }
                }
                else if (!string.IsNullOrWhiteSpace(areaId))
                {
                    if (!sectorsByArea.TryGetValue(areaId!, out var list) || list.Count == 0)
                    {
                        issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Branch '{branchId}' references area '{areaId}' with no sectors.", block.StartLine));
                        continue;
                    }
                    if (list.Count > 1)
                    {
                        issues.Add(new TrackMapIssue(
                            TrackMapIssueSeverity.Error,
                            $"Branch '{branchId}' references area '{areaId}' used by multiple sectors. Specify sector.",
                            block.StartLine));
                        continue;
                    }
                    sector = list[0];
                }
                else
                {
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Branch '{branchId}' requires area or sector.", block.StartLine));
                    continue;
                }

                var name = TryGetValue(block, "name", out var nameValue) ? nameValue : null;
                var role = TryGetValue(block, "role", out var roleValue) && Enum.TryParse(roleValue, true, out TrackBranchRole parsedRole)
                    ? parsedRole
                    : (sector != null ? InferBranchRole(sector.Type) : TrackBranchRole.Undefined);

                var entryPortal = TryGetValue(block, "entry_portal", out var entryPortalValue) ? entryPortalValue : null;
                var entryHeading = TryReadHeading(block, "entry_heading", out var entryHeadingValue) ? entryHeadingValue : (float?)null;
                var width = TryFloat(block, "width", out var widthValue) ? Math.Max(0.1f, widthValue) : (float?)null;
                var length = TryFloat(block, "length", out var lengthValue) ? Math.Max(0.1f, lengthValue) : (float?)null;
                var tolerance = TryFloat(block, "tolerance", out var toleranceValue) ? Math.Max(0f, toleranceValue) : (float?)null;

                var entries = ParsePortalRefs(block, "entries", "entry_portals", "entry_list");
                if (entries.Count == 0 && !string.IsNullOrWhiteSpace(entryPortal))
                    entries.Add(new PortalRef(entryPortal!, entryHeading));
                ApplyHeadingsList(entries, block, "entry_headings");

                var exits = new List<TrackBranchExitDefinition>();
                var parsedExits = ParseBranchExits(block);
                if (parsedExits.Count > 0)
                    exits.AddRange(parsedExits);
                if (exits.Count == 0 && TryGetValue(block, "exit_portals", out var exitPortalsRaw) && !string.IsNullOrWhiteSpace(exitPortalsRaw))
                {
                    foreach (var token in SplitTokens(exitPortalsRaw))
                    {
                        if (TryParseExitToken(token, out var portalId, out var heading))
                            exits.Add(new TrackBranchExitDefinition(portalId, heading));
                    }
                }

                ApplyExitHeadings(exits, block, "exit_headings");
                ApplyExitNames(exits, block, "exit_names");

                var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (TryGetValue(block, "preferred_exit", out var preferredExit))
                    metadata["preferred_exit"] = preferredExit.Trim();

                foreach (var pair in block.Values)
                {
                    if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value == null || pair.Value.Count == 0)
                        continue;
                    var key = pair.Key.Trim();
                    if (IsBranchKnownKey(key))
                        continue;
                    var raw = pair.Value[pair.Value.Count - 1];
                    if (!string.IsNullOrWhiteSpace(raw))
                        metadata[key] = raw.Trim();
                }

                if (entries.Count == 0)
                {
                    if (!branchIds.Add(branchId))
                    {
                        issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Duplicate branch id '{branchId}'.", block.StartLine));
                        continue;
                    }

                    ValidateBranchPortals(branchId, entryPortal, exits, portalIds, issues, block.StartLine);

                    branches.Add(new TrackBranchDefinition(
                        branchId,
                        sector!.Id,
                        name,
                        role,
                        entryPortal,
                        entryHeading,
                        exits,
                        width,
                        length,
                        tolerance,
                        metadata));
                    continue;
                }

                foreach (var entry in entries)
                {
                    var derivedId = entries.Count == 1 ? branchId : $"{branchId}_{entry.PortalId}";
                    if (!branchIds.Add(derivedId))
                    {
                        issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Duplicate branch id '{derivedId}'.", block.StartLine));
                        continue;
                    }

                    ValidateBranchPortals(derivedId, entry.PortalId, exits, portalIds, issues, block.StartLine);

                    branches.Add(new TrackBranchDefinition(
                        derivedId,
                        sector!.Id,
                        name,
                        role,
                        entry.PortalId,
                        entry.Heading,
                        exits,
                        width,
                        length,
                        tolerance,
                        metadata));
                }
            }
        }

        private static List<TrackBranchExitDefinition> ParseBranchExits(SectionBlock block)
        {
            var exits = new List<TrackBranchExitDefinition>();
            if (!TryGetValue(block, "exits", out var raw) || string.IsNullOrWhiteSpace(raw))
                return exits;

            foreach (var token in SplitTokens(raw))
            {
                if (TryParseExitTokenFull(token, out var portalId, out var heading, out var name))
                    exits.Add(new TrackBranchExitDefinition(portalId, heading, name));
            }

            return exits;
        }

        private static bool TryParseExitTokenFull(string token, out string portalId, out float? heading, out string? name)
        {
            portalId = string.Empty;
            heading = null;
            name = null;
            if (string.IsNullOrWhiteSpace(token))
                return false;

            var trimmed = token.Trim();
            var parts = trimmed.Split(new[] { '@', ':' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return false;

            portalId = parts[0].Trim();
            if (string.IsNullOrWhiteSpace(portalId))
                return false;

            if (parts.Length >= 2)
            {
                var candidate = parts[1].Trim();
                if (TryDirection(candidate, out var direction))
                    heading = DirectionToDegrees(direction);
                else if (TryFloat(candidate, out var headingValue))
                    heading = headingValue;
                else
                    name = candidate;

                if (parts.Length >= 3)
                {
                    var tail = string.Join(":", parts, 2, parts.Length - 2).Trim();
                    if (!string.IsNullOrWhiteSpace(tail))
                        name = tail;
                }
            }

            return true;
        }

        private static void ApplyExitHeadings(List<TrackBranchExitDefinition> exits, SectionBlock block, string key)
        {
            if (exits.Count == 0)
                return;
            if (!TryGetValue(block, key, out var raw) || string.IsNullOrWhiteSpace(raw))
                return;

            var headings = new List<float>();
            foreach (var token in SplitTokens(raw))
            {
                if (TryDirection(token, out var direction))
                    headings.Add(DirectionToDegrees(direction));
                else if (TryFloat(token, out var value))
                    headings.Add(value);
            }

            if (headings.Count == 0)
                return;

            var count = Math.Min(exits.Count, headings.Count);
            for (var i = 0; i < count; i++)
            {
                if (exits[i].HeadingDegrees.HasValue)
                    continue;
                exits[i] = new TrackBranchExitDefinition(exits[i].PortalId, headings[i], exits[i].Name, exits[i].Metadata);
            }
        }

        private static void ApplyExitNames(List<TrackBranchExitDefinition> exits, SectionBlock block, string key)
        {
            if (exits.Count == 0)
                return;
            if (!TryGetValue(block, key, out var raw) || string.IsNullOrWhiteSpace(raw))
                return;

            var names = new List<string>();
            foreach (var token in SplitTokens(raw))
            {
                var trimmed = token.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                    names.Add(trimmed);
            }

            if (names.Count == 0)
                return;

            var count = Math.Min(exits.Count, names.Count);
            for (var i = 0; i < count; i++)
            {
                if (!string.IsNullOrWhiteSpace(exits[i].Name))
                    continue;
                exits[i] = new TrackBranchExitDefinition(exits[i].PortalId, exits[i].HeadingDegrees, names[i], exits[i].Metadata);
            }
        }

        private static void ValidateBranchPortals(
            string branchId,
            string? entryPortal,
            IReadOnlyList<TrackBranchExitDefinition> exits,
            HashSet<string> portalIds,
            List<TrackMapIssue> issues,
            int? line)
        {
            if (!string.IsNullOrWhiteSpace(entryPortal))
            {
                var portalId = entryPortal!;
                if (!portalIds.Contains(portalId))
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Branch '{branchId}' references missing entry portal '{portalId}'.", line));
            }

            foreach (var exit in exits)
            {
                if (exit == null || string.IsNullOrWhiteSpace(exit.PortalId))
                    continue;
                if (!portalIds.Contains(exit.PortalId))
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Branch '{branchId}' references missing exit portal '{exit.PortalId}'.", line));
            }
        }

        private static TrackBranchRole InferBranchRole(TrackSectorType type)
        {
            return type switch
            {
                TrackSectorType.Intersection => TrackBranchRole.Intersection,
                TrackSectorType.Merge => TrackBranchRole.Merge,
                TrackSectorType.Split => TrackBranchRole.Split,
                TrackSectorType.Curve => TrackBranchRole.Curve,
                _ => TrackBranchRole.Undefined
            };
        }

        private static bool IsBranchKnownKey(string key)
        {
            switch (key.Trim().ToLowerInvariant())
            {
                case "id":
                case "area":
                case "sector":
                case "name":
                case "role":
                case "entry_portal":
                case "entry_heading":
                case "entries":
                case "entry_portals":
                case "entry_list":
                case "entry_headings":
                case "width":
                case "length":
                case "tolerance":
                case "exits":
                case "exit_portals":
                case "exit_list":
                case "exit_headings":
                case "exit_names":
                case "preferred_exit":
                    return true;
            }
            return false;
        }

        private static IEnumerable<string> SplitTokens(string raw)
        {
            return raw.Split(new[] { ',', '|', ';' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static bool TryParseExitToken(string token, out string portalId, out float? heading)
        {
            portalId = string.Empty;
            heading = null;
            if (string.IsNullOrWhiteSpace(token))
                return false;

            var trimmed = token.Trim();
            var separator = trimmed.IndexOfAny(new[] { ':', '@' });
            if (separator > 0)
            {
                portalId = trimmed.Substring(0, separator).Trim();
                var headingRaw = trimmed.Substring(separator + 1).Trim();
                if (TryFloat(headingRaw, out var headingValue))
                    heading = headingValue;
                else if (TryDirection(headingRaw, out var direction))
                    heading = DirectionToDegrees(direction);
            }
            else
            {
                portalId = trimmed;
            }

            return !string.IsNullOrWhiteSpace(portalId);
        }

        private static void ApplyPortal(
            List<PortalDefinition> portals,
            SectionBlock block,
            List<TrackMapIssue> issues)
        {
            if (!TryReadId(block, out var id))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Portal requires an id.", block.StartLine));
                return;
            }

            if (!TryGetValue(block, "sector", out var sectorId) || string.IsNullOrWhiteSpace(sectorId))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Portal requires a sector id.", block.StartLine));
                return;
            }

            if (!TryFloat(block, "x", out var x) || !TryFloat(block, "z", out var z))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Portal requires x and z.", block.StartLine));
                return;
            }

            if (portals.Any(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Duplicate portal id '{id}'.", block.StartLine));
                return;
            }

            var width = TryFloat(block, "width", out var widthMeters) ? Math.Max(0.1f, widthMeters) : 0f;
            var entryHeading = TryReadHeading(block, "entry", out var entryHeadingValue) ? entryHeadingValue : (float?)null;
            var exitHeading = TryReadHeading(block, "exit", out var exitHeadingValue) ? exitHeadingValue : (float?)null;

            if (!entryHeading.HasValue && !exitHeading.HasValue && TryReadHeadingFallback(block, out var bothHeading))
            {
                entryHeading = bothHeading;
                exitHeading = bothHeading;
            }

            var role = TryPortalRole(block, out var parsedRole) ? parsedRole : PortalRole.EntryExit;
            if (!TryPortalRole(block, out _))
            {
                if (entryHeading.HasValue && !exitHeading.HasValue)
                    role = PortalRole.Entry;
                else if (exitHeading.HasValue && !entryHeading.HasValue)
                    role = PortalRole.Exit;
            }

            portals.Add(new PortalDefinition(id, sectorId.Trim(), x, z, width, entryHeading, exitHeading, role));
        }

        private static void ApplyTurn(
            TrackMapMetadata metadata,
            List<TrackSectorDefinition> sectors,
            List<TrackAreaDefinition> areas,
            List<ShapeDefinition> shapes,
            List<PortalDefinition> portals,
            List<TrackApproachDefinition> approaches,
            SectionBlock block,
            List<TrackMapIssue> issues)
        {
            if (!TryReadId(block, out var id))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Turn requires an id.", block.StartLine));
                return;
            }

            if (sectors.Any(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase)) ||
                areas.Any(a => string.Equals(a.Id, $"{id}_area", StringComparison.OrdinalIgnoreCase)) ||
                shapes.Any(s => string.Equals(s.Id, $"{id}_shape", StringComparison.OrdinalIgnoreCase)) ||
                portals.Any(p => string.Equals(p.Id, $"{id}_entry", StringComparison.OrdinalIgnoreCase)) ||
                portals.Any(p => string.Equals(p.Id, $"{id}_exit", StringComparison.OrdinalIgnoreCase)) ||
                approaches.Any(a => string.Equals(a.SectorId, id, StringComparison.OrdinalIgnoreCase)))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Turn id '{id}' conflicts with existing ids.", block.StartLine));
                return;
            }

            var name = TryGetValue(block, "name", out var nameValue) ? nameValue : null;
            var turnMaterialId = TryMaterialId(block, "material", out var materialValue) ? materialValue : null;
            var turnWallMaterialId = TryMaterialId(block, "wall_material_id", out var wallMaterialValue) ? wallMaterialValue : null;
            var hasTurnWallHeight = TryFloatAny(block, out var turnWallHeight, "wall_height", "wall_height_m");
            var turnNoise = TryNoise(block, "noise", out var noiseValue) ? noiseValue : (TrackNoise?)null;
            var turnWidth = TryFloatAny(block, out var turnWidthValue, "area_width", "corridor_width", "area_width_m") ? Math.Max(0.1f, turnWidthValue) : (float?)null;
            var turnFlags = TryAreaFlags(block, out var parsedTurnFlags) ? parsedTurnFlags : TrackAreaFlags.None;
            var hasTurnElevation = TryFloatAny(block, out var turnElevationValue, "elevation");
            var hasTurnHeight = TryFloatAny(block, out var turnHeightValue, "height");
            var hasTurnCeiling = TryFloatAny(block, out var turnCeilingValue, "ceiling_height", "ceiling");
            var turnRoomId = TryGetValue(block, "room", out var roomValue) ? roomValue?.Trim() : null;
            if (string.IsNullOrWhiteSpace(turnRoomId))
                turnRoomId = null;
            var turnRoomOverrides = ReadRoomOverrides(block, out var hasTurnRoomOverrides);

            if (!TryReadHeading(block, "from", out var fromHeading) &&
                !TryReadHeading(block, "entry", out fromHeading) &&
                !TryReadHeading(block, "start", out fromHeading))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Turn requires a from_heading.", block.StartLine));
                return;
            }

            if (!TryReadHeading(block, "to", out var toHeading) &&
                !TryReadHeading(block, "exit", out toHeading) &&
                !TryReadHeading(block, "end", out toHeading))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Turn requires a to_heading.", block.StartLine));
                return;
            }

            if (!TryDirectionFromDegrees(fromHeading, out var fromDir) ||
                !TryDirectionFromDegrees(toHeading, out var toDir))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Turn headings must be cardinal directions.", block.StartLine));
                return;
            }

            var fromVec = DirectionVector(fromDir);
            var toVec = DirectionVector(toDir);
            var leftVec = new Vector2(-fromVec.Y, fromVec.X);
            var rightVec = new Vector2(fromVec.Y, -fromVec.X);
            if (!VectorsEqual(toVec, leftVec) && !VectorsEqual(toVec, rightVec))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Turn requires a left or right to_heading relative to from_heading.", block.StartLine));
                return;
            }

            var alongAxisX = Math.Abs(fromVec.X) > 0.5f;
            float start;
            float end;
            if (alongAxisX)
            {
                if (!TryFloatAny(block, out start, "start_x", "from_x", "x_start", "x1") ||
                    !TryFloatAny(block, out end, "end_x", "to_x", "x_end", "x2"))
                {
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Turn requires start_x and end_x for east/west turns.", block.StartLine));
                    return;
                }
            }
            else
            {
                if (!TryFloatAny(block, out start, "start_z", "from_z", "z_start", "z1") ||
                    !TryFloatAny(block, out end, "end_z", "to_z", "z_end", "z2"))
                {
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Turn requires start_z and end_z for north/south turns.", block.StartLine));
                    return;
                }
            }

            var baseCoord = 0f;
            if (alongAxisX)
                TryFloatAny(block, out baseCoord, "base_z", "origin_z", "center_z", "line_z");
            else
                TryFloatAny(block, out baseCoord, "base_x", "origin_x", "center_x", "line_x");

            if (!TryFloatAny(block, out var sideSpace, "side_space", "turn_space", "space", "side_length", "turn_length") &&
                !TryDirectionalSpace(block, toDir, out sideSpace) &&
                !TryFloatAny(block, out sideSpace, "turn_width", "width"))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Turn requires side_space (or north/south/east/west_space).", block.StartLine));
                return;
            }

            if (sideSpace <= 0f)
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Turn side_space must be greater than 0.", block.StartLine));
                return;
            }

            float minX;
            float minZ;
            float width;
            float height;
            if (alongAxisX)
            {
                minX = Math.Min(start, end);
                width = Math.Abs(end - start);
                height = sideSpace;
                minZ = toVec.Y >= 0f ? baseCoord : baseCoord - sideSpace;
            }
            else
            {
                minZ = Math.Min(start, end);
                height = Math.Abs(end - start);
                width = sideSpace;
                minX = toVec.X >= 0f ? baseCoord : baseCoord - sideSpace;
            }

            if (width <= 0f || height <= 0f)
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Turn dimensions must be greater than 0.", block.StartLine));
                return;
            }

            var shapeId = $"{id}_shape";
            var areaId = $"{id}_area";
            var entryPortalId = $"{id}_entry";
            var exitPortalId = $"{id}_exit";

            shapes.Add(new ShapeDefinition(shapeId, ShapeType.Rectangle, minX, minZ, width, height));
            if (!metadata.BaseHeightMeters.HasValue || !metadata.DefaultAreaHeightMeters.HasValue)
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Turn requires meta base_height and default_area_height for auto-generated areas.", block.StartLine));
                return;
            }
            if (string.IsNullOrWhiteSpace(turnMaterialId) &&
                (!metadata.DefaultMaterialDefined || string.IsNullOrWhiteSpace(metadata.DefaultMaterialId)))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Turn requires meta default_material for auto-generated areas.", block.StartLine));
                return;
            }

            var turnElevation = hasTurnElevation ? turnElevationValue : metadata.BaseHeightMeters.Value;
            var turnHeight = hasTurnHeight ? turnHeightValue : metadata.DefaultAreaHeightMeters.Value;
            if (turnHeight <= 0f)
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Turn height must be greater than 0.", block.StartLine));
                return;
            }

            var turnCeiling = hasTurnCeiling
                ? (float?)turnCeilingValue
                : metadata.DefaultCeilingHeightMeters;
            if (turnCeiling.HasValue && turnCeiling.Value <= turnElevation)
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Turn ceiling_height must be above elevation.", block.StartLine));
                return;
            }
            if (turnCeiling.HasValue && hasTurnWallHeight)
            {
                var expectedWallHeight = turnCeiling.Value - turnElevation;
                if (Math.Abs(turnWallHeight - expectedWallHeight) > 0.01f)
                {
                    issues.Add(new TrackMapIssue(
                        TrackMapIssueSeverity.Error,
                        $"Turn wall_height must match {expectedWallHeight.ToString(CultureInfo.InvariantCulture)} when a ceiling is defined.",
                        block.StartLine));
                    return;
                }
            }
            if (hasTurnRoomOverrides && string.IsNullOrWhiteSpace(turnRoomId))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Turn has room overrides but no room id.", block.StartLine));
                return;
            }

            areas.Add(new TrackAreaDefinition(
                areaId,
                TrackAreaType.Curve,
                shapeId,
                turnElevation,
                turnHeight,
                turnCeiling,
                turnRoomId,
                turnRoomOverrides,
                name,
                string.IsNullOrWhiteSpace(turnMaterialId) ? metadata.DefaultMaterialId : turnMaterialId,
                turnNoise,
                turnWidth,
                turnFlags,
                BuildTurnAreaMetadata(block, turnWallMaterialId, hasTurnWallHeight, turnWallHeight)));

            var metadataMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["approach_side"] = "exit",
                ["beacon_shape"] = shapeId,
                ["turn_shape"] = shapeId
            };

            if (TryFloatAny(block, out var turnRange, "turn_range", "guidance_range", "turn_guidance_range"))
                metadataMap["turn_range"] = turnRange.ToString(CultureInfo.InvariantCulture);
            if (TryFloatAny(block, out var beaconRange, "beacon_range", "approach_range", "range"))
                metadataMap["beacon_range"] = beaconRange.ToString(CultureInfo.InvariantCulture);
            if (TryFloatAny(block, out var radius, "radius", "turn_radius"))
                metadataMap["radius"] = radius.ToString(CultureInfo.InvariantCulture);

            sectors.Add(new TrackSectorDefinition(id, TrackSectorType.Curve, name, areaId, null, null, null, TrackSectorFlags.None, metadataMap));

            var pathWidth = metadata.DefaultWidthMeters;
            TryFloatAny(block, out pathWidth, "lane_width", "portal_width");
            pathWidth = Math.Max(0.5f, pathWidth);

            var entryPos = alongAxisX ? new Vector2(start, baseCoord) : new Vector2(baseCoord, start);
            var exitPos = alongAxisX ? new Vector2(end, baseCoord) : new Vector2(baseCoord, end);

            portals.Add(new PortalDefinition(entryPortalId, id, entryPos.X, entryPos.Y, pathWidth, fromHeading, null, PortalRole.Entry));
            portals.Add(new PortalDefinition(exitPortalId, id, exitPos.X, exitPos.Y, pathWidth, null, toHeading, PortalRole.Exit));
            approaches.Add(new TrackApproachDefinition(id, name, entryPortalId, exitPortalId, fromHeading, toHeading, pathWidth, alongAxisX ? width : height, null, metadataMap));
        }

        private static void ApplyLink(
            List<LinkDefinition> links,
            SectionBlock block,
            List<TrackMapIssue> issues)
        {
            if (!TryGetValue(block, "from", out var from) || string.IsNullOrWhiteSpace(from) ||
                !TryGetValue(block, "to", out var to) || string.IsNullOrWhiteSpace(to))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Link requires from and to portal ids.", block.StartLine));
                return;
            }

            var direction = TryLinkDirection(block, out var parsedDirection) ? parsedDirection : LinkDirection.TwoWay;
            var id = TryReadId(block, out var linkId) ? linkId : $"{from.Trim()}->{to.Trim()}";

            if (links.Any(l => string.Equals(l.Id, id, StringComparison.OrdinalIgnoreCase)))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Duplicate link id '{id}'.", block.StartLine));
                return;
            }

            links.Add(new LinkDefinition(id, from, to, direction));
        }

        private static void ApplyBeacon(
            List<TrackBeaconDefinition> beacons,
            SectionBlock block,
            List<TrackMapIssue> issues)
        {
            if (!TryReadId(block, out var id))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Beacon requires an id.", block.StartLine));
                return;
            }

            if (!TryFloat(block, "x", out var x) || !TryFloat(block, "z", out var z))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Beacon requires x and z.", block.StartLine));
                return;
            }

            if (beacons.Any(b => string.Equals(b.Id, id, StringComparison.OrdinalIgnoreCase)))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Duplicate beacon id '{id}'.", block.StartLine));
                return;
            }

            var type = TryBeaconType(block, out var parsedType) ? parsedType : TrackBeaconType.Undefined;
            var role = TryBeaconRole(block, out var parsedRole) ? parsedRole : TrackBeaconRole.Undefined;
            var name = TryGetValue(block, "name", out var nameValue) ? nameValue : null;
            var name2 = TryGetValue(block, "name2", out var name2Value) ? name2Value :
                (TryGetValue(block, "secondary", out name2Value) ? name2Value : null);
            var sectorId = TryGetValue(block, "sector", out var sectorValue) ? sectorValue :
                (TryGetValue(block, "object", out sectorValue) ? sectorValue : null);
            var shapeId = TryGetValue(block, "shape", out var shapeValue) ? shapeValue : null;
            var heading = TryReadHeadingValue(block, out var headingValue) ? headingValue : (float?)null;
            float? radius = null;
            if (TryFloat(block, "radius", out var radiusValue) && radiusValue > 0f)
                radius = radiusValue;
            else if (TryFloat(block, "activation_radius", out radiusValue) && radiusValue > 0f)
                radius = radiusValue;
            var metadata = CollectBeaconMetadata(block);

            beacons.Add(new TrackBeaconDefinition(id, type, x, z, name, name2, sectorId, shapeId, heading, radius, role, metadata));
        }

        private static void ApplyMarker(
            List<TrackMarkerDefinition> markers,
            SectionBlock block,
            List<TrackMapIssue> issues)
        {
            if (!TryReadId(block, out var id))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Marker requires an id.", block.StartLine));
                return;
            }

            if (!TryFloat(block, "x", out var x) || !TryFloat(block, "z", out var z))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Marker requires x and z.", block.StartLine));
                return;
            }

            if (markers.Any(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase)))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Duplicate marker id '{id}'.", block.StartLine));
                return;
            }

            var type = TryMarkerType(block, out var parsedType) ? parsedType : TrackMarkerType.Undefined;
            var name = TryGetValue(block, "name", out var nameValue) ? nameValue : null;
            var shapeId = TryGetValue(block, "shape", out var shapeValue) ? shapeValue : null;
            var heading = TryReadHeadingValue(block, out var headingValue) ? headingValue : (float?)null;
            var metadata = CollectMarkerMetadata(block);

            markers.Add(new TrackMarkerDefinition(id, type, x, z, name, shapeId, heading, metadata));
        }

        private static void ApplyWall(
            TrackMapMetadata metadata,
            List<TrackWallDefinition> walls,
            SectionBlock block,
            List<TrackMapIssue> issues)
        {
            if (block.Values.ContainsKey("acoustic_material") ||
                block.Values.ContainsKey("audio_material") ||
                block.Values.ContainsKey("sound_material") ||
                block.Values.ContainsKey("wall_acoustic_material") ||
                block.Values.ContainsKey("wall_audio_material") ||
                block.Values.ContainsKey("wall_sound_material"))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Wall acoustic_material is not supported. Use material instead.", block.StartLine));
                return;
            }
            if (block.Values.ContainsKey("wall_material") ||
                block.Values.ContainsKey("collision_material"))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Wall collision material is defined by the material. Remove wall_material/collision_material and set collision on the [material] section instead.", block.StartLine));
                return;
            }

            if (!TryReadId(block, out var id))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Wall requires an id.", block.StartLine));
                return;
            }

            if (walls.Any(w => string.Equals(w.Id, id, StringComparison.OrdinalIgnoreCase)))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Duplicate wall id '{id}'.", block.StartLine));
                return;
            }

            if (!TryGetValue(block, "shape", out var shapeId) || string.IsNullOrWhiteSpace(shapeId))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Wall requires a shape id.", block.StartLine));
                return;
            }

            var name = TryGetValue(block, "name", out var nameValue) ? nameValue : null;
            var width = TryFloatAny(block, out var widthValue, "width", "wall_width") ? Math.Max(0f, widthValue) : 0f;
            var height = TryFloatAny(block, out var heightValue, "height", "wall_height") ? Math.Max(0f, heightValue) : 2f;
            var materialId = TryMaterialId(block, "material", out var materialValue) ? materialValue : null;
            var material = TrackWallMaterial.Hard;
            if (string.IsNullOrWhiteSpace(materialId) && !metadata.DefaultMaterialDefined)
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Wall '{id}' requires material or meta default_material.", block.StartLine));
                return;
            }

            var collisionMode = TrackWallCollisionMode.Block;
            if (TryGetValue(block, "collision", out var collisionRaw) ||
                TryGetValue(block, "collision_mode", out collisionRaw) ||
                TryGetValue(block, "behavior", out collisionRaw) ||
                TryGetValue(block, "mode", out collisionRaw))
            {
                if (!TryParseWallCollision(collisionRaw, out collisionMode))
                {
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Wall '{id}' has invalid collision mode '{collisionRaw}'.", block.StartLine));
                    return;
                }
            }

            var wallMetadata = CollectWallMetadata(block);
            walls.Add(new TrackWallDefinition(id, shapeId, width, material, collisionMode, name, wallMetadata, height, materialId));
        }

        private static void ApplyMaterial(
            List<TrackMaterialDefinition> materials,
            SectionBlock block,
            List<TrackMapIssue> issues)
        {
            if (!TryReadId(block, out var id))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Material requires an id.", block.StartLine));
                return;
            }

            if (materials.Any(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase)))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Duplicate material id '{id}'.", block.StartLine));
                return;
            }

            var name = TryGetValue(block, "name", out var nameValue) ? nameValue : null;
            TrackMaterialDefinition? preset = null;
            if (TryGetValue(block, "preset", out var presetValue) && !string.IsNullOrWhiteSpace(presetValue))
            {
                if (!TrackMaterialLibrary.TryGetPreset(presetValue.Trim(), out preset))
                {
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Material '{id}' has unknown preset '{presetValue}'.", block.StartLine));
                    return;
                }
            }

            var absorption = ResolveTriple(block, "absorption", "absorption_low", "absorption_mid", "absorption_high", preset, out var absLow, out var absMid, out var absHigh);
            var transmission = ResolveTriple(block, "transmission", "transmission_low", "transmission_mid", "transmission_high", preset, out var transLow, out var transMid, out var transHigh);
            var scattering = TryFloat(block, "scattering", out var scatterValue) ? Clamp01(scatterValue) : preset?.Scattering ?? 0f;
            var collisionMaterial = preset?.CollisionMaterial ?? TrackWallMaterial.Hard;

            if (TryGetValue(block, "collision", out var collisionRaw) ||
                TryGetValue(block, "collision_material", out collisionRaw))
            {
                if (!Enum.TryParse(collisionRaw, true, out collisionMaterial))
                {
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Material '{id}' has invalid collision material '{collisionRaw}'.", block.StartLine));
                    return;
                }
            }

            if (!absorption && !transmission && preset == null)
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Material '{id}' requires preset or absorption/transmission values.", block.StartLine));
                return;
            }

            materials.Add(new TrackMaterialDefinition(
                id,
                name,
                absLow,
                absMid,
                absHigh,
                scattering,
                transLow,
                transMid,
                transHigh,
                collisionMaterial));
        }

        private static void ApplyRoom(
            List<TrackRoomDefinition> rooms,
            SectionBlock block,
            List<TrackMapIssue> issues)
        {
            if (!TryReadId(block, out var id))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Room requires an id.", block.StartLine));
                return;
            }

            if (rooms.Any(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase)))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Duplicate room id '{id}'.", block.StartLine));
                return;
            }

            var name = TryGetValue(block, "name", out var nameValue) ? nameValue : null;
            TrackRoomDefinition? preset = null;
            if (TryGetValue(block, "preset", out var presetValue) && !string.IsNullOrWhiteSpace(presetValue))
            {
                if (!TrackRoomLibrary.TryGetPreset(presetValue.Trim(), out preset))
                {
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Room '{id}' has unknown preset '{presetValue}'.", block.StartLine));
                    return;
                }
            }

            var hasReverbTime = TryFloat(block, "reverb_time", out var reverbTime);
            var hasReverbGain = TryFloat(block, "reverb_gain", out var reverbGain);
            var hasHfDecay = TryFloat(block, "hf_decay_ratio", out var hfDecayRatio);
            var hasEarlyGain = TryFloat(block, "early_reflections_gain", out var earlyGain);
            var hasLateGain = TryFloat(block, "late_reverb_gain", out var lateGain);
            var hasDiffusion = TryFloat(block, "diffusion", out var diffusion);
            var hasAirAbsorption = TryFloat(block, "air_absorption", out var airAbsorption);
            var hasOcclusion = TryFloat(block, "occlusion_scale", out var occlusionScale);
            var hasTransmission = TryFloat(block, "transmission_scale", out var transmissionScale);

            var hasAny = preset != null || hasReverbTime || hasReverbGain || hasHfDecay ||
                         hasEarlyGain || hasLateGain || hasDiffusion || hasAirAbsorption ||
                         hasOcclusion || hasTransmission;

            if (!hasAny)
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Room '{id}' requires preset or room values.", block.StartLine));
                return;
            }

            if (preset == null &&
                (!hasReverbTime || !hasReverbGain || !hasHfDecay || !hasEarlyGain || !hasLateGain ||
                 !hasDiffusion || !hasAirAbsorption || !hasOcclusion || !hasTransmission))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Room '{id}' requires all room values when no preset is provided.", block.StartLine));
                return;
            }

            var resolvedReverbTime = hasReverbTime ? Math.Max(0f, reverbTime) : preset!.ReverbTimeSeconds;
            var resolvedReverbGain = hasReverbGain ? Clamp01(reverbGain) : preset!.ReverbGain;
            var resolvedHfDecay = hasHfDecay ? Clamp01(hfDecayRatio) : preset!.HfDecayRatio;
            var resolvedEarlyGain = hasEarlyGain ? Clamp01(earlyGain) : preset!.EarlyReflectionsGain;
            var resolvedLateGain = hasLateGain ? Clamp01(lateGain) : preset!.LateReverbGain;
            var resolvedDiffusion = hasDiffusion ? Clamp01(diffusion) : preset!.Diffusion;
            var resolvedAirAbsorption = hasAirAbsorption ? Clamp01(airAbsorption) : preset!.AirAbsorption;
            var resolvedOcclusion = hasOcclusion ? Clamp01(occlusionScale) : preset!.OcclusionScale;
            var resolvedTransmission = hasTransmission ? Clamp01(transmissionScale) : preset!.TransmissionScale;

            rooms.Add(new TrackRoomDefinition(
                id,
                name,
                resolvedReverbTime,
                resolvedReverbGain,
                resolvedHfDecay,
                resolvedEarlyGain,
                resolvedLateGain,
                resolvedDiffusion,
                resolvedAirAbsorption,
                resolvedOcclusion,
                resolvedTransmission));
        }

        private static void ApplyApproach(
            List<TrackApproachDefinition> approaches,
            SectionBlock block,
            List<TrackMapIssue> issues)
        {
            if (!TryReadId(block, out var id))
            {
                if (TryGetValue(block, "sector", out var sectorIdValue) && !string.IsNullOrWhiteSpace(sectorIdValue))
                {
                    id = sectorIdValue.Trim();
                }
                else
                {
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Approach requires an id or sector.", block.StartLine));
                    return;
                }
            }

            var sectorId = id;
            if (TryGetValue(block, "sector", out var sectorValue) && !string.IsNullOrWhiteSpace(sectorValue))
                sectorId = sectorValue.Trim();

            if (approaches.Any(a => string.Equals(a.SectorId, sectorId, StringComparison.OrdinalIgnoreCase)))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Duplicate approach sector id '{sectorId}'.", block.StartLine));
                return;
            }

            var name = TryGetValue(block, "name", out var nameValue) ? nameValue : null;
            var entryPortalId = TryGetValue(block, "entry_portal", out var entryValue) ? entryValue :
                (TryGetValue(block, "entry", out entryValue) ? entryValue :
                 (TryGetValue(block, "runway_entry", out entryValue) ? entryValue :
                  (TryGetValue(block, "taxi_entry", out entryValue) ? entryValue :
                   (TryGetValue(block, "gate_entry", out entryValue) ? entryValue : null))));
            var exitPortalId = TryGetValue(block, "exit_portal", out var exitValue) ? exitValue :
                (TryGetValue(block, "exit", out exitValue) ? exitValue :
                 (TryGetValue(block, "runway_exit", out exitValue) ? exitValue :
                  (TryGetValue(block, "taxi_exit", out exitValue) ? exitValue :
                   (TryGetValue(block, "gate_exit", out exitValue) ? exitValue : null))));

            var entryHeading = TryReadHeading(block, "entry", out var entryHeadingValue)
                ? entryHeadingValue
                : (TryReadHeading(block, "approach", out entryHeadingValue) ? entryHeadingValue : (float?)null);
            var exitHeading = TryReadHeading(block, "exit", out var exitHeadingValue)
                ? exitHeadingValue
                : (TryReadHeading(block, "threshold", out exitHeadingValue) ? exitHeadingValue : (float?)null);

            var width = TryFloat(block, "width", out var widthMeters) ? Math.Max(0.1f, widthMeters) :
                (TryFloat(block, "lane_width", out widthMeters) ? Math.Max(0.1f, widthMeters) :
                 (TryFloat(block, "approach_width", out widthMeters) ? Math.Max(0.1f, widthMeters) : (float?)null));
            var length = TryFloat(block, "length", out var lengthMeters) ? Math.Max(0.1f, lengthMeters) :
                (TryFloat(block, "approach_length", out lengthMeters) ? Math.Max(0.1f, lengthMeters) : (float?)null);
            var tolerance = TryFloat(block, "tolerance", out var toleranceDegrees) ? Math.Max(0f, toleranceDegrees) :
                (TryFloat(block, "alignment_tolerance", out toleranceDegrees) ? Math.Max(0f, toleranceDegrees) :
                 (TryFloat(block, "align_tol", out toleranceDegrees) ? Math.Max(0f, toleranceDegrees) : (float?)null));

            var metadata = CollectApproachMetadata(block);

            approaches.Add(new TrackApproachDefinition(
                sectorId,
                name,
                entryPortalId,
                exitPortalId,
                entryHeading,
                exitHeading,
                width,
                length,
                tolerance,
                metadata));
        }

        private static bool TryGetValue(SectionBlock block, string key, out string value)
        {
            value = string.Empty;
            if (!block.Values.TryGetValue(key, out var values) || values.Count == 0)
                return false;
            value = values[values.Count - 1];
            return true;
        }

        private static bool TryGetMetadataValue(
            IReadOnlyDictionary<string, string> metadata,
            out string value,
            params string[] keys)
        {
            value = string.Empty;
            if (metadata == null || metadata.Count == 0)
                return false;

            foreach (var key in keys)
            {
                if (metadata.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(raw))
                {
                    value = raw;
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetMetadataBool(
            IReadOnlyDictionary<string, string> metadata,
            out bool value,
            params string[] keys)
        {
            value = false;
            if (metadata == null || metadata.Count == 0)
                return false;

            foreach (var key in keys)
            {
                if (!metadata.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
                    continue;
                if (TryBool(raw, out value))
                    return true;
            }

            return false;
        }

        private static IEnumerable<string> GetValues(SectionBlock block, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (block.Values.TryGetValue(key, out var values))
                {
                    foreach (var value in values)
                        yield return value;
                }
            }
        }

        private static bool TryReadId(SectionBlock block, out string id)
        {
            id = string.Empty;
            if (!string.IsNullOrWhiteSpace(block.Argument))
            {
                id = block.Argument!.Trim();
                return true;
            }
            if (TryGetValue(block, "id", out var rawId) && !string.IsNullOrWhiteSpace(rawId))
            {
                id = rawId.Trim();
                return true;
            }
            return false;
        }

        private static bool TryInt(SectionBlock block, string key, out int value)
        {
            value = 0;
            if (!TryGetValue(block, key, out var raw))
                return false;
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryFloat(SectionBlock block, string key, out float value)
        {
            value = 0f;
            if (!TryGetValue(block, key, out var raw))
                return false;
            return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryFloatAny(SectionBlock block, out float value, params string[] keys)
        {
            value = 0f;
            foreach (var key in keys)
            {
                if (!TryGetValue(block, key, out var raw))
                    continue;
                if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                    return true;
            }
            return false;
        }

        private static bool TryDirectionalSpace(SectionBlock block, MapDirection direction, out float value)
        {
            value = 0f;
            switch (direction)
            {
                case MapDirection.North:
                    return TryFloatAny(block, out value, "north_space", "north_extent");
                case MapDirection.South:
                    return TryFloatAny(block, out value, "south_space", "south_extent");
                case MapDirection.East:
                    return TryFloatAny(block, out value, "east_space", "east_extent");
                case MapDirection.West:
                    return TryFloatAny(block, out value, "west_space", "west_extent");
                default:
                    return false;
            }
        }

        private static IReadOnlyDictionary<string, string> BuildTurnAreaMetadata(
            SectionBlock block,
            string? wallMaterialId,
            bool hasWallHeight,
            float wallHeight)
        {
            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            AddTurnMetadataValue(block, metadata, "auto_walls", "auto_walls", "auto_wall", "walls_auto", "auto_wall_enabled");
            AddTurnMetadataValue(block, metadata, "wall_edges", "wall_edges", "wall_edge", "wall_sides", "wall_side");
            AddTurnMetadataValue(block, metadata, "wall_width", "wall_width", "wall_thickness", "wall_size");
            AddTurnMetadataValue(block, metadata, "wall_collision", "wall_collision", "wall_collision_mode", "collision", "collision_mode", "wall_mode");
            if (!string.IsNullOrWhiteSpace(wallMaterialId))
                metadata["wall_material_id"] = wallMaterialId!;
            if (hasWallHeight)
                metadata["wall_height"] = wallHeight.ToString(CultureInfo.InvariantCulture);
            return metadata;
        }

        private static void AddTurnMetadataValue(
            SectionBlock block,
            Dictionary<string, string> metadata,
            string key,
            params string[] aliases)
        {
            foreach (var alias in aliases)
            {
                if (TryGetValue(block, alias, out var raw) && !string.IsNullOrWhiteSpace(raw))
                {
                    metadata[key] = raw.Trim();
                    return;
                }
            }
        }

        private static bool TryBool(SectionBlock block, string key, out bool value)
        {
            value = false;
            if (!TryGetValue(block, key, out var raw))
                return false;
            return TryBool(raw, out value);
        }

        private static TrackRoomOverrides? ReadRoomOverrides(SectionBlock block, out bool hasAny)
        {
            var overrides = new TrackRoomOverrides();
            if (TryFloat(block, "reverb_time", out var reverbTime))
                overrides.ReverbTimeSeconds = Math.Max(0f, reverbTime);
            if (TryFloat(block, "reverb_gain", out var reverbGain))
                overrides.ReverbGain = Clamp01(reverbGain);
            if (TryFloat(block, "hf_decay_ratio", out var hfDecayRatio))
                overrides.HfDecayRatio = Clamp01(hfDecayRatio);
            if (TryFloat(block, "early_reflections_gain", out var earlyGain))
                overrides.EarlyReflectionsGain = Clamp01(earlyGain);
            if (TryFloat(block, "late_reverb_gain", out var lateGain))
                overrides.LateReverbGain = Clamp01(lateGain);
            if (TryFloat(block, "diffusion", out var diffusion))
                overrides.Diffusion = Clamp01(diffusion);
            if (TryFloat(block, "air_absorption", out var airAbsorption))
                overrides.AirAbsorption = Clamp01(airAbsorption);
            if (TryFloat(block, "occlusion_scale", out var occlusionScale))
                overrides.OcclusionScale = Clamp01(occlusionScale);
            if (TryFloat(block, "transmission_scale", out var transmissionScale))
                overrides.TransmissionScale = Clamp01(transmissionScale);

            hasAny = overrides.HasAny;
            return hasAny ? overrides : null;
        }

        private static bool TryMaterialId(SectionBlock block, string key, out string materialId)
        {
            materialId = string.Empty;
            if (!TryGetValue(block, key, out var raw))
                return false;
            var trimmed = raw?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                return false;
            materialId = trimmed!;
            return true;
        }

        private static bool TryNoise(SectionBlock block, string key, out TrackNoise noise)
        {
            noise = TrackNoise.NoNoise;
            if (!TryGetValue(block, key, out var raw))
                return false;
            return Enum.TryParse(raw, true, out noise);
        }

        private static bool ResolveTriple(
            SectionBlock block,
            string baseKey,
            string lowKey,
            string midKey,
            string highKey,
            TrackMaterialDefinition? preset,
            out float low,
            out float mid,
            out float high)
        {
            low = preset?.AbsorptionLow ?? 0f;
            mid = preset?.AbsorptionMid ?? 0f;
            high = preset?.AbsorptionHigh ?? 0f;
            if (baseKey.StartsWith("transmission", StringComparison.OrdinalIgnoreCase))
            {
                low = preset?.TransmissionLow ?? 0f;
                mid = preset?.TransmissionMid ?? 0f;
                high = preset?.TransmissionHigh ?? 0f;
            }

            if (TryGetValue(block, baseKey, out var raw) && !string.IsNullOrWhiteSpace(raw))
            {
                if (TryParseTriple(raw, out var tLow, out var tMid, out var tHigh))
                {
                    low = Clamp01(tLow);
                    mid = Clamp01(tMid);
                    high = Clamp01(tHigh);
                    return true;
                }
            }

            var has = false;
            if (TryFloat(block, lowKey, out var lowVal))
            {
                low = Clamp01(lowVal);
                has = true;
            }
            if (TryFloat(block, midKey, out var midVal))
            {
                mid = Clamp01(midVal);
                has = true;
            }
            if (TryFloat(block, highKey, out var highVal))
            {
                high = Clamp01(highVal);
                has = true;
            }

            return has;
        }

        private static bool TryParseTriple(string raw, out float low, out float mid, out float high)
        {
            low = mid = high = 0f;
            var tokens = raw.Split(new[] { ',', ';', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
                return false;
            if (tokens.Length == 1 && float.TryParse(tokens[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var single))
            {
                low = mid = high = single;
                return true;
            }
            if (tokens.Length >= 3 &&
                float.TryParse(tokens[0], NumberStyles.Float, CultureInfo.InvariantCulture, out low) &&
                float.TryParse(tokens[1], NumberStyles.Float, CultureInfo.InvariantCulture, out mid) &&
                float.TryParse(tokens[2], NumberStyles.Float, CultureInfo.InvariantCulture, out high))
            {
                return true;
            }
            return false;
        }

        private static float Clamp01(float value)
        {
            if (value < 0f)
                return 0f;
            if (value > 1f)
                return 1f;
            return value;
        }

        private static bool TrySectorFlags(SectionBlock block, out TrackSectorFlags flags)
        {
            flags = TrackSectorFlags.None;
            var found = false;
            foreach (var raw in GetValues(block, "flags", "flag", "caps", "capabilities"))
            {
                if (!TryParseSectorFlags(raw, out var parsed))
                    continue;
                flags |= parsed;
                found = true;
            }
            return found;
        }

        private static IReadOnlyDictionary<string, string>? CollectSectorMetadata(SectionBlock block)
        {
            Dictionary<string, string>? metadata = null;
            foreach (var pair in block.Values)
            {
                if (SectorKnownKeys.Contains(pair.Key))
                    continue;
                if (pair.Value.Count == 0)
                    continue;
                metadata ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                metadata[pair.Key] = pair.Value[pair.Value.Count - 1];
            }
            return metadata;
        }

        private static bool TryAreaFlags(SectionBlock block, out TrackAreaFlags flags)
        {
            flags = TrackAreaFlags.None;
            var found = false;
            foreach (var raw in GetValues(block, "flags", "flag", "caps", "capabilities"))
            {
                if (!TryParseAreaFlags(raw, out var parsed))
                    continue;
                flags |= parsed;
                found = true;
            }
            return found;
        }

        private static IReadOnlyDictionary<string, string>? CollectAreaMetadata(SectionBlock block)
        {
            Dictionary<string, string>? metadata = null;
            foreach (var pair in block.Values)
            {
                if (AreaKnownKeys.Contains(pair.Key))
                    continue;
                if (pair.Value.Count == 0)
                    continue;
                metadata ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                metadata[pair.Key] = pair.Value[pair.Value.Count - 1];
            }
            return metadata;
        }

        private static IReadOnlyDictionary<string, string>? CollectWallMetadata(SectionBlock block)
        {
            Dictionary<string, string>? metadata = null;
            foreach (var pair in block.Values)
            {
                if (WallKnownKeys.Contains(pair.Key))
                    continue;
                if (pair.Value.Count == 0)
                    continue;
                metadata ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                metadata[pair.Key] = pair.Value[pair.Value.Count - 1];
            }
            return metadata;
        }

        private static IReadOnlyDictionary<string, string>? CollectBeaconMetadata(SectionBlock block)
        {
            Dictionary<string, string>? metadata = null;
            foreach (var pair in block.Values)
            {
                if (BeaconKnownKeys.Contains(pair.Key))
                    continue;
                if (pair.Value.Count == 0)
                    continue;
                metadata ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                metadata[pair.Key] = pair.Value[pair.Value.Count - 1];
            }
            return metadata;
        }

        private static IReadOnlyDictionary<string, string>? CollectMarkerMetadata(SectionBlock block)
        {
            Dictionary<string, string>? metadata = null;
            foreach (var pair in block.Values)
            {
                if (MarkerKnownKeys.Contains(pair.Key))
                    continue;
                if (pair.Value.Count == 0)
                    continue;
                metadata ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                metadata[pair.Key] = pair.Value[pair.Value.Count - 1];
            }
            return metadata;
        }

        private static IReadOnlyDictionary<string, string>? CollectApproachMetadata(SectionBlock block)
        {
            Dictionary<string, string>? metadata = null;
            foreach (var pair in block.Values)
            {
                if (ApproachKnownKeys.Contains(pair.Key))
                    continue;
                if (pair.Value.Count == 0)
                    continue;
                metadata ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                metadata[pair.Key] = pair.Value[pair.Value.Count - 1];
            }
            return metadata;
        }

        private static bool TryDirection(SectionBlock block, string key, out MapDirection direction)
        {
            direction = MapDirection.North;
            if (!TryGetValue(block, key, out var raw))
                return false;
            return TryDirection(raw, out direction);
        }

        private static bool TryPortalRole(SectionBlock block, out PortalRole role)
        {
            role = PortalRole.EntryExit;
            if (!TryGetValue(block, "role", out var raw))
                return false;
            return TryPortalRole(raw, out role);
        }

        private static bool TryBeaconType(SectionBlock block, out TrackBeaconType type)
        {
            type = TrackBeaconType.Undefined;
            if (!TryGetValue(block, "type", out var raw))
                return false;
            return TryBeaconType(raw, out type);
        }

        private static bool TryBeaconRole(SectionBlock block, out TrackBeaconRole role)
        {
            role = TrackBeaconRole.Undefined;
            if (!TryGetValue(block, "role", out var raw))
                return false;
            return TryBeaconRole(raw, out role);
        }

        private static bool TryMarkerType(SectionBlock block, out TrackMarkerType type)
        {
            type = TrackMarkerType.Undefined;
            if (!TryGetValue(block, "type", out var raw))
                return false;
            return TryMarkerType(raw, out type);
        }

        private static bool TryLinkDirection(SectionBlock block, out LinkDirection direction)
        {
            direction = LinkDirection.TwoWay;
            if (TryGetValue(block, "dir", out var raw))
                return TryLinkDirection(raw, out direction);
            if (TryGetValue(block, "direction", out raw))
                return TryLinkDirection(raw, out direction);
            if (TryGetValue(block, "oneway", out var oneway) && TryBool(oneway, out var isOneWay))
            {
                direction = isOneWay ? LinkDirection.OneWay : LinkDirection.TwoWay;
                return true;
            }
            return false;
        }

        private static bool TryReadHeading(SectionBlock block, string key, out float headingDegrees)
        {
            headingDegrees = 0f;
            foreach (var raw in GetValues(block, $"{key}_heading", $"{key}_heading_deg", $"{key}_dir", $"{key}_direction", key))
            {
                if (TryDirection(raw, out var direction))
                {
                    headingDegrees = DirectionToDegrees(direction);
                    return true;
                }
                if (TryFloat(raw, out headingDegrees))
                    return true;
            }

            return false;
        }

        private static bool TryReadHeadingFallback(SectionBlock block, out float headingDegrees)
        {
            headingDegrees = 0f;
            foreach (var raw in GetValues(block, "heading", "dir", "direction"))
            {
                if (TryDirection(raw, out var direction))
                {
                    headingDegrees = DirectionToDegrees(direction);
                    return true;
                }
                if (TryFloat(raw, out headingDegrees))
                    return true;
            }
            return false;
        }

        private static bool TryReadHeadingValue(SectionBlock block, out float headingDegrees)
        {
            headingDegrees = 0f;
            foreach (var raw in GetValues(block, "heading", "orientation", "dir", "direction"))
            {
                if (TryDirection(raw, out var direction))
                {
                    headingDegrees = DirectionToDegrees(direction);
                    return true;
                }
                if (TryFloat(raw, out headingDegrees))
                    return true;
            }
            return false;
        }

        private static float NormalizeDegrees(float degrees)
        {
            var result = degrees % 360f;
            if (result < 0f)
                result += 360f;
            return result;
        }

        private static bool TryParsePoints(SectionBlock block, out List<Vector2> points)
        {
            points = new List<Vector2>();

            foreach (var raw in GetValues(block, "points"))
            {
                if (!TryParsePoints(raw, out var parsed))
                    return false;
                points.AddRange(parsed);
            }

            foreach (var raw in GetValues(block, "point"))
            {
                if (!TryParsePoints(raw, out var parsed))
                    return false;
                points.AddRange(parsed);
            }

            return points.Count > 0;
        }
        private static bool TryDirection(string value, out MapDirection direction)
        {
            direction = MapDirection.North;
            if (string.IsNullOrWhiteSpace(value))
                return false;
            var trimmed = value.Trim().ToLowerInvariant();
            switch (trimmed)
            {
                case "n":
                case "north":
                    direction = MapDirection.North;
                    return true;
                case "e":
                case "east":
                    direction = MapDirection.East;
                    return true;
                case "s":
                case "south":
                    direction = MapDirection.South;
                    return true;
                case "w":
                case "west":
                    direction = MapDirection.West;
                    return true;
            }
            return false;
        }

        private static bool TryShapeType(string value, out ShapeType type)
        {
            type = ShapeType.Undefined;
            if (string.IsNullOrWhiteSpace(value))
                return false;
            var trimmed = value.Trim().ToLowerInvariant();
            switch (trimmed)
            {
                case "rect":
                case "rectangle":
                    type = ShapeType.Rectangle;
                    return true;
                case "circle":
                    type = ShapeType.Circle;
                    return true;
                case "ring":
                case "band":
                    type = ShapeType.Ring;
                    return true;
                case "polygon":
                case "poly":
                    type = ShapeType.Polygon;
                    return true;
                case "polyline":
                case "line":
                case "path":
                    type = ShapeType.Polyline;
                    return true;
            }
            return Enum.TryParse(value, true, out type);
        }

        private static bool TryPortalRole(string value, out PortalRole role)
        {
            role = PortalRole.EntryExit;
            if (string.IsNullOrWhiteSpace(value))
                return false;
            var trimmed = value.Trim().ToLowerInvariant();
            switch (trimmed)
            {
                case "entry":
                    role = PortalRole.Entry;
                    return true;
                case "exit":
                    role = PortalRole.Exit;
                    return true;
                case "both":
                case "entryexit":
                case "entry_exit":
                    role = PortalRole.EntryExit;
                    return true;
            }
            return Enum.TryParse(value, true, out role);
        }

        private static bool TryBeaconType(string value, out TrackBeaconType type)
        {
            type = TrackBeaconType.Undefined;
            if (string.IsNullOrWhiteSpace(value))
                return false;
            var trimmed = value.Trim().ToLowerInvariant();
            switch (trimmed)
            {
                case "voice":
                case "speech":
                case "announce":
                    type = TrackBeaconType.Voice;
                    return true;
                case "beep":
                case "pip":
                case "tone":
                    type = TrackBeaconType.Beep;
                    return true;
                case "silent":
                case "none":
                    type = TrackBeaconType.Silent;
                    return true;
            }
            return Enum.TryParse(value, true, out type);
        }

        private static bool TryBeaconRole(string value, out TrackBeaconRole role)
        {
            role = TrackBeaconRole.Undefined;
            if (string.IsNullOrWhiteSpace(value))
                return false;
            var trimmed = value.Trim().ToLowerInvariant();
            switch (trimmed)
            {
                case "guide":
                case "guidance":
                    role = TrackBeaconRole.Guidance;
                    return true;
                case "align":
                case "alignment":
                    role = TrackBeaconRole.Alignment;
                    return true;
                case "entry":
                    role = TrackBeaconRole.Entry;
                    return true;
                case "exit":
                    role = TrackBeaconRole.Exit;
                    return true;
                case "center":
                case "centre":
                    role = TrackBeaconRole.Center;
                    return true;
                case "warn":
                case "warning":
                case "hazard":
                    role = TrackBeaconRole.Warning;
                    return true;
            }
            return Enum.TryParse(value, true, out role);
        }

        private static bool TryMarkerType(string value, out TrackMarkerType type)
        {
            type = TrackMarkerType.Undefined;
            if (string.IsNullOrWhiteSpace(value))
                return false;
            var trimmed = value.Trim().ToLowerInvariant();
            switch (trimmed)
            {
                case "start":
                    type = TrackMarkerType.Start;
                    return true;
                case "finish":
                case "end":
                    type = TrackMarkerType.Finish;
                    return true;
                case "checkpoint":
                case "check":
                    type = TrackMarkerType.Checkpoint;
                    return true;
                case "entry":
                    type = TrackMarkerType.Entry;
                    return true;
                case "exit":
                    type = TrackMarkerType.Exit;
                    return true;
                case "apex":
                    type = TrackMarkerType.Apex;
                    return true;
                case "curve":
                case "turn":
                    type = TrackMarkerType.Curve;
                    return true;
                case "intersection":
                case "cross":
                    type = TrackMarkerType.Intersection;
                    return true;
                case "merge":
                    type = TrackMarkerType.Merge;
                    return true;
                case "split":
                    type = TrackMarkerType.Split;
                    return true;
                case "branch":
                    type = TrackMarkerType.Branch;
                    return true;
                case "warning":
                case "hazard":
                    type = TrackMarkerType.Warning;
                    return true;
            }
            return Enum.TryParse(value, true, out type);
        }

        private static bool TryLinkDirection(string value, out LinkDirection direction)
        {
            direction = LinkDirection.TwoWay;
            if (string.IsNullOrWhiteSpace(value))
                return false;
            var trimmed = value.Trim().ToLowerInvariant();
            switch (trimmed)
            {
                case "two":
                case "both":
                case "twoway":
                    direction = LinkDirection.TwoWay;
                    return true;
                case "one":
                case "oneway":
                    direction = LinkDirection.OneWay;
                    return true;
            }
            return Enum.TryParse(value, true, out direction);
        }

        private static float DirectionToDegrees(MapDirection direction)
        {
            return direction switch
            {
                MapDirection.North => 0f,
                MapDirection.East => 90f,
                MapDirection.South => 180f,
                MapDirection.West => 270f,
                _ => 0f
            };
        }

        private static bool TryDirectionFromDegrees(float headingDegrees, out MapDirection direction)
        {
            direction = MapDirection.North;
            var normalized = NormalizeDegrees(headingDegrees);
            if (Math.Abs(normalized - 0f) <= 0.5f)
            {
                direction = MapDirection.North;
                return true;
            }
            if (Math.Abs(normalized - 90f) <= 0.5f)
            {
                direction = MapDirection.East;
                return true;
            }
            if (Math.Abs(normalized - 180f) <= 0.5f)
            {
                direction = MapDirection.South;
                return true;
            }
            if (Math.Abs(normalized - 270f) <= 0.5f)
            {
                direction = MapDirection.West;
                return true;
            }
            return false;
        }

        private static Vector2 DirectionVector(MapDirection direction)
        {
            return direction switch
            {
                MapDirection.North => new Vector2(0f, 1f),
                MapDirection.East => new Vector2(1f, 0f),
                MapDirection.South => new Vector2(0f, -1f),
                MapDirection.West => new Vector2(-1f, 0f),
                _ => new Vector2(0f, 1f)
            };
        }

        private static bool VectorsEqual(Vector2 a, Vector2 b)
        {
            return Vector2.DistanceSquared(a, b) <= 0.0001f;
        }

        private static bool TryParsePoints(string raw, out List<Vector2> points)
        {
            points = new List<Vector2>();
            if (string.IsNullOrWhiteSpace(raw))
                return false;
            var segments = raw.Split(new[] { ';', '|' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var segment in segments)
            {
                var trimmed = segment.Trim();
                if (trimmed.Length == 0)
                    continue;
                var coords = trimmed.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (coords.Length < 2)
                    return false;
                if (!float.TryParse(coords[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x))
                    return false;
                if (!float.TryParse(coords[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
                    return false;
                points.Add(new Vector2(x, z));
            }
            return points.Count > 0;
        }

        private static bool TryParseSectorFlags(string raw, out TrackSectorFlags flags)
        {
            flags = TrackSectorFlags.None;
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            var found = false;
            var tokens = raw.Split(new[] { ',', '|', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var token in tokens)
            {
                if (TryParseSectorFlagToken(token, out var flag))
                {
                    flags |= flag;
                    found = true;
                }
            }

            return found;
        }

        private static bool TryParseSectorFlagToken(string token, out TrackSectorFlags flag)
        {
            flag = TrackSectorFlags.None;
            if (string.IsNullOrWhiteSpace(token))
                return false;

            var trimmed = token.Trim().ToLowerInvariant();
            switch (trimmed)
            {
                case "fuel":
                case "refuel":
                case "gas":
                case "pump":
                    flag = TrackSectorFlags.Fuel;
                    return true;
                case "parking":
                case "park":
                    flag = TrackSectorFlags.Parking;
                    return true;
                case "boarding":
                case "board":
                    flag = TrackSectorFlags.Boarding;
                    return true;
                case "service":
                case "servicing":
                    flag = TrackSectorFlags.Service;
                    return true;
                case "pit":
                case "pitlane":
                case "pit_box":
                case "pitbox":
                    flag = TrackSectorFlags.Pit;
                    return true;
                case "safe":
                case "safezone":
                    flag = TrackSectorFlags.SafeZone;
                    return true;
                case "hazard":
                case "danger":
                    flag = TrackSectorFlags.Hazard;
                    return true;
                case "closed":
                case "blocked":
                    flag = TrackSectorFlags.Closed;
                    return true;
                case "restricted":
                    flag = TrackSectorFlags.Restricted;
                    return true;
            }

            return Enum.TryParse(token, true, out flag);
        }

        private static bool TryParseAreaFlags(string raw, out TrackAreaFlags flags)
        {
            flags = TrackAreaFlags.None;
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            var found = false;
            var tokens = raw.Split(new[] { ',', '|', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var token in tokens)
            {
                if (TryParseAreaFlagToken(token, out var flag))
                {
                    flags |= flag;
                    found = true;
                }
            }

            return found;
        }

        private static bool TryParseAreaFlagToken(string token, out TrackAreaFlags flag)
        {
            flag = TrackAreaFlags.None;
            if (string.IsNullOrWhiteSpace(token))
                return false;

            var trimmed = token.Trim().ToLowerInvariant();
            switch (trimmed)
            {
                case "safe":
                case "safezone":
                    flag = TrackAreaFlags.SafeZone;
                    return true;
                case "hazard":
                case "danger":
                    flag = TrackAreaFlags.Hazard;
                    return true;
                case "slow":
                case "slowzone":
                    flag = TrackAreaFlags.SlowZone;
                    return true;
                case "closed":
                case "blocked":
                    flag = TrackAreaFlags.Closed;
                    return true;
                case "restricted":
                case "noentry":
                    flag = TrackAreaFlags.Restricted;
                    return true;
                case "pit":
                case "pitspeed":
                    flag = TrackAreaFlags.PitSpeed;
                    return true;
            }

            return Enum.TryParse(token, true, out flag);
        }

        private static bool TryParseWallCollision(string raw, out TrackWallCollisionMode mode)
        {
            mode = TrackWallCollisionMode.Block;
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            var trimmed = raw.Trim().ToLowerInvariant();
            switch (trimmed)
            {
                case "block":
                case "solid":
                case "stop":
                    mode = TrackWallCollisionMode.Block;
                    return true;
                case "bounce":
                case "rebound":
                case "reflect":
                    mode = TrackWallCollisionMode.Bounce;
                    return true;
                case "pass":
                case "ignore":
                case "none":
                case "ghost":
                    mode = TrackWallCollisionMode.Pass;
                    return true;
            }

            return Enum.TryParse(raw, true, out mode);
        }

        private static bool TryFloat(string raw, out float value)
        {
            return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryInt(string raw, out int value)
        {
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryBool(string raw, out bool value)
        {
            value = false;
            if (string.IsNullOrWhiteSpace(raw))
                return false;
            if (bool.TryParse(raw, out value))
                return true;
            if (raw == "1")
            {
                value = true;
                return true;
            }
            if (raw == "0")
            {
                value = false;
                return true;
            }
            return false;
        }

    }

    public static class TrackMapValidator
    {
        public static TrackMapValidationResult ValidateFile(string nameOrPath, TrackMapValidationOptions? options = null)
        {
            if (!TrackMapFormat.TryParse(nameOrPath, out var map, out var issues) || map == null)
                return new TrackMapValidationResult(issues);

            var opts = options ?? new TrackMapValidationOptions();
            Validate(map, opts, issues);
            return new TrackMapValidationResult(issues);
        }

        public static TrackMapValidationResult Validate(TrackMapDefinition map, TrackMapValidationOptions? options = null)
        {
            var issues = new List<TrackMapIssue>();
            var opts = options ?? new TrackMapValidationOptions();
            Validate(map, opts, issues);
            return new TrackMapValidationResult(issues);
        }

        private static void Validate(TrackMapDefinition map, TrackMapValidationOptions options, List<TrackMapIssue> issues)
        {
            var hasTopology = map.Areas.Count > 0 || map.Shapes.Count > 0 || map.Sectors.Count > 0 || map.Portals.Count > 0;
            if (!hasTopology)
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Map contains no topology."));
                return;
            }

            if (!HasDrivableAreas(map.Areas))
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Map requires at least one drivable area."));

            if (map.Metadata.CellSizeMeters <= 0f)
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Cell size must be positive."));

            var safeCount = 0;
            var intersectionCount = 0;
            foreach (var area in map.Areas)
            {
                if (area == null)
                    continue;
                if (area.Type == TrackAreaType.SafeZone || (area.Flags & TrackAreaFlags.SafeZone) != 0)
                    safeCount++;
                if (area.Type == TrackAreaType.Intersection)
                    intersectionCount++;
            }

            if (options.RequireSafeZones && safeCount == 0)
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Warning, "Map has no safe zones."));

            if (options.RequireIntersections && intersectionCount == 0)
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Warning, "Map has no intersections."));

            ValidateTopology(map, issues);
            ValidateRegionOverlaps(map, issues);
        }

        private static void ValidateTopology(TrackMapDefinition map, List<TrackMapIssue> issues)
        {
            if (map.Shapes.Count == 0 && map.Portals.Count == 0 &&
                map.Links.Count == 0 && map.Areas.Count == 0 && map.Beacons.Count == 0 && map.Markers.Count == 0 &&
                map.Approaches.Count == 0)
                return;

            var sectorIds = new HashSet<string>(map.Sectors.Select(s => s.Id), StringComparer.OrdinalIgnoreCase);
            var shapeIds = new HashSet<string>(map.Shapes.Select(s => s.Id), StringComparer.OrdinalIgnoreCase);
            var portalIds = new HashSet<string>(map.Portals.Select(p => p.Id), StringComparer.OrdinalIgnoreCase);
            var materialIds = new HashSet<string>(map.Materials.Select(m => m.Id), StringComparer.OrdinalIgnoreCase);
            var roomIds = new HashSet<string>(map.Rooms.Select(r => r.Id), StringComparer.OrdinalIgnoreCase);

            if (map.Materials.Count > 0)
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var material in map.Materials)
                {
                    if (material == null)
                        continue;
                    if (!seen.Add(material.Id))
                        issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Duplicate material id '{material.Id}'."));
                }
            }

            if (map.Rooms.Count > 0)
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var room in map.Rooms)
                {
                    if (room == null)
                        continue;
                    if (!seen.Add(room.Id))
                        issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Duplicate room id '{room.Id}'."));
                }
            }

            foreach (var area in map.Areas)
            {
                if (area.WidthMeters.HasValue && area.WidthMeters.Value < 0f)
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Area '{area.Id}' width must be positive."));
                if (!shapeIds.Contains(area.ShapeId))
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Area '{area.Id}' references missing shape '{area.ShapeId}'."));
                if (!string.IsNullOrWhiteSpace(area.MaterialId))
                {
                    var materialId = area.MaterialId!;
                    if (!materialIds.Contains(materialId) && !TrackMaterialLibrary.IsPreset(materialId))
                        issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Area '{area.Id}' references missing material '{materialId}'."));
                }
                if (!string.IsNullOrWhiteSpace(area.RoomId))
                {
                    var roomId = area.RoomId!;
                    if (!roomIds.Contains(roomId) && !TrackRoomLibrary.IsPreset(roomId))
                        issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Area '{area.Id}' references missing room '{roomId}'."));
                }
            }

            foreach (var portal in map.Portals)
            {
                if (portal.WidthMeters < 0f)
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Portal '{portal.Id}' width must be positive."));
                if (sectorIds.Count > 0 && !sectorIds.Contains(portal.SectorId))
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Warning, $"Portal '{portal.Id}' references missing sector '{portal.SectorId}'."));
            }

            foreach (var link in map.Links)
            {
                if (!portalIds.Contains(link.FromPortalId) || !portalIds.Contains(link.ToPortalId))
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Link '{link.Id}' references missing portal(s)."));
            }

            foreach (var beacon in map.Beacons)
            {
                if (!string.IsNullOrWhiteSpace(beacon.ShapeId) && !shapeIds.Contains(beacon.ShapeId!))
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Beacon '{beacon.Id}' references missing shape '{beacon.ShapeId}'."));
                if (sectorIds.Count > 0 && !string.IsNullOrWhiteSpace(beacon.SectorId) && !sectorIds.Contains(beacon.SectorId!))
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Warning, $"Beacon '{beacon.Id}' references missing sector '{beacon.SectorId}'."));
                if (string.IsNullOrWhiteSpace(beacon.ShapeId) && !beacon.ActivationRadiusMeters.HasValue)
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Warning, $"Beacon '{beacon.Id}' has no activation area."));
            }

            foreach (var marker in map.Markers)
            {
                if (!string.IsNullOrWhiteSpace(marker.ShapeId) && !shapeIds.Contains(marker.ShapeId!))
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Marker '{marker.Id}' references missing shape '{marker.ShapeId}'."));
            }

            if (map.Branches.Count > 0)
            {
                var branchIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var branch in map.Branches)
                {
                    if (branch == null)
                        continue;
                    if (!branchIds.Add(branch.Id))
                        issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Duplicate branch id '{branch.Id}'."));
                    if (!sectorIds.Contains(branch.SectorId))
                        issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Branch '{branch.Id}' references missing sector '{branch.SectorId}'."));
                    if (!string.IsNullOrWhiteSpace(branch.EntryPortalId) && !portalIds.Contains(branch.EntryPortalId!))
                        issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Branch '{branch.Id}' references missing entry portal '{branch.EntryPortalId}'."));

                    foreach (var exit in branch.Exits)
                    {
                        if (exit == null || string.IsNullOrWhiteSpace(exit.PortalId))
                            continue;
                        if (!portalIds.Contains(exit.PortalId))
                            issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Branch '{branch.Id}' references missing exit portal '{exit.PortalId}'."));
                    }
                }
            }

            foreach (var wall in map.Walls)
            {
                if (!shapeIds.Contains(wall.ShapeId))
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Wall '{wall.Id}' references missing shape '{wall.ShapeId}'."));
                if (wall.WidthMeters < 0f)
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Wall '{wall.Id}' width must be non-negative."));
                if (!string.IsNullOrWhiteSpace(wall.MaterialId))
                {
                    var materialId = wall.MaterialId!;
                    if (!materialIds.Contains(materialId) && !TrackMaterialLibrary.IsPreset(materialId))
                        issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Wall '{wall.Id}' references missing material '{materialId}'."));
                }
            }

            foreach (var approach in map.Approaches)
            {
                if (sectorIds.Count > 0 && !sectorIds.Contains(approach.SectorId))
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Warning, $"Approach references missing sector '{approach.SectorId}'."));
                if (!string.IsNullOrWhiteSpace(approach.EntryPortalId) && !portalIds.Contains(approach.EntryPortalId!))
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Approach '{approach.SectorId}' references missing entry portal '{approach.EntryPortalId}'."));
                if (!string.IsNullOrWhiteSpace(approach.ExitPortalId) && !portalIds.Contains(approach.ExitPortalId!))
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Approach '{approach.SectorId}' references missing exit portal '{approach.ExitPortalId}'."));
                if (approach.WidthMeters.HasValue && approach.WidthMeters.Value <= 0f)
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Approach '{approach.SectorId}' width must be positive."));
                if (approach.LengthMeters.HasValue && approach.LengthMeters.Value <= 0f)
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Approach '{approach.SectorId}' length must be positive."));
                if (approach.AlignmentToleranceDegrees.HasValue && approach.AlignmentToleranceDegrees.Value < 0f)
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Approach '{approach.SectorId}' tolerance must be non-negative."));
            }
        }

        private sealed class RegionSample
        {
            public RegionSample(string id, ShapeDefinition shape, float widthMeters, bool closedCentered)
            {
                Id = id;
                Shape = shape;
                WidthMeters = widthMeters;
                ClosedCentered = closedCentered;
                Bounds = GetBounds(shape, widthMeters, closedCentered);
            }

            public string Id { get; }
            public ShapeDefinition Shape { get; }
            public float WidthMeters { get; }
            public bool ClosedCentered { get; }
            public Bounds Bounds { get; }
        }

        private readonly struct Bounds
        {
            public Bounds(float minX, float minZ, float maxX, float maxZ)
            {
                MinX = minX;
                MinZ = minZ;
                MaxX = maxX;
                MaxZ = maxZ;
            }

            public float MinX { get; }
            public float MinZ { get; }
            public float MaxX { get; }
            public float MaxZ { get; }
        }

        private static void ValidateRegionOverlaps(TrackMapDefinition map, List<TrackMapIssue> issues)
        {
            if (map.Shapes.Count == 0 || map.Areas.Count == 0)
                return;

            var areaManager = new TrackAreaManager(map.Shapes, map.Areas);

            var hasDrivableAreas = HasDrivableAreas(map.Areas);
            var drivableRegions = new List<RegionSample>();
            var nonDrivableAreas = new List<RegionSample>();

            if (hasDrivableAreas)
            {
                foreach (var area in map.Areas)
                {
                    if (area == null)
                        continue;
                    if (IsOverlayArea(area) || IsNonDrivableArea(area))
                        continue;
                    if (!areaManager.TryGetShape(area.ShapeId, out var shape))
                        continue;

                    var closedCentered = IsCenteredClosedWidth(area.Metadata);
                    var width = area.WidthMeters.GetValueOrDefault();
                    drivableRegions.Add(new RegionSample($"area:{area.Id}", shape, width, closedCentered));
                }
            }
            foreach (var area in map.Areas)
            {
                if (area == null)
                    continue;
                if (!IsNonDrivableArea(area))
                    continue;
                if (!areaManager.TryGetShape(area.ShapeId, out var shape))
                    continue;

                var closedCentered = IsCenteredClosedWidth(area.Metadata);
                var width = area.WidthMeters.GetValueOrDefault();
                nonDrivableAreas.Add(new RegionSample($"area:{area.Id}", shape, width, closedCentered));
            }

            if (drivableRegions.Count == 0)
                return;

            var step = ResolveOverlapStep(map.Metadata.CellSizeMeters);
            var occupied = new Dictionary<long, string>();
            var overlapPairs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var blockedRegions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var region in drivableRegions)
            {
                var bounds = region.Bounds;
                var minX = AlignMin(bounds.MinX, step);
                var minZ = AlignMin(bounds.MinZ, step);
                var maxX = AlignMax(bounds.MaxX, step);
                var maxZ = AlignMax(bounds.MaxZ, step);

                for (var x = minX; x <= maxX; x += step)
                {
                    for (var z = minZ; z <= maxZ; z += step)
                    {
                        var sample = new Vector2(x + step * 0.5f, z + step * 0.5f);
                        if (!Contains(region.Shape, sample, region.WidthMeters, region.ClosedCentered))
                            continue;

                        var key = MakeKey(sample.X, sample.Y, step);
                        if (occupied.TryGetValue(key, out var existing) && !string.Equals(existing, region.Id, StringComparison.OrdinalIgnoreCase))
                        {
                            var pairKey = MakePairKey(existing, region.Id);
                            if (overlapPairs.Add(pairKey))
                                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Drivable regions '{existing}' and '{region.Id}' overlap."));
                        }
                        else
                        {
                            occupied[key] = region.Id;
                        }

                        if (nonDrivableAreas.Count == 0)
                            continue;

                        foreach (var blocked in nonDrivableAreas)
                        {
                            if (!Contains(blocked.Shape, sample, blocked.WidthMeters, blocked.ClosedCentered))
                                continue;
                            if (blockedRegions.Add($"{region.Id}|{blocked.Id}"))
                            {
                                issues.Add(new TrackMapIssue(
                                    TrackMapIssueSeverity.Error,
                                    $"Drivable region '{region.Id}' overlaps non-drivable area '{blocked.Id}'."));
                            }
                        }
                    }
                }
            }
        }

        private static bool IsOverlayArea(TrackAreaDefinition area)
        {
            if (area == null)
                return false;

            switch (area.Type)
            {
                case TrackAreaType.Start:
                case TrackAreaType.Finish:
                case TrackAreaType.Checkpoint:
                case TrackAreaType.Intersection:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsNonDrivableArea(TrackAreaDefinition area)
        {
            if (area == null)
                return false;
            if (area.Type == TrackAreaType.Boundary || area.Type == TrackAreaType.OffTrack)
                return true;
            return false;
        }

        private static bool HasDrivableAreas(IReadOnlyList<TrackAreaDefinition> areas)
        {
            if (areas == null || areas.Count == 0)
                return false;
            foreach (var area in areas)
            {
                if (area == null)
                    continue;
                if (IsOverlayArea(area) || IsNonDrivableArea(area))
                    continue;
                return true;
            }
            return false;
        }

        private static float ResolveOverlapStep(float cellSizeMeters)
        {
            if (cellSizeMeters <= 0f)
                return 1f;
            return Math.Max(0.5f, Math.Min(5f, cellSizeMeters));
        }

        private static float AlignMin(float value, float step)
        {
            return (float)Math.Floor(value / step) * step;
        }

        private static float AlignMax(float value, float step)
        {
            return (float)Math.Ceiling(value / step) * step;
        }

        private static long MakeKey(float x, float z, float step)
        {
            var gx = (int)Math.Floor(x / step);
            var gz = (int)Math.Floor(z / step);
            return ((long)gx << 32) | (uint)gz;
        }

        private static string MakePairKey(string a, string b)
        {
            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase) <= 0
                ? $"{a}|{b}"
                : $"{b}|{a}";
        }

        private static Bounds GetBounds(ShapeDefinition shape, float widthMeters, bool closedCentered)
        {
            if (shape == null)
                return new Bounds(0f, 0f, 0f, 0f);

            var expand = Math.Abs(widthMeters);
            switch (shape.Type)
            {
                case ShapeType.Rectangle:
                {
                    var minX = Math.Min(shape.X, shape.X + shape.Width);
                    var maxX = Math.Max(shape.X, shape.X + shape.Width);
                    var minZ = Math.Min(shape.Z, shape.Z + shape.Height);
                    var maxZ = Math.Max(shape.Z, shape.Z + shape.Height);
                    return new Bounds(minX - expand, minZ - expand, maxX + expand, maxZ + expand);
                }
                case ShapeType.Circle:
                {
                    var radius = Math.Abs(shape.Radius);
                    var outer = Math.Max(radius, radius + expand);
                    return new Bounds(shape.X - outer, shape.Z - outer, shape.X + outer, shape.Z + outer);
                }
                case ShapeType.Ring:
                {
                    var ringWidth = shape.RingWidth > 0f ? shape.RingWidth : expand;
                    if (shape.Radius > 0f)
                    {
                        var outer = Math.Abs(shape.Radius) + Math.Abs(ringWidth);
                        return new Bounds(shape.X - outer, shape.Z - outer, shape.X + outer, shape.Z + outer);
                    }

                    var minX = Math.Min(shape.X, shape.X + shape.Width);
                    var maxX = Math.Max(shape.X, shape.X + shape.Width);
                    var minZ = Math.Min(shape.Z, shape.Z + shape.Height);
                    var maxZ = Math.Max(shape.Z, shape.Z + shape.Height);
                    var ring = Math.Abs(ringWidth);
                    return new Bounds(minX - ring, minZ - ring, maxX + ring, maxZ + ring);
                }
                case ShapeType.Polygon:
                case ShapeType.Polyline:
                {
                    if (shape.Points == null || shape.Points.Count == 0)
                        return new Bounds(0f, 0f, 0f, 0f);
                    var minX = float.MaxValue;
                    var minZ = float.MaxValue;
                    var maxX = float.MinValue;
                    var maxZ = float.MinValue;
                    foreach (var point in shape.Points)
                    {
                        if (point.X < minX) minX = point.X;
                        if (point.Y < minZ) minZ = point.Y;
                        if (point.X > maxX) maxX = point.X;
                        if (point.Y > maxZ) maxZ = point.Y;
                    }

                    var expandBy = expand;
                    if (closedCentered && expandBy > 0f)
                        expandBy *= 0.5f;

                    return new Bounds(minX - expandBy, minZ - expandBy, maxX + expandBy, maxZ + expandBy);
                }
                default:
                    return new Bounds(0f, 0f, 0f, 0f);
            }
        }

        private static bool Contains(ShapeDefinition shape, Vector2 position, float widthMeters, bool closedCentered)
        {
            if (shape == null)
                return false;
            switch (shape.Type)
            {
                case ShapeType.Rectangle:
                    return widthMeters > 0f
                        ? ContainsRectanglePath(shape, position, widthMeters)
                        : ContainsRectangle(shape, position);
                case ShapeType.Circle:
                    return widthMeters > 0f
                        ? ContainsCirclePath(shape, position, widthMeters)
                        : ContainsCircle(shape, position);
                case ShapeType.Ring:
                    return widthMeters > 0f
                        ? ContainsRingPath(shape, position, widthMeters)
                        : ContainsRing(shape, position);
                case ShapeType.Polygon:
                    return ContainsPolygonPath(shape.Points, position, widthMeters, closedCentered);
                case ShapeType.Polyline:
                    return ContainsPolylinePath(shape.Points, position, widthMeters, closedCentered);
                default:
                    return false;
            }
        }

        private static bool ContainsRectangle(ShapeDefinition shape, Vector2 position)
        {
            var minX = shape.X;
            var minZ = shape.Z;
            var maxX = shape.X + shape.Width;
            var maxZ = shape.Z + shape.Height;
            return position.X >= minX && position.X <= maxX &&
                   position.Y >= minZ && position.Y <= maxZ;
        }

        private static bool ContainsCircle(ShapeDefinition shape, Vector2 position)
        {
            var dx = position.X - shape.X;
            var dz = position.Y - shape.Z;
            return (dx * dx + dz * dz) <= (shape.Radius * shape.Radius);
        }

        private static bool ContainsRectanglePath(ShapeDefinition shape, Vector2 position, float widthMeters)
        {
            if (widthMeters <= 0f)
                return false;

            var minX = Math.Min(shape.X, shape.X + shape.Width);
            var maxX = Math.Max(shape.X, shape.X + shape.Width);
            var minZ = Math.Min(shape.Z, shape.Z + shape.Height);
            var maxZ = Math.Max(shape.Z, shape.Z + shape.Height);
            var centerX = (minX + maxX) * 0.5f;
            var centerZ = (minZ + maxZ) * 0.5f;
            var lengthX = Math.Abs(shape.Width);
            var lengthZ = Math.Abs(shape.Height);
            var halfWidth = widthMeters * 0.5f;
            if (lengthX >= lengthZ)
            {
                if (position.X < minX || position.X > maxX)
                    return false;
                return Math.Abs(position.Y - centerZ) <= halfWidth;
            }

            if (position.Y < minZ || position.Y > maxZ)
                return false;
            return Math.Abs(position.X - centerX) <= halfWidth;
        }

        private static bool ContainsCirclePath(ShapeDefinition shape, Vector2 position, float widthMeters)
        {
            var radius = Math.Abs(shape.Radius);
            if (radius <= 0f || widthMeters <= 0f)
                return false;

            var dist = Vector2.Distance(new Vector2(shape.X, shape.Z), position);
            var inner = Math.Max(0f, radius - widthMeters);
            return dist >= inner && dist <= radius;
        }

        private static bool ContainsRing(ShapeDefinition shape, Vector2 position)
        {
            var ringWidth = Math.Abs(shape.RingWidth);
            if (ringWidth <= 0f)
                return false;

            if (shape.Radius > 0f)
                return ContainsRingCircle(shape, position, ringWidth);

            return ContainsRingRectangle(shape, position, ringWidth);
        }

        private static bool ContainsRingPath(ShapeDefinition shape, Vector2 position, float widthMeters)
        {
            var ringWidth = Math.Abs(widthMeters);
            if (ringWidth <= 0f)
                return false;

            if (shape.Radius > 0f)
                return ContainsRingCircle(shape, position, ringWidth);

            return ContainsRingRectangle(shape, position, ringWidth);
        }

        private static bool ContainsRingCircle(ShapeDefinition shape, Vector2 position, float ringWidth)
        {
            var dx = position.X - shape.X;
            var dz = position.Y - shape.Z;
            var distSq = dx * dx + dz * dz;
            var inner = Math.Abs(shape.Radius);
            var outer = inner + ringWidth;
            return distSq >= (inner * inner) && distSq <= (outer * outer);
        }

        private static bool ContainsRingRectangle(ShapeDefinition shape, Vector2 position, float ringWidth)
        {
            var innerMinX = shape.X;
            var innerMinZ = shape.Z;
            var innerMaxX = shape.X + shape.Width;
            var innerMaxZ = shape.Z + shape.Height;
            if (innerMaxX <= innerMinX || innerMaxZ <= innerMinZ)
                return false;

            var outerMinX = innerMinX - ringWidth;
            var outerMinZ = innerMinZ - ringWidth;
            var outerMaxX = innerMaxX + ringWidth;
            var outerMaxZ = innerMaxZ + ringWidth;

            var insideOuter = position.X >= outerMinX && position.X <= outerMaxX &&
                              position.Y >= outerMinZ && position.Y <= outerMaxZ;
            if (!insideOuter)
                return false;

            var insideInner = position.X >= innerMinX && position.X <= innerMaxX &&
                              position.Y >= innerMinZ && position.Y <= innerMaxZ;
            return !insideInner;
        }

        private static bool ContainsPolygon(IReadOnlyList<Vector2> points, Vector2 position)
        {
            if (points == null || points.Count < 3)
                return false;

            var inside = false;
            var j = points.Count - 1;
            for (var i = 0; i < points.Count; i++)
            {
                var xi = points[i].X;
                var zi = points[i].Y;
                var xj = points[j].X;
                var zj = points[j].Y;

                var intersect = ((zi > position.Y) != (zj > position.Y)) &&
                                (position.X < (xj - xi) * (position.Y - zi) / (zj - zi + float.Epsilon) + xi);
                if (intersect)
                    inside = !inside;
                j = i;
            }

            return inside;
        }

        private static bool ContainsPolygonPath(
            IReadOnlyList<Vector2> points,
            Vector2 position,
            float widthMeters,
            bool closedCentered)
        {
            if (points == null || points.Count < 3)
                return false;

            var width = Math.Abs(widthMeters);
            if (width <= 0f)
                return ContainsPolygon(points, position);

            if (closedCentered)
            {
                var radius = width * 0.5f;
                return DistanceToPolylineSquared(points, position, true) <= (radius * radius);
            }

            if (!ContainsPolygon(points, position))
                return false;
            return DistanceToPolylineSquared(points, position, true) <= (width * width);
        }

        private static bool ContainsPolylinePath(
            IReadOnlyList<Vector2> points,
            Vector2 position,
            float widthMeters,
            bool closedCentered)
        {
            if (points == null || points.Count < 2)
                return false;

            var width = Math.Abs(widthMeters);
            if (width <= 0f)
                return false;

            var closed = IsClosedPolyline(points);
            if (!closed)
            {
                var radius = width * 0.5f;
                return DistanceToPolylineSquared(points, position, false) <= (radius * radius);
            }

            if (closedCentered)
            {
                var radius = width * 0.5f;
                return DistanceToPolylineSquared(points, position, true) <= (radius * radius);
            }

            if (!ContainsPolygon(points, position))
                return false;
            return DistanceToPolylineSquared(points, position, true) <= (width * width);
        }

        private static float DistanceToPolylineSquared(IReadOnlyList<Vector2> points, Vector2 position, bool closed)
        {
            if (points == null || points.Count < 2)
                return float.MaxValue;

            var segmentCount = points.Count - 1;
            var lastIndex = points.Count - 1;
            var lastEqualsFirst = Vector2.DistanceSquared(points[0], points[lastIndex]) <= 0.0001f;

            var best = float.MaxValue;
            for (var i = 0; i < segmentCount; i++)
            {
                var a = points[i];
                var b = points[i + 1];
                var dist = DistanceToSegmentSquared(a, b, position);
                if (dist < best)
                    best = dist;
            }

            if (closed && !lastEqualsFirst)
            {
                var dist = DistanceToSegmentSquared(points[lastIndex], points[0], position);
                if (dist < best)
                    best = dist;
            }

            return best;
        }

        private static float DistanceToSegmentSquared(Vector2 a, Vector2 b, Vector2 p)
        {
            var ab = b - a;
            var ap = p - a;
            var abLenSq = Vector2.Dot(ab, ab);
            if (abLenSq <= float.Epsilon)
                return Vector2.Dot(ap, ap);

            var t = Vector2.Dot(ap, ab) / abLenSq;
            if (t <= 0f)
                return Vector2.Dot(ap, ap);
            if (t >= 1f)
                return Vector2.DistanceSquared(p, b);
            var projection = a + ab * t;
            return Vector2.DistanceSquared(p, projection);
        }

        private static bool IsClosedPolyline(IReadOnlyList<Vector2> points)
        {
            if (points == null || points.Count < 3)
                return false;
            return Vector2.DistanceSquared(points[0], points[points.Count - 1]) <= 0.0001f;
        }

        private static bool TryGetMetadataValue(
            IReadOnlyDictionary<string, string> metadata,
            out string value,
            params string[] keys)
        {
            value = string.Empty;
            if (metadata == null || metadata.Count == 0)
                return false;

            foreach (var key in keys)
            {
                if (metadata.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(raw))
                {
                    value = raw;
                    return true;
                }
            }

            return false;
        }

        private static bool IsCenteredClosedWidth(IReadOnlyDictionary<string, string> metadata)
        {
            if (metadata == null || metadata.Count == 0)
                return false;

            if (!TryGetMetadataValue(metadata, out var mode, "width_mode", "path_width_mode", "width_align", "width_alignment"))
                return false;

            var trimmed = mode.Trim().ToLowerInvariant();
            return trimmed.Contains("center") ||
                   trimmed.Contains("centre") ||
                   trimmed.Contains("both") ||
                   trimmed.Contains("sym");
        }

    }
}




