using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using TopSpeed.Core;
using TopSpeed.Data;
using TopSpeed.Tracks.Materials;
using TopSpeed.Tracks.Rooms;
using TopSpeed.Tracks.Areas;
using TopSpeed.Tracks.Topology;
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
                path = nameOrPath;
                return File.Exists(path) && LooksLikeMap(path);
            }

            if (!Path.HasExtension(nameOrPath))
            {
                path = Path.Combine(AssetPaths.Root, "Tracks", nameOrPath + MapExtension);
                return File.Exists(path);
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
                StartX = definition.Metadata.StartX,
                StartZ = definition.Metadata.StartZ,
                StartHeadingDegrees = definition.Metadata.StartHeadingDegrees,
                StartHeading = definition.Metadata.StartHeading
            };

            foreach (var sector in definition.Sectors)
                map.AddSector(sector);
            foreach (var area in definition.Areas)
                map.AddArea(area);
            foreach (var shape in definition.Shapes)
                map.AddShape(shape);
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

            AddSafeZoneRing(map, definition.Metadata);
            AddOuterRing(map, definition.Metadata);
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
                return Path.Combine(AssetPaths.Root, "Tracks", nameOrPath + MapExtension);
            return Path.Combine(AssetPaths.Root, "Tracks", nameOrPath);
        }

        private static void AddSafeZoneRing(TrackMap map, TrackMapMetadata metadata)
        {
            if (map == null || metadata == null)
                return;

            var ringMeters = metadata.SafeZoneRingMeters;
            if (ringMeters <= 0f)
                return;
            if (HasExplicitSafeZone(map))
                return;

            if (!TryGetDrivableBounds(map, out var minX, out var minZ, out var maxX, out var maxZ))
                return;

            var innerMinX = minX;
            var innerMaxX = maxX;
            var innerMinZ = minZ;
            var innerMaxZ = maxZ;

            var name = string.IsNullOrWhiteSpace(metadata.SafeZoneName) ? "Safe zone" : metadata.SafeZoneName!;
            var materialId = metadata.SafeZoneMaterialId;
            var noise = metadata.SafeZoneNoise;
            var flags = TrackAreaFlags.SafeZone;

            if (!metadata.BaseHeightMeters.HasValue || !metadata.DefaultAreaHeightMeters.HasValue)
                return;

            AddRingShapeArea(
                map,
                "__safe_zone",
                innerMinX,
                innerMinZ,
                innerMaxX - innerMinX,
                innerMaxZ - innerMinZ,
                ringMeters,
                name,
                materialId,
                noise,
                metadata.BaseHeightMeters.Value,
                metadata.DefaultAreaHeightMeters.Value,
                metadata.DefaultCeilingHeightMeters,
                TrackAreaType.SafeZone,
                flags);
        }

        private static void AddOuterRing(TrackMap map, TrackMapMetadata metadata)
        {
            if (map == null || metadata == null)
                return;

            var ringMeters = metadata.OuterRingMeters;
            if (ringMeters <= 0f)
                return;

            if (!TryGetDrivableBounds(map, out var minX, out var minZ, out var maxX, out var maxZ))
                return;

            var innerMinX = minX;
            var innerMaxX = maxX;
            var innerMinZ = minZ;
            var innerMaxZ = maxZ;

            var name = string.IsNullOrWhiteSpace(metadata.OuterRingName) ? "Outer ring" : metadata.OuterRingName!;
            var materialId = metadata.OuterRingMaterialId;
            var noise = metadata.OuterRingNoise;
            var flags = metadata.OuterRingFlags;
            var areaType = metadata.OuterRingType;

            if (!metadata.BaseHeightMeters.HasValue || !metadata.DefaultAreaHeightMeters.HasValue)
                return;

            AddRingShapeArea(
                map,
                "__outer_ring",
                innerMinX,
                innerMinZ,
                innerMaxX - innerMinX,
                innerMaxZ - innerMinZ,
                ringMeters,
                name,
                materialId,
                noise,
                metadata.BaseHeightMeters.Value,
                metadata.DefaultAreaHeightMeters.Value,
                metadata.DefaultCeilingHeightMeters,
                areaType,
                flags);
        }

        private static void AddAutoWalls(TrackMap map, TrackMapDefinition definition)
        {
            if (map == null || definition == null)
                return;

            if (definition.Areas.Count == 0)
                return;

            var shapesById = new Dictionary<string, ShapeDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (var shape in map.Shapes)
            {
                if (shape == null)
                    continue;
                shapesById[shape.Id] = shape;
            }

            var areaManager = map.BuildAreaManager();
            var stepMeters = Math.Max(0.5f, map.CellSizeMeters);
            var edgeProbe = Math.Max(0.01f, stepMeters * 0.05f);

            foreach (var area in definition.Areas)
            {
                if (area == null || area.Metadata == null || area.Metadata.Count == 0)
                    continue;
                if (!TryGetMetadataBool(area.Metadata, out var enabled, "auto_walls", "auto_wall", "walls_auto", "auto_wall_enabled"))
                    continue;
                if (!enabled)
                    continue;
                if (!shapesById.TryGetValue(area.ShapeId, out var shape))
                    continue;
                if (shape.Type != ShapeType.Rectangle)
                    continue;

                var edges = ParseWallEdges(area.Metadata);
                if (edges == WallEdges.None)
                    continue;

                var wallWidth = TryGetMetadataFloat(area.Metadata, out var widthValue, "wall_width", "wall_thickness", "wall_size")
                    ? Math.Max(0.1f, widthValue)
                    : 2f;
                var defaultWallHeight = TryGetMetadataFloat(area.Metadata, out var heightValue, "wall_height", "wall_height_m")
                    ? Math.Max(0f, heightValue)
                    : 2f;
                var ceilingHeight = area.CeilingHeightMeters ?? (area.ElevationMeters + area.HeightMeters);
                var wallHeight = area.CeilingHeightMeters.HasValue
                    ? Math.Max(0f, ceilingHeight - area.ElevationMeters)
                    : defaultWallHeight;
                var wallMaterialId = TryGetMetadataValue(area.Metadata, out var acousticValue, "wall_material_id")
                    ? acousticValue
                    : area.MaterialId;
                var material = ResolveCollisionMaterial(map, wallMaterialId);
                var collision = ParseWallCollision(area.Metadata);

                var minX = Math.Min(shape.X, shape.X + shape.Width);
                var maxX = Math.Max(shape.X, shape.X + shape.Width);
                var minZ = Math.Min(shape.Z, shape.Z + shape.Height);
                var maxZ = Math.Max(shape.Z, shape.Z + shape.Height);
                if ((edges & WallEdges.North) != 0)
                    AddWallEdgeSegments(map, shapesById, area, areaManager, stepMeters, edgeProbe, "north",
                        minX, maxX, maxZ, wallWidth, wallHeight, wallMaterialId, material, collision);
                if ((edges & WallEdges.South) != 0)
                    AddWallEdgeSegments(map, shapesById, area, areaManager, stepMeters, edgeProbe, "south",
                        minX, maxX, minZ, wallWidth, wallHeight, wallMaterialId, material, collision);
                if ((edges & WallEdges.East) != 0)
                    AddWallEdgeSegments(map, shapesById, area, areaManager, stepMeters, edgeProbe, "east",
                        minZ, maxZ, maxX, wallWidth, wallHeight, wallMaterialId, material, collision);
                if ((edges & WallEdges.West) != 0)
                    AddWallEdgeSegments(map, shapesById, area, areaManager, stepMeters, edgeProbe, "west",
                        minZ, maxZ, minX, wallWidth, wallHeight, wallMaterialId, material, collision);
            }
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
                    wall.ShapeId,
                    wall.WidthMeters,
                    collisionMaterial,
                    wall.CollisionMode,
                    wall.Name,
                    wall.Metadata,
                    wall.HeightMeters,
                    resolvedMaterialId);
                map.AddWall(resolvedWall);
            }
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

        private static void AddWallEdgeSegments(
            TrackMap map,
            Dictionary<string, ShapeDefinition> shapesById,
            TrackAreaDefinition area,
            TrackAreaManager areaManager,
            float stepMeters,
            float edgeProbe,
            string edge,
            float spanMin,
            float spanMax,
            float edgePosition,
            float wallWidth,
            float wallHeight,
            string? wallMaterialId,
            TrackWallMaterial material,
            TrackWallCollisionMode collision)
        {
            var length = spanMax - spanMin;
            if (length <= 0.01f)
                return;

            var steps = Math.Max(1, (int)Math.Ceiling(length / stepMeters));
            var segment = length / steps;
            var runStart = float.NaN;

            for (var i = 0; i < steps; i++)
            {
                var segStart = spanMin + (segment * i);
                var segEnd = (i == steps - 1) ? spanMax : segStart + segment;
                var segMid = (segStart + segEnd) * 0.5f;
                var adjacent = IsAdjacent(areaManager, edge, segStart, segEnd, segMid, edgePosition, edgeProbe);

                if (adjacent)
                {
                    if (!float.IsNaN(runStart))
                    {
                        AddWallRun(map, shapesById, area, edge, runStart, segStart, edgePosition, wallWidth, wallHeight, wallMaterialId, material, collision);
                        runStart = float.NaN;
                    }
                }
                else
                {
                    if (float.IsNaN(runStart))
                        runStart = segStart;
                }
            }

            if (!float.IsNaN(runStart))
                AddWallRun(map, shapesById, area, edge, runStart, spanMax, edgePosition, wallWidth, wallHeight, wallMaterialId, material, collision);
        }

        private static bool IsAdjacent(
            TrackAreaManager areaManager,
            string edge,
            float segStart,
            float segEnd,
            float segMid,
            float edgePosition,
            float edgeProbe)
        {
            var outside = edge switch
            {
                "north" => new Vector2(segMid, edgePosition + edgeProbe),
                "south" => new Vector2(segMid, edgePosition - edgeProbe),
                "east" => new Vector2(edgePosition + edgeProbe, segMid),
                "west" => new Vector2(edgePosition - edgeProbe, segMid),
                _ => new Vector2(segMid, edgePosition + edgeProbe)
            };

            if (areaManager.ContainsTrackArea(outside))
                return true;

            var startPoint = edge switch
            {
                "north" => new Vector2(segStart, edgePosition + edgeProbe),
                "south" => new Vector2(segStart, edgePosition - edgeProbe),
                "east" => new Vector2(edgePosition + edgeProbe, segStart),
                "west" => new Vector2(edgePosition - edgeProbe, segStart),
                _ => new Vector2(segStart, edgePosition + edgeProbe)
            };

            if (areaManager.ContainsTrackArea(startPoint))
                return true;

            var endPoint = edge switch
            {
                "north" => new Vector2(segEnd, edgePosition + edgeProbe),
                "south" => new Vector2(segEnd, edgePosition - edgeProbe),
                "east" => new Vector2(edgePosition + edgeProbe, segEnd),
                "west" => new Vector2(edgePosition - edgeProbe, segEnd),
                _ => new Vector2(segEnd, edgePosition + edgeProbe)
            };

            return areaManager.ContainsTrackArea(endPoint);
        }

        private static void AddWallRun(
            TrackMap map,
            Dictionary<string, ShapeDefinition> shapesById,
            TrackAreaDefinition area,
            string edge,
            float runStart,
            float runEnd,
            float edgePosition,
            float wallWidth,
            float wallHeight,
            string? wallMaterialId,
            TrackWallMaterial material,
            TrackWallCollisionMode collision)
        {
            var runLength = runEnd - runStart;
            if (runLength <= 0.01f)
                return;

            switch (edge)
            {
                case "north":
                    AddWallEdge(map, shapesById, area, edge, runStart, edgePosition, runLength, wallWidth, wallHeight, wallMaterialId, material, collision);
                    break;
                case "south":
                    AddWallEdge(map, shapesById, area, edge, runStart, edgePosition - wallWidth, runLength, wallWidth, wallHeight, wallMaterialId, material, collision);
                    break;
                case "east":
                    AddWallEdge(map, shapesById, area, edge, edgePosition, runStart, wallWidth, runLength, wallHeight, wallMaterialId, material, collision);
                    break;
                case "west":
                    AddWallEdge(map, shapesById, area, edge, edgePosition - wallWidth, runStart, wallWidth, runLength, wallHeight, wallMaterialId, material, collision);
                    break;
            }
        }

        [Flags]
        private enum WallEdges
        {
            None = 0,
            North = 1 << 0,
            South = 1 << 1,
            East = 1 << 2,
            West = 1 << 3,
            All = North | South | East | West
        }

        private static WallEdges ParseWallEdges(IReadOnlyDictionary<string, string> metadata)
        {
            if (!TryGetMetadataValue(metadata, out var raw, "wall_edges", "wall_sides", "walls", "edges"))
                return WallEdges.All;

            var trimmed = raw.Trim().ToLowerInvariant();
            if (trimmed == "none" || trimmed == "off")
                return WallEdges.None;
            if (trimmed == "all" || trimmed == "every")
                return WallEdges.All;

            var edges = WallEdges.None;
            var tokens = trimmed.Split(new[] { ',', '|', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var token in tokens)
            {
                switch (token)
                {
                    case "n":
                    case "north":
                    case "top":
                        edges |= WallEdges.North;
                        break;
                    case "s":
                    case "south":
                    case "bottom":
                        edges |= WallEdges.South;
                        break;
                    case "e":
                    case "east":
                    case "right":
                        edges |= WallEdges.East;
                        break;
                    case "w":
                    case "west":
                    case "left":
                        edges |= WallEdges.West;
                        break;
                }
            }
            return edges;
        }

        private static TrackWallCollisionMode ParseWallCollision(IReadOnlyDictionary<string, string> metadata)
        {
            if (!TryGetMetadataValue(metadata, out var raw, "wall_collision", "wall_collision_mode", "collision", "collision_mode", "wall_mode"))
                return TrackWallCollisionMode.Block;

            var trimmed = raw.Trim().ToLowerInvariant();
            switch (trimmed)
            {
                case "bounce":
                case "rebound":
                case "reflect":
                    return TrackWallCollisionMode.Bounce;
                case "pass":
                case "ignore":
                case "none":
                case "ghost":
                    return TrackWallCollisionMode.Pass;
                default:
                    return TrackWallCollisionMode.Block;
            }
        }

        private static void AddWallEdge(
            TrackMap map,
            Dictionary<string, ShapeDefinition> shapesById,
            TrackAreaDefinition area,
            string edge,
            float x,
            float z,
            float width,
            float height,
            float wallHeight,
            string? wallMaterialId,
            TrackWallMaterial material,
            TrackWallCollisionMode collision)
        {
            var shapeBase = $"__auto_wall_{area.Id}_{edge}_shape";
            var wallBase = $"__auto_wall_{area.Id}_{edge}";
            var shapeId = shapeBase;
            var wallId = wallBase;
            var suffix = 1;
            while (shapesById.ContainsKey(shapeId))
            {
                shapeId = $"{shapeBase}_{suffix}";
                wallId = $"{wallBase}_{suffix}";
                suffix++;
            }

            var wallName = string.IsNullOrWhiteSpace(area.Name) ? null : $"{area.Name} {edge} wall";
            var shape = new ShapeDefinition(shapeId, ShapeType.Rectangle, x, z, width, height);
            map.AddShape(shape);
            map.AddWall(new TrackWallDefinition(wallId, shapeId, 0f, material, collision, wallName, null, wallHeight, wallMaterialId));
            shapesById[shapeId] = shape;
        }

        private static bool TryGetMetadataBool(IReadOnlyDictionary<string, string> metadata, out bool value, params string[] keys)
        {
            value = false;
            if (!TryGetMetadataValue(metadata, out var raw, keys))
                return false;
            if (bool.TryParse(raw, out value))
                return true;
            var trimmed = raw.Trim().ToLowerInvariant();
            if (trimmed == "1" || trimmed == "yes" || trimmed == "y" || trimmed == "on" || trimmed == "true")
            {
                value = true;
                return true;
            }
            if (trimmed == "0" || trimmed == "no" || trimmed == "n" || trimmed == "off" || trimmed == "false")
            {
                value = false;
                return true;
            }
            return false;
        }

        private static bool TryGetMetadataFloat(IReadOnlyDictionary<string, string> metadata, out float value, params string[] keys)
        {
            value = 0f;
            if (!TryGetMetadataValue(metadata, out var raw, keys))
                return false;
            return float.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        private static bool TryGetMetadataValue(IReadOnlyDictionary<string, string> metadata, out string value, params string[] keys)
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

        private static bool HasExplicitSafeZone(TrackMap map)
        {
            if (map == null)
                return false;
            foreach (var area in map.Areas)
            {
                if (area == null)
                    continue;
                if (area.Type == TrackAreaType.SafeZone || (area.Flags & TrackAreaFlags.SafeZone) != 0)
                    return true;
            }
            return false;
        }

        private static void AddRingShapeArea(
            TrackMap map,
            string idPrefix,
            float innerMinX,
            float innerMinZ,
            float innerWidth,
            float innerHeight,
            float ringWidth,
            string name,
            string materialId,
            TrackNoise noise,
            float elevationMeters,
            float heightMeters,
            float? ceilingHeightMeters,
            TrackAreaType areaType,
            TrackAreaFlags flags)
        {
            if (ringWidth <= 0f || innerWidth <= 0f || innerHeight <= 0f)
                return;

            var shapeId = idPrefix + "_shape";
            var areaId = idPrefix + "_area";
            map.AddShape(new ShapeDefinition(shapeId, ShapeType.Ring, innerMinX, innerMinZ, innerWidth, innerHeight, ringWidth: ringWidth));
            map.AddArea(new TrackAreaDefinition(areaId, areaType, shapeId, elevationMeters, heightMeters, ceilingHeightMeters, null, null, name, materialId, noise, null, flags));
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
            if (definition == null || area == null || string.IsNullOrWhiteSpace(area.ShapeId))
                return false;

            ShapeDefinition? shape = null;
            foreach (var candidate in definition.Shapes)
            {
                if (candidate != null && string.Equals(candidate.Id, area.ShapeId, StringComparison.OrdinalIgnoreCase))
                {
                    shape = candidate;
                    break;
                }
            }

            if (shape == null)
                return false;

            switch (shape.Type)
            {
                case ShapeType.Rectangle:
                    var minX = Math.Min(shape.X, shape.X + shape.Width);
                    var maxX = Math.Max(shape.X, shape.X + shape.Width);
                    var minZ = Math.Min(shape.Z, shape.Z + shape.Height);
                    var maxZ = Math.Max(shape.Z, shape.Z + shape.Height);
                    center = new System.Numerics.Vector2((minX + maxX) * 0.5f, (minZ + maxZ) * 0.5f);
                    return true;
                case ShapeType.Circle:
                    center = new System.Numerics.Vector2(shape.X, shape.Z);
                    return true;
                case ShapeType.Ring:
                    if (shape.Radius > 0f)
                    {
                        center = new System.Numerics.Vector2(shape.X, shape.Z);
                        return true;
                    }
                    var rMinX = Math.Min(shape.X, shape.X + shape.Width);
                    var rMaxX = Math.Max(shape.X, shape.X + shape.Width);
                    var rMinZ = Math.Min(shape.Z, shape.Z + shape.Height);
                    var rMaxZ = Math.Max(shape.Z, shape.Z + shape.Height);
                    center = new System.Numerics.Vector2((rMinX + rMaxX) * 0.5f, (rMinZ + rMaxZ) * 0.5f);
                    return true;
                case ShapeType.Polygon:
                case ShapeType.Polyline:
                    if (shape.Points == null || shape.Points.Count == 0)
                        return false;
                    float sumX = 0f;
                    float sumZ = 0f;
                    foreach (var point in shape.Points)
                    {
                        sumX += point.X;
                        sumZ += point.Y;
                    }
                    center = new System.Numerics.Vector2(sumX / shape.Points.Count, sumZ / shape.Points.Count);
                    return true;
            }

            return false;
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

        private static bool TryGetDrivableBounds(TrackMap map, out float minX, out float minZ, out float maxX, out float maxZ)
        {
            minX = 0f;
            minZ = 0f;
            maxX = 0f;
            maxZ = 0f;
            var hasBounds = false;

            var shapesById = new Dictionary<string, ShapeDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (var shape in map.Shapes)
            {
                if (shape == null)
                    continue;
                shapesById[shape.Id] = shape;
            }

            var portalsById = new Dictionary<string, PortalDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (var portal in map.Portals)
            {
                if (portal == null)
                    continue;
                portalsById[portal.Id] = portal;
            }

            var hasDrivableAreas = HasDrivableAreas(map.Areas);
            if (hasDrivableAreas)
            {
                foreach (var area in map.Areas)
                {
                    if (area == null || IsOverlayArea(area) || IsNonDrivableArea(area))
                        continue;

                    if (!shapesById.TryGetValue(area.ShapeId, out var shape))
                        continue;

                    var expand = area.WidthMeters.GetValueOrDefault();
                    if (!TryGetShapeBoundsExpanded(shape, expand, out var sMinX, out var sMinZ, out var sMaxX, out var sMaxZ))
                        continue;

                    MergeBounds(ref hasBounds, ref minX, ref minZ, ref maxX, ref maxZ, sMinX, sMinZ, sMaxX, sMaxZ);
                }

                return hasBounds;
            }

            if (hasBounds)
                return true;

            return TryGetTopologyBounds(map, out minX, out minZ, out maxX, out maxZ);
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

        private static bool IsOverlayArea(TrackAreaDefinition area)
        {
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
            return area.Type == TrackAreaType.Boundary || area.Type == TrackAreaType.OffTrack;
        }

        private static void MergeBounds(
            ref bool hasBounds,
            ref float minX,
            ref float minZ,
            ref float maxX,
            ref float maxZ,
            float sMinX,
            float sMinZ,
            float sMaxX,
            float sMaxZ)
        {
            if (!hasBounds)
            {
                minX = sMinX;
                minZ = sMinZ;
                maxX = sMaxX;
                maxZ = sMaxZ;
                hasBounds = true;
                return;
            }

            if (sMinX < minX) minX = sMinX;
            if (sMinZ < minZ) minZ = sMinZ;
            if (sMaxX > maxX) maxX = sMaxX;
            if (sMaxZ > maxZ) maxZ = sMaxZ;
        }

        private static bool TryGetShapeBoundsExpanded(
            ShapeDefinition shape,
            float expand,
            out float minX,
            out float minZ,
            out float maxX,
            out float maxZ)
        {
            if (!TryGetShapeBounds(shape, out minX, out minZ, out maxX, out maxZ))
                return false;
            if (expand <= 0f)
                return true;
            minX -= expand;
            minZ -= expand;
            maxX += expand;
            maxZ += expand;
            return true;
        }

        private static bool TryGetTopologyBounds(TrackMap map, out float minX, out float minZ, out float maxX, out float maxZ)
        {
            minX = 0f;
            minZ = 0f;
            maxX = 0f;
            maxZ = 0f;
            var hasBounds = false;

            if (map.Shapes.Count > 0)
            {
                foreach (var shape in map.Shapes)
                {
                    if (shape == null || !TryGetShapeBounds(shape, out var sMinX, out var sMinZ, out var sMaxX, out var sMaxZ))
                        continue;
                    if (!hasBounds)
                    {
                        minX = sMinX;
                        minZ = sMinZ;
                        maxX = sMaxX;
                        maxZ = sMaxZ;
                        hasBounds = true;
                    }
                    else
                    {
                        if (sMinX < minX) minX = sMinX;
                        if (sMinZ < minZ) minZ = sMinZ;
                        if (sMaxX > maxX) maxX = sMaxX;
                        if (sMaxZ > maxZ) maxZ = sMaxZ;
                    }
                }
            }

            if (map.Portals.Count > 0)
            {
                foreach (var portal in map.Portals)
                {
                    if (portal == null)
                        continue;
                    if (!hasBounds)
                    {
                        minX = portal.X;
                        maxX = portal.X;
                        minZ = portal.Z;
                        maxZ = portal.Z;
                        hasBounds = true;
                        continue;
                    }
                    if (portal.X < minX) minX = portal.X;
                    if (portal.X > maxX) maxX = portal.X;
                    if (portal.Z < minZ) minZ = portal.Z;
                    if (portal.Z > maxZ) maxZ = portal.Z;
                }
            }

            return hasBounds;
        }

        private static bool TryGetShapeBounds(ShapeDefinition shape, out float minX, out float minZ, out float maxX, out float maxZ)
        {
            minX = 0f;
            minZ = 0f;
            maxX = 0f;
            maxZ = 0f;

            if (shape == null)
                return false;

            switch (shape.Type)
            {
                case ShapeType.Rectangle:
                    minX = Math.Min(shape.X, shape.X + shape.Width);
                    maxX = Math.Max(shape.X, shape.X + shape.Width);
                    minZ = Math.Min(shape.Z, shape.Z + shape.Height);
                    maxZ = Math.Max(shape.Z, shape.Z + shape.Height);
                    return true;
                case ShapeType.Circle:
                    minX = shape.X - shape.Radius;
                    maxX = shape.X + shape.Radius;
                    minZ = shape.Z - shape.Radius;
                    maxZ = shape.Z + shape.Radius;
                    return true;
                case ShapeType.Ring:
                    if (shape.Radius > 0f)
                    {
                        var outer = Math.Abs(shape.Radius) + Math.Abs(shape.RingWidth);
                        minX = shape.X - outer;
                        maxX = shape.X + outer;
                        minZ = shape.Z - outer;
                        maxZ = shape.Z + outer;
                        return true;
                    }
                    var ringMinX = Math.Min(shape.X, shape.X + shape.Width) - Math.Abs(shape.RingWidth);
                    var ringMaxX = Math.Max(shape.X, shape.X + shape.Width) + Math.Abs(shape.RingWidth);
                    var ringMinZ = Math.Min(shape.Z, shape.Z + shape.Height) - Math.Abs(shape.RingWidth);
                    var ringMaxZ = Math.Max(shape.Z, shape.Z + shape.Height) + Math.Abs(shape.RingWidth);
                    minX = ringMinX;
                    maxX = ringMaxX;
                    minZ = ringMinZ;
                    maxZ = ringMaxZ;
                    return true;
                case ShapeType.Polygon:
                case ShapeType.Polyline:
                    if (shape.Points == null || shape.Points.Count == 0)
                        return false;
                    minX = float.MaxValue;
                    minZ = float.MaxValue;
                    maxX = float.MinValue;
                    maxZ = float.MinValue;
                    foreach (var point in shape.Points)
                    {
                        if (point.X < minX) minX = point.X;
                        if (point.X > maxX) maxX = point.X;
                        if (point.Y < minZ) minZ = point.Y;
                        if (point.Y > maxZ) maxZ = point.Y;
                    }
                    return true;
            }

            return false;
        }

        private static bool TryGetRingBounds(
            ShapeDefinition shape,
            out float minX,
            out float minZ,
            out float maxX,
            out float maxZ)
        {
            minX = 0f;
            minZ = 0f;
            maxX = 0f;
            maxZ = 0f;

            var ringWidth = Math.Abs(shape.RingWidth);
            if (ringWidth <= 0f)
                return false;

            if (shape.Radius > 0f)
            {
                var inner = Math.Abs(shape.Radius);
                var outer = inner + ringWidth;
                minX = shape.X - outer;
                maxX = shape.X + outer;
                minZ = shape.Z - outer;
                maxZ = shape.Z + outer;
                return true;
            }

            var innerMinX = Math.Min(shape.X, shape.X + shape.Width);
            var innerMaxX = Math.Max(shape.X, shape.X + shape.Width);
            var innerMinZ = Math.Min(shape.Z, shape.Z + shape.Height);
            var innerMaxZ = Math.Max(shape.Z, shape.Z + shape.Height);
            if (innerMaxX <= innerMinX || innerMaxZ <= innerMinZ)
                return false;

            minX = innerMinX - ringWidth;
            maxX = innerMaxX + ringWidth;
            minZ = innerMinZ - ringWidth;
            maxZ = innerMaxZ + ringWidth;
            return true;
        }
    }
}
