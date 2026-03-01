using System;
using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using WorldBuilder.Shared.Modules.Landscape.Models;

namespace WorldBuilder.Shared.Models {
    /// <summary>
    /// Represents a position in the world using landblock coordinates as the source of truth.
    /// Supports three coordinate systems:
    /// - Local: Landblock + Cell + offset within landblock
    /// - Global: World space coordinates
    /// - Map: NS/EW coordinates
    /// </summary>
    public class Position {
        // Map constants
        // Map constants
        private const int DefaultMapWidthInLandblocks = 255;
        private const int DefaultMapHeightInLandblocks = 255;
        private const float DefaultCellSizeInUnits = 24f;
        private const int LandblockCellLength = 8;
        private const float DefaultLandblockSizeInUnits = DefaultCellSizeInUnits * LandblockCellLength;

        // Offset to align with ACE/AC coordinate system
        // Corresponds to 101.95 coordinates * 240 units/coord = 24468 units
        // Or approx (127 blocks * 192) + (3.5 cells * 24)
        private static readonly Vector2 DefaultMapOffset = new Vector2(-24468f, -24468f);

        private static readonly Regex CoordinateRegex = new Regex(
            @"(?<NSval>[0-9]{1,3}(?:\.[0-9]{1,3})?)(?<NSchr>(?:[ns]))(?:[,\s]+)?(?<EWval>[0-9]{1,3}(?:\.[0-9]{1,3})?)(?<EWchr>(?:[ew]))?(,?\s*(?<Zval>\-?\d+.?\d+)z)?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );

        private static readonly Regex LandblockRegex = new Regex(
            @"(?:Your location is:\s*)?0x(?<LandblockId>[0-9A-Fa-f]{4})(?<CellId>[0-9A-Fa-f]{4})\s*\[\s*(?<LocalX>-?\d+\.?\d*)\s+(?<LocalY>-?\d+\.?\d*)\s+(?<LocalZ>-?\d+\.?\d*)\s*\](?:\s+\[?\s*(?<QuatX>-?\d+\.?\d*)\s+(?<QuatY>-?\d+\.?\d*)\s+(?<QuatZ>-?\d+\.?\d*)\s+(?<QuatW>-?\d+\.?\d*)\s*\]?)?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );

        // Source of truth: Local landblock coordinates
        private ushort _landblockId;
        private ushort _cellId;
        private float _localX;
        private float _localY;
        private float _localZ;

        #region Properties - Local Coordinates (Source of Truth)

        /// <summary>
        /// The landblock id (0xXXYY where XX is LandblockX, YY is LandblockY)
        /// </summary>
        public ushort LandblockId {
            get => _landblockId;
            set => _landblockId = value;
        }

        /// <summary>
        /// The cell id (1-64, or &lt; 0x100 for outdoor cells)
        /// </summary>
        public ushort CellId {
            get => _cellId;
            set => _cellId = value;
        }

        /// <summary>
        /// Local X position within the landblock (0-192 for default 8x8 grid).
        /// Automatically adjusts landblock if value exceeds boundaries.
        /// </summary>
        public float LocalX {
            get => _localX;
            set => SetLocalX(value);
        }

        /// <summary>
        /// Local Y position within the landblock (0-192 for default 8x8 grid).
        /// Automatically adjusts landblock if value exceeds boundaries.
        /// </summary>
        public float LocalY {
            get => _localY;
            set => SetLocalY(value);
        }

        /// <summary>
        /// Local Z position (altitude)
        /// </summary>
        public float LocalZ {
            get => _localZ;
            set => _localZ = value;
        }

        /// <summary>
        /// Optional rotation quaternion (X, Y, Z, W). Null if no rotation is set.
        /// </summary>
        public Quaternion? Rotation { get; set; }

        #endregion

        #region Properties - Derived Coordinates

        /// <summary>
        /// The landblock X coordinate (0-254)
        /// </summary>
        public int LandblockX => _landblockId >> 8;

        /// <summary>
        /// The landblock Y coordinate (0-254)
        /// </summary>
        public int LandblockY => _landblockId & 0xFF;

        /// <summary>
        /// The cell X coordinate (0-7)
        /// </summary>
        public int CellX => (_cellId - 1) / 8;

        /// <summary>
        /// The cell Y coordinate (0-7)
        /// </summary>
        public int CellY => (_cellId - 1) % 8;

        /// <summary>
        /// Whether this position is outside (outdoor cell, cellId &lt; 0x100)
        /// </summary>
        public bool IsOutside => _cellId < 0x100;

        #endregion

        #region Properties - Map Coordinates (NS/EW)

        /// <summary>
        /// North/South coordinate. North is positive, South is negative.
        /// Setting this updates the local coordinates.
        /// </summary>
        public float NS {
            get => GlobalYToNS(GlobalY);
            set {
                var global = new Vector3(GlobalX, NSToGlobalY(value), GlobalZ);
                SetFromGlobal(global);
            }
        }

        /// <summary>
        /// East/West coordinate. East is positive, West is negative.
        /// Setting this updates the local coordinates.
        /// </summary>
        public float EW {
            get => GlobalXToEW(GlobalX);
            set {
                var global = new Vector3(EWToGlobalX(value), GlobalY, GlobalZ);
                SetFromGlobal(global);
            }
        }

        #endregion

        #region Properties - Global Coordinates

        /// <summary>
        /// Global X coordinate in world space.
        /// Setting this updates the local coordinates.
        /// </summary>
        public float GlobalX {
            get => LocalToGlobalX(_landblockId, _localX);
            set {
                var global = new Vector3(value, GlobalY, GlobalZ);
                SetFromGlobal(global);
            }
        }

        /// <summary>
        /// Global Y coordinate in world space.
        /// Setting this updates the local coordinates.
        /// </summary>
        public float GlobalY {
            get => LocalToGlobalY(_landblockId, _localY);
            set {
                var global = new Vector3(GlobalX, value, GlobalZ);
                SetFromGlobal(global);
            }
        }

        /// <summary>
        /// Global Z coordinate (altitude).
        /// Setting this updates the local Z.
        /// </summary>
        public float GlobalZ {
            get => _localZ;
            set => _localZ = value;
        }

        /// <summary>
        /// Gets the global position as a Vector3.
        /// </summary>
        public Vector3 GlobalPosition {
            get => new Vector3(GlobalX, GlobalY, GlobalZ);
            set => SetFromGlobal(value);
        }

        #endregion

        #region Constructors

        /// <summary>
        /// Default constructor - initializes to origin
        /// </summary>
        public Position() {
            _landblockId = 0;
            _cellId = 1;
            _localX = 0;
            _localY = 0;
            _localZ = 0;
        }

        /// <summary>
        /// Creates a Position from local landblock coordinates (source of truth)
        /// </summary>
        public Position(ushort landblockId, ushort cellId, float localX, float localY, float localZ) {
            _landblockId = landblockId;
            _cellId = cellId;
            _localX = localX;
            _localY = localY;
            _localZ = localZ;
        }

        /// <summary>
        /// Creates a Position from map coordinates (NS/EW)
        /// </summary>
        public Position(float ns, float ew, float z, ITerrainInfo? region = null) {
            var global = new Vector3(EWToGlobalX(ew), NSToGlobalY(ns), z);
            SetFromGlobal(global, region);
        }

        #endregion

        #region Static Factory Methods

        /// <summary>
        /// Creates a Position from global world coordinates.
        /// </summary>
        public static Position FromGlobal(Vector3 globalPos, ITerrainInfo? region = null, uint? baseEnvCellId = null) {
            var pos = new Position();
            pos.SetFromGlobal(globalPos, region, baseEnvCellId);
            return pos;
        }

        /// <summary>
        /// Creates a Position from global coordinates.
        /// </summary>
        public static Position FromGlobal(float x, float y, float z, ITerrainInfo? region = null, uint? baseEnvCellId = null) {
            return FromGlobal(new Vector3(x, y, z), region, baseEnvCellId);
        }

        /// <summary>
        /// Creates a Position from map coordinates (NS/EW).
        /// </summary>
        public static Position FromMapCoordinates(float ns, float ew, float z, ITerrainInfo? region = null) {
            return new Position(ns, ew, z, region);
        }

        /// <summary>
        /// Parses a position from a string. Supports two formats:
        /// 1. Map coordinates: "12.3N, 41.4E" or "12.3N, 41.4E, 150z"
        /// 2. Landblock format: "0x7D640014 [67.197197 95.557037 12.004999]" with optional rotation quaternion
        /// 3. Landblock with location prefix: "Your location is: 0x7D640014 [67.197197 95.557037 12.004999] -0.521528 0.000000 0.000000 0.853234"
        /// </summary>
        public static bool TryParse(string input, out Position? position, ITerrainInfo? region = null) {
            position = null;
            if (string.IsNullOrWhiteSpace(input)) {
                return false;
            }

            // Try landblock format first
            var landblockMatch = LandblockRegex.Match(input);
            if (landblockMatch.Success) {
                return TryParseLandblockFormat(landblockMatch, out position);
            }

            // Try map coordinate format
            var coordinateMatch = CoordinateRegex.Match(input);
            if (coordinateMatch.Success) {
                return TryParseMapCoordinateFormat(coordinateMatch, out position, region);
            }

            return false;
        }

        /// <summary>
        /// Parses landblock format: "0x7D640014 [67.197197 95.557037 12.004999] -0.521528 0.000000 0.000000 0.853234"
        /// </summary>
        private static bool TryParseLandblockFormat(Match match, out Position? position) {
            position = null;

            // Parse landblock ID (hex)
            if (!ushort.TryParse(match.Groups["LandblockId"].Value, NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out ushort landblockId)) {
                return false;
            }

            // Parse cell ID (hex)
            if (!ushort.TryParse(match.Groups["CellId"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                    out ushort cellId)) {
                return false;
            }

            // Parse local coordinates
            if (!float.TryParse(match.Groups["LocalX"].Value, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out float localX)) {
                return false;
            }

            if (!float.TryParse(match.Groups["LocalY"].Value, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out float localY)) {
                return false;
            }

            if (!float.TryParse(match.Groups["LocalZ"].Value, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out float localZ)) {
                return false;
            }

            position = new Position(landblockId, cellId, localX, localY, localZ);

            // Parse optional quaternion rotation
            if (match.Groups["QuatX"].Success &&
                match.Groups["QuatY"].Success &&
                match.Groups["QuatZ"].Success &&
                match.Groups["QuatW"].Success) {
                if (float.TryParse(match.Groups["QuatX"].Value, NumberStyles.Float, CultureInfo.InvariantCulture,
                        out float quatX) &&
                    float.TryParse(match.Groups["QuatY"].Value, NumberStyles.Float, CultureInfo.InvariantCulture,
                        out float quatY) &&
                    float.TryParse(match.Groups["QuatZ"].Value, NumberStyles.Float, CultureInfo.InvariantCulture,
                        out float quatZ) &&
                    float.TryParse(match.Groups["QuatW"].Value, NumberStyles.Float, CultureInfo.InvariantCulture,
                        out float quatW)) {
                    position.Rotation = new Quaternion(quatX, quatY, quatZ, quatW);
                }
            }

            return true;
        }

        /// <summary>
        /// Parses map coordinate format: "12.3N, 41.4E, 150z"
        /// </summary>
        private static bool TryParseMapCoordinateFormat(Match match, out Position? position,
            ITerrainInfo? region = null) {
            position = null;

            if (!float.TryParse(match.Groups["NSval"].Value, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out float ns)) {
                return false;
            }

            if (!float.TryParse(match.Groups["EWval"].Value, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out float ew)) {
                return false;
            }

            // Apply direction modifiers
            if (match.Groups["NSchr"].Value.Equals("s", StringComparison.OrdinalIgnoreCase)) {
                ns = -ns;
            }

            if (match.Groups["EWchr"].Value.Equals("w", StringComparison.OrdinalIgnoreCase)) {
                ew = -ew;
            }

            // Parse optional Z coordinate
            float z = 0;
            if (match.Groups["Zval"].Success) {
                if (!float.TryParse(match.Groups["Zval"].Value, NumberStyles.Float, CultureInfo.InvariantCulture,
                        out z)) {
                    return false;
                }
            }

            position = new Position(ns, ew, z, region);
            return true;
        }

        /// <summary>
        /// Parses a position from a string, throwing an exception if parsing fails.
        /// </summary>
        public static Position Parse(string input, ITerrainInfo? region = null) {
            if (TryParse(input, out var position, region)) {
                return position!;
            }

            throw new FormatException($"Unable to parse position from string: '{input}'");
        }

        #endregion

        #region Private Helper Methods

        /// <summary>
        /// Sets LocalX with automatic landblock adjustment if it exceeds boundaries.
        /// </summary>
        private void SetLocalX(float value, ITerrainInfo? region = null) {
            if (_cellId == 0 || !IsOutside) {
                // Out of bounds or inside - just set directly
                _localX = value;
                return;
            }

            float landblockSizeInUnits = region?.LandblockSizeInUnits ?? DefaultLandblockSizeInUnits;

            // Handle overflow/underflow
            while (value >= landblockSizeInUnits) {
                value -= landblockSizeInUnits;
                // Move to next landblock in X direction
                int lbX = LandblockX + 1;
                int lbY = LandblockY;
                _landblockId = (ushort)((lbX << 8) + lbY);
            }

            while (value < 0) {
                value += landblockSizeInUnits;
                // Move to previous landblock in X direction
                int lbX = LandblockX - 1;
                int lbY = LandblockY;
                _landblockId = (ushort)((lbX << 8) + lbY);
            }

            _localX = value;

            // Update cell based on new local position
            UpdateCellFromLocal(region);
        }

        /// <summary>
        /// Sets LocalY with automatic landblock adjustment if it exceeds boundaries.
        /// </summary>
        private void SetLocalY(float value, ITerrainInfo? region = null) {
            if (_cellId == 0 || !IsOutside) {
                // Out of bounds or inside - just set directly
                _localY = value;
                return;
            }

            float landblockSizeInUnits = region?.LandblockSizeInUnits ?? DefaultLandblockSizeInUnits;

            // Handle overflow/underflow
            while (value >= landblockSizeInUnits) {
                value -= landblockSizeInUnits;
                // Move to next landblock in Y direction
                int lbX = LandblockX;
                int lbY = LandblockY + 1;
                _landblockId = (ushort)((lbX << 8) + lbY);
            }

            while (value < 0) {
                value += landblockSizeInUnits;
                // Move to previous landblock in Y direction
                int lbX = LandblockX;
                int lbY = LandblockY - 1;
                _landblockId = (ushort)((lbX << 8) + lbY);
            }

            _localY = value;

            // Update cell based on new local position
            UpdateCellFromLocal(region);
        }

        /// <summary>
        /// Updates the cell ID based on current local X/Y coordinates.
        /// </summary>
        private void UpdateCellFromLocal(ITerrainInfo? region = null) {
            if (_cellId == 0) return; // Don't update if out of bounds

            float landblockSizeInUnits = region?.LandblockSizeInUnits ?? DefaultLandblockSizeInUnits;
            float cellSize = landblockSizeInUnits / 8f;

            int cellX = (int)(_localX / cellSize);
            int cellY = (int)(_localY / cellSize);

            // Clamp to valid range
            cellX = Math.Clamp(cellX, 0, 7);
            cellY = Math.Clamp(cellY, 0, 7);

            _cellId = (ushort)((cellX * 8) + cellY + 1);
        }

        /// <summary>
        /// Updates local coordinates from a global position.
        /// This is the core method that maintains local coordinates as source of truth.
        /// </summary>
        private void SetFromGlobal(Vector3 globalPos, ITerrainInfo? region = null, uint? baseEnvCellId = null) {
            float mapOffset_X = region?.MapOffset.X ?? DefaultMapOffset.X;
            float mapOffset_Y = region?.MapOffset.Y ?? DefaultMapOffset.Y;
            float landblockSizeInUnits = region?.LandblockSizeInUnits ?? DefaultLandblockSizeInUnits;
            int mapWidthInLandblocks = region?.MapWidthInLandblocks ?? DefaultMapWidthInLandblocks;
            int mapHeightInLandblocks = region?.MapHeightInLandblocks ?? DefaultMapHeightInLandblocks;

            // Convert global to map coordinates
            float mapX = globalPos.X - mapOffset_X;
            float mapY = globalPos.Y - mapOffset_Y;

            // Check if out of bounds
            bool outOfBounds = mapX < 0 || mapY < 0 ||
                               mapX >= mapWidthInLandblocks * landblockSizeInUnits ||
                               mapY >= mapHeightInLandblocks * landblockSizeInUnits;

            if (outOfBounds) {
                // For out of bounds, store in a special way (cell 0, using local as global)
                _landblockId = 0;
                _cellId = 0;
                _localX = globalPos.X;
                _localY = globalPos.Y;
                _localZ = globalPos.Z;
                return;
            }

            int lbX, lbY;

            if (baseEnvCellId.HasValue && baseEnvCellId.Value != 0) {
                // We are inside an EnvCell. Calculate local offset based on the EnvCell's base Landblock.
                ushort baseLandblockId = (ushort)(baseEnvCellId.Value >> 16);
                ushort cellId = (ushort)(baseEnvCellId.Value & 0xFFFF);
                lbX = baseLandblockId >> 8;
                lbY = baseLandblockId & 0xFF;

                float localLbX = mapX - (lbX * landblockSizeInUnits);
                float localLbY = mapY - (lbY * landblockSizeInUnits);

                _landblockId = baseLandblockId;
                _cellId = cellId;
                _localX = localLbX;
                _localY = localLbY;
                _localZ = globalPos.Z;
            }
            else {
                // Calculate landblock coordinates
                lbX = (int)(mapX / landblockSizeInUnits);
                lbY = (int)(mapY / landblockSizeInUnits);

                // Calculate local position within landblock
                float localLbX = mapX - (lbX * landblockSizeInUnits);
                float localLbY = mapY - (lbY * landblockSizeInUnits);

                // Calculate cell coordinates
                float cellSize = landblockSizeInUnits / 8f;
                int cellX = (int)(localLbX / cellSize);
                int cellY = (int)(localLbY / cellSize);

                // Clamp cell coordinates to valid range
                cellX = Math.Clamp(cellX, 0, 7);
                cellY = Math.Clamp(cellY, 0, 7);

                // Update source of truth - local coordinates
                _landblockId = region?.GetLandblockId(lbX, lbY) ?? (ushort)((lbX << 8) + lbY);
                _cellId = (ushort)((cellX * 8) + cellY + 1);
                _localX = localLbX;
                _localY = localLbY;
                _localZ = globalPos.Z;
            }
        }

        private float LocalToGlobalX(ushort landblockId, float localX, ITerrainInfo? region = null) {
            if (_cellId == 0) return localX; // Out of bounds case

            float mapOffset_X = region?.MapOffset.X ?? DefaultMapOffset.X;
            float landblockSizeInUnits = region?.LandblockSizeInUnits ?? DefaultLandblockSizeInUnits;

            int lbX = landblockId >> 8;
            float mapX = lbX * landblockSizeInUnits + localX;
            return mapX + mapOffset_X;
        }

        private float LocalToGlobalY(ushort landblockId, float localY, ITerrainInfo? region = null) {
            if (_cellId == 0) return localY; // Out of bounds case

            float mapOffset_Y = region?.MapOffset.Y ?? DefaultMapOffset.Y;
            float landblockSizeInUnits = region?.LandblockSizeInUnits ?? DefaultLandblockSizeInUnits;

            int lbY = landblockId & 0xFF;
            float mapY = lbY * landblockSizeInUnits + localY;
            return mapY + mapOffset_Y;
        }

        // Map coordinate conversion helpers
        private static float GlobalYToNS(float y) => (y / (10f * 24f));
        private static float GlobalXToEW(float x) => (x / (10f * 24f));
        private static float NSToGlobalY(float ns) => (ns) * 10f * 24f;
        private static float EWToGlobalX(float ew) => (ew) * 10f * 24f;

        #endregion

        #region Distance and Heading Methods

        /// <summary>
        /// Calculates the 3D distance to another position.
        /// </summary>
        public float DistanceTo(Position other) {
            return Vector3.Distance(GlobalPosition, other.GlobalPosition);
        }

        /// <summary>
        /// Calculates the 2D distance to another position (ignoring Z).
        /// </summary>
        public float DistanceToFlat(Position other) {
            var p1 = GlobalPosition;
            var p2 = other.GlobalPosition;
            return Vector2.Distance(new Vector2(p1.X, p1.Y), new Vector2(p2.X, p2.Y));
        }

        /// <summary>
        /// Calculates the heading (in degrees, 0-360) to another position.
        /// 0° = North, 90° = East, 180° = South, 270° = West
        /// </summary>
        public float HeadingTo(Position other) {
            var p1 = GlobalPosition;
            var p2 = other.GlobalPosition;
            var deltaY = p2.Y - p1.Y;
            var deltaX = p2.X - p1.X;
            return (float)(360f - (Math.Atan2(deltaY, deltaX) * 180f / Math.PI) + 90f) % 360f;
        }

        #endregion

        #region String Representation

        /// <summary>
        /// Returns a string representation of this position.
        /// </summary>
        public override string ToString() {
            if (_cellId == 0) {
                return $"Out of Bounds ({_localX:F1}, {_localY:F1}, {_localZ:F1})";
            }

            var ns = NS;
            var ew = EW;

            string result = $"{Math.Abs(ns):F2}{(ns >= 0 ? "N" : "S")}, " +
                            $"{Math.Abs(ew):F2}{(ew >= 0 ? "E" : "W")}, " +
                            $"{_localZ:F2}Z " +
                            $"[0x{_landblockId:X4}{_cellId:X4} {_localX:F2}, {_localY:F2}, {_localZ:F2}]";

            if (Rotation.HasValue) {
                var q = Rotation.Value;
                result += $" [{q.X:F6} {q.Y:F6} {q.Z:F6} {q.W:F6}]";
            }

            return result;
        }

        /// <summary>
        /// Returns a compact string representation with just map coordinates.
        /// </summary>
        public string ToMapString() {
            if (_cellId == 0) {
                return "Out of Bounds";
            }

            var ns = NS;
            var ew = EW;

            return $"{Math.Abs(ns):F2}{(ns >= 0 ? "N" : "S")}, " +
                   $"{Math.Abs(ew):F2}{(ew >= 0 ? "E" : "W")}, " +
                   $"{_localZ:F2}Z";
        }

        /// <summary>
        /// Returns a string representation with landblock information in game format.
        /// </summary>
        public string ToLandblockString() {
            if (_cellId == 0) {
                return $"Out of Bounds ({_localX:F1}, {_localY:F1}, {_localZ:F1})";
            }

            string result = $"0x{_landblockId:X4}{_cellId:X4} [{_localX:F3} {_localY:F3} {_localZ:F3}]";

            if (Rotation.HasValue) {
                var q = Rotation.Value;
                result += $" {q.X:F6} {q.Y:F6} {q.Z:F6} {q.W:F6}";
            }

            return result;
        }

        #endregion

        #region Equality

        public override bool Equals(object? obj) {
            if (obj is Position other) {
                bool coordsEqual = _landblockId == other._landblockId &&
                                   _cellId == other._cellId &&
                                   Math.Abs(_localX - other._localX) < 0.001f &&
                                   Math.Abs(_localY - other._localY) < 0.001f &&
                                   Math.Abs(_localZ - other._localZ) < 0.001f;

                // Check rotation equality
                if (!coordsEqual) return false;

                if (Rotation.HasValue != other.Rotation.HasValue) return false;

                if (Rotation is { } q1 && other.Rotation is { } q2) {
                    return Math.Abs(q1.X - q2.X) < 0.001f &&
                           Math.Abs(q1.Y - q2.Y) < 0.001f &&
                           Math.Abs(q1.Z - q2.Z) < 0.001f &&
                           Math.Abs(q1.W - q2.W) < 0.001f;
                }

                return true;
            }

            return false;
        }

        public override int GetHashCode() {
            return HashCode.Combine(_landblockId, _cellId, _localX, _localY, _localZ, Rotation);
        }

        #endregion
    }
}