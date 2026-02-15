using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using TopSpeed.Data;
using TopSpeed.Tracks.Materials;
using TopSpeed.Tracks.Rooms;
using TopSpeed.Tracks.Sounds;
using TopSpeed.Tracks.Areas;
using TopSpeed.Tracks.Beacons;
using TopSpeed.Tracks.Guidance;
using TopSpeed.Tracks.Markers;
using TopSpeed.Tracks.Geometry;
using TopSpeed.Tracks.Sectors;
using TopSpeed.Tracks.Surfaces;
using TopSpeed.Tracks.Topology;
using TopSpeed.Tracks.Volumes;
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
        public float SurfaceResolutionMeters { get; set; } = 5f;
        public TrackWeather Weather { get; set; } = TrackWeather.Sunny;
        public TrackAmbience Ambience { get; set; } = TrackAmbience.NoAmbience;
        public string DefaultMaterialId { get; set; } = "asphalt";
        public bool DefaultMaterialDefined { get; set; }
        public TrackNoise DefaultNoise { get; set; } = TrackNoise.NoNoise;
        public float DefaultWidthMeters { get; set; } = 12f;
        public float? BaseHeightMeters { get; set; }
        public float? DefaultAreaHeightMeters { get; set; }
        public float? DefaultCeilingHeightMeters { get; set; }
        public float? MinX { get; set; }
        public float? MinZ { get; set; }
        public float? MaxX { get; set; }
        public float? MaxZ { get; set; }
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
            Geometries = Array.Empty<GeometryDefinition>();
            Volumes = Array.Empty<TrackVolumeDefinition>();
            Surfaces = Array.Empty<TrackSurfaceDefinition>();
            Profiles = Array.Empty<TrackProfileDefinition>();
            Banks = Array.Empty<TrackBankDefinition>();
            Portals = Array.Empty<PortalDefinition>();
            Links = Array.Empty<LinkDefinition>();
            Beacons = Array.Empty<TrackBeaconDefinition>();
            Markers = Array.Empty<TrackMarkerDefinition>();
            Approaches = Array.Empty<TrackApproachDefinition>();
            Branches = Array.Empty<TrackBranchDefinition>();
            Walls = Array.Empty<TrackWallDefinition>();
            Materials = Array.Empty<TrackMaterialDefinition>();
            Rooms = Array.Empty<TrackRoomDefinition>();
            SoundSources = Array.Empty<TrackSoundSourceDefinition>();
        }

        public TrackMapDefinition(
            TrackMapMetadata metadata,
            List<TrackSectorDefinition> sectors,
            List<TrackAreaDefinition> areas,
            List<GeometryDefinition> geometries,
            List<TrackVolumeDefinition> volumes,
            List<TrackSurfaceDefinition> surfaces,
            List<TrackProfileDefinition> profiles,
            List<TrackBankDefinition> banks,
            List<PortalDefinition> portals,
            List<LinkDefinition> links,
            List<TrackBeaconDefinition> beacons,
            List<TrackMarkerDefinition> markers,
            List<TrackApproachDefinition> approaches,
            List<TrackBranchDefinition> branches,
            List<TrackWallDefinition> walls,
            List<TrackMaterialDefinition> materials,
            List<TrackRoomDefinition> rooms,
            List<TrackSoundSourceDefinition> soundSources)
        {
            Metadata = metadata;
            Sectors = sectors ?? new List<TrackSectorDefinition>();
            Areas = areas ?? new List<TrackAreaDefinition>();
            Geometries = geometries ?? new List<GeometryDefinition>();
            Volumes = volumes ?? new List<TrackVolumeDefinition>();
            Surfaces = surfaces ?? new List<TrackSurfaceDefinition>();
            Profiles = profiles ?? new List<TrackProfileDefinition>();
            Banks = banks ?? new List<TrackBankDefinition>();
            Portals = portals ?? new List<PortalDefinition>();
            Links = links ?? new List<LinkDefinition>();
            Beacons = beacons ?? new List<TrackBeaconDefinition>();
            Markers = markers ?? new List<TrackMarkerDefinition>();
            Approaches = approaches ?? new List<TrackApproachDefinition>();
            Branches = branches ?? new List<TrackBranchDefinition>();
            Walls = walls ?? new List<TrackWallDefinition>();
            Materials = materials ?? new List<TrackMaterialDefinition>();
            Rooms = rooms ?? new List<TrackRoomDefinition>();
            SoundSources = soundSources ?? new List<TrackSoundSourceDefinition>();
        }

        public TrackMapMetadata Metadata { get; }
        public IReadOnlyList<TrackSectorDefinition> Sectors { get; }
        public IReadOnlyList<TrackAreaDefinition> Areas { get; }
        public IReadOnlyList<GeometryDefinition> Geometries { get; }
        public IReadOnlyList<TrackVolumeDefinition> Volumes { get; }
        public IReadOnlyList<TrackSurfaceDefinition> Surfaces { get; }
        public IReadOnlyList<TrackProfileDefinition> Profiles { get; }
        public IReadOnlyList<TrackBankDefinition> Banks { get; }
        public IReadOnlyList<PortalDefinition> Portals { get; }
        public IReadOnlyList<LinkDefinition> Links { get; }
        public IReadOnlyList<TrackBeaconDefinition> Beacons { get; }
        public IReadOnlyList<TrackMarkerDefinition> Markers { get; }
        public IReadOnlyList<TrackApproachDefinition> Approaches { get; }
        public IReadOnlyList<TrackBranchDefinition> Branches { get; }
        public IReadOnlyList<TrackWallDefinition> Walls { get; }
        public IReadOnlyList<TrackMaterialDefinition> Materials { get; }
        public IReadOnlyList<TrackRoomDefinition> Rooms { get; }
        public IReadOnlyList<TrackSoundSourceDefinition> SoundSources { get; }
    }

    public static class TrackMapFormat
    {
        private const string MapExtension = ".tsm";
        private enum PolygonPlanarityMode
        {
            Strict = 0,
            Relaxed = 1,
            Ignore = 2
        }
        private static readonly HashSet<string> SectorKnownKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "id",
            "type",
            "name",
            "code",
            "area",
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
            "geometry",
            "geometry_id",
            "volume",
            "volume_id",
            "surface",
            "surface_id",
            "material",
            "noise",
            "width",
            "elevation",
            "height",
            "ceiling",
            "ceiling_height",
            "thickness",
            "offset",
            "min_y",
            "max_y",
            "volume_mode",
            "volume_offset_mode",
            "offset_mode",
            "offset_anchor",
            "volume_offset_anchor",
            "offset_align",
            "volume_offset_align",
            "volume_offset_space",
            "offset_space",
            "volume_minmax_space",
            "minmax_space",
            "bounds_space",
            "volume_bounds_space",
            "room",
            "reverb_time",
            "reverb_gain",
            "reflection_wet",
            "hf_decay_ratio",
            "early_reflections_gain",
            "late_reverb_gain",
            "diffusion",
            "air_absorption",
            "air_absorption_override",
            "air_absorption_override_low",
            "air_absorption_override_mid",
            "air_absorption_override_high",
            "occlusion_scale",
            "occlusion_override",
            "transmission_scale",
            "transmission_override",
            "transmission_override_low",
            "transmission_override_mid",
            "transmission_override_high",
            "flags",
            "flag",
            "caps",
            "capabilities",
            "sources",
            "source",
            "sound_sources",
            "sound_source"
        };
        private static readonly HashSet<string> GeometryKnownKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "id",
            "type",
            "name",
            "points3d",
            "points_3d",
            "points3",
            "point3d",
            "point_3d",
            "point3",
            "vertices",
            "vertex",
            "vertices3d",
            "vertices_3d",
            "vertex3d",
            "vertex_3d",
            "mesh_points3d",
            "mesh_points_3d",
            "mesh_points3",
            "mesh_vertices3d",
            "mesh_vertices_3d",
            "mesh_vertices",
            "mesh_vertex3d",
            "mesh_vertex_3d",
            "mesh_vertex",
            "triangles",
            "triangle_indices",
            "indices",
            "faces",
            "tris",
            "tri",
            "triangles3d",
            "triangle3d",
            "triangle_points",
            "triangle_points3d",
            "tri_points",
            "tri_points3d",
            "mesh_triangles",
            "mesh_triangle_indices",
            "mesh_indices",
            "mesh_faces",
            "mesh_tris",
            "index_base",
            "indices_base",
            "indexbase"
        };
        private static readonly HashSet<string> VolumeKnownKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "id",
            "type",
            "name",
            "geometry",
            "geometry_id",
            "mesh",
            "points3d",
            "points_3d",
            "points3",
            "vertices",
            "vertex",
            "x",
            "y",
            "z",
            "elevation",
            "center",
            "position",
            "pos",
            "origin",
            "width",
            "height",
            "depth",
            "length",
            "volume_height",
            "volume_thickness",
            "size",
            "dimensions",
            "extents",
            "radius",
            "r",
            "min_x",
            "max_x",
            "min_y",
            "max_y",
            "min_z",
            "max_z",
            "offset",
            "volume_offset",
            "volume_center",
            "offset_mode",
            "offset_anchor",
            "offset_align",
            "volume_offset_mode",
            "volume_offset_anchor",
            "volume_offset_align",
            "rotation",
            "rotation_deg",
            "rotation_degrees",
            "rot",
            "rot_deg",
            "rot_degrees",
            "yaw",
            "pitch",
            "roll",
            "rotation_yaw",
            "rotation_pitch",
            "rotation_roll"
        };
        private static readonly HashSet<string> SurfaceKnownKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "id",
            "type",
            "name",
            "geometry",
            "profile",
            "bank",
            "layer",
            "resolution",
            "material"
        };
        private static readonly HashSet<string> ProfileKnownKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "id",
            "type",
            "name"
        };
        private static readonly HashSet<string> BankKnownKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "id",
            "type",
            "name",
            "side",
            "direction"
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
            "geometry",
            "volume",
            "volume_id",
            "x",
            "y",
            "z",
            "heading",
            "orientation",
            "radius",
            "activation_radius",
            "height",
            "thickness",
            "offset",
            "min_y",
            "max_y",
            "volume_mode",
            "volume_thickness",
            "volume_height",
            "volume_offset",
            "volume_center",
            "volume_offset_mode",
            "offset_mode",
            "offset_anchor",
            "volume_offset_anchor",
            "offset_align",
            "volume_offset_align",
            "volume_offset_space",
            "offset_space",
            "volume_minmax_space",
            "minmax_space",
            "bounds_space",
            "volume_bounds_space"
        };
        private static readonly HashSet<string> MarkerKnownKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "id",
            "type",
            "name",
            "geometry",
            "volume",
            "volume_id",
            "x",
            "y",
            "z",
            "heading",
            "orientation",
            "height",
            "thickness",
            "offset",
            "min_y",
            "max_y",
            "volume_mode",
            "volume_thickness",
            "volume_height",
            "volume_offset",
            "volume_center",
            "volume_offset_mode",
            "offset_mode",
            "offset_anchor",
            "volume_offset_anchor",
            "offset_align",
            "volume_offset_align",
            "volume_offset_space",
            "offset_space",
            "volume_minmax_space",
            "minmax_space",
            "bounds_space",
            "volume_bounds_space"
        };
        private static readonly HashSet<string> ApproachKnownKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "id",
            "sector",
            "name",
            "volume",
            "volume_id",
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
            "align_tol",
            "height",
            "thickness",
            "offset",
            "min_y",
            "max_y",
            "volume_mode",
            "volume_thickness",
            "volume_height",
            "volume_offset",
            "volume_center",
            "volume_offset_mode",
            "offset_mode",
            "offset_anchor",
            "volume_offset_anchor",
            "offset_align",
            "volume_offset_align",
            "volume_offset_space",
            "offset_space",
            "volume_minmax_space",
            "minmax_space",
            "bounds_space",
            "volume_bounds_space"
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
            "reflection_wet",
            "hf_decay_ratio",
            "early_reflections_gain",
            "late_reverb_gain",
            "diffusion",
            "air_absorption",
            "air_absorption_override",
            "air_absorption_override_low",
            "air_absorption_override_mid",
            "air_absorption_override_high",
            "occlusion_scale",
            "occlusion_override",
            "transmission_scale"
            ,
            "transmission_override",
            "transmission_override_low",
            "transmission_override_mid",
            "transmission_override_high"
        };

        public static bool TryResolvePath(string nameOrPath, out string path)
        {
            path = string.Empty;
            if (string.IsNullOrWhiteSpace(nameOrPath))
                return false;

            if (nameOrPath.IndexOfAny(new[] { '\\', '/' }) >= 0)
            {
                if (Directory.Exists(nameOrPath))
                {
                    path = Path.Combine(nameOrPath, "track" + MapExtension);
                    return File.Exists(path);
                }

                path = nameOrPath;
                return File.Exists(path) && LooksLikeMap(path);
            }

            if (!Path.HasExtension(nameOrPath))
            {
                var folderPath = Path.Combine(AppContext.BaseDirectory, "Tracks", nameOrPath, "track" + MapExtension);
                if (File.Exists(folderPath))
                {
                    path = folderPath;
                    return true;
                }

                return false;
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
            var geometries = new List<GeometryDefinition>();
            var volumes = new List<TrackVolumeDefinition>();
            var surfaces = new List<TrackSurfaceDefinition>();
            var profiles = new List<TrackProfileDefinition>();
            var banks = new List<TrackBankDefinition>();
            var portals = new List<PortalDefinition>();
            var links = new List<LinkDefinition>();
            var beacons = new List<TrackBeaconDefinition>();
            var markers = new List<TrackMarkerDefinition>();
            var approaches = new List<TrackApproachDefinition>();
            var walls = new List<TrackWallDefinition>();
            var branches = new List<TrackBranchDefinition>();
            var materials = new List<TrackMaterialDefinition>();
            var rooms = new List<TrackRoomDefinition>();
            var soundSources = new List<TrackSoundSourceDefinition>();
            var guideBlocks = new List<SectionBlock>();
            var branchBlocks = new List<SectionBlock>();
            var mapRoot = Path.GetDirectoryName(path) ?? AppContext.BaseDirectory;

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
                        issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Grid cell sections are no longer supported. Use geometry and areas instead.", block.StartLine));
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
                    case "geometry":
                        ApplyGeometry(geometries, block, issues);
                        break;
                    case "volume":
                        ApplyVolume(volumes, block, issues);
                        break;
                    case "surface":
                        ApplySurface(surfaces, block, issues);
                        break;
                    case "profile":
                        ApplyProfile(profiles, block, issues);
                        break;
                    case "bank":
                        ApplyBank(banks, block, issues);
                        break;
                    case "portal":
                        ApplyPortal(portals, block, issues, metadata.BaseHeightMeters);
                        break;
                    case "link":
                        ApplyLink(links, block, issues);
                        break;
                    case "path":
                        issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Path sections are not supported. Use [geometry] with type=polyline or type=spline instead.", block.StartLine));
                        break;
                    case "lane":
                        issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Lane sections are not supported. Use area metadata for optional lane guidance.", block.StartLine));
                        break;
                    case "beacon":
                        ApplyBeacon(beacons, block, issues, metadata.BaseHeightMeters);
                        break;
                    case "marker":
                        ApplyMarker(markers, block, issues, metadata.BaseHeightMeters);
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
                    case "ambient":
                        ApplySoundSource(soundSources, block, TrackSoundSourceType.Ambient, mapRoot, issues);
                        break;
                    case "static_source":
                    case "static":
                        ApplySoundSource(soundSources, block, TrackSoundSourceType.Static, mapRoot, issues);
                        break;
                    case "moving_source":
                    case "moving":
                        ApplySoundSource(soundSources, block, TrackSoundSourceType.Moving, mapRoot, issues);
                        break;
                    case "random_source":
                    case "random":
                        ApplySoundSource(soundSources, block, TrackSoundSourceType.Random, mapRoot, issues);
                        break;
                    case "acoustic_material":
                        issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "acoustic_material sections are not supported. Use [material] instead.", block.StartLine));
                        break;
                    case "room":
                        ApplyRoom(rooms, block, issues);
                        break;
                    case "turn":
                        issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Turn sections are not supported. Use [geometry], [area], [portal], and [approach] instead.", block.StartLine));
                        break;
                    case "curve":
                        issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Curve sections are not supported. Use areas, portals, and approach metadata instead.", block.StartLine));
                        break;
                }
            }

            if (guideBlocks.Count > 0)
                ApplyGuides(guideBlocks, sectors, areas, geometries, portals, approaches, issues);
            if (branchBlocks.Count > 0)
                ApplyBranches(branchBlocks, sectors, areas, portals, branches, issues);

            ValidateVolumeReferences(volumes, areas, portals, beacons, markers, approaches, geometries, issues);
            ValidateSoundSourceReferences(soundSources, areas, geometries, issues);

            map = new TrackMapDefinition(metadata, sectors, areas, geometries, volumes, surfaces, profiles, banks, portals, links, beacons, markers, approaches, branches, walls, materials, rooms, soundSources);
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

            if (TryGetValue(block, "surface_resolution", out var surfaceResRaw) ||
                TryGetValue(block, "surface_cell_size", out surfaceResRaw) ||
                TryGetValue(block, "surface_grid", out surfaceResRaw))
            {
                if (TryFloat(surfaceResRaw, out var surfaceRes))
                    metadata.SurfaceResolutionMeters = Math.Max(0.1f, surfaceRes);
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

            if (TryGetValue(block, "min_x", out var minXRaw) && TryFloat(minXRaw, out var minX))
                metadata.MinX = minX;

            if (TryGetValue(block, "min_z", out var minZRaw) && TryFloat(minZRaw, out var minZ))
                metadata.MinZ = minZ;

            if (TryGetValue(block, "max_x", out var maxXRaw) && TryFloat(maxXRaw, out var maxX))
                metadata.MaxX = maxX;

            if (TryGetValue(block, "max_z", out var maxZRaw) && TryFloat(maxZRaw, out var maxZ))
                metadata.MaxZ = maxZ;

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

        private static void ApplyGeometry(
            List<GeometryDefinition> geometries,
            SectionBlock block,
            List<TrackMapIssue> issues)
        {
            if (!TryReadId(block, out var id))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Geometry requires an id.", block.StartLine));
                return;
            }

            if (geometries.Any(g => string.Equals(g.Id, id, StringComparison.OrdinalIgnoreCase)))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Duplicate geometry id '{id}'.", block.StartLine));
                return;
            }

            if (!TryGetValue(block, "type", out var rawType) ||
                string.IsNullOrWhiteSpace(rawType) ||
                !TryGeometryType(rawType, out var geometryType))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Geometry requires a valid type.", block.StartLine));
                return;
            }

            var points = new List<Vector3>();
            var triangleIndices = new List<int>();
            var hasPoints = TryParsePoints3D(block, out points);

            if (geometryType == GeometryType.Mesh)
            {
                var hasTrianglePoints = TryParseTrianglePoints3D(block, out var trianglePoints);
                var hasIndices = TryParseTriangleIndices(block, out triangleIndices);
                var hasIndexBase = TryIntAny(block, out var indexBaseValue, "index_base", "indices_base", "indexbase");
                var indexBase = hasIndexBase ? indexBaseValue : 0;

                // Mesh payload must be explicit and unambiguous: either triangles3d or vertices+indices.
                if (hasTrianglePoints && (hasPoints || hasIndices || hasIndexBase))
                {
                    issues.Add(new TrackMapIssue(
                        TrackMapIssueSeverity.Error,
                        "Mesh payload is ambiguous. Use either triangles3d OR points3d/vertices + triangle indices.",
                        block.StartLine));
                    return;
                }

                if (hasTrianglePoints)
                {
                    if (trianglePoints.Count < 3 || (trianglePoints.Count % 3) != 0)
                    {
                        issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Mesh triangles3d must be a multiple of 3 points.", block.StartLine));
                        return;
                    }

                    points = trianglePoints;
                    triangleIndices = new List<int>(points.Count);
                    for (var i = 0; i < points.Count; i++)
                        triangleIndices.Add(i);
                }
                else
                {
                    if (!hasPoints)
                    {
                        issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Mesh requires vertices (points3d/vertices3d) or triangles3d.", block.StartLine));
                        return;
                    }

                    if (!hasIndices)
                    {
                        issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Mesh requires triangle indices (triangles/indices/faces).", block.StartLine));
                        return;
                    }

                    if (triangleIndices.Count < 3 || (triangleIndices.Count % 3) != 0)
                    {
                        issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Mesh triangle indices must be a multiple of 3.", block.StartLine));
                        return;
                    }

                    if (indexBase != 0)
                    {
                        for (var i = 0; i < triangleIndices.Count; i++)
                            triangleIndices[i] -= indexBase;
                    }

                    for (var i = 0; i < triangleIndices.Count; i++)
                    {
                        var index = triangleIndices[i];
                        if (index < 0 || index >= points.Count)
                        {
                            issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Mesh index {index} is out of range for {points.Count} vertices.", block.StartLine));
                            return;
                        }
                    }
                }

                if (!ValidateMeshTriangles(points, triangleIndices, issues, block.StartLine))
                    return;
            }
            else
            {
                if (!hasPoints)
                {
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Geometry requires points3d.", block.StartLine));
                    return;
                }

                if ((geometryType == GeometryType.Polyline || geometryType == GeometryType.Spline) && points.Count < 2)
                {
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Polyline requires at least 2 points.", block.StartLine));
                    return;
                }

                if (geometryType == GeometryType.Polygon)
                {
                    var normalized = NormalizePolygonPoints3D(points);
                    if (normalized.Count < 3)
                    {
                        issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Polygon requires at least 3 distinct points.", block.StartLine));
                        return;
                    }

                    if (!TryGetPlane(normalized, out var planeOrigin, out var planeNormal))
                    {
                        issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Polygon points are collinear.", block.StartLine));
                        return;
                    }

                    var planarityMode = TryParsePolygonPlanarityMode(block, out var parsedPlanarity)
                        ? parsedPlanarity
                        : PolygonPlanarityMode.Strict;

                    if (planarityMode == PolygonPlanarityMode.Strict && !IsPlanar(normalized, planeOrigin, planeNormal))
                    {
                        issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Polygon points must be planar.", block.StartLine));
                        return;
                    }

                    var projected = ProjectToPlane(normalized, planeOrigin, planeNormal);
                    var normalized2D = NormalizePolygonPoints(projected);
                    if (normalized2D.Count < 3)
                    {
                        issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Polygon requires at least 3 distinct points.", block.StartLine));
                        return;
                    }

                    if (!ValidatePolygon(normalized2D, id, issues, block.StartLine))
                        return;

                    points = normalized;
                }
            }

            var name = TryGetValue(block, "name", out var nameValue) ? nameValue : null;
            var metadata = CollectGeometryMetadata(block);
            geometries.Add(new GeometryDefinition(id, geometryType, points, triangleIndices, name, metadata));
        }

        private static bool ValidateMeshTriangles(
            IReadOnlyList<Vector3> points,
            IReadOnlyList<int> triangleIndices,
            List<TrackMapIssue> issues,
            int? lineNumber)
        {
            if (points == null || triangleIndices == null || triangleIndices.Count < 3)
                return false;

            for (var i = 0; i + 2 < triangleIndices.Count; i += 3)
            {
                var ia = triangleIndices[i];
                var ib = triangleIndices[i + 1];
                var ic = triangleIndices[i + 2];

                if (ia == ib || ib == ic || ia == ic)
                {
                    issues.Add(new TrackMapIssue(
                        TrackMapIssueSeverity.Error,
                        $"Mesh triangle {i / 3} repeats vertex indices ({ia}, {ib}, {ic}).",
                        lineNumber));
                    return false;
                }

                var a = points[ia];
                var b = points[ib];
                var c = points[ic];
                var areaVector = Vector3.Cross(b - a, c - a);
                if (areaVector.LengthSquared() <= 0.00000001f)
                {
                    issues.Add(new TrackMapIssue(
                        TrackMapIssueSeverity.Error,
                        $"Mesh triangle {i / 3} is degenerate.",
                        lineNumber));
                    return false;
                }
            }

            return true;
        }

        private static void ApplyVolume(
            List<TrackVolumeDefinition> volumes,
            SectionBlock block,
            List<TrackMapIssue> issues)
        {
            if (!TryReadId(block, out var id))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Volume requires an id.", block.StartLine));
                return;
            }

            if (volumes.Any(v => string.Equals(v.Id, id, StringComparison.OrdinalIgnoreCase)))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Duplicate volume id '{id}'.", block.StartLine));
                return;
            }

            if (!TryGetValue(block, "type", out var rawType) ||
                string.IsNullOrWhiteSpace(rawType) ||
                !TryVolumeType(rawType, out var volumeType))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Volume requires a valid type.", block.StartLine));
                return;
            }

            var geometryId = TryGetValue(block, "geometry", out var geometryValue) ? geometryValue :
                (TryGetValue(block, "geometry_id", out geometryValue) ? geometryValue : null);
            if (string.IsNullOrWhiteSpace(geometryId) && TryGetValue(block, "mesh", out var meshValue))
                geometryId = meshValue;

            var hasCenter = TryParseVector3Any(block, out var center, "center", "position", "pos", "origin");
            if (!hasCenter)
            {
                var hasX = TryFloat(block, "x", out var xValue);
                var hasZ = TryFloat(block, "z", out var zValue);
                var yValue = TryFloatAny(block, out var yParsed, "y", "elevation") ? yParsed : 0f;
                if (hasX && hasZ)
                {
                    center = new Vector3(xValue, yValue, zValue);
                    hasCenter = true;
                }
            }

            var rotation = Vector3.Zero;
            TryParseRotation(block, out rotation);

            var size = Vector3.Zero;
            var radius = 0f;
            var height = 0f;
            float? minY = null;
            float? maxY = null;

            if (volumeType == TrackVolumeType.Box)
            {
                if (TryParseVector3Any(block, out var sizeValue, "size", "dimensions", "extents"))
                    size = sizeValue;
                else
                {
                    var hasWidth = TryFloat(block, "width", out var widthValue);
                    var hasHeight = TryFloatAny(block, out var heightValue, "height", "volume_height");
                    var hasDepth = TryFloatAny(block, out var depthValue, "depth", "length");
                    if (hasWidth && hasHeight && hasDepth)
                        size = new Vector3(widthValue, heightValue, depthValue);
                }

                var hasMinX = TryFloatAny(block, out var minXValue, "min_x", "minx");
                var hasMaxX = TryFloatAny(block, out var maxXValue, "max_x", "maxx");
                var hasMinY = TryFloatAny(block, out var minYValue, "min_y", "miny");
                var hasMaxY = TryFloatAny(block, out var maxYValue, "max_y", "maxy");
                var hasMinZ = TryFloatAny(block, out var minZValue, "min_z", "minz");
                var hasMaxZ = TryFloatAny(block, out var maxZValue, "max_z", "maxz");

                if (hasMinX || hasMaxX || hasMinY || hasMaxY || hasMinZ || hasMaxZ)
                {
                    if (!hasMinX || !hasMaxX || !hasMinY || !hasMaxY || !hasMinZ || !hasMaxZ)
                    {
                        issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Volume '{id}' box min/max requires all axes.", block.StartLine));
                        return;
                    }

                    size = new Vector3(maxXValue - minXValue, maxYValue - minYValue, maxZValue - minZValue);
                    center = new Vector3((minXValue + maxXValue) * 0.5f, (minYValue + maxYValue) * 0.5f, (minZValue + maxZValue) * 0.5f);
                    hasCenter = true;
                }

                if (size.X <= 0f || size.Y <= 0f || size.Z <= 0f)
                {
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Volume '{id}' box requires positive size.", block.StartLine));
                    return;
                }

                if (!hasCenter)
                {
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Volume '{id}' box requires center (x,y,z) or min/max bounds.", block.StartLine));
                    return;
                }
            }
            else if (volumeType == TrackVolumeType.Sphere)
            {
                if (!TryFloatAny(block, out var radiusValue, "radius", "r") || radiusValue <= 0f)
                {
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Volume '{id}' sphere requires radius.", block.StartLine));
                    return;
                }
                radius = radiusValue;
                if (!hasCenter)
                {
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Volume '{id}' sphere requires center (x,y,z).", block.StartLine));
                    return;
                }
            }
            else if (volumeType == TrackVolumeType.Cylinder || volumeType == TrackVolumeType.Capsule)
            {
                if (!TryFloatAny(block, out var radiusValue, "radius", "r") || radiusValue <= 0f)
                {
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Volume '{id}' requires radius.", block.StartLine));
                    return;
                }
                if (!TryFloatAny(block, out var heightValue, "height", "length", "volume_height", "volume_thickness", "thickness") || heightValue <= 0f)
                {
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Volume '{id}' requires height.", block.StartLine));
                    return;
                }
                radius = radiusValue;
                height = heightValue;
                if (!hasCenter)
                {
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Volume '{id}' requires center (x,y,z).", block.StartLine));
                    return;
                }
            }
            else if (volumeType == TrackVolumeType.Prism)
            {
                if (string.IsNullOrWhiteSpace(geometryId))
                {
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Volume '{id}' prism requires geometry.", block.StartLine));
                    return;
                }

                if (TryFloatAny(block, out var minYValue, "min_y", "miny"))
                    minY = minYValue;
                if (TryFloatAny(block, out var maxYValue, "max_y", "maxy"))
                    maxY = maxYValue;

                if (minY.HasValue && maxY.HasValue && maxY.Value <= minY.Value)
                {
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Volume '{id}' prism max_y must be above min_y.", block.StartLine));
                    return;
                }

                if (!minY.HasValue || !maxY.HasValue)
                {
                    if (!TryFloatAny(block, out var heightValue, "height", "thickness", "volume_height", "volume_thickness") || heightValue <= 0f)
                    {
                        issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Volume '{id}' prism requires height or min/max y.", block.StartLine));
                        return;
                    }

                    var offset = TryFloatAny(block, out var offsetValue, "offset", "volume_offset", "volume_center") ? offsetValue : 0f;
                    var offsetMode = TryParseAreaVolumeOffsetMode(block, out var offsetModeValue)
                        ? offsetModeValue
                        : TrackAreaVolumeOffsetMode.Bottom;

                    var minLocal = offset;
                    switch (offsetMode)
                    {
                        case TrackAreaVolumeOffsetMode.Center:
                            minLocal = offset - (heightValue * 0.5f);
                            break;
                        case TrackAreaVolumeOffsetMode.Top:
                            minLocal = offset - heightValue;
                            break;
                    }
                    minY = minLocal;
                    maxY = minLocal + heightValue;
                }
            }
            else if (volumeType == TrackVolumeType.Mesh)
            {
                if (string.IsNullOrWhiteSpace(geometryId))
                {
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Volume '{id}' mesh requires geometry.", block.StartLine));
                    return;
                }
            }

            var metadata = CollectVolumeMetadata(block);
            volumes.Add(new TrackVolumeDefinition(
                id,
                volumeType,
                center,
                hasCenter,
                size,
                radius,
                height,
                geometryId,
                minY,
                maxY,
                rotation,
                metadata));
        }

        private static void ApplySurface(
            List<TrackSurfaceDefinition> surfaces,
            SectionBlock block,
            List<TrackMapIssue> issues)
        {
            if (!TryReadId(block, out var id))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Surface requires an id.", block.StartLine));
                return;
            }

            if (!TryGetValue(block, "type", out var rawType) ||
                string.IsNullOrWhiteSpace(rawType) ||
                !TrySurfaceType(rawType, out var surfaceType))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Surface requires a valid type.", block.StartLine));
                return;
            }

            if (surfaces.Any(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase)))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Duplicate surface id '{id}'.", block.StartLine));
                return;
            }

            var name = TryGetValue(block, "name", out var nameValue) ? nameValue : null;
            var shapeId = TryGetValue(block, "geometry", out var shapeValue) ? shapeValue : null;
            var profileId = TryGetValue(block, "profile", out var profileValue) ? profileValue : null;
            var bankId = TryGetValue(block, "bank", out var bankValue) ? bankValue : null;
            var materialId = TryMaterialId(block, "material", out var materialValue) ? materialValue : null;
            var layer = TryInt(block, "layer", out var layerValue) ? layerValue : 0;
            var resolution = TryFloat(block, "resolution", out var resolutionValue) ? Math.Max(0.1f, resolutionValue) : (float?)null;
            var metadata = CollectSurfaceMetadata(block);

            surfaces.Add(new TrackSurfaceDefinition(id, surfaceType, shapeId, profileId, bankId, layer, resolution, materialId, name, metadata));
        }

        private static void ApplyProfile(
            List<TrackProfileDefinition> profiles,
            SectionBlock block,
            List<TrackMapIssue> issues)
        {
            if (!TryReadId(block, out var id))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Profile requires an id.", block.StartLine));
                return;
            }

            if (!TryGetValue(block, "type", out var rawType) ||
                string.IsNullOrWhiteSpace(rawType) ||
                !TryProfileType(rawType, out var profileType))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Profile requires a valid type.", block.StartLine));
                return;
            }

            if (profiles.Any(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Duplicate profile id '{id}'.", block.StartLine));
                return;
            }

            var name = TryGetValue(block, "name", out var nameValue) ? nameValue : null;
            var parameters = CollectProfileMetadata(block);
            profiles.Add(new TrackProfileDefinition(id, profileType, name, parameters));
        }

        private static void ApplyBank(
            List<TrackBankDefinition> banks,
            SectionBlock block,
            List<TrackMapIssue> issues)
        {
            if (!TryReadId(block, out var id))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Bank requires an id.", block.StartLine));
                return;
            }

            if (!TryGetValue(block, "type", out var rawType) ||
                string.IsNullOrWhiteSpace(rawType) ||
                !TryBankType(rawType, out var bankType))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Bank requires a valid type.", block.StartLine));
                return;
            }

            if (banks.Any(b => string.Equals(b.Id, id, StringComparison.OrdinalIgnoreCase)))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Duplicate bank id '{id}'.", block.StartLine));
                return;
            }

            var name = TryGetValue(block, "name", out var nameValue) ? nameValue : null;
            var side = TrackBankSide.Right;
            var hasSide = TryGetValue(block, "side", out var sideRaw) || TryGetValue(block, "direction", out sideRaw);
            if (hasSide && !TryBankSide(sideRaw, out side))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Bank side must be left or right.", block.StartLine));
                return;
            }

            if (bankType != TrackBankType.Flat && !hasSide)
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Bank requires side=left or side=right.", block.StartLine));
                return;
            }

            var parameters = CollectBankMetadata(block);
            banks.Add(new TrackBankDefinition(id, bankType, side, name, parameters));
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
            var areaId = TryGetValue(block, "area", out var areaValue) ? areaValue : null;
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

        private static List<Vector2> NormalizePolygonPoints(IReadOnlyList<Vector2> points)
        {
            var result = new List<Vector2>();
            if (points == null || points.Count == 0)
                return result;

            for (var i = 0; i < points.Count; i++)
            {
                var current = points[i];
                if (result.Count == 0 || !NearlyEqual(result[result.Count - 1], current))
                    result.Add(current);
            }

            if (result.Count > 2 && NearlyEqual(result[0], result[result.Count - 1]))
                result.RemoveAt(result.Count - 1);

            return result;
        }

        private static List<Vector3> NormalizePolygonPoints3D(IReadOnlyList<Vector3> points)
        {
            var result = new List<Vector3>();
            if (points == null || points.Count == 0)
                return result;

            for (var i = 0; i < points.Count; i++)
            {
                var current = points[i];
                if (result.Count == 0 || !NearlyEqual(result[result.Count - 1], current))
                    result.Add(current);
            }

            if (result.Count > 2 && NearlyEqual(result[0], result[result.Count - 1]))
                result.RemoveAt(result.Count - 1);

            return result;
        }

        private static bool TryGetPlane(IReadOnlyList<Vector3> points, out Vector3 origin, out Vector3 normal)
        {
            origin = Vector3.Zero;
            normal = Vector3.UnitY;
            if (points == null || points.Count < 3)
                return false;

            for (var i = 0; i < points.Count - 2; i++)
            {
                var a = points[i];
                for (var j = i + 1; j < points.Count - 1; j++)
                {
                    var b = points[j];
                    for (var k = j + 1; k < points.Count; k++)
                    {
                        var c = points[k];
                        var ab = b - a;
                        var ac = c - a;
                        var cross = Vector3.Cross(ab, ac);
                        if (cross.LengthSquared() > 0.000001f)
                        {
                            origin = a;
                            normal = Vector3.Normalize(cross);
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static bool IsPlanar(IReadOnlyList<Vector3> points, Vector3 origin, Vector3 normal)
        {
            if (points == null || points.Count == 0)
                return false;
            for (var i = 0; i < points.Count; i++)
            {
                var distance = Vector3.Dot(normal, points[i] - origin);
                if (Math.Abs(distance) > 0.001f)
                    return false;
            }
            return true;
        }

        private static List<Vector2> ProjectToPlane(IReadOnlyList<Vector3> points, Vector3 origin, Vector3 normal)
        {
            var up = Math.Abs(normal.Y) < 0.99f ? Vector3.UnitY : Vector3.UnitX;
            var axisX = Vector3.Normalize(Vector3.Cross(up, normal));
            var axisY = Vector3.Normalize(Vector3.Cross(normal, axisX));

            var result = new List<Vector2>(points.Count);
            for (var i = 0; i < points.Count; i++)
            {
                var rel = points[i] - origin;
                result.Add(new Vector2(
                    Vector3.Dot(rel, axisX),
                    Vector3.Dot(rel, axisY)));
            }

            return result;
        }

        private static bool ValidatePolygon(IReadOnlyList<Vector2> points, string shapeId, List<TrackMapIssue> issues, int line)
        {
            if (points == null || points.Count < 3)
                return false;

            if (Math.Abs(SignedArea(points)) <= 0.0001f)
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Polygon '{shapeId}' has zero area.", line));
                return false;
            }

            for (var i = 0; i < points.Count; i++)
            {
                for (var j = i + 1; j < points.Count; j++)
                {
                    if (NearlyEqual(points[i], points[j]))
                    {
                        issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Polygon '{shapeId}' has duplicate points.", line));
                        return false;
                    }
                }
            }

            var count = points.Count;
            for (var i = 0; i < count; i++)
            {
                var a1 = points[i];
                var a2 = points[(i + 1) % count];
                for (var j = i + 1; j < count; j++)
                {
                    if (Math.Abs(i - j) <= 1)
                        continue;
                    if (i == 0 && j == count - 1)
                        continue;

                    var b1 = points[j];
                    var b2 = points[(j + 1) % count];
                    if (SegmentsIntersect(a1, a2, b1, b2))
                    {
                        issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Polygon '{shapeId}' self-intersects.", line));
                        return false;
                    }
                }
            }

            return true;
        }

        private static float SignedArea(IReadOnlyList<Vector2> points)
        {
            var sum = 0f;
            for (var i = 0; i < points.Count; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % points.Count];
                sum += (a.X * b.Y) - (b.X * a.Y);
            }
            return sum * 0.5f;
        }

        private static bool SegmentsIntersect(Vector2 a1, Vector2 a2, Vector2 b1, Vector2 b2)
        {
            if (NearlyEqual(a1, b1) || NearlyEqual(a1, b2) || NearlyEqual(a2, b1) || NearlyEqual(a2, b2))
                return true;

            var o1 = Orientation(a1, a2, b1);
            var o2 = Orientation(a1, a2, b2);
            var o3 = Orientation(b1, b2, a1);
            var o4 = Orientation(b1, b2, a2);

            if (o1 * o2 < 0f && o3 * o4 < 0f)
                return true;

            if (Math.Abs(o1) <= 0.0001f && OnSegment(a1, a2, b1))
                return true;
            if (Math.Abs(o2) <= 0.0001f && OnSegment(a1, a2, b2))
                return true;
            if (Math.Abs(o3) <= 0.0001f && OnSegment(b1, b2, a1))
                return true;
            if (Math.Abs(o4) <= 0.0001f && OnSegment(b1, b2, a2))
                return true;

            return false;
        }

        private static float Orientation(Vector2 a, Vector2 b, Vector2 c)
        {
            return (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        }

        private static bool OnSegment(Vector2 a, Vector2 b, Vector2 p)
        {
            return p.X >= Math.Min(a.X, b.X) - 0.0001f &&
                   p.X <= Math.Max(a.X, b.X) + 0.0001f &&
                   p.Y >= Math.Min(a.Y, b.Y) - 0.0001f &&
                   p.Y <= Math.Max(a.Y, b.Y) + 0.0001f;
        }

        private static bool NearlyEqual(Vector2 a, Vector2 b)
        {
            return Vector2.DistanceSquared(a, b) <= 0.0001f * 0.0001f;
        }

        private static bool NearlyEqual(Vector3 a, Vector3 b)
        {
            return Vector3.DistanceSquared(a, b) <= 0.0001f * 0.0001f;
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
            if (block.Values.ContainsKey("acoustic_material") ||
                block.Values.ContainsKey("audio_material") ||
                block.Values.ContainsKey("sound_material") ||
                block.Values.ContainsKey("wall_acoustic_material") ||
                block.Values.ContainsKey("wall_audio_material") ||
                block.Values.ContainsKey("wall_sound_material"))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Area acoustic_material is not supported. Use material instead.", block.StartLine));
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

            var geometryId = TryGetValue(block, "geometry", out var geometryValue) ? geometryValue :
                (TryGetValue(block, "geometry_id", out geometryValue) ? geometryValue : null);
            if (string.IsNullOrWhiteSpace(geometryId))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Area requires a geometry id.", block.StartLine));
                return;
            }
            geometryId = geometryId!.Trim();

            var volumeId = TryGetValue(block, "volume", out var volumeValue) ? volumeValue :
                (TryGetValue(block, "volume_id", out volumeValue) ? volumeValue : null);
            if (string.IsNullOrWhiteSpace(volumeId))
                volumeId = null;

            var surfaceId = TryGetValue(block, "surface", out var surfaceValue) ? surfaceValue :
                (TryGetValue(block, "surface_id", out surfaceValue) ? surfaceValue : null);
            if (string.IsNullOrWhiteSpace(surfaceId))
                surfaceId = null;

            if (TryGetValue(block, "invert", out _) ||
                TryGetValue(block, "outside", out _) ||
                TryGetValue(block, "outside_of", out _))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Area invert/outside is not supported. Use a dedicated polygon volume instead.", block.StartLine));
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

            var volumeThickness = TryFloatAny(block, out var thicknessValue, "thickness", "volume_thickness", "volume_height")
                ? Math.Max(0.01f, thicknessValue)
                : (float?)null;
            var volumeOffset = TryFloatAny(block, out var offsetValue, "offset", "volume_offset", "volume_center")
                ? offsetValue
                : (float?)null;
            var minY = TryFloatAny(block, out var minYValue, "min_y", "miny") ? minYValue : (float?)null;
            var maxY = TryFloatAny(block, out var maxYValue, "max_y", "maxy") ? maxYValue : (float?)null;
            var volumeMode = TryParseAreaVolumeMode(block, out var modeValue) ? modeValue : TrackAreaVolumeMode.LocalBand;
            var volumeOffsetMode = TryParseAreaVolumeOffsetMode(block, out var offsetModeValue)
                ? offsetModeValue
                : TrackAreaVolumeOffsetMode.Bottom;
            var volumeOffsetSpace = TryParseAreaVolumeSpace(block, out var offsetSpaceValue, "volume_offset_space", "offset_space")
                ? offsetSpaceValue
                : TrackAreaVolumeSpace.Inherit;
            var volumeMinMaxSpace = TryParseAreaVolumeSpace(block, out var minMaxSpaceValue, "volume_minmax_space", "minmax_space", "bounds_space", "volume_bounds_space")
                ? minMaxSpaceValue
                : TrackAreaVolumeSpace.Inherit;

            if (minY.HasValue && maxY.HasValue && maxY.Value <= minY.Value)
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Area '{id}' max_y must be above min_y.", block.StartLine));
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

            var soundSourceIds = ParseStringListFromBlock(block, "sources", "source", "sound_sources", "sound_source");

            areas.Add(new TrackAreaDefinition(
                id,
                areaType,
                geometryId,
                elevationMeters,
                heightMeters,
                ceilingMeters,
                roomId,
                roomOverrides,
                name,
                materialId,
                noise,
                width,
                flags,
                areaMetadata,
                soundSourceIds,
                volumeId,
                surfaceId,
                volumeThickness,
                volumeOffset,
                minY,
                maxY,
                volumeMode,
                volumeOffsetMode,
                volumeOffsetSpace,
                volumeMinMaxSpace));
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
                case "beacon_geometry":
                case "turn_range":
                case "turn_geometry":
                case "centerline_geometry":
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
            List<GeometryDefinition> geometries,
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

            var areasById = new Dictionary<string, TrackAreaDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (var area in areas)
            {
                if (area == null || string.IsNullOrWhiteSpace(area.Id))
                    continue;
                areasById[area.Id] = area;
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
                if (sector == null)
                    continue;
                var sectorAreaId = sector.AreaId;
                if (string.IsNullOrWhiteSpace(sectorAreaId))
                    continue;
                sectorAreaId = sectorAreaId!.Trim();
                if (!sectorsByArea.TryGetValue(sectorAreaId, out var list))
                {
                    list = new List<TrackSectorDefinition>();
                    sectorsByArea[sectorAreaId] = list;
                }
                list.Add(sector);
            }

            var geometryIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var geometry in geometries)
            {
                if (geometry == null || string.IsNullOrWhiteSpace(geometry.Id))
                    continue;
                geometryIds.Add(geometry.Id);
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
                var volumeId = TryGetValue(block, "volume", out var volumeValue) ? volumeValue :
                    (TryGetValue(block, "volume_id", out volumeValue) ? volumeValue : null);

                var volumeThickness = TryFloatAny(block, out var thicknessValue, "height", "thickness", "volume_thickness", "volume_height")
                    ? Math.Max(0.01f, thicknessValue)
                    : (float?)null;
                var volumeOffset = TryFloatAny(block, out var offsetValue, "offset", "volume_offset", "volume_center")
                    ? offsetValue
                    : (float?)null;
                var minY = TryFloatAny(block, out var minYValue, "min_y", "miny") ? minYValue : (float?)null;
                var maxY = TryFloatAny(block, out var maxYValue, "max_y", "maxy") ? maxYValue : (float?)null;
                var volumeMode = TryParseAreaVolumeMode(block, out var modeValue) ? modeValue : TrackAreaVolumeMode.LocalBand;
                var volumeOffsetMode = TryParseAreaVolumeOffsetMode(block, out var offsetModeValue)
                    ? offsetModeValue
                    : TrackAreaVolumeOffsetMode.Bottom;
                var volumeOffsetSpace = TryParseAreaVolumeSpace(block, out var offsetSpaceValue, "volume_offset_space", "offset_space")
                    ? offsetSpaceValue
                    : TrackAreaVolumeSpace.Inherit;
                var volumeMinMaxSpace = TryParseAreaVolumeSpace(block, out var minMaxSpaceValue, "volume_minmax_space", "minmax_space", "bounds_space", "volume_bounds_space")
                    ? minMaxSpaceValue
                    : TrackAreaVolumeSpace.Inherit;

                if (minY.HasValue && maxY.HasValue && maxY.Value <= minY.Value)
                {
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Guide '{id}' max_y must be above min_y.", block.StartLine));
                    continue;
                }

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
                if (TryGetValue(block, "beacon_geometry", out var beaconGeometry))
                    metadata["beacon_geometry"] = beaconGeometry.Trim();
                if (TryGetValue(block, "direction", out var direction))
                    metadata["direction"] = direction.Trim();
                if (TryGetValue(block, "turn_direction", out var turnDirection))
                    metadata["turn_direction"] = turnDirection.Trim();
                if (TryGetValue(block, "turn_heading", out var turnHeading))
                    metadata["turn_heading"] = turnHeading.Trim();
                if (TryGetValue(block, "announcement_heading", out var announcementHeading))
                    metadata["announcement_heading"] = announcementHeading.Trim();
                if (TryGetValue(block, "turn_range", out var turnRange))
                    metadata["turn_range"] = turnRange.Trim();
                if (TryGetValue(block, "turn_geometry", out var turnGeometry))
                    metadata["turn_geometry"] = turnGeometry.Trim();
                if (TryGetValue(block, "centerline_geometry", out var centerlineGeometry))
                    metadata["centerline_geometry"] = centerlineGeometry.Trim();
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

                if (!metadata.ContainsKey("beacon_geometry"))
                {
                    var areaGeometryId = ResolveGuideAreaGeometry(areaId, targets, areasById);
                    if (!string.IsNullOrWhiteSpace(areaGeometryId))
                        metadata["beacon_geometry"] = areaGeometryId!;
                }

                ValidateGuideGeometry(metadata, geometryIds, "beacon_geometry", id, issues, block.StartLine);
                ValidateGuideGeometry(metadata, geometryIds, "turn_geometry", id, issues, block.StartLine);
                ValidateGuideGeometry(metadata, geometryIds, "centerline_geometry", id, issues, block.StartLine);

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
                        volumeId,
                        volumeThickness,
                        volumeOffset,
                        minY,
                        maxY,
                        volumeMode,
                        volumeOffsetMode,
                        volumeOffsetSpace,
                        volumeMinMaxSpace,
                        metadata,
                        id,
                        issues,
                        block.StartLine);
                }
            }
        }

        private static void ValidateGuideGeometry(
            IReadOnlyDictionary<string, string> metadata,
            HashSet<string> geometryIds,
            string key,
            string guideId,
            List<TrackMapIssue> issues,
            int? line)
        {
            if (!metadata.TryGetValue(key, out var value))
                return;
            if (string.IsNullOrWhiteSpace(value))
                return;
            if (!geometryIds.Contains(value.Trim()))
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Guide '{guideId}' references missing geometry '{value}' for {key}.", line));
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
                if (TryParseCompassHeading(headingRaw, out var headingValue))
                    heading = headingValue;
                else if (TryFloat(headingRaw, out var headingNumeric))
                    heading = headingNumeric;
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
                if (TryParseCompassHeading(token, out var headingValue))
                    headings.Add(headingValue);
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
            string? volumeId,
            float? volumeThickness,
            float? volumeOffset,
            float? minY,
            float? maxY,
            TrackAreaVolumeMode volumeMode,
            TrackAreaVolumeOffsetMode volumeOffsetMode,
            TrackAreaVolumeSpace volumeOffsetSpace,
            TrackAreaVolumeSpace volumeMinMaxSpace,
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
                        AddGuideApproach(approaches, sectorId, name, entries[i], exits[i], width, length, tolerance,
                            volumeId,
                            volumeThickness, volumeOffset, minY, maxY, volumeMode, volumeOffsetMode, volumeOffsetSpace, volumeMinMaxSpace,
                            metadata);
                    }
                    return;
                }

                foreach (var entry in entries)
                {
                    foreach (var exit in exits)
                        AddGuideApproach(approaches, sectorId, name, entry, exit, width, length, tolerance,
                            volumeId,
                            volumeThickness, volumeOffset, minY, maxY, volumeMode, volumeOffsetMode, volumeOffsetSpace, volumeMinMaxSpace,
                            metadata);
                }

                return;
            }

            if (entries.Count > 0)
            {
                foreach (var entry in entries)
                    AddGuideApproach(approaches, sectorId, name, entry, null, width, length, tolerance,
                        volumeId,
                        volumeThickness, volumeOffset, minY, maxY, volumeMode, volumeOffsetMode, volumeOffsetSpace, volumeMinMaxSpace,
                        metadata);
                return;
            }

            foreach (var exit in exits)
                AddGuideApproach(approaches, sectorId, name, null, exit, width, length, tolerance,
                    volumeId,
                    volumeThickness, volumeOffset, minY, maxY, volumeMode, volumeOffsetMode, volumeOffsetSpace, volumeMinMaxSpace,
                    metadata);
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
            string? volumeId,
            float? volumeThickness,
            float? volumeOffset,
            float? minY,
            float? maxY,
            TrackAreaVolumeMode volumeMode,
            TrackAreaVolumeOffsetMode volumeOffsetMode,
            TrackAreaVolumeSpace volumeOffsetSpace,
            TrackAreaVolumeSpace volumeMinMaxSpace,
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
                metadata,
                volumeThickness,
                volumeOffset,
                minY,
                maxY,
                volumeMode,
                volumeOffsetMode,
                volumeOffsetSpace,
                volumeMinMaxSpace,
                volumeId));
        }

        private static string? ResolveGuideAreaGeometry(
            string? areaId,
            List<TrackSectorDefinition> targets,
            Dictionary<string, TrackAreaDefinition> areasById)
        {
            if (!string.IsNullOrWhiteSpace(areaId) && areasById.TryGetValue(areaId!.Trim(), out var area))
                return area.GeometryId;

            if (targets != null && targets.Count == 1)
            {
                var sectorAreaId = targets[0].AreaId;
                if (!string.IsNullOrWhiteSpace(sectorAreaId) &&
                    areasById.TryGetValue(sectorAreaId!.Trim(), out var sectorArea))
                {
                    return sectorArea.GeometryId;
                }
            }

            return null;
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
            List<TrackMapIssue> issues,
            float? baseHeightMeters)
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

            var hasY = TryFloatAny(block, out var yValue, "y", "elevation");
            var y = hasY ? yValue : baseHeightMeters.GetValueOrDefault(0f);

            if (portals.Any(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Duplicate portal id '{id}'.", block.StartLine));
                return;
            }

            var width = TryFloat(block, "width", out var widthMeters) ? Math.Max(0.1f, widthMeters) : 0f;
            var entryHeading = TryReadHeading(block, "entry", out var entryHeadingValue) ? entryHeadingValue : (float?)null;
            var exitHeading = TryReadHeading(block, "exit", out var exitHeadingValue) ? exitHeadingValue : (float?)null;
            var volumeId = TryGetValue(block, "volume", out var volumeValue) ? volumeValue :
                (TryGetValue(block, "volume_id", out volumeValue) ? volumeValue : null);

            var volumeThickness = TryFloatAny(block, out var thicknessValue, "height", "thickness", "volume_thickness", "volume_height")
                ? Math.Max(0.01f, thicknessValue)
                : (float?)null;
            var volumeOffset = TryFloatAny(block, out var offsetValue, "offset", "volume_offset", "volume_center")
                ? offsetValue
                : (float?)null;
            var minY = TryFloatAny(block, out var minYValue, "min_y", "miny") ? minYValue : (float?)null;
            var maxY = TryFloatAny(block, out var maxYValue, "max_y", "maxy") ? maxYValue : (float?)null;
            var volumeMode = TryParseAreaVolumeMode(block, out var modeValue) ? modeValue : TrackAreaVolumeMode.LocalBand;
            var volumeOffsetMode = TryParseAreaVolumeOffsetMode(block, out var offsetModeValue)
                ? offsetModeValue
                : TrackAreaVolumeOffsetMode.Bottom;
            var volumeOffsetSpace = TryParseAreaVolumeSpace(block, out var offsetSpaceValue, "volume_offset_space", "offset_space")
                ? offsetSpaceValue
                : TrackAreaVolumeSpace.Inherit;
            var volumeMinMaxSpace = TryParseAreaVolumeSpace(block, out var minMaxSpaceValue, "volume_minmax_space", "minmax_space", "bounds_space", "volume_bounds_space")
                ? minMaxSpaceValue
                : TrackAreaVolumeSpace.Inherit;

            if (minY.HasValue && maxY.HasValue && maxY.Value <= minY.Value)
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Portal '{id}' max_y must be above min_y.", block.StartLine));
                return;
            }

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

            portals.Add(new PortalDefinition(
                id,
                sectorId.Trim(),
                x,
                y,
                z,
                width,
                entryHeading,
                exitHeading,
                role,
                volumeThickness,
                volumeOffset,
                minY,
                maxY,
                volumeMode,
                volumeOffsetMode,
                volumeOffsetSpace,
                volumeMinMaxSpace,
                volumeId));
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
            List<TrackMapIssue> issues,
            float? baseHeightMeters)
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

            var hasY = TryFloatAny(block, out var yValue, "y", "elevation");
            var y = hasY ? yValue : baseHeightMeters.GetValueOrDefault(0f);

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
            var shapeId = TryGetValue(block, "geometry", out var shapeValue) ? shapeValue : null;
            var heading = TryReadHeadingValue(block, out var headingValue) ? headingValue : (float?)null;
            var volumeId = TryGetValue(block, "volume", out var volumeValue) ? volumeValue :
                (TryGetValue(block, "volume_id", out volumeValue) ? volumeValue : null);
            float? radius = null;
            if (TryFloat(block, "radius", out var radiusValue) && radiusValue > 0f)
                radius = radiusValue;
            else if (TryFloat(block, "activation_radius", out radiusValue) && radiusValue > 0f)
                radius = radiusValue;
            var metadata = CollectBeaconMetadata(block);

            var volumeThickness = TryFloatAny(block, out var thicknessValue, "height", "thickness", "volume_thickness", "volume_height")
                ? Math.Max(0.01f, thicknessValue)
                : (float?)null;
            var volumeOffset = TryFloatAny(block, out var offsetValue, "offset", "volume_offset", "volume_center")
                ? offsetValue
                : (float?)null;
            var minY = TryFloatAny(block, out var minYValue, "min_y", "miny") ? minYValue : (float?)null;
            var maxY = TryFloatAny(block, out var maxYValue, "max_y", "maxy") ? maxYValue : (float?)null;
            var volumeMode = TryParseAreaVolumeMode(block, out var modeValue) ? modeValue : TrackAreaVolumeMode.LocalBand;
            var volumeOffsetMode = TryParseAreaVolumeOffsetMode(block, out var offsetModeValue)
                ? offsetModeValue
                : TrackAreaVolumeOffsetMode.Bottom;
            var volumeOffsetSpace = TryParseAreaVolumeSpace(block, out var offsetSpaceValue, "volume_offset_space", "offset_space")
                ? offsetSpaceValue
                : TrackAreaVolumeSpace.Inherit;
            var volumeMinMaxSpace = TryParseAreaVolumeSpace(block, out var minMaxSpaceValue, "volume_minmax_space", "minmax_space", "bounds_space", "volume_bounds_space")
                ? minMaxSpaceValue
                : TrackAreaVolumeSpace.Inherit;

            if (minY.HasValue && maxY.HasValue && maxY.Value <= minY.Value)
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Beacon '{id}' max_y must be above min_y.", block.StartLine));
                return;
            }

            beacons.Add(new TrackBeaconDefinition(
                id,
                type,
                x,
                y,
                z,
                name,
                name2,
                sectorId,
                shapeId,
                heading,
                radius,
                role,
                metadata,
                volumeThickness,
                volumeOffset,
                minY,
                maxY,
                volumeMode,
                volumeOffsetMode,
                volumeOffsetSpace,
                volumeMinMaxSpace,
                volumeId));
        }

        private static void ApplyMarker(
            List<TrackMarkerDefinition> markers,
            SectionBlock block,
            List<TrackMapIssue> issues,
            float? baseHeightMeters)
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

            var hasY = TryFloatAny(block, out var yValue, "y", "elevation");
            var y = hasY ? yValue : baseHeightMeters.GetValueOrDefault(0f);

            if (markers.Any(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase)))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Duplicate marker id '{id}'.", block.StartLine));
                return;
            }

            var type = TryMarkerType(block, out var parsedType) ? parsedType : TrackMarkerType.Undefined;
            var name = TryGetValue(block, "name", out var nameValue) ? nameValue : null;
            var shapeId = TryGetValue(block, "geometry", out var shapeValue) ? shapeValue : null;
            var heading = TryReadHeadingValue(block, out var headingValue) ? headingValue : (float?)null;
            var volumeId = TryGetValue(block, "volume", out var volumeValue) ? volumeValue :
                (TryGetValue(block, "volume_id", out volumeValue) ? volumeValue : null);
            var metadata = CollectMarkerMetadata(block);

            var volumeThickness = TryFloatAny(block, out var thicknessValue, "height", "thickness", "volume_thickness", "volume_height")
                ? Math.Max(0.01f, thicknessValue)
                : (float?)null;
            var volumeOffset = TryFloatAny(block, out var offsetValue, "offset", "volume_offset", "volume_center")
                ? offsetValue
                : (float?)null;
            var minY = TryFloatAny(block, out var minYValue, "min_y", "miny") ? minYValue : (float?)null;
            var maxY = TryFloatAny(block, out var maxYValue, "max_y", "maxy") ? maxYValue : (float?)null;
            var volumeMode = TryParseAreaVolumeMode(block, out var modeValue) ? modeValue : TrackAreaVolumeMode.LocalBand;
            var volumeOffsetMode = TryParseAreaVolumeOffsetMode(block, out var offsetModeValue)
                ? offsetModeValue
                : TrackAreaVolumeOffsetMode.Bottom;
            var volumeOffsetSpace = TryParseAreaVolumeSpace(block, out var offsetSpaceValue, "volume_offset_space", "offset_space")
                ? offsetSpaceValue
                : TrackAreaVolumeSpace.Inherit;
            var volumeMinMaxSpace = TryParseAreaVolumeSpace(block, out var minMaxSpaceValue, "volume_minmax_space", "minmax_space", "bounds_space", "volume_bounds_space")
                ? minMaxSpaceValue
                : TrackAreaVolumeSpace.Inherit;

            if (minY.HasValue && maxY.HasValue && maxY.Value <= minY.Value)
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Marker '{id}' max_y must be above min_y.", block.StartLine));
                return;
            }

            markers.Add(new TrackMarkerDefinition(
                id,
                type,
                x,
                y,
                z,
                name,
                shapeId,
                heading,
                metadata,
                volumeThickness,
                volumeOffset,
                minY,
                maxY,
                volumeMode,
                volumeOffsetMode,
                volumeOffsetSpace,
                volumeMinMaxSpace,
                volumeId));
        }

        private static void ValidateVolumeReferences(
            List<TrackVolumeDefinition> volumes,
            List<TrackAreaDefinition> areas,
            List<PortalDefinition> portals,
            List<TrackBeaconDefinition> beacons,
            List<TrackMarkerDefinition> markers,
            List<TrackApproachDefinition> approaches,
            List<GeometryDefinition> geometries,
            List<TrackMapIssue> issues)
        {
            if (issues == null)
                return;

            var volumeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (volumes != null)
            {
                foreach (var volume in volumes)
                {
                    if (volume == null || string.IsNullOrWhiteSpace(volume.Id))
                        continue;
                    volumeIds.Add(volume.Id);
                }
            }

            var geometryLookup = new Dictionary<string, GeometryDefinition>(StringComparer.OrdinalIgnoreCase);
            if (geometries != null)
            {
                foreach (var geometry in geometries)
                {
                    if (geometry == null || string.IsNullOrWhiteSpace(geometry.Id))
                        continue;
                    geometryLookup[geometry.Id] = geometry;
                }
            }

            if (volumes != null)
            {
                foreach (var volume in volumes)
                {
                    if (volume == null)
                        continue;
                    if ((volume.Type == TrackVolumeType.Prism || volume.Type == TrackVolumeType.Mesh) &&
                        !string.IsNullOrWhiteSpace(volume.GeometryId))
                    {
                        if (!geometryLookup.TryGetValue(volume.GeometryId!, out var geometry))
                        {
                            issues.Add(new TrackMapIssue(
                                TrackMapIssueSeverity.Error,
                                $"Volume '{volume.Id}' references unknown geometry '{volume.GeometryId}'."));
                            continue;
                        }

                        if (volume.Type == TrackVolumeType.Prism && geometry.Type != GeometryType.Polygon)
                        {
                            issues.Add(new TrackMapIssue(
                                TrackMapIssueSeverity.Error,
                                $"Volume '{volume.Id}' prism requires polygon geometry '{volume.GeometryId}'."));
                        }
                        else if (volume.Type == TrackVolumeType.Mesh && geometry.Type != GeometryType.Mesh)
                        {
                            issues.Add(new TrackMapIssue(
                                TrackMapIssueSeverity.Error,
                                $"Volume '{volume.Id}' mesh requires mesh geometry '{volume.GeometryId}'."));
                        }
                    }
                }
            }

            if (areas != null)
            {
                foreach (var area in areas)
                {
                    if (area == null || string.IsNullOrWhiteSpace(area.VolumeId))
                        continue;
                    if (!volumeIds.Contains(area.VolumeId!))
                    {
                        issues.Add(new TrackMapIssue(
                            TrackMapIssueSeverity.Error,
                            $"Area '{area.Id}' references unknown volume '{area.VolumeId}'."));
                    }
                }
            }

            if (portals != null)
            {
                foreach (var portal in portals)
                {
                    if (portal == null || string.IsNullOrWhiteSpace(portal.VolumeId))
                        continue;
                    if (!volumeIds.Contains(portal.VolumeId!))
                    {
                        issues.Add(new TrackMapIssue(
                            TrackMapIssueSeverity.Error,
                            $"Portal '{portal.Id}' references unknown volume '{portal.VolumeId}'."));
                    }
                }
            }

            if (beacons != null)
            {
                foreach (var beacon in beacons)
                {
                    if (beacon == null || string.IsNullOrWhiteSpace(beacon.VolumeId))
                        continue;
                    if (!volumeIds.Contains(beacon.VolumeId!))
                    {
                        issues.Add(new TrackMapIssue(
                            TrackMapIssueSeverity.Error,
                            $"Beacon '{beacon.Id}' references unknown volume '{beacon.VolumeId}'."));
                    }
                }
            }

            if (markers != null)
            {
                foreach (var marker in markers)
                {
                    if (marker == null || string.IsNullOrWhiteSpace(marker.VolumeId))
                        continue;
                    if (!volumeIds.Contains(marker.VolumeId!))
                    {
                        issues.Add(new TrackMapIssue(
                            TrackMapIssueSeverity.Error,
                            $"Marker '{marker.Id}' references unknown volume '{marker.VolumeId}'."));
                    }
                }
            }

            if (approaches != null)
            {
                foreach (var approach in approaches)
                {
                    if (approach == null || string.IsNullOrWhiteSpace(approach.VolumeId))
                        continue;
                    if (!volumeIds.Contains(approach.VolumeId!))
                    {
                        issues.Add(new TrackMapIssue(
                            TrackMapIssueSeverity.Error,
                            $"Approach '{approach.SectorId}' references unknown volume '{approach.VolumeId}'."));
                    }
                }
            }
        }

        private static void ValidateSoundSourceReferences(
            List<TrackSoundSourceDefinition> soundSources,
            List<TrackAreaDefinition> areas,
            List<GeometryDefinition> geometries,
            List<TrackMapIssue> issues)
        {
            if (issues == null)
                return;

            var sourceLookup = new Dictionary<string, TrackSoundSourceDefinition>(StringComparer.OrdinalIgnoreCase);
            if (soundSources != null)
            {
                foreach (var source in soundSources)
                {
                    if (source == null || string.IsNullOrWhiteSpace(source.Id))
                        continue;
                    sourceLookup[source.Id] = source;
                }
            }

            var geometryLookup = new Dictionary<string, GeometryDefinition>(StringComparer.OrdinalIgnoreCase);
            if (geometries != null)
            {
                foreach (var geometry in geometries)
                {
                    if (geometry == null || string.IsNullOrWhiteSpace(geometry.Id))
                        continue;
                    geometryLookup[geometry.Id] = geometry;
                }
            }

            var areaLookup = new Dictionary<string, TrackAreaDefinition>(StringComparer.OrdinalIgnoreCase);
            if (areas != null)
            {
                foreach (var area in areas)
                {
                    if (area == null || string.IsNullOrWhiteSpace(area.Id))
                        continue;
                    areaLookup[area.Id] = area;
                }
            }

            if (areas != null)
            {
                foreach (var area in areas)
                {
                    if (area == null || area.SoundSourceIds == null)
                        continue;
                    foreach (var id in area.SoundSourceIds)
                    {
                        if (string.IsNullOrWhiteSpace(id))
                            continue;
                        if (!sourceLookup.ContainsKey(id))
                        {
                            issues.Add(new TrackMapIssue(
                                TrackMapIssueSeverity.Error,
                                $"Area '{area.Id}' references unknown sound source '{id}'."));
                        }
                    }
                }
            }

            var referencedByRandom = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (soundSources != null)
            {
                foreach (var source in soundSources)
                {
                    if (source == null)
                        continue;
                    foreach (var candidate in source.VariantSourceIds)
                    {
                        if (!string.IsNullOrWhiteSpace(candidate))
                            referencedByRandom.Add(candidate);
                    }
                }
            }

            if (soundSources == null)
                return;

            foreach (var source in soundSources)
            {
                if (source == null)
                    continue;

                if (source.Type == TrackSoundSourceType.Random)
                {
                    if ((source.VariantPaths == null || source.VariantPaths.Count == 0) &&
                        (source.VariantSourceIds == null || source.VariantSourceIds.Count == 0) &&
                        string.IsNullOrWhiteSpace(source.Path))
                    {
                        issues.Add(new TrackMapIssue(
                            TrackMapIssueSeverity.Error,
                            $"Random sound source '{source.Id}' requires variants or a path."));
                    }
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(source.Path))
                    {
                        issues.Add(new TrackMapIssue(
                            TrackMapIssueSeverity.Error,
                            $"Sound source '{source.Id}' requires a path."));
                    }
                }

                if (!string.IsNullOrWhiteSpace(source.GeometryId) &&
                    !geometryLookup.ContainsKey(source.GeometryId!))
                {
                    issues.Add(new TrackMapIssue(
                        TrackMapIssueSeverity.Error,
                        $"Sound source '{source.Id}' references unknown geometry '{source.GeometryId}'."));
                }

                if (!string.IsNullOrWhiteSpace(source.PathGeometryId))
                {
                    if (!geometryLookup.TryGetValue(source.PathGeometryId!, out var geometry))
                    {
                        issues.Add(new TrackMapIssue(
                            TrackMapIssueSeverity.Error,
                            $"Sound source '{source.Id}' references unknown path geometry '{source.PathGeometryId}'."));
                    }
                    else if (source.Type == TrackSoundSourceType.Moving &&
                             geometry.Type != GeometryType.Polyline &&
                             geometry.Type != GeometryType.Spline &&
                             geometry.Type != GeometryType.Polygon)
                    {
                        issues.Add(new TrackMapIssue(
                            TrackMapIssueSeverity.Error,
                            $"Moving sound source '{source.Id}' requires polyline/spline/polygon geometry '{source.PathGeometryId}'."));
                    }
                }
                else if (source.Type == TrackSoundSourceType.Moving)
                {
                    issues.Add(new TrackMapIssue(
                        TrackMapIssueSeverity.Error,
                        $"Moving sound source '{source.Id}' requires path_geometry."));
                }

                if (!string.IsNullOrWhiteSpace(source.StartAreaId) &&
                    !areaLookup.ContainsKey(source.StartAreaId!))
                {
                    issues.Add(new TrackMapIssue(
                        TrackMapIssueSeverity.Error,
                        $"Sound source '{source.Id}' references unknown start area '{source.StartAreaId}'."));
                }

                if (!string.IsNullOrWhiteSpace(source.EndAreaId) &&
                    !areaLookup.ContainsKey(source.EndAreaId!))
                {
                    issues.Add(new TrackMapIssue(
                        TrackMapIssueSeverity.Error,
                        $"Sound source '{source.Id}' references unknown end area '{source.EndAreaId}'."));
                }

                if (source.VariantSourceIds != null && source.VariantSourceIds.Count > 0)
                {
                    foreach (var variantId in source.VariantSourceIds)
                    {
                        if (string.IsNullOrWhiteSpace(variantId))
                            continue;
                        if (!sourceLookup.ContainsKey(variantId))
                        {
                            issues.Add(new TrackMapIssue(
                                TrackMapIssueSeverity.Error,
                                $"Sound source '{source.Id}' references unknown variant source '{variantId}'."));
                        }
                        else if (string.Equals(source.Id, variantId, StringComparison.OrdinalIgnoreCase))
                        {
                            issues.Add(new TrackMapIssue(
                                TrackMapIssueSeverity.Error,
                                $"Sound source '{source.Id}' cannot reference itself as a variant."));
                        }
                    }
                }

                var hasStartEnd = !string.IsNullOrWhiteSpace(source.StartAreaId) ||
                                  !string.IsNullOrWhiteSpace(source.EndAreaId) ||
                                  source.StartPosition.HasValue ||
                                  source.EndPosition.HasValue;
                var isReferencedByArea = false;
                if (areas != null)
                {
                    foreach (var area in areas)
                    {
                        if (area == null || area.SoundSourceIds == null)
                            continue;
                        if (area.SoundSourceIds.Any(id => string.Equals(id, source.Id, StringComparison.OrdinalIgnoreCase)))
                        {
                            isReferencedByArea = true;
                            break;
                        }
                    }
                }

                if (!source.Global && !hasStartEnd && !isReferencedByArea && !referencedByRandom.Contains(source.Id))
                {
                    issues.Add(new TrackMapIssue(
                        TrackMapIssueSeverity.Warning,
                        $"Sound source '{source.Id}' is not referenced by any area and has no start/end; it will not play."));
                }
            }
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

            if (!TryGetValue(block, "geometry", out var shapeId) || string.IsNullOrWhiteSpace(shapeId))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Wall requires a geometry id.", block.StartLine));
                return;
            }

            var name = TryGetValue(block, "name", out var nameValue) ? nameValue : null;
            var width = TryFloatAny(block, out var widthValue, "width", "wall_width") ? Math.Max(0f, widthValue) : 0f;
            var height = TryFloatAny(block, out var heightValue, "height", "wall_height") ? Math.Max(0f, heightValue) : 2f;
            var elevation = TryFloatAny(block, out var elevationValue, "elevation", "wall_elevation", "base_height")
                ? elevationValue
                : metadata.BaseHeightMeters.GetValueOrDefault(0f);
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
            walls.Add(new TrackWallDefinition(id, shapeId, width, elevation, material, collisionMode, name, wallMetadata, height, materialId));
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
            var hasReflectionWet = TryFloat(block, "reflection_wet", out var reflectionWet);
            var hasHfDecay = TryFloat(block, "hf_decay_ratio", out var hfDecayRatio);
            var hasEarlyGain = TryFloat(block, "early_reflections_gain", out var earlyGain);
            var hasLateGain = TryFloat(block, "late_reverb_gain", out var lateGain);
            var hasDiffusion = TryFloat(block, "diffusion", out var diffusion);
            var hasAirAbsorption = TryFloat(block, "air_absorption", out var airAbsorption);
            var hasOcclusion = TryFloat(block, "occlusion_scale", out var occlusionScale);
            var hasTransmission = TryFloat(block, "transmission_scale", out var transmissionScale);
            var hasOcclusionOverride = TryFloat(block, "occlusion_override", out var occlusionOverride);
            var hasTransmissionOverride = ResolveOverrideTriple(
                block,
                "transmission_override",
                "transmission_override_low",
                "transmission_override_mid",
                "transmission_override_high",
                out float? transOverrideLow,
                out float? transOverrideMid,
                out float? transOverrideHigh);
            var hasAirAbsorptionOverride = ResolveOverrideTriple(
                block,
                "air_absorption_override",
                "air_absorption_override_low",
                "air_absorption_override_mid",
                "air_absorption_override_high",
                out float? airOverrideLow,
                out float? airOverrideMid,
                out float? airOverrideHigh);

            var hasAny = preset != null || hasReverbTime || hasReverbGain || hasHfDecay ||
                         hasEarlyGain || hasLateGain || hasDiffusion || hasAirAbsorption ||
                         hasOcclusion || hasTransmission || hasOcclusionOverride ||
                         hasTransmissionOverride || hasAirAbsorptionOverride;

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
            var resolvedReflectionWet = hasReflectionWet
                ? Clamp01(reflectionWet)
                : (hasReverbGain ? Clamp01(reverbGain) : (preset?.ReflectionWet ?? TrackRoomLibrary.DefaultReflectionWet));
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
                resolvedReflectionWet,
                resolvedHfDecay,
                resolvedEarlyGain,
                resolvedLateGain,
                resolvedDiffusion,
                resolvedAirAbsorption,
                resolvedOcclusion,
                resolvedTransmission,
                hasOcclusionOverride ? Clamp01(occlusionOverride) : preset?.OcclusionOverride,
                hasTransmissionOverride ? transOverrideLow : preset?.TransmissionOverrideLow,
                hasTransmissionOverride ? transOverrideMid : preset?.TransmissionOverrideMid,
                hasTransmissionOverride ? transOverrideHigh : preset?.TransmissionOverrideHigh,
                hasAirAbsorptionOverride ? airOverrideLow : preset?.AirAbsorptionOverrideLow,
                hasAirAbsorptionOverride ? airOverrideMid : preset?.AirAbsorptionOverrideMid,
                hasAirAbsorptionOverride ? airOverrideHigh : preset?.AirAbsorptionOverrideHigh));
        }

        private static void ApplySoundSource(
            List<TrackSoundSourceDefinition> soundSources,
            SectionBlock block,
            TrackSoundSourceType type,
            string mapRoot,
            List<TrackMapIssue> issues)
        {
            if (!TryReadId(block, out var id))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, "Sound source requires an id.", block.StartLine));
                return;
            }

            if (soundSources.Any(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase)))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Duplicate sound source id '{id}'.", block.StartLine));
                return;
            }

            string? path = null;
            if (TryGetValueAny(block, out var pathValue, "path", "file", "sound", "audio", "sound_path", "audio_path"))
                path = pathValue;
            if (string.IsNullOrWhiteSpace(path))
                path = null;

            var variantPaths = ParseStringListFromBlock(block, "variants", "variant", "variant_paths", "variant_path");
            var variantSourceIds = ParseStringListFromBlock(block, "variant_sources", "variant_source", "variant_source_ids", "variant_sources_ids");

            var randomMode = TrackSoundRandomMode.OnStart;
            if (TryGetValueAny(block, out var randomModeRaw, "random_mode", "random"))
            {
                if (!TryParseSoundRandomMode(randomModeRaw, out randomMode))
                {
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Sound source '{id}' has invalid random_mode '{randomModeRaw}'.", block.StartLine));
                    return;
                }
            }

            var loop = type == TrackSoundSourceType.Ambient ||
                       type == TrackSoundSourceType.Static ||
                       type == TrackSoundSourceType.Moving;
            if (TryGetValueAny(block, out var loopRaw, "loop", "looping", "repeat"))
            {
                if (!TryBool(loopRaw, out loop))
                {
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Sound source '{id}' has invalid loop value '{loopRaw}'.", block.StartLine));
                    return;
                }
            }

            var volume = 1f;
            if (TryFloatAny(block, out var volumeValue, "volume", "gain", "level"))
                volume = Clamp01(volumeValue);

            var spatial = type != TrackSoundSourceType.Ambient;
            if (TryGetValueAny(block, out var spatialRaw, "spatial", "positional", "spatialize"))
            {
                if (!TryBool(spatialRaw, out spatial))
                {
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Sound source '{id}' has invalid spatial value '{spatialRaw}'.", block.StartLine));
                    return;
                }
            }

            var allowHrtf = spatial;
            if (TryGetValueAny(block, out var hrtfRaw, "hrtf", "allow_hrtf", "use_hrtf"))
            {
                if (!TryBool(hrtfRaw, out allowHrtf))
                {
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Sound source '{id}' has invalid hrtf value '{hrtfRaw}'.", block.StartLine));
                    return;
                }
            }

            var useReflections = false;
            if (TryGetValueAny(block, out var reflectionsRaw, "reflections", "use_reflections"))
            {
                if (!TryBool(reflectionsRaw, out useReflections))
                {
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Sound source '{id}' has invalid reflections value '{reflectionsRaw}'.", block.StartLine));
                    return;
                }
            }

            var useBakedReflections = false;
            if (TryGetValueAny(block, out var bakedRaw, "baked_reflections", "use_baked_reflections"))
            {
                if (!TryBool(bakedRaw, out useBakedReflections))
                {
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Sound source '{id}' has invalid baked_reflections value '{bakedRaw}'.", block.StartLine));
                    return;
                }
            }
            if (useBakedReflections)
                useReflections = true;

            var fadeInSeconds = TryFloatAny(block, out var fadeInValue, "fade_in", "fadein", "fade_in_seconds")
                ? Math.Max(0f, fadeInValue)
                : 0f;
            var fadeOutSeconds = TryFloatAny(block, out var fadeOutValue, "fade_out", "fadeout", "fade_out_seconds")
                ? Math.Max(0f, fadeOutValue)
                : 0f;
            var crossfadeSeconds = TryFloatAny(block, out var crossfadeValue, "crossfade", "crossfade_seconds", "crossfade_time")
                ? Math.Max(0f, crossfadeValue)
                : (float?)null;

            var pitch = TryFloatAny(block, out var pitchValue, "pitch", "playback_rate", "playback_speed", "rate")
                ? (pitchValue <= 0f ? 1.0f : pitchValue)
                : 1.0f;
            var pan = TryFloatAny(block, out var panValue, "pan", "balance")
                ? Math.Max(-1f, Math.Min(1f, panValue))
                : 0f;

            var minDistance = TryFloatAny(block, out var minDistanceValue, "min_distance", "min_dist", "ref_distance", "reference_distance")
                ? Math.Max(0.001f, minDistanceValue)
                : (float?)null;
            var maxDistance = TryFloatAny(block, out var maxDistanceValue, "max_distance", "max_dist")
                ? Math.Max(0.001f, maxDistanceValue)
                : (float?)null;
            var rolloff = TryFloatAny(block, out var rolloffValue, "rolloff", "rolloff_factor", "rolloff_scale")
                ? Math.Max(0f, rolloffValue)
                : (float?)null;

            var global = false;
            if (TryGetValueAny(block, out var globalRaw, "global", "always", "always_on"))
            {
                if (!TryBool(globalRaw, out global))
                {
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Sound source '{id}' has invalid global value '{globalRaw}'.", block.StartLine));
                    return;
                }
            }

            var startAreaId = TryGetValueAny(block, out var startAreaValue, "start_area", "start_area_id") ? startAreaValue : null;
            var endAreaId = TryGetValueAny(block, out var endAreaValue, "end_area", "end_area_id") ? endAreaValue : null;

            var startPosition = TryParsePosition(block, "start", out var startPos) ? startPos : (Vector3?)null;
            var startRadius = TryFloatAny(block, out var startRadiusValue, "start_radius", "start_range")
                ? Math.Max(0f, startRadiusValue)
                : (float?)null;
            var endPosition = TryParsePosition(block, "end", out var endPos) ? endPos : (Vector3?)null;
            var endRadius = TryFloatAny(block, out var endRadiusValue, "end_radius", "end_range")
                ? Math.Max(0f, endRadiusValue)
                : (float?)null;
            var position = TryParsePosition(block, out var pos) ? pos : (Vector3?)null;

            var geometryId = TryGetValueAny(block, out var geometryValue, "geometry", "geometry_id") ? geometryValue : null;
            var pathGeometryId = TryGetValueAny(block, out var pathGeometryValue, "path_geometry", "path_geometry_id") ? pathGeometryValue : null;
            if (type == TrackSoundSourceType.Moving && string.IsNullOrWhiteSpace(pathGeometryId))
                pathGeometryId = geometryId;

            var speed = TryFloatAny(block, out var speedValue, "speed", "speed_mps", "velocity")
                ? Math.Max(0f, speedValue)
                : (float?)null;

            if (path != null && !IsTrackRelativePath(path, mapRoot))
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Sound source '{id}' path must be relative to the track folder.", block.StartLine));
                return;
            }
            if (variantPaths.Count > 0)
            {
                for (int i = 0; i < variantPaths.Count; i++)
                {
                    if (!IsTrackRelativePath(variantPaths[i], mapRoot))
                    {
                        issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Sound source '{id}' variant path must be relative to the track folder.", block.StartLine));
                        return;
                    }
                }
            }

            soundSources.Add(new TrackSoundSourceDefinition(
                id,
                type,
                path,
                variantPaths,
                variantSourceIds,
                randomMode,
                loop,
                volume,
                spatial,
                allowHrtf,
                useReflections,
                useBakedReflections,
                fadeInSeconds,
                fadeOutSeconds,
                crossfadeSeconds,
                pitch,
                pan,
                minDistance,
                maxDistance,
                rolloff,
                global,
                startAreaId,
                endAreaId,
                startPosition,
                startRadius,
                endPosition,
                endRadius,
                position,
                geometryId,
                pathGeometryId,
                speed));
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
            var volumeId = TryGetValue(block, "volume", out var volumeValue) ? volumeValue :
                (TryGetValue(block, "volume_id", out volumeValue) ? volumeValue : null);

            var volumeThickness = TryFloatAny(block, out var thicknessValue, "height", "thickness", "volume_thickness", "volume_height")
                ? Math.Max(0.01f, thicknessValue)
                : (float?)null;
            var volumeOffset = TryFloatAny(block, out var offsetValue, "offset", "volume_offset", "volume_center")
                ? offsetValue
                : (float?)null;
            var minY = TryFloatAny(block, out var minYValue, "min_y", "miny") ? minYValue : (float?)null;
            var maxY = TryFloatAny(block, out var maxYValue, "max_y", "maxy") ? maxYValue : (float?)null;
            var volumeMode = TryParseAreaVolumeMode(block, out var modeValue) ? modeValue : TrackAreaVolumeMode.LocalBand;
            var volumeOffsetMode = TryParseAreaVolumeOffsetMode(block, out var offsetModeValue)
                ? offsetModeValue
                : TrackAreaVolumeOffsetMode.Bottom;
            var volumeOffsetSpace = TryParseAreaVolumeSpace(block, out var offsetSpaceValue, "volume_offset_space", "offset_space")
                ? offsetSpaceValue
                : TrackAreaVolumeSpace.Inherit;
            var volumeMinMaxSpace = TryParseAreaVolumeSpace(block, out var minMaxSpaceValue, "volume_minmax_space", "minmax_space", "bounds_space", "volume_bounds_space")
                ? minMaxSpaceValue
                : TrackAreaVolumeSpace.Inherit;

            if (minY.HasValue && maxY.HasValue && maxY.Value <= minY.Value)
            {
                issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Approach '{sectorId}' max_y must be above min_y.", block.StartLine));
                return;
            }

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
                metadata,
                volumeThickness,
                volumeOffset,
                minY,
                maxY,
                volumeMode,
                volumeOffsetMode,
                volumeOffsetSpace,
                volumeMinMaxSpace,
                volumeId));
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

        private static IReadOnlyList<string> ParseStringListFromBlock(SectionBlock block, params string[] keys)
        {
            List<string>? list = null;
            foreach (var raw in GetValues(block, keys))
            {
                if (string.IsNullOrWhiteSpace(raw))
                    continue;
                foreach (var token in SplitListTokens(raw))
                {
                    var trimmed = token.Trim().Trim('"');
                    if (trimmed.Length == 0)
                        continue;
                    list ??= new List<string>();
                    list.Add(trimmed);
                }
            }
            return list ?? (IReadOnlyList<string>)Array.Empty<string>();
        }

        private static IEnumerable<string> SplitListTokens(string raw)
        {
            var tokens = raw.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < tokens.Length; i++)
            {
                var token = tokens[i]?.Trim();
                if (!string.IsNullOrWhiteSpace(token))
                    yield return token!;
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

        private static bool TryIntAny(SectionBlock block, out int value, params string[] keys)
        {
            value = 0;
            foreach (var key in keys)
            {
                if (!TryGetValue(block, key, out var raw))
                    continue;
                if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                    return true;
            }
            return false;
        }

        private static bool TryGetValueAny(SectionBlock block, out string value, params string[] keys)
        {
            value = string.Empty;
            foreach (var key in keys)
            {
                if (TryGetValue(block, key, out var raw) && !string.IsNullOrWhiteSpace(raw))
                {
                    value = raw;
                    return true;
                }
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
            if (TryFloat(block, "reflection_wet", out var reflectionWet))
                overrides.ReflectionWet = Clamp01(reflectionWet);
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
            if (TryFloat(block, "occlusion_override", out var occlusionOverride))
                overrides.OcclusionOverride = Clamp01(occlusionOverride);
            if (ResolveOverrideTriple(
                    block,
                    "transmission_override",
                    "transmission_override_low",
                    "transmission_override_mid",
                    "transmission_override_high",
                    out var transOverrideLow,
                    out var transOverrideMid,
                    out var transOverrideHigh))
            {
                overrides.TransmissionOverrideLow = transOverrideLow;
                overrides.TransmissionOverrideMid = transOverrideMid;
                overrides.TransmissionOverrideHigh = transOverrideHigh;
            }
            if (ResolveOverrideTriple(
                    block,
                    "air_absorption_override",
                    "air_absorption_override_low",
                    "air_absorption_override_mid",
                    "air_absorption_override_high",
                    out var airOverrideLow,
                    out var airOverrideMid,
                    out var airOverrideHigh))
            {
                overrides.AirAbsorptionOverrideLow = airOverrideLow;
                overrides.AirAbsorptionOverrideMid = airOverrideMid;
                overrides.AirAbsorptionOverrideHigh = airOverrideHigh;
            }

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

        private static bool ResolveOverrideTriple(
            SectionBlock block,
            string baseKey,
            string lowKey,
            string midKey,
            string highKey,
            out float? low,
            out float? mid,
            out float? high)
        {
            low = null;
            mid = null;
            high = null;
            var hasAny = false;

            if (TryFloat(block, baseKey, out var baseValue))
            {
                var clamped = Clamp01(baseValue);
                low = clamped;
                mid = clamped;
                high = clamped;
                hasAny = true;
            }

            if (TryFloat(block, lowKey, out var lowValue))
            {
                low = Clamp01(lowValue);
                hasAny = true;
            }
            if (TryFloat(block, midKey, out var midValue))
            {
                mid = Clamp01(midValue);
                hasAny = true;
            }
            if (TryFloat(block, highKey, out var highValue))
            {
                high = Clamp01(highValue);
                hasAny = true;
            }

            return hasAny;
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

        private static IReadOnlyDictionary<string, string>? CollectGeometryMetadata(SectionBlock block)
        {
            Dictionary<string, string>? metadata = null;
            foreach (var pair in block.Values)
            {
                if (GeometryKnownKeys.Contains(pair.Key))
                    continue;
                if (pair.Value.Count == 0)
                    continue;
                metadata ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                metadata[pair.Key] = pair.Value[pair.Value.Count - 1];
            }
            return metadata;
        }

        private static IReadOnlyDictionary<string, string>? CollectVolumeMetadata(SectionBlock block)
        {
            Dictionary<string, string>? metadata = null;
            foreach (var pair in block.Values)
            {
                if (VolumeKnownKeys.Contains(pair.Key))
                    continue;
                if (pair.Value.Count == 0)
                    continue;
                metadata ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                metadata[pair.Key] = pair.Value[pair.Value.Count - 1];
            }
            return metadata;
        }

        private static IReadOnlyDictionary<string, string>? CollectSurfaceMetadata(SectionBlock block)
        {
            Dictionary<string, string>? metadata = null;
            foreach (var pair in block.Values)
            {
                if (SurfaceKnownKeys.Contains(pair.Key))
                    continue;
                if (pair.Value.Count == 0)
                    continue;
                metadata ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                metadata[pair.Key] = pair.Value[pair.Value.Count - 1];
            }
            return metadata;
        }

        private static IReadOnlyDictionary<string, string>? CollectProfileMetadata(SectionBlock block)
        {
            Dictionary<string, string>? metadata = null;
            foreach (var pair in block.Values)
            {
                if (ProfileKnownKeys.Contains(pair.Key))
                    continue;
                if (pair.Value.Count == 0)
                    continue;
                metadata ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                metadata[pair.Key] = pair.Value[pair.Value.Count - 1];
            }
            return metadata;
        }

        private static IReadOnlyDictionary<string, string>? CollectBankMetadata(SectionBlock block)
        {
            Dictionary<string, string>? metadata = null;
            foreach (var pair in block.Values)
            {
                if (BankKnownKeys.Contains(pair.Key))
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
                if (TryParseCompassHeading(raw, out headingDegrees))
                    return true;
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
                if (TryParseCompassHeading(raw, out headingDegrees))
                    return true;
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
                if (TryParseCompassHeading(raw, out headingDegrees))
                    return true;
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

        private static bool TryParsePoints3D(SectionBlock block, out List<Vector3> points)
        {
            points = new List<Vector3>();

            foreach (var raw in GetValues(
                         block,
                         "points3d",
                         "points_3d",
                         "points3",
                         "vertices3d",
                         "vertices_3d",
                         "vertex3d",
                         "vertex_3d",
                         "vertices",
                         "vertex",
                         "mesh_points3d",
                         "mesh_points_3d",
                         "mesh_points3",
                         "mesh_vertices3d",
                         "mesh_vertices_3d",
                         "mesh_vertices",
                         "mesh_vertex3d",
                         "mesh_vertex_3d",
                         "mesh_vertex"))
            {
                if (!TryParsePoints3D(raw, out var parsed))
                    return false;
                points.AddRange(parsed);
            }

            foreach (var raw in GetValues(block, "point3d", "point_3d", "point3"))
            {
                if (!TryParsePoints3D(raw, out var parsed))
                    return false;
                points.AddRange(parsed);
            }

            return points.Count > 0;
        }

        private static bool TryParseTrianglePoints3D(SectionBlock block, out List<Vector3> points)
        {
            points = new List<Vector3>();

            foreach (var raw in GetValues(block, "triangles3d", "triangle3d", "triangle_points", "triangle_points3d", "tri_points", "tri_points3d"))
            {
                if (!TryParsePoints3D(raw, out var parsed))
                    return false;
                points.AddRange(parsed);
            }

            return points.Count > 0;
        }

        private static bool TryParseTriangleIndices(SectionBlock block, out List<int> indices)
        {
            indices = new List<int>();

            foreach (var raw in GetValues(
                         block,
                         "triangles",
                         "triangle_indices",
                         "indices",
                         "faces",
                         "tris",
                         "tri",
                         "mesh_triangles",
                         "mesh_triangle_indices",
                         "mesh_indices",
                         "mesh_faces",
                         "mesh_tris"))
            {
                if (!TryParseIndices(raw, out var parsed))
                    return false;
                indices.AddRange(parsed);
            }

            return indices.Count > 0;
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

        private static bool TryParseCompassHeading(string value, out float headingDegrees)
        {
            headingDegrees = 0f;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var trimmed = value.Trim().ToLowerInvariant()
                .Replace(" ", string.Empty)
                .Replace("-", string.Empty)
                .Replace("_", string.Empty);

            switch (trimmed)
            {
                case "n":
                case "north":
                    headingDegrees = 0f;
                    return true;
                case "nne":
                case "northnortheast":
                    headingDegrees = 22.5f;
                    return true;
                case "ne":
                case "northeast":
                    headingDegrees = 45f;
                    return true;
                case "ene":
                case "eastnortheast":
                    headingDegrees = 67.5f;
                    return true;
                case "e":
                case "east":
                    headingDegrees = 90f;
                    return true;
                case "ese":
                case "eastsoutheast":
                    headingDegrees = 112.5f;
                    return true;
                case "se":
                case "southeast":
                    headingDegrees = 135f;
                    return true;
                case "sse":
                case "southsoutheast":
                    headingDegrees = 157.5f;
                    return true;
                case "s":
                case "south":
                    headingDegrees = 180f;
                    return true;
                case "ssw":
                case "southsouthwest":
                    headingDegrees = 202.5f;
                    return true;
                case "sw":
                case "southwest":
                    headingDegrees = 225f;
                    return true;
                case "wsw":
                case "westsouthwest":
                    headingDegrees = 247.5f;
                    return true;
                case "w":
                case "west":
                    headingDegrees = 270f;
                    return true;
                case "wnw":
                case "westnorthwest":
                    headingDegrees = 292.5f;
                    return true;
                case "nw":
                case "northwest":
                    headingDegrees = 315f;
                    return true;
                case "nnw":
                case "northnorthwest":
                    headingDegrees = 337.5f;
                    return true;
            }

            return false;
        }

        private static bool TryGeometryType(string value, out GeometryType type)
        {
            type = GeometryType.Undefined;
            if (string.IsNullOrWhiteSpace(value))
                return false;
            var trimmed = value.Trim().ToLowerInvariant();
            switch (trimmed)
            {
                case "polygon":
                case "poly":
                    type = GeometryType.Polygon;
                    return true;
                case "polyline":
                case "line":
                case "path":
                    type = GeometryType.Polyline;
                    return true;
                case "spline":
                case "curve":
                    type = GeometryType.Spline;
                    return true;
                case "mesh":
                    type = GeometryType.Mesh;
                    return true;
            }
            return Enum.TryParse(value, true, out type);
        }

        private static bool TryVolumeType(string value, out TrackVolumeType type)
        {
            type = TrackVolumeType.Undefined;
            if (string.IsNullOrWhiteSpace(value))
                return false;
            var trimmed = value.Trim().ToLowerInvariant();
            switch (trimmed)
            {
                case "box":
                case "rect":
                case "rectangle":
                case "cube":
                    type = TrackVolumeType.Box;
                    return true;
                case "sphere":
                case "ball":
                    type = TrackVolumeType.Sphere;
                    return true;
                case "cylinder":
                case "cyl":
                    type = TrackVolumeType.Cylinder;
                    return true;
                case "capsule":
                case "cap":
                    type = TrackVolumeType.Capsule;
                    return true;
                case "prism":
                case "extrude":
                case "extrusion":
                    type = TrackVolumeType.Prism;
                    return true;
                case "mesh":
                    type = TrackVolumeType.Mesh;
                    return true;
            }
            return Enum.TryParse(value, true, out type);
        }

        private static bool TrySurfaceType(string value, out TrackSurfaceType type)
        {
            type = TrackSurfaceType.Undefined;
            if (string.IsNullOrWhiteSpace(value))
                return false;
            var trimmed = value.Trim().ToLowerInvariant();
            switch (trimmed)
            {
                case "loft":
                case "ribbon":
                case "path":
                    type = TrackSurfaceType.Loft;
                    return true;
                case "polygon":
                case "area":
                    type = TrackSurfaceType.Polygon;
                    return true;
                case "mesh":
                    type = TrackSurfaceType.Mesh;
                    return true;
            }
            return Enum.TryParse(value, true, out type);
        }

        private static bool TryProfileType(string value, out TrackProfileType type)
        {
            type = TrackProfileType.Undefined;
            if (string.IsNullOrWhiteSpace(value))
                return false;
            var trimmed = value.Trim().ToLowerInvariant();
            switch (trimmed)
            {
                case "flat":
                    type = TrackProfileType.Flat;
                    return true;
                case "plane":
                    type = TrackProfileType.Plane;
                    return true;
                case "linear":
                case "linear_along_path":
                case "linear_along_centerline":
                    type = TrackProfileType.LinearAlongPath;
                    return true;
                case "spline":
                case "spline_along_path":
                case "spline_along_centerline":
                    type = TrackProfileType.SplineAlongPath;
                    return true;
                case "bezier":
                case "bezier_along_path":
                case "bezier_along_centerline":
                    type = TrackProfileType.BezierAlongPath;
                    return true;
                case "grid":
                case "height_grid":
                    type = TrackProfileType.Grid;
                    return true;
            }
            return Enum.TryParse(value, true, out type);
        }

        private static bool TryBankType(string value, out TrackBankType type)
        {
            type = TrackBankType.Undefined;
            if (string.IsNullOrWhiteSpace(value))
                return false;
            var trimmed = value.Trim().ToLowerInvariant();
            switch (trimmed)
            {
                case "flat":
                    type = TrackBankType.Flat;
                    return true;
                case "linear":
                case "linear_along_path":
                case "linear_along_centerline":
                    type = TrackBankType.LinearAlongPath;
                    return true;
                case "spline":
                case "spline_along_path":
                case "spline_along_centerline":
                    type = TrackBankType.SplineAlongPath;
                    return true;
                case "bezier":
                case "bezier_along_path":
                case "bezier_along_centerline":
                    type = TrackBankType.BezierAlongPath;
                    return true;
            }
            return Enum.TryParse(value, true, out type);
        }

        private static bool TryParseAreaVolumeMode(SectionBlock block, out TrackAreaVolumeMode mode)
        {
            mode = TrackAreaVolumeMode.LocalBand;
            if (!TryGetValue(block, "volume_mode", out var raw) || string.IsNullOrWhiteSpace(raw))
                return false;

            var trimmed = raw.Trim().ToLowerInvariant();
            switch (trimmed)
            {
                case "local":
                case "local_band":
                case "band":
                    mode = TrackAreaVolumeMode.LocalBand;
                    return true;
                case "world":
                case "world_band":
                case "world_y":
                case "worldy":
                    mode = TrackAreaVolumeMode.WorldBand;
                    return true;
                case "closed":
                case "closed_mesh":
                    mode = TrackAreaVolumeMode.ClosedMesh;
                    return true;
            }

            return Enum.TryParse(raw, true, out mode);
        }

        private static bool TryParseAreaVolumeOffsetMode(SectionBlock block, out TrackAreaVolumeOffsetMode mode)
        {
            mode = TrackAreaVolumeOffsetMode.Bottom;
            if (!TryGetValueAny(
                    block,
                    out var raw,
                    "volume_offset_mode",
                    "offset_mode",
                    "offset_anchor",
                    "volume_offset_anchor",
                    "offset_align",
                    "volume_offset_align") ||
                string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            var trimmed = raw.Trim().ToLowerInvariant();
            switch (trimmed)
            {
                case "bottom":
                case "min":
                case "lower":
                case "start":
                    mode = TrackAreaVolumeOffsetMode.Bottom;
                    return true;
                case "center":
                case "centre":
                case "middle":
                case "mid":
                    mode = TrackAreaVolumeOffsetMode.Center;
                    return true;
                case "top":
                case "max":
                case "upper":
                case "end":
                    mode = TrackAreaVolumeOffsetMode.Top;
                    return true;
            }

            return Enum.TryParse(raw, true, out mode);
        }

        private static bool TryParseAreaVolumeSpace(SectionBlock block, out TrackAreaVolumeSpace space, params string[] keys)
        {
            space = TrackAreaVolumeSpace.Inherit;
            if (!TryGetValueAny(block, out var raw, keys) || string.IsNullOrWhiteSpace(raw))
                return false;

            var trimmed = raw.Trim().ToLowerInvariant();
            switch (trimmed)
            {
                case "inherit":
                case "default":
                case "auto":
                    space = TrackAreaVolumeSpace.Inherit;
                    return true;
                case "local":
                case "relative":
                case "elevation":
                case "area":
                    space = TrackAreaVolumeSpace.Local;
                    return true;
                case "world":
                case "absolute":
                case "global":
                    space = TrackAreaVolumeSpace.World;
                    return true;
            }

            return Enum.TryParse(raw, true, out space);
        }

        private static bool TryParseSoundRandomMode(string value, out TrackSoundRandomMode mode)
        {
            mode = TrackSoundRandomMode.OnStart;
            if (string.IsNullOrWhiteSpace(value))
                return false;
            var trimmed = value.Trim().ToLowerInvariant();
            switch (trimmed)
            {
                case "on_start":
                case "onstart":
                case "start":
                case "once":
                    mode = TrackSoundRandomMode.OnStart;
                    return true;
                case "per_area":
                case "perarea":
                case "area":
                    mode = TrackSoundRandomMode.PerArea;
                    return true;
            }
            return Enum.TryParse(value, true, out mode);
        }

        private static bool IsTrackRelativePath(string rawPath, string mapRoot)
        {
            if (string.IsNullOrWhiteSpace(rawPath))
                return false;
            if (Path.IsPathRooted(rawPath))
                return false;
            if (string.IsNullOrWhiteSpace(mapRoot))
                return true;

            var rootFull = Path.GetFullPath(mapRoot);
            if (!rootFull.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                rootFull += Path.DirectorySeparatorChar;
            var combined = Path.GetFullPath(Path.Combine(rootFull, rawPath));
            return combined.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryParsePolygonPlanarityMode(SectionBlock block, out PolygonPlanarityMode mode)
        {
            mode = PolygonPlanarityMode.Strict;
            if (!TryGetValueAny(block, out var raw, "planarity", "polygon_planarity", "planar", "planar_mode", "plane_mode"))
                return false;

            var trimmed = raw.Trim().ToLowerInvariant();
            switch (trimmed)
            {
                case "strict":
                case "required":
                case "enforce":
                    mode = PolygonPlanarityMode.Strict;
                    return true;
                case "relaxed":
                case "allow":
                case "best_fit":
                case "bestfit":
                case "fit":
                    mode = PolygonPlanarityMode.Relaxed;
                    return true;
                case "ignore":
                case "none":
                case "off":
                    mode = PolygonPlanarityMode.Ignore;
                    return true;
            }

            return Enum.TryParse(raw, true, out mode);
        }

        private static bool TryBankSide(string value, out TrackBankSide side)
        {
            side = TrackBankSide.Right;
            if (string.IsNullOrWhiteSpace(value))
                return false;
            var trimmed = value.Trim().ToLowerInvariant();
            switch (trimmed)
            {
                case "left":
                case "l":
                    side = TrackBankSide.Left;
                    return true;
                case "right":
                case "r":
                    side = TrackBankSide.Right;
                    return true;
            }
            return Enum.TryParse(value, true, out side);
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

        private static bool TryParsePoints3D(string raw, out List<Vector3> points)
        {
            points = new List<Vector3>();
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            var normalized = raw
                .Replace("(", " ")
                .Replace(")", " ")
                .Replace("[", " ")
                .Replace("]", " ")
                .Replace("{", " ")
                .Replace("}", " ");
            var tokens = normalized.Split(new[] { ',', ';', '|', ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 3 || (tokens.Length % 3) != 0)
                return false;

            for (var i = 0; i < tokens.Length; i += 3)
            {
                if (!float.TryParse(tokens[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var x))
                    return false;
                if (!float.TryParse(tokens[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                    return false;
                if (!float.TryParse(tokens[i + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
                    return false;
                points.Add(new Vector3(x, y, z));
            }

            return points.Count > 0;
        }

        private static bool TryParseVector3(string raw, out Vector3 value)
        {
            value = Vector3.Zero;
            if (!TryParsePoints3D(raw, out var points) || points.Count != 1)
                return false;
            value = points[0];
            return true;
        }

        private static bool TryParseVector3Any(SectionBlock block, out Vector3 value, params string[] keys)
        {
            value = Vector3.Zero;
            foreach (var raw in GetValues(block, keys))
            {
                if (TryParseVector3(raw, out value))
                    return true;
            }
            return false;
        }

        private static bool TryParsePosition(SectionBlock block, out Vector3 value)
        {
            return TryParsePosition(block, string.Empty, out value);
        }

        private static bool TryParsePosition(SectionBlock block, string prefix, out Vector3 value)
        {
            value = Vector3.Zero;
            var normalizedPrefix = string.IsNullOrWhiteSpace(prefix) ? string.Empty : prefix.Trim().TrimEnd('_');
            if (string.IsNullOrWhiteSpace(normalizedPrefix))
            {
                if (TryParseVector3Any(block, out value, "position", "pos", "center"))
                    return true;

                var hasX = TryFloatAny(block, out var x, "x", "pos_x", "position_x");
                var hasY = TryFloatAny(block, out var y, "y", "pos_y", "position_y");
                var hasZ = TryFloatAny(block, out var z, "z", "pos_z", "position_z");
                if (hasX || hasY || hasZ)
                {
                    value = new Vector3(hasX ? x : 0f, hasY ? y : 0f, hasZ ? z : 0f);
                    return true;
                }

                return false;
            }

            var keyPrefix = normalizedPrefix + "_";
            if (TryParseVector3Any(
                    block,
                    out value,
                    keyPrefix + "position",
                    keyPrefix + "pos",
                    keyPrefix + "point"))
            {
                return true;
            }

            var hasPrefixX = TryFloatAny(block, out var px, keyPrefix + "x");
            var hasPrefixY = TryFloatAny(block, out var py, keyPrefix + "y");
            var hasPrefixZ = TryFloatAny(block, out var pz, keyPrefix + "z");
            if (hasPrefixX || hasPrefixY || hasPrefixZ)
            {
                value = new Vector3(hasPrefixX ? px : 0f, hasPrefixY ? py : 0f, hasPrefixZ ? pz : 0f);
                return true;
            }

            return false;
        }

        private static bool TryParseRotation(SectionBlock block, out Vector3 rotationDegrees)
        {
            rotationDegrees = Vector3.Zero;

            if (TryParseVector3Any(block, out var vector, "rotation", "rotation_deg", "rotation_degrees", "rot", "rot_deg", "rot_degrees"))
            {
                rotationDegrees = vector;
                return true;
            }

            var hasAny = false;
            var pitch = 0f;
            var yaw = 0f;
            var roll = 0f;

            if (TryFloatAny(block, out var yawValue, "yaw", "rotation_yaw"))
            {
                yaw = yawValue;
                hasAny = true;
            }
            if (TryFloatAny(block, out var pitchValue, "pitch", "rotation_pitch"))
            {
                pitch = pitchValue;
                hasAny = true;
            }
            if (TryFloatAny(block, out var rollValue, "roll", "rotation_roll"))
            {
                roll = rollValue;
                hasAny = true;
            }

            if (!hasAny)
                return false;

            rotationDegrees = new Vector3(pitch, yaw, roll);
            return true;
        }

        private static bool TryParseIndices(string raw, out List<int> indices)
        {
            indices = new List<int>();
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            var normalized = raw
                .Replace("(", " ")
                .Replace(")", " ")
                .Replace("[", " ")
                .Replace("]", " ")
                .Replace("{", " ")
                .Replace("}", " ");
            var tokens = normalized.Split(new[] { ',', ';', '|', ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
                return false;

            for (var i = 0; i < tokens.Length; i++)
            {
                var token = tokens[i];
                if (token.IndexOf('/') >= 0)
                {
                    var parts = token.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 0)
                        return false;
                    for (var partIndex = 0; partIndex < parts.Length; partIndex++)
                    {
                        if (!int.TryParse(parts[partIndex], NumberStyles.Integer, CultureInfo.InvariantCulture, out var splitIndex))
                            return false;
                        indices.Add(splitIndex);
                    }
                    continue;
                }

                if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
                    return false;
                indices.Add(index);
            }

            return indices.Count > 0;
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
            var hasTopology = map.Areas.Count > 0 || map.Geometries.Count > 0 || map.Sectors.Count > 0 || map.Portals.Count > 0;
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
            if (map.Geometries.Count == 0 && map.Portals.Count == 0 &&
                map.Links.Count == 0 && map.Areas.Count == 0 && map.Beacons.Count == 0 && map.Markers.Count == 0 &&
                map.Approaches.Count == 0)
                return;

            var sectorIds = new HashSet<string>(map.Sectors.Select(s => s.Id), StringComparer.OrdinalIgnoreCase);
            var geometryIds = new HashSet<string>(map.Geometries.Select(g => g.Id), StringComparer.OrdinalIgnoreCase);
            var portalIds = new HashSet<string>(map.Portals.Select(p => p.Id), StringComparer.OrdinalIgnoreCase);
            var materialIds = new HashSet<string>(map.Materials.Select(m => m.Id), StringComparer.OrdinalIgnoreCase);
            var roomIds = new HashSet<string>(map.Rooms.Select(r => r.Id), StringComparer.OrdinalIgnoreCase);
            var profileIds = new HashSet<string>(map.Profiles.Select(p => p.Id), StringComparer.OrdinalIgnoreCase);
            var bankIds = new HashSet<string>(map.Banks.Select(b => b.Id), StringComparer.OrdinalIgnoreCase);

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
                if (!geometryIds.Contains(area.GeometryId))
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Area '{area.Id}' references missing geometry '{area.GeometryId}'."));
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
                if (!string.IsNullOrWhiteSpace(beacon.GeometryId) && !geometryIds.Contains(beacon.GeometryId!))
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Beacon '{beacon.Id}' references missing geometry '{beacon.GeometryId}'."));
                if (sectorIds.Count > 0 && !string.IsNullOrWhiteSpace(beacon.SectorId) && !sectorIds.Contains(beacon.SectorId!))
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Warning, $"Beacon '{beacon.Id}' references missing sector '{beacon.SectorId}'."));
                if (string.IsNullOrWhiteSpace(beacon.GeometryId) && !beacon.ActivationRadiusMeters.HasValue)
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Warning, $"Beacon '{beacon.Id}' has no activation area."));
            }

            foreach (var marker in map.Markers)
            {
                if (!string.IsNullOrWhiteSpace(marker.GeometryId) && !geometryIds.Contains(marker.GeometryId!))
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Marker '{marker.Id}' references missing geometry '{marker.GeometryId}'."));
            }

            foreach (var surface in map.Surfaces)
            {
                if (surface == null)
                    continue;

                if (surface.Type == TrackSurfaceType.Undefined)
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Surface '{surface.Id}' has undefined type."));

                if (string.IsNullOrWhiteSpace(surface.GeometryId))
                {
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Surface '{surface.Id}' requires geometry."));
                    continue;
                }

                if (!geometryIds.Contains(surface.GeometryId!))
                {
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Surface '{surface.Id}' references missing geometry '{surface.GeometryId}'."));
                    continue;
                }

                var geometry = map.Geometries.FirstOrDefault(
                    g => g != null && string.Equals(g.Id, surface.GeometryId, StringComparison.OrdinalIgnoreCase));
                if (geometry == null)
                    continue;

                switch (surface.Type)
                {
                    case TrackSurfaceType.Mesh:
                        if (geometry.Type != GeometryType.Mesh)
                        {
                            issues.Add(new TrackMapIssue(
                                TrackMapIssueSeverity.Error,
                                $"Surface '{surface.Id}' type=mesh requires mesh geometry '{surface.GeometryId}'."));
                        }
                        break;
                    case TrackSurfaceType.Loft:
                        if (geometry.Type != GeometryType.Polyline && geometry.Type != GeometryType.Spline)
                        {
                            issues.Add(new TrackMapIssue(
                                TrackMapIssueSeverity.Error,
                                $"Surface '{surface.Id}' type=loft requires polyline/spline geometry '{surface.GeometryId}'."));
                        }
                        break;
                    case TrackSurfaceType.Polygon:
                        if (geometry.Type != GeometryType.Polygon && geometry.Type != GeometryType.Mesh)
                        {
                            issues.Add(new TrackMapIssue(
                                TrackMapIssueSeverity.Error,
                                $"Surface '{surface.Id}' type=polygon requires polygon or mesh geometry '{surface.GeometryId}'."));
                        }
                        break;
                }

                if (!string.IsNullOrWhiteSpace(surface.ProfileId) && !profileIds.Contains(surface.ProfileId!))
                {
                    issues.Add(new TrackMapIssue(
                        TrackMapIssueSeverity.Error,
                        $"Surface '{surface.Id}' references missing profile '{surface.ProfileId}'."));
                }

                if (!string.IsNullOrWhiteSpace(surface.BankId) && !bankIds.Contains(surface.BankId!))
                {
                    issues.Add(new TrackMapIssue(
                        TrackMapIssueSeverity.Error,
                        $"Surface '{surface.Id}' references missing bank '{surface.BankId}'."));
                }

                if (!string.IsNullOrWhiteSpace(surface.MaterialId))
                {
                    var materialId = surface.MaterialId!;
                    if (!materialIds.Contains(materialId) && !TrackMaterialLibrary.IsPreset(materialId))
                    {
                        issues.Add(new TrackMapIssue(
                            TrackMapIssueSeverity.Error,
                            $"Surface '{surface.Id}' references missing material '{materialId}'."));
                    }
                }
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
                if (!geometryIds.Contains(wall.GeometryId))
                    issues.Add(new TrackMapIssue(TrackMapIssueSeverity.Error, $"Wall '{wall.Id}' references missing geometry '{wall.GeometryId}'."));
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
            public RegionSample(string id, GeometryDefinition geometry, float widthMeters, bool closedCentered)
            {
                Id = id;
                Geometry = geometry;
                WidthMeters = widthMeters;
                ClosedCentered = closedCentered;
                Points2D = ProjectToXZ(geometry);
                Bounds = GetBounds(geometry, Points2D, widthMeters, closedCentered);
            }

            public string Id { get; }
            public GeometryDefinition Geometry { get; }
            public float WidthMeters { get; }
            public bool ClosedCentered { get; }
            public IReadOnlyList<Vector2> Points2D { get; }
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
            if (map.Geometries.Count == 0 || map.Areas.Count == 0)
                return;

            var geometryById = new Dictionary<string, GeometryDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (var geometry in map.Geometries)
            {
                if (geometry == null || string.IsNullOrWhiteSpace(geometry.Id))
                    continue;
                geometryById[geometry.Id] = geometry;
            }

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
                    if (!geometryById.TryGetValue(area.GeometryId, out var geometry))
                        continue;

                    var closedCentered = IsCenteredClosedWidth(area.Metadata);
                    var width = area.WidthMeters.GetValueOrDefault();
                    drivableRegions.Add(new RegionSample($"area:{area.Id}", geometry, width, closedCentered));
                }
            }
            foreach (var area in map.Areas)
            {
                if (area == null)
                    continue;
                if (!IsNonDrivableArea(area))
                    continue;
                if (!geometryById.TryGetValue(area.GeometryId, out var geometry))
                    continue;

                var closedCentered = IsCenteredClosedWidth(area.Metadata);
                var width = area.WidthMeters.GetValueOrDefault();
                nonDrivableAreas.Add(new RegionSample($"area:{area.Id}", geometry, width, closedCentered));
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
                        if (!Contains(region.Geometry, region.Points2D, sample, region.WidthMeters, region.ClosedCentered))
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
                            if (!Contains(blocked.Geometry, blocked.Points2D, sample, blocked.WidthMeters, blocked.ClosedCentered))
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

        private static IReadOnlyList<Vector2> ProjectToXZ(GeometryDefinition geometry)
        {
            if (geometry == null || geometry.Points == null || geometry.Points.Count == 0)
                return Array.Empty<Vector2>();

            var projected = new List<Vector2>(geometry.Points.Count);
            foreach (var point in geometry.Points)
                projected.Add(new Vector2(point.X, point.Z));
            return projected;
        }

        private static Bounds GetBounds(
            GeometryDefinition geometry,
            IReadOnlyList<Vector2> points2D,
            float widthMeters,
            bool closedCentered)
        {
            if (geometry == null || points2D == null || points2D.Count == 0)
                return new Bounds(0f, 0f, 0f, 0f);

            var minX = float.MaxValue;
            var minZ = float.MaxValue;
            var maxX = float.MinValue;
            var maxZ = float.MinValue;
            foreach (var point in points2D)
            {
                if (point.X < minX) minX = point.X;
                if (point.Y < minZ) minZ = point.Y;
                if (point.X > maxX) maxX = point.X;
                if (point.Y > maxZ) maxZ = point.Y;
            }

            var expandBy = Math.Abs(widthMeters);
            if (closedCentered && expandBy > 0f)
                expandBy *= 0.5f;

            return new Bounds(minX - expandBy, minZ - expandBy, maxX + expandBy, maxZ + expandBy);
        }

        private static bool Contains(
            GeometryDefinition geometry,
            IReadOnlyList<Vector2> points2D,
            Vector2 position,
            float widthMeters,
            bool closedCentered)
        {
            if (geometry == null)
                return false;
            switch (geometry.Type)
            {
                case GeometryType.Polygon:
                    return ContainsPolygonPath(points2D, position, widthMeters, closedCentered);
                case GeometryType.Polyline:
                case GeometryType.Spline:
                    return ContainsPolylinePath(points2D, position, widthMeters, closedCentered);
                case GeometryType.Mesh:
                case GeometryType.Undefined:
                default:
                    return false;
            }
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




