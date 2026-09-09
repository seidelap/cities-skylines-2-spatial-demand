using Colossal.Serialization.Entities;
using Game.Companies;
using Game.Pathfind;
using Unity.Entities;

namespace SpatialDemand.Mod
{
    // Recovery state only. Vanilla retains the sole purchase order and money ledger.
    public struct ShoppingRouteHold : IComponentData, ISerializable
    {
        public PathInformation Original;
        public ResourceBuyer Request;
        public Entity Origin;
        public uint Started;
        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        { Original.Serialize(writer); Request.Serialize(writer); writer.Write(Origin); writer.Write(Started); }
        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        { Original.Deserialize(reader); Request.Deserialize(reader); reader.Read(out Origin); reader.Read(out Started); }
    }

    [InternalBufferCapacity(0)]
    public struct ShoppingOriginalPathElement : IBufferElementData, ISerializable
    {
        public PathElement Element;
        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter { Element.Serialize(writer); }
        public void Deserialize<TReader>(TReader reader) where TReader : IReader { Element.Deserialize(reader); }
    }

    // Proxy entities cannot match vanilla citizen, buyer or moving PathOwner queries.
    public struct ShoppingRouteProbe : IComponentData, ISerializable
    {
        public Entity Shopper;
        public uint Started;
        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        { writer.Write(Shopper); writer.Write(Started); }
        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        { reader.Read(out Shopper); reader.Read(out Started); }
    }
}
