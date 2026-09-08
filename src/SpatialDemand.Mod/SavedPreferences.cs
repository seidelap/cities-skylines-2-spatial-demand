using System;
using Colossal.Serialization.Entities;
using Unity.Entities;

namespace SpatialDemand.Mod
{
    // Attached to the vanilla household, so identity follows the game's saved entity.
    public struct SavedPreferences : IComponentData, ISerializable
    {
        public uint Seed;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        { writer.Write(1); writer.Write(Seed); }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out int version);
            if (version != 1) throw new InvalidOperationException("Unsupported Spatial Demand preference version.");
            reader.Read(out Seed);
        }
    }

    // A queue submission is not a completed move. Persist the receipt so loading between
    // submission and settlement can recover instead of leaving a seeker permanently disabled.
    public struct PendingHome : IComponentData, ISerializable
    {
        public Entity Property;
        public uint Frame;
        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        { writer.Write(1); writer.Write(Property); writer.Write(Frame); }
        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out int version);
            if (version != 1) throw new InvalidOperationException("Unsupported Spatial Demand receipt version.");
            reader.Read(out Property); reader.Read(out Frame);
        }
    }
}
