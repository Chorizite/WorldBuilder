using System;

namespace Chorizite.OpenGLSDLBackend.Lib {
    public struct SelectedStaticObject {
        public ushort LandblockKey;
        public uint InstanceId;

        public override bool Equals(object? obj) => obj is SelectedStaticObject other && LandblockKey == other.LandblockKey && InstanceId == other.InstanceId;
        public override int GetHashCode() => HashCode.Combine(LandblockKey, InstanceId);
        public static bool operator ==(SelectedStaticObject left, SelectedStaticObject right) => left.Equals(right);
        public static bool operator !=(SelectedStaticObject left, SelectedStaticObject right) => !(left == right);
    }
}
