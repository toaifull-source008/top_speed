# Tracks (audio-only authoring guide)

This guide explains how track files work in Top Speed using words and spatial descriptions rather than visuals. The goal is to help you picture a track as a set of walkable regions and boundaries in a flat map. Every track is two-dimensional and measured in meters. The horizontal axis is X and the vertical axis is Z. If you imagine standing on a sheet of paper, X is left to right and Z is front to back.

If you are new, read the Getting Started guide after this document. It assumes you already understand the terms used here and then walks through creating a simple track step by step.

## File structure

Each .tsm file is a list of sections. A section begins with a header like [meta], [shape], or [area]. Each section contains keys in the form name=value. The order does not matter, but keep it organized so the file is easy to read later.

The most important sections are meta, shape, area, sector, portal, branch, and guide. Beacons, markers, approaches, and walls are optional but useful. If you are new, focus on the first set.

## Meta settings

The meta section contains global settings such as the track name, default material, default noise, and start position. The start position uses X and Z plus a heading. A heading of 0 is north, 90 is east, 180 is south, and 270 is west. Any angle between these is allowed and useful for diagonals or gentle bends.

Meta can also define a safe zone ring or an outer ring. These create a band around the drivable track so players can recover when they leave the main road. If you do not want an automatic ring, leave the ring values at zero and define the safe zone as a normal area instead.

Meta can also define vertical defaults. Use base_height for the default floor level, default_area_height for the default vertical thickness of areas, and default_ceiling_height if you want a closed space by default. Areas can override any of these per section.

## Shapes

Shapes define geometry without meaning. They are the raw outlines you attach to areas, portals, guides, or walls. The most common shape is a rectangle because it is easy to author by coordinates. A rectangle is defined by a top-left corner (X,Z) plus width and height. If you say X 0, Z 0, width 40, height 30, the shape covers X 0 to 40 and Z 0 to 30.

Circles are defined by a center (X,Z) and a radius. Rings are like a circle or rectangle with a hollow middle, defined by the inner size plus a ring width. You can use a ring for a safe zone around a track or an outer boundary.

Polylines and polygons are lists of points. A polyline is a path you could trace with your finger. A polygon is a closed outline. Both are useful for irregular shapes or curved paths. A polyline by itself is not drivable; it becomes drivable only when an area attaches to it and supplies a width.

## Areas and width

Areas turn shapes into drivable or non-drivable regions. An area is what the car can actually occupy. If you attach an area to a rectangle or circle, the full shape is used. If you attach an area to a polyline, the area width becomes a corridor around that line. The width is measured across the corridor, not along the length, so a width of 20 means you can move 10 meters to each side of the line.

Area types control meaning. A normal zone is drivable. Start, finish, checkpoint, and intersection are special types that do not change movement but are used by race logic and guidance. Boundary and off-track are not drivable and are used to mark unsafe regions. Safe zones are drivable but often have different material or noise so you can hear that you left the main track.

Areas can also contain metadata. This is how you enable auto-walls for a specific edge, define grid placement for starting positions, or add guidance-related flags without cluttering the sector. A material is required on every area. The material defines both the surface sound (if a sound file exists) and the acoustic properties used later for occlusion and reverb.

Areas also define vertical space. Each area has a floor elevation and a height. Set elevation and height on the area, or define base_height and default_area_height in [meta] so areas inherit them. A ceiling can be optional; use ceiling_height (or ceiling) to define a closed space. If no ceiling is defined, the area is treated as open.

## Sectors and movement rules

Sectors are the behavior layer. A sector is linked to an area and tells the game what that space represents, such as straight, turn, or junction. Sectors do not define geometry; they define rules and semantics. This is how the game knows when to announce a turn or restrict movement in a direction.

Sectors can define allowed headings, entry and exit portals, and gameplay flags. This is important for blind players because it drives the audio guidance and whether a movement is considered valid inside that space.

## Portals

A portal is a point on the map with a heading. It represents a meaningful entry or exit direction for a sector. You can think of it as a doorway with a direction the car is expected to align to. Portals are used by guides and branches to determine where the player should enter, where they should leave, and what direction they should face.

If a turn has a clear entry and exit, define a portal at the start of the turn (entry) and another at the end (exit). If a straight connects to an intersection, define portals at each connection point so the game can reason about the available directions.

Portals accept an explicit heading in degrees. Use this when you want something other than the cardinal directions. This is important for gentle curves or diagonal paths because the audio guidance can then point in the exact intended direction.

## Branches

Branches describe possible exits from a sector. A branch references a sector and then lists one or more exit portals. This is how the game knows that an intersection offers multiple choices, or that a corridor can continue straight or turn.

Branches can include a preferred exit. That gives the guidance system a default suggestion, which is useful for racing lines or tutorials. Without branches, the game cannot reliably tell the player what options exist at a junction, and the audio guidance will be less precise.

## Guides and turn announcements

Guides are for turn announcements and beacons. A guide is linked to a sector and can include entry portals, exit portals, and headings. When you define a guide, the game can announce "turn north in 60 meters" and play a beacon that points toward the exit.

