using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using TopSpeed.Core;
using TopSpeed.Data;
using TopSpeed.Tracks.Materials;
using TopSpeed.Tracks.Rooms;
using TopSpeed.Tracks.Areas;
using TopSpeed.Tracks.Geometry;
using TopSpeed.Tracks.Topology;
using TopSpeed.Tracks.Surfaces;
using TopSpeed.Tracks.Walls;

namespace TopSpeed.Tracks.Map
{
    internal static class TrackMapLoader
    {
        private const string MapExtension = ".tsm";

        public static bool LooksLikeMap(string nameOrPath)
        {
            if (string.IsNullOrWhiteSpace(nameOrPath))
                return false;
            if (Path.HasExtension(nameOrPath))
                return string.Equals(Path.GetExtension(nameOrPath), MapExtension, StringComparison.OrdinalIgnoreCase);
            return false;
        }

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
                var folderPath = Path.Combine(AssetPaths.Root, "Tracks", nameOrPath, "track" + MapExtension);
                if (File.Exists(folderPath))
                {
                    path = folderPath;
                    return true;
                }

                return false;
            }

            path = Path.Combine(AssetPaths.Root, "Tracks", nameOrPath);
            return File.Exists(path) && LooksLikeMap(path);
        }

        public static TrackMap Load(string nameOrPath)
        {
            var path = ResolvePath(nameOrPath);
            if (!File.Exists(path))
                throw new FileNotFoundException("Track map not found.", path);

            var definition = TrackMapFormat.Parse(path);
            var mapRoot = Path.GetDirectoryName(path) ?? Path.Combine(AssetPaths.Root, "Tracks");

            var map = new TrackMap(definition.Metadata.Name, definition.Metadata.CellSizeMeters)
            {
                Weather = definition.Metadata.Weather,
                Ambience = definition.Metadata.Ambience,
                DefaultMaterialId = definition.Metadata.DefaultMaterialId,
                DefaultNoise = definition.Metadata.DefaultNoise,
                DefaultWidthMeters = definition.Metadata.DefaultWidthMeters,
                BaseHeightMeters = definition.Metadata.BaseHeightMeters ?? 0f,
                DefaultAreaHeightMeters = definition.Metadata.DefaultAreaHeightMeters ?? 0f,
                DefaultCeilingHeightMeters = definition.Metadata.DefaultCeilingHeightMeters,
                MinX = definition.Metadata.MinX,
                MinZ = definition.Metadata.MinZ,
                MaxX = definition.Metadata.MaxX,
                MaxZ = definition.Metadata.MaxZ,
                StartX = definition.Metadata.StartX,
                StartZ = definition.Metadata.StartZ,
                StartHeadingDegrees = definition.Metadata.StartHeadingDegrees,
                StartHeading = definition.Metadata.StartHeading,
                SurfaceResolutionMeters = definition.Metadata.SurfaceResolutionMeters,
                RootDirectory = mapRoot,
                MapPath = path
            };

            foreach (var sector in definition.Sectors)
                map.AddSector(sector);
            foreach (var area in definition.Areas)
                map.AddArea(area);
            foreach (var geometry in definition.Geometries)
                map.AddGeometry(geometry);
            foreach (var volume in definition.Volumes)
                map.AddVolume(volume);
            foreach (var portal in definition.Portals)
                map.AddPortal(portal);
            foreach (var link in definition.Links)
                map.AddLink(link);
            foreach (var beacon in definition.Beacons)
                map.AddBeacon(beacon);
            foreach (var marker in definition.Markers)
                map.AddMarker(marker);
            foreach (var approach in definition.Approaches)
                map.AddApproach(approach);
            foreach (var branch in definition.Branches)
                map.AddBranch(branch);
            foreach (var material in definition.Materials)
                map.AddMaterial(material);
            foreach (var room in definition.Rooms)
                map.AddRoom(room);
            foreach (var profile in definition.Profiles)
                map.AddProfile(profile);
            foreach (var bank in definition.Banks)
                map.AddBank(bank);
            foreach (var surface in definition.Surfaces)
                map.AddSurface(surface);
            foreach (var source in definition.SoundSources)
                map.AddSoundSource(source);

            AddExplicitWalls(map, definition);
            AddAutoWalls(map, definition);
            AddPresetMaterials(map, definition);
            AddPresetRooms(map, definition);
            ApplyStartFromAreas(map, definition);
            ApplyFinishFromAreas(map, definition);

            return map;
        }

        private static string ResolvePath(string nameOrPath)
        {
            if (string.IsNullOrWhiteSpace(nameOrPath))
                return nameOrPath;
            if (nameOrPath.IndexOfAny(new[] { '\\', '/' }) >= 0)
                return nameOrPath;
            if (!Path.HasExtension(nameOrPath))
                return Path.Combine(AssetPaths.Root, "Tracks", nameOrPath, "track" + MapExtension);
            return Path.Combine(AssetPaths.Root, "Tracks", nameOrPath);
        }

        private static void AddPresetMaterials(TrackMap map, TrackMapDefinition definition)
        {
            if (map == null || definition == null)
                return;

            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var material in map.Materials)
            {
                if (material == null)
                    continue;
                existing.Add(material.Id);
            }

            foreach (var area in map.Areas)
            {
                if (area == null || string.IsNullOrWhiteSpace(area.MaterialId))
                    continue;
                var id = area.MaterialId!.Trim();
                if (existing.Contains(id))
                    continue;
                if (TrackMaterialLibrary.TryGetPreset(id, out var preset))
                {
                    map.AddMaterial(preset);
                    existing.Add(id);
                }
            }

            foreach (var wall in map.Walls)
            {
                if (wall == null || string.IsNullOrWhiteSpace(wall.MaterialId))
                    continue;
                var id = wall.MaterialId!.Trim();
                if (existing.Contains(id))
                    continue;
                if (TrackMaterialLibrary.TryGetPreset(id, out var preset))
                {
                    map.AddMaterial(preset);
                    existing.Add(id);
                }
            }
        }

        private static void AddPresetRooms(TrackMap map, TrackMapDefinition definition)
        {
            if (map == null || definition == null)
                return;

            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var room in map.Rooms)
            {
                if (room == null)
                    continue;
                existing.Add(room.Id);
            }

            foreach (var area in map.Areas)
            {
                if (area == null || string.IsNullOrWhiteSpace(area.RoomId))
                    continue;
                var id = area.RoomId!.Trim();
                if (existing.Contains(id))
                    continue;
                if (TrackRoomLibrary.TryGetPreset(id, out var preset))
                {
                    map.AddRoom(preset);
                    existing.Add(id);
                }
            }
        }

        private static void AddExplicitWalls(TrackMap map, TrackMapDefinition definition)
        {
            if (map == null || definition == null)
                return;

            foreach (var wall in definition.Walls)
            {
                if (wall == null)
                    continue;
                var resolvedMaterialId = string.IsNullOrWhiteSpace(wall.MaterialId)
                    ? map.DefaultMaterialId
                    : wall.MaterialId!;
                var collisionMaterial = ResolveCollisionMaterial(map, resolvedMaterialId);
                var resolvedWall = new TrackWallDefinition(
                    wall.Id,
                    wall.GeometryId,
                    wall.WidthMeters,
                    wall.ElevationMeters,
                    collisionMaterial,
                    wall.CollisionMode,
                    wall.Name,
                    wall.Metadata,
                    wall.HeightMeters,
                    resolvedMaterialId);
                map.AddWall(resolvedWall);
            }
        }

        private static void AddAutoWalls(TrackMap map, TrackMapDefinition definition)
        {
            if (map == null || definition == null)
                return;

            var areaManager = new TrackAreaManager(map.Geometries, map.Areas, map.Volumes);
            var geometryIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var wallIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var geometry in map.Geometries)
            {
                if (geometry != null && !string.IsNullOrWhiteSpace(geometry.Id))
                    geometryIds.Add(geometry.Id);
            }
            foreach (var wall in map.Walls)
            {
                if (wall != null && !string.IsNullOrWhiteSpace(wall.Id))
                    wallIds.Add(wall.Id);
            }

            foreach (var area in map.Areas)
            {
                if (area == null || area.Metadata == null || area.Metadata.Count == 0)
                    continue;

                if (!TryGetBool(area.Metadata, out var autoWallsEnabled, "auto_walls", "auto_wall", "walls_auto", "auto_wall_enabled") ||
                    !autoWallsEnabled)
                    continue;

                if (!areaManager.TryGetGeometry(area.GeometryId, out var geometry))
                    continue;

                if (geometry.Type != GeometryType.Polygon)
                    continue;

                var edges = ParseWallEdges(area.Metadata);
                if (edges.Count == 0)
                    continue;

                var width = TryGetFloat(area.Metadata, out var widthValue, "wall_width", "wall_thickness", "wall_size")
                    ? Math.Max(0f, widthValue)
                    : 1f;

                var wallHeight = ResolveWallHeight(area);
                if (TryGetFloat(area.Metadata, out var wallHeightValue, "wall_height", "wall_height_m"))
                    wallHeight = Math.Max(0f, wallHeightValue);

                var collisionMode = TrackWallCollisionMode.Block;
                if (TryGetString(area.Metadata, out var collisionRaw, "wall_collision", "wall_collision_mode", "collision", "collision_mode", "wall_mode") &&
                    TryParseWallCollision(collisionRaw, out var parsedMode))
                    collisionMode = parsedMode;

                var resolvedMaterialId = ResolveAutoWallMaterialId(map, area);
                var collisionMaterial = ResolveCollisionMaterial(map, resolvedMaterialId);

                var points2D = ProjectToXZ(geometry.Points);
                var pointCount = NormalizePolygonPointCount(points2D);
                if (pointCount < 3)
                    continue;

                var isCounterClockwise = ComputeSignedArea(points2D, pointCount) > 0f;
                var sampleDistance = Math.Max(0.25f, Math.Min(1.0f, width > 0f ? width * 0.5f : 0.5f));

                var edgeIndex = 0;
                for (var i = 0; i < pointCount; i++)
                {
                    var nextIndex = i + 1;
                    if (nextIndex >= pointCount)
                        nextIndex = 0;

                    var a = points2D[i];
                    var b = points2D[nextIndex];
                    if (Vector2.DistanceSquared(a, b) <= 0.0001f)
                        continue;

                    var normal = ComputeOutwardNormal(a, b, isCounterClockwise);
                    var direction = ResolveDirection(normal);
                    if (!edges.Contains(direction))
                        continue;

                    var midpoint = (a + b) * 0.5f;
                    var sample = midpoint + (normal * sampleDistance);
                    if (areaManager.Contains(area, sample))
                    {
                        normal = -normal;
                        sample = midpoint + (normal * sampleDistance);
                    }

                    if (IsTouchingTrackArea(areaManager, area, sample))
                        continue;

                    var geometryId = CreateUniqueId(geometryIds, $"auto_wall_geom_{area.Id}_{edgeIndex}");
                    var wallId = CreateUniqueId(wallIds, $"auto_wall_{area.Id}_{edgeIndex}");

                    var wallGeometry = new GeometryDefinition(
                        geometryId,
                        GeometryType.Polyline,
                        new[]
                        {
                            new Vector3(a.X, area.ElevationMeters, a.Y),
                            new Vector3(b.X, area.ElevationMeters, b.Y)
                        });
                    map.AddGeometry(wallGeometry);

                    var wall = new TrackWallDefinition(
                        wallId,
                        geometryId,
                        width,
                        area.ElevationMeters,
                        collisionMaterial,
                        collisionMode,
                        name: null,
                        metadata: null,
                        heightMeters: wallHeight,
                        materialId: resolvedMaterialId);
                    map.AddWall(wall);
                    edgeIndex++;
                }
            }
        }

        private static int NormalizePolygonPointCount(IReadOnlyList<Vector2> points)
        {
            if (points == null)
                return 0;
            var count = points.Count;
            if (count < 3)
                return count;
            if (Vector2.DistanceSquared(points[0], points[count - 1]) <= 0.0001f)
                count--;
            return count;
        }

        private static float ComputeSignedArea(IReadOnlyList<Vector2> points, int count)
        {
            if (points == null || count < 3)
                return 0f;
            var area = 0f;
            for (var i = 0; i < count; i++)
            {
                var j = i + 1;
                if (j >= count)
                    j = 0;
                var a = points[i];
                var b = points[j];
                area += (a.X * b.Y) - (b.X * a.Y);
            }
            return area * 0.5f;
        }

        private static Vector2 ComputeOutwardNormal(Vector2 a, Vector2 b, bool isCounterClockwise)
        {
            var dx = b.X - a.X;
            var dz = b.Y - a.Y;
            var normal = isCounterClockwise ? new Vector2(dz, -dx) : new Vector2(-dz, dx);
            var lengthSq = normal.LengthSquared();
            if (lengthSq <= 0.000001f)
                return Vector2.Zero;
            return normal / (float)Math.Sqrt(lengthSq);
        }

        private static MapDirection ResolveDirection(Vector2 normal)
        {
            var absX = Math.Abs(normal.X);
            var absZ = Math.Abs(normal.Y);
            if (absX >= absZ)
                return normal.X >= 0f ? MapDirection.East : MapDirection.West;
            return normal.Y >= 0f ? MapDirection.North : MapDirection.South;
        }

        private static bool IsTouchingTrackArea(
            TrackAreaManager areaManager,
            TrackAreaDefinition currentArea,
            Vector2 sample)
        {
            foreach (var area in areaManager.Areas)
            {
                if (area == null || ReferenceEquals(area, currentArea))
                    continue;
                if (!IsTrackArea(area))
                    continue;
                if (areaManager.Contains(area, sample))
                    return true;
            }
            return false;
        }

        private static bool IsTrackArea(TrackAreaDefinition area)
        {
            if (area == null)
                return false;
            if (area.Type == TrackAreaType.Boundary || area.Type == TrackAreaType.OffTrack)
                return false;
            if (area.Type == TrackAreaType.Start || area.Type == TrackAreaType.Finish ||
                area.Type == TrackAreaType.Checkpoint || area.Type == TrackAreaType.Intersection)
                return false;
            return true;
        }

        private static string ResolveAutoWallMaterialId(TrackMap map, TrackAreaDefinition area)
        {
            if (area?.Metadata != null &&
                TryGetString(area.Metadata, out var materialId, "wall_material_id", "wall_material"))
                return materialId;
            if (!string.IsNullOrWhiteSpace(area?.MaterialId))
                return area!.MaterialId!;
            return map.DefaultMaterialId;
        }

        private static float ResolveWallHeight(TrackAreaDefinition area)
        {
            if (area == null)
                return 2f;
            if (area.CeilingHeightMeters.HasValue)
                return Math.Max(0f, area.CeilingHeightMeters.Value - area.ElevationMeters);
            return Math.Max(0f, area.HeightMeters);
        }

        private static HashSet<MapDirection> ParseWallEdges(IReadOnlyDictionary<string, string> metadata)
        {
            var edges = new HashSet<MapDirection>();
            if (!TryGetString(metadata, out var raw, "wall_edges", "wall_edge", "wall_sides", "wall_side"))
            {
                edges.Add(MapDirection.North);
                edges.Add(MapDirection.East);
                edges.Add(MapDirection.South);
                edges.Add(MapDirection.West);
                return edges;
            }

            var tokens = raw.Split(new[] { ',', ';', '|', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var token in tokens)
            {
                var trimmed = token.Trim().ToLowerInvariant();
                if (trimmed.Length == 0)
                    continue;
                switch (trimmed)
                {
                    case "all":
                        edges.Add(MapDirection.North);
                        edges.Add(MapDirection.East);
                        edges.Add(MapDirection.South);
                        edges.Add(MapDirection.West);
                        return edges;
                    case "none":
                        return edges;
                    case "n":
                    case "north":
                        edges.Add(MapDirection.North);
                        break;
                    case "e":
                    case "east":
                        edges.Add(MapDirection.East);
                        break;
                    case "s":
                    case "south":
                        edges.Add(MapDirection.South);
                        break;
                    case "w":
                    case "west":
                        edges.Add(MapDirection.West);
                        break;
                    case "ns":
                    case "sn":
                        edges.Add(MapDirection.North);
                        edges.Add(MapDirection.South);
                        break;
                    case "ew":
                    case "we":
                        edges.Add(MapDirection.East);
                        edges.Add(MapDirection.West);
                        break;
                }
            }

            if (edges.Count == 0)
            {
                edges.Add(MapDirection.North);
                edges.Add(MapDirection.East);
                edges.Add(MapDirection.South);
                edges.Add(MapDirection.West);
            }

            return edges;
        }

        private static string CreateUniqueId(HashSet<string> existing, string baseId)
        {
            if (existing == null)
                throw new ArgumentNullException(nameof(existing));
            if (string.IsNullOrWhiteSpace(baseId))
                baseId = "auto_wall";

            var candidate = baseId.Trim();
            var suffix = 1;
            while (existing.Contains(candidate))
            {
                candidate = $"{baseId}_{suffix}";
                suffix++;
            }
            existing.Add(candidate);
            return candidate;
        }

        private static IReadOnlyList<Vector2> ProjectToXZ(IReadOnlyList<Vector3> points)
        {
            if (points == null || points.Count == 0)
                return Array.Empty<Vector2>();

            var projected = new List<Vector2>(points.Count);
            foreach (var point in points)
                projected.Add(new Vector2(point.X, point.Z));
            return projected;
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

        private static bool TryGetBool(
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
                if (TryParseBool(raw, out value))
                    return true;
            }

            return false;
        }

        private static bool TryParseBool(string raw, out bool value)
        {
            value = false;
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            var trimmed = raw.Trim().ToLowerInvariant();
            switch (trimmed)
            {
                case "1":
                case "true":
                case "yes":
                case "y":
                case "on":
                case "enable":
                case "enabled":
                    value = true;
                    return true;
                case "0":
                case "false":
                case "no":
                case "n":
                case "off":
                case "disable":
                case "disabled":
                    value = false;
                    return true;
            }

            return bool.TryParse(trimmed, out value);
        }

        private static TrackWallMaterial ResolveCollisionMaterial(TrackMap map, string? materialId)
        {
            if (map == null)
                return TrackWallMaterial.Hard;

            var trimmed = materialId?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                trimmed = map.DefaultMaterialId?.Trim();

            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                var material = FindMaterial(map, trimmed!);
                if (material == null && TrackMaterialLibrary.TryGetPreset(trimmed!, out var preset))
                {
                    map.AddMaterial(preset);
                    material = preset;
                }

                if (material != null)
                    return material.CollisionMaterial;
            }

            return TrackWallMaterial.Hard;
        }

        private static TrackMaterialDefinition? FindMaterial(TrackMap map, string materialId)
        {
            foreach (var material in map.Materials)
            {
                if (material != null && string.Equals(material.Id, materialId, StringComparison.OrdinalIgnoreCase))
                    return material;
            }
            return null;
        }

        private static void ApplyStartFromAreas(TrackMap map, TrackMapDefinition definition)
        {
            if (map == null || definition == null)
                return;

            TrackAreaDefinition? startArea = null;
            foreach (var area in definition.Areas)
            {
                if (area != null && area.Type == TrackAreaType.Start)
                {
                    startArea = area;
                    break;
                }
            }

            if (startArea == null)
                return;

            map.StartAreaId = startArea.Id;

            if (TryGetStartPosition(startArea, out var startPos) ||
                TryGetAreaCenter(definition, startArea, out startPos))
            {
                map.StartX = startPos.X;
                map.StartZ = startPos.Y;
            }

            if (TryGetStartHeading(startArea, out var headingDegrees))
            {
                map.StartHeadingDegrees = MapMovement.NormalizeDegrees(headingDegrees);
                map.StartHeading = MapMovement.ToCardinal(map.StartHeadingDegrees);
            }
        }

        private static void ApplyFinishFromAreas(TrackMap map, TrackMapDefinition definition)
        {
            if (map == null || definition == null)
                return;

            foreach (var area in definition.Areas)
            {
                if (area != null && area.Type == TrackAreaType.Finish)
                {
                    map.FinishAreaId = area.Id;
                    return;
                }
            }

            if (!string.IsNullOrWhiteSpace(map.StartAreaId))
            {
                map.FinishAreaId = map.StartAreaId;
                return;
            }

            foreach (var area in definition.Areas)
            {
                if (area != null && area.Type == TrackAreaType.Start)
                {
                    map.FinishAreaId = area.Id;
                    return;
                }
            }
        }

        private static bool TryGetStartPosition(TrackAreaDefinition area, out System.Numerics.Vector2 position)
        {
            position = default;
            if (area?.Metadata == null || area.Metadata.Count == 0)
                return false;

            if (!TryGetFloat(area.Metadata, out var x, "start_x", "spawn_x", "x") ||
                !TryGetFloat(area.Metadata, out var z, "start_z", "spawn_z", "z"))
                return false;

            position = new System.Numerics.Vector2(x, z);
            return true;
        }

        private static bool TryGetStartHeading(TrackAreaDefinition area, out float headingDegrees)
        {
            headingDegrees = 0f;
            if (area?.Metadata == null || area.Metadata.Count == 0)
                return false;

            if (!TryGetString(area.Metadata, out var raw, "start_heading", "heading", "grid_heading", "orientation"))
                return false;

            if (TryParseHeading(raw, out var parsed))
            {
                headingDegrees = MapMovement.HeadingFromDirection(parsed);
                return true;
            }

            if (TryParseDegrees(raw, out var degrees))
            {
                headingDegrees = MapMovement.NormalizeDegrees(degrees);
                return true;
            }

            return false;
        }

        private static bool TryGetAreaCenter(TrackMapDefinition definition, TrackAreaDefinition area, out System.Numerics.Vector2 center)
        {
            center = default;
            if (definition == null || area == null || string.IsNullOrWhiteSpace(area.GeometryId))
                return false;

            GeometryDefinition? geometry = null;
            foreach (var candidate in definition.Geometries)
            {
                if (candidate != null && string.Equals(candidate.Id, area.GeometryId, StringComparison.OrdinalIgnoreCase))
                {
                    geometry = candidate;
                    break;
                }
            }

            if (geometry == null || geometry.Points == null || geometry.Points.Count == 0)
                return false;

            float sumX = 0f;
            float sumZ = 0f;
            foreach (var point in geometry.Points)
            {
                sumX += point.X;
                sumZ += point.Z;
            }

            center = new System.Numerics.Vector2(sumX / geometry.Points.Count, sumZ / geometry.Points.Count);
            return true;
        }

        private static bool TryGetFloat(
            IReadOnlyDictionary<string, string> metadata,
            out float value,
            params string[] keys)
        {
            value = 0f;
            if (metadata == null || metadata.Count == 0)
                return false;
            foreach (var key in keys)
            {
                if (!metadata.TryGetValue(key, out var raw))
                    continue;
                if (float.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value))
                    return true;
            }
            return false;
        }

        private static bool TryGetString(
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
                    value = raw.Trim();
                    return true;
                }
            }
            return false;
        }

        private static bool TryParseHeading(string raw, out MapDirection heading)
        {
            heading = MapDirection.North;
            if (string.IsNullOrWhiteSpace(raw))
                return false;
            switch (raw.Trim().ToLowerInvariant())
            {
                case "n":
                case "north":
                    heading = MapDirection.North;
                    return true;
                case "e":
                case "east":
                    heading = MapDirection.East;
                    return true;
                case "s":
                case "south":
                    heading = MapDirection.South;
                    return true;
                case "w":
                case "west":
                    heading = MapDirection.West;
                    return true;
            }
            return false;
        }

        private static bool TryParseDegrees(string raw, out float degrees)
        {
            return float.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out degrees);
        }
    }
}
