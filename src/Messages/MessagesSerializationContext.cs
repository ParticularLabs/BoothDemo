using System.Text.Json.Serialization;

namespace Messages;

[JsonSerializable(typeof(OrderPlaced))]
[JsonSerializable(typeof(OrderBilled))]
[JsonSerializable(typeof(PlaceOrder))]
[JsonSerializable(typeof(OrderShipped))]
public partial class MessagesSerializationContext : JsonSerializerContext;