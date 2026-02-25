namespace TopSpeed.Protocol
{
    public enum PacketStream : byte
    {
        Control = 0,
        Room = 1,
        RaceState = 2,
        RaceEvent = 3,
        Media = 4,
        Chat = 5,
        Direct = 6,
        Query = 7
    }

    public enum PacketDeliveryKind : byte
    {
        Unreliable = 0,
        ReliableOrdered = 1,
        Sequenced = 2
    }

    public readonly struct PacketStreamSpec
    {
        public PacketStreamSpec(PacketStream stream, byte channel, PacketDeliveryKind delivery)
        {
            Stream = stream;
            Channel = channel;
            Delivery = delivery;
        }

        public PacketStream Stream { get; }
        public byte Channel { get; }
        public PacketDeliveryKind Delivery { get; }
    }

    public static class PacketStreams
    {
        public const int Count = 8;

        public static PacketStreamSpec Control => new PacketStreamSpec(PacketStream.Control, 0, PacketDeliveryKind.ReliableOrdered);
        public static PacketStreamSpec Room => new PacketStreamSpec(PacketStream.Room, 1, PacketDeliveryKind.ReliableOrdered);
        public static PacketStreamSpec RaceState => new PacketStreamSpec(PacketStream.RaceState, 2, PacketDeliveryKind.Unreliable);
        public static PacketStreamSpec RaceEvent => new PacketStreamSpec(PacketStream.RaceEvent, 3, PacketDeliveryKind.ReliableOrdered);
        public static PacketStreamSpec Media => new PacketStreamSpec(PacketStream.Media, 4, PacketDeliveryKind.ReliableOrdered);
        public static PacketStreamSpec Chat => new PacketStreamSpec(PacketStream.Chat, 5, PacketDeliveryKind.ReliableOrdered);
        public static PacketStreamSpec Direct => new PacketStreamSpec(PacketStream.Direct, 6, PacketDeliveryKind.ReliableOrdered);
        public static PacketStreamSpec Query => new PacketStreamSpec(PacketStream.Query, 7, PacketDeliveryKind.ReliableOrdered);

        public static PacketStreamSpec Get(PacketStream stream)
        {
            return stream switch
            {
                PacketStream.Control => Control,
                PacketStream.Room => Room,
                PacketStream.RaceState => RaceState,
                PacketStream.RaceEvent => RaceEvent,
                PacketStream.Media => Media,
                PacketStream.Chat => Chat,
                PacketStream.Direct => Direct,
                PacketStream.Query => Query,
                _ => Control
            };
        }
    }
}