Guides can cover simple turns or complex junctions. If you define multiple entries and exits, the game can pair them and produce guidance for each approach. This is useful when a turn has more than one way in, or when you want early announcements for sharper turns.

## Beacons and markers

Beacons are sounds that help a player align to a direction. A beacon can be placed at a point, or it can be linked to a guide so it follows the turn direction. The beacon is spatial, so it appears to come from the direction the player should face. This is the most important cue for blind players to line up with a turn.

Markers are points of interest. They can be used for cues like "apex", "center", or "brake point". Markers do not affect movement; they exist only to provide audio signals.

## Safe zones and rings

Safe zones are areas that allow slower movement or recovery when you leave the main track. They are authored as areas too. A ring is a convenient way to add a band around a track, but it is still just an area under the hood. If you want a safe zone around your track, you can either define it manually or use the ring settings in meta to generate it.

## Materials

Materials are used later when Steam Audio simulation is enabled. They describe how sound is absorbed, scattered, and transmitted by a surface. You can assign a material to any area or wall. If you do not assign one, the engine will fall back to defaults.

You can use presets such as concrete, asphalt, brick, metal, wood, glass, plaster, fabric, grass, dirt, sand, gravel, snow, water, or rubber. If you need full control, define a custom material in a [material] section with numeric values and then reference it by id from an area or wall.

To define a custom material, create a section like [material: my_asphalt] and provide absorption, scattering, and transmission. Each value is a number between 0 and 1. You can provide one number or three numbers for low, mid, and high frequencies. Then use material=my_asphalt in an area or wall section. If a sound file named my_asphalt.wav exists under Sounds\\Legacy\\Materials, it will be used as the surface sound when driving on that area.

Materials can also extend a preset. For example, you can set preset=asphalt and then override only transmission or absorption to create a variant without redefining everything.

If you want walls made of a specific material to behave as soft or hard collisions, set collision or collision_material in the [material] section. Valid values are hard, soft, rubber, metal, concrete, wood, dirt, grass, or sand. Walls then use the collision behavior of their material automatically.

## Rooms (acoustic profiles)

Rooms describe how a space sounds beyond surface materials. A room profile controls reverb time, wetness, diffusion, air absorption, and other acoustic scaling. Areas can reference a room with room=id and can override any room values directly on the area. If an area has no ceiling, you can still use a room profile, but most tracks will choose an outdoor preset for open areas.

Room presets include outdoor_open, outdoor_urban, outdoor_forest, tunnel_short, tunnel_long, garage_small, garage_large, underpass, canyon, stadium_open, hall_medium, hall_large, room_small, room_medium, and room_large. You can create your own room section and optionally start from a preset, then override just the values you want to change.

Room keys are reverb_time, reverb_gain, hf_decay_ratio, early_reflections_gain, late_reverb_gain, diffusion, air_absorption, occlusion_scale, and transmission_scale. All values are 0 to 1 except reverb_time, which is seconds.

## Walls

Walls prevent movement into empty space. They are separate from areas. A wall has a shape and a width, and it blocks movement when the player reaches it. Auto-walls can be generated per area using metadata. They place a wall along specific edges and only where the edge borders empty space, so connections between areas stay open.

If you want a wall to block a specific gap, enable auto-walls on that edge and make sure the neighboring areas do not touch that edge. If you want a custom wall, define it explicitly with a wall shape. In either case, walls should be used to make the boundary feel intentional, so a blind player understands the road is blocked rather than simply "missing". For auto-walls, you can set wall_material_id in the area metadata to override the material used by the generated walls. If a wall does not specify material and you did not set default_material in [meta], the file is invalid.

Walls can also define a height. Height does not affect driving today, but it is important for acoustics and occlusion later, because taller walls block more sound. If a wall does not specify a material, it will use the default material (or the parent area material for auto-walls). Collision hardness is defined by the material itself (see the collision option in a [material] section).

## A concrete example you can imagine

Imagine a 250 by 250 meter square. The south straight is a rectangle from X 30 to 205 and Z 0 to 40. The east turn is a rectangle from X 205 to 250 and Z 0 to 60. The east straight is a rectangle from X 205 to 250 and Z 60 to 210. The north straight is a rectangle from X 40 to 210 and Z 210 to 250. The west straight is a rectangle from X 0 to 40 and Z 40 to 210. Corners are rectangles at each corner that connect these straights. This gives you a full loop that is easy to navigate with audio cues.

Portals would be placed at the start and end of each turn. For example, at the south-to-east turn, place one portal near X 205, Z 15 pointing east, and another near X 235, Z 15 pointing north. A guide linked to that turn would announce "turn north in 60 meters" and play a beacon pointing north.

## Common mistakes and how to fix them

If you hear "track boundary" inside a turn, the turn area is not wide enough or it does not touch the straight it should connect to. If a beacon seems to point in the wrong direction, check the portal headings or the guide exit heading. If you get stuck at a junction, check that the branch references the correct sector and exit portals.

If you can walk into empty space, add a wall along that edge or extend the neighboring area so the boundary is clear. If you cannot walk into a connection that should exist, check whether an auto-wall is blocking that edge.
