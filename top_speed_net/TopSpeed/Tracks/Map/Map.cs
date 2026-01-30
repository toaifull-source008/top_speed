using System;
using System.Collections.Generic;
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
    internal sealed class TrackMap
    {
        private readonly List<TrackSectorDefinition> _sectors;
        private readonly List<ShapeDefinition> _shapes;
        private readonly List<TrackAreaDefinition> _areas;
        private readonly List<PortalDefinition> _portals;
        private readonly List<LinkDefinition> _links;
        private readonly List<TrackBeaconDefinition> _beacons;
        private readonly List<TrackMarkerDefinition> _markers;
        private readonly List<TrackApproachDefinition> _approaches;
        private readonly List<TrackBranchDefinition> _branches;
        private readonly List<TrackWallDefinition> _walls;
        private readonly List<TrackMaterialDefinition> _materials;
        private readonly List<TrackRoomDefinition> _rooms;

        public TrackMap(string name, float cellSizeMeters)
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Track" : name.Trim();
            CellSizeMeters = Math.Max(0.1f, cellSizeMeters);
            _sectors = new List<TrackSectorDefinition>();
            _shapes = new List<ShapeDefinition>();
            _areas = new List<TrackAreaDefinition>();
            _portals = new List<PortalDefinition>();
            _links = new List<LinkDefinition>();
            _beacons = new List<TrackBeaconDefinition>();
            _markers = new List<TrackMarkerDefinition>();
            _approaches = new List<TrackApproachDefinition>();
            _branches = new List<TrackBranchDefinition>();
            _walls = new List<TopSpeed.Tracks.Walls.TrackWallDefinition>();
            _materials = new List<TrackMaterialDefinition>();
            _rooms = new List<TrackRoomDefinition>();
        }

        public string Name { get; }
        public float CellSizeMeters { get; }
        public IReadOnlyList<TrackSectorDefinition> Sectors => _sectors;
        public IReadOnlyList<TrackAreaDefinition> Areas => _areas;
        public IReadOnlyList<ShapeDefinition> Shapes => _shapes;
        public IReadOnlyList<PortalDefinition> Portals => _portals;
        public IReadOnlyList<LinkDefinition> Links => _links;
        public IReadOnlyList<TrackBeaconDefinition> Beacons => _beacons;
        public IReadOnlyList<TrackMarkerDefinition> Markers => _markers;
        public IReadOnlyList<TrackApproachDefinition> Approaches => _approaches;
        public IReadOnlyList<TrackBranchDefinition> Branches => _branches;
        public IReadOnlyList<TrackWallDefinition> Walls => _walls;
        public IReadOnlyList<TrackMaterialDefinition> Materials => _materials;
        public IReadOnlyList<TrackRoomDefinition> Rooms => _rooms;
        public TrackWeather Weather { get; set; } = TrackWeather.Sunny;
        public TrackAmbience Ambience { get; set; } = TrackAmbience.NoAmbience;
        public string DefaultMaterialId { get; set; } = "asphalt";
        public TrackNoise DefaultNoise { get; set; } = TrackNoise.NoNoise;
        public float DefaultWidthMeters { get; set; } = 12f;
        public float BaseHeightMeters { get; set; } = 0f;
        public float DefaultAreaHeightMeters { get; set; } = 5f;
        public float? DefaultCeilingHeightMeters { get; set; }
        public float StartX { get; set; }
        public float StartZ { get; set; }
        public float StartHeadingDegrees { get; set; } = 0f;
        public MapDirection StartHeading { get; set; } = MapDirection.North;
        public string? StartAreaId { get; set; }
        public string? FinishAreaId { get; set; }


        public void AddSector(TrackSectorDefinition sector)
        {
            if (sector == null)
                throw new ArgumentNullException(nameof(sector));
            _sectors.Add(sector);
        }

        public void AddShape(ShapeDefinition shape)
        {
            if (shape == null)
                throw new ArgumentNullException(nameof(shape));
            _shapes.Add(shape);
        }

        public void AddArea(TrackAreaDefinition area)
        {
            if (area == null)
                throw new ArgumentNullException(nameof(area));
            _areas.Add(area);
        }

        public void AddPortal(PortalDefinition portal)
        {
            if (portal == null)
                throw new ArgumentNullException(nameof(portal));
            _portals.Add(portal);
        }

        public void AddLink(LinkDefinition link)
        {
            if (link == null)
                throw new ArgumentNullException(nameof(link));
            _links.Add(link);
        }


        public void AddBeacon(TrackBeaconDefinition beacon)
        {
            if (beacon == null)
                throw new ArgumentNullException(nameof(beacon));
            _beacons.Add(beacon);
        }

        public void AddMarker(TrackMarkerDefinition marker)
        {
            if (marker == null)
                throw new ArgumentNullException(nameof(marker));
            _markers.Add(marker);
        }

        public void AddWall(TrackWallDefinition wall)
        {
            if (wall == null)
                throw new ArgumentNullException(nameof(wall));
            _walls.Add(wall);
        }

        public void AddRoom(TrackRoomDefinition room)
        {
            if (room == null)
                throw new ArgumentNullException(nameof(room));
            _rooms.Add(room);
        }

        public void AddMaterial(TrackMaterialDefinition material)
        {
            if (material == null)
                throw new ArgumentNullException(nameof(material));
            _materials.Add(material);
        }

        public void AddApproach(TrackApproachDefinition approach)
        {
            if (approach == null)
                throw new ArgumentNullException(nameof(approach));
            _approaches.Add(approach);
        }

        public void AddBranch(TrackBranchDefinition branch)
        {
            if (branch == null)
                throw new ArgumentNullException(nameof(branch));
            _branches.Add(branch);
        }

        public TrackAreaManager BuildAreaManager()
        {
            return new TrackAreaManager(_shapes, _areas);
        }


        public TrackPortalManager BuildPortalManager()
        {
            return new TrackPortalManager(_portals, _links);
        }

        public bool TryGetStartAreaBounds(out float minX, out float minZ, out float maxX, out float maxZ)
        {
            minX = 0f;
            minZ = 0f;
            maxX = 0f;
            maxZ = 0f;
            if (string.IsNullOrWhiteSpace(StartAreaId))
                return false;
            return TryGetAreaBounds(StartAreaId!, out minX, out minZ, out maxX, out maxZ);
        }

        public bool TryGetFinishAreaBounds(out float minX, out float minZ, out float maxX, out float maxZ)
        {
            minX = 0f;
            minZ = 0f;
            maxX = 0f;
            maxZ = 0f;
            if (string.IsNullOrWhiteSpace(FinishAreaId))
                return false;
            return TryGetAreaBounds(FinishAreaId!, out minX, out minZ, out maxX, out maxZ);
        }

        public bool TryGetAreaBounds(string areaId, out float minX, out float minZ, out float maxX, out float maxZ)
        {
            minX = 0f;
            minZ = 0f;
            maxX = 0f;
            maxZ = 0f;

            if (string.IsNullOrWhiteSpace(areaId))
                return false;

            TrackAreaDefinition? area = null;
            foreach (var candidate in _areas)
            {
                if (candidate != null && string.Equals(candidate.Id, areaId, StringComparison.OrdinalIgnoreCase))
                {
                    area = candidate;
                    break;
                }
            }
            if (area == null || string.IsNullOrWhiteSpace(area.ShapeId))
                return false;

            ShapeDefinition? shape = null;
            foreach (var candidate in _shapes)
            {
                if (candidate != null && string.Equals(candidate.Id, area.ShapeId, StringComparison.OrdinalIgnoreCase))
                {
                    shape = candidate;
                    break;
                }
            }
            if (shape == null)
                return false;

            return TryGetShapeBounds(shape, out minX, out minZ, out maxX, out maxZ);
        }

        public bool TryGetStartAreaDefinition(out TrackAreaDefinition area)
        {
            return TryGetAreaDefinition(StartAreaId, out area);
        }

        public bool TryGetFinishAreaDefinition(out TrackAreaDefinition area)
        {
            return TryGetAreaDefinition(FinishAreaId, out area);
        }

        private bool TryGetAreaDefinition(string? areaId, out TrackAreaDefinition area)
        {
            area = null!;
            if (string.IsNullOrWhiteSpace(areaId))
                return false;

            foreach (var candidate in _areas)
            {
                if (candidate != null && string.Equals(candidate.Id, areaId, StringComparison.OrdinalIgnoreCase))
                {
                    area = candidate;
                    return true;
                }
            }
            return false;
        }

        public TrackSectorManager BuildSectorManager()
        {
            return new TrackSectorManager(_sectors, BuildAreaManager(), BuildPortalManager());
        }

        public TrackApproachManager BuildApproachManager()
        {
            return new TrackApproachManager(_sectors, _approaches, BuildPortalManager());
        }

        public TrackSectorRuleManager BuildSectorRuleManager()
        {
            return new TrackSectorRuleManager(_sectors, BuildPortalManager());
        }

        public TrackBranchManager BuildBranchManager()
        {
            return new TrackBranchManager(_sectors, _approaches, _branches, BuildPortalManager());
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

    }
}
